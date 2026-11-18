using System.Security.Cryptography;
using GoldBox.Core.Ecl;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Importer;

/// <summary>What an import wrote and found.</summary>
public sealed class PorImportReport
{
    public int Scripts { get; set; }
    public int Maps { get; set; }
    public int Areas { get; set; }
    public int Monsters { get; set; }
    public int ItemLists { get; set; }
    public int EffectLists { get; set; }
    public int TreasureLists { get; set; }
    public int ItemTypes { get; set; }
    /// <summary>Per disk: ids that are both a <c>MON&lt;d&gt;ITM</c> block and an <c>ITEM&lt;d&gt;</c> block (§16).</summary>
    public SortedDictionary<int, List<int>> ItemIdCollisions { get; } = [];
    /// <summary>Colliding ids whose two blocks hold different bytes.</summary>
    public int CollisionsWithDifferentContent { get; set; }
    public List<string> Notes { get; } = [];
    public List<ModuleInput> Inputs { get; } = [];
}

/// <summary>
/// Pool of Radiance (DOS) → module (docs/module-format.md §16): scripts, maps, areas, monsters with their item and
/// effect lists, placed treasure and the item types. Art, rules tables, text and saves are not imported yet; the
/// engine still reads those from the player's folder.
/// </summary>
public static class PorImporter
{
    public const string ImporterName = "GoldBox.Importer 0.1";

    /// <summary>Program rule (GAME.OVR 0x2EC5D): while script 0 or 10 runs, off-map squares read 0 instead of wrapping.</summary>
    private static readonly int[] NoWrapScripts = [0, 10];

    /// <summary>Program rule (GAME.OVR 0x3F28–0x3F99): blocks 25–27 switch to the wilderness view when $49E6 = 0.</summary>
    private const int FirstWildernessScript = 25, LastWildernessScript = 27;

    /// <summary>
    /// Imports <paramref name="gameDir"/> into <paramref name="outDir"/>, which must not exist or be empty (nothing is
    /// overwritten or deleted).
    /// </summary>
    public static PorImportReport Import(string gameDir, string outDir)
    {
        var game = new PorGameDirectory(gameDir);
        if (!game.EclFiles().Any())
            throw new FileNotFoundException($"{gameDir} has no ECL1..8.DAX: not a Pool of Radiance (DOS) folder");
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
            throw new IOException($"{outDir} is not empty; the importer writes only into a new or empty folder");
        Directory.CreateDirectory(outDir);

        var report = new PorImportReport();
        var read = new SortedSet<string>(StringComparer.Ordinal);
        DaxFile? Dax(string name)
        {
            var f = game.Dax(name);
            if (f is not null)
                read.Add(Path.GetFileName(game.Find(name)!).ToUpperInvariant());
            return f;
        }
        string Out(string rel) => Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar));

        // ---- scripts and areas ----
        var scriptDisk = new Dictionary<int, int>();
        var scriptBlocks = new Dictionary<int, byte[]>();
        for (int d = 1; d <= 8; d++)
        {
            if (Dax($"ECL{d}.DAX") is not { } ecl)
                continue;
            foreach (var e in ecl.Entries)
            {
                if (scriptDisk.TryGetValue(e.Id, out var other))
                    throw new FormatException($"ECL block {e.Id} is on disk {other} and disk {d}: script ids must be unique (bank 0)");
                var block = ecl.Decode(e);
                var (bytes, sidecar) = PorModuleCodec.SplitScript(block, d, e.Id);
                var key = ResourceKey.Script(e.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(Out(key.RelativePath(".ecl")))!);
                File.WriteAllBytes(Out(key.RelativePath(".ecl")), bytes);
                ModuleJson.Write(Out(key.RelativePath(".json")), sidecar);
                scriptDisk[e.Id] = d;
                scriptBlocks[e.Id] = block;
                report.Scripts++;
            }
        }

        // ---- maps ----
        var mapDisk = new Dictionary<int, int>();
        for (int d = 1; d <= 8; d++)
        {
            if (Dax($"GEO{d}.DAX") is not { } geo)
                continue;
            foreach (var e in geo.Entries)
            {
                if (mapDisk.TryGetValue(e.Id, out var other))
                    throw new FormatException($"GEO block {e.Id} is on disk {other} and disk {d}: map ids must be unique (bank 0)");
                ModuleJson.Write(Out(ResourceKey.Map(e.Id).RelativePath(".json")), PorModuleCodec.MapFromGeo(geo.Decode(e), d, e.Id));
                mapDisk[e.Id] = d;
                report.Maps++;
            }
        }

        foreach (var (id, block) in scriptBlocks.OrderBy(kv => kv.Key))
        {
            ModuleJson.Write(Out(ResourceKey.Area(id).RelativePath(".json")), Area(id, block, scriptDisk[id], mapDisk));
            report.Areas++;
        }

        // ---- monsters, their item and effect lists, placed treasure ----
        for (int d = 1; d <= 8; d++)
        {
            var cha = Dax($"MON{d}CHA.DAX");
            var itm = Dax($"MON{d}ITM.DAX");
            var spc = Dax($"MON{d}SPC.DAX");
            var treasure = Dax($"ITEM{d}.DAX");
            var itmIds = itm?.Entries.Select(e => (int)e.Id).ToHashSet() ?? [];
            var spcIds = spc?.Entries.Select(e => (int)e.Id).ToHashSet() ?? [];

            if (itm is not null)
                foreach (var e in itm.Entries)
                {
                    ModuleJson.Write(Out(ResourceKey.ItemList(d, e.Id).RelativePath(".json")),
                        PorModuleCodec.RecordList(itm.Decode(e), PorRecordFields.Item, new SourceRef { Container = $"MON{d}ITM.DAX", Block = e.Id }));
                    report.ItemLists++;
                }
            if (spc is not null)
                foreach (var e in spc.Entries)
                {
                    ModuleJson.Write(Out(ResourceKey.EffectList(d, e.Id).RelativePath(".json")),
                        PorModuleCodec.RecordList(spc.Decode(e), PorRecordFields.Effect, new SourceRef { Container = $"MON{d}SPC.DAX", Block = e.Id }));
                    report.EffectLists++;
                }
            if (cha is not null)
                foreach (var e in cha.Entries)
                {
                    var raw = cha.Decode(e);
                    var doc = new MonsterDocument
                    {
                        Fields = raw.Length == PorRecordFields.Creature.RecordSize ? PorRecordFields.Creature.Decode(raw) : null,
                        Raw = raw,
                        Items = itmIds.Contains(e.Id) ? KeyRef.Of(ResourceKey.ItemList(d, e.Id)) : null,
                        Effects = spcIds.Contains(e.Id) ? KeyRef.Of(ResourceKey.EffectList(d, e.Id)) : null,
                        Source = new SourceRef { Container = $"MON{d}CHA.DAX", Block = e.Id },
                    };
                    if (doc.Fields is null)
                        report.Notes.Add($"MON{d}CHA block {e.Id} is {raw.Length} bytes, not a 285-byte record: raw only");
                    ModuleJson.Write(Out(ResourceKey.Monster(d, e.Id).RelativePath(".json")), doc);
                    report.Monsters++;
                }
            if (treasure is not null)
            {
                foreach (var e in treasure.Entries)
                {
                    var block = treasure.Decode(e);
                    ModuleJson.Write(Out(ResourceKey.TreasureList(d, e.Id).RelativePath(".json")),
                        PorModuleCodec.RecordList(block, PorRecordFields.Item, new SourceRef { Container = $"ITEM{d}.DAX", Block = e.Id }));
                    report.TreasureLists++;
                    if (itmIds.Contains(e.Id))
                    {
                        if (!report.ItemIdCollisions.TryGetValue(d, out var list))
                            report.ItemIdCollisions[d] = list = [];
                        list.Add(e.Id);
                        if (!itm!.Block(e.Id)!.AsSpan().SequenceEqual(block))
                            report.CollisionsWithDifferentContent++;
                    }
                }
            }
        }

        // ---- item types ----
        if (game.Find("ITEMS") is { } itemsPath)
        {
            var doc = PorModuleCodec.ItemTypes(File.ReadAllBytes(itemsPath));
            ModuleJson.Write(Out(ItemTypesDocument.RelativePath), doc);
            report.ItemTypes = doc.Types.Count;
            read.Add(Path.GetFileName(itemsPath).ToUpperInvariant());
        }
        else
            report.Notes.Add("no ITEMS file: records/itemTypes.json not written");

        // ---- manifest ----
        foreach (var name in read)
            report.Inputs.Add(new ModuleInput { File = name, Sha256 = Sha256(game.Find(name)!) });
        var manifest = new ModuleManifest
        {
            FormatVersion = ModuleManifest.CurrentFormatVersion,
            Id = "por",
            Title = "Pool of Radiance",
            Kind = "game",
            // [?] the release is not detected: the engine's profiles are for v1.3, the only release it supports.
            Rules = "por-1.3",
            Vm = "ecl-por-1.3",
            Source = new ModuleSource
            {
                Importer = ImporterName,
                Game = "por",
                Platform = "dos",
                Inputs = report.Inputs,
            },
        };
        manifest.Save(outDir);
        report.Notes.Add("not imported yet (read from the game folder): art, rules tables, text, saves; manifest start [?]");
        return report;
    }

    private static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    /// <summary>
    /// <c>areas/&lt;id&gt;.json</c> for ECL block <paramref name="id"/>: the maps are the immediate LOADFILES map operands on
    /// code reachable from the vectors (<see cref="EclAnalysis"/>) that name a GEO block on the script's own disk
    /// (LOADFILES reads GEO&lt;disk&gt;; computed operands are not followed, so an area may load more [?]).
    /// </summary>
    private static AreaDocument Area(int id, byte[] block, int disk, Dictionary<int, int> mapDisk)
    {
        var an = new EclAnalysis(block);
        var maps = an.Instructions.Values
            .Where(i => i.Op == EclOpcodes.LoadFiles && i.Operands.Count > 0 && !i.Operands[0].IsMemory)
            .Select(i => (int)(i.Operands[0].Word ?? i.Operands[0].Lo))
            .Where(m => m is not (255 or 127) && mapDisk.TryGetValue(m, out var d) && d == disk)
            .Distinct().Order().ToList();
        bool computed = an.Instructions.Values.Any(i => i.Op == EclOpcodes.LoadFiles && i.Operands.Count > 0 && i.Operands[0].IsMemory);
        var views = new List<string>();
        if (maps.Count > 0 || computed)
            views.Add("3d");
        if (id is >= FirstWildernessScript and <= LastWildernessScript)
            views.Add("overland-tiles");
        var notes = new List<string>();
        if (computed)
            notes.Add("LOADFILES with a computed map operand: maps lists only the immediate ones [?]");
        return new AreaDocument
        {
            Id = id,
            Views = views,
            Script = KeyRef.Of(ResourceKey.Script(id)),
            Maps = maps,
            NoWrapOffMap = NoWrapScripts.Contains(id),
            Notes = notes.Count > 0 ? string.Join("; ", notes) : null,
        };
    }
}
