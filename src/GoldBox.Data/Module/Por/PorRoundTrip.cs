using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>The result of <see cref="PorRoundTrip.Check"/>: blocks compared per container kind, and every mismatch.</summary>
public sealed class RoundTripReport
{
    public SortedDictionary<string, int> Checked { get; } = new(StringComparer.Ordinal);
    public List<string> Mismatches { get; } = [];
    public int Total => Checked.Values.Sum();

    internal void Count(string kind) => Checked[kind] = Checked.GetValueOrDefault(kind) + 1;

    /// <summary>Adds another report's counts and mismatches to this one.</summary>
    public void Add(RoundTripReport other)
    {
        foreach (var (k, n) in other.Checked)
            Checked[k] = Checked.GetValueOrDefault(k) + n;
        Mismatches.AddRange(other.Mismatches);
    }
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
        // ---- art: every block of every art DAX file, as bytes and as the engine's pictures ----
        foreach (var path in Directory.EnumerateFiles(game.Root).Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path).ToUpperInvariant();
            if (!file.EndsWith(".DAX", StringComparison.Ordinal) || PorImporter.IsNotArt(file[..^4]))
                continue;
            var name = file[..^4];
            var f = game.Dax(file)!;
            foreach (var e in f.Entries)
            {
                var b = f.Decode(e);
                Compare($"{name} block {e.Id}", module.ArtData(name, e.Id), b, "ART");
                if (game.Picture(name, e.Id) is { } frames)
                {
                    r.Count("ART (engine)");
                    var mf = module.Picture(name, e.Id);
                    if (!name.StartsWith("WALLDEF", StringComparison.Ordinal) && (mf is null || !SamePictures(frames, mf)))
                        r.Mismatches.Add($"{name} block {e.Id} via PorModule: pictures differ");
                }
            }
        }

        // ---- program tables and texts: the module's give the same as the folder's ----
        if (game.Program() is { } gp)
        {
            r.Count("PROGRAM");
            var mp = module.Program();
            if (mp is null)
                r.Mismatches.Add("program: rules/program.json missing");
            else if (Describe(gp) != Describe(mp))
                r.Mismatches.Add("program: the module's tables or texts differ from the program's");

            // The decoded rules files (§12): all of them when the program's tables decode, else none; each file's
            // fields give the program's bytes back, cell for cell.
            bool decodes = PorRulesTables.Decode(gp.Ds, gp.Offsets) is not null;
            foreach (var (path, type) in PorRulesTables.Files)
            {
                if (m.File(path) is not { } file)
                {
                    if (decodes)
                        r.Mismatches.Add($"{path}: missing");
                    continue;
                }
                if (!decodes)
                {
                    r.Mismatches.Add($"{path}: written although the program's tables do not decode");
                    continue;
                }
                r.Count("RULES");
                var doc = (IRulesDocument)(System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(file), type, ModuleSchemas.Strict)
                    ?? throw new FormatException($"{file}: empty JSON"));
                foreach (var (off, b) in PorRulesTables.Encode(doc))
                    if (off >= gp.Ds.Length || gp.Ds[off] != b)
                    {
                        r.Mismatches.Add($"{path}: DS:{off:X4} differs from the program's byte");
                        break;
                    }
            }
        }

        // ---- the overland: the document rebuilds the program's bytes, and the engine reads the same map ----
        if (game.Program() is { Overland: { } go, OverlandOffsets: { } at } gp2)
        {
            var doc = m.File(OverlandDocument.RelativePath) is { } op ? ModuleJson.Read<OverlandDocument>(op) : null;
            var fromDoc = doc is null ? null : PorModuleCodec.Overland(doc);
            var ds = gp2.Ds;
            Compare("overland tiles", fromDoc?.Tiles, ds[at.Tiles..(at.Tiles + PorOverland.Width * PorOverland.Height)], "OVERLAND");
            Compare("overland special locations", fromDoc is null ? null : PorOverland.SiteBytes(fromDoc.Sites),
                ds[at.Sites..(at.Sites + PorOverland.SiteCount * PorOverland.SiteSize)], "OVERLAND");
            var mo = ((IPorContent)module).Overland();
            Compare("overland tiles via PorModule", mo?.Tiles, go.Tiles, "OVERLAND (engine)");
            Compare("overland special locations via PorModule", mo is null ? null : PorOverland.SiteBytes(mo.Sites),
                PorOverland.SiteBytes(go.Sites), "OVERLAND (engine)");
            if (mo is not null && (mo.Width != go.Width || mo.Height != go.Height))
                r.Mismatches.Add($"overland: {mo.Width} × {mo.Height} in the module, {go.Width} × {go.Height} in the program");
            Compare("combat wilderness terrain via PorModule", module.Program()?.CombatTables().WildMap, gp2.CombatTables().WildMap!,
                "OVERLAND (engine)");
            // The land's recovery (s60): the document's tables give the program's bytes at their offsets, the squares its
            // code immediates, and the engine reads the same tables.
            if (gp2.RecoveryOffsets is { } ro && go.Recovery is { } gr)
            {
                var dr = fromDoc?.Recovery;
                byte[] Pan(OverlandRecoveryTables t, bool y) =>
                    [.. t.Pan.SelectMany(p => BitConverter.GetBytes((ushort)(y ? p.Y : p.X)))];
                Compare("overland recovery list A", dr?.ListA.ToArray(), ds[ro.ListA..(ro.ListA + ro.CountA)], "OVERLAND");
                Compare("overland recovery list B", dr?.ListB.ToArray(), ds[ro.ListB..(ro.ListB + ro.CountB)], "OVERLAND");
                Compare("overland recovery from", dr?.From.ToArray(), ds[ro.From..(ro.From + OverlandRecovery.Colours)], "OVERLAND");
                Compare("overland recovery map A", dr?.ToA.ToArray(), ds[ro.ToA..(ro.ToA + OverlandRecovery.Colours)], "OVERLAND");
                Compare("overland recovery map B", dr?.ToB.ToArray(), ds[ro.ToB..(ro.ToB + OverlandRecovery.Colours)], "OVERLAND");
                Compare("overland recovery pan x", dr is null ? null : Pan(dr, false), ds[ro.PanX..(ro.PanX + 2 * ro.Frames)], "OVERLAND");
                Compare("overland recovery pan y", dr is null ? null : Pan(dr, true), ds[ro.PanY..(ro.PanY + 2 * ro.Frames)], "OVERLAND");
                r.Count("OVERLAND");
                if (dr is null || dr.First != ro.First || dr.Last != ro.Last)
                    r.Mismatches.Add("overland recovery: the squares before / after the frames differ from the program's code");
                r.Count("OVERLAND (engine)");
                if (mo?.Recovery is not { } mr || !mr.ListA.SequenceEqual(gr.ListA) || !mr.ListB.SequenceEqual(gr.ListB)
                    || !mr.From.SequenceEqual(gr.From)
                    || !mr.ToA.SequenceEqual(gr.ToA) || !mr.ToB.SequenceEqual(gr.ToB) || !mr.Pan.SequenceEqual(gr.Pan)
                    || mr.First != gr.First || mr.Last != gr.Last)
                    r.Mismatches.Add("overland recovery via PorModule differs from the program's tables");
            }
            foreach (var id in game.EclFiles().SelectMany(f => f.File.Entries.Select(e => (int)e.Id)).Distinct())
            {
                r.Count("OVERLAND (areas)");
                if (module.OverlandColumn(id) != ((IPorContent)game).OverlandColumn(id))
                    r.Mismatches.Add($"area {id}: overland column {module.OverlandColumn(id)} in the module, {((IPorContent)game).OverlandColumn(id)} in the program");
            }
        }

        // ---- shipped saves (§15): the module keeps them only as engine saves; each saves/<slot>.json writes the original
        // SAVGAM and party files back byte for byte, and exactly the files the game folder has for that save ----
        var gameFiles = Directory.EnumerateFiles(game.Root)
            .ToDictionary(f => Path.GetFileName(f).ToUpperInvariant(), f => f, StringComparer.Ordinal);
        foreach (var (file, path) in gameFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!(file.StartsWith("SAVGAM", StringComparison.Ordinal) && file.EndsWith(".DAT", StringComparison.Ordinal)
                  && file.Length == "SAVGAMA.DAT".Length && file[6] is >= 'A' and <= 'J'))
                continue;
            var json = module.SaveFolder is { } sf ? Path.Combine(sf, PorEngineSave.FileName(file[6])) : null;
            if (json is null || !File.Exists(json))
            {
                r.Count("ENGINE SAVE");
                r.Mismatches.Add($"engine save of {file}: missing in the module");
                continue;
            }
            var save = PorEngineSave.Read(json);
            var rebuilt = PorEngineSave.ToOriginalFiles(save);
            foreach (var (name, bytes) in rebuilt)
                Compare($"engine save {Path.GetFileName(json)} → {name}", bytes,
                    gameFiles.TryGetValue(name, out var orig) ? File.ReadAllBytes(orig) : [], "ENGINE SAVE");
            // The party files the original names (stems of its name slots) that the engine save did not write back.
            var stems = new PorSaveGame(File.ReadAllBytes(path)).PartyFileStems;
            foreach (var stem in stems)
                foreach (var ext in new[] { ".SAV", ".ITM", ".SPC" })
                    if (gameFiles.ContainsKey(stem + ext) && !rebuilt.Any(f => f.Name == stem + ext))
                        r.Mismatches.Add($"engine save {Path.GetFileName(json)}: {stem + ext} not written back");
        }
        return r;
    }

    private static bool SamePictures(IReadOnlyList<DaxPicture> a, IReadOnlyList<DaxPicture> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            p.First.Width == p.Second.Width && p.First.Height == p.Second.Height && p.First.X == p.Second.X &&
            p.First.Y == p.Second.Y && p.First.Delay == p.Second.Delay && p.First.Cga.AsSpan().SequenceEqual(p.Second.Cga) &&
            p.First.Items.Count == p.Second.Items.Count &&
            p.First.Items.Zip(p.Second.Items).All(i => i.First.AsSpan().SequenceEqual(i.Second)));

    /// <summary>Everything the engine reads from <see cref="PorProgramData"/>, as text (for an exact comparison).</summary>
    public static string Describe(PorProgramData p)
    {
        var sb = new System.Text.StringBuilder();
        string Bytes(IEnumerable<byte>? b) => b is null ? "null" : Convert.ToHexString([.. b]);
        sb.AppendLine(Bytes(p.Ds));
        sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(p.Offsets));
        foreach (var g in p.GnomeDefenseNames ?? [])
            sb.AppendLine(Bytes(g));
        if (p.View is { } v)
            sb.AppendLine(string.Join(",", [.. v.Layout.Start, .. v.Layout.Width, .. v.Layout.Height, .. v.Layout.Column, .. v.Layout.Row])
                          + "|" + Bytes(v.SkyColours) + "|" + string.Join(" ", v.ExploreCommands ?? []));
        foreach (var (k, t) in p.Texts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(k).Append('=').AppendLine(t);
        sb.AppendLine(string.Join("|", p.SpellNames ?? []));
        sb.AppendLine(p.PagePrompt);
        if (p.Overland is { } o)
            sb.AppendLine($"{o.Width}x{o.Height}|{Bytes(o.Tiles)}|{Bytes(PorOverland.SiteBytes(o.Sites))}");
        return sb.ToString();
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
