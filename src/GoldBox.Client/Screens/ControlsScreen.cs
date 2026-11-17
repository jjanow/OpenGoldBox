using GoldBox.Client.Input;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>
/// Shows every action with its key and pad button and lets the player rebind them: choose a row, then press the new
/// key or pad button (a click or Escape/Start leaves it as it was). Also: reset to defaults, back, quit. Fully usable
/// with keyboard, mouse or pad alone.
/// </summary>
public sealed class ControlsScreen(TextFont font, IScreenHost host) : IScreen
{
    private const int FirstRow = 4;

    private static readonly (GameAction Action, string Label)[] Rows =
    [
        (GameAction.Forward, "STEP FORWARD"),
        (GameAction.TurnLeft, "TURN LEFT"),
        (GameAction.TurnRight, "TURN RIGHT"),
        (GameAction.TurnAround, "TURN AROUND"),
        (GameAction.AreaMap, "AREA MAP"),
        (GameAction.Controls, "CONTROLS"),
        (GameAction.MenuUp, "MENU UP"),
        (GameAction.MenuDown, "MENU DOWN"),
        (GameAction.Confirm, "CHOOSE"),
        (GameAction.Cancel, "BACK"),
    ];

    private static readonly string[] Extra = ["RESET TO DEFAULTS", "BACK TO THE GAME", "QUIT"];

    private int _selected;
    private bool _waiting;
    private string _note = "";

    private int Count => Rows.Length + Extra.Length;

    private static int RowOf(int item) => FirstRow + item + (item >= Rows.Length ? 1 : 0);

    private int? ItemAt(int y)
    {
        for (int i = 0; i < Count; i++)
            if (y / 8 == RowOf(i))
                return i;
        return null;
    }

    public GameAction? MapClick(ScreenClick click)
    {
        if (_waiting)
        {
            _waiting = false;
            _note = "NOT CHANGED.";
            return null;
        }
        if (click.Right)
            return GameAction.Cancel;
        if (ItemAt(click.Y) is { } i)
        {
            _selected = i;
            return GameAction.Confirm;
        }
        return null;
    }

    public void Hover(int x, int y)
    {
        if (!_waiting && ItemAt(y) is { } i)
            _selected = i;
    }

    public void Update(ActionInput input)
    {
        if (_waiting)
        {
            if (input.NewInput is not { } n)
                return;
            _waiting = false;
            if (n.Name is "Escape" or "Start")
            {
                _note = "NOT CHANGED.";
                return;
            }
            var action = Rows[_selected].Action;
            input.Bindings.Rebind(action, n.Device, n.Name);
            host.SaveBindings();
            _note = $"{Rows[_selected].Label} NOW {(n.Device == InputDevice.Keyboard ? PromptLabels.Key(n.Name) : PromptLabels.Button(n.Name))}.";
            input.Swallow();
            return;
        }
        if (input.Pressed(GameAction.MenuUp))
            _selected = (_selected + Count - 1) % Count;
        if (input.Pressed(GameAction.MenuDown))
            _selected = (_selected + 1) % Count;
        if (input.Pressed(GameAction.Cancel) || input.Pressed(GameAction.Controls))
        {
            host.Pop();
            return;
        }
        if (!input.Pressed(GameAction.Confirm))
            return;
        if (_selected < Rows.Length)
        {
            _waiting = true;
            _note = "";
            return;
        }
        switch (_selected - Rows.Length)
        {
            case 0:
                input.Bindings = InputBindings.Defaults();
                host.SaveBindings();
                _note = "DEFAULTS RESTORED.";
                break;
            case 1:
                host.Pop();
                break;
            case 2:
                host.Quit();
                break;
        }
    }

    public void Draw(IndexedFrame f, ActionInput input)
    {
        f.Clear(0);
        Chrome.Box(f, 0, 0, 39, 24);
        Chrome.Text(f, font, 2, 1, "CONTROLS", Chrome.White);
        Chrome.Text(f, font, 2, 2, "ACTION", Chrome.Grey);
        Chrome.Text(f, font, 16, 2, "KEY", Chrome.Grey);
        Chrome.Text(f, font, 26, 2, "PAD", Chrome.Grey);
        var b = input.Bindings;
        for (int i = 0; i < Rows.Length; i++)
        {
            byte c = i == _selected ? Chrome.White : Chrome.Cyan;
            int row = RowOf(i);
            Chrome.Text(f, font, 2, row, Rows[i].Label, c);
            Chrome.Text(f, font, 16, row, Fit(PromptLabels.For(b, Rows[i].Action, InputDevice.Keyboard), 9), Chrome.Green);
            Chrome.Text(f, font, 26, row, Fit(PromptLabels.For(b, Rows[i].Action, InputDevice.Gamepad), 12), Chrome.Green);
        }
        for (int i = 0; i < Extra.Length; i++)
            Chrome.Text(f, font, 2, RowOf(Rows.Length + i), Extra[i], Rows.Length + i == _selected ? Chrome.White : Chrome.Cyan);
        Chrome.Text(f, font, 1, RowOf(_selected), ">", Chrome.Yellow);

        string help = _waiting
            ? $"PRESS A KEY OR PAD BUTTON FOR {Rows[_selected].Label}"
            : input.LastDevice switch
            {
                InputDevice.Gamepad => $"{PromptLabels.For(b, GameAction.Confirm, InputDevice.Gamepad)} CHOOSE  {PromptLabels.For(b, GameAction.Cancel, InputDevice.Gamepad)} BACK",
                InputDevice.Mouse => "CLICK TO CHOOSE  RIGHT CLICK: BACK",
                _ => $"{PromptLabels.For(b, GameAction.Confirm, InputDevice.Keyboard)} CHOOSE  {PromptLabels.For(b, GameAction.Cancel, InputDevice.Keyboard)} BACK",
            };
        Chrome.Text(f, font, 2, 21, Fit(help, 37), Chrome.Grey);
        if (_note.Length > 0)
            Chrome.Text(f, font, 2, 22, Fit(_note, 37), Chrome.Yellow);
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
