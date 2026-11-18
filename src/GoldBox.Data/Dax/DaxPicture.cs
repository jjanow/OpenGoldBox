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
    /// <summary>The 8-byte CGA colour map of the header (kept so the block can be written back).</summary>
    public byte[] Cga { get; init; } = new byte[8];

    private DaxPicture(int w, int h, int x, int y, List<byte[]> items)
    {
        Width = w;
        Height = h;
        X = x;
        Y = y;
        Items = items;
    }

    /// <summary>A picture from its parts (width a multiple of 8; each item Width × Height indices 0..15).</summary>
    public static DaxPicture Create(int width, int height, int x, int y, IEnumerable<byte[]> items, byte[]? cga = null,
        uint? delay = null)
    {
        if (width % 8 != 0 || width < 0 || height < 0)
            throw new DaxFormatException($"picture size {width}×{height}: width must be a multiple of 8");
        var list = items.ToList();
        if (list.Any(i => i.Length != width * height))
            throw new DaxFormatException("picture item of the wrong size");
        return new DaxPicture(width, height, x, y, list) { Cga = cga ?? new byte[8], Delay = delay };
    }

    /// <summary>The DaxBlock bytes (inverse of <see cref="Decode(ReadOnlySpan{byte}, int, out int)"/>).</summary>
    public byte[] Encode()
    {
        int w8 = Width / 8, row = w8 * 4, size = Height * row;
        var o = new byte[HeaderSize + Items.Count * size];
        BinaryPrimitives.WriteUInt16LittleEndian(o, (ushort)Height);
        BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(2), (ushort)w8);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(4), (short)X);
        BinaryPrimitives.WriteInt16LittleEndian(o.AsSpan(6), (short)Y);
        o[8] = (byte)Items.Count;
        Cga.AsSpan(0, 8).CopyTo(o.AsSpan(9));
        int p = HeaderSize;
        foreach (var px in Items)
            for (int j = 0; j < size; j++)
                o[p++] = (byte)(px[2 * j] << 4 | px[2 * j + 1] & 15);
        return o;
    }

    /// <summary>An animation block (frame count, then per frame the u32 delay and the DaxBlock).</summary>
    public static byte[] EncodeAnimation(IReadOnlyList<DaxPicture> frames)
    {
        var o = new List<byte> { (byte)frames.Count };
        foreach (var f in frames)
        {
            var d = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(d, f.Delay ?? 0);
            o.AddRange(d);
            o.AddRange(f.Encode());
        }
        return [.. o];
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
        int row = w8 * 4;
        long size64 = (long)h * row;
        int p = offset + HeaderSize;
        if (p + n * size64 > b.Length || n > 0 && size64 * 2 > int.MaxValue)
            throw new DaxFormatException($"picture of {n} items {w8 * 8}×{h} past the end");
        int size = (int)size64;
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
        return new DaxPicture(w8 * 8, h, x, y, items) { Cga = b.Slice(offset + 9, 8).ToArray() };
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
            frames.Add(new DaxPicture(pic.Width, pic.Height, pic.X, pic.Y, [.. pic.Items]) { Delay = delay, Cga = pic.Cga });
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
