using GoldBox.Core.Explore;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// PROTECT's glyphs as the Amiga program draws them (docs/re/ecl-format.md "PROTECT"; <see cref="PorAmigaRunes"/>):
/// the rune font from disk A (module <c>art/amiga/protect/0/8</c>), one Text call per glyph in JAM1, so only the set
/// bits are drawn, in the pen PROTECT passes (one of the interface colours 16–31, the PNG's palette). Under Original
/// DOS, or without the font in the module, there is none and the DOS text font's glyphs are drawn whole in colour 15.
/// </summary>
public sealed class RuneGlyphs
{
    private readonly byte[][] _cells;

    private RuneGlyphs(byte[][] cells) => _cells = cells;

    /// <summary>
    /// The Amiga rune font under <paramref name="choice"/>, its pens moved into the screen palette's run for its
    /// colours (<paramref name="paletteBase"/>), or null (Original DOS, or no such asset).
    /// </summary>
    public static RuneGlyphs? Load(IPorContent? content, PorArtChoice choice, Func<byte[], int> paletteBase)
    {
        if (choice == PorArtChoice.Dos
            || content?.Art(PorAmigaRunes.Container, PorAmigaRunes.Id, choice) is not
                { Variant: PorAmigaArt.Variant, Frames: [{ Width: PorAmigaRunes.Cell, Height: PorAmigaRunes.Cell } font] } img
            || font.Items.Count < PorAmigaRunes.Characters)
            return null;
        int b = paletteBase(img.PaletteRgb);
        var cells = new byte[PorAmigaRunes.Characters][];
        for (int c = 0; c < cells.Length; c++)
            cells[c] = [.. font.Items[c].Select(v => v == 0 ? (byte)0 : (byte)(b + v))];
        return new RuneGlyphs(cells);
    }

    /// <summary>The screen-palette index of pixel (<paramref name="x"/>, <paramref name="y"/>) of glyph <paramref name="glyph"/>, 0 where nothing is drawn.</summary>
    public byte Pixel(int glyph, int x, int y) => _cells[glyph & 0xFF][y * PorAmigaRunes.Cell + x];

    /// <summary>
    /// Draws <paramref name="ch"/> in text cell (<paramref name="col"/>, <paramref name="row"/>) when it is a raw glyph
    /// (<see cref="TextFont.RawGlyph"/>): its set bits only, nothing cleared behind them. False for any other character
    /// (the caller draws it with the text font).
    /// </summary>
    public bool Draw(IndexedFrame f, int col, int row, char ch)
    {
        if (TextFont.RawGlyphNumber(ch) is not { } g)
            return false;
        for (int y = 0; y < PorAmigaRunes.Cell; y++)
            for (int x = 0; x < PorAmigaRunes.Cell; x++)
            {
                int xx = col * PorAmigaRunes.Cell + x, yy = row * PorAmigaRunes.Cell + y;
                if (Pixel(g, x, y) is var v and not 0 && xx < f.Width && yy < f.Height)
                    f[xx, yy] = v;
            }
        return true;
    }
}
