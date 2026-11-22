using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// The Items page's own commands (GAME.OVR unit 0x2103A, read whole s51): Ready / unready 0x2250A and Drop (0x22398 in
// the Items loop 0x21F32), for every caller of the page (DS:49F3 0 party menu / hall, 1 shop / temple, 2 camp, 3 / 4
// command line, 5 fight, 6 loot). Halve 0x22827 and Join 0x228D7 are FightItemScreens.Halve / Join. Checked under
// emulation: tools/scripts/por_items_oracle.py runs the loop 0x21F32 whole (ItemsPageProgramTests).

/// <summary>0x2250A's refusals, in the code it keeps in [bp−2] (the last test that fails wins: hands, slot, class).</summary>
public enum ReadyRefusal { None = 0, WrongClass = 1, SlotTaken = 2, HandsFull = 3, Cursed = 4 }

/// <summary>
/// What Ready did: the refusal; for <see cref="ReadyRefusal.SlotTaken"/> the item in the slot the message names
/// (<see cref="InTheWay"/>); whether the refusal's message (START 0x1693, row 24) is shown.
/// </summary>
public readonly record struct ReadyResult(ReadyRefusal Refusal, byte[]? InTheWay, bool Message);

public static class ItemCommands
{
    /// <summary>Creature bytes: hands in use 0x100 (START 0x1758), class mask 0xB0, computer-controlled 0x10F.</summary>
    public const int HandsUsed = 0x100, ClassMask = 0xB0, AutoCombat = 0x10F;
    /// <summary>Item bytes: readied 0x34, cursed 0x36, handler kind 0x3E (above 0x7F: 0x2AEEA runs on ready / unready).</summary>
    public const int Readied = 0x34, Cursed = 0x36, Kind = 0x3E;

    /// <summary>
    /// 0x2250A on the current creature (DS:5D92), <paramref name="context"/> = DS:49F3:
    /// <list type="bullet">
    /// <item>Readied: cursed (0x36 ≠ 0) → "It's Cursed" (refusal 4); else 0x34 := 0 and, for kind 0x3E &gt; 0x7F, the
    /// handler 0x2AEEA(kind, creature, item, removed 1).</item>
    /// <item>Not readied: 0 unless hands 0x100 + the type's hands (ITEMS byte 1) &gt; 2 → 3; the type's slot (byte 0)
    /// 0–8 taken (record +0xCC + 4·slot) → 2, slot 9 (rings) with the second ring slot (+0xF4) taken → 2; type 0x49
    /// (arrows) with +0xF8 taken → 2 naming slot 11, type 0x1C (bolts) with +0xFC taken → 2 naming slot 12; the class
    /// mask (ITEMS byte 13 AND record 0xB0) 0 → 1. 0: 0x34 := 1 and the handler with removed 0.</item>
    /// <item>Messages (START 0x1693): 4 "It's Cursed", 1 "Wrong Class", 2 "already using " + the name of the item in
    /// the slot (START 0xFE2 on it; a ring names the first ring slot 9, though the second was tested), 3 "Your hands
    /// are full!" except in a fight (5) for a creature with 0x10F ≠ 0 (computer-controlled: no message).</item>
    /// </list>
    /// The equipment slots are the record's pointers START 0x1758 set (<see cref="FightRules.Equipment"/>, from the
    /// readied items, as the recalculation leaves them). [?] The program's 0x2AEEA gets the creature DS:5D92; the
    /// Client passes the item's owner, which the page always makes current.
    /// </summary>
    public static ReadyResult Ready(BaseItemTable items, EclCharacter c, byte[] item, byte context,
        Action<EclCharacter, byte[], bool>? handler)
    {
        bool magic = item[Kind] > 0x7F;
        if (item[Readied] != 0)
        {
            if (item[Cursed] != 0)
                return new(ReadyRefusal.Cursed, null, true);
            item[Readied] = 0;
            if (magic)
                handler?.Invoke(c, item, true);
            return new(ReadyRefusal.None, null, false);
        }
        var eq = FightRules.Equipment(c, items);
        int t = item[0x2E];
        var refusal = ReadyRefusal.None;
        if (c[HandsUsed] + items[t, 1] > 2)
            refusal = ReadyRefusal.HandsFull;
        int slot = items[t, BaseItemTable.Slot];
        if (slot <= 8)
        {
            if (eq[slot] is not null)
                refusal = ReadyRefusal.SlotTaken;
        }
        else if (slot == 9 && eq[10] is not null)
            refusal = ReadyRefusal.SlotTaken;
        if (t == 0x49 && eq[FightRules.SlotArrows] is not null)
        {
            refusal = ReadyRefusal.SlotTaken;
            slot = FightRules.SlotArrows;
        }
        if (t == 0x1C && eq[FightRules.SlotBolts] is not null)
        {
            refusal = ReadyRefusal.SlotTaken;
            slot = FightRules.SlotBolts;
        }
        if ((items[t, 13] & c[ClassMask]) == 0)
            refusal = ReadyRefusal.WrongClass;
        switch (refusal)
        {
            case ReadyRefusal.None:
                item[Readied] = 1;
                if (magic)
                    handler?.Invoke(c, item, false);
                return new(ReadyRefusal.None, null, false);
            case ReadyRefusal.SlotTaken:
                return new(refusal, slot < eq.Length ? eq[slot] : null, true);
            case ReadyRefusal.HandsFull:
                return new(refusal, null, !(context == FightItemScreens.InFight && c[AutoCombat] != 0));
            default:
                return new(refusal, null, true);
        }
    }

    /// <summary>
    /// Drop (0x22398) after 0x21DAA let the item go: "Your " + the item's name + "will be gone forever" in the text box
    /// (1, 21)–(38, 22), colour 14 (START 0x77F8, cleared first), and the yes/no "Drop It? " (0x2D9A0, colours 13, 10,
    /// 15); Y: START 0x20A9 unlinks and frees the item. Every caller alike: the item is gone (no pool, treasure or shop
    /// stock gains it). Then the box is blanked (START 0x4047). Returns whether it went.
    /// </summary>
    public static bool Drop(EclCharacter c, byte[] item, bool yes) => yes && c.Items.Remove(item);
}
