using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// Spell routines 34–48, 51 and 55–67 (table DS:6B84 + 4·id, GAME.OVR file offsets), docs/re/spells-effects.md
// "Routines". The targets are in Targets (DS:6C95), DS:681B = the spell, the aimed square in Aim (DS:6DB9/6DBA).
public sealed partial class FightSpells
{
    /// <summary>VM $49E6 (word [DS:49D2]+0x1CC): 0 makes Fireball rebuild its target list with range 2 (0x29683).
    /// $49E6 = 0 is the wilderness overland (the script init sets 1; the wilderness scripts write 0/1; save-format.md),
    /// so outdoors Fireball reaches range 2 (a 5-square diameter).</summary>
    public const ushort FireballRangeCell = 0x49E6;

    /// <summary>The program's DS:4844 / DS:4845, shared by every fight and camp of a run (<see cref="EclMemory.SpellGlobals"/>;
    /// FightKit and the camp hand the game's in).</summary>
    public SpellGlobals Globals { get; set; } = new();

    /// <summary>DS:4845: the exceptional Strength Enlarge gives (0x282BD), also written by Sleep (each target's cost,
    /// 0x2857F–0x285CF) and Dispel Magic (an effect's level, 0x29420).</summary>
    public byte Ds4845 { get => Globals.Ds4845; set => Globals.Ds4845 = value; }

    private bool MoreRoutine(int spell, EclCharacter caster)
    {
        switch (spell)
        {
            case 34: // 0x28B29 Stinking Cloud
                StinkingCloud(spell, caster);
                return true;
            case 35: // 0x28F59 Strength
                Strength(spell);
                return true;
            case 36: // 0x29087 Animate Dead
                AnimateDead(spell, caster);
                return true;
            case 37: // 0x2922F Cure Blindness: the first target loses Blindness 0x21 (message) → message
                if (First is { } t37 && RemoveWithMessage(t37, 0x21))
                    Event?.Invoke(t37, spell, EffectEvent.SpellBlindnessCured);
                return true;
            case 38 or 44 or 45 or 61 or 63: // 0x29275, 0x2961C, 0x29655, 0x29FB1, 0x2A042: plain resolution
                Resolve(spell, 0, false, 0, 0, caster);
                return true;
            case 39: // 0x29347 Cure Disease (0x292A2; its result is not used)
                CureDisease();
                return true;
            case 40: // 0x29364 Cause Disease: resolution with removal on
                Resolve(spell, 0, true, 0, 0, caster);
                return true;
            case 41 or 46: // 0x2939D Dispel Magic
                DispelMagic(spell);
                return true;
            case 42: // 0x294EE Prayer: strength = casting level + the current character's hostile byte × 16 (byte)
                Resolve(spell, (byte)(CasterLevel(spell) + ((sbyte)(Current?[FightRules.Hostile] ?? 0) << 4)), false, 0, 0,
                    caster);
                return true;
            case 43: // 0x29558 Remove Curse
                RemoveCurse(spell);
                return true;
            case 47 or 64: // 0x29683 Fireball (64: the wand)
                Fireball(spell, caster);
                return true;
            case 48: // 0x298AA Haste: the caster's side; a Slow (0x2A) on a target is cancelled instead
                OneSideRate(spell, 0x2A, caster[FightRules.Hostile], caster);
                return true;
            case 51: // 0x29BCF Lightning Bolt: (casting level)d6 (0x2BCCD: DS:681C not set), the aimed square, then the bolt
            {
                byte d = Dice(CasterLevel(spell), 6);
                BoltHit(Aim.X, Aim.Y, d, 4);
                Bolt(8, d, 4, true);
                return true;
            }
            case 55: // 0x29C21 Slow: the other side (START 0x2F57); a Haste (0x27) on a target is cancelled instead
                OneSideRate(spell, 0x27, caster[FightRules.Hostile] == 0 ? (byte)1 : (byte)0, caster);
                return true;
            case 56: // 0x29C5B Restoration
                Restoration(spell);
                return true;
            case 57: // 0x29E1A: the first target losing a Slow (0x2A, message) ends it; else plain resolution
                if (!(First is { } t57 && RemoveWithMessage(t57, 0x2A)))
                    Resolve(spell, 0, false, 0, 0, caster);
                return true;
            case 58: // 0x29E65
                Spell58();
                return true;
            case 59: // 0x29EFD: Strength 21 (0x2C046, message when it rose), effect 0x26 for the duration, removal on
            {
                if (First is not { } t59)
                    return true;
                if (Effects.RaiseStrength(t59, 0x15, 0, out byte code))
                    Event?.Invoke(t59, spell, EffectEvent.SpellStrengthened);
                EffectHandlers.AddEffect(t59, 0x26, Duration(spell), code, true);
                return true;
            }
            case 60: // 0x29F65: 1d6 + 20 on the aimed square, then a bolt of 3 squares doing 20 that does not rebound
                BoltHit(Aim.X, Aim.Y, (byte)(Dice(1, 6) + 0x14), 4);
                Bolt(3, 0x14, 4, false);
                return true;
            case 62: // 0x29FE8: the first target healed 2d4 + 2 (no bypass); message when healed
                if (First is { } t62 && Effects.RestoreHitPoints(t62, (byte)(Dice(2, 4) + 2), false))
                    Event?.Invoke(t62, spell, EffectEvent.Healed);
                return true;
            case 65: // 0x2A070: damage 2d4 + 2 (count to DS:681C), element 8
            {
                F.DamageDice = 2;
                byte d = (byte)(Dice(2, 4) + 2);
                Resolve(spell, 0, false, d, 8, caster);
                return true;
            }
            case 66: // 0x2A0AA: nothing
                return true;
            case 67: // 0x2A0BA: resolution with strength 255, removal on
                Resolve(spell, 0xFF, true, 0, 0, caster);
                return true;
        }
        return false;
    }

    // ---- 34 Stinking Cloud ---------------------------------------------------------------------------------------

    /// <summary>
    /// 0x28B29: DS:6820 = 1. A new node goes at the end of the cloud list DS:6DBB, numbered by the clouds the current
    /// character has there, not counting the list's last node (the walk stops on it: a quirk, so two clouds can share a
    /// number and handler 40 then clears the older one first); the current character gets effect 0x28 for (casting level) minutes, param =
    /// number × 16 + level, removal on (handler 40 clears the cells). For the four cells (the aimed square and its east,
    /// south-east and south neighbours, DS:290D): the slot there is kept; a cell with terrain (0x30BF8) whose move cost
    /// is below 0xFF is clouded. The terrain kept under a cell is its own, except under a cloud (0x1E) that of the first
    /// other node over that cell whose kept terrain is not 0x1E, and under a body (0x1F) the terrain of the last body
    /// entry on it. Clouded cells become 0x1E. (Message, map redraw.) Then each creature found in the cells gets the
    /// terrain check (0x2B9D1).
    /// </summary>
    private void StinkingCloud(int spell, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        byte lvl = CasterLevel(spell);
        var cur = Current ?? caster;
        // The walk to the list's end counts every node but the last one (it stops on the node whose next is nil).
        int n = Effects.Clouds.Take(Math.Max(0, Effects.Clouds.Count - 1)).Count(k => ReferenceEquals(k.Caster, cur));
        EffectHandlers.AddEffect(cur, 0x28, lvl, (byte)(n * 16 + lvl), true);
        var cloud = new StinkingCloud { Caster = cur, Index = n, X = (sbyte)(byte)Aim.X, Y = (sbyte)(byte)Aim.Y };
        Effects.Clouds.Add(cloud);
        if (_fight?.Geometry is not CombatMap map)
        {
            Diag("[?] Stinking Cloud needs the combat map");
            return;
        }
        var slots = new int[5];
        for (int i = 1; i <= 4; i++)
        {
            var (x, y) = cloud.Cell(i);
            var (slot, terrain) = map.Contents(x, y);
            slots[i] = slot;
            cloud.Used[i] = terrain > 0 && map.Tables.Cost(terrain) < 0xFF;
            if (terrain == 0x1E)
            {
                foreach (var other in Effects.Clouds)
                {
                    if (ReferenceEquals(other, cloud))
                        break;
                    for (int k = 1; k <= 4; k++)
                        if (other.Used[k] && other.Cell(k) == (x, y) && other.Under[k] != 0x1E)
                            terrain = other.Under[k];
                }
            }
            else if (terrain == 0x1F)
            {
                foreach (var b in map.Bodies)
                    if (b is not null && b.X == x && b.Y == y)
                        terrain = b.Terrain;
            }
            cloud.Under[i] = (byte)terrain;
            if (cloud.Used[i])
                map.Arena[x, y] = 0x1E;
        }
        Event?.Invoke(cur, spell, EffectEvent.SpellCloudFormed);
        for (int i = 1; i <= 4; i++)
            if (slots[i] > 0 && map.SlotCreature(slots[i]) is { } c)
                Effects.TerrainCheck(c);
    }

    // ---- 35 Strength, 36 Animate Dead -----------------------------------------------------------------------------

    /// <summary>
    /// 0x28F59: gain = 1d4 for a magic-user level (0x9B) above 0, then 1d6 for a cleric (0x96) or thief (0x9C) level,
    /// then 1d8 for a fighter level (0x98), each roll replacing the last ([?] with none of them the gain is an
    /// uninitialised byte; 0 here). Strength + gain above 18: a fighter gets 18 / ((Str + gain − 18) × 10 + its
    /// exceptional Strength, at most 100), others 18 / 0. 0x2C046 raises the first target (message when it rose); effect
    /// 0x26 for the duration with the returned code, removal on.
    /// </summary>
    private void Strength(int spell)
    {
        if (First is not { } t)
            return;
        byte gain = 0;
        bool rolled = false;
        if ((sbyte)t[MageLevel] > 0)
        {
            gain = Dice(1, 4);
            rolled = true;
        }
        if ((sbyte)t[ClericLevel] > 0 || (sbyte)t[FightRules.ThiefLevel] > 0)
        {
            gain = Dice(1, 6);
            rolled = true;
        }
        if ((sbyte)t[EffectHandlers.FighterLevel] > 0)
        {
            gain = Dice(1, 8);
            rolled = true;
        }
        if (!rolled)
            Diag("[?] Strength on a creature with no class level adds an uninitialised byte; 0 taken");
        byte str = (byte)(t[EffectHandlers.Str] + gain);
        byte exc = 0;
        if (str > 0x12)
        {
            if ((sbyte)t[EffectHandlers.FighterLevel] > 0)
            {
                exc = (byte)((str - 0x12) * 10 + t[EffectHandlers.StrExc]);
                if (exc > 0x64)
                    exc = 0x64;
            }
            str = 0x12;
        }
        if (Effects.RaiseStrength(t, str, exc, out byte code))
            Event?.Invoke(t, spell, EffectEvent.SpellStrengthened);
        EffectHandlers.AddEffect(t, 0x26, Duration(spell), code, true);
    }

    /// <summary>
    /// 0x29087: DS:6820 = 1, the target list emptied, budget = casting level. Walking the whole creature list while the
    /// budget lasts: a dead (status 6) creature of type 0 whose square takes it (0x317E1,
    /// <see cref="EffectHandlers.PutBackOnSquare"/>) is raised: param = casting level + its old hostile byte × 16; hostile =
    /// the current character's, computer-controlled, undead level 0x76 = 2, 0x6B = 0, base movement 6, memorised spells
    /// cleared, NPC byte 0x84 → 0xB2 when above 0x7F else 0xB3, type 4; in a fight its last target is cleared. The budget
    /// drops by one, then 0x2C753 puts it back with its maximum HP; when that works it gets effect 0x20 (permanent,
    /// param, removal on) and status 1 (animated).
    /// </summary>
    private void AnimateDead(int spell, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        int budget = CasterLevel(spell);
        Targets.Clear();
        var cur = Current ?? caster;
        foreach (var p in Effects.List().ToList())
        {
            if (budget <= 0)
                break;
            if (p[EffectHandlers.Status] != 6 || p[EffectHandlers.CreatureType] != 0)
                continue;
            if (!Effects.PutBackOnSquare(p))
                continue;
            byte param = (byte)(CasterLevel(spell) + ((sbyte)p[FightRules.Hostile] << 4));
            p[FightRules.Hostile] = cur[FightRules.Hostile];
            p[EffectHandlers.Auto] = 1;
            p[EffectHandlers.UndeadLevel] = 2;
            p[EffectHandlers.AttackLevel] = 0;
            p[EffectHandlers.MoveBase] = 6;
            for (int i = 0; i < MemorisedSlots; i++)
                p[Memorised + i] = 0;
            p[EffectHandlers.Npc] = p[EffectHandlers.Npc] > 0x7F ? (byte)0xB2 : EffectHandlers.AnimatedMember;
            p[EffectHandlers.CreatureType] = 4;
            if (InCombat)
                F.Record(p).Other = null;
            budget--;
            if (Effects.Revive(p, p[EffectHandlers.HpMax]))
            {
                Effects.AddEffectWithMessage(p, 0x20, 0, param, true, 0, false, EffectEvent.EffectGained,
                    EffectTexts.Animated); // 0x291FF
                p[EffectHandlers.Status] = 1;
            }
        }
    }

    // ---- 39 Cure Disease, 41/46 Dispel Magic, 43 Remove Curse -------------------------------------------------

    /// <summary>
    /// 0x292A2 (also spell 58): under the effect lock, the first target loses (each with a message) Cause Disease 0x22;
    /// 0x2B and then its 0x2C and 0x1F; rot 0x32 and then its 0x39. True when any of 0x22, 0x2B, 0x32 went.
    /// </summary>
    private bool CureDisease()
    {
        if (First is not { } t)
            return false;
        bool any = false;
        Effects.Cells.Lock = 1;
        if (RemoveWithMessage(t, 0x22))
            any = true;
        if (RemoveWithMessage(t, 0x2B))
        {
            any = true;
            Effects.RemoveEffect(t, 0x2C);
            Effects.RemoveEffect(t, 0x1F);
        }
        if (RemoveWithMessage(t, 0x32))
        {
            any = true;
            Effects.RemoveEffect(t, 0x39);
        }
        Effects.Cells.Lock = 0;
        return any;
    }

    /// <summary>
    /// 0x2939D: DS:6820 = 1, level = casting level. Once per target in the list — but always on the <b>first</b>
    /// target (the routine reads DS:6C95 each time, a quirk): each of its effect records with param below 255 (the next
    /// one taken first): DS:4845 = param AND 0x0F (the effect's level), chance = 50 + 5 × (level − that) when the caster
    /// is higher, 50 − 2 × (that − level) when lower, else 50 (a byte); 1d100 ≤ chance → the record goes (0x2AF10).
    /// Any gone → message.
    /// </summary>
    private void DispelMagic(int spell)
    {
        Effects.Cells.MultiTarget = 1;
        byte lvl = CasterLevel(spell);
        int count = Targets.Count;
        for (int i = 1; i <= count; i++)
        {
            if (First is not { } t)
            {
                Diag("[?] Dispel Magic with a dropped first target reads a nil pointer");
                return;
            }
            bool any = false;
            foreach (var node in t.Effects.ToList())
            {
                if (!t.Effects.Contains(node))
                    continue; // [?] the program follows the next pointer it saved even when a handler freed that node
                if (node[3] >= 0xFF)
                    continue;
                Ds4845 = (byte)(node[3] & 0x0F);
                byte chance = lvl > Ds4845 ? (byte)((lvl - Ds4845) * 5 + 0x32)
                    : lvl < Ds4845 ? (byte)(0x32 - (Ds4845 - lvl) * 2) : (byte)0x32;
                Globals.Ds4844 = chance; // 0x29446 / 0x2946B / 0x29470
                if (Dice(1, 100) <= chance)
                {
                    Effects.RemoveEffect(t, node[0], node);
                    any = true;
                }
            }
            if (any)
                Event?.Invoke(t, spell, EffectEvent.SpellMagicDispelled);
        }
    }

    /// <summary>
    /// 0x29558: the first target losing Bestow Curse 0x24 (message) → message and done. Else its first item with the
    /// cursed byte 0x36 set gets 0x36 = 0 (only that one) → message.
    /// </summary>
    private void RemoveCurse(int spell)
    {
        if (First is not { } t)
            return;
        if (RemoveWithMessage(t, 0x24))
        {
            Event?.Invoke(t, spell, EffectEvent.SpellCurseLifted);
            return;
        }
        foreach (var it in t.Items)
            if (it.Length > 0x36 && it[0x36] != 0)
            {
                it[0x36] = 0;
                Event?.Invoke(t, spell, EffectEvent.SpellItemUncursed);
                return;
            }
    }

    // ---- 47/64 Fireball -----------------------------------------------------------------------------------------

    /// <summary>
    /// 0x29683: DS:6820 = 1; dice = 1d3 × 2 + 1 for the wand (64), else the casting level. When VM $49E6 is 0 the target
    /// list is rebuilt around the aimed square: 0x3045B with range 2, no cone, icon size 1, every listed slot in order.
    /// (Drawing 0x310D0.) Then the resolution with (dice)d6 (count to DS:681C), element 9.
    /// </summary>
    private void Fireball(int spell, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        byte dice = spell == 0x40 ? (byte)(Dice(1, 3) * 2 + 1) : CasterLevel(spell);
        if (_fight is { } f && f.Memory.Read(FireballRangeCell) == 0)
        {
            if (f.Geometry is CombatMap map)
            {
                map.BuildTargetList(Aim.X, Aim.Y, 2, 0xFF, 1);
                Targets.Clear();
                foreach (var tg in map.Targets)
                    Targets.Add(map.SlotCreature(tg.Slot));
            }
            else
                Diag("[?] Fireball's range-2 target list (0x3045B) needs the combat map");
        }
        F.DamageDice = dice;
        byte d = Dice(dice, 6);
        Resolve(spell, 0, false, d, 9, caster);
    }

    // ---- 48 Haste, 55 Slow ----------------------------------------------------------------------------------------

    /// <summary>
    /// 0x2977C: DS:6820 = 1, budget = casting level. In list order a target on <paramref name="side"/> while the budget
    /// lasts uses one: when it loses <paramref name="cancels"/> (message) it leaves the list; every other target leaves
    /// too. The resolution runs; then the rate effects (class 0x12) run on each target left.
    /// </summary>
    private void OneSideRate(int spell, byte cancels, byte side, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        int budget = CasterLevel(spell);
        for (int i = 0; i < Targets.Count; i++)
        {
            if (Targets[i] is not { } t)
                continue;
            if (t[FightRules.Hostile] == side && budget > 0)
            {
                budget--;
                if (RemoveWithMessage(t, cancels))
                    Targets[i] = null;
            }
            else
                Targets[i] = null;
        }
        Resolve(spell, 0, false, 0, 0, caster);
        foreach (var t in Targets.ToList())
            if (t is not null)
                Effects.Trigger(FightRules.TriggerRate, t, F);
    }

    // ---- 51 Lightning Bolt, 60 ------------------------------------------------------------------------------------

    /// <summary>
    /// 0x298D4: the cell (0x30BF8): a terrain whose move cost is 0xFF makes the bolt rebound (the result). A creature in
    /// the cell: DS:6819 = 0x0C, it saves vs. <paramref name="saveType"/> (modifier 0) and takes the damage as
    /// non-weapon damage, halved when saved; DS:6819 = 0.
    /// </summary>
    private bool BoltHit(int x, int y, byte damage, int saveType)
    {
        if (_fight?.Geometry is not CombatMap map)
        {
            Diag("[?] Lightning Bolt needs the combat map");
            return false;
        }
        var (slot, terrain) = map.Contents(x, y);
        bool rebound = terrain > 0 && map.Tables.Cost(terrain) == 0xFF;
        if (slot > 0 && map.SlotCreature(slot) is { } c)
        {
            F.DamageFlags = 0x0C;
            bool saved = Effects.SavingThrow(c, saveType, 0);
            Effects.NonWeaponDamage(c, damage, 2, saved);
            F.DamageFlags = 0;
        }
        return rebound;
    }

    /// <summary>
    /// 0x29973: the bolt from the aimed square away from the caster. Nothing when the caster stands on the aimed square.
    /// DS:6820 = 1; left = 2 × <paramref name="length"/> half squares; the slot on the aimed square is the last one hit.
    /// While left: a line (0x2FCF2) from the aimed square towards aim + (aim − caster) × direction × left; step along
    /// it (0x2FE1E) until the line ends, a slot other than the last one hit, a cell off the arena (terrain 0), a terrain
    /// with move cost above 1, or the walked distance reaching what is left. That slot becomes the last one hit; off
    /// the arena nothing is left. (Drawing.) The cell is hit (<see cref="BoltHit"/>). On a rebound the aimed square
    /// moves to the cell; when the first rebound happens within 8 half squares of the caster (a line walked to the
    /// caster) the walked distance gets + 8; the direction reverses, no further bonus, no last slot. Left − the walked
    /// distance (the line's total, not the step since the last hit: a quirk), at least 0. Without a rebound the walk
    /// goes on along the same line, else a new line starts. DS:6820 = 0 at the end.
    /// </summary>
    private void Bolt(int length, byte damage, int saveType, bool reboundBonus)
    {
        if (_fight?.Geometry is not CombatMap map || Current is not { } cur || !map.OnMap(cur))
        {
            Diag("[?] Lightning Bolt needs the combat map");
            return;
        }
        var (sx, sy) = map.Position(cur);
        int lastSlot = map.Contents(Aim.X, Aim.Y).Slot;
        int dir = 1;
        bool bonus = reboundBonus;
        if (sx == Aim.X && sy == Aim.Y)
            return;
        byte left = (byte)(length * 2);
        Effects.Cells.MultiTarget = 1;
        while (left > 0)
        {
            int tx = Aim.X, ty = Aim.Y;
            var line = CombatMap.LineWalk.Start(tx, ty, (short)(tx + (tx - sx) * dir * left), (short)(ty + (ty - sy) * dir * left));
            while (true)
            {
                // 0x29A68: the flight's start [bp−0x37] / [bp−0x38] is the line's current cell, set on entry (the line's
                // start) and again each time the walk goes on after a hit (jmp 0x29A68 at 0x29BBE): the cell last hit.
                int fx = line.Cx, fy = line.Cy;
                int slot = 0, terrain = 0;
                while (true)
                {
                    bool ok = line.Next();
                    (slot, terrain) = map.Contents(line.Cx, line.Cy);
                    if (!ok || (slot > 0 && slot != lastSlot) || terrain == 0 || map.Tables.Cost(terrain) > 1)
                        break;
                    if (line.Dist >= left)
                        break;
                }
                lastSlot = slot;
                if (terrain == 0)
                    left = 0;
                // 0x29AEC–0x29B02: the moving icon (cell 0x13 = COMSPR 6, 0x2997C) from the cell the walk went on from
                // ([bp−0x37], [bp−0x38]) to the cell about to be hit, 4 frames, 50 ms (DOSBox-X s27: the third flight of a
                // breath started on the second's end square, the view centred on the midpoint).
                F.Flight?.Invoke(FlightAnimation.Bolt(fx, fy, line.Cx, line.Cy));
                bool rebound = BoltHit(line.Cx, line.Cy, damage, saveType);
                if (rebound)
                {
                    Aim = (line.Cx, line.Cy);
                    var back = CombatMap.LineWalk.Start(Aim.X, Aim.Y, sx, sy);
                    while (back.Next())
                    {
                    }
                    if (bonus && back.Dist <= 8)
                        line.Dist += 8;
                    dir = -dir;
                    bonus = false;
                    lastSlot = 0;
                }
                left = line.Dist >= left ? (byte)0 : (byte)(left - line.Dist);
                if (rebound || left == 0)
                    break;
            }
        }
        Effects.Cells.MultiTarget = 0;
    }

    /// <summary>
    /// The breath of effect handler 88 (0x2A0F1, in this unit), after its message: the target pick (DS:6B84, 0x148BE in a
    /// fight) for spell 0x33 with ai = 1 (<see cref="PickAuto"/>; its picked flag DS:70C6 is not read); the aimed
    /// square DS:6DB9 / 6DBA becomes the breather's square + the sign of (aim − square) on each axis (0x2FB52), and on
    /// an axis where that is the square + 1 one more (as read: two squares toward positive x / y, one toward negative);
    /// invisibility off (0x2BEBC); the moving icon (0x257E(cell 0x13), 4 frames, 50 ms, 0x2A1E4) from the breather to
    /// the aim; <see cref="BoltHit"/> on the aim and the bolt (<see cref="Bolt"/>, length 10, no rebound bonus), both
    /// with damage = the breather's byte 0x32 and save type 3. The bolt starts from the current creature (DS:5D92).
    /// [?] When the pick aims nowhere the aim stays on the breather: the bolt hit lands on its own slot (as read).
    /// </summary>
    internal void LightningBreath(Fight fight, EclCharacter c)
    {
        Bind(fight);
        if (fight.Geometry is not CombatMap map || !map.OnMap(c))
        {
            Diag("[?] handler 88's breath needs the combat map");
            return;
        }
        var (px, py) = map.Position(c);
        (TargetPick ?? PickAuto)(fight, c, 0x33); // 0x2A14C–0x2A157: lcall [DS:6B84]
        int ax = (sbyte)(byte)(px + Math.Sign(Aim.X - px)), ay = (sbyte)(byte)(py + Math.Sign(Aim.Y - py));
        if (ax == px + 1)
            ax++;
        if (ay == py + 1)
            ay++;
        Aim = (ax, ay);
        Effects.RemoveInvisibility(c); // 0x2A1C1
        F.Flight?.Invoke(FlightAnimation.Bolt(px, py, ax, ay)); // 0x2A1C6–0x2A1E4
        byte damage = c[MaxHp];
        BoltHit(ax, ay, damage, 3); // 0x2A202
        Bolt(10, damage, 3, false); // 0x2A217
    }

    // ---- 56 Restoration, 58 ---------------------------------------------------------------------------------------

    /// <summary>
    /// 0x29C5B: a first target with drained levels (0x74 &gt; 0): g = HP drained 0x75 ÷ 0x74; max HP, HP and rolled HP
    /// 0xB1 + g, 0x75 − g, 0x74 − 1. Over classes 0..7: a level above 0 and not above the best so far (from 13) whose
    /// next level's experience (DS:409B, level + 1) is above 0 and below the lowest so far (from 10,000,000), and that the
    /// race allows (START 0x3AF7), is chosen; it gains a level and experience 0xAC rises to that threshold when below.
    /// Level stats (0x2AA87) and a message. With no class chosen (a monster: all levels 0, drain bytes 255) the routine
    /// adds 1 at its uninitialised index [bp−0xC] (and experience below 10,000,000 still rises to it). DOSBox-X showed
    /// that index as 8 in all three samples (a scroll used in a fight on a party member and on a goblin guard, and
    /// outside a fight), so record 0x9E (gender) + 1: <see cref="RestorationResidueIndex"/>. A party member at its race
    /// limit (START 0x3AF7 is an equality on the current level) or its class's top level takes the same path; checked
    /// with the real limit and level stats by RestorationProgramTests (spells-effects.md "Restoration at the race limit"),
    /// and in DOSBox-X on two such party members (index 8 again, fixture <c>restoration_limit</c>).
    /// </summary>
    /// <summary>
    /// The class index Restoration raises when no class qualifies: residue in the program ([bp−0xC] of 0x29C5B), 8 in
    /// every DOSBox-X sample (fixtures <c>fight_restoration</c>, <c>restoration_limit</c>), which lands on record
    /// 0x96 + 8 = 0x9E (gender).
    /// Signed in the program (cwde), so any value would address 0x16..0x115.
    /// </summary>
    public const int RestorationResidueIndex = 8;

    private void Restoration(int spell)
    {
        if (First is not { } t || t[0x74] == 0)
            return;
        byte g = (byte)(t[0x75] / t[0x74]);
        t[EffectHandlers.HpMax] += g;
        t[FightRules.Hp] += g;
        t[EffectHandlers.HpRolled] += g;
        t[0x75] -= g;
        t[0x74]--;
        int best = 0x0D, cls = -1, min = 0x989680;
        for (int k = 0; k < 8; k++)
        {
            int lvl = (sbyte)t[EffectHandlers.ClassLevels + k];
            if (lvl <= 0 || lvl > best)
                continue;
            int thr = Effects.Experience(k, lvl + 1);
            if (thr <= 0 || thr >= min)
                continue;
            if (Effects.RaceLevelLimit is { } lim ? lim(t, k) : NoRaceLimit())
                continue;
            best = lvl;
            cls = k;
            min = thr;
        }
        // No class: the program's index is stack residue; DOSBox gave 8 on every sampled path (sampled, not proven
        // for every path; spells-effects.md "Restoration").
        t[EffectHandlers.ClassLevels + (cls < 0 ? RestorationResidueIndex : cls)]++;
        int xp = t[0xAC] | (t[0xAD] << 8) | (t[0xAE] << 16) | (t[0xAF] << 24);
        if (xp < min)
        {
            t[0xAC] = (byte)min;
            t[0xAD] = (byte)(min >> 8);
            t[0xAE] = (byte)(min >> 16);
            t[0xAF] = (byte)(min >> 24);
        }
        if (Effects.LevelStats is { } ls)
            ls(t);
        else
            Diag("[?] level stats 0x2AA87 not modelled");
        Event?.Invoke(t, spell, EffectEvent.SpellLevelRestored);
    }

    private bool NoRaceLimit()
    {
        Diag("[?] the race level limit (START 0x3AF7) not modelled; no class stopped");
        return false;
    }

    /// <summary>
    /// 0x29E65: the first target losing poison 0x37 (message) → under the lock its first 0x16 goes. Else Cure Disease
    /// (0x292A2); nothing cured → healed 1d4 + 8 (no bypass), message when healed.
    /// </summary>
    private void Spell58()
    {
        if (First is not { } t)
            return;
        if (RemoveWithMessage(t, 0x37))
        {
            Effects.Cells.Lock = 1;
            Effects.RemoveEffect(t, 0x16);
            Effects.Cells.Lock = 0;
            return;
        }
        if (CureDisease())
            return;
        if (Effects.RestoreHitPoints(t, (byte)(Dice(1, 4) + 8), false))
            Event?.Invoke(t, 58, EffectEvent.Healed);
    }
}

/// <summary>
/// Program globals of the spell routines that are never reset by a fight or a camp: DS:4844 (Enlarge's Strength 18,
/// Sleep's 4d4 budget, Dispel Magic's scratch) and DS:4845 (Enlarge's exceptional part, Sleep's last target cost,
/// Dispel Magic's last effect level). Both lie in the uninitialised data (the run-time does not clear it): the value
/// before the first writer is `[?]` (0 here, as DOSBox-X's fresh memory).
/// </summary>
public sealed class SpellGlobals
{
    public byte Ds4844 { get; set; }
    public byte Ds4845 { get; set; }
}
