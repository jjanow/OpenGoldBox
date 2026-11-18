using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The party menu's Create (GAME.OVR 0x19EEB) against the program itself: the private fixture
/// <c>program-v1.3/chargen.json</c> (<c>tools/scripts/por_chargen_oracle.py</c>) holds the record, the race's effect
/// list and RandSeed the routine leaves behind for a seed, the four list picks and a number of re-rolls, run under
/// emulation. <see cref="CharacterCreation"/> with the tables read from the player's program must give the same bytes.
/// </summary>
public class CharacterCreationProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "chargen.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/chargen.json";
        }
    }

    /// <summary>Runs the steps as the routine does for one fixture case (list picks are 1-based).</summary>
    internal static (EclCharacter Character, uint Seed) Create(CreationTables t, BaseItemTable items, uint seed,
        IReadOnlyList<int> picks, int rerolls, string name = "ORACLE")
    {
        var rng = new TurboPascalRandom(seed);
        var cc = new CharacterCreation(t, rng.Next);
        cc.PickRace(CharacterCreation.RaceCodes[picks[0] - 1]);
        cc.PickGender(picks[1] - 1);
        cc.PickClass(cc.ClassChoices()[picks[2] - 1]);
        cc.PickAlignment(cc.AlignmentChoices()[picks[3] - 1]);
        for (int k = 0; k <= rerolls; k++)
            cc.Roll(items);
        cc.SetName(name);
        return (cc.Character, rng.Seed);
    }

    /// <summary>The far pointers (effect list 0x7F, item list 0xC8, equipment 0xCC..0xFF, next 0x104, combat 0x108): the
    /// emulator's heap addresses, stale on disk (character-format.md).</summary>
    private static bool Stale(int i) => i is >= 0x7F and <= 0x82 or >= 0xC8 and <= 0xFF or >= 0x104 and <= 0x10B;

    [OracleFact]
    public void Matches_0x19EEB_under_emulation()
    {
        var content = PorData.Open();
        var tables = content.Program()!.CreationTables();
        Assert.NotNull(tables);
        var items = PorFight.Kit(content)!.Items;
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            uint seed = k.GetProperty("seed").GetUInt32();
            var picks = k.GetProperty("picks").EnumerateArray().Select(x => x.GetInt32()).ToList();
            int rerolls = k.GetProperty("rerolls").GetInt32();
            var (c, after) = Create(tables!, items, seed, picks, rerolls);
            var want = Convert.FromHexString(k.GetProperty("record").GetString()!);
            for (int i = 0; i < want.Length; i++)
                if (!Stale(i) && c.Record[i] != want[i])
                    fails.Add($"case {n} [{string.Join(",", picks)}] +{i:X2}: {c.Record[i]:X2} vs {want[i]:X2}");
            var effects = k.GetProperty("effects").EnumerateArray().Select(x => x.GetString()!).ToList();
            var got = c.Effects.Select(e => Convert.ToHexString(e, 0, 5).ToLowerInvariant()).ToList();
            if (!effects.SequenceEqual(got))
                fails.Add($"case {n}: effects {string.Join(" ", got)} vs {string.Join(" ", effects)}");
            if (after != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"case {n}: RandSeed {after:X8} vs {k.GetProperty("seedAfter").GetUInt32():X8}");
            n++;
        }
        Assert.True(n > 400, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences: " + string.Join("; ", fails.Take(40)));
    }
}
