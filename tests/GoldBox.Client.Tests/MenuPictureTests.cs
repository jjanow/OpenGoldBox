using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The first menu after a Load while VM $4AFF is set draws the loaded picture at cell (3, 3) on every pass, whatever
/// page is up (0x2CD5D–0x2CDD6; training.md "The picture under $4AFF on other pages"). DOSBox-X (s44): save A with
/// vector 4 = PICTURE 2 and then a shop, a temple, a COMBAT with no monsters or a fight, loaded from the start-up menu.
/// Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class MenuPictureTests
{
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private const byte Picture = 0x0E, ClrMon = 0x1C, LoadMon = 0x0B, Combat = 0x24, Treasure = 0x27, Save = 0x09;

    private static byte[] Imm(params byte[] values) => [.. values.SelectMany(v => new byte[] { 0, v })];

    /// <summary>Vector 4 of each test save: PICTURE 2, then what follows.</summary>
    private static byte[] Code(string kind) => kind switch
    {
        "shop" => [Picture, .. Imm(2), ClrMon, Treasure, .. Imm(0, 0, 0, 0, 0, 0, 0, 255), Save, .. Imm(1), 1, 0x6C, 0x6E, Combat],
        "temple" => [Picture, .. Imm(2), ClrMon, Save, .. Imm(1), 1, 0xE2, 0x6D, Combat],
        "treasure" => [Picture, .. Imm(2), ClrMon, Treasure, .. Imm(100, 0, 0, 0, 0, 0, 0, 255), Combat],
        "fight" => [Picture, .. Imm(2), ClrMon, LoadMon, .. Imm(2, 8, 2), Combat],
        _ => throw new ArgumentException(kind),
    };

    /// <summary>Save A with vectors 0–3 → EXIT and vector 4 = <paramref name="code"/>, written as SAVGAMB.DAT into <paramref name="folder"/>.</summary>
    private static string WriteSave(PorGameDirectory dir, DirectoryInfo folder, byte[] code, bool quick = false)
    {
        var bytes = File.ReadAllBytes(dir.Find("SAVGAMA.DAT")!);
        var area = new List<byte>();
        foreach (ushort v in new ushort[] { 0x991F, 0x991F, 0x991F, 0x991F, 0x9920 })
            area.AddRange([1, 1, (byte)v, (byte)(v >> 8)]);
        area.AddRange(new byte[0x1F - 0x14]);
        area.Add(0);                                     // $991F: EXIT
        area.AddRange(code);                             // $9920
        area.Add(0);
        Array.Clear(bytes, PorSaveGame.OffScript, 7680);
        area.CopyTo(bytes, PorSaveGame.OffScript);
        foreach (var f in Directory.GetFiles(dir.Root, "CHRDATA*"))
        {
            var copy = Path.Combine(folder.FullName, Path.GetFileName(f));
            File.Copy(f, copy);
            if (quick && copy.EndsWith(".SAV", StringComparison.OrdinalIgnoreCase))
            {
                var record = File.ReadAllBytes(copy);
                record[0x10F] = 1;                           // computer-controlled (Quick) from the fight's start
                File.WriteAllBytes(copy, record);
            }
        }
        var path = Path.Combine(folder.FullName, "SAVGAMB.DAT");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The party menu's Load of the test save (its page up, <see cref="ClientPlayer.PartyMenuUnder"/>), run to the first request.</summary>
    private static Rig Load(PorGameDirectory dir, string path, bool menuAnimates = true, bool quickFight = false,
        bool fightMenu = false)
    {
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        runner.Answered = player.MenuEnded; // as Program.cs
        runner.Asking = player.MenuAsked;
        player.PartyMenuUnder = true;
        var who = new PorPlayer(player, player, player, player, FightMenu: fightMenu ? player.Chooser : null, QuickFight: quickFight);
        runner.Start(() => game = PorGame.LoadAtPartyMenu(dir, PorViewArt.Load(dir), path, who, 1, started: g =>
        {
            game = g;
            if (!menuAnimates)
                g.Memory.MenuAnimates = false;
        }));
        var art = new ClientArt(dir, game!.Art, new ScreenPalette(), PorArtChoice.Dos);
        var screen = new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art);
        var page = new byte[320 * 8 * 24];
        Array.Fill(page, (byte)5);
        screen.PartyMenuPage = page;
        return new Rig(screen, game, runner, new ActionInput(InputBindings.Defaults()), player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    private static IndexedFrame Draw(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    /// <summary>Pixels of the 88 × 88 window at (24, 24) that are not black.</summary>
    private static int InBox(IndexedFrame f)
    {
        int n = 0;
        for (int y = 24; y < 112; y++)
            for (int x = 24; x < 112; x++)
                if (f[x, y] != 0)
                    n++;
        return n;
    }

    private static void Run(string kind, Action<PorGameDirectory, string> test, bool quick = false)
    {
        if (TestRig.PorDir() is not { } d)
            return;
        var dir = new PorGameDirectory(d);
        var folder = Directory.CreateTempSubdirectory("menu-picture-");
        try
        {
            test(dir, WriteSave(dir, folder, Code(kind), quick));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("temple")]
    public void A_venues_first_menu_after_a_load_runs_the_picture_in_its_view_box_until_the_screen_is_drawn_again(string kind) =>
        Run(kind, (dir, path) =>
        {
            // DOSBox-X s44: the shop's (temple's) screen with PICTURE 2 running in the view box, a frame per pass; after
            // Pool the last frame stays; View's exit (START 0x3379 case 1) leaves the box empty.
            var r = Load(dir, path);
            var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.True(menu.EveryPass);
            Assert.False(r.Player.PartyMenuUnder);              // the entry's START 0x3379 replaced the party menu page
            Assert.True(r.Player.VenuePictureOver);
            var f = Draw(r);
            Assert.NotEqual(5, f[0, 0]);
            Assert.True(InBox(f) > 2000);
            Key(r, Keys.P);                                      // Pool: no redraw
            var again = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(again.EveryPass);                       // the first menu cleared $4AFF
            Assert.True(r.Player.VenuePictureOver);
            Assert.True(InBox(Draw(r)) > 2000);
            Key(r, Keys.V);
            for (int guard = 0; r.Runner.Pending is not ChoiceRequest { Vertical: false } && guard < 10; guard++)
                Press(r, GameAction.Cancel);                     // View's page, out again
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(r.Player.VenuePictureOver);
            Assert.Equal(0, InBox(Draw(r)));
            r.Runner.Dispose();
        });

    [Fact]
    public void A_venue_menu_with_the_cell_clear_keeps_its_view_box_empty() =>
        Run("shop", (dir, path) =>
        {
            // DOSBox-X s24: PICTURE 2 before the shop with $4AFF = 0 leaves the box empty.
            var r = Load(dir, path, menuAnimates: false);
            var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(menu.EveryPass);
            Assert.False(r.Player.VenuePictureOver);
            Assert.Equal(0, InBox(Draw(r)));
            r.Runner.Dispose();
        });

    [Fact]
    public void The_summarys_press_enter_runs_the_loaded_picture_over_the_window_then_the_loot_shows_the_chest() =>
        Run("treasure", (dir, path) =>
        {
            // DOSBox-X s44: a COMBAT with no monsters skips the fight start (0x9881) and its free; the summary window's
            // press-enter menu ran PICTURE 2 over the window; the loot menu (START 0x3379 case 6) showed the chest.
            var r = Load(dir, path);
            var key = Assert.IsType<KeyRequest>(r.Runner.Pending);
            Assert.True(key.AsMenu);
            Assert.True(key.EveryPass);
            Assert.False(r.Player.PartyMenuUnder);
            Assert.False(r.Player.LootDrawn);
            var f = Draw(r);
            Assert.NotEqual(5, f[0, 0]);
            Assert.True(InBox(f) > 2000);                        // the goblin over the window
            int first = r.Screen.PictureFrame;
            r.Screen.Update(r.Input);
            Assert.NotEqual(first, r.Screen.PictureFrame);       // a frame per pass
            Press(r, GameAction.Confirm);
            var loot = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(loot.EveryPass);
            Assert.True(r.Player.LootDrawn);
            r.Runner.Dispose();
        });

    [Fact]
    public void A_fight_from_a_loaded_entry_frees_the_picture() =>
        Run("fight", (dir, path) =>
        {
            // 0xE40D: the fight start frees the frame table (DOSBox-X s44: the combat menu without PICTURE 2). With no
            // fight menu nothing asks "continue battle", so $4AFF is still set at the summary's press-enter menu, which
            // has no picture to draw. In v1.3 only a fight whose party side empties by fleeing gets there (P); DOSBox-X
            // s45 with the question patched out of a scratch GAME.OVR: the summary stood still, no picture.
            var r = Load(dir, path, quickFight: true);
            for (int guard = 0; r.Runner.Pending is not (null or KeyRequest { AsMenu: true }) && guard < 5000; guard++)
                r.Runner.Answer(null);
            var key = Assert.IsType<KeyRequest>(r.Runner.Pending);
            Assert.True(r.Game.Memory.MenuAnimates);             // no menu ran since the Load
            Assert.False(key.EveryPass);
            Assert.False(r.Player.PartyMenuUnder);
            Assert.Equal(ViewPictureKind.Picture, r.Player.ViewPicture?.Kind); // still PICTURE 2 to the Client, but freed
            r.Runner.Dispose();
        });

    [Fact]
    public void A_won_fight_asks_to_continue_and_that_menu_clears_the_flag_before_the_summary() =>
        Run("fight", (dir, path) =>
        {
            // A won round asks "continue battle" (yes/no 0x2D9A0 from 0xA1A5) unless DS:49F4, which only the start-up
            // clears (0xE99E) and nothing sets; that menu clears $4AFF. DOSBox-X s45 (all six on Quick, so no player
            // menu): the question with no picture (the table freed at 0xE40D), No, the summary with no picture.
            var r = Load(dir, path, quickFight: true, fightMenu: true);
            for (int guard = 0; r.Runner.Pending is not (null or ChoiceRequest) && guard < 5000; guard++)
                r.Runner.Answer(null);
            var question = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.Equal(new CombatText()[CombatMsg.ContinueBattle].TrimEnd(), question.MenuPrompt); // the rig's text
            Assert.Equal(2, question.Items.Count);
            Assert.Equal(1, question.Selected);                  // opens on No
            Assert.False(question.EveryPass);
            Assert.True(r.Game.Memory.MenuAnimates);             // the first menu since the Load
            r.Runner.Answer(1);                                  // No
            for (int guard = 0; r.Runner.Pending is not (null or KeyRequest { AsMenu: true }) && guard < 5000; guard++)
                r.Runner.Answer(null);
            var key = Assert.IsType<KeyRequest>(r.Runner.Pending);
            Assert.False(r.Game.Memory.MenuAnimates);
            Assert.False(key.EveryPass);
            Assert.False(r.Player.PartyMenuUnder);
            r.Runner.Dispose();
        }, quick: true);
}
