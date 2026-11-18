using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>
/// The title painting of Amiga Pool of Radiance v1.0 (docs/re/por-art-formats.md §5): disk A's <c>intro</c> is a
/// PowerPacker self-extracting executable; decrunched, it is a hunk executable whose DATA hunk #5 starts with the
/// 320×200 title in 4 bitplanes (plane after plane, 8,000 bytes each). The code in hunk #4 (+0x22A) blits it from hunk 5
/// +0 to the screen and then (0x844) fades the colour registers in from the 16 colours at hunk 5 +0x1F52E; those are
/// the title's colours (the copper list has one palette and no waits). Own code from the decruncher in the file.
/// </summary>
public static class AmigaIntro
{
    public const int Width = 320, Height = 200, Planes = 4;
    private const int RowBytes = Width / 8, PlaneBytes = RowBytes * Height;

    /// <summary>Decrunched intro: the DATA hunk holding the pictures and the code hunk that shows them.</summary>
    public const int PictureHunk = 5, CodeHunk = 4;

    /// <summary>Hunk 5 offset of the colours the title fades in to (read by the fade routine at hunk 4 +0x844).</summary>
    public const int TitlePalette = 0x1F52E;

    /// <summary>The PowerPacker offset-bit table of the intro's decruncher (hunk 0 +0x180).</summary>
    private static readonly int[] OffsetBits = [9, 9, 9, 9];

    /// <summary>
    /// Code checked before the hunks are trusted (hunk 4): +0x22A <c>lea hunk5+0,a0</c> … <c>move.l #200,d3</c> (the
    /// title blit's arguments), +0x85C <c>lea hunk5+$1F52E,a0</c> (the fade-in's colour source), +0x9AC
    /// <c>lea hunk5+$1F54E,a0</c> (the other fade-in's).
    /// </summary>
    private static readonly (int At, byte[] Code)[] Checks =
    [
        (0x22A, Convert.FromHexString("41F90000000022790001F486203C00000000223C00000000263C000000C8")),
        (0x85C, Convert.FromHexString("41F90001F52E")),
        (0x9AC, Convert.FromHexString("41F90001F54E")),
    ];

    /// <summary>The title painting (320×200 indices 0..15) and its 16 colours (RGB triplets) from the intro file's bytes.</summary>
    public static (byte[] Pixels, byte[] PaletteRgb) Title(ReadOnlySpan<byte> intro)
    {
        var (dec, pic, _, _) = Hunks(intro);
        return (Planar(dec.AsSpan(pic, Planes * PlaneBytes), Width, Height),
            AmigaHunks.Rgb12To24(dec.AsSpan(pic + TitlePalette), 16));
    }

    /// <summary>Hunk 5 offset of the credits sheet (320×600, 4 planes of 24,000 bytes), right after the title and its unread colours.</summary>
    public const int SheetOffset = 0x7D20, SheetRows = 600;

    /// <summary>Hunk 5 offset of the colours the one other fade (hunk 4 +0x994 in, +0xA3C out) reads.</summary>
    public const int SecondPalette = 0x1F54E;

    /// <summary>Hunk 4 offsets of the eight credits panels' argument blocks, in the order the intro shows them (0x42 apart).</summary>
    public static readonly IReadOnlyList<int> PanelCode = [.. Enumerable.Range(0, 8).Select(i => 0x1A + 0x42 * i)];

    /// <summary>
    /// One credits panel of the intro (hunk 4 +0, read s24): the screen is cleared (+0x7EE), <paramref name="Rows"/> rows
    /// of the sheet from <paramref name="SheetRow"/> are blitted to screen row <paramref name="ScreenRow"/> (+0x512, all 320
    /// pixels), then faded in from black to its colours (+0x844 with the title's colours, or +0x994 with
    /// <see cref="SecondPalette"/> when <paramref name="Second"/>).
    /// </summary>
    public sealed record Panel(int SheetRow, int ScreenRow, int Rows, bool Second);

    /// <summary>
    /// The eight panels, read from the intro's code: at each <see cref="PanelCode"/> offset <c>lea hunk5+$7D20,a0</c>,
    /// <c>movea.l hunk5+$1F486,a1</c>, <c>move.l #row,d0</c>, <c>move.l #y,d1</c>, <c>move.l #rows,d3</c>,
    /// <c>jsr +$512</c>, then <c>jsr +$844</c> or <c>jsr +$994</c> (the fade, which names the colours).
    /// </summary>
    public static IReadOnlyList<Panel> Panels(ReadOnlySpan<byte> intro)
    {
        var (dec, _, code, _) = Hunks(intro);
        var list = new List<Panel>();
        foreach (int at in PanelCode)
        {
            var c = dec.AsSpan(code + at);
            if (!c[..10].SequenceEqual(Convert.FromHexString("41F900007D2022790001"))
                || BinaryPrimitives.ReadUInt16BigEndian(c[10..]) != 0xF486
                || !c[12..14].SequenceEqual(Op(0x203C)) || !c[18..20].SequenceEqual(Op(0x223C))
                || !c[24..26].SequenceEqual(Op(0x263C)) || !c[30..36].SequenceEqual(Convert.FromHexString("4EB900000512"))
                || !c[36..40].SequenceEqual(Convert.FromHexString("4EB90000")))
                throw new DaxFormatException($"'intro': not the Amiga Pool of Radiance v1.0 intro (panel code at hunk 4 +0x{at:X})");
            int row = (int)BinaryPrimitives.ReadUInt32BigEndian(c[14..]), y = (int)BinaryPrimitives.ReadUInt32BigEndian(c[20..]);
            int rows = (int)BinaryPrimitives.ReadUInt32BigEndian(c[26..]);
            int fade = BinaryPrimitives.ReadUInt16BigEndian(c[40..]);
            if (fade is not (0x844 or 0x994) || row < 0 || rows <= 0 || row + rows > SheetRows || y < 0 || y + rows > Height)
                throw new DaxFormatException($"'intro': panel at hunk 4 +0x{at:X} out of range");
            list.Add(new Panel(row, y, rows, fade == 0x994));
        }
        return list;
    }

    /// <summary>
    /// Each panel as the screen shows it once faded in: 320×200 indices (index 0 outside the panel's rows, as the cleared
    /// screen) and its 16 colours (RGB triplets).
    /// </summary>
    public static IReadOnlyList<(Panel Panel, byte[] Pixels, byte[] PaletteRgb)> PanelScreens(ReadOnlySpan<byte> intro)
    {
        var panels = Panels(intro);
        var (dec, pic, _, _) = Hunks(intro);
        var sheet = Planar(dec.AsSpan(pic + SheetOffset, Planes * RowBytes * SheetRows), Width, SheetRows);
        var first = AmigaHunks.Rgb12To24(dec.AsSpan(pic + TitlePalette), 16);
        var second = AmigaHunks.Rgb12To24(dec.AsSpan(pic + SecondPalette), 16);
        var o = new List<(Panel, byte[], byte[])>();
        foreach (var p in panels)
        {
            var px = new byte[Width * Height];
            Array.Copy(sheet, p.SheetRow * Width, px, p.ScreenRow * Width, p.Rows * Width);
            o.Add((p, px, p.Second ? second : first));
        }
        return o;
    }

    private static byte[] Op(int w) => [(byte)(w >> 8), (byte)w];

    /// <summary>The decrunched file, the picture hunk's and code hunk's data offsets and the code hunk's size, after the checks.</summary>
    private static (byte[] Dec, int Pic, int Code, int CodeSize) Hunks(ReadOnlySpan<byte> intro)
    {
        var dec = DecrunchExecutable(intro);
        var hunks = AmigaHunks.Parse(dec);
        if (hunks.Count <= PictureHunk || hunks[PictureHunk] is not { Type: AmigaHunks.Data, DataOffset: { } pic } ph
            || ph.Size < SecondPalette + 32 || hunks[CodeHunk] is not { Type: AmigaHunks.Code, DataOffset: { } code } ch
            || ch.Size < 0x9B2)
            throw new DaxFormatException("'intro': not the Amiga Pool of Radiance v1.0 intro (hunk layout)");
        foreach (var (at, bytes) in Checks)
            if (at + bytes.Length > ch.Size || !dec.AsSpan(code + at, bytes.Length).SequenceEqual(bytes))
                throw new DaxFormatException($"'intro': not the Amiga Pool of Radiance v1.0 intro (code at hunk 4 +0x{at:X})");
        return (dec, pic, code, ch.Size);
    }

    /// <summary>
    /// A picture stored plane after plane (plane k = bit k, rows of width/8 bytes, MSB = left pixel) as one index per pixel.
    /// </summary>
    public static byte[] Planar(ReadOnlySpan<byte> planes, int width, int height, int planeCount = Planes)
    {
        int rb = width / 8, ps = rb * height;
        if (width % 8 != 0 || planes.Length < ps * planeCount)
            throw new DaxFormatException("planar picture: short data");
        var o = new byte[width * height];
        for (int k = 0; k < planeCount; k++)
            for (int r = 0; r < height; r++)
                for (int x = 0; x < width; x++)
                    if ((planes[k * ps + r * rb + (x >> 3)] & (0x80 >> (x & 7))) != 0)
                        o[r * width + x] |= (byte)(1 << k);
        return o;
    }

    /// <summary>
    /// The decrunched executable: the crunched stream is the data of the file's hunk #1 (the decruncher reads it from
    /// its start, length stored at hunk 0 +0x158, which equals the hunk's size).
    /// </summary>
    public static byte[] DecrunchExecutable(ReadOnlySpan<byte> intro)
    {
        var hunks = AmigaHunks.Parse(intro);
        if (hunks.Count < 2 || hunks[1] is not { DataOffset: { } at } h)
            throw new DaxFormatException("'intro': no crunched data hunk");
        return PowerPacker(intro.Slice(at, h.Size), OffsetBits);
    }

    /// <summary>
    /// PowerPacker decrunch (docs/re/por-art-formats.md §5), read from the end: the last long is (length &lt;&lt; 8) |
    /// skip bits; bits are taken LSB first from longs read towards the start. A <c>0</c> bit starts a literal run (2-bit
    /// counts, repeated while 3, then that many + 1 bytes); a match always follows: 2-bit index i, length i + 2, offset
    /// <paramref name="offsetBits"/>[i] bits (for i = 3 one bit picks 7 offset bits when 0, and 3-bit length
    /// extensions repeat while 7). The output is written from its end.
    /// </summary>
    public static byte[] PowerPacker(ReadOnlySpan<byte> src, IReadOnlyList<int> offsetBits)
    {
        int pos = src.Length;
        if (pos < 8 || pos % 4 != 0)
            throw new DaxFormatException("PowerPacker: stream too short");
        uint ReadLong(ReadOnlySpan<byte> s)
        {
            if (pos < 4)
                throw new DaxFormatException("PowerPacker: read before the stream start");
            pos -= 4;
            return BinaryPrimitives.ReadUInt32BigEndian(s[pos..]);
        }
        uint v = ReadLong(src);
        int skip = (int)(v & 0xFF), length = (int)(v >> 8);
        if (skip > 31)
            throw new DaxFormatException("PowerPacker: bad skip count");
        var output = new byte[length];
        int o = length;
        uint buf = ReadLong(src) >> skip;
        int left = 32 - skip;
        int Bits(ReadOnlySpan<byte> s, int n)
        {
            int r = 0;
            for (int i = 0; i < n; i++)
            {
                r = r << 1 | (int)(buf & 1);
                buf >>= 1;
                if (--left == 0)
                {
                    left = 32;
                    buf = ReadLong(s);
                }
            }
            return r;
        }
        while (true)
        {
            if (Bits(src, 1) == 0)
            {
                int n = 0, b;
                do
                {
                    b = Bits(src, 2);
                    n += b;
                } while (b == 3);
                for (int i = 0; i <= n; i++)
                {
                    if (--o < 0)
                        throw new DaxFormatException("PowerPacker: literals past the output start");
                    output[o] = (byte)Bits(src, 8);
                }
                if (o <= 0)
                    break;
            }
            int idx = Bits(src, 2), len = idx + 1, off;
            if (len == 4)
            {
                off = Bits(src, Bits(src, 1) != 0 ? offsetBits[idx] : 7);
                int b;
                do
                {
                    b = Bits(src, 3);
                    len += b;
                } while (b == 7);
            }
            else
                off = Bits(src, offsetBits[idx]);
            for (int i = 0; i <= len; i++)
            {
                if (--o < 0 || o + off + 1 >= length)
                    throw new DaxFormatException("PowerPacker: match outside the output");
                output[o] = output[o + off + 1];
            }
            if (o <= 0)
                break;
        }
        return output;
    }
}
