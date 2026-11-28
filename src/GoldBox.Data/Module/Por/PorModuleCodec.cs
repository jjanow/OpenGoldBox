using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// PoR's DAX blocks ↔ module documents (docs/module-format.md §16). Both directions live here: the importer turns
/// blocks into documents, the engine side turns documents back into the bytes Core consumes, and the round-trip
/// test checks that the second gives the original block byte for byte.
/// </summary>
public static class PorModuleCodec
{
    // ---- scripts (§8) ----------------------------------------------------------------------------------------------

    /// <summary>An ECL block → the bytecode the VM loads at $9900 and the sidecar with the dropped 2-byte prefix.</summary>
    public static (byte[] Ecl, ScriptSidecar Sidecar) SplitScript(byte[] block, int disk, int id)
    {
        if (block.Length < EclLayout.Prefix)
            throw new FormatException($"ECL{disk} block {id} is shorter than its prefix");
        return (block[EclLayout.Prefix..], new ScriptSidecar
        {
            Prefix = block[..EclLayout.Prefix],
            Meta = new ResourceMeta { Disk = disk },
            Source = new SourceRef { Container = $"ECL{disk}.DAX", Block = id },
        });
    }

    /// <summary>The original block: sidecar prefix + bytecode.</summary>
    public static byte[] ScriptBlock(byte[] ecl, ScriptSidecar sidecar) => [.. sidecar.Prefix, .. ecl];

    // ---- maps (§7) -------------------------------------------------------------------------------------------------

    /// <summary>Layer names of a PoR map: wall type and barrier per face, and the event byte.</summary>
    public static readonly string[] MapLayers =
        ["wallN", "wallE", "wallS", "wallW", "barrierN", "barrierE", "barrierS", "barrierW", "event"];

    /// <summary>
    /// A GEO block → map document. The layers hold the stored bits (geo-format.md "Layout"), not what the engine's
    /// accessors return (a face with no wall reads as passable whatever its barrier bits say), so the block rebuilds.
    /// </summary>
    public static MapDocument MapFromGeo(byte[] block, int disk, int id)
    {
        if (block.Length != PorGeo.BlockSize)
            throw new FormatException($"GEO{disk} block {id} is {block.Length} bytes, expected {PorGeo.BlockSize}");
        const int n = AreaMap.PlaneSize;
        var p = block.AsSpan(2);
        var layers = MapLayers.ToDictionary(l => l, _ => new int[n]);
        for (int i = 0; i < n; i++)
        {
            layers["wallN"][i] = p[i] >> 4;
            layers["wallE"][i] = p[i] & 15;
            layers["wallS"][i] = p[n + i] >> 4;
            layers["wallW"][i] = p[n + i] & 15;
            layers["event"][i] = p[2 * n + i];
            byte b = p[3 * n + i];
            layers["barrierN"][i] = b & 3;
            layers["barrierE"][i] = (b >> 2) & 3;
            layers["barrierS"][i] = (b >> 4) & 3;
            layers["barrierW"][i] = (b >> 6) & 3;
        }
        return new MapDocument
        {
            Width = AreaMap.Size,
            Height = AreaMap.Size,
            Edges = "wrap",
            Layers = layers,
            Raw = new MapRaw { Header = block[..2] },
            Meta = new ResourceMeta { Disk = disk },
            Source = new SourceRef { Container = $"GEO{disk}.DAX", Block = id },
        };
    }

    /// <summary>The four planes of a 16 × 16 PoR map document (1024 bytes, as <see cref="AreaMap"/> holds them).</summary>
    public static byte[] MapPlanes(MapDocument m)
    {
        if (m.Width != AreaMap.Size || m.Height != AreaMap.Size)
            throw new FormatException($"a PoR map is 16 × 16, not {m.Width} × {m.Height}");
        const int n = AreaMap.PlaneSize;
        int[] L(string name) => m.Layers.TryGetValue(name, out var a) && a.Length == n
            ? a : throw new FormatException($"map layer {name} missing or not {n} values");
        var wn = L("wallN"); var we = L("wallE"); var ws = L("wallS"); var ww = L("wallW");
        var bn = L("barrierN"); var be = L("barrierE"); var bs = L("barrierS"); var bw = L("barrierW");
        var ev = L("event");
        var planes = new byte[AreaMap.PlanesLength];
        for (int i = 0; i < n; i++)
        {
            planes[i] = (byte)((wn[i] & 15) << 4 | (we[i] & 15));
            planes[n + i] = (byte)((ws[i] & 15) << 4 | (ww[i] & 15));
            planes[2 * n + i] = (byte)ev[i];
            planes[3 * n + i] = (byte)((bn[i] & 3) | (be[i] & 3) << 2 | (bs[i] & 3) << 4 | (bw[i] & 3) << 6);
        }
        return planes;
    }

    /// <summary>The original GEO block: header bytes + planes.</summary>
    public static byte[] GeoBlock(MapDocument m) => [.. m.Raw?.Header ?? [0x00, 0x04], .. MapPlanes(m)];

    // ---- record lists (§11) ----------------------------------------------------------------------------------------

    public static RecordListDocument RecordList(byte[] block, RecordFieldCodec codec, SourceRef source)
    {
        int size = codec.RecordSize, whole = block.Length / size * size;
        var records = new List<RecordDocument>();
        for (int k = 0; k < whole; k += size)
        {
            var raw = block[k..(k + size)];
            records.Add(new RecordDocument { Fields = codec.Decode(raw), Raw = raw });
        }
        return new RecordListDocument
        {
            RecordSize = size,
            Records = records,
            Trailing = whole < block.Length ? block[whole..] : null,
            Source = source,
        };
    }

    /// <summary>The list's records as the engine takes them (raw bytes; format 0.x: raw is authoritative).</summary>
    public static List<byte[]> Records(RecordListDocument d) => [.. d.Records.Select(r => r.Raw)];

    /// <summary>The original block: the records' raw bytes in order, then any trailing bytes.</summary>
    public static byte[] Block(RecordListDocument d) => [.. d.Records.SelectMany(r => r.Raw), .. d.Trailing ?? []];

    // ---- item types (§11) ------------------------------------------------------------------------------------------

    /// <summary>The <c>ITEMS</c> file → item types: 2-byte header (never read by the program), then 128 × 16.</summary>
    public static ItemTypesDocument ItemTypes(byte[] file)
    {
        var codec = PorRecordFields.ItemType;
        int size = codec.RecordSize, head = Math.Min(2, file.Length);
        int whole = (file.Length - head) / size * size;
        var types = new List<ItemTypeDocument>();
        for (int k = 0; k < whole; k += size)
        {
            var raw = file[(head + k)..(head + k + size)];
            types.Add(new ItemTypeDocument { Type = k / size, Fields = codec.Decode(raw), Raw = raw });
        }
        return new ItemTypesDocument
        {
            EntrySize = size,
            Header = file[..head],
            Types = types,
            Trailing = head + whole < file.Length ? file[(head + whole)..] : null,
            Source = new SourceRef { Container = "ITEMS" },
        };
    }

    /// <summary>The original <c>ITEMS</c> file bytes.</summary>
    public static byte[] ItemsFile(ItemTypesDocument d) =>
        [.. d.Header ?? [], .. d.Types.OrderBy(t => t.Type).SelectMany(t => t.Raw), .. d.Trailing ?? []];

    // ---- overland (§6, §12) ------------------------------------------------------------------------------------------

    /// <summary>The overland → <c>rules/overland.json</c> (values as stored).</summary>
    public static OverlandDocument Overland(OverlandMap map, PorOverlandOffsets? at, PorOverlandRecoveryOffsets? recoveryAt = null) => new()
    {
        Width = map.Width,
        Height = map.Height,
        Tiles = [.. map.Tiles.Select(b => (int)b)],
        SpecialLocations = [.. map.Sites.Select(s => new OverlandSiteDocument { X = s.X, Y = s.Y, Tile = s.Tile })],
        Recovery = map.Recovery is { } r ? new OverlandRecoveryDocument
        {
            ListA = [.. r.ListA.Select(b => (int)b)],
            ListB = [.. r.ListB.Select(b => (int)b)],
            From = [.. r.From.Select(b => (int)b)],
            ToA = [.. r.ToA.Select(b => (int)b)],
            ToB = [.. r.ToB.Select(b => (int)b)],
            First = new OverlandSquareDocument { X = r.First.X, Y = r.First.Y },
            Last = new OverlandSquareDocument { X = r.Last.X, Y = r.Last.Y },
            Pan = [.. r.Pan.Select(p => new OverlandSquareDocument { X = p.X, Y = p.Y })],
            Source = recoveryAt is { } o ? new OverlandRecoverySource
            {
                Container = "START.EXE", ListA = o.ListA, ListB = o.ListB, From = o.From, ToA = o.ToA, ToB = o.ToB,
                PanX = o.PanX, PanY = o.PanY,
            } : null,
        } : null,
        Source = new OverlandSource { Container = "START.EXE", Tiles = at?.Tiles, SpecialLocations = at?.Sites },
    };

    /// <summary><c>rules/overland.json</c> → the overland; a value that does not fit the stored width fails.</summary>
    public static OverlandMap Overland(OverlandDocument d)
    {
        byte Byte(int v) => v is >= 0 and <= 255 ? (byte)v : throw new FormatException($"overland tile {v} is not a byte");
        ushort Word(int v) => v is >= 0 and <= 0xFFFF ? (ushort)v : throw new FormatException($"overland value {v} is not a word");
        OverlandRecoveryTables? recovery = null;
        if (d.Recovery is { } r)
        {
            recovery = new OverlandRecoveryTables([.. r.ListA.Select(Byte)], [.. r.ListB.Select(Byte)],
                [.. r.From.Select(Byte)], [.. r.ToA.Select(Byte)], [.. r.ToB.Select(Byte)], (r.First.X, r.First.Y),
                (r.Last.X, r.Last.Y), [.. r.Pan.Select(p => (p.X, p.Y))]);
            if (recovery.Problem() is { } problem)
                throw new FormatException("overland recovery: " + problem);
        }
        return new OverlandMap(d.Width, d.Height, [.. d.Tiles.Select(Byte)],
            [.. d.SpecialLocations.Select(s => new OverlandSite(Word(s.X), Word(s.Y), Word(s.Tile)))]) { Recovery = recovery };
    }
}
