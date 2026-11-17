using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The View (0x21AED) and Items / Use (0x21F32) screens' Core side (docs/re/combat-menu.md "View / Use").</summary>
public class FightItemScreensTests
{
    private sealed class Script : IItemScreenChooser
    {
        public Queue<ViewEntry> Views { get; } = new();
        public Queue<ItemChoice> Choices { get; } = new();
        public Queue<(int, ushort)?> Coins { get; } = new();
        public List<ItemNotice> Notices { get; } = [];
        public List<IReadOnlyList<ItemsEntry>> Shown { get; } = [];
        public bool DropYes { get; set; } = true;
        public ViewEntry ChooseView(EclCharacter c, IReadOnlyList<ViewEntry> entries) =>
            Views.Count > 0 ? Views.Dequeue() : ViewEntry.Exit;
        public ItemChoice ChooseItem(EclCharacter c, IReadOnlyList<ItemsEntry> entries, IReadOnlyList<byte[]> items)
        {
            Shown.Add(entries);
            return Choices.Count > 0 ? Choices.Dequeue() : new ItemChoice(ItemsEntry.Exit, null);
        }
        public bool ConfirmDrop(EclCharacter c, byte[] item) => DropYes;
        public (int Kind, ushort Amount)? ChooseCoins(EclCharacter c, IReadOnlyList<int> kinds) =>
            Coins.Count > 0 ? Coins.Dequeue() : null;
        public (IReadOnlyList<EclCharacter> Targets, (int X, int Y)? Square)? AimItem(Fight fight, EclCharacter user, int spell) =>
            ([user], null);
        public void Notice(ItemNotice notice, EclCharacter c, byte[]? item) => Notices.Add(notice);
    }

    private const int Wand = 40, Scroll = 41;

    private static (Fight Fight, EclCharacter C) Setup()
    {
        var table = new byte[BaseItemTable.EntrySize * BaseItemTable.Count];
        table[BaseItemTable.EntrySize * Scroll + BaseItemTable.Slot] = 12;
        table[BaseItemTable.EntrySize * Wand + BaseItemTable.Slot] = 4;
        table[BaseItemTable.EntrySize * Wand + 13] = 0xFF; // class mask: any class may ready it
        var c = new EclCharacter();
        c[FightRules.InPlay] = 1;
        c[MonsterAi.ClassMask] = 1;
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(c);
        var rng = new TurboPascalRandom(1);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable(table), rng.Next, new EndActionsController(),
            new GridGeometry());
        // spell 5: area mode 0 (the caster), castable in a fight, no effect
        var spells = new SpellTable(new Dictionary<int, SpellInfo> { [5] = new(0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0) });
        fight.Spells = new FightSpells(spells, new EffectHandlers(rng.Next), rng.Next);
        fight.State.Current = c;
        fight.Record(c)[CombatRecord.ItemUseAllowed] = 1;
        fight.Record(c).InitiativeValue = 9;
        return (fight, c);
    }

    private static byte[] Item(int type, bool readied, byte charges = 0, byte power = 0, byte count = 0)
    {
        var b = new byte[63];
        b[0x2E] = (byte)type;
        b[0x34] = (byte)(readied ? 1 : 0);
        b[0x39] = count;
        b[0x3C] = charges;
        b[0x3D] = power;
        return b;
    }

    private static void AddItems(EclCharacter c, params byte[][] items)
    {
        c.Items.AddRange(items);
        c[FightItemScreens.ItemCount] = (byte)c.Items.Count;
    }

    [Fact]
    public void View_entries_in_a_fight_have_no_trade()
    {
        var (_, c) = Setup();
        AddItems(c, Item(Wand, false));
        c[0x17 + 3] = 5;
        c[FightItemScreens.Money + 6] = 10; // gold
        Assert.Equal([ViewEntry.Items, ViewEntry.Spells, ViewEntry.Drop, ViewEntry.Exit],
            FightItemScreens.ViewEntries(c, FightItemScreens.InFight));
        Assert.Equal([ViewEntry.Items, ViewEntry.Spells, ViewEntry.Trade, ViewEntry.Drop, ViewEntry.Exit],
            FightItemScreens.ViewEntries(c, 0));
    }

    [Fact]
    public void Items_entries_follow_the_fight_record_and_the_no_magic_cell()
    {
        var (f, c) = Setup();
        AddItems(c, Item(Wand, false));
        var rec = f.Record(c);
        Assert.Equal([ItemsEntry.Ready, ItemsEntry.Use, ItemsEntry.Drop, ItemsEntry.Halve, ItemsEntry.Join, ItemsEntry.Exit],
            FightItemScreens.ItemsEntries(c, FightItemScreens.InFight, rec, f.Memory));
        rec[CombatRecord.ItemUseAllowed] = 0;
        Assert.DoesNotContain(ItemsEntry.Use, FightItemScreens.ItemsEntries(c, FightItemScreens.InFight, rec, f.Memory));
        rec[CombatRecord.ItemUseAllowed] = 1;
        f.Memory.Write(FightItemScreens.NoMagicCell, 1);
        Assert.DoesNotContain(ItemsEntry.Use, FightItemScreens.ItemsEntries(c, FightItemScreens.InFight, rec, f.Memory));
        f.Memory.Write(FightItemScreens.NoMagicCell, 0);
        c[FightItemScreens.ItemCount] = 16;
        Assert.DoesNotContain(ItemsEntry.Halve, FightItemScreens.ItemsEntries(c, FightItemScreens.InFight, rec, f.Memory));
        Assert.Equal([ItemsEntry.Ready, ItemsEntry.Trade, ItemsEntry.Drop, ItemsEntry.Join, ItemsEntry.Sell, ItemsEntry.Id,
            ItemsEntry.Exit], FightItemScreens.ItemsEntries(c, FightItemScreens.InShop, null, f.Memory));
    }

    [Fact]
    public void Use_needs_a_readied_item_then_casts_spends_a_charge_and_reports_done()
    {
        var (f, c) = Setup();
        var wand = Item(Wand, false, charges: 2, power: 5);
        AddItems(c, wand);
        var s = new Script();
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Use, wand));
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Ready, wand));
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Use, wand));
        var screens = new FightItemScreens(s);
        Assert.True(screens.Use(f, c));
        Assert.Equal([ItemNotice.MustBeReadied], s.Notices);
        Assert.Equal(1, wand[0x3C]);
        Assert.Equal(0, f.Record(c).InitiativeValue); // the spell's byte 11 ≠ 0: actions end
        Assert.Equal(3, s.Shown.Count); // done ends the screen
    }

    [Fact]
    public void Drop_refuses_a_readied_item_and_asks_first()
    {
        var (f, c) = Setup();
        var a = Item(Wand, true);
        var b = Item(Wand, false);
        AddItems(c, a, b);
        var s = new Script { DropYes = true };
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Drop, a));
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Drop, b));
        Assert.False(new FightItemScreens(s).Use(f, c));
        Assert.Equal([ItemNotice.MustBeUnreadied], s.Notices);
        Assert.Equal([a], c.Items);
    }

    [Fact]
    public void View_items_use_ends_the_view_when_done()
    {
        var (f, c) = Setup();
        var wand = Item(Wand, true, charges: 1, power: 5);
        AddItems(c, wand);
        var s = new Script();
        s.Views.Enqueue(ViewEntry.Items);
        s.Views.Enqueue(ViewEntry.Items); // not reached
        s.Choices.Enqueue(new ItemChoice(ItemsEntry.Use, wand));
        Assert.True(new FightItemScreens(s).View(f, c));
        Assert.Empty(c.Items); // the last charge: the item leaves the list
        Assert.Single(s.Views);
    }

    [Fact]
    public void Dropped_money_in_a_fight_is_lost()
    {
        var (_, c) = Setup();
        c[FightItemScreens.Money + 2 * 3] = 50; // gold (kind 3)
        c[FightItemScreens.Weight] = 60;
        var s = new Script();
        s.Coins.Enqueue((3, 20));
        s.Coins.Enqueue((3, 500)); // at most what is held
        var pool = new uint[7];
        new FightItemScreens(s).DropMoney(c, FightItemScreens.InFight, pool);
        Assert.Equal(0, FightItemScreens.Coins(c, 3));
        Assert.Equal(10, c[FightItemScreens.Weight]);
        Assert.All(pool, p => Assert.Equal(0u, p));
    }
}
