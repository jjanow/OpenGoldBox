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

    /// <summary>Presses one key per frame while the program does not read, with a key-up frame between.</summary>
    private static void Waiting(ActionInput i, params Keys[][] presses)
    {
        foreach (var p in presses)
        {
            Frame(i, p);
            i.ReadingKeys(false);
            Frame(i);
            i.ReadingKeys(false);
        }
    }

    private static void Read(ActionInput i)
    {
        Frame(i);
        i.ReadingKeys(true);
    }

    [Fact]
    public void An_extended_key_after_a_character_reads_as_its_scan_code()
    {
        // DOSBox-X s24, party menu: X then Right during the hall word's message opened Modify ('M' = Right's 0x4D).
        var i = new ActionInput(InputBindings.Defaults());
        Waiting(i, [Keys.X], [Keys.Right]);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "M"), i.NewInput);
        Assert.False(i.Pressed(GameAction.MenuRight));
        Assert.False(i.HasBuffered);
    }

    [Fact]
    public void A_character_after_an_extended_key_reads_as_an_extended_code()
    {
        // DOSBox-X s24: Right then C read an extended 0x63, which no menu takes: nothing happened, C was used up.
        var i = new ActionInput(InputBindings.Defaults());
        Waiting(i, [Keys.Right], [Keys.C]);
        Read(i);
        Assert.Null(i.NewInput);
        Assert.False(i.HasBuffered);
        // An extended key after an extended key reads as the last one.
        Waiting(i, [Keys.Right], [Keys.Left]);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "Left"), i.NewInput);
        Assert.True(i.Pressed(GameAction.MenuLeft));
        Waiting(i, [Keys.Home], [Keys.LeftAlt, Keys.X]);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "Alt+X"), i.NewInput);
    }

    [Fact]
    public void A_single_read_leaves_an_extended_keys_scan_code_waiting()
    {
        // A key wait or line input calls GetKey once: an extended key first gives 0 and its scan code stays; the next
        // read takes it as a character (DOSBox-X s24: Right at the character-name prompt typed M).
        var i = new ActionInput(InputBindings.Defaults());
        Waiting(i, [Keys.Right]);
        Frame(i);
        i.ReadingKeys(true, scanRead: false);
        Assert.Equal((InputDevice.Keyboard, "Right"), i.NewInput);
        Assert.True(i.HasBuffered);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "M"), i.NewInput);
        Assert.False(i.HasBuffered);
        // With more keys behind it, the next read drains them and keeps the last as a character.
        Waiting(i, [Keys.Right], [Keys.C]);
        Frame(i);
        i.ReadingKeys(true, scanRead: false);
        Assert.Equal((InputDevice.Keyboard, "Right"), i.NewInput);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "C"), i.NewInput);
        // A character first: the single read drains everything as GetKey does.
        Waiting(i, [Keys.X], [Keys.Up]);
        Frame(i);
        i.ReadingKeys(true, scanRead: false);
        Assert.Equal((InputDevice.Keyboard, "H"), i.NewInput);
        Assert.False(i.HasBuffered);
    }

    [Fact]
    public void One_waiting_extended_key_is_read_as_itself()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Waiting(i, [Keys.Right]);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "Right"), i.NewInput);
        Assert.True(i.Pressed(GameAction.MenuRight));
    }

    [Fact]
    public void The_buffer_holds_fifteen_keys_and_loses_later_ones()
    {
        // DOSBox-X s24: fifteen keys waiting, the last one C, opened Create; sixteen or seventeen did not.
        var i = new ActionInput(InputBindings.Defaults());
        for (int k = 0; k < 14; k++)
            Waiting(i, [Keys.X]);
        Waiting(i, [Keys.C]);
        Assert.Equal(ActionInput.BufferSize, i.Buffered);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "C"), i.NewInput);
        for (int k = 0; k < 15; k++)
            Waiting(i, [Keys.X]);
        Waiting(i, [Keys.C]);
        Assert.Equal(ActionInput.BufferSize, i.Buffered);
        Read(i);
        Assert.Equal((InputDevice.Keyboard, "X"), i.NewInput);
    }

    [Fact]
    public void A_waiting_pad_button_read_last_is_itself()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Waiting(i, [Keys.Right]);
        PadFrame(i, Buttons.A);
        i.ReadingKeys(false);
        Read(i);
        Assert.True(i.Pressed(GameAction.Confirm));
        Assert.Equal((InputDevice.Gamepad, "A"), i.NewInput);
    }

    [Fact]
    public void Dos_key_bytes()
    {
        Assert.Equal(((byte)'m', (byte)0x32), DosKeys.Of("M"));
        Assert.Equal(((byte)0, (byte)0x32), DosKeys.Of("Alt+M"));
        Assert.Equal(((byte)0x13, (byte)0x1F), DosKeys.Of("Ctrl+S"));
        Assert.Equal(((byte)0, (byte)0x48), DosKeys.Of("Up"));
        Assert.Equal(((byte)'\r', (byte)0), DosKeys.Of("Enter"));
        Assert.Equal(((byte)'2', (byte)3), DosKeys.Of("D2"));
        Assert.Null(DosKeys.Of("F12"));
        Assert.Equal("M", DosKeys.OfCharacter(0x4D));
        Assert.Equal("Space", DosKeys.OfCharacter(0x20));
        Assert.Equal("Up", DosKeys.OfScan(0x48));
        Assert.Equal("Alt+X", DosKeys.OfScan(0x2D));
        Assert.Null(DosKeys.OfScan(0x63));
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

    [Fact]
    public void Alt_keys_are_their_own_bindings_and_reach_the_AI_key_check()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Frame(i);
        Frame(i, Keys.LeftAlt, Keys.M);
        Assert.True(i.Pressed(GameAction.FightQuickMagic));
        Assert.Equal((InputDevice.Keyboard, "Alt+M"), i.NewInput);   // not the hotkey M
        Frame(i);
        Frame(i, Keys.M);
        Assert.False(i.Pressed(GameAction.FightQuickMagic));
        Assert.Equal((InputDevice.Keyboard, "M"), i.NewInput);
        Frame(i);
        Frame(i, Keys.RightAlt, Keys.X);
        i.ReadingKeys(false);
        Assert.Equal(0x2D, i.TakeAiKey());                           // Alt-X: scan code 0x2D (0xBB81 → 0x1638E)
        Frame(i);
        Frame(i, Keys.LeftAlt, Keys.M);
        i.ReadingKeys(false);
        Assert.Equal(0x32, i.TakeAiKey());                           // Alt-M: 0x32
        Frame(i);
        Frame(i, Keys.LeftAlt, Keys.Q);
        i.ReadingKeys(false);
        Assert.Equal(0, i.TakeAiKey());                              // Alt-Q: menu only
        Frame(i);
        Frame(i, Keys.D2);
        i.ReadingKeys(false);
        Assert.Equal(0x32, i.TakeAiKey());                           // the check compares the byte: '2' is Alt-M's 0x32
        Assert.Equal("ALT-M", PromptLabels.Key("Alt+M"));
    }

    /// <summary>
    /// The Amiga program's fight keys (raw-key table hunk 20 +0x629): ';' and F8 give the magic toggle 0x3B, F6 the
    /// all-quick key 0xE6; Alt is not read, so there is no Alt-M or Alt-Q, and the AI's key check takes only the toggle
    /// and space ('2' is not the toggle there, '-' no Alt-X).
    /// </summary>
    [Fact]
    public void The_amiga_fight_keys_are_semicolon_f8_and_f6()
    {
        var i = new ActionInput(InputBindings.Defaults()) { AmigaKeys = true };
        Frame(i);
        Frame(i, Keys.F8);
        Assert.True(i.Pressed(GameAction.FightQuickMagic));
        Frame(i);
        Frame(i, Keys.OemSemicolon);
        Assert.True(i.Pressed(GameAction.FightQuickMagic));
        Frame(i);
        Frame(i, Keys.LeftAlt, Keys.M);
        Assert.False(i.Pressed(GameAction.FightQuickMagic));
        Frame(i);
        Frame(i, Keys.F6);
        Assert.True(i.Pressed(GameAction.FightAllQuick));
        Frame(i);
        Frame(i, Keys.LeftAlt, Keys.Q);
        Assert.False(i.Pressed(GameAction.FightAllQuick));
        Frame(i);
        Frame(i, Keys.OemSemicolon);
        i.ReadingKeys(false);
        Assert.Equal(0x32, i.TakeAiKey());   // the toggle
        Frame(i);
        Frame(i, Keys.D2);
        i.ReadingKeys(false);
        Assert.Equal(0, i.TakeAiKey());
        Frame(i);
        Frame(i, Keys.OemMinus);
        i.ReadingKeys(false);
        Assert.Equal(0, i.TakeAiKey());
        Frame(i);
        Frame(i, Keys.Space);
        i.ReadingKeys(false);
        Assert.Equal(0x20, i.TakeAiKey());
        PadFrame(i, Buttons.LeftTrigger);    // the pad keeps its buttons
        Assert.True(i.Pressed(GameAction.FightQuickMagic));

        var dos = new ActionInput(InputBindings.Defaults()) { AmigaKeys = false };
        Frame(dos);
        Frame(dos, Keys.F8);
        Assert.False(dos.Pressed(GameAction.FightQuickMagic));
    }

    [Fact]
    public void The_triggers_toggle_magic_and_all_quick_on_the_pad()
    {
        var i = new ActionInput(InputBindings.Defaults());
        Assert.Contains("LeftTrigger", i.Bindings.Buttons(GameAction.FightQuickMagic));
        Assert.Contains("RightTrigger", i.Bindings.Buttons(GameAction.FightAllQuick));
        PadFrame(i, Buttons.LeftTrigger);
        Assert.True(i.Pressed(GameAction.FightQuickMagic));
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
