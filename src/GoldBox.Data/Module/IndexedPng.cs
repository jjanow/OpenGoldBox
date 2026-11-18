using System.Buffers.Binary;
using System.IO.Compression;

namespace GoldBox.Data.Module;

/// <summary>
/// Indexed-colour PNG (colour type 3, bit depth 8) for module art (docs/module-format.md §10): pixels stay palette
/// indices, never flattened to RGB. Writes with filter 0; reads every standard filter, bit depths 1, 2, 4 and 8, and no
/// interlace. Own code (PNG spec, zlib via <see cref="ZLibStream"/>).
/// </summary>
public static class IndexedPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The standard EGA colours (the hardware's 16 default palette entries), as RGB.</summary>
    public static readonly byte[] EgaPalette =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0x00, 0x00, 0xAA, 0xAA,
        0xAA, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0xAA, 0x55, 0x00, 0xAA, 0xAA, 0xAA,
        0x55, 0x55, 0x55, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0x55, 0x55, 0xFF, 0xFF,
        0xFF, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF,
    ];

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> pixels, byte[] paletteRgb)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException("pixel count does not match the size");
        using var ms = new MemoryStream();
        ms.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 3;
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "PLTE", paletteRgb);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, leaveOpen: true))
                for (int y = 0; y < height; y++)
                {
                    zs.WriteByte(0);
                    zs.Write(pixels.Slice(y * width, width));
                }
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual(Signature))
            throw new FormatException("not a PNG file");
        int p = 8, width = 0, height = 0, depth = 0;
        using var idat = new MemoryStream();
        while (p + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png[p..]);
            var type = System.Text.Encoding.ASCII.GetString(png.Slice(p + 4, 4));
            if (len < 0 || p + 12 + len > png.Length)
                throw new FormatException("PNG chunk past the end");
            var data = png.Slice(p + 8, len);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8];
                    if (data[9] != 3 || data[12] != 0 || depth is not (1 or 2 or 4 or 8))
                        throw new FormatException("module art must be an indexed, non-interlaced PNG");
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }
            p += 12 + len;
            if (type == "IEND")
                break;
        }
        int stride = (width * depth + 7) / 8;
        var raw = new byte[(stride + 1) * height];
        using (var zs = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
            zs.ReadExactly(raw);
        var rows = new byte[stride * height];
        int bpp = 1;
        for (int y = 0; y < height; y++)
        {
            int f = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var cur = rows.AsSpan(y * stride, stride);
            var prev = y > 0 ? rows.AsSpan((y - 1) * stride, stride) : new byte[stride];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                cur[x] = (byte)(src[x] + f switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new FormatException($"PNG filter {f}"),
                });
            }
        }
        var px = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int bit = x * depth;
                int v = rows[y * stride + bit / 8];
                px[y * width + x] = (byte)(v >> (8 - depth - bit % 8) & ((1 << depth) - 1));
            }
        return (width, height, px);
    }

    /// <summary>The PNG's palette (PLTE chunk) as RGB triplets, or null when it has none.</summary>
    public static byte[]? Palette(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual(Signature))
            throw new FormatException("not a PNG file");
        for (int p = 8; p + 8 <= png.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png[p..]);
            if (len < 0 || p + 12 + len > png.Length)
                throw new FormatException("PNG chunk past the end");
            var type = System.Text.Encoding.ASCII.GetString(png.Slice(p + 4, 4));
            if (type == "PLTE")
                return png.Slice(p + 8, len).ToArray();
            if (type is "IDAT" or "IEND")
                return null;
            p += 12 + len;
        }
        return null;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, head[4..]);
        s.Write(head);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, head[4..]), data) ^ 0xFFFFFFFF;
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> b)
    {
        foreach (var x in b)
            crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
