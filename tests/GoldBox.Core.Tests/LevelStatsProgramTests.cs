using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The level-stats routine (GAME.OVR 0x2AA87) and the race level limit (START 0x3AF7) against the program itself: the
/// private fixture <c>program-v1.3/level_stats.json</c> (<c>tools/scripts/por_level_oracle.py</c>) holds 400 made-up
/// creature records with the record after 0x2AA87 and START 0x3AF7's answer for each class, run under emulation.
/// <see cref="LevelStats"/> with the tables read from the player's program must give the same bytes.
/// </summary>
public class LevelStatsProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "level_stats.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/level_stats.json";
        }
    }

    [OracleFact]
    public void Matches_0x2AA87_and_START_0x3AF7_under_emulation()
    {
        var tables = PorSpellData.LevelTables(PorProgram.Open(PorData.Open())!);
        Assert.NotNull(tables);
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(k.GetProperty("before").GetString()!);
            var limits = k.GetProperty("limits").EnumerateArray().Select(x => x.GetInt32()).ToList();
            for (int cls = 0; cls < 8; cls++)
                if (LevelStats.RaceLimitStops(new EclCharacter((byte[])before.Clone()), cls) != (limits[cls] != 0))
                    fails.Add($"case {n} class {cls}: limit {limits[cls]}");
            var c = new EclCharacter((byte[])before.Clone());
            LevelStats.Apply(c, tables!);
            var want = Convert.FromHexString(k.GetProperty("after").GetString()!);
            for (int i = 0; i < want.Length; i++)
                if (c.Record[i] != want[i])
                    fails.Add($"case {n} +{i:X2}: {c.Record[i]:X2} vs {want[i]:X2}");
            n++;
        }
        Assert.Equal(400, n);
        Assert.True(fails.Count == 0, string.Join("; ", fails.Take(30)));
    }

    /// <summary>
    /// ADDNPC runs 0x2AA87 on the joined record (0x1F42C → 0x1F55A, after the icon slot and $6E3E + 1): every monster
    /// record v1.3's scripts add with ADDNPC (fixture <c>npcs</c>: 17 records of 14 ids over the sets), joined through
    /// the VM with the level seams wired as <c>PorGame</c> wires them, comes out as the program's routine leaves it,
    /// apart from what ADDNPC itself writes around it (icon slot 0xBF, morale 0x84, hostile 0x10E).
    /// </summary>
    [OracleFact]
    public void AddNpc_runs_the_level_stats_on_the_joined_record()
    {
        var tables = PorSpellData.LevelTables(PorProgram.Open(PorData.Open())!)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, changed = 0;
        foreach (var k in doc.RootElement.GetProperty("npcs").EnumerateArray())
        {
            var before = Convert.FromHexString(k.GetProperty("before").GetString()!);
            var want = Convert.FromHexString(k.GetProperty("after").GetString()!);
            var party = new EclParty();
            party.Members.Add(new EclCharacter { Name = "A" });
            party.CurrentIndex = 0;
            var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.AddNpc, Opnd.Imm8(k.GetProperty("id").GetInt32()), Opnd.Imm8(100))
                .Op(EclOpcodes.Exit), null, m => m.Party = party);
            rig.Memory.Write(EclPartyRules.PartySizeCell, 1);
            FightKit.WireLevels(rig.Vm.Effects, tables);
            rig.Host.NpcFactory = _ => new EclCharacter((byte[])before.Clone());
            rig.Run();
            Assert.Equal(2, party.Members.Count);
            var got = party.Members[1].Record;
            string id = $"set {k.GetProperty("set").GetInt32()} id {k.GetProperty("id").GetInt32()}";
            for (int i = 0; i < want.Length; i++)
                if (i is not (0xBF or EclPartyRules.Morale or EclPartyRules.Hostile) && got[i] != want[i])
                    fails.Add($"{id} +{i:X2}: {got[i]:X2} vs {want[i]:X2}");
            if (!before.AsSpan().SequenceEqual(want))
                changed++;
            Assert.DoesNotContain(rig.AllDiagnostics, d => d.Contains("0x2AA87"));
            n++;
        }
        Assert.Equal(17, n);
        Assert.True(changed > 0);
        Assert.True(fails.Count == 0, string.Join("; ", fails.Take(30)));
    }
}
