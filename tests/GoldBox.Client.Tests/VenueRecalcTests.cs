using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The shop's Buy (0x7035) and the loot's Take (0x6184) through the copy step 0x6F0D, and Appraise (0x2663B): where the
/// program runs the full stat recalculation START 0x1758 (docs/re/shop-format.md "What is recalculated").
/// </summary>
public class VenueRecalcTests
{
    private static BaseItemTable Weapons()
    {
        var t = new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]);
        for (int k = 0; k < BaseItemTable.Count; k++)
            t.Set(k, BaseItemTable.Slot, 14);
        return t;
    }

    /// <summary>A member whose derived bytes are stale (an edited save): weight 999, item count 9, dice 3d7.</summary>
    private static EclCharacter Stale(ushort platinum = 0)
    {
        var c = new EclCharacter { Name = "ANNA" };
        c[0xA3] = 1;
        c[0xA5] = 2;
        c[0x115] = 3;
        c[0x117] = 7;
        c[StatRecalc.ItemCount] = 9;
        c.SetWord(Money.Platinum, platinum);
        c.SetWord(Money.Weight, 999);
        return c;
    }

    private static byte[] Item(ushort value = 0, ushort weight = 10)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = 5;
        r[0x37] = (byte)weight;
        r[0x3A] = (byte)value;
        r[0x3B] = (byte)(value >> 8);
        return r;
    }

    private static TradeContext Context(EclCharacter c, List<byte[]> treasure, Queue<object?> answers)
    {
        var party = new EclParty();
        party.Members.Add(c);
        party.CurrentIndex = 0;
        return new TradeContext
        {
            Dialog = new Dialog(r => r is ListRequest or ChoiceRequest ? answers.Dequeue() : null, new TextWindow()),
            Text = new GameText(),
            Party = party,
            Pool = new uint[7],
            Treasure = treasure,
            Random = _ => 0,
            ItemTypes = Weapons(),
        };
    }

    [Fact]
    public void Loot_take_recalculates_in_full_through_the_copy_step()
    {
        var c = Stale();
        var t = Context(c, [Item()], new Queue<object?>([new ListAnswer(0, 0)]));
        VenueScreens.TakeItems(t);
        Assert.Single(c.Items);
        Assert.Equal(1, c[StatRecalc.ItemCount]);
        Assert.Equal(10, c.Word(Money.Weight));
        Assert.Equal((1, 2), (c[0x115], c[0x117]));
    }

    [Fact]
    public void Buy_recalculates_in_the_copy_step_and_not_after_the_payment()
    {
        var c = Stale(platinum: 20);
        var t = Context(c, [Item(value: 60)], new Queue<object?>([new ListAnswer(0, 0), new ListAnswer(1, 0)]));
        VenueScreens.Buy(t);
        Assert.Single(c.Items);
        Assert.Equal(8, c.Word(Money.Platinum));
        Assert.Equal(1, c[StatRecalc.ItemCount]);
        Assert.Equal(30, c.Word(Money.Weight)); // the 20 pp before the payment + the item (DOSBox-X capture s4buy)
        Assert.Equal((1, 2), (c[0x115], c[0x117]));
    }

    [Fact]
    public void Appraise_recalculates_after_each_pass_and_says_nothing_when_the_last_gem_is_gone()
    {
        var c = Stale();
        c.SetWord(Money.Gems, 1);
        var seen = new List<PlayerRequest>();
        var answers = new Queue<object?>([0, 0]); // Gems, then Sell
        var party = new EclParty();
        party.Members.Add(c);
        party.CurrentIndex = 0;
        var w = new TextWindow();
        var t = new TradeContext
        {
            Dialog = new Dialog(r =>
            {
                seen.Add(r);
                return r is ChoiceRequest ? answers.Dequeue() : null;
            }, w),
            Text = new GameText(),
            Party = party,
            Pool = new uint[7],
            Treasure = [],
            Random = _ => 0,
            ItemTypes = Weapons(),
        };
        MoneyScreens.Appraise(t);
        Assert.Empty(answers);
        Assert.IsType<ChoiceRequest>(seen[^1]); // no "no gems" key wait once the last one went (0x26698)
        Assert.Equal(0, c.Word(Money.Gems));
        Assert.Equal(2, c.Word(Money.Platinum)); // a 10-gold gem sold: 2 pp
        Assert.Equal(2, c.Word(Money.Weight));  // recalculated after the pass
        Assert.Equal(0, c[StatRecalc.ItemCount]);
        Assert.Equal((1, 2), (c[0x115], c[0x117]));
    }
}
