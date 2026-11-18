using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The data-segment tables the training hall reads (PoR DOS v1.3 offsets, read by GoldBox.Data's
/// <c>PorTrainingTables</c> from the player's program; nothing is copied into our code). The routines index them with
/// their own arithmetic, past the table ends where the program does, so they stay offsets into the data segment.
/// </summary>
public sealed class TrainingTables
{
    /// <summary>The program's data segment as loaded (bytes past it read as 0).</summary>
    public required byte[] Data { get; init; }
    /// <summary>The level-stats routine's tables (0x2AA87, run after the levels rise).</summary>
    public required LevelStatsTables Levels { get; init; }
    /// <summary>DS:409B: experience by class × 0x38 + level × 4 (signed dwords).</summary>
    public int Experience { get; init; } = 0x409B;
    /// <summary>DS:05DE: a class's hall bits (byte per class 0..7), summed per character and ANDed with VM $6DA8.</summary>
    public int ClassBits { get; init; } = 0x05DE;
    /// <summary>DS:08B6 / DS:08BE: hit dice per class (count, sides).</summary>
    public int HitDiceCount { get; init; } = 0x08B6;
    public int HitDiceSides { get; init; } = 0x08BE;
    /// <summary>DS:408E: the Constitution hit-point adjustment by Constitution (signed byte).</summary>
    public int ConBonus { get; init; } = 0x408E;
    /// <summary>DS:0D24: coin values in copper, signed dwords (copper, silver, electrum, gold, platinum; the payment
    /// reads on past them).</summary>
    public int CoinValues { get; init; } = 0x0D24;

    public byte At(int offset) => offset >= 0 && offset < Data.Length ? Data[offset] : (byte)0;

    public int Dword(int offset) => At(offset) | At(offset + 1) << 8 | At(offset + 2) << 16 | At(offset + 3) << 24;

    /// <summary>The experience cell for <paramref name="cls"/> and <paramref name="level"/> as the program reads it.</summary>
    public int Threshold(int cls, int level) => Dword(Experience + cls * 0x38 + level * 4);
}

/// <summary>Why the hall's Train Character stops before the question (the message's routine offset in the comment).</summary>
public enum TrainingRefusal
{
    None,
    /// <summary>Status (0x10C) not 0 (GAME.OVR unit string 0x1C26D).</summary>
    NotConscious,
    /// <summary>Less than 1000 gold (0x1C28C).</summary>
    NoMoney,
    /// <summary>None of the character's classes is trained in this hall (0x1C2A4).</summary>
    WrongClass,
    /// <summary>No class the hall trains can rise (0x1C2C3).</summary>
    NotEnoughExperience,
}

/// <summary>What Train Character found before its question: the refusal, or the classes it offers (hall bits).</summary>
public readonly record struct TrainingOffer(TrainingRefusal Refusal, byte Train)
{
    public bool Refused => Refusal != TrainingRefusal.None;
}

/// <summary>
/// Pool of Radiance DOS v1.3: the training hall, ECL PROGRAM 0 (GAME.OVR 0x19AE0, the same menu as the start-up
/// screen) and its Train Character (0x1C32D). Program read (s19), checked under emulation
/// (<c>tools/scripts/por_train_oracle.py</c>); docs/re/training.md. Mirrors the program's byte arithmetic.
/// </summary>
public static class TrainingHall
{
    public const int Status = 0x10C, Levels = 0x96, Race = 0x2E, Strength = 0x10, Con = 0x14, ClassByte = 0x2F,
        Experience = 0xAC, DrainLevels = 0x74, DrainHp = 0x75, MaxHp = 0x32, Hp = 0x11B, RolledHp = 0xB1,
        MageLevel = 0x9B, Known = 0x32, Slots = 0xB1;

    /// <summary>The price of a level (immediate in 0x1C375 and 0x1C83E), in gold.</summary>
    public const int Price = 1000;

    /// <summary>VM $6DA8: the hall's class mask (the town script writes it before PROGRAM 0; read as the $6B00 area's
    /// word +0x550).</summary>
    public const ushort HallMaskCell = 0x6DA8;

    /// <summary>The menu's eleven entries in the program's order (DS:0619 + 42·i: Pascal string[40], flag at +41).</summary>
    public enum Entry { Create, Drop, Modify, Train, View, Add, Remove, Load, Save, Begin, Exit }

    /// <summary>The DS address of entry <paramref name="e"/>'s string (0x619 + 42·i) and of its flag (+0x29).</summary>
    public static int EntryString(Entry e) => 0x619 + 42 * (int)e;

    /// <summary>
    /// 0x19B2D: the entries the menu shows. With a current character: Drop, Modify, View, Remove, Save and Begin on,
    /// Train when the hall mask (unsigned word) is above 0 or the cheat flag DS:470C is set, Load off. Without one:
    /// those off and Load on. Create, Add and Exit keep their data-segment defaults (all on in v1.3: <paramref
    /// name="defaults"/>, the flag bytes at DS:0642 / 0714 / 07E6).
    /// </summary>
    public static bool[] MenuFlags(bool hasCurrent, ushort hallMask, bool cheat, bool[]? defaults = null)
    {
        var f = new bool[11];
        if (defaults is not null)
            Array.Copy(defaults, f, Math.Min(defaults.Length, f.Length));
        else
            f[(int)Entry.Create] = f[(int)Entry.Add] = f[(int)Entry.Exit] = true;
        f[(int)Entry.Drop] = f[(int)Entry.Modify] = f[(int)Entry.View] = f[(int)Entry.Remove] =
            f[(int)Entry.Save] = f[(int)Entry.Begin] = hasCurrent;
        f[(int)Entry.Train] = hasCurrent && (hallMask > 0 || cheat);
        f[(int)Entry.Load] = !hasCurrent;
        return f;
    }

    private static sbyte Level(EclCharacter c, int cls) => (sbyte)c[Levels + cls];

    private static int Xp(EclCharacter c) =>
        c[Experience] | c[Experience + 1] << 8 | c[Experience + 2] << 16 | c[Experience + 3] << 24;

    /// <summary>
    /// 0x1C32D up to its question. Refused when the status is not 0, or the gold total is below 1000 (unless
    /// <paramref name="cheat"/>). Then over the classes with a level L &gt; 0 (signed): every class's bits are added to
    /// "all" (byte sum); a class the race limit stops (the inline copy of START 0x3AF7, <see
    /// cref="LevelStats.RaceLimitStops"/>) is skipped; a class whose next threshold T(L+1) is above the experience
    /// (signed) is skipped unless cheat, and one whose T(L+1) is not above 0 (past the class's last level) too; the
    /// others add their bits to "ok", and when T(L+2) &gt; 0, experience ≥ T(L+2) and T(L+2) above the cap so far, the cap
    /// becomes T(L+2) − 1. A cap above 0 is written into the experience <b>now</b>, before any further refusal or the
    /// question. Without cheat: (all AND mask) = 0 → wrong class; (ok AND mask) = 0 → not enough experience; the
    /// classes trained are ok AND mask (ok alone with cheat). Masks are the low byte of $6DA8.
    /// </summary>
    public static TrainingOffer Check(EclCharacter c, ushort hallMask, bool cheat, TrainingTables t)
    {
        if (c[Status] != 0 && !cheat)
            return new(TrainingRefusal.NotConscious, 0);
        if (Money.CharacterGold(c) < Price && !cheat)
            return new(TrainingRefusal.NoMoney, 0);
        byte all = 0, ok = 0;
        int cap = 0, xp = Xp(c);
        for (int cls = 0; cls <= 7; cls++)
        {
            int l = Level(c, cls);
            if (l <= 0)
                continue;
            byte bits = t.At(t.ClassBits + cls);
            all = (byte)(all + bits);
            if (LevelStats.RaceLimitStops(c, cls))
                continue;
            int t1 = t.Threshold(cls, l + 1);
            if (t1 > xp && !cheat)
                continue;
            if (t1 <= 0)
                continue;
            ok = (byte)(ok + bits);
            int t2 = t.Threshold(cls, l + 2);
            if (t2 > 0 && xp >= t2 && t2 > cap)
                cap = t2 - 1;
        }
        if (cap > 0)
        {
            c[Experience] = (byte)cap;
            c[Experience + 1] = (byte)(cap >> 8);
            c[Experience + 2] = (byte)(cap >> 16);
            c[Experience + 3] = (byte)(cap >> 24);
        }
        byte mask = (byte)hallMask;
        if (!cheat)
        {
            if ((all & mask) == 0)
                return new(TrainingRefusal.WrongClass, 0);
            if ((ok & mask) == 0)
                return new(TrainingRefusal.NotEnoughExperience, 0);
            return new(TrainingRefusal.None, (byte)(ok & mask));
        }
        return new(TrainingRefusal.None, ok);
    }

    /// <summary>
    /// The classes the "will become" list names and the level-up raises: every class with a level &gt; 0 whose hall bits
    /// meet <paramref name="train"/> (the bits only; paladins and rangers share the fighter's and cleric's bits), with
    /// the level it becomes.
    /// </summary>
    public static List<(int Class, int NewLevel)> Raised(EclCharacter c, byte train, TrainingTables t)
    {
        var list = new List<(int, int)>();
        for (int cls = 0; cls <= 7; cls++)
            if (Level(c, cls) > 0 && (t.At(t.ClassBits + cls) & train) != 0)
                list.Add((cls, Level(c, cls) + 1));
        return list;
    }

    /// <summary>
    /// 0x1C823 onwards (after Yes): the price paid (0x1DC6C, not with cheat); n = classes with a level &gt; 0; each raised
    /// class (<see cref="Raised"/>) + 1 and, with drained levels (0x74 &gt; 0), 0x75 − 0x75 div 0x74 and 0x74 − 1; the
    /// level stats (0x2AA87); a magic-user level that rose (0x9B, signed) learns one spell (<paramref name="learn"/>
    /// picks from <see cref="Learnable"/>; with an empty list the program writes byte 0x32 + 0, the maximum hit points,
    /// := 1); then the hit points: rolls = <see cref="HitDice"/>; rolled HP 0xB1 + max(1, rolls div n); maximum HP 0x32 +
    /// max(1, (rolls + <see cref="ConBonus"/>) div n), current HP keeping the same distance below it (bytes).
    /// <paramref name="learn"/> gets the non-empty list and returns a spell from it (the menu repeats until one is
    /// chosen). Throws <see cref="ProgramHaltException"/> where the payment divides by zero (see <see cref="Pay"/>).
    /// </summary>
    public static void Train(EclCharacter c, byte train, bool cheat, TrainingTables t, Func<ushort, ushort> random,
        Func<IReadOnlyList<int>, int> learn)
    {
        if (!cheat)
            Pay(c, Price, t);
        int n = 0;
        sbyte mageBefore = (sbyte)c[MageLevel];
        for (int cls = 0; cls <= 7; cls++)
        {
            if (Level(c, cls) <= 0)
                continue;
            n++;
            if ((t.At(t.ClassBits + cls) & train) == 0)
                continue;
            c[Levels + cls]++;
            if (c[DrainLevels] > 0)
            {
                c[DrainHp] = (byte)(c[DrainHp] - c[DrainHp] / c[DrainLevels]);
                c[DrainLevels]--;
            }
        }
        LevelStats.Apply(c, t.Levels);
        if ((sbyte)c[MageLevel] > mageBefore)
        {
            var list = Learnable(c, t.Levels);
            int spell = list.Count == 0 ? 0 : learn(list);
            c[Known + spell] = 1;
        }
        byte rolls = HitDice(c, train, t, random);
        int per = rolls / n;
        if (per == 0)
            per = 1;
        c[RolledHp] = (byte)(c[RolledHp] + per);
        int per2 = (rolls + ConBonus(c, t)) / n;
        if (per2 < 1)
            per2 = 1;
        byte below = (byte)(c[MaxHp] - c[Hp]);
        c[MaxHp] = (byte)(c[MaxHp] + per2);
        c[Hp] = (byte)(c[MaxHp] - below);
    }

    /// <summary>
    /// 0x1DBB4 (character, classes): for each class with a level &gt; 0 whose bits meet <paramref name="train"/>, the dice
    /// DS:08B6[class] × d DS:08BE[class] (0x2BCCD); at level 1 a roll below 2·sides div 3 becomes 2·sides div 3; the
    /// byte sum.
    /// </summary>
    public static byte HitDice(EclCharacter c, byte train, TrainingTables t, Func<ushort, ushort> random)
    {
        byte sum = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            if (Level(c, cls) <= 0 || (t.At(t.ClassBits + cls) & train) == 0)
                continue;
            byte sides = t.At(t.HitDiceSides + cls);
            byte roll = FightRules.Dice(random, t.At(t.HitDiceCount + cls), sides);
            if (c[Levels + cls] == 1 && roll < sides * 2 / 3)
                roll = (byte)(sides * 2 / 3);
            sum = (byte)(sum + roll);
        }
        return sum;
    }

    /// <summary>
    /// 0x1D8B0: per class with a level &gt; 0, DS:408E[Constitution] (signed), and one more each for Constitution above
    /// 16 and above 17 when the class byte 0x2F is 2; a signed byte sum.
    /// </summary>
    public static sbyte ConBonus(EclCharacter c, TrainingTables t)
    {
        byte sum = 0;
        for (int cls = 0; cls <= 7; cls++)
        {
            if (Level(c, cls) <= 0)
                continue;
            sum = (byte)(sum + t.At(t.ConBonus + c[Con]));
            if (c[Con] > 16 && c[ClassByte] == 2)
                sum++;
            if (c[Con] > 17 && c[ClassByte] == 2)
                sum++;
        }
        return (sbyte)sum;
    }

    /// <summary>
    /// The spell list's mode 4 (0x275B4, "Learn"): spells 1..56 whose class (table byte 0) and level (byte 1, both
    /// signed) have a slot byte (0xB1 + 3·class + level) above 0 and that are not known (0x32 + spell = 0), in id order.
    /// </summary>
    public static List<int> Learnable(EclCharacter c, LevelStatsTables t)
    {
        var list = new List<int>();
        for (int s = 1; s <= 0x38; s++)
        {
            int e = t.SpellTable + 16 * s;
            int at = Slots + 3 * (sbyte)t.At(e) + (sbyte)t.At(e + 1);
            if (at >= 0 && at < c.Record.Length && c[at] > 0 && c[Known + s] == 0)
                list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// 0x1DC6C (character, gold): the price in copper C = gold × 200. From coin kind k = 0 up while C &gt; 0: n = (C div
    /// value[k] + 1) as a word, at most the coins held; C − n·value; coins − n. Then, when C &lt; 0, change from k = 4
    /// down while C &gt; 0: n = C div value[k] (a word); C − n·value; coins + n. Values are the signed dwords at DS:0D24 +
    /// 4k and coins the words at record 0x88 + 2k, read past the five coin kinds as the program does (gems, jewellery,
    /// then the record's following words; values from the bytes after the table). A value of 0 is the long division's
    /// run-time error 200: <see cref="ProgramHaltException"/>.
    /// </summary>
    public static void Pay(EclCharacter c, int gold, TrainingTables t)
    {
        int cost = unchecked(gold * 200);
        sbyte k = 0;
        while (cost > 0)
        {
            int v = Value(t, k);
            ushort n = unchecked((ushort)(cost / v + 1));
            int at = Coin(c, k);
            ushort held = c.Word(at);
            if (held < n)
                n = held;
            cost = unchecked(cost - n * v);
            c.SetWord(at, (ushort)(held - n));
            k++;
        }
        if (cost >= 0)
            return;
        cost = unchecked(-cost);
        k = 4;
        while (cost > 0)
        {
            int v = Value(t, k);
            ushort n = unchecked((ushort)(cost / v));
            cost = unchecked(cost - n * v);
            int at = Coin(c, k);
            c.SetWord(at, unchecked((ushort)(c.Word(at) + n)));
            k--;
        }
    }

    private static int Value(TrainingTables t, sbyte k)
    {
        int v = t.Dword(t.CoinValues + 4 * k);
        if (v == 0)
            throw new ProgramHaltException(200, "training payment: long division by zero (START 0xBA5D)");
        return v;
    }

    private static int Coin(EclCharacter c, sbyte k)
    {
        int at = Money.Coins + 2 * k;
        if (at < 0 || at + 1 >= c.Record.Length)
            throw new InvalidOperationException($"training payment reads past the record (coin {k}) [?]");
        return at;
    }
}
