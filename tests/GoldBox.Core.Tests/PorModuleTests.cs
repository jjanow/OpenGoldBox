using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;
using GoldBox.Importer;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// The PoR importer on the player's v1.3 files (docs/module-format.md §16–§17): every block rebuilds from the
/// module, and the engine runs the same scripts and areas from the module as from the DAX files. Skipped without
/// <c>GOLDBOX_POR_DIR</c>.
/// </summary>
public class PorModuleTests(ITestOutputHelper output)
{
    [PorFact]
    public void Every_imported_block_rebuilds_byte_for_byte()
    {
        var r = PorRoundTrip.Check(PorData.Open(), PorModuleData.Open());
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
        Assert.Equal([24, 31], Area(24).Maps);
        Assert.Equal([16, 30], Area(16).Maps);
        Assert.Equal([0], Area(8).Maps);                          // council: map 0, no map of its own
        Assert.Empty(Area(19).Views);                             // silver dragon lair: no LOADFILES
        Assert.Equal(["3d", "overland-tiles"], Area(26).Views);
        Assert.True(Area(0).NoWrapOffMap);
        Assert.True(Area(10).NoWrapOffMap);
        Assert.False(Area(24).NoWrapOffMap);
        Assert.Contains("[?]", Area(5).Notes);                    // the hedge maze loads computed maps only
    }

    [PorFact]
    public void The_module_answers_null_where_the_program_would_not_find_the_block()
    {
        var m = PorModuleData.Open();
        var dir = PorData.Open();
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
        var dir = PorData.Open();
        var module = PorModuleData.Open();
        var art = PorViewArt.Load(dir);
        string Run(IPorContent? content)
        {
            var (g, host, ui) = NewGame(p => PorGame.Enter(dir, art, script, 8, 8, Facing.North, p, 1234, content));
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
            var b = PorArea.Enter(dir, PorViewArt.Load(dir), script, 8, 8, Facing.North, areaCounting);
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
        var dir = PorData.Open();
        var module = PorModuleData.Open();
        AreaCommand[] commands =
        [
            AreaCommand.Forward, AreaCommand.Forward, AreaCommand.TurnLeft, AreaCommand.Forward, AreaCommand.Forward,
            AreaCommand.Forward, AreaCommand.TurnRight, AreaCommand.Forward, AreaCommand.Search, AreaCommand.Forward,
            AreaCommand.TurnAround, AreaCommand.Forward, AreaCommand.Forward, AreaCommand.Forward,
        ];
        List<string> Run(IPorContent? content)
        {
            var (g, host, ui) = NewGame(p => PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find(save)!, p, 99, content));
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
        var a = new EclCaptureComparer(Captures.Dir!, PorData.Open()).Compare(s);
        var b = new EclCaptureComparer(Captures.Dir!, PorData.Open(), PorModuleData.Open()).Compare(s);
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
        var cmp = new EclCaptureComparer(Captures.Dir!, PorData.Open(), counting);
        foreach (var s in PorGoldenCaptures.All)
            cmp.Compare(s);
        output.WriteLine(string.Join(", ", counting.Calls.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(counting[nameof(IPorContent.LoadBlock)] > 0);
        Assert.True(counting[nameof(IPorContent.LoadMap)] > 0);
        Assert.True(counting[nameof(IPorContent.Monster)] > 0);
    }
}
