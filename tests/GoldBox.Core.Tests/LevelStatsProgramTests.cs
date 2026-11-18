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
}
