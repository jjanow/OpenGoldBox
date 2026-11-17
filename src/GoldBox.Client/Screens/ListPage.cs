using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>
/// A full-screen <see cref="ListRequest"/> page (item lists, the character view): the fixed lines from the top, the
/// rows to pick from, the actions on the bottom line as the original's menus (hotkey letters), and a prompt line for
/// the device in use. Keyboard: arrows pick rows and actions, Enter, Esc, the action letters, Home / End for the
/// member. Pad: D-pad / stick, A, B, LB / RB. Mouse: point and click rows and actions, right click backs out, the
/// prompt line's PREV / NEXT switch the member.
/// </summary>
public sealed class ListPage
{
    public const int ActionRow = 24;

    /// <summary>Where each action word goes: the bottom line, or (when they do not fit in 40 columns) two lines, 23 and 24.</summary>
    private static List<(int Col, int Row)> ActionCells(IReadOnlyList<string> actions)
    {
        var cells = new List<(int, int)>();
        bool two = actions.Sum(a => a.Length + 1) - 1 > 40;
        int row = two ? ActionRow - 1 : ActionRow, col = 0;
        foreach (var a in actions)
        {
            if (col + a.Length > 40)
            {
                row = ActionRow;
                col = 0;
            }
            cells.Add((col, row));
            col += a.Length + 1;
        }
        return cells;
    }

    private static bool TwoLines(ListRequest r) => ActionCells(r.Actions) is var c && c.Count > 0 && c[0].Row != ActionRow;
    private static int HintRow(ListRequest r) => TwoLines(r) ? ActionRow - 2 : ActionRow - 1;
    private static int LastListRow(ListRequest r) => HintRow(r) - 1;

    private ListRequest? _shown;
    private int _row, _top, _action;
    private ListAnswer? _clicked;

    public int SelectedRow => _row;
    public int SelectedAction => _action;

    /// <summary>A new page starts on its first row (or the one it asks for) and its first action.</summary>
    public void Sync(ListRequest r)
    {
        if (ReferenceEquals(r, _shown))
            return;
        _shown = r;
        _row = Math.Clamp(r.Selected, 0, Math.Max(0, r.Rows.Count - 1));
        _action = Math.Clamp(r.SelectedAction, 0, Math.Max(0, r.Actions.Count - 1));
        _top = 0;
        _clicked = null;
        Scroll(r);
    }

    private static int FirstRow(ListRequest r) => Math.Max(r.FirstRow, r.Lines.Count + 1);

    private static int Visible(ListRequest r) => Math.Max(1, Math.Min(r.VisibleRows, LastListRow(r) - FirstRow(r) + 1));

    private void Scroll(ListRequest r)
    {
        int v = Visible(r);
        if (_row < _top)
            _top = _row;
        if (_row >= _top + v)
            _top = _row - v + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, r.Rows.Count - v));
    }

    private bool Usable(ListRequest r, int action) => r.Rows.Count > 0 || !r.RowActions.Contains(action);

    /// <summary>This frame's answer, or null.</summary>
    public ListAnswer? Update(ListRequest r, ActionInput input)
    {
        Sync(r);
        if (_clicked is { } c)
        {
            _clicked = null;
            return c;
        }
        int n = r.Rows.Count, m = r.Actions.Count;
        if (input.Pressed(GameAction.MenuUp) && n > 0) _row = (_row + n - 1) % n;
        if (input.Pressed(GameAction.MenuDown) && n > 0) _row = (_row + 1) % n;
        if (input.Pressed(GameAction.MenuLeft)) _action = (_action + m - 1) % m;
        if (input.Pressed(GameAction.MenuRight)) _action = (_action + 1) % m;
        Scroll(r);
        if (r.PartySelect && input.Pressed(GameAction.PreviousMember))
            return new ListAnswer(ChoiceRequest.PreviousMember, _row);
        if (r.PartySelect && input.Pressed(GameAction.NextMember))
            return new ListAnswer(ChoiceRequest.NextMember, _row);
        if (input.Pressed(GameAction.Confirm) && Usable(r, _action))
            return new ListAnswer(_action, _row);
        if (input.Pressed(GameAction.Cancel) && r.CancelAction is { } ca)
            return new ListAnswer(ca, _row);
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < m; i++)
                if (r.Actions[i].Length > 0 && r.Actions[i][0] == key[0] && Usable(r, i))
                    return new ListAnswer(i, _row);
        return null;
    }

    /// <summary>Mouse: rows and actions under the pointer are highlighted.</summary>
    public void Hover(ListRequest r, int col, int row)
    {
        Sync(r);
        if (RowAt(r, row) is { } i)
            _row = i;
        else if (ActionAt(r, col, row) is { } a)
            _action = a;
    }

    /// <summary>Mouse: a click on a row picks it with the first action, on an action runs it; right click backs out.</summary>
    public GameAction? Click(ListRequest r, ScreenClick click)
    {
        Sync(r);
        int col = click.X / 8, row = click.Y / 8;
        if (click.Right)
            return r.CancelAction is not null ? GameAction.Cancel : null;
        if (RowAt(r, row) is { } i)
        {
            if (i == _row && Usable(r, 0))
                _clicked = new ListAnswer(0, i);
            _row = i;
            return null;
        }
        if (ActionAt(r, col, row) is { } a && Usable(r, a))
        {
            _clicked = new ListAnswer(a, _row);
            return null;
        }
        if (row == HintRow(r) && r.PartySelect)
        {
            if (col < 6)
                _clicked = new ListAnswer(ChoiceRequest.PreviousMember, _row);
            else if (col >= 7 && col < 13)
                _clicked = new ListAnswer(ChoiceRequest.NextMember, _row);
        }
        return null;
    }

    /// <summary>A click this frame already chose (the screen answers it on its next update).</summary>
    public bool HasClick => _clicked is not null;

    private int? RowAt(ListRequest r, int row)
    {
        int first = FirstRow(r), v = Visible(r);
        int i = _top + row - first;
        return row >= first && row < first + v && i >= 0 && i < r.Rows.Count ? i : null;
    }

    private static int? ActionAt(ListRequest r, int col, int row)
    {
        var cells = ActionCells(r.Actions);
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].Row == row && col >= cells[i].Col && col < cells[i].Col + r.Actions[i].Length)
                return i;
        return null;
    }

    public void Draw(IndexedFrame f, TextFont font, ListRequest r, ActionInput input)
    {
        Sync(r);
        f.Clear(0);
        for (int i = 0; i < r.Lines.Count && i <= LastListRow(r); i++)
        {
            var l = r.Lines[i];
            Chrome.Text(f, font, l.Column, i, Fit(l.Text, 40 - l.Column), l.Colour);
        }
        int first = FirstRow(r), v = Visible(r);
        for (int k = 0; k < v && _top + k < r.Rows.Count; k++)
        {
            int i = _top + k;
            bool sel = i == _row;
            Chrome.Text(f, font, 1, first + k, Fit(r.Rows[i], 38), sel ? Chrome.White : Chrome.Green);
            if (sel)
                Chrome.Text(f, font, 0, first + k, ">", Chrome.Yellow);
        }
        if (_top > 0)
            Chrome.Text(f, font, 39, first, "^", Chrome.Yellow);
        if (_top + v < r.Rows.Count)
            Chrome.Text(f, font, 39, first + v - 1, "V", Chrome.Yellow);

        var cells = ActionCells(r.Actions);
        for (int i = 0; i < r.Actions.Count; i++)
        {
            var w = r.Actions[i];
            var (col, row) = cells[i];
            byte rest = !Usable(r, i) ? Chrome.Grey : i == _action ? Chrome.White : Chrome.Green;
            Chrome.Text(f, font, col, row, w[..1], Usable(r, i) ? Chrome.White : Chrome.Grey);
            Chrome.Text(f, font, col + 1, row, w[1..], rest);
        }
        Chrome.Text(f, font, 0, HintRow(r), Fit(Hint(r, input), 40), Chrome.Grey);
    }

    /// <summary>The prompt line in the device used last.</summary>
    private static string Hint(ListRequest r, ActionInput input)
    {
        var d = input.LastDevice;
        if (d == InputDevice.Mouse)
            return r.PartySelect ? "<PREV  NEXT> CLICK ROW / ACTION" : "CLICK A ROW, THEN AN ACTION";
        var b = input.Bindings;
        string L(GameAction a) => PromptLabels.For(b, a, d);
        string pick = d == InputDevice.Gamepad ? "DPAD" : "ARROWS";
        string s = $"{pick} {L(GameAction.Confirm)} OK {L(GameAction.Cancel)} BACK";
        if (r.PartySelect)
            s += $" {L(GameAction.PreviousMember)}/{L(GameAction.NextMember)} WHO";
        return s;
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
