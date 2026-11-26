using System.Text.Json;
using GoldBox.Core.Explore;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// DOS's line inputs against the program itself: the private fixture <c>program-v1.3/line_input.json</c>
/// (<c>tools/scripts/por_input_oracle.py</c>) holds START 0x7A73 (the line input) and START 0x7BF6 (INPUTNUM: the line
/// with 6 characters, Val, asked again until the value is taken) run under emulation on generated keys.
/// <see cref="DosLineInput"/> must give the same line, the same number and read the same keys.
/// </summary>
public class LineInputProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "line_input.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/line_input.json";
        }
    }

    [OracleFact]
    public void Matches_START_0x7A73_and_0x7BF6_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int lines = 0, numbers = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            Assert.False(k.GetProperty("starved").GetBoolean());
            var keys = k.GetProperty("keys").EnumerateArray().Select(x => x.GetInt32()).ToList();
            int read = k.GetProperty("read").GetInt32();
            if (k.GetProperty("kind").GetString() == "str")
            {
                lines++;
                string want = new([.. Convert.FromHexString(k.GetProperty("text").GetString()!).Select(b => (char)b)]);
                var got = DosLineInput.Line(keys, 0, k.GetProperty("max").GetInt32());
                if (got is not { } g || g.Text != want || g.Next != read)
                    fails.Add($"line [{string.Join(",", keys)}]: program \"{want}\" after {read} keys, port \"{got?.Text}\" after {got?.Next}");
            }
            else
            {
                numbers++;
                int want = k.GetProperty("value").GetInt32();
                int at = 0;
                ushort? got = null;
                while (got is null && DosLineInput.Line(keys, at, 6) is { } l)
                {
                    at = l.Next;
                    got = DosLineInput.Number(l.Text);
                }
                if (got != want || at != read)
                    fails.Add($"number [{string.Join(",", keys)}]: program {want} after {read} keys, port {got} after {at}");
            }
        }
        Assert.True(lines >= 500 && numbers >= 300, $"{lines} lines, {numbers} numbers");
        Assert.True(fails.Count == 0, $"{fails.Count} differ:\n" + string.Join("\n", fails.Take(20)));
    }
}
