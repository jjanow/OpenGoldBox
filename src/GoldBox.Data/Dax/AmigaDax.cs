using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>One index entry of an Amiga DAX file (docs/re/por-art-formats.md §2 "Amiga DAX").</summary>
public readonly record struct AmigaDaxEntry(ushort Id, uint Offset, ushort CompSize, ushort RawSize);

/// <summary>
/// Amiga Pool of Radiance DAX container (Ubi Soft 1990; docs/re/por-art-formats.md §2): big-endian u16 index length H,
/// H/10 entries of (u16 id, u32 offset, u16 packed size, u16 raw size), then the blocks, each a ByteKiller stream.
/// </summary>
public sealed class AmigaDaxFile
{
    private readonly byte[] _data;
    private readonly int _base;

    public string? Path { get; }
    public IReadOnlyList<AmigaDaxEntry> Entries { get; }

    public AmigaDaxFile(byte[] data, string? path = null)
    {
        _data = data;
        Path = path;
        if (data.Length < 2)
            throw new DaxFormatException("file too short");
        int h = BinaryPrimitives.ReadUInt16BigEndian(data);
        if (h == 0 || h % 10 != 0 || 2 + h > data.Length)
            throw new DaxFormatException($"bad Amiga index length {h}");
        _base = 2 + h;
        var list = new List<AmigaDaxEntry>(h / 10);
        for (int p = 2; p < 2 + h; p += 10)
        {
            var e = new AmigaDaxEntry(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p)),
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p + 2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 6)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 8)));
            if ((long)_base + e.Offset + e.CompSize > data.Length)
                throw new DaxFormatException($"block {e.Id} lies outside the file");
            list.Add(e);
        }
        Entries = list;
    }

    public static AmigaDaxFile Open(string path) => new(File.ReadAllBytes(path), path);

    public byte[] Decode(AmigaDaxEntry e)
    {
        var o = ByteKiller(_data.AsSpan(_base + (int)e.Offset, e.CompSize));
        if (o.Length != e.RawSize)
            throw new DaxFormatException($"block {e.Id}: {o.Length} bytes decoded, the index says {e.RawSize}");
        return o;
    }

    /// <summary>
    /// ByteKiller decrunch, read from the end (docs/re/por-art-formats.md §2): last long = output size, the one before
    /// it the checksum, then the first bit buffer (highest set bit = sentinel). Longs are read towards the start and
    /// bits taken LSB first; a refilled long gets bit 31 set. Codes: <c>00 nnn</c> n+1 literals; <c>01 o8</c> copy 2;
    /// <c>1 00 o9</c> copy 3; <c>1 01 o10</c> copy 4; <c>1 10 n8 o12</c> copy n+1; <c>1 11 n8</c> n+9 literals. A copy
    /// reads at write position + offset. The XOR of every long read equals the checksum.
    /// </summary>
    public static byte[] ByteKiller(ReadOnlySpan<byte> stream)
    {
        if (stream.Length % 4 != 0 || stream.Length < 12)
            throw new DaxFormatException($"ByteKiller stream of {stream.Length} bytes");
        var comp = stream.ToArray();
        int pos = comp.Length / 4 - 1;
        uint L(int i) => BinaryPrimitives.ReadUInt32BigEndian(comp.AsSpan(i * 4));
        uint size = L(pos--), checksum = L(pos--), cur = L(pos--);
        if (size > 1 << 24)
            throw new DaxFormatException($"ByteKiller size {size}");
        uint x = checksum ^ cur;
        var o = new byte[size];
        int w = (int)size;

        int Bit()
        {
            int b = (int)(cur & 1);
            cur >>= 1;
            if (cur == 0)
            {
                if (pos < 0)
                    throw new DaxFormatException("ByteKiller stream underrun");
                uint v = L(pos--);
                x ^= v;
                b = (int)(v & 1);
                cur = v >> 1 | 0x80000000;
            }
            return b;
        }
        int Bits(int k)
        {
            int v = 0;
            for (int i = 0; i < k; i++)
                v = v << 1 | Bit();
            return v;
        }
        void Literal(int k)
        {
            if (k > w)
                throw new DaxFormatException("ByteKiller literal past the start");
            for (int i = 0; i < k; i++)
                o[--w] = (byte)Bits(8);
        }
        void Copy(int k, int off)
        {
            if (k > w)
                throw new DaxFormatException("ByteKiller copy past the start");
            for (int i = 0; i < k; i++)
            {
                w--;
                if (w + off >= o.Length)
                    throw new DaxFormatException("ByteKiller copy past the end");
                o[w] = o[w + off];
            }
        }

        while (w > 0)
        {
            if (Bit() == 0)
            {
                if (Bit() == 0)
                    Literal(Bits(3) + 1);
                else
                    Copy(2, Bits(8));
            }
            else
            {
                switch (Bits(2))
                {
                    case 3: Literal(Bits(8) + 9); break;
                    case 2: { int k = Bits(8) + 1; Copy(k, Bits(12)); break; }
                    case 1: Copy(4, Bits(10)); break;
                    default: Copy(3, Bits(9)); break;
                }
            }
        }
        if (x != 0)
            throw new DaxFormatException("ByteKiller checksum mismatch");
        return o;
    }
}
