using GoldBox.Core.Explore;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The text as the Amiga program draws it (docs/re/por-art-formats.md §4 "Text font"; <see cref="PorAmigaText"/>): disk
/// A's <c>poolrad.font</c> (module <c>art/amiga/poolrad/0/8</c>), each line twice, in the pen the program uses for that
/// DOS colour and in the next pen one pixel to the right (<c>art/amiga/text-pens.json</c>), set bits only. Only under the
/// Amiga art setting (user, s39: Best keeps the DOS font until the pens of the other DOS colours are read);
/// Original DOS and Best, or a module without the font, keep the DOS text font.
/// </summary>
public static class AmigaText
{
    /// <summary>The face <see cref="TextFont.Face"/> takes under <paramref name="choice"/>, or null (the DOS font).</summary>
    public static TextFont.TwoPassFace? Load(IPorContent? content, PorArtChoice choice, Func<byte[], int> paletteBase)
    {
        if (choice != PorArtChoice.Amiga
            || content?.AmigaTextPens() is not { Length: 16 } pens
            || content.Art(PorAmigaText.Container, PorAmigaText.Id, choice) is not
                { Variant: PorAmigaArt.Variant, Frames: [{ Width: PorAmigaRunes.Cell, Height: PorAmigaRunes.Cell } font] } img
            || font.Items.Count < TextFont.TwoPassFace.Characters)
            return null;
        int b = paletteBase(img.PaletteRgb) - PorAmigaRunes.FirstInterfacePen;   // pen p is palette index b + p
        var glyphs = new byte[TextFont.TwoPassFace.Characters * 8];
        for (int c = 0; c < TextFont.TwoPassFace.Characters; c++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (font.Items[c][y * 8 + x] != 0)
                        glyphs[c * 8 + y] |= (byte)(0x80 >> x);
        // The highlight's text pens are of the picture half (registers 0–15): the window frame's palette (table 3) holds
        // them, the same colours in every table.
        TextFont.LitPens? lit = null;
        if (content.AmigaTextHighlight() is [var bar, var pen, var shadow]
            && content.Art(PorAmigaFrame.Container, PorAmigaFrame.FirstId, choice) is { Variant: PorAmigaArt.Variant } frame)
        {
            int lo = paletteBase(frame.PaletteRgb);
            lit = new TextFont.LitPens((byte)(b + bar), (byte)(lo + pen), (byte)(lo + shadow));
        }
        return new TextFont.TwoPassFace(glyphs, [.. pens.Select(p => (byte)(b + p))], [.. pens.Select(p => (byte)(b + p + 1))])
            { Lit = lit, InterfaceBase = b + PorAmigaRunes.FirstInterfacePen };
    }
}
