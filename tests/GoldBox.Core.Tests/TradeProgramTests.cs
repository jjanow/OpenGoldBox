using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
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
    public void Item_trade_and_the_givers_recalculation_match_the_program_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var table = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        int n = 0, moved = 0, stale = 0;
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
            // The Items loop's START 0x1758 on DS:5D92 after the command (0x224AF): the giver in full, its stale attack
            // and movement bytes (0xEE in every other case) rebuilt.
            StatRecalc.Recalculate(giver, table);
            Same(fails, $"case {n} giver after the loop", giver, k.GetProperty("giverAfterLoop").GetString()!);
            stale += giver[0x115] != 0xEE && Convert.FromHexString(k.GetProperty("giver").GetString()!)[0x115] == 0xEE ? 1 : 0;
            n++;
        }
        Assert.True(n >= 200 && moved > 30 && stale > 100, $"{n} cases, {moved} moved, {stale} stale givers rebuilt");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }

    [OracleFact]
    public void Copy_step_of_buy_and_loot_take_matches_0x6F0D_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var table = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        int n = 0, refused = 0, stale = 0;
        foreach (var k in doc.RootElement.GetProperty("copies").EnumerateArray())
        {
            var c = Char(k.GetProperty("record").GetString()!, k.GetProperty("items").EnumerateArray().Select(x => x.GetString()!));
            var item = Convert.FromHexString(k.GetProperty("item").GetString()!);
            bool ok = ShopRules.GiveCopy(c, item, Carrying.StrengthAllowance(c), x => StatRecalc.Recalculate(x, table));
            bool wantRefused = k.GetProperty("refused").GetInt32() != 0;
            if (ok == wantRefused || (k.GetProperty("messages").GetInt32() != 0) != wantRefused)
                fails.Add($"case {n}: gave {ok}, program refused {wantRefused}");
            var want = k.GetProperty("itemsAfter").EnumerateArray().Select(x => x.GetString()!).ToList();
            var have = c.Items.Select(i =>
            {
                var b = (byte[])i.Clone();
                Array.Clear(b, 0x2A, 4);
                return Convert.ToHexString(b).ToLowerInvariant();
            }).ToList();
            if (!want.SequenceEqual(have))
                fails.Add($"case {n} items: {have.Count} vs {want.Count}");
            Same(fails, $"case {n}", c, k.GetProperty("after").GetString()!);
            refused += wantRefused ? 1 : 0;
            stale += Convert.FromHexString(k.GetProperty("record").GetString()!)[0x115] == 0xEE ? 1 : 0;
            n++;
        }
        Assert.True(n >= 200 && refused > 20 && n - refused > 30 && stale > 100, $"{n} cases, {refused} refused, {stale} stale");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }

    /// <summary>
    /// TREASURE's item maker 0x25C99 run under emulation for every type 0..255 and 1744 random ones, RandSeed set: <see
    /// cref="TreasureMaker"/> with the player's random item array and the same seed makes the same 63-byte record
    /// (the link at +0x2A left out) and leaves the same RandSeed.
    /// </summary>
    public sealed class OracleDataFactAttribute : FactAttribute
    {
        public OracleDataFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/trade.json";
        }
    }

    [OracleDataFact]
    public void Treasure_item_maker_matches_0x25C99_under_emulation()
    {
        var maker = new TreasureMaker(PorProgram.Open(PorData.Open())!.RandomItemTable());
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, built = 0;
        foreach (var k in doc.RootElement.GetProperty("treasure").EnumerateArray())
        {
            n++;
            byte type = (byte)k.GetProperty("type").GetInt32();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var got = maker.Make(type, m => rng.Next((ushort)m));
            var want = Convert.FromHexString(k.GetProperty("item").GetString()!);
            if (want.Any(b => b != 0))
                built++;
            for (int b = 0; b < want.Length; b++)
                if (b is not (>= 0x2A and < 0x2E) && got[b] != want[b])
                    fails.Add($"type {type} seed {k.GetProperty("seed")}: +{b:X2} {got[b]} vs {want[b]}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"type {type} seed {k.GetProperty("seed")}: RandSeed differs ({rng.Draws} draws)");
        }
        Assert.True(n >= 2000 && built > 1000, $"{n} cases, {built} built");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// Pool 0x25191 and Share 0x252C3 (its member count 0x25269, the carry routines 0x24C89–0x24CDD) run under
    /// emulation on party lists with byte 0x84 varied and made-up pools: <see cref="ShopRules.PoolMoney"/> and <see
    /// cref="ShopRules.Share"/> leave the same records, pool and "pooled" byte DS:6DC0, and halt where the program
    /// stops (Share with money and no member that counts: run-time error 200, nothing changed).
    /// </summary>
    [OracleFact]
    public void Pool_and_share_match_0x25191_and_0x252C3_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, shares = 0, halts = 0;
        foreach (var k in doc.RootElement.GetProperty("money").EnumerateArray())
        {
            n++;
            var party = k.GetProperty("records").EnumerateArray().Select(r => Char(r.GetString()!)).ToList();
            var pool = k.GetProperty("pool").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            bool share = k.GetProperty("routine").GetString() == "share";
            bool halted = false;
            bool pooled = false;
            try
            {
                pooled = share ? ShopRules.Share(party, pool, Carrying.StrengthAllowance) : ShopRules.PoolMoney(party, pool);
            }
            catch (ProgramHaltException)
            {
                halted = true;
            }
            if (halted != k.GetProperty("halt").GetBoolean())
                fails.Add($"case {n}: halt {halted}");
            if (!halted && (pooled ? 1 : 0) != k.GetProperty("pooled").GetInt32())
                fails.Add($"case {n}: pooled {pooled} vs {k.GetProperty("pooled")}");
            var after = k.GetProperty("after").EnumerateArray().ToList();
            for (int m = 0; m < party.Count; m++)
                Same(fails, $"case {n} member {m}", party[m], after[m].GetString()!);
            if (!pool.SequenceEqual(k.GetProperty("poolAfter").EnumerateArray().Select(p => p.GetUInt32())))
                fails.Add($"case {n}: pool {string.Join(",", pool)} vs {k.GetProperty("poolAfter")}");
            shares += share ? 1 : 0;
            halts += halted ? 1 : 0;
        }
        Assert.True(n >= 800 && shares > 400 && halts > 20, $"{n} cases, {shares} shares, {halts} halts");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// The coin take's step 0x25711 and AddPlatinum 0x24E8C run under emulation: <see cref="ShopRules.Take"/> and <see
    /// cref="Money.AddPlatinum"/> leave the same record and pool and refuse (the row-24 message) alike. Takes whose
    /// amount would wrap the coin word or the weight are the s50 fix (<see cref="ShopRules.TakeMaximum"/>): counted,
    /// not compared.
    /// </summary>
    [OracleFact]
    public void Coin_take_and_add_platinum_match_0x25711_and_0x24E8C_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, takes = 0, refused = 0, wraps = 0;
        foreach (var k in doc.RootElement.GetProperty("takeCoins").EnumerateArray())
        {
            n++;
            var c = Char(k.GetProperty("record").GetString()!);
            var pool = k.GetProperty("pool").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            int kind = k.GetProperty("kind").GetInt32();
            var amount = (ushort)k.GetProperty("amount").GetInt32();
            bool take = k.GetProperty("routine").GetString() == "take";
            bool messaged;
            if (take)
            {
                if (amount > ShopRules.TakeMaximum(c, ushort.MaxValue, kind))
                {
                    wraps++;
                    continue;
                }
                takes++;
                messaged = !ShopRules.Take(c, pool, kind, amount, Carrying.StrengthAllowance(c));
            }
            else
                messaged = Money.AddPlatinum(c, pool, amount, Carrying.StrengthAllowance(c));
            if (messaged != (k.GetProperty("messages").GetInt32() != 0))
                fails.Add($"case {n} ({(take ? "take" : "platinum")}): message {messaged}");
            refused += messaged ? 1 : 0;
            Same(fails, $"case {n}", c, k.GetProperty("after").GetString()!);
            if (!pool.SequenceEqual(k.GetProperty("poolAfter").EnumerateArray().Select(p => p.GetUInt32())))
                fails.Add($"case {n}: pool {string.Join(",", pool)} vs {k.GetProperty("poolAfter")}");
        }
        Assert.True(takes > 250 && n - takes - wraps > 300 && refused > 100, $"{n} cases, {takes} takes, {wraps} wraps, {refused} refused");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// The appraiser 0x2663B run under emulation with its menus answered by a script: the loop of the Client's
    /// <c>MoneyScreens.Appraise</c> (no gems or jewellery at the start: the message, redraw flag 0, no recalculation;
    /// G / J appraise when there is one, E or 0 end, any other key nothing; START 0x1758 after every pass) with <see
    /// cref="ShopRules.Appraise"/> gives the same menu lines, record, items, pool, messages and RandSeed. Keeping with
    /// no items loses the gem or jewellery in the program; Core adds it as the first item (original bug, fixed: user,
    /// s53), so such a case is checked for the item and not compared further.
    /// </summary>
    [OracleFact]
    public void Appraise_matches_0x2663B_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var table = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        int n = 0, kept = 0, firstItem = 0;
        foreach (var k in doc.RootElement.GetProperty("appraise").EnumerateArray())
        {
            n++;
            string what = $"case {n}";
            var c = Char(k.GetProperty("record").GetString()!, k.GetProperty("items").EnumerateArray().Select(x => x.GetString()!));
            var pool = k.GetProperty("pool").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var log = k.GetProperty("log").EnumerateArray().ToList();
            int i = 0, messages = 0, redraw = 1;
            bool diverged = false;
            (string Line, int Key)? Next()
            {
                if (i >= log.Count)
                {
                    fails.Add($"{what}: Core asks past the log");
                    return null;
                }
                var l = log[i++];
                return (l.GetProperty("line").GetString()!, l.GetProperty("key").GetInt32());
            }
            for (bool first = true; ; first = false)
            {
                int gems = c.Word(Money.Gems), jewels = c.Word(Money.Jewellery);
                if (gems == 0 && jewels == 0)
                {
                    if (first)
                    {
                        messages++;
                        redraw = 0;
                    }
                    break;
                }
                string line = (gems > 0 ? "  Gems" : "") + (jewels > 0 ? "  Jewelry" : "") + " Exit";
                if (Next() is not { } m)
                    break;
                if (m.Line != line)
                    fails.Add($"{what}: line '{line}' vs '{m.Line}'");
                bool done = m.Key is 'E' or 0;
                if ((m.Key == 'G' && gems > 0) || (m.Key == 'J' && jewels > 0))
                {
                    int itemsBefore = c.Items.Count;
                    bool keepAsked = false;
                    ShopRules.Appraise(c, pool, m.Key == 'J', x => rng.Next((ushort)x), (_, canKeep) =>
                    {
                        if (Next() is not { } a)
                            return false;
                        if (a.Line != (canKeep ? "Sell Keep" : "Sell"))
                            fails.Add($"{what}: keep line {canKeep} vs '{a.Line}'");
                        keepAsked = a.Key == 'K' && canKeep;
                        return a.Key == 'K';
                    }, Carrying.StrengthAllowance(c));
                    if (keepAsked)
                    {
                        kept++;
                        if (itemsBefore == 0)
                        {
                            firstItem++;
                            diverged = true; // the program lost it; from here the two differ by that item
                            if (c.Items.Count != 1 || c.Items[0][0x2E] != 70 || c.Items[0][0x31] != (m.Key == 'J' ? 0xD6 : 0x65))
                                fails.Add($"{what}: the kept item is not the first item");
                            break;
                        }
                    }
                }
                StatRecalc.Recalculate(c, table);
                if (done)
                    break;
            }
            if (diverged)
                continue;
            // AddPlatinum's overloaded message is the only other row-24 message.
            int want = k.GetProperty("messages").GetInt32();
            if (i != log.Count)
                fails.Add($"{what}: Core used {i} of {log.Count} answers");
            Same(fails, what, c, k.GetProperty("after").GetString()!);
            var items = c.Items.Select(x => { var b = (byte[])x.Clone(); Array.Clear(b, 0x2A, 4); return Convert.ToHexString(b).ToLowerInvariant(); });
            if (!items.SequenceEqual(k.GetProperty("itemsAfter").EnumerateArray().Select(x => x.GetString()!)))
                fails.Add($"{what}: items differ");
            if (!pool.SequenceEqual(k.GetProperty("poolAfter").EnumerateArray().Select(p => p.GetUInt32())))
                fails.Add($"{what}: pool differs");
            if (redraw != k.GetProperty("redraw").GetInt32())
                fails.Add($"{what}: redraw {redraw}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed differs");
            if (messages > want)
                fails.Add($"{what}: messages {messages} vs {want}");
        }
        Assert.True(n >= 400 && kept > 50 && firstItem > 10, $"{n} cases, {kept} kept, {firstItem} into an empty pack");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }
}
