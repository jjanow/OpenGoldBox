namespace GoldBox.Core.Explore;

/// <summary>
/// The game's 1-bit 8×8 text font (8X8D1 block 201 in PoR DOS: 177 glyphs × 8 bytes, bit 7 = left pixel). Glyph order
/// read off the data (docs/re/view3d.md "Font"): glyphs 32..63 are ASCII 0x20..0x3F, glyphs 1..31 are the letters
/// 0x41..0x5F (code − 0x40); lower case is drawn as upper case. This is the glyph routine's rule (START.EXE 0x726F:
/// glyph = UpCase(character) mod 64; docs/re/view3d.md "Font").
/// </summary>
public sealed class TextFont
{
    private readonly byte[] _glyphs;

    public int GlyphCount => _glyphs.Length / 8;

    public TextFont(byte[] glyphs)
    {
        if (glyphs.Length == 0 || glyphs.Length % 8 != 0)
            throw new ArgumentException("font data must be whole 8-byte glyphs", nameof(glyphs));
        _glyphs = glyphs;
    }

    /// <summary>
    /// A raw glyph number as a character (U+E000 + glyph): the glyph routine with its flag clear draws the byte as the
    /// glyph number itself (PROTECT's runes, GAME.OVR 0x3C53; docs/re/view3d.md "Font").
    /// </summary>
    public static char RawGlyph(byte glyph) => (char)(RawGlyphBase + glyph);

    private const int RawGlyphBase = 0xE000;

    /// <summary>The glyph number of a <see cref="RawGlyph"/> character, or null for any other character.</summary>
    public static byte? RawGlyphNumber(char c) =>
        c is >= (char)RawGlyphBase and <= (char)(RawGlyphBase + 0xFF) ? (byte)(c - RawGlyphBase) : null;

    /// <summary>Glyph index of a character, or −1 when the font has none.</summary>
    public int GlyphOf(char c)
    {
        if (c is >= 'a' and <= 'z')
            c = (char)(c - 32);
        int g = c switch
        {
            >= (char)0x20 and <= (char)0x3F => c,
            >= (char)0x40 and <= (char)0x5F => c - 0x40,
            >= (char)RawGlyphBase and <= (char)(RawGlyphBase + 0xFF) => c - RawGlyphBase,
            _ => -1,
        };
        return g < GlyphCount ? g : -1;
    }

    /// <summary>
    /// The Amiga text font and pens (docs/re/por-art-formats.md §4 "Text font"), or null for this font's own glyphs. Set
    /// by the Client under the Amiga art; colours 0–15 then draw through it, any other colour and raw glyphs as before.
    /// </summary>
    public TwoPassFace? Face { get; set; }

    /// <summary>
    /// A font drawn as the Amiga program draws text (hunk 25 +0xA4E / +0xC3C): the whole line in <see cref="Pens"/>[colour]
    /// at x, then again in <see cref="Shadows"/>[colour] one pixel to the right, set bits only (the second pass reaches
    /// one pixel into the cell after the line). <see cref="Glyphs"/>: 256 characters × 8 rows, bit 7 the left pixel,
    /// indexed by the character code as is (no upper-casing); pens are screen-palette indices.
    /// </summary>
    public sealed record TwoPassFace(byte[] Glyphs, byte[] Pens, byte[] Shadows)
    {
        public const int Characters = 256;

        public byte Row(char c, int y) => c < Characters ? Glyphs[c * 8 + y] : (byte)0;

        /// <summary>The highlight's screen indices (bar, text, pass-2 text), or null (no highlight: callers draw a colour).</summary>
        public LitPens? Lit { get; init; }

        /// <summary>The screen index of interface pen 16 (pens 16–31 follow it), for colours the program passes itself.</summary>
        public int InterfaceBase { get; init; } = -1;
    }

    /// <summary>The Amiga highlight (hunk 13 +0x3B64 / +0x3146): a bar, the text over it in one pen, then the next one pixel right.</summary>
    public sealed record LitPens(byte Bar, byte Pen, byte Shadow);

    /// <summary>
    /// Draws <paramref name="text"/> lit the Amiga way when the face has a highlight, and says so; false draws nothing.
    /// Leading blanks are skipped (the bar starts at the first character). A lit party member (hunk 13 +0x3B64–0x3C4E):
    /// the bar from x to x + 8 · length and y to y + 8, both ends included. A lit list row (<paramref name="listRow"/>,
    /// hunk 13 +0x3146–0x3296): the bar from x + 1 to x + 8 · length − 1 and y to y + 6. Then the text in the text pen
    /// and again one pixel right in the pass-2 pen, set bits only.
    /// </summary>
    public bool DrawLit(IndexedFrame f, int x, int y, string text, bool listRow)
    {
        if (Face is not { Lit: { } lit } face)
            return false;
        int lead = text.Length - text.TrimStart(' ').Length;
        text = text[lead..];
        x += 8 * lead;
        if (text.Length == 0)
            return true;
        int x0 = listRow ? x + 1 : x, x1 = listRow ? x + 8 * text.Length - 1 : x + 8 * text.Length;
        int y1 = listRow ? y + 6 : y + 8;
        for (int yy = y; yy <= y1; yy++)
            for (int xx = x0; xx <= x1; xx++)
                Put(f, xx, yy, lit.Bar);
        Pass(face, f, x, y, text, lit.Pen);
        Pass(face, f, x + 1, y, text, lit.Shadow);
        return true;
    }

    /// <summary>
    /// Draws <paramref name="text"/> in an Amiga colour argument (below 16: pen colour + 16 − colour mod 2, the glyph
    /// routine's rule; 16–30: that pen) and the next pen one pixel right, when the face knows its pens; false otherwise.
    /// For call sites whose Amiga colour differs from the DOS colour's usual pen (e.g. the party list's hit points).
    /// </summary>
    public bool DrawAmiga(IndexedFrame f, int x, int y, string text, int amigaColour)
    {
        if (Face is not { InterfaceBase: >= 0 } face)
            return false;
        int pen = amigaColour < 16 ? amigaColour + 16 - amigaColour % 2 : amigaColour;
        Pass(face, f, x, y, text, (byte)(face.InterfaceBase + pen - 16));
        Pass(face, f, x + 1, y, text, (byte)(face.InterfaceBase + pen - 15));
        return true;
    }

    /// <summary>
    /// The Amiga string routine (hunk 25 +0xC3C): the whole string in pass 1 in JAM2 (unset bits of each 8 × 8 cell in
    /// <paramref name="background"/>, the rastport's background pen, which the program never sets `[?]`), then pass 2
    /// one pixel right in JAM1. <paramref name="colour"/> below 16 is a DOS colour (the face's pens), 16–31 an
    /// interface pen. False without a face that knows the colour (nothing drawn).
    /// </summary>
    public bool DrawString(IndexedFrame f, int x, int y, string text, int colour, byte background)
    {
        if (Face is not { } face)
            return false;
        if (colour < 16 && colour < face.Pens.Length)
        {
            DrawTwoPass(face, f, x, y, text, (byte)colour, background);
            return true;
        }
        if (colour is < 16 or > 30 || face.InterfaceBase < 0)
            return false;
        for (int i = 0; i < text.Length; i++)
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    Put(f, x + 8 * i + c, y + r, background);
        Pass(face, f, x, y, text, (byte)(face.InterfaceBase + colour - 16));
        Pass(face, f, x + 1, y, text, (byte)(face.InterfaceBase + colour - 15));
        return true;
    }

    /// <summary>
    /// Draws <paramref name="text"/> once in the palette index <paramref name="pen"/>, set bits only, with the face's
    /// glyphs (an Intuition text drawn in one pass, e.g. a string gadget's); false without a face.
    /// </summary>
    public bool DrawPen(IndexedFrame f, int x, int y, string text, byte pen)
    {
        if (Face is not { } face)
            return false;
        Pass(face, f, x, y, text, pen);
        return true;
    }

    /// <summary>
    /// The menu routine's prompt (hunk 19 +0x4EEE–0x4F6A): with the face, in the pass-2 pen at x + 3 first, then in the
    /// pen at x + 4 (set bits; the first pass's JAM2 background `[?]`); without it as <see cref="Draw"/>. A colour of 16–30
    /// is an interface pen (the pass-2 pen the next one).
    /// </summary>
    public void DrawMenuPrompt(IndexedFrame f, int x, int y, string text, byte colour)
    {
        if (Face is { } face && colour < face.Pens.Length)
        {
            Pass(face, f, x + 3, y, text, face.Shadows[colour]);
            Pass(face, f, x + 4, y, text, face.Pens[colour]);
            return;
        }
        // An interface pen the caller passes itself (16–30, e.g. the yes/no warnings' 24).
        if (Face is { InterfaceBase: >= 0 } own && colour is >= 16 and <= 30)
        {
            Pass(own, f, x + 3, y, text, (byte)(own.InterfaceBase + colour - 15));
            Pass(own, f, x + 4, y, text, (byte)(own.InterfaceBase + colour - 16));
            return;
        }
        Draw(f, x, y, text, colour);
    }

    private static void Pass(TwoPassFace face, IndexedFrame f, int x, int y, string text, byte pen)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (RawGlyphNumber(ch) is not null)
                continue;
            for (int r = 0; r < 8; r++)
            {
                byte bits = face.Row(ch, r);
                for (int c = 0; c < 8; c++)
                    if ((bits & (0x80 >> c)) != 0)
                        Put(f, x + 8 * i + c, y + r, pen);
            }
        }
    }

    /// <summary>Draws text with the glyphs' set bits in <paramref name="colour"/>; with a background, unset bits too.</summary>
    public void Draw(IndexedFrame f, int x, int y, string text, byte colour, byte? background = null)
    {
        if (Face is { } face && colour < face.Pens.Length)
        {
            DrawTwoPass(face, f, x, y, text, colour, background);
            return;
        }
        DrawOwn(f, x, y, text, colour, background);
    }

    private void DrawTwoPass(TwoPassFace face, IndexedFrame f, int x, int y, string text, byte colour, byte? background)
    {
        // Pass 1 (JAM2 in the string routine, JAM1 in the glyph routine; the Client's background stands for the
        // former's background pen, which the program never sets [?]).
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            int x0 = x + 8 * i;
            if (RawGlyphNumber(ch) is not null)
            {
                DrawOwn(f, x0, y, ch.ToString(), colour, background);
                continue;
            }
            for (int r = 0; r < 8; r++)
            {
                byte bits = face.Row(ch, r);
                for (int c = 0; c < 8; c++)
                    if ((bits & (0x80 >> c)) != 0)
                        Put(f, x0 + c, y + r, face.Pens[colour]);
                    else if (background is { } bg)
                        Put(f, x0 + c, y + r, bg);
            }
        }
        // Pass 2: the next pen, one pixel to the right, set bits only.
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (RawGlyphNumber(ch) is not null)
                continue;
            for (int r = 0; r < 8; r++)
            {
                byte bits = face.Row(ch, r);
                for (int c = 0; c < 8; c++)
                    if ((bits & (0x80 >> c)) != 0)
                        Put(f, x + 8 * i + c + 1, y + r, face.Shadows[colour]);
            }
        }
    }

    private static void Put(IndexedFrame f, int x, int y, byte v)
    {
        if (x >= 0 && y >= 0 && x < f.Width && y < f.Height)
            f[x, y] = v;
    }

    private void DrawOwn(IndexedFrame f, int x, int y, string text, byte colour, byte? background)
    {
        foreach (char ch in text)
        {
            int g = GlyphOf(ch);
            for (int r = 0; r < 8; r++)
            {
                byte bits = g >= 0 ? _glyphs[g * 8 + r] : (byte)0;
                for (int c = 0; c < 8; c++)
                {
                    int xx = x + c, yy = y + r;
                    if (xx < 0 || yy < 0 || xx >= f.Width || yy >= f.Height)
                        continue;
                    if ((bits & (0x80 >> c)) != 0)
                        f[xx, yy] = colour;
                    else if (background is { } bg)
                        f[xx, yy] = bg;
                }
            }
            x += 8;
        }
    }
}
