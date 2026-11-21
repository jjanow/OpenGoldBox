using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>Money, shop and temple rules (docs/re/shop-format.md).</summary>
public class ShopRulesTests
{
    private const int Allowance = 0; // carrying limit 1500

    private static EclCharacter Char(ushort cp = 0, ushort sp = 0, ushort ep = 0, ushort gp = 0, ushort pp = 0, ushort weight = 0)
    {
        var c = new EclCharacter();
        c.SetWord(Money.Coins, cp);
        c.SetWord(Money.Coins + 2, sp);
        c.SetWord(Money.Coins + 4, ep);
        c.SetWord(Money.Gold, gp);
        c.SetWord(Money.Platinum, pp);
        c.SetWord(Money.Weight, weight);
        return c;
    }

    private static byte[] Item(ushort value, ushort weight = 10, byte count = 0)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x3A] = (byte)value; r[0x3B] = (byte)(value >> 8);
        r[0x37] = (byte)weight; r[0x38] = (byte)(weight >> 8);
        r[0x39] = count;
        r[0x35] = 6;
        return r;
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 0, 1)]
    [InlineData(0, 0, 1, 0, 0, 1)]   // 1 ep = 100 cp: (100 + 100) / 200 = 1, rounds half up
    [InlineData(99, 0, 0, 0, 0, 0)]
    [InlineData(0, 10, 0, 0, 0, 1)]
    [InlineData(0, 0, 0, 3, 2, 13)]
    public void Character_gold_rounds_half_up(int cp, int sp, int ep, int gp, int pp, int gold) =>
        Assert.Equal(gold, Money.CharacterGold(Char((ushort)cp, (ushort)sp, (ushort)ep, (ushort)gp, (ushort)pp)));

    [Fact]
    public void Paying_rewrites_the_coins_as_platinum_and_gold()
    {
        var c = Char(cp: 150, sp: 7, gp: 12, pp: 3);  // (150 + 70 + 2400 + 3000 + 100) / 200 = 28
        var pool = new uint[7];
        Assert.Equal(Money.Payer.Character, Money.Pay(c, pool, 10, false));
        Assert.Equal(0, c.Word(Money.Coins));
        Assert.Equal(0, c.Word(Money.Coins + 2));
        Assert.Equal(3, c.Word(Money.Platinum)); // 18 = 3 pp + 3 gp
        Assert.Equal(3, c.Word(Money.Gold));
    }

    [Fact]
    public void Pool_pays_when_the_character_cannot()
    {
        var c = Char(gp: 1);
        var pool = new uint[] { 0, 0, 0, 100, 0, 7, 0 };
        Assert.Equal(Money.Payer.Pool, Money.Pay(c, pool, 50, false));
        Assert.Equal(new uint[] { 0, 0, 0, 0, 10, 7, 0 }, pool);
        Assert.Equal(Money.Payer.None, Money.Pay(c, pool, 51, false));
    }

    /// <summary>START 0x1758 as far as these tests need it: weight and item count.</summary>
    private static void Recalc(EclCharacter c)
    {
        Carrying.RecalculateWeight(c);
        c[ShopRules.ItemCount] = (byte)c.Items.Count;
    }

    [Fact]
    public void Buy_copies_then_pays_and_the_stock_stays()
    {
        var c = Char(pp: 20, weight: 999); // a stale weight word: the copy step recalculates it
        var pool = new uint[7];
        var stock = Item(60);
        Assert.Equal(ShopRules.BuyResult.Bought, ShopRules.Buy(c, pool, stock, Allowance, Recalc));
        Assert.Single(c.Items);
        Assert.NotSame(stock, c.Items[0]);
        Assert.Equal(8, c.Word(Money.Platinum));
        Assert.Equal(0, c.Word(Money.Gold));
        Assert.Equal(1, c[ShopRules.ItemCount]);
        Assert.Equal(30, c.Word(Money.Weight)); // 20 pp + the item's 10, recalculated before the payment (no recalculation after it)
        Assert.Equal(ShopRules.BuyResult.NotEnoughMoney, ShopRules.Buy(c, pool, Item(41), Allowance, Recalc));
        var heavy = Char(pp: 20);
        heavy.SetWord(Money.Coins, 1475); // 1495 coins' weight once recalculated
        Assert.Equal(ShopRules.BuyResult.CannotCarry, ShopRules.Buy(heavy, pool, Item(10, weight: 6), Allowance, Recalc));
        Assert.Equal(20, heavy.Word(Money.Platinum));
        Assert.Equal(1495, heavy.Word(Money.Weight));
    }

    [Theory]
    [InlineData(100, 0, 50)]
    [InlineData(101, 1, 50)]
    [InlineData(40, 10, 10)]   // 10 × 20 / 20
    [InlineData(1, 5, 0)]
    public void Sell_price(int value, int count, int price) =>
        Assert.Equal(price, ShopRules.SellPrice(Item((ushort)value, count: (byte)count)));

    [Fact]
    public void Sell_pays_platinum_and_gold()
    {
        var c = Char();
        var it = Item(46); // 23 gp = 4 pp + 3 gp
        c.Items.Add(it);
        ShopRules.Sell(c, new uint[7], it, Allowance);
        Assert.Empty(c.Items);
        Assert.Equal(4, c.Word(Money.Platinum));
        Assert.Equal(3, c.Word(Money.Gold));
        var ready = Item(1);
        ready[0x34] = 1;
        Assert.Null(ShopRules.SellCheck(ready, false));
        Assert.Equal(false, ShopRules.SellCheck(Item(1), false));
    }

    [Fact]
    public void Identify_costs_200_even_when_known()
    {
        var c = Char(pp: 80); // 400 gold
        var it = Item(10);
        Assert.Equal(ShopRules.IdentifyResult.Identified, ShopRules.Identify(c, new uint[7], it));
        Assert.Equal(0, it[0x35]);
        Assert.Equal(40, c.Word(Money.Platinum));
        Assert.Equal(ShopRules.IdentifyResult.AlreadyIdentified, ShopRules.Identify(c, new uint[7], it));
        Assert.Equal(0, c.Word(Money.Platinum));
        Assert.Equal(ShopRules.IdentifyResult.NotEnoughMoney, ShopRules.Identify(c, new uint[7], it));
    }

    [Theory]
    [InlineData(25, 10)]
    [InlineData(26, 50)]
    [InlineData(70, 100)]
    [InlineData(90, 500)]
    [InlineData(99, 1000)]
    [InlineData(100, 5000)]
    public void Gem_values(int d100, int value) => Assert.Equal(value, ShopRules.GemValue(d100));

    [Fact]
    public void Appraise_keep_or_sell()
    {
        var c = Char();
        c.SetWord(Money.Gems, 2);
        c.SetWord(Money.Jewellery, 1);
        var pool = new uint[7];
        Assert.Equal((ushort)500, ShopRules.Appraise(c, pool, false, _ => 79, (_, _) => false, Allowance));
        Assert.Equal(100, c.Word(Money.Platinum));
        Assert.Equal(1, c.Word(Money.Gems));
        var rolls = new Queue<int>([94, 123]); // 1d100 = 95 → 2000 + Random(10000)
        Assert.Equal((ushort)2123, ShopRules.Appraise(c, pool, true, _ => rolls.Dequeue(), (_, can) => can, Allowance));
        Assert.Single(c.Items);
        Assert.Equal(70, c.Items[0][0x2E]);
        Assert.Equal(0xD6, c.Items[0][0x31]);
        Assert.Equal(2123, c.Items[0][0x3A] | c.Items[0][0x3B] << 8);
        Assert.Null(ShopRules.Appraise(c, pool, true, _ => 0, (_, _) => true, Allowance));
    }

    [Fact]
    public void Pool_and_share_split_with_remainders_first_come()
    {
        var a = Char(gp: 5, weight: 5);
        var b = Char(gp: 2, weight: 2);
        var npc = Char(gp: 9, weight: 9);
        npc[0x84] = 0x82;
        var party = new List<EclCharacter> { a, b, npc };
        var pool = new uint[7];
        ShopRules.PoolMoney(party, pool);
        Assert.Equal(7u, pool[Money.PoolGold]);
        Assert.Equal(9, npc.Word(Money.Gold));
        Assert.Equal(0, a.Word(Money.Weight));
        bool left = ShopRules.Share(party, pool, _ => Allowance);
        Assert.False(left);
        Assert.Equal(4, a.Word(Money.Gold)); // 7 / 2 = 3 each, the odd coin to the first
        Assert.Equal(3, b.Word(Money.Gold));
        Assert.Equal(9, npc.Word(Money.Gold));
        Assert.All(pool, v => Assert.Equal(0u, v));
    }

    [Fact]
    public void Share_with_no_eligible_member_halts_like_the_program()
    {
        // n = 0 (only NPCs, 0x84 ≠ 0 / 0xB3): the program's longint division by 0 is run-time error 200 (START 0xBA5D).
        var npc = Char(weight: 9);
        npc[0x84] = 0xB1;
        var party = new List<EclCharacter> { npc };
        var pool = new uint[] { 0, 0, 0, 7, 0, 0, 0 };
        var e = Assert.Throws<ProgramHaltException>(() => ShopRules.Share(party, pool, _ => Allowance));
        Assert.Equal(200, e.Code);
        Assert.Equal(7u, pool[Money.PoolGold]);
        Assert.Equal(9, npc.Word(Money.Weight));
        // An empty pool never divides.
        Assert.False(ShopRules.Share(party, new uint[7], _ => Allowance));
    }

    [Fact]
    public void Share_gives_the_odd_coin_only_when_the_whole_remainder_would_fit()
    {
        // 11 gold over 4 members: share 2, remainder 3. The first member has room for 3 more coins: its share
        // fits, and one more coin would too, but the program tests the whole remainder (3), so it gets no odd coin.
        var a = Char(weight: 1497);
        var b = Char();
        var c = Char();
        var d = Char();
        var party = new List<EclCharacter> { a, b, c, d };
        var pool = new uint[] { 0, 0, 0, 11, 0, 0, 0 };
        ShopRules.Share(party, pool, _ => Allowance);
        Assert.Equal(2, a.Word(Money.Gold));
        Assert.Equal(1499, a.Word(Money.Weight));
        Assert.Equal(3, b.Word(Money.Gold));
        Assert.Equal(3, c.Word(Money.Gold));
        Assert.Equal(3, d.Word(Money.Gold));
        Assert.All(pool, v => Assert.Equal(0u, v));
    }

    [Fact]
    public void Remove_curse_clears_effect_36_or_the_first_cursed_item()
    {
        var c = Char();
        var i1 = Item(1); i1[0x36] = 1;
        var i2 = Item(1); i2[0x36] = 1;
        c.Items.AddRange([i1, i2]);
        c.Effects.Add([36, 0, 0, 255, 0, 0, 0, 0, 0]);
        TempleRules.RemoveCurse(c);
        Assert.Empty(c.Effects);
        Assert.Equal(1, i1[0x36]);
        TempleRules.RemoveCurse(c);
        Assert.Equal(0, i1[0x36]);
        Assert.Equal(1, i2[0x36]);
    }

    [Fact]
    public void Take_moves_at_most_the_pool()
    {
        var c = Char();
        var pool = new uint[] { 0, 0, 0, 5, 0, 0, 0 };
        Assert.True(ShopRules.Take(c, pool, Money.PoolGold, 9, Allowance));
        Assert.Equal(5, c.Word(Money.Gold));
        Assert.Equal(5, c.Word(Money.Weight));
        Assert.Equal(0u, pool[Money.PoolGold]);
        Assert.False(ShopRules.Take(Char(weight: 1500), pool, Money.PoolGold, 1, Allowance));
    }

    // ---- Temple ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Temple_heal_is_capped_and_blocked_by_effect_50()
    {
        var c = Char();
        c[0x32] = 20;
        c[0x11B] = 5;
        TempleRules.Apply(TempleRules.Cure.CureCritical, c, _ => 7); // 3 × 8 + 3 = 27
        Assert.Equal(20, c[0x11B]);
        c[0x11B] = 5;
        c.Effects.Add([50, 0, 0, 255, 0, 0, 0, 0, 0]);
        TempleRules.Apply(TempleRules.Cure.CureLight, c, _ => 7);
        Assert.Equal(5, c[0x11B]);
        Assert.Equal(1000, TempleRules.Price(TempleRules.Cure.Disease));
        Assert.True(TempleRules.Applies(TempleRules.Cure.Disease, c));
        TempleRules.Apply(TempleRules.Cure.Disease, c, _ => 0);
        Assert.Empty(c.Effects);
    }

    [Fact]
    public void Raise_dead_follows_the_program()
    {
        var c = Char();
        c[0x10C] = 6;
        c[0x14] = 17;       // Con 17 → 16
        c[0x32] = 40;       // max HP
        c[0xB1] = 30;       // rolled HP: d = 10
        c[0x96 + 2] = 4;    // fighter level 4: s = 4 × (16 − 14) = 8 → d = 1
        Assert.True(TempleRules.Applies(TempleRules.Cure.RaiseDead, c));
        TempleRules.Apply(TempleRules.Cure.RaiseDead, c, _ => 0);
        Assert.Equal(0, c[0x10C]);
        Assert.Equal(1, c[0x10D]);
        Assert.Equal(1, c[0x11B]);
        Assert.Equal(16, c[0x14]);
        Assert.Equal(39, c[0x32]);
    }
}
