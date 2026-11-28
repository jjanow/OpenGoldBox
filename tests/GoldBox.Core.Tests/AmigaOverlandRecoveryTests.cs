using System.Text.Json;
using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;
using GoldBox.Core.Tests.Support;

namespace GoldBox.Core.Tests;

/// <summary>
/// The Amiga land's recovery (hunk 13 +0x2872) and its recolour (hunk 27 +0x7160) against the program itself: the
/// private fixture <c>amiga-v1.0/tile_recolour.json</c> (<c>tools/scripts/por_amiga_recolour_oracle.py</c>) holds
/// random tiles, maps, random flags and generator states, the beam bytes fed (an LCG's seed and read count), the
/// pixels the routines left, the generator state after and the recovery's view / copy / pause calls.
/// </summary>
public class AmigaOverlandRecoveryTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "amiga-v1.0", "tile_recolour.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (FixturePath is null)
                Skip = "needs the private fixture tests/fixtures-private/por/amiga-v1.0/tile_recolour.json";
            else if (PorAmigaData.Dir is null)
                Skip = "needs the player's Amiga Pool of Radiance v1.0 files (the tables): set " + PorAmigaData.EnvVar;
        }
    }

    public sealed class ProgramFactAttribute : FactAttribute
    {
        public ProgramFactAttribute()
        {
            if (PorAmigaData.Dir is null)
                Skip = "needs the player's Amiga Pool of Radiance v1.0 files: set " + PorAmigaData.EnvVar;
            else if (PorAmigaData.Missing is { } missing)
                Skip = missing;
        }
    }


    /// <summary>The tables read from the player's program (null without GOLDBOX_POR_AMIGA_DIR).</summary>
    private static AmigaOverlandRecoveryTables? ProgramTables() =>
        PorAmigaData.Dir is null || PorAmigaData.Missing is not null ? null
            : PorAmigaOverlandRecovery.Read(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir).Program));

    private static JsonElement Root() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement;

    /// <summary>The oracle's beam feed: x := x · 1103515245 + 12345, the byte (x &gt;&gt; 16) &amp; 0xFF.</summary>
    private sealed class Lcg(uint seed)
    {
        private uint _x = seed;
        public int Reads { get; private set; }

        public byte Next()
        {
            Reads++;
            _x = unchecked(_x * 1103515245u + 12345u);
            return (byte)(_x >> 16);
        }
    }

    [OracleFact]
    public void Recolour_matches_hunk_27_7160_under_emulation()
    {
        var tables = ProgramTables()!;
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root().GetProperty("cases").EnumerateArray())
        {
            n++;
            var px = Convert.FromHexString(k.GetProperty("before").GetString()!);
            var map = Convert.FromHexString(k.GetProperty("map").GetString()!);
            var gen = new AmigaOverlandRecovery.Generator(tables) { State = (byte)k.GetProperty("state").GetInt32() };
            var beam = new Lcg(k.GetProperty("beam_seed").GetUInt32());
            AmigaOverlandRecovery.Recolour(px, map, k.GetProperty("flag").GetInt32() != 0, gen, beam.Next);
            if (Convert.ToHexString(px).ToLowerInvariant() != k.GetProperty("after").GetString())
                fails.Add($"case {n}: pixels differ");
            if (gen.State != k.GetProperty("state_after").GetInt32())
                fails.Add($"case {n}: state {gen.State} != {k.GetProperty("state_after").GetInt32()}");
            if (beam.Reads != k.GetProperty("beam_reads").GetInt32())
                fails.Add($"case {n}: beam reads {beam.Reads} != {k.GetProperty("beam_reads").GetInt32()}");
        }
        Assert.True(n > 0);
        Assert.True(fails.Count == 0, $"{fails.Count} differ:\n" + string.Join("\n", fails.Take(10)));
    }

    [OracleFact]
    public void Recovery_matches_hunk_13_2872_under_emulation()
    {
        var tables = ProgramTables()!;
        int runs = 0;
        foreach (var r in Root().GetProperty("recovery").EnumerateArray())
        {
            runs++;
            var tiles = new Dictionary<int, byte[]>();
            foreach (var t in r.GetProperty("tiles").EnumerateObject())
                tiles[int.Parse(t.Name)] = Convert.FromHexString(t.Value.GetString()!);
            var gen = new AmigaOverlandRecovery.Generator(tables) { State = (byte)r.GetProperty("state").GetInt32() };
            var beam = new Lcg(r.GetProperty("beam_seed").GetUInt32());
            var calls = new List<string>();
            AmigaOverlandRecovery.Run(tables, r.GetProperty("animated").GetInt32() != 0, t => (tiles[t], 0), gen, beam.Next,
                (x, y) => { calls.Add($"view {x} {y}"); calls.Add("copy"); }, () => calls.Add("pause"));
            var want = r.GetProperty("calls").EnumerateArray()
                .Select(c => string.Join(' ', c.EnumerateArray().Select(e => e.ToString()))).ToList();
            Assert.Equal(want, calls);
            foreach (var t in r.GetProperty("after").EnumerateObject())
                Assert.True(Convert.ToHexString(tiles[int.Parse(t.Name)]).ToLowerInvariant() == t.Value.GetString(),
                    $"run {runs}: tile {t.Name} differs");
            Assert.Equal(r.GetProperty("state_after").GetInt32(), gen.State);
            Assert.Equal(r.GetProperty("beam_reads").GetInt32(), beam.Reads);
        }
        Assert.Equal(2, runs);
    }

    /// <summary>Tables of the routine's shape with made-up values (the real ones are the player's, read at import).</summary>
    private static AmigaOverlandRecoveryTables Synthetic() => new(
        [0x10, 0x11, 0x90], [0x12, 0x91], [0, 1, 2, 3, 4, 1, 6, 7, 8, 9, 10, 11, 2, 2, 10, 15], (1, 2), (5, 6),
        [(1, 2), (3, 4), (5, 6)], [.. Enumerable.Range(0, 16).Select(i => (byte)(15 - i))],
        [.. Enumerable.Range(0, 16).Select(i => (byte)((i * 7) & 15))]);

    [Fact]
    public void A_map_that_changes_a_pen_twice_is_refused()
    {
        var t = Synthetic();
        Assert.Null(t.Problem());
        var map = t.Map.ToArray();
        map[1] = 3; // 5 → 1 → 3
        Assert.NotNull((t with { Map = map }).Problem());
    }

    [Fact]
    public void Run_leaves_the_game_generator_alone_and_ends_fully_recoloured()
    {
        var tables = Synthetic();
        var tiles = new Dictionary<int, byte[]>();
        foreach (byte t in tables.ListA.Concat(tables.ListB))
            tiles[t] = Enumerable.Range(0, 576).Select(i => (byte)(32 + (i % 4 == 0 ? 5 : i % 4 == 1 ? 12 : i % 4 == 2 ? 13 : 14))).ToArray();
        var gen = new AmigaOverlandRecovery.Generator(tables);
        var rnd = new Random(1);
        int frames = 0, pauses = 0;
        AmigaOverlandRecovery.Run(tables, true, t => (tiles[t], 32), gen, () => (byte)rnd.Next(256), (_, _) => frames++, () => pauses++);
        Assert.Equal(tables.Pan.Count + 2, frames);
        Assert.Equal(2, pauses);
        Assert.True(gen.Calls > 0);
        foreach (var px in tiles.Values)
            Assert.DoesNotContain(px, p => p is 32 + 5 or 32 + 12 or 32 + 13 or 32 + 14);
    }

    [Fact]
    public void Document_round_trips_the_tables()
    {
        var t = Synthetic();
        var back = AmigaOverlandRecoveryDocument.From(t).ToTables()!;
        Assert.Equal(t.ListA, back.ListA);
        Assert.Equal(t.Map, back.Map);
        Assert.Equal(t.Pan, back.Pan);
        Assert.Equal(t.High, back.High);
        Assert.Equal((t.First, t.Last), (back.First, back.Last));
    }

    /// <summary>The reader on the player's <c>program</c>: the routine's shape, and the lists and pan squares equal to
    /// the DOS program's (read s60: the Amiga routine is 0x20D50's copy with its own map).</summary>
    [ProgramFact]
    public void Tables_read_from_the_program_have_DOS_lists_and_pan()
    {
        var t = ProgramTables();
        Assert.NotNull(t);
        Assert.Null(t!.Problem());
        Assert.Equal(11, t.ListA.Count);
        Assert.Equal(18, t.ListB.Count);
        Assert.Equal(9, t.Pan.Count);
        Assert.Equal(t.First, t.Pan[0]);
        Assert.Equal(t.Last, t.Pan[^1]);
        if (PorData.Dir is not null && GoldBox.Data.Por.PorProgramData.Open(PorData.Open()) is { Overland.Recovery: { } d })
        {
            Assert.Equal(d.ListA, t.ListA);
            Assert.Equal(d.ListB, t.ListB);
            Assert.Equal(d.Pan, t.Pan);
            Assert.Equal((d.First, d.Last), (t.First, t.Last));
        }
    }
}
