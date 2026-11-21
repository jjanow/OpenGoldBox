using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>Messages the AI shows (paraphrased; the Client words them).</summary>
public enum AiMessage
{
    /// <summary>0xACA6: a creature fleeing on morale (+0x14 set, +0x10 not).</summary>
    FleesInPanic,
    /// <summary>0x1638E's message (GAME.OVR 0x1637A) when Alt-X works in an AI turn.</summary>
    GodsIntervene,
    /// <summary>0xBCD5: a creature made to flee by an effect (combat record +0x10).</summary>
    ForcedToFlee,
    /// <summary>0xBE19: a creature that can neither stand nor outrun its enemies gives up (0x2BDF0).</summary>
    Surrenders,
    /// <summary>0xBBC8 / 0xBBDE: quick magic switched on / off.</summary>
    QuickMagicOn,
    QuickMagicOff,
}

/// <summary>
/// The monster AI, GAME.OVR overlay unit 0x38 (0xABC0–0xC4E7), entry 0xABCF (docs/re/combat-ai.md). Used for every
/// creature with record 0x10F ≠ 0: monsters and party members on Quick. Acts through <see cref="Fight"/>
/// (<see cref="Fight.Attack"/>, <see cref="Fight.EndActions"/>, <see cref="Fight.Guard"/>) and the seams
/// <see cref="IFightMap"/>, <see cref="IFightMoves"/>, <see cref="IFightSpells"/>. Random draws follow the program's
/// order (dice routine 0x2BCCD; <see cref="FightRules.Dice"/>).
/// </summary>
public sealed class MonsterAi : IFightController
{
    /// <summary>Combat record bytes the AI reads: turned / forced to flee (+0x10, <see cref="CombatRecord.Turned"/>),
    /// tried turning (+0x11, <see cref="CombatRecord.TriedTurning"/>), fleeing (+0x14), step pattern row 1–6 (+0x15).</summary>
    public const int ForcedFlee = CombatRecord.Turned, NoTurning = CombatRecord.TriedTurning, Fleeing = 0x14, StepRow = 0x15;
    /// <summary>Effects the morale check removes first (DS:0C24, DS:0C25; 0x2BF2B) and again on fleeing.</summary>
    public static readonly byte[] MoraleEffects = [0x4A, 0x4B];
    /// <summary>Effects that let a creature enter ground type 0x1E (START 0x2D39 calls in 0xB30A–0xB364).</summary>
    public static readonly byte[] CrossingEffects = [0x20, 0x1E, 0x6F, 0x7D];
    /// <summary>Trigger classes of the AI's 0x2B1E4 calls.</summary>
    public const int TriggerTargetDefender = 1, TriggerTargetAttacker = 0, TriggerMoveAttack = 0x0E, TriggerMorale = 0x11;
    /// <summary>VM cells: $49E5 (word [DS:49D2]+0x1CA; ≠ 0 = no item use, [?] meaning), the morale threshold $6DC6.</summary>
    public const ushort NoItemUseCell = 0x49E5, MoraleThresholdCell = 0x6DC6;
    /// <summary>Record bytes: class (0x2F; 5 = magic-user), Int (0x11), cleric level (0x96), hands used (0x100),
    /// class mask (0xB0), alignment (0xA0, [?] name), natural dice / sides / bonus (0xA3, 0xA5, 0xA7), memorised
    /// spells (0x17–0x2B).</summary>
    public const int Class = 0x2F, Int = 0x11, ClericLevel = 0x96, HandsUsed = 0x100, ClassMask = 0xB0, Alignment = 0xA0,
        NaturalDice = 0xA3, NaturalSides = 0xA5, NaturalBonus = 0xA7, SpellsFirst = 0x17, SpellsCount = 21;

    private readonly IReadOnlyList<byte> _steps;
    private byte _lastDir, _blocked;

    /// <param name="stepOffsets">DS:02AE: the step pattern table, indexed 5 × row + attempt (row = combat record
    /// +0x15, attempt 1–5; the program also reads index 5 × row + 6). From the player's START.EXE
    /// (<c>PorProgram.AiStepOffsets</c>).</param>
    /// <param name="menu">The player's menu for creatures with 0x10F = 0; null = their actions end.</param>
    /// <param name="turnUndead">The turn-undead table (START.EXE), needed once a cleric finds undead.</param>
    public MonsterAi(IReadOnlyList<byte> stepOffsets, IFightController? menu = null, TurnUndeadTable? turnUndead = null)
    {
        _steps = stepOffsets;
        Menu = menu;
        TurnUndeadTable = turnUndead;
    }

    public TurnUndeadTable? TurnUndeadTable { get; }

    public IFightController? Menu { get; }
    /// <summary>0xBB81: the key the player pressed during an AI turn (BIOS scan code), 0 for none. Null = no keyboard.</summary>
    public Func<byte>? ReadKey { get; set; }
    /// <summary>
    /// Key 0x2D (Alt-X) in an AI turn calls 0x1638E: true when the start word matches (the program's debugging word;
    /// then <see cref="FightMenu.GodsIntervene"/> runs after its message), false: nothing (the AI path prints no
    /// refusal; 0x1638E's result is not used). Null = never.
    /// </summary>
    public Func<bool>? AltX { get; set; }
    /// <summary>Messages for the screen.</summary>
    public Action<EclCharacter, AiMessage>? Message { get; set; }
    /// <summary>0x2AEEA for items with byte 0x3E &gt; 0x7F when the AI readies (false) or unreadies (true) them [?].</summary>
    public Action<EclCharacter, byte[], bool>? ItemHandler { get; set; }
    /// <summary>0xABCF's start, also when the menu's Quick hands the turn over (row 24 and the message area cleared on screen).</summary>
    public Action<EclCharacter>? Starting { get; set; }

    public bool ContinueBattle() => Menu?.ContinueBattle() ?? false;

    public void Act(Fight fight, EclCharacter creature, bool auto)
    {
        if (auto)
            Turn(fight, creature);
        else if (Menu is not null)
            Menu.Act(fight, creature, false);
        else
            fight.EndActions(creature);
    }

    /// <summary>The side a creature fights (START 0x2F57): 1 when it is on the party side (hostile 0), else 0.</summary>
    public static int EnemySide(EclCharacter c) => c[FightRules.Hostile] == 0 ? 1 : 0;

    private static byte D(Fight f, byte sides) => FightRules.Dice(f.Random, 1, sides);

    // ---- 0xABCF -------------------------------------------------------------------------------------------------

    /// <summary>
    /// 0xABCF. Key check; out of play → actions end; the opening dice (always); morale (when no key ended the turn);
    /// then the first of: an item, a spell begun earlier, turn undead, a spell; else ready the best weapon, key check,
    /// and repeat target search + move/attack until the turn is over.
    /// </summary>
    public void Turn(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        f.State.Current = c;
        Starting?.Invoke(c);
        bool done = CheckKeystroke(f, c);
        if (c[FightRules.InPlay] == 0)
        {
            f.EndActions(c);
            done = true;
        }
        OpeningDice(f, c);
        if (!done)
            done = CheckMorale(f, c);
        if (rec[Fleeing] != 0 && rec[ForcedFlee] == 0)
            Message?.Invoke(c, AiMessage.FleesInPanic);
        if (done)
            return;
        if (UseItem(f, c) || CastBegun(f, c) || TryTurnUndead(f, c))
        {
            f.EndActions(c);
            return;
        }
        if (CastSpell(f, c))
            return;
        EquipWeapon(f, c);
        done = CheckKeystroke(f, c);
        for (int guard = 0; !done; guard++)
        {
            if (guard > 200) // not the program's: a seam that never ends the turn
            {
                f.Diagnostics.Add($"AI loop for {c.Name} did not end; actions ended");
                f.EndActions(c);
                return;
            }
            if (AcquireTarget(f, c, 0xFF, true, false) && rec.InitiativeValue > 0)
                done = MoveAttack(f, c);
            else
                done = FinishRound(f, c);
        }
    }

    /// <summary>0xACEE: a spell begun earlier (+0 &gt; 0) is cast now, 0x27C38(spell, ai = 1, 1), which aims by itself
    /// (<see cref="IFightSpells.ContinueCastingAuto"/>).</summary>
    private static bool CastBegun(Fight f, EclCharacter c)
    {
        if (f.Record(c)[CombatRecord.SpellCast] == 0)
            return false;
        f.Spells.ContinueCastingAuto(f, c);
        return true;
    }

    /// <summary>
    /// 0xAC05–0xAC71: the step pattern row (+0x15). A row of 1–4 is kept unless 1d4 = 1; otherwise (and for 0 or
    /// 5–6) 1d8: an 8 gives 1d2 + 4, anything else 1d4.
    /// </summary>
    public static void OpeningDice(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        byte v = rec[StepRow];
        if (v == 0 || v > 4 || D(f, 4) == 1)
            v = D(f, 8) == 8 ? (byte)(D(f, 2) + 4) : D(f, 4);
        rec[StepRow] = v;
    }

    /// <summary>
    /// 0xBB81: with a key pressed: M (0x32) toggles quick magic (DS:6E30); space (0x20) puts every party-side
    /// character (0x84 &lt; 0x80, status ≠ 1) back under the player's control (0x10F = 0) and, when that includes this
    /// creature, initiative 20 and true; X (0x2D) calls 0x1638E. False otherwise.
    /// </summary>
    public bool CheckKeystroke(Fight f, EclCharacter c)
    {
        byte key = ReadKey?.Invoke() ?? 0;
        switch (key)
        {
            case 0x32:
                f.State.QuickMagic = !f.State.QuickMagic;
                Message?.Invoke(c, f.State.QuickMagic ? AiMessage.QuickMagicOn : AiMessage.QuickMagicOff);
                return false;
            case 0x20:
                foreach (var m in f.List)
                    if (m[CombatRules.Npc] < 0x80 && m[FightRules.Status] != 1)
                        m[CombatRules.AutoCombat] = 0;
                if (c[CombatRules.AutoCombat] == 0)
                {
                    f.Record(c).InitiativeValue = 20;
                    return true;
                }
                return false;
            case 0x2D:
                if (AltX?.Invoke() == true)
                {
                    Message?.Invoke(c, AiMessage.GodsIntervene);
                    FightMenu.GodsIntervene(f);
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>0xBB0F: helpless, a ranged weapon or initiative 0 → actions end; else guard (START 0x3226). True.</summary>
    public static bool FinishRound(Fight f, EclCharacter c)
    {
        if (FightRules.IsHelpless(c) || FightRules.HasRangedWeapon(c, f.Items) || f.Record(c).InitiativeValue == 0)
            f.EndActions(c);
        else
            f.Guard(c);
        return true;
    }

    // ---- Morale 0xBC97 ------------------------------------------------------------------------------------------

    /// <summary>
    /// 0xBC97. +0x14 = 0; effects 0x4A, 0x4B removed. Forced (+0x10) → flee. Only NPCs (0x84 &gt; 0x7F) test morale:
    /// m = 2 × (0x84 AND 0x7F) as a byte, 0 when above 102, into DS:6825; trigger 0x11. Steady when m ≠ 0 and
    /// m ≥ 100 − HP% (HP × 100 div max HP, signed). Else DS:6825 = the enemy-side HP % DS:6E2F, trigger 0x11, and
    /// steady when it is ≠ 0, ≥ 100 − $6DC6 (unsigned word) and the creature is hostile. Else: the fastest enemy's
    /// movement / 2 ≤ its own / 2 → flee (+0x14 = 1, effects 0x4A/0x4B removed again); else Int &gt; 5 → it surrenders
    /// (0x2BDF0 with status 4) and the turn ends (true).
    /// </summary>
    public bool CheckMorale(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        rec[Fleeing] = 0;
        foreach (var id in MoraleEffects)
            FightRules.RemoveFirstEffect(c, id, f.Effects);
        if (rec[ForcedFlee] != 0)
        {
            rec[Fleeing] = 1;
            Message?.Invoke(c, AiMessage.ForcedToFlee);
            return false;
        }
        if (c[CombatRules.Npc] <= 0x7F)
            return false;
        byte m = (byte)((c[CombatRules.Npc] & 0x7F) << 1);
        if (m > 0x66)
            m = 0;
        f.State.Morale = m;
        f.Effects.Trigger(TriggerMorale, c, f.State);
        // [?] max HP 0 would be a divide error in the program.
        int pct = c[FightRules.HpMax] == 0 ? 0 : (short)(c[FightRules.Hp] * 100) / c[FightRules.HpMax];
        if (f.State.Morale >= 100 - pct && f.State.Morale != 0)
            return false;
        f.State.Morale = f.State.HostileHpPercent;
        f.Effects.Trigger(TriggerMorale, c, f.State);
        ushort limit = (ushort)(100 - f.Memory.Read(MoraleThresholdCell));
        if (f.State.Morale >= limit && f.State.Morale != 0 && c[FightRules.Hostile] != 0)
            return false;
        int fastest = HighestEnemyMovement(f, c);
        int own = FightRules.CreatureMovement(c, f.Memory.Read(FightRules.PartyMoveCell), f.State, f.Effects) >> 1;
        if (fastest <= own)
        {
            rec[Fleeing] = 1;
            foreach (var id in MoraleEffects)
                FightRules.RemoveFirstEffect(c, id, f.Effects);
            return false;
        }
        if (c[Int] > 5)
        {
            Surrender(f, c);
            f.EndActions(c); // 0xBE36: morale ends the actions itself too, whatever 0x2BDF0 did (START 0x31D7: true)
            return true;
        }
        return false;
    }

    /// <summary>0x15103: the highest movement / 2 (0x1291E) among enemies in play.</summary>
    public static int HighestEnemyMovement(Fight f, EclCharacter c)
    {
        int side = EnemySide(c), best = 0;
        foreach (var e in f.List)
            if (e[FightRules.Hostile] == side && e[FightRules.InPlay] != 0)
                best = Math.Max(best, FightRules.CreatureMovement(e, f.Memory.Read(FightRules.PartyMoveCell), f.State, f.Effects) >> 1);
        return best;
    }

    /// <summary>
    /// 0x2BDF0(c, 4, text): nothing at all for a creature out of play (0x2BE0C, the message included); else the view
    /// on it and the message (START 0x16EC, row 10, pause), in play 0, status 4, HP 0, off the map, actions end,
    /// dropped effects removed (0x2BEF4).
    /// </summary>
    private void Surrender(Fight f, EclCharacter c)
    {
        if (c[FightRules.InPlay] == 0)
            return;
        Message?.Invoke(c, AiMessage.Surrenders);
        c[FightRules.InPlay] = 0;
        c[FightRules.Status] = (byte)CreatureStatus.Unconscious;
        c[FightRules.Hp] = 0;
        f.Map?.Remove(c);
        f.EndActions(c);
        foreach (var id in FightRules.DroppedEffects)
            FightRules.RemoveFirstEffect(c, id, f.Effects);
    }

    // ---- Targets ------------------------------------------------------------------------------------------------

    /// <summary>
    /// START 0x2FD3: the enemies (hostile byte = <see cref="EnemySide"/>) within <paramref name="range"/> of the
    /// creature, in map-slot order (the list DS:6DE4). Empty without a combat map.
    /// </summary>
    public static List<EclCharacter?> EnemiesWithin(Fight f, EclCharacter c, int range) =>
        FightSearch.EnemiesNear(f, c, range).ToList<EclCharacter?>();

    /// <summary>
    /// 0x13880: may the attacker choose the target? Same creature → yes. Else DS:681E = 0, trigger 1 on the target,
    /// and when not refused trigger 0 on the attacker with +0x0A set to the target for the call; yes when DS:681E = 0.
    /// </summary>
    public static bool CanTarget(Fight f, EclCharacter attacker, EclCharacter? target)
    {
        if (target is null)
            return false;
        if (ReferenceEquals(attacker, target))
            return true;
        f.State.TargetBlocked = 0;
        f.Effects.Trigger(TriggerTargetDefender, target, f.State);
        if (f.State.TargetBlocked == 0)
        {
            var rec = f.Record(attacker);
            var saved = rec.Other;
            rec.Other = target;
            f.Effects.Trigger(TriggerTargetAttacker, attacker, f.State);
            rec.Other = saved;
        }
        return f.State.TargetBlocked == 0;
    }

    /// <summary>
    /// 0x15FDF (stub 0x68). The remembered target (+0x0A) stays when not <paramref name="forceNew"/> and it is on the
    /// other side, in play and may be chosen; else it is cleared. Without one, up to two passes (the second with
    /// obstacles ignored when not <paramref name="relaxed"/>): the enemies within <paramref name="range"/>, then up
    /// to 20 tries of 1d(count): an entry is taken when (relaxed and obstacles ignored) or it may be chosen; else it
    /// is struck out, and the pass ends when none is left.
    /// </summary>
    public static bool AcquireTarget(Fight f, EclCharacter c, int range, bool relaxed, bool forceNew)
    {
        var rec = f.Record(c);
        var t = rec.Other;
        if (forceNew || (t is not null && !(t[FightRules.Hostile] != c[FightRules.Hostile] && t[FightRules.InPlay] != 0 && CanTarget(f, c, t))))
            rec.Other = null;
        bool found = rec.Other is not null, pass = false, lastPass = false;
        while (!found && !lastPass)
        {
            lastPass = pass;
            if (pass && !relaxed)
                f.State.IgnoreObstacles = true;
            int tries = 20;
            var list = EnemiesWithin(f, c, range);
            int count = list.Count;
            while (tries > 0 && !found && count > 0)
            {
                tries--;
                int k = D(f, (byte)count);
                if (list[k - 1] is not { } e)
                    continue;
                if ((relaxed && f.State.IgnoreObstacles) || CanTarget(f, c, e))
                {
                    found = true;
                    rec.Other = e;
                }
                else
                {
                    list[k - 1] = null;
                    if (!list.Take(count).Any(x => x is not null))
                        count = 0;
                }
            }
            pass = true;
        }
        f.State.IgnoreObstacles = false;
        return found;
    }

    // ---- Move / attack 0xB6FC ---------------------------------------------------------------------------------

    /// <summary>
    /// 0xB6FC. DS:443C = 8, DS:443D = 0; trigger 0x0E. Loop: a fleeing creature steps while it has movement and an
    /// initiative of 1–19; initiative 0 or 20 ends the loop. After 20 rounds of the loop the round is finished. The
    /// weapon range (base byte 12 − 1; 0 or −1 → 1); the remembered target (only one on the hostile side and in play)
    /// is in reach when it may be chosen and a line reaches it with length / 2 ≤ range. Else the enemies within range:
    /// none → a target search and a step (or finish); some → 1d(count) picks one; a ranged non-throwing weapon with an
    /// enemy adjacent → ready the best weapon and stop; else in reach when adjacent or it may be chosen. In reach:
    /// sweep (0x1367D) or the attack (0x13FF1 + 0x1407F) with the ammunition (none for a throwing weapon at
    /// distance 1). Returns true when the turn is over.
    /// </summary>
    public bool MoveAttack(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        _lastDir = 8;
        _blocked = 0;
        int loops = 0;
        bool cont = true, done = false;
        f.Effects.Trigger(TriggerMoveAttack, c, f.State);
        if (rec.InitiativeValue == 0)
            cont = false;
        for (int guard = 0; !done && cont; guard++)
        {
            if (guard > 500)
            {
                f.Diagnostics.Add($"AI move/attack loop for {c.Name} did not end");
                f.EndActions(c);
                return true;
            }
            for (int g = 0; rec[Fleeing] != 0 && rec[CombatRecord.Movement] > 0 && rec.InitiativeValue > 0 && rec.InitiativeValue < 20; g++)
            {
                if (g > 200)
                {
                    f.EndActions(c);
                    break;
                }
                MoveStep(f, c);
            }
            if (rec.InitiativeValue == 0 || rec.InitiativeValue == 20)
            {
                cont = false;
                continue;
            }
            done = false;
            if (++loops > 20)
            {
                done = true;
                cont = !FinishRound(f, c);
            }
            bool inReach = false;
            byte range = 1;
            if (FightRules.Equipment(c, f.Items)[FightRules.SlotWeapon] is { } w)
                range = (byte)(f.Items[w[0x2E], BaseItemTable.Range] - 1);
            if (range is 0 or 0xFF)
                range = 1;
            var target = rec.Other;
            if (target is not null && (target[FightRules.InPlay] == 0 || target[FightRules.Hostile] == 0))
                target = null; // [P] the program keeps only targets with hostile ≠ 0 here (0xB85C)
            if (target is not null && CanTarget(f, c, target) && f.Map is { } map)
            {
                var (tx, ty) = map.Position(target);
                var (x, y) = map.Position(c);
                if (map.Line(f, x, y, tx, ty, range, out int len) && (len >> 1) <= range)
                    inReach = true;
            }
            if (!inReach)
            {
                var list = EnemiesWithin(f, c, range);
                if (list.Count == 0)
                {
                    if (AcquireTarget(f, c, 0xFF, false, false))
                        MoveStep(f, c);
                    else
                        done = FinishRound(f, c);
                    continue;
                }
                target = list[D(f, (byte)list.Count) - 1];
                if (FightRules.HasRangedWeapon(c, f.Items) && !FightRules.HasThrowableWeapon(c, f.Items) && EnemiesWithin(f, c, 1).Count > 0)
                {
                    EquipWeapon(f, c);
                    done = true;
                    continue;
                }
                if (f.Geometry.Distance(c, target!) == 1 || CanTarget(f, c, target))
                    inReach = true;
            }
            if (!inReach || target is null)
                continue;
            rec[CombatRecord.Facing] = (byte)f.Geometry.DirectionToward(c, target); // 0x310D0 [?] drawing; MakeAttack faces anyway
            if (SweepAttack.Try(f, c, target)) // 0xBA3C
            {
                f.EndActions(c);
                done = true;
                continue;
            }
            byte[]? ammo = null;
            if (FightRules.HasRangedWeapon(c, f.Items))
            {
                FightRules.FindRangedAmmo(c, f.Items, out ammo);
                if (FightRules.HasThrowableWeapon(c, f.Items) && f.Geometry.Distance(c, target) == 1)
                    ammo = null;
            }
            var outcome = f.Attack(c, target, false, ammo);
            if (outcome.ActionsDone)
                cont = false;
            else if (target[FightRules.InPlay] == 0)
                done = true;
        }
        return !cont;
    }

    /// <summary>
    /// 0xB3B2: one step. Key check; movement / 2 ≤ 0 or initiative ≤ 0 → finish; a magic-user (class 5) that is not
    /// fleeing does not advance (finish). Base direction: towards the target (0x14E38), or when fleeing +0x15 = 1d2
    /// and f − ((f + 2) mod 4) / 2 from the area facing f (DS:6AAF), + 4 on the party side, mod 8. Attempts 1–5 with
    /// the row's offsets (<see cref="CanStep"/>); a fleeing creature at the edge tries to flee (0x13465). After five
    /// failures or a step back the way it came (DS:443C) the block count DS:443D rises and the row moves on
    /// (row mod 6 + 1); on the second the target is dropped and searched again (finish when none), on the third the
    /// movement ends. Else the creature faces and steps (<see cref="IFightMoves.Step"/>).
    /// </summary>
    public void MoveStep(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        if (CheckKeystroke(f, c))
            return;
        if ((rec[CombatRecord.Movement] >> 1) <= 0 || rec.InitiativeValue <= 0)
        {
            FinishRound(f, c);
            return;
        }
        if (rec[Fleeing] == 0 && c[Class] == 5)
        {
            FinishRound(f, c);
            return;
        }
        int dir;
        if (rec[Fleeing] == 0)
            dir = rec.Other is { } t ? f.Geometry.DirectionToward(c, t) & 0xFF : 0; // [?] no target: the program reads a nil pointer
        else
        {
            rec[StepRow] = D(f, 2);
            byte facing = f.Memory.Position.Facing;
            byte d = (byte)(facing - (((facing + 2) % 4) >> 1));
            if (c[FightRules.Hostile] == 0)
                d = (byte)(d + 4);
            dir = d % 8;
        }
        int attempt = 1;
        bool done = false;
        for (int g = 0; attempt < 6 && !done; g++)
        {
            if (CanStep(f, c, attempt, dir, out bool edge))
                break;
            if (rec[Fleeing] != 0 && edge)
            {
                done = f.Moves.Flee(f, c);
                if (!done && g > 50) // not the program's: a flee seam that never ends
                    done = true;
            }
            else
                attempt++;
        }
        if (done)
        {
            rec[CombatRecord.Movement] = 0;
            rec[Fleeing] = 0;
            f.EndActions(c);
            return;
        }
        byte newDir = (byte)((dir + Step(rec[StepRow], attempt)) % 8);
        if (attempt == 6 || (newDir + 4) % 8 == _lastDir)
        {
            _blocked++;
            rec[StepRow] = (byte)(rec[StepRow] % 6 + 1);
            if (_blocked > 1)
            {
                rec.Other = null;
                if (_blocked > 2)
                {
                    rec[CombatRecord.Movement] = 0;
                    done = true;
                }
                else if (!AcquireTarget(f, c, 0xFF, true, false))
                    done = FinishRound(f, c);
            }
        }
        if (attempt < 6)
            _lastDir = newDir;
        else
            done = true;
        if (done)
            return;
        rec[CombatRecord.Facing] = newDir; // 0x31232
        // 0x31232 draws the cell again before the attacks of opportunity (DOSBox-X s30: a fleeing spectre's cursor box
        // gone before Bakshi's free attack). [?] Its frame argument here is not read; the Client only drops the box.
        f.State.Posed?.Invoke(c);
        f.Moves.Step(f, c, newDir);
        if (c[FightRules.InPlay] == 0)
            f.EndActions(c);
    }

    private byte Step(byte row, int attempt)
    {
        int i = 5 * row + attempt;
        return i < _steps.Count ? _steps[i] : (byte)0;
    }

    /// <summary>
    /// 0xB235: can the creature step in direction (base + offset of the row, attempt) mod 8? Ground 0 = the edge
    /// (<paramref name="edge"/>, no). Another creature there, or a cost ≥ movement (+6): no. Need = cost × 2, × 3
    /// diagonally; ground 0x1E without one of <see cref="CrossingEffects"/> needs movement + 1. Yes when movement ≥ need.
    /// </summary>
    public bool CanStep(Fight f, EclCharacter c, int attempt, int baseDir, out bool edge)
    {
        edge = false;
        var rec = f.Record(c);
        if (f.Map is not { } map)
            return false;
        int dir = (Step(rec[StepRow], attempt) + baseDir) % 8;
        var (ground, occupant) = map.Look(c, dir);
        if (ground == 0)
        {
            edge = true;
            return false;
        }
        if (occupant is not null)
            return false;
        byte cost = map.MoveCost(ground);
        byte move = rec[CombatRecord.Movement];
        if (cost >= move)
            return false;
        byte need = (byte)(cost * ((dir & 1) != 0 ? 3 : 2));
        if (ground == 0x1E && !CrossingEffects.Any(c.HasEffect))
            need = (byte)(move + 1);
        return move >= need;
    }

    // ---- Items, spells, turning ---------------------------------------------------------------------------------

    /// <summary>
    /// 0xAEAB: should the creature cast this spell now? Priority (spell table byte 13) below the minimum → no. A spell
    /// other than 3 with byte 14 = 0 → yes. Spell 3 with a cure target (0x143F3) → yes. Else the enemies within the
    /// spell's range (0x27741): none → no; area (byte 15) 0 → yes; else no when the area around any of them takes in a
    /// creature not on the enemy side (0xAE15).
    /// </summary>
    public static bool DecideCast(Fight f, EclCharacter c, int spell, int minPriority)
    {
        var (priority, targeted, area) = f.Spells.AiInfo(spell);
        if (priority < minPriority)
            return false;
        if (spell != 3 && targeted == 0)
            return true;
        if (spell == 3 && f.Spells.FindCureTarget(f, c))
            return true;
        var list = EnemiesWithin(f, c, f.Spells.Range(f, c, spell));
        if (list.Count == 0)
            return false;
        if (area == 0)
            return true;
        int side = EnemySide(f.State.Current ?? c);
        foreach (var e in list)
            if (AreaAround(f, e!, area).Any(o => o[FightRules.Hostile] != side))
                return false;
        return true;
    }

    /// <summary>0xAE15's list: the target list 0x3045B around the creature's slot square with size 1 (no cone).</summary>
    private static IReadOnlyList<EclCharacter> AreaAround(Fight f, EclCharacter e, int radius)
    {
        if (f.Geometry is IFightTargetList tl && f.Map is { } map)
        {
            var (x, y) = map.Position(e);
            return tl.Around(x, y, radius);
        }
        return FightSearch.Nearby(f, e, radius); // [?] no combat map: the search from the creature's footprint
    }

    /// <summary>
    /// 0xAFA4: 1d7 (always) passes with priority 7, 6, …; with item use allowed (+2), enemies left and VM $49E5 = 0,
    /// each pass looks at every readied item (0x34) that is not of base slot 11–13 (0x2A255), with 0x3E &lt; 0x80 and a
    /// spell 0x3D &gt; 0 (above 0x38: − 0x17) for one <see cref="DecideCast"/> accepts. The pick is used (0x22AC4).
    /// </summary>
    public bool UseItem(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        byte[]? chosen = null;
        int priority = 7;
        byte n = D(f, 7);
        if (rec[CombatRecord.ItemUseAllowed] != 0 && f.State.TeamCount[EnemySide(c)] > 0 && f.Memory.Read(NoItemUseCell) == 0)
        {
            for (int i = 1; i <= n; i++, priority--)
                foreach (var item in c.Items)
                {
                    if (chosen is not null)
                        break;
                    byte spell = item[0x3D];
                    byte slot = f.Items[item[0x2E], BaseItemTable.Slot];
                    if (slot is > 10 and < 14 || item[0x3E] >= 0x80 || item[CombatRules.ItemEquipped] == 0 || spell == 0)
                        continue;
                    if (spell > 0x38)
                        spell -= 0x17;
                    if (DecideCast(f, c, spell, priority))
                        chosen = item;
                }
        }
        if (chosen is null)
            return false;
        f.Spells.UseItem(f, c, chosen);
        return true;
    }

    /// <summary>
    /// 0xB105: the memorised spells (record 0x17–0x2B, non-zero, in order) when cast is allowed (+1); 1d7 (always).
    /// With spells, an NPC (0x84 &gt; 0x7F) or quick magic on, and enemies left: passes with priority 7, 6, … of up to
    /// three tries of 1d(count) each, until <see cref="DecideCast"/> accepts. The pick is cast with the program's target
    /// choice (0x14C0F, ai = 1); returns its result.
    /// </summary>
    public bool CastSpell(Fight f, EclCharacter c)
    {
        var rec = f.Record(c);
        var spells = new List<byte>();
        if (rec[CombatRecord.CastAllowed] != 0)
            for (int i = 0; i < SpellsCount; i++)
                if (c[SpellsFirst + i] > 0)
                    spells.Add(c[SpellsFirst + i]);
        int chosen = 0, priority = 7;
        byte n = D(f, 7);
        if (spells.Count > 0 && (c[CombatRules.Npc] > 0x7F || f.State.QuickMagic) && f.State.TeamCount[EnemySide(c)] > 0)
            for (int j = 1; j <= n && chosen == 0; j++, priority--)
                for (int k = 1; k < 4 && chosen == 0; k++)
                {
                    byte s = spells[D(f, (byte)spells.Count) - 1];
                    if (DecideCast(f, c, s, priority))
                        chosen = s;
                }
        return chosen > 0 && f.Spells.CastAuto(f, c, chosen);
    }

    /// <summary>0xADC3: +0x11 = 0, a cleric level (0x96, signed) &gt; 0 and an undead target (0x13B4C) → turn (0x13965,
    /// call site 0xAE01).</summary>
    public bool TryTurnUndead(Fight f, EclCharacter c)
    {
        if (f.Record(c)[NoTurning] != 0 || (sbyte)c[ClericLevel] <= 0 || TurnUndead.NextTarget(f, c) is null)
            return false;
        TurnUndead.Run(f, c, TurnUndeadTable);
        return true;
    }

    // ---- Weapons 0xBF8A -----------------------------------------------------------------------------------------

    /// <summary>
    /// 0xBE4D: a weapon's worth: small dice × sides (byte); + 8 × plus when plus &gt; 0; + 2 × small bonus when &gt; 0;
    /// type 0x55 against a target whose byte 0x76 &gt; 0 → 8; ammunition weapons (flags bit 3) + 2 × (rate − 1);
    /// one-handed (byte 1 ≤ 1) + 3. Zero when hands used (0x100) + its hands &gt; 3, when 0x3E = 0x84 and 0x3D AND 15 ≠
    /// the creature's 0xA0, when 0x3D = 0x53, or when cursed (0x36).
    /// </summary>
    public static byte WeaponPriority(Fight f, EclCharacter c, byte[] item)
    {
        int t = item[0x2E];
        byte B(int k) => f.Items[t, k];
        byte p = (byte)(B(9) * B(10));
        if ((sbyte)item[CombatRules.ItemPlus] > 0)
            p = (byte)(p + (sbyte)item[CombatRules.ItemPlus] * 8);
        if ((sbyte)B(11) > 0)
            p = (byte)(p + (sbyte)B(11) * 2);
        if (t == 0x55 && f.Record(c).Other is { } o && (sbyte)o[0x76] > 0)
            p = 8;
        if ((B(BaseItemTable.Flags) & 8) != 0)
            p = (byte)(p + (B(BaseItemTable.RateOfFire) - 1) * 2);
        if (B(1) <= 1)
            p = (byte)(p + 3);
        if (c[HandsUsed] + B(1) > 3)
            p = 0;
        if (item[0x3E] == 0x84 && (item[0x3D] & 0xF) != c[Alignment])
            p = 0;
        if (item[0x3D] == 0x53)
            p = 0;
        if (item[0x36] != 0)
            p = 0;
        return p;
    }

    /// <summary>The hands-used byte 0x100 as START 0x1758 rebuilds it: Σ base byte 1 of the readied items (byte).</summary>
    public static void RecalcHands(Fight f, EclCharacter c)
    {
        byte h = 0;
        foreach (var it in c.Items)
            if (it[CombatRules.ItemEquipped] != 0)
                h = (byte)(h + f.Items[it[0x2E], 1]);
        c[HandsUsed] = h;
    }

    private byte Hands(Fight f, byte[] item) => f.Items[item[0x2E], 1];

    /// <summary>
    /// 0xBF8A: the hands of the weapon and off-hand item are taken off 0x100; over the items usable by the class
    /// (base byte 13 AND 0xB0): best ranged weapon (flags bit 3 or 4; worth above 1), best melee weapon (flags bit 3
    /// clear; worth above the natural attack 0xA3 × 0xA5 + 2 × 0xA7), best shield (base slot 1; plus + 1, or 0 when the
    /// plus is negative, above 0). The ranged weapon is chosen when its worth &gt; melee worth / 2, it has ammunition
    /// and it is thrown or no enemy is adjacent; else the melee one. A weapon that is not the chosen one and not cursed
    /// is unreadied and the chosen readied (0x2250A). Then, with 0x100 &gt; 2 the off-hand item (or, when that is cursed
    /// or missing, the chosen weapon) is unreadied; with 0x100 &lt; 2 a different, non-cursed off-hand item gives way to
    /// the best shield.
    /// </summary>
    public void EquipWeapon(Fight f, EclCharacter c)
    {
        var eq = FightRules.Equipment(c, f.Items);
        if (eq[FightRules.SlotWeapon] is { } w0)
            c[HandsUsed] = (byte)(c[HandsUsed] - Hands(f, w0));
        if (eq[FightRules.SlotOffHand] is { } o0)
            c[HandsUsed] = (byte)(c[HandsUsed] - Hands(f, o0));
        byte[]? ranged = null, melee = null, shield = null;
        byte rangedP = 1, shieldP = 0;
        byte meleeP = (byte)(c[NaturalDice] * c[NaturalSides]);
        if ((sbyte)c[NaturalBonus] > 0)
            meleeP = (byte)(meleeP + (sbyte)c[NaturalBonus] * 2);
        foreach (var it in c.Items)
        {
            int t = it[0x2E];
            bool usable = (f.Items[t, 13] & c[ClassMask]) != 0;
            byte flags = f.Items[t, BaseItemTable.Flags];
            if (f.Items[t, BaseItemTable.Slot] == 0 && usable)
            {
                byte p = WeaponPriority(f, c, it);
                if ((flags & 0x18) != 0 && p > rangedP)
                {
                    ranged = it;
                    rangedP = p;
                }
                if ((flags & 8) == 0 && p > meleeP)
                {
                    melee = it;
                    meleeP = p;
                }
            }
            if (f.Items[t, BaseItemTable.Slot] == 1 && usable)
            {
                byte p = (sbyte)it[CombatRules.ItemPlus] >= 0 ? (byte)((sbyte)it[CombatRules.ItemPlus] + 1) : (byte)0;
                if (p > shieldP)
                {
                    shield = it;
                    shieldP = p;
                }
            }
        }
        bool throwable = ranged is not null && f.Items[ranged[0x2E], BaseItemTable.Range] > 1 &&
                         (f.Items[ranged[0x2E], BaseItemTable.Flags] & 0x14) == 0x14;
        bool hasAmmo = false;
        if (ranged is not null)
        {
            byte fl = f.Items[ranged[0x2E], BaseItemTable.Flags];
            byte[]? ammo = null;
            if ((fl & 0x10) != 0)
                ammo = ranged;
            if ((fl & 8) != 0)
            {
                if ((fl & 1) != 0)
                    ammo = eq[FightRules.SlotArrows];
                if ((fl & 0x80) != 0)
                    ammo = eq[FightRules.SlotBolts];
            }
            hasAmmo = ammo is not null || fl == 0x0A;
        }
        var chosen = ranged is not null && rangedP > meleeP >> 1 && hasAmmo && (throwable || EnemiesWithin(f, c, 1).Count == 0)
            ? ranged : melee;
        bool changed = false;
        var weapon = eq[FightRules.SlotWeapon];
        if (!(weapon is not null && (ReferenceEquals(weapon, chosen) || weapon[0x36] != 0)))
        {
            if (weapon is not null)
                Toggle(f, c, weapon);
            f.RecalculateStats(c); // 0xC35D
            if (FightRules.Equipment(c, f.Items)[FightRules.SlotOffHand] is { } off && off[0x36] == 0)
                c[HandsUsed] = (byte)(c[HandsUsed] - Hands(f, off));
            if (chosen is not null)
                Toggle(f, c, chosen);
            changed = true;
        }
        f.RecalculateStats(c); // 0xC3CD
        FightRules.RecalcAttacksLeft(c, f.Record(c), f.Items, f.State, f.Effects);
        var offHand = FightRules.Equipment(c, f.Items)[FightRules.SlotOffHand];
        bool change = !(offHand is not null && (ReferenceEquals(offHand, shield) || offHand[0x36] != 0));
        if (c[HandsUsed] > 2)
        {
            if (offHand is not null && offHand[0x36] == 0)
                Toggle(f, c, offHand);
            else if (chosen is not null)
                Toggle(f, c, chosen);
            changed = true;
        }
        else if (c[HandsUsed] < 2 && change)
        {
            if (offHand is not null)
                Toggle(f, c, offHand);
            f.RecalculateStats(c); // 0xC4A4
            if (shield is not null)
                Toggle(f, c, shield);
            changed = true;
        }
        f.RecalculateStats(c); // 0xC4C6
        _ = changed; // START 0x1583 redraws the character when something changed
    }

    /// <summary>
    /// 0x2250A for the current creature: a readied item is unreadied unless cursed; an unreadied one is readied when
    /// 0x100 + its hands ≤ 2, its slot (0–8; ring slot 9 → the second ring; arrows 0x49 / bolts 0x1C → slots 11/12)
    /// is free and its class mask fits. Items with 0x3E &gt; 0x7F run their handler (0x2AEEA, <see cref="ItemHandler"/>).
    /// </summary>
    public void Toggle(Fight f, EclCharacter c, byte[] item) => ToggleReady(f, c, item, ItemHandler);

    /// <summary>0x2250A as <see cref="Toggle"/>, with the item handler 0x2AEEA given (null = none). Returns the
    /// refusal: 0 done, 1 class, 2 slot taken, 3 hands full (the message 0x2250A shows), 4 cursed.</summary>
    public static int ToggleReady(Fight f, EclCharacter c, byte[] item, Action<EclCharacter, byte[], bool>? itemHandler)
    {
        bool magic = item[0x3E] > 0x7F;
        if (item[CombatRules.ItemEquipped] != 0)
        {
            if (item[0x36] != 0)
                return 4;
            item[CombatRules.ItemEquipped] = 0;
            if (magic)
                itemHandler?.Invoke(c, item, true);
            return 0;
        }
        var eq = FightRules.Equipment(c, f.Items);
        int t = item[0x2E];
        int result = 0;
        if (c[HandsUsed] + f.Items[t, 1] > 2)
            result = 3;
        byte slot = f.Items[t, BaseItemTable.Slot];
        if (slot <= 8 && eq[slot] is not null)
            result = 2;
        else if (slot == 9 && eq[10] is not null)
            result = 2;
        if (t == 0x49 && eq[FightRules.SlotArrows] is not null)
            result = 2;
        if (t == 0x1C && eq[FightRules.SlotBolts] is not null)
            result = 2;
        if ((f.Items[t, 13] & c[ClassMask]) == 0)
            result = 1;
        if (result != 0)
            return result; // messages only
        item[CombatRules.ItemEquipped] = 1;
        if (magic)
            itemHandler?.Invoke(c, item, false);
        return 0;
    }
}
