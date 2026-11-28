using System.Text;
using System.Text.Json;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The coin row parser GAME.OVR 0x2581C against the program: the private fixture <c>program-v1.3/coin_row.json</c>
/// (<c>tools/scripts/por_coinrow_oracle.py</c>) holds every DS:0FFA name as the money Trade, Drop and Take build their
/// rows, other letter cases and rows that match nothing, with the kind and label the routine returned under emulation.
/// <see cref="CoinRows.KindOf"/> must give the same kind (-1 where the program returns its uninitialised local, the
/// stack filler 0xEE) and <see cref="CoinRows.LabelKey"/> the string it copied (with the game files present).
/// </summary>
public class CoinRowProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "coin_row.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/coin_row.json";
        }
    }

    private static string? Label(byte[] ovr, string key)
    {
        int at = PorTextCatalog.Overlay[key];
        return Encoding.Latin1.GetString(ovr, at + 1, ovr[at]);
    }

    [OracleFact]
    public void Every_row_gives_the_programs_kind_and_label()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        int residue = doc.RootElement.GetProperty("residue").GetInt32();
        byte[]? ovr = PorData.Present ? File.ReadAllBytes(Path.Combine(PorData.Dir!, "GAME.OVR")) : null;
        int n = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string row = c.GetProperty("row").GetString()!;
            int kind = c.GetProperty("kind").GetInt32();
            string label = c.GetProperty("label").GetString()!;
            int got = CoinRows.KindOf(row);
            Assert.True(got == (kind == residue ? -1 : kind), $"row '{row}': program {kind}, engine {got}");
            if (got < 0)
                Assert.Equal("", label);
            else if (ovr is not null)
                Assert.Equal(label, Label(ovr, CoinRows.LabelKey(got)!));
            n++;
        }
        Assert.True(n > 400);
    }

    [OracleFact]
    public void Every_coin_name_row_parses_to_its_own_kind()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var names = doc.RootElement.GetProperty("names").EnumerateArray().Select(e => e.GetString()!).ToArray();
        int width = names.Max(s => s.Length);
        for (int k = 0; k < 7; k++)
        {
            Assert.Equal(k, CoinRows.KindOf(names[k] + " 12"));
            Assert.Equal(k, CoinRows.KindOf(names[k].PadLeft(width) + " 12"));
        }
    }
}
