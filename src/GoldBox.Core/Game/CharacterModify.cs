using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>The keys Modify Character tells apart (its word menu 0x2CC8C runs with the keypad flag set).</summary>
public enum ModifyKey
{
    /// <summary>Down (extended 0x50, keypad 2; the extended code 0x0D does the same): the next field, 6 → 0.</summary>
    Down,
    /// <summary>Up (extended 0x48, keypad 8): the previous field, 0 → 6.</summary>
    Up,
    /// <summary>Left (extended 0x4B, keypad 4): the field one lower.</summary>
    Left,
    /// <summary>Right (extended 0x4D, keypad 6): the field one higher.</summary>
    Right,
    /// <summary>K (not extended): keep the changes.</summary>
    Keep,
    /// <summary>E, or Escape (the menu returns 0): put the old values back.</summary>
    Exit,
    /// <summary>Any other key (Return, space, the other cursor keys): nothing changes but the page is redone.</summary>
    Other,
}

/// <summary>
/// Modify Character (Pool of Radiance DOS v1.3, GAME.OVR 0x1BA1E; the party menu's and the training hall's Modify;
/// program read s20, docs/re/start-menu.md "Modify"). Only a character with no experience may be changed; the six
/// abilities and the maximum hit points step up and down within the race's limits on the character page. No dice are
/// rolled and no file is written: the record in the party changes. Checked against the routine under emulation
/// (<c>CharacterModifyProgramTests</c>, <c>tools/scripts/por_modify_oracle.py</c>); the program's bugs are kept:
/// <list type="bullet">
/// <item>Lowering an ability below the class minimum sets it to the race minimum, which can be lower still.</item>
/// <item>Raising checks only the race maximum (never the class); the multi-class cleric's Wisdom 13 is not applied.</item>
/// <item>Exit puts back the abilities, exceptional strength and maximum HP, sets current HP to that maximum (a wounded
/// character is healed), but keeps the cleric spell slots a Wisdom change rewrote.</item>
/// <item>Dexterity changes leave the thief skills as they were; nothing redoes the saving throws.</item>
/// </list>
/// </summary>
public sealed class CharacterModify
{
    /// <summary>Fields: the abilities 0..5 (record 0x10 + i), then the maximum hit points.</summary>
    public const int HpField = 6, FieldCount = 7;

    /// <summary>Record bytes: experience (dword), NPC treasure share (Keep writes 1), fighter level.</summary>
    public const int Experience = 0xAC, TreasureShare = 0x85, FighterLevel = 0x98;

    private const int Abilities = CharacterCreation.Abilities, Exceptional = CharacterCreation.ExceptionalStrength,
        HpMax = CharacterCreation.HpMax, HpCurrent = CharacterCreation.HpCurrent, Levels = CharacterCreation.Levels;

    private readonly CreationTables _t;
    private readonly BaseItemTable _items;
    private readonly byte[] _saved = new byte[6];
    private readonly byte _savedExceptional, _savedHp;

    public EclCharacter Character { get; }

    /// <summary>The field the cursor is on (the program's [bp-0xD]): 0..5 an ability, 6 the hit points.</summary>
    public int Field { get; private set; }

    /// <summary>
    /// The refusal test (0x1BA25): the experience dword 0xAC, signed, above 0 → the name and <c>PartyMsg.CantModify</c>
    /// on the bottom line (START 0x7DF8, colour 14) and nothing else. Negative experience may be modified.
    /// </summary>
    public static bool CanModify(EclCharacter c) =>
        (int)(c.Word(Experience) | (uint)c.Word(Experience + 2) << 16) <= 0;

    /// <summary>
    /// The start (0x1BA61–0x1BAA9): the page (View 0x21084, which runs the stat recalculation START 0x1758); the six
    /// abilities, exceptional strength and maximum HP are kept for Exit; the cursor on Strength.
    /// </summary>
    public CharacterModify(CreationTables tables, BaseItemTable items, EclCharacter c)
    {
        _t = tables;
        _items = items;
        Character = c;
        StatRecalc.Recalculate(c, items);
        for (int i = 0; i < 6; i++)
            _saved[i] = c[Abilities + i];
        _savedExceptional = c[Exceptional];
        _savedHp = c[HpMax];
        Field = 0;
    }

    private byte Limit(int k) => _t.At(CreationTables.RaceLimits + Character[CharacterCreation.Race] * 16 + k);

    private byte ClassMin(int stat) => _t.At(CreationTables.ClassMinimums + Character[CharacterCreation.Class] * 6 + stat);

    /// <summary>The race limits of an ability: Strength's by gender (min +g, max +2+g), the others min / max at 6 + 2(i−1).</summary>
    private (byte Min, byte Max) Limits(int i)
    {
        if (i == 0)
            return (Limit(Character[CharacterCreation.Gender]), Limit(2 + Character[CharacterCreation.Gender]));
        return (Limit(6 + 2 * (i - 1)), Limit(7 + 2 * (i - 1)));
    }

    /// <summary>
    /// One key of the loop (0x1BAB6–0x1C263). Every key but Exit ends with the stat recalculation (START 0x1758, and
    /// again in the redraw 0x21642). Returns true when Modify is over (Keep or Exit).
    /// </summary>
    public bool Press(ModifyKey key)
    {
        var c = Character;
        switch (key)
        {
            case ModifyKey.Down:
                Field = Field + 1 > HpField ? 0 : Field + 1;
                break;
            case ModifyKey.Up:
                Field = Field == 0 ? HpField : Field - 1;
                break;
            case ModifyKey.Left:
                Lower();
                break;
            case ModifyKey.Right:
                Raise();
                break;
            case ModifyKey.Exit:
                // 0x1C1D8: the old values back; current HP := the restored maximum. 0xB2 and the Wisdom bonus slots stay.
                for (int i = 0; i < 6; i++)
                    c[Abilities + i] = _saved[i];
                c[Exceptional] = _savedExceptional;
                c[HpMax] = _savedHp;
                c[HpCurrent] = c[HpMax];
                StatRecalc.Recalculate(c, _items);
                return true;
        }
        StatRecalc.Recalculate(c, _items);
        StatRecalc.Recalculate(c, _items);
        if (key != ModifyKey.Keep)
            return false;
        c[TreasureShare] = 1;
        return true;
    }

    /// <summary>
    /// Left (0x1BB37–0x1BEDE), byte arithmetic. Strength: with exceptional strength above 0 that goes down instead.
    /// An ability (after the step) below its race minimum or its class minimum (DS:3F81 + 6·class + i) is set to the
    /// race minimum. Wisdom: cleric slot 0xB2 := 1 when above 0, then the Wisdom bonus (0x2AC36). Constitution: the
    /// maximum HP comes down to <see cref="HpMaximum"/>. Hit points: one less, at least <see cref="HpMinimum"/>.
    /// Constitution and hit points set current HP := maximum.
    /// </summary>
    private void Lower()
    {
        var c = Character;
        if (Field >= HpField)
        {
            c[HpMax]--;
            byte lo = HpMinimum(_t, c);
            if (lo > c[HpMax])
                c[HpMax] = lo;
            c[HpCurrent] = c[HpMax];
            return;
        }
        int at = Abilities + Field;
        c[at]--;
        if (Field == 0 && c[Exceptional] > 0)
        {
            c[Exceptional]--;
            c[at]++;
            return;
        }
        var (min, _) = Limits(Field);
        if (c[at] < min || c[at] < ClassMin(Field))
            c[at] = min;
        AfterChange(lowered: true);
    }

    /// <summary>
    /// Right (0x1BEE8–0x1C1C6). An ability above its race maximum goes back to it; Strength first: past 18 with a
    /// fighter level (0x98 &gt; 0) and exceptional strength below the race's (+4 + gender), exceptional strength + 1.
    /// Wisdom as for Left. Constitution: the maximum HP comes up to <see cref="HpMinimum"/>. Hit points: one more, at
    /// most <see cref="HpMaximum"/>.
    /// </summary>
    private void Raise()
    {
        var c = Character;
        if (Field >= HpField)
        {
            c[HpMax]++;
            byte hi = HpMaximum(_t, c);
            if (hi < c[HpMax])
                c[HpMax] = hi;
            c[HpCurrent] = c[HpMax];
            return;
        }
        int at = Abilities + Field;
        c[at]++;
        var (_, max) = Limits(Field);
        if (c[at] > max)
        {
            if (Field == 0 && c[at] > 18 && (sbyte)c[FighterLevel] > 0 &&
                Limit(4 + c[CharacterCreation.Gender]) > c[Exceptional])
                c[Exceptional]++;
            c[at] = max;
        }
        AfterChange(lowered: false);
    }

    private void AfterChange(bool lowered)
    {
        var c = Character;
        switch (Field)
        {
            case CharacterCreation.Wis:
                if (c[CharacterCreation.ClericSlot1] > 0)
                    c[CharacterCreation.ClericSlot1] = 1;
                LevelStats.WisdomBonus(c);
                break;
            case CharacterCreation.Con:
                if (lowered)
                {
                    byte hi = HpMaximum(_t, c);
                    if (hi < c[HpMax])
                        c[HpMax] = hi;
                }
                else
                {
                    byte lo = HpMinimum(_t, c);
                    if (lo > c[HpMax])
                        c[HpMax] = lo;
                }
                c[HpCurrent] = c[HpMax];
                break;
        }
    }

    /// <summary>
    /// The Constitution bonus applied as Create does (0x1D99D / 0x1DB46): a negative bonus b leaves (sum + b) / classes
    /// when sum &gt; |b| + classes, else 1; otherwise (sum + b) / classes. A byte.
    /// </summary>
    private static byte WithBonus(int sum, sbyte bonus, int classes)
    {
        if (bonus < 0)
            return sum > Math.Abs((int)bonus) + classes ? (byte)((sum + bonus) / classes) : (byte)1;
        return (byte)((sum + bonus) / classes);
    }

    /// <summary>
    /// 0x1D932, the least maximum HP: the sum of the class levels (each base class with a level above 0, signed), one
    /// more for a ranger (4) or monk (7) level, with the Constitution bonus (0x1D8B0) as <see cref="WithBonus"/>.
    /// </summary>
    public static byte HpMinimum(CreationTables t, EclCharacter c)
    {
        byte sum = 0, classes = 0;
        for (int k = 0; k <= 7; k++)
        {
            if ((sbyte)c[Levels + k] <= 0)
                continue;
            sum += c[Levels + k];
            classes++;
            if (k is 4 or 7)
                sum++;
        }
        return WithBonus(sum, CharacterCreation.ConHpBonus(t, c), classes);
    }

    /// <summary>
    /// 0x1DA0B, the most maximum HP: per base class with a level L above 0: cleric, druid L × 8; fighter, paladin L × 10;
    /// ranger (L + 1) × 8; magic-user L × 4; thief L × 6; monk (L + 1) × 4, summed in a byte; with the Constitution
    /// bonus as <see cref="WithBonus"/>. (The program takes the bonus of the current character DS:5D92, the same record
    /// whenever Modify calls it.)
    /// </summary>
    public static byte HpMaximum(CreationTables t, EclCharacter c)
    {
        byte sum = 0, classes = 0;
        for (int k = 0; k <= 7; k++)
        {
            int l = (sbyte)c[Levels + k];
            if (l <= 0)
                continue;
            classes++;
            sum += (byte)(k switch
            {
                0 or 1 => l * 8,
                2 or 3 => l * 10,
                4 => (l + 1) * 8,
                5 => l * 4,
                6 => l * 6,
                _ => (l + 1) * 4,
            });
        }
        return WithBonus(sum, CharacterCreation.ConHpBonus(t, c), classes);
    }
}
