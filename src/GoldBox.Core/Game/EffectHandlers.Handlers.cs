using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// The effect handlers 1..72 (table DS:6828; file offsets in the overlay unit that starts at GAME.OVR 0xEC5B). The
// handler gets (creature, its effect record or the aura carrier's, removed). DS cells: 6816 save roll, 6817 effect
// being added, 6818 damage, 6819 element mask, 681A rate, 681B spell, 681C damage dice, 681D movement flag, 681E
// no-target, 681F lock, 6820 multi-target, 6822 attack roll, 6823 phase-1 hits, 6825 morale, 6826 other creature.
public sealed partial class EffectHandlers
{
    /// <summary>Ids that share the empty handler 0x11DF6 (and 16, 54, 55: empty handlers of their own).</summary>
    public static readonly byte[] EmptyHandlers = [5, 13, 16, 19, 24, 27, 54, 55, 60, 63, 92, 127];

    /// <summary>
    /// The far call 0x2AEEA: handler <paramref name="id"/> with the creature, the record and the removal flag. Ids
    /// whose handler is not modelled are reported in <see cref="Diagnostics"/> and do nothing [?].
    /// </summary>
    public void CallHandler(byte id, EclCharacter c, byte[]? node, bool removed)
    {
        if (EmptyHandlers.Contains(id))
            return;
        var rec = F.Record(c);
        byte param = node is { Length: > 3 } ? node[3] : (byte)0;
        switch (id)
        {
            case 1: // 0xED68 Bless: morale + 5, attack + 1
                F.Morale = (byte)(F.Morale + 5);
                F.AttackRoll++;
                return;
            case 2: // 0xED78 Curse: morale − 5 (not below 0), attack − 1
                F.Morale = F.Morale < 5 ? (byte)0 : (byte)(F.Morale - 5);
                F.AttackRoll--;
                return;
            case 3: // 0xED96: against the creature last attacked when it is undead (0x9F = 4): attack + 2, damage + 2
                Cells.Other = rec.Other;
                if (Cells.Other is { } u && u[CreatureType] == 4)
                {
                    F.AttackRoll += 2;
                    F.PendingDamage = (byte)(F.PendingDamage + 2);
                }
                return;
            case 6: // 0xEE1E: bonus by the target's type (0x0A → 1, 9 or 0x0C → 2, 4 → 3) to attack and damage; element 9
            {
                Cells.Other = rec.Other;
                byte b = Cells.Other?[CreatureType] switch { 0x0A => 1, 9 or 0x0C => 2, 4 => 3, _ => 0 };
                F.AttackRoll = (sbyte)(F.AttackRoll + b);
                F.PendingDamage = (byte)(F.PendingDamage + b);
                F.DamageFlags = 9;
                return;
            }
            case 8 or 45: // 0xEFD2 Protection from Evil: current character's alignment 2, 5 or 8 → save + 2, attack − 2
                if (Current is { } e && e[Alignment] is 2 or 5 or 8)
                {
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + 2);
                    F.AttackRoll -= 2;
                }
                return;
            case 9 or 46: // 0xF007 Protection from Good: alignment 0, 3 or 6
                if (Current is { } g && g[Alignment] is 0 or 3 or 6)
                {
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + 2);
                    F.AttackRoll -= 2;
                }
                return;
            case 10: // 0xF03C Resist Cold: element bit 1 → damage halved, save + 3
                if ((F.DamageFlags & 2) != 0)
                {
                    F.PendingDamage = (byte)(F.PendingDamage >> 1);
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + 3);
                }
                return;
            case 11: // 0xF05B Charm
                Charm(c, node!, removed, rec);
                return;
            case 14: // 0xF231 Friends: Charisma 0x15 = param
                c[Cha] = param;
                return;
            case 15: // 0xF246: re-adds itself for 10 minutes (lock permitting) and with HP > 1 deals 1 point, element 0
                if (AddEffectIfNoLock(c, 0x0F, param, 10) && c[Hp] > 1)
                {
                    F.DamageFlags = 0;
                    NonWeaponDamage(c, 1, 0, false);
                    Emit(c, EffectEvent.HandlerDamageListed); // 0xF28A: outside a fight the party list
                }
                return;
            case 17: // 0xF2A9 Shield: AC byte at least 0x39 (AC 3), save + 1, no damage from Magic Missile (spell 15)
                if (c[0x111] < 0x39)
                    c[0x111] = 0x39;
                Cells.SaveRoll++;
                if (Cells.Spell == 0x0F)
                    F.PendingDamage = 0;
                return;
            case 18: // 0xF2D4: target type 1 and size 1 named in the gnome list (4) → attack + 1 per match
                Cells.Other = rec.Other;
                if (Cells.Other is { } t18 && t18[CreatureType] == 1 && t18[Size] == 1)
                    for (int i = 0; i < 4; i++)
                        if (NameIs(t18, Names.GnomeFoes, i))
                            F.AttackRoll++;
                return;
            case 20: // 0xF33F Resist Fire: element bit 0 → damage halved, save + 3
                if ((F.DamageFlags & 1) != 0)
                {
                    F.PendingDamage = (byte)(F.PendingDamage >> 1);
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + 3);
                }
                return;
            case 21: // 0xF36A Silence: (message when item use was allowed) item use and casting not allowed
                if (rec[CombatRecord.ItemUseAllowed] != 0)
                    Emit(c, EffectEvent.Silenced);
                rec[CombatRecord.ItemUseAllowed] = 0;
                rec[CombatRecord.CastAllowed] = 0;
                return;
            case 22: // 0xF3D0: poisoned (0x37) → dies; then effect 0x0F is removed under the lock
                if (FindEffect(c, 0x37) is not null)
                    InflictStatus(c, 6, EffectEvent.PoisonDeath); // text CS:0764
                Cells.Lock = 1;
                RemoveEffect(c, 0x0F);
                Cells.Lock = 0;
                return;
            case 23: // 0xF43B Spiritual Hammer
                SpiritualHammer(c, removed);
                return;
            case 25: // 0xF573 Invisibility: no target unless the current character sees invisible (0x18); attack − 4
                if (Current is null || FindEffect(Current, 0x18) is null)
                    F.TargetBlocked = 1;
                F.AttackRoll -= 4;
                return;
            case 26: // 0xF5A2: like 18 with the dwarf list (8 slots)
                Cells.Other = rec.Other;
                if (Cells.Other is { } t26 && t26[CreatureType] == 1 && t26[Size] == 1)
                    for (int i = 0; i < 8; i++)
                        if (NameIs(t26, Names.DwarfFoes, i))
                            F.AttackRoll++;
                return;
            case 28: // 0xF61B Mirror Image
            {
                byte d = Dice(1, (byte)(param + 1));
                if (d > 1 && Cells.Spell > 0 && Cells.MultiTarget == 0)
                {
                    Nullify(0);
                    Emit(c, EffectEvent.LostImage);
                    node![3]--;
                    if (node[3] == 0)
                        RemoveEffect(c, 0x1C);
                }
                return;
            }
            case 29: // 0xF698: damage − damage ÷ 4
                F.PendingDamage = (byte)(F.PendingDamage - (F.PendingDamage >> 2));
                return;
            case 30: // 0xF6C0 coughing in a stinking cloud
                if (rec[CombatRecord.ItemUseAllowed] != 0)
                    Emit(c, EffectEvent.Coughing);
                rec[CombatRecord.ItemUseAllowed] = 0;
                rec[CombatRecord.CastAllowed] = 0;
                RecalculateStats(c);
                c[0x112] = c[0x112] > 0x34 ? (byte)(c[0x112] - 2) : (byte)0x32;
                c[0x111] = c[0x112];
                if (ReferenceEquals(c, Current))
                    Emit(c, EffectEvent.PanelRedrawn); // 0xF749–0xF761: START 0x1583 for DS:5D92
                return;
            case 31 or 51 or 52 or 53: // 0xFF9A helpless, snake charm, paralysis, sleep: actions end (START 0x31D7)
                FightRules.EndActions(rec);
                return;
            case 32: // 0xF776 Animate Dead
                node![4] = 0;
                if (Cells.Lock == 0)
                    InflictStatus(c, 6, EffectEvent.Collapses); // text CS:0B11
                c[Hostile] = (byte)(param >> 4);
                c[Auto] = 1;
                c[UndeadLevel] = 0;
                c[AttackLevel] = c[FighterLevel];
                c[MoveBase] = 0x0C;
                if (c[Npc] == AnimatedMember)
                    c[Npc] = 0;
                c[CreatureType] = 0;
                return;
            case 33: // 0xF807 Blind: attack − 4, AC bytes 0x111 and 0x112 − 4, save − 4
                F.AttackRoll -= 4;
                c[0x111] = (byte)(c[0x111] - 4);
                c[0x112] = (byte)(c[0x112] - 4);
                Cells.SaveRoll = (byte)(Cells.SaveRoll - 4);
                return;
            case 34: // 0xF82A disease: handlers 0x2B and 0x2C with the same arguments
                CallHandler(0x2B, c, node, removed);
                CallHandler(0x2C, c, node, removed);
                return;
            case 35: // 0xF861: save + 1, attack + 1
                Cells.SaveRoll++;
                F.AttackRoll++;
                return;
            case 36: // 0xF870 Bestow Curse: attack − 4, save − 4
                F.AttackRoll -= 4;
                Cells.SaveRoll = (byte)(Cells.SaveRoll - 4);
                return;
            case 37: // 0xF881 Blink: while the carrier's initiative is above 0, no target and attack roll −1
                if (rec.InitiativeValue > 0)
                {
                    F.TargetBlocked = 1;
                    F.AttackRoll = -1;
                }
                return;
            case 39: // 0xF8A6 Haste: first time (param bit 4 clear) param + 0x10, ages one year (word 0x30); rate × 2
                if ((param & 0x10) == 0)
                {
                    node![3] = (byte)(param + 0x10);
                    Emit(c, EffectEvent.Ages);
                    c.SetWord(Age, (ushort)(c.Word(Age) + 1));
                }
                F.RateX2 = (byte)(F.RateX2 * 2);
                return;
            case 41: // 0xFCE1 Protection from Normal Missiles: the attacker's missile (or weapon) with plus 0
            {
                var w = Current is { } a ? MissileOf(a) : null;
                if (w is not null && w[0x32] == 0)
                    DodgeMissile(c, 100);
                return;
            }
            case 42: // 0xFD1C Slow: rate ÷ 2
                F.RateX2 = (byte)(F.RateX2 >> 1);
                return;
            case 43: // 0xFD39: re-added for 60 minutes; Strength above 3 loses 1, else helpless (0x1F) when not yet
                if (AddEffectIfNoLock(c, 0x2B, param, 0x3C))
                {
                    if (c[Str] > 3)
                    {
                        Emit(c, EffectEvent.Weakens);
                        c[Str]--;
                    }
                    else if (FindEffect(c, 0x1F) is null)
                        AddEffect(c, 0x1F, 0, 0xFF, false);
                }
                return;
            case 44: // 0xFDC3: re-added for 10 minutes; HP above 1 loses 1 (element 0), else helpless when not yet
                if (AddEffectIfNoLock(c, 0x2C, param, 0x0A))
                {
                    if (c[Hp] > 1)
                    {
                        F.DamageFlags = 0;
                        NonWeaponDamage(c, 1, 0, false);
                        Emit(c, EffectEvent.HandlerDamageListed); // 0xFE0A: outside a fight the party list
                    }
                    else if (FindEffect(c, 0x1F) is null)
                        AddEffect(c, 0x1F, 0, 0xFF, false);
                }
                return;
            case 47: // 0xFE54: current character type 1 and size (0x6C AND 0x7F) 2; the carrier's last target named in the
                     // giant list (8 slots) → attack − 4 per match
                Cells.Other = rec.Other;
                if (Current is { } c47 && c47[CreatureType] == 1 && (c47[Size] & 0x7F) == 2 && Cells.Other is { } o47)
                    for (int i = 0; i < 8; i++)
                        if (NameIs(o47, Names.GiantFoes, i))
                            F.AttackRoll -= 4;
                return;
            case 48: // 0xFED1: current character type 1 named as one of the two → attack − 4
                if (Current is { } c48 && c48[CreatureType] == 1 &&
                    (NameIs(c48, Names.GnomeDefenseFoes, 0) || NameIs(c48, Names.GnomeDefenseFoes, 1)))
                    F.AttackRoll -= 4;
                return;
            case 49: // 0xFF0D Prayer: the carrier's side (param bit 4) gets handler 35, the other side attack − 1, save − 1
                if (c[Hostile] == (byte)((param & 0x10) >> 4))
                    CallHandler(35, c, node, removed);
                else
                {
                    F.AttackRoll--;
                    Cells.SaveRoll--;
                }
                return;
            case 50: // 0xFF56: re-added for 14400 minutes; then a 1-point heal past the rot (0x2C647 with bypass)
                if (AddEffectIfNoLock(c, 0x32, param, 0x3840) && RestoreHitPoints(c, 1, true))
                    Emit(c, EffectEvent.Healed); // START 0x2EEA: the healed message, then outside a fight the party list
                return;
            case 56: // 0xFFBE: invisibility (0x19) for 1 minute, param 12
                AddEffect(c, 0x19, 1, 0x0C, false);
                return;
            case 57: // 0xFFF4 mummy rot
                if (Cells.Lock != 0)
                    return;
                Emit(c, EffectEvent.Rots); // CS:1382, row 10, pause flag
                c[Cha] = c[Cha] > 2 ? (byte)(c[Cha] - 2) : (byte)0;
                if (c[Cha] < 3)
                    c[Cha] = 3;
                if (param >> 4 > 1)
                {
                    node![3] = (byte)(param - 0x10);
                    AddEffectIfNoLock(c, 0x39, node[3], 0xA8C0);
                }
                else
                    InflictStatus(c, 8, EffectEvent.RotsAway); // text CS:138A
                return;
            case 58: // 0x100AD: movement +6 = 0; during the movement count the rate is 0
                rec[CombatRecord.Movement] = 0;
                if (F.CalculatingMovement)
                    F.RateX2 = 0;
                return;
            case 59: // 0x100CD: adds regeneration 0x62 (permanent, param 255)
                AddEffect(c, 0x62, 0, 0xFF, false);
                return;
            case 61: // 0x100EB ring of fire resistance
                if ((F.DamageFlags & 1) != 0)
                {
                    byte n = F.DamageDice;
                    for (int i = 1; i <= n; i++)
                    {
                        F.PendingDamage = (byte)(F.PendingDamage - 2);
                        if (F.PendingDamage < F.DamageDice)
                            F.PendingDamage = F.DamageDice;
                    }
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + 4);
                    if ((F.DamageFlags & 8) == 0)
                        Nullify(0);
                }
                return;
            case 62: // 0x10146: re-added for 60 minutes, then a 1-point heal (0x2C647 with bypass)
                if (AddEffectIfNoLock(c, 0x3E, param, 0x3C) && RestoreHitPoints(c, 1, true))
                    Emit(c, EffectEvent.Healed); // START 0x2EEA: the healed message, then outside a fight the party list
                return;
            case 64: PoisonAttack(c, 0); return;            // 0x102B8
            case 65: PoisonAttack(c, 4); return;            // 0x102CC
            case 66: PoisonAttack(c, 2); return;            // 0x102E0
            case 70: PoisonAttack(c, unchecked((sbyte)0xFE)); return; // 0x10366
            case 67: // 0x102F4: paralysis for 2d8 minutes
                ParalysingHit(c, Dice(2, 8));
                return;
            case 68: // 0x10316: paralysis for 63 minutes unless the target's race byte 0x2E is 2 (elf)
                if (rec.Other is { } t68 && t68[Race] != 2)
                    ParalysingHit(c, 0x3F);
                return;
            case 69: // 0x10341: paralysis for 1d9 + 10 minutes (the −2 passed is not used by 0x10246)
                ParalysingHit(c, (ushort)(Dice(1, 9) + 10));
                return;
            case 71: // 0x1037A: no target, attack − 4
                F.TargetBlocked = 1;
                F.AttackRoll -= 4;
                return;
            case 72: // 0x1038B: 1d100 ≤ 95 → invisibility (0x19) for 1 minute, param 12
                if (Dice(1, 100) <= 0x5F)
                    AddEffect(c, 0x19, 1, 0x0C, false);
                return;
        }
        bool handled = false;
        CallMoreHandlers(id, c, node, removed, ref handled);
        if (!handled)
            Diag($"[?] effect handler {id} not modelled");
    }

    /// <summary>Handlers 73 and up, in their own file.</summary>
    partial void CallMoreHandlers(byte id, EclCharacter c, byte[]? node, bool removed, ref bool handled);

    /// <summary>START 0xBB90 on the creature's name (Pascal string at record 0) and a 16-byte table slot.</summary>
    private static bool NameIs(EclCharacter c, IReadOnlyList<byte[]> table, int i)
    {
        if (i >= table.Count)
            return false;
        var s = table[i];
        int n = s.Length > 0 ? s[0] : 0;
        if (c[0] != n)
            return false;
        for (int k = 1; k <= n; k++)
            if (k >= s.Length || k >= 0x11D || c[k] != s[k])
                return false;
        return true;
    }

    /// <summary>
    /// 0xF05B Charm. Removed: hostile = param bit 6; an NPC byte 0xB3 becomes 0. Else, the first time (param bit 5
    /// clear): param += 0x20 + hostile × 64 (byte), hostile = param bit 7, computer-controlled, NPC byte 0xB3 unless
    /// above 0x7F, no last target, teams counted again (START 0x2F7B). Then morale DS:6825 = 100.
    /// </summary>
    private void Charm(EclCharacter c, byte[] node, bool removed, CombatRecord rec)
    {
        if (removed)
        {
            c[Hostile] = (byte)((node[3] & 0x40) >> 6);
            if (c[Npc] == AnimatedMember)
                c[Npc] = 0;
            return;
        }
        if ((node[3] & 0x20) == 0)
        {
            node[3] = (byte)(node[3] + 0x20 + ((sbyte)c[Hostile] << 6));
            c[Hostile] = (byte)(node[3] >> 7);
            c[Auto] = 1;
            if (c[Npc] <= 0x7F)
                c[Npc] = AnimatedMember;
            rec.Other = null;
            FightRules.CountTeams(List(), F);
        }
        F.Morale = 100; // DS:6825, the value the morale check 0xBC97 tests after trigger 0x11
    }

    /// <summary>
    /// 0xF43B: the creature's first item of type 0x14 with word 0x31 = 0xF3 (the hammer). Removed: that item goes
    /// (START 0x20A9). Else, without it and with fewer than 16 items (0xC7): a new item (type 0x14, words 0x14 / 0xF3,
    /// plus 1, props 0x3D = 23, 0x3E = 137) is added (START 0x2197). Stats recalculated either way.
    /// </summary>
    private void SpiritualHammer(EclCharacter c, bool removed)
    {
        var item = c.Items.FirstOrDefault(i => i[0x2E] == 0x14 && i[0x31] == 0xF3);
        if (removed && item is not null)
            c.Items.Remove(item);
        if (!removed && item is null && c[0xC7] < 0x10)
        {
            var it = new byte[EclCharacter.ItemRecordSize];
            it[0x2E] = 0x14;
            it[0x30] = 0x14;
            it[0x31] = 0xF3;
            it[0x32] = 1;
            it[0x3D] = 0x17;
            it[0x3E] = 0x89;
            c.Items.Add(it); // START 0x2197: a copy appended at the list end; the count 0xC7 is left (s52, por_handler_oracle.py)
            Emit(c, EffectEvent.GainsItem);
        }
        ResetBaseline(c);
        RecalculateStats(c);
    }

    /// <summary>0xFC78: the missile a creature shoots (START 0x3A2B) or, without one, its weapon; null unarmed.</summary>
    private byte[]? MissileOf(EclCharacter a)
    {
        if (Items is null)
            return null;
        var eq = FightRules.Equipment(a, Items);
        if (eq[FightRules.SlotWeapon] is null)
            return null;
        bool found = FightRules.FindRangedAmmo(a, Items, out var ammo);
        return found && ammo is not null ? ammo : eq[FightRules.SlotWeapon];
    }

    /// <summary>
    /// 0xFC07: the current character has a weapon and stands more than 1 square away (START 0x30FB): on 1d100 ≤
    /// <paramref name="chance"/> the missile is dodged: damage 0, attack roll −1, phase-1 hits DS:6823 − 1.
    /// </summary>
    private void DodgeMissile(EclCharacter c, byte chance)
    {
        var a = Current;
        if (a is null || Items is null || FightRules.Equipment(a, Items)[FightRules.SlotWeapon] is null)
            return;
        if (Geometry is null)
        {
            Diag("[?] missile dodge needs the distance (IFightGeometry)");
            return;
        }
        if (Geometry.Distance(a, c) <= 1)
            return;
        if (Dice(1, 100) > chance)
            return;
        Emit(c, EffectEvent.MissileDodged);
        F.PendingDamage = 0;
        F.AttackRoll = -1;
        F.HitsMade[1]--;
    }

    /// <summary>
    /// 0x101A0: the creature's last target (+0x0A) saves vs. type 0 with <paramref name="modifier"/>; failing: the poisoned
    /// message (START 0x16EC row 10, then PauseOnly), poisoned (0x37, permanent, param 255; 0x2BD3C) and dead (0xECB3,
    /// status 6, its own text).
    /// </summary>
    private void PoisonAttack(EclCharacter c, sbyte modifier)
    {
        var other = F.Record(c).Other;
        Cells.Other = other;
        if (other is null || SavingThrow(other, 0, modifier))
            return;
        Emit(other, EffectEvent.Poisoned); // CS:152F, row 10, no pause flag, then PauseOnly
        AddEffect(other, 0x37, 0, 0xFF, false);
        InflictStatus(other, 6, EffectEvent.PoisonKilled); // text CS:153B
    }

    /// <summary>
    /// 0x10246: the creature's last target saves vs. type 0 (modifier 0); failing: (message) paralysed (0x34) for
    /// <paramref name="minutes"/>, param 12.
    /// </summary>
    private void ParalysingHit(EclCharacter c, ushort minutes)
    {
        var other = F.Record(c).Other;
        Cells.Other = other;
        if (other is null || SavingThrow(other, 0, 0))
            return;
        Emit(other, EffectEvent.ParalysisFailed);
        AddEffect(other, 0x34, minutes, 0x0C, false);
    }
}
