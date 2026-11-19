using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>One hunk of an AmigaDOS load file: its type and where its data sits in the file (null for BSS).</summary>
public readonly record struct AmigaHunk(int Type, int? DataOffset, int Size);

/// <summary>One RELOC32 entry: the long at <see cref="Offset"/> in hunk <see cref="Hunk"/> gets hunk <see cref="Target"/>'s address added.</summary>
public readonly record struct AmigaReloc(int Hunk, int Target, int Offset);

/// <summary>
/// Minimal AmigaDOS load-file (hunk executable) reader: the list of CODE, DATA and BSS hunks in file order, with the
/// file offset of each hunk's data. Enough to find tables in a program by hunk and offset (the Amiga PoR palettes,
/// docs/re/por-art-formats.md §4). Own code from the AmigaDOS hunk format.
/// </summary>
public static class AmigaHunks
{
    public const int HunkHeader = 0x3F3, Code = 0x3E9, Data = 0x3EA, Bss = 0x3EB, Reloc32 = 0x3EC, End = 0x3F2;

    public static IReadOnlyList<AmigaHunk> Parse(ReadOnlySpan<byte> d) => Walk(d, null);

    /// <summary>Every RELOC32 entry of the file, in file order.</summary>
    public static IReadOnlyList<AmigaReloc> Relocations(ReadOnlySpan<byte> d)
    {
        var r = new List<AmigaReloc>();
        Walk(d, r);
        return r;
    }

    private static IReadOnlyList<AmigaHunk> Walk(ReadOnlySpan<byte> d, List<AmigaReloc>? relocs)
    {
        int o = 0;
        uint L(ReadOnlySpan<byte> s, int at) =>
            at + 4 <= s.Length ? BinaryPrimitives.ReadUInt32BigEndian(s[at..]) : throw new DaxFormatException("hunk file past the end");
        if (L(d, 0) != HunkHeader)
            throw new DaxFormatException("not an AmigaDOS hunk executable");
        o = 4;
        while (L(d, o) != 0)                      // resident library names
            o += 4 + 4 * (int)L(d, o);
        o += 4;
        int first = (int)L(d, o + 4), last = (int)L(d, o + 8);
        o += 12 + 4 * (last - first + 1);
        var hunks = new List<AmigaHunk>();
        while (o + 4 <= d.Length)
        {
            int t = (int)(L(d, o) & 0x3FFFFFFF);
            o += 4;
            switch (t)
            {
                case Code or Data:
                {
                    int n = (int)L(d, o);
                    hunks.Add(new AmigaHunk(t, o + 4, n * 4));
                    o += 4 + n * 4;
                    break;
                }
                case Bss:
                    hunks.Add(new AmigaHunk(t, null, (int)L(d, o) * 4));
                    o += 4;
                    break;
                case Reloc32:
                    while (true)
                    {
                        int n = (int)L(d, o);
                        o += 4;
                        if (n == 0)
                            break;
                        if (relocs is not null)
                        {
                            int target = (int)L(d, o);
                            for (int k = 0; k < n; k++)
                                relocs.Add(new AmigaReloc(hunks.Count - 1, target, (int)L(d, o + 4 + 4 * k)));
                        }
                        o += 4 + 4 * n;
                    }
                    break;
                case End:
                    break;
                default:
                    throw new DaxFormatException($"unhandled hunk type 0x{t:X} at {o - 4}");
            }
        }
        return hunks;
    }

    /// <summary>12-bit Amiga colour words (0RGB) as 8-bit RGB triplets (each nibble × 17).</summary>
    public static byte[] Rgb12To24(ReadOnlySpan<byte> bigEndianWords, int count)
    {
        var o = new byte[count * 3];
        for (int i = 0; i < count; i++)
        {
            int w = BinaryPrimitives.ReadUInt16BigEndian(bigEndianWords[(2 * i)..]);
            o[3 * i] = (byte)((w >> 8 & 15) * 17);
            o[3 * i + 1] = (byte)((w >> 4 & 15) * 17);
            o[3 * i + 2] = (byte)((w & 15) * 17);
        }
        return o;
    }
}
