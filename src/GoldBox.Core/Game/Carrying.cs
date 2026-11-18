using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Strength and carried weight (START.EXE 0x1D8A, 0x1F4B and the weight part of the stat recalculation 0x1758;
/// docs/re/character-format.md "Strength code and carrying allowance", "Movement recalculation"). The shop, temple
/// and loot rules take the allowance as their <c>strengthAllowance</c> argument.
/// </summary>
public static class Carrying
{
    private const int Str = 0x10, StrExceptional = 0x16;

    /// <summary>
    /// The strength code (START 0x1D8A): Str 0..17 → Str; 18 → by exceptional strength 0 → 18, 1–50 → 19, 51–75 → 20,
    /// 76–90 → 21, 91–99 → 22, 100 → 23; Str 19..25 → Str + 5 (24..30). [?] For Str above 25, or Str 18 with an
    /// exceptional value above 100, the routine returns an uninitialised stack byte; we answer null.
    /// </summary>
    public static int? StrengthCode(EclCharacter c)
    {
        byte s = c[Str];
        if (s <= 17)
            return s;
        if (s == 18)
            return c[StrExceptional] switch
            {
                0 => 18,
                <= 50 => 19,
                <= 75 => 20,
                <= 90 => 21,
                <= 99 => 22,
                100 => 23,
                _ => null,
            };
        if (s <= 25)
            return s + 5;
        return null;
    }

    /// <summary>
    /// The carrying allowance in coins (START 0x1F4B) by strength code: 1–3 → −350, 4–5 → −250, 6–7 → −150,
    /// 12–13 → 100, 14–15 → 200, 16 → 350, 17–21 → 500 + 250 × (code − 17), 22–26 → 2000 + 1000 × (code − 22),
    /// 27 → 7500, 28–30 → 9000 + 3000 × (code − 28), any other code (0, 8–11) → 0.
    /// </summary>
    public static int AllowanceForCode(int code) => code switch
    {
        >= 1 and <= 3 => -350,
        4 or 5 => -250,
        6 or 7 => -150,
        12 or 13 => 100,
        14 or 15 => 200,
        16 => 350,
        >= 17 and <= 21 => 500 + 250 * (code - 17),
        >= 22 and <= 26 => 2000 + 1000 * (code - 22),
        27 => 7500,
        >= 28 and <= 30 => 9000 + 3000 * (code - 28),
        _ => 0,
    };

    /// <summary>
    /// The character's carrying allowance (START 0x1F4B). [?] When the strength code is undefined
    /// (<see cref="StrengthCode"/> null) the program reads a stray byte; we take code 0 (allowance 0).
    /// </summary>
    public static int StrengthAllowance(EclCharacter c) => AllowanceForCode(StrengthCode(c) ?? 0);

    /// <summary>
    /// The carried weight the stat recalculation writes to +0x102 (START 0x17B4–0x1948): Σ item weight (word 0x37) ×
    /// count (byte 0x39, 0 counts as 1) + the seven money words (each coin, gem or jewel weighs 1). 16-bit sums.
    /// A readied Bag of Holding (item byte 0x2F = <see cref="StatRecalc.BagMarker"/>, name word "Holding"; START
    /// 0x18DA) takes 5000 off, floored at 0 (START 0x1AEC), but never below the readied items' own weight (DS:4846).
    /// </summary>
    public static ushort Weight(EclCharacter c)
    {
        ushort w = 0, readied = 0;
        bool bag = false;
        foreach (var it in c.Items)
        {
            ushort iw = (ushort)(it[0x37] | it[0x38] << 8);
            if (it[0x39] > 0)
                iw = unchecked((ushort)(iw * it[0x39]));
            w = unchecked((ushort)(w + iw));
            if (it[CombatRules.ItemEquipped] == 0)
                continue;
            readied = unchecked((ushort)(readied + iw));
            if (it[0x2F] == StatRecalc.BagMarker)
                bag = true;
        }
        for (int k = 0; k < 7; k++)
            w = unchecked((ushort)(w + c.Word(Money.Coins + 2 * k)));
        if (bag)
        {
            w = w < 5000 ? (ushort)0 : (ushort)(w - 5000);
            if (w < readied)
                w = readied;
        }
        return w;
    }

    /// <summary>
    /// The weight part of the stat recalculation (START 0x1758): +0x102 := <see cref="Weight"/>. [?] The rest of the
    /// recalculation (movement, AC, equipment pointers) is not redone here.
    /// </summary>
    public static void RecalculateWeight(EclCharacter c) => c.SetWord(Money.Weight, Weight(c));
}
