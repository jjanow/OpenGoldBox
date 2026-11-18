using GoldBox.Client.Input;
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
            Assert.All(b.Keys(a), k => Assert.True(Enum.TryParse<Keys>(k, out _), k));
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
