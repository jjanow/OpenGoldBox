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

    /// <summary>
    /// A text window's draws (<see cref="Play.TextWindow.Runs"/>) in the order the program made them. Under the Amiga
    /// font a whole string goes through the string routine (<see cref="TextFont.DrawString"/>: pass 1 over the whole
    /// string, JAM2, then pass 2) and a printed character through the glyph routine (its two passes, JAM1), so a glyph's
    /// pass-2 pixel one to the right stays only when no later pass 1 covers it (s45). Colours 16–31 are Amiga pens
    /// (white without the Amiga font). DOS draws every cell in one pass, so the order does not matter there.
    /// <paramref name="special"/> draws a cell itself (PROTECT's runes) and says so.
    /// </summary>
    public static void TextRuns(IndexedFrame f, TextFont font, IEnumerable<Play.TextRun> runs,
        Func<int, int, char, bool>? special = null)
    {
        foreach (var run in runs)
        {
            if (run.Text.Length == 1 && special?.Invoke(run.Column, run.Row, run.Text[0]) == true)
                continue;
            if (run.Whole && font.DrawString(f, run.Column * 8, run.Row * 8, run.Text, run.Colour, 0))
                continue;
            Glyphs(f, font, run.Column, run.Row, run.Text, run.Colour);
        }
    }

    /// <summary>
    /// Printed text glyph by glyph, as the Amiga's text routine draws it (hunk 25 +0x10EC → the glyph routine +0xA4E,
    /// both passes of one character before the next); colours 16–31 are Amiga pens (white without the Amiga font).
    /// Under the DOS font the same as <see cref="Text"/>.
    /// </summary>
    public static void Glyphs(IndexedFrame f, TextFont font, int col, int row, string text, byte colour)
    {
        if (font.Face is null)
        {
            Text(f, font, col, row, text, colour is >= 16 and <= 31 ? White : colour);
            return;
        }
        for (int i = 0; i < text.Length; i++)
        {
            string ch = text[i].ToString();
            if (colour < 16)
                Text(f, font, col + i, row, ch, colour);
            else if (!font.DrawAmiga(f, (col + i) * 8, row * 8, ch, colour))
                Text(f, font, col + i, row, ch, White);
        }
    }

    /// <summary>The menu routine's prompt on the bottom row: the Amiga way under its text font (<see cref="TextFont.DrawMenuPrompt"/>).</summary>
    public static void MenuPrompt(IndexedFrame f, TextFont font, int col, int row, string text, byte colour) =>
        font.DrawMenuPrompt(f, col * 8, row * 8, text, colour);

    /// <summary>
    /// A lit entry: the Amiga highlight under its text font (a party member, or a list row with <paramref name="listRow"/>),
    /// else the text in <paramref name="colour"/> (DOS: the program's lit colour, white).
    /// </summary>
    public static void Lit(IndexedFrame f, TextFont font, int col, int row, string text, byte colour, bool listRow)
    {
        if (!font.DrawLit(f, col * 8, row * 8, text, listRow))
            Text(f, font, col, row, text, colour);
    }

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
    public static void BraidCell(IndexedFrame f, int col, int row, bool corner)
    {
        if (AmigaFrame is { } a)
        {
            if (corner)
                a.Corner(f, col, row);
            else
                a.HLine(f, row, col, col);
            return;
        }
        FrameCell(f, col, row, corner ? FramePiece.Corner : FramePiece.Horizontal);
    }

    /// <summary>
    /// The Amiga window frame (<see cref="Play.AmigaFrame"/>), or null for the 8X8D symbols. Set by the Client from its
    /// art setting (Amiga only); tests leave it null.
    /// </summary>
    public static Play.AmigaFrame? AmigaFrame { get; set; }

    /// <summary>
    /// The current menu entry as the Amiga's pressed gadget (<see cref="Play.AmigaGadget"/>), or null for the lit
    /// colours. Set by the Client from its art setting (Amiga only); tests leave it null.
    /// </summary>
    public static Play.AmigaGadget? AmigaGadget { get; set; }

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
        Func<EclCharacter, bool>? hide = null, Func<EclCharacter, bool>? heldOutOfPlay = null,
        Func<EclCharacter, EclCharacter>? asDrawn = null)
    {
        Text(f, font, col, 2, "NAME", White);
        TextRight(f, font, 34, 2, "AC", White);
        TextRight(f, font, 38, 2, "HP", White);
        if (party is null)
            return;
        for (int i = 0, row = 4; i < party.Members.Count && row < 12; i++)
        {
            var live = party.Members[i];
            if (hide?.Invoke(live) == true)
                continue;
            var m = asDrawn?.Invoke(live) ?? live; // the values the list last drew
            var name = m.Name.Length > 15 ? m.Name[..15] : m.Name;
            bool lit = i == (highlighted ?? party.CurrentIndex);
            byte nameColour = lit ? White : m[0x10D] == 0 || heldOutOfPlay?.Invoke(live) == true ? LightRed : m[0x10E] == 1 ? Yellow : Cyan;
            if (font.Face?.Lit is not null)
                Clear(f, col, row, 39 - col);   // the Amiga blanks each member's row first (hunk 13 +0x3B18)
            if (lit)
                Lit(f, font, col, row, name.ToUpperInvariant(), nameColour, listRow: false);
            else
                Text(f, font, col, row, name.ToUpperInvariant(), nameColour);
            TextRight(f, font, 34, row, (60 - m[0x111]).ToString(CultureInfo.InvariantCulture), Green);
            var hp = m[0x11B].ToString(CultureInfo.InvariantCulture);
            // Amiga (hunk 13 +0x3DF6–0x3E0A): hit points below the maximum in colour 8, else 10 (pens 24 / 26), not
            // the DOS colours' usual pens.
            if (!font.DrawAmiga(f, (38 - hp.Length + 1) * 8, row * 8, hp, m[0x11B] < m[0x32] ? 8 : 10))
                TextRight(f, font, 38, row, hp, m[0x11B] < m[0x32] ? Yellow : Green);
            row++;
        }
    }

    public static void HLine(IndexedFrame f, int row, int col0, int col1)
    {
        if (AmigaFrame is { } a)
        {
            a.HLine(f, row, col0, col1);
            return;
        }
        for (int c = col0; c <= col1; c++)
            FrameCell(f, c, row, FramePiece.Horizontal);
    }

    public static void VLine(IndexedFrame f, int col, int row0, int row1)
    {
        if (AmigaFrame is { } a)
        {
            a.VLine(f, col, row0, row1);
            return;
        }
        for (int r = row0; r <= row1; r++)
            FrameCell(f, col, r, FramePiece.Vertical);
    }

    public static void Box(IndexedFrame f, int col0, int row0, int col1, int row1)
    {
        if (AmigaFrame is { } a)
        {
            a.Window(f, col0 + 1, row0 + 1, col1 - 1, row1 - 1); // the window inside the frame (hunk 25 +0x16D4)
            return;
        }
        HLine(f, row0, col0, col1);
        HLine(f, row1, col0, col1);
        VLine(f, col0, row0, row1);
        VLine(f, col1, row0, row1);
        foreach (var (c, r) in new[] { (col0, row0), (col1, row0), (col0, row1), (col1, row1) })
            BraidCell(f, c, r, true);
    }
}
