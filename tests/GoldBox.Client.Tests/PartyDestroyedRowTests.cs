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
/// The party-destroyed window keeps row 24 as the fight left it (read s39: from the defeat at 0x6AA5 through 0x5A47 and
/// 0x672A to the window 0x6B37–0x6BAA nothing writes the row before the key line 0709:0BF5; DOSBox-X s39: the vampire's
/// move line stays under "THE END!" while its text types). Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class PartyDestroyedRowTests
{
    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    private const int Row24 = 24 * 8;

    private static bool RowHas(IndexedFrame f, byte colour) => f.Pixels.Skip(Row24 * f.Width).Take(8 * f.Width).Contains(colour);

    [Fact]
    public void The_fights_last_move_line_stays_on_row_24_until_the_key_line()
    {
        if (TestRig.PorDir() is not { } dir)
            return;
        var content = new PorGameDirectory(dir);
        using var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory, typesText: true);
        game = PorGame.FromSave(content, PorViewArt.Load(content), content.Find("SAVGAMA.DAT")!,
            new PorPlayer(player, player, player, player), 7);
        game.Memory.TypesText = true;
        game.Memory.GameSpeed = 4;
        var screen = new ExploreScreen(game, player, runner, new TestRig.NoHost());
        var input = new ActionInput(InputBindings.Defaults());
        var frame = new IndexedFrame(320, 200);

        // The fight's last frame: an AI creature's move line on row 24 (green).
        var icon = new byte[64];
        Array.Fill(icon, (byte)0xFF);
        var map = new CombatMap(new CombatTables(new byte[64 * 4], icon, new byte[16], new byte[16], [7, 2, 3, 6],
            new byte[8], new byte[8], new byte[60]));
        player.FightView.Fight = new Fight(new EclMemory(), new EncounterState(),
            new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), map);
        player.FightView.MovePrompt = "MOVE/ATTACK, MOVE LEFT = 11";
        screen.Draw(frame, input);
        Assert.True(RowHas(frame, Chrome.Green));
        // Its pixels under the area palette: the combat screen's 0/8 swap undone (the background black again).
        var fightRow = frame.Pixels.Skip(Row24 * frame.Width).Take(8 * frame.Width)
            .Select(v => v switch { 0 => (byte)8, 8 => (byte)0, _ => v }).ToArray();
        Assert.Contains((byte)0, fightRow);
        player.FightView.Clear();

        runner.Start(() => player.PartyDestroyed(null!));
        // The text types (a chained delay per character): the window is up, row 24 is the fight's.
        var typing = Assert.IsType<DelayRequest>(runner.Pending);
        Assert.True(typing.Chained);
        screen.Draw(frame, input);
        Assert.NotNull(player.Window);
        Assert.Equal(fightRow, frame.Pixels.Skip(Row24 * frame.Width).Take(8 * frame.Width).ToArray());

        for (int guard = 0; runner.Pending is DelayRequest && guard < 2000; guard++)
            runner.Answer(null);
        // The key line (START 0x7C85 through 0709:0BF5) replaces the row.
        Assert.IsType<KeyRequest>(runner.Pending);
        screen.Draw(frame, input);
        Assert.False(RowHas(frame, Chrome.Green));
        runner.Answer(null);
        Assert.False(player.KeepsCombatRow24);
    }
}
