using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The stat recalculation (START 0x1758) against the program itself: the fixture
/// <c>program-v1.3/stat_recalc.json</c> (private) holds creatures of the fight_quick states, many with random readied
/// flags, pluses, Str/Dex, money and the Str bonus flag, before and after the program's routine ran under emulation
/// on the DOSBox-X RAM. <see cref="StatRecalc.Recalculate"/> must give the same record bytes.
/// </summary>
public class StatRecalcProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "stat_recalc.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/stat_recalc.json";
        }
    }

    [OracleFact]
    public void Matches_START_0x1758_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var items = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        int n = 0;
        var fails = new List<string>();
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("before").GetString()!));
            foreach (var it in k.GetProperty("items").EnumerateArray())
                c.Items.Add(Convert.FromHexString(it.GetString()!));
            StatRecalc.Recalculate(c, items);
            var want = Convert.FromHexString(k.GetProperty("after").GetString()!);
            for (int b = 0; b < want.Length; b++)
                if (want[b] != c[b] && b is not (>= 0xC8 and < 0x108)) // pointers (items, slots, list) are the program's
                    fails.Add($"case {n} +{b:X2}: {c[b]} vs {want[b]}");
            n++;
        }
        Assert.True(n > 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }
}
