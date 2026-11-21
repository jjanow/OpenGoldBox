using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The training hall (PROGRAM 0) and the end game (PROGRAM 8) through the explore screen and the action layer, on save
/// A (skipped without GOLDBOX_POR_DIR): keyboard, mouse and pad.
/// </summary>
public class HallPlayTests
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

    private static Rig Start()
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
    }

    private static void Frame(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
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

    private static void Settle(Rig r)
    {
        for (int guard = 0; (r.Runner.Pending is null && r.Runner.Busy || r.Runner.Pending is DelayRequest) && guard < 4000; guard++)
        {
            Thread.Sleep(2);
            Frame(r);
        }
    }

    private static void SetXp(Core.Ecl.EclCharacter c, int xp)
    {
        for (int i = 0; i < 4; i++)
            c[0xAC + i] = (byte)(xp >> (8 * i));
    }

    [Fact]
    public void Hall_trains_a_thief_with_keys_and_leaves_with_the_pad()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        var thief = party.Members[5];
        SetXp(thief, 3000);
        thief.SetWord(0x90, 1000);                       // 1000 platinum
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 116);
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.Equal(10, menu.Entries.Count);            // all but Load (a party is there)
        Assert.NotNull(r.Player.Hall);
        Press(r, GameAction.PreviousMember);             // Home: the head wraps to the last member
        Settle(r);
        Assert.Same(thief, party.Current);
        Key(r, Keys.T);
        Settle(r);
        var ask = Assert.IsType<ChoiceRequest>(r.Runner.Pending);   // "will become", the question
        Assert.NotNull(r.Player.Hall!.Page);
        Key(r, Keys.Y);
        Settle(r);
        Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, thief[0x9C]);                    // thief level 2
        Assert.Equal(800, thief.Word(0x90));             // 200 platinum paid
        Assert.Equal(2500, thief[0xAC] | thief[0xAD] << 8);   // capped at the level-3 threshold − 1
        // the pad: Down to Begin (entry 8 of 10), Confirm
        for (int i = 0; i < 8; i++)
            Press(r, GameAction.MenuDown);
        Press(r, GameAction.Confirm);
        Settle(r);
        Assert.False(r.Runner.Busy);
        Assert.Null(r.Player.Hall);
        _ = ask;
    }

    [Fact]
    public void Hall_refuses_with_a_message_and_a_click_picks_an_entry()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 116);  // thieves only: the dwarf fighter is refused
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        int train = menu.Entries.ToList().FindIndex(e => e.Key == 'T');
        Assert.Null(r.Screen.MapClick(new ScreenClick(HallView.MenuColumn * 8 + 4, (HallView.MenuRow + train) * 8 + 4, false)));
        Frame(r);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.NotNull(r.Player.Hall!.Message);
        Settle(r);
        Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Key(r, Keys.B);
        Settle(r);
        Assert.False(r.Runner.Busy);
    }

    [Fact]
    public void End_game_plays_its_steps_and_clears_the_scene()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.GameSpeed = 9;   // 2 loops per animation
        r.Input.SkipPauses = true;     // QoL: Enter ends a pause early (the original's cannot be cut short)
        r.Runner.Start(() => r.Player.EndGameSequence());
        bool sawScene = false, sawSpirit = false;
        int frames = 0;
        for (int guard = 0; r.Runner.Busy && guard < 20000; guard++)
        {
            if (r.Player.Scene is { } s)
            {
                sawScene = true;
                sawSpirit |= s.Layers.Any(l => l.Block == 7);
            }
            Thread.Sleep(1);
            if (r.Runner.Pending is DelayRequest)
            {
                frames++;
                Press(r, GameAction.Confirm);   // Enter ends the pause (QoL SkipPauses)
            }
        }
        Assert.False(r.Runner.Busy);
        Assert.True(sawScene && sawSpirit);
        Assert.Equal(2 * 4 + 2 * 6 + 6 + 3, frames);   // dragon 2 × 4 frames, spirit 2 × 6, block 8 once, three pauses
        Assert.Null(r.Player.Scene);
    }

    [Fact]
    public void Amiga_end_game_plays_its_three_blocks_once_each_block_8_backwards()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.GameSpeed = 9;   // no effect on the Amiga sequence
        r.Input.SkipPauses = true;
        var counts = new Dictionary<int, int> { [2] = 4, [7] = 4, [8] = 6 };
        var pic = Data.Dax.DaxPicture.Create(88, 88, 0, 0, [new byte[88 * 88]]);
        r.Player.EndArt = (_, block) => counts.TryGetValue(block, out int n)
            ? new PorArtImage("amiga", [.. Enumerable.Repeat(pic, n)], new byte[48]) : null;
        r.Runner.Start(() => r.Player.EndGameSequence());
        var shown = new List<(int Block, int Frame)>();
        for (int guard = 0; r.Runner.Busy && guard < 20000; guard++)
        {
            Thread.Sleep(1);
            if (r.Runner.Pending is DelayRequest d && r.Player.Scene is { } s)
            {
                Assert.True(s.Amiga);
                Assert.Equal(EndGame.AmigaFrameTime, d.Duration);
                var l = Assert.Single(s.Layers);
                Assert.Equal((EndGame.AmigaColumn, EndGame.AmigaRow), (l.Column, l.Row));
                shown.Add((l.Block, l.Frame));
                Press(r, GameAction.Confirm);
            }
        }
        Assert.False(r.Runner.Busy);
        Assert.Equal([(2, 0), (2, 1), (2, 2), (2, 3), (7, 0), (7, 1), (7, 2), (7, 3),
            (8, 5), (8, 4), (8, 3), (8, 2), (8, 1), (8, 0)], shown);
        Assert.Null(r.Player.Scene);

        // Off (the one switch), or a block missing from the Amiga art: the DOS sequence.
        r.Player.AmigaEndGame = false;
        Assert.False(HallScreens.AmigaEndGame(r.Player, 5));
        r.Player.AmigaEndGame = true;
        Assert.True(HallScreens.AmigaEndGame(r.Player, 5));
        counts.Remove(8);
        Assert.False(HallScreens.AmigaEndGame(r.Player, 5));
    }

    /// <summary>The program's word for the hall's J, read from the player's files (never written in the tests).</summary>
    private static string Word(Rig r) => r.Player.Resources.HallTexts.Program!.Text("Hall.Password")!;

    private static int Entries(Rig r) => Assert.IsType<HallMenuRequest>(r.Runner.Pending).Entries.Count;

    /// <summary>
    /// J (0x19E2C) with the program's first command-line word equal to the word in the files: the cheat flag shows
    /// Train in a hall that teaches nothing ($6DA8 = 0), after the message with its pause. Keyboard, pad and mouse.
    /// </summary>
    [Theory]
    [InlineData(InputDevice.Keyboard)]
    [InlineData(InputDevice.Gamepad)]
    [InlineData(InputDevice.Mouse)]
    public void Hall_J_with_the_programs_word_turns_the_cheat_on(InputDevice device)
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Player.ProgramWord = Word(r) + " MORE";       // ParamStr(1): the first word of the command tail
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 0);
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        Assert.Equal(9, Entries(r));                     // no Train, no Load
        var menu = (HallMenuRequest)r.Runner.Pending!;
        switch (device)
        {
            case InputDevice.Keyboard:
                Key(r, Keys.J);
                break;
            case InputDevice.Gamepad:
                Press(r, GameAction.HallJ);
                break;
            default:
                Assert.Null(r.Screen.MapClick(new ScreenClick(4, HallView.BottomRow * 8 + 4, false)));   // the prompt
                Frame(r);
                break;
        }
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal((r.Player.Resources.HallTexts["Hall.Understood"].ToUpperInvariant(), (byte)10), r.Player.Hall!.Message);
        Settle(r);
        Assert.Equal(10, Entries(r));                    // Train is offered
        Assert.Contains(((HallMenuRequest)r.Runner.Pending!).Entries, e => e.Key == 'T');
        Assert.NotSame(menu, r.Runner.Pending);
        Key(r, Keys.B);
        Settle(r);
        Assert.False(r.Runner.Busy);
    }

    /// <summary>J with no word, another word, the word in another case or as the second word: nothing happens (no
    /// message, no line read, no Train).</summary>
    [Fact]
    public void Hall_J_with_another_word_does_nothing()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 0);
        foreach (var word in new[] { null, "", Word(r).ToLowerInvariant(), Word(r) + "X", "X " + Word(r) })
        {
            r.Player.ProgramWord = word;
            r.Runner.Start(() => r.Player.TrainingHall());
            Settle(r);
            Key(r, Keys.J);
            Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.Null(r.Player.Hall!.Message);
            Assert.Equal(9, Entries(r));
            Key(r, Keys.B);
            Settle(r);
            Assert.False(r.Runner.Busy);
        }
    }

    /// <summary>Lit pixels of text row <paramref name="row"/> (8 pixel lines) between columns 1 and 38.</summary>
    private static int LitPixels(IndexedFrame f, int row)
    {
        int n = 0;
        for (int y = row * 8; y < row * 8 + 8; y++)
            for (int x = 8; x < 39 * 8; x++)
                if (f[x, y] != 0)
                    n++;
        return n;
    }

    /// <summary>
    /// The hall over an empty party (DOSBox-X s42: Remove × 6, then Load): the four entries alone, no NAME / AC / HP
    /// heading, and the entries stay while Load's slot question is on row 24 (the menu routine draws only that row).
    /// </summary>
    [Fact]
    public void The_hall_over_an_empty_party_keeps_its_entries_under_the_load_question()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.Party!.Members.Clear();
        r.Game.Memory.Party.CurrentIndex = -1;
        r.Player.HallHooks.Load = () => r.Runner.Ask(new ChoiceRequest(null, ["A", "J"], false, null) { MenuPrompt = "LOAD WHICH GAME:" });
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.Equal(4, menu.Entries.Count);              // Create, Add, Load, Exit
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(0, LitPixels(f, 2));                 // no heading
        int entries = Enumerable.Range(HallView.MenuRow, 4).Sum(row => LitPixels(f, row));
        Assert.True(entries > 0);
        Key(r, Keys.L);
        Settle(r);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        var g = new IndexedFrame(320, 200);
        r.Screen.Draw(g, r.Input);
        Assert.Equal(entries, Enumerable.Range(HallView.MenuRow, 4).Sum(row => LitPixels(g, row)));
        Assert.Equal(0, LitPixels(g, 2));
        r.Runner.Dispose();
    }

    /// <summary>
    /// A hall Save writes the wall-set cells, so VM $4AFF is not 0 (0x1FE70), but the hall's own menu (the menu routine
    /// 0x2CC8C) clears it at its end (0x2CFAD): after Begin the step's HMENU over PICTURE 2 does not animate (DOSBox-X
    /// s42: PICTURE 2; PROGRAM 0; PRINT; HMENU from a start-up Load, Save E in the hall, Begin: the view stayed still).
    /// </summary>
    [Fact]
    public void The_halls_menu_clears_the_menu_animate_flag_a_save_set()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Runner.Answered = r.Player.MenuEnded; // as Program.cs
        var folder = Directory.CreateTempSubdirectory("hall-save-");
        try
        {
            r.Player.SaveFolder = folder.FullName;
            var m = r.Game.Memory;
            m.PictureArmed = false;                       // as after a start-up Load: HMENU's own flag stays 0
            r.Runner.Start(() =>
            {
                r.Player.ShowPicture(2, null);
                r.Player.TrainingHall();
                r.Player.HorizontalMenu(["WAIT"]);
            });
            Settle(r);
            Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Key(r, Keys.S);
            Settle(r);
            var slots = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            int e = Enumerable.Range(0, slots.Items.Count).First(k => slots.HotkeyOf(k) == 'E');
            r.Runner.Answer(e);
            Settle(r);
            Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.True(m.MenuAnimates);                  // the save wrote the cells
            Key(r, Keys.B);
            Settle(r);
            var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(m.MenuAnimates);                 // the hall's menu ended (Begin): before s42 still set here
            Assert.False(menu.Animates);
            r.Runner.Dispose();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>Pixels of cells (3, 3)–(13, 13) (the picture's 88 × 88) on text rows fromRow to toRow that differ.</summary>
    private static int PictureDiff(IndexedFrame a, IndexedFrame b, int fromRow = 3, int toRow = 13)
    {
        int n = 0;
        for (int y = fromRow * 8; y < toRow * 8 + 8; y++)
            for (int x = 24; x < 112; x++)
                if (a[x, y] != b[x, y])
                    n++;
        return n;
    }

    private static IndexedFrame Shot(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    /// <summary>
    /// The menu routine draws the loaded frame table at cell (3, 3) on every pass while $4AFF ≠ 0, whatever page is up
    /// (0x2CD5D–0x2CDD6). DOSBox-X (s43, a step's PICTURE 2; PROGRAM 0 from a start-up Load of save A): after Save E the
    /// hall's next menu drew PICTURE 2's frames over the hall page; the slot question after it kept the last frame;
    /// Escape, and the loop drew its page again (no picture: the menu had cleared $4AFF). A second Save in the same run
    /// after another PICTURE 2 drew nothing only because the hall's disk 3 has no PIC 2 (PICTURE 29 showed both times).
    /// </summary>
    [Fact]
    public void A_hall_menu_after_a_save_draws_the_loaded_picture_over_the_page()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Runner.Answered = r.Player.MenuEnded; // as Program.cs
        r.Runner.Asking = r.Player.MenuAsked;
        var folder = Directory.CreateTempSubdirectory("hall-save-");
        try
        {
            r.Player.SaveFolder = folder.FullName;
            r.Game.Memory.MenuAnimates = false;           // the command line ran since the Load
            r.Runner.Start(() =>
            {
                r.Player.ShowPicture(2, null);
                r.Player.TrainingHall();
            });
            Settle(r);
            var first = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.False(first.EveryPass);
            var page = Shot(r);
            Key(r, Keys.S);
            Settle(r);
            var slots = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            r.Runner.Answer(Enumerable.Range(0, slots.Items.Count).First(k => slots.HotkeyOf(k) == 'E'));
            Settle(r);
            var after = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.True(after.EveryPass);                 // the save set $4AFF
            Assert.Equal(HallPicture.Whole, r.Player.Hall!.PictureOver);
            Frame(r);
            var a = Shot(r);
            Frame(r);
            var b = Shot(r);
            Assert.True(PictureDiff(page, a) > 2000);     // the picture over the list and the entries
            Assert.True(PictureDiff(a, b) > 0);           // a frame on every pass (PIC 2 has two)
            Assert.Equal(0, PictureDiff(page, b, 14, 22) + PictureDiff(page, b, 0, 2)); // nothing else changed
            Key(r, Keys.S);                               // the slot question: the menu cleared $4AFF, the frame stays
            Settle(r);
            var again = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.False(again.EveryPass);
            Assert.True(PictureDiff(page, Shot(r)) > 2000);
            Press(r, GameAction.Cancel);
            Settle(r);
            var redrawn = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.False(redrawn.EveryPass);
            Assert.Equal(HallPicture.None, r.Player.Hall!.PictureOver);
            Assert.Equal(0, PictureDiff(page, Shot(r)));  // the loop drew its page again
            r.Runner.Dispose();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A member pick (Home / End: 0x19C9E then the party list START 0x1307, 0x19CAB) does not draw the hall's page again
    /// (0x19E69 skips the redraw flag): the list's rows 4 to 4 + n are blanked from column 1 to 38 and drawn over the
    /// picture, the rest of it stays (DOSBox-X s43: End after a Save left rows 3 and 11–13 of PICTURE 2).
    /// </summary>
    [Fact]
    public void A_member_pick_draws_the_party_list_over_the_picture_left_on_the_hall_page()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Runner.Answered = r.Player.MenuEnded;
        r.Runner.Asking = r.Player.MenuAsked;
        r.Game.Memory.MenuAnimates = true;                // as after a Save or a Load
        r.Runner.Start(() =>
        {
            r.Player.ShowPicture(2, null);
            r.Player.TrainingHall();
        });
        Settle(r);
        Assert.True(Assert.IsType<HallMenuRequest>(r.Runner.Pending).EveryPass);
        Press(r, GameAction.NextMember);
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.False(menu.EveryPass);
        var hall = r.Player.Hall!;
        Assert.Equal(HallPicture.UnderList, hall.PictureOver);
        var over = Shot(r);
        hall.PictureOver = HallPicture.None;
        var page = Shot(r);
        int members = r.Game.Memory.Party!.Members.Count;
        Assert.Equal(0, PictureDiff(page, over, 4, 4 + members)); // the list's rows as on the page
        Assert.True(PictureDiff(page, over, 3, 3) > 0);            // the picture's top row stays
        Assert.True(PictureDiff(page, over, 5 + members, 13) > 0); // and its rows under the list
        r.Runner.Dispose();
    }

    /// <summary>
    /// The key buffer (GetKey START 0x89E9 keeps the last waiting key): keys pressed during the refusal message's pause
    /// do not act then; the last one is read by the menu afterwards (B: Begin leaves the hall).
    /// </summary>
    [Fact]
    public void Keys_pressed_during_a_hall_message_act_after_it()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.GameSpeed = 2;
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 116);  // thieves only: the dwarf fighter is refused
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        Key(r, Keys.T);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Key(r, Keys.V);                                  // View … then Begin: the last key wins
        Key(r, Keys.B);
        Assert.IsType<DelayRequest>(r.Runner.Pending);   // the pause goes on (Enter would not end it either)
        Assert.True(r.Input.HasBuffered);
        Settle(r);
        Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Frame(r);                                        // the menu reads the waiting key
        Settle(r);
        Assert.False(r.Runner.Busy);                     // B was read by the menu: Begin
        Assert.Null(r.Player.Hall);
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

    /// <summary>The hall's roster entries are the party menu's (PartyMenuFlow on the game's party): Remove writes the
    /// character to the roster and lowers $6E3E, Add brings it back over the overlay page and raises it again, and Load
    /// ends the game for the screen to load the save.</summary>
    [Fact]
    public void Hall_roster_entries_run_the_party_menu_on_the_games_party()
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
        var folder = Directory.CreateTempSubdirectory("hall-roster-");
        try
        {
            var flow = new PartyMenuFlow(runner.Ask, new PartyMenuTexts(program, player.Resources), program.CreationTables()!,
                PorFight.Kit(dir)!.Items, new PorRoster(folder.FullName), 1)
            {
                SaveSlots = () => ['A'],
                LoadGame = (_, _, _) => throw new InvalidOperationException("the hall does not load itself"),
            };
            flow.Party = game.Memory.Party!;
            flow.Random = game.Rng;
            player.HallHooks.Remove = flow.Remove;
            player.HallHooks.Add = flow.Add;
            player.HallHooks.Load = () =>
            {
                if (flow.PickSlot() is { } slot)
                    throw new HallLoadException(slot);
            };
            player.HallHooks.Notice = flow.TakeNotice;
            player.HallHooks.Modify = flow.Modify;
            player.HallHooks.View = flow.View;
            var host = new Stack();
            char? loaded = null;
            var input = new ActionInput(InputBindings.Defaults());
            var screen = new ExploreScreen(game, player, runner, host)
            {
                PartyPages = () => new PartyMenuScreen(flow, runner, host, game.Art.Font, player.Resources,
                    new ClientArt(dir, art, new ScreenPalette(), PorArtChoice.Dos), _ => null!) { Overlay = true },
                LoadSave = slot =>
                {
                    loaded = slot;
                    return new NoScreen();
                },
            };
            var r = new Rig(screen, game, runner, input, player);
            var party = game.Memory.Party!;
            int size = party.Members.Count;
            Assert.Equal(size, game.Memory.Read(Core.Ecl.EclPartyRules.PartySizeCell));
            string name = party.Current!.Name;
            runner.Start(() => player.TrainingHall());
            Settle(r);
            Key(r, Keys.R);                                  // Remove: to the roster
            Settle(r);
            Assert.IsType<HallMenuRequest>(runner.Pending);
            Assert.Equal(size - 1, party.Members.Count);
            Assert.Equal(size - 1, game.Memory.Read(Core.Ecl.EclPartyRules.PartySizeCell));
            Assert.Contains(name, new PorRoster(folder.FullName).Entries(null));
            Key(r, Keys.A);                                  // Add: the roster page goes over the hall
            Settle(r);
            var pick = Assert.IsType<PickRequest>(runner.Pending);
            Frame(r);
            var overlay = Assert.IsType<PartyMenuScreen>(Assert.Single(host.Pushed));
            int row = pick.Rows.ToList().IndexOf(name);
            Assert.True(row >= 0);
            runner.Answer(new ListAnswer(PickRequest.RowChosen, row));
            Settle(r);
            if (runner.Pending is PickRequest again)        // Add ends by itself once six player characters are counted
            {
                runner.Answer(new ListAnswer(again.CancelAction, 0));
                Settle(r);
            }
            Assert.IsType<HallMenuRequest>(runner.Pending);
            overlay.Update(input);                           // the hall's menu is back: the overlay closes
            Assert.Equal(1, host.Pops);
            Assert.Equal(size, party.Members.Count);
            Assert.Equal(name, party.Members[^1].Name);
            Assert.Equal(size, game.Memory.Read(Core.Ecl.EclPartyRules.PartySizeCell));
            Key(r, Keys.M);                                  // Modify: save A's characters have experience: refused
            for (int guard = 0; runner.Pending is not DelayRequest && guard < 2000; guard++)
            {
                Thread.Sleep(2);
                Frame(r);
            }
            Assert.Contains(party.Current!.Name.ToUpperInvariant(), player.Hall!.Message!.Value.Text);
            Settle(r);
            Assert.IsType<HallMenuRequest>(runner.Pending);
            Key(r, Keys.V);                                  // View: the party menu's (0x21AED on the program's page)
            Settle(r);
            var view = Assert.IsType<SheetRequest>(runner.Pending);
            Assert.Same(party.Current, view.Character);
            Assert.Equal(FightItemScreens.ViewEntries(party.Current!, 0).Count, view.Actions.Count);
            Assert.Equal("EXIT", view.Actions[^1]);
            runner.Answer(view.CancelAction);
            Settle(r);
            Assert.IsType<HallMenuRequest>(runner.Pending);
            Key(r, Keys.B);                                  // Begin: the hall ends
            Settle(r);
            Assert.False(runner.Busy);
            // Load is offered with no current character; a picked save ends the game and the screen loads it.
            party.Members.Clear();
            party.CurrentIndex = -1;
            runner.Start(() => player.TrainingHall());
            Settle(r);
            Assert.Contains(Assert.IsType<HallMenuRequest>(runner.Pending).Entries, e => e.Key == 'L');
            Key(r, Keys.L);
            Settle(r);
            Assert.IsType<ChoiceRequest>(runner.Pending);    // A, EXIT
            Key(r, Keys.A);
            Settle(r);
            Assert.False(runner.Busy);
            Assert.Equal('A', loaded);
            Assert.IsType<NoScreen>(host.Pushed[^1]);
        }
        finally
        {
            runner.Dispose();
            folder.Delete(true);
        }
    }

    /// <summary>SAVGAMA.DAT with its script area replaced: vector 4 (the entry after a load) runs PROGRAM 0, the other
    /// vectors an EXIT (as tools/scripts/por_test_saves.py builds its test saves), written as the engine save
    /// <c>b.json</c> in <paramref name="folder"/> (save A's party, interface state 2: the load leaves the menu).</summary>
    internal static string HallEntrySave(PorGameDirectory dir, string folder)
    {
        var d = File.ReadAllBytes(dir.Find("SAVGAMA.DAT")!);
        var area = d.AsSpan(PorSaveGame.OffScript, Core.Ecl.EclLayout.AreaSize);
        area.Clear();
        for (int v = 0; v < 5; v++)
        {
            ushort to = v == 4 ? (ushort)0x9920 : (ushort)0x991F;   // $991F EXIT (0x00), $9920 the code
            area[4 * v] = 1;
            area[4 * v + 1] = 1;
            area[4 * v + 2] = (byte)to;
            area[4 * v + 3] = (byte)(to >> 8);
        }
        area[0x20] = 0x38;                                          // PROGRAM 0
        area[0x21] = 0;
        area[0x22] = 0;
        Assert.Equal(2, d[PorSaveGame.OffInterface]);
        var path = Path.Combine(folder, GoldBox.Data.Module.Por.PorEngineSave.FileName('B'));
        GoldBox.Data.Module.Por.PorEngineSave.Write(path, GoldBox.Data.Module.Por.PorEngineSave.FromFolders(d, "SAVGAMB.DAT",
            GoldBox.Data.Module.Por.PorEngineSave.Identity(dir), dir.LoadBlock, [dir.Root]));
        return path;
    }

    /// <summary>
    /// A load whose vector 4 opens the training hall (a start with --save, a load in the game): the player's hosts read
    /// the game (<c>() => game!.Memory</c>) while vector 4 runs, so the load hands the game over before the entry
    /// (<c>started</c>). Without it the hall's first read of the memory threw (no game yet) or read the game being left.
    /// The hall comes up on the loaded game's party under its explore screen, and Begin ends the entry.
    /// </summary>
    [Fact]
    public void Hall_opened_by_a_loaded_saves_entry_runs_on_the_loaded_game()
    {
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        var who = new PorPlayer(player, player, player, player);
        var folder = Directory.CreateTempSubdirectory("hall-entry-");
        try
        {
            var path = HallEntrySave(dir, folder.FullName);
            var input = new ActionInput(InputBindings.Defaults());
            for (int pass = 0; pass < 2; pass++)            // with no game yet (start-up), then from a running game
            {
                var before = game;
                PorGame? handed = null;
                runner.Start(() => game = PorGame.FromSave(dir, art, path, who, 7, started: g => handed = game = g));
                Assert.Null(runner.Fault);
                Assert.IsType<HallMenuRequest>(runner.Pending);
                Assert.NotNull(handed);
                Assert.NotSame(before, handed);
                Assert.Same(handed, game);
                Assert.Equal(6, handed!.Memory.Party!.Members.Count);
                var r = new Rig(new ExploreScreen(handed, player, runner, new NoHost()), handed, runner, input, player);
                Settle(r);
                Assert.NotNull(player.Hall);
                Key(r, Keys.B);                              // Begin: the hall ends, and so does vector 4
                Settle(r);
                Assert.Null(runner.Fault);
                Assert.False(runner.Busy);
                Assert.Null(player.Hall);
                Assert.Same(handed, game);
                Assert.Equal(6, game!.Memory.Party!.Members.Count);
            }
        }
        finally
        {
            runner.Dispose();
            folder.Delete(true);
        }
    }

    private sealed class NoScreen : IScreen
    {
        public GameAction? MapClick(ScreenClick click) => null;
        public void Update(ActionInput input) { }
        public void Draw(IndexedFrame frame, ActionInput input) { }
    }
}
