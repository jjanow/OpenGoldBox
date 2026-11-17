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
/// Characters are drawn as the glyph routine (START 0x726F) does: upper case. [?] The program also types the text
/// out with a delay of DS:49E2 × 10 ms per character (while DS:4A00 is set, as PRINT sets it); we show it at once.
/// </summary>
public sealed class TextWindow
{
    /// <summary>The script text box of PRINT / PRINTCLR (GAME.OVR 0x174F): columns 1–38, rows 17–22.</summary>
    public const int Left = 1, Top = 17, Right = 38, Bottom = 22;

    /// <summary>The colour PRINT uses (EGA light green; the glyph routine remaps it only in CGA mode, DS:4A1E = 0).</summary>
    public const byte ScriptColour = 10;

    private readonly char[,] _chars = new char[Bottom - Top + 1, Right - Left + 1];
    private readonly byte[,] _colours = new byte[Bottom - Top + 1, Right - Left + 1];

    /// <summary>The text cursor (screen cells).</summary>
    public int Column { get; private set; } = Left;
    public int Row { get; private set; } = Top;

    /// <summary>Called when the box is full and more text follows: show the page prompt and wait for a key.</summary>
    public Action? Pause { get; set; }

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

    /// <summary>The character and colour of a box cell (row and column in screen cells); '\0' = blank.</summary>
    public (char Char, byte Colour) Cell(int row, int col) => _chars[row - Top, col - Left] is var c && c != '\0'
        ? (c, _colours[row - Top, col - Left])
        : ('\0', (byte)0);

    /// <summary>The box rows as strings (blanks as spaces, trailing blanks cut): for tests and logs.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var list = new List<string>();
            for (int r = 0; r <= Bottom - Top; r++)
            {
                var s = new char[Right - Left + 1];
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
        Home();
    }

    /// <summary>The cursor to the box's top-left corner without blanking (EXIT: column 1, row 17).</summary>
    public void Home()
    {
        Column = Left;
        Row = Top;
    }

    /// <summary>PRINTRET (GAME.OVR 0x3831): column 1 of the next row (the next print starts at the top if that is below the box).</summary>
    public void NewLine()
    {
        Column = Left;
        Row++;
    }

    /// <summary>
    /// PRINT (<paramref name="clear"/> false: from the cursor) or PRINTCLR (true: the box blanked first), in
    /// <paramref name="colour"/>.
    /// </summary>
    public void Print(string text, bool clear = false, byte colour = ScriptColour)
    {
        if (Column < Left || Column > Right || Row < Top || Row > Bottom)
            Home();
        if (clear)
            Clear();
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
            if (Column + (i - p) > Right)
            {
                // (0x79A9–0x79E2 test for a word whose last cell is exactly the right edge; it cannot be reached.)
                Column = Left;
                Row++;
                while (p < len && S(p) == ' ')
                    p++;
                if (Row > Bottom && p < len)
                {
                    Column = Left;
                    Row = Top;
                    Pause?.Invoke();
                    Array.Clear(_chars);
                    Array.Clear(_colours);
                    p = Put(text, p, i, colour);
                }
                // else the word is printed by the next pass at the new row
            }
            else
                p = Put(text, p, i, colour);
        }
        while (p <= len);
        if (Column > Right)
        {
            Column = Left;
            Row++;
        }
    }

    /// <summary>Characters p..i at the cursor, one cell each (0x7737); returns i + 1.</summary>
    private int Put(string text, int p, int i, byte colour)
    {
        for (; p <= i; p++)
        {
            int r = Row - Top, c = Column - Left;
            // [?] A cell outside the box (a one-character last word after a full box) is drawn by the program on the
            // row below the box; we drop it.
            if (r >= 0 && r < _chars.GetLength(0) && c >= 0 && c < _chars.GetLength(1))
            {
                _chars[r, c] = char.ToUpperInvariant(text[p - 1]);
                _colours[r, c] = colour;
            }
            Column++;
        }
        return p;
    }

    /// <summary>The delimiter set of the word cut (START.EXE DS:0D16, a set constant for characters 0x20–0x3F).</summary>
    public static bool IsDelimiter(char c) => c is '!' or ',' or '-' or '.' or ':' or ';' or '?';
}
