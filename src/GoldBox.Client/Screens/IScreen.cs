using GoldBox.Client.Input;
using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>One screen of the Client. Screens read actions only (never keys or buttons) and draw into the 320×200 frame.</summary>
public interface IScreen
{
    /// <summary>The action a mouse click on this screen stands for, or null.</summary>
    GameAction? MapClick(ScreenClick click);

    /// <summary>Called when the mouse points at a frame pixel (menus highlight what is under it).</summary>
    void Hover(int x, int y) { }

    void Update(ActionInput input);

    void Draw(IndexedFrame frame, ActionInput input);
}

/// <summary>What screens may ask of the game: open and close screens, quit.</summary>
public interface IScreenHost
{
    void Push(IScreen screen);
    void Pop();
    /// <summary>Closes every screen and opens <paramref name="screen"/> (a game ends: the party menu, another game).</summary>
    void Replace(IScreen screen)
    {
        Pop();
        Push(screen);
    }
    void Quit();
    void SaveBindings();
}

/// <summary>Text and frame drawing in the original's 40 × 25 text-cell grid.</summary>
public static class Chrome
{
    public const int Columns = 40, Rows = 25;

    /// <summary>EGA colours the original screens use for text (measured in DOSBox-X: hotkey letter white, rest green).</summary>
    public const byte White = 15, Green = 10, Cyan = 11, Yellow = 14, Red = 4, LightRed = 12, Grey = 7;

    public static void Text(IndexedFrame f, TextFont font, int col, int row, string text, byte colour) =>
        font.Draw(f, col * 8, row * 8, text, colour);

    /// <summary>Blank <paramref name="cells"/> cells from (col, row).</summary>
    public static void Clear(IndexedFrame f, int col, int row, int cells)
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < cells * 8; x++)
                f[col * 8 + x, row * 8 + y] = 0;
    }

    /// <summary>Right-aligned text ending at column <paramref name="endCol"/> (inclusive).</summary>
    public static void TextRight(IndexedFrame f, TextFont font, int endCol, int row, string text, byte colour) =>
        Text(f, font, endCol - text.Length + 1, row, text, colour);

    /// <summary>
    /// The 8 × 8 symbols the frames are drawn from (the program's symbol set, <see cref="SymbolSet"/>), set once the
    /// content's art is loaded; null draws the stand-in (tests, or before the art is read).
    /// </summary>
    public static SymbolSet? FrameSymbols { get; set; }

    /// <summary>First frame symbol (START 0x3F10, read s21: symbol 0x114 + 3 · style; +0 the corners, +1 the left and right
    /// columns, +2 the top and bottom rows; 8X8D1 block 202 tiles 20…). Every window seen so far uses style 0.</summary>
    public const int FrameSymbol = 0x114;

    /// <summary>The frame's pieces: a corner (or a joint), a cell of a left/right column, a cell of a top/bottom row.</summary>
    public enum FramePiece { Corner = 0, Vertical = 1, Horizontal = 2 }

    /// <summary>A frame cell drawn as the program draws it (Put8x8Symbol of <see cref="FrameSymbol"/> + piece, unmasked).</summary>
    public static void FrameCell(IndexedFrame f, int col, int row, FramePiece piece)
    {
        int x0 = col * 8, y0 = row * 8;
        var tile = FrameSymbols is { } s ? s.Tile(FrameSymbol + (int)piece) : [];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                f[x0 + x, y0 + y] = tile.Length == SymbolSet.TilePixels ? tile[y * 8 + x] : StandIn(x, y, piece);
    }

    /// <summary>A corner cell (also the joints where dividers meet a frame).</summary>
    public static void BraidCell(IndexedFrame f, int col, int row, bool corner) =>
        FrameCell(f, col, row, corner ? FramePiece.Corner : FramePiece.Horizontal);

    // Without the art: the old stand-in in the same colours.
    private static byte StandIn(int x, int y, FramePiece piece) => piece == FramePiece.Corner
        ? (x is 0 or 7 || y is 0 or 7 || x is 2 or 5 || y is 2 or 5 ? Yellow : (byte)0)
        : ((x + y) % 4) switch { 0 => LightRed, 1 => Red, _ => (byte)0 };

    /// <summary>
    /// The party list (START 0x1307, read s21): "NAME" at <paramref name="col"/> and "AC" / "HP" right-aligned at 34 / 38
    /// on row 2 (white); the members from row 4. The current member's name is white, the others take the name colour of
    /// START 0x23B6 (out of play light red, hostile yellow, else cyan). AC is |0x111 − 60| with "-" first when 0x111 &gt; 60
    /// (START 0x148F), green; HP is 0x11B, yellow below 0x32 else green (START 0x1513, flag 0). Members for which
    /// <paramref name="hide"/> answers true are left out (their rows close up).
    /// </summary>
    public static void PartyList(IndexedFrame f, TextFont font, EclParty? party, int col, int? highlighted = null,
        Func<EclCharacter, bool>? hide = null, Func<EclCharacter, bool>? heldOutOfPlay = null)
    {
        Text(f, font, col, 2, "NAME", White);
        TextRight(f, font, 34, 2, "AC", White);
        TextRight(f, font, 38, 2, "HP", White);
        if (party is null)
            return;
        for (int i = 0, row = 4; i < party.Members.Count && row < 12; i++)
        {
            var m = party.Members[i];
            if (hide?.Invoke(m) == true)
                continue;
            var name = m.Name.Length > 15 ? m.Name[..15] : m.Name;
            byte nameColour = i == (highlighted ?? party.CurrentIndex) ? White : m[0x10D] == 0 || heldOutOfPlay?.Invoke(m) == true ? LightRed : m[0x10E] == 1 ? Yellow : Cyan;
            Text(f, font, col, row, name.ToUpperInvariant(), nameColour);
            TextRight(f, font, 34, row, (60 - m[0x111]).ToString(CultureInfo.InvariantCulture), Green);
            TextRight(f, font, 38, row, m[0x11B].ToString(CultureInfo.InvariantCulture), m[0x11B] < m[0x32] ? Yellow : Green);
            row++;
        }
    }

    public static void HLine(IndexedFrame f, int row, int col0, int col1)
    {
        for (int c = col0; c <= col1; c++)
            FrameCell(f, c, row, FramePiece.Horizontal);
    }

    public static void VLine(IndexedFrame f, int col, int row0, int row1)
    {
        for (int r = row0; r <= row1; r++)
            FrameCell(f, col, r, FramePiece.Vertical);
    }

    public static void Box(IndexedFrame f, int col0, int row0, int col1, int row1)
    {
        HLine(f, row0, col0, col1);
        HLine(f, row1, col0, col1);
        VLine(f, col0, row0, row1);
        VLine(f, col1, row0, row1);
        foreach (var (c, r) in new[] { (col0, row0), (col1, row0), (col0, row1), (col1, row1) })
            BraidCell(f, c, r, true);
    }
}
