using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoldBox.Client.Input;

/// <summary>
/// Everything the player can do, whatever the device. Screens ask for actions, never for keys or buttons, so every
/// action works from the keyboard, the mouse and the pad (PLAN.md section 3, decided s6).
/// </summary>
public enum GameAction
{
    /// <summary>Step forward (the original's Up key).</summary>
    Forward,
    /// <summary>Turn left (Left key).</summary>
    TurnLeft,
    /// <summary>Turn right (Right key).</summary>
    TurnRight,
    /// <summary>About-face (Down key).</summary>
    TurnAround,
    /// <summary>Toggle the area map (the original's A command).</summary>
    AreaMap,
    /// <summary>Open the controls screen (rebinding, quit).</summary>
    Controls,
    /// <summary>Search mode on/off (the original's S command: steps take 10 minutes).</summary>
    Search,
    /// <summary>Look around this square (L: 10 minutes, the square's search event).</summary>
    Look,
    /// <summary>Encamp (E).</summary>
    Encamp,
    /// <summary>Menus: move the selection up.</summary>
    MenuUp,
    /// <summary>Menus: move the selection down.</summary>
    MenuDown,
    /// <summary>Menus: move the selection left (menus on the bottom line; text entry: delete).</summary>
    MenuLeft,
    /// <summary>Menus: move the selection right (text entry: add the picked character).</summary>
    MenuRight,
    /// <summary>Menus: choose.</summary>
    Confirm,
    /// <summary>Menus: back out.</summary>
    Cancel,
    /// <summary>Shop, temple, loot, camp and view: the previous party member (the original's Home key).</summary>
    PreviousMember,
    /// <summary>Shop, temple, loot, camp and view: the next party member (the original's End key).</summary>
    NextMember,
    /// <summary>Combat: step or move the cursor up-left (the original's keypad Home / 7).</summary>
    StepUpLeft,
    /// <summary>Combat: up-right (keypad PgUp / 9).</summary>
    StepUpRight,
    /// <summary>Combat: down-left (keypad End / 1).</summary>
    StepDownLeft,
    /// <summary>Combat: down-right (keypad PgDn / 3).</summary>
    StepDownRight,
}

/// <summary>The input device a binding or an input event belongs to.</summary>
public enum InputDevice
{
    Keyboard,
    Mouse,
    Gamepad,
}

/// <summary>
/// The rebindable bindings: per action, the keys and pad buttons that trigger it, by name (MonoGame's <c>Keys</c> and
/// <c>Buttons</c> member names, e.g. "Up", "NumPad8", "DPadUp", "LeftThumbstickUp", "Y"). This type holds no MonoGame
/// types so the rules can be tested without a window; <see cref="ActionInput"/> maps the names. Saved as JSON.
/// </summary>
public sealed class InputBindings
{
    public sealed class Binding
    {
        public List<string> Keys { get; set; } = [];
        public List<string> Buttons { get; set; } = [];
    }

    [JsonInclude]
    public Dictionary<GameAction, Binding> Actions { get; private set; } = [];

    /// <summary>The defaults: the original's keys first (arrows, A), then common alternatives; the pad's D-pad and left stick move.</summary>
    public static InputBindings Defaults()
    {
        var b = new InputBindings();
        b.Set(GameAction.Forward, ["Up", "NumPad8"], ["DPadUp", "LeftThumbstickUp"]);
        b.Set(GameAction.TurnLeft, ["Left", "NumPad4"], ["DPadLeft", "LeftThumbstickLeft"]);
        b.Set(GameAction.TurnRight, ["Right", "NumPad6"], ["DPadRight", "LeftThumbstickRight"]);
        b.Set(GameAction.TurnAround, ["Down", "NumPad2"], ["DPadDown", "LeftThumbstickDown"]);
        b.Set(GameAction.AreaMap, ["A"], ["Y"]);
        b.Set(GameAction.Controls, ["F1"], ["Back"]);
        b.Set(GameAction.Search, ["S"], ["X"]);
        b.Set(GameAction.Look, ["L"], ["RightShoulder"]);
        b.Set(GameAction.Encamp, ["E"], ["LeftShoulder"]);
        b.Set(GameAction.MenuLeft, ["Left"], ["DPadLeft", "LeftThumbstickLeft"]);
        b.Set(GameAction.MenuRight, ["Right"], ["DPadRight", "LeftThumbstickRight"]);
        b.Set(GameAction.MenuUp, ["Up"], ["DPadUp", "LeftThumbstickUp"]);
        b.Set(GameAction.MenuDown, ["Down"], ["DPadDown", "LeftThumbstickDown"]);
        b.Set(GameAction.Confirm, ["Enter"], ["A"]);
        b.Set(GameAction.Cancel, ["Escape"], ["B"]);
        // The shoulders are Look / Encamp while exploring; those screens never ask for both at once.
        b.Set(GameAction.PreviousMember, ["Home"], ["LeftShoulder"]);
        b.Set(GameAction.NextMember, ["End"], ["RightShoulder"]);
        // Combat diagonals: the original's keypad (Home, PgUp, End, PgDn; the digits 7, 9, 1, 3). The pad steps
        // diagonally with the left stick or D-pad held between two directions, or with the right stick turned 45°
        // (up = up-right, right = down-right, down = down-left, left = up-left). Home / End double as the member keys,
        // which combat does not read.
        b.Set(GameAction.StepUpLeft, ["Home", "NumPad7"], ["RightThumbstickLeft"]);
        b.Set(GameAction.StepUpRight, ["PageUp", "NumPad9"], ["RightThumbstickUp"]);
        b.Set(GameAction.StepDownLeft, ["End", "NumPad1"], ["RightThumbstickDown"]);
        b.Set(GameAction.StepDownRight, ["PageDown", "NumPad3"], ["RightThumbstickRight"]);
        return b;
    }

    private void Set(GameAction a, List<string> keys, List<string> buttons) => Actions[a] = new Binding { Keys = keys, Buttons = buttons };

    public IReadOnlyList<string> Keys(GameAction a) => Actions.TryGetValue(a, out var b) ? b.Keys : [];
    public IReadOnlyList<string> Buttons(GameAction a) => Actions.TryGetValue(a, out var b) ? b.Buttons : [];

    /// <summary>
    /// Rebinds the action's first key (keyboard) or first button (pad) to <paramref name="name"/>. Other actions keep
    /// their bindings: menu actions and movement share keys on purpose (each screen asks only for its own actions).
    /// </summary>
    public void Rebind(GameAction a, InputDevice device, string name)
    {
        if (!Actions.TryGetValue(a, out var b))
            Actions[a] = b = new Binding();
        var list = device switch
        {
            InputDevice.Keyboard => b.Keys,
            InputDevice.Gamepad => b.Buttons,
            _ => throw new ArgumentException("the mouse has no rebindable bindings", nameof(device)),
        };
        list.Remove(name);
        if (list.Count == 0)
            list.Add(name);
        else
            list[0] = name;
    }

    /// <summary>The actions whose bindings for <paramref name="device"/> include <paramref name="name"/>.</summary>
    public IEnumerable<GameAction> ActionsFor(InputDevice device, string name) =>
        Actions.Where(kv => (device == InputDevice.Keyboard ? kv.Value.Keys : kv.Value.Buttons).Contains(name)).Select(kv => kv.Key);

    /// <summary>Fills actions missing from a saved file with their defaults (a newer Client may add actions).</summary>
    private void CompleteFrom(InputBindings defaults)
    {
        foreach (var (a, b) in defaults.Actions)
            if (!Actions.ContainsKey(a))
                Actions[a] = new Binding { Keys = [.. b.Keys], Buttons = [.. b.Buttons] };
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads saved bindings; a broken or empty file gives the defaults.</summary>
    public static InputBindings FromJson(string? json)
    {
        var defaults = Defaults();
        if (string.IsNullOrWhiteSpace(json))
            return defaults;
        try
        {
            var b = JsonSerializer.Deserialize<InputBindings>(json, Json) ?? defaults;
            b.CompleteFrom(defaults);
            return b;
        }
        catch (JsonException)
        {
            return defaults;
        }
    }
}
