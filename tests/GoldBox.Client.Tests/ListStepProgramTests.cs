using System.Text.Json;
using GoldBox.Client.Screens;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Client.Tests;

/// <summary>
/// The list window's highlight and page moves against the program itself: the fixture <c>program-v1.3/list_steps.json</c>
/// (private, from <c>tools/scripts/por_list_oracle.py</c>) holds GAME.OVR 0x2D594 (one row down / up, wrapping in the
/// window), 0x2D3D2 (past label rows) and 0x2D502 (a page down / up, then past labels) run under emulation on generated
/// windows. <see cref="ListWindowSteps"/> (the moves <see cref="ListPage"/> and VMENU make) must leave the same highlight
/// and window top.
/// </summary>
public class ListStepProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "list_steps.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/list_steps.json";
        }
    }

    [OracleFact]
    public void Steps_match_0x2D594_0x2D3D2_0x2D502_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, skipped = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (k.GetProperty("oob").GetBoolean())
            {
                // The program asked 0x2C8CF for an entry outside the list and read the label flag at 0000:0029.
                skipped++;
                continue;
            }
            string kind = k.GetProperty("kind").GetString()!;
            int rows = k.GetProperty("rows").GetInt32(), count = k.GetProperty("count").GetInt32();
            var labels = k.GetProperty("labels").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
            int top = k.GetProperty("top").GetInt32(), row = k.GetProperty("row").GetInt32();
            bool down = k.GetProperty("down").GetBoolean();
            int wantRow = k.GetProperty("rowAfter").GetInt32(), wantTop = k.GetProperty("topAfter").GetInt32();
            int gotRow, gotTop = top;
            switch (kind)
            {
                case "step":
                    gotRow = ListWindowSteps.SkipLabels(ListWindowSteps.Step(row, top, rows, count, down), top, rows, count,
                        down, labels.Contains);
                    break;
                case "skip":
                    gotRow = ListWindowSteps.SkipLabels(row, top, rows, count, down, labels.Contains);
                    break;
                default:
                    (gotRow, gotTop) = ListWindowSteps.Page(row, top, rows, count, down);
                    gotRow = ListWindowSteps.SkipLabels(gotRow, gotTop, rows, count, down, labels.Contains);
                    var drawn = k.GetProperty("drawn").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    if (drawn.Length != 1 || (short)drawn[0] != gotTop)
                        fails.Add($"{kind} rows {rows} count {count} top {top}: rows drawn from {string.Join(",", drawn)}");
                    break;
            }
            n++;
            if (gotRow != wantRow || gotTop != wantTop)
                fails.Add($"{kind} rows {rows} count {count} labels [{string.Join(",", labels)}] top {top} row {row} " +
                          $"down {down}: program ({wantRow}, {wantTop}), port ({gotRow}, {gotTop})");
        }
        Assert.True(n > 2900, $"only {n} cases ({skipped} skipped)");
        Assert.True(fails.Count == 0, $"{fails.Count} of {n} differ:\n" + string.Join("\n", fails.Take(20)));
    }
}
