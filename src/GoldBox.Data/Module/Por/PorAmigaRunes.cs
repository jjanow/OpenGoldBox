using System.Buffers.Binary;
using System.Text;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// The Amiga v1.0 rune font (docs/re/ecl-format.md "PROTECT", docs/re/por-art-formats.md §4 "Runes"; P, read s38 in the
/// Amiga <c>program</c>, hunks numbered from 0 in file order). PROTECT (ECL 0x3C; the dispatch at hunk 16 +0x4FBA calls
/// hunk 16 +0x4C06) draws each glyph through the glyph routine (hunk 25 +0xA4E, the copy of DOS START 0x726F) with its
/// flag clear and the colour pushed at hunk 16 +0x4CCE. With the flag clear that routine sets the rastport's font to the
/// one start-up opened from the TextAttr at hunk 2 +0x64 (hunk 1 +0x1D4: OpenDiskFont, kept at hunk 32 +0x6A), takes the
/// colour as a screen pen (below 16: pen + 16 − pen mod 2), and draws the byte with one Text call in JAM1 at
/// (column · 8, row · 8 + 6), then puts the text font back. The pen is one of registers 16–31, which start-up loads from
/// hunk 2 +0 (hunk 1 +0x210) and nothing changes later.
/// <para>The import writes the font as <c>art/amiga/protect/0/8</c> (category <c>font</c>): 256 items of 8 × 8, item c
/// being character c as Text draws it at that position, index 0 where no bit is set (JAM1 leaves those pixels alone)
/// and pen − 16 where one is; the PNG's palette is registers 16–31 from the player's <c>program</c>. Nothing of the
/// game is in this file: the code bytes below are only checked, the font and colours come from the player's disks.</para>
/// </summary>
public static class PorAmigaRunes
{
    /// <summary>The overlay's container and id: the font's name without <c>.font</c> and its height.</summary>
    public const string Container = "protect";
    public const int Id = 8;
    public const string Category = "font";

    /// <summary>The cell the glyph routine draws a character in (8 × 8 text cells; Move to row · 8 + <see cref="BaselineRow"/>).</summary>
    public const int Cell = 8, BaselineRow = 6, Characters = 256, FirstInterfacePen = 16, InterfacePens = 16;

    /// <summary>What the program says about the rune font: its name and height, the pen and the interface colours.</summary>
    public sealed record Setup(string FontName, int YSize, int Pen, byte[] InterfaceRgb);

    // (hunk, offset, bytes): the code the reading relies on, checked before anything is taken.
    private static readonly (int Hunk, int At, string Hex)[] CodeChecks =
    [
        (16, 0x4FBA, "b08166066100fc46"),          // opcode 0x3C → bsr 0x4C06 (PROTECT)
        (16, 0x4CC6, "42a72f0248780001"),          // flag 0, glyph, count 1 …
        (16, 0x4CCE, "4878"),                      // … pea colour.w
        (16, 0x4CD2, "2f012f004eba08ae"),          // row, column, jsr the stub at 0x5586
        (16, 0x5586, "4ef9"),                      // jmp hunk 25 +0xA4E (reloc below)
        (25, 0xA6C, "7001b02d001f660000fa"),       // flag ≠ 1 → 0xB6E
        (25, 0xB6E, "2f39"),                       // SetFont(rp, the font at hunk 32 +0x6A)
        (25, 0xB80, "7010ba006416"),               // pen below 16: the remap
        (25, 0xBE4, "e78072001206e7815c81"),       // x = column · 8, y = row · 8 + 6
        (25, 0xBFC, "4297"),                       // SetDrMd(rp, 0): JAM1
        (1, 0x1D4, "4879"),                        // pea TextAttr (hunk 2 +0x64)
        (1, 0x1DA, "4eba510e23c0"),                // OpenDiskFont, store at hunk 32 +0x6A
        (1, 0x210, "7e007020be806c1c"),            // 32 colour words …
        (1, 0x21C, "41f9"),                        // … from hunk 2 +0
    ];

    // (hunk, offset, target hunk, value): RELOC32 longs the reading relies on.
    private static readonly (int Hunk, int At, int Target, uint Value)[] RelocChecks =
    [
        (16, 0x5588, 25, 0xA4E),
        (25, 0xB70, 32, 0x6A),
        (1, 0x1D6, 2, 0x64),
        (1, 0x1E0, 32, 0x6A),
        (1, 0x21E, 2, 0),
    ];

    private const int TextAttrHunk = 2, TextAttrAt = 0x64, PenAt = 0x4CD0, ColourHunk = 2;

    /// <summary>The rune font's set-up read from the player's <c>program</c>, or null when its code is not laid out so.</summary>
    public static Setup? Read(byte[] program)
    {
        try
        {
            var hunks = AmigaHunks.Parse(program);
            var relocs = AmigaHunks.Relocations(program).ToHashSet();
            int? Base(int h) => h < hunks.Count && hunks[h].DataOffset is { } at ? at : null;
            bool In(int h, int at, int length) => Base(h) is not null && at >= 0 && at + length <= hunks[h].Size;
            foreach (var (h, at, hex) in CodeChecks)
            {
                var want = Convert.FromHexString(hex);
                if (!In(h, at, want.Length) || !program.AsSpan(Base(h)!.Value + at, want.Length).SequenceEqual(want))
                    return null;
            }
            foreach (var (h, at, target, value) in RelocChecks)
                if (!In(h, at, 4) || !relocs.Contains(new AmigaReloc(h, target, at))
                    || BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(h)!.Value + at)) != value)
                    return null;
            // TextAttr: ta_Name (a long into hunk 2), ta_YSize.
            int ta = Base(TextAttrHunk)!.Value + TextAttrAt;
            if (!In(TextAttrHunk, TextAttrAt, 8) || !relocs.Contains(new AmigaReloc(TextAttrHunk, TextAttrHunk, TextAttrAt)))
                return null;
            int nameAt = (int)BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(ta));
            if (!In(TextAttrHunk, nameAt, 1))
                return null;
            var name = program.AsSpan(Base(TextAttrHunk)!.Value + nameAt, hunks[TextAttrHunk].Size - nameAt);
            int z = name.IndexOf((byte)0);
            if (z <= 0)
                return null;
            int ysize = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(ta + 4));
            int pen = ScreenPen(BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(Base(16)!.Value + PenAt)));
            if (ysize != Id || pen <= FirstInterfacePen || pen >= FirstInterfacePen + InterfacePens || !In(ColourHunk, 2 * FirstInterfacePen, 2 * InterfacePens))
                return null;
            var rgb = AmigaHunks.Rgb12To24(program.AsSpan(Base(ColourHunk)!.Value + 2 * FirstInterfacePen), InterfacePens);
            return new Setup(Encoding.Latin1.GetString(name[..z]), ysize, pen, rgb);
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    /// <summary>The glyph routine's colour → pen (hunk 25 +0xB80): below 16, pen + 16 − pen mod 2; else the pen itself.</summary>
    public static int ScreenPen(int colour) => colour < 16 ? colour + 16 - colour % 2 : colour;

    /// <summary>
    /// The font file the program opens (<paramref name="fontName"/>, e.g. <c>protect.font</c>, height
    /// <paramref name="ysize"/>) under <paramref name="amigaDir"/>: the contents file of that name (any case, up to three
    /// folders deep), then its entry of that height, relative to the contents file's folder (as diskfont.library looks in
    /// FONTS:). Null when absent: the font is optional, the Client then draws the DOS runes.
    /// </summary>
    public static string? LocateFont(string amigaDir, string fontName, int ysize)
    {
        var contents = Directory.EnumerateFiles(amigaDir, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive,
        }).Where(p => Path.GetFileName(p).Equals(fontName, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).FirstOrDefault();
        if (contents is null)
            return null;
        try
        {
            foreach (var (file, size) in AmigaDiskFont.Contents(File.ReadAllBytes(contents)))
                if (size == ysize && Resolve(Path.GetDirectoryName(contents)!, file) is { } path)
                    return path;
        }
        catch (DaxFormatException)
        {
        }
        return null;
    }

    /// <summary>An Amiga path (<c>a/b</c>) under <paramref name="dir"/>, each part matched in any case.</summary>
    private static string? Resolve(string dir, string amigaPath)
    {
        string cur = dir;
        var parts = amigaPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;
            var next = (last ? Directory.EnumerateFiles(cur) : Directory.EnumerateDirectories(cur))
                .Where(p => Path.GetFileName(p).Equals(parts[i], StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null)
                return null;
            cur = next;
        }
        return parts.Length > 0 ? cur : null;
    }

    /// <summary>
    /// The 256 cells (character c = item c, <see cref="Cell"/> × <see cref="Cell"/>) as Text draws them in the glyph
    /// routine: a glyph's top line at row · 8 + 6 − baseline, its bits from x = column · 8; set bits are
    /// <paramref name="pen"/> − 16, the rest 0 (JAM1: not drawn). Null for a font Text would space otherwise
    /// (proportional, or not 8 wide).
    /// </summary>
    public static byte[][]? Cells(AmigaDiskFont font, int pen)
    {
        if ((font.Flags & AmigaDiskFont.Proportional) != 0 || font.XSize != Cell)
            return null;
        byte index = (byte)(pen - FirstInterfacePen);
        int top = BaselineRow - font.Baseline;
        var cells = new byte[Characters][];
        for (int c = 0; c < Characters; c++)
        {
            var px = new byte[Cell * Cell];
            for (int y = 0; y < font.YSize; y++)
                for (int x = 0; x < Cell; x++)
                    if (y + top is >= 0 and < Cell && font.Pixel(c, x, y))
                        px[(y + top) * Cell + x] = index;
            cells[c] = px;
        }
        return cells;
    }
}
