using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Tests;

/// <summary>
/// The list routine's global window top (DOS DS:6B29, Amiga hunk 32 +0x17D6; <see cref="MenuHighlight.ListTop"/>),
/// read s45: only the start-up, the routine's entry (0x2D766–0x2D792, hunk 13 +0x2C74) and its paging (0x2D502, hunk
/// 13 +0x340E) write it, so every list moves it and Next / Prev leave it where they went.
/// </summary>
public class ListTopTests
{
    private static ListRequest Spells(int rows, int selected = 0, int top = 0) =>
        new([new ListLine("TITLE")], [.. Enumerable.Range(0, rows).Select(i => $"  SPELL {i}")], ["CAST", "EXIT"], 1,
            FirstRow: SpellLists.FirstRow)
        {
            RowActions = new HashSet<int> { 0 },
            Paging = new ListPaging("NEXT", "PREV", 18, 1),
            Selected = selected,
            Top = top,
        };

    private static ListAnswer? Press(ListPage page, ListRequest r, GameAction a)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        return page.Update(r, input);
    }

    [Fact]
    public void A_paged_list_enters_the_global_top_and_next_leaves_it_there()
    {
        var h = new MenuHighlight();                      // DOS start-up: 1
        var page = new ListPage { Shared = h };
        var r = Spells(40);                               // 0x270A2: position −1 → 0
        page.Sync(r);
        Assert.Equal(0, page.Top);
        Assert.Equal(0, h.ListTop);                       // entry: the top no further down than the position
        Assert.Null(Press(page, r, GameAction.StepDownRight)); // PgDn: 0x2D502, top + 18
        Assert.Equal(18, page.Top);
        Assert.Equal(18, h.ListTop);
        Assert.NotNull(Press(page, r, GameAction.Cancel));  // Escape ends the list; the word stays
        Assert.Equal(18, h.ListTop);
        Assert.Equal(1, MenuHighlight.EnterList(h.ListTop, 1, 7)); // so a creation list then hides its label (top 1)

        // A list whose caller keeps its position (Memorize, Scribe, Learn) comes back where the word is, whatever
        // the request's own top says.
        var again = Spells(40, selected: 30, top: 0);
        page.Sync(again);
        Assert.Equal(18, page.Top);
        Press(page, again, GameAction.StepUpRight);       // PgUp: 0x2D502, top − 18, at least 0
        Assert.Equal(0, h.ListTop);
    }

    [Fact]
    public void Without_the_shared_cells_the_request_keeps_its_own_top()
    {
        var page = new ListPage();
        page.Sync(Spells(40, selected: 30, top: 18));
        Assert.Equal(18, page.Top);
    }

    [Fact]
    public void An_empty_list_leaves_the_top()
    {
        // 0x2D696–0x2D6B6: a nil list returns before the cell and the top.
        var h = new MenuHighlight();
        Assert.Equal(1, h.ListEntered(0, 0));
        Assert.Equal(1, h.ListTop);
        var page = new ListPage { Shared = h };
        page.Sync(Spells(0));
        Assert.Equal(1, h.ListTop);
    }

    [Fact]
    public void A_list_without_paging_enters_the_top_at_its_position()
    {
        // View's Items under a venue, the shop's lists: the Client's scrolling list, the program's routine underneath.
        var h = new MenuHighlight();
        var page = new ListPage { Shared = h };
        page.Sync(new ListRequest([], ["A", "B"], ["EXIT"], 0));
        Assert.Equal(0, h.ListTop);
        h.ListTop = 1;
        page.Sync(new ListRequest([], ["A", "B"], ["EXIT"], 0) { SharedHighlight = true }); // View's menu: not the list routine
        Assert.Equal(1, h.ListTop);
    }
}
