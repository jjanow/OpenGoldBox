namespace GoldBox.Data.Module;

// The decoded rules tables (docs/module-format.md §12): one file per table group, each a named-field view of a byte
// range of the program's data segment. Every file names its rules profile and, per table, the DS offset the program
// indexes it from (the "base"; cells sit at base + the program's index arithmetic). A file owns only the cells whose
// meaning is settled; the rest of the data segment stays in rules/program.json, which the engine reads first and then
// overwrites with each decoded file's bytes (PorRulesTables). Writing a file's fields back gives the original bytes.
// Numbers are plain ints: unsigned bytes 0..255, signed bytes −128..127, words 0..65535, dwords signed (as the program
// compares them); an out-of-range value is refused when the engine encodes the file.

/// <summary>Fields every decoded rules file has.</summary>
public interface IRulesDocument
{
    /// <summary>The rules profile the table belongs to (<c>por-1.3</c>).</summary>
    string Profile { get; }
    SourceRef? Source { get; }
}

/// <summary><c>rules/spells.json</c>: the spell table (DS:31FA in v1.3, 16 bytes per id, ids 1..67).</summary>
public sealed record SpellsDocument : IRulesDocument
{
    public const string RelativePath = "rules/spells.json";
    public required string Profile { get; init; }
    /// <summary>DS offset of entry 0 (entry id at base + 16·id; entry 0 is not part of the table).</summary>
    public required int DsOffset { get; init; }
    public required List<SpellEntry> Spells { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary>One spell table entry (docs/re/spells-effects.md "Spells"; names as Core's <c>SpellInfo</c>). Ids 1..56 are
/// spells, 57..67 item powers.</summary>
public sealed record SpellEntry
{
    public required int Id { get; init; }
    /// <summary>0 cleric, 1 magic-user, 2 item (casting level 12).</summary>
    public int CastClass { get; init; }
    public int Level { get; init; }
    /// <summary>Range: fixed + per level × casting level (255 = attack roll).</summary>
    public int RangeFixed { get; init; }
    public int RangePerLevel { get; init; }
    /// <summary>Duration: fixed + per level × casting level (some spells have their own rule, spells-effects.md).</summary>
    public int DurationFixed { get; init; }
    public int DurationPerLevel { get; init; }
    /// <summary>Area: low nibble the mode, the other bits the radius.</summary>
    public int Area { get; init; }
    /// <summary>Target mode (outside a fight).</summary>
    public int Target { get; init; }
    /// <summary>0 none; also the non-weapon reduction and the "save negates" flag.</summary>
    public int Save { get; init; }
    public int SaveType { get; init; }
    public int Effect { get; init; }
    /// <summary>0 = not castable in a fight.</summary>
    public int WhenCast { get; init; }
    /// <summary>Casting time (delay = ÷ 3).</summary>
    public int CastTime { get; init; }
    public int AiPriority { get; init; }
    /// <summary>The AI's "needs targets" byte.</summary>
    public int Offensive { get; init; }
    public int AiRadius { get; init; }
}

/// <summary>
/// <c>rules/experience.json</c>: experience needed for each level (DS:409B in v1.3, class × 0x38 + level × 4, signed
/// dwords). Only levels 2..9 are decoded: the program reads the table for levels above 1, the cells of levels 0 and 1 are
/// other data `[?]`, and a class's cells for levels 10..13 hold other tables (the cleric and magic-user spell slots in
/// <c>levelStats.json</c>) and stay in <c>program.json</c>. −1 marks a level the class does not reach in PoR `[?]`.
/// </summary>
public sealed record ExperienceDocument : IRulesDocument
{
    public const string RelativePath = "rules/experience.json";
    public const int FirstLevel = 2, LastLevel = 9;
    public required string Profile { get; init; }
    /// <summary>DS offset of class 0 level 0.</summary>
    public required int DsOffset { get; init; }
    /// <summary>Classes 0..7 (0 cleric, 1 druid, 2 fighter, 3 paladin, 4 ranger, 5 magic-user, 6 thief, 7 monk).</summary>
    public required List<ExperienceClass> Classes { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record ExperienceClass
{
    public required int Class { get; init; }
    /// <summary>Experience for levels 2..9, in order.</summary>
    public required int[] Levels { get; init; }
}

/// <summary>
/// <c>rules/levelStats.json</c>: the tables of the level-stats routine GAME.OVR 0x2AA87 (docs/re/spells-effects.md
/// "Level stats"). Each table keeps the base the routine indexes from; the decoded rows are the ones PoR's classes,
/// levels and races reach. Rows outside them read other tables' bytes, as the program does `[?]`.
/// </summary>
public sealed record LevelStatsDocument : IRulesDocument
{
    public const string RelativePath = "rules/levelStats.json";
    public required string Profile { get; init; }
    /// <summary>Base THAC0 (DS:3C7C, class × 11 + level): classes 0..7, levels 0..10.</summary>
    public required Thac0Table Thac0 { get; init; }
    /// <summary>Cleric spell slots (DS:40BC, level × 3 + spell level 1..3): levels 2..6.</summary>
    public required SpellSlotTable ClericSlots { get; init; }
    /// <summary>Magic-user spell slots (DS:41D4, as the cleric's): levels 2..6.</summary>
    public required SpellSlotTable MageSlots { get; init; }
    /// <summary>Saving throws (DS:426E, class × 0x2D + level × 5 + save): classes 0..7, levels 1..9.</summary>
    public required SaveTable Saves { get; init; }
    /// <summary>Thief skills by thief level (DS:3CF3, level × 8 + skill 1..8): levels 1..9.</summary>
    public required ThiefLevelTable ThiefBase { get; init; }
    /// <summary>Thief skill adjustment by race (DS:3D3B, race × 8 + skill, signed): races 1..7 (race 0's cells are thief
    /// level 9).</summary>
    public required ThiefRaceTable ThiefRace { get; init; }
    /// <summary>Thief skill adjustment by Dexterity (DS:3D4E, Dexterity × 5 + skill 1..5, signed): Dexterity 9..18.</summary>
    public required ThiefDexTable ThiefDex { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record Thac0Table
{
    public const int Levels = 11;
    public required int DsOffset { get; init; }
    public required List<Thac0Class> Classes { get; init; }
}

public sealed record Thac0Class
{
    public required int Class { get; init; }
    /// <summary>Base THAC0 byte for levels 0..10 (the routine takes the highest, unsigned, over all classes).</summary>
    public required int[] Levels { get; init; }
}

public sealed record SpellSlotTable
{
    public const int FirstLevel = 2, LastLevel = 6;
    public required int DsOffset { get; init; }
    public required List<SpellSlotLevel> Levels { get; init; }
}

public sealed record SpellSlotLevel
{
    public required int Level { get; init; }
    /// <summary>Slots for spell levels 1, 2, 3.</summary>
    public required int[] Slots { get; init; }
}

public sealed record SaveTable
{
    public const int FirstLevel = 1, LastLevel = 9;
    public required int DsOffset { get; init; }
    public required List<SaveClass> Classes { get; init; }
}

public sealed record SaveClass
{
    public required int Class { get; init; }
    public required List<SaveLevel> Levels { get; init; }
}

/// <summary>Saving-throw targets in record order (character bytes 0x6D..0x71).</summary>
public sealed record SaveLevel
{
    public required int Level { get; init; }
    public int Paralyzation { get; init; }
    public int Petrification { get; init; }
    public int RodStaffWand { get; init; }
    public int Breath { get; init; }
    public int Spell { get; init; }
}

/// <summary>The eight thief skills in record order (character bytes 0x77..0x7E).</summary>
public sealed record ThiefSkills
{
    public int PickPockets { get; init; }
    public int OpenLocks { get; init; }
    public int FindTraps { get; init; }
    public int MoveSilently { get; init; }
    public int HideInShadows { get; init; }
    public int HearNoise { get; init; }
    public int ClimbWalls { get; init; }
    public int ReadLanguages { get; init; }
}

public sealed record ThiefLevelTable
{
    public const int FirstLevel = 1, LastLevel = 9;
    public required int DsOffset { get; init; }
    public required List<ThiefLevel> Levels { get; init; }
}

public sealed record ThiefLevel
{
    public required int Level { get; init; }
    public required ThiefSkills Skills { get; init; }
}

public sealed record ThiefRaceTable
{
    public const int FirstRace = 1, LastRace = 7;
    public required int DsOffset { get; init; }
    /// <summary>Races 1 dwarf, 2 elf, 3 gnome, 4 half-elf, 5 halfling, 6 half-orc, 7 human.</summary>
    public required List<ThiefRace> Races { get; init; }
}

public sealed record ThiefRace
{
    public required int Race { get; init; }
    public required ThiefSkills Skills { get; init; }
}

public sealed record ThiefDexTable
{
    public const int FirstDexterity = 9, LastDexterity = 18;
    public required int DsOffset { get; init; }
    public required List<ThiefDex> Dexterity { get; init; }
}

/// <summary>Dexterity adjustment of the first five skills.</summary>
public sealed record ThiefDex
{
    public required int Dexterity { get; init; }
    public int PickPockets { get; init; }
    public int OpenLocks { get; init; }
    public int FindTraps { get; init; }
    public int MoveSilently { get; init; }
    public int HideInShadows { get; init; }
}

/// <summary>
/// <c>rules/turnUndead.json</c> (DS:0447 in v1.3, docs/re/combat-menu.md "Turn undead"): signed bytes at base + 10 × row +
/// column, rows 1..10 (creature byte 0x76), columns 1..10 (from the cleric's level). v &gt; 0 turns on a roll of at least
/// v, v ≤ 0 destroys on a roll of at least |v|.
/// </summary>
public sealed record TurnUndeadDocument : IRulesDocument
{
    public const string RelativePath = "rules/turnUndead.json";
    public required string Profile { get; init; }
    public required int DsOffset { get; init; }
    public required List<TurnUndeadRow> Rows { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record TurnUndeadRow
{
    public required int Row { get; init; }
    /// <summary>Columns 1..10.</summary>
    public required int[] Columns { get; init; }
}

/// <summary>
/// <c>rules/aiSteps.json</c>: the AI's step pattern table (DS:02AE in v1.3, docs/re/combat-ai.md), base + 5 × row +
/// attempt, rows 1..6, attempts 1..5. Each value is a direction offset 1..8 (8 = straight on). The cell after row 6
/// (attempt 6, read by 0xB56D when unused) is other data and stays in <c>program.json</c>.
/// </summary>
public sealed record AiStepsDocument : IRulesDocument
{
    public const string RelativePath = "rules/aiSteps.json";
    public required string Profile { get; init; }
    public required int DsOffset { get; init; }
    public required List<AiStepRow> Rows { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record AiStepRow
{
    public required int Row { get; init; }
    /// <summary>Attempts 1..5.</summary>
    public required int[] Attempts { get; init; }
}

/// <summary>
/// <c>rules/randomItems.json</c>: the random item array (docs/re/item-format.md "Random item array"): the program indexes
/// words at base + 2·(n + field) with n = 1 + 8k, so entry k's eight words start at base + 2 + 16k (DS:0972 in v1.3).
/// Words are kept whole; the maker copies only the low byte of the name words and the props.
/// </summary>
public sealed record RandomItemsDocument : IRulesDocument
{
    public const string RelativePath = "rules/randomItems.json";
    public const int Entries = 19;
    public required string Profile { get; init; }
    /// <summary>The base the code indexes from (word 0 is not part of an entry).</summary>
    public required int DsOffset { get; init; }
    public required List<RandomItemEntry> Items { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record RandomItemEntry
{
    /// <summary>n = 1 + 8k, the number the maker uses.</summary>
    public required int Magic { get; init; }
    /// <summary>→ item byte 0x2F (name word, slot 1).</summary>
    public int Word3 { get; init; }
    /// <summary>→ 0x30.</summary>
    public int Word2 { get; init; }
    /// <summary>→ 0x31.</summary>
    public int Word1 { get; init; }
    public int Weight { get; init; }
    public int Value { get; init; }
    /// <summary>→ props 0x3C..0x3E.</summary>
    public required int[] Props { get; init; }
}

/// <summary>
/// <c>rules/temple.json</c>: the temple's heal list names (string[40] at base + 41·k, k = 1..10; DS:00F1 in v1.3,
/// docs/re/shop-format.md "Temple"). The prices are immediates in GAME.OVR's code, not data (Core <c>TempleRules</c>).
/// </summary>
public sealed record TempleDocument : IRulesDocument
{
    public const string RelativePath = "rules/temple.json";
    public const int Count = 10, SlotSize = 41;
    public required string Profile { get; init; }
    public required int DsOffset { get; init; }
    public required List<TempleEntry> Heals { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record TempleEntry
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    /// <summary>The slot's bytes after the name, when any of them is not zero (kept so the slot rebuilds).</summary>
    public byte[]? Padding { get; init; }
}

/// <summary>
/// <c>rules/combatMap.json</c>: the combat map's tables (docs/re/combat-map.md "Tables"; Core <c>CombatTables</c>), at
/// the v1.3 DS offsets the code uses. Terrain entries 64 and 65 (read by the program) are icon size 0's cells, which no
/// caller reads; both stay in <c>program.json</c>. The wilderness terrain (DS:3648) is the overland tile table, owned by
/// <c>rules/overland.json</c> (<see cref="OverlandDocument"/>), so it is not repeated here.
/// </summary>
public sealed record CombatMapDocument : IRulesDocument
{
    public const string RelativePath = "rules/combatMap.json";
    public required string Profile { get; init; }
    public required TerrainTable Terrain { get; init; }
    public required IconCellTable IconCells { get; init; }
    public required PlacementTables Placement { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record TerrainTable
{
    public const int Count = 64;
    public required int DsOffset { get; init; }
    public required List<TerrainEntry> Entries { get; init; }
}

public sealed record TerrainEntry
{
    public required int Terrain { get; init; }
    /// <summary>255 = impassable.</summary>
    public int MoveCost { get; init; }
    /// <summary>Sight height of a viewer standing on it.</summary>
    public int SightHeight { get; init; }
    /// <summary>A line of sight stops on a cell whose blocking height is above the viewer's.</summary>
    public int BlockingHeight { get; init; }
    /// <summary>The 24×24 tile drawn.</summary>
    public int Tile { get; init; }
}

/// <summary>DS:2844: icon footprints, size × 8 + cell × 2 → (dx, dy) signed; a negative dx ends the footprint. Sizes 1..7.</summary>
public sealed record IconCellTable
{
    public const int FirstSize = 1, LastSize = 7, Cells = 4;
    public required int DsOffset { get; init; }
    public required List<IconSize> Sizes { get; init; }
}

public sealed record IconSize
{
    public required int Size { get; init; }
    public required List<CellDelta> Cells { get; init; }
}

public sealed record CellDelta
{
    public int Dx { get; init; }
    public int Dy { get; init; }
}

/// <summary>The fight placement tables (docs/re/combat-map.md; Core <c>FightPlacement</c>).</summary>
public sealed record PlacementTables
{
    /// <summary>DS:02D2: 4 team directions × 4 rings → the 3D direction of each placement ring.</summary>
    public required ByteGrid RingDirection { get; init; }
    /// <summary>DS:02E2: 4 team directions × 4 rings → the start corner (× 2) of each ring.</summary>
    public required ByteGrid RingStart { get; init; }
    /// <summary>DS:02F2: 3D facing ÷ 2 → arena direction (4 bytes).</summary>
    public required ByteGrid QuadDirection { get; init; }
    /// <summary>DS:02F6: start x, [ring &gt; 0] × 4 corners, signed.</summary>
    public required ByteGrid StartQuadX { get; init; }
    /// <summary>DS:02FE: start y, as x.</summary>
    public required ByteGrid StartQuadY { get; init; }
    /// <summary>DS:0306: 5 grids × 6 rows × (lo, hi) signed: the open columns of each row (rows of 12).</summary>
    public required ByteGrid RowBand { get; init; }
}

/// <summary>A small byte table kept as rows (each row the bytes of one first index), signed or not as noted.</summary>
public sealed record ByteGrid
{
    public required int DsOffset { get; init; }
    public bool Signed { get; init; }
    public required List<int[]> Rows { get; init; }
}

