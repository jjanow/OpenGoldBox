using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Explore;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

public class InputBindingsTests
{
    [Fact]
    public void Every_action_is_bound_on_keyboard_and_pad()
    {
        var b = InputBindings.Defaults();
        foreach (var a in Enum.GetValues<GameAction>())
        {
            Assert.NotEmpty(b.Keys(a));
            Assert.NotEmpty(b.Buttons(a));
        }
    }

    [Fact]
    public void Binding_names_are_real_monogame_keys_and_buttons()
    {
        var b = InputBindings.Defaults();
        foreach (var a in Enum.GetValues<GameAction>())
        {
            // A key may carry modifiers ("Alt+M", "Ctrl+S"); the rest is a MonoGame key name.
            Assert.All(b.Keys(a), k => Assert.True(Enum.TryParse<Keys>(k.Replace("Ctrl+", "").Replace("Alt+", ""), out _), k));
            Assert.All(b.Buttons(a), p => Assert.True(Enum.TryParse<Buttons>(p, out _), p));
        }
    }

    [Fact]
    public void Defaults_keep_the_originals_keys()
    {
        var b = InputBindings.Defaults();
        Assert.Equal("Up", b.Keys(GameAction.Forward)[0]);
        Assert.Equal("Left", b.Keys(GameAction.TurnLeft)[0]);
        Assert.Equal("Right", b.Keys(GameAction.TurnRight)[0]);
        Assert.Equal("Down", b.Keys(GameAction.TurnAround)[0]);
        Assert.Equal("A", b.Keys(GameAction.AreaMap)[0]);
    }

    [Fact]
    public void Rebinding_replaces_the_first_binding_of_that_device_only()
    {
        var b = InputBindings.Defaults();
        b.Rebind(GameAction.TurnLeft, InputDevice.Gamepad, "X");
        Assert.Equal(["X", "LeftThumbstickLeft"], b.Buttons(GameAction.TurnLeft));
        Assert.Equal(["Left", "NumPad4"], b.Keys(GameAction.TurnLeft));
        b.Rebind(GameAction.TurnLeft, InputDevice.Keyboard, "NumPad4");
        Assert.Equal(["NumPad4"], b.Keys(GameAction.TurnLeft)); // no duplicate
        Assert.Throws<ArgumentException>(() => b.Rebind(GameAction.TurnLeft, InputDevice.Mouse, "Left"));
    }

    [Fact]
    public void Bindings_round_trip_through_json()
    {
        var b = InputBindings.Defaults();
        b.Rebind(GameAction.AreaMap, InputDevice.Keyboard, "M");
        var back = InputBindings.FromJson(b.ToJson());
        Assert.Equal(["M"], back.Keys(GameAction.AreaMap));
        Assert.Contains(GameAction.AreaMap, back.ActionsFor(InputDevice.Keyboard, "M"));
    }

    [Fact]
    public void Broken_or_partial_files_fall_back_to_defaults()
    {
        Assert.Equal("Up", InputBindings.FromJson("{ not json").Keys(GameAction.Forward)[0]);
        Assert.Equal("Up", InputBindings.FromJson(null).Keys(GameAction.Forward)[0]);
        var partial = InputBindings.FromJson("""{ "Actions": { "Forward": { "Keys": ["W"], "Buttons": ["A"] } } }""");
        Assert.Equal(["W"], partial.Keys(GameAction.Forward));
        Assert.Equal("Escape", partial.Keys(GameAction.Cancel)[0]); // missing actions get their defaults
    }
}

public class PromptLabelTests
{
    [Fact]
    public void Prompts_follow_the_device()
    {
        var b = InputBindings.Defaults();
        Assert.Equal("UP", PromptLabels.For(b, GameAction.Forward, InputDevice.Keyboard));
        Assert.Equal("DPAD UP", PromptLabels.For(b, GameAction.Forward, InputDevice.Gamepad));
        Assert.Equal("CLICK VIEW CENTRE", PromptLabels.For(b, GameAction.Forward, InputDevice.Mouse));
        Assert.Equal("Y", PromptLabels.For(b, GameAction.AreaMap, InputDevice.Gamepad));
        Assert.Equal("ESC", PromptLabels.For(b, GameAction.Cancel, InputDevice.Keyboard));
    }

    [Theory]
    [InlineData("NumPad8", "PAD 8")]
    [InlineData("D5", "5")]
    [InlineData("F1", "F1")]
    [InlineData("M", "M")]
    public void Key_labels(string key, string label) => Assert.Equal(label, PromptLabels.Key(key));

    [Fact]
    public void Prompt_labels_use_only_characters_the_game_font_has()
    {
        var font = new TextFont(new byte[177 * 8]);
        var b = InputBindings.Defaults();
        foreach (var a in Enum.GetValues<GameAction>())
            foreach (var d in Enum.GetValues<InputDevice>())
                Assert.All(PromptLabels.For(b, a, d), c => Assert.True(font.GlyphOf(c) >= 0, $"{a} {d}: '{c}'"));
        foreach (var k in Enum.GetNames<Keys>())
            Assert.All(PromptLabels.Key(k), c => Assert.True(font.GlyphOf(c) >= 0, $"{k}: '{c}'"));
        foreach (var p in Enum.GetNames<Buttons>())
            Assert.All(PromptLabels.Button(p), c => Assert.True(font.GlyphOf(c) >= 0, $"{p}: '{c}'"));
    }
}

public class ClientOptionsTests
{
    [Fact]
    public void Area_start_parses_square_and_facing()
    {
        var o = ClientOptions.Parse(["--game", "/g", "--area", "20", "--at", "15,4,W", "--scale", "2"]);
        Assert.Equal("/g", o.GameDirectory);
        Assert.Equal(20, o.Area);
        Assert.Equal((15, 4, Facing.West), o.At);
        Assert.Equal(2, o.Scale);
    }

    [Fact]
    public void Bad_command_lines_are_refused()
    {
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--area", "20"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--area", "20", "--at", "1,2,X"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--area", "20", "--at", "16,2,N"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--save", "A", "--area", "1", "--at", "1,1,N"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--bogus"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--game"]));
    }

    [Fact]
    public void Save_letters_name_the_save_file()
    {
        Assert.Equal("SAVGAMB.DAT", ClientOptions.SaveFileName("b"));
        Assert.Equal("/x/MY.DAT", ClientOptions.SaveFileName("/x/MY.DAT"));
        Assert.Equal('B', ClientOptions.SlotOf("b"));
        Assert.Equal('C', ClientOptions.SlotOf("/x/SAVGAMC.DAT"));
        Assert.Equal('D', ClientOptions.SlotOf("/x/d.json"));
        Assert.Equal('A', ClientOptions.SlotOf("/x/MY.DAT"));
    }

    [Fact]
    public void A_save_letter_finds_engine_saves_before_original_files_and_own_saves_first()
    {
        var tmp = Directory.CreateTempSubdirectory("goldbox-saves-");
        try
        {
            var own = Directory.CreateDirectory(Path.Combine(tmp.FullName, "own")).FullName;
            var module = Directory.CreateDirectory(Path.Combine(tmp.FullName, "module")).FullName;
            File.WriteAllText(Path.Combine(module, "SAVGAMA.DAT"), "");
            File.WriteAllText(Path.Combine(module, "a.json"), "");
            File.WriteAllText(Path.Combine(module, "SAVGAMJ.DAT"), "");
            File.WriteAllText(Path.Combine(own, "SAVGAMB.DAT"), "");
            File.WriteAllText(Path.Combine(own, "SAVGAMA.DAT"), "");
            Assert.Equal(Path.Combine(own, "SAVGAMA.DAT"), ClientOptions.ResolveSave("a", own, module));
            File.WriteAllText(Path.Combine(own, "a.json"), "");
            Assert.Equal(Path.Combine(own, "a.json"), ClientOptions.ResolveSave("A", own, module));
            Assert.Equal(Path.Combine(own, "SAVGAMB.DAT"), ClientOptions.ResolveSave("B", own, module));
            Assert.Equal(Path.Combine(module, "SAVGAMJ.DAT"), ClientOptions.ResolveSave("J", own, module));
            Assert.Null(ClientOptions.ResolveSave("C", own, module));
            Assert.Equal(Path.Combine(module, "a.json"), ClientOptions.ResolveSave("A", Path.Combine(tmp.FullName, "none"), module));
            Assert.Equal(Path.Combine(module, "SAVGAMJ.DAT"), ClientOptions.ResolveSave("savgamj.dat", own, module));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }
}

public class PadSlotTests
{
    [Fact]
    public void The_first_connected_pad_is_read_whatever_its_slot()
    {
        var connected = new GamePadState(default, default, new GamePadButtons(Buttons.A), default);
        Assert.True(connected.IsConnected);
        var asked = new List<int>();
        GamePadState State(int i)
        {
            asked.Add(i);
            return i == 2 ? connected : default;
        }
        var s = GoldBoxGame.FirstConnectedPad(4, State);
        Assert.True(s.IsConnected);
        Assert.Equal(ButtonState.Pressed, s.Buttons.A);
        Assert.Equal([0, 1, 2], asked);
        // None connected: slot 0's (disconnected) state.
        Assert.False(GoldBoxGame.FirstConnectedPad(4, _ => default).IsConnected);
    }
}

/// <summary>
/// The Amiga's input requester types letters as its keymap does (s46, TextRequest.KeepsCase): lower case unless Shift
/// or Caps Lock; DOS's line inputs keep the capitals.
/// </summary>
public class AmigaKeymapTests
{
    private static ActionInput Press(params Keys[] keys) => Press(false, keys);

    private static ActionInput Press(bool capsLock, params Keys[] keys)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(new KeyboardState(keys, capsLock), default, default, TimeSpan.Zero, _ => null);
        return input;
    }

    private static char? Typed(ActionInput input, bool keepsCase) =>
        input.NewInput is (InputDevice.Keyboard, var name) ? GoldBox.Client.Screens.CharGrid.KeyChar(name, input, keepsCase) : null;

    [Fact]
    public void A_letter_is_lower_case_unless_shift_or_caps_lock_under_the_amiga_requester()
    {
        Assert.Equal('l', Typed(Press(Keys.L), keepsCase: true));
        Assert.Equal('L', Typed(Press(Keys.LeftShift, Keys.L), keepsCase: true));   // Shift in the same frame gives way
        Assert.Equal('L', Typed(Press(true, Keys.L), keepsCase: true));
        Assert.Equal('l', Typed(Press(true, Keys.RightShift, Keys.L), keepsCase: true));
        Assert.Equal('L', Typed(Press(Keys.L), keepsCase: false));                   // DOS: capitals
        Assert.Equal('7', Typed(Press(Keys.D7), keepsCase: true));
        Assert.True(GoldBox.Client.Screens.CharGrid.Holds(GoldBox.Client.Screens.CharGrid.Letters, 'l'));
    }
}

/// <summary>
/// The keyboard on a line input (s55): every printable key as a US keyboard gives it, Shift included, and each request's
/// rule for what it takes (DOS START 0x7A73: 0x20–0x7A; the money input 0x24F1D and the Amiga's number requester: digits;
/// the Amiga's string requester: anything printable).
/// </summary>
public class LineInputKeyTests
{
    private static ActionInput Press(params Keys[] keys)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(new KeyboardState(keys), default, default, TimeSpan.Zero, _ => null);
        return input;
    }

    private static char? Typed(params Keys[] keys) =>
        Press(keys) is { NewInput: (InputDevice.Keyboard, var name) } input ? GoldBox.Client.Screens.CharGrid.KeyChar(name, input, false) : null;

    [Fact]
    public void Printable_keys_type_as_the_keyboard_gives_them()
    {
        Assert.Equal('!', DosKeys.Printable("D1", true));
        Assert.Equal('1', DosKeys.Printable("D1", false));
        Assert.Equal(')', DosKeys.Printable("D0", true));
        Assert.Equal('~', DosKeys.Printable("OemTilde", true));
        Assert.Equal('`', DosKeys.Printable("OemTilde", false));
        Assert.Equal('*', DosKeys.Printable("Multiply", false));
        Assert.Equal('7', DosKeys.Printable("NumPad7", true));
        Assert.Equal('Q', DosKeys.Printable("Q", false));
        Assert.Null(DosKeys.Printable("Tab", false));
        Assert.Null(DosKeys.Printable("Enter", false));
        Assert.Null(DosKeys.Printable("Escape", false));
        Assert.Null(DosKeys.Printable("Back", false));
        Assert.Null(DosKeys.Printable("F1", false));
        Assert.Null(DosKeys.Printable("Alt+A", false));
        Assert.Equal('_', Typed(Keys.LeftShift, Keys.OemMinus));
        Assert.Equal('[', Typed(Keys.OemOpenBrackets));
        Assert.Equal('?', Typed(Keys.RightShift, Keys.OemQuestion));
        Assert.Equal('A', Typed(Keys.A));
    }

    [Fact]
    public void Each_input_takes_its_own_characters()
    {
        var dos = new TextRequest("", false, 40);
        foreach (char c in " !\"#[\\]^_`AZaz09")
            Assert.True(dos.Takes(c), c.ToString());
        foreach (char c in "{|}~")
            Assert.False(dos.Takes(c), c.ToString());
        var number = new TextRequest("", true, 6);       // DOS INPUTNUM: 0x7A73 too, Val judges the line
        Assert.True(number.Takes('x'));
        var amount = new TextRequest("", true, 6) { Maximum = 100 };
        Assert.True(amount.Takes('5'));
        Assert.False(amount.Takes('!'));
        Assert.False(amount.Takes(' '));
        var amiga = new TextRequest("", false, 39) { KeepsCase = true };
        foreach (char c in "{|}~a!")
            Assert.True(amiga.Takes(c), c.ToString());
        Assert.False(amiga.Takes('\x7F'));
    }
}

