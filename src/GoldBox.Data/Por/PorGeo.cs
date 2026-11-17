using System.Buffers.Binary;
using GoldBox.Core.Explore;

namespace GoldBox.Data.Por;

/// <summary>
/// GEO map blocks of PoR DOS (docs/re/geo-format.md): exactly 1026 bytes, u16 0x0400, then the four 256-byte planes.
/// The engine refuses any other size (Load3DMap, "Unable to load geo").
/// </summary>
public static class PorGeo
{
    public const int BlockSize = 2 + AreaMap.PlanesLength;

    public static AreaMap Parse(ReadOnlySpan<byte> block, int id = -1)
    {
        if (block.Length != BlockSize)
            throw new FormatException($"GEO block {id} is {block.Length} bytes, expected {BlockSize}");
        int header = BinaryPrimitives.ReadUInt16LittleEndian(block);
        if (header != AreaMap.PlanesLength)
            throw new FormatException($"GEO block {id} header is 0x{header:X4}, expected 0x0400");
        return new AreaMap(block[2..], id);
    }

    /// <summary>Map <paramref name="id"/> from <c>GEO&lt;disk&gt;.DAX</c>.</summary>
    public static AreaMap Load(PorGameDirectory dir, int disk, int id)
    {
        var dax = dir.Dax($"GEO{disk}.DAX") ?? throw new FileNotFoundException($"GEO{disk}.DAX not in {dir.Root}");
        var block = dax.Block(id) ?? throw new FormatException($"GEO{disk}.DAX has no block {id}");
        return Parse(block, id);
    }

    /// <summary>The disk whose GEO file holds map <paramref name="id"/> (ids are unique over GEO1..8), or null.</summary>
    public static int? DiskOf(PorGameDirectory dir, int id)
    {
        for (int d = 1; d <= 8; d++)
            if (dir.Dax($"GEO{d}.DAX") is { } f && f.Contains(id))
                return d;
        return null;
    }
}
