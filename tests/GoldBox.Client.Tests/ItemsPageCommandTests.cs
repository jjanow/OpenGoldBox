using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// Ready, Drop, Halve and Join on the program's Items page outside a fight (0x2250A, 0x22398, 0x22827, 0x228D7; read
/// s51): refusals on row 24 during the game-speed pause (START 0x1693), Drop's warning in the box (1, 21)–(38, 22) in
/// colour 14 and its yes/no on row 24, the item handler 0x2AEEA on ready / unready. Our wordings (no game files).
/// </summary>
public class ItemsPageCommandTests
{
    private const int Sword = 1, Shield = 2, Ring = 3;

    private static BaseItemTable Table()
    {
        var t = new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]);
        void Type(int type, byte slot, byte hands, byte classes)
        {
            t.Set(type, BaseItemTable.Slot, slot);
            t.Set(type, 1, hands);
            t.Set(type, 13, classes);
        }
        Type(Sword, 0, 1, 0x04);  // weapon slot, one hand, fighters
        Type(Shield, 1, 1, 0x04); // off hand
        Type(Ring, 9, 0, 0xFF);
        return t;
    }

    private static byte[] Item(int type, byte count = 1, bool readied = false)
    {
        var item = new byte[63];
        item[0x2E] = (byte)type;
        item[0x2F] = (byte)(type + 10); // a name word (ItemName needs none to be found: the words are the Client's)
        item[0x34] = readied ? (byte)1 : (byte)0;
        item[0x39] = count;
        return item;
    }

    private sealed record Run(TradeContext T, List<object> Seen, List<bool> Talk, EclCharacter C, EffectHandlers Fx, List<bool> Framed);

    private static Run Page(byte venueClasses, IEnumerable<byte[]> items, params object?[] answers)
    {
        var w = new TextWindow();
        var seen = new List<object>();
        var talk = new List<bool>();
        var framed = new List<bool>();
        var q = new Queue<object?>(answers);
        var c = new EclCharacter { Name = "Anna" };
        c[0x10D] = 1;
        c[ItemCommands.ClassMask] = venueClasses;
        c.Items.AddRange(items);
        var p = new EclParty();
        p.Members.Add(c);
        p.CurrentIndex = 0;
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        TradeContext t = new()
        {
            Dialog = new Dialog(r =>
            {
                switch (r)
                {
                    case ChoiceRequest ch:
                        seen.Add(("ask", ch.MenuPrompt, w.Lines[4], w.Lines[5], w.Cell(21, 1).Colour));
                        return q.Dequeue();
                    case DelayRequest { Chained: true }:
                        return null; // typing
                    case DelayRequest d:
                        seen.Add(("pause", d.BottomLine?.Text ?? w.Lines[1].Trim() + "/" + w.Lines[2].Trim()));
                        return null;
                    default:
                        throw new InvalidOperationException($"unexpected {r}");
                }
            }, w),
            Text = new GameText(),
            CombatTexts = new CombatText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = _ => 0,
            ItemTypes = Table(),
            ItemEffects = () => fx,
            Memory = new EclMemory { GameSpeed = 2 },
            TalkOverItemsPage = on => talk.Add(on),
            FrameOverItemsPage = on => framed.Add(on),
            ItemsPage = (_, entries, row, _) =>
            {
                // The scripted (entry, row) pairs, then Exit.
                if (q.Count > 0 && q.Peek() is (ItemsEntry e, int at))
                {
                    q.Dequeue();
                    return new ListAnswer(entries.ToList().IndexOf(e), at);
                }
                return new ListAnswer(entries.Count - 1, row);
            },
        };
        return new Run(t, seen, talk, c, fx, framed);
    }

    [Fact]
    public void Ready_swaps_weapons_through_unready_then_ready_and_names_the_weapon_in_the_slot()
    {
        var old = Item(Sword, readied: true);
        var other = Item(Sword);
        var r = Page(0x04, [old, other], (ItemsEntry.Ready, 1), (ItemsEntry.Ready, 0), (ItemsEntry.Ready, 1));
        CharacterView.Items(r.T, Venue.Camp);
        // The second sword while the first is readied: "already using " and the first's name, on row 24.
        var first = ((string, string?))r.Seen[0];
        Assert.Equal("pause", first.Item1);
        Assert.StartsWith(new CombatText()[CombatMsg.AlreadyUsing], first.Item2);
        Assert.Equal(new CombatText()[CombatMsg.AlreadyUsing] + r.T.StoredItemName(old), first.Item2);
        Assert.Equal(0, old[0x34]);  // then unreadied
        Assert.Equal(1, other[0x34]); // and the other readied
        Assert.Single(r.Seen);
        Assert.Equal([true, false, true, false, true, false], r.Talk);
    }

    [Fact]
    public void Ready_refusals_cursed_wrong_class_and_hands_full_show_on_row_24()
    {
        var cursed = Item(Ring, readied: true);
        cursed[ItemCommands.Cursed] = 1;
        var sword = Item(Sword);
        var r = Page(0x01, [cursed, sword], (ItemsEntry.Ready, 0), (ItemsEntry.Ready, 1));
        CharacterView.Items(r.T, Venue.Shop);
        var x = new CombatText();
        Assert.Equal([("pause", x[CombatMsg.Cursed]), ("pause", x[CombatMsg.WrongClass])],
            r.Seen.Cast<(string, string?)>().ToList());
        Assert.Equal(1, cursed[0x34]); // still readied
    }

    [Fact]
    public void Hands_full_is_shown_outside_a_fight()
    {
        var two = Item(7);
        var r = Page(0x04, [two], (ItemsEntry.Ready, 0));
        r.T.ItemTypes!.Set(7, BaseItemTable.Slot, 0);
        r.T.ItemTypes.Set(7, 1, 3);
        r.T.ItemTypes.Set(7, 13, 0x04);
        CharacterView.Items(r.T, Venue.PartyMenu);
        Assert.Equal([("pause", new CombatText()[CombatMsg.HandsFull])], r.Seen.Cast<(string, string?)>().ToList());
        Assert.Equal(0, two[0x34]);
    }

    [Fact]
    public void Ready_of_an_item_with_a_handler_runs_it_outside_a_fight()
    {
        var ring = Item(Ring);
        ring[ItemCommands.Kind] = 0x89; // 137: removed → effect 0x17 removed
        var r = Page(0x04, [ring], (ItemsEntry.Ready, 0), (ItemsEntry.Ready, 0));
        r.C.Effects.Add([0x17, 0, 0, 0, 0]);
        CharacterView.Items(r.T, Venue.Camp);
        Assert.Equal(0, ring[0x34]);                       // readied, then unreadied
        Assert.DoesNotContain(r.C.Effects, e => e[0] == 0x17); // the unready ran handler 137 with "removed"
    }

    [Fact]
    public void Gauntlets_ready_shows_the_handler_text_framed_over_the_page()
    {
        // Handler 131 (0x11BB2) from Ready outside a fight: START 0x16EC draws the text window's frame again over the
        // page, the name on row 18 and the stronger words from row 19, the pause (DOSBox-X s51, the camp's Items page).
        var g = Item(Ring);
        g[0x2E] = 9;
        g[ItemCommands.Kind] = 131;
        var r = Page(0x04, [g], (ItemsEntry.Ready, 0));
        r.T.ItemTypes!.Set(9, BaseItemTable.Slot, 6);
        r.T.ItemTypes.Set(9, 13, 0xFF);
        r.C[0x10] = 12; // Strength 12: the gauntlets raise it
        CharacterView.Items(r.T, Venue.Camp);
        Assert.Equal(1, g[0x34]);
        Assert.Equal(18, r.C[0x10]);
        var pause = Assert.Single(r.Seen.Cast<(string, string?)>());
        Assert.Equal(("pause", "ANNA/" + new CombatText()[CombatMsg.HandlerStronger].Trim()), pause);
        Assert.Equal([true, false], r.Framed);
        Assert.Equal([true, false], r.Talk);
    }

    [Fact]
    public void The_item_handler_runs_on_its_own_engine_so_the_games_display_does_not_show_it_twice()
    {
        var c = new EclCharacter { Name = "Anna" };
        c[0x10] = 12;
        var p = new EclParty();
        p.Members.Add(c);
        p.CurrentIndex = 0;
        var game = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        int heard = 0, pauses = 0;
        game.Event += (_, _) => heard++; // the area window's display (ClientPlayer.ShowOutsideEffects)
        var t = new TradeContext
        {
            Dialog = new Dialog(r => { if (r is DelayRequest { Chained: false }) pauses++; return null; }, new TextWindow()),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = _ => 0,
            Memory = new EclMemory { GameSpeed = 2 },
            Effects = game,
            ItemEffects = () => new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current },
        };
        var g = Item(Ring);
        g[ItemCommands.Kind] = 131;
        t.ItemHandler!(c, g, false);
        Assert.Equal(0, heard);
        Assert.Equal(1, pauses);
    }

    [Fact]
    public void Drop_warns_in_the_box_asks_and_the_item_is_gone_in_every_venue()
    {
        foreach (var venue in new[] { Venue.Camp, Venue.Shop, Venue.Loot, Venue.PartyMenu, Venue.Explore })
        {
            var keep = Item(Ring);
            var r = Page(0x04, [keep, Item(Sword)], (ItemsEntry.Drop, 0), 1, (ItemsEntry.Drop, 0), 0);
            CharacterView.Items(r.T, venue);
            var ask = ((string, string?, string, string, byte))r.Seen[0];
            Assert.Equal(new CombatText()[CombatMsg.DropIt].TrimEnd(), ask.Item2);
            Assert.StartsWith(new CombatText()[CombatMsg.Your].Trim(), ask.Item3); // the warning on rows 21–22
            Assert.Equal(14, ask.Item5);
            Assert.Single(r.C.Items);          // No kept it, Yes dropped it
            Assert.DoesNotContain(keep, r.C.Items);
            Assert.True(r.T.Pool.All(v => v == 0));
            Assert.Empty(r.T.Treasure);
            Assert.True(r.T.Dialog.Text.IsEmpty); // the box blanked
        }
    }

    [Fact]
    public void Drop_of_a_readied_item_says_unready_it_first()
    {
        var r = Page(0x04, [Item(Sword, readied: true)], (ItemsEntry.Drop, 0));
        CharacterView.Items(r.T, Venue.Camp);
        Assert.Equal([("pause", new GameText()[Msg.MustBeUnreadied])], r.Seen.Cast<(string, string?)>().ToList());
        Assert.Single(r.C.Items);
    }

    [Fact]
    public void Halve_splits_a_stack_join_puts_it_back_and_a_single_cannot_be_halved()
    {
        var arrows = Item(Ring, 7);
        var r = Page(0x04, [arrows], (ItemsEntry.Halve, 0), (ItemsEntry.Join, 0), (ItemsEntry.Halve, 0));
        r.C.Items.Add(Item(Sword));
        CharacterView.Items(r.T, Venue.Explore);
        Assert.Equal(3, r.C.Items.Count); // 7 → 4 + 3, joined back to 7, halved again: 4, 3, the sword
        Assert.Equal(4, r.C.Items[0][0x39]);
        Assert.Equal(3, r.C.Items[1][0x39]);
        Assert.Empty(r.Seen);

        var one = Page(0x04, [Item(Sword)], (ItemsEntry.Halve, 0));
        CharacterView.Items(one.T, Venue.Camp);
        Assert.Equal([("pause", new CombatText()[CombatMsg.CannotHalve])], one.Seen.Cast<(string, string?)>().ToList());
        Assert.Single(one.C.Items);
    }
}
