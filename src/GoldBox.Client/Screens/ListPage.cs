using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>
/// A full-screen <see cref="ListRequest"/> page (item lists, the character view): the fixed lines from the top, the
/// rows to pick from, the actions on the bottom line as the original's menus (hotkey letters), and a prompt line for
/// the device in use. Keyboard: arrows pick rows and actions, Enter, Esc, the action letters, Home / End for the
/// member. Pad: D-pad / stick, A, B, LB / RB. Mouse: point and click rows and actions, right click backs out, the
/// prompt line's PREV / NEXT switch the member. With <see cref="ListRequest.Paging"/> the rows are the list routine
/// 0x2D669's window, moved by its Next / Prev words (<see cref="ListPaging"/>).
/// </summary>
public sealed class ListPage
{
    public const int ActionRow = 24;

    /// <summary>Label rows in the vertical menu's prompt colour (light magenta, 13).</summary>
    private const byte LabelColour = 13;

    /// <summary>Where each action word goes: the bottom line, or (when they do not fit in 40 columns) two lines, 23 and 24.</summary>
    private static List<(int Col, int Row)> ActionCells(IReadOnlyList<string> actions, int start = 0)
    {
        var cells = new List<(int, int)>();
        if (start > 0)
        {
            // the spell list's menu line: one row after its prompt (0x2CC8C draws it as one string)
            int c = start;
            foreach (var a in actions)
            {
                cells.Add((c, ActionRow));
                c += a.Length + 1;
            }
            return cells;
        }
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

    private static bool TwoLines(ListRequest r) => r.Spell is null && ActionCells(Menu(r, r.Top).Words) is var c && c.Count > 0 && c[0].Row != ActionRow;
    private static int HintRow(ListRequest r) => TwoLines(r) ? ActionRow - 2 : ActionRow - 1;
    private static int LastListRow(ListRequest r) => HintRow(r) - 1;

    private ListRequest? _shown;
    private int _row, _top, _action;
    private ListAnswer? _clicked;

    public int SelectedRow => _row;

    /// <summary>The highlighted menu entry, counted on the words shown (with paging, Next / Prev included).</summary>
    public int SelectedAction => _action;

    /// <summary>The window's first row (DS:6B29 with paging).</summary>
    public int Top => _top;

    /// <summary>The words on the menu line now (with paging, Next / Prev where they apply).</summary>
    public IReadOnlyList<string> ShownActions => _words;

    // ---- The list routine 0x2D669's window (ListRequest.Paging; P, s30) --------------------------------------------

    /// <summary>The words on the menu line now: the actions, with Next / Prev put in while they apply (0x2D7DC–0x2D889).</summary>
    private List<string> _words = [];

    /// <summary>For each word shown, the request's action index, or <see cref="NextWord"/> / <see cref="PrevWord"/>.</summary>
    private List<int> _map = [];

    private const int NextWord = -1, PrevWord = -2;

    /// <summary>The menu line for a window at <paramref name="top"/>: Next when count − rows &gt; top, Prev when top &gt; the leading labels.</summary>
    private static (List<string> Words, List<int> Map) Menu(ListRequest r, int top)
    {
        var words = new List<string>();
        var map = new List<int>();
        var p = r.Paging;
        int at = p is null ? r.Actions.Count : Math.Clamp(p.WordsAt, 0, r.Actions.Count);
        for (int i = 0; i <= r.Actions.Count; i++)
        {
            if (i == at && p is not null)
            {
                if (r.Rows.Count - p.Rows > top)
                {
                    words.Add(p.Next);
                    map.Add(NextWord);
                }
                if (top > LeadingLabels(r))
                {
                    words.Add(p.Prev);
                    map.Add(PrevWord);
                }
            }
            if (i < r.Actions.Count)
            {
                words.Add(r.Actions[i]);
                map.Add(i);
            }
        }
        return (words, map);
    }

    /// <summary>The labels before the first pickable row (0x2D6FF: the entries with +0x29 ≠ 0 from the list's head).</summary>
    private static int LeadingLabels(ListRequest r)
    {
        int n = 0;
        while (n < r.Rows.Count && r.Labels.Contains(n))
            n++;
        return n;
    }

    private void Remenu(ListRequest r)
    {
        (_words, _map) = Menu(r, _top);
        if (_action >= _words.Count)
            _action = 0; // 0x2CCE5: a cell past the entries goes back to the first
    }

    /// <summary>0x2D594: the highlight one row down (up), wrapping inside the window, then past labels (0x2D3D2).</summary>
    private void Step(ListRequest r, bool down)
    {
        Move(r, down);
        SkipWindowLabels(r, down);
    }

    private void Move(ListRequest r, bool down)
    {
        int rows = r.Paging!.Rows, n = r.Rows.Count;
        if (down)
        {
            _row++;
            if (_top + rows - 1 < _row) _row = _top;
            if (n - 1 < _row) _row = _top;
        }
        else
        {
            _row--;
            if (_row < _top) _row = _top + rows - 1;
            if (n - 1 < _row) _row = n - 1;
        }
    }

    /// <summary>0x2D3D2: past label rows in the direction, at most a window's rows, with the same wrapping.</summary>
    private void SkipWindowLabels(ListRequest r, bool down)
    {
        for (int k = 0; k < r.Paging!.Rows && r.Labels.Contains(_row); k++)
            Move(r, down);
    }

    /// <summary>0x2D502: the window a page down (up), the highlight at the same place in it, then past labels.</summary>
    private void Page(ListRequest r, bool down)
    {
        int rows = r.Paging!.Rows, n = r.Rows.Count, offset = _row - _top;
        if (down)
        {
            _top += rows;
            if (n - rows < _top) _top = n - rows;
        }
        else
        {
            _top -= rows;
            if (_top < 0) _top = 0;
        }
        _row = _top + offset;
        SkipWindowLabels(r, down);
        Remenu(r);
    }

    /// <summary>A new page starts on its first row (or the one it asks for) and its first action.</summary>
    public void Sync(ListRequest r)
    {
        if (ReferenceEquals(r, _shown))
            return;
        _shown = r;
        _clicked = null;
        if (r.Paging is not null)
        {
            // 0x2D766–0x2D7A1: the top no further down than the highlight and not past the list; then the highlight one
            // row down and up again (inside the window) and past labels upwards.
            _row = Math.Max(0, r.Selected);
            _top = Math.Max(0, r.Top);
            if (_top > _row) _top = _row;
            if (_top > r.Rows.Count) _top = 0;
            if (r.Rows.Count > 0)
            {
                _row++;
                Step(r, down: false);
            }
            _row = Math.Clamp(_row, 0, Math.Max(0, r.Rows.Count - 1));
            _action = Math.Max(0, r.SelectedAction);
            Remenu(r);
            return;
        }
        _row = Math.Clamp(r.Selected, 0, Math.Max(0, r.Rows.Count - 1));
        _row = SkipLabels(r, _row, 1);
        _action = Math.Clamp(r.SelectedAction, 0, Math.Max(0, r.Actions.Count - 1));
        _top = 0;
        Scroll(r);
        Remenu(r);
    }

    /// <summary>The highlighted action of the page on screen (the shared menu highlight, set by the screen).</summary>
    public void SetAction(ListRequest r, int action)
    {
        Sync(r);
        _action = Math.Clamp(action, 0, Math.Max(0, _words.Count - 1));
    }

    private static int FirstRow(ListRequest r) => r.Paging is not null ? r.FirstRow : Math.Max(r.FirstRow, r.Lines.Count + 1);

    private static int Visible(ListRequest r) => r.Paging is { } p
        ? Math.Max(1, p.Rows)
        : Math.Max(1, Math.Min(r.VisibleRows, LastListRow(r) - FirstRow(r) + 1));

    private void Scroll(ListRequest r)
    {
        if (r.Paging is not null)
            return; // the window moves by Next / Prev only
        int v = Visible(r);
        if (_row < _top)
            _top = _row;
        if (_row >= _top + v)
            _top = _row - v + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, r.Rows.Count - v));
    }

    private static bool Usable(ListRequest r, int action) => Pickable(r) || !r.RowActions.Contains(action);

    /// <summary>A shown word can be chosen: the page words always, an action that needs a row only with one.</summary>
    private bool UsableWord(ListRequest r, int word) => word >= 0 && word < _map.Count && (_map[word] < 0 || Usable(r, _map[word]));

    /// <summary>Runs shown word <paramref name="word"/>: Next / Prev page here (null), any other answers the request.</summary>
    private ListAnswer? Choose(ListRequest r, int word)
    {
        _action = word;
        switch (_map[word])
        {
            case NextWord:
                Page(r, down: true);
                return null;
            case PrevWord:
                Page(r, down: false);
                return null;
            default:
                return Answer(_map[word], _row);
        }
    }

    /// <summary>
    /// The page keys of 0x2D669's window, each only while its word is shown: PgDn / PgUp (keypad 3 / 9; the pad's right
    /// stick, as their step bindings), and on the pad the shoulders / triggers (no original; no paging list picks a member).
    /// </summary>
    private bool PageKeys(ListRequest r, ActionInput input)
    {
        bool pad = input.LastDevice == InputDevice.Gamepad;
        bool down = input.Pressed(GameAction.StepDownRight) || (pad && input.Pressed(GameAction.NextMember));
        bool up = input.Pressed(GameAction.StepUpRight) || (pad && input.Pressed(GameAction.PreviousMember));
        if (down && _map.Contains(NextWord))
        {
            Page(r, down: true);
            return true;
        }
        if (up && _map.Contains(PrevWord))
        {
            Page(r, down: false);
            return true;
        }
        return false;
    }

    /// <summary>An answer; with paging it carries the window's top for the caller to keep.</summary>
    private ListAnswer Answer(int action, int row) => new(action, row, _shown?.Paging is null ? 0 : _top);

    /// <summary>The list has a row that is not a label.</summary>
    private static bool Pickable(ListRequest r) => r.Rows.Count > r.Labels.Count;

    /// <summary>The first row from <paramref name="row"/> on (going <paramref name="step"/>, wrapping) that is not a label.</summary>
    private static int SkipLabels(ListRequest r, int row, int step)
    {
        int n = r.Rows.Count;
        if (n == 0 || !Pickable(r))
            return Math.Clamp(row, 0, Math.Max(0, n - 1));
        for (int k = 0; k < n && r.Labels.Contains(row); k++)
            row = ((row + step) % n + n) % n;
        return row;
    }

    /// <summary>This frame's answer, or null.</summary>
    public ListAnswer? Update(ListRequest r, ActionInput input)
    {
        Sync(r);
        if (_clicked is { } c)
        {
            _clicked = null;
            return c;
        }
        int n = r.Rows.Count, m = _words.Count;
        if (r.Paging is not null)
        {
            // 0x2D669: Home / End (keypad 7 / 1) move the highlight; the Client's Up / Down do the same. On the keyboard
            // only (the pad's right stick turned to those diagonals pages, PageKeys); a list that picks a member keeps them.
            bool keys = input.LastDevice == InputDevice.Keyboard && !r.PartySelect;
            if ((input.Pressed(GameAction.MenuUp) || (keys && input.Pressed(GameAction.StepUpLeft))) && n > 0) Step(r, down: false);
            if ((input.Pressed(GameAction.MenuDown) || (keys && input.Pressed(GameAction.StepDownLeft))) && n > 0) Step(r, down: true);
        }
        else
        {
            if (input.Pressed(GameAction.MenuUp) && n > 0) _row = SkipLabels(r, (_row + n - 1) % n, -1);
            if (input.Pressed(GameAction.MenuDown) && n > 0) _row = SkipLabels(r, (_row + 1) % n, 1);
        }
        if (m > 0 && input.Pressed(GameAction.MenuLeft)) _action = (_action + m - 1) % m;
        if (m > 0 && input.Pressed(GameAction.MenuRight)) _action = (_action + 1) % m;
        Scroll(r);
        if (r.PartySelect && input.Pressed(GameAction.PreviousMember))
            return Answer(ChoiceRequest.PreviousMember, _row);
        if (r.PartySelect && input.Pressed(GameAction.NextMember))
            return Answer(ChoiceRequest.NextMember, _row);
        if (r.Paging is not null && PageKeys(r, input))
            return null;
        if (input.Pressed(GameAction.Confirm) && input.LastDevice != InputDevice.Mouse && r.Paging is not null && _action < _map.Count && _map[_action] < 0)
        {
            // The program's Enter returns 13 with the highlighted row (0x2D669: any key but the page words' hotkeys,
            // PgUp / PgDn, Home / End and Escape / E / 0), which the callers take as the pick (CS:004A): Enter never
            // runs a lit Next / Prev. Decided (user, s30): match it; the pad pages with its shoulders.
            int first = _map.FindIndex(a => a >= 0);
            return first >= 0 && UsableWord(r, first) ? Choose(r, first) : null;
        }
        if (input.Pressed(GameAction.Confirm) && UsableWord(r, _action))
            return Choose(r, _action);
        if (input.Pressed(GameAction.Cancel) && r.CancelAction is { } ca)
            return Answer(ca, _row);
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < m; i++)
                if (_words[i].Length > 0 && _words[i][0] == key[0] && UsableWord(r, i))
                    return Choose(r, i);
        return null;
    }

    /// <summary>Mouse: rows and actions under the pointer are highlighted.</summary>
    public void Hover(ListRequest r, int col, int row)
    {
        Sync(r);
        if (RowAt(r, row) is { } i && !r.Labels.Contains(i))
            _row = i;
        else if (ActionAt(col, row) is { } a)
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
            if (r.Labels.Contains(i))
                return null;
            if (i == _row && Usable(r, 0))
                _clicked = Answer(0, i);
            _row = i;
            return null;
        }
        if (ActionAt(col, row) is { } a && UsableWord(r, a))
        {
            _clicked = Choose(r, a);
            return null;
        }
        if (row == HintRow(r) && r.PartySelect)
        {
            if (col < 6)
                _clicked = Answer(ChoiceRequest.PreviousMember, _row);
            else if (col >= 7 && col < 13)
                _clicked = Answer(ChoiceRequest.NextMember, _row);
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

    private int? ActionAt(int col, int row)
    {
        var cells = ActionCells(_words, _shown?.Spell?.MenuStart ?? 0);
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].Row == row && col >= cells[i].Col && col < cells[i].Col + _words[i].Length)
                return i;
        return null;
    }

    /// <param name="highlight">False for a page left under something else: the list routine 0x2D669 draws its rows
    /// again without the highlight when it returns (D s33: every row green under Memorize's slot message, the keep
    /// question, the Items page's handler text).</param>
    public void Draw(IndexedFrame f, TextFont font, ListRequest r, ActionInput input, bool highlight = true)
    {
        Sync(r);
        if (r.Spell is { } spell && r.Paging is { } paging)
        {
            DrawSpellPage(f, font, r, spell, paging, input, highlight);
            return;
        }
        f.Clear(0);
        int linesEnd = r.Paging is not null ? FirstRow(r) - 1 : LastListRow(r);
        for (int i = 0; i < r.Lines.Count && i <= linesEnd; i++)
        {
            var l = r.Lines[i];
            Chrome.Text(f, font, l.Column, i, Fit(l.Text, 40 - l.Column), l.Colour);
        }
        int first = FirstRow(r), v = Visible(r);
        for (int k = 0; k < v && _top + k < r.Rows.Count; k++)
        {
            int i = _top + k;
            if (r.Labels.Contains(i))
            {
                Chrome.Text(f, font, 1, first + k, Fit(r.Rows[i], 38), LabelColour);
                continue;
            }
            bool sel = i == _row && highlight;
            Chrome.Text(f, font, 1, first + k, Fit(r.Rows[i], 38), sel ? Chrome.White : Chrome.Green);
            if (sel)
                Chrome.Text(f, font, 0, first + k, ">", Chrome.Yellow);
        }
        if (r.Paging is null && _top > 0)
            Chrome.Text(f, font, 39, first, "^", Chrome.Yellow);
        if (r.Paging is null && _top + v < r.Rows.Count)
            Chrome.Text(f, font, 39, first + v - 1, "V", Chrome.Yellow);

        var cells = ActionCells(_words);
        for (int i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            if (w.Length == 0)
                continue;
            var (col, row) = cells[i];
            bool usable = UsableWord(r, i);
            byte rest = !usable ? Chrome.Grey : i == _action ? Chrome.White : Chrome.Green;
            Chrome.Text(f, font, col, row, w[..1], usable ? Chrome.White : Chrome.Grey);
            Chrome.Text(f, font, col + 1, row, w[1..], rest);
        }
        Chrome.Text(f, font, 0, HintRow(r), Fit(Hint(r, input), 40), Chrome.Grey);
    }

    /// <summary>
    /// The spell list's screen (<see cref="SpellPageLayout"/>; P + D, s33): the title window (1, 1)–(38, 1), the list
    /// window (1, 3)–(38, last row), Memorize's text box (1, 17)–(38, 22), each framed one cell outside as START 03F1:02E8
    /// and 03F1:0000 draw them, in the program's order; the rows (labels 13, the highlight white, the rest green, no
    /// cursor); row 24 the prompt (13) and the words from <see cref="SpellPageLayout.MenuStart"/>. The Client's device
    /// hint goes on row 23 for the pad and the mouse only, as on the fight's pages.
    /// </summary>
    private void DrawSpellPage(IndexedFrame f, TextFont font, ListRequest r, SpellPageLayout s, ListPaging p, ActionInput input,
        bool highlight)
    {
        f.Clear(0);
        int first = FirstRow(r), last = first + p.Rows - 1;
        Chrome.Box(f, 0, 0, 39, 2);
        Chrome.Box(f, 0, 2, 39, last + 1);
        Chrome.Text(f, font, 1, 1, Fit(s.Name + "'S", 38), s.NameColour);
        int titleCol = 1 + s.Name.Length + 3;
        if (titleCol < 39)
            Chrome.Text(f, font, titleCol, 1, Fit(s.Title, 39 - titleCol), Chrome.Green);
        if (s.Box is { } box)
        {
            Chrome.Box(f, 0, 16, 39, 23);
            foreach (var l in box)
                Chrome.Text(f, font, l.Column, l.Row, Fit(l.Text, Math.Max(0, 39 - l.Column)), l.Colour);
        }
        for (int k = 0; k < p.Rows && _top + k < r.Rows.Count; k++)
        {
            int i = _top + k;
            byte colour = r.Labels.Contains(i) ? LabelColour : i == _row && highlight ? Chrome.White : Chrome.Green;
            Chrome.Text(f, font, 1, first + k, Fit(r.Rows[i], 38), colour);
        }
        if (s.Prompt is { Length: > 0 } prompt)
            Chrome.Text(f, font, 0, ActionRow, Fit(prompt, 40), LabelColour);
        var cells = ActionCells(_words, s.MenuStart);
        for (int i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            var (col, row) = cells[i];
            if (w.Length == 0 || col + w.Length > 40)
                continue;
            bool usable = UsableWord(r, i);
            byte rest = !usable ? Chrome.Grey : i == _action ? Chrome.White : Chrome.Green;
            Chrome.Text(f, font, col, row, w[..1], usable ? Chrome.White : Chrome.Grey);
            Chrome.Text(f, font, col + 1, row, w[1..], rest);
        }
        if (input.LastDevice != InputDevice.Keyboard)
            Chrome.Text(f, font, 0, ActionRow - 1, Fit(Hint(r, input), 40), Chrome.Grey);
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
        else if (r.Paging is not null && d == InputDevice.Gamepad)
            s += $" {L(GameAction.PreviousMember)}/{L(GameAction.NextMember)} PAGE";
        return s;
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
