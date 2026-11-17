using System.Buffers.Binary;

namespace GoldBox.Data.Dax;

/// <summary>One index entry of a DOS DAX file (docs/re/dax-format.md "Layout").</summary>
public readonly record struct DaxEntry(byte Id, int Offset, ushort RawSize, ushort CompSize);

public sealed class DaxFormatException(string message) : Exception(message);

/// <summary>
/// DOS Gold Box DAX container (docs/re/dax-format.md): u16 index length H, H/9 entries of
/// (u8 id, u32 offset, u16 raw size, u16 packed size), then the packed blocks. Lookup is by id.
/// </summary>
public sealed class DaxFile
{
    private readonly byte[] _data;
    private readonly int _base;

    public string? Path { get; }
    public IReadOnlyList<DaxEntry> Entries { get; }

    public DaxFile(byte[] data, string? path = null)
    {
        _data = data;
        Path = path;
        if (data.Length < 2)
            throw new DaxFormatException("file too short");
        int h = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (h == 0 || h % 9 != 0 || 2 + h > data.Length)
            throw new DaxFormatException($"bad index length {h}");
        _base = 2 + h;
        var list = new List<DaxEntry>(h / 9);
        for (int p = 2; p < 2 + h; p += 9)
        {
            var e = new DaxEntry(data[p], BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 1)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 5)),   // unsigned (spec: SQRPACI block 1)
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 7)));
            if (e.Offset < 0 || (long)_base + e.Offset + e.CompSize > data.Length)
                throw new DaxFormatException($"block {e.Id} lies outside the file");
            list.Add(e);
        }
        Entries = list;
    }

    public static DaxFile Open(string path) => new(File.ReadAllBytes(path), path);

    public bool Contains(int id) => Entries.Any(e => e.Id == id);

    /// <summary>Decoded block <paramref name="id"/>, or null when the file has none.</summary>
    public byte[]? Block(int id)
    {
        foreach (var e in Entries)
            if (e.Id == id)
                return Decode(e);
        return null;
    }

    public byte[] Decode(DaxEntry e) => Unrle(_data.AsSpan(_base + e.Offset, e.CompSize), e.RawSize);

    /// <summary>
    /// Run-length decoding: signed control byte c ≥ 0 copies the next c+1 bytes; c &lt; 0 repeats the next byte −c
    /// times. Control bytes 0x7F and 0x80 hang the original decoder and are rejected. Output must be exactly
    /// <paramref name="rawSize"/> bytes.
    /// </summary>
    public static byte[] Unrle(ReadOnlySpan<byte> comp, int rawSize)
    {
        var outBuf = new byte[rawSize];
        int o = 0, i = 0;
        while (i < comp.Length)
        {
            byte c = comp[i];
            if (c is 0x7F or 0x80)
                throw new DaxFormatException($"control byte {c:X2} (hangs the original decoder)");
            if (c < 0x80)
            {
                int n = c + 1;
                if (i + 1 + n > comp.Length || o + n > rawSize)
                    throw new DaxFormatException("literal run past the end");
                comp.Slice(i + 1, n).CopyTo(outBuf.AsSpan(o));
                o += n;
                i += n + 1;
            }
            else
            {
                int n = 256 - c;
                if (i + 1 >= comp.Length || o + n > rawSize)
                    throw new DaxFormatException("repeat run past the end");
                outBuf.AsSpan(o, n).Fill(comp[i + 1]);
                o += n;
                i += 2;
            }
        }
        if (o != rawSize)
            throw new DaxFormatException($"decoded {o} bytes, index says {rawSize}");
        return outBuf;
    }
}
