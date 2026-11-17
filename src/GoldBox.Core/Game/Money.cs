using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Coins, the treasure pool and carrying weight as the shop, temple and loot code handle them (GAME.OVR unit
/// 0x24C89–0x24F1A and 0x238F8; docs/re/shop-format.md "Money"). Character money is seven words at creature +0x88
/// (copper, silver, electrum, gold, platinum, gems, jewellery); the pool is seven dwords at DS:67F4
/// (<see cref="EncounterState.Money"/>).
/// </summary>
public static class Money
{
    /// <summary>Creature record offsets.</summary>
    public const int Coins = 0x88, Gold = 0x8E, Platinum = 0x90, Gems = 0x92, Jewellery = 0x94, Weight = 0x102;
    /// <summary>Pool indexes (DS:67F4 + 4·k).</summary>
    public const int PoolGold = 3, PoolPlatinum = 4, PoolGems = 5, PoolJewellery = 6;

    /// <summary>Value of one coin of kinds 0..4 in copper (DS:0D24, five dwords); gold (DS:0D30) is the divisor.</summary>
    public static readonly int[] CoinValue = [1, 10, 100, 200, 1000];

    /// <summary>
    /// A character's coins in gold pieces (0x238F8): (Σ coins × value + 100) div 200, i.e. rounded half up; gems
    /// and jewellery do not count.
    /// </summary>
    public static int CharacterGold(EclCharacter c)
    {
        int sum = 0;
        for (int k = 0; k < 5; k++)
            sum = unchecked(sum + c.Word(Coins + 2 * k) * CoinValue[k]);
        return unchecked(sum + 100) / 200;
    }

    /// <summary>The pool's coins in gold pieces (0x24D31): the same rule on the dwords (signed 32-bit arithmetic).</summary>
    public static int PoolGoldTotal(uint[] pool)
    {
        int sum = 0;
        for (int k = 0; k < 5; k++)
            sum = unchecked(sum + (int)pool[k] * CoinValue[k]);
        return unchecked(sum + 100) / 200;
    }

    /// <summary>
    /// Replace a character's coins by <paramref name="gold"/> gold pieces (0x24DB9): all five coin words 0, then
    /// platinum = gold div 5, gold = gold mod 5. The weight word +0x102 is not touched.
    /// </summary>
    public static void SetCharacterGold(EclCharacter c, ushort gold)
    {
        for (int k = 0; k < 5; k++)
            c.SetWord(Coins + 2 * k, 0);
        c.SetWord(Platinum, (ushort)(gold / 5));
        c.SetWord(Gold, (ushort)(gold % 5));
    }

    /// <summary>Replace the pool's coins by <paramref name="gold"/> gold pieces (0x24E0E), the same way.</summary>
    public static void SetPoolGold(uint[] pool, ushort gold)
    {
        for (int k = 0; k < 5; k++)
            pool[k] = 0;
        pool[PoolPlatinum] = (uint)(gold / 5);
        pool[PoolGold] = (uint)(gold % 5);
    }

    /// <summary>Most a character may carry (0x24C89): the strength allowance (START 0x1F4B) + 1500, as a word.</summary>
    public static ushort CarryLimit(int strengthAllowance) => unchecked((ushort)(strengthAllowance + 1500));

    /// <summary>
    /// Whether adding <paramref name="add"/> coins of weight overloads the character (0x24CDD): weight + add (16-bit)
    /// above the limit. <paramref name="room"/> = limit − weight when overloaded, else 0.
    /// </summary>
    public static bool Overloaded(EclCharacter c, ushort add, int strengthAllowance, out ushort room)
    {
        ushort limit = CarryLimit(strengthAllowance);
        ushort w = c.Word(Weight);
        if (unchecked((ushort)(w + add)) > limit)
        {
            room = unchecked((ushort)(limit - w));
            return true;
        }
        room = 0;
        return false;
    }

    /// <summary>
    /// Add platinum to the current character (0x24E8C), used for gems and jewellery sold at the appraiser: what does
    /// not fit (overloaded) goes to the pool's platinum. The character's weight grows by what it keeps. Returns true
    /// when some went to the pool (the "Overloaded" message).
    /// </summary>
    public static bool AddPlatinum(EclCharacter c, uint[] pool, ushort amount, int strengthAllowance)
    {
        if (Overloaded(c, amount, strengthAllowance, out var room))
        {
            c.SetWord(Platinum, unchecked((ushort)(c.Word(Platinum) + room)));
            c.SetWord(Weight, unchecked((ushort)(c.Word(Weight) + room)));
            pool[PoolPlatinum] = unchecked(pool[PoolPlatinum] + amount - room);
            return true;
        }
        c.SetWord(Platinum, unchecked((ushort)(c.Word(Platinum) + amount)));
        c.SetWord(Weight, unchecked((ushort)(c.Word(Weight) + amount)));
        return false;
    }

    /// <summary>Who paid a price.</summary>
    public enum Payer { None, Character, Pool }

    /// <summary>
    /// Pay <paramref name="price"/> gold the way Buy (0x7035), the temple (0x4636) and Identify (0x22FF0) do: the
    /// character when its gold covers it, else the pool, else nobody. The payer's coins are rewritten as platinum +
    /// gold of the remainder; the pool's remainder is passed as one word. Buy and the temple compare the price with
    /// the low word of the character's total (<paramref name="wideCharacterTotal"/> false); Identify compares the
    /// 32-bit total. The pool is always compared as a signed 32-bit total.
    /// </summary>
    public static Payer Pay(EclCharacter c, uint[] pool, ushort price, bool wideCharacterTotal)
    {
        int total = CharacterGold(c);
        bool covers = wideCharacterTotal ? total >= price : (ushort)total >= price;
        if (covers)
        {
            SetCharacterGold(c, unchecked((ushort)(total - price)));
            return Payer.Character;
        }
        int poolTotal = PoolGoldTotal(pool);
        if (poolTotal >= price)
        {
            SetPoolGold(pool, unchecked((ushort)(poolTotal - price)));
            return Payer.Pool;
        }
        return Payer.None;
    }
}
