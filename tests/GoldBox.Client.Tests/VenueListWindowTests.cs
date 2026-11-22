using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The shop's Buy (0x6D04) and the loot's item take (0x6092) on the list routine 0x2D669's window (1, 4)–(38, 22), paged
/// through the shared top (<see cref="MenuHighlight.ListTop"/>), P + D s46 (por-art-formats.md "Creation's list labels",
/// shop-format.md "Buy"). DOSBox-X frames with 28 items: the shop loaded straight after start-up (top 1) showed items
/// 2–20 with the first window row lit and "ITEMS: BUY NEXT PREV EXIT"; after Take's coin kinds (top 0) items 1–19 with
/// the second row lit; the loot from the top 0 items 1–19 with the first lit, Next items 10–28, a take there left the
/// window at item 10 (now the old 11th) with "TAKE PREV EXIT".
/// </summary>
public class VenueListWindowTests
{
    private static byte[] Item(ushort value)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x3A] = (byte)value;
        r[0x3B] = (byte)(value >> 8);
        return r;
    }

    private static (TradeContext T, List<PlayerRequest> Asked) Context(int items, Queue<object?> answers)
    {
        var c = new EclCharacter { Name = "ANNA" };
        c[0x10] = 18;
        var party = new EclParty();
        party.Members.Add(c);
        party.CurrentIndex = 0;
        var asked = new List<PlayerRequest>();
        var t = new TradeContext
        {
            Dialog = new Dialog(r =>
            {
                asked.Add(r);
                return answers.Count > 0 ? answers.Dequeue() : null;
            }, new TextWindow()),
            Text = new GameText(),
            ListWords = ("NEXT", "PREV"),
            Party = party,
            Pool = new uint[7],
            Treasure = [.. Enumerable.Range(0, items).Select(i => Item((ushort)(i + 1)))],
            Random = _ => 0,
        };
        return (t, asked);
    }

    private static ListAnswer? Press(ListPage page, ListRequest r, GameAction a)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        return page.Update(r, input);
    }

    [Fact]
    public void Buy_opens_at_position_1_in_the_programs_window()
    {
        var (t, asked) = Context(28, new Queue<object?>([new ListAnswer(1, 0)]));
        VenueScreens.Buy(t);
        var r = Assert.IsType<ListRequest>(Assert.Single(asked));
        Assert.Equal(1, r.Selected);                                   // 0x7050
        Assert.Equal((4, 19, 1), (r.FirstRow, r.Paging!.Rows, r.Paging.WordsAt));
        Assert.Equal(["BUY", "EXIT"], r.Actions);
        Assert.True(r.Spell!.CentredTitle);
        Assert.Equal("ITEMS: ", r.Spell.Prompt);
        Assert.Equal(7, r.Spell.MenuStart);                            // BUY at column 7 (DOSBox-X)

        var h = new MenuHighlight();                                   // DOS start-up: top 1
        var page = new ListPage { Shared = h };
        page.Sync(r);
        Assert.Equal((1, 1), (page.Top, page.SelectedRow));            // the second item on the window's first row
        Assert.Equal(["BUY", "NEXT", "PREV", "EXIT"], page.ShownActions);

        h.ListTop = 0;                                                 // after a list at position 0 (Take's kinds)
        page = new ListPage { Shared = h };
        page.Sync(r);
        Assert.Equal((0, 1), (page.Top, page.SelectedRow));            // the first window, the second row lit
        Assert.Equal(["BUY", "NEXT", "EXIT"], page.ShownActions);
    }

    [Fact]
    public void The_loot_take_keeps_its_position_and_the_shared_top_after_a_take()
    {
        var (t, asked) = Context(28, new Queue<object?>([new ListAnswer(0, 9), new ListAnswer(1, 0)]));
        VenueScreens.TakeItems(t);
        Assert.Equal(27, t.Treasure.Count);
        var first = Assert.IsType<ListRequest>(asked[0]);
        var second = Assert.IsType<ListRequest>(asked[1]);
        Assert.Equal(0, first.Selected);                               // 0x619F
        Assert.Equal(9, second.Selected);                              // the row taken, kept

        var h = new MenuHighlight { ListTop = 0 };
        var page = new ListPage { Shared = h };
        page.Sync(first);
        Assert.Equal((0, 0), (page.Top, page.SelectedRow));
        Assert.Null(Press(page, first, GameAction.StepDownRight));     // Next: top min(0 + 19, 28 − 19) = 9
        Assert.Equal((9, 9), (page.Top, page.SelectedRow));
        Assert.Equal(9, h.ListTop);
        page.Sync(second);                                             // 27 items: the top stays 9, no Next
        Assert.Equal((9, 9), (page.Top, page.SelectedRow));
        Assert.Equal(["TAKE", "PREV", "EXIT"], page.ShownActions);
    }

    [Fact]
    public void A_stock_row_is_the_name_over_the_value_right_aligned_in_30()
    {
        Assert.Equal("POTION".PadRight(27) + "200", VenueScreens.PriceRow("POTION", Item(200)));
        var scroll = "CLERICAL SCROLL WITH 2 SPELLS ";                // 30 characters: the value hidden
        Assert.Equal(scroll, VenueScreens.PriceRow(scroll, Item(300)));
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZABC0", VenueScreens.PriceRow("ABCDEFGHIJKLMNOPQRSTUVWXYZABC", Item(300)));
    }
}
