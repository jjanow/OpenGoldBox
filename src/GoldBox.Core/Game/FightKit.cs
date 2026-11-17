using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The program's tables a fight needs, and the builder that puts the parts of the fight together: the combat map
/// (<see cref="CombatMap"/>, geometry and map), the moves (<see cref="FightMoves"/>), the effect engine
/// (<see cref="EffectHandlers"/>), the spells (<see cref="FightSpells"/>), the monster AI (<see cref="MonsterAi"/>) and
/// the player's menu (<see cref="MenuController"/>). The tables come from the player's program (GoldBox.Data's
/// <c>PorFight</c> loads them for Pool of Radiance).
/// </summary>
public sealed class FightKit
{
    /// <summary>Combat map tables (terrain costs, directions, placement rings).</summary>
    public required CombatTables Tables { get; init; }
    /// <summary>The spell table (DS:31FA).</summary>
    public required SpellTable Spells { get; init; }
    /// <summary>The base item table (<c>ITEMS</c>).</summary>
    public required BaseItemTable Items { get; init; }
    /// <summary>The AI's step pattern table (START DS:02AE).</summary>
    public required IReadOnlyList<byte> AiSteps { get; init; }
    /// <summary>The effect handlers' race-name tables; null = empty.</summary>
    public EffectNameTables? Names { get; init; }
    /// <summary>The experience table (DS:409B, <see cref="EffectHandlers.ExperienceTable"/>); null = not loaded.</summary>
    public int[]? Experience { get; init; }
    /// <summary>The turn-undead table (START.EXE); null = clerics can't turn.</summary>
    public TurnUndeadTable? TurnUndead { get; init; }

    /// <summary>
    /// One fight over the creature list in <paramref name="memory"/>'s party (DS:5D96, party first, then the monsters),
    /// set up as the fight start 0xE402 leaves it: the arena and placement (<see cref="FightPlacement.Start"/>; the
    /// arena's dice are the only draws). Party members not on Quick (record 0x10F = 0) get the menu answered by
    /// <paramref name="chooser"/>; with no chooser they act through the AI as if on Quick [our choice: a headless
    /// fight]. The VM-cell part of 0xE402 (<see cref="EncounterHost"/>) is the caller's.
    /// </summary>
    public Fight Create(EclMemory memory, EncounterState encounter, Func<ushort, ushort> random, FightStartArea area,
        IFightMenuChooser? chooser = null, IFightItemScreens? screens = null)
    {
        var map = new CombatMap(Tables);
        var effects = new EffectHandlers(random, Names);
        effects.ExperienceTable = Experience;
        var ai = new MonsterAi(AiSteps, null, TurnUndead);
        // The View / Use screens (0x21AED / 0x21F32) when the chooser also answers them.
        screens ??= chooser is IItemScreenChooser itemChooser ? new FightItemScreens(itemChooser) : null;
        IFightController controller = chooser is null
            ? new AllQuickController(ai)
            : new MenuController(chooser, ai, screens, TurnUndead);
        var fight = new Fight(memory, encounter, Items, random, controller, map, effects)
        {
            Moves = new FightMoves(map),
        };
        effects.LeaveMap = map.Remove;
        fight.Recalculate = c => StatRecalc.Recalculate(c, Items);
        effects.Recalculate = fight.Recalculate;
        fight.Spells = new FightSpells(Spells, effects, random);
        effects.Attach(fight);
        FightPlacement.Start(fight, map, area);
        return fight;
    }

    /// <summary>A headless controller: every creature acts through the AI (party members as if on Quick).</summary>
    public sealed class AllQuickController(MonsterAi ai) : IFightController
    {
        public MonsterAi Ai { get; } = ai;
        public void Act(Fight fight, EclCharacter creature, bool auto) => Ai.Turn(fight, creature);
    }
}
