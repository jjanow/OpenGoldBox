using System.Globalization;
using GoldBox.Core.Ecl;

namespace GoldBox.Client.Play;

/// <summary>
/// The character page of View (GAME.OVR 0x21084, read in s18 and checked against DOSBox-X): what it writes where, in
/// the program's order. Texts come from the program (<see cref="GameText"/>, the DS name tables of
/// <see cref="ClientResources"/>); the layout is the routine's:
/// <list type="bullet">
/// <item>row 1: the name (START 0x1816: in play 0 → light red, hostile → yellow, else cyan) at column 1; an NPC
/// (record 0x84 &gt; 0x7F) gets the NPC tag in green at column name length + 3;</item>
/// <item>row 3: gender (0x9E), race (0x2E) and the age word with record word 0x30, white, from column 1, each one column
/// after the previous text; row 4 the alignment (0xA0), row 5 the class (0x2F), white;</item>
/// <item>rows 7–12: the six ability labels (green, column 1) and values (0x2193B: green at column 5, 6 when below 10;
/// strength 18 with exceptional strength 0x16 &gt; 0 adds "(nn)" at column 7: two digits, 100 as "00");</item>
/// <item>rows 7.. (0x21547): the money of the current creature, coin kinds 6 down to 0 with a non-zero word 0x88 + 2k,
/// one row each: the coin name right-aligned to end at column 19, the amount at column 21, green;</item>
/// <item>row 15: the level label (column 1), the class levels 0x96–0x9D above 0 joined by "/" (column 7), the
/// experience label with the dword 0xAC (column 17), white;</item>
/// <item>rows 17–18 (0x21642, after the stat recalculation START 0x1758): AC label (1,17) and value (START 0x8EF: |0x111 −
/// 60| at column 4, "-" first when 0x111 &gt; 60, green); HP label (1,18) and 0x11B (START 0x973: column 4, yellow below
/// the maximum 0x32, else green); THAC0 label (9,17) and 60 − 0x110 (column 15); damage label (8,18) and 0x115 "d" 0x117
/// with 0x119 signed as "+n" / "-n" (column 15); encumbrance label (22,17) and word 0x102 (column 34); movement label
/// (25,18) and 0x11C, doubled with effect 0x2A, halved with effect 0x27 (column 34). Labels white, values green;</item>
/// <item>row 20: when a weapon is readied, the weapon label (1,20) and its name (8,20, green); row 21: when body armour is
/// readied, the armour label at column 2 and its name at column 8; row 22: the status label (1,22) and the status name
/// (record 0x10C) at column 8, green.</item>
/// </list>
/// Numbers are the program's unsigned Str of a byte, word or dword. The page's frame (braid rows 0 and 23) and its menu
/// are the <see cref="CombatPage"/>'s.
/// </summary>
public static class CombatSheet
{
    public const byte White = 15, Green = 10, Yellow = 14;
    public const int MoneyFirstRow = 7, MoneyNameEnd = 19, MoneyValueCol = 21;

    /// <summary>The name colour of START 0x1816: out of play light red, hostile yellow, else cyan.</summary>
    public static byte NameColour(EclCharacter c) =>
        c[0x10D] == 0 ? (byte)12 : c[0x10E] == 1 ? (byte)14 : (byte)11;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Our words where the program's tables are not read.</summary>
    private static readonly string[] Genders = ["MALE", "FEMALE"];
    private static readonly string[] Statuses =
        ["OKAY", "ANIMATED", "TEMP GONE", "RUNNING", "UNCONSCIOUS", "DYING", "DEAD", "STONED", "GONE"];
    private static readonly string[] Coins = ["CP", "SP", "EP", "GP", "PP", "GEMS", "JEWELS"];

    private static string Or(string? s, string fallback) => AmigaView.Case(s ?? fallback);

    /// <summary>The coin kinds the money lines show, top to bottom (0x21547: kind 6 down to 0, amount above 0).</summary>
    public static List<int> MoneyKinds(EclCharacter c)
    {
        var l = new List<int>();
        for (int k = 6; k >= 0; k--)
            if (c.Word(0x88 + 2 * k) > 0)
                l.Add(k);
        return l;
    }

    /// <summary>A coin kind's name on the money lines (DS:0FFA table), upper case.</summary>
    public static string CoinName(ClientResources r, int kind) =>
        Or(r.SheetCoin(kind), kind is >= 0 and < 7 ? Coins[kind] : "?");

    /// <summary>One money line: (column, text) of the name and of the amount.</summary>
    public static ((int Col, string Text) Name, (int Col, string Text) Amount) MoneyLine(ClientResources r, EclCharacter c, int kind)
    {
        string name = CoinName(r, kind);
        return ((MoneyNameEnd + 1 - name.Length, name), (MoneyValueCol, N(c.Word(0x88 + 2 * kind))));
    }

    /// <summary>
    /// The page's texts (column, row, text, colour). <paramref name="weapon"/> and <paramref name="armour"/> are the
    /// readied weapon and body armour (equipment slots 0 and 2), <paramref name="itemName"/> names an item as
    /// START 0x442 does.
    /// </summary>
    public static List<(int Col, int Row, string Text, byte Colour)> Build(ClientResources r, EclCharacter c,
        byte[]? weapon, byte[]? armour, Func<byte[], string> itemName)
    {
        var texts = r.Texts;
        string T(Msg m) => AmigaView.On ? texts.Raw(m) : texts[m];
        var l = new List<(int, int, string, byte)>();
        void Put(int col, int row, string s, byte colour) => l.Add((col, row, AmigaView.Case(s), colour));

        // Name and NPC tag.
        string name = AmigaView.Case(c.Name);
        Put(1, 1, name, NameColour(c));
        if (c[0x84] > 0x7F)
            Put(name.Length + 3, 1, T(Msg.NpcTag), Green);

        // Row 3: gender, race, age; rows 4, 5: alignment, class.
        int col = 1;
        foreach (var s in new[]
                 {
                     Or(r.Gender(c[0x9E]), c[0x9E] < 2 ? Genders[c[0x9E]] : "?"),
                     Or(r.Race(c[0x2E]), "RACE " + N(c[0x2E])),
                 })
        {
            Put(col, 3, s, White);
            col += s.Length + 1;
        }
        Put(col, 3, GameText.Cat(T(Msg.Age), N(c.Word(0x30))), White);
        Put(1, 4, Or(r.Alignment(c[0xA0]), "ALIGNMENT " + N(c[0xA0])), White);
        Put(1, 5, Or(r.ClassName(c[0x2F]), "CLASS " + N(c[0x2F])), White);

        // Abilities (labels at column 1, values by 0x2193B).
        Msg[] labels = [Msg.Str, Msg.Int, Msg.Wis, Msg.Dex, Msg.Con, Msg.Cha];
        for (int i = 0; i < 6; i++)
        {
            Put(1, 7 + i, T(labels[i]), Green);
            byte v = c[0x10 + i];
            Put(v < 10 ? 6 : 5, 7 + i, N(v), Green);
            if (i == 0 && v == 18 && c[0x16] > 0)
            {
                byte pct = c[0x16];
                string p = pct == 100 ? "00" : pct < 10 ? "0" + N(pct) : N(pct);
                Put(7, 7, "(" + p + ")", Green);
            }
        }

        // Money (0x21547).
        int row = MoneyFirstRow;
        foreach (int k in MoneyKinds(c))
        {
            var (n, a) = MoneyLine(r, c, k);
            Put(n.Col, row, n.Text, Green);
            Put(a.Col, row, a.Text, Green);
            row++;
        }

        // Level and experience (row 15).
        Put(1, 15, T(Msg.Level), White);
        var levels = new List<string>();
        for (int k = 0; k < 8; k++)
            if ((sbyte)c[0x96 + k] > 0)
                levels.Add(N(c[0x96 + k]));
        Put(7, 15, string.Join("/", levels), White);
        uint xp = (uint)(c[0xAC] | c[0xAD] << 8 | c[0xAE] << 16 | c[0xAF] << 24);
        Put(17, 15, GameText.Cat(T(Msg.Exp), N(xp)), White);

        // The fighting block (0x21642).
        Put(1, 17, T(Msg.AcLabel), White);
        byte ac = c[0x111];
        Put(4, 17, (ac > 60 ? "-" : "") + N(Math.Abs(ac - 60)), Green);
        Put(1, 18, T(Msg.HpLabel), White);
        Put(4, 18, N(c[0x11B]), c[0x11B] < c[0x32] ? Yellow : Green);
        Put(9, 17, T(Msg.Thac0Label), White);
        Put(15, 17, N((byte)(60 - c[0x110])), Green);
        string dmg = N(c[0x115]) + "d" + N(c[0x117]);
        sbyte bonus = (sbyte)c[0x119];
        if (bonus > 0)
            dmg += "+" + N(bonus);
        else if (bonus < 0)
            dmg += "-" + N((byte)Math.Abs(bonus));
        Put(8, 18, T(Msg.DamageLabel), White);
        Put(15, 18, dmg, Green);
        Put(22, 17, T(Msg.Encumbrance), White);
        Put(34, 17, N(c.Word(0x102)), Green);
        byte move = c[0x11C];
        if (c.HasEffect(0x2A))
            move = (byte)(move << 1);
        if (c.HasEffect(0x27))
            move = (byte)(move >> 1);
        Put(25, 18, T(Msg.Movement), White);
        Put(34, 18, N(move), Green);

        // Weapon, armour, status.
        row = 20;
        if (weapon is not null)
        {
            Put(1, row, T(Msg.WeaponLabel), White);
            Put(8, row, itemName(weapon), Green);
        }
        row++;
        if (armour is not null)
        {
            Put(2, row, T(Msg.ArmorLabel), White);
            Put(8, row, itemName(armour), Green);
        }
        row++;
        Put(1, row, T(Msg.Status), White);
        byte st = c[0x10C];
        Put(8, row, Or(r.StatusName(st), st < Statuses.Length ? Statuses[st] : N(st)), Green);
        return l;
    }
}
