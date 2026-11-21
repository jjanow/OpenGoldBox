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

    /// <summary>Draws text with the glyphs' set bits in <paramref name="colour"/>; with a background, unset bits too.</summary>
    public void Draw(IndexedFrame f, int x, int y, string text, byte colour, byte? background = null)
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
