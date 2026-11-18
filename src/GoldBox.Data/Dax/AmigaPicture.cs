using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>
/// Amiga Pool of Radiance picture blocks (docs/re/por-art-formats.md §3 "Amiga block"): big-endian u16 height, u16
/// width (8-px units), i16 x, i16 y, u16 item count, u16 plane bytes (= height × row bytes, row bytes = width rounded
/// up to even), then ONE 4-plane bitmap holding every item stacked top to bottom, plane 0 first (plane k = bit k).
/// The animation wrapper is u32 frame count, then per frame u32 delay and a block. Decoded to
/// <see cref="DaxPicture"/>s (colour indices 0–15; no CGA map).
/// </summary>
public static class AmigaPicture
{
    public const int HeaderSize = 12;

    /// <summary>Decodes one block at <paramref name="offset"/>; <paramref name="end"/> is the byte after it.</summary>
    public static DaxPicture Decode(ReadOnlySpan<byte> b, int offset, out int end, uint? delay = null)
    {
        if (b.Length - offset < HeaderSize)
            throw new DaxFormatException("Amiga picture header past the end");
        int h = BinaryPrimitives.ReadUInt16BigEndian(b[offset..]);
        int w8 = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 2)..]);
        int x = BinaryPrimitives.ReadInt16BigEndian(b[(offset + 4)..]);
        int y = BinaryPrimitives.ReadInt16BigEndian(b[(offset + 6)..]);
        int n = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 8)..]);
        int pb = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 10)..]);
        int rb = (w8 + 1) & ~1;
        if (pb != h * rb)
            throw new DaxFormatException($"Amiga plane size {pb} is not {h} × {rb}");
        long ps = (long)pb * n;
        int p = offset + HeaderSize;
        if (p + 4 * ps > b.Length)
            throw new DaxFormatException($"Amiga picture of {n} items {w8 * 8}×{h} past the end");
        int w = w8 * 8;
        var items = new List<byte[]>(n);
        for (int i = 0; i < n; i++)
        {
            var px = new byte[w * h];
            for (int r = 0; r < h; r++)
            {
                int row = (i * h + r) * rb;
                for (int cx = 0; cx < w; cx++)
                {
                    int m = 0x80 >> (cx & 7), v = 0;
                    for (int k = 0; k < 4; k++)
                        if ((b[(int)(p + k * ps + row + (cx >> 3))] & m) != 0)
                            v |= 1 << k;
                    px[r * w + cx] = (byte)v;
                }
            }
            items.Add(px);
        }
        end = (int)(p + 4 * ps);
        return DaxPicture.Create(w, h, x, y, items, delay: delay);
    }

    /// <summary>The block bytes of one picture (inverse of <see cref="Decode"/>; a row's pad byte is written as 0).</summary>
    public static byte[] Encode(DaxPicture pic)
    {
        int w8 = pic.Width / 8, rb = (w8 + 1) & ~1, h = pic.Height, n = pic.Items.Count, pb = h * rb, ps = pb * n;
        var o = new byte[HeaderSize + 4 * ps];
        BinaryPrimitives.WriteUInt16BigEndian(o, (ushort)h);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(2), (ushort)w8);
        BinaryPrimitives.WriteInt16BigEndian(o.AsSpan(4), (short)pic.X);
        BinaryPrimitives.WriteInt16BigEndian(o.AsSpan(6), (short)pic.Y);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(8), (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(10), (ushort)pb);
        for (int i = 0; i < n; i++)
        {
            var px = pic.Items[i];
            for (int r = 0; r < h; r++)
            {
                int row = (i * h + r) * rb;
                for (int cx = 0; cx < pic.Width; cx++)
                {
                    int v = px[r * pic.Width + cx];
                    for (int k = 0; k < 4; k++)
                        if ((v >> k & 1) != 0)
                            o[HeaderSize + k * ps + row + (cx >> 3)] |= (byte)(0x80 >> (cx & 7));
                }
            }
        }
        return o;
    }

    /// <summary>The animation wrapper test: u32 frame count (1–255), then frames filling the block exactly.</summary>
    public static bool IsAnimation(ReadOnlySpan<byte> b)
    {
        if (b.Length < 20)
            return false;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(b);
        if (count is 0 or > 255)
            return false;
        long p = 4;
        for (int i = 0; i < count; i++)
        {
            if (p + 4 + HeaderSize > b.Length)
                return false;
            int n = BinaryPrimitives.ReadUInt16BigEndian(b[(int)(p + 12)..]);
            int pb = BinaryPrimitives.ReadUInt16BigEndian(b[(int)(p + 14)..]);
            p += 4 + HeaderSize + 4L * n * pb;
        }
        return p == b.Length;
    }

    /// <summary>The frames of an animation block, or the block itself as one frame (which must fill it exactly).</summary>
    public static IReadOnlyList<DaxPicture> DecodeFrames(ReadOnlySpan<byte> b)
    {
        if (!IsAnimation(b))
        {
            var pic = Decode(b, 0, out int end);
            if (end != b.Length)
                throw new DaxFormatException($"Amiga picture block has {b.Length - end} bytes after its items");
            return [pic];
        }
        uint count = BinaryPrimitives.ReadUInt32BigEndian(b);
        var frames = new List<DaxPicture>((int)count);
        int p = 4;
        for (int i = 0; i < count; i++)
            frames.Add(Decode(b, p + 4, out p, BinaryPrimitives.ReadUInt32BigEndian(b[p..])));
        return frames;
    }

    /// <summary>An animation block (frame count, then per frame the u32 delay and the block).</summary>
    public static byte[] EncodeAnimation(IReadOnlyList<DaxPicture> frames)
    {
        var o = new List<byte>();
        var u = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(u, (uint)frames.Count);
        o.AddRange(u);
        foreach (var f in frames)
        {
            BinaryPrimitives.WriteUInt32BigEndian(u, f.Delay ?? 0);
            o.AddRange(u);
            o.AddRange(Encode(f));
        }
        return [.. o];
    }

    /// <summary>
    /// Cuts each item into <paramref name="tileWidth"/>-wide tiles, left to right (Amiga 8x8d items are 16×8, two wall
    /// tiles; sqrpaci, bacpac and the combat grounds are 48×24, two 24×24 tiles). The result has DOS's item shape.
    /// </summary>
    public static DaxPicture SplitTiles(DaxPicture pic, int tileWidth)
    {
        if (tileWidth <= 0 || pic.Width % tileWidth != 0)
            throw new DaxFormatException($"{pic.Width}-px items do not split into {tileWidth}-px tiles");
        int per = pic.Width / tileWidth;
        var tiles = new List<byte[]>(pic.Items.Count * per);
        foreach (var item in pic.Items)
            for (int t = 0; t < per; t++)
            {
                var tile = new byte[tileWidth * pic.Height];
                for (int r = 0; r < pic.Height; r++)
                    item.AsSpan(r * pic.Width + t * tileWidth, tileWidth).CopyTo(tile.AsSpan(r * tileWidth));
                tiles.Add(tile);
            }
        return DaxPicture.Create(tileWidth, pic.Height, pic.X, pic.Y, tiles, delay: pic.Delay);
    }

    /// <summary>Inverse of <see cref="SplitTiles"/>: joins every <paramref name="per"/> tiles side by side into one item.</summary>
    public static DaxPicture JoinTiles(DaxPicture pic, int per)
    {
        if (per <= 0 || pic.Items.Count % per != 0)
            throw new DaxFormatException($"{pic.Items.Count} tiles do not join in groups of {per}");
        int w = pic.Width * per;
        var items = new List<byte[]>(pic.Items.Count / per);
        for (int i = 0; i < pic.Items.Count; i += per)
        {
            var item = new byte[w * pic.Height];
            for (int t = 0; t < per; t++)
                for (int r = 0; r < pic.Height; r++)
                    pic.Items[i + t].AsSpan(r * pic.Width, pic.Width).CopyTo(item.AsSpan(r * w + t * pic.Width));
            items.Add(item);
        }
        return DaxPicture.Create(w, pic.Height, pic.X, pic.Y, items, delay: pic.Delay);
    }
}
