using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Sound;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The program's sound calls wired into the game logic at the program's points (docs/re/sound.md §2.4): the explore
/// loop's step cues and the fight's impact, miss and step cues.
/// </summary>
public class SoundHookTests
{
    private static PorGame SaveA(ScriptedAreaUi ui, SoundLog log)
    {
        var dir = PorData.Open();
        var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
            new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), ui);
        var g = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, player, 1);
        g.Loop.Sound = log;
        return g;
    }

    [PorFact]
    public void A_step_plays_the_step_cue_twice_and_turns_left_or_right_once()
    {
        var ui = new ScriptedAreaUi();
        var log = new SoundLog();
        var g = SaveA(ui, log);
        g.Loop.Execute(AreaCommand.Forward);                       // 15,4 W → 14,4: the move (0x16E09), then 0x4215
        Assert.Equal((14, 4), (g.Memory.Position.X, g.Memory.Position.Y));
        Assert.Equal(["Step", "Step"], log.Calls);
        log.Calls.Clear();
        g.Loop.Execute(AreaCommand.TurnLeft);
        g.Loop.Execute(AreaCommand.TurnRight);
        Assert.Equal(["Step", "Step"], log.Calls);
        log.Calls.Clear();
        g.Loop.Execute(AreaCommand.TurnAround);                   // 0x1718B: no sound
        Assert.Empty(log.Calls);
    }

    [PorFact]
    public void A_refused_step_bumps_and_plays_no_step()
    {
        var ui = new ScriptedAreaUi();
        var log = new SoundLog();
        var g = SaveA(ui, log);
        // Face each way from the start square until a step is refused.
        for (int i = 0; i < 4 && !ui.Calls.Contains("Bump"); i++)
        {
            g.Loop.Execute(AreaCommand.TurnRight);
            log.Calls.Clear();
            var (x, y) = (g.Memory.Position.X, g.Memory.Position.Y);
            g.Loop.Execute(AreaCommand.Forward);
            if (ui.Calls.Contains("Bump"))
            {
                Assert.Equal((x, y), (g.Memory.Position.X, g.Memory.Position.Y));
                Assert.Empty(log.Calls);
                return;
            }
        }
        Assert.Fail("no wall around the start square");
    }

    private static (Fight Fight, EclCharacter A, EclCharacter D) Duel(SoundLog log, byte defAc, Func<ushort, ushort>? random = null)
    {
        var m = new EclMemory();
        var a = new EclCharacter();
        var d = new EclCharacter();
        foreach (var c in new[] { a, d })
        {
            c[FightRules.InPlay] = 1;
            c[FightRules.Hp] = 50;
        }
        a[0x113] = 1;
        a[0x116] = 1; a[0x118] = 1; a[0x115] = 1; a[0x117] = 4;
        d[FightRules.Ac] = defAc;
        d[FightRules.AcRear] = defAc;
        var geo = new GridGeometry();
        geo.Place(a, 0, 0);
        geo.Place(d, 1, 0);
        var f = new Fight(m, new EncounterState(), new BaseItemTable(new byte[128 * 16]), random ?? (_ => 0), new EndActionsController(), geo);
        f.State.Sound = log;
        f.Record(a)[CombatRecord.AttackPhase] = 1;
        return (f, a, d);
    }

    [Fact]
    public void A_missed_attack_plays_miss_once()
    {
        var log = new SoundLog();
        var (f, a, d) = Duel(log, 0x3C);                          // every die rolls 1: the d20 misses
        var o = f.Attack(a, d);
        Assert.False(o.AnyHit);
        Assert.Equal(["Miss"], log.Calls);
    }

    [Fact]
    public void A_hit_plays_impact()
    {
        var log = new SoundLog();
        var (f, a, d) = Duel(log, 0x3C, n => (ushort)(n - 1));    // every die rolls its top: the d20 hits
        var o = f.Attack(a, d);
        Assert.True(o.AnyHit);
        Assert.Equal(Enumerable.Repeat("Impact", o.Hits), log.Calls);
    }

    [Fact]
    public void An_attack_on_a_helpless_defender_plays_impact_first()
    {
        var log = new SoundLog();
        var (f, a, d) = Duel(log, 0x3C);
        EffectHandlers.AddEffect(d, FightRules.HelplessEffects[0], 10, 0, false);
        Assert.True(FightRules.IsHelpless(d));
        f.Attack(a, d);
        Assert.Equal("Impact", log.Calls.First());
    }

    [Fact]
    public void A_missile_attack_brackets_its_flight()
    {
        var log = new SoundLog();
        var (f, a, d) = Duel(log, 0x3C);
        f.Attack(a, d, ammo: new byte[63]);
        Assert.Equal(["(", "MissileOut", "MissileIn", ")"], log.Calls.Take(4));
    }
}
