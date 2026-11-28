using GoldBox.Client.Input;

namespace GoldBox.Client.Screens;

/// <summary>
/// The list routine GAME.OVR 0x2D669 whole, with its helpers and the key layer of the menu routine 0x2CC8C under it (read
/// whole s60, run under emulation by <c>tools/scripts/por_listwin_oracle.py</c>; s60-lane-T.md "The list window"). The
/// pages draw and step through <see cref="ListWindowSteps"/>; this class is the routine as one loop, the reference the
/// pages' keys and words are checked against (<c>ListWindowProgramTests</c>).
/// <list type="bullet">
/// <item>0x2C936: the entries counted; 0x2C8CF: entry k (nil past the list).</item>
/// <item>Entry (0x2D6B9–0x2D7B6): the menu cell DS:6B2B := 1; the top DS:6B29 down to the position when above it, to 0
/// when past the count (either sets the caller's redraw flag); the position + 1, one row up (0x2D594) and past labels
/// upwards (0x2D3D2); the rows drawn (0x2D073) when the flag is set, then the flag cleared.</item>
/// <item>0x2D073 (rows from the top): the window's inside blanked (START 0x4047), then each entry from the top on its row
/// (START 0x76B6 at x0: labels c24, rows c22) with the rest of the row blanked (START 0x726F, colour 0, spaces) until
/// the list or the window ends. 0x2CFE0 (the last label at or above the top) is called but its result is not used.</item>
/// <item>Each pass: the highlight (0x2D238: the entry's text from its first to its last non-space character,
/// 0x2D1A6 / 0x2D1F2, at its own column in c20), the menu line (the caller's word, " Next" when count − rows &gt; top,
/// " Prev" when top &gt; the leading labels, " Exit" with the flag) through 0x2CC8C (keypad flag 1, no animation),
/// then the row unlit (0x2D2D7: the same text in c24 / c22).</item>
/// <item>Keys: extended Home / End (keypad 7 / 1) a row up / down (0x2D594), PgUp / PgDn (keypad 9 / 3) a page while
/// Prev / Next shows (0x2D502); plain P / N a page whatever shows (they reach the routine only as hotkeys of the menu
/// line); 0 (Escape) or E: no entry, key 0; any other key: the entry at the position and the key.</item>
/// </list>
/// </summary>
public sealed class ListWindowRoutine
{
    public ListWindowRoutine(IReadOnlyList<string> rows, ISet<int> labels, (int X0, int Y0, int X1, int Y1) window,
        (byte Label, byte Row, byte Lit) colours, bool exit, string word)
    {
        Rows = rows;
        Labels = labels;
        Window = window;
        Colours = colours;
        Exit = exit;
        Word = word;
        int l = 0;
        while (l < rows.Count && labels.Contains(l))
            l++;
        LeadingLabels = l;
    }

    public IReadOnlyList<string> Rows { get; }
    public ISet<int> Labels { get; }
    public (int X0, int Y0, int X1, int Y1) Window { get; }
    public (byte Label, byte Row, byte Lit) Colours { get; }
    public bool Exit { get; }
    public string Word { get; }

    /// <summary>The entries at the head with the label flag (0x2D6FF–0x2D72C).</summary>
    public int LeadingLabels { get; }

    public int WindowRows => Window.Y1 - Window.Y0 + 1;
    public int Width => Window.X1 - Window.X0 + 1;
    public int Top { get; private set; }
    public int Position { get; private set; }

    /// <summary>A text draw: 's' a string (column, row, colour, text), 'g' a glyph run (column, row, colour, count,
    /// character), 'b' the window's inside blanked (x0, y0, x1 in Colour, y1 in Count).</summary>
    public sealed record Draw(char Kind, int Col, int Row, int Colour, string Text = "", int Count = 0, int Char = 0);

    public List<Draw> Draws { get; } = [];

    /// <summary>The routine's entry. Returns false for an empty list (0x2D69C: position 0, no entry, nothing drawn).</summary>
    public bool Enter(int top, int position, bool redraw, ref int cell)
    {
        if (Rows.Count == 0)
        {
            Position = 0;
            Top = top;
            return false;
        }
        cell = 1;
        if (top > position)
        {
            top = position;
            redraw = true;
        }
        if (top > Rows.Count)
        {
            top = 0;
            redraw = true;
        }
        Top = top;
        Position = position + 1;
        StepRow(down: false);
        if (redraw)
            DrawRows();
        return true;
    }

    /// <summary>The menu line of this pass (0x2D7C8–0x2D889).</summary>
    public string MenuLine()
    {
        var (next, prev) = ListWindowSteps.Words(Top, WindowRows, Rows.Count, LeadingLabels);
        return Word + (next ? " Next" : "") + (prev ? " Prev" : "") + (Exit ? " Exit" : "");
    }

    /// <summary>What a key the menu routine returned does: null = the loop goes on; else the entry (−1 nil) and the key.</summary>
    public (int Entry, byte Key)? Key(byte key, bool ext)
    {
        var (next, prev) = ListWindowSteps.Words(Top, WindowRows, Rows.Count, LeadingLabels);
        if (ext)
        {
            switch (key)
            {
                case 0x47: StepRow(down: false); break;
                case 0x4F: StepRow(down: true); break;
                case 0x49 when prev: PageWindow(down: false); break;
                case 0x51 when next: PageWindow(down: true); break;
            }
            return null;
        }
        switch (key)
        {
            case (byte)'P': PageWindow(down: false); return null;
            case (byte)'N': PageWindow(down: true); return null;
            case 0x1B or (byte)'E' or 0: return (-1, 0);
            default: return (Position >= 0 && Position < Rows.Count ? Position : -1, key);
        }
    }

    /// <summary>0x2D238: the highlighted row; 0x2D2D7: the row unlit.</summary>
    public void DrawLit(bool lit)
    {
        if (Position < 0 || Position >= Rows.Count)
            return; // nil: the program reads the interrupt table (not modelled)
        var (col, text) = Trimmed(Rows[Position]);
        byte colour = lit ? Colours.Lit : Labels.Contains(Position) ? Colours.Label : Colours.Row;
        Draws.Add(new Draw('s', (Window.X0 + col - 1) & 0xFF, (Window.Y0 + Position - Top) & 0xFF, colour, text));
    }

    /// <summary>
    /// 0x2D1A6 / 0x2D1F2 and the Copy between them: the first non-space index (from 1, stopping at the length) and the
    /// last (down to 1); the count a byte (an all-space text gives its last character).
    /// </summary>
    public static (int First, string Text) Trimmed(string s)
    {
        int len = s.Length, first = 1;
        while (first < len && s[first - 1] == ' ')
            first++;
        int last = len;
        while (last > 1 && s[last - 1] == ' ')
            last--;
        int count = (last - first + 1) & 0xFF;
        if (first > len)
            return (first, "");
        return (first, s.Substring(first - 1, Math.Min(count, len - first + 1)));
    }

    private void StepRow(bool down)
    {
        Position = ListWindowSteps.Step(Position, Top, WindowRows, Rows.Count, down);
        Position = ListWindowSteps.SkipLabels(Position, Top, WindowRows, Rows.Count, down, Labels.Contains);
    }

    private void PageWindow(bool down)
    {
        (Position, Top) = ListWindowSteps.Page(Position, Top, WindowRows, Rows.Count, down);
        DrawRows();
        Position = ListWindowSteps.SkipLabels(Position, Top, WindowRows, Rows.Count, down, Labels.Contains);
    }

    /// <summary>0x2D073 from the window's top.</summary>
    private void DrawRows()
    {
        Draws.Add(new Draw('b', Window.X0, Window.Y0, Window.X1, Count: Window.Y1));
        int row = Window.Y0;
        for (int i = Top; i >= 0 && i < Rows.Count && row <= Window.Y1; i++, row++)
        {
            string t = Rows[i];
            Draws.Add(new Draw('s', Window.X0, row, Labels.Contains(i) ? Colours.Label : Colours.Row, t));
            if (t.Length < Width)
                Draws.Add(new Draw('g', Window.X0 + t.Length, row, 0, Count: Width - t.Length, Char: ' '));
        }
    }

    // ---- The menu routine 0x2CC8C's key layer ------------------------------------------------------------------------

    /// <summary>The menu line's words (0x2C99A): a digit or capital letter starts a word; the word count.</summary>
    public static int WordCount(string menu)
    {
        int n = 0;
        foreach (char c in menu)
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z')
                n++;
        return Math.Max(1, n);
    }

    /// <summary>DS:288C: the keypad flag's digits 1–9 as the cursor pad's codes (5 gives a space).</summary>
    public static readonly byte[] KeypadCodes = [0x4F, 0x50, 0x51, 0x4B, 0x20, 0x4D, 0x47, 0x48, 0x49];

    /// <summary>
    /// One key through the menu routine's loop (0x2CE35–0x2CF9E): <paramref name="ch"/> the character ReadKey gives
    /// (0 for an extended key, whose <paramref name="scan"/> follows). Null: the loop waits on (an extended key without
    /// the keypad flag, Return without its flag, ',' / '.' moving the cell, a character that is no hotkey). Else the
    /// byte returned and the extended flag. Escape returns 0. A space ends the menu; a digit or letter (upper-cased)
    /// that the menu line holds lights its word (the cell) and ends it; with the keypad flag the digits 1–9 and '\'
    /// then become the cursor pad's codes, extended (DS:288C; '\' 0x37).
    /// </summary>
    public static (byte Key, bool Ext)? MenuKey(byte ch, byte scan, string menu, bool keypad, bool enterGives13, ref int cell)
    {
        int words = WordCount(menu);
        if (cell > words)
            cell = 1;
        if (ch == 0)
            return keypad ? (scan, true) : null;
        if (ch == 0x1B)
            return (0, false);
        if (ch == 13)
            return enterGives13 ? (13, false) : null;
        if (ch == ',')
        {
            cell = cell == 1 ? words : cell - 1;
            return null;
        }
        if (ch == '.')
        {
            cell = cell == words ? 1 : cell + 1;
            return null;
        }
        byte k = ch is >= (byte)'a' and <= (byte)'z' ? (byte)(ch - 32) : ch;
        bool done = false;
        if (k is (byte)' ' or >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'Z')
        {
            if (k == ' ')
                done = true;
            else
            {
                int w = 0;
                for (int i = 0; i < menu.Length; i++)
                {
                    char c = menu[i];
                    if (c is >= '0' and <= '9' or >= 'A' and <= 'Z')
                        w++;
                    if (c == k)
                    {
                        done = true;
                        cell = w;
                    }
                }
            }
        }
        if (keypad && (k is >= (byte)'1' and <= (byte)'9' || k == (byte)'\\'))
            return (k == '\\' ? (byte)0x37 : KeypadCodes[k - '1'], true);
        return done ? (k, false) : null;
    }

    /// <summary>The cursor pad's codes the list routine acts on (Home, End, PgUp, PgDn).</summary>
    public const byte HomeCode = 0x47, EndCode = 0x4F, PgUpCode = 0x49, PgDnCode = 0x51;

    /// <summary>
    /// The digits' row as the list routine's menu reads it with the keypad flag (DS:288C): 7 / 1 / 9 / 3 give Home / End /
    /// PgUp / PgDn's codes, which 0x2D669 acts on (a row up / down, a page while Prev / Next shows). The keypad's own
    /// digits come as the step bindings (NumPad7 …). The Client gives these keys no meaning on a list, so they act as
    /// the program (the s50 / s53 rule). Null for any other key, and with Shift (Shift-7 types '&amp;').
    /// </summary>
    public static byte? DigitRowKey(ActionInput input) =>
        input.NewInput is (InputDevice.Keyboard, { Length: 2 } name) && name[0] == 'D' && name[1] is '1' or '3' or '7' or '9'
            && !input.NewShiftHeld
            ? KeypadCodes[name[1] - '1']
            : null;
}
