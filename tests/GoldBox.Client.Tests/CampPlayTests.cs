using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>The camp through the explore screen and the action layer, on save A (skipped without GOLDBOX_POR_DIR).</summary>
public class CampPlayTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class NoHost : IScreenHost
    {
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static Rig Start(string? save = null, string? saveDir = null)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        game = PorGame.FromSave(dir, art, save ?? dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        player.SaveFolder = saveDir;
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
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

    /// <summary>Frames with no input until the rest's polls are answered and something else waits.</summary>
    private static void Idle(Rig r)
    {
        for (int guard = 0; r.Runner.Pending is DelayRequest && guard < 2000; guard++)
        {
            Thread.Sleep(5);
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            r.Screen.Update(r.Input);
        }
    }

    [Fact]
    public void Rest_with_actions_then_save_with_keys_and_load_it_again()
    {
        if (Dir() is null)
            return;
        var tmp = Directory.CreateTempSubdirectory("goldbox-camp-");
        try
        {
            var r = Start(saveDir: tmp.FullName);
            long t0 = GameClock.TotalMinutes(r.Game.Memory);
            Press(r, GameAction.Encamp);
            Key(r, Keys.R);                                        // Rest (the prep time is 0)
            var rest = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.Equal(7, rest.Items.Count);
            Press(r, GameAction.MenuUp);                           // Inc: 5 minutes (minutes highlighted first)
            Press(r, GameAction.MenuUp);
            Key(r, Keys.R);                                        // REST (the menu opened on the camp's Rest place, 4th)
            Idle(r);
            Assert.Equal(t0 + 10, GameClock.TotalMinutes(r.Game.Memory));
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);        // back at the camp menu
            Assert.Same(r.Game.Camp, r.Player.CampState);          // the session's program variables are the game's
            r.Game.Memory.GameSpeed = 5;                           // DS:49E2: the save copies it into $49FC
            Key(r, Keys.S);                                        // Save
            Key(r, Keys.C);                                        // slot C
            Key(r, Keys.N);                                        // Quit to DOS? No
            Press(r, GameAction.Cancel);                           // leave the camp
            Assert.Null(r.Runner.Pending);
            var file = Path.Combine(tmp.FullName, "c.json");          // an engine save (docs/module-format.md §15)
            Assert.True(File.Exists(file));
            var names = r.Game.Memory.Party!.Members.Select(m => m.Name).ToList();
            r.Runner.Dispose();

            var back = Start(file, tmp.FullName);
            Assert.Equal(names, back.Game.Memory.Party!.Members.Select(m => m.Name));
            Assert.Equal(t0 + 10, GameClock.TotalMinutes(back.Game.Memory));
            var doc = System.Text.Json.JsonSerializer.Deserialize<GoldBox.Data.Module.EngineSaveDocument>(File.ReadAllText(file), GoldBox.Data.Module.ModuleSchemas.Strict)!;
            Assert.Equal(["c.json"], Directory.EnumerateFiles(tmp.FullName).Select(Path.GetFileName));
            Assert.Equal(names.Count, doc.Party!.Count);
            var written = GoldBox.Data.Module.Por.PorEngineSave.ToSaveGame(doc);
            Assert.Equal("C", doc.Por!.Slot);
            Assert.Equal(2, written.InterfaceState);
            Assert.Equal(r.Game.Memory.ViewMode, written.LocationType);   // DS:49FA
            Assert.Equal(5, back.Game.Memory.GameSpeed);
            back.Runner.Dispose();
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_camp_uses_the_games_state_and_the_level_seams()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Assert.Same(r.Game.Camp, r.Player.CampState);
        var effects = r.Player.CampEffects(r.Game.Memory.Party!);
        Assert.NotNull(effects.LevelStats);                        // FightKit.WireLevels with the program's tables
        Assert.NotNull(effects.RaceLevelLimit);
        Assert.NotNull(effects.ExperienceTable);
        Assert.NotSame(r.Game.OutsideEffects, effects);            // its own engine: the camp's spells hook into it
        r.Runner.Dispose();
    }

    /// <summary>One left click at cell (col, row) through the same path as the window (MapClick, Inject, Update).</summary>
    private static void Click(Rig r, int col, int row)
    {
        var down = new MouseState(col * 8 + 3, row * 8 + 3, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
        r.Input.Update(default, default, down, TimeSpan.Zero, p => p);
        foreach (var c in r.Input.Clicks.ToList())
            if (r.Screen.MapClick(c) is { } a)
                r.Input.Inject(a);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, p => p);
    }

    [Fact]
    public void Camp_by_mouse_rest_stop_and_place_order()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Click(r, 16, 24);                                          // ENCAMP on the command line (AREA CAST VIEW ENCAMP)
        Assert.True(r.Player.Camping);
        Click(r, 22, 24);                                          // REST ("CAMP: " then SAVE VIEW MAGIC REST)
        Assert.Equal(7, Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items.Count);
        for (int i = 0; i < 6; i++)
            Click(r, 22, 24);                                      // INC ×6: 30 minutes (REST DAYS HOURS MINS INC)
        Click(r, 1, 24);                                           // REST
        Assert.IsType<DelayRequest>(r.Runner.Pending);             // the first key poll, after 4 ticks
        Click(r, 10, 20);                                          // a click anywhere: stop resting?
        Click(r, 14, 24);                                          // YES ("STOP RESTING? " then YES)
        Assert.Equal(t0 + 20, GameClock.TotalMinutes(r.Game.Memory));

        var first = party.Members[0];
        Assert.Equal("CAMP:", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
        Click(r, 28, 24);                                          // ALTER (… REST ALTER)
        Assert.Equal("ALTER:", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
        Click(r, 8, 24);                                           // ORDER ("ALTER: " then ORDER)
        Assert.Equal("SELECT", Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items[0]);
        Click(r, 14, 24);                                          // SELECT → place ("PARTY ORDER: " SELECT)
        Assert.Equal("PLACE", Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items[0]);
        Click(r, 20, 5);                                         // a name below the current one: down one place
        Assert.Same(first, party.Members[1]);
        Assert.Same(first, party.Current);
        Click(r, 20, 0);                                           // nothing there
        Press(r, GameAction.Cancel);
        Press(r, GameAction.Cancel);
        Press(r, GameAction.Cancel);
        Assert.Null(r.Runner.Pending);
        r.Runner.Dispose();
    }

    [Fact]
    public void Area_menus_open_on_the_shared_highlight_as_in_dosbox()
    {
        // DOSBox-X (s19), save A loaded as A: Encamp → the camp menu on REST; R → the rest menu on MINS; Exit (7th) →
        // the camp menu on SAVE (past its end); A → the Alter menu on its 5th; Escape → the camp menu on ALTER; Save, C
        // → the quit question on NO; N → the camp menu on VIEW.
        if (Dir() is null)
            return;
        var tmp = Directory.CreateTempSubdirectory("goldbox-camp-");
        try
        {
            var r = Start(saveDir: tmp.FullName);
            Assert.Equal(0, r.Screen.MenuHighlight);               // AREA
            Press(r, GameAction.Encamp);
            Sync(r);
            Assert.Equal(3, r.Screen.Selected);                    // REST
            Key(r, Keys.R);
            Sync(r);
            Assert.Equal(7, Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items.Count);
            Assert.Equal(3, r.Screen.Selected);                    // MINS
            Key(r, Keys.E);
            Sync(r);
            Assert.Equal("CAMP:", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
            Assert.Equal(0, r.Screen.Selected);                    // SAVE
            Key(r, Keys.A);
            Sync(r);
            Assert.Equal(4, r.Screen.Selected);
            Press(r, GameAction.Cancel);
            Sync(r);
            Assert.Equal(4, r.Screen.Selected);                    // ALTER
            Key(r, Keys.S);
            Key(r, Keys.C);
            Sync(r);
            Assert.Equal(["YES", "NO"], Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items);
            Assert.Equal(1, r.Screen.Selected);                    // NO
            Key(r, Keys.N);
            Sync(r);
            Assert.Equal(1, r.Screen.Selected);                    // VIEW
            Press(r, GameAction.Cancel);                           // Escape leaves the cell: the command line on CAST
            Assert.Null(r.Runner.Pending);
            Assert.Equal(1, r.Screen.MenuHighlight);
            r.Runner.Dispose();
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_camp_picture_animates_while_a_camp_menu_waits()
    {
        // PIC 29 (two frames, delays 2 and 2: 200 ms each); DOSBox-X alternates them about every 0.22 s.
        if (Dir() is null)
            return;
        var r = Start();
        Press(r, GameAction.Encamp);
        Sync(r);
        Assert.Equal(0, r.Screen.PictureFrame);
        var seen = new HashSet<int>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 700)
        {
            Thread.Sleep(20);
            Sync(r);
            seen.Add(r.Screen.PictureFrame);
        }
        Assert.Equal([0, 1], seen.Order());
        Press(r, GameAction.Cancel);
        r.Runner.Dispose();
    }

    /// <summary>A frame with no input (the screen takes up the request on screen).</summary>
    private static void Sync(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
    }

    [Fact]
    public void Alter_order_by_pad_actions_moves_the_member()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        var first = party.Members[0];
        Press(r, GameAction.Encamp);
        Key(r, Keys.A);                                            // Alter
        Assert.Equal(4, r.Screen.MenuHighlight);                   // the Alter menu opens on the camp's Alter place
        Key(r, Keys.O);                                            // ORDER
        Press(r, GameAction.Confirm);                              // SELECT → place mode
        Press(r, GameAction.NextMember);                           // RB / End: one place down
        Assert.Same(first, party.Members[1]);
        Assert.Same(first, party.Current);
        Press(r, GameAction.Cancel);                               // leave order
        Press(r, GameAction.Cancel);                               // leave alter
        Press(r, GameAction.Cancel);                               // leave the camp
        Assert.Null(r.Runner.Pending);
        r.Runner.Dispose();
    }

    private sealed class Stack : IScreenHost
    {
        public List<IScreen> Pushed { get; } = [];
        public int Pops { get; private set; }
        public void Push(IScreen screen) => Pushed.Add(screen);
        public void Pop() => Pops++;
        public void Quit() { }
        public void SaveBindings() { }
    }

    private static void To(IScreen s, ActionInput input, Keys k)
    {
        input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        s.Update(input);
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    private static void To(IScreen s, ActionInput input, GameAction a)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        s.Update(input);
    }

    /// <summary>
    /// Alter Icon (0x1951F) runs the icon editor 0x1D1AC on the current member: the explore screen puts the party menu's
    /// icon page over the camp, which takes keys, the pad and the mouse, and closes when the Alter menu asks again.
    /// </summary>
    [Fact]
    public void Alter_icon_edits_the_current_members_icon_over_the_camp()
    {
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        var program = dir.Program()!;
        var folder = Directory.CreateTempSubdirectory("camp-icon-");
        try
        {
            var flow = new PartyMenuFlow(runner.Ask, new PartyMenuTexts(program, player.Resources), program.CreationTables()!,
                PorFight.Kit(dir)!.Items, new PorRoster(folder.FullName), 1);
            player.IconEditor = flow.EditIcon;
            var host = new Stack();
            var input = new ActionInput(InputBindings.Defaults());
            var screen = new ExploreScreen(game, player, runner, host)
            {
                PartyPages = () => new PartyMenuScreen(flow, runner, host, game.Art.Font, player.Resources,
                    new ClientArt(dir, art, new ScreenPalette(), PorArtChoice.Dos), _ => null!) { Program = program, Overlay = true },
            };
            var r = new Rig(screen, game, runner, input, player);
            var who = game.Memory.Party!.Current!;
            byte colours = who[IconEditor.Colours];
            Press(r, GameAction.Encamp);
            Key(r, Keys.A);                                        // Alter
            Key(r, Keys.I);                                        // Icon
            var main = Assert.IsType<IconRequest>(runner.Pending);
            Assert.Same(who, main.Character);
            Assert.Equal("P12SE", main.Hotkeys);                   // the editor's main menu (DS:07E8)
            input.Update(default, default, default, TimeSpan.Zero, _ => null);
            screen.Update(input);                                  // a frame: the icon page goes over the camp
            var page = Assert.IsType<PartyMenuScreen>(Assert.Single(host.Pushed));
            To(page, input, Keys.D1);                              // keys: color-1, Body, Next, Keep
            To(page, input, Keys.B);
            To(page, input, Keys.N);
            Assert.Equal((colours & 0xF0) | ((colours + 1) & 0x0F), who[IconEditor.Colours]);
            To(page, input, Keys.K);
            To(page, input, GameAction.Cancel);                    // the pad's B: Escape, back to the main menu
            Assert.Equal("P12SE", Assert.IsType<IconRequest>(runner.Pending).Hotkeys);
            // The mouse: a click on the main menu's last word (Exit) on the bottom line.
            var words = Assert.IsType<IconRequest>(runner.Pending).Actions;
            int col = words.Take(words.Count - 1).Sum(w => w.Length + 1);
            input.Update(default, default, default, TimeSpan.Zero, _ => null);
            if (page.MapClick(new ScreenClick(col * 8 + 2, 24 * 8 + 4, false)) is { } click)
                input.Inject(click);
            page.Update(input);
            var ok = Assert.IsType<IconRequest>(runner.Pending);   // the IconOk question, opening on No
            Assert.Equal(2, ok.Actions.Count);
            To(page, input, GameAction.MenuLeft);                  // the pad: to Yes, A
            To(page, input, GameAction.Confirm);
            Assert.IsType<ChoiceRequest>(runner.Pending);          // the Alter menu again
            page.Update(input);
            Assert.Equal(1, host.Pops);                            // the page closes over the camp's menu
            Assert.Equal((colours & 0xF0) | ((colours + 1) & 0x0F), who[IconEditor.Colours]);
            Key(r, Keys.E);                                        // Exit Alter
            Press(r, GameAction.Cancel);                           // leave the camp
            Assert.Null(runner.Pending);
        }
        finally
        {
            runner.Dispose();
            folder.Delete(recursive: true);
        }
    }
}
