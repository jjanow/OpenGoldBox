using System.Buffers.Binary;
using GoldBox.Core.Explore;

namespace GoldBox.Data.Por;

/// <summary>DS offsets of the overland's tables (v1.3: tiles 0x3648, special locations 0x0C36, columns 0x3C76).</summary>
public sealed record PorOverlandOffsets(int Tiles, int Sites, int Columns);

/// <summary>
/// PoR's overland (wilderness) tile map and special locations, read from the player's <c>START.EXE</c> data segment
/// at the offsets the <c>GAME.OVR</c> code uses (docs/re/geo-format.md "Wilderness (overland)"). Nothing from the
/// program is copied into our code.
/// </summary>
public static class PorOverland
{
    /// <summary>Row length: the code's stride (<c>mov dx,2Ch / mul dx</c>, part of <see cref="TilesPattern"/>).</summary>
    public const int Width = 44;
    /// <summary>Rows: the square-ahead routine clamps y to 0–35 (GAME.OVR 0x16F4D) and the 5 × 5 view its top row to 0–31 (0x2DE06).</summary>
    public const int Height = 36;
    /// <summary>Special locations: the draw loop runs indices 1–9 (GAME.OVR 0x2DFFB); 6 bytes each (x, y, tile words).</summary>
    public const int SiteCount = OverlandMap.SiteRuleCount, SiteSize = 6;
    /// <summary>The wilderness scripts and their view modes (DS:49FA; GAME.OVR 0x3F4C–0x3F99: blocks 25, 26, 27 → 2, 3, 4).</summary>
    public const int FirstScript = 25, LastScript = 27;

    /// <summary><c>mov dx,2Ch / mul dx / mov di,ax / add di,cx / mov al,[di+imm]</c>: a tile read (0xD7D9, 0x16F79, 0x2DE78).</summary>
    private static readonly byte[] TilesPattern = [0xBA, 0x2C, 0x00, 0xF7, 0xE2, 0x8B, 0xF8, 0x03, 0xF9, 0x8A, 0x85];
    /// <summary>
    /// <c>shl di,1 / mov si,di / shl di,1 / add di,si / mov ax,[di+imm] / xor dx,dx</c>: index × 6 and the record's x
    /// word read with a 1-based index (0x2DE9A), so the first record is at imm + 6.
    /// </summary>
    private static readonly byte[] SitesPattern = [0xD1, 0xE7, 0x8B, 0xF7, 0xD1, 0xE7, 0x01, 0xF7, 0x8B, 0x85];
    /// <summary><c>mov al,[49FA] / xor ah,ah / mov di,ax / mov al,[di+imm]</c>: the view mode's first column (0x16F57, 0x2DB8D).</summary>
    private static readonly byte[] ColumnsPattern = [0xA0, 0xFA, 0x49, 0x30, 0xE4, 0x8B, 0xF8, 0x8A, 0x85];

    /// <summary>The view mode (DS:49FA) a wilderness script switches to outdoors, or null for other scripts.</summary>
    public static int? ViewMode(int script) => script is >= FirstScript and <= LastScript ? script - FirstScript + 2 : null;

    /// <summary>The tables' DS offsets from GAME.OVR's code, or null when a pattern is missing or its reads disagree.</summary>
    public static PorOverlandOffsets? Locate(byte[]? overlay)
    {
        if (overlay is null)
            return null;
        var tiles = Immediates(overlay, TilesPattern).Distinct().ToList();
        var cols = Immediates(overlay, ColumnsPattern).Distinct().ToList();
        // The x read is the one followed by xor dx,dx (31 D2); the y read follows at +2.
        var siteReads = Immediates(overlay, SitesPattern, then: [0x31, 0xD2]).ToList();
        if (tiles.Count != 1 || cols.Count != 1 || siteReads.Count < 2 || siteReads[1] != siteReads[0] + 2)
            return null;
        return new PorOverlandOffsets(tiles[0], siteReads[0] + SiteSize, cols[0]);
    }

    private static List<int> Immediates(byte[] code, byte[] pattern, byte[]? then = null)
    {
        var found = new List<int>();
        int from = 0;
        while (from < code.Length)
        {
            int i = code.AsSpan(from).IndexOf(pattern);
            if (i < 0)
                break;
            int at = from + i + pattern.Length;
            if (at + 2 + (then?.Length ?? 0) <= code.Length
                && (then is null || code.AsSpan(at + 2, then.Length).SequenceEqual(then)))
                found.Add(BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(at)));
            from += i + 1;
        }
        return found;
    }

    /// <summary>The overland from the data segment at <paramref name="o"/>, or null when the tables run past it.</summary>
    public static OverlandMap? Read(ReadOnlySpan<byte> ds, PorOverlandOffsets o)
    {
        if (o.Tiles + Width * Height > ds.Length || o.Sites + SiteCount * SiteSize > ds.Length)
            return null;
        var sites = new OverlandSite[SiteCount];
        for (int i = 0; i < SiteCount; i++)
        {
            var r = ds.Slice(o.Sites + i * SiteSize, SiteSize);
            sites[i] = new OverlandSite(BinaryPrimitives.ReadUInt16LittleEndian(r), BinaryPrimitives.ReadUInt16LittleEndian(r[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(r[4..]));
        }
        return new OverlandMap(Width, Height, ds.Slice(o.Tiles, Width * Height).ToArray(), sites);
    }

    /// <summary>The special locations as the program stores them (6 bytes each).</summary>
    public static byte[] SiteBytes(IReadOnlyList<OverlandSite> sites)
    {
        var b = new byte[sites.Count * SiteSize];
        for (int i = 0; i < sites.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * SiteSize), sites[i].X);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * SiteSize + 2), sites[i].Y);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * SiteSize + 4), sites[i].Tile);
        }
        return b;
    }

    /// <summary>
    /// The overland column where wilderness script <paramref name="script"/>'s x = 0 sits (DS:3C76[view mode]), or null
    /// for another script or a table past the data segment.
    /// </summary>
    public static int? Column(ReadOnlySpan<byte> ds, PorOverlandOffsets o, int script) =>
        ViewMode(script) is { } m && o.Columns + m < ds.Length ? ds[o.Columns + m] : null;
}
