using System.Text.Json;
using System.Text.Json.Nodes;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The decoded rules files (docs/module-format.md §12): they rebuild the program's bytes, the engine reads them (a
/// changed field changes the table), the tables equal the ones read from the program files, and the rules profile
/// checks them.
/// </summary>
public class RulesTablesTests
{
    private static readonly Lazy<PorProgramData> Original = new(() => PorProgramData.Open(PorData.Open())!);

    private static string Rules(string rel) => Path.Combine(PorModuleData.Dir, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A new module folder (temp) with the imported manifest, <c>rules/program.json</c>, the texts and the decoded files,
    /// after <paramref name="edit"/> changed them (path → JSON).</summary>
    private static string MiniModule(Action<Dictionary<string, JsonNode>>? edit = null, bool decoded = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "goldbox-tests", $"rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "rules"));
        File.Copy(Rules(ModuleManifest.FileName), Path.Combine(dir, ModuleManifest.FileName));
        File.Copy(Rules(ProgramRulesDocument.RelativePath), Path.Combine(dir, "rules", "program.json"));
        File.Copy(Rules(OverlandDocument.RelativePath), Path.Combine(dir, "rules", "overland.json"));
        Directory.CreateDirectory(Path.Combine(dir, "text"));
        File.Copy(Rules(TextDocument.RelativePath), Path.Combine(dir, "text", "en.json"));
        var docs = decoded
            ? PorRulesTables.Files.ToDictionary(f => f.Path, f => JsonNode.Parse(File.ReadAllText(Rules(f.Path)))!)
            : [];
        edit?.Invoke(docs);
        foreach (var (path, node) in docs)
            File.WriteAllText(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar)), node.ToJsonString());
        return dir;
    }

    [PorFact]
    public void The_importer_writes_every_decoded_file_and_each_rebuilds_the_program_bytes()
    {
        Assert.Equal(PorRulesTables.Files.Count, PorModuleData.Report.RulesTables);
        var ds = Original.Value.Ds;
        int cells = 0;
        foreach (var (path, type) in PorRulesTables.Files)
        {
            var doc = (IRulesDocument)JsonSerializer.Deserialize(File.ReadAllText(Rules(path)), type, ModuleSchemas.Strict)!;
            Assert.Equal("por-1.3", doc.Profile);
            var bytes = PorRulesTables.Encode(doc);
            Assert.NotEmpty(bytes);
            foreach (var (off, b) in bytes)
                Assert.True(ds[off] == b, $"{path}: DS:{off:X4} {b:X2}, program {ds[off]:X2}");
            cells += bytes.Count;
        }
        // The files own disjoint cells (Apply refuses an overlap) and give the program's data segment back.
        var set = PorRulesTables.Read(PorModuleData.Open().Modules);
        var (applied, _) = PorRulesTables.Apply(new byte[ds.Length], Original.Value.Offsets, set);
        Assert.Equal(cells, set.Present().Sum(p => PorRulesTables.Encode(p.Doc).Count));
        foreach (var (path, doc) in set.Present())
            foreach (var (off, _) in PorRulesTables.Encode(doc))
                Assert.Equal(ds[off], applied[off]);
    }

    [PorFact]
    public void The_module_tables_equal_the_program_tables_with_and_without_the_decoded_files()
    {
        var p = Original.Value;
        foreach (var m in new[] { PorModuleData.Open().Program()!, PorModule.Open(MiniModule(decoded: false)).Program()! })
        {
            Assert.Equal(PorRoundTrip.Describe(p), PorRoundTrip.Describe(m));
            for (int id = 1; id <= Core.Game.SpellTable.Count; id++)
                Assert.Equal(p.Spells()![id], m.Spells()![id]);
            Assert.Equal(p.ExperienceTable(), m.ExperienceTable());
            Assert.Equal(p.AiSteps(), m.AiSteps());
            Assert.Equal(p.RandomItemTable(), m.RandomItemTable());
            Assert.Equal(p.TempleNames(), m.TempleNames());
            for (int r = 0; r <= 10; r++)
                for (int c = 0; c <= 10; c++)
                    if (r * 10 + c < Core.Game.TurnUndeadTable.Length)
                        Assert.Equal(p.TurnUndeadTable()!.Value(r, c), m.TurnUndeadTable()!.Value(r, c));
            var (pl, ml) = (p.LevelTables()!, m.LevelTables()!);
            Assert.Equal(pl.Data, ml.Data);
            Assert.Equal((pl.Thac0, pl.ClericSlots, pl.MageSlots, pl.SpellTable, pl.Saves, pl.ThiefBase, pl.ThiefRace, pl.ThiefDex),
                (ml.Thac0, ml.ClericSlots, ml.MageSlots, ml.SpellTable, ml.Saves, ml.ThiefBase, ml.ThiefRace, ml.ThiefDex));
            var (pc, mc) = (p.CombatTables(), m.CombatTables());
            Assert.Equal(pc.Terrain, mc.Terrain);
            Assert.Equal(pc.IconDeltaBytes, mc.IconDeltaBytes);
            Assert.Equal(pc.RowBand, mc.RowBand);
            Assert.Equal(pc.WildMap, mc.WildMap);
        }
    }

    private static JsonNode Find(JsonNode list, string key, int value) =>
        list.AsArray().First(n => n![key]!.GetValue<int>() == value)!;

    [PorFact]
    public void The_engine_reads_the_decoded_fields()
    {
        var dir = MiniModule(d =>
        {
            Find(d[SpellsDocument.RelativePath]["spells"]!, "id", 1)["castTime"] = 99;
            Find(d[ExperienceDocument.RelativePath]["classes"]!, "class", 2)["levels"]![0] = 2222;
            var ls = d[LevelStatsDocument.RelativePath];
            Find(ls["thac0"]!["classes"]!, "class", 2)["levels"]![1] = 77;
            Find(ls["thiefRace"]!["races"]!, "race", 1)["skills"]!["openLocks"] = -7;
            Find(d[TurnUndeadDocument.RelativePath]["rows"]!, "row", 1)["columns"]![0] = -3;
            Find(d[AiStepsDocument.RelativePath]["rows"]!, "row", 1)["attempts"]![0] = 3;
            Find(d[RandomItemsDocument.RelativePath]["items"]!, "magic", 1)["value"] = 1234;
            Find(d[TempleDocument.RelativePath]["heals"]!, "index", 1)["name"] = "Edited";
            Find(d[CombatMapDocument.RelativePath]["terrain"]!["entries"]!, "terrain", 5)["moveCost"] = 7;
        });
        var m = PorModule.Open(dir).Program()!;
        Assert.Equal(99, m.Spells()![1].CastTime);
        Assert.Equal(2222, m.ExperienceTable()![2 * 14 + 2]);
        var lt = m.LevelTables()!;
        Assert.Equal(77, lt.At(lt.Thac0 + 2 * 11 + 1));
        Assert.Equal(-7, (sbyte)lt.At(lt.ThiefRace + 1 * 8 + 2));
        Assert.Equal(-3, m.TurnUndeadTable()!.Value(1, 1));
        Assert.Equal(3, m.AiSteps()![6]);
        Assert.Equal(1234, m.RandomItemTable()![1 + 4]);
        Assert.Equal("Edited", m.TempleNames()![0]);
        Assert.Equal(7, m.CombatTables().Cost(5));
        // Everything else is unchanged.
        Assert.Equal(Original.Value.Spells()![2], m.Spells()![2]);
        Assert.Equal(Original.Value.TempleNames()![1], m.TempleNames()![1]);
    }

    [PorFact]
    public void The_rules_profile_checks_the_decoded_files()
    {
        var missing = PorModule.Open(MiniModule(d => d.Remove(TurnUndeadDocument.RelativePath)));
        Assert.Contains("rules/turnUndead.json", Assert.Throws<RulesTablesException>(() => missing.Program()).Message);

        var other = PorModule.Open(MiniModule(d => d[SpellsDocument.RelativePath]["profile"] = "por-1.2"));
        Assert.Contains("por-1.2", Assert.Throws<RulesTablesException>(() => other.Program()).Message);

        var bad = PorModule.Open(MiniModule(d => Find(d[TurnUndeadDocument.RelativePath]["rows"]!, "row", 2)["columns"]![0] = 200));
        Assert.Throws<RulesTablesException>(() => bad.Program());

        var dir = MiniModule();
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, ModuleManifest.FileName)))!;
        manifest["rules"] = "frua";
        File.WriteAllText(Path.Combine(dir, ModuleManifest.FileName), manifest.ToJsonString());
        Assert.Throws<UnknownRulesProfileException>(() => PorModule.Open(dir).Program());
    }

    [Fact]
    public void Encoding_refuses_values_out_of_range_and_repeated_rows()
    {
        TurnUndeadDocument Doc(int v, int secondRow) => new()
        {
            Profile = "por-1.3",
            DsOffset = 0x447,
            Rows = [.. Enumerable.Range(1, 10).Select(r => new TurnUndeadRow
                { Row = r == 2 ? secondRow : r, Columns = [v, 0, 0, 0, 0, 0, 0, 0, 0, 0] })],
        };
        Assert.Equal(100, PorRulesTables.Encode(Doc(-128, 2)).Count);
        Assert.Throws<RulesTablesException>(() => PorRulesTables.Encode(Doc(128, 2)));
        Assert.Throws<RulesTablesException>(() => PorRulesTables.Encode(Doc(0, 1)));

        // Two files owning one byte are refused when laid over the data segment.
        var spells = new SpellsDocument
        {
            Profile = "por-1.3",
            DsOffset = 0x447 - 16,
            Spells = [.. Enumerable.Range(1, Core.Game.SpellTable.Count).Select(id => new SpellEntry { Id = id })],
        };
        var set = new PorRulesSet { TurnUndead = Doc(0, 2), Spells = spells };
        Assert.Throws<RulesTablesException>(() => PorRulesTables.Apply(new byte[0x2000], new PorProgramOffsets(), set));
    }
}
