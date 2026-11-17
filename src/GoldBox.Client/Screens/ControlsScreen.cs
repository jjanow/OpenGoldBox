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
    private const int FirstRow = 3;

    private static readonly (GameAction Action, string Label)[] Rows =
    [
        (GameAction.Forward, "STEP FORWARD"),
        (GameAction.TurnLeft, "TURN LEFT"),
        (GameAction.TurnRight, "TURN RIGHT"),
        (GameAction.TurnAround, "TURN AROUND"),
        (GameAction.AreaMap, "AREA MAP"),
        (GameAction.Search, "SEARCH ON/OFF"),
        (GameAction.Look, "LOOK"),
        (GameAction.Encamp, "ENCAMP"),
        (GameAction.Controls, "CONTROLS"),
        (GameAction.MenuUp, "MENU UP"),
        (GameAction.MenuDown, "MENU DOWN"),
        (GameAction.MenuLeft, "MENU LEFT"),
        (GameAction.MenuRight, "MENU RIGHT"),
        (GameAction.Confirm, "CHOOSE"),
        (GameAction.Cancel, "BACK"),
        (GameAction.PreviousMember, "PREVIOUS MEMBER"),
        (GameAction.NextMember, "NEXT MEMBER"),
        (GameAction.StepUpLeft, "FIGHT: UP-LEFT"),
        (GameAction.StepUpRight, "FIGHT: UP-RIGHT"),
        (GameAction.StepDownLeft, "FIGHT: DOWN-LEFT"),
        (GameAction.StepDownRight, "FIGHT: DOWN-RIGHT"),
    ];

    private static readonly string[] Extra = ["RESET TO DEFAULTS", "BACK TO THE GAME", "QUIT"];

    private int _selected;
    private bool _waiting;
    private string _note = "";

    private int Count => Rows.Length + Extra.Length;

    /// <summary>
    /// The actions scroll in rows 3–17 (keyboard and pad move the window with the selection; the mouse clicks the
    /// MORE line on row 18 to page). The extras stay on rows 19–21, so every device always reaches them.
    /// </summary>
    private const int VisibleRows = 15, MoreRow = FirstRow + VisibleRows, ExtraRow = MoreRow + 1;

    private int _top;

    private bool Scrolls => Rows.Length > VisibleRows;

    /// <summary>The screen row of an item, or null when scrolled out of view.</summary>
    private int? RowOf(int item)
    {
        if (item >= Rows.Length)
            return ExtraRow + item - Rows.Length;
        int l = item - _top;
        return l >= 0 && l < VisibleRows ? FirstRow + l : null;
    }

    private int? ItemAt(int y)
    {
        for (int i = 0; i < Count; i++)
            if (RowOf(i) is { } r && y / 8 == r)
                return i;
        return null;
    }

    /// <summary>Keyboard / pad: keep the selected action inside the window (the mouse never moves it).</summary>
    private void KeepSelectionVisible()
    {
        if (_selected >= Rows.Length)
            return;
        if (_selected < _top)
            _top = _selected;
        else if (_selected >= _top + VisibleRows)
            _top = _selected - VisibleRows + 1;
    }

    /// <summary>The MORE line: the next page of actions, back to the first after the last.</summary>
    private void Page() => _top = _top + VisibleRows >= Rows.Length ? 0 : Math.Min(_top + VisibleRows, Rows.Length - VisibleRows);

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
        if (Scrolls && click.Y / 8 == MoreRow)
        {
            Page();
            return null;
        }
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
        KeepSelectionVisible();
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
            if (RowOf(i) is not { } row)
                continue;
            Chrome.Text(f, font, 2, row, Rows[i].Label, c);
            Chrome.Text(f, font, 16, row, Fit(PromptLabels.For(b, Rows[i].Action, InputDevice.Keyboard), 9), Chrome.Green);
            Chrome.Text(f, font, 26, row, Fit(PromptLabels.For(b, Rows[i].Action, InputDevice.Gamepad), 12), Chrome.Green);
        }
        for (int i = 0; i < Extra.Length; i++)
            if (RowOf(Rows.Length + i) is { } row)
                Chrome.Text(f, font, 2, row, Extra[i], Rows.Length + i == _selected ? Chrome.White : Chrome.Cyan);
        if (RowOf(_selected) is { } sel)
            Chrome.Text(f, font, 1, sel, ">", Chrome.Yellow);
        if (Scrolls)
            Chrome.Text(f, font, 2, MoreRow, $"MORE ({_top + 1}-{Math.Min(_top + VisibleRows, Rows.Length)} OF {Rows.Length})", Chrome.Grey);

        string help = _waiting
            ? $"PRESS A KEY OR PAD BUTTON FOR {Rows[_selected].Label}"
            : input.LastDevice switch
            {
                InputDevice.Gamepad => $"{PromptLabels.For(b, GameAction.Confirm, InputDevice.Gamepad)} CHOOSE  {PromptLabels.For(b, GameAction.Cancel, InputDevice.Gamepad)} BACK",
                InputDevice.Mouse => "CLICK TO CHOOSE  RIGHT CLICK: BACK",
                _ => $"{PromptLabels.For(b, GameAction.Confirm, InputDevice.Keyboard)} CHOOSE  {PromptLabels.For(b, GameAction.Cancel, InputDevice.Keyboard)} BACK",
            };
        Chrome.Text(f, font, 2, 22, Fit(help, 37), Chrome.Grey);
        if (_note.Length > 0)
            Chrome.Text(f, font, 2, 23, Fit(_note, 37), Chrome.Yellow);
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
