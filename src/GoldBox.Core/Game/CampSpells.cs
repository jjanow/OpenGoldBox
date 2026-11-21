using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>Why a character cannot cast, memorise or scribe in camp (GAME.OVR 0x17B9D).</summary>
public enum CampRefusal
{
    None,
    /// <summary>Cast only: VM cell $49E5 (anti-magic area) is non-zero.</summary>
    NoMagicHere,
    /// <summary>Status 1 (animated) or not in play (record 0x10D = 0).</summary>
    NoCondition,
}

/// <summary>What the camp's spell activity check is for (0x17B9D's argument).</summary>
public enum CampActivity { Cast = 1, Memorise = 2, Scribe = 3 }

/// <summary>
/// One row of a camp spell list (GAME.OVR 0x274xx list builder): the spell id (pending bit removed), whether the
/// slot or scroll byte was pending, the scroll it is on (scroll lists), and the spell level when a level heading
/// comes before it (the level differs from the previous row's; the first row always has one).
/// </summary>
public sealed record CampSpellRow(int Spell, bool Pending, byte[]? Scroll, int? HeadingLevel);

/// <summary>One line of the camp's Display list (0x187ED) for a character's effect.</summary>
/// <param name="Spell">The spell 1..56 whose table effect (+10) is the effect id, else 0.</param>
/// <param name="EffectId">The effect id.</param>
/// <param name="Funky">A spell-effect id no spell 1..56 produces (the program's fallback text and the number).</param>
public readonly record struct CampEffectLine(int Spell, byte EffectId, bool Funky);

/// <summary>
/// The camp's spell rules (GAME.OVR 0x17831–0x187ED, the time unit's spell preparation 0x246F1–0x2497E): pending
/// memorisation and scribing, the preparation time, the slots, the spell lists, the finishing of pending spells while
/// resting, and the Display list. Record offsets: memorised slots 0x17..0x2B (bit 7 = pending), preparation hours
/// 0x2C, spell book 0x32 + id, slots 0xB2 + 3·class + level − 1. Scroll bytes: item 0x3C..0x3E (bit 7 = to scribe).
/// </summary>
public sealed class CampSpells(SpellTable table, Func<byte[], bool> readable)
{
    public const int Memorised = 0x17, Slots = 21, PrepHours = 0x2C, Known = 0x32, SlotBase = 0xB1,
        Status = 0x10C, InPlay = 0x10D, ClericLevel = 0x96, Hidden = 0x35, ScrollBytes = 0x3C, ScrollWord = 0x30;

    /// <summary>VM cell the cast check reads (zone $4900 + 0x1CA bytes = $49E5): non-zero = no spells here.</summary>
    public const ushort AntiMagicCell = 0x49E5;

    public SpellTable Table { get; } = table;

    /// <summary>Base item slot 11..13 (0x2A255; 0x2463D's scan): the item holds scroll spells.</summary>
    public Func<byte[], bool> Readable { get; } = readable;

    private int Level(int spell) => Table[spell & 0x7F].Level;

    // ---- Checks (0x17B9D) -----------------------------------------------------------------------------------------

    /// <summary>
    /// 0x17B9D: casting is refused when $49E5 is non-zero; any activity when the character is animated (status 1) or
    /// not in play (0x10D = 0).
    /// </summary>
    public static CampRefusal Check(CampActivity a, EclCharacter c, EclMemory memory)
    {
        if (a == CampActivity.Cast && memory.Read(AntiMagicCell) > 0)
            return CampRefusal.NoMagicHere;
        if (c[Status] == 1 || c[InPlay] == 0)
            return CampRefusal.NoCondition;
        return CampRefusal.None;
    }

    // ---- Preparation time (0x17831) and resets (0x1798A, 0x179CD, 0x17A45) -----------------------------------------

    /// <summary>
    /// 0x17831: minutes this character needs: base hours (0; 4 when any memorising or scribing is pending; 6 when a
    /// pending spell's level is above 2) × 60 + 15 × the sum of the pending levels (byte sums; memorised slots with
    /// bit 7, then bytes of readable items above 0x7F). The base hours go to record 0x2C.
    /// </summary>
    public ushort PrepMinutes(EclCharacter c)
    {
        byte maxMem = 0, maxScr = 0, sumMem = 0, sumScr = 0;
        for (int i = 0; i < Slots; i++)
            if (c[Memorised + i] > 0x7F)
            {
                byte l = (byte)Level(c[Memorised + i]);
                if (l > maxMem) maxMem = l;
                sumMem += l;
            }
        foreach (var it in c.Items)
            if (Readable(it))
                for (int k = 0; k < 3; k++)
                    if (it[ScrollBytes + k] > 0x7F)
                    {
                        byte l = (byte)Level(it[ScrollBytes + k]);
                        if (l > maxScr) maxScr = l;
                        sumScr += l;
                    }
        byte hours = 0;
        if (sumMem > 0 || sumScr > 0)
            hours = 4;
        if (maxMem > 2 || maxScr > 2)
            hours = 6;
        c[PrepHours] = hours;
        return (ushort)(hours * 60 + sumMem * 15 + sumScr * 15);
    }

    /// <summary>0x1798A: pending memorised slots (bit 7) become empty; record 0x2C := 0.</summary>
    public static void ResetMemorise(EclCharacter c)
    {
        for (int i = 0; i < Slots; i++)
            if (c[Memorised + i] > 0x7F)
                c[Memorised + i] = 0;
        c[PrepHours] = 0;
    }

    /// <summary>0x179CD: bit 7 cleared on every scroll byte of the character's readable items.</summary>
    public void ResetScribe(EclCharacter c)
    {
        foreach (var it in c.Items)
            if (Readable(it))
                for (int k = 0; k < 3; k++)
                    it[ScrollBytes + k] &= 0x7F;
    }

    /// <summary>0x17A45: both resets for every party member (camp entry and exit, 0x196A0).</summary>
    public void ResetParty(IEnumerable<EclCharacter> party)
    {
        foreach (var c in party)
        {
            ResetMemorise(c);
            ResetScribe(c);
        }
    }

    // ---- Slots (0x17A8D) and memorising (0x1813B, 0x18055) --------------------------------------------------------

    /// <summary>The slot byte for a class and level (0xB1 + 3·class + level; unchecked like the program, so a level
    /// above 3 reads other record bytes).</summary>
    public static byte SlotsFor(EclCharacter c, int cls, int level)
    {
        int off = SlotBase + 3 * cls + level;
        return off is >= 0 and < EclCharacter.RecordSize ? c[off] : (byte)0;
    }

    /// <summary>
    /// 0x17A8D: the slots of a class and level less the memorised or pending spells of that level and class (a byte:
    /// it wraps below 0 after a level loss).
    /// </summary>
    public byte SlotsLeft(EclCharacter c, int level, int cls)
    {
        byte n = 0;
        for (int i = 0; i < Slots; i++)
        {
            byte s = c[Memorised + i];
            if (s > 0 && Table[s & 0x7F].Level == level && Table[s & 0x7F].CastClass == cls)
                n++;
        }
        return unchecked((byte)(SlotsFor(c, cls, level) - n));
    }

    /// <summary>
    /// 0x17DD5's test: the character has any slot (cleric or magic-user, levels 1..3), whatever is memorised. The
    /// screen shows <see cref="SlotsLeft"/> per class and level (blank where the slot byte is 0).
    /// </summary>
    public static bool HasSlots(EclCharacter c)
    {
        for (int cls = 0; cls <= 1; cls++)
            for (int l = 1; l <= 3; l++)
                if (SlotsFor(c, cls, l) != 0)
                    return true;
        return false;
    }

    /// <summary>
    /// 0x1813B's choice: when the spell's class and level have a slot left, the spell goes pending (+0x80) into the
    /// first empty byte from 0x17 on (the walk is not bounded by the 21 slots), then the slots are sorted. Returns
    /// whether it was marked.
    /// </summary>
    public bool MarkMemorise(EclCharacter c, int spell)
    {
        var t = Table[spell];
        if (SlotsLeft(c, t.Level, t.CastClass) == 0)
            return false;
        int off = Memorised;
        while (off < EclCharacter.RecordSize && c[off] != 0)
            off++;
        if (off >= EclCharacter.RecordSize)
            return false;
        c[off] = (byte)(spell + 0x80);
        Sort(c);
        return true;
    }

    /// <summary>0x18055: an exchange sort of the 21 slots by id (bit 7 ignored, the bit kept with its spell); empty first.</summary>
    public static void Sort(EclCharacter c)
    {
        for (int i = 0; i < Slots - 1; i++)
            for (int j = i; j < Slots; j++)
                if ((c[Memorised + i] & 0x7F) > (c[Memorised + j] & 0x7F))
                    (c[Memorised + i], c[Memorised + j]) = (c[Memorised + j], c[Memorised + i]);
    }

    // ---- Scribing (0x1838C) ---------------------------------------------------------------------------------------

    public enum ScribeResult { Marked, AlreadyKnown, AlreadyScribing, NoSlot }

    /// <summary>
    /// 0x1838C's choice of a scroll spell: known (0x32 + id ≠ 0) → refused; a readable item with that spell pending →
    /// refused; no slot byte for the spell's class and level → refused. Else the first byte equal to the spell (not
    /// pending) on any item, readable or not, gets bit 7.
    /// </summary>
    public ScribeResult MarkScribe(EclCharacter c, int spell)
    {
        if (c[Known + spell] != 0)
            return ScribeResult.AlreadyKnown;
        foreach (var it in c.Items)
            if (Readable(it))
                for (int k = 0; k < 3; k++)
                    if (it[ScrollBytes + k] > 0x7F && (it[ScrollBytes + k] & 0x7F) == spell)
                        return ScribeResult.AlreadyScribing;
        var t = Table[spell];
        if (SlotsFor(c, t.CastClass, t.Level) == 0)
            return ScribeResult.NoSlot;
        foreach (var it in c.Items)
            for (int k = 0; k < 3; k++)
                if (it[ScrollBytes + k] == spell)
                {
                    it[ScrollBytes + k] |= 0x80;
                    return ScribeResult.Marked;
                }
        return ScribeResult.Marked;
    }

    // ---- Spell lists (the list builder behind 0x2390x; menu types 0, 1, 3, 5, 6) ---------------------------------

    public enum ListKind { InMemory = 0, SpellBook = 1, OnScrolls = 3, ToMemorise = 5, ToScribe = 6 }

    /// <summary>
    /// The camp's spell lists: 0 memorised (byte 1..0x7F), 5 pending memorised (above 0x7F), 1 the spell book (ids
    /// 1..55 known: 56 is never listed), 3 / 6 the spells of the character's readable items (6: only those above 0x80).
    /// For the scroll lists each scroll identifies itself first when the reader has effect 0x10 (Read Magic) or a
    /// cleric level with a cleric scroll (slot 12); a scroll still hidden (0x35 ≠ 0) adds nothing.
    /// </summary>
    public List<CampSpellRow> List(EclCharacter c, ListKind kind, Func<byte[], int>? itemSlot = null)
    {
        var rows = new List<(int Spell, bool Pending, byte[]? Scroll)>();
        switch (kind)
        {
            case ListKind.InMemory:
                for (int i = 0; i < Slots; i++)
                    if (c[Memorised + i] is > 0 and < 0x80)
                        rows.Add((c[Memorised + i], false, null));
                break;
            case ListKind.ToMemorise:
                for (int i = 0; i < Slots; i++)
                    if (c[Memorised + i] > 0x7F)
                        rows.Add((c[Memorised + i] & 0x7F, true, null));
                break;
            case ListKind.SpellBook:
                for (int s = 1; s <= 55; s++)
                    if (c[Known + s] != 0)
                        rows.Add((s, false, null));
                break;
            case ListKind.OnScrolls:
            case ListKind.ToScribe:
                bool forScribe = kind == ListKind.ToScribe;
                foreach (var it in c.Items)
                {
                    if (!Readable(it))
                        continue;
                    if (c.HasEffect(0x10) || (c[ClericLevel] > 0 && itemSlot?.Invoke(it) == 12))
                        it[Hidden] = 0;
                    if (it[Hidden] != 0)
                        continue;
                    for (int k = 0; k < 3; k++)
                    {
                        byte b = it[ScrollBytes + k];
                        if (forScribe ? b > 0x80 : b > 0)
                            rows.Add((b & 0x7F, b > 0x7F, it));
                    }
                }
                break;
        }
        return WithHeadings(rows, Level);
    }

    /// <summary>
    /// List kind 2, a scroll's spells (0x275B4 → 0x27466(0), read s38): the scroll used (DS:6B80) identifies itself
    /// (0x35 := 0) when the reader has effect 0x10 or a cleric level with a cleric scroll (<paramref name="slot"/> 12); a
    /// scroll still hidden lists nothing; else each non-zero byte 0x3C–0x3E in order, through the row routine 0x272A5
    /// (level and name of the byte AND 0x7F, a byte above 0x7F marked pending), with the level headings.
    /// </summary>
    public static List<CampSpellRow> ScrollList(SpellTable table, EclCharacter c, byte[] scroll, int slot)
    {
        if (c.HasEffect(0x10) || (c[ClericLevel] > 0 && slot == 12))
            scroll[Hidden] = 0;
        var rows = new List<(int Spell, bool Pending, byte[]? Scroll)>();
        if (scroll[Hidden] == 0)
            for (int k = 0; k < 3; k++)
                if (scroll[ScrollBytes + k] is var b and > 0)
                    rows.Add((b & 0x7F, b > 0x7F, scroll));
        return WithHeadings(rows, s => table[s & 0x7F].Level);
    }

    private static List<CampSpellRow> WithHeadings(List<(int Spell, bool Pending, byte[]? Scroll)> rows, Func<int, int> level)
    {
        var result = new List<CampSpellRow>();
        int last = 0;
        foreach (var (spell, pending, scroll) in rows)
        {
            int l = level(spell);
            result.Add(new CampSpellRow(spell, pending, scroll, l != last ? l : null));
            last = l;
        }
        return result;
    }

    // ---- Finishing pending spells while resting (0x246F1, 0x247B3) -------------------------------------------------

    /// <summary>
    /// 0x246F1: the memorised slots in order. A pending slot (above 0x7F): with <paramref name="done"/> clear it is
    /// finished (bit 7 removed, <paramref name="finished"/> called) and done is set; with done set the scan stops and
    /// returns that spell's level. Returns 0 when no further pending slot exists.
    /// </summary>
    public int FinishMemorise(EclCharacter c, ref bool done, Action<EclCharacter, int> finished)
    {
        for (int i = 0; i < Slots; i++)
        {
            byte s = c[Memorised + i];
            if (s <= 0x7F)
                continue;
            if (done)
                return Level(s);
            c[Memorised + i] = (byte)(s - 0x80);
            finished(c, c[Memorised + i]);
            done = true;
        }
        return 0;
    }

    /// <summary>
    /// 0x247B3: the scribing counterpart over the readable items' scroll bytes above 0x80: finishing one marks the
    /// spell known (0x32 + id := 1), takes it off the scroll (<see cref="RemoveFromScroll"/>, which may drop the item;
    /// the scan goes on with the item that followed it) and calls <paramref name="finished"/>.
    /// </summary>
    public int FinishScribe(EclCharacter c, ref bool done, Action<EclCharacter, int> finished)
    {
        var items = c.Items.ToList();
        foreach (var it in items)
        {
            if (!Readable(it))
                continue;
            for (int k = 0; k < 3; k++)
            {
                byte b = it[ScrollBytes + k];
                if (b <= 0x80)
                    continue;
                if (done)
                    return Level(b);
                int sp = b & 0x7F;
                c[Known + sp] = 1;
                RemoveFromScroll(c, it, sp);
                finished(c, sp);
                done = true;
            }
        }
        return 0;
    }

    /// <summary>
    /// The scroll-spell removal the scribing calls (far 0096:0043): the last byte of the three whose id (bit 7 off)
    /// is the spell becomes 0 and the item's name word 0x30 (0xD1 + spell count) goes down by one; below 0xD2 the item
    /// leaves the character. [?] The item removal routine (START) is not read beyond unlinking the item.
    /// </summary>
    public static void RemoveFromScroll(EclCharacter c, byte[] item, int spell)
    {
        int found = -1;
        for (int k = 0; k < 3; k++)
            if ((item[ScrollBytes + k] & 0x7F) == (spell & 0x7F))
                found = k;
        if (found < 0)
            return;
        item[ScrollBytes + found] = 0;
        item[ScrollWord]--;
        if (item[ScrollWord] < 0xD2)
            c.Items.Remove(item);
    }

    // ---- Display (0x187ED) ----------------------------------------------------------------------------------------

    /// <summary>Effect ids 0x187ED names by the spell that produces them (spell table field 10, ids 1..56).</summary>
    public static readonly byte[] SpellEffects =
        [1, 2, 5, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 20, 21, 22, 23, 24, 25, 28, 29, 32, 33, 34, 36, 37, 38, 39, 41,
         42, 45, 46, 49, 51, 52, 53];

    /// <summary>Effect ids 0x187ED names with a fixed text of its own (code-segment strings, in this order).</summary>
    public static readonly byte[] LabelledEffects = [4, 7, 27, 31, 35, 44, 50, 54, 55, 59, 61, 71, 72, 89];

    /// <summary>
    /// 0x187ED: a character's effects in list order: ids of <see cref="SpellEffects"/> named by the first spell 1..56
    /// whose effect byte equals the id (the fallback text and the id when none), ids of <see cref="LabelledEffects"/> by their text;
    /// every other effect is left out.
    /// </summary>
    public IReadOnlyList<CampEffectLine> Display(EclCharacter c)
    {
        var lines = new List<CampEffectLine>();
        foreach (var e in c.Effects)
        {
            byte id = e[0];
            if (SpellEffects.Contains(id))
            {
                int spell = 0;
                for (int s = 1; s <= 56 && spell == 0; s++)
                    if (Table[s].Effect == id)
                        spell = s;
                lines.Add(new CampEffectLine(spell, id, spell == 0));
            }
            else if (LabelledEffects.Contains(id))
                lines.Add(new CampEffectLine(0, id, false));
        }
        return lines;
    }
}
