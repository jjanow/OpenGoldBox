using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The current menu entry under the Amiga art (decided by the user, s39): drawn as the Amiga program's pressed menu
/// gadget. The menu routine makes each entry a BOOLGADGET with Flags 0, GADGHCOMP (hunk 19 +0x50A0–0x515E): its select
/// box, (column · 8, 192) wide 8 · length + 2 and 8 high, is complemented while it is pressed. On the 5-plane screen
/// (start-up's NewScreen, hunk 2 +0x74) that turns pen p into pen 31 − p, shown in row 24's colours (start-up's copper
/// list changes registers 1–3, 14, 15 and 31 from line 192, hunk 1 +0x290–0x31A; the others are the interface colours
/// and palette table 3, which agree with tables 0 and 4 for the pens row 24 holds) `[?]` for pens of other tables.
/// </summary>
public sealed class AmigaGadget
{
    private readonly Dictionary<byte, byte> _invert;

    private AmigaGadget(Dictionary<byte, byte> invert) => _invert = invert;

    /// <summary>The gadget highlight under <paramref name="choice"/>, or null (Original DOS, Best, or a module without the data).</summary>
    public static AmigaGadget? Load(IPorContent? content, PorArtChoice choice, Func<byte[], int> paletteBase)
    {
        if (choice != PorArtChoice.Amiga || content?.AmigaRow24() is not { } row24
            || content.Art(PorAmigaText.Container, PorAmigaText.Id, choice) is not { Variant: PorAmigaArt.Variant } font
            || content.Art(PorAmigaFrame.Container, PorAmigaFrame.FirstId, choice) is not { Variant: PorAmigaArt.Variant } frame)
            return null;
        // Registers 0–31 as the program has them outside row 24 (table 3, interface colours), then row 24's.
        var all = new byte[32 * 3];
        frame.PaletteRgb.AsSpan(0, 48).CopyTo(all);
        font.PaletteRgb.AsSpan(0, 48).CopyTo(all.AsSpan(48));
        var row = (byte[])all.Clone();
        for (int i = 0; i + 1 < row24.Length; i += 2)
            AmigaHunks.Rgb12To24([(byte)(row24[i + 1] >> 8), (byte)row24[i + 1]], 1).CopyTo(row, 3 * row24[i]);
        int lo = paletteBase(frame.PaletteRgb), hi = paletteBase(font.PaletteRgb);
        int rowLo = paletteBase(row[..48]), rowHi = paletteBase(row[48..]);
        byte Row(int pen) => (byte)(pen < 16 ? rowLo + pen : rowHi + pen - 16);
        var map = new Dictionary<byte, byte> { [0] = Row(31) };   // the Client's blank (EGA 0) is pen 0
        for (int p = 0; p < 16; p++)
        {
            map.TryAdd((byte)(lo + p), Row(31 - p));
            map.TryAdd((byte)(hi + p), Row(15 - p));
        }
        return new AmigaGadget(map);
    }

    /// <summary>Complements the box at (<paramref name="x"/>, <paramref name="y"/>): each pen p becomes pen 31 − p in row 24's colours.</summary>
    public void Invert(IndexedFrame f, int x, int y, int w, int h)
    {
        for (int yy = y; yy < y + h && yy < f.Height; yy++)
            for (int xx = x; xx < x + w && xx < f.Width; xx++)
                if (xx >= 0 && yy >= 0 && _invert.TryGetValue(f[xx, yy], out var v))
                    f[xx, yy] = v;
    }

    /// <summary>A menu entry's select box: the cells of its first word plus 2 pixels (hunk 19 +0x50DE–0x510A).</summary>
    public void Entry(IndexedFrame f, int col, int row, string entry)
    {
        int len = entry.IndexOfAny([' ', '_']) is var e and >= 0 ? e : entry.Length;
        Invert(f, col * 8, row * 8, 8 * len + 2, 8);
    }
}
