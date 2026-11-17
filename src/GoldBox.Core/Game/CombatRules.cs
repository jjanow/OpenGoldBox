using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>Creature status byte (record 0x10C, docs/re/character-format.md).</summary>
public enum CreatureStatus : byte
{
    Okay = 0, Animated = 1, TemporarilyRemoved = 2, Fled = 3, Unconscious = 4, Dying = 5, Dead = 6, Petrified = 7, Gone = 8,
}

/// <summary>
/// Combat and after-combat rules read from the program (docs/re/combat-format.md). Pure functions on creature records;
/// the encounter host applies them. Undecoded parts of the fight (actions, AI, spells) are not here.
/// </summary>
public static class CombatRules
{
    /// <summary>Record offsets the combat code reads (docs/re/character-format.md).</summary>
    public const int Str = 0x10, Int = 0x11, Wis = 0x12, Dex = 0x13, Class = 0x2F, LevelHighest = 0x73, Npc = 0x84,
        Money = 0x88, Experience = 0xAC, HpRolled = 0xB1, XpBase = 0xB8, XpPerRolledHp = 0xBA, Status = 0x10C,
        InPlay = 0x10D, Hostile = 0x10E, AutoCombat = 0x10F, HpCurrent = 0x11B;

    /// <summary>Item record offsets (docs/re/item-format.md): plus, equipped, value.</summary>
    public const int ItemPlus = 0x32, ItemEquipped = 0x34, ItemValue = 0x3A;

    /// <summary>
    /// The next creature to act (GAME.OVR 0x9938). Walks the whole list in order and rolls 1d100 for every creature on
    /// every call (so each pick costs one draw per list member). A creature with a higher initiative than the best so
    /// far resets the best roll to its own; a creature is picked when its initiative is ≥ the best and its roll ≥ the
    /// best roll (so a full tie goes to the later creature). Initiative is the signed byte +3 of the combat record.
    /// Returns the index, or null when no creature has an initiative above 0.
    /// </summary>
    /// <param name="initiatives">Initiative of each list member, in list order.</param>
    /// <param name="d100">One 1d100 roll (1–100), called once per member in list order.</param>
    public static int? NextCreature(IReadOnlyList<sbyte> initiatives, Func<int> d100)
    {
        int best = 0, bestRoll = 0;
        int? next = null;
        for (int i = 0; i < initiatives.Count; i++)
        {
            int roll = (byte)d100();
            int init = initiatives[i];
            if (init > best)
                bestRoll = roll;
            if (init >= best && roll >= bestRoll)
            {
                bestRoll = roll;
                best = (byte)init; // the program keeps the best in a byte
                next = i;
            }
        }
        return best == 0 ? null : next;
    }

    /// <summary>
    /// Experience one defeated creature is worth (GAME.OVR 0x5645–0x5673): xp_base (0xB8) + xp_per_rolled_hp (0xBA) ×
    /// hp.rolled (0xB1). The product is a 16-bit multiply, sign-extended.
    /// </summary>
    public static int CreatureExperience(EclCharacter c) =>
        c.Word(XpBase) + (short)(ushort)(c[HpRolled] * c[XpPerRolledHp]);

    /// <summary>
    /// Experience from the treasure pool (GAME.OVR 0x57B1–0x5845): copper / 200 + silver / 20 + electrum / 2 + gold +
    /// platinum × 5 + gems × 250 + jewellery × 2200, integer divisions on the pool totals (DS:67F4, seven dwords).
    /// </summary>
    public static int PoolExperience(IReadOnlyList<uint> pool) =>
        (int)(pool[0] / 200) + (int)(pool[1] / 20) + (int)(pool[2] / 2) + (int)pool[3] + (int)pool[4] * 5 +
        (int)pool[5] * 250 + (int)pool[6] * 2200;

    /// <summary>Experience for one treasure item (GAME.OVR 0x586F–0x588A): 400 × plus when plus &gt; 0, 16-bit product sign-extended.</summary>
    public static int ItemExperience(byte[] item)
    {
        sbyte plus = (sbyte)item[ItemPlus];
        return plus > 0 ? (short)(ushort)(plus * 400) : 0;
    }

    /// <summary>
    /// What one character gets of a share (GAME.OVR 0x58D3): by class byte 0x2F — cleric (0) with Wisdom &gt; 15,
    /// fighter (2) with Strength &gt; 15, magic-user (5) with Intelligence &gt; 15, thief (6) with Dexterity &gt; 15:
    /// share / 10 + share; classes 8, 10–14 and 16 (two classes): share / 2; 9 and 15 (three classes): share / 3;
    /// any other class (druid, paladin, ranger, monk, or a prime score ≤ 15): the share. Integer division.
    /// </summary>
    public static int ExperienceFor(EclCharacter c, int share)
    {
        bool Prime(int offset) => c[offset] > 15;
        return c[Class] switch
        {
            0 => Prime(Wis) ? share / 10 + share : share,
            2 => Prime(Str) ? share / 10 + share : share,
            5 => Prime(Int) ? share / 10 + share : share,
            6 => Prime(Dex) ? share / 10 + share : share,
            8 or (>= 10 and <= 14) or 16 => share / 2,
            9 or 15 => share / 3,
            _ => share,
        };
    }

    /// <summary>
    /// The award loop (GAME.OVR 0x58D3): every creature in the list whose in-play byte (0x10D) is not 0 and whose
    /// status is not animated gets <see cref="ExperienceFor"/> added to its experience dword (0xAC). The loop runs
    /// over the whole list, monsters included (they are removed right after).
    /// </summary>
    public static void AwardExperience(IEnumerable<EclCharacter> list, int share)
    {
        foreach (var c in list)
        {
            if (c[InPlay] == 0 || c[Status] == (byte)CreatureStatus.Animated)
                continue;
            uint xp = (uint)(c[Experience] | c[Experience + 1] << 8 | c[Experience + 2] << 16 | c[Experience + 3] << 24);
            xp = unchecked(xp + (uint)ExperienceFor(c, share));
            c[Experience] = (byte)xp;
            c[Experience + 1] = (byte)(xp >> 8);
            c[Experience + 2] = (byte)(xp >> 16);
            c[Experience + 3] = (byte)(xp >> 24);
        }
    }
}
