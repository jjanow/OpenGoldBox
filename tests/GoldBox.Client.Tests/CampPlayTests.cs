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

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player)
    {
        /// <summary>The screen's shared cells (DS:6B2B, the list top DS:6B29), when the rig passed them.</summary>
        public MenuHighlight? Highlight { get; init; }
    }

    private static Rig Start(string? save = null, string? saveDir = null)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game, TypesText = false };
        game = PorGame.FromSave(dir, art, save ?? dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        player.SaveFolder = saveDir;
        var input = new ActionInput(InputBindings.Defaults());
        var highlight = new MenuHighlight { Index = ExploreScreen.LoadMenuIndex(game.Content.SaveFolder, 'A') };
        return new Rig(new ExploreScreen(game, player, runner, new NoHost(), highlight: highlight), game, runner, input, player)
        {
            Highlight = highlight,
        };
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

    /// <summary>
    /// The loot menu's extended keys through the screen (user s51, <see cref="LootRules.ExtendedKey"/>): Del, Shift-F1
    /// and Shift-F3 answer Share, Take and View; F10 answers Detect only when Detect is on the line; F1 alone stays
    /// the controls key and Shift-F1 is the program's other key.
    /// </summary>
    [Fact]
    public void Loot_menu_extended_keys_reach_the_menu()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var answers = new List<char>();
        void Run(bool detect)
        {
            r.Runner.Start(() =>
            {
                while (r.Runner.Ask(new ChoiceRequest(null, ["VIEW", "EXIT"], false, 1)
                       {
                           ExtendedKeys = scan => LootRules.ExtendedKey(scan, detect) is { } w ? w[0] : null,
                       }) is int a and not 1)
                    answers.Add((char)a);
            });
        }
        void Keys2(params Keys[] keys)
        {
            r.Input.Update(new KeyboardState(keys), default, default, TimeSpan.Zero, _ => null);
            r.Screen.Update(r.Input);
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        }

        Run(detect: false);
        Key(r, Keys.F10);                                  // Detect not on the line: nothing
        Key(r, Keys.Delete);
        Keys2(Keys.LeftShift, Keys.F1);
        Keys2(Keys.RightShift, Keys.F3);
        Key(r, Keys.F2);                                   // no meaning in the loot menu
        Assert.Equal(['S', 'T', 'V'], answers);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Key(r, Keys.E);
        Assert.Null(r.Runner.Pending);

        answers.Clear();
        Run(detect: true);
        Key(r, Keys.F10);
        Assert.Equal(['D'], answers);
        Key(r, Keys.E);
        Assert.Null(r.Runner.Pending);
    }

    /// <summary>
    /// The s53 venue keys through the screen (<c>ExploreScreen.MenuExtendedAnswer</c>): a keypad digit gives its
    /// cursor-pad code (keypad 9 = PgUp = I in Take:), Right keeps moving the lit word, Home stays the member key on a
    /// menu that picks members and gives 0x47 (G, Gems) on Appraise's, which does not.
    /// </summary>
    [Fact]
    public void Venue_extended_keys_reach_the_menu()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var answers = new List<int>();
        void Run(bool partySelect)
        {
            r.Runner.Start(() =>
            {
                while (r.Runner.Ask(new ChoiceRequest(null, ["MONEY", "ITEMS", "EXIT"], false, 2)
                       {
                           PartySelect = partySelect,
                           ExtendedKeys = scan => scan is 0x49 or 0x47 or 0x4D ? scan : null,
                       }) is int a and not 2)
                    answers.Add(a);
            });
        }

        Run(partySelect: true);
        Key(r, Keys.NumPad9);
        Key(r, Keys.Right);                                // the Client's lit word, not M
        Key(r, Keys.Home);                                 // the member key
        Assert.Equal([0x49, ChoiceRequest.PreviousMember], answers);
        Key(r, Keys.E);
        Assert.Null(r.Runner.Pending);

        answers.Clear();
        Run(partySelect: false);
        Key(r, Keys.Home);
        Assert.Equal([0x47], answers);
        Key(r, Keys.E);
        Assert.Null(r.Runner.Pending);
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

    /// <summary>
    /// A camp Save sets $4AFF, so the quit question after it (yes/no 0x2D9A0, the menu routine) runs the camp picture's
    /// frames on every pass; it clears the cell, and the camp menu after it holds them for their delays again
    /// (DOSBox-X s43: the picture changed on every capture under the quit question, 0.22 s holds after No).
    /// </summary>
    [Fact]
    public void The_quit_question_after_a_camp_save_runs_the_camp_picture_every_pass()
    {
        if (Dir() is null)
            return;
        var tmp = Directory.CreateTempSubdirectory("goldbox-camp-");
        try
        {
            var r = Start(saveDir: tmp.FullName);
            r.Runner.Answered = r.Player.MenuEnded; // as Program.cs
            r.Runner.Asking = r.Player.MenuAsked;
            r.Game.Memory.MenuAnimates = false;
            Press(r, GameAction.Encamp);
            var camp = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.True(camp.Animates);
            Assert.False(camp.EveryPass);
            Key(r, Keys.S);
            Key(r, Keys.C);
            var quit = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.Equal(["YES", "NO"], quit.Items);
            Assert.True(quit.EveryPass);                           // the save wrote $4AFF
            Assert.True(quit.Animates);
            Key(r, Keys.N);
            var back = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.True(back.Animates);
            Assert.False(back.EveryPass);                          // the yes/no cleared it
            Assert.False(r.Game.Memory.MenuAnimates);
            r.Runner.Dispose();
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
    public void A_menu_line_keeps_the_programs_spacing()
    {
        // The menu routine draws its line as it is (D s33: the rest-time menu); one space everywhere gives no columns.
        Assert.Equal([0, 7, 12, 18, 25, 29, 35], CampScreens.MenuColumns("Rest   daYs Hours Mins   Inc Dec   Exit"));
        Assert.Null(CampScreens.MenuColumns("Save View Magic Rest Alter Exit"));
        Assert.Null(CampScreens.MenuColumns("Save Game View")); // a word with no capital joins the item before it
    }

    [Fact]
    public void Resting_holds_the_position_line_and_the_minutes_stay_lit()
    {
        // D s33: the rest loop never draws the position line (the camp's Rest draws it after, 0x1804A); the rest-time
        // line keeps the field the menu left lit until the 5th tick shows it again.
        if (Dir() is null)
            return;
        var r = Start();
        Press(r, GameAction.Encamp);
        Key(r, Keys.R);                                        // Rest: the rest-time menu
        Key(r, Keys.M);                                        // Mins
        Key(r, Keys.I);                                        // Inc: 5 minutes
        Assert.Equal((byte)15, r.Player.Text.Cell(17, 18).Colour);
        Key(r, Keys.R);                                        // Rest
        Assert.True(r.Player.PositionLineHeld || r.Runner.Pending is ChoiceRequest);
        Idle(r);
        Assert.False(r.Player.PositionLineHeld);               // released after the rest
        r.Runner.Dispose();
    }

    private static void Frame(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
    }

    /// <summary>The colour of a text cell's lit pixels (the most common non-black one), 0 when the cell is empty.</summary>
    private static byte CellColour(IndexedFrame f, int col, int row)
    {
        var n = new int[16];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                n[f[col * 8 + x, row * 8 + y]]++;
        n[0] = 0;
        return (byte)Array.IndexOf(n, n.Max());
    }

    [Fact]
    public void Rest_time_menu_left_right_move_the_field_and_an_entry_starts_at_its_capital()
    {
        // 0x244ED (D s38): Left / Right move the time field on row 17 and leave row 24's lit word; the menu routine's
        // entries run from their capital to two cells before the next, so "daYs" lit shows "YS" white and "DA" green;
        // Rest lit also lights the D.
        if (Dir() is null)
            return;
        var r = Start();
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.Encamp);
        Key(r, Keys.R);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var menu = Row(f, 24);
        Press(r, GameAction.MenuRight);                        // minutes → days
        Assert.Equal((byte)15, r.Player.Text.Cell(17, 12).Colour);
        Assert.Equal((byte)10, r.Player.Text.Cell(17, 18).Colour);
        var g = new IndexedFrame(320, 200);
        r.Screen.Draw(g, r.Input);
        Assert.Equal(menu, Row(g, 24));                        // row 24 unchanged
        Press(r, GameAction.MenuLeft);                         // days → past days back to minutes
        Assert.Equal((byte)15, r.Player.Text.Cell(17, 18).Colour);
        Key(r, Keys.Y);                                        // daYs
        Frame(r);
        var h = new IndexedFrame(320, 200);
        r.Screen.Draw(h, r.Input);
        Assert.Equal([10, 10, 15, 15], Enumerable.Range(7, 4).Select(c => CellColour(h, c, 24)));
        Key(r, Keys.M);
        Press(r, GameAction.MenuUp);                           // 5 minutes
        Press(r, GameAction.Confirm);                          // Enter = Rest
        Idle(r);
        Assert.Equal(t0 + 5, GameClock.TotalMinutes(r.Game.Memory));
        r.Runner.Dispose();
    }

    [Fact]
    public void Rest_lit_also_lights_the_D_of_days()
    {
        // D s38: with Rest lit the menu routine lights "Rest   d" (its entry runs to two cells before the Y).
        var font = new TextFont(Enumerable.Repeat((byte)0xFF, 177 * 8).ToArray());
        var line = "Rest   daYs Hours Mins   Inc Dec   Exit";
        var (items, keys) = CampScreens.ParseMenu(line);
        var f = new IndexedFrame(320, 200);
        ExploreScreen.DrawLine(f, font, items, 0, 0, i => keys[i], CampScreens.MenuColumns(line));
        Assert.Equal([15, 15, 15, 15, 15, 10, 15, 10, 15, 10], new[] { 0, 1, 2, 3, 7, 8, 9, 10, 12, 13 }.Select(c => CellColour(f, c, 24)));
    }

    [Fact]
    public void The_icon_editors_line_lights_from_its_digit_hotkeys()
    {
        // D s39: "Parts color-1 color-2 Size Exit" (hotkeys P 1 2 S E, 0x2C99A): Parts lit runs to two cells before the
        // 1, so COLOR of color-1 is white too; color-1 lit runs from its 1 to the C of Size − 2 (the next COLOR white).
        var font = new TextFont(Enumerable.Repeat((byte)0xFF, 177 * 8).ToArray());
        var (items, keys) = CampScreens.ParseMenu("Parts color-1 color-2 Size Exit");
        Assert.Equal("P12SE", keys);
        var f = new IndexedFrame(320, 200);
        ExploreScreen.DrawLine(f, font, items, 0, 0, i => keys[i]);
        Assert.Equal("fffff.fffffaf.aaaaaaf.faaa.faaa", Row(f));
        f = new IndexedFrame(320, 200);
        ExploreScreen.DrawLine(f, font, items, 1, 0, i => keys[i]);
        Assert.Equal("faaaa.aaaaaaf.fffffaf.faaa.faaa", Row(f));

        static string Row(IndexedFrame f) =>
            string.Concat(Enumerable.Range(0, 31).Select(c => CellColour(f, c, 24) is var k && k == 0 ? "." : k.ToString("x")));
    }

    [Fact]
    public void A_held_name_stays_through_steps_and_turns_until_the_camp_draws_the_list()
    {
        // D s39 (revive then Exit, Left, Up, Right, Up, A, A, Encamp): the light red name stays through the steps, the
        // turns and the area map (nothing there calls 00BA:0767; here turns and the map, as a step in save A runs its
        // script); the camp's entry (START 0x27D9 case 2) draws it cyan.
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        var who = party.Members[^1];
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);                                 // the area's first list
        r.Player.HoldRosterName(who);
        foreach (var a in new[] { GameAction.TurnLeft, GameAction.TurnAround, GameAction.AreaMap, GameAction.AreaMap, GameAction.TurnRight })
        {
            Press(r, a);
            Idle(r);
            r.Screen.Draw(f, r.Input);
        }
        Assert.True(r.Player.RosterNameHeld(who));
        // LOADCHAR changes the current member without a redraw: the list keeps the player's pick, the name stays.
        int pick = party.CurrentIndex;
        party.Selected = party.Current;
        party.ChangedByScript = true;
        party.CurrentIndex = (pick + 1) % party.Members.Count;
        r.Screen.Draw(f, r.Input);
        Assert.True(r.Player.RosterNameHeld(who));
        party.CurrentIndex = pick;
        party.ChangedByScript = false;
        Press(r, GameAction.Encamp);
        Assert.False(r.Player.RosterNameHeld(who));
        Press(r, GameAction.Cancel);                               // leave the camp
        // A script switch draws the list again (0x402B).
        r.Player.HoldRosterName(who);
        r.Screen.Draw(f, r.Input);
        Assert.True(r.Player.RosterNameHeld(who));
        r.Game.Vm.SetCurrentScript(r.Game.Vm.CurrentScript + 1, r.Game.Vm.CurrentDisk);
        r.Screen.Draw(f, r.Input);
        Assert.False(r.Player.RosterNameHeld(who));
        r.Runner.Dispose();
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
            Click(r, 26, 24);                                      // INC ×6: 30 minutes (the line as the program spaces it: INC at 25–27)
        Click(r, 1, 24);                                           // REST
        Assert.IsType<DelayRequest>(r.Runner.Pending);             // the first key poll, before the first tick (s55)
        Click(r, 10, 20);                                          // a click anywhere: stop resting?
        Click(r, 14, 24);                                          // YES ("STOP RESTING? " then YES)
        Assert.Equal(t0, GameClock.TotalMinutes(r.Game.Memory));   // stopped before any tick

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
    public void Display_is_the_programs_list_an_empty_entry_then_names_as_labels()
    {
        // P s34 (0x187ED): one full-screen window with the title centred, 0x2D669's window (1, 4)-(38, 22) with colours
        // 11 / 10 / 15 and Exit alone; the list opens with an empty entry, each member's name a label, an empty entry after.
        if (Dir() is null)
            return;
        var r = Start();
        Press(r, GameAction.Encamp);
        Key(r, Keys.M);                                        // Magic
        r.Highlight!.ListTop = MenuHighlight.DosListTop;
        Key(r, Keys.D);                                        // Display
        var page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Key(r, Keys.None);
        Assert.Equal(0, r.Highlight.ListTop);                  // position 0: the list routine's global top to 0 (s45)
        Assert.True(page.Spell!.CentredTitle);
        Assert.Equal((byte)11, page.Spell.LabelColour);
        Assert.Equal(4, page.FirstRow);
        Assert.Equal(19, page.Paging!.Rows);
        Assert.Equal("", page.Rows[0]);
        Assert.DoesNotContain(0, page.Labels);
        Assert.Contains(1, page.Labels);
        Assert.Equal(r.Game.Memory.Party!.Members[0].Name.ToUpperInvariant(), page.Rows[1]);
        Assert.Equal("", page.Rows[^1]);
        Assert.Equal(["EXIT"], page.Actions);
        Key(r, Keys.Enter);                                    // any key that ends the list leaves
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);        // the Magic menu
        r.Runner.Dispose();
    }

    [Fact]
    public void Memorize_types_its_slot_lines_first_asks_over_the_page_and_redraws_at_the_end()
    {
        // D s33 (DOSBox-X, camp Memorize): 0x17DD5's message (name row 18, "can memorize:" from row 19, the class lines)
        // over the camp screen with the box's frame drawn again, then the book page with those lines in its text box; the
        // keep question over the pending list (prompt colour 14); START 0x27D9 at the end empties the text window.
        if (Dir() is null)
            return;
        var r = Start();
        var bakshi = r.Game.Memory.Party!.Members[1];
        for (int i = 0; i < 21; i++)
            bakshi[0x17 + i] = 0; // every slot free
        Press(r, GameAction.Encamp);
        Key(r, Keys.M);                                        // Magic
        Press(r, GameAction.NextMember);                       // Bakshi
        Assert.Same(bakshi, r.Game.Memory.Party.Current);
        Key(r, Keys.M);                                        // Memorize
        var book = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.NotNull(book.Spell?.Box);
        Assert.Equal(11, book.Paging!.Rows);
        Assert.Equal('B', r.Player.Text.Cell(18, 1).Char);   // the slot message stays in the text box under the page
        Assert.True(r.Player.TextBoxFramed);
        Key(r, Keys.Enter);                                    // the highlighted spell goes pending
        Assert.IsType<ListRequest>(r.Runner.Pending);          // the book again (the position kept)
        Press(r, GameAction.Cancel);                           // nothing chosen: the pending list
        var pending = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.NotSame(book, pending);
        Assert.Null(pending.Spell!.Box);
        Key(r, Keys.Enter);                                    // its Exit
        var question = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal((byte)14, question.MenuPromptColour);
        Assert.Same(pending, r.Player.PageUnder);              // asked over the page, no text box
        Assert.False(r.Player.PageUnderBox);
        Key(r, Keys.Y);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);        // the Magic menu
        Assert.Null(r.Player.PageUnder);
        Assert.True(r.Player.Text.IsEmpty);                    // the redraw
        Assert.False(r.Player.TextBoxFramed);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_camp_cast_keeps_the_spell_list_under_the_text_box()
    {
        // 0x17D15–0x17D39 (P, s30; DOSBox-X s29): the box's frame is drawn over the spell list and its inside blanked;
        // the list's title stays above it and row 24 keeps the list's menu line while the cast's messages show.
        if (Dir() is null)
            return;
        var tmp = Directory.CreateTempSubdirectory("goldbox-camp-");
        try
        {
            var r = Start(saveDir: tmp.FullName);
            var who = r.Game.Memory.Party!.Current!;
            for (int i = 0; i < 21; i++)
                who[0x17 + i] = 0;
            who[0x17] = 1; // one memorised spell
            Press(r, GameAction.Encamp);
            Key(r, Keys.M);
            Key(r, Keys.C);
            var list = Assert.IsType<ListRequest>(r.Runner.Pending);
            Assert.NotNull(list.Paging);
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            var title = Row(f, 0);
            var menuLine = Row(f, 24);
            Key(r, Keys.Enter);                                    // the highlighted spell (row 1 after its heading)
            for (int guard = 0; r.Runner.Pending is not DelayRequest && r.Runner.Pending is not null && guard < 10; guard++)
                Key(r, Keys.Enter);                                // a target question, if the spell asks one
            Assert.Same(list, r.Player.PageUnder);
            var g = new IndexedFrame(320, 200);
            r.Screen.Draw(g, r.Input);
            Assert.Equal(title, Row(g, 0));                         // the page's title row is still there
            if (r.Runner.Pending is DelayRequest { BottomLine: null })
                Assert.Equal(menuLine, Row(g, 24));                 // and its menu line on row 24
            Assert.NotEqual(Row(f, 16), Row(g, 16));                // the box's top frame row over the list
            r.Runner.Dispose();
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    private static byte[] Row(IndexedFrame f, int row)
    {
        var px = new byte[320 * 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 320; x++)
                px[y * 320 + x] = f[x, row * 8 + y];
        return px;
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
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game, TypesText = false };
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
