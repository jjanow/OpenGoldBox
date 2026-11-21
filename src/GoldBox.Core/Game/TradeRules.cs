using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// View's Trade (GAME.OVR 0x2276E for an item from the Items page, 0x23190 for money; docs/re/camp.md "Trade"), the
/// record side: what moves once the player has picked the member to trade with (START's member pick 0x37E8). The
/// screens, the pick and the messages are the Client's. Checked against the program run under emulation
/// (<c>TradeProgramTests</c>, fixture from <c>tools/scripts/por_trade_oracle.py</c>).
/// </summary>
public static class TradeRules
{
    /// <summary>
    /// The item Trade after the pick (0x227B7–0x22810): the carry test 0x23795 on <paramref name="pick"/> (which first
    /// runs the stat recalculation START 0x1758 on it, <paramref name="recalculate"/>; then <see cref="ShopRules.CanCarry"/>
    /// with its strength allowance) refuses with no change (false: the caller shows the overloaded message). Else
    /// START 0x2197 appends a copy (next cleared) to the pick's items, START 0x20A9 takes the item out of
    /// <paramref name="giver"/>'s list (the giver's item count byte is left to its next recalculation) and the pick is
    /// recalculated again. The pick may be the giver: the item then moves to the end of its own list (the carry test
    /// counts it twice).
    /// </summary>
    public static bool GiveItem(EclCharacter giver, EclCharacter pick, byte[] item, Func<EclCharacter, int> strengthAllowance,
        Action<EclCharacter> recalculate)
    {
        recalculate(pick);
        if (!ShopRules.CanCarry(pick, item, strengthAllowance(pick)))
            return false;
        var copy = (byte[])item.Clone();
        Array.Clear(copy, 0x2A, 4); // next = nil
        pick.Items.Add(copy);
        int at = giver.Items.FindIndex(i => ReferenceEquals(i, item));
        if (at >= 0)
            giver.Items.RemoveAt(at);
        recalculate(pick);
        return true;
    }

    /// <summary>
    /// The money transfer 0x250D5 (kind, amount, the pick, DS:5D92): refused (false: the overloaded message, START
    /// 0x1693 on row 24, and nothing moves) when the pick's stored weight 0x102 + amount (a 16-bit sum) is above its
    /// limit (0x24C89: START 0x1F4B + 1500, <see cref="Money.Overloaded"/>); no recalculation runs first. Else the
    /// giver's coin word of the kind goes down and the pick's goes up by the amount (16-bit words, no cap: the input's
    /// maximum is the giver's count). Then the amount leaves the giver's weight 0x102 and joins the pick's. **Original
    /// bug, fixed (user, s42; not an option, as the boar double free):** 0x250D5 takes the amount off the giver's 0x102
    /// (0x24CA9) and then adds it back to the giver's (0x24CC3 on DS:5D92 again), so in the program neither weight word
    /// moves and the pick could be handed coins past its limit until a recalculation. The pick may be the giver: the
    /// test can refuse, otherwise nothing changes.
    /// </summary>
    public static bool GiveCoins(EclCharacter giver, EclCharacter pick, int kind, ushort amount, int pickStrengthAllowance)
    {
        if (Money.Overloaded(pick, amount, pickStrengthAllowance, out _))
            return false;
        int at = Money.Coins + 2 * kind;
        giver.SetWord(at, unchecked((ushort)(giver.Word(at) - amount)));
        pick.SetWord(at, unchecked((ushort)(pick.Word(at) + amount)));
        if (!ReferenceEquals(giver, pick))
        {
            giver.SetWord(Money.Weight, unchecked((ushort)(giver.Word(Money.Weight) - amount)));
            pick.SetWord(Money.Weight, unchecked((ushort)(pick.Word(Money.Weight) + amount)));
        }
        return true;
    }

    /// <summary>
    /// After a transfer 0x23190 asks again while the giver still holds money (0x23415–0x2344A): back to its coin list
    /// (the money lines redrawn first, 0x21547); with no money left Trade ends.
    /// </summary>
    public static bool HasMoney(EclCharacter c) =>
        Enumerable.Range(0, FightItemScreens.CoinKinds).Any(k => c.Word(Money.Coins + 2 * k) != 0);
}
