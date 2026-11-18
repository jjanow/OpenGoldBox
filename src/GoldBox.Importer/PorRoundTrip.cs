using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Importer;

/// <summary>The result of <see cref="PorRoundTrip.Check"/>: blocks compared per container kind, and every mismatch.</summary>
public sealed class RoundTripReport
{
    public SortedDictionary<string, int> Checked { get; } = new(StringComparer.Ordinal);
    public List<string> Mismatches { get; } = [];
    public int Total => Checked.Values.Sum();

    internal void Count(string kind) => Checked[kind] = Checked.GetValueOrDefault(kind) + 1;
}

/// <summary>
/// Module → original bytes (docs/module-format.md §17.3): for every block of every DAX file the importer reads (and
/// the <c>ITEMS</c> file), rebuild it from the module and compare it with the decoded block. The loop runs over the
/// game's files, not over the module's, so a block the importer missed is a mismatch. The decoded field views are
/// also checked: writing a record's fields back over its raw bytes must give the raw bytes.
/// </summary>
public static class PorRoundTrip
{
    public static RoundTripReport Check(PorGameDirectory game, PorModule module)
    {
        var r = new RoundTripReport();
        var m = module.Modules.Main;

        void Each(string pattern, Action<int, DaxFile, DaxEntry, byte[]> check)
        {
            for (int d = 1; d <= 8; d++)
                if (game.Dax(string.Format(pattern, d)) is { } f)
                    foreach (var e in f.Entries)
                        check(d, f, e, f.Decode(e));
        }
        void Compare(string what, byte[]? rebuilt, byte[] original, string kind)
        {
            r.Count(kind);
            if (rebuilt is null)
                r.Mismatches.Add($"{what}: missing in the module");
            else if (!rebuilt.AsSpan().SequenceEqual(original))
                r.Mismatches.Add($"{what}: {rebuilt.Length} bytes rebuilt, {original.Length} original, first difference at {FirstDiff(rebuilt, original)}");
        }
        T? Read<T>(ResourceKey key) where T : class => m.Find(key, ".json") is { } p ? ModuleJson.Read<T>(p) : null;
        void Fields(string what, RecordFieldCodec codec, System.Text.Json.Nodes.JsonObject? fields, byte[] raw)
        {
            if (fields is not null && raw.Length == codec.RecordSize && !codec.Encode(fields, raw).AsSpan().SequenceEqual(raw))
                r.Mismatches.Add($"{what}: fields do not encode back to the raw bytes");
        }
        void List(string what, RecordListDocument? doc, RecordFieldCodec codec)
        {
            foreach (var (rec, i) in doc?.Records.Select((x, i) => (x, i)) ?? [])
                Fields($"{what} record {i}", codec, rec.Fields, rec.Raw);
        }

        Each("ECL{0}.DAX", (d, _, e, b) =>
        {
            var key = ResourceKey.Script(e.Id);
            byte[]? rebuilt = null;
            if (m.Find(key, ".ecl") is { } ecl && m.Find(key, ".json") is { } side)
            {
                var sc = ModuleJson.Read<ScriptSidecar>(side);
                rebuilt = PorModuleCodec.ScriptBlock(File.ReadAllBytes(ecl), sc);
                if (sc.Meta?.Disk != d)
                    r.Mismatches.Add($"ECL{d} block {e.Id}: sidecar disk {sc.Meta?.Disk}");
            }
            Compare($"ECL{d} block {e.Id}", rebuilt, b, "ECL");
            // The engine's path: the module source answers the VM's (disk, id) request with the same block.
            Compare($"ECL{d} block {e.Id} via PorModule", module.LoadBlock(d, e.Id), b, "ECL (engine)");
        });
        Each("GEO{0}.DAX", (d, _, e, b) =>
        {
            var doc = Read<MapDocument>(ResourceKey.Map(e.Id));
            Compare($"GEO{d} block {e.Id}", doc is null ? null : PorModuleCodec.GeoBlock(doc), b, "GEO");
            var map = module.LoadMap(d, e.Id);
            Compare($"GEO{d} block {e.Id} via PorModule", map is null ? null : [.. b[..2], .. map.PlanesArray], b, "GEO (engine)");
        });
        Each("MON{0}CHA.DAX", (d, _, e, b) =>
        {
            var doc = Read<MonsterDocument>(ResourceKey.Monster(d, e.Id));
            Compare($"MON{d}CHA block {e.Id}", doc?.Raw, b, "MONCHA");
            if (doc is not null)
                Fields($"MON{d}CHA block {e.Id}", PorRecordFields.Creature, doc.Fields, doc.Raw);
            Compare($"MON{d}CHA block {e.Id} via PorModule", module.Monster(d, e.Id)?.Record, b, "MONCHA (engine)");
        });
        Each("MON{0}ITM.DAX", (d, _, e, b) =>
        {
            var doc = Read<RecordListDocument>(ResourceKey.ItemList(d, e.Id));
            Compare($"MON{d}ITM block {e.Id}", doc is null ? null : PorModuleCodec.Block(doc), b, "MONITM");
            List($"MON{d}ITM block {e.Id}", doc, PorRecordFields.Item);
            var mon = module.Monster(d, e.Id);
            Compare($"MON{d}ITM block {e.Id} via PorModule", mon is null ? null : [.. mon.Items.SelectMany(x => x)], b, "MONITM (engine)");
        });
        Each("MON{0}SPC.DAX", (d, _, e, b) =>
        {
            var doc = Read<RecordListDocument>(ResourceKey.EffectList(d, e.Id));
            Compare($"MON{d}SPC block {e.Id}", doc is null ? null : PorModuleCodec.Block(doc), b, "MONSPC");
            List($"MON{d}SPC block {e.Id}", doc, PorRecordFields.Effect);
            var mon = module.Monster(d, e.Id);
            Compare($"MON{d}SPC block {e.Id} via PorModule", mon is null ? null : [.. mon.Effects.SelectMany(x => x)], b, "MONSPC (engine)");
        });
        Each("ITEM{0}.DAX", (d, _, e, b) =>
        {
            var doc = Read<RecordListDocument>(ResourceKey.TreasureList(d, e.Id));
            Compare($"ITEM{d} block {e.Id}", doc is null ? null : PorModuleCodec.Block(doc), b, "ITEM");
            List($"ITEM{d} block {e.Id}", doc, PorRecordFields.Item);
            var list = module.TreasureList(d, e.Id);
            Compare($"ITEM{d} block {e.Id} via PorModule", list is null ? null : [.. list.SelectMany(x => x)], b, "ITEM (engine)");
        });
        if (game.Find("ITEMS") is { } items)
        {
            var original = File.ReadAllBytes(items);
            var doc = module.ItemTypesDocument();
            Compare("ITEMS", doc is null ? null : PorModuleCodec.ItemsFile(doc), original, "ITEMS");
            foreach (var t in doc?.Types ?? [])
                Fields($"ITEMS type {t.Type}", PorRecordFields.ItemType, t.Fields, t.Raw);
        }
        return r;
    }

    private static int FirstDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return n;
    }
}
