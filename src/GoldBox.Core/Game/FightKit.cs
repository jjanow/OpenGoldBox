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
    /// <summary>The level-stats routine's tables (0x2AA87, <see cref="LevelStats"/>); null = not loaded.</summary>
    public LevelStatsTables? LevelTables { get; init; }

    /// <summary>
    /// The AI's key check (0xBB81, <see cref="MonsterAi.ReadKey"/>) in the fights this kit builds: a waiting key (the
    /// last one, as GetKey takes it), 0 for none or one the check ignores; reading it empties the key buffer. Also read
    /// once, result dropped, when a fight is built (the fight start's START 0x8AB2 reads). Null = no keyboard (headless).
    /// </summary>
    public Func<byte>? AiKey { get; set; }

    /// <summary>
    /// The program's Alt-X debugging switch works in the fights this kit builds (0x1638E: ParamStr(1) equals the word at
    /// GAME.OVR 0x16374), asked when a fight is built; null = never (the original started without the word).
    /// </summary>
    public Func<bool>? AltXEnabled { get; set; }

    /// <summary>
    /// The level seams of an effect engine: the race level limit (START 0x3AF7, <see cref="LevelStats.RaceLimitStops"/>)
    /// and, with <paramref name="tables"/>, the level stats 0x2AA87 (<see cref="LevelStats.Apply"/>).
    /// </summary>
    public static void WireLevels(EffectHandlers effects, LevelStatsTables? tables)
    {
        effects.RaceLevelLimit = LevelStats.RaceLimitStops;
        if (tables is not null)
            effects.LevelStats = c => LevelStats.Apply(c, tables);
    }

    /// <summary>
    /// One fight over the creature list in <paramref name="memory"/>'s party (DS:5D96, party first, then the monsters),
    /// set up as the fight start 0xE402 leaves it: the arena and placement (<see cref="FightPlacement.Start"/>; the
    /// arena's dice are the only draws). Party members not on Quick (record 0x10F = 0) get the menu answered by
    /// <paramref name="chooser"/>; with no chooser they act through the AI as if on Quick [our choice: a headless
    /// fight]. The VM-cell part of 0xE402 (<see cref="EncounterHost"/>) is the caller's.
    /// </summary>
    /// <remarks><paramref name="time"/>: the session's program variables (the effect flags DS:471E the end of round's
    /// clock uses, <see cref="WireClock"/>); null = a fresh set for this fight. <paramref name="deferStartEffects"/>: the
    /// trigger-8 effects wait for <see cref="Fight.Run"/> (a display attaches in between; <see cref="FightPlacement.Start"/>).</remarks>
    public Fight Create(EclMemory memory, EncounterState encounter, Func<ushort, ushort> random, FightStartArea area,
        IFightMenuChooser? chooser = null, IFightItemScreens? screens = null, CampState? time = null,
        bool deferStartEffects = false)
    {
        var map = new CombatMap(Tables);
        var effects = new EffectHandlers(random, Names);
        effects.ExperienceTable = Experience;
        WireLevels(effects, LevelTables);
        // The fight start's key reads (START 0x8AB2 from 0xE402 and 0xDFC6): a waiting key is taken and dropped.
        _ = AiKey?.Invoke();
        bool altX = AltXEnabled?.Invoke() == true;
        // Ready / unready of an item with 0x3E > 0x7F runs its handler through 0x2AEEA (0x2250A; s30: handlers 131 and
        // 135 show their texts, 128… add the item's effect).
        void ItemHandler(EclCharacter c, byte[] item, bool removed) => effects.CallHandler(item[0x3E], c, item, removed);
        var ai = new MonsterAi(AiSteps, null, TurnUndead) { ReadKey = AiKey, AltX = () => altX, ItemHandler = ItemHandler };
        // The View / Use screens (0x21AED / 0x21F32) when the chooser also answers them.
        screens ??= chooser is IItemScreenChooser itemChooser ? new FightItemScreens(itemChooser, ItemHandler) : null;
        IFightController controller = chooser is null
            ? new AllQuickController(ai)
            : new MenuController(chooser, ai, screens, TurnUndead) { CheatEnabled = altX };
        var fight = new Fight(memory, encounter, Items, random, controller, map, effects)
        {
            Moves = new FightMoves(map),
        };
        effects.LeaveMap = c => map.Remove(c, shown: true);
        fight.Recalculate = c => StatRecalc.Recalculate(c, Items);
        effects.Recalculate = fight.Recalculate;
        fight.Spells = new FightSpells(Spells, effects, random);
        effects.Attach(fight);
        WireClock(fight, effects, time ?? new CampState());
        FightPlacement.Start(fight, map, area, deferStartEffects);
        return fight;
    }

    /// <summary>
    /// The end of round's minute (0xA087 → the clock 0x241A1 with count 1, unit 1): the game clock $49C6–$49CC, then
    /// the effect timeout 0x23DCC (<see cref="CampTime.AdvanceOutsideCamp"/>; DS:49F3 = 5 in a fight, so every flag of
    /// <paramref name="time"/> is set first) over the whole creature list, monsters included: each effect with minutes
    /// left loses one, one with 1 minute left is removed (0x2AF10, its handler with "removed" first when byte 4 is set).
    /// </summary>
    public static void WireClock(Fight fight, EffectHandlers effects, CampState time) =>
        fight.MinutePassed = () => CampTime.AdvanceOutsideCamp(fight.Memory, time, Explore.GameClock.Minutes, 1,
            (c, e) => effects.RemoveEffect(c, e[0], e));

    /// <summary>A headless controller: every creature acts through the AI (party members as if on Quick).</summary>
    public sealed class AllQuickController(MonsterAi ai) : IFightController
    {
        public MonsterAi Ai { get; } = ai;
        public void Act(Fight fight, EclCharacter creature, bool auto) => Ai.Turn(fight, creature);
    }
}
