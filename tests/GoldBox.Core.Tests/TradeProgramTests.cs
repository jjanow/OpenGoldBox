using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// View's Trade against the program itself: the fixture <c>program-v1.3/trade.json</c> (private, from
/// <c>tools/scripts/por_trade_oracle.py</c>) holds the money transfer 0x250D5 and the item Trade 0x2276E (its redraw
/// trapped, the member pick scripted) run under emulation on made-up characters. <see cref="TradeRules"/> must give
/// the same records.
/// </summary>
public class TradeProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "trade.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/trade.json";
        }
    }

    private static EclCharacter Char(string hex, IEnumerable<string>? items = null)
    {
        var c = new EclCharacter(Convert.FromHexString(hex));
        foreach (var it in items ?? [])
            c.Items.Add(Convert.FromHexString(it));
        return c;
    }

    private static void Same(List<string> fails, string what, EclCharacter c, string wantHex)
    {
        var want = Convert.FromHexString(wantHex);
        for (int b = 0; b < want.Length; b++)
            if (want[b] != c[b] && b is not (>= 0xC8 and < 0x108) and not (>= 0x7F and < 0x83)) // the program's pointers
                fails.Add($"{what} +{b:X2}: {c[b]} vs {want[b]}");
    }

    [OracleFact]
    public void Money_transfer_matches_0x250D5_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, refused = 0, self = 0;
        foreach (var k in doc.RootElement.GetProperty("coins").EnumerateArray())
        {
            var giver = Char(k.GetProperty("giver").GetString()!);
            bool same = k.GetProperty("pick").ValueKind == JsonValueKind.Null;
            var pick = same ? giver : Char(k.GetProperty("pick").GetString()!);
            bool moved = TradeRules.GiveCoins(giver, pick, k.GetProperty("kind").GetInt32(),
                (ushort)k.GetProperty("amount").GetInt32(), Carrying.StrengthAllowance(pick));
            int messages = k.GetProperty("messages").GetInt32();
            if (moved != (messages == 0))
                fails.Add($"case {n}: moved {moved}, messages {messages}");
            Same(fails, $"case {n} giver", giver, k.GetProperty("giverAfter").GetString()!);
            if (!same)
                Same(fails, $"case {n} pick", pick, k.GetProperty("pickAfter").GetString()!);
            refused += moved ? 0 : 1;
            self += same ? 1 : 0;
            n++;
        }
        Assert.True(n >= 200 && refused > 20 && n - refused > 20 && self > 5, $"{n} cases, {refused} refused, {self} self");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }

    [OracleFact]
    public void Item_trade_matches_0x2276E_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var table = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        int n = 0, moved = 0;
        foreach (var k in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var giver = Char(k.GetProperty("giver").GetString()!, k.GetProperty("giverItems").EnumerateArray().Select(x => x.GetString()!));
            var other = Char(k.GetProperty("pick").GetString()!, k.GetProperty("pickItems").EnumerateArray().Select(x => x.GetString()!));
            string who = k.GetProperty("who").GetString()!;
            var pick = who switch { "self" => giver, "none" => null, _ => other };
            var item = giver.Items[k.GetProperty("item").GetInt32()];
            int messages = k.GetProperty("messages").GetInt32();
            if (pick is not null)
            {
                bool ok = TradeRules.GiveItem(giver, pick, item, Carrying.StrengthAllowance, c => StatRecalc.Recalculate(c, table));
                if (ok != (messages == 0))
                    fails.Add($"case {n}: gave {ok}, messages {messages}");
                moved += ok ? 1 : 0;
                // DS:471A takes the pick whether or not it could carry the item
                string target = k.GetProperty("target").GetString()!;
                if (target != (who == "self" ? "giver" : "pick"))
                    fails.Add($"case {n}: DS:471A {target}");
            }
            else if (messages != 0)
                fails.Add($"case {n}: no pick but a message");
            void Items(string what, EclCharacter c, string prop)
            {
                var want = k.GetProperty(prop).EnumerateArray().Select(x => x.GetString()!).ToList();
                var have = c.Items.Select(i =>
                {
                    var b = (byte[])i.Clone();
                    Array.Clear(b, 0x2A, 4);
                    return Convert.ToHexString(b).ToLowerInvariant();
                }).ToList();
                if (!want.SequenceEqual(have))
                    fails.Add($"case {n} {what} items: {have.Count} vs {want.Count}");
            }
            Items("giver", giver, "giverItemsAfter");
            Items("pick", other, "pickItemsAfter");
            Same(fails, $"case {n} giver", giver, k.GetProperty("giverAfter").GetString()!);
            Same(fails, $"case {n} pick", other, k.GetProperty("pickAfter").GetString()!);
            n++;
        }
        Assert.True(n >= 200 && moved > 30, $"{n} cases, {moved} moved");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }
}
