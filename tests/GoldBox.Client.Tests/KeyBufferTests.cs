using GoldBox.Client.Input;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The key buffer (docs/re/view3d.md "Keys while the program is busy"): keys pressed while the program does not read
/// keys wait, and the next read gets the last of them (GetKey START 0x89E9); the QoL SkipPauses drops them instead.
/// </summary>
public class KeyBufferTests
{
    private static readonly TimeSpan T0 = TimeSpan.Zero;

    private static void Frame(ActionInput i, params Keys[] keys) =>
        i.Update(new KeyboardState(keys), default, default, T0, _ => null);

    private static void PadFrame(ActionInput i, Buttons b) =>
        i.Update(default, new GamePadState(default, default, new GamePadButtons(b), default), default, T0, _ => null);

    [Fact]
    public void Keys_during_a_pause_wait_and_the_last_one_is_read()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Frame(i, Keys.T);
        i.ReadingKeys(false);
        Assert.False(i.Pressed(GameAction.Confirm));
        Assert.Null(i.NewInput);                 // nothing acts during the pause
        Frame(i);
        i.ReadingKeys(false);
        Frame(i, Keys.Enter);
        i.ReadingKeys(false);
        Assert.False(i.SkipsPause);              // Enter does not end the original's pause
        Frame(i);
        i.ReadingKeys(false);
        Assert.True(i.HasBuffered);
        Frame(i);
        i.ReadingKeys(true);                     // the next read: the last key only
        Assert.True(i.Pressed(GameAction.Confirm));
        Assert.Equal((InputDevice.Keyboard, "Enter"), i.NewInput);
        Assert.False(i.HasBuffered);
        Frame(i);
        i.ReadingKeys(true);
        Assert.Null(i.NewInput);                 // read once
    }

    [Fact]
    public void A_new_key_at_the_read_is_newer_than_the_waiting_one()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Frame(i, Keys.Escape);
        i.ReadingKeys(false);
        Frame(i, Keys.Escape, Keys.S);
        i.ReadingKeys(true);
        Assert.Equal((InputDevice.Keyboard, "S"), i.NewInput);
        Assert.False(i.Pressed(GameAction.Cancel));
        Assert.False(i.HasBuffered);
    }

    [Fact]
    public void Pad_buttons_wait_like_keys_and_clicks_are_dropped()
    {
        var i = new ActionInput(InputBindings.Defaults());
        PadFrame(i, Buttons.A);
        i.ReadingKeys(false);
        Assert.True(i.HasBuffered);
        i.Update(default, default, default, T0, _ => null);
        i.Inject(GameAction.Cancel);             // a click turned into an action during the pause
        i.ReadingKeys(false);
        Assert.False(i.Pressed(GameAction.Cancel));
        i.Update(default, default, default, T0, _ => null);
        i.ReadingKeys(true);
        Assert.True(i.Pressed(GameAction.Confirm)); // the pad's A, not the click
        Assert.Equal(InputDevice.Gamepad, i.LastDevice);
    }

    [Fact]
    public void Discard_empties_the_buffer()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Frame(i, Keys.Enter);
        i.ReadingKeys(false);
        i.Discard();
        Frame(i);
        i.ReadingKeys(true);
        Assert.False(i.Pressed(GameAction.Confirm));
        Assert.Null(i.NewInput);
    }

    [Fact]
    public void Skip_pauses_ends_a_pause_on_enter_and_keeps_nothing()
    {
        var i = new ActionInput(InputBindings.Defaults()) { SkipPauses = true };
        Frame(i, Keys.Enter);
        i.ReadingKeys(false);
        Assert.True(i.SkipsPause);
        Frame(i);
        i.ReadingKeys(false);
        Frame(i, Keys.T);
        i.ReadingKeys(false);
        Assert.False(i.HasBuffered);
        Frame(i);
        i.ReadingKeys(true);
        Assert.Null(i.NewInput);
    }

    [Fact]
    public void The_AI_key_check_takes_the_waiting_key()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Assert.Equal(0, i.TakeAiKey());          // none waiting
        Frame(i, Keys.Enter);
        i.ReadingKeys(false);
        Assert.Equal(0, i.TakeAiKey());          // taken, no effect: it does not reach the next menu
        Assert.False(i.HasBuffered);
        Frame(i);
        i.ReadingKeys(true);
        Assert.False(i.Pressed(GameAction.Confirm));
        Frame(i, Keys.Space);
        i.ReadingKeys(false);
        Assert.Equal(0x20, i.TakeAiKey());       // space: the party back under the player's control
        Assert.False(i.HasBuffered);
    }

    [Fact]
    public void The_pad_takes_control_back_through_the_AI_key_check()
    {
        var i = new ActionInput(InputBindings.Defaults());
        PadFrame(i, Buttons.RightStick);         // R3 during a monster's pause
        i.ReadingKeys(false);
        Assert.Equal(0x20, i.TakeAiKey());
        PadFrame(i, Buttons.A);
        i.ReadingKeys(false);
        Assert.Equal(0, i.TakeAiKey());
    }

    /// <summary>The explore screen hands the fights its game builds the key check (0xBB81) on its action layer.</summary>
    [Fact]
    public void The_screen_wires_the_fights_key_check_to_its_buffer()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return;
        var dir = new Data.Por.PorGameDirectory(d);
        var art = Data.Por.PorViewArt.Load(dir);
        var runner = new Play.ScriptRunner();
        Data.Por.PorGame? game = null;
        var player = new Play.ClientPlayer(runner, () => game!.Memory) { Resources = Play.ClientResources.Load(dir), Game = () => game };
        game = Data.Por.PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!,
            new Data.Por.PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true), 7);
        var screen = new Screens.ExploreScreen(game, player, runner, new NoHost());
        var input = new ActionInput(InputBindings.Defaults());
        Frame(input);
        screen.Update(input);
        var check = Assert.IsType<Func<byte>>(game.Kit!.AiKey);
        Frame(input, Keys.Space);
        input.ReadingKeys(false);
        Assert.Equal(0x20, check());
        Assert.False(input.HasBuffered);
        runner.Dispose();
    }

    private sealed class NoHost : Screens.IScreenHost
    {
        public void Push(Screens.IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }

    [Fact]
    public void Skip_pauses_is_a_setting_that_starts_off()
    {
        Assert.False(new ClientSettings().SkipPauses);
        Assert.True(ClientSettings.FromJson("{\"SkipPauses\": true}").SkipPauses);
    }
}

/// <summary>The hall's J: the program's first command-line word (ParamStr(1)).</summary>
public class HallWordTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("WORD", "WORD")]
    [InlineData("  Word two", "Word")]
    [InlineData("\tA\tB", "A")]
    public void ParamStr1_is_the_first_word_of_the_command_tail(string? tail, string first) =>
        Assert.Equal(first, Play.HallScreens.ParamStr1(tail));

    [Fact]
    public void The_param_option_is_given_and_cleared()
    {
        Assert.Equal("W", ClientOptions.Parse(["--param", "W"]).ProgramWord);
        Assert.Equal("", ClientOptions.Parse(["--no-param"]).ProgramWord);
        Assert.Null(ClientOptions.Parse([]).ProgramWord);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--param"]));
    }

    [Fact]
    public void Without_the_game_files_there_is_no_word()
    {
        var text = new Play.HallText(null);
        Assert.Null(text.Program?.Text("Hall.Password"));
    }
}
