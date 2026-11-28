using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por.Dosbox;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>
/// The land's recovery (GAME.OVR 0x20D50) and its tile recolour (START 0x464:0x1E5C) against the program: the private
/// fixture <c>program-v1.3/tile_recolour.json</c> (<c>tools/scripts/por_recolour_oracle.py</c>) holds, per case, a
/// random 24 × 24 tile, colour map, random flag and RandSeed, the pixels the routine left and RandSeed after.
/// </summary>
public class OverlandRecoveryTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "tile_recolour.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/tile_recolour.json";
        }
    }

    [OracleFact]
    public void Recolour_matches_0x649C_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            n++;
            var px = Convert.FromHexString(k.GetProperty("before").GetString()!);
            var to = Convert.FromHexString(k.GetProperty("to").GetString()!);
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            OverlandRecovery.Recolour(px, Identity, to, k.GetProperty("flag").GetInt32() != 0, rng.Next);
            string got = Convert.ToHexString(px).ToLowerInvariant();
            if (got != k.GetProperty("after").GetString())
                fails.Add($"case {n}: pixels differ");
            if (rng.Seed != k.GetProperty("seed_after").GetUInt32())
                fails.Add($"case {n}: seed {rng.Seed:X8} != {k.GetProperty("seed_after").GetUInt32():X8}");
        }
        Assert.True(n > 0);
        Assert.True(fails.Count == 0, $"{fails.Count} differ:\n" + string.Join("\n", fails.Take(10)));
    }

    /// <summary>The colour maps' identity (0x649C's "from" in every caller).</summary>
    private static readonly byte[] Identity = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];

    private static byte[] Map(params (int From, int To)[] changes)
    {
        var m = (byte[])Identity.Clone();
        foreach (var (f, t) in changes)
            m[f] = (byte)t;
        return m;
    }

    /// <summary>Tables of this test's own (not the game's): two short lists, three pan frames.</summary>
    private static readonly OverlandRecoveryTables Test = new([0x10, 0x90], [0x20, 0x21, 0xA0], Identity,
        Map((0, 1), (7, 2)), Map((7, 2)), (3, 4), (9, 8), [(3, 4), (5, 6), (9, 8)]);

    /// <summary>The player's tables, read from the program as the importer does.</summary>
    private static OverlandRecoveryTables Program()
    {
        var p = GoldBox.Data.Por.PorProgramData.Open(PorData.Open())!;
        return p.Overland?.Recovery ?? throw new InvalidOperationException("no recovery tables found in the program");
    }

    [Fact]
    public void Animated_run_draws_per_frame_and_list_and_ends_fully_recoloured()
    {
        var tiles = new Dictionary<int, byte[]>();
        foreach (byte t in Test.ListA.Concat(Test.ListB))
            tiles[t] = Enumerable.Range(0, 576).Select(i => (byte)(i % 3 == 0 ? 0 : i % 3 == 1 ? 7 : 5)).ToArray();
        var frames = new List<(int, int)>();
        int pauses = 0;
        var rng = new TurboPascalRandom(1234);
        OverlandRecovery.Run(Test, true, t => tiles[t], rng.Next, (x, y) => frames.Add((x, y)), () => pauses++);
        // 3 frames × (2 tiles × 2 colours + 3 tiles × 1 colour) × 24 rows × 3 groups × 2 draws
        Assert.Equal(3 * 7 * 24 * 3 * 2, OverlandRecovery.AnimatedDraws(Test));
        Assert.Equal(OverlandRecovery.AnimatedDraws(Test), rng.Draws);
        Assert.Equal([(3, 4), (3, 4), (5, 6), (9, 8), (9, 8)], frames); // First, the pan, Last
        Assert.Equal(2, pauses);
        foreach (byte t in Test.ListA)
            Assert.DoesNotContain(tiles[t], p => p is 0 or 7);
        foreach (byte t in Test.ListB)
        {
            Assert.DoesNotContain(tiles[t], p => p == 7);
            Assert.Contains(tiles[t], p => p == 0);
        }
    }

    [PorFact]
    public void The_programs_tables_are_read_from_its_code_and_draw_51840_times()
    {
        // 0x20D50's loops: 11 and 18 list items (cmp 0Ah / 11h), 9 frames (cmp 8); First / Last are code immediates.
        var t = Program();
        Assert.Null(t.Problem());
        Assert.Equal(11, t.ListA.Count);
        Assert.Equal(18, t.ListB.Count);
        Assert.Equal(9, t.Pan.Count);
        Assert.Equal(Identity, t.From);
        Assert.Equal(t.Pan[0], t.First);
        Assert.Equal(t.Pan[^1], t.Last);
        Assert.Equal(51840, OverlandRecovery.AnimatedDraws(t));
    }

    [Fact]
    public void Still_run_draws_nothing()
    {
        var rng = new TurboPascalRandom(1);
        OverlandRecovery.Run(Test, false, _ => null, rng.Next);
        Assert.Equal(0, rng.Draws);
    }

    private sealed class Scripts(Dictionary<(int, int), byte[]> blocks) : IEclScriptSource
    {
        public byte[]? LoadBlock(int disk, int script) => blocks.GetValueOrDefault((disk, script));
    }

    /// <summary>NEWECL 26 with $49E6 = 0 (vector 4) and the given cells: the recovery's draws, or none.</summary>
    private static long SwitchDraws(int script, ushort yarash, ushort river, bool noTables = false)
    {
        var wild = new EclAsm()
            .Label("init").Op(EclOpcodes.Save, Imm8(0), Mem(0x49E6)).Op(EclOpcodes.Exit)
            .Label("other").Op(EclOpcodes.Exit)
            .Vector(0, "other").Vector(1, "other").Vector(4, "init");
        var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.NewEcl, Imm8(script)));
        rig.Host.Rng = new TurboPascalRandom(5);
        rig.Memory.Write(OverlandRecovery.YarashCell, yarash);
        rig.Memory.Write(OverlandRecovery.RiverCell, river);
        rig.Vm.Scripts = new Scripts(new() { [(0, script)] = wild.Build() });
        rig.Vm.OverlandRecovery = noTables ? null : Test;
        rig.Run();
        rig.Vm.FollowScriptChanges();
        return rig.Host.Rng.Draws;
    }

    [Fact]
    public void Switch_into_block_26_after_Yarash_runs_the_animated_recovery()
    {
        Assert.Equal(OverlandRecovery.AnimatedDraws(Test), SwitchDraws(26, 0xFE, 0));
        Assert.Equal(0, SwitchDraws(26, 0xFE, 255)); // $4AA2 set: the land has recovered already
        Assert.Equal(0, SwitchDraws(26, 0xFF, 0));   // a word compare with 0xFE (0x3F71)
        Assert.Equal(0, SwitchDraws(27, 0xFE, 0));   // block 26 only (0x3F5F)
        Assert.Equal(0, SwitchDraws(26, 0xFE, 0, noTables: true)); // a module without "recovery": skipped (diagnostic)
    }
}
