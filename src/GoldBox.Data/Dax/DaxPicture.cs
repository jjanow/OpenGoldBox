using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>
/// A DOS picture block ("DaxBlock", docs/re/por-art-formats.md §3): u16 height (px), u16 width (8-px units), i16 x,
/// i16 y, u8 item count, 8-byte CGA map, then the items, each height × width·4 bytes of packed 4-bit EGA indices (high
/// nibble = left pixel). Items are unpacked to one byte per pixel.
/// </summary>
public sealed class DaxPicture
{
    public const int HeaderSize = 17;

    public int Width { get; }
    public int Height { get; }
    public int X { get; }
    public int Y { get; }
    /// <summary>The items, each Width × Height EGA indices, row-major.</summary>
    public IReadOnlyList<byte[]> Items { get; }
    /// <summary>The frame delay from the animation wrapper, when the block had one ([?] unit).</summary>
    public uint? Delay { get; init; }

    private DaxPicture(int w, int h, int x, int y, List<byte[]> items)
    {
        Width = w;
        Height = h;
        X = x;
        Y = y;
        Items = items;
    }

    /// <summary>Decodes one DaxBlock at <paramref name="offset"/>; <paramref name="end"/> is the byte after it.</summary>
    public static DaxPicture Decode(ReadOnlySpan<byte> b, int offset, out int end)
    {
        if (b.Length - offset < HeaderSize)
            throw new DaxFormatException("picture header past the end");
        int h = BinaryPrimitives.ReadUInt16LittleEndian(b[offset..]);
        int w8 = BinaryPrimitives.ReadUInt16LittleEndian(b[(offset + 2)..]);
        int x = BinaryPrimitives.ReadInt16LittleEndian(b[(offset + 4)..]);
        int y = BinaryPrimitives.ReadInt16LittleEndian(b[(offset + 6)..]);
        int n = b[offset + 8];
        int row = w8 * 4, size = h * row;
        int p = offset + HeaderSize;
        if (p + (long)n * size > b.Length)
            throw new DaxFormatException($"picture of {n} items {w8 * 8}×{h} past the end");
        var items = new List<byte[]>(n);
        for (int i = 0; i < n; i++, p += size)
        {
            var px = new byte[w8 * 8 * h];
            var src = b.Slice(p, size);
            for (int j = 0; j < size; j++)
            {
                px[2 * j] = (byte)(src[j] >> 4);
                px[2 * j + 1] = (byte)(src[j] & 15);
            }
            items.Add(px);
        }
        end = p;
        return new DaxPicture(w8 * 8, h, x, y, items);
    }

    /// <summary>A whole block that is exactly one DaxBlock (8X8D, CPIC, …).</summary>
    public static DaxPicture Decode(ReadOnlySpan<byte> block)
    {
        var pic = Decode(block, 0, out int end);
        if (end != block.Length)
            throw new DaxFormatException($"picture block has {block.Length - end} bytes after its items");
        return pic;
    }

    /// <summary>
    /// The animation wrapper test (PIC, SPRIT): u8 frame count, then per frame u32 delay + a DaxBlock, filling the
    /// block exactly.
    /// </summary>
    public static bool IsAnimation(ReadOnlySpan<byte> b)
    {
        if (b.Length < 22 || b[0] == 0)
            return false;
        long p = 1;
        for (int i = 0; i < b[0]; i++)
        {
            if (p + 21 > b.Length)
                return false;
            int h = BinaryPrimitives.ReadUInt16LittleEndian(b[(int)(p + 4)..]);
            int w8 = BinaryPrimitives.ReadUInt16LittleEndian(b[(int)(p + 6)..]);
            p += 4 + HeaderSize + (long)b[(int)(p + 12)] * h * w8 * 4;
        }
        return p == b.Length;
    }

    /// <summary>The frames of an animation block, or the block itself as one frame.</summary>
    public static IReadOnlyList<DaxPicture> DecodeFrames(ReadOnlySpan<byte> b)
    {
        if (!IsAnimation(b))
            return [Decode(b)];
        var frames = new List<DaxPicture>(b[0]);
        int p = 1;
        for (int i = 0; i < b[0]; i++)
        {
            uint delay = BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
            var pic = Decode(b, p + 4, out p);
            frames.Add(new DaxPicture(pic.Width, pic.Height, pic.X, pic.Y, [.. pic.Items]) { Delay = delay });
        }
        return frames;
    }

    /// <summary>All items of an 8-pixel-wide picture laid out as consecutive 8×8 tiles (8X8D symbol blocks).</summary>
    public byte[] AsTiles()
    {
        if (Width != 8 || Height != 8)
            throw new DaxFormatException($"8×8 tiles expected, picture is {Width}×{Height}");
        var o = new byte[Items.Count * 64];
        for (int i = 0; i < Items.Count; i++)
            Items[i].CopyTo(o, i * 64);
        return o;
    }
}
