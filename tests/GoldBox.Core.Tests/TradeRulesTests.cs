using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>View's Trade without game data (docs/re/camp.md "Trade"; the program's own cases: TradeProgramTests).</summary>
public class TradeRulesTests
{
    private static byte[] Item(byte tag, ushort weight = 10)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2F] = tag;
        r[0x37] = (byte)weight;
        r[0x38] = (byte)(weight >> 8);
        return r;
    }

    private static void Recalc(EclCharacter c)
    {
        Carrying.RecalculateWeight(c);
        c[StatRecalc.ItemCount] = (byte)c.Items.Count;
    }

    [Fact]
    public void An_item_traded_to_its_owner_goes_to_the_end_of_the_list()
    {
        var c = new EclCharacter();
        var (a, b, d) = (Item(1), Item(2), Item(3));
        c.Items.AddRange([a, b, d]);
        Assert.True(TradeRules.GiveItem(c, c, a, _ => 0, Recalc));
        Assert.Equal(new byte[] { 2, 3, 1 }, c.Items.Select(i => i[0x2F]).ToArray());
        Assert.Equal(30, c.Word(Money.Weight));
    }

    [Fact]
    public void An_overloaded_pick_gets_nothing_and_a_full_one_neither()
    {
        var giver = new EclCharacter();
        var item = Item(1, 600);
        giver.Items.Add(item);
        var heavy = new EclCharacter();
        heavy.Items.Add(Item(9, 1000)); // limit 1500 with allowance 0: 1000 + 600 is too much
        Assert.False(TradeRules.GiveItem(giver, heavy, item, _ => 0, Recalc));
        Assert.Single(giver.Items);
        Assert.Single(heavy.Items);
        var full = new EclCharacter();
        for (int i = 0; i < 16; i++)
            full.Items.Add(Item((byte)i, 0));
        Assert.False(TradeRules.GiveItem(giver, full, item, _ => 0, Recalc)); // 0xC7 > 15
        var light = new EclCharacter();
        Assert.True(TradeRules.GiveItem(giver, light, item, _ => 0, Recalc));
        Assert.Empty(giver.Items);
        Assert.Equal(600, light.Word(Money.Weight));
        Assert.NotSame(item, light.Items[0]);
    }

    [Fact]
    public void Coins_move_and_so_does_their_weight_the_program_bug_fixed()
    {
        var giver = new EclCharacter();
        giver.SetWord(Money.Gold, 300);
        giver.SetWord(Money.Weight, 500);
        var pick = new EclCharacter();
        pick.SetWord(Money.Weight, 1400);
        Assert.True(TradeRules.GiveCoins(giver, pick, 3, 100, 0));  // 1400 + 100 = the limit 1500
        Assert.Equal(200, giver.Word(Money.Gold));
        Assert.Equal(100, pick.Word(Money.Gold));
        Assert.Equal(400, giver.Word(Money.Weight));
        Assert.Equal(1500, pick.Word(Money.Weight)); // the program adds it back to the giver (bug fixed, user s42)
        Assert.False(TradeRules.GiveCoins(giver, pick, 3, 1, 0));   // the pick is now at its limit
        Assert.Equal(200, giver.Word(Money.Gold));
        Assert.Equal(100, pick.Word(Money.Gold));
    }

    [Fact]
    public void Coins_to_the_giver_change_nothing_unless_refused()
    {
        var c = new EclCharacter();
        c.SetWord(Money.Gold, 300);
        c.SetWord(Money.Weight, 1450);
        Assert.False(TradeRules.GiveCoins(c, c, 3, 51, 0));
        Assert.True(TradeRules.GiveCoins(c, c, 3, 50, 0));
        Assert.Equal(300, c.Word(Money.Gold));
    }
}
