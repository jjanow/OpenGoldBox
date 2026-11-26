using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Moving on the combat map (stub 0x68 unit: guards 0x12E22, one square 0x12F0D, free attacks 0x130AD, flee 0x13465;
/// docs/re/combat-map.md "Moves"). <see cref="Step"/> is the sequence the player's move menu (0xA3CF–0xA512) and the
/// AI (0xB663–0xB6E2) share; what to do with a creature or the map edge in the way is the caller's (attack, or the
/// flee question → <see cref="Flee"/>).
/// </summary>
public sealed class FightMoves(CombatMap map) : IFightMoves
{
    public CombatMap Map { get; } = map;

    /// <summary>Effect ids that keep a creature from making free attacks (START 0x2D39 tests at 0x13294, 0x132AE).</summary>
    public const byte EffectNoFreeAttack1 = 0x4B, EffectNoFreeAttack2 = 0x4A;
    /// <summary>Effect trigger classes of the "can attack" test 0x13880.</summary>
    public const int TriggerCanBeTargeted = 1, TriggerCanTarget = 0;

    /// <summary>
    /// One step in direction <paramref name="direction"/>, as the player's move menu makes it (0xA3CF–0xA512):
    /// <list type="number">
    /// <item>the creature faces that way (SetCreatureFacing 0x31232, combat record +9);</item>
    /// <item>the step is looked at (<see cref="CombatMap.Probe"/>): another creature there → <see cref="StepResult.Blocked"/>
    /// (the menu attacks it instead); the arena edge (terrain 0) → the flee (<see cref="Flee"/>, 0x13465, which ends the
    /// actions): <see cref="StepResult.Fled"/> when the creature got away, else Blocked; terrain whose move cost is above
    /// the movement left (+6) → Blocked (the menu's refusal);</item>
    /// <item>the rest is <see cref="MoveAfterChecks"/>.</item>
    /// </list>
    /// The AI (0xB235, 0xB663–0xB6E2) tests the square itself and with a different rule (a step only when the terrain
    /// cost is strictly below +6), and flees through 0x13465 on its own choice: it should call
    /// <see cref="MoveAfterChecks"/> and <see cref="Flee"/>. The menu also ends the actions of a creature helpless after
    /// the step (START 0x1713, 0xA4F2); the caller checks <see cref="FightRules.IsHelpless"/> for that.
    /// </summary>
    public StepResult Step(Fight fight, EclCharacter c, int direction)
    {
        var rec = fight.Record(c);
        rec[CombatRecord.Facing] = (byte)direction;
        var (slot, terr) = Map.Probe(c, direction);
        if (slot > 0)
            return StepResult.Blocked;
        if (terr == 0)
            return Flee(fight, c) ? StepResult.Fled : StepResult.Blocked;
        if (Map.Tables.Cost(terr) > rec[CombatRecord.Movement])
            return StepResult.Blocked;
        return MoveAfterChecks(fight, c, direction);
    }

    /// <summary>
    /// The calls both the menu (0xA48F–0xA4ED) and the AI (0xB678–0xB6E2) make once a step is allowed: free attacks from
    /// the enemies the step leaves (<see cref="AttacksOfOpportunity"/>); a creature out of play after them ends its
    /// actions (START 0x31D7) → <see cref="StepResult.Dropped"/> (no terrain effects); the move (<see cref="MoveOne"/>),
    /// which can still fail for want of movement (cost 2, 3 diagonally, above +6: +6 = 0) after the free attacks were
    /// taken → <see cref="StepResult.NoMovement"/>; out of play after it (the guards' attacks) → actions end, Dropped;
    /// then in every case the terrain effects (0x2B9D1, <see cref="IFightEffects.Terrain"/>).
    /// </summary>
    public StepResult MoveAfterChecks(Fight fight, EclCharacter c, int direction)
    {
        var rec = fight.Record(c);
        AttacksOfOpportunity(fight, c, direction);
        if (c[FightRules.InPlay] == 0)
        {
            FightRules.EndActions(rec);
            return StepResult.Dropped;
        }
        bool moved = MoveOne(fight, c, direction);
        var result = moved ? StepResult.Moved : StepResult.NoMovement;
        if (c[FightRules.InPlay] == 0)
        {
            FightRules.EndActions(rec);
            result = StepResult.Dropped;
        }
        fight.Effects.Terrain(c, fight.State);
        return result;
    }

    /// <summary>
    /// 0x12F0D: cost 3 for a diagonal (odd direction), else 2. Cost above the movement left (+6): +6 = 0 and no move
    /// (false). Else +6 − cost, the slot moves one cell, the grid is rebuilt, the flank counters +0x0F and +0x12 = 0,
    /// the guards attack (<see cref="GuardsAttack"/>), and a creature then out of play or helpless has movement 0. The
    /// terrain's own cost only gates the step (the menu's test); it is not spent. The step is drawn in
    /// two halves: <see cref="Fight.StepDrawing"/> before the slot moves (old-square scroll, old cell redrawn) and
    /// <see cref="Fight.StepDrawn"/> after the grid rebuild (new-square scroll), before the step sound (0x1306D) and the
    /// guards.
    /// Every branch run under emulation (por_round_oracle.py, RoundProgramTests).
    /// </summary>
    public bool MoveOne(Fight fight, EclCharacter c, int direction)
    {
        var rec = fight.Record(c);
        byte cost = (byte)((direction & 1) != 0 ? 3 : 2);
        if (cost > rec[CombatRecord.Movement])
        {
            rec[CombatRecord.Movement] = 0;
            return false;
        }
        rec[CombatRecord.Movement] -= cost;
        int slot = Map.Slot(c);
        var (x, y) = Map.SlotPosition(slot);
        int nx = (sbyte)(byte)(x + CombatTables.DirDx[direction & 0xFF]), ny = (sbyte)(byte)(y + CombatTables.DirDy[direction & 0xFF]);
        fight.StepDrawing?.Invoke(c, (nx, ny)); // 0x12FB4–0x13011: old-square scroll (AI), old cell redrawn
        Map.SetSlotPosition(slot, nx, ny);
        Map.Rebuild();
        fight.StepDrawn?.Invoke(c, (x, y)); // 0x1303F–0x1304E: new-square scroll, before the sound and the guards
        rec[CombatRecord.FlankCount] = 0;
        rec[CombatRecord.FlankSpread] = 0;
        fight.State.Sound.Cue(Sound.SoundCue.Step); // 0x1306D
        GuardsAttack(fight, c);
        if (c[FightRules.InPlay] == 0 || FightRules.IsHelpless(c))
            rec[CombatRecord.Movement] = 0;
        return true;
    }

    /// <summary>
    /// 0x12E22, after a move: the adjacent enemies (<see cref="CombatMap.CountTargets"/>, range 1) in list order, while
    /// the mover is in play: one on guard (+7 ≠ 0) and not helpless leaves guard (+7 = 0) and attacks it: the defender
    /// notes the attack (0x13FF1) and MakeAttack (0x1407F) — <see cref="Fight.Attack"/>.
    /// </summary>
    public void GuardsAttack(Fight fight, EclCharacter c)
    {
        int n = Map.CountTargets(c, 1);
        var slots = Map.PotentialTargets.ToArray();
        for (int i = 0; i < n; i++)
        {
            if (c[FightRules.InPlay] == 0)
                continue;
            if (Map.SlotCreature(slots[i]) is not { } guard)
                continue;
            var g = fight.Record(guard);
            if (g[CombatRecord.Guarding] == 0 || FightRules.IsHelpless(guard))
                continue;
            fight.GuardAttacking?.Invoke(c); // 0x12E9E–0x12EBC: ScrollView(mover's square, 2, 8)
            g[CombatRecord.Guarding] = 0;
            fight.Attack(guard, c);
        }
    }

    /// <summary>
    /// 0x130AD, before a step: the adjacent enemies now (range 1), minus those still adjacent from the cell one step
    /// on (the slot is moved there for the count and back). Each remaining one, in order, while the mover is in play,
    /// makes one free attack when it is not helpless, may attack the mover (<see cref="CanTarget"/>), carries neither
    /// effect 0x4B nor 0x4A, and has initiative &gt; 0, or was not attacked this turn (+0x0F = 0), or has the mover in
    /// the cone of one of its facing − 2 … facing + 2 (tried in that order). The attack: phase 2 when 0xA1 = 0 else 1,
    /// then the last phase (1, then 2) with attacks left (0x112 + phase) wins; with none left that phase gets 1; +4 =
    /// phase; MakeAttack from behind with no weapon (<see cref="Fight.MakeAttack"/>: the mover does not note the
    /// attack (no 0x13FF1) and does not turn); the attacker's +0x0A is put back afterwards. MakeAttack ends the
    /// attacker's actions when its attacks are used up (START 0x31D7), even out of its turn.
    /// </summary>
    public void AttacksOfOpportunity(Fight fight, EclCharacter c, int direction)
    {
        int slot = Map.Slot(c);
        int n = Map.CountTargets(c, 1);
        if (n == 0)
            return;
        var candidates = Map.PotentialTargets.Take(n).ToArray();
        var (x, y) = Map.SlotPosition(slot);
        int dx = CombatTables.DirDx[direction & 0xFF], dy = CombatTables.DirDy[direction & 0xFF];
        Map.SetSlotPosition(slot, (sbyte)(byte)(x + dx), (sbyte)(byte)(y + dy));
        int after = Map.CountTargets(c, 1);
        var still = Map.PotentialTargets.Take(after).ToHashSet();
        Map.SetSlotPosition(slot, x, y);
        for (int i = 0; i < n; i++)
            if (still.Contains(candidates[i]))
                candidates[i] = 0;
        for (int i = 0; i < n; i++)
        {
            if (candidates[i] == 0 || c[FightRules.InPlay] == 0)
                continue;
            if (Map.SlotCreature(candidates[i]) is not { } attacker)
                continue;
            if (FightRules.IsHelpless(attacker) || !CanTarget(fight, attacker, c) ||
                attacker.HasEffect(EffectNoFreeAttack1) || attacker.HasEffect(EffectNoFreeAttack2))
                continue;
            var a = fight.Record(attacker);
            byte facing = a[CombatRecord.Facing];
            for (int f = (byte)(facing + 6); f <= (byte)(facing + 10); f++)
            {
                if (!(a.InitiativeValue > 0 || a[CombatRecord.FlankCount] == 0 ||
                      IsInCone(attacker, c, f % 8)))
                    continue;
                int phase = attacker[FightRules.BaseAttacksPrimary] == 0 ? 2 : 1;
                for (int p = 1; p <= 2; p++)
                    if (attacker[FightRules.AttacksLeft + p] > 0)
                        phase = p;
                if (attacker[FightRules.AttacksLeft + phase] == 0)
                    attacker[FightRules.AttacksLeft + phase] = 1;
                a[CombatRecord.AttackPhase] = (byte)phase;
                var saved = a.Other;
                fight.MakeAttack(attacker, c, fromBehind: true);
                a.Other = saved;
                if (c[FightRules.InPlay] != 0)
                    fight.PanelShown?.Invoke(c); // 0x13418–0x13429: START 0x1583 for the mover still in play
                break;
            }
        }
    }

    private bool IsInCone(EclCharacter from, EclCharacter to, int dir)
    {
        var (x1, y1) = Map.Position(from);
        var (x2, y2) = Map.Position(to);
        return CombatMap.IsDirToward(x1, y1, x2, y2, dir);
    }

    /// <summary>
    /// 0x13880: may <paramref name="attacker"/> attack <paramref name="target"/>? No target → false; itself → true.
    /// Else DS:681E = 0, the target's trigger-1 effects, and when still 0 the attacker's trigger-0 effects with its
    /// +0x0A set to the target for the call (put back after); true when DS:681E stayed 0
    /// (<see cref="FightState.TargetBlocked"/>).
    /// </summary>
    public static bool CanTarget(Fight fight, EclCharacter attacker, EclCharacter? target)
    {
        if (target is null)
            return false;
        if (ReferenceEquals(attacker, target))
            return true;
        fight.State.TargetBlocked = 0;
        fight.Effects.Trigger(TriggerCanBeTargeted, target, fight.State);
        if (fight.State.TargetBlocked == 0)
        {
            var rec = fight.Record(attacker);
            var saved = rec.Other;
            rec.Other = target;
            fight.Effects.Trigger(TriggerCanTarget, attacker, fight.State);
            rec.Other = saved;
        }
        return fight.State.TargetBlocked == 0;
    }

    /// <summary>
    /// 0x13465, the flee (the menu's yes to the flee question at the arena edge; the AI's own choice): away when no
    /// enemy is in sight (<see cref="CombatMap.CountTargets"/> with range 255), else when the fastest enemy in play
    /// (0x15103: movement 0x1291E shr 1) is slower than the creature's own movement shr 1, or as fast and 1d2 = 1 (the
    /// one draw). Away: put out of play with status 3, fled (0x2BDF0: its message first, <see cref="FightState.FleeMessage"/>; its icon leaves the map, in play 0, status
    /// 3, HP kept; actions end; its dropped-on-down effects go, 0x2BEF4). Not away: START 0x1693's message (same hook). Either way the actions end
    /// (START 0x31D7). Returns whether it got away (the program returns 0x31D7's 1 either way, so both callers end the
    /// move after one try: the menu and the AI 0xB52D, s52). The team counts are not lowered here (they are recounted).
    /// </summary>
    public bool Flee(Fight fight, EclCharacter c)
    {
        bool away = false;
        if (Map.CountTargets(c, 0xFF) == 0)
            away = true;
        else
        {
            ushort partyMove = fight.Memory.Read(FightRules.PartyMoveCell);
            byte own = (byte)(FightRules.CreatureMovement(c, partyMove, fight.State, fight.Effects) >> 1);
            byte enemy = HighestEnemyMovement(fight, c);
            if (enemy < own)
                away = true;
            else if (enemy == own && FightRules.Dice(fight.Random, 1, 2) == 1)
                away = true;
        }
        // 0x134CE–0x13502: away → 0x2BDF0(creature, 3, CS:0C4F), whose message comes before the status change (nothing
        // at all for a creature already out of play, 0x2BE0C); else the message routine START 0x1693 with CS:0C58.
        if (!away || c[FightRules.InPlay] != 0)
            fight.State.FleeMessage?.Invoke(c, away);
        if (away)
            PutOutOfPlay(fight, c, CreatureStatus.Fled);
        FightRules.EndActions(fight.Record(c));
        return away;
    }

    /// <summary>0x15103: the highest movement (0x1291E shr 1) of the creatures of the other side in play, list order.</summary>
    public static byte HighestEnemyMovement(Fight fight, EclCharacter c)
    {
        byte enemy = c[FightRules.Hostile] == 0 ? (byte)1 : (byte)0;
        ushort partyMove = fight.Memory.Read(FightRules.PartyMoveCell);
        byte best = 0;
        foreach (var e in fight.List)
        {
            if (e[FightRules.Hostile] != enemy || e[FightRules.InPlay] == 0)
                continue;
            byte m = (byte)(FightRules.CreatureMovement(e, partyMove, fight.State, fight.Effects) >> 1);
            if (m > best)
                best = m;
        }
        return best;
    }

    /// <summary>
    /// 0x2BDF0 (the message part aside): a creature in play leaves it: in play 0, status = <paramref name="status"/>,
    /// HP 0 unless the status is 3 (fled), its icon leaves the map, its actions end (START 0x31D7), and it loses its
    /// <see cref="FightRules.DroppedEffects"/> (0x2BEF4). Nothing happens to a creature already out of play.
    /// </summary>
    public void PutOutOfPlay(Fight fight, EclCharacter c, CreatureStatus status)
    {
        if (c[FightRules.InPlay] == 0)
            return;
        c[FightRules.InPlay] = 0;
        c[FightRules.Status] = (byte)status;
        if (status != CreatureStatus.Fled)
            c[FightRules.Hp] = 0;
        Map.TakeOff(c);
        FightRules.EndActions(fight.Record(c));
        foreach (var id in FightRules.DroppedEffects)
            FightRules.RemoveFirstEffect(c, id, fight.Effects);
    }
}
