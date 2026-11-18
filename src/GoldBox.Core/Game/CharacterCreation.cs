using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Where the party menu's Create (GAME.OVR 0x19EEB) finds its tables in the program's data segment (Pool of Radiance
/// DOS v1.3; docs/re/start-menu.md "Tables"). Read from the player's program (GoldBox.Data's <c>PorProgramData</c>);
/// nothing is copied into our code. The routine indexes them with its own arithmetic, so they stay offsets into the
/// data segment, read byte by byte as the program reads them.
/// </summary>
public sealed class CreationTables
{
    /// <summary>DS offsets (v1.3).</summary>
    public const int ClassesByRace = 0x3E17, AlignmentsByClass = 0x3FE7, StartingAge = 0x3E5B, AgeSteps = 0x3F2F,
        RaceLimits = 0x3DA3, ClassMinimums = 0x3F81, GoldDice = 0x4263, IconColours = 0x3CF5, Usability = 0x05D6,
        HpDiceCount = 0x08B6, HpDiceSides = 0x08BE, ClassBits = 0x05DE, ConHp = 0x408E;

    /// <summary>The program's data segment (bytes past it read as 0).</summary>
    public required byte[] Data { get; init; }
    /// <summary>The level-stats tables (THAC0, saves, thief skills, spell table) the routine shares with 0x2AA87.</summary>
    public required LevelStatsTables Levels { get; init; }

    public byte At(int offset) => offset >= 0 && offset < Data.Length ? Data[offset] : (byte)0;

    public ushort Word(int offset) => (ushort)(At(offset) | At(offset + 1) << 8);
}

/// <summary>
/// The party menu's Create (Pool of Radiance DOS v1.3, GAME.OVR 0x19EEB; program read s19, docs/re/start-menu.md
/// "Create"), one step per menu so a front end can show each pick. Every step writes the 285-byte record as the
/// program does, and every roll draws from the game's generator in the program's order, so the record equals the one
/// the program builds from the same RandSeed and picks (<c>CharacterCreationProgramTests</c>, run against the routine
/// under emulation by <c>tools/scripts/por_chargen_oracle.py</c>).
/// </summary>
public sealed class CharacterCreation
{
    public const int Name = 0x00, Abilities = 0x10, ExceptionalStrength = 0x16, Thac0Base = 0x2D, Race = 0x2E,
        Class = 0x2F, Age = 0x30, HpMax = 0x32, KnownBase = 0x32, IconFootprint = 0x6C, MovementBase = 0x72,
        LevelHighest = 0x73, Gold = 0x8E, Levels = 0x96, Gender = 0x9E, Alignment = 0xA0, PrimaryAttacks = 0xA1,
        PrimaryDice = 0xA3, PrimarySides = 0xA5, AcBase = 0xA9, StrengthBonus = 0xAA, RandomByte = 0xAB,
        Usable = 0xB0, HpRolled = 0xB1, ClericSlot1 = 0xB2, MageSlot1 = 0xB5, PortraitHead = 0xBB, PortraitBody = 0xBC,
        IconHead = 0xBD, IconBody = 0xBE, IconSize = 0xC0, IconColours = 0xC1, Status = 0x10C, InPlay = 0x10D,
        HpCurrent = 0x11B;

    /// <summary>Stat indices (record 0x10 + i), as the program orders them.</summary>
    public const int Str = 0, Int = 1, Wis = 2, Dex = 3, Con = 4, Cha = 5;

    /// <summary>Class codes the multi-class cleric Wisdom rule covers (GAME.OVR set at 0x19EA4): 8..12.</summary>
    public static bool IsClericMulti(int code) => code is >= 8 and <= 12;

    /// <summary>
    /// The race menu's entries in list order: race codes 1..5, then 7 (the program skips half-orc, 6, and maps the
    /// sixth line to human).
    /// </summary>
    public static readonly IReadOnlyList<int> RaceCodes = [1, 2, 3, 4, 5, 7];

    private readonly CreationTables _t;
    private readonly Func<ushort, ushort> _random;

    public EclCharacter Character { get; }

    /// <summary>
    /// The record before the first menu (0x19EEB–0x19FA9): zeroed; icon colours 0xC1..0xC6 from DS:3CF5[1..6] (colour
    /// c gives the pair (c + 8)·16 + c); base AC 50 and THAC0 40 (60 − x codes); status okay, in play; portrait head and
    /// body 1; icon footprint 1; byte 0xAB := Random(256), the first draw.
    /// </summary>
    public CharacterCreation(CreationTables tables, Func<ushort, ushort> random)
    {
        _t = tables;
        _random = random;
        var c = Character = new EclCharacter();
        for (int k = 1; k <= 6; k++)
        {
            int col = tables.At(CreationTables.IconColours + k);
            c[IconColours - 1 + k] = (byte)(((col + 8) << 4) + col);
        }
        c[AcBase] = 0x32;
        c[Thac0Base] = 0x28;
        c[Status] = 0;
        c[InPlay] = 1;
        c[PortraitHead] = 1;
        c[PortraitBody] = 1;
        c[IconFootprint] = 1;
        c[RandomByte] = (byte)random(256);
    }

    // ---- race, gender ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Race pick (0x1A10B–0x1A298): the race code; icon size 0xC0 := 1 for dwarf, gnome and halfling, else 2; and the
    /// race's permanent effects (time 0, value 0xFF; effect ids in the order the program adds them).
    /// </summary>
    public void PickRace(int race)
    {
        var c = Character;
        c[Race] = (byte)race;
        byte[] effects = race switch
        {
            5 => [0x5A, 0x61],
            1 => [0x5A, 0x61, 0x1A, 0x2F],
            3 => [0x61, 0x12, 0x2F, 0x30],
            2 => [0x6B],
            4 => [0x7C],
            _ => [],
        };
        c[IconSize] = race is 1 or 3 or 5 ? (byte)1 : (byte)2;
        foreach (var id in effects)
            EffectHandlers.AddEffect(c, id, 0, 0xFF, false);
    }

    /// <summary>Gender pick (0x1A3A1): 0 male, 1 female.</summary>
    public void PickGender(int gender) => Character[Gender] = (byte)gender;

    // ---- class --------------------------------------------------------------------------------------------------------

    /// <summary>The class codes the race may take, in menu order (DS:3E17 + 12·race: count, then the codes).</summary>
    public IReadOnlyList<int> ClassChoices() => ClassesFor(_t, Character[Race]);

    public static IReadOnlyList<int> ClassesFor(CreationTables t, int race)
    {
        int n = t.At(CreationTables.ClassesByRace + 12 * race);
        var list = new int[n];
        for (int k = 1; k <= n; k++)
            list[k - 1] = t.At(CreationTables.ClassesByRace + 12 * race + k);
        return list;
    }

    /// <summary>The base classes (0..7) of a class code, in level-byte order.</summary>
    public static IReadOnlyList<int> BaseClasses(int code) => code switch
    {
        <= 7 => [code],
        8 => [0, 2],
        9 => [0, 2, 5],
        10 => [0, 4],
        11 => [0, 5],
        12 => [0, 6],
        13 => [2, 5],
        14 => [2, 6],
        15 => [2, 5, 6],
        16 => [5, 6],
        _ => [],
    };

    /// <summary>
    /// Class pick (0x1A4F6–0x1A6EA): class code; highest level 1; level 1 in each base class; thief skills (0x2ADB0)
    /// when the thief level is set (Dexterity is still 0 here, as in the program); THAC0 base := the best of
    /// DS:3C7C[class·11 + level] over the classes, and the usability bits 0xB0 := the sum of DS:05D6[class]; then the
    /// saving throws (0x2ACDC).
    /// </summary>
    public void PickClass(int code)
    {
        var c = Character;
        c[Class] = (byte)code;
        c[LevelHighest] = 1;
        foreach (var b in BaseClasses(code))
            c[Levels + b] = 1;
        if ((sbyte)c[Levels + 6] > 0)
            LevelStats.ThiefSkillsOf(c, _t.Levels);
        c[Usable] = 0;
        c[Thac0Base] = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            int lvl = (sbyte)c[Levels + cls];
            if (lvl <= 0)
                continue;
            byte th = _t.Levels.At(_t.Levels.Thac0 + cls * 11 + lvl);
            if (th > c[Thac0Base])
                c[Thac0Base] = th;
            c[Usable] = (byte)(c[Usable] + _t.At(CreationTables.Usability + cls));
        }
        LevelStats.SavingThrows(c, _t.Levels);
    }

    // ---- alignment, age -----------------------------------------------------------------------------------------------

    /// <summary>The alignments the class allows, in menu order (DS:3FE7 + 10·class: count, then the codes).</summary>
    public IReadOnlyList<int> AlignmentChoices() => AlignmentsFor(_t, Character[Class]);

    public static IReadOnlyList<int> AlignmentsFor(CreationTables t, int code)
    {
        int n = t.At(CreationTables.AlignmentsByClass + 10 * code);
        var list = new int[n];
        for (int k = 1; k <= n; k++)
            list[k - 1] = t.At(CreationTables.AlignmentsByClass + 10 * code + k);
        return list;
    }

    /// <summary>
    /// Alignment pick (0x1A838), then the starting age (0x1A862–0x1AB5C). DS:3E5B + 0x1C·race + 4·class holds a word
    /// base, a dice count and sides. A single class (0..7) rolls: age := dice + base. A multi-class takes base + count ×
    /// sides (the dice's maximum, no roll) of one class: cleric for 8, 9, 11, 12; magic-user for 13, 15, 16; fighter for
    /// 14. Cleric/ranger (10) is not handled: the age stays 0.
    /// </summary>
    public void PickAlignment(int alignment)
    {
        var c = Character;
        c[Alignment] = (byte)alignment;
        int race = (sbyte)c[Race], code = (sbyte)c[Class];
        int Entry(int cls) => CreationTables.StartingAge + race * 0x1C + cls * 4;
        if (code <= 7)
        {
            int e = Entry(code);
            byte roll = FightRules.Dice(_random, _t.At(e + 2), _t.At(e + 3));
            c.SetWord(Age, (ushort)(roll + _t.Word(e)));
            return;
        }
        int? from = code switch
        {
            8 or 9 or 11 or 12 => 0,
            13 or 15 or 16 => 5,
            14 => 2,
            _ => null,
        };
        if (from is { } cls)
        {
            int e = Entry(cls);
            c.SetWord(Age, (ushort)(_t.Word(e) + _t.At(e + 2) * _t.At(e + 3)));
        }
    }

    // ---- rolls --------------------------------------------------------------------------------------------------------

    private ushort AgeStep(int k) => _t.Word(CreationTables.AgeSteps + Character[Race] * 10 + 2 * k);

    private bool Older(int k) => AgeStep(k) < Character.Word(Age);

    private byte RaceMin(int field) => _t.At(CreationTables.RaceLimits + Character[Race] * 16 + field);

    private byte ClassMin(int stat) => _t.At(CreationTables.ClassMinimums + Character[Class] * 6 + stat);

    /// <summary>
    /// One roll of the character (the loop from 0x1AB7F to the keep question, 0x1B79B): the six abilities, then gold,
    /// spells and hit points, then the stat recalculation the screen runs (START 0x1758). Rolled again for each "No".
    /// <list type="bullet">
    /// <item>Each ability is the best of six 3d6. The program's race adjustment (+1 Con −1 Cha for a dwarf and so on)
    /// tests the race menu's loop counter, which is 7 after the menu, so it never applies (a program bug, kept).</item>
    /// <item>Age steps (DS:3F2F + 10·race, words 1..4: "older than"): Str +1, −1, −2, −1 (each only while Str &gt; 0); Int
    /// +1 at steps 2, 4; Wis −1 when not past step 1, +1 at steps 2, 3, 4; Dex −2 at 3, −1 at 4; Con −1 at 2, 3, 4.</item>
    /// <item>Then the race limits (DS:3DA3 + 16·race: Str min/max by gender, exceptional max by gender, then min/max of
    /// Int, Wis, Dex, Con, Cha), the class minimum (DS:3F81 + 6·class + stat); Wisdom at least 13 for a cleric
    /// multi-class (8..12). Str 18 with a fighter level: exceptional := Random(100) + 1, at most the race's.</item>
    /// <item>HP current := max; 2 primary attacks (× 2), 1d2 unarmed, Str bonus allowed, movement 12. Per base class:
    /// cleric slot 1 := 1, magic-user slot 1 := 1; gold dice DS:4263[2·class] (count, sides) summed as a byte; a cleric
    /// gets the Wisdom bonus (0x2AC36) and knows every first-level cleric spell; a magic-user knows spells 11, 18, 19 and
    /// 21. Gold := sum × 10 / classes.</item>
    /// <item>Rolled HP 0xB1 := the class dice (0x1DBB4, every class); max HP := (rolled + Con bonus (0x1D8B0)) /
    /// classes, or 1 when a negative bonus would leave too little; current := max; 0xB1 := rolled / classes.</item>
    /// </list>
    /// </summary>
    public void Roll(BaseItemTable items)
    {
        var c = Character;
        c[ExceptionalStrength] = 0;
        for (int i = 0; i <= 5; i++)
        {
            int at = Abilities + i;
            c[at] = 0;
            for (int j = 0; j < 6; j++)
            {
                byte roll = FightRules.Dice(_random, 3, 6);
                if (c[at] < roll)
                    c[at] = roll;
            }
            switch (i)
            {
                case Str:
                    if (c[at] > 0 && Older(1))
                        c[at]++;
                    if (c[at] > 0 && Older(2))
                        c[at]--;
                    if (c[at] > 0 && Older(3))
                        c[at] -= 2;
                    if (c[at] > 0 && Older(4))
                        c[at]--;
                    Clamp(at, RaceMin(c[Gender]), RaceMin(2 + c[Gender]), ClassMin(i));
                    if (c[at] == 18 && (sbyte)c[Levels + 2] > 0)
                    {
                        c[ExceptionalStrength] = (byte)(_random(100) + 1);
                        byte most = RaceMin(4 + c[Gender]);
                        if (most < c[ExceptionalStrength])
                            c[ExceptionalStrength] = most;
                    }
                    break;
                case Int:
                    if (Older(2))
                        c[at]++;
                    if (Older(4))
                        c[at]++;
                    Clamp(at, RaceMin(6), RaceMin(7), ClassMin(i));
                    break;
                case Wis:
                    if (!Older(1))
                        c[at]--;
                    if (Older(2))
                        c[at]++;
                    if (Older(3))
                        c[at]++;
                    if (Older(4))
                        c[at]++;
                    Clamp(at, RaceMin(8), RaceMin(9), ClassMin(i));
                    if (c[at] < 13 && IsClericMulti(c[Class]))
                        c[at] = 13;
                    break;
                case Dex:
                    if (Older(3))
                        c[at] -= 2;
                    if (Older(4))
                        c[at]--;
                    Clamp(at, RaceMin(10), RaceMin(11), ClassMin(i));
                    break;
                case Con:
                    if (Older(2))
                        c[at]--;
                    if (Older(3))
                        c[at]--;
                    if (Older(4))
                        c[at]--;
                    Clamp(at, RaceMin(12), RaceMin(13), ClassMin(i));
                    break;
                case Cha:
                    Clamp(at, RaceMin(14), RaceMin(15), ClassMin(i));
                    break;
            }
        }

        c[HpCurrent] = c[HpMax];
        c[PrimaryAttacks] = 2;
        c[PrimaryDice] = 1;
        c[PrimarySides] = 2;
        c[StrengthBonus] = 1;
        c[MovementBase] = 12;
        byte goldSum = 0, classes = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            if ((sbyte)c[Levels + cls] <= 0)
                continue;
            if (cls == 0)
                c[ClericSlot1] = 1;
            else if (cls == 5)
                c[MageSlot1] = 1;
            goldSum = (byte)(goldSum + FightRules.Dice(_random, _t.At(CreationTables.GoldDice + 2 * cls),
                _t.At(CreationTables.GoldDice + 2 * cls + 1)));
            classes++;
            if (cls == 0)
            {
                LevelStats.WisdomBonus(c);
                for (int s = 1; s <= 0x38; s++)
                {
                    int e = _t.Levels.SpellTable + s * 16;
                    if (_t.Levels.At(e) == 0 && _t.Levels.At(e + 1) == 1)
                        c[KnownBase + s] = 1;
                }
            }
            else if (cls == 5)
            {
                foreach (int s in (int[])[11, 18, 19, 21])
                    c[KnownBase + s] = 1;
            }
        }
        c.SetWord(Gold, (ushort)(goldSum * 10 / classes));

        c[HpRolled] = RollFirstLevelHp(c, 0xFF);
        c[HpMax] = c[HpRolled];
        int bonus = ConHpBonus(c);
        if (bonus < 0)
            c[HpMax] = c[HpMax] > Math.Abs(bonus) + classes ? (byte)((c[HpMax] + bonus) / classes) : (byte)1;
        else
            c[HpMax] = (byte)((c[HpMax] + bonus) / classes);
        c[HpCurrent] = c[HpMax];
        c[HpRolled] = (byte)(c[HpRolled] / classes);

        StatRecalc.Recalculate(c, items);
    }

    private void Clamp(int at, byte min, byte max, byte classMin)
    {
        var c = Character;
        if (c[at] < min)
            c[at] = min;
        if (c[at] > max)
            c[at] = max;
        if (c[at] < classMin)
            c[at] = classMin;
    }

    /// <summary>
    /// 0x1DBB4 (creature, class mask): for each base class with a level and a bit in the mask (DS:05DE[class]), dice
    /// DS:08B6[class] × DS:08BE[class]; at level 1 at least two thirds of the sides. A byte sum.
    /// </summary>
    public byte RollFirstLevelHp(EclCharacter c, byte mask)
    {
        byte sum = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            if ((sbyte)c[Levels + cls] <= 0 || (_t.At(CreationTables.ClassBits + cls) & mask) == 0)
                continue;
            byte sides = _t.At(CreationTables.HpDiceSides + cls);
            byte roll = FightRules.Dice(_random, _t.At(CreationTables.HpDiceCount + cls), sides);
            if (c[Levels + cls] == 1 && roll < sides * 2 / 3)
                roll = (byte)(sides * 2 / 3);
            sum = (byte)(sum + roll);
        }
        return sum;
    }

    /// <summary>
    /// 0x1D8B0: Constitution bonus to hit points, a signed byte: DS:408E[Con] once per base class with a level, plus 1
    /// above Con 16 and 1 more above 17 when the class code is fighter (2).
    /// </summary>
    public sbyte ConHpBonus(EclCharacter c) => ConHpBonus(_t, c);

    /// <inheritdoc cref="ConHpBonus(EclCharacter)"/>
    public static sbyte ConHpBonus(CreationTables t, EclCharacter c)
    {
        sbyte bonus = 0;
        byte con = c[Abilities + Con];
        for (int cls = 0; cls <= 7; cls++)
        {
            if ((sbyte)c[Levels + cls] <= 0)
                continue;
            bonus = (sbyte)(bonus + (sbyte)t.At(CreationTables.ConHp + con));
            if (con > 16 && c[Class] == 2)
                bonus++;
            if (con > 17 && c[Class] == 2)
                bonus++;
        }
        return bonus;
    }

    // ---- after "keep" -------------------------------------------------------------------------------------------------

    /// <summary>The name prompt (0x1B7BE): up to 15 characters; an empty name is asked again.</summary>
    public void SetName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("a character needs a name", nameof(name));
        Character.Name = name.Length > EclCharacter.MaxNameLength ? name[..EclCharacter.MaxNameLength] : name;
    }
}
