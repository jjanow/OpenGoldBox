using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// Effect handlers 4, 7, 12/38, 73..126 and the item codes 128..139 (table DS:6828; file offsets in the overlay unit
// that starts at GAME.OVR 0xEC5B). For the item codes the record passed is the 63-byte item (readied: apply, unreadied:
// removed). Handlers 40, 73–77, 81–86 and 121 are in EffectHandlers.Holds.cs.
public sealed partial class EffectHandlers
{
    /// <summary>Ids with no handler modelled (none since handler 88, whose DS:6828 entry the spell table builder writes
    /// at 0x2A7B9, was read).</summary>
    public static readonly byte[] UnmodelledHandlers = [];

    /// <summary>DS:6821: set by the item handlers 131 and 132 (read elsewhere [?]).</summary>
    public byte ItemFlag { get; set; }

    /// <summary>
    /// START 0x3262: the casting level of a spell for the current character, read by the magic-resistance handlers
    /// (0x11570). [?] Not modelled here; without it the level is taken as 0 and a diagnostic is noted.
    /// </summary>
    public Func<byte, byte>? CasterLevel { get; set; }

    /// <summary>
    /// Replaces <see cref="PutBackOnSquare"/> (the map test of 0x2C753 and Animate Dead) when set; for tests.
    /// </summary>
    public Func<EclCharacter, bool>? CanRevive { get; set; }

    /// <summary>
    /// 0x317E1(creature, its square 0x31321 / 0x31349, 1), the test 0x2C753 and Animate Dead (0x29101) make before a
    /// creature comes back: outside a fight (DS:49F3 ≠ 5) true; in a fight the combat map puts it on its square
    /// (<see cref="CombatMap.TryPlace"/>, moving: a party member's own bodies are cleared) and says whether the square
    /// takes it. <see cref="CanRevive"/> replaces it when set. Without a combat map in a fight: taken as free, with a
    /// diagnostic.
    /// </summary>
    public bool PutBackOnSquare(EclCharacter c)
    {
        if (CanRevive is { } f)
            return f(c);
        if (!InCombat)
            return true;
        if (Map is { } m)
        {
            var (x, y) = m.SlotPosition(m.Slot(c));
            return m.TryPlace(c, x, y, true, F);
        }
        Diag("[?] 0x317E1 needs the combat map; taken as free");
        return true;
    }

    partial void CallMoreHandlers(byte id, EclCharacter c, byte[]? node, bool removed, ref bool handled)
    {
        if (UnmodelledHandlers.Contains(id))
            return;
        handled = true;
        var rec = F.Record(c);
        byte param = node is { Length: > 3 } ? node[3] : (byte)0;
        switch (id)
        {
            case 4: // 0xEDDA: (message) training effect 7 for 43200 minutes, param 255, removal runs the handler
                Emit(c, EffectEvent.StartsTraining);
                AddEffect(c, 7, 0xA8C0, 0xFF, true);
                return;
            case 7: // 0xEE8E
                TrainingManual(c);
                return;
            case 12 or 38: // 0xF115 Enlarge / Strength
                StrengthEffect(c, node!);
                return;
            case 78: // 0x10634: back in play with its HP (0x2C753); failing that re-added for 1 minute (lock permitting)
                if (!Revive(c, c[Hp]))
                    AddEffectIfNoLock(c, 0x4E, param, 1);
                return;
            case 79: // 0x10674: element 9, the last target takes 2d10 (0x2BD18, dice count to DS:681C) non-weapon damage
                F.DamageFlags = 9;
                if (rec.Other is { } t79)
                    NonWeaponDamage(t79, DamageDiceRoll(2, 10), 0, false);
                else
                    DamageDiceRoll(2, 10);
                return;
            case 80: // 0x106A7: element 0x10, the last target takes 1d4 non-weapon damage
                F.DamageFlags = 0x10;
                if (rec.Other is { } t80)
                    NonWeaponDamage(t80, DamageDiceRoll(1, 4), 0, false);
                else
                    DamageDiceRoll(1, 4);
                return;
            case 87: // 0x11047: a last target without rot (0x32) gets rot for 14400 min (param 255) and 0x39 for
                     // 43200 min with param 1d6·16 + 15 (both removal runs the handler), message
            {
                var o = rec.Other;
                Cells.Other = o;
                if (o is null || FindEffect(o, 0x32) is not null)
                    return;
                AddEffect(o, 0x32, 0x3840, 0xFF, true);
                AddEffect(o, 0x39, 0xA8C0, (byte)(Dice(1, 6) * 16 + 0x0F), true);
                Emit(o, EffectEvent.Diseased); // 0x110E0: START 0x16EC(target, CS:23E0, row 10, pause 1)
                return;
            }
            case 89: // 0x110EB Displacement: round counter 0 and attack roll 0 (the fight start): param AND 0x0F; else
                     // the first attack of a fight on it (param bit 4 clear): attack roll −1 and the bit set
                if (F.RoundCounter == 0 && F.AttackRoll == 0)
                    node![3] = (byte)(param & 0x0F);
                else if ((param & 0x10) == 0)
                {
                    F.AttackRoll = -1;
                    node![3] = (byte)(param | 0x10);
                }
                return;
            case 90: // 0x11134: save type 0 → save + Constitution bonus
                if (Cells.SaveType == 0)
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + ConSaveBonus(c));
                return;
            case 91: // 0x111B1: Magic Missile (spell 15) → nothing (message); else element bit 2 → nothing
                if (Cells.Spell == 0x0F)
                {
                    Nullify(0);
                    Emit(c, EffectEvent.Unaffected);
                }
                else if ((F.DamageFlags & 4) != 0)
                    Nullify(0);
                return;
            case 93: // 0x111FD: element bit 0 → damage halved
                if ((F.DamageFlags & 1) != 0)
                    F.PendingDamage >>= 1;
                return;
            case 94: // 0x11217: the attacker's missile or weapon with base byte 7 AND 0x81 → damage halved
                if (Current is { } a94 && MissileOf(a94) is { } w94 && Items is { } it94 && (it94[w94[0x2E], 7] & 0x81) != 0)
                    F.PendingDamage >>= 1;
                return;
            case 95: // 0x1126B: record byte 4 cleared; a creature in play dies (0xECB3, status 6)
                node![4] = 0;
                if (c[InPlay] != 0)
                    InflictStatus(c, 6, EffectEvent.FallsDead); // text CS:2605
                return;
            case 96: // 0x112A6: no missile/weapon, or one with plus 0 whose word 0x31 is not 0xB1 → damage 0
            {
                var w = Current is { } a96 ? MissileOf(a96) : null;
                if (w is null || (w[0x32] == 0 && w[0x31] != 0xB1))
                    F.PendingDamage = 0;
                return;
            }
            case 97: // 0x112E5: save type 4 or 2 → save + Constitution bonus
                if (Cells.SaveType is 4 or 2)
                    Cells.SaveRoll = (byte)(Cells.SaveRoll + ConSaveBonus(c));
                return;
            case 98: // 0x1135B Regeneration: HP + 3 (byte), at most the maximum
                c[Hp] = (byte)(c[Hp] + 3);
                if (c[Hp] > c[HpMax])
                    c[Hp] = c[HpMax];
                return;
            case 99: // 0x1138B: dying with combat record +0x0E < 6 → n = 6 − it; unconscious → n = 6; with n > 0 and
                     // back in play with n HP: effect 0x5F for 1d4 + 1 minutes (param 255, removal runs the handler),
                     // then this record's byte 4 is cleared and it is removed
            {
                byte n = 0;
                if (c[Status] == 5 && rec[CombatRecord.Dying] < 6)
                    n = (byte)(6 - rec[CombatRecord.Dying]);
                if (c[Status] == 4)
                    n = 6;
                if (n > 0 && Revive(c, n))
                {
                    AddEffect(c, 0x5F, (ushort)(Dice(1, 4) + 1), 0xFF, true);
                    node![4] = 0;
                    RemoveEffect(c, 0x63, node);
                }
                return;
            }
            case 100: // 0x11434: element bits 0 and 4 clear → effect 0x66 for 3d6 minutes (param 255, removal handler)
                if ((F.DamageFlags & 1) == 0 && (F.DamageFlags & 0x10) == 0)
                    AddEffect(c, 0x66, Dice(3, 6), 0xFF, true);
                return;
            case 101: // 0x1146F: without 0x62 and 0x3B → effect 0x3B for 3 minutes (param 255, removal handler)
                if (FindEffect(c, 0x62) is null && FindEffect(c, 0x3B) is null)
                    AddEffect(c, 0x3B, 3, 0xFF, true);
                return;
            case 102: // 0x114C1: back in play with full HP (0x32); failing that 0x66 re-added for 1 minute (lock permitting)
                if (!Revive(c, c[HpMax]))
                    AddEffectIfNoLock(c, 0x66, param, 1);
                return;
            case 103: // 0x1151A: byte 4 cleared, 0x319D2(creature, 3, 0) (the view, done again by 0x2BDF0), out of play
                      // with status 3 and its text CS:28A5 (0x2BDF0)
                node![4] = 0;
                Emit(c, EffectEvent.EscapeView);
                PutOutOfPlay(c, 3, EffectEvent.Escapes);
                return;
            case 104: // 0x1155C: missile dodge with 60 % (0xFC07)
                DodgeMissile(c, 0x3C);
                return;
            case 105: MagicResistance(0x32); return; // 0x115D1
            case 106: MagicResistance(0x0F); return; // 0x115DF
            case 107: // 0x115ED: 1d100 ≤ 90 → sleep (0x35) and charm (0x0B) do not take
                if (Dice(1, 100) <= 0x5A)
                {
                    Nullify(0x35);
                    Nullify(0x0B);
                }
                return;
            case 108: Nullify(0x0B); Nullify(0x35); return; // 0x11611
            case 109: Nullify(0x34); return;                 // 0x11626
            case 110: // 0x11634: element bit 1 → nothing
                if ((F.DamageFlags & 2) != 0)
                    Nullify(0);
                return;
            case 111: // 0x1164B: poison (0x37) and paralysis (0x34) do not take; save type 0 → save roll 100
                Nullify(0x37);
                Nullify(0x34);
                if (Cells.SaveType == 0)
                    Cells.SaveRoll = 100;
                return;
            case 112: // 0x1166C: element bit 0 → nothing
                if ((F.DamageFlags & 1) != 0)
                    Nullify(0);
                return;
            case 113: // 0x11683: element bit 0 → damage − 1 per die (DS:681C), not below the dice count
                if ((F.DamageFlags & 1) != 0)
                {
                    byte n = F.DamageDice;
                    for (int i = 1; i <= n; i++)
                    {
                        F.PendingDamage--;
                        if (F.PendingDamage < F.DamageDice)
                            F.PendingDamage = F.DamageDice;
                    }
                }
                return;
            case 114: // 0x116C8: element bit 2 → damage halved
                if ((F.DamageFlags & 4) != 0)
                    F.PendingDamage >>= 1;
                return;
            case 115: // 0x116E2: the attacker's missile/weapon with base byte 7 = 0 or bit 0 set → damage halved
                if (Current is { } a115 && MissileOf(a115) is { } w115 && Items is { } it115)
                {
                    byte f = it115[w115[0x2E], 7];
                    if (f == 0 || (f & 1) != 0)
                        F.PendingDamage >>= 1;
                }
                return;
            case 116: // 0x11741: the attacker's missile/weapon with plus above 0 (signed) → damage halved
                if (Current is { } a116 && MissileOf(a116) is { } w116 && (sbyte)w116[0x32] > 0)
                    F.PendingDamage >>= 1;
                return;
            case 117: // 0x1177B: the attacker's weapon slot holds type 0x55 → damage = 1d6 + 1 (0x2BD18)
                if (Current is { } a117 && WeaponOf(a117) is { } w117 && w117[0x2E] == 0x55)
                    F.PendingDamage = (byte)(DamageDiceRoll(1, 6) + 1);
                return;
            case 118: // 0x117BE: element bit 1 → damage halved
                if ((F.DamageFlags & 2) != 0)
                    F.PendingDamage >>= 1;
                return;
            case 119: // 0x117D8: no missile/weapon or one with plus 0: attacker's byte 0x2E above 0 (signed) or its
                      // byte 0x73 below 4 (signed) → damage 0
            {
                if (Current is not { } a119)
                    return;
                var w = MissileOf(a119);
                if (w is null || w[0x32] == 0)
                    if ((sbyte)a119[0x2E] > 0 || (sbyte)a119[0x73] < 4)
                        F.PendingDamage = 0;
                return;
            }
            case 120: // 0x11823: the attacker's weapon slot holds type 0x57 or 0x58 → missile dodge with 50 %
                if (Current is { } a120 && WeaponOf(a120) is { } w120 && w120[0x2E] is 0x57 or 0x58)
                    DodgeMissile(c, 0x32);
                return;
            case 122: // 0x11993: weapon slot type 0x56 → damage = 3d8 (0x2BD18); then element AND 9 → damage + DS:681C
                if (Current is { } a122 && WeaponOf(a122) is { } w122 && w122[0x2E] == 0x56)
                    F.PendingDamage = DamageDiceRoll(3, 8);
                if ((F.DamageFlags & 9) != 0)
                    F.PendingDamage = (byte)(F.PendingDamage + F.DamageDice);
                return;
            case 123: // 0x119E3: no missile/weapon → 0; plus 0 and word 0x31 = 0xB1 → halved; other plus 0 → 0
            {
                var w = Current is { } a123 ? MissileOf(a123) : null;
                if (w is null)
                    F.PendingDamage = 0;
                else if (w[0x32] == 0 && w[0x31] == 0xB1)
                    F.PendingDamage >>= 1;
                else if (w[0x32] == 0)
                    F.PendingDamage = 0;
                return;
            }
            case 124: // 0x11A3F: 1d100 ≤ 30 → charm (0x0B) and sleep (0x35) do not take
                if (Dice(1, 100) <= 0x1E)
                {
                    Nullify(0x0B);
                    Nullify(0x35);
                }
                return;
            case 125: // 0x11A63: charm, sleep, paralysis, poison do not take; save type 0 → save roll 100
                Nullify(0x0B);
                Nullify(0x35);
                Nullify(0x34);
                Nullify(0x37);
                if (Cells.SaveType == 0)
                    Cells.SaveRoll = 100;
                return;
            case 126: // 0x11A92: the last target carries a readied item with a name word 0x98 (and word 0x31 = 0xFC)
                      // or 0x76 → no target
            {
                Cells.Other = rec.Other;
                if (Cells.Other is not { } o)
                    return;
                foreach (var it in o.Items)
                {
                    if (F.TargetBlocked != 0)
                        break;
                    if (it[0x34] == 0)
                        continue;
                    for (int k = 1; k <= 3; k++)
                        if ((it[0x2E + k] == 0x98 && it[0x31] == 0xFC) || it[0x2E + k] == 0x76)
                            F.TargetBlocked = 1;
                }
                return;
            }
            case 128 or 129 or 130 or 133 or 134 or 136 or 138 or 139: // 0x11B35: worn item granting effect 0x3D
                if (removed)
                    RemoveEffect(c, node![0x3D]);
                else
                {
                    AddEffect(c, node![0x3D], 0, 0x0C, false);
                    CallHandler(node[0x3D], c, null, false);
                }
                return;
            case 131: // 0x11BB2 gauntlets
                Gauntlets(c, removed);
                return;
            case 132: // 0x11D00: readied by a creature whose alignment 0xA0 is not 0x3D AND 0x0F: unreadied, element 8,
                      // (0x3728 in a fight [?]) non-weapon damage 0x3D shr 4, DS:6821 = 1
                if (!removed && (node![0x3D] & 0x0F) != c[Alignment])
                {
                    node[0x34] = 0;
                    F.DamageFlags = 8;
                    NonWeaponDamage(c, (byte)(node[0x3D] >> 4), 0, false);
                    ItemFlag = 1;
                }
                return;
            case 135: // 0x11D8F: readied with Strength below 19: unreadied (message)
                if (!removed && c[Str] < 0x13)
                {
                    node![0x34] = 0;
                    Emit(c, EffectEvent.NeedsGiantStrength); // 0x11DCA: START 0x1693 (row 24) with CS:311B
                }
                return;
            case 137: // 0x11DD5: removed → effect 0x17 removed
                if (removed)
                    RemoveEffect(c, 0x17);
                return;
        }
        handled = false;
        CallHoldHandlers(id, c, node, removed, ref handled);
    }

    /// <summary>Handlers 40, 73–77, 81–86, 88, 121 (EffectHandlers.Holds.cs).</summary>
    partial void CallHoldHandlers(byte id, EclCharacter c, byte[]? node, bool removed, ref bool handled);

    /// <summary>0x2BD18: dice with the count stored in DS:681C first.</summary>
    private byte DamageDiceRoll(int count, int sides)
    {
        F.DamageDice = (byte)count;
        return Dice(count, sides);
    }

    /// <summary>The weapon slot of the equipment (creature +0xCC).</summary>
    private byte[]? WeaponOf(EclCharacter a) => Items is null ? null : FightRules.Equipment(a, Items)[FightRules.SlotWeapon];

    /// <summary>
    /// 0x11134 / 0x112E5: Constitution 0x14: 4–6 → 1, 7–10 → 2, 11–13 → 3, 14–17 → 4, 18–20 → 5 (checked against the
    /// program, por_handler_oracle.py). [?] Outside those ranges the program adds the handler's uninitialised [bp−1]
    /// (the saving throw's BP − 0x125), stack residue from before the throw: DOSBox-X showed 0x1F and 0x00 on the same
    /// path (spells-effects.md "Handlers 40, 84, 88, 90, 97 under emulation"); taken as 0. Carriers (created dwarves,
    /// gnomes, halflings) leave 4..20 through the temple's raise (Constitution − 1 each, 0x4B85) or handler 7's + 1 at 20.
    /// </summary>
    private int ConSaveBonus(EclCharacter c)
    {
        byte con = c[Con];
        switch (con)
        {
            case >= 4 and <= 6: return 1;
            case >= 7 and <= 10: return 2;
            case >= 11 and <= 13: return 3;
            case >= 14 and <= 17: return 4;
            case >= 18 and <= 20: return 5;
        }
        Diag("[?] Constitution save bonus outside 4..20 reads stack residue (varies); 0 taken");
        return 0;
    }

    /// <summary>
    /// 0x11570: magic resistance of <paramref name="pct"/>. chance = pct − 5 × (11 − casting level of DS:681B, START
    /// 0x3262), a byte. When an effect is being added (DS:6817 ≠ 0) or element bit 3 is set: 1d100 ≤ chance → nothing
    /// (DS:6817, DS:6818 = 0).
    /// </summary>
    private void MagicResistance(byte pct)
    {
        byte level;
        if (CasterLevel is { } cl)
            level = cl(Cells.Spell);
        else
        {
            Diag("[?] magic resistance needs the casting level (START 0x3262)");
            level = 0;
        }
        byte chance = (byte)(pct - 5 * (11 - level));
        if (Cells.PendingEffect == 0 && (F.DamageFlags & 8) == 0)
            return;
        if (Dice(1, 100) <= chance)
            Nullify(0);
    }

    /// <summary>
    /// 0x2C753 (read s30): when the creature's square takes it (0x317E1, <see cref="PutBackOnSquare"/>), status 0, in
    /// play, HP = <paramref name="hp"/>; then <see cref="EffectEvent.Revived"/> (in a fight 0x319D2(creature, 3, 0), then
    /// START 0x16EC(creature, text, row 10, pause 1), the text by the hostile byte 0x10E) and the teams are counted
    /// (START 0x2F7B). True then.
    /// </summary>
    public bool Revive(EclCharacter c, byte hp)
    {
        if (!PutBackOnSquare(c))
            return false;
        c[Status] = 0;
        c[InPlay] = 1;
        c[Hp] = hp;
        Emit(c, EffectEvent.Revived);
        FightRules.CountTeams(List(), F);
        return true;
    }

    /// <summary>
    /// 0x2BDF0 (read s30) for a creature in play: the caller's message <paramref name="message"/> (0x319D2(creature, 3, 0),
    /// then START 0x16EC(creature, text, row 10, pause 1)), out of play, status <paramref name="status"/>, HP 0 unless the
    /// status is 3 (fled); its icon leaves the map (0x30C65, START 0x464:0x1B52, DS:5F2A + 4·slot = 0, 0x30ADA:
    /// <see cref="CombatMap.TakeOff"/>), actions end (START 0x31D7, which returns true) and so its DS:0C14 effects go
    /// (0x2BEF4). Nothing at all (the message included) for a creature out of play (0x2BE0C).
    /// </summary>
    private void PutOutOfPlay(EclCharacter c, byte status, EffectEvent message)
    {
        if (c[InPlay] == 0)
            return;
        Emit(c, message);
        c[InPlay] = 0;
        c[Status] = status;
        if (c[Status] != 3)
            c[Hp] = 0;
        if (InCombat)
            Map?.TakeOff(c);
        FightRules.EndActions(F.Record(c));
        RemoveEffectsHp0(c);
    }

    /// <summary>0x2BFF7: a Strength code to (base, exceptional): code AND 0x7F ≤ 101 → 18 / code − 1, else code − 100 / 0.</summary>
    public static (byte Base, byte Exc) StrengthFromCode(byte code)
    {
        byte v = (byte)(code & 0x7F);
        return v <= 0x65 ? ((byte)18, (byte)(v - 1)) : ((byte)(v - 0x64), (byte)0);
    }

    /// <summary>0x2BFCE: (base, exceptional) to a code: base + 100, but exceptional + 1 for base 18.</summary>
    public static byte StrengthCode(byte b, byte exc) => b == 0x12 ? (byte)(exc + 1) : (byte)(b + 0x64);

    /// <summary>
    /// 0xF115 (Enlarge 12, Strength 38): nothing when param &gt; 0x7F. Else Strength 0x10 / 0x16 = the record's value;
    /// then the best other 12/38 record (higher base, or for a best base of 18 a higher exceptional) gets the creature's
    /// Strength code as param and the creature takes the best value.
    /// </summary>
    private void StrengthEffect(EclCharacter c, byte[] node)
    {
        if (node[3] > 0x7F)
            return;
        (c[Str], c[StrExc]) = StrengthFromCode(node[3]);
        byte[]? best = null;
        byte bb = 0, be = 0;
        foreach (var q in c.Effects)
        {
            if (q[0] is not (0x0C or 0x26) || ReferenceEquals(q, node))
                continue;
            var (b, e) = StrengthFromCode(q[3]);
            if (b > bb || (bb == 0x12 && e > be))
            {
                bb = b;
                be = e;
                best = q;
            }
        }
        if (best is not null)
        {
            best[3] = StrengthCode(c[Str], c[StrExc]);
            c[Str] = bb;
            c[StrExc] = be;
        }
    }

    /// <summary>
    /// 0x2C046: raise Strength to (b, e). Lower than the current (b below 0x10, or b = 18 with e below 0x16): false,
    /// code = Strength code + 0x80. Else the first 12/38 record with param below 0x80 (if any) takes the current code +
    /// 0x80 and its old value becomes the "previous"; Strength is set (stats recalculated) and code = the previous
    /// value's code; true.
    /// </summary>
    public bool RaiseStrength(EclCharacter c, byte b, byte e, out byte code)
    {
        byte cb = c[Str], ce = c[StrExc];
        if (b < cb || (b == 0x12 && e < ce))
        {
            code = (byte)(StrengthCode(b, e) + 0x80);
            return false;
        }
        var q = c.Effects.FirstOrDefault(x => x[0] is 0x0C or 0x26 && x[3] < 0x80);
        if (q is not null)
        {
            byte cur = StrengthCode(cb, ce);
            (cb, ce) = StrengthFromCode(q[3]);
            q[3] = (byte)(cur + 0x80);
        }
        c[Str] = b;
        c[StrExc] = e;
        ResetBaseline(c);
        code = StrengthCode(cb, ce);
        return true;
    }

    /// <summary>
    /// 0x11BB2 gauntlets. Readied: 0x2C046 to 18/00 (message when it took), DS:6821 = 1, and effect 0x26 (permanent,
    /// removal runs the handler) with the code it returned. Unreadied: with Strength now 18/00 the first 0x26 record goes
    /// (the program compares the record's id byte with 0x80, always below); otherwise the first 0x26 record whose code is
    /// 18/00.
    /// </summary>
    private void Gauntlets(EclCharacter c, bool removed)
    {
        if (removed)
        {
            bool at1800 = c[Str] == 0x12 && c[StrExc] == 0x64;
            foreach (var q in c.Effects.ToList())
            {
                if (q[0] != 0x26)
                    continue;
                var (b, e) = StrengthFromCode(q[3]);
                if ((at1800 && q[0] < 0x80) || (b == 0x12 && e == 0x64 && !at1800))
                {
                    RemoveEffect(c, 0x26, q);
                    return;
                }
            }
            return;
        }
        // 0x11C81–0x11CD8: only when 0x2C046 raised it; in a fight DS:49F3 = 2 around START 0x16EC (row 10, pause 1).
        if (RaiseStrength(c, 0x12, 0x64, out byte code))
            Emit(c, EffectEvent.Stronger);
        ItemFlag = 1;
        AddEffect(c, 0x26, 0, code, true);
    }

    /// <summary>
    /// 0xEE8E Manual of Bodily Health: (message) Constitution 0x14 + 1; at 20 or more: effect 0x3E for 60 minutes
    /// (param 255, removal handler) and done. Else gain = (max HP 0x32 − rolled HP 0xB1, byte); from Constitution 15 up:
    /// per class level (0x96 + j, j 0..7, signed above 0): fighter (j = 2) adds level × (Con − 15), else level × 2 above
    /// 15, else the level (byte sums); 0 → 1; gain ÷= that; with Constitution below 17 or fighter levels, max HP and HP
    /// rise by the gain.
    /// </summary>
    private void TrainingManual(EclCharacter c)
    {
        Emit(c, EffectEvent.Hardier);
        c[Con]++;
        if (c[Con] >= 0x14)
        {
            AddEffect(c, 0x3E, 0x3C, 0xFF, true);
            return;
        }
        byte gain = (byte)(c[HpMax] - c[HpRolled]);
        byte bonus = 0;
        if (c[Con] < 0x0F)
            return;
        for (int j = 0; j <= 7; j++)
        {
            sbyte lvl = (sbyte)c[ClassLevels + j];
            if (lvl <= 0)
                continue;
            if (j == 2)
                bonus = (byte)(bonus + lvl * (c[Con] - 0x0F));
            else if (c[Con] > 0x0F)
                bonus = (byte)(bonus + lvl * 2);
            else
                bonus = (byte)(bonus + lvl);
        }
        if (bonus == 0)
            bonus = 1;
        gain = (byte)(gain / bonus);
        if (c[Con] < 0x11 || (sbyte)c[FighterLevel] > 0)
        {
            c[HpMax] = (byte)(c[HpMax] + gain);
            c[Hp] = (byte)(c[Hp] + gain);
        }
    }
}
