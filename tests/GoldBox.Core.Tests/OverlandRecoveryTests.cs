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
            OverlandRecovery.Recolour(px, OverlandRecovery.From, to, k.GetProperty("flag").GetInt32() != 0, rng.Next);
            string got = Convert.ToHexString(px).ToLowerInvariant();
            if (got != k.GetProperty("after").GetString())
                fails.Add($"case {n}: pixels differ");
            if (rng.Seed != k.GetProperty("seed_after").GetUInt32())
                fails.Add($"case {n}: seed {rng.Seed:X8} != {k.GetProperty("seed_after").GetUInt32():X8}");
        }
        Assert.True(n > 0);
        Assert.True(fails.Count == 0, $"{fails.Count} differ:\n" + string.Join("\n", fails.Take(10)));
    }

    [Fact]
    public void Animated_run_draws_51840_times_and_ends_fully_recoloured()
    {
        var tiles = new Dictionary<int, byte[]>();
        foreach (byte t in OverlandRecovery.ListA.Concat(OverlandRecovery.ListB))
            tiles[t] = Enumerable.Range(0, 576).Select(i => (byte)(i % 3 == 0 ? 0 : i % 3 == 1 ? 7 : 5)).ToArray();
        var frames = new List<(int, int)>();
        int pauses = 0;
        var rng = new TurboPascalRandom(1234);
        OverlandRecovery.Run(true, t => tiles[t], rng.Next, (x, y) => frames.Add((x, y)), () => pauses++);
        Assert.Equal(51840, OverlandRecovery.AnimatedDraws);
        Assert.Equal(OverlandRecovery.AnimatedDraws, rng.Draws);
        Assert.Equal(11, frames.Count); // (17, 15), the 9 pan frames, (23, 25)
        Assert.Equal((17, 15), frames[0]);
        Assert.Equal((23, 25), frames[^1]);
        Assert.Equal(2, pauses);
        foreach (byte t in OverlandRecovery.ListA)
            Assert.DoesNotContain(tiles[t], p => p is 0 or 7);
        foreach (byte t in OverlandRecovery.ListB)
        {
            Assert.DoesNotContain(tiles[t], p => p == 7);
            Assert.Contains(tiles[t], p => p == 0);
        }
    }

    [Fact]
    public void Still_run_draws_nothing()
    {
        var rng = new TurboPascalRandom(1);
        OverlandRecovery.Run(false, _ => null, rng.Next);
        Assert.Equal(0, rng.Draws);
    }

    private sealed class Scripts(Dictionary<(int, int), byte[]> blocks) : IEclScriptSource
    {
        public byte[]? LoadBlock(int disk, int script) => blocks.GetValueOrDefault((disk, script));
    }

    /// <summary>NEWECL 26 with $49E6 = 0 (vector 4) and the given cells: the recovery's draws, or none.</summary>
    private static long SwitchDraws(int script, ushort yarash, ushort river)
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
        rig.Run();
        rig.Vm.FollowScriptChanges();
        return rig.Host.Rng.Draws;
    }

    [Fact]
    public void Switch_into_block_26_after_Yarash_runs_the_animated_recovery()
    {
        Assert.Equal(OverlandRecovery.AnimatedDraws, SwitchDraws(26, 0xFE, 0));
        Assert.Equal(0, SwitchDraws(26, 0xFE, 255)); // $4AA2 set: the land has recovered already
        Assert.Equal(0, SwitchDraws(26, 0xFF, 0));   // a word compare with 0xFE (0x3F71)
        Assert.Equal(0, SwitchDraws(27, 0xFE, 0));   // block 26 only (0x3F5F)
    }
}
