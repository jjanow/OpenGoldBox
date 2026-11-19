using System.Security.Cryptography;
using GoldBox.Core.Ecl;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

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
    public int Pictures { get; set; }
    public int ArtData { get; set; }
    public int Texts { get; set; }
    /// <summary>Decoded rules files written (rules/spells.json …, §12).</summary>
    public int RulesTables { get; set; }
    /// <summary>Original save files read (<c>SAVGAM?.DAT</c>, <c>CHRDAT*</c>): manifest inputs, held as engine saves.</summary>
    public int Saves { get; set; }
    /// <summary>Engine saves written (<c>saves/&lt;slot&gt;.json</c>, one per <c>SAVGAMx.DAT</c>).</summary>
    public int EngineSaves { get; set; }
    /// <summary><c>rules/overland.json</c> was written.</summary>
    public bool Overland { get; set; }
    /// <summary><c>audio/dos/pcsound.json</c> was written (the program's sound driver, docs/re/sound.md).</summary>
    public bool Sound { get; set; }
    /// <summary>Per disk: ids that are both a <c>MON&lt;d&gt;ITM</c> block and an <c>ITEM&lt;d&gt;</c> block (§16).</summary>
    public SortedDictionary<int, List<int>> ItemIdCollisions { get; } = [];
    /// <summary>Colliding ids whose two blocks hold different bytes.</summary>
    public int CollisionsWithDifferentContent { get; set; }
    public List<string> Notes { get; } = [];
    public List<ModuleInput> Inputs { get; } = [];
    /// <summary>The Amiga art overlay, when an Amiga folder was given.</summary>
    public PorAmigaReport? Amiga { get; set; }
}

/// <summary>
/// Pool of Radiance (DOS) → module (docs/module-format.md §16): scripts, maps, areas, monsters with their item and
/// effect lists, placed treasure, the item types, the program's tables (<c>rules/program.json</c>) and texts
/// (<c>text/en.json</c>), the DOS art and the shipped saves. The engine and the Client then need no game folder.
/// </summary>
public static class PorImporter
{
    public const string ImporterName = "GoldBox.Importer 0.7.20";

    /// <summary>VM $6E12 (DS:5376): the disk LOADFILES reads GEO&lt;d&gt; from.</summary>
    private const ushort DiskCell = 0x6E12;

    /// <summary>Program rule (GAME.OVR 0x2EC5D): while script 0 or 10 runs, off-map squares read 0 instead of wrapping.</summary>
    private static readonly int[] NoWrapScripts = [0, 10];

    /// <summary>DAX files that are not art (imported as scripts, maps and records above): name + disk digit.</summary>
    private static readonly System.Text.RegularExpressions.Regex NotArt =
        new(@"^(ECL[1-8]|GEO[1-8]|ITEM[1-8]|MON[1-8](CHA|ITM|SPC))$");

    /// <summary>True for a DAX name (no extension) imported as scripts, maps or records rather than art.</summary>
    public static bool IsNotArt(string name) => NotArt.IsMatch(name);


    /// <summary>
    /// Imports <paramref name="gameDir"/> into <paramref name="outDir"/>, which must not exist or be empty (nothing is
    /// overwritten or deleted). <paramref name="amigaDir"/>, when given, holds the unpacked Amiga v1.0 disks
    /// (<c>program</c> and the data files): their art becomes the <c>art/amiga</c> overlay (<see cref="PorAmigaArt"/>).
    /// </summary>
    public static PorImportReport Import(string gameDir, string outDir, string? amigaDir = null)
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

        var programData = PorProgramData.Open(game);
        foreach (var (id, block) in scriptBlocks.OrderBy(kv => kv.Key))
        {
            ModuleJson.Write(Out(ResourceKey.Area(id).RelativePath(".json")), Area(id, block, scriptDisk[id], mapDisk, programData));
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

        // ---- program tables and texts ----
        if (programData is { } program)
        {
            read.Add("START.EXE");
            if (game.Find("GAME.OVR") is not null)
                read.Add("GAME.OVR");
            ModuleJson.Write(Out(ProgramRulesDocument.RelativePath), new ProgramRulesDocument
            {
                DataSegment = program.Ds,
                Offsets = program.Offsets,
                GnomeDefenseNames = program.GnomeDefenseNames?.ToList(),
                View = program.View is { } v ? new ViewTablesDocument
                {
                    Start = [.. v.Layout.Start], Width = [.. v.Layout.Width], Height = [.. v.Layout.Height],
                    Column = [.. v.Layout.Column], Row = [.. v.Layout.Row], SkyColours = [.. v.SkyColours],
                    ExploreCommands = v.ExploreCommands?.ToList(),
                } : null,
                Source = new SourceRef { Container = "START.EXE" },
            });
            if (PorRulesTables.Write(outDir, program))
                report.RulesTables = PorRulesTables.Files.Count;
            else
                report.Notes.Add("a rules table was not found: no decoded rules/*.json (the engine reads rules/program.json)");
            ModuleJson.Write(Out(TextDocument.RelativePath), new TextDocument
            {
                Strings = new Dictionary<string, string>(program.Texts.OrderBy(kv => kv.Key, StringComparer.Ordinal)),
                SpellNames = program.SpellNames?.ToList(),
                PagePrompt = program.PagePrompt,
            });
            if (program.Overland is { } overland)
            {
                ModuleJson.Write(Out(OverlandDocument.RelativePath), PorModuleCodec.Overland(overland, program.OverlandOffsets));
                report.Overland = true;
            }
            else
                report.Notes.Add("the overland tables were not found in GAME.OVR's code: rules/overland.json not written");
            // The sound driver's data (docs/re/sound.md §4); the cue table stays in program.json's data segment.
            var cueTable = PorSoundDriver.CueTable(program.Ds);
            if (game.Find("START.EXE") is { } exePath
                && PorSoundDriver.Read(PorProgram.LoadImage(File.ReadAllBytes(exePath)).Image) is { } driver && cueTable is not null)
            {
                ModuleJson.Write(Out(PcSoundDocument.RelativePath), PcSoundDocument.From(driver, cueTable));
                report.Sound = true;
            }
            else
                report.Notes.Add("the sound driver or its cue table was not found in START.EXE: audio/dos/pcsound.json not written");
            report.Texts = program.Texts.Count;
            if (program.Texts.Count == 0)
                report.Notes.Add("GAME.OVR is not the v1.3 overlay: no program texts (the Client uses its own wording)");
        }
        else
            report.Notes.Add("no START.EXE: rules/program.json and text/en.json not written (no fights, no program texts)");

        // ---- art (DOS) ----
        foreach (var path in Directory.EnumerateFiles(gameDir).Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path).ToUpperInvariant();
            if (!file.EndsWith(".DAX", StringComparison.Ordinal))
                continue;
            var name = file[..^4];
            if (IsNotArt(name))
                continue;
            var dax = Dax(file)!;
            bool data = name.StartsWith("WALLDEF", StringComparison.Ordinal);
            foreach (var e in dax.Entries)
                if (PorArtCodec.Write(outDir, "dos", name, e.Id, dax.Decode(e), tryPicture: !data))
                    report.Pictures++;
                else
                    report.ArtData++;
        }

        // ---- art (Amiga overlay) ----
        if (amigaDir is not null)
            report.Amiga = PorAmigaArt.Import(amigaDir, outDir);

        // ---- shipped saves: inputs only; the module holds them as engine saves (written after the manifest, since they
        // name the module's content hash), which rebuild the original files byte for byte ----
        foreach (var path in Directory.EnumerateFiles(gameDir).Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path).ToUpperInvariant();
            if (!(file.StartsWith("SAVGAM", StringComparison.Ordinal) && file.EndsWith(".DAT", StringComparison.Ordinal))
                && !file.StartsWith("CHRDAT", StringComparison.Ordinal))
                continue;
            read.Add(file);
            report.Saves++;
        }

        // ---- manifest ----
        foreach (var name in read)
            report.Inputs.Add(new ModuleInput { File = name, Sha256 = Sha256(game.Find(name)!) });
        if (report.Amiga is { } amiga)
            report.Inputs.AddRange(amiga.Inputs);
        var manifest = new ModuleManifest
        {
            FormatVersion = ModuleManifest.CurrentFormatVersion,
            Id = "por",
            Title = "Pool of Radiance",
            Kind = "game",
            // [?] the release is not detected: the engine's profiles are for v1.3, the only release it supports.
            Rules = "por-1.3",
            Vm = "ecl-por-1.3",
            Art = report.Amiga is null
                ? new ModuleArt { Variants = ["dos"] }
                : new ModuleArt { Variants = ["dos", PorAmigaArt.Variant], Defaults = PorAmigaArt.BestDefaults() },
            Source = new ModuleSource
            {
                Importer = ImporterName,
                Game = "por",
                Platform = "dos",
                Inputs = report.Inputs,
            },
        };
        manifest.Save(outDir);

        // ---- engine saves (§15): one per shipped SAVGAMx.DAT, converted with its party files ----
        var identity = PorEngineSave.Identity(ModuleResolver.Open(outDir));
        foreach (var path in Directory.EnumerateFiles(gameDir).Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path).ToUpperInvariant();
            if (!(file.StartsWith("SAVGAM", StringComparison.Ordinal) && file.EndsWith(".DAT", StringComparison.Ordinal)
                  && file.Length == "SAVGAMA.DAT".Length && file[6] is >= 'A' and <= 'J'))
                continue;
            var save = PorEngineSave.FromFolders(File.ReadAllBytes(path), file, identity,
                (disk, id) => scriptDisk.GetValueOrDefault(id) == disk ? scriptBlocks.GetValueOrDefault(id) : null, gameDir);
            PorEngineSave.Write(Out("saves/" + PorEngineSave.FileName(file[6])), save);
            report.EngineSaves++;
            if (save.Script.MatchesModule == false)
                report.Notes.Add($"{file}: its script {save.Script.Key.Id} differs from scripts/0/{save.Script.Key.Id}.ecl (the saved copy wins on load)");
        }
        report.Notes.Add(report.Amiga is null
            ? "no Amiga folder given: DOS art only; manifest start [?]"
            : "manifest start [?]");
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
    private static AreaDocument Area(int id, byte[] block, int disk, Dictionary<int, int> mapDisk, PorProgramData? program)
    {
        var an = new EclAnalysis(block);
        var maps = an.Instructions.Values
            .Where(i => i.Op == EclOpcodes.LoadFiles && i.Operands.Count > 0 && !i.Operands[0].IsMemory)
            .Select(i => (int)(i.Operands[0].Word ?? i.Operands[0].Lo))
            .Where(m => m is not (255 or 127) && mapDisk.TryGetValue(m, out var d) && d == disk)
            .Distinct().Order().ToList();
        // Computed map operands: the values the cell can hold there, when the code before the LOADFILES fixes them
        // (EclValueSets); the disk may be switched on the way ($6E12, e.g. block 24 before leaving for 14 or 26).
        var computedAt = an.Instructions.Values
            .Where(i => i.Op == EclOpcodes.LoadFiles && i.Operands.Count > 0 && i.Operands[0].IsMemory && i.Operands[0].Word is not null)
            .OrderBy(i => i.Offset).ToList();
        bool computed = computedAt.Count > 0;
        var unresolved = new List<EclInstruction>();
        foreach (var i in computedAt)
        {
            if (EclValueSets.Before(an, i.Offset, i.Operands[0].Word!.Value) is not { } values)
            {
                unresolved.Add(i);
                continue;
            }
            var disks = new HashSet<int>(EclValueSets.ImmediateWritesBefore(an, i.Offset, DiskCell)) { disk };
            maps.AddRange(values.Where(m => m is not (255 or 127) && mapDisk.TryGetValue(m, out var d) && disks.Contains(d)));
        }
        maps = [.. maps.Distinct().Order()];
        var views = new List<string>();
        if (maps.Count > 0 || computed)
            views.Add("3d");
        // Program rule (GAME.OVR 0x3F28–0x3F99): blocks 25–27 switch to the wilderness view (modes 2–4) when $49E6 = 0.
        AreaOverland? overland = null;
        if (PorOverland.ViewMode(id) is not null)
        {
            views.Add("overland-tiles");
            if (program is { OverlandOffsets: { } at } && PorOverland.Column(program.Ds, at, id) is { } column)
                overland = new AreaOverland { Column = column };
        }
        var notes = new List<string>();
        if (unresolved.Count > 0)
            notes.Add("LOADFILES with a computed map operand not fixed by the code before it (" +
                      string.Join(", ", unresolved.Select(i => $"${i.Address:X4} [${i.Operands[0].Word:X4}]")) +
                      "): maps may miss what it loads [?]");
        else if (computed)
            notes.Add("maps includes the values of computed LOADFILES operands, found from the code before them");
        return new AreaDocument
        {
            Id = id,
            Views = views,
            Script = KeyRef.Of(ResourceKey.Script(id)),
            Maps = maps,
            NoWrapOffMap = NoWrapScripts.Contains(id),
            Overland = overland,
            Notes = notes.Count > 0 ? string.Join("; ", notes) : null,
        };
    }
}
