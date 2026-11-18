namespace GoldBox.Client.Input;

/// <summary>
/// Short on-screen names of bindings, in the device the player used last, so help lines show "UP" to a keyboard player
/// and "DPAD UP" to a pad player. Upper case only: the game font has no lower-case letters.
/// </summary>
public static class PromptLabels
{
    public static string Key(string name) => name switch
    {
        "Up" => "UP",
        "Down" => "DOWN",
        "Left" => "LEFT",
        "Right" => "RIGHT",
        "Enter" => "ENTER",
        "Escape" => "ESC",
        "Space" => "SPACE",
        "Back" => "BKSP",
        "Tab" => "TAB",
        "PageUp" => "PGUP",
        "PageDown" => "PGDN",
        "Home" => "HOME",
        "End" => "END",
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) => "PAD " + name[6..],
        _ when name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) => name[1..],
        _ => name.ToUpperInvariant(),
    };

    public static string Button(string name) => name switch
    {
        "DPadUp" => "DPAD UP",
        "DPadDown" => "DPAD DOWN",
        "DPadLeft" => "DPAD LEFT",
        "DPadRight" => "DPAD RIGHT",
        "LeftThumbstickUp" => "LSTICK UP",
        "LeftThumbstickDown" => "LSTICK DOWN",
        "LeftThumbstickLeft" => "LSTICK LEFT",
        "LeftThumbstickRight" => "LSTICK RIGHT",
        "RightThumbstickUp" => "RSTICK UP",
        "RightThumbstickDown" => "RSTICK DOWN",
        "RightThumbstickLeft" => "RSTICK LEFT",
        "RightThumbstickRight" => "RSTICK RIGHT",
        "LeftShoulder" => "LB",
        "RightShoulder" => "RB",
        "LeftTrigger" => "LT",
        "RightTrigger" => "RT",
        "LeftStick" => "L3",
        "RightStick" => "R3",
        _ => name.ToUpperInvariant(),
    };

    /// <summary>What the mouse does for an action (the mouse has fixed gestures, not bindings).</summary>
    public static string Mouse(GameAction a) => a switch
    {
        GameAction.Forward => "CLICK VIEW CENTRE",
        GameAction.TurnLeft => "CLICK VIEW LEFT",
        GameAction.TurnRight => "CLICK VIEW RIGHT",
        GameAction.TurnAround => "CLICK VIEW BOTTOM",
        GameAction.AreaMap => "CLICK AREA",
        GameAction.Controls => "CLICK CONTROLS",
        GameAction.Confirm => "CLICK",
        GameAction.Cancel => "RIGHT CLICK",
        GameAction.MenuUp or GameAction.MenuDown or GameAction.MenuLeft or GameAction.MenuRight => "POINT",
        GameAction.Search => "CLICK SEARCH",
        GameAction.Look => "CLICK LOOK",
        GameAction.Encamp => "CLICK ENCAMP",
        GameAction.PreviousMember or GameAction.NextMember => "CLICK A NAME",
        GameAction.StepUpLeft or GameAction.StepUpRight or GameAction.StepDownLeft or GameAction.StepDownRight => "CLICK A SQUARE",
        GameAction.HallJ => "CLICK THE PROMPT",
        _ => "",
    };

    /// <summary>The label of an action's first binding on a device ("" when it has none there).</summary>
    public static string For(InputBindings b, GameAction a, InputDevice device) => device switch
    {
        InputDevice.Keyboard => b.Keys(a) is [var k, ..] ? Key(k) : "",
        InputDevice.Gamepad => b.Buttons(a) is [var p, ..] ? Button(p) : "",
        _ => Mouse(a),
    };
}
