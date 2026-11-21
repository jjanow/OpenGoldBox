using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>Strength code, carrying allowance, weight (START.EXE 0x1D8A, 0x1F4B, 0x1758) and item names (0x0FE2).</summary>
public class CarryingTests
{
    private static EclCharacter Str(byte str, byte exceptional = 0)
    {
        var c = new EclCharacter();
        c[0x10] = str;
        c[0x16] = exceptional;
        return c;
    }

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(17, 0, 17)]
    [InlineData(18, 0, 18)]
    [InlineData(18, 1, 19)]
    [InlineData(18, 50, 19)]
    [InlineData(18, 51, 20)]
    [InlineData(18, 76, 21)]
    [InlineData(18, 91, 22)]
    [InlineData(18, 100, 23)]
    [InlineData(19, 0, 24)]
    [InlineData(25, 0, 30)]
    public void Strength_code_follows_the_bands(byte str, byte exc, int code) => Assert.Equal(code, Carrying.StrengthCode(Str(str, exc)));

    [Fact]
    public void Strength_code_is_undefined_past_the_table() // [?] the program reads a stray stack byte
    {
        Assert.Null(Carrying.StrengthCode(Str(26)));
        Assert.Null(Carrying.StrengthCode(Str(18, 101)));
        Assert.Equal(0, Carrying.StrengthAllowance(Str(26)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -350)]
    [InlineData(5, -250)]
    [InlineData(7, -150)]
    [InlineData(8, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 100)]
    [InlineData(15, 200)]
    [InlineData(16, 350)]
    [InlineData(17, 500)]
    [InlineData(18, 750)]
    [InlineData(21, 1500)]
    [InlineData(22, 2000)]
    [InlineData(26, 6000)]
    [InlineData(27, 7500)]
    [InlineData(28, 9000)]
    [InlineData(30, 15000)]
    public void Allowance_by_code(int code, int allowance) => Assert.Equal(allowance, Carrying.AllowanceForCode(code));

    [Fact]
    public void Weight_counts_items_by_stack_and_every_coin()
    {
        var c = new EclCharacter();
        var a = new byte[EclCharacter.ItemRecordSize];
        a[0x37] = 50;                // single
        var b = new byte[EclCharacter.ItemRecordSize];
        b[0x37] = 2;
        b[0x39] = 10;                // 10 arrows of 2
        c.Items.Add(a);
        c.Items.Add(b);
        c.SetWord(Money.Platinum, 7);
        c.SetWord(Money.Gems, 3);
        Carrying.RecalculateWeight(c);
        Assert.Equal(50 + 20 + 7 + 3, c.Word(Money.Weight));
    }

    [Fact]
    public void A_readied_bag_of_holding_carries_5000_but_not_the_readied_items()
    {
        var c = new EclCharacter();
        var bag = new byte[EclCharacter.ItemRecordSize];
        bag[0x2F] = StatRecalc.BagMarker; // name word "Holding"
        bag[0x37] = 150;
        var sword = new byte[EclCharacter.ItemRecordSize];
        sword[0x37] = 60;
        sword[0x34] = 1;
        c.Items.Add(bag);
        c.Items.Add(sword);
        c.SetWord(Money.Platinum, 6000);
        Assert.Equal(150 + 60 + 6000, Carrying.Weight(c)); // bag not readied: no effect
        bag[0x34] = 1;
        Assert.Equal(150 + 60 + 6000 - 5000, Carrying.Weight(c));
        c.SetWord(Money.Platinum, 100);
        Assert.Equal(210, Carrying.Weight(c)); // floored at 0, then at least the readied weight (210)
        // Same rule as the full recalculation.
        var r = new EclCharacter();
        r.Items.Add((byte[])bag.Clone());
        r.SetWord(Money.Platinum, 5500);
        Assert.Equal(650, Carrying.Weight(r));
    }

    [Fact]
    public void Copy_step_refuses_a_seventeenth_item()
    {
        var c = new EclCharacter();
        var item = new byte[EclCharacter.ItemRecordSize];
        void Recalc(EclCharacter x) => x[ShopRules.ItemCount] = (byte)x.Items.Count; // 0xC7 from START 0x1758
        for (int i = 0; i < 16; i++)
            Assert.True(ShopRules.GiveCopy(c, item, 0, Recalc));
        Assert.False(ShopRules.GiveCopy(c, item, 0, Recalc));
        Assert.Equal(16, c.Items.Count);
    }

    private static string? Word(int w) => w switch { 1 => "Long", 2 => "Sword", 3 => "Arrow", 9 => "+1", _ => null };

    [Fact]
    public void Names_follow_the_hidden_mask_and_the_plural_rule()
    {
        var it = new byte[EclCharacter.ItemRecordSize];
        it[0x2E] = 5;
        it[0x2F] = 9;    // k = 1 (written last)
        it[0x30] = 1;    // k = 2
        it[0x31] = 2;    // k = 3 (written first)
        Assert.Equal("Sword Long +1 ", ItemNames.Compose(it, Word));
        it[0x35] = 4;    // bit 2 hides k = 1 (the +1)
        Assert.Equal("Sword Long ", ItemNames.Compose(it, Word));
        it[0x32] = 1;    // plus: magical
        Assert.Equal("* Sword Long ", ItemNames.Compose(it, Word, detect: true));

        var arrows = new byte[EclCharacter.ItemRecordSize];
        arrows[0x2E] = 0x49;
        arrows[0x31] = 3;
        arrows[0x39] = 12;
        Assert.Equal("12 Arrows ", ItemNames.Compose(arrows, Word));
    }
}
