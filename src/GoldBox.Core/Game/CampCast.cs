using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Casting outside a fight: the cast routine GAME.OVR 0x27C38 with DS:49F3 ≠ 5 (the camp's Magic → Cast 0x17CD2, the
/// Items page's Use 0x222F0 → 0x22AC4, the loot menu's D 0x6605) with the non-combat target handler 0x27AA9, on Core's
/// spell routines (<see cref="FightSpells.Routine"/>) with the effect engine out of combat. `spells-effects.md` "Cast".
/// </summary>
public sealed class CampCast
{
    public enum Outcome
    {
        /// <summary>Table byte 7 (target mode) = 0: combat only. The question was asked (or, through <see cref="Cast"/>
        /// without hooks, is left to the caller).</summary>
        CombatOnly,
        /// <summary>No target picked, or a target mode the handler does not take (not 1, 2 or 4): nothing happens.</summary>
        NoTarget,
        Cast,
        /// <summary>[?] The spell's routine is not modelled in Core yet: nothing is cast and the spell stays.</summary>
        NotModelled,
    }

    /// <summary>The display and answers 0x27C38 asks for outside a fight (all optional).</summary>
    public sealed class Hooks
    {
        /// <summary>0x27C75–0x27D30, table byte 7 = 0: (spell, item = DS:6DBF ≠ 0) → the two lines at (1, 19) / (1, 20)
        /// and the yes/no (0x2D9A0 with 10, 15, 13): the spell's "Lose it?" or the item's "Use it?". True = Y.</summary>
        public Func<int, bool, bool>? CombatOnly { get; init; }
        /// <summary>0x27D48: the cast message 0x2A332 (caster, spell), once before the pick; only with the announce
        /// argument ≠ 0 and DS:6DBF = 0.</summary>
        public Action<EclCharacter, int>? Announce { get; init; }
        /// <summary>0x27AA9 mode 2: the member pick (START 0x37E8) from the remembered target; null = given up.</summary>
        public Func<EclCharacter?, EclCharacter?>? PickMember { get; init; }
        /// <summary>0x27F44: START 0x2383 on every way out (outside a fight it clears (1, 18)–(38, 22)).</summary>
        public Action? End { get; init; }
        /// <summary>[Test seam] Replaces the target handler 0x27AA9 (spell → done), as the oracle's scripted pick.</summary>
        public Func<int, bool>? Pick { get; init; }
        /// <summary>[Test seam] Replaces the call of the spell's routine (DS:6B84 + 4·spell).</summary>
        public Action<int>? Routine { get; init; }
    }

    public FightSpells Spells { get; }
    private readonly EclParty _party;

    /// <summary>DS:6DB5: the remembered target, cleared when the cast menu opens and when the camp ends. A program
    /// global (<see cref="SpellGlobals.Ds6DB5"/>, on <see cref="FightSpells.Globals"/>): every cast of the run that
    /// shares the game's globals sees the same one.</summary>
    public EclCharacter? Remembered
    {
        get => Spells.Globals.Ds6DB5;
        set => Spells.Globals.Ds6DB5 = value;
    }

    public CampCast(FightSpells spells, EclParty party)
    {
        Spells = spells;
        _party = party;
        spells.Effects.InCombat = false;
        spells.Effects.List = () => _party.Members;
        spells.Effects.CurrentCharacter = () => _party.Current;
        spells.UsingItem = false;
    }

    /// <summary>The target mode, table byte 7: 1 the caster, 2 one party member, 4 every creature of the list.</summary>
    public int TargetMode(int spell) => Spells.Table[spell].Target;

    /// <summary>
    /// 0x27C38 outside a fight for the current character's memorised <paramref name="spell"/> (DS:6DBF = 0) without the
    /// combat-only question: mode 0 → <see cref="Outcome.CombatOnly"/> (the caller asks, <see cref="Lose"/>). Else as
    /// <see cref="Run"/> with <paramref name="pick"/> as the member pick.
    /// </summary>
    public Outcome Cast(int spell, Func<EclCharacter?, EclCharacter?> pick)
    {
        if (TargetMode(spell) == 0)
            return _party.Current is null ? Outcome.NoTarget : Outcome.CombatOnly;
        bool done = false;
        return Run(spell, announce: false, new Hooks { PickMember = pick }, ref done);
    }

    /// <summary>
    /// 0x27C38(spell, ai, announce, var done) with DS:49F3 ≠ 5, whole (read s49), the caster being DS:5D92 (the current
    /// character). DS:6DBF (<see cref="FightSpells.UsingItem"/>) says whether an item casts. Table byte 7 = 0 (combat
    /// only): the two lines and the question (<see cref="Hooks.CombatOnly"/>); a spell's Yes removes the memorised copy
    /// (START 0x202B), an item's Yes sets <paramref name="done"/> := 1 (its caller then spends the charge with no
    /// effect); either way no cast. Else: with <paramref name="announce"/> and no item the cast message, then the target
    /// handler 0x27AA9 (it sets done := 1, mode 2 given up and modes other than 1, 2, 4 set done := 0); picked → the
    /// caster's invisibility goes (0x2BEBC), the memorised copy is removed unless an item casts, DS:681B = spell, the
    /// spell's routine (DS:6B84 + 4·spell), DS:681B = DS:6820 = 0; not picked → nothing (no question, nothing lost,
    /// outside a fight). Every way out ends with START 0x2383 (<see cref="Hooks.End"/>). The ai argument matters only
    /// in a fight.
    /// </summary>
    public Outcome Run(int spell, bool announce, Hooks hooks, ref bool done)
    {
        if (_party.Current is not { } caster)
            return Outcome.NoTarget;
        bool item = Spells.UsingItem;
        try
        {
            if (TargetMode(spell) == 0)
            {
                bool yes = hooks.CombatOnly?.Invoke(spell, item) ?? false;
                if (yes)
                {
                    if (item)
                        done = true; // 0x27D3C
                    else
                        FightSpells.RemoveSpell(caster, spell); // 0x27CD7: START 0x202B
                }
                return Outcome.CombatOnly;
            }
            if (announce && !item)
                hooks.Announce?.Invoke(caster, spell);
            if (hooks.Routine is null && !FightSpells.Modelled.Contains(spell))
                return Outcome.NotModelled;
            done = hooks.Pick is { } scripted ? scripted(spell) : Pick(spell, caster, hooks.PickMember);
            if (!done)
                return Outcome.NoTarget;
            Spells.Effects.RemoveInvisibility(caster);
            if (!item)
                FightSpells.RemoveSpell(caster, spell);
            Spells.Effects.Cells.Spell = (byte)spell;
            if (hooks.Routine is { } routine)
                routine(spell);
            else
                Spells.Routine(spell, caster);
            Spells.Effects.Cells.Spell = 0;
            Spells.Effects.Cells.MultiTarget = 0;
            return Outcome.Cast;
        }
        finally
        {
            hooks.End?.Invoke();
        }
    }

    /// <summary>
    /// 0x27AA9 (DS:6B84 entry 0 outside a fight): DS:6DB5 defaults to the caster; the target list is the caster and done
    /// := 1; mode 1: so; mode 2: the member pick from DS:6DB5 (given up: the list and DS:6DB5 cleared, done := 0);
    /// mode 4: every creature of the list DS:5D96; other modes: done := 0.
    /// </summary>
    private bool Pick(int spell, EclCharacter caster, Func<EclCharacter?, EclCharacter?>? pick)
    {
        Remembered ??= caster;
        var t = Spells.Targets;
        t.Clear();
        t.Add(caster);
        switch (TargetMode(spell))
        {
            case 1:
                return true;
            case 2:
                Remembered = pick?.Invoke(Remembered);
                t.Clear();
                if (Remembered is null)
                    return false;
                t.Add(Remembered);
                return true;
            case 4:
                t.Clear();
                t.AddRange(_party.Members);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The combat-only spell's question answered yes (0x27C38 → START 0x202B).</summary>
    public void Lose(int spell)
    {
        if (_party.Current is { } c)
            FightSpells.RemoveSpell(c, spell);
    }

    /// <summary>
    /// 0x22AC4 outside a fight (the Items page's Use 0x222F0, DS:49F3 = 2–4; read s49), the user being the current
    /// character. DS:6DBF = 0. A scroll (0x2A255): DS:6B80 = the item and the spell is the one picked from its list
    /// (0x239D6 mode 2, <paramref name="scrollSpell"/>: the row's byte AND 0x7F, 0 = none). Else an item with power
    /// 0x3D &gt; 0 and kind 0x3E &lt; 0x80: DS:6DBF = 1, spell = power. No spell → nothing. A power item: above 0x38
    /// the spell is power − 0x17, then its message (<paramref name="itemMessage"/>: the user's name and words through
    /// START 0x16EC, the item's name on row 22, PauseOnly, START 0x2383). Then DS:6DBF = 1 (scrolls too) and the cast
    /// <see cref="Run"/>(spell, ai = 0x10F, announce 0); DS:6DBF = 0. When done (cast, or the combat-only "Use it?"
    /// answered yes): <see cref="FightSpells.SpendItem"/> (a scroll loses the spell, a charged item a charge).
    /// Returns done; 0x222F0 then clears it outside a fight, so the Items page stays.
    /// </summary>
    public bool UseItem(BaseItemTable items, byte[] item, Func<int> scrollSpell, Action<EclCharacter, byte[]>? itemMessage,
        Hooks hooks)
    {
        if (_party.Current is not { } user)
            return false;
        Spells.UsingItem = false;
        bool scroll = FightSpells.IsScroll(items, item);
        int spell = 0;
        if (scroll)
            spell = scrollSpell();
        else if (item[FightSpells.ItemPower] > 0 && item[FightSpells.ItemKind] < 0x80)
        {
            Spells.UsingItem = true;
            spell = item[FightSpells.ItemPower];
        }
        if (spell == 0)
        {
            Spells.UsingItem = false;
            return false;
        }
        if (Spells.UsingItem)
        {
            if (spell > 0x38)
                spell -= 0x17;
            itemMessage?.Invoke(user, item);
        }
        Spells.UsingItem = true;
        bool done = false;
        try
        {
            Run(spell, announce: false, hooks, ref done);
        }
        finally
        {
            Spells.UsingItem = false;
        }
        if (done)
            FightSpells.SpendItem(user, item, scroll, spell);
        return done;
    }
}
