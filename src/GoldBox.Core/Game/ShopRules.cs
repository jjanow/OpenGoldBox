using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The shop's rules (GAME.OVR 0x7212 and the routines its menu calls; docs/re/shop-format.md), headless: the menus
/// and messages stay with the caller. The shop's stock is the treasure list (<see cref="EncounterState.TreasureItems"/>)
/// and the pool is <see cref="EncounterState.Money"/>. <c>strengthAllowance</c> is START 0x1F4B for the character
/// (its strength's carrying allowance, character-format.md "Movement recalculation"), which the caller supplies.
/// </summary>
public static class ShopRules
{
    /// <summary>Most items a character carries: the copy refuses a 17th (item count +0xC7 &gt; 15).</summary>
    public const int ItemCount = 0xC7, MaxItems = 16;
    /// <summary>Identify costs 200 gold (0x23025).</summary>
    public const ushort IdentifyPrice = 200;

    private const int ItemWeight = 0x37, ItemCountByte = 0x39, ItemValue = 0x3A, ItemType = 0x2E, ItemHidden = 0x35;

    /// <summary>
    /// Whether a character can take an item (0x23795): no if it already has more than 15 items, or if weight +
    /// the item's weight × count (count 0 = 1) is above the carrying limit. The program first recalculates the
    /// character (START 0x1758), which refreshes +0x102.
    /// </summary>
    public static bool CanCarry(EclCharacter c, byte[] item, int strengthAllowance)
    {
        if (c[ItemCount] > 15)
            return false;
        ushort w = Word(item, ItemWeight);
        if (item[ItemCountByte] > 0)
            w = unchecked((ushort)(w * item[ItemCountByte]));
        return unchecked((ushort)(c.Word(Money.Weight) + w)) <= Money.CarryLimit(strengthAllowance); // 16-bit sum
    }

    public enum BuyResult { Bought, CannotCarry, NotEnoughMoney }

    /// <summary>
    /// Buy (0x7035): price = the item's value word. If the character's gold covers it, or else the pool's, a copy
    /// goes to the end of the character's items (0x6F0D; the stock keeps its item) and only then is the price paid
    /// (<see cref="Money.Pay"/>). A copy refused by <see cref="CanCarry"/> costs nothing.
    /// The caller recalculates the character afterwards (START 0x1758 runs inside the copy, before payment).
    /// </summary>
    public static BuyResult Buy(EclCharacter c, uint[] pool, byte[] stockItem, int strengthAllowance)
    {
        ushort price = Word(stockItem, ItemValue);
        bool charPays = (ushort)Money.CharacterGold(c) >= price;
        if (!charPays && Money.PoolGoldTotal(pool) < price)
            return BuyResult.NotEnoughMoney;
        if (!CanCarry(c, stockItem, strengthAllowance))
            return BuyResult.CannotCarry;
        var copy = (byte[])stockItem.Clone();
        Array.Clear(copy, 0x2A, 4); // next = nil
        c.Items.Add(copy);
        c[ItemCount] = (byte)c.Items.Count;
        Money.Pay(c, pool, price, wideCharacterTotal: false);
        return BuyResult.Bought;
    }

    /// <summary>
    /// The price offered for an item (0x22D28): value / 2; for a stack (count &gt; 1) count × that / 20 (16-bit
    /// product, high word dropped). The program's separate branch for types 73 and 28 (count × half value) cannot be
    /// reached: its test asks for a type equal to both.
    /// </summary>
    public static ushort SellPrice(byte[] item)
    {
        ushort p = (ushort)(Word(item, ItemValue) / 2);
        if (item[ItemCountByte] > 1)
            p = (ushort)(unchecked((ushort)(item[ItemCountByte] * p)) / 20);
        return p;
    }

    /// <summary>
    /// Whether Sell may go ahead without a question (0x21DAA): a readied item is refused ("must be unreadied"); a
    /// scroll with a spell flagged for scribing (a props byte &gt; 0x7F) asks first. Returns null when refused, else
    /// whether the extra question is asked.
    /// </summary>
    public static bool? SellCheck(byte[] item, bool isScroll)
    {
        if (item[0x34] != 0)
            return null;
        return isScroll && (item[0x3C] > 0x7F || item[0x3D] > 0x7F || item[0x3E] > 0x7F);
    }

    /// <summary>
    /// Sell after a yes (0x22E34–0x22EE6): the item leaves the inventory (START 0x20A9), the price is paid as
    /// platinum (price div 5) and gold (price mod 5). When that overloads the character, platinum up to the room
    /// left goes to the character and the rest to the pool's platinum; the gold always goes to the character. The
    /// weight word is not updated here. Returns true when some went to the pool.
    /// </summary>
    public static bool Sell(EclCharacter c, uint[] pool, byte[] item, int strengthAllowance)
    {
        ushort price = SellPrice(item);
        c.Items.Remove(item);
        c[ItemCount] = (byte)c.Items.Count;
        ushort pp = (ushort)(price / 5), gp = (ushort)(price % 5);
        bool toPool = false;
        if (Money.Overloaded(c, (ushort)(pp + gp), strengthAllowance, out var room))
        {
            if (room > pp)
                c.SetWord(Money.Platinum, unchecked((ushort)(c.Word(Money.Platinum) + pp)));
            else
            {
                c.SetWord(Money.Platinum, unchecked((ushort)(c.Word(Money.Platinum) + room)));
                pool[Money.PoolPlatinum] = unchecked(pool[Money.PoolPlatinum] + (uint)(pp - room));
                toPool = true;
            }
        }
        else
            c.SetWord(Money.Platinum, unchecked((ushort)(c.Word(Money.Platinum) + pp)));
        c.SetWord(Money.Gold, unchecked((ushort)(c.Word(Money.Gold) + gp)));
        return toPool;
    }

    public enum IdentifyResult { Identified, AlreadyIdentified, NotEnoughMoney }

    /// <summary>
    /// Identify (0x22F8C, after the yes): 200 gold from the character (32-bit total) or else the pool; then, if the
    /// item was already identified, only a message (the money is spent), else <c>hidden</c> := 0.
    /// </summary>
    public static IdentifyResult Identify(EclCharacter c, uint[] pool, byte[] item)
    {
        if (Money.Pay(c, pool, IdentifyPrice, wideCharacterTotal: true) == Money.Payer.None)
            return IdentifyResult.NotEnoughMoney;
        if (item[ItemHidden] == 0)
            return IdentifyResult.AlreadyIdentified;
        item[ItemHidden] = 0;
        return IdentifyResult.Identified;
    }

    /// <summary>Gem value from the appraiser's 1d100 (0x268FB): 10, 50, 100, 500, 1000 or 5000 gold.</summary>
    public static ushort GemValue(int d100) => d100 switch
    {
        <= 25 => 10,
        <= 50 => 50,
        <= 70 => 100,
        <= 90 => 500,
        <= 99 => 1000,
        _ => 5000,
    };

    /// <summary>
    /// Jewellery value (0x26B88): a 1d100 picks a band, then Random(n) + base: 1–10 → 100 + R(900), 11–20 → 200 +
    /// R(1000), 21–40 → 300 + R(1500), 41–50 → 500 + R(2500), 51–70 → 1000 + R(5000), 71–90 → 2000 + R(6000),
    /// 91–100 → 2000 + R(10000). <paramref name="random"/> answers Random(n) (0..n−1).
    /// </summary>
    public static ushort JewelleryValue(Func<int, int> random)
    {
        int d = random(100) + 1;
        var (n, b) = d switch
        {
            <= 10 => (900, 100),
            <= 20 => (1000, 200),
            <= 40 => (1500, 300),
            <= 50 => (2500, 500),
            <= 70 => (5000, 1000),
            <= 90 => (6000, 2000),
            _ => (10000, 2000),
        };
        return (ushort)(random(n) + b);
    }

    /// <summary>
    /// Appraise one gem or piece of jewellery of the current character (0x2663B): the count word goes down by one and
    /// the value is rolled. Keep (offered only when the character is not overloaded by 1 coin of weight and has
    /// fewer than 16 items) adds an identified item of type 70, weight 1, that value and word 0x65 (gem) or 0xD6
    /// (jewellery) at the end of the inventory; anything else sells it for value div 5 platinum
    /// (<see cref="Money.AddPlatinum"/>). Returns the value, or null when the character has none of that kind.
    /// </summary>
    public static ushort? Appraise(EclCharacter c, uint[] pool, bool jewellery, Func<int, int> random,
        Func<ushort, bool, bool> keep, int strengthAllowance)
    {
        int at = jewellery ? Money.Jewellery : Money.Gems;
        if (c.Word(at) == 0)
            return null;
        c.SetWord(at, (ushort)(c.Word(at) - 1));
        ushort value = jewellery ? JewelleryValue(random) : GemValue(random(100) + 1);
        bool canKeep = !Money.Overloaded(c, 1, strengthAllowance, out _) && c[ItemCount] < MaxItems;
        if (keep(value, canKeep) && canKeep)
        {
            var it = new byte[EclCharacter.ItemRecordSize];
            it[0x31] = (byte)(jewellery ? 0xD6 : 0x65);
            it[ItemType] = 70;
            it[ItemValue] = (byte)value;
            it[ItemValue + 1] = (byte)(value >> 8);
            it[ItemWeight] = 1;
            c.Items.Add(it);
            c[ItemCount] = (byte)c.Items.Count;
        }
        else
            Money.AddPlatinum(c, pool, (ushort)(value / 5), strengthAllowance);
        return value;
    }

    /// <summary>
    /// Pool (0x25191): every party member whose byte 0x84 is 0 or 0xB3 puts its seven money words into the pool and
    /// loses that weight. Sets the "pooled" byte DS:6DC0 (returned as true).
    /// </summary>
    public static bool PoolMoney(IEnumerable<EclCharacter> party, uint[] pool)
    {
        foreach (var c in party)
        {
            if (c[0x84] is not (0 or 0xB3))
                continue;
            for (int k = 0; k < 7; k++)
            {
                ushort v = c.Word(Money.Coins + 2 * k);
                pool[k] = unchecked(pool[k] + v);
                c.SetWord(Money.Weight, unchecked((ushort)(c.Word(Money.Weight) - v)));
                c.SetWord(Money.Coins + 2 * k, 0);
            }
        }
        return true;
    }

    /// <summary>
    /// Share (0x252C3). n = party members with 0x84 = 0 or 0xB3 (0x25269); per kind each share is pool div n and
    /// the remainder pool mod n (signed 32-bit, taken as words). Members with 0x84 &lt; 0x80 then receive, kinds 6
    /// down to 0: the share if it fits (and then one coin of the remainder if the whole remainder would fit), else as much as fits
    /// (the rest joins the remainder). Then each remainder, kinds 6 down to 0, goes to every member in list order
    /// (no 0x84 test) up to the room each has left. The pool keeps what is left; returns the new "pooled" byte
    /// (DS:6DC0 = 1 when anything is left). [?] With n = 0 and money in the pool the program divides by zero.
    /// </summary>
    public static bool Share(IReadOnlyList<EclCharacter> party, uint[] pool, Func<EclCharacter, int> strengthAllowance)
    {
        int n = party.Count(c => c[0x84] is 0 or 0xB3);
        var per = new ushort[7];
        var rem = new ushort[7];
        for (int k = 0; k < 7; k++)
        {
            int v = (int)pool[k];
            if (v > 0)
            {
                per[k] = (ushort)(v / n);
                rem[k] = (ushort)(v % n);
            }
        }
        foreach (var c in party)
        {
            if (c[0x84] >= 0x80)
                continue;
            int sa = strengthAllowance(c);
            for (int k = 6; k >= 0; k--)
            {
                int at = Money.Coins + 2 * k;
                if (!Money.Overloaded(c, per[k], sa, out var room))
                {
                    AddCoins(c, at, per[k]);
                    // the program tests the whole remainder, then adds one coin (0x25450)
                    if (rem[k] > 0 && !Money.Overloaded(c, rem[k], sa, out _))
                    {
                        AddCoins(c, at, 1);
                        rem[k]--;
                    }
                }
                else
                {
                    AddCoins(c, at, room);
                    rem[k] = unchecked((ushort)(rem[k] + per[k] - room));
                }
            }
        }
        for (int k = 6; k >= 0; k--)
        {
            if (rem[k] == 0)
                continue;
            foreach (var c in party)
            {
                ushort room = unchecked((ushort)(Money.CarryLimit(strengthAllowance(c)) - c.Word(Money.Weight)));
                if (room == 0)
                    continue;
                ushort give = rem[k] > room ? room : rem[k];
                AddCoins(c, Money.Coins + 2 * k, give);
                rem[k] -= give;
            }
        }
        bool left = false;
        for (int k = 0; k < 7; k++)
        {
            pool[k] = rem[k];
            left |= rem[k] != 0;
        }
        return left;
    }

    /// <summary>
    /// Take (0x25711): if <paramref name="amount"/> would overload the character, nothing (a message); else the
    /// amount, limited to what the pool holds of that kind, moves to the character, whose weight grows by it.
    /// </summary>
    public static bool Take(EclCharacter c, uint[] pool, int kind, ushort amount, int strengthAllowance)
    {
        if (Money.Overloaded(c, amount, strengthAllowance, out _))
            return false;
        if ((int)pool[kind] < amount)
            amount = (ushort)pool[kind];
        pool[kind] = unchecked(pool[kind] - amount);
        AddCoins(c, Money.Coins + 2 * kind, amount);
        return true;
    }

    private static void AddCoins(EclCharacter c, int at, ushort n)
    {
        c.SetWord(at, unchecked((ushort)(c.Word(at) + n)));
        c.SetWord(Money.Weight, unchecked((ushort)(c.Word(Money.Weight) + n)));
    }

    private static ushort Word(byte[] r, int o) => (ushort)(r[o] | r[o + 1] << 8);
}
