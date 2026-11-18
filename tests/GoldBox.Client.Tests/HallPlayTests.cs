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
                LoadGame = (_, _) => throw new InvalidOperationException("the hall does not load itself"),
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

    private sealed class NoScreen : IScreen
    {
        public GameAction? MapClick(ScreenClick click) => null;
        public void Update(ActionInput input) { }
        public void Draw(IndexedFrame frame, ActionInput input) { }
    }
}
