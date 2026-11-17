using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// TREASURE's random item maker (GAME.OVR 0x25C99, docs/re/item-format.md "Random treasure"): one 63-byte item record
/// for a base type, with the program's dice in the program's order. The random item array is the player's own data
/// (START.EXE DS:0970, read by GoldBox.Data); without it the copy step is skipped and noted in
/// <see cref="Diagnostics"/>.
/// </summary>
public sealed class TreasureMaker
{
    /// <summary>
    /// Words of the random item array the maker can reach, from DS:0970: index = magic number + field, magic up to
    /// 0x91 (and the javelin's 0x90), field 0..7.
    /// </summary>
    public const int TableWords = 0x91 + 8;

    private readonly ushort[]? _table;

    /// <param name="table">The words at DS:0970 onward (at least <see cref="TableWords"/>), or null.</param>
    public TreasureMaker(IReadOnlyList<ushort>? table)
    {
        if (table is not null && table.Count < TableWords)
            throw new ArgumentException($"random item array: {table.Count} words, need {TableWords}", nameof(table));
        _table = table?.Take(TableWords).ToArray();
    }

    /// <summary>Problems met (no random item array).</summary>
    public List<string> Diagnostics { get; } = [];

    // Item record offsets (docs/re/item-format.md).
    private const int Type = 0x2E, Word3 = 0x2F, Word2 = 0x30, Word1 = 0x31, Plus = 0x32, SaveBonus = 0x33,
        Hidden = 0x35, Weight = 0x37, Count = 0x39, Value = 0x3A, Props = 0x3C;

    /// <summary>
    /// One item of base <paramref name="type"/>. <paramref name="random"/> answers Random(n) (0..n−1); the program's
    /// dice are 1 + Random(n).
    /// </summary>
    public byte[] Make(byte type, Func<int, int> random)
    {
        int D(int sides) => random(sides) + 1;
        var r = new byte[EclCharacter.ItemRecordSize]; // zeroed (0x25CAB), next = nil
        r[Hidden] = 6;                                  // words 2 and 3 hidden (0x25CEA)
        r[Type] = type;
        byte magic = 0;                                 // [bp-4]

        if (type is >= 1 and <= 59 or 73 or 77 or 93)
        {
            r[Plus] = PlusRoll(D(20));
            sbyte plus = (sbyte)r[Plus];
            byte plusWord = (byte)(plus + 0xA1);
            switch (type)
            {
                case 21: // javelin: the 1d5 after the plus roll; a 5 sets the stray magic number 0x90
                    if (D(5) == 5)
                        magic = 0x90;
                    r[Word1] = 21;
                    r[Word2] = plusWord;
                    break;
                case 28:
                    r[Word1] = 28;
                    r[Word2] = plusWord;
                    break;
                case 50 or 51:
                    ArmourWords(r, 0x31, plusWord);
                    break;
                case 52:
                    ArmourWords(r, 0x32, plusWord);
                    break;
                case >= 53 and <= 58:
                    ArmourWords(r, 0x30, plusWord);
                    break;
                case 73:
                    r[Word1] = 0x3D;
                    r[Word2] = plusWord;
                    break;
                case 77: // bracers: plus becomes 2·plus + 2 (AC 4, 6, 8) with a word per value
                    r[Word1] = 0x4F;
                    r[Word2] = 0xA7;
                    r[Plus] = (byte)(plus * 2 + 2);
                    r[Word3] = r[Plus] switch { 4 => 0xDD, 6 => 0xDE, 8 => 0xDC, _ => 0 };
                    break;
                case 93:
                    r[Word1] = 0x42;
                    r[Word2] = 0xE0;
                    r[Word3] = plusWord;
                    break;
                default:
                    r[Word1] = type;
                    r[Word2] = plusWord;
                    break;
            }
            r[SaveBonus] = 0;
            r[Count] = 0;
            var (weight, count) = WeaponWeight(type);
            Put16(r, Weight, weight);
            if (count != 0)
                r[Count] = count;
            // value = plus × multiplier, 16-bit product of the sign-extended plus (0x2612E–0x26207)
            Put16(r, Value, (ushort)((sbyte)r[Plus] * ValueMultiplier(type)));
        }
        else if (type is 61 or 62)
        {
            Scroll(r, type, D);
        }
        else
        {
            magic = type switch
            {
                63 => 0x49,
                67 => 0x91,
                69 => D(10) switch { 1 => 0x29, <= 6 => 0x79, _ => 0x81 },
                78 => (D(3) + 1) switch { 2 => 0x41, 3 => 0x71, _ => 0 }, // 1 + 1d3: 4 gives nothing (0x31 unreachable)
                79 => 0x39,
                84 => 0x09,
                92 => 0x61,
                71 => D(11) switch { <= 5 => 0x11, <= 8 => 0x01, 9 => 0x19, 10 => 0x51, _ => 0 },
                70 => D(10) switch { <= 7 => 0x59, _ => 0x89 },
                _ => 0, // any other type: the record keeps only its type
            };
        }

        if (magic != 0)
            CopyFromTable(r, magic);
        return r;
    }

    /// <summary>The plus roll (0x25C5B): 1d20 1–14 → 1, 15–19 → 2, 20 → 3.</summary>
    public static byte PlusRoll(int d20) => d20 switch { >= 1 and <= 14 => 1, >= 15 and <= 19 => 2, _ => 3 };

    private static void ArmourWords(byte[] r, byte suffix, byte plusWord)
    {
        r[Word1] = r[Type];
        r[Word2] = suffix;
        r[Word3] = plusWord;
        r[Hidden] = 4; // only the plus word hidden
    }

    /// <summary>
    /// Weight (coins) and count of a plussed item (0x25F1E–0x26126). The program tests the types in this order; type
    /// 47 is in the 80 list before the 1 list, so only 93 gets weight 1. Unlisted types get 40 with count 10.
    /// </summary>
    public static (ushort Weight, byte Count) WeaponWeight(byte type) => type switch
    {
        1 or 13 or 14 or 35 => (75, 0),
        2 or 20 or 29 or 31 or 32 or 33 or 39 or 42 or 44 or 46 or 59 => (50, 0),
        3 or 24 or 40 => (125, 0),
        4 or 15 or 23 or 34 or 43 or 45 or 51 => (100, 0),
        5 or 12 or 17 or 19 or 50 => (150, 0),
        6 => (15, 0),
        7 => (30, 0),
        8 or 77 => (10, 0),
        9 => (25, 5),
        10 or 26 or 36 => (60, 0),
        11 or 16 or 25 or 27 or 41 or 47 => (80, 0),
        18 => (175, 0),
        21 => (20, 0),
        22 or 30 => (40, 0),
        37 => (35, 0),
        38 or 53 => (250, 0),
        52 => (200, 0),
        54 or 56 => (400, 0),
        55 => (300, 0),
        57 => (350, 0),
        58 => (450, 0),
        93 => (1, 0),
        _ => (40, 10),
    };

    /// <summary>Value per point of plus (0x2612E–0x26207); for bracers the plus is already 2·plus + 2.</summary>
    public static int ValueMultiplier(byte type) => type switch
    {
        59 => 2500,
        73 or 28 => 150,
        53 or 54 => 3000,
        55 or 56 => 3500,
        57 => 4000,
        58 => 5000,
        77 => 3000,
        _ => 2000,
    };

    /// <summary>Scrolls (0x26219–0x26375): 1d3 spells; per spell 1d3 for the level, then the id die.</summary>
    private static void Scroll(byte[] r, byte type, Func<int, int> d)
    {
        int n = d(3);
        r[Word1] = (byte)(type == 61 ? 0xD1 : 0xD0);
        r[Word2] = (byte)(0xD1 + n);
        r[Word3] = 0;
        r[Plus] = 1;
        Put16(r, Weight, 25);
        r[Count] = 0;
        Put16(r, Value, 0);
        for (int k = 1; k <= n; k++)
        {
            int level = d(3);
            int id = type == 61
                ? level switch { 1 => d(13) + 8, 2 => d(7) + 28, _ => d(11) + 44 }
                : level switch { 1 => d(8), 2 => d(7) + 21, _ => d(9) + 35 };
            r[Props + k - 1] = (byte)id;
            Put16(r, Value, (ushort)(Get16(r, Value) + level * 300));
        }
    }

    /// <summary>
    /// Copy entry <paramref name="magic"/> of the random item array (0x2649D–0x26561): words n..n+2 → bytes 2F..31,
    /// plus and save bonus 1 (2 for n = 0x61), weight word n+3, count 0, value word n+4, words n+5..n+7 → props.
    /// Indexes are n + field, so a stray n (the javelin's 0x90) reads across two entries.
    /// </summary>
    private void CopyFromTable(byte[] r, byte magic)
    {
        r[Plus] = 1;
        r[SaveBonus] = 1;
        if (magic == 0x61)
        {
            r[Plus] = 2;
            r[SaveBonus] = 2;
        }
        r[Count] = 0;
        if (_table is null)
        {
            Diagnostics.Add($"random item array missing: entry {magic} not copied");
            return;
        }
        for (int f = 0; f < 3; f++)
            r[Word3 + f] = (byte)_table[magic + f];
        Put16(r, Weight, _table[magic + 3]);
        Put16(r, Value, _table[magic + 4]);
        for (int f = 0; f < 3; f++)
            r[Props + f] = (byte)_table[magic + 5 + f];
    }

    private static void Put16(byte[] r, int o, ushort v) { r[o] = (byte)v; r[o + 1] = (byte)(v >> 8); }
    private static ushort Get16(byte[] r, int o) => (ushort)(r[o] | r[o + 1] << 8);
}
