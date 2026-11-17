using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The stat recalculation START.EXE 0x1758 (with 0x0BA0 weapon, 0x0D9B armour movement, 0x0E22 AC items, 0x0F40
/// encumbrance, 0x1C86 Dex AC, 0x1D08 Dex missile adjustment, 0x1E22 / 0x1EC5 Str to-hit / damage, 0x1F4B allowance).
/// It rebuilds, from the record's base bytes and readied items: item count 0xC7, hands 0x100, weight 0x102, save
/// bonus 0x101, THAC0 0x110, AC 0x111, rear AC 0x112, attack dice/sides/bonus 0x115–0x11A, movement 0x11C and sweep
/// level 0x6B. The fight calls it at each turn start (GAME.OVR 0x9A8A), on the defender before its AC effects
/// (0x13D84), on the attacker after an attack (0x14299), in the AI's weapon pick (0xC35D–0xC4C6) and elsewhere.
/// Checked against the program run under emulation (<c>StatRecalcProgramTests</c>).
/// </summary>
public static class StatRecalc
{
    public const int ItemCount = 0xC7, Hands = 0x100, SaveBonus = 0x101, Weight = 0x102, BaseThac0 = 0x2D, BaseAc = 0xA9,
        BaseMove = 0x72, Race = 0x2E, StrBonusFlag = 0xAA, SweepBase = 0x98;

    /// <summary>Item byte 0x2F value of the bag that carries 5000 coins' weight for free (START 0x18DA). [?] which item.</summary>
    public const byte BagMarker = 0xBA;

    public static void Recalculate(EclCharacter c, BaseItemTable items)
    {
        byte count = 0, hands = 0;
        ushort weight = 0, readied = 0;
        bool bag = false;
        var eq = FightRules.Equipment(c, items);
        foreach (var it in c.Items)
        {
            count++;
            ushort w = (ushort)(it[0x37] | it[0x38] << 8);
            if (it[0x39] > 0)
                w = unchecked((ushort)(w * it[0x39]));
            weight = unchecked((ushort)(weight + w));
            if (it[CombatRules.ItemEquipped] == 0)
                continue;
            readied = unchecked((ushort)(readied + w));
            if (it[0x2F] == BagMarker)
                bag = true;
            hands = (byte)(hands + items[it[0x2E], 1]);
        }
        c[ItemCount] = count;
        c[Hands] = hands;
        for (int k = 0; k < 7; k++)
            weight = unchecked((ushort)(weight + c.Word(0x88 + 2 * k)));
        for (int p = 1; p <= 2; p++)
        {
            c[0x114 + p] = c[0xA2 + p];
            c[0x116 + p] = c[0xA4 + p];
            c[0x118 + p] = c[0xA6 + p];
        }
        var ac = new sbyte[5]; // [0] Dex, [1] shield, [2] other items, [3] rings, [4] armour
        bool magicArmour = false;
        c[SaveBonus] = 0;
        c[FightRules.Ac] = c[BaseAc];
        c[FightRules.Movement] = c[BaseMove];
        c[FightRules.Thac0] = c[BaseThac0];
        ac[0] = DexAc(c[FightRules.Dex]);
        if (eq[FightRules.SlotWeapon] is null)
        {
            c[FightRules.Thac0] = (byte)(c[FightRules.Thac0] + StrHit(c));
            c[0x119] = (byte)((sbyte)c[0x119] + StrDamage(c));
        }
        else
            Weapon(c, eq, items);
        foreach (var it in c.Items)
        {
            if (it[CombatRules.ItemEquipped] == 0)
                continue;
            ArmourMove(c, it, items);
            AcItem(c, it, items, ac, ref magicArmour);
        }
        if (magicArmour)
            ac[3] = 0;
        if (bag)
        {
            weight = weight < 5000 ? (ushort)0 : (ushort)(weight - 5000);
            if (weight < readied)
                weight = readied;
        }
        c.SetWord(Weight, weight);
        Encumbrance(c, weight);
        if (ac[4] < c[FightRules.Ac])
            ac[4] = (sbyte)c[FightRules.Ac];
        byte total = 0;
        foreach (var a in ac)
            total = (byte)(total + a);
        c[FightRules.Ac] = total;
        c[FightRules.AcRear] = (byte)(ac[4] + ac[2] + ac[3] - 2);
        c[FightRules.SweepLevel] = (sbyte)c[SweepBase] > 0 && (sbyte)c[Race] > 0 ? c[SweepBase] : (byte)1;
    }

    /// <summary>0x0BA0: THAC0 from base, + Dex (0x1D08) for flags bit 1, bonus = small bonus, + Str for flags bit 2,
    /// the plus (with arrows / bolts), + 1 for race 2 with types 0x24, 0x25, 0x29–0x2C; dice and sides = small.</summary>
    private static void Weapon(EclCharacter c, byte[]?[] eq, BaseItemTable items)
    {
        var w = eq[FightRules.SlotWeapon]!;
        int t = w[0x2E];
        byte flags = items[t, BaseItemTable.Flags];
        c[FightRules.Thac0] = c[BaseThac0];
        if ((flags & 2) != 0)
            c[FightRules.Thac0] = (byte)(c[FightRules.Thac0] + FightRules.DexAdjust(c[FightRules.Dex]));
        c[0x119] = items[t, BaseItemTable.SmallBonus];
        if ((flags & 4) != 0)
        {
            c[FightRules.Thac0] = (byte)(c[FightRules.Thac0] + StrHit(c));
            c[0x119] = (byte)((sbyte)c[0x119] + StrDamage(c));
        }
        sbyte plus = (sbyte)w[CombatRules.ItemPlus];
        if (flags > 0x7F && eq[FightRules.SlotBolts] is { } bolts)
            plus = (sbyte)(plus + (sbyte)bolts[CombatRules.ItemPlus]);
        if ((flags & 1) != 0 && eq[FightRules.SlotArrows] is { } arrows)
            plus = (sbyte)(plus + (sbyte)arrows[CombatRules.ItemPlus]);
        c[0x119] = (byte)((sbyte)c[0x119] + plus);
        if (c[Race] == 2 && (t is > 0x28 and < 0x2D || t == 0x25 || t == 0x24))
            plus++;
        c[FightRules.Thac0] = (byte)(c[FightRules.Thac0] + plus);
        c[0x115] = items[t, 9];
        c[0x117] = items[t, 10];
    }

    /// <summary>0x0D9B: body armour (base slot 2) sets the movement by its weight (≤ 150 → base 0x72, ≤ 399 → 9,
    /// else 6); a plus ≠ 0 adds 3 when the movement is ≤ 9.</summary>
    private static void ArmourMove(EclCharacter c, byte[] it, BaseItemTable items)
    {
        if (items[it[0x2E], BaseItemTable.Slot] != 2)
            return;
        int w = it[0x37] | it[0x38] << 8;
        c[FightRules.Movement] = w <= 0x96 ? c[BaseMove] : w <= 0x18F ? (byte)9 : (byte)6;
        if (it[CombatRules.ItemPlus] != 0 && c[FightRules.Movement] <= 9)
            c[FightRules.Movement] = (byte)(c[FightRules.Movement] + 3);
    }

    /// <summary>0x0E22: base byte 6 above 0x7F gives AC: code = byte 6 AND 0x7F. A shield (slot 1): shield AC = code
    /// + plus. Code 0: a ring (slot 9) keeps the best plus, other items add their plus, and byte 0x33 adds to the save
    /// bonus 0x101. Else the best code + plus is the armour value; a body armour (slot 2) with plus &gt; 0 that wins
    /// marks magic armour (rings of protection then count 0).</summary>
    private static void AcItem(EclCharacter c, byte[] it, BaseItemTable items, sbyte[] ac, ref bool magicArmour)
    {
        byte code = items[it[0x2E], 6];
        if (code <= 0x7F)
            return;
        code &= 0x7F;
        byte slot = items[it[0x2E], BaseItemTable.Slot];
        sbyte plus = (sbyte)it[CombatRules.ItemPlus];
        if (slot == 1)
            ac[1] = (sbyte)(code + plus);
        else if (code == 0)
        {
            if (slot == 9)
            {
                if (plus > ac[3])
                    ac[3] = plus;
            }
            else
                ac[2] = (sbyte)(ac[2] + plus);
            c[SaveBonus] = (byte)((sbyte)c[SaveBonus] + (sbyte)it[0x33]);
        }
        else if (code + plus > ac[4])
        {
            ac[4] = (sbyte)(code + plus);
            if (plus > 0 && slot == 2)
                magicArmour = true;
        }
    }

    /// <summary>0x0F40: weight over the allowance (0x1F4B; below 0 → 0): ≤ 512 keeps the movement, ≤ 768 caps it at
    /// 9, ≤ 1024 at 6, more at 3 (signed 16-bit compares).</summary>
    private static void Encumbrance(EclCharacter c, ushort weight)
    {
        short over = unchecked((short)(weight - Carrying.StrengthAllowance(c)));
        if (over < 0)
            over = 0;
        byte cap = over <= 0x200 ? c[FightRules.Movement] : over <= 0x300 ? (byte)9 : over <= 0x400 ? (byte)6 : (byte)3;
        if (cap < c[FightRules.Movement])
            c[FightRules.Movement] = cap;
    }

    /// <summary>0x1C86: AC from Dex: 1–3 → −4, 4–6 → Dex − 7, 15–18 → Dex − 14, 19–20 → 4, 21–23 → 5, 24–25 → 6.</summary>
    public static sbyte DexAc(byte dex) => dex switch
    {
        >= 1 and <= 3 => -4,
        >= 4 and <= 6 => (sbyte)(dex - 7),
        >= 15 and <= 18 => (sbyte)(dex - 14),
        19 or 20 => 4,
        >= 21 and <= 23 => 5,
        24 or 25 => 6,
        _ => 0,
    };

    /// <summary>0x1E22: Str to-hit when byte 0xAA ≠ 0, by strength code: 1–3 → −3, 4–5 → −2, 6–7 → −1, 17–19 → 1,
    /// 20–22 → 2, 23–25 → 3, 26–27 → 4, 28–30 → code − 23. [?] An undefined strength code is taken as 0.</summary>
    public static sbyte StrHit(EclCharacter c)
    {
        if (c[StrBonusFlag] == 0)
            return 0;
        int s = Carrying.StrengthCode(c) ?? 0;
        return s switch
        {
            >= 1 and <= 3 => -3,
            4 or 5 => -2,
            6 or 7 => -1,
            >= 17 and <= 19 => 1,
            >= 20 and <= 22 => 2,
            >= 23 and <= 25 => 3,
            26 or 27 => 4,
            >= 28 and <= 30 => (sbyte)(s - 23),
            _ => 0,
        };
    }

    /// <summary>0x1EC5: Str damage when byte 0xAA ≠ 0: 1–2 → −2, 3–5 → −1, 16 → 1, 17–19 → code − 16, 20–29 →
    /// code − 17, 30 → 14. [?] An undefined strength code is taken as 0.</summary>
    public static sbyte StrDamage(EclCharacter c)
    {
        if (c[StrBonusFlag] == 0)
            return 0;
        int s = Carrying.StrengthCode(c) ?? 0;
        return s switch
        {
            1 or 2 => -2,
            >= 3 and <= 5 => -1,
            16 => 1,
            >= 17 and <= 19 => (sbyte)(s - 16),
            >= 20 and <= 29 => (sbyte)(s - 17),
            30 => 14,
            _ => 0,
        };
    }
}
