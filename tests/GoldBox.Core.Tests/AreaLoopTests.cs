using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>A headless <see cref="IAreaUi"/>: door choices from a queue, a log.</summary>
public sealed class ScriptedAreaUi : IAreaUi
{
    public Queue<DoorAction?> Doors { get; } = new();
    public List<string> Calls { get; } = [];
    public bool CampInterrupted { get; set; }

    public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices)
    {
        Calls.Add($"Door {barrier} [{string.Join(",", choices)}]");
        return Doors.TryDequeue(out var d) ? d : null;
    }
    public void Bump() => Calls.Add("Bump");
    public void Message(AreaMessage message) => Calls.Add("Message " + message);
    public bool Camp()
    {
        Calls.Add("Camp");
        return CampInterrupted;
    }
    public Action? OnView { get; set; }
    public void View()
    {
        Calls.Add("View");
        OnView?.Invoke();
    }
    public void Cast() => Calls.Add("Cast");
}

/// <summary>
/// The game clock (0x241A1), the backdrop rule (0x2DCF4) and the area loop (0x403E) replaying the DOSBox captures
/// from their <c>before</c> state: one command, then the whole VM state, the clock, the map and RandSeed must equal
/// <c>after</c>.
/// </summary>
public class AreaLoopTests
{
    private static EclMemory ClockAt(int hour, int tens, int ones, int day = 0, int month = 0, int year = 0)
    {
        var m = new EclMemory();
        ushort[] v = [0, (ushort)ones, (ushort)tens, (ushort)hour, (ushort)day, (ushort)month, (ushort)year];
        for (int i = 0; i < v.Length; i++)
            m.Write((ushort)(GameClock.FirstCell + i), v[i]);
        return m;
    }

    [Fact]
    public void Clock_carries_minutes_into_hours_and_days()
    {
        var m = ClockAt(10, 5, 9);
        GameClock.Advance(m, GameClock.Minutes, 1);
        Assert.Equal("11:00", GameClock.Format(m));
        m = ClockAt(23, 5, 0);
        GameClock.Advance(m, GameClock.TenMinutes, 1);
        Assert.Equal("00:00", GameClock.Format(m));
        Assert.Equal(1, m.Read(GameClock.FirstCell + GameClock.Days));
        m = ClockAt(10, 5, 0);
        GameClock.Advance(m, GameClock.Minutes, 25);
        Assert.Equal("11:15", GameClock.Format(m));
        Assert.Equal(0, m.Read(GameClock.FirstCell));                    // the minute segment is untouched by unit 1
    }

    [Fact]
    public void Clock_month_and_year_carry_and_the_year_overflow_ages_the_party()
    {
        var m = ClockAt(23, 5, 9, day: 29, month: 11, year: 255);
        var c = new EclCharacter();
        c.SetWord(GameClock.AgeOffset, 20);
        m.Party = new EclParty();
        m.Party.Members.Add(c);
        GameClock.Advance(m, GameClock.Minutes, 1);
        Assert.Equal(256, m.Read(GameClock.FirstCell + GameClock.Years)); // not wrapped
        Assert.Equal(21, c.Word(GameClock.AgeOffset));
        GameClock.Advance(m, GameClock.Minutes, 1);                      // still ≥ 256: ages again on every add
        Assert.Equal(22, c.Word(GameClock.AgeOffset));
    }

    [Fact]
    public void Backdrop_follows_the_colour_bytes()
    {
        Assert.Equal((15, 7, 6), (BackdropRule.Horizon1(9), BackdropRule.Horizon2(9), BackdropRule.Ground(0x41)));
        // Valhingen Graveyard (ECL4 block 10): $C05F = 8, $C059 = 11.
        Assert.Equal((0, 8, 8), (BackdropRule.Horizon1(8), BackdropRule.Horizon2(8), BackdropRule.Ground(11)));
        Assert.Equal((0, 7), (BackdropRule.Horizon1(0x41), BackdropRule.Horizon2(0x41)));
        Assert.Equal(1, BackdropRule.Ground(6));
        var m = new EclMemory();
        m.Write(0x49FD, 3);
        m.Write(0x49FE, 2);
        byte[] sky = [0, 15, 12, 11, 13, 10, 9, 14, 0, 15, 12, 11, 13, 10, 9, 14];
        Assert.Equal(new Backdrop(11, 15, 7, 6), BackdropRule.For(m, sky, 0x10));
        m.Write(0xC059, 0x0B);
        m.Write(0xC05F, 8);
        Assert.Equal(new Backdrop(12, 0, 8, 8), BackdropRule.For(m, sky, 0x80));
    }

    // ---- Capture replays ----

    private sealed record Replay(PorGame Game, ScriptedAreaUi Ui, ScriptedEclHost Host);

    private static Replay Start(string capture)
    {
        var dir = PorData.Open();
        var art = PorViewArt.Load(dir);
        var before = new DosboxVmState(Path.Combine(Captures.Dir!, capture, "before"));
        var ui = new ScriptedAreaUi();
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var player = new PorPlayer(host, new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), ui);
        var party = PorParty.Load(PorSaveGame.Open(dir.Find("SAVGAMA.DAT")!), dir.Root);
        var g = PorGame.Restore(dir, art, player, before.RandSeed, vm => before.ApplyTo(vm),
            new AreaMap(before.GeoPlanes!, before.Area4900[0x49C5 - 0x4900]), party);
        return new Replay(g, ui, host);
    }

    /// <summary>Every VM cell (except the wall-set bookkeeping $4AFF), the position, the map and RandSeed.</summary>
    private static void AssertMatches(PorGame g, string capture)
    {
        var after = new DosboxVmState(Path.Combine(Captures.Dir!, capture, "after"));
        var m = g.Memory;
        var bad = new List<string>();
        for (int i = 0; i < 1024; i++)
            if (m.Cells4900[i] != after.Area4900[i] && i != 0x4AFF - 0x4900)
                bad.Add($"${0x4900 + i:X4} {m.Cells4900[i]} vs {after.Area4900[i]}");
        for (int i = 0; i < 1024; i++)
            if (m.Cells6B00[i] != after.Area6B00[i])
                bad.Add($"${0x6B00 + i:X4} {m.Cells6B00[i]} vs {after.Area6B00[i]}");
        for (int i = 0; i < 512; i++)
            if (m.Cells9700[i] != after.Area9700[i])
                bad.Add($"${0x9700 + i:X4} {m.Cells9700[i]} vs {after.Area9700[i]}");
        Assert.Empty(bad);
        var p = after.Position;
        Assert.Equal((p.X, p.Y, p.Facing, p.Wall, p.Event), (m.Position.X, m.Position.Y, m.Position.Facing, m.Position.WallAhead, m.Position.EventByte));
        Assert.Equal(after.GeoPlanes, g.Loop.Map!.PlanesArray);
        Assert.Equal(after.RandSeed, g.Rng.Seed);
        Assert.Equal((after.Disk, after.Script_), (m.Disk, g.Vm.CurrentScript));
    }

    [CaptureTheory]
    [InlineData("step_west")]
    [InlineData("oneway_through")]
    [InlineData("oneway_blocked")]
    [InlineData("oneway_barrier_through")]
    [InlineData("oneway_barrier_blocked")]
    [InlineData("edge_exit_east")]
    public void Forward_replays_the_capture(string capture)
    {
        var r = Start(capture);
        r.Game.Loop.Execute(AreaCommand.Forward);
        AssertMatches(r.Game, capture);
        bool refused = capture.EndsWith("_blocked", StringComparison.Ordinal);
        Assert.Equal(refused, r.Ui.Calls.Contains("Bump"));
    }

    [CaptureFact]
    public void Forward_into_the_door_with_bash_replays_door_bash()
    {
        var r = Start("door_bash");
        r.Ui.Doors.Enqueue(DoorAction.Bash);
        r.Game.Loop.Execute(AreaCommand.Forward);
        Assert.Contains("Door Locked [Bash,Pick]", r.Ui.Calls);
        AssertMatches(r.Game, "door_bash");
        Assert.Equal(EclStopReason.AwaitingInput, r.Game.Loop.Runs[^1].Reason); // vector 1 waits in its HMENU
        Assert.True(r.Game.Loop.DoorFlags.Pick);                                // reset by the step through
    }

    [CaptureFact]
    public void Turn_runs_no_script_and_takes_no_time()
    {
        var r = Start("turn_left");
        r.Game.Loop.Execute(AreaCommand.TurnLeft);
        AssertMatches(r.Game, "turn_left");
        Assert.Empty(r.Game.Loop.Runs);
    }

    [CaptureFact]
    public void Edge_exit_switches_map_and_bounds()
    {
        var r = Start("edge_exit_east");
        r.Game.Loop.Execute(AreaCommand.Forward);
        Assert.Equal(0, r.Game.Loop.Map!.Id);
        Assert.True(r.Game.Loop.Map.Bounded);                      // script 0: off-map squares read as nothing
        Assert.Same(r.Game.Loop.Map.PlanesArray, ((MapPlanes)r.Game.Encounter.Map!).Planes);
    }

    [PorFact]
    public void Search_steps_cost_ten_minutes_and_look_runs_vector_1_alone()
    {
        var dir = PorData.Open();
        var ui = new ScriptedAreaUi();
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var player = new PorPlayer(host, new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), ui);
        var g = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, player, 1);
        Assert.Equal(20, g.Loop.Map!.Id);
        long t0 = GameClock.TotalMinutes(g.Memory);
        g.Loop.Execute(AreaCommand.Search);
        Assert.True(g.Loop.SearchOn);
        g.Loop.Execute(AreaCommand.Forward);                       // 15,4 W → 14,4
        Assert.Equal((14, 4), (g.Memory.Position.X, g.Memory.Position.Y));
        Assert.Equal(t0 + 10, GameClock.TotalMinutes(g.Memory));
        g.Loop.Execute(AreaCommand.Search);
        g.Loop.Execute(AreaCommand.Look);
        Assert.Equal(t0 + 20, GameClock.TotalMinutes(g.Memory));
        Assert.Equal(0, g.Memory.Read(AreaLoop.SearchMode));      // bit 0 restored after the vector 1 run
        Assert.Equal((14, 4), (g.Memory.Position.X, g.Memory.Position.Y));
    }

    [PorFact]
    public void Steps_and_Look_age_the_partys_timed_effects()
    {
        // 0x241A1 runs 0x23DCC after every advance (step, Look, CLOCK): a 1-minute step takes a minute off each timed
        // effect, Look 10; one with no more minutes than the step goes. The flags DS:471E are the session's (PorGame.Camp).
        var dir = PorData.Open();
        var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
            new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), new ScriptedAreaUi());
        var g = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, player, 1);
        var c = g.Memory.Party!.Members[0];
        c.Effects.Clear();
        EffectHandlers.AddEffect(c, 0x1C, 12, 4, false);
        g.Loop.Execute(AreaCommand.Forward);
        Assert.Equal(11, c.Effects.Single()[1]);
        Assert.Equal(1, g.Camp.EffectFlags[0]);
        g.Loop.Execute(AreaCommand.Look);
        Assert.Equal(1, c.Effects.Single()[1]);
        g.AreaHost.Clock(1);                                       // the CLOCK opcode's advance
        Assert.Empty(c.Effects);
        Assert.Equal(0, g.Camp.EffectFlags[0]);
    }

    [PorFact]
    public void Area_map_is_refused_where_49FB_is_set()
    {
        var dir = PorData.Open();
        var ui = new ScriptedAreaUi();
        var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
            new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), ui);
        var g = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, player, 1);
        g.Loop.Execute(AreaCommand.AreaMap);
        Assert.True(g.Loop.ShowAreaMap);
        g.Memory.Write(AreaLoop.NoAreaMap, 1);
        g.Loop.Execute(AreaCommand.AreaMap);
        Assert.Contains("Message NoAreaMapHere", ui.Calls);
        g.Loop.Redraw();
        Assert.False(g.Loop.ShowAreaMap);
    }
}
