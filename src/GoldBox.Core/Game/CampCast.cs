using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Casting outside a fight (the camp's Magic → Cast, GAME.OVR 0x17CD2 → the cast 0x27C38 with the non-combat target
/// handler 0x27AA9), on Core's spell routines (<see cref="FightSpells.Routine"/>) with the effect engine out of combat.
/// </summary>
public sealed class CampCast
{
    public enum Outcome
    {
        /// <summary>Table byte 7 (target mode) = 0: combat only; the caller asks whether to lose it.</summary>
        CombatOnly,
        /// <summary>No target picked, or a target mode the handler does not take (not 1, 2 or 4): nothing happens.</summary>
        NoTarget,
        Cast,
        /// <summary>[?] The spell's routine is not modelled in Core yet: nothing is cast and the spell stays.</summary>
        NotModelled,
    }

    public FightSpells Spells { get; }
    private readonly EclParty _party;

    /// <summary>DS:6DB5: the remembered target, cleared when the cast menu opens and when the camp ends.</summary>
    public EclCharacter? Remembered { get; set; }

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
    /// 0x27C38 outside a fight, for the current character's memorised <paramref name="spell"/>: mode 0 → combat only.
    /// Else the targets (0x27AA9): the remembered target defaults to the caster; mode 1 the caster; 2 the member
    /// <paramref name="pick"/> returns (given the remembered one; null gives up); 4 the whole party list; others give
    /// up. On a pick: invisibility leaves the caster (0x2BEBC), the memorised spell is used up (START 0x202B),
    /// DS:681B = spell and the spell's routine runs.
    /// </summary>
    public Outcome Cast(int spell, Func<EclCharacter?, EclCharacter?> pick)
    {
        if (_party.Current is not { } caster)
            return Outcome.NoTarget;
        int mode = TargetMode(spell);
        if (mode == 0)
            return Outcome.CombatOnly;
        if (!FightSpells.Modelled.Contains(spell))
            return Outcome.NotModelled;
        Remembered ??= caster;
        var t = Spells.Targets;
        t.Clear();
        switch (mode)
        {
            case 1:
                t.Add(caster);
                break;
            case 2:
                Remembered = pick(Remembered);
                if (Remembered is null)
                    return Outcome.NoTarget;
                t.Add(Remembered);
                break;
            case 4:
                t.AddRange(_party.Members);
                break;
            default:
                return Outcome.NoTarget;
        }
        Spells.Effects.RemoveInvisibility(caster);
        FightSpells.RemoveSpell(caster, spell);
        Spells.Effects.Cells.Spell = (byte)spell;
        Spells.Routine(spell, caster);
        Spells.Effects.Cells.Spell = 0;
        Spells.Effects.Cells.MultiTarget = 0;
        return Outcome.Cast;
    }

    /// <summary>The combat-only spell's question answered yes (0x27C38 → START 0x202B).</summary>
    public void Lose(int spell)
    {
        if (_party.Current is { } c)
            FightSpells.RemoveSpell(c, spell);
    }
}
