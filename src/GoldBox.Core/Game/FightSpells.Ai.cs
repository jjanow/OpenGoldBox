using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// The computer's side of casting (docs/re/combat-ai.md "Casts by the AI", "Item use"): the cure-target test 0x143F3,
// the AI branch of the aim 0x14611, the AI's target pick (0x148BE with ai = 1), the cast 0x14C0F / 0x27C38 with ai = 1,
// and the item use 0x22AC4 (shared with the player's Use screen). GAME.OVR file offsets; START = the unpacked START.EXE.

public sealed partial class FightSpells
{
    /// <summary>Creature bytes: maximum hit points 0x32, item readied 0x34, charges / power / kind 0x3C–0x3E,
    /// stack count 0x39 (item record).</summary>
    public const int MaxHp = 0x32, ItemCharges = 0x3C, ItemPower = 0x3D, ItemKind = 0x3E, ItemCount = 0x39;

    /// <summary>DS:6DB9 / DS:6DBA: the square the last aim chose (0x148BE starts it at the caster's square); the same
    /// cell the cast's facing (CastNow) reads.</summary>
    public (int X, int Y) AimedSquare
    {
        get => Aim;
        set => Aim = value;
    }

    // ---- Cure target (0x143F3) --------------------------------------------------------------------------------------

    /// <summary>
    /// 0x143F3: the creature Cure Light Wounds would heal, or null. The nine cells around the caster's square (directions
    /// 0–7, then the square itself, DS:2736 / DS:273F) are looked at (0x30BF8). A creature on one (its slot cell) that is
    /// on the caster's side (0x10E equal) and below its maximum HP (0x11B &lt; 0x32, unsigned) is the best so far when
    /// its HP is below the best's (starting at 255), or when it is the caster itself with HP &lt; max shr 1. An empty
    /// cell of ground 0x1F (a body) is matched against the bodies list (DS:66D6 + 7k, k = 1..DS:6715): an entry still
    /// set, whose creature's status 0x10C is not 6, 7 or 8 and whose square is this cell; the last match is kept. No
    /// side test for bodies. Result: the body's creature when one was found and the best HP is 8 or more (or no living
    /// candidate), else the best living one.
    /// </summary>
    public EclCharacter? CureTarget(Fight fight, EclCharacter caster)
    {
        if (fight.Geometry is not CombatMap map)
        {
            Diag("[?] the cure test 0x143F3 needs the combat map");
            return null;
        }
        var (cx, cy) = map.Position(caster);
        EclCharacter? best = null;
        byte bestHp = 0xFF;
        EclCharacter? body = null;
        for (int dir = 0; dir <= 8; dir++)
        {
            int x = (sbyte)(byte)(cx + CombatTables.DirDx[dir]), y = (sbyte)(byte)(cy + CombatTables.DirDy[dir]);
            var (slot, ground) = map.Contents(x, y);
            if (slot > 0)
            {
                if (map.SlotCreature(slot) is not { } c)
                    continue;
                if (c[FightRules.Hostile] != caster[FightRules.Hostile] || c[FightRules.Hp] >= c[MaxHp])
                    continue;
                if (c[FightRules.Hp] < bestHp || (ReferenceEquals(c, caster) && c[FightRules.Hp] < c[MaxHp] >> 1))
                {
                    best = c;
                    bestHp = c[FightRules.Hp];
                }
            }
            else if (ground == ArenaBuilder.Body)
            {
                foreach (var b in map.Bodies)
                    if (b is not null && b.Creature[FightRules.Status] is not (6 or 7 or 8) && b.X == x && b.Y == y)
                        body = b.Creature;
            }
        }
        return bestHp < 8 || body is null ? best : body;
    }

    /// <summary>0x143F3 as the AI's should-cast test 0xAEAB asks it (spell 3): a cure target exists.</summary>
    public bool FindCureTarget(Fight fight, EclCharacter caster) => CureTarget(fight, caster) is not null;

    // ---- The AI's aim (0x14611, ai ≠ 0) -----------------------------------------------------------------------------

    /// <summary>
    /// 0x14611 for the computer (ai ≠ 0), the caster being the current creature. A spell whose table byte 14 is 0: the
    /// target is the caster, or for spell 3 the cure target (0x143F3; none → the aim fails). Otherwise up to 20 tries,
    /// each a target search 0x15FDF(range 0x27741, not relaxed, forget the old target) — so +0x0A of the caster is the
    /// creature found, or nil — rejected when the target list around its square with radius byte 15 (0x3045B, size 1)
    /// holds a creature not on the side the caster fights (START 0x2F57), or when it is helpless (START 0x1713) and the
    /// spell's effect (byte 10) is one of the helpless effects (DS:286C–286F). On success the aimed square
    /// (DS:6DB9 / 6DBA) is the target's square. The program's square mode argument does not matter here.
    /// </summary>
    public bool AimAuto(Fight fight, EclCharacter caster, int spell, out EclCharacter? target)
    {
        Bind(fight);
        target = null;
        var info = Table[spell];
        if (info.Offensive == 0)
        {
            var t = spell == 3 ? CureTarget(fight, caster) : caster;
            if (t is null)
                return false;
            target = t;
            AimedSquare = SquareOf(fight, t);
            return true;
        }
        var rec = fight.Record(caster);
        byte side = FightSearch.EnemySide(caster);
        for (int tries = 20; tries > 0; tries--)
        {
            bool ok = true;
            if (!MonsterAi.AcquireTarget(fight, caster, Range(fight, caster, spell), false, true))
                continue;
            var t = rec.Other!;
            if (info.AiRadius > 0)
            {
                var (tx, ty) = SquareOf(fight, t);
                foreach (var o in Around(fight, (tx, ty), info.AiRadius))
                    if (o[FightRules.Hostile] != side)
                        ok = false;
            }
            if (FightRules.IsHelpless(t) && FightRules.HelplessEffects.Contains(info.Effect))
                ok = false;
            if (ok)
            {
                target = t;
                AimedSquare = SquareOf(fight, t);
                return true;
            }
        }
        return false;
    }

    private static (int X, int Y) SquareOf(Fight fight, EclCharacter c) => fight.Map?.Position(c) ?? (0, 0);

    // ---- The AI's target pick (0x148BE, ai = 1) ---------------------------------------------------------------------

    /// <summary>
    /// 0x148BE with ai = 1: picked = true, list and DS:6820 cleared, aimed square = the caster's. Area mode (byte 6 AND
    /// 0x0F): 0 → the caster. 0x0F → aim (<see cref="AimAuto"/>); then the caster's remembered target +0x0A when set
    /// (the aim of a byte-14 = 0 spell does not set it, so an old target is taken), else the list around the aimed
    /// square with radius (mode shr 4) = 0, DS:6820 = 1; a failed aim → not picked. 8–0x0E → aim; the list around the
    /// aimed square with radius area AND 7, DS:6820 = 1; a failed aim → not picked. 1–7 → (area AND 3) + 1 picks, each
    /// an aim: its creature is added unless already listed; a repeat or a failed aim uses up the pick. Not picked when
    /// the list stays empty.
    /// </summary>
    public bool PickAuto(Fight fight, EclCharacter caster, int spell)
    {
        Bind(fight);
        Targets.Clear();
        Effects.Cells.MultiTarget = 0;
        AimedSquare = SquareOf(fight, caster);
        int area = Table[spell].Area, mode = area & 0x0F;
        if (mode == 0)
        {
            Targets.Add(caster);
            return true;
        }
        if (mode == 0x0F)
        {
            if (!AimAuto(fight, caster, spell, out _))
                return false;
            if (fight.Record(caster).Other is { } other)
                Targets.Add(other);
            else
            {
                Targets.AddRange(Around(fight, AimedSquare, mode >> 4));
                Effects.Cells.MultiTarget = 1;
            }
            return true;
        }
        if (mode >= 8)
        {
            if (!AimAuto(fight, caster, spell, out _))
                return false;
            Targets.AddRange(Around(fight, AimedSquare, area & 7));
            Effects.Cells.MultiTarget = 1;
            return true;
        }
        for (int picks = (area & 3) + 1; picks > 0; picks--)
            if (AimAuto(fight, caster, spell, out var t) && !Targets.Contains(t))
                Targets.Add(t);
        return Targets.Count > 0;
    }

    /// <summary>0x14C0F with ai = 1 (the AI's cast 0xB105): as <see cref="Cast"/>, the target pick being
    /// <see cref="PickAuto"/> when the spell is cast now.</summary>
    public bool CastAuto(Fight fight, EclCharacter caster, int spell) =>
        Begin(fight, caster, spell, () => PickAuto(fight, caster, spell));

    /// <summary>0xACEE: the AI's turn of a creature that began casting: 0x27C38(spell, ai = 1) picks with
    /// <see cref="PickAuto"/>; the actions end (START 0x31D7).</summary>
    public bool ContinueCastingAuto(Fight fight, EclCharacter caster)
    {
        Bind(fight);
        var rec = fight.Record(caster);
        int spell = rec[CombatRecord.SpellCast];
        if (spell == 0)
            return false;
        CastNow(fight, caster, spell, () => PickAuto(fight, caster, spell));
        FightRules.EndActions(rec);
        return true;
    }

    // ---- Item use (0x22AC4) -----------------------------------------------------------------------------------------

    /// <summary>0x2A255: a scroll (base item slot 11–13).</summary>
    public static bool IsScroll(BaseItemTable items, byte[] item) => items[item[0x2E], BaseItemTable.Slot] is >= 11 and <= 13;

    /// <summary>An item the Use command can use: a scroll, or power 0x3D &gt; 0 with kind 0x3E &lt; 0x80.</summary>
    public static bool IsUsable(BaseItemTable items, byte[] item) =>
        IsScroll(items, item) || (item[ItemPower] > 0 && item[ItemKind] < 0x80);

    /// <summary>0x22AC4 for the AI's item pick (0xAFA4): the user is computer-controlled, so the cast aims by itself.</summary>
    public void UseItem(Fight fight, EclCharacter user, byte[] item) => UseItemNow(fight, user, item);

    /// <summary>
    /// 0x22AC4 in a fight, the user being the current creature. DS:6DBF = 0. A scroll (0x2A255): the spell is the one
    /// picked from it (the spell list 0x239D6, <paramref name="scrollSpell"/>; 0 = none). Otherwise an item with power
    /// 0x3D &gt; 0 and kind 0x3E &lt; 0x80: DS:6DBF = 1, spell = power (above 0x38: − 0x17). No spell → nothing, not done.
    /// Then DS:6DBF = 1 (for scrolls too: casting level 6, no memorised copy lost) and the cast 0x27C38(spell, ai = the
    /// user's 0x10F) runs at once (no casting time): the computer aims itself (<see cref="PickAuto"/>), a player through
    /// <paramref name="playerPick"/>(spell) (the aim 0x14611 / 0x15D53 and the pick; null = not picked). DS:6DBF = 0. "Done" = picked; in a
    /// fight a spell whose table byte 11 ≠ 0 ends the actions (START 0x31D7) and done = 1 even when the aim failed.
    /// When done: a scroll loses the spell (0x2A2A2); else with charges 0x3C &gt; 0 a stack (count 0x39 &gt; 1) loses one,
    /// a single item one charge, and at 0 charges the item leaves the list (START 0x20A9; 0xC7 is not rewritten here).
    /// <paramref name="cast"/> replaces the cast (spell, ai) → picked, for tests.
    /// </summary>
    public bool UseItemNow(Fight fight, EclCharacter user, byte[] item, int scrollSpell = 0, Func<int, bool>? playerPick = null,
        Func<int, bool, bool>? cast = null)
    {
        Bind(fight);
        UsingItem = false;
        bool scroll = IsScroll(fight.Items, item);
        int spell = 0;
        if (scroll)
            spell = scrollSpell;
        else if (item[ItemPower] > 0 && item[ItemKind] < 0x80)
        {
            UsingItem = true;
            spell = item[ItemPower];
        }
        if (spell == 0)
            return false;
        if (UsingItem && spell > 0x38)
            spell -= 0x17;
        if (UsingItem)
            ItemUsed?.Invoke(user, item);
        UsingItem = true;
        bool ai = user[CombatRules.AutoCombat] != 0;
        bool done;
        try
        {
            done = cast is not null
                ? cast(spell, ai)
                : CastNow(fight, user, spell, ai ? () => PickAuto(fight, user, spell) : () => playerPick?.Invoke(spell) ?? false);
        }
        finally
        {
            UsingItem = false;
        }
        if (Table[spell].WhenCast != 0)
        {
            FightRules.EndActions(fight.Record(user));
            done = true;
        }
        if (!done)
            return false;
        if (scroll)
            RemoveScrollSpell(user, item, spell);
        else if (item[ItemCharges] > 0)
        {
            if (item[ItemCount] > 1)
                item[ItemCount]--;
            else if (--item[ItemCharges] == 0)
                user.Items.Remove(item);
        }
        return true;
    }

    /// <summary>
    /// 0x2A2A2: the last of the scroll's spell bytes 0x3C–0x3E whose low 7 bits are the spell's (low 7 bits) is cleared
    /// and name word 0x30 drops by one; below 0xD2 the scroll leaves the list (START 0x20A9).
    /// </summary>
    public static void RemoveScrollSpell(EclCharacter user, byte[] scroll, int spell)
    {
        int found = 0;
        for (int i = 1; i <= 3; i++)
            if ((scroll[0x3B + i] & 0x7F) == (spell & 0x7F))
                found = i;
        if (found == 0)
            return;
        scroll[0x3B + found] = 0;
        scroll[0x30]--;
        if (scroll[0x30] < 0xD2)
            user.Items.Remove(scroll);
    }
}
