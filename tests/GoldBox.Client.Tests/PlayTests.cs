using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

public class ScriptRunnerTests
{
    [Fact]
    public void Worker_parks_on_each_request_and_gets_the_answers()
    {
        using var r = new ScriptRunner();
        var got = new List<object?>();
        r.Start(() =>
        {
            got.Add(r.Ask(new ChoiceRequest(null, ["A", "B"], false)));
            got.Add(r.Ask(new TextRequest("", false, 5)));
        });
        Assert.True(r.Busy);
        Assert.IsType<ChoiceRequest>(r.Pending);
        r.Answer(1);
        Assert.IsType<TextRequest>(r.Pending);
        r.Answer("OK");
        Assert.False(r.Busy);
        Assert.Null(r.Pending);
        Assert.Equal([1, "OK"], got);
    }

    [Fact]
    public void A_fault_ends_the_command_and_is_kept()
    {
        using var r = new ScriptRunner();
        r.Start(() => throw new InvalidOperationException("boom"));
        Assert.False(r.Busy);
        Assert.Equal("boom", r.Fault?.Message);
    }

    [Fact]
    public void Dispose_stops_a_parked_worker()
    {
        var r = new ScriptRunner();
        bool after = false;
        r.Start(() =>
        {
            r.Ask(new DelayRequest(TimeSpan.FromHours(1)));
            after = true;
        });
        r.Dispose();
        Assert.False(r.Busy);
        Assert.False(after);
        Assert.Null(r.Fault);
    }
}

public class TextWindowTests
{
    [Fact]
    public void Prints_from_the_cursor_and_wraps_whole_words()
    {
        var t = new TextWindow();
        t.Print("ab");
        t.Print("cd");
        Assert.Equal("ABCD", t.Lines[0]);
        t.Print(new string('x', 32) + " yy", clear: true);   // 33 cells with the space, then "YY" needs 2 more of 38: fits
        Assert.Equal(new string('X', 32) + " YY", t.Lines[0]);
        t.Print(new string('x', 36) + " bb", clear: true);   // "BB" would end in column 39: next row
        Assert.Equal([new string('X', 36), "BB"], t.Lines.Take(2));
    }

    [Fact]
    public void Breaks_after_a_delimiter_too()
    {
        var t = new TextWindow();
        t.Print(new string('a', 34) + "-bbbb");                 // the word ends with the hyphen: "BBBB" goes down
        Assert.Equal([new string('A', 34) + "-", "BBBB"], t.Lines.Take(2));
    }

    [Fact]
    public void A_full_box_waits_for_a_key_then_starts_again_at_the_top()
    {
        int pauses = 0;
        var t = new TextWindow { Pause = () => pauses++ };
        t.Print(string.Join(" ", Enumerable.Range(1, 7).Select(i => new string((char)('a' + i), 30))));
        Assert.Equal(1, pauses);
        Assert.Equal(new string('H', 30), t.Lines[0]);          // the 7th word on a cleared box
        Assert.All(t.Lines.Skip(1), l => Assert.Equal("", l));
    }

    [Fact]
    public void Print_return_and_exit_move_the_cursor_without_clearing()
    {
        var t = new TextWindow();
        t.Print("one");
        t.NewLine();
        t.Print("two");
        Assert.Equal(["ONE", "TWO"], t.Lines.Take(2));
        t.Home();
        t.Print("X");
        Assert.Equal(["XNE", "TWO"], t.Lines.Take(2));
        Assert.False(t.IsEmpty);
        t.Clear();
        Assert.True(t.IsEmpty);
    }
}

/// <summary>The explore screen headless, on save A of the player's files (skipped without GOLDBOX_POR_DIR).</summary>
public class ExploreScreenTests
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

    private static Rig Start(uint seed = 7)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { TypesText = false };
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), seed);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    /// <summary>
    /// Answer whatever the game waits for: back out where the request allows it (the loot, shop and camp menus loop
    /// until Exit; their exit question, the menu helper 0x8B57, takes only its hotkeys: N leaves), else take the first
    /// choice.
    /// </summary>
    private static void Settle(Rig r)
    {
        for (int guard = 0; r.Runner.Pending is { } p && guard < 200; guard++)
        {
            if (p is ChoiceRequest { KeyboardEnterIgnored: true })
                Key(r, Keys.N);
            else
                Press(r, p is ChoiceRequest { CancelIndex: not null } or ListRequest { CancelAction: not null }
                    ? GameAction.Cancel : GameAction.Confirm);
        }
        Assert.Null(r.Runner.Pending);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    [Fact]
    public void Monsters_loaded_before_a_fight_stay_off_the_party_list()
    {
        // LOADMON appends to the party list (DS:5D96), but DOSBox-X s29 (a vampire loaded by a step's script) shows
        // only the six members on the area screen through the battle line: the list is drawn only at fixed points.
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        byte[] Rows()
        {
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            return f.Pixels.Skip(4 * 8 * 320).Take(8 * 8 * 320).ToArray();
        }
        var before = Rows();
        r.Game.Encounter.LoadMonster(2, 2, 2);
        Assert.Equal(8, r.Game.Memory.Party!.Members.Count);
        Assert.Equal(before, Rows());
    }

    [Fact]
    public void Home_and_end_on_the_command_line_pick_the_member_and_redraw_the_position_line()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var party = r.Game.Memory.Party!;
        int before = party.CurrentIndex;
        r.Player.PositionLineErased = true;                 // as a DAMAGE line leaves it
        Press(r, GameAction.NextMember);                    // 0x173BF: the pick, then 0x173D9 draws the line
        Assert.Equal((before + 1) % party.Members.Count, party.CurrentIndex);
        Assert.False(r.Player.PositionLineErased);
        Assert.Null(r.Runner.Pending);
        Press(r, GameAction.PreviousMember);
        Assert.Equal(before, party.CurrentIndex);
    }

    [Fact]
    public void Typing_keeps_visible_exploration_commands_until_a_prompt_replaces_them()
    {
        if (Dir() is null)
            return;
        var r = Start();
        using var runner = r.Runner;
        byte[] CommandRow()
        {
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            return f.Pixels.Skip(192 * 320).ToArray();
        }

        var commands = CommandRow();
        Assert.Contains(commands, pixel => pixel != 0);
        r.Player.TypesText = true;
        r.Game.Memory.TypesText = true;
        r.Game.Memory.GameSpeed = 2;
        runner.Start(() =>
        {
            r.Player.Print("AB", clearFirst: true);
            runner.Ask(new KeyRequest("Continue"));
            r.Player.Print("C", clearFirst: true);
        });

        for (int i = 0; i < 2; i++)
        {
            Assert.IsType<DelayRequest>(runner.Pending);
            Assert.Equal(commands, CommandRow());
            runner.Answer(null);
        }
        Assert.IsType<KeyRequest>(runner.Pending);
        Assert.NotEqual(commands, CommandRow());
        runner.Answer(null);
        Assert.IsType<DelayRequest>(runner.Pending);
        Assert.All(CommandRow(), pixel => Assert.Equal(0, pixel));
        runner.Answer(null);
        Assert.Equal(commands, CommandRow());
    }

    private static byte[] ViewBox(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var box = new byte[88 * 88];
        for (int y = 0; y < 88; y++)
            for (int x = 0; x < 88; x++)
                box[y * 88 + x] = f[24 + x, 24 + y];
        return box;
    }

    [Fact]
    public void A_picture_on_another_disk_leaves_the_picture_shown_until_the_view_is_redrawn()
    {
        // DOSBox-X s24 (save A, disk 2): PICTURE 2, then PICTURE 3 (only PIC4 holds it) keeps picture 2 in the window;
        // the step's redraw then shows the view. With nothing shown before, the view stays.
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        Assert.Equal(2, r.Game.Memory.Disk);
        var view = ViewBox(r);
        r.Player.ShowPicture(3, null);
        Assert.Equal(view, ViewBox(r));
        r.Player.ShowPicture(2, null);
        var picture = ViewBox(r);
        Assert.NotEqual(view, picture);
        r.Player.ShowPicture(3, null);
        Assert.Equal(picture, ViewBox(r));
        r.Game.Loop.Redraw();
        Assert.Equal(view, ViewBox(r));
    }

    [Fact]
    public void The_view_redraw_takes_a_picture_off_the_window()
    {
        // The step's redraw 0x2DCF4 draws the view over a picture still up (DOSBox-X s24: PICTURE 2 stays until the step).
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var view = ViewBox(r);
        r.Player.ShowPicture(2, null);
        Assert.NotEqual(view, ViewBox(r));
        r.Game.Loop.Redraw();
        Assert.Equal(view, ViewBox(r));
    }

    [Fact]
    public void The_close_up_animates_only_after_the_steps_pass()
    {
        // DS:84E1 is set by the step's pass before vector 1 (0x4223), not before vector 0: after a load a PICTURE in the
        // first step's vector 0 stays on its first frame (DOSBox-X s24).
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        Assert.False(r.Player.PictureArmed);
        Press(r, GameAction.Forward);
        Settle(r);
        Assert.True(r.Player.PictureArmed);
    }

    private static void Pad(Rig r, Buttons b)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Update(default, new GamePadState(default, default, new GamePadButtons(b), default), default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
    }

    [Fact]
    public void The_pads_shoulders_stay_look_and_encamp_and_the_triggers_pick_the_member()
    {
        // The shoulders are bound to Look / Encamp and to the member keys; on the command line the commands win.
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var party = r.Game.Memory.Party!;
        int before = party.CurrentIndex;
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Pad(r, Buttons.RightShoulder);                      // Look: 10 minutes, no pick
        Settle(r);
        Assert.Equal(before, party.CurrentIndex);
        Assert.True(GameClock.TotalMinutes(r.Game.Memory) > t0);
        Pad(r, Buttons.RightTrigger);                       // the next member
        Assert.Equal((before + 1) % party.Members.Count, party.CurrentIndex);
        Pad(r, Buttons.LeftTrigger);
        Assert.Equal(before, party.CurrentIndex);
        Pad(r, Buttons.LeftShoulder);                       // Encamp: the camp menu, no pick
        Assert.NotNull(r.Runner.Pending);
        Assert.Equal(before, party.CurrentIndex);
        Settle(r);
    }

    [Fact]
    public void Walks_to_the_door_and_the_door_menu_waits_for_the_player()
    {
        if (Dir() is null)
            return;
        var r = Start();
        // Save A at 15,4 W; the route of the door captures to 10,2 facing N.
        GameAction[] route =
        [
            GameAction.Forward, GameAction.TurnLeft, GameAction.Forward, GameAction.Forward, GameAction.Forward,
            GameAction.TurnRight, GameAction.Forward, GameAction.TurnRight, GameAction.Forward, GameAction.Forward,
            GameAction.Forward, GameAction.Forward, GameAction.Forward, GameAction.TurnLeft, GameAction.Forward,
            GameAction.Forward, GameAction.Forward, GameAction.TurnRight,
        ];
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        int steps = 0;
        foreach (var a in route)
        {
            Press(r, a);
            // An event on the way (text, menus) may wait for the player.
            Settle(r);
            if (a == GameAction.Forward)
                steps++;
        }
        var p = r.Game.Memory.Position;
        Assert.Equal((10, 2, Facing.North), (p.X, p.Y, (int)p.Facing));
        Assert.Equal(t0 + steps, GameClock.TotalMinutes(r.Game.Memory));

        Press(r, GameAction.Forward);
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(["BASH", "PICK", "EXIT"], menu.Items);
        Press(r, GameAction.Cancel);                        // Exit: no try, the step is refused, vector 1 runs
        Settle(r);
        Assert.Equal((10, 2), (r.Game.Memory.Position.X, r.Game.Memory.Position.Y));
        r.Runner.Dispose();
    }

    [Fact]
    public void Search_and_look_take_the_original_time()
    {
        if (Dir() is null)
            return;
        var r = Start();
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.Search);
        Assert.True(r.Game.Loop.SearchOn);
        Press(r, GameAction.Forward);
        Settle(r);
        Assert.Equal(t0 + 10, GameClock.TotalMinutes(r.Game.Memory));
        Press(r, GameAction.Look);
        Settle(r);
        Assert.Equal(t0 + 20, GameClock.TotalMinutes(r.Game.Memory));
        r.Runner.Dispose();
    }

    [Fact]
    public void Encamp_opens_the_programs_camp_menu_and_view()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Press(r, GameAction.Encamp);
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(6, menu.Items.Count);                 // the program's camp line (DS:0508 in v1.3)
        Assert.NotNull(menu.MenuPrompt);
        Assert.True(r.Player.Camping);
        Assert.Contains(" CAMPING", ExploreScreen.StatusLine(r.Game, r.Player.Camping));
        Key(r, Keys.V);                                    // View: the character page
        var page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.Equal(r.Game.Memory.Party!.Current!.Name.ToUpperInvariant(), page.Lines[0].Text);
        Press(r, GameAction.NextMember);                   // the next member's page
        page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.Equal(r.Game.Memory.Party!.Members[1].Name.ToUpperInvariant(), page.Lines[0].Text);
        Press(r, GameAction.Cancel);                       // leave the view
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Press(r, GameAction.Cancel);                       // Exit the camp
        Settle(r);
        Assert.False(r.Player.Camping);
        r.Runner.Dispose();
    }

    [Fact]
    public void Text_entry_works_with_keys_and_with_the_grid()
    {
        if (Dir() is null)
            return;
        var r = Start();
        object? answer = null;
        r.Runner.Start(() => answer = r.Runner.Ask(new TextRequest("", false, 10)));
        Key(r, Keys.H);
        Key(r, Keys.I);
        Key(r, Keys.Back);
        Press(r, GameAction.MenuRight);                    // grid: A → B
        Press(r, GameAction.Confirm);                      // adds B
        Key(r, Keys.Enter);
        Assert.Equal("HB", answer);
        r.Runner.Dispose();
    }

    [Fact]
    public void Clicking_a_menu_item_chooses_it()
    {
        if (Dir() is null)
            return;
        var r = Start();
        object? answer = null;
        r.Runner.Start(() => answer = r.Runner.Ask(new ChoiceRequest(null, ["YES", "NO"], false)));
        // "YES NO" on the bottom row: NO starts at column 4.
        var click = r.Screen.MapClick(new ScreenClick(4 * 8 + 2, 24 * 8 + 3, false));
        Assert.Equal(GameAction.Confirm, click);
        Press(r, GameAction.Confirm);
        Assert.Equal(1, answer);
        _ = new Point(0, 0);
        r.Runner.Dispose();
    }

    /// <summary>
    /// V and C on the command line (0x17054 → 0x21AED / 0x17CD2, P + D s41) by key, pad and mouse: no time passes and no
    /// vector runs; View's exit and a Cast that showed a list draw START 0x3379 (the CLEARBOX box ring back, the text
    /// box blank); a Cast with nothing memorised (Thrender, a fighter) draws nothing, so the ring stays blank.
    /// </summary>
    [Fact]
    public void View_and_cast_on_the_command_line_by_key_pad_and_mouse()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var party = r.Game.Memory.Party!;
        party.CurrentIndex = 0;
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        ushort pc = r.Game.Vm.Pc;
        r.Player.ViewBoxBlank = true;                       // CLEARBOX, then a step's redraw (the view alone)
        Key(r, Keys.V);
        var page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.Equal(party.Members[0].Name.ToUpperInvariant(), page.Lines[0].Text);
        Press(r, GameAction.Cancel);
        Assert.Null(r.Runner.Pending);
        Assert.False(r.Player.ViewBoxBlank);                // 0x21D4F → START 0x3379 case 4
        Assert.Equal(t0, GameClock.TotalMinutes(r.Game.Memory));
        Assert.Equal(pc, r.Game.Vm.Pc);
        Assert.Equal(2, r.Screen.MenuHighlight);             // the cell moved to VIEW (3rd word)

        r.Player.ViewBoxBlank = true;
        r.Game.Memory.GameSpeed = 0;                       // no pause after the message (speed 0 waits 0 ms)
        Key(r, Keys.C);                                    // a fighter: the no-spells message only
        Settle(r);
        Assert.True(r.Player.ViewBoxBlank);
        Assert.Equal(1, r.Screen.MenuHighlight);

        Pad(r, Buttons.Start);                             // the pad's View
        Assert.IsType<ListRequest>(r.Runner.Pending);
        Pad(r, Buttons.B);
        Assert.Null(r.Runner.Pending);

        party.CurrentIndex = 3;                            // Brother Sean has spells memorised
        r.Player.ViewBoxBlank = true;
        Pad(r, Buttons.RightStick);                        // the pad's Cast: the spell list
        Assert.IsType<ListRequest>(r.Runner.Pending);
        Pad(r, Buttons.B);
        Settle(r);
        Assert.False(r.Player.ViewBoxBlank);               // 0x17D96 → START 0x3379

        // The mouse: VIEW is columns 10–13 of row 24 on the 3D line (AREA CAST VIEW …).
        Assert.Equal(GameAction.View, r.Screen.MapClick(new ScreenClick(11 * 8, 24 * 8 + 3, false)));
        Assert.Equal(GameAction.Cast, r.Screen.MapClick(new ScreenClick(6 * 8, 24 * 8 + 3, false)));
        Assert.Equal(t0, GameClock.TotalMinutes(r.Game.Memory));
        r.Runner.Dispose();
    }

    /// <summary>
    /// START 0x3379 with DS:49F3 = 2 (a training hall's Load of a camp save, D s39 / s41): the camp picture loaded but
    /// the window blank until a menu animates it; the first menu after the Load animates it ($4AFF from the save) and
    /// clears the cell; "CAMPING" stays on the position line until DS:49F3 is written again (View's exit draws case 2
    /// again, the window blank).
    /// </summary>
    [Fact]
    public void A_camp_state_left_by_a_hall_load_draws_the_camp_screen()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var m = r.Game.Memory;
        m.InterfaceState = 2;
        m.MenuAnimates = true;                              // the save's $4AFF (3 in save A)
        m.PictureShown = true;
        m.PictureArmed = false;                             // as after a start-up Load (D s24 / s41)
        r.Runner.Start(() =>
        {
            r.Player.DrawInterface();
            r.Player.HorizontalMenu(["WAIT"]);
        });
        Assert.True(r.Player.PictureUndrawn);
        Assert.Equal(ExploreScreen.CampPicture, r.Player.ViewPicture!.Id);
        Assert.True(r.Player.CampingLine);
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.True(menu.Animates);                         // $4AFF ≠ 0 although DS:84E1 is 0 after the start-up load
        Assert.True(menu.EveryPass);                        // and a frame on every pass (0x2CDD6; user s42)
        r.Screen.Update(r.Input);
        Assert.False(r.Player.PictureUndrawn);
        Press(r, GameAction.Confirm);
        Assert.False(m.MenuAnimates);
        Key(r, Keys.V);
        Press(r, GameAction.Cancel);
        Assert.True(r.Player.PictureUndrawn);               // case 2 again: the window blank, no menu animates it
        Assert.Contains(" CAMPING", ExploreScreen.StatusLine(r.Game, r.Player.CampingLine));
        Press(r, GameAction.TurnLeft);
        Assert.True(r.Player.CampingLine);                  // a turn writes no DS:49F3
        Assert.False(r.Player.PictureUndrawn);              // but draws the view in the blank box (0x2EEF4; DOSBox-X s42)
        Assert.Null(r.Player.ViewPicture);
        r.Runner.Dispose();
    }

    /// <summary>
    /// A turn (0x2ED72 + 0x2EEF4) and the area map toggle (0x2EEF4) draw the view into the window over a picture, as a
    /// redraw does; the picture flags DS:84E0 / 84E1 are not touched (DOSBox-X s42: the view after Left, h5).
    /// </summary>
    [Fact]
    public void A_turn_or_the_area_map_draws_over_a_picture()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        r.Runner.Start(() => r.Player.ShowPicture(2, null));
        Assert.Equal(ViewPictureKind.Picture, r.Player.ViewPicture?.Kind);
        bool shown = r.Game.Memory.PictureShown;
        Press(r, GameAction.TurnLeft);
        Settle(r);
        Assert.Null(r.Player.ViewPicture);
        Assert.Equal(shown, r.Game.Memory.PictureShown);
        r.Runner.Start(() => r.Player.ShowPicture(2, null));
        Assert.NotNull(r.Player.ViewPicture);
        Press(r, GameAction.AreaMap);
        Settle(r);
        Assert.True(r.Game.Loop.ShowAreaMap);
        Assert.Null(r.Player.ViewPicture);
        r.Runner.Dispose();
    }

    /// <summary>
    /// A party menu Load's vector 4 draws over the party menu's page until START 0x3379 (DOSBox-X s42, PRINT, PICTURE 2,
    /// HMENU in vector 4: the page with the picture at cell (3, 3) and no view box, the text at row 17, WAIT on row 24;
    /// 0 differing EGA pixels against the Client).
    /// </summary>
    [Fact]
    public void A_party_menu_loads_vector_4_draws_over_the_menu_page()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Settle(r);
        var page = new byte[320 * 8 * 24];
        Array.Fill(page, (byte)5);
        r.Screen.PartyMenuPage = page;
        r.Player.PartyMenuUnder = true;
        r.Runner.Start(() =>
        {
            r.Player.ShowPicture(2, null);
            r.Player.HorizontalMenu(["WAIT"]);
        });
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(5, f[0, 0]);                           // the page, not the explore frame
        Assert.Equal(5, f[20, 20]);                         // no view box around the picture
        Assert.Equal(5, f[200, 100]);                       // no party list or position line
        Assert.NotEqual(5, f[60, 60]);                      // the picture at (24, 24)
        Press(r, GameAction.Confirm);
        r.Player.InterfaceDrawn(4);                         // START 0x3379 (the pair, or after the vector)
        Assert.False(r.Player.PartyMenuUnder);
        r.Runner.Dispose();
    }
}
