using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The temple's heal list (GAME.OVR 0x4F4E, entries 0x47C0–0x4E9F; docs/re/shop-format.md "Temple"). Every entry
/// works on the current character. The flow per entry: if the condition is absent the program says so and asks
/// "cast cure anyway" (0x458A); on yes (or when the condition holds) it asks to pay (0x4636, <see cref="Money.Pay"/>
/// with the 16-bit character total); after a payment the effect runs (and the "is cured" text shows even when the
/// effect then does nothing).
/// </summary>
public static class TempleRules
{
    public enum Cure { Blindness = 0, Disease = 1, CureLight = 2, CureSerious = 3, CureCritical = 4, Poison = 5,
        RaiseDead = 6, RemoveCurse = 7, StoneToFlesh = 8 }

    /// <summary>Prices in gold (immediates in each entry routine).</summary>
    public static ushort Price(Cure cure) => cure switch
    {
        Cure.Blindness or Cure.Disease or Cure.Poison => 1000,
        Cure.CureLight => 100,
        Cure.CureSerious => 350,
        Cure.CureCritical => 600,
        Cure.RaiseDead => 5500,
        Cure.RemoveCurse => 3500,
        Cure.StoneToFlesh => 2000,
        _ => throw new ArgumentOutOfRangeException(nameof(cure)),
    };

    /// <summary>Effect ids entry 1 tests and removes (DS:0114, six bytes; 0x4864).</summary>
    public static readonly byte[] DiseaseEffects = [31, 34, 43, 44, 50, 57];
    /// <summary>Effect ids entry 5 removes, in this order (0x4D45–0x4D87; the engine removes every record of each, see
    /// <see cref="CurePoison"/>); its test is effect 55 alone.</summary>
    public static readonly byte[] PoisonEffects = [55, 22, 15];

    private const int Status = 0x10C, InPlay = 0x10D, Hp = 0x11B, MaxHp = 0x32, Con = 0x14, RolledHp = 0xB1,
        Levels = 0x96, FighterLevel = 0x98;

    /// <summary>
    /// Whether the entry's condition holds (else the program asks "cure anyway"). Cures 2–4 have none (always
    /// true). Remove curse: an item with <c>cursed</c> (0x36) ≠ 0, or effect 36.
    /// </summary>
    public static bool Applies(Cure cure, EclCharacter c) => cure switch
    {
        Cure.Blindness => c.HasEffect(33),
        Cure.Disease => DiseaseEffects.Any(c.HasEffect),
        Cure.Poison => c.HasEffect(55),
        Cure.RaiseDead => c[Status] is 6 or 1,
        Cure.RemoveCurse => c.Items.Any(i => i[0x36] != 0) || c.HasEffect(36),
        Cure.StoneToFlesh => c[Status] == 7,
        _ => true,
    };

    /// <summary>
    /// The effect after payment. <paramref name="random"/> answers Random(n) (used only by cures 2–4: 1d8, 2d8 + 1,
    /// 3d8 + 3 through 0x2BCCD). With <paramref name="effects"/> (the game's effect engine outside a fight) every entry
    /// runs as the program's (s49 cure 1, s51 the rest; checked under emulation, `por_venue_oracle.py`): effect removal
    /// goes through 0x2AF10 (<see cref="EffectHandlers.RemoveEffect"/>: the handler first, with "removed", when the
    /// record's byte 4 is set), cures 1, 5 and 6 under the effect lock DS:681F = 1 (written 0 after, not restored), and
    /// the healing of cures 2–4 is 0x2C647 with flag 0 (<see cref="EffectHandlers.RestoreHitPoints"/>, which revives an
    /// unconscious character through handler 0x4E). Without an engine the records are only dropped and the healing is
    /// <see cref="Heal"/>.
    /// </summary>
    public static void Apply(Cure cure, EclCharacter c, Func<int, int> random, EffectHandlers? effects = null)
    {
        switch (cure)
        {
            case Cure.Blindness: // 0x483D: 0x2AF10(member, 33, nil), no lock
                if (effects is not null)
                    effects.RemoveEffect(c, 33);
                else
                    RemoveEffects(c, 33);
                break;
            case Cure.Disease:
                if (effects is not null)
                    CureDisease(c, effects);
                else
                    RemoveEffects(c, DiseaseEffects);
                break;
            case Cure.CureLight or Cure.CureSerious or Cure.CureCritical:
            {
                int n = cure - Cure.CureLight + 1;
                int sum = 0;
                for (int k = 0; k < n; k++)
                    sum += random(8) + 1;
                byte amount = (byte)(sum + n switch { 2 => 1, 3 => 3, _ => 0 });
                if (effects is not null)
                    effects.RestoreHitPoints(c, amount, false); // 0x49C2 / 0x4A11 / 0x4A61: 0x2C647(member, amount, 0)
                else
                    Heal(c, amount);
                break;
            }
            case Cure.Poison:
                if (effects is not null)
                    CurePoison(c, effects);
                else
                    foreach (var id in PoisonEffects) // every record of each (the s51 fix, as CurePoison)
                        c.Effects.RemoveAll(e => e.Length > 0 && e[0] == id);
                break;
            case Cure.RaiseDead:
                if (Applies(cure, c)) // 0x4B18: the status test made before the questions ([bp−0x106])
                    RaiseDead(c, effects);
                break;
            case Cure.RemoveCurse:
                RemoveCurse(c, effects);
                break;
            case Cure.StoneToFlesh:
                if (c[Status] == 7)
                {
                    c[Status] = 0;
                    c[InPlay] = 1;
                    c[Hp] = 1;
                }
                break;
        }
    }

    /// <summary>
    /// The heal routine with its last argument 0 (0x2C647) without an effect engine: only for status 0, 1, 4 or 5;
    /// effect 50 blocks it; HP += amount (byte), capped at max HP. A creature out of play with status 5 (dying)
    /// becomes 4. The program then runs effect handler 0x4E for a status-4 creature outside a fight (back in play,
    /// status 0: <see cref="EffectHandlers.RestoreHitPoints"/>, which <see cref="Apply"/> uses when given the engine);
    /// this fallback leaves it out. Returns whether it healed.
    /// </summary>
    public static bool Heal(EclCharacter c, byte amount)
    {
        if (c[Status] is not (0 or 1 or 4 or 5))
            return false;
        if (c.HasEffect(50))
            return false;
        c[Hp] = unchecked((byte)(c[Hp] + amount));
        if (c[Hp] > c[MaxHp])
            c[Hp] = c[MaxHp];
        if (c[InPlay] == 0 && c[Status] == 5)
            c[Status] = 4;
        return true;
    }

    /// <summary>
    /// Raise dead (0x4B22–0x4C9D), read literally: remove effects 32 and 55; HP 1, status 0, in play 1; Con − 1 if
    /// above 0; d = max HP − rolled HP (0xB1) when above, else 0. With the new Con ≥ 14: s = Σ over the 8 level bytes
    /// (0x96 + k) that are &gt; 0 of: fighter (k = 2) level × (Con − 14); others 2 × level when Con &gt; 15, else
    /// level (byte sum); d = d div s when s &gt; 0; then max HP −= d unless Con ≥ 17 and the fighter level ≤ 0.
    /// No dice. [?] The intent (removing one level's worth of Constitution hit points?) is not settled.
    /// </summary>
    public static void RaiseDead(EclCharacter c, EffectHandlers? effects = null)
    {
        if (effects is not null)
        {
            // 0x4B22–0x4B53: DS:681F := 1, 0x2AF10(member, 32, nil), 0x2AF10(member, 55, nil), DS:681F := 0. Handler 32
            // (Animate Dead, 0xF776) runs on a record with byte 4 set: under the lock no death, but it writes the
            // hostile, auto, undead-level, attack-level, move and type bytes before the HP / status writes below.
            effects.Cells.Lock = 1;
            effects.RemoveEffect(c, 32);
            effects.RemoveEffect(c, 55);
            effects.Cells.Lock = 0;
        }
        else
            RemoveEffects(c, 32, 55);
        c[Hp] = 1;
        c[Status] = 0;
        c[InPlay] = 1;
        if (c[Con] > 0)
            c[Con]--;
        byte d = c[MaxHp] > c[RolledHp] ? (byte)(c[MaxHp] - c[RolledHp]) : (byte)0;
        if (c[Con] < 14)
            return;
        byte s = 0;
        for (int k = 0; k < 8; k++)
        {
            sbyte lv = (sbyte)c[Levels + k];
            if (lv <= 0)
                continue;
            if (k == 2)
                s = unchecked((byte)(s + lv * (c[Con] - 14)));
            else if (c[Con] > 15)
                s = unchecked((byte)(s + 2 * lv));
            else
                s = unchecked((byte)(s + lv));
        }
        if (s > 0)
            d = (byte)(d / s);
        if (c[Con] >= 17 && (sbyte)c[FighterLevel] <= 0)
            return;
        c[MaxHp] = unchecked((byte)(c[MaxHp] - d));
    }

    /// <summary>
    /// The remove-curse routine (0x29558) on the character: with effect 36 only that effect is removed; else the
    /// first item with <c>cursed</c> ≠ 0 gets <c>cursed</c> := 0 (later cursed items stay cursed).
    /// </summary>
    public static void RemoveCurse(EclCharacter c, EffectHandlers? effects = null)
    {
        if (c.HasEffect(36))
        {
            if (effects is not null)
                effects.RemoveEffect(c, 36); // 0x2BF6B → 0x2AF10
            else
                RemoveEffects(c, 36);
            return;
        }
        var item = c.Items.FirstOrDefault(i => i[0x36] != 0);
        if (item is not null)
            item[0x36] = 0;
    }

    /// <summary>
    /// 0x4864 after payment (0x48FD–0x4934): DS:681F := 1; for each id of DS:0114 (31, 34, 43, 44, 50, 57) the remove
    /// routine 0x2AF10(creature, id, nil): the first record with the id leaves, its handler running first with
    /// "removed" when byte 4 is set; DS:681F := 0. 0x2AF10 never reads the lock; the handlers do: 43, 44 and 50 (and 34
    /// through 43 and 44) re-add themselves through 0xEC7A, which adds nothing while the lock is set, and their harm (a
    /// Strength point, a hit point, the healing) is done only when the re-add happened; 57 does nothing under the
    /// lock. Without the lock a removal would put the disease straight back. 31 ends the creature's actions on its
    /// combat record (START 0x31D7) whatever the lock.
    /// </summary>
    public static void CureDisease(EclCharacter c, EffectHandlers effects)
    {
        effects.Cells.Lock = 1;
        foreach (var id in DiseaseEffects)
            effects.RemoveEffect(c, id);
        effects.Cells.Lock = 0; // 0x4934 writes 0, not the value it found
    }

    /// <summary>
    /// 0x4CC8 after payment. The program (0x4D45–0x4D8C): DS:681F := 1; 0x2AF10(member, id, nil) for 55, 22 and 15 in
    /// that order, the first record of each; DS:681F := 0. Handler 22 (0xF3D0) runs when its record has byte 4 set:
    /// with another record of 55 still on the member it kills it (status 6, the poison death text); then it sets the
    /// lock to 1 itself, removes 15 and writes the lock 0 (0xF40B / 0xF424), so the cure's own removal of a second 15
    /// runs unlocked: handler 15 (0xF246) re-adds itself for 10 minutes and takes a hit point when HP &gt; 1 (checked
    /// under emulation and in DOSBox-X, s51). <b>Original bug, fixed (user, s51; not an option):</b> the engine removes
    /// every record of 55, then of 22, then of 15, the lock set again before each removal so a handler's own write of
    /// 0 never leaves the next removal unlocked; with no 55 left when a handler 22 runs, the paid cure never harms or
    /// kills. The lock is 0 afterwards, as the program leaves it.
    /// </summary>
    public static void CurePoison(EclCharacter c, EffectHandlers effects)
    {
        foreach (var id in PoisonEffects)
            while (EffectHandlers.FindEffect(c, id) is { } node)
            {
                effects.Cells.Lock = 1;
                effects.RemoveEffect(c, id, node);
            }
        effects.Cells.Lock = 0; // 0x4D8C writes 0, not the value it found
    }

    private static void RemoveEffects(EclCharacter c, params byte[] ids)
    {
        foreach (var id in ids)
        {
            int i = c.Effects.FindIndex(e => e.Length > 0 && e[0] == id); // 0x2AF10 removes the first match
            if (i >= 0)
                c.Effects.RemoveAt(i);
        }
    }
}
