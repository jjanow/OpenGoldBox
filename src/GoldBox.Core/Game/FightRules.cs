using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The base item table (DS:5582, 128 entries of 16 bytes, loaded from the <c>ITEMS</c> file without its 2-byte header
/// by GAME.OVR 0xEBA0; docs/re/item-format.md "Base item table"). Index = item byte 0x2E.
/// </summary>
public sealed class BaseItemTable
{
    public const int EntrySize = 16, Count = 128;
    /// <summary>Entry bytes: slot (0), hands (1), large dice / sides / bonus (2–4), rate of fire ×2 (5), AC code (6),
    /// small dice / sides / bonus (9–11), range (12), class mask (13), flags (14).</summary>
    public const int Slot = 0, LargeDice = 2, LargeSides = 3, LargeBonus = 4, RateOfFire = 5, SmallBonus = 11, Range = 12,
        Flags = 14;

    private readonly byte[] _table;

    public BaseItemTable(byte[] table)
    {
        _table = new byte[EntrySize * Count];
        table.AsSpan(0, Math.Min(table.Length, _table.Length)).CopyTo(_table);
    }

    /// <summary>From the <c>ITEMS</c> file (2-byte header, then the table).</summary>
    public static BaseItemTable FromItemsFile(byte[] file) => new(file.AsSpan(2).ToArray());

    public byte this[int type, int field] => _table[(type & 0x7F) * EntrySize + field];
    public void Set(int type, int field, byte value) => _table[(type & 0x7F) * EntrySize + field] = value;
}

/// <summary>
/// Effect hooks the fight calls (dispatcher GAME.OVR 0x2B1E4 runs every effect of the creature whose trigger class
/// matches; handlers DS:6828), implemented by <see cref="EffectHandlers"/>; <see cref="NoFightEffects"/> does nothing,
/// which is the program's behaviour for a creature with no effects.
/// </summary>
public interface IFightEffects
{
    /// <summary>0x2B1E4: run the effects of <paramref name="c"/> with trigger class <paramref name="trigger"/>
    /// (<see cref="FightRules"/> lists the classes the fight uses). They may change <paramref name="fight"/>'s scratch cells.</summary>
    void Trigger(int trigger, EclCharacter c, FightState fight);
    /// <summary>The removal call of an effect (0x2AF10 runs its handler with "removed" set).</summary>
    void Removed(EclCharacter c, byte[] effect);
    /// <summary>The terrain check 0x2B9D1 the end of round runs for every creature after its trigger-0x13 effects.</summary>
    void Terrain(EclCharacter c, FightState fight) { }
}

public sealed class NoFightEffects : IFightEffects
{
    public static readonly NoFightEffects Instance = new();
    public void Trigger(int trigger, EclCharacter c, FightState fight) { }
    public void Removed(EclCharacter c, byte[] effect) { }
}

/// <summary>
/// Direction and distance between creatures on the combat map: 0x14E38 and START 0x30FB, implemented by
/// <see cref="CombatMap"/> (arena and placement: <see cref="FightPlacement"/>); <see cref="GridGeometry"/> is a test
/// stand-in.
/// </summary>
public interface IFightGeometry
{
    /// <summary>0x14E38: direction 0–7 from <paramref name="from"/> towards <paramref name="to"/>.</summary>
    int DirectionToward(EclCharacter from, EclCharacter to);
    /// <summary>START 0x30FB: distance in squares between two creatures (path distance ÷ 2).</summary>
    int Distance(EclCharacter a, EclCharacter b);
}

/// <summary>
/// Rules of the fight read from the program (docs/re/combat-format.md "The fight"): start of round, attack rates,
/// movement, the attack roll, damage and what damage does, end of round, the NPC treasure share. Pure functions over
/// creature records, combat records and the RNG; dice are <c>count × (Random(sides) + 1)</c> in draw order (0x2BCCD).
/// </summary>
public static class FightRules
{
    /// <summary>Creature record offsets the fight reads (docs/re/character-format.md).</summary>
    public const int Dex = 0x13, HpMax = 0x32, SweepLevel = 0x6B, Size = 0x6C, ThiefLevel = 0x9C, BaseAttacksPrimary = 0xA1,
        BaseAttacksSecondary = 0xA2, Status = 0x10C, InPlay = 0x10D, Hostile = 0x10E, Thac0 = 0x110, Ac = 0x111,
        AcRear = 0x112, AttacksLeft = 0x112 /* + phase */, DamageDice = 0x114 /* + phase */,
        DamageSides = 0x116 /* + phase */, DamageBonus = 0x118 /* + phase */, Hp = 0x11B, Movement = 0x11C,
        NpcShare = 0x85;

    /// <summary>VM cells: surprise mask ($6DCB: bit 0 party, bit 1 monsters), monster / party THAC0 modifiers
    /// ($6E70 / $6E71, low byte, signed), party movement modifier ($6E72).</summary>
    public const ushort SurpriseCell = 0x6DCB, MonsterThac0Cell = 0x6E70, PartyThac0Cell = 0x6E71, PartyMoveCell = 0x6E72;

    /// <summary>Effect trigger classes the fight passes to 0x2B1E4 (call sites cited in the spec).</summary>
    public const int TriggerDamageAttacker = 4, TriggerDamageDefender = 5, TriggerTurnStart = 7, TriggerAttackRollAttacker = 0x0A,
        TriggerDefenderAc = 0x0B, TriggerHpZero = 0x0D, TriggerActionStart = 0x0F, TriggerAttackRollDefender = 0x10,
        TriggerRate = 0x12, TriggerRoundEnd = 0x13;

    /// <summary>Effect ids whose presence makes a creature helpless (DS:286C, START 0x1713). [?] which spells they are.</summary>
    public static readonly byte[] HelplessEffects = [0x33, 0x34, 0x35, 0x1F];
    /// <summary>Invisibility (0x2BEBC removes every instance when the creature attacks).</summary>
    public const byte InvisibilityEffect = 0x19;
    /// <summary>Effects removed when a creature drops (0x2BEF4, list DS:0C14: the first instance of each).</summary>
    public static readonly byte[] DroppedEffects = [7, 11, 30, 31, 32, 51, 52, 53, 54, 58, 59, 95, 98, 137, 74, 75];

    // ---- Equipment (START 0x1758) --------------------------------------------------------------------------------

    /// <summary>Equipment slots (creature +0xCC, 13 far pointers): 0 weapon, 1 off hand, 2 body armour, …, 9/10 rings, 11 arrows, 12 bolts.</summary>
    public const int SlotWeapon = 0, SlotOffHand = 1, SlotBodyArmour = 2, SlotArrows = 11, SlotBolts = 12;

    /// <summary>
    /// The 13 equipment pointers the stat recalculation rebuilds (START 0x1758–0x18D7): for each readied item (0x34 ≠ 0)
    /// in list order, base slot 0–8 → that slot (a later item wins); base slot 9 → ring 1 when empty, else ring 2 when
    /// that is empty; then, whatever its slot, type 0x49 → arrows and type 0x1C → bolts.
    /// </summary>
    public static byte[]?[] Equipment(EclCharacter c, BaseItemTable items)
    {
        var slots = new byte[]?[13];
        foreach (var it in c.Items)
        {
            if (it[CombatRules.ItemEquipped] == 0)
                continue;
            byte s = items[it[0x2E], BaseItemTable.Slot];
            if (s <= 8)
                slots[s] = it;
            else if (s == 9)
            {
                if (slots[9] is null)
                    slots[9] = it;
                else if (slots[10] is null)
                    slots[10] = it;
            }
            if (it[0x2E] == 0x49)
                slots[SlotArrows] = it;
            if (it[0x2E] == 0x1C)
                slots[SlotBolts] = it;
        }
        return slots;
    }

    /// <summary>START 0x39A3: a weapon is readied and its base range (byte 12) is above 1.</summary>
    public static bool HasRangedWeapon(EclCharacter c, BaseItemTable items) =>
        Equipment(c, items)[SlotWeapon] is { } w && items[w[0x2E], BaseItemTable.Range] > 1;

    /// <summary>START 0x39E6: a ranged weapon whose flags have both bit 2 and bit 4 (0x14).</summary>
    public static bool HasThrowableWeapon(EclCharacter c, BaseItemTable items) =>
        HasRangedWeapon(c, items) && (items[Equipment(c, items)[SlotWeapon]![0x2E], BaseItemTable.Flags] & 0x14) == 0x14;

    /// <summary>
    /// START 0x3A2B: the item a ranged attack uses up. Weapon flags bit 4 (thrown) → the weapon; then, when bit 3 is
    /// set, bit 0 → the arrows slot, and bit 7 → the bolts slot (each replaces the previous pick, even with nil). True
    /// when an item was found or the flags are exactly 0x0A.
    /// </summary>
    public static bool FindRangedAmmo(EclCharacter c, BaseItemTable items, out byte[]? ammo)
    {
        var eq = Equipment(c, items);
        ammo = null;
        byte flags = 0;
        if (eq[SlotWeapon] is { } w)
        {
            flags = items[w[0x2E], BaseItemTable.Flags];
            if ((flags & 0x10) != 0)
                ammo = w;
            if ((flags & 8) != 0)
            {
                if ((flags & 1) != 0)
                    ammo = eq[SlotArrows];
                if ((flags & 0x80) != 0)
                    ammo = eq[SlotBolts];
            }
        }
        return ammo is not null || flags == 0x0A;
    }

    // ---- Start of round (0x127FB) --------------------------------------------------------------------------------

    /// <summary>START 0x1D08: initiative adjustment by Dexterity (record 0x13): 0–2 → −4, 3–5 → Dex − 6, 16–18 → Dex − 15,
    /// 19–20 → 3, 21–23 → 4, 24–25 → 5, else 0.</summary>
    public static sbyte DexAdjust(byte dex) => dex switch
    {
        <= 2 => -4,
        <= 5 => (sbyte)(dex - 6),
        >= 16 and <= 18 => (sbyte)(dex - 15),
        19 or 20 => 3,
        >= 21 and <= 23 => 4,
        24 or 25 => 5,
        _ => 0,
    };

    /// <summary>0x1364D: whole attacks from a rate in halves: + 1 when the round counter DS:6DE3 is odd (byte add), then ÷ 2.</summary>
    public static byte AttacksFromRate(byte rateX2, byte roundCounter) =>
        (byte)((byte)(rateX2 + (roundCounter & 1)) >> 1);

    /// <summary>
    /// 0x1291E: movement for the round, in half squares. Record 0x11C, plus the low byte of VM $6E72 on the party side
    /// (hostile 0); outside 1–96 it becomes 1; × 2 into DS:681A with DS:681D = 1, the trigger-0x12 effects, and the
    /// result is DS:681A.
    /// </summary>
    public static byte CreatureMovement(EclCharacter c, ushort partyMoveModifier, FightState fight, IFightEffects effects)
    {
        byte m = c[Movement];
        if (c[Hostile] == 0)
            m = (byte)(m + partyMoveModifier);
        fight.CalculatingMovement = true;
        if (m < 1 || m > 0x60)
            m = 1;
        fight.RateX2 = (byte)(m * 2);
        effects.Trigger(TriggerRate, c, fight);
        fight.CalculatingMovement = false;
        return fight.RateX2;
    }

    /// <summary>
    /// 0x1351E: attacks left in phase 1 (record 0x113). It first sets 0x113 = the base rate 0xA1 (halves). With a ranged
    /// weapon and ammunition (START 0x3A2B) the rate is the weapon's rate of fire (base byte 5, at least 2), else 0x113;
    /// the trigger-0x12 effects; whole attacks for the round (<see cref="AttacksFromRate"/>), at most the ammunition's
    /// count (byte 0x39, at least 1). 0x113 takes that value when the creature has not attacked this round, or it is
    /// below the value 0x113 had on entry, or (attacked, not ranged and below twice that value); otherwise 0x113 keeps
    /// the base rate written first.
    /// </summary>
    public static void RecalcAttacksLeft(EclCharacter c, CombatRecord rec, BaseItemTable items, FightState fight, IFightEffects effects)
    {
        bool ranged = false;
        byte previous = c[0x113];
        c[0x113] = c[BaseAttacksPrimary];
        byte[]? ammo = null;
        if (HasRangedWeapon(c, items) && FindRangedAmmo(c, items, out ammo))
        {
            ranged = true;
            byte rate = items[Equipment(c, items)[SlotWeapon]![0x2E], BaseItemTable.RateOfFire];
            fight.RateX2 = rate < 2 ? (byte)2 : rate;
        }
        else
            fight.RateX2 = c[0x113];
        fight.CalculatingMovement = false;
        effects.Trigger(TriggerRate, c, fight);
        byte attacks = AttacksFromRate(fight.RateX2, fight.RoundCounter);
        if (ranged && ammo is not null)
        {
            byte limit = Math.Max((byte)1, ammo[0x39]);
            if (limit < attacks)
                attacks = limit;
        }
        bool made = rec[CombatRecord.MadeAttack] != 0;
        if (!made || attacks < previous || (attacks < previous * 2 && !ranged))
            c[0x113] = attacks;
    }

    /// <summary>
    /// The start of a creature's round (GAME.OVR 0x127FB), run for every creature in list order before the first pick.
    /// Combat record: spell 0, cast allowed 1, item use allowed 1, attacked 0, phase 2; attacks left in phase 1
    /// (<see cref="RecalcAttacksLeft"/>); phase 2 = whole attacks from 0xA2 (after the trigger-0x12 effects); sweep
    /// budget = 0x6B. In play (0x10D ≠ 0): initiative = 1d6 + <see cref="DexAdjust"/> (signed byte), at least 1; − 6 when
    /// (hostile + 1) AND the surprise mask $6DCB has a bit set; outside 0–20 it becomes 0. Out of play: 0. Then the
    /// movement (<see cref="CreatureMovement"/>) into +6. One draw (the 1d6) per creature in play.
    /// </summary>
    public static void BeginRound(EclCharacter c, CombatRecord rec, FightState fight, BaseItemTable items, EclMemory memory,
        Func<ushort, ushort> random, IFightEffects effects)
    {
        rec[CombatRecord.SpellCast] = 0;
        rec[CombatRecord.CastAllowed] = 1;
        rec[CombatRecord.ItemUseAllowed] = 1;
        rec[CombatRecord.MadeAttack] = 0;
        rec[CombatRecord.AttackPhase] = 2;
        RecalcAttacksLeft(c, rec, items, fight, effects);
        fight.RateX2 = c[BaseAttacksSecondary];
        fight.CalculatingMovement = false;
        effects.Trigger(TriggerRate, c, fight);
        c[0x114] = AttacksFromRate(fight.RateX2, fight.RoundCounter);
        rec[CombatRecord.SweepBudget] = c[SweepLevel];
        if (c[InPlay] != 0)
        {
            int adj = DexAdjust(c[Dex]);
            sbyte init = (sbyte)(byte)(Dice(random, 1, 6) + adj);
            if (init < 1)
                init = 1;
            int mask = ((sbyte)c[Hostile] + 1) & memory.Read(SurpriseCell);
            if ((byte)mask != 0)
                init = (sbyte)(init - 6);
            if (init < 0 || init > 20)
                init = 0;
            rec.InitiativeValue = init;
        }
        else
            rec.InitiativeValue = 0;
        rec[CombatRecord.Movement] = CreatureMovement(c, memory.Read(PartyMoveCell), fight, effects);
    }

    /// <summary>The dice routine 0x2BCCD: a byte sum of <paramref name="count"/> × (Random(sides) + 1).</summary>
    public static byte Dice(Func<ushort, ushort> random, byte count, byte sides)
    {
        byte total = 0;
        for (int i = 0; i < count; i++)
            total = (byte)(total + random(sides) + 1);
        return total;
    }

    // ---- Attacks ---------------------------------------------------------------------------------------------------

    /// <summary>START 0x1713: the creature carries one of the <see cref="HelplessEffects"/>.</summary>
    public static bool IsHelpless(EclCharacter c) => HelplessEffects.Any(c.HasEffect);

    /// <summary>
    /// The attack roll (0x2BBA1). Removes the attacker's invisibility, every instance (0x2BEBC); 1d20 into DS:6822 (signed); a roll of 1 or
    /// less misses; 20 becomes 100; trigger 0x0A (attacker) and 0x10 (defender); then a hit when roll ≥ 0 and roll +
    /// THAC0 byte 0x110 + modifier ≥ <paramref name="defense"/>, as 16-bit signed sums. The modifier is the low byte of
    /// VM $6E71 for the party side (hostile 0), $6E70 for the other, signed. With both bytes in the 60 − x encoding
    /// this is "roll ≥ THAC0 − AC − modifier".
    /// </summary>
    public static bool AttackRoll(EclCharacter attacker, EclCharacter defender, byte defense, FightState fight, EclMemory memory,
        Func<ushort, ushort> random, IFightEffects effects)
    {
        RemoveInvisibility(attacker, effects);
        fight.AttackRoll = (sbyte)Dice(random, 1, 20);
        if (fight.AttackRoll <= 1)
            return false;
        if (fight.AttackRoll == 20)
            fight.AttackRoll = 100;
        effects.Trigger(TriggerAttackRollAttacker, attacker, fight);
        effects.Trigger(TriggerAttackRollDefender, defender, fight);
        int mod = (sbyte)(byte)memory.Read(attacker[Hostile] == 0 ? PartyThac0Cell : MonsterThac0Cell);
        return fight.AttackRoll >= 0 && fight.AttackRoll + attacker[Thac0] + mod >= defense;
    }

    /// <summary>
    /// The attack roll without an attacker (0x2BB39): 1d20, ≤ 1 misses, 20 → 100, trigger 0x10 on the defender, hit when
    /// roll ≥ 0 and roll + <paramref name="thac0"/> &gt; the defender's AC byte 0x111 (strictly greater).
    /// </summary>
    public static bool AttackRollNoAttacker(EclCharacter defender, byte thac0, FightState fight, Func<ushort, ushort> random,
        IFightEffects effects)
    {
        fight.AttackRoll = (sbyte)Dice(random, 1, 20);
        if (fight.AttackRoll <= 1)
            return false;
        if (fight.AttackRoll == 20)
            fight.AttackRoll = 100;
        effects.Trigger(TriggerAttackRollDefender, defender, fight);
        return fight.AttackRoll >= 0 && fight.AttackRoll + thac0 > defender[Ac];
    }

    /// <summary>
    /// 0x14D6F: a backstab. The attacker has thief levels (0x9C &gt; 0, signed); its off hand is empty or holds type 0x32;
    /// its weapon slot is empty or holds type 7, 8 or 0x23–0x25; the defender has been attacked more than once this
    /// turn (+0x0F &gt; 1) and the attacker stands in the defender's facing direction (0x14E38 = defender +9).
    /// </summary>
    public static bool IsBackstab(EclCharacter attacker, EclCharacter defender, CombatRecord defRec, BaseItemTable items,
        IFightGeometry geometry)
    {
        var eq = Equipment(attacker, items);
        bool weaponOk = false;
        if ((sbyte)attacker[ThiefLevel] > 0 && (eq[SlotOffHand] is null || eq[SlotOffHand]![0x2E] == 0x32))
        {
            var w = eq[SlotWeapon];
            weaponOk = w is null || w[0x2E] is 7 or 8 or (> 0x22 and < 0x26);
        }
        return weaponOk && defRec[CombatRecord.FlankCount] > 1 &&
               geometry.DirectionToward(attacker, defender) == defRec[CombatRecord.Facing];
    }

    /// <summary>
    /// 0x14319: the range penalty on the defense byte. Distance d (START 0x30FB); band = (weapon range − 1) div 3 with a
    /// ranged weapon, else d. If d − band &gt; 0: d −= band, defense + 2; again if d − band &gt; 0: defense + 3 (byte adds).
    /// [?] A ranged weapon with range 0 would divide 0xFFFF:0xFFFF by 3 (a divide overflow); none is in the table.
    /// </summary>
    public static byte RangePenalty(EclCharacter attacker, EclCharacter defender, byte defense, BaseItemTable items, IFightGeometry geometry)
    {
        sbyte d = (sbyte)geometry.Distance(attacker, defender);
        sbyte band = HasRangedWeapon(attacker, items)
            ? (sbyte)((items[Equipment(attacker, items)[SlotWeapon]![0x2E], BaseItemTable.Range] - 1) / 3)
            : d;
        if (d - band > 0)
        {
            d = (sbyte)(d - band);
            defense = (byte)(defense + 2);
        }
        if (d - band > 0)
            defense = (byte)(defense + 3);
        return defense;
    }

    /// <summary>
    /// 0x1298C: damage of one hit in <paramref name="phase"/> (1 or 2): dice 0x114+phase of sides 0x116+phase (0x2BD18,
    /// count into DS:681C) plus the signed bonus 0x118+phase, kept as a byte — a negative total wraps (the program's
    /// "below 0" test is unsigned and never true). A backstab multiplies by (thief level ÷ 4 + 2), low byte. Then DS:6819
    /// = 0 and the triggers 4 (attacker) and 5 (defender).
    /// </summary>
    public static byte WeaponDamage(EclCharacter attacker, EclCharacter defender, int phase, bool backstab, FightState fight,
        Func<ushort, ushort> random, IFightEffects effects)
    {
        fight.DamageDice = attacker[DamageDice + phase];
        byte roll = Dice(random, attacker[DamageDice + phase], attacker[DamageSides + phase]);
        fight.PendingDamage = (byte)(roll + (sbyte)attacker[DamageBonus + phase]);
        if (backstab)
            fight.PendingDamage = (byte)(fight.PendingDamage * (((ushort)(short)(sbyte)attacker[ThiefLevel] >> 2) + 2));
        fight.DamageFlags = 0;
        effects.Trigger(TriggerDamageAttacker, attacker, fight);
        effects.Trigger(TriggerDamageDefender, defender, fight);
        return fight.PendingDamage;
    }

    /// <summary>
    /// START 0x2DC8: <paramref name="damage"/> points to a creature. Over = damage − HP when it exceeds the HP, else
    /// remain = HP − damage. Over &gt; 9, or remain 0 on an animated creature (status 1): dead (6). Else over &gt; 0:
    /// dying (5), and in a fight (DS:49F3 = 5) combat record +0x0E = over. Else remain 0: unconscious (4). A status other
    /// than okay/animated: in play 0, HP 0, and in a fight the team count of its side − 1 and initiative 0. Else HP = remain.
    /// </summary>
    public static void InflictDamage(EclCharacter c, byte damage, FightState? fight)
    {
        byte over = 0, remain = 0;
        if (c[Hp] >= damage)
            remain = (byte)(c[Hp] - damage);
        else
            over = (byte)(damage - c[Hp]);
        if (over > 9 || (remain == 0 && c[Status] == (byte)CreatureStatus.Animated))
            c[Status] = (byte)CreatureStatus.Dead;
        else if (over > 0)
        {
            c[Status] = (byte)CreatureStatus.Dying;
            if (fight is not null)
                fight.Record(c)[CombatRecord.Dying] = over;
        }
        else if (remain == 0)
            c[Status] = (byte)CreatureStatus.Unconscious;
        if (c[Status] is not ((byte)CreatureStatus.Okay or (byte)CreatureStatus.Animated))
        {
            c[InPlay] = 0;
            c[Hp] = 0;
            if (fight is not null)
            {
                int side = (sbyte)c[Hostile];
                if (side is 0 or 1)
                    fight.TeamCount[side]--;
                fight.Record(c).InitiativeValue = 0;
            }
        }
        else
            c[Hp] = remain;
    }

    /// <summary>START 0x31D7: the creature's actions end: initiative, spell, guarding and movement 0.</summary>
    public static void EndActions(CombatRecord rec)
    {
        rec.InitiativeValue = 0;
        rec[CombatRecord.SpellCast] = 0;
        rec[CombatRecord.Guarding] = 0;
        rec[CombatRecord.Movement] = 0;
    }

    /// <summary>
    /// The damage part of 0x12AF4 (the rest is the message): a hit inflicts the damage; damage &gt; 0 clears the
    /// defender's cast allowed (+1) and loses a spell it was casting: START 0x202B (0x12D2C) removes the first memorised
    /// copy of that spell from the <b>current</b> character (DS:5D92, the attacker during an attack), then +0 → 0. A
    /// defender now out of play loses its <see cref="DroppedEffects"/> (0x2BEF4) and runs trigger 0x0D; the map removal
    /// (0x3154B) is the caller's (<see cref="Fight.MakeAttack"/>).
    /// </summary>
    public static void ApplyAttackResult(EclCharacter defender, byte damage, bool hit, FightState fight, IFightEffects effects)
    {
        if (hit)
            InflictDamage(defender, damage, fight);
        var rec = fight.Record(defender);
        if (damage > 0)
        {
            rec[CombatRecord.CastAllowed] = 0;
            if (rec[CombatRecord.SpellCast] > 0)
            {
                if (fight.Current is { } cur)
                    FightSpells.RemoveSpell(cur, rec[CombatRecord.SpellCast]);
                rec[CombatRecord.SpellCast] = 0;
            }
        }
        if (defender[InPlay] == 0)
        {
            foreach (var id in DroppedEffects)
                RemoveFirstEffect(defender, id, effects);
            effects.Trigger(TriggerHpZero, defender, fight);
        }
    }

    /// <summary>0x2BEBC: while the creature has an invisibility effect (0x19, START 0x2199), 0x2AF10 removes it.</summary>
    public static void RemoveInvisibility(EclCharacter c, IFightEffects effects)
    {
        while (c.Effects.Any(e => e.Length > 0 && e[0] == InvisibilityEffect))
            RemoveFirstEffect(c, InvisibilityEffect, effects);
    }

    /// <summary>0x2AF10 with no node: the first effect with this id leaves the list; its handler runs with "removed".</summary>
    public static void RemoveFirstEffect(EclCharacter c, byte id, IFightEffects effects)
    {
        int i = c.Effects.FindIndex(e => e.Length > 0 && e[0] == id);
        if (i < 0)
            return;
        var e = c.Effects[i];
        c.Effects.RemoveAt(i);
        effects.Removed(c, e);
    }

    /// <summary>What one call of the attack routine did.</summary>
    public readonly record struct AttackOutcome(bool AnyHit, bool DefenderDown, bool ActionsDone, int Hits, int Attacks);

    /// <summary>
    /// The attack routine 0x13C01 (attacker on defender). Counters DS:6823/6824 and DS:6E2D/6E2E and DS:6818 cleared;
    /// attacker +8 = 1.
    /// <list type="bullet">
    /// <item>Helpless defender (START 0x1713): the phase steps down while it has no attacks left (no lower bound); one
    /// attack is counted; the defender takes HP + 5 (byte), with the slay-helpless message; invisibility goes; both phases' attacks
    /// left = 0; actions done. No draw.</item>
    /// <item>Otherwise: with a weapon and a large defender (0x6C &gt; 0x80, or 0x6C AND 7 &gt; 1) the phase-1 damage dice
    /// become the weapon's large dice and the bonus +0x119 changes by large − small bonus. Trigger 0x0B on the defender
    /// (after its stat recalculation START 0x1758 at 0x13D84, which the effects' <see cref="EffectHandlers.Recalculate"/>
    /// runs). Defense: backstab → rear AC 0x112 − 2; else AC 0x111, or the
    /// rear AC when attacked from behind (argument, or +0x0F &gt; 1, the attacker in the defender's facing direction and
    /// +0x12 &gt; 4); then the range penalty. From the current phase (+4) down to 1, while the phase has attacks left
    /// and the defender is up: one attack (−1 left, phase stored, counted); a hit when the attack roll hits, or (after a
    /// miss) the defender is helpless; a hit counts, rolls damage and applies it, and runs trigger phase + 1 on the
    /// attacker while the defender is still in play. No hit at all: the miss message. Actions done when neither phase
    /// has attacks left; sweep budget +5 = 0; done → <see cref="EndActions"/>.</item>
    /// </list>
    /// </summary>
    public static AttackOutcome RollAttacks(EclCharacter att, EclCharacter def, bool fromBehind, FightState fight, BaseItemTable items,
        EclMemory memory, Func<ushort, ushort> random, IFightEffects effects, IFightGeometry geometry)
    {
        var aRec = fight.Record(att);
        var dRec = fight.Record(def);
        fight.HitsMade[1] = fight.HitsMade[2] = 0;
        fight.AttacksMade[1] = fight.AttacksMade[2] = 0;
        bool anyHit = false, down = false, done;
        fight.PendingDamage = 0;
        aRec[CombatRecord.MadeAttack] = 1;
        int hits = 0, attacks = 0;
        if (IsHelpless(def))
        {
            while (aRec[CombatRecord.AttackPhase] > 0 && att[AttacksLeft + aRec[CombatRecord.AttackPhase]] == 0)
                aRec[CombatRecord.AttackPhase]--; // [?] the program has no lower bound here
            fight.AttacksMade[Math.Clamp((int)aRec[CombatRecord.AttackPhase], 0, 2)]++;
            attacks++;
            hits++;
            ApplyAttackResult(def, (byte)(def[Hp] + 5), true, fight, effects);
            RemoveInvisibility(att, effects);
            att[0x113] = 0;
            att[0x114] = 0;
            anyHit = true;
            done = true;
        }
        else
        {
            if (Equipment(att, items)[SlotWeapon] is { } w && (def[Size] > 0x80 || (def[Size] & 7) > 1))
            {
                int t = w[0x2E];
                att[0x115] = items[t, BaseItemTable.LargeDice];
                att[0x117] = items[t, BaseItemTable.LargeSides];
                att[0x119] = (byte)((sbyte)att[0x119] - (sbyte)items[t, BaseItemTable.SmallBonus] + (sbyte)items[t, BaseItemTable.LargeBonus]);
            }
            effects.Trigger(TriggerDefenderAc, def, fight);
            bool behind = fromBehind;
            byte defense;
            bool backstab = IsBackstab(att, def, dRec, items, geometry);
            if (backstab)
                defense = (byte)(def[AcRear] - 2);
            else
            {
                if (dRec[CombatRecord.FlankCount] > 1 && geometry.DirectionToward(att, def) == dRec[CombatRecord.Facing] &&
                    dRec[CombatRecord.FlankSpread] > 4)
                    behind = true;
                defense = def[behind ? AcRear : Ac];
            }
            defense = RangePenalty(att, def, defense, items, geometry);
            backstab = IsBackstab(att, def, dRec, items, geometry);
            for (int phase = aRec[CombatRecord.AttackPhase]; phase >= 1; phase--)
            {
                while (att[AttacksLeft + phase] > 0 && !down)
                {
                    att[AttacksLeft + phase]--;
                    aRec[CombatRecord.AttackPhase] = (byte)phase;
                    fight.AttacksMade[phase]++;
                    attacks++;
                    if (AttackRoll(att, def, defense, fight, memory, random, effects) || IsHelpless(def))
                    {
                        fight.HitsMade[phase]++;
                        hits++;
                        anyHit = true;
                        byte dmg = WeaponDamage(att, def, phase, backstab, fight, random, effects);
                        ApplyAttackResult(def, dmg, true, fight, effects);
                        if (def[InPlay] != 0)
                            effects.Trigger(phase + 1, att, fight);
                        if (def[InPlay] == 0)
                            down = true;
                    }
                }
            }
            if (!anyHit)
                ApplyAttackResult(def, 0, false, fight, effects);
            done = att[0x113] == 0 && att[0x114] == 0;
            aRec[CombatRecord.SweepBudget] = 0;
        }
        if (done)
            EndActions(aRec);
        return new AttackOutcome(anyHit, def[InPlay] == 0, done, hits, attacks);
    }

    /// <summary>
    /// The thrown-weapon part of MakeAttack (0x141DF–0x14280), for an attack made with <paramref name="weapon"/> (the
    /// ammunition): its count 0x39, when above 0, drops by the phase-1 attacks made (DS:6E2D, byte). At count 0 the
    /// item leaves the attacker (START 0x20A9); when the attacker has a throwable weapon (START 0x39E6) and the item's
    /// byte 0x3E is not 0x89, a copy with readied = 0 becomes the head of the treasure list (DS:6810) and DS:5D9A points
    /// to it (<see cref="EncounterState.LastThrownItem"/>).
    /// </summary>
    public static void DropSpentWeapon(EclCharacter attacker, byte[] weapon, FightState fight, EncounterState state, BaseItemTable items)
    {
        if (weapon[0x39] > 0)
            weapon[0x39] = (byte)(weapon[0x39] - fight.AttacksMade[1]);
        if (weapon[0x39] != 0)
            return;
        if (HasThrowableWeapon(attacker, items) && weapon[0x3E] != 0x89)
        {
            var copy = (byte[])weapon.Clone();
            copy[CombatRules.ItemEquipped] = 0;
            state.TreasureItems.Insert(0, copy);
            attacker.Items.Remove(weapon);
            state.LastThrownItem = copy;
        }
        else
            attacker.Items.Remove(weapon);
    }

    // ---- Teams, end of round ---------------------------------------------------------------------------------------

    /// <summary>START 0x2F7B: creatures in play per side.</summary>
    public static void CountTeams(IEnumerable<EclCharacter> list, FightState fight)
    {
        fight.TeamCount[0] = fight.TeamCount[1] = 0;
        foreach (var c in list)
            if (c[InPlay] != 0 && (sbyte)c[Hostile] is 0 or 1)
                fight.TeamCount[c[Hostile]]++;
    }

    /// <summary>
    /// 0x15074: the enemy side's hit points as a percentage in steps of 5: over hostile creatures (0x10E = 1), cur = Σ HP
    /// (0x11B) of those in play, max = Σ maximum HP (0x32) of all; when max &gt; 0, DS:6E2F = low byte of
    /// ((20 · cur, 16 bits) ÷ max) × 5.
    /// </summary>
    public static void UpdateHostileHpPercent(IEnumerable<EclCharacter> list, FightState fight)
    {
        ushort cur = 0, max = 0;
        foreach (var c in list)
        {
            if (c[Hostile] != 1)
                continue;
            if (c[InPlay] != 0)
                cur = (ushort)(cur + c[Hp]);
            max = (ushort)(max + c[HpMax]);
        }
        if (max > 0)
            fight.HostileHpPercent = (byte)((ushort)(20 * cur) / max * 5);
    }

    /// <summary>
    /// The per-creature part of the end of round (0xA087): trigger 0x13 effects and the terrain effects (0x2B9D1 [?]),
    /// then a dying creature (status 5) adds 1 to combat record +0x0E and is dead (6) when that exceeds 9.
    /// </summary>
    public static void EndOfRoundCreature(EclCharacter c, FightState fight, IFightEffects effects)
    {
        effects.Trigger(TriggerRoundEnd, c, fight);
        effects.Terrain(c, fight);
        if (c[Status] == (byte)CreatureStatus.Dying)
        {
            var rec = fight.Record(c);
            rec[CombatRecord.Dying]++;
            if (rec[CombatRecord.Dying] > 9)
                c[Status] = (byte)CreatureStatus.Dead;
        }
    }

    // ---- After the fight: NPC treasure share (0x6866) ------------------------------------------------------------

    /// <summary>
    /// The NPCs' share of the coins (GAME.OVR 0x6866), run after the monsters are removed. Over the list: an NPC
    /// (0x84 &gt; 0x7F) with status okay adds (0x85 AND 7) to the NPC shares and to the total; every other creature adds 1
    /// to the total (byte counters). With NPC shares &gt; 0, for each of the seven pool totals that is above 0 (signed):
    /// cut = low byte of (total ÷ shares) (signed long division, START 0xB9F6), and the pool loses cut × NPC shares,
    /// a 16-bit product sign-extended. Returns the NPCs named in the message (okay NPCs with 0x85 ≠ 0) when any coin
    /// was taken, else an empty list (no message).
    /// </summary>
    public static List<EclCharacter> NpcTreasureShare(IReadOnlyList<EclCharacter> list, uint[] pool)
    {
        byte npcShares = 0, total = 0;
        foreach (var c in list)
        {
            if (c[CombatRules.Npc] > 0x7F && c[Status] == 0)
            {
                npcShares = (byte)(npcShares + (c[NpcShare] & 7));
                total = (byte)(total + (c[NpcShare] & 7));
            }
            else
                total++;
        }
        bool took = false;
        if (npcShares > 0)
        {
            for (int i = 0; i < 7; i++)
            {
                int v = (int)pool[i];
                if (v <= 0)
                    continue;
                byte cut = (byte)(v / total);
                int taken = (short)(ushort)(cut * npcShares);
                pool[i] = (uint)(v - taken);
                took = true;
            }
        }
        if (!took)
            return [];
        return list.Where(c => c[CombatRules.Npc] > 0x7F && c[Status] == 0 && c[NpcShare] > 0).ToList();
    }
}
