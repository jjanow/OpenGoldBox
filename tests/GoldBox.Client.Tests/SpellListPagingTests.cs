using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The list routine 0x2D669's window behind the spell lists (P, s30; combat-menu.md "Spell lists' Next / Prev"):
/// Next while rows remain below the window, Prev while the top is past the leading labels, both before Exit; Next /
/// Prev move the window by its rows, the highlight keeping its place; Up / Down wrap inside the window and skip labels.
/// </summary>
public class SpellListPagingTests
{
    private static readonly ListPaging Paging18 = new("NEXT", "PREV", 18, 1);

    /// <summary>A spell list of <paramref name="levels"/> headings each followed by <paramref name="perLevel"/> spells.</summary>
    private static ListRequest List(int levels, int perLevel, ListPaging? paging = null, int selected = 0, int top = 0)
    {
        var rows = new List<string>();
        var labels = new HashSet<int>();
        for (int l = 1; l <= levels; l++)
        {
            labels.Add(rows.Count);
            rows.Add($"LEVEL {l}");
            for (int s = 0; s < perLevel; s++)
                rows.Add($"  SPELL {l}.{s}");
        }
        return new ListRequest([new ListLine("TITLE")], rows, ["CAST", "EXIT"], 1, FirstRow: SpellLists.FirstRow)
        {
            Labels = labels,
            RowActions = new HashSet<int> { 0 },
            Paging = paging ?? Paging18,
            Selected = selected,
            Top = top,
        };
    }

    private static ListAnswer? Press(ListPage page, ListRequest r, GameAction a)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        return page.Update(r, input);
    }

    /// <summary>Chooses the shown word <paramref name="word"/> with the highlight and Confirm.</summary>
    private static ListAnswer? Choose(ListPage page, ListRequest r, string word)
    {
        page.SetAction(r, page.ShownActions.ToList().IndexOf(word));
        return Press(page, r, GameAction.Confirm);
    }

    [Fact]
    public void A_short_list_has_no_page_words_and_starts_on_its_last_row()
    {
        // Two headings and 3 spells each: 8 rows in 18. The entry steps one row down and up again, then past the label
        // at row 0 upwards, which wraps to the window's last row and then to the list's (0x2D5F1–0x2D646, 0x2D46E).
        var page = new ListPage();
        var r = List(2, 3);
        page.Sync(r);
        Assert.Equal(["CAST", "EXIT"], page.ShownActions);
        Assert.Equal(7, page.SelectedRow);
    }

    [Fact]
    public void Next_shows_while_rows_remain_below_and_pages_by_the_window()
    {
        var page = new ListPage();
        var r = List(4, 6, selected: 1); // 28 rows: two pages of 18
        page.Sync(r);
        Assert.Equal(1, page.SelectedRow);
        Assert.Equal(["CAST", "NEXT", "EXIT"], page.ShownActions); // 28 − 18 > 0; top 0 is not past the leading label
        Assert.Null(Choose(page, r, "NEXT"));                       // Next pages here, the request stays up
        Assert.Equal(10, page.Top);                                 // min(0 + 18, 28 − 18)
        Assert.Equal(11, page.SelectedRow);                         // the same place in the window
        Assert.Equal(["CAST", "PREV", "EXIT"], page.ShownActions);
        Assert.Equal("PREV", page.ShownActions[page.SelectedAction]); // the cell stays on the 2nd entry
        Assert.Null(Choose(page, r, "PREV"));
        Assert.Equal(0, page.Top);
        Assert.Equal(1, page.SelectedRow);
        var pick = Choose(page, r, "CAST");
        Assert.Equal(new ListAnswer(0, 1, 0), pick);
    }

    [Fact]
    public void Prev_needs_the_top_past_the_leading_labels()
    {
        // A list whose first row is a heading: with the top at 1 (only the heading above the window) there is no Prev
        // (0x2D821: top > the count of leading +0x29 entries).
        var page = new ListPage();
        var r = List(1, 19, selected: 1, top: 1); // 20 rows
        page.Sync(r);
        Assert.Equal(1, page.Top);
        Assert.DoesNotContain("PREV", page.ShownActions);
        Assert.Contains("NEXT", page.ShownActions);                 // 20 − 18 > 1
    }

    [Fact]
    public void Up_and_down_wrap_inside_the_window_and_skip_headings()
    {
        var page = new ListPage();
        var r = List(4, 6, selected: 1); // headings at 0, 7, 14, 21
        page.Sync(r);
        Press(page, r, GameAction.MenuUp);                          // row 0 is a heading: past it upwards, wraps to 17
        Assert.Equal(17, page.SelectedRow);
        Press(page, r, GameAction.MenuDown);                        // 18 is outside the window: back to the top, a heading, on to 1
        Assert.Equal(1, page.SelectedRow);
        Assert.Equal(0, page.Top);                                  // the window never scrolls with the highlight
        for (int i = 0; i < 6; i++)
            Press(page, r, GameAction.MenuDown);                    // 2..6, then the heading at 7 is skipped
        Assert.Equal(8, page.SelectedRow);
    }

    [Fact]
    public void A_kept_place_comes_back_on_its_page()
    {
        var page = new ListPage();
        var r = List(4, 6, selected: 20, top: 10);
        page.Sync(r);
        Assert.Equal((10, 20), (page.Top, page.SelectedRow));
        var fresh = List(4, 6, selected: 3, top: 10);               // a top below the highlight goes up to it (0x2D766)
        page.Sync(fresh);
        Assert.Equal(3, page.Top);
    }

    /// <summary>One frame with keyboard key <paramref name="k"/> or pad button <paramref name="b"/> pressed.</summary>
    private static ListAnswer? Device(ListPage page, ListRequest r, Keys? k = null, Buttons? b = null)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        var keys = k is { } key ? new KeyboardState(key) : default;
        var pad = b is { } button ? new GamePadState(default, default, new GamePadButtons(button), default) : default;
        input.Update(keys, pad, default, TimeSpan.FromMilliseconds(20), _ => null);
        return page.Update(r, input);
    }

    [Fact]
    public void Enter_on_a_lit_next_picks_the_row_as_the_program_does()
    {
        // 0x2D669 returns Enter (13) with the highlighted row; the callers take it as the pick (user decision s30).
        var page = new ListPage();
        var r = List(4, 6, selected: 1);
        page.Sync(r);
        page.SetAction(r, page.ShownActions.ToList().IndexOf("NEXT"));
        Assert.Equal(new ListAnswer(0, 1, 0), Device(page, r, Keys.Enter));
    }

    [Fact]
    public void PgDn_and_PgUp_page_while_their_word_shows_and_the_pad_pages_with_its_shoulders()
    {
        var page = new ListPage();
        var r = List(4, 6, selected: 1);
        page.Sync(r);
        Assert.Null(Device(page, r, Keys.PageUp));                  // no Prev shown: nothing
        Assert.Equal(0, page.Top);
        Assert.Null(Device(page, r, Keys.PageDown));
        Assert.Equal((10, 11), (page.Top, page.SelectedRow));
        Assert.Null(Device(page, r, b: Buttons.LeftShoulder));
        Assert.Equal((0, 1), (page.Top, page.SelectedRow));
        Assert.Null(Device(page, r, b: Buttons.RightShoulder));
        Assert.Equal(10, page.Top);
        Assert.Equal(new ListAnswer(0, 11, 10), Device(page, r, b: Buttons.A)); // A picks the row even with Prev lit
    }

    [Fact]
    public void Memorize_has_an_eleven_row_window_and_learn_puts_the_words_after_its_word()
    {
        var text = new CombatText();
        var memorize = SpellLists.Paging(text, memorize: true, wordsAt: 1);
        Assert.Equal(11, memorize.Rows);
        Assert.Equal(18, SpellLists.Paging(text, memorize: false, wordsAt: 1).Rows);
        var page = new ListPage();
        var r = List(2, 6, memorize, selected: 1);                  // 14 rows in 11
        page.Sync(r);
        Assert.Equal(["CAST", "NEXT", "EXIT"], page.ShownActions);
        var learn = new ListRequest([], r.Rows, ["LEARN"], null, FirstRow: SpellLists.FirstRow)
        {
            Labels = r.Labels,
            Paging = SpellLists.Paging(text, memorize: false, wordsAt: 1) with { Rows = 11 },
            Selected = 1,
        };
        page.Sync(learn);
        Assert.Equal(["LEARN", "NEXT"], page.ShownActions);
    }
}
