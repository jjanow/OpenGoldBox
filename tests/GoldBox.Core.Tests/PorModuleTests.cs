using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;
using GoldBox.Data.Module.Por;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// The PoR importer on the player's v1.3 files (docs/module-format.md §16–§17): every block rebuilds from the
/// module, and the engine runs the same scripts and areas from the module as from the DAX files. Skipped without
/// <c>GOLDBOX_POR_DIR</c>. The totals are the v1.3 release's: both sides read <see cref="PorData.ReleaseDir"/>, so extra
/// saves in the folder (the test saves B–I of <c>tools/scripts/por_test_saves.py</c>, a played game's) do not count.
/// </summary>
public class PorModuleTests(ITestOutputHelper output)
{
    [PorFact]
    public void Every_imported_block_rebuilds_byte_for_byte()
    {
        var r = PorRoundTrip.Check(PorData.OpenRelease(), PorModuleData.Open());
        output.WriteLine("left out of the release copy: " + string.Join(", ", PorData.NotRelease));
        output.WriteLine(string.Join(", ", r.Checked.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Empty(r.Mismatches);
        Assert.Equal(29, r.Checked["ECL"]);
        Assert.Equal(29, r.Checked["GEO"]);
        Assert.Equal(172, r.Checked["MONCHA"]);
        Assert.Equal(105, r.Checked["MONITM"]);
        Assert.Equal(42, r.Checked["MONSPC"]);
        Assert.Equal(46, r.Checked["ITEM"]);
        Assert.Equal(1, r.Checked["ITEMS"]);
        foreach (var engine in new[] { "ECL", "GEO", "MONCHA", "MONITM", "MONSPC", "ITEM" })
            Assert.Equal(r.Checked[engine], r.Checked[engine + " (engine)"]);
        // Art (every block of every art DAX; the pictures as the engine decodes them), the program's tables and
        // texts, and the shipped saves (held only as engine saves: no copy of the original files in saves/).
        Assert.Equal(822, r.Checked["ART"]);
        Assert.Equal(783, r.Checked["ART (engine)"]);
        Assert.Equal(1, r.Checked["PROGRAM"]);
        Assert.Equal(PorRulesTables.Files.Count, r.Checked["RULES"]);
        Assert.False(r.Checked.ContainsKey("SAVE"));
        Assert.Equal(36, r.Checked["ENGINE SAVE"]);  // every original save file written back from the engine saves
        Assert.Equal(36, PorModuleData.Report.Saves); // read as inputs (manifest)
        Assert.Equal(["a.json", "j.json"], Directory.EnumerateFiles(PorModuleData.Open().SaveFolder!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        // The overland: tiles and special locations as bytes, as the engine reads them (and the combat terrain), and
        // every area's column.
        Assert.Equal(2, r.Checked["OVERLAND"]);
        Assert.Equal(3, r.Checked["OVERLAND (engine)"]);
        Assert.Equal(29, r.Checked["OVERLAND (areas)"]);
    }

    [PorFact]
    public void The_overland_is_imported_into_rules_and_placed_by_the_wilderness_areas()
    {
        var dir = PorData.OpenRelease();
        var m = PorModuleData.Open();
        Assert.True(PorModuleData.Report.Overland);
        // Located through GAME.OVR's code at the v1.3 offsets (docs/re/geo-format.md "Wilderness (overland)").
        Assert.Equal(new PorOverlandOffsets(0x3648, 0x0C36, 0x3C76), dir.Program()!.OverlandOffsets);
        var a = ((IPorContent)dir).Overland()!;
        var b = ((IPorContent)m).Overland()!;
        Assert.Equal((44, 36, 9), (b.Width, b.Height, b.Sites.Count));
        Assert.Equal(a.Tiles, b.Tiles);
        Assert.Equal(a.Sites, b.Sites);
        // Areas 25-27 sit at the columns the program's view modes 2-4 use; no other area has an overland.
        AreaDocument Area(int id) => ModuleJson.Read<AreaDocument>(m.Modules.Main.Find(ResourceKey.Area(id), ".json")!);
        Assert.Equal([0, 13, 26], new[] { 25, 26, 27 }.Select(id => Area(id).Overland!.Column));
        Assert.All(Enumerable.Range(0, 33), id => Assert.Equal(((IPorContent)dir).OverlandColumn(id), m.OverlandColumn(id)));
        Assert.Null(m.OverlandColumn(24));
        // The engine draws and fights on the same squares from either source.
        var cells = new Dictionary<ushort, ushort> { [OverlandMap.Cell4AB3] = 0xFE };
        ushort Cell(ushort c) => cells.GetValueOrDefault(c);
        var ta = dir.Program()!.CombatTables();
        var tb = m.Program()!.CombatTables();
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 44; x++)
            {
                Assert.Equal(a.DrawnTile(x, y, Cell), b.DrawnTile(x, y, Cell));
                Assert.Equal(ta.WildTerrain(x, y), tb.WildTerrain(x, y));
            }
    }

    [PorFact]
    public void The_combat_wilderness_terrain_comes_from_the_overland_not_the_data_segment()
    {
        var p = PorModuleData.Open().Program()!;
        var tiles = (byte[])p.Overland!.Tiles.Clone();
        tiles[5 * 44 + 7] ^= 0xFF;
        var edited = new PorProgramData { Ds = p.Ds, Offsets = p.Offsets, Overland = new OverlandMap(44, 36, tiles, p.Overland.Sites) };
        Assert.Equal(tiles[5 * 44 + 7], edited.CombatTables().WildTerrain(7, 5));
        Assert.NotEqual(p.CombatTables().WildTerrain(7, 5), edited.CombatTables().WildTerrain(7, 5));
    }

    [PorFact]
    public void The_module_holds_the_program_tables_texts_art_and_saves()
    {
        var rep = PorModuleData.Report;
        Assert.Equal((783, 39, 36), (rep.Pictures, rep.ArtData, rep.Saves));
        Assert.Equal(PorTextCatalog.Overlay.Count + PorTextCatalog.Image.Count, rep.Texts);
        var m = PorModuleData.Open();
        var dir = PorData.OpenRelease();
        Assert.Equal(PorRoundTrip.Describe(dir.Program()!), PorRoundTrip.Describe(m.Program()!));
        // The tables the engine builds from them.
        var a = dir.Program()!;
        var b = m.Program()!;
        Assert.Equal(a.RandomItemTable(), b.RandomItemTable());
        Assert.Equal(a.ExperienceTable(), b.ExperienceTable());
        Assert.Equal(a.AiSteps(), b.AiSteps());
        Assert.Equal(a.TempleNames(), b.TempleNames());
        Assert.Equal(Enumerable.Range(1, 255).Select(a.ItemWord), Enumerable.Range(1, 255).Select(b.ItemWord));
        Assert.Equal(a.CampMenu(), b.CampMenu());
        Assert.Equal(Enumerable.Range(0, 0x1000).Select(a.NpcPortraitByte), Enumerable.Range(0, 0x1000).Select(b.NpcPortraitByte));
        // The 3D view's art: symbols, font and every wall set the same from the module.
        var va = PorViewArt.Load(dir);
        var vb = PorViewArt.Load(m);
        Assert.Equal(va.Layout.Start, vb.Layout.Start);
        Assert.Equal(va.Program.SkyColours, vb.Program.SkyColours);
        Assert.Equal(va.Program.ExploreCommands, vb.Program.ExploreCommands);
        byte[] Symbols(PorViewArt v) => [.. Enumerable.Range(0, 256).SelectMany(i => v.Symbols.Tile(i).ToArray())];
        for (int disk = 1; disk <= 8; disk++)
            for (int set = 0; set < 64; set++)
            {
                va.LoadWallSet(disk, 1, set);
                vb.LoadWallSet(disk, 1, set);
                Assert.Equal(va.Walls.Data.ToArray(), vb.Walls.Data.ToArray());
                Assert.Equal(Symbols(va), Symbols(vb));
            }
        Assert.Equal(va.Diagnostics, vb.Diagnostics);
        IndexedFrame Text(PorViewArt v)
        {
            var f = new IndexedFrame(320, 16);
            v.Font.Draw(f, 0, 0, new string([.. Enumerable.Range(32, 40).Select(c => (char)c)]), 15, 1);
            v.Font.Draw(f, 0, 8, new string([.. Enumerable.Range(72, 40).Select(c => (char)c)]), 15, 1);
            return f;
        }
        Assert.Equal(Text(va).Pixels.ToArray(), Text(vb).Pixels.ToArray());
    }

    [PorFact]
    public void Placed_treasure_and_monster_item_lists_share_ids_so_treasure_has_its_own_kind()
    {
        var rep = PorModuleData.Report;
        Assert.Equal(new Dictionary<int, List<int>>
        {
            [1] = [6, 9, 22], [2] = [1, 2, 3, 5], [3] = [51, 53], [4] = [29], [8] = [32],
        }, rep.ItemIdCollisions);
        Assert.Equal(11, rep.CollisionsWithDifferentContent);
        Assert.Equal((172, 105, 42, 46, 128), (rep.Monsters, rep.ItemLists, rep.EffectLists, rep.TreasureLists, rep.ItemTypes));
        // Both lists of disk 1 id 6 are in the module, under different kinds.
        var m = PorModuleData.Open();
        Assert.NotNull(m.RecordList(ResourceKey.ItemList(1, 6)));
        Assert.NotNull(m.RecordList(ResourceKey.TreasureList(1, 6)));
    }

    [PorFact]
    public void Areas_list_the_maps_their_scripts_load()
    {
        var m = PorModuleData.Open();
        AreaDocument Area(int id) => ModuleJson.Read<AreaDocument>(m.Modules.Main.Find(ResourceKey.Area(id), ".json")!);
        Assert.Equal([16, 30], Area(16).Maps);
        Assert.Equal([0], Area(8).Maps);                          // council: map 0, no map of its own
        Assert.Empty(Area(19).Views);                             // silver dragon lair: no LOADFILES
        Assert.Equal(["3d", "overland-tiles"], Area(26).Views);
        Assert.True(Area(0).NoWrapOffMap);
        Assert.True(Area(10).NoWrapOffMap);
        Assert.False(Area(24).NoWrapOffMap);
        Assert.Contains("[?]", Area(5).Notes);                    // the hedge maze: $9AD8 reads a cell set only behind IFs
        // Computed operands the code before them fixes: a facing table (3), 3 + Random(4) (5), a facing table that
        // also leaves for areas 14 and 26 after switching the disk (24).
        Assert.Equal([3, 5], Area(3).Maps);
        Assert.Equal([3, 4, 5, 6], Area(5).Maps);
        Assert.Equal([14, 24, 26, 31], Area(24).Maps);
        Assert.DoesNotContain("[?]", Area(24).Notes);
        // The engine reads the flag from the area: the same squares wrap as under the program's rule.
        var dir = PorData.OpenRelease();
        Assert.All(Enumerable.Range(0, 30), id => Assert.Equal(dir.NoWrapOffMap(id), m.NoWrapOffMap(id)));
    }

    [PorFact]
    public void The_module_answers_null_where_the_program_would_not_find_the_block()
    {
        var m = PorModuleData.Open();
        var dir = PorData.OpenRelease();
        // Script 20 and map 20 are on disk 2 only; the program reads ECL<disk>/GEO<disk> of the current disk.
        Assert.Equal(2, m.ScriptDisk(20));
        Assert.NotNull(m.LoadBlock(2, 20));
        Assert.Null(m.LoadBlock(3, 20));
        Assert.Null(dir.LoadBlock(3, 20));
        Assert.Null(m.LoadMap(3, 20));
        Assert.Null(dir.LoadMap(3, 20));
        // Monsters are banked by disk: ids 22 and 23 have no effects on disk 1 (no MON1SPC.DAX), as in the files.
        Assert.Equal(dir.Monster(1, 22)!.Effects.Count, m.Monster(1, 22)!.Effects.Count);
        Assert.Empty(m.Monster(1, 22)!.Effects);
        Assert.Null(m.Monster(1, 200));
        Assert.Equal(dir.ItemTypes()![30, BaseItemTable.Slot], m.ItemTypes()![30, BaseItemTable.Slot]);
    }

    /// <summary>Everything a run leaves in the VM and the map, as text, for comparing two runs.</summary>
    private static string Snapshot(EclVm vm, AreaMap? map, IEnumerable<string> calls, EclRunResult run)
    {
        var m = vm.Memory;
        return string.Join("\n",
            $"run {run}",
            $"script {vm.CurrentScript} disk {vm.CurrentDisk} memdisk {m.Disk} pos {m.Position.X},{m.Position.Y},{m.Position.Facing}",
            "script area " + Convert.ToHexString(m.ScriptArea),
            "4900 " + string.Join(",", m.Cells4900.ToArray()),
            "6B00 " + string.Join(",", m.Cells6B00.ToArray()),
            "9700 " + string.Join(",", m.Cells9700.ToArray()),
            $"map {map?.Id} " + (map is null ? "" : Convert.ToHexString(map.PlanesArray)),
            "calls " + string.Join(" | ", calls),
            "diag " + string.Join(" | ", vm.Diagnostics));
    }

    private static (PorGame Game, ScriptedEclHost Host, ScriptedAreaUi Ui) NewGame(Func<PorPlayer, PorGame> make)
    {
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var ui = new ScriptedAreaUi();
        var player = new PorPlayer(host, new ScriptedEncounterUi(host.MenuChoices) { StopWhenInputMissing = true },
            new RecordingCombatRunner(), ui, QuickFight: true);
        return (make(player), host, ui);
    }

    public static TheoryData<int> Scripts()
    {
        var d = new TheoryData<int>();
        foreach (var s in Enumerable.Range(0, 30).Where(i => i != 12))
            d.Add(s);
        return d;
    }

    [PorTheory]
    [MemberData(nameof(Scripts))]
    public void Every_script_enters_the_same_from_the_module(int script)
    {
        var dir = PorData.OpenRelease();
        var module = PorModuleData.Open();
        string Run(IPorContent? content)
        {
            var (g, host, ui) = NewGame(p => PorGame.Enter(content ?? dir, PorViewArt.Load(content ?? dir), script, 8, 8, Facing.North, p, 1234));
            return Snapshot(g.Vm, g.Loop.Map, host.Calls.Concat(ui.Calls), g.EntryRun);
        }
        var fromDax = Run(null);
        var counting = new CountingContent(module);
        var fromModule = Run(counting);
        Assert.Equal(fromDax, fromModule);
        // The module served the run (not the folder): the script, the fight kit's item types, the map if any.
        Assert.True(counting[nameof(IPorContent.LoadBlock)] > 0);
        Assert.True(counting[nameof(IPorContent.ItemTypes)] > 0);
        // A map in memory after the entry came from the module (5 and the wilderness load none at 8,8 from fresh memory).
        Assert.Equal(!fromDax.Contains("\nmap  ", StringComparison.Ordinal), counting[nameof(IPorContent.LoadMap)] > 0);
        if (script != 19)
        {
            // PorArea (the view check path) also runs from the module.
            var a = PorArea.Enter(dir, PorViewArt.Load(dir), script, 8, 8, Facing.North);
            var areaCounting = new CountingContent(module);
            var b = PorArea.Enter(areaCounting, PorViewArt.Load(module), script, 8, 8, Facing.North);
            Assert.True(areaCounting[nameof(IPorContent.LoadBlock)] > 0 && areaCounting[nameof(IPorContent.LoadMap)] > 0);
            Assert.Equal((a.Map.Id, a.SkyIndex, a.SkyAltIndex, a.EntryRun), (b.Map.Id, b.SkyIndex, b.SkyAltIndex, b.EntryRun));
            Assert.Equal(a.Map.PlanesArray, b.Map.PlanesArray);
            Assert.Equal(a.EntryCalls, b.EntryCalls);
        }
    }

    [PorTheory]
    [InlineData("SAVGAMA.DAT")]
    [InlineData("SAVGAMJ.DAT")]
    public void A_save_loads_and_plays_the_same_from_the_module(string save)
    {
        var dir = PorData.OpenRelease();
        var module = PorModuleData.Open();
        AreaCommand[] commands =
        [
            AreaCommand.Forward, AreaCommand.Forward, AreaCommand.TurnLeft, AreaCommand.Forward, AreaCommand.Forward,
            AreaCommand.Forward, AreaCommand.TurnRight, AreaCommand.Forward, AreaCommand.Search, AreaCommand.Forward,
            AreaCommand.TurnAround, AreaCommand.Forward, AreaCommand.Forward, AreaCommand.Forward,
        ];
        List<string> Run(IPorContent? content)
        {
            // The module run reads the save from the module's saves/ folder (its engine save, the only form the module
            // keeps): nothing from the game folder.
            var path = content is null ? dir.Find(save)! : Path.Combine(content.SaveFolder!, PorEngineSave.FileName(save[6]));
            var (g, host, ui) = NewGame(p => PorGame.FromSave(content ?? dir, PorViewArt.Load(content ?? dir), path, p, 99));
            var states = new List<string> { Snapshot(g.Vm, g.Loop.Map, host.Calls.Concat(ui.Calls), g.EntryRun) };
            foreach (var c in commands)
            {
                try
                {
                    g.Loop.Execute(c);
                }
                catch (Exception e) when (e is InvalidOperationException or EclVmFault)
                {
                    states.Add("stopped: " + e.GetType().Name);
                    break;
                }
                states.Add($"{c}: " + Snapshot(g.Vm, g.Loop.Map, host.Calls.Concat(ui.Calls), g.EntryRun));
            }
            return states;
        }
        var fromDax = Run(null);
        var counting = new CountingContent(module);
        var fromModule = Run(counting);
        output.WriteLine($"{save}: {fromDax.Count} states; module calls {string.Join(", ", counting.Calls.Select(kv => $"{kv.Key} {kv.Value}"))}");
        // The save holds the script bytes (no script load); the map is read again from the module.
        Assert.True(counting[nameof(IPorContent.LoadMap)] > 0 && counting[nameof(IPorContent.ItemTypes)] > 0);
        Assert.Equal(fromDax.Count, fromModule.Count);
        for (int i = 0; i < fromDax.Count; i++)
            Assert.Equal(fromDax[i], fromModule[i]);
    }

    /// <summary>
    /// docs/module-format.md §15: a game loaded from the module's engine save (<c>saves/a.json</c>) is the game loaded
    /// from the original files in the game folder: the same VM state, party records, program variables and entry run,
    /// and the same states over a walk.
    /// </summary>
    [PorTheory]
    [InlineData('A')]
    [InlineData('J')]
    public void A_game_from_the_engine_save_is_the_game_from_the_original_files(char slot)
    {
        var dir = PorData.OpenRelease();
        var module = PorModuleData.Open();
        var json = Path.Combine(module.SaveFolder!, PorEngineSave.FileName(slot));
        var doc = PorEngineSave.Read(json);
        Assert.Equal(PorEngineSave.Identity(module.Modules), doc.Module);
        Assert.Equal(EclProfiles.Por13Name, doc.Vm);
        Assert.Equal(slot.ToString(), doc.Por!.Slot);
        Assert.True(doc.Script.MatchesModule);
        Assert.Equal(6, doc.Party!.Count);
        // The save file and the party the original files give (the reader before engine saves existed).
        var originalPath = dir.Find(PorSaveGame.FileName(slot))!;
        Assert.Equal(File.ReadAllBytes(originalPath), PorEngineSave.ToSavgam(doc));
        string Records(EclParty p) => string.Join("/", p.Members.Select(c => Convert.ToHexString(c.Record) + "|" +
            string.Join(",", c.Items.Select(Convert.ToHexString)) + "|" + string.Join(",", c.Effects.Select(Convert.ToHexString))));
        Assert.Equal(Records(PorParty.Load(PorSaveGame.Open(originalPath), dir.Root)), Records(PorEngineSave.Party(doc)!));
        AreaCommand[] commands =
        [
            AreaCommand.Forward, AreaCommand.TurnLeft, AreaCommand.Forward, AreaCommand.Forward, AreaCommand.TurnRight,
            AreaCommand.Forward, AreaCommand.Search, AreaCommand.Forward, AreaCommand.TurnAround, AreaCommand.Forward,
        ];
        (List<string> States, string Program, List<string> Party) Run(bool engine)
        {
            var (g, host, ui) = NewGame(p => engine
                ? PorGame.FromSave(module, PorViewArt.Load(module), json, p, 99)
                : PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find(PorSaveGame.FileName(slot))!, p, 99));
            var m = g.Memory;
            var program = $"view {m.ViewMode} speed {m.GameSpeed} disk {m.Disk} current {m.Party!.CurrentIndex}";
            var party = m.Party.Members.Select(c => Convert.ToHexString(c.Record) + "|" +
                string.Join(",", c.Items.Select(Convert.ToHexString)) + "|" + string.Join(",", c.Effects.Select(Convert.ToHexString))).ToList();
            var states = new List<string> { Snapshot(g.Vm, g.Loop.Map, host.Calls.Concat(ui.Calls), g.EntryRun) };
            foreach (var c in commands)
            {
                try
                {
                    g.Loop.Execute(c);
                }
                catch (Exception e) when (e is InvalidOperationException or EclVmFault)
                {
                    states.Add("stopped: " + e.GetType().Name);
                    break;
                }
                states.Add($"{c}: " + Snapshot(g.Vm, g.Loop.Map, host.Calls.Concat(ui.Calls), g.EntryRun));
            }
            return (states, program, party);
        }
        var original = Run(engine: false);
        var engine = Run(engine: true);
        Assert.Equal(original.Program, engine.Program);
        Assert.Equal(original.Party, engine.Party);
        Assert.Equal(original.States.Count, engine.States.Count);
        for (int i = 0; i < original.States.Count; i++)
            Assert.Equal(original.States[i], engine.States[i]);
    }

    /// <summary>The importer's engine saves name the module's content hash, and re-importing gives the same hash.</summary>
    [PorFact]
    public void The_engine_saves_name_the_module()
    {
        var module = PorModuleData.Open();
        Assert.Equal(2, PorModuleData.Report.EngineSaves);
        var hash = ModuleContentHash.Of(module.Modules);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        foreach (var slot in "AJ")
            Assert.Equal(hash, PorEngineSave.Read(Path.Combine(module.SaveFolder!, PorEngineSave.FileName(slot))).Module.ContentHash);
        // A second import of the same folder (as the Client's cache does after an importer change) gives the same hash
        // and the same engine saves. The folder is left for the temp folder's own cleanup, as PorModuleData's.
        var again = Path.Combine(Path.GetTempPath(), "goldbox-tests", $"por-module-{Guid.NewGuid():N}");
        PorImporter.Import(PorData.ReleaseDir, again);
        Assert.Equal(hash, ModuleContentHash.Of(again));
        foreach (var slot in "AJ")
            Assert.Equal(File.ReadAllText(Path.Combine(module.SaveFolder!, PorEngineSave.FileName(slot))),
                File.ReadAllText(Path.Combine(again, "saves", PorEngineSave.FileName(slot))));
    }

    public static TheoryData<string> CaptureNames()
    {
        var d = new TheoryData<string>();
        foreach (var s in PorGoldenCaptures.All)
            d.Add(s.Name);
        return d;
    }

    /// <summary>Every golden DOSBox capture replays from the module exactly as it does from the DAX files.</summary>
    [CaptureTheory]
    [MemberData(nameof(CaptureNames))]
    public void Golden_captures_replay_the_same_from_the_module(string name)
    {
        var s = PorGoldenCaptures.Find(name)!;
        var a = new EclCaptureComparer(Captures.Dir!, PorData.OpenRelease()).Compare(s);
        var b = new EclCaptureComparer(Captures.Dir!, PorModuleData.Open()).Compare(s);
        Assert.Equal(a.ToString(), b.ToString());
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(a.Divergences, b.Divergences);
        Assert.Equal((a.VmSteps, a.HostDraws, a.TraceMatched, a.TraceTotal, a.StopReason),
            (b.VmSteps, b.HostDraws, b.TraceMatched, b.TraceTotal, b.StopReason));
        Assert.Equal(a.Rolls, b.Rolls);
    }

    [CaptureFact]
    public void Golden_captures_read_scripts_maps_and_monsters_from_the_module()
    {
        var counting = new CountingContent(PorModuleData.Open());
        var cmp = new EclCaptureComparer(Captures.Dir!, counting);
        foreach (var s in PorGoldenCaptures.All)
            cmp.Compare(s);
        output.WriteLine(string.Join(", ", counting.Calls.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(counting[nameof(IPorContent.LoadBlock)] > 0);
        Assert.True(counting[nameof(IPorContent.LoadMap)] > 0);
        Assert.True(counting[nameof(IPorContent.Monster)] > 0);
    }
}
