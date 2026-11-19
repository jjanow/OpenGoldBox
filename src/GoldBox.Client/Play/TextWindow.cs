namespace GoldBox.Client.Play;

/// <summary>
/// The text window as the program prints into it (START.EXE 0x77F8, program read s12; docs/re/view3d.md "Text
/// window"): a box of screen cells (PRINT and PRINTCLR use columns 1–38, rows 17–22, colour 10) and the program's
/// text cursor (DS:5E24 column, DS:5E25 row), which stays where the last print left it.
/// <list type="bullet">
/// <item>A cursor outside the box goes to its top-left corner first; with "clear" the box is blanked and the cursor
/// goes there too.</item>
/// <item>The text is cut into words: leading delimiters (<c>! , - . : ; ?</c>), then characters up to a space or a
/// delimiter, then, when it did not stop on a space, the delimiters that follow. A word ends with the space or
/// delimiter that ended it.</item>
/// <item>A word that would run past the right edge goes to the next row (spaces at its start are skipped). Past the
/// bottom row the window does not scroll: the cursor goes back to the top, the page prompt is shown on the bottom line
/// and the program waits for a key (<see cref="Pause"/>), then the box is blanked and the word printed.</item>
/// <item>After the text, a cursor past the right edge moves to the start of the next row.</item>
/// </list>
/// Characters are drawn as the glyph routine (START 0x726F) does: upper case. While DS:4A00 is set the program types
/// the text out: after each character's glyph (spaces included) Delay(DS:49E2 × 10) ms (0x777B–0x778D); the owner
/// sets <see cref="Typed"/> for that time (PRINT / PRINTCLR, the fight's panel).
/// </summary>
public sealed class TextWindow
{
    /// <summary>The script text box of PRINT / PRINTCLR (GAME.OVR 0x174F): columns 1–38, rows 17–22.</summary>
    public const int Left = 1, Top = 17, Right = 38, Bottom = 22;

    /// <summary>The colour PRINT uses (EGA light green; the glyph routine remaps it only in CGA mode, DS:4A1E = 0).</summary>
    public const byte ScriptColour = 10;

    private readonly char[,] _chars;
    private readonly byte[,] _colours;
    private readonly int _left, _top, _right, _bottom;

    /// <summary>The script text box (<see cref="Left"/>, <see cref="Top"/>)–(<see cref="Right"/>, <see cref="Bottom"/>).</summary>
    public TextWindow() : this(Left, Top, Right, Bottom)
    {
    }

    /// <summary>
    /// Screen cells (<paramref name="left"/>, <paramref name="top"/>)–(<paramref name="right"/>,
    /// <paramref name="bottom"/>) the program prints into through windows inside them (the combat panel: columns 23–38,
    /// rows 1–21, the windows of START 0x16EC); the cursor and <see cref="Print"/>'s window start as the whole area.
    /// </summary>
    public TextWindow(int left, int top, int right, int bottom)
    {
        (_left, _top, _right, _bottom) = (left, top, right, bottom);
        _chars = new char[bottom - top + 1, right - left + 1];
        _colours = new byte[bottom - top + 1, right - left + 1];
        _overhang = new (char, byte)[bottom - top + 1];
        Column = left;
        Row = top;
    }

    /// <summary>The text cursor (screen cells; DS:5E24 column, DS:5E25 row).</summary>
    public int Column { get; private set; }
    public int Row { get; private set; }

    /// <summary>Called when the box is full and more text follows: show the page prompt and wait for a key.</summary>
    public Action? Pause { get; set; }

    /// <summary>
    /// Called after each character <see cref="PrintIn"/> draws (0x7737) while the program types (DS:4A00 set): the
    /// owner waits DS:49E2 × 10 ms there. Null = the text shows at once. <see cref="Put"/> (a fixed-cell string) does
    /// not type.
    /// </summary>
    public Action? Typed { get; set; }

    public bool IsEmpty
    {
        get
        {
            foreach (var c in _chars)
                if (c is not '\0' and not ' ')
                    return false;
            return true;
        }
    }

    /// <summary>
    /// The character and colour of a box cell (row and column in screen cells); '\0' = blank. Column right + 1 is the
    /// overhang cell <see cref="Put"/> can draw on the frame.
    /// </summary>
    public (char Char, byte Colour) Cell(int row, int col) => col == _right + 1
        ? _overhang[row - _top]
        : _chars[row - _top, col - _left] is var c && c != '\0'
            ? (c, _colours[row - _top, col - _left])
            : ('\0', (byte)0);

    /// <summary>
    /// Characters <see cref="Put"/> drew one column right of the box, over the frame (the glyph routine draws up to
    /// column 39; DOSBox-X s22: the end game's 39-character line). The box clear leaves them; <see cref="Clear"/>
    /// removes them.
    /// </summary>
    private readonly (char, byte)[] _overhang;

    /// <summary>The box rows as strings (blanks as spaces, trailing blanks cut): for tests and logs.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var list = new List<string>();
            for (int r = 0; r <= _bottom - _top; r++)
            {
                var s = new char[_right - _left + 1];
                for (int c = 0; c < s.Length; c++)
                    s[c] = _chars[r, c] == '\0' ? ' ' : _chars[r, c];
                list.Add(new string(s).TrimEnd());
            }
            return list;
        }
    }

    /// <summary>Blank the box (0x7E63) and put the cursor at its top-left corner.</summary>
    public void Clear()
    {
        Array.Clear(_chars);
        Array.Clear(_colours);
        Array.Clear(_overhang);
        Home();
    }

    /// <summary>The cursor to the box's top-left corner without blanking (EXIT: column 1, row 17).</summary>
    public void Home()
    {
        Column = _left;
        Row = _top;
    }

    /// <summary>
    /// Text at a fixed cell (the program's DisplayString at column, row, colour, as the camp's rest time and messages
    /// use it); cut at the box edge. The cursor does not move.
    /// </summary>
    public void Put(int row, int col, string text, byte colour = ScriptColour)
    {
        if (row < _top || row > _bottom)
            return;
        for (int i = 0; i < text.Length; i++)
        {
            int c = col + i;
            if (c == _right + 1 && c <= 39)
                _overhang[row - _top] = (char.ToUpperInvariant(text[i]), colour);
            if (c < _left || c > _right)
                continue;
            _chars[row - _top, c - _left] = text[i];
            _colours[row - _top, c - _left] = colour;
        }
    }

    /// <summary>Sets the text cursor (DS:5E24 column, DS:5E25 row) without drawing.</summary>
    public void MoveTo(int column, int row)
    {
        Column = column;
        Row = row;
    }

    /// <summary>The box's cells as window lines (runs of one colour per row), for a full-screen window.</summary>
    public IReadOnlyList<WindowLine> WindowLines()
    {
        var lines = new List<WindowLine>();
        for (int r = _top; r <= _bottom; r++)
        {
            int c = _left;
            while (c <= _right)
            {
                var (ch, colour) = Cell(r, c);
                if (ch == '\0')
                {
                    c++;
                    continue;
                }
                int start = c;
                var sb = new System.Text.StringBuilder();
                while (c <= _right && Cell(r, c) is var (ch2, col2) && (ch2 == '\0' || col2 == colour))
                {
                    sb.Append(ch2 == '\0' ? ' ' : ch2);
                    c++;
                }
                lines.Add(new WindowLine(start, r, sb.ToString().TrimEnd(), colour));
            }
        }
        return lines;
    }

    /// <summary>Blanks one row of the box.</summary>
    public void ClearRow(int row) => ClearRect(_left, row, _right, row);

    /// <summary>Blanks the cells (<paramref name="left"/>, <paramref name="top"/>)–(<paramref name="right"/>,
    /// <paramref name="bottom"/>) inside the area (ClearBox START 0x4047). The cursor does not move.</summary>
    public void ClearRect(int left, int top, int right, int bottom)
    {
        for (int r = Math.Max(top, _top); r <= Math.Min(bottom, _bottom); r++)
            for (int c = Math.Max(left, _left); c <= Math.Min(right, _right); c++)
            {
                _chars[r - _top, c - _left] = '\0';
                _colours[r - _top, c - _left] = 0;
            }
    }

    /// <summary>PRINTRET (GAME.OVR 0x3831): column 1 of the next row (the next print starts at the top if that is below the box).</summary>
    public void NewLine()
    {
        Column = _left;
        Row++;
    }

    /// <summary>
    /// PRINT (<paramref name="clear"/> false: from the cursor) or PRINTCLR (true: the box blanked first), in
    /// <paramref name="colour"/>.
    /// </summary>
    public void Print(string text, bool clear = false, byte colour = ScriptColour) =>
        PrintIn(_left, _top, _right, _bottom, text, clear, colour);

    /// <summary>
    /// START 0x77F8 with its window (<paramref name="left"/>, <paramref name="top"/>)–(<paramref name="right"/>,
    /// <paramref name="bottom"/>), inside this area: the same word wrap as <see cref="Print"/>.
    /// </summary>
    public void PrintIn(int left, int top, int right, int bottom, string text, bool clear, byte colour)
    {
        if (Column < left || Column > right || Row < top || Row > bottom)
        {
            Column = left;
            Row = top;
        }
        if (clear)
        {
            ClearRect(left, top, right, bottom);
            Column = left;
            Row = top;
        }
        int len = text.Length;
        if (len == 0)
            return;
        // 1-based indexes as the Pascal routine.
        char S(int i) => text[i - 1];
        int p = 1;
        do
        {
            int i = p;
            while (i < len && IsDelimiter(S(i)))
                i++;
            while (i < len && !IsDelimiter(S(i)) && S(i) != ' ')
                i++;
            if (S(i) != ' ')
                while (i + 1 < len && IsDelimiter(S(i + 1)))
                    i++;
            if (Column + (i - p) > right)
            {
                // (0x79A9–0x79E2 test for a word whose last cell is exactly the right edge; it cannot be reached.)
                Column = left;
                Row++;
                while (p < len && S(p) == ' ')
                    p++;
                if (Row > bottom && p < len)
                {
                    Column = left;
                    Row = top;
                    Pause?.Invoke();
                    ClearRect(left, top, right, bottom);
                    p = Put(text, p, i, colour);
                }
                // else the word is printed by the next pass at the new row
            }
            else
                p = Put(text, p, i, colour);
        }
        while (p <= len);
        if (Column > right)
        {
            Column = left;
            Row++;
        }
    }

    /// <summary>Characters p..i at the cursor, one cell each (0x7737); returns i + 1.</summary>
    private int Put(string text, int p, int i, byte colour)
    {
        for (; p <= i; p++)
        {
            int r = Row - _top, c = Column - _left;
            // [?] A cell outside the box (a one-character last word after a full box) is drawn by the program on the
            // row below the box; we drop it.
            if (r >= 0 && r < _chars.GetLength(0) && c >= 0 && c < _chars.GetLength(1))
            {
                _chars[r, c] = char.ToUpperInvariant(text[p - 1]);
                _colours[r, c] = colour;
            }
            Column++;
            Typed?.Invoke();
        }
        return p;
    }

    /// <summary>The delimiter set of the word cut (START.EXE DS:0D16, a set constant for characters 0x20–0x3F).</summary>
    public static bool IsDelimiter(char c) => c is '!' or ',' or '-' or '.' or ':' or ';' or '?';
}
