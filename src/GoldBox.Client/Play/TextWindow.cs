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
/// <para>Draw order and routine (the Amiga's, s45; docs/re/por-art-formats.md "Text routines"): the Amiga's print
/// (hunk 25 +0x10EC) draws each character through the glyph routine (hunk 25 +0xA4E, count 1: pass 1 then pass 2, JAM1),
/// one cell at a time; a fixed-cell string (<see cref="Put"/>, DOS's string at a column and row) goes through the string
/// routine (hunk 25 +0xC3C: the whole string in pass 1, JAM2, then in pass 2). Each cell keeps the number of the draw
/// that made it and whether that draw was a whole string (<see cref="Runs"/>), so a drawer can repeat the program's
/// order: under the Amiga font a glyph's pass-2 pixel one to the right survives only when no later pass 1 covers it.</para>
/// </summary>
public sealed class TextWindow
{
    /// <summary>The script text box of PRINT / PRINTCLR (GAME.OVR 0x174F): columns 1–38, rows 17–22.</summary>
    public const int Left = 1, Top = 17, Right = 38, Bottom = 22;

    /// <summary>The colour PRINT uses (EGA light green; the glyph routine remaps it only in CGA mode, DS:4A1E = 0).</summary>
    public const byte ScriptColour = 10;

    private readonly char[,] _chars;
    private readonly byte[,] _colours;
    // The draw that made each cell (0: none), and whether it was a whole string (Put) rather than one glyph (print).
    private readonly int[,] _draws;
    private readonly bool[,] _whole;
    // Cells drawn outside the area (the glyph routine draws any cell up to column 39, row 24): a whole string's
    // character one column right of the box (Put), a printed glyph below the box or a run-on's over the frame.
    private readonly Dictionary<(int Row, int Col), (char Char, byte Colour, int Draw, bool Whole)> _outside = [];
    private int _lastDraw;
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
        _draws = new int[bottom - top + 1, right - left + 1];
        _whole = new bool[bottom - top + 1, right - left + 1];
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

    /// <summary>
    /// Called after each word the Amiga's print (<see cref="PrintAmiga"/>) draws, the last one (only the NUL) included:
    /// hunk 25 +0x1286–0x1292 Delay(speed byte hunk 32 +0xA8 × 2) in AmigaDOS ticks of 1/50 s, so 40 ms per speed step
    /// per word, with no typing-flag test (DOS's DS:4A00 has no Amiga counterpart here: every print waits). The owner
    /// waits there; <see cref="Typed"/> is not called on that path. Null = at once.
    /// </summary>
    public Action? WordTyped { get; set; }

    /// <summary>The cells <see cref="Print"/> / <see cref="PrintIn"/> have stored so far (for a typed line's reveal).</summary>
    public int Printed { get; private set; }

    /// <summary>The Amiga print's wait per word (<see cref="WordTyped"/>): speed × 2 ticks of 20 ms.</summary>
    public static TimeSpan AmigaWordDelay(int speed) => TimeSpan.FromMilliseconds(40 * speed);

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
    /// The character and colour of a screen cell (row and column in screen cells); '\0' = blank. Cells outside the area
    /// are those the program draws there: <see cref="Put"/>'s one column right of the box, over the frame (the glyph
    /// routine draws up to column 39; DOSBox-X s22: the end game's 39-character line), and the print's below the box or
    /// in a run-on (<see cref="PrintIn"/>). The box clear leaves them; <see cref="Clear"/> removes them.
    /// </summary>
    public (char Char, byte Colour) Cell(int row, int col)
    {
        if (Inside(row, col))
            return _chars[row - _top, col - _left] is var c && c != '\0'
                ? (c, _colours[row - _top, col - _left])
                : ('\0', (byte)0);
        return _outside.TryGetValue((row, col), out var o) ? (o.Char, o.Colour) : ('\0', (byte)0);
    }

    private bool Inside(int row, int col) => row >= _top && row <= _bottom && col >= _left && col <= _right;

    /// <summary>The cells drawn outside the area (<see cref="Cell"/>), as (row, column).</summary>
    public IEnumerable<(int Row, int Col)> OutsideCells => _outside.Keys;

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
        Array.Clear(_draws);
        Array.Clear(_whole);
        _outside.Clear();
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
        int draw = ++_lastDraw;
        for (int i = 0; i < text.Length; i++)
        {
            int c = col + i;
            if (c == _right + 1 && c <= 39)
                _outside[(row, c)] = (Glyph(text[i], AmigaView.On), colour, draw, true);
            if (c < _left || c > _right)
                continue;
            _chars[row - _top, c - _left] = text[i];
            _colours[row - _top, c - _left] = colour;
            _draws[row - _top, c - _left] = draw;
            _whole[row - _top, c - _left] = true;
        }
    }

    /// <summary>Sets the text cursor (DS:5E24 column, DS:5E25 row) without drawing.</summary>
    public void MoveTo(int column, int row)
    {
        Column = column;
        Row = row;
    }

    /// <summary>
    /// The box's cells as window lines, for a full-screen window: each whole string (<see cref="Put"/>) as one line,
    /// the printed cells as runs of one colour per row with <see cref="WindowLine.Glyphs"/> set (drawn glyph by glyph
    /// under the Amiga font), in the order they were drawn.
    /// </summary>
    public IReadOnlyList<WindowLine> WindowLines()
    {
        var lines = new List<(int Draw, WindowLine Line)>();
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
                var (draw, whole) = DrawOf(r, c);
                int start = c;
                var sb = new System.Text.StringBuilder();
                if (whole)
                    while (c <= _right && Cell(r, c).Char != '\0' && DrawOf(r, c) == (draw, true))
                        sb.Append(Cell(r, c++).Char);
                else
                    while (c <= _right && Cell(r, c) is var (ch2, col2) && (ch2 == '\0' || col2 == colour && !DrawOf(r, c).Whole))
                    {
                        sb.Append(ch2 == '\0' ? ' ' : ch2);
                        c++;
                    }
                lines.Add((draw, new WindowLine(start, r, sb.ToString().TrimEnd(), colour) { Glyphs = !whole }));
            }
        }
        return [.. lines.OrderBy(l => l.Draw).Select(l => l.Line)];
    }

    /// <summary>The draw that made a cell (0 for a blank one) and whether it was a whole string.</summary>
    private (int Draw, bool Whole) DrawOf(int row, int col) => Inside(row, col)
        ? (_draws[row - _top, col - _left], _whole[row - _top, col - _left])
        : _outside.TryGetValue((row, col), out var o) ? (o.Draw, o.Whole) : (0, false);

    /// <summary>
    /// The cells of rows <paramref name="top"/>–<paramref name="bottom"/> (columns 0–39, the cells outside the area
    /// included) as the draws that made them, in the order they were made: a whole string (<see cref="Put"/>) as one
    /// run of the cells it still holds on a row, each printed character as a run of one. Blank cells are left out.
    /// </summary>
    public IReadOnlyList<TextRun> Runs(int top, int bottom)
    {
        var runs = new List<(int Draw, TextRun Run)>();
        for (int r = Math.Max(top, 0); r <= Math.Min(bottom, ScreenBottom); r++)
        {
            int c = 0;
            while (c <= ScreenRight)
            {
                var (ch, colour) = Cell(r, c);
                if (ch == '\0')
                {
                    c++;
                    continue;
                }
                var (draw, whole) = DrawOf(r, c);
                int start = c;
                var sb = new System.Text.StringBuilder().Append(ch);
                c++;
                if (whole)
                    while (c <= ScreenRight && Cell(r, c) is var (ch2, col2) && ch2 != '\0' && col2 == colour
                           && DrawOf(r, c) == (draw, true))
                    {
                        sb.Append(ch2);
                        c++;
                    }
                runs.Add((draw, new TextRun(start, r, sb.ToString(), colour, whole)));
            }
        }
        return [.. runs.OrderBy(x => x.Draw).Select(x => x.Run)];
    }

    /// <summary>Blanks one row of the box.</summary>
    public void ClearRow(int row) => ClearRect(_left, row, _right, row);

    /// <summary>Blanks the cells (<paramref name="left"/>, <paramref name="top"/>)–(<paramref name="right"/>,
    /// <paramref name="bottom"/>) (ClearBox START 0x4047 / 0x7E63), cells drawn outside the area among them. The cursor
    /// does not move.</summary>
    public void ClearRect(int left, int top, int right, int bottom)
    {
        if (_outside.Count > 0)
            foreach (var k in _outside.Keys.Where(k => k.Row >= top && k.Row <= bottom && k.Col >= left && k.Col <= right).ToList())
                _outside.Remove(k);
        for (int r = Math.Max(top, _top); r <= Math.Min(bottom, _bottom); r++)
            for (int c = Math.Max(left, _left); c <= Math.Min(right, _right); c++)
            {
                _chars[r - _top, c - _left] = '\0';
                _colours[r - _top, c - _left] = 0;
                _draws[r - _top, c - _left] = 0;
                _whole[r - _top, c - _left] = false;
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
        if (Amiga ?? (AmigaPrint && AmigaView.On))
        {
            PrintAmiga(left, top, right, bottom, text, clear, colour);
            return;
        }
        PrintDos(left, top, right, bottom, text, clear, colour);
    }

    /// <summary>The program's string length limit: a Pascal string[255] (START 0x780E copies the text with at most 255).</summary>
    public const int MaxText = 255;

    /// <summary>The screen's last column and row (the glyph routine START 0x726F draws nothing past them).</summary>
    public const int ScreenRight = 39, ScreenBottom = 24;

    /// <summary>
    /// Called when DOS's print never returns (<see cref="PrintDos"/>: a 255-character text, or a box narrower than one
    /// cell): the program hangs there, so the owner parks the game for good. Null (tests): the print returns.
    /// </summary>
    public Action? Hang { get; set; }

    /// <summary>
    /// START 0x77F8 whole (P s47; tools/scripts/por_print_oracle.py, PrintProgramTests). Its counters (position p, word
    /// end i, length) and the cursor DS:5E24 / DS:5E25 are bytes.
    /// <list type="number">
    /// <item>A box with x0 &gt; 39, y0 &gt; 24, x1 &gt; 39 or y1 &gt; 39 does nothing (0x7813–0x782B).</item>
    /// <item>A cursor outside the box goes to (x0, y0); with clear the box is blanked (0x7E63) and the cursor goes there.</item>
    /// <item>The text is copied into a string[255] (0x780E: a longer one is cut); an empty one ends here.</item>
    /// <item>Words as <see cref="Print"/> says; a word that does not fit wraps (column x0, row + 1, spaces skipped
    /// 0x77A4); below the box with text left the page prompt (<see cref="Pause"/>; then 0899:0104 = START 0x8A94, which
    /// jumps over its own key-flush loop, so no key is dropped), the box blanked and the word drawn at the top. Below
    /// the box with only the word's last character left (p = length) no prompt: the next pass draws it on the row below
    /// the box (row 23 under PRINT, over the frame).</item>
    /// <item>Each character through the glyph routine at the cursor (0x7737), then column + 1 and the typing wait.
    /// The loop runs while p ≤ i with p a byte: a word ending at character 255 never ends (p wraps to 0). The program
    /// then draws the length byte (character 255: glyph 63, '?') and the text again from its start, the column wrapping
    /// at 256, for ever; after one lap of 256 every cell it reaches (columns 0–39 of that row) holds what it keeps, so
    /// one lap is drawn and the print hangs (<see cref="Hang"/>).</item>
    /// <item>A box with x1 &lt; x0 whose last word is one character counts the row on for ever and draws nothing (hang).</item>
    /// <item>After the text a cursor past x1 goes to column x0 of the next row.</item>
    /// </list>
    /// </summary>
    private void PrintDos(int left, int top, int right, int bottom, string text, bool clear, byte colour)
    {
        if (left > ScreenRight || top > ScreenBottom || right > ScreenRight || bottom > 39)
            return;
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
        if (text.Length > MaxText)
            text = text[..MaxText];
        int len = text.Length;
        if (len == 0)
            return;
        // 1-based indexes as the Pascal routine; index 0 is the length byte.
        char S(int i) => i == 0 ? (char)len : text[i - 1];
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
                Row = (Row + 1) & 0xFF;
                while (p < len && S(p) == ' ')
                    p++;
                if (Row > bottom && p < len)
                {
                    Column = left;
                    Row = top;
                    Pause?.Invoke();
                    ClearRect(left, top, right, bottom);
                    if (!PutDos(S, ref p, i, colour))
                        return;
                }
                else if (p == len && left > right)
                {
                    // The one-character word never fits a box narrower than one cell: the row counts on for ever.
                    Hang?.Invoke();
                    return;
                }
                // else the word is printed by the next pass at the new row
            }
            else if (!PutDos(S, ref p, i, colour))
                return;
        }
        while (p <= len);
        if (Column > right)
        {
            Column = left;
            Row = (Row + 1) & 0xFF;
        }
    }

    /// <summary>
    /// 0x7737: characters p..i at the cursor, one glyph each (<see cref="DrawGlyph"/>); p ends at i + 1. False when p
    /// wrapped (i = 255): one lap of the run-on is drawn, then <see cref="Hang"/>.
    /// </summary>
    private bool PutDos(Func<int, char> s, ref int p, int i, byte colour)
    {
        while (p <= i)
        {
            DrawGlyph(s(p), colour);
            p = (p + 1) & 0xFF;
            if (p == 0)
            {
                for (int k = 0; k <= 0xFF; k++)
                    DrawGlyph(s(k), colour);
                Hang?.Invoke();
                return false;
            }
        }
        return true;
    }

    /// <summary>The glyph routine START 0x726F at the cursor (nothing past column 39 or row 24; a cell outside the area
    /// is kept apart, <see cref="Cell"/>), then column + 1 (a byte) and <see cref="Typed"/>.</summary>
    private void DrawGlyph(char ch, byte colour)
    {
        if (Column <= ScreenRight && Row <= ScreenBottom)
        {
            char g = Glyph(ch, AmigaView.On);
            int draw = ++_lastDraw;
            if (Inside(Row, Column))
            {
                _chars[Row - _top, Column - _left] = g;
                _colours[Row - _top, Column - _left] = colour;
                _draws[Row - _top, Column - _left] = draw;
                _whole[Row - _top, Column - _left] = false;
            }
            else
                _outside[(Row, Column)] = (g, colour, draw, false);
            Printed++;
        }
        Column = (Column + 1) & 0xFF;
        Typed?.Invoke();
    }

    /// <summary>Which machine's print <see cref="PrintIn"/> follows when set (tests); null = the Amiga's for a window
    /// with <see cref="AmigaPrint"/> (the default) under the Amiga pages (<see cref="AmigaView.On"/>), else DOS's.</summary>
    public bool? Amiga { get; set; }

    /// <summary>
    /// This window follows the Amiga's print under the Amiga pages (<see cref="PrintAmiga"/>): every text window does,
    /// since the program has one print for all of them (hunk 25 +0x10EC: PRINT / PRINTCLR, venues, DAMAGE, the fight's
    /// panel). Seen in FS-UAE s45 in the fight's panel and a paged PRINTCLR (rows 17–22 equal); false keeps DOS's cut.
    /// </summary>
    public bool AmigaPrint { get; init; } = true;

    /// <summary>
    /// The Amiga's print (hunk 25 +0x10EC–0x12A6, read s45; a C port of START 0x77F8 with its own word cut): with
    /// "clear" the box is blanked and the cursor goes to its top-left corner (no check of a cursor outside the box). Each
    /// word runs up to and including the first space or delimiter, or the string's terminating NUL, and every one of
    /// those characters counts, the NUL too: a word goes to the next row (column left, nothing skipped) when the cursor
    /// column plus that count passes right + 1, so a line's last word needs one cell more than on DOS (FS-UAE s45:
    /// "Hitting for 12" / "points of" / "damage" in the fight panel, columns 23–38). Past the bottom row the cursor goes
    /// back to the top, the page prompt waits (<see cref="Pause"/>) and the box is blanked; then the word is printed,
    /// its space or delimiter included. Nothing moves the cursor after the text. After every word, the NUL's included,
    /// the per-word wait (hunk 25 +0x1286: <see cref="WordTyped"/>); none per character.
    /// </summary>
    private void PrintAmiga(int left, int top, int right, int bottom, string text, bool clear, byte colour)
    {
        if (clear)
        {
            ClearRect(left, top, right, bottom);
            Column = left;
            Row = top;
        }
        int pos = 0;
        char c;
        do
        {
            int start = pos, count = Column;
            do
            {
                c = pos < text.Length ? text[pos] : '\0';
                pos++;
                count++;
            }
            while (c != '\0' && c != ' ' && !IsDelimiter(c));
            int end = c == '\0' ? pos - 1 : pos;   // exclusive; the NUL is not drawn
            if (count > right + 1)
            {
                Row++;
                Column = left;
            }
            if (Row > bottom)
            {
                Row = top;
                Column = left;
                Pause?.Invoke();
                ClearRect(left, top, right, bottom);
            }
            if (end > start)
                Put(text, start + 1, end, colour, typed: false);
            WordTyped?.Invoke();
        }
        while (c != '\0');
    }

    /// <summary>Characters p..i at the cursor, one cell each (the Amiga's print; DOS's is <see cref="PutDos"/>), each
    /// followed by <see cref="Typed"/> unless <paramref name="typed"/> is false (the Amiga's print waits per word);
    /// returns i + 1.</summary>
    private int Put(string text, int p, int i, byte colour, bool typed = true)
    {
        for (; p <= i; p++)
        {
            int r = Row - _top, c = Column - _left;
            // [?] A cell outside the box (a one-character last word after a full box) is drawn by the program on the
            // row below the box; we drop it.
            if (r >= 0 && r < _chars.GetLength(0) && c >= 0 && c < _chars.GetLength(1))
            {
                _chars[r, c] = Glyph(text[p - 1], AmigaView.On);
                _colours[r, c] = colour;
                _draws[r, c] = ++_lastDraw;
                _whole[r, c] = false;
                Printed++;
            }
            Column++;
            if (typed)
                Typed?.Invoke();
        }
        return p;
    }

    /// <summary>
    /// The character a text-window cell keeps: DOS's print (START 0x7737) draws glyph UpCase(c) mod 64 (the glyph
    /// routine 0x726F; UpCase changes a–z only), kept as the character 0x20–0x5F of that glyph (glyph 0 as '@'); the
    /// Amiga's (hunk 25 +0x10EC, the words copied as stored, then each byte through the glyph routine) keeps it, so a
    /// lower-case letter shows its own glyph.
    /// </summary>
    public static char Glyph(char c, bool amiga) => amiga ? c : DosGlyph(c);

    /// <summary>DOS's glyph routine's character (START 0x726F with the upper-case flag): UpCase, then mod 64, as the
    /// character 0x20–0x5F that names that glyph. Characters past 0xFF (the Client's raw glyphs) are kept.</summary>
    public static char DosGlyph(char c)
    {
        if (c > 0xFF)
            return c;
        if (c is >= 'a' and <= 'z')
            c = (char)(c - 32);
        int g = c & 63;
        return (char)(g < 32 ? g + 64 : g);
    }

    /// <summary>The delimiter set of the word cut (START.EXE DS:0D16, a set constant for characters 0x20–0x3F).</summary>
    public static bool IsDelimiter(char c) => c is '!' or ',' or '-' or '.' or ':' or ';' or '?';
}

/// <summary>
/// One draw of a text window's cells (<see cref="TextWindow.Runs"/>): a whole string of the string routine
/// (<paramref name="Whole"/>) or one printed glyph, at a cell, in a colour (0–15 a DOS colour, 16–31 an Amiga pen).
/// </summary>
public sealed record TextRun(int Column, int Row, string Text, byte Colour, bool Whole);

