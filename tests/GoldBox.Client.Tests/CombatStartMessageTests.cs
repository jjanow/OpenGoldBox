using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The fight start's row-24 line (0xE430–0xE452) and the AI's flee / surrender messages (0xACC1, 0xBCF0, 0x2BDF0), read
/// s29: the requests the Client makes for them, with the program's words from the player's GAME.OVR. Skipped without
/// GOLDBOX_POR_DIR (except the parts that need no game text).
/// </summary>
public class CombatStartMessageTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    private static CombatText Texts() => Dir() is { } dir ? new CombatText(new PorGameDirectory(dir).Program()) : new CombatText();

    [Fact]
    public void The_battle_line_comes_from_the_game_files()
    {
        if (Dir() is not { } dir)
            return;
        var t = new CombatText(new PorGameDirectory(dir).Program());
        Assert.False(string.IsNullOrWhiteSpace(t[CombatMsg.BattleBegins]));
        Assert.NotEqual(new CombatText()[CombatMsg.BattleBegins], t[CombatMsg.BattleBegins]);
    }

    [Theory]
    [InlineData(4, 900)]
    [InlineData(1, 225)]
    [InlineData(0, -1)]
    public void The_battle_line_follows_a_blank_row_24_pause_and_holds_one_update(int speed, int pauseMs)
    {
        // 0xE430: row 24 cleared, PauseOnly (speed × 225 ms, nothing at speed 0), then the text at column 0 in colour
        // 10 with no pause after it (the arena build and the combat screen follow).
        var seen = new List<DelayRequest>();
        var view = new CombatView { Speed = (byte)speed };
        var chooser = new CombatChooser(q =>
        {
            if (q is DelayRequest d)
                seen.Add(d);
            return null;
        }, view) { Text = Texts() };
        chooser.BattleBegins();
        var lines = seen.Select(d => d.BottomLine).ToList();
        if (pauseMs >= 0)
        {
            Assert.Equal(2, seen.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(pauseMs), seen[0].Duration);
            Assert.Equal(("", CombatChooser.StartLineColour), lines[0]);
        }
        else
            Assert.Single(seen);
        Assert.Equal(TimeSpan.Zero, seen[^1].Duration);
        Assert.Equal((chooser.Text[CombatMsg.BattleBegins], (byte)10), lines[^1]);
        Assert.False(view.Active);                        // still the area screen: the combat screen is not attached

        seen.Clear();
        chooser.Pauses = false;                           // the tests' no-wait setting asks nothing
        chooser.BattleBegins();
        Assert.Empty(seen);
    }

    [Fact]
    public void A_zero_duration_bottom_line_waits_for_a_draw_even_if_updates_catch_up()
    {
        if (Dir() is not { } dir)
            return;
        var content = new PorGameDirectory(dir);
        using var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        game = PorGame.FromSave(content, PorViewArt.Load(content), content.Find("SAVGAMA.DAT")!,
            new PorPlayer(player, player, player, player), 7);
        var screen = new ExploreScreen(game, player, runner, new TestRig.NoHost());
        var input = new ActionInput(InputBindings.Defaults());
        var frame = new IndexedFrame(320, 200);
        runner.Start(() =>
        {
            runner.Ask(new DelayRequest(TimeSpan.Zero) { BottomLine = ("READY", 10) });
            runner.Ask(new DelayRequest(TimeSpan.Zero) { BottomLine = ("READY", 10) });
        });

        for (int i = 0; i < 2; i++)
        {
            var pending = Assert.IsType<DelayRequest>(runner.Pending);
            screen.Update(input);
            screen.Update(input);
            Assert.Same(pending, runner.Pending);
            screen.Draw(frame, input);
            Assert.Contains((byte)10, frame.Pixels.Skip(192 * frame.Width));
            screen.Update(input);
            Assert.NotSame(pending, runner.Pending);
        }
        Assert.False(runner.Busy);
    }

    /// <summary>A chooser with a 50 × 25 ground map, whose AI's messages are recorded at each pause.</summary>
    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 2 };
        public readonly List<(TimeSpan Wait, IReadOnlyList<string> Rows, int ViewX, int ViewY)> Pauses = [];
        public readonly CombatChooser Chooser;
        public readonly Fight Fight;
        public readonly CombatMap Map;

        public Rig(CombatText text)
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest d)
                    Pauses.Add((d.Duration, View.Panel.Lines, View.ViewX, View.ViewY));
                return null;
            }, View) { Text = text };
            var icon = new byte[64];
            Array.Fill(icon, (byte)0xFF);
            icon[8] = 0; icon[9] = 0; // size 1: one cell
            Map = new CombatMap(new CombatTables(new byte[64 * 4], icon, new byte[16], new byte[16], [7, 2, 3, 6],
                new byte[8], new byte[8], new byte[60]));
            Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), Map);
            View.Fight = Fight;
        }

        public static string Row(IReadOnlyList<string> rows, int r) => rows[r - 1].Trim();

        /// <summary>Puts a creature in map slot 1 at (x, y), size 1 (as the placement leaves it).</summary>
        public void Put(EclCharacter c, int x, int y)
        {
            Map.SetSlotCreature(1, c);
            Map.SetSlotPosition(1, x, y);
            Map.SetSlotSize(1, 1);
            Map.SlotCount = 2;
            Map.Rebuild();
        }
    }

    private static EclCharacter Creature(string name) =>
        new() { Name = name, [FightRules.Hostile] = 1, [FightRules.InPlay] = 1, [FightRules.Size] = 1 };

    [Theory]
    [InlineData(AiMessage.FleesInPanic, CombatMsg.FleesInPanic)]
    [InlineData(AiMessage.ForcedToFlee, CombatMsg.ForcedToFlee)]
    [InlineData(AiMessage.Surrenders, CombatMsg.Surrenders)]
    public void Flee_and_surrender_messages_are_panel_messages_from_row_10_with_the_pause(AiMessage m, CombatMsg word)
    {
        // START 0x16EC(creature, text, row 10, pause 1): the name on row 10, the text from row 11, speed × 225 ms, then
        // rows 10–21 cleared.
        var r = new Rig(Texts());
        var orc = Creature("Orc");
        r.Chooser.AiSays(orc, m);
        var (wait, rows, _, _) = Assert.Single(r.Pauses);
        Assert.Equal(TimeSpan.FromMilliseconds(450), wait);
        Assert.Equal("ORC", Rig.Row(rows, 10));
        Assert.NotEqual("", Rig.Row(rows, 11));           // wrapped into columns 23–38 from row 11
        Assert.Equal(string.Join(" ", r.Chooser.Text[word].Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            string.Join(" ", Enumerable.Range(11, 11).Select(k => Rig.Row(rows, k)).Where(x => x.Length > 0)));
        Assert.All(Enumerable.Range(10, 12), row => Assert.Equal("", Rig.Row(r.View.Panel.Lines, row)));
    }

    [Fact]
    public void A_surrender_first_brings_the_creature_into_view_without_a_cursor_box()
    {
        // 0x2BDF0 → 0x319D2(creature, 3, 0): ScrollView on its square with delta 3, the arena's cursor flag off.
        var r = new Rig(Texts());
        var orc = Creature("Orc");
        r.Put(orc, 30, 15);
        r.View.ViewX = 0;
        r.View.ViewY = 0;
        r.Chooser.AiSays(orc, AiMessage.Surrenders);
        var first = r.Pauses[0];
        Assert.True(first.ViewX <= 30 && 30 < first.ViewX + CombatView.ViewSquares);
        Assert.True(first.ViewY <= 15 && 15 < first.ViewY + CombatView.ViewSquares);
        Assert.Null(r.View.Cursor);

        // The panic message moves nothing (the turn start 0x319D2(creature, 2, 1) has already shown the creature).
        var r2 = new Rig(Texts());
        var orc2 = Creature("Orc");
        r2.Put(orc2, 30, 15);
        r2.View.ViewX = 0;
        r2.View.ViewY = 0;
        r2.Chooser.AiSays(orc2, AiMessage.FleesInPanic);
        Assert.All(r2.Pauses, p => Assert.Equal((0, 0), (p.ViewX, p.ViewY)));
    }
}
