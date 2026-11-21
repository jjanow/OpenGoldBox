using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// The character View screen (GAME.OVR 0x21AED) and its Items screen (0x21F32, also the combat menu's Use), the Core
// side: which entries they show, what each one does to the records (docs/re/combat-menu.md "View / Use"). Drawing,
// the item names and the list scrolling are the Client's.

/// <summary>The View menu's entries (0x21AED). Rename is the Amiga program's own (hunk 10 +0x542C, see
/// <see cref="FightItemScreens.WithAmigaRename"/>); DOS has no such entry.</summary>
public enum ViewEntry { Items, Spells, Trade, Drop, Rename, Exit }

/// <summary>The Items menu's entries (0x21F32).</summary>
public enum ItemsEntry { Ready, Use, Trade, Drop, Halve, Join, Sell, Id, Exit }

/// <summary>Messages of the item screens (paraphrased names; the text is the Client's).</summary>
public enum ItemNotice
{
    /// <summary>0x222FE: Use on an item that is not readied.</summary>
    MustBeReadied,
    /// <summary>0x21DBE: Trade, Drop or Sell of a readied item.</summary>
    MustBeUnreadied,
    /// <summary>0x228BD: Halve of an item with a count below 2.</summary>
    CannotHalve,
    /// <summary>0x2250A refusals: cursed, wrong class, slot taken, hands full.</summary>
    Cursed, WrongClass, SlotTaken, HandsFull,
    /// <summary>An entry whose routine is not read here (Trade, Sell, Id) [?].</summary>
    NotModelled,
}

/// <summary>One answer on the Items screen: an entry and the item the list cursor is on (null: no item).</summary>
public readonly record struct ItemChoice(ItemsEntry Entry, byte[]? Item);

/// <summary>
/// The player's side of the item screens: the Client's UI (or a test script) answers each question the program asks,
/// at the point where it waits for a key.
/// </summary>
public interface IItemScreenChooser
{
    /// <summary>The View menu (0x2CC8C in 0x21AED): one of <paramref name="entries"/>.</summary>
    ViewEntry ChooseView(EclCharacter c, IReadOnlyList<ViewEntry> entries);
    /// <summary>The Items list and menu (0x2D669 in 0x21F32): an entry and the item it applies to.</summary>
    ItemChoice ChooseItem(EclCharacter c, IReadOnlyList<ItemsEntry> entries, IReadOnlyList<byte[]> items);
    /// <summary>"Drop it?" (0x22419).</summary>
    bool ConfirmDrop(EclCharacter c, byte[] item) => false;
    /// <summary>0x21E70: the scroll holds a spell flagged for scribing; is it all right to lose it?</summary>
    bool ConfirmLoseScroll(EclCharacter c, byte[] scroll) => false;
    /// <summary>Drop money (0x234E1): a coin kind 0–6 from <paramref name="kinds"/> (those the character has) and an
    /// amount (the input 0x24F1D, at most what is held), or null to stop.</summary>
    (int Kind, ushort Amount)? ChooseCoins(EclCharacter c, IReadOnlyList<int> kinds) => null;
    /// <summary>Use of a scroll: the spell list 0x239D6 over the scroll's spells; 0 = none.</summary>
    int ChooseScrollSpell(EclCharacter c, byte[] scroll) => 0;
    /// <summary>The player's aim for an item's spell (0x14611 with ai = 0, the aim 0x15D53): the creatures picked and
    /// the square, or null when the player aborts.</summary>
    (IReadOnlyList<EclCharacter> Targets, (int X, int Y)? Square)? AimItem(Fight fight, EclCharacter user, int spell) => null;
    void Notice(ItemNotice notice, EclCharacter c, byte[]? item) { }
}

/// <summary>
/// The View (0x21AED) and Items (0x21F32) screens in a fight (<see cref="IFightItemScreens"/>); the entry lists also for
/// the other places the program opens them (DS:49F3: 1 shop, 2–4 [?], 5 fight, 6 [?]).
/// </summary>
public sealed class FightItemScreens(IItemScreenChooser chooser, Action<EclCharacter, byte[], bool>? itemHandler = null)
    : IFightItemScreens
{
    /// <summary>DS:49F3 in a fight.</summary>
    public const byte InFight = 5, InShop = 1;
    /// <summary>Creature bytes: memorised spells 0x17 (21), money words 0x88 (7), NPC 0x84, item count 0xC7, weight
    /// 0x102.</summary>
    public const int Spells = 0x17, SpellSlots = 21, Money = 0x88, CoinKinds = 7, Npc = 0x84, ItemCount = 0xC7, Weight = 0x102;
    /// <summary>VM $49E5 (word [DS:49D2]+0x1CA): no item use / no magic here.</summary>
    public const ushort NoMagicCell = 0x49E5;

    public IItemScreenChooser Chooser { get; } = chooser;
    /// <summary>0x2AEEA for items with kind 0x3E &gt; 0x7F when readied (false) or unreadied (true).</summary>
    public Action<EclCharacter, byte[], bool>? ItemHandler { get; } = itemHandler;

    // ---- Entry lists ------------------------------------------------------------------------------------------------

    /// <summary>A coin word (0x88 + 2·kind).</summary>
    public static ushort Coins(EclCharacter c, int kind) => (ushort)(c[Money + 2 * kind] | c[Money + 2 * kind + 1] << 8);

    private static void SetCoins(EclCharacter c, int kind, ushort v)
    {
        c[Money + 2 * kind] = (byte)v;
        c[Money + 2 * kind + 1] = (byte)(v >> 8);
    }

    private static bool HasMoney(EclCharacter c) => Enumerable.Range(0, CoinKinds).Any(k => Coins(c, k) > 0);

    /// <summary>Not an NPC in play (0x21BFA / 0x22031): 0x84 &lt; 0x80, or out of play (0x10D = 0), or status 1.</summary>
    private static bool MayDeal(EclCharacter c) =>
        c[Npc] < 0x80 || c[FightRules.InPlay] == 0 || c[FightRules.Status] == 1;

    /// <summary>
    /// 0x21AED: Items when the item list is not empty; Spells when a memorised byte (0x17–0x2B) is non-zero; Trade when
    /// <see cref="MayDeal"/>, the character has money and not in a fight; Drop when it has money; Exit.
    /// </summary>
    public static IReadOnlyList<ViewEntry> ViewEntries(EclCharacter c, byte context)
    {
        var e = new List<ViewEntry>();
        if (c.Items.Count > 0)
            e.Add(ViewEntry.Items);
        if (Enumerable.Range(0, SpellSlots).Any(i => c[Spells + i] > 0))
            e.Add(ViewEntry.Spells);
        if (MayDeal(c) && HasMoney(c) && context != InFight)
            e.Add(ViewEntry.Trade);
        if (HasMoney(c))
            e.Add(ViewEntry.Drop);
        e.Add(ViewEntry.Exit);
        return e;
    }

    /// <summary>
    /// The Amiga View menu (hunk 10 +0x5332–0x548C, the copy of 0x21AED): the DOS entries and, before Exit, Rename when
    /// the character is not an NPC (Amiga record byte 0x85, DOS 0x84, below 0x80; hunk 10 +0x542C). Its other entries
    /// test the same as DOS's.
    /// </summary>
    public static IReadOnlyList<ViewEntry> WithAmigaRename(IReadOnlyList<ViewEntry> entries, EclCharacter c)
    {
        if (c[Npc] >= 0x80 || entries.Contains(ViewEntry.Rename))
            return entries;
        var e = entries.Where(x => x != ViewEntry.Exit).ToList();
        e.Add(ViewEntry.Rename);
        if (entries.Contains(ViewEntry.Exit))
            e.Add(ViewEntry.Exit);
        return e;
    }

    /// <summary>
    /// 0x21F32: Ready always; Use when in play, VM $49E5 = 0 and (DS:49F3 is 2, 3 or 4, or a fight with item use allowed
    /// on the combat record, +2); Trade when <see cref="MayDeal"/> and not in a fight; Drop; Halve when the item count
    /// 0xC7 &lt; 16; Join; Sell and Id in a shop (DS:49F3 = 1; Sell also needs <see cref="MayDeal"/>); Exit.
    /// </summary>
    public static IReadOnlyList<ItemsEntry> ItemsEntries(EclCharacter c, byte context, CombatRecord? record, EclMemory memory)
    {
        var e = new List<ItemsEntry> { ItemsEntry.Ready };
        if (c[FightRules.InPlay] != 0 && memory.Read(NoMagicCell) == 0 &&
            (context is 2 or 3 or 4 || (context == InFight && record is not null && record[CombatRecord.ItemUseAllowed] != 0)))
            e.Add(ItemsEntry.Use);
        if (MayDeal(c) && context != InFight)
            e.Add(ItemsEntry.Trade);
        e.Add(ItemsEntry.Drop);
        if (c[ItemCount] < 16)
            e.Add(ItemsEntry.Halve);
        e.Add(ItemsEntry.Join);
        if (MayDeal(c) && context == InShop)
            e.Add(ItemsEntry.Sell);
        if (context == InShop)
            e.Add(ItemsEntry.Id);
        e.Add(ItemsEntry.Exit);
        return e;
    }

    // ---- The screens (IFightItemScreens) ----------------------------------------------------------------------------

    /// <summary>
    /// The keys after which the View loop 0x21AED draws the whole page again (the set at CS:0A93 of its unit, GAME.OVR
    /// 0x21ACD: I, S, T). Drop (D) only clears and rewrites the money lines (0x21547); any other key redraws nothing.
    /// </summary>
    public static bool RedrawsViewPage(ViewEntry e) => e is ViewEntry.Items or ViewEntry.Spells or ViewEntry.Trade;

    /// <summary>
    /// 0x21AED in a fight for the current creature: until Exit or "done", the View menu: Items → the Items screen
    /// (<see cref="Use"/>, which may report done); Spells → the memorised list (nothing changes); Drop → drop money
    /// (<see cref="DropMoney"/>). Returns done. The page (0x21084) recalculates the creature (0x21642 → START 0x1758)
    /// each time it is drawn: on entry and after the keys of <see cref="RedrawsViewPage"/> (read s19), so after Drop
    /// the weight word is lower but the derived bytes (movement…) stand until the next recalculation.
    /// </summary>
    public bool View(Fight fight, EclCharacter c)
    {
        bool done = false;
        fight.RecalculateStats(c);
        while (!done)
        {
            var entries = ViewEntries(c, InFight);
            var pick = Chooser.ChooseView(c, entries);
            if (!entries.Contains(pick) || pick == ViewEntry.Exit)
                break;
            switch (pick)
            {
                case ViewEntry.Items:
                    done = Use(fight, c);
                    break;
                case ViewEntry.Drop:
                    DropMoney(c, InFight, null);
                    break;
                case ViewEntry.Trade:
                    Chooser.Notice(ItemNotice.NotModelled, c, null); // 0x23190 [?]
                    break;
            }
            if (!done && RedrawsViewPage(pick))
                fight.RecalculateStats(c);
        }
        return done;
    }

    /// <summary>
    /// 0x21F32 in a fight (the menu's Use, and View's Items) for the current creature: while not done, the item count
    /// 0xC7 is above 0 and the player does not leave: the entries (<see cref="ItemsEntries"/>) and the item list; the
    /// chosen entry acts on the chosen item: Ready → 0x2250A; Use → a "needs readying" message unless readied, else for a usable
    /// item the item use 0x22AC4 (<see cref="FightSpells.UseItemNow"/>; done as it reports); Drop → <see cref="CanLetGo"/>
    /// and "drop it?", then the item leaves (START 0x20A9); Halve (0x22827); Join (0x228D7). After each choice the stats
    /// are recalculated (START 0x1758). Returns done.
    /// </summary>
    public bool Use(Fight fight, EclCharacter c, FightMenu menu) => FromMenu(menu, () => Use(fight, c));

    public bool View(Fight fight, EclCharacter c, FightMenu menu) => FromMenu(menu, () => View(fight, c));

    private FightMenu? _menu;

    private bool FromMenu(FightMenu menu, Func<bool> screen)
    {
        var saved = _menu;
        _menu = menu;
        try
        {
            return screen();
        }
        finally
        {
            _menu = saved;
        }
    }

    public bool Use(Fight fight, EclCharacter c)
    {
        bool done = false;
        while (!done && c[ItemCount] > 0)
        {
            if (c.Items.Count == 0)
            {
                fight.RecalculateStats(c);
                continue;
            }
            var entries = ItemsEntries(c, InFight, fight.Record(c), fight.Memory);
            var (entry, item) = Chooser.ChooseItem(c, entries, c.Items);
            if (!entries.Contains(entry) || entry == ItemsEntry.Exit)
                break;
            if (item is null || !c.Items.Contains(item))
            {
                fight.RecalculateStats(c);
                continue;
            }
            switch (entry)
            {
                case ItemsEntry.Ready:
                    Ready(fight, c, item);
                    break;
                case ItemsEntry.Use:
                    done = UseItem(fight, c, item);
                    break;
                case ItemsEntry.Drop:
                    if (CanLetGo(fight.Items, c, item) && Chooser.ConfirmDrop(c, item))
                        c.Items.Remove(item);
                    break;
                case ItemsEntry.Halve:
                    if (!Halve(c, item))
                        Chooser.Notice(ItemNotice.CannotHalve, c, item);
                    break;
                case ItemsEntry.Join:
                    Join(c, item);
                    break;
                default: // Trade 0x2276E, Sell 0x22D28, Id 0x22F8C: not shown in a fight; not read here [?]
                    Chooser.Notice(ItemNotice.NotModelled, c, item);
                    break;
            }
            fight.RecalculateStats(c);
        }
        return done;
    }

    private void Ready(Fight fight, EclCharacter c, byte[] item)
    {
        int r = MonsterAi.ToggleReady(fight, c, item, ItemHandler);
        if (r != 0)
            Chooser.Notice(r switch
            {
                1 => ItemNotice.WrongClass, 2 => ItemNotice.SlotTaken, 3 => ItemNotice.HandsFull, _ => ItemNotice.Cursed,
            }, c, item);
    }

    /// <summary>0x222F0: Use of an item: not readied → a "needs readying" message, nothing else; a usable item (a scroll, or power
    /// 0x3D &gt; 0 with kind 0x3E &lt; 0x80) → 0x22AC4; anything else → nothing.</summary>
    private bool UseItem(Fight fight, EclCharacter c, byte[] item)
    {
        if (item[CombatRules.ItemEquipped] == 0)
        {
            Chooser.Notice(ItemNotice.MustBeReadied, c, item);
            return false;
        }
        if (!FightSpells.IsUsable(fight.Items, item))
            return false;
        if (fight.Spells is not FightSpells spells)
        {
            fight.Diagnostics.Add("[?] item use needs FightSpells");
            return false;
        }
        int scrollSpell = FightSpells.IsScroll(fight.Items, item) ? Chooser.ChooseScrollSpell(c, item) : 0;
        return spells.UseItemNow(fight, c, item, scrollSpell,
            _menu is { } menu ? menu.PickForItem : spell => PlayerPick(fight, spells, c, spell));
    }

    /// <summary>The player's pick for an item's spell outside the combat menu (opened from the menu, the menu's aim
    /// runs: <see cref="FightMenu.PickForItem"/>): the aim (<see cref="IItemScreenChooser.AimItem"/>) and the target
    /// pick 0x148BE over what it chose (<see cref="FightSpells.Pick"/>). Aborted → not picked.</summary>
    private bool PlayerPick(Fight fight, FightSpells spells, EclCharacter user, int spell) =>
        Chooser.AimItem(fight, user, spell) is { } aim && spells.Pick(fight, user, spell, aim.Targets, aim.Square);

    // ---- Item actions -----------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x21DAA (Trade, Drop, Sell): a readied item can't be let go ("must be unreadied"). A scroll with a spell byte
    /// (0x3C–0x3E) above 0x7F (flagged for scribing) needs a yes. Anything else may go.
    /// </summary>
    public bool CanLetGo(BaseItemTable items, EclCharacter c, byte[] item)
    {
        if (item[CombatRules.ItemEquipped] != 0)
        {
            Chooser.Notice(ItemNotice.MustBeUnreadied, c, item);
            return false;
        }
        if (!FightSpells.IsScroll(items, item))
            return true;
        if (item[0x3C] > 0x7F || item[0x3D] > 0x7F || item[0x3E] > 0x7F)
            return Chooser.ConfirmLoseScroll(c, item);
        return true;
    }

    /// <summary>
    /// 0x22827: half = count 0x39 shr 1; 0 → false (a message). Else a copy of the item with count = half and readied
    /// 0 goes right after it in the list, and the item keeps count − half.
    /// </summary>
    public static bool Halve(EclCharacter c, byte[] item)
    {
        int half = item[FightSpells.ItemCount] >> 1;
        if (half == 0)
            return false;
        var copy = (byte[])item.Clone();
        copy[FightSpells.ItemCount] = (byte)half;
        copy[CombatRules.ItemEquipped] = 0;
        item[FightSpells.ItemCount] = (byte)(item[FightSpells.ItemCount] - half);
        c.Items.Insert(c.Items.IndexOf(item) + 1, copy);
        return true;
    }

    /// <summary>
    /// 0x228D7 on the current creature's list: total = the item's count. Every other item in list order with count &gt;
    /// 0 and the same name words 0x2F–0x31, type 0x2E, plus 0x32, save bonus 0x33, curse 0x36 and weight word 0x37
    /// whose charges 0x3C are below 2 joins: while the total stays ≤ 255 it is added and the other item leaves the list
    /// (START 0x20A9); otherwise the item being filled gets 255, the other keeps what does not fit and becomes the one
    /// being filled. At the end the one being filled gets the total. (The program compares 0x3C, 0x3D and 0x3E of the
    /// other item with itself, so only "0x3C &lt; 2" counts; readied 0x34, hidden 0x35 and value 0x3A are not compared.)
    /// </summary>
    public static void Join(EclCharacter c, byte[] item)
    {
        byte[] target = item;
        int total = item[FightSpells.ItemCount];
        foreach (var o in c.Items.ToList())
        {
            if (ReferenceEquals(o, target) || o[FightSpells.ItemCount] == 0)
                continue;
            if (o[0x2F] != target[0x2F] || o[0x30] != target[0x30] || o[0x31] != target[0x31] || o[0x2E] != target[0x2E] ||
                o[0x32] != target[0x32] || o[0x33] != target[0x33] || o[0x36] != target[0x36] ||
                o[0x37] != target[0x37] || o[0x38] != target[0x38] || o[0x3C] >= 2)
                continue;
            if (total + o[FightSpells.ItemCount] <= 0xFF)
            {
                total += o[FightSpells.ItemCount];
                c.Items.Remove(o);
            }
            else
            {
                target[FightSpells.ItemCount] = 0xFF;
                o[FightSpells.ItemCount] = (byte)(o[FightSpells.ItemCount] - (0xFF - total));
                total = o[FightSpells.ItemCount];
                target = o;
            }
        }
        target[FightSpells.ItemCount] = (byte)total;
    }

    /// <summary>
    /// 0x234E1: until the player stops or no money is left: a coin kind the character has and an amount (at most what it
    /// holds: the input 0x24F1D clamps to the count, docs/re/camp.md "Trade"); 0x2568A takes the coins off, lowers the weight word
    /// 0x102 by the amount and, in a shop or DS:49F3 = 6, adds them to the pool (DS:67F4 + 4·kind); in a fight they are
    /// lost.
    /// </summary>
    public void DropMoney(EclCharacter c, byte context, uint[]? pool)
    {
        while (HasMoney(c))
        {
            var kinds = Enumerable.Range(0, CoinKinds).Where(k => Coins(c, k) != 0).ToList();
            if (Chooser.ChooseCoins(c, kinds) is not { } pick || !kinds.Contains(pick.Kind))
                return;
            ushort amount = Math.Min(pick.Amount, Coins(c, pick.Kind));
            DropCoins(c, pick.Kind, amount, context, pool);
        }
    }

    /// <summary>0x2568A: coins − amount, weight 0x102 − amount (0x24CA9); in a shop (1) or DS:49F3 = 6 the pool gains
    /// them.</summary>
    public static void DropCoins(EclCharacter c, int kind, ushort amount, byte context, uint[]? pool)
    {
        SetCoins(c, kind, (ushort)(Coins(c, kind) - amount));
        ushort w = (ushort)(c[Weight] | c[Weight + 1] << 8);
        w -= amount;
        c[Weight] = (byte)w;
        c[Weight + 1] = (byte)(w >> 8);
        if (context is 1 or 6 && pool is not null && kind < pool.Length)
            pool[kind] += amount;
    }
}
