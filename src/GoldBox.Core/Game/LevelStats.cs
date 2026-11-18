using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Where the level-stats routine's tables lie in the program's data segment (read from the player's program by
/// GoldBox.Data's <c>PorSpellData.LevelTables</c>; nothing is copied into our code). The routine indexes them with its
/// own arithmetic (Pascal arrays with non-zero bases, signed levels), so they are kept as offsets into the data segment
/// and read byte by byte as the program reads them.
/// </summary>
public sealed class LevelStatsTables
{
    /// <summary>The program's data segment as loaded (initialised data; bytes past it read as 0).</summary>
    public required byte[] Data { get; init; }
    /// <summary>DS:3C7C: base THAC0 by class × 11 + level.</summary>
    public required int Thac0 { get; init; }
    /// <summary>DS:40BC / DS:41D4: cleric / magic-user spell slots, level × 3 + spell level (1..3).</summary>
    public required int ClericSlots { get; init; }
    public required int MageSlots { get; init; }
    /// <summary>DS:31FA: the spell table, 16 bytes per spell (byte 0 class, byte 1 level).</summary>
    public required int SpellTable { get; init; }
    /// <summary>DS:426E: saving throws, class × 0x2D + level × 5 + save.</summary>
    public required int Saves { get; init; }
    /// <summary>DS:3CF3 / DS:3D3B / DS:3D4E: thief skills by level × 8 + skill, race × 8 + skill (signed), Dexterity × 5
    /// + skill (signed).</summary>
    public required int ThiefBase { get; init; }
    public required int ThiefRace { get; init; }
    public required int ThiefDex { get; init; }

    public byte At(int offset) => offset >= 0 && offset < Data.Length ? Data[offset] : (byte)0;
}

/// <summary>
/// Pool of Radiance DOS v1.3: the level-stats routine GAME.OVR 0x2AA87 (after a level changes: Restoration, level
/// drain) and the race level limit START 0x3AF7 (00BA:2F57). Program read (s15), docs/re/spells-effects.md "Level stats".
/// </summary>
public static class LevelStats
{
    public const int Thac0Base = 0x2D, LevelHighest = 0x73, FighterAttacks = 0xA1, Levels = 0x96, Wisdom = 0x12,
        Strength = 0x10, Dexterity = 0x13, Race = 0x2E, SlotsBase = 0xB1, MageSlotsBase = 0xB4, KnownBase = 0x32,
        Saves = 0x6D, ThiefLevel = 0x9C, ThiefSkills = 0x76;

    private static sbyte Level(EclCharacter c, int cls) => (sbyte)c[Levels + cls];

    /// <summary>
    /// 0x2AA87. 0x2D := 0; for each class 0..7 with level L (signed): 0x2D := DS:3C7C[class × 11 + L] when larger
    /// (unsigned); 0x73 := L when larger (signed). Fighter (2): 0xA1 := 3 when L &gt; 6, else 2. Cleric (0) with L &gt; 1:
    /// slots 0xB2..0xB4 := DS:40BC[L × 3 + 1..3], the Wisdom bonus (0x2AC36), and every cleric spell (table byte 0 = 0)
    /// of spells 1..56 whose level has slots becomes known (0x32 + spell := 1). Magic-user (5) with L &gt; 1: slots
    /// 0xB5..0xB7 := DS:41D4[L × 3 + 1..3]. Then the saving throws (0x2ACDC) and, with a thief level &gt; 0 (signed), the
    /// thief skills (0x2ADB0).
    /// </summary>
    public static void Apply(EclCharacter c, LevelStatsTables t)
    {
        c[Thac0Base] = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            int lvl = Level(c, cls);
            byte th = t.At(t.Thac0 + cls * 11 + lvl);
            if (th > c[Thac0Base])
                c[Thac0Base] = th;
            if ((sbyte)c[LevelHighest] < lvl)
                c[LevelHighest] = (byte)lvl;
            if (cls == 2)
                c[FighterAttacks] = lvl > 6 ? (byte)3 : (byte)2;
            else if (cls == 0 && lvl > 1)
            {
                for (int i = 1; i <= 3; i++)
                    c[SlotsBase + i] = t.At(t.ClericSlots + lvl * 3 + i);
                WisdomBonus(c);
                for (int spell = 1; spell <= 0x38; spell++)
                {
                    int e = t.SpellTable + 16 * spell;
                    if (t.At(e) == 0 && c[SlotsBase + (sbyte)t.At(e + 1)] > 0)
                        c[KnownBase + spell] = 1;
                }
            }
            else if (cls == 5 && lvl > 1)
                for (int i = 1; i <= 3; i++)
                    c[MageSlotsBase + i] = t.At(t.MageSlots + lvl * 3 + i);
        }
        SavingThrows(c, t);
        if ((sbyte)c[ThiefLevel] > 0)
            ThiefSkillsOf(c, t);
    }

    /// <summary>0x2AC36: Wisdom above 12, 13 add a 1st-level slot (0xB2), above 14, 15 a 2nd (0xB3), above 16 a 3rd
    /// (0xB4), each only when that level already has a slot.</summary>
    public static void WisdomBonus(EclCharacter c)
    {
        (int Above, int Slot)[] steps = [(12, 0xB2), (13, 0xB2), (14, 0xB3), (15, 0xB3), (16, 0xB4)];
        foreach (var (above, slot) in steps)
            if (c[Wisdom] > above && c[slot] > 0)
                c[slot]++;
    }

    /// <summary>0x2ACDC: each save 0..4 := 20, then the lowest (unsigned) of DS:426E[class × 0x2D + L × 5 + save] over the
    /// classes with level L &gt; 0 (signed).</summary>
    public static void SavingThrows(EclCharacter c, LevelStatsTables t)
    {
        for (int s = 0; s <= 4; s++)
        {
            c[Saves + s] = 0x14;
            for (int cls = 0; cls <= 7; cls++)
            {
                int lvl = Level(c, cls);
                if (lvl <= 0)
                    continue;
                byte v = t.At(t.Saves + cls * 0x2D + lvl * 5 + s);
                if (c[Saves + s] > v)
                    c[Saves + s] = v;
            }
        }
    }

    /// <summary>
    /// 0x2ADB0: thief skills 0x77..0x7E (i = 1..8): base b = DS:3CF3[thief level × 8 + i], race adjustment
    /// r = DS:3D3B[race × 8 + i] (signed). r &lt; 0 and |r| &gt; b → 0; else b + r (a byte), and for i &lt; 6 plus the
    /// Dexterity adjustment DS:3D4E[Dex × 5 + i] (signed, a byte sum).
    /// </summary>
    public static void ThiefSkillsOf(EclCharacter c, LevelStatsTables t)
    {
        int lvl = (sbyte)c[ThiefLevel], race = (sbyte)c[Race];
        for (int i = 1; i <= 8; i++)
        {
            byte b = t.At(t.ThiefBase + lvl * 8 + i);
            sbyte r = (sbyte)t.At(t.ThiefRace + race * 8 + i);
            if (r < 0 && Math.Abs((int)r) > b)
            {
                c[ThiefSkills + i] = 0;
                continue;
            }
            c[ThiefSkills + i] = (byte)(b + r);
            if (i < 6)
                c[ThiefSkills + i] = (byte)(c[ThiefSkills + i] + (sbyte)t.At(t.ThiefDex + c[Dexterity] * 5 + i));
        }
    }

    /// <summary>
    /// START 0x3AF7 (00BA:2F57; creature, class): true when the race's level limit stops the class from rising. Level
    /// L (signed) ≤ 0: no. Dwarf (race 1), fighter: L = 8 with Strength 17, or L = 7 with Strength below 17. Elf (2),
    /// fighter: L = 7; L = 6 with Strength 17; L = 5 below 17. Gnome (3), fighter: L = 6; L = 5 below 18. Half-elf (4):
    /// cleric L = 5; fighter L = 8; L = 7 with 17; L = 6 below 17. Halfling (5), fighter: L = 6; L = 5 with 17; L = 4
    /// below 17. Every other race and class: no. (Strength is byte 0x10 alone; 18/xx counts as 18.)
    /// </summary>
    public static bool RaceLimitStops(EclCharacter c, int cls)
    {
        int lvl = Level(c, cls);
        if (lvl <= 0)
            return false;
        byte str = c[Strength];
        return c[Race] switch
        {
            1 => cls == 2 && ((lvl == 8 && str == 17) || (lvl == 7 && str < 17)),
            2 => cls == 2 && (lvl == 7 || (lvl == 6 && str == 17) || (lvl == 5 && str < 17)),
            3 => cls == 2 && (lvl == 6 || (lvl == 5 && str < 18)),
            4 => (cls == 0 && lvl == 5) || (cls == 2 && (lvl == 8 || (lvl == 7 && str == 17) || (lvl == 6 && str < 17))),
            5 => cls == 2 && (lvl == 6 || (lvl == 5 && str == 17) || (lvl == 4 && str < 17)),
            _ => false,
        };
    }
}
