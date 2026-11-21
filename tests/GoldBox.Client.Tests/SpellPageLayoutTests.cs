using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The spell list's own screen (0x239D6 / 0x270A2 / 0x17DD5; P + D s33, combat-menu.md "Spell lists' Next / Prev"):
/// the title window on rows 0–2 with the name + "'S" in the name colour and "SPELLS …" green at the name's length + 4,
/// the list window framed down to row 23 (Memorize's to 16, its slot lines in the text box below), the rows with no
/// cursor, the choose-spell prompt on row 24 before the words. Drawn with a solid font so every text cell is filled
/// with its colour.
/// </summary>
public class SpellPageLayoutTests
{
    private static readonly TextFont Solid = new(Enumerable.Repeat((byte)0xFF, 177 * 8).ToArray());

    private static ListRequest List(bool memorize, string? prompt, IReadOnlyList<WindowLine>? box = null)
    {
        var rows = new List<string> { "1ST LEVEL" };
        var labels = new HashSet<int> { 0 };
        for (int i = 0; i < 13; i++)
            rows.Add("  SPELL " + i);
        string[] actions = prompt is null ? ["EXIT"] : ["MEMORIZE", "EXIT"];
        return new ListRequest([new ListLine("TITLE")], rows, actions, actions.Length - 1, FirstRow: SpellLists.FirstRow)
        {
            Labels = labels,
            RowActions = new HashSet<int> { 0 },
            Paging = new ListPaging("NEXT", "PREV", memorize ? 11 : 18, actions.Length - 1),
            Spell = new SpellPageLayout("RHIANNON", 11, "SPELLS IN SPELL BOOK") { Prompt = prompt, Box = box },
        };
    }

    private static IndexedFrame Draw(ListRequest r, bool highlight = true, bool mouse = false)
    {
        var page = new ListPage();
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (mouse)
            input.Inject(GameAction.MenuLeft); // a mouse action: the device in use is the mouse
        var f = new IndexedFrame(320, 200);
        page.Draw(f, Solid, r, input, highlight);
        return f;
    }

    /// <summary>A text cell's colour (the solid font fills it), or null when it is not one colour.</summary>
    private static byte? Cell(IndexedFrame f, int col, int row)
    {
        byte c = f[col * 8, row * 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if (f[col * 8 + x, row * 8 + y] != c)
                    return null;
        return c;
    }

    [Fact]
    public void The_title_window_holds_the_name_with_its_s_and_the_list_words()
    {
        var f = Draw(List(memorize: false, prompt: "CHOOSE SPELL: "));
        for (int col = 1; col <= 10; col++)
            Assert.Equal((byte)11, Cell(f, col, 1));        // "RHIANNON'S" in the name colour (START 0x1816, flag 1)
        Assert.Equal((byte)0, Cell(f, 11, 1));
        Assert.Equal((byte)10, Cell(f, 12, 1));             // "SPELLS …" at the name's length + 4, green
        foreach (int row in new[] { 0, 2, 23 })
            Assert.Null(Cell(f, 5, row));                   // braid rows (03F1:02E8 one cell outside each window)
        Assert.Null(Cell(f, 0, 10));
        Assert.Null(Cell(f, 39, 10));
    }

    [Fact]
    public void Rows_have_no_cursor_and_the_prompt_leads_row_24()
    {
        var f = Draw(List(memorize: false, prompt: "CHOOSE SPELL: "));
        Assert.Equal((byte)13, Cell(f, 1, 5));              // the heading in the label colour
        // the entry rule puts the highlight on the window's last row when row 0 is a heading: here the list's last (13)
        Assert.Equal((byte)15, Cell(f, 3, 5 + 13));
        Assert.Equal((byte)10, Cell(f, 3, 5 + 12));
        for (int col = 0; col < 14; col++)
            Assert.Equal((byte)13, Cell(f, col, 24));       // "CHOOSE SPELL: " in 13 (the solid font fills its spaces too)
        Assert.Equal((byte)15, Cell(f, 14, 24));            // MEMORIZE, the first word, lit
        Assert.Equal((byte)15, Cell(f, 23, 24));            // EXIT's hotkey letter
        Assert.Equal((byte)10, Cell(f, 24, 24));
    }

    [Fact]
    public void A_list_with_no_prompt_starts_its_words_at_column_1()
    {
        // View's Spells and the pending lists: the caller's word is empty, so the line is " Next Exit" / " Exit".
        var f = Draw(List(memorize: false, prompt: null));
        Assert.Equal((byte)0, Cell(f, 0, 24));
        Assert.Equal((byte)15, Cell(f, 1, 24));
    }

    [Fact]
    public void Memorize_frames_its_window_to_row_16_and_shows_the_slot_lines_in_the_text_box()
    {
        var box = new List<WindowLine>
        {
            new(1, 18, "RHIANNON", 11), new(1, 19, "CAN MEMORIZE:"), new(1, 20, "MAGIC-USER SPELLS:"), new(20, 20, "4"),
        };
        var f = Draw(List(memorize: true, prompt: "CHOOSE SPELL: ", box));
        Assert.Null(Cell(f, 5, 16));                        // the list window's bottom frame / the text box's top
        Assert.Equal((byte)15, Cell(f, 3, 15));             // the window ends on row 15 (11 rows): the highlight on its last row
        Assert.Equal((byte)11, Cell(f, 1, 18));
        Assert.Equal((byte)10, Cell(f, 1, 19));
        Assert.Equal((byte)10, Cell(f, 20, 20));
        Assert.Equal((byte)0, Cell(f, 21, 20));
    }

    [Fact]
    public void A_page_left_under_something_shows_no_highlight_and_the_pad_gets_its_hint_on_row_23()
    {
        var r = List(memorize: false, prompt: "CHOOSE SPELL: ");
        var f = Draw(r, highlight: false);
        for (int row = 6; row <= 18; row++)
            Assert.NotEqual((byte)15, Cell(f, 3, row));
        Assert.Null(Cell(f, 5, 23));                        // the keyboard: the frame row stays
        var g = Draw(r, mouse: true);
        Assert.NotNull(Cell(g, 5, 23));                     // the mouse: the device hint over it
    }

    [Fact]
    public void Slot_lines_follow_the_program_columns_and_skip_a_class_with_no_slot()
    {
        // 0x17DD5: per class with any slot byte, its line at column 1 from row 20, the free slots at 20, 23, 26 as Str
        // gives them (no padding), blank where the slot byte is 0.
        var who = new EclCharacter(new byte[EclCharacter.RecordSize]);
        who.Name = "RHIANNON";
        who[CampSpells.SlotBase + 3 + 1] = 4; // magic-user level 1
        who[CampSpells.SlotBase + 3 + 2] = 2;
        var box = SpellLists.SlotBox(who, new GameText(), (level, cls) => level == 1 ? 3 : 2);
        Assert.Equal((1, 18), (box[0].Column, box[0].Row));
        Assert.Equal((1, 19), (box[1].Column, box[1].Row));
        Assert.Equal(20, box[2].Row);                       // the only class with slots, on row 20
        Assert.True(box[2].Text.Length >= 18);              // right-aligned in 18 as the cleric line's own spaces do
        Assert.Contains(box, l => (l.Column, l.Row, l.Text) == (20, 20, "3"));
        Assert.Contains(box, l => (l.Column, l.Row, l.Text) == (23, 20, "2"));
        Assert.DoesNotContain(box, l => l.Column == 26);    // level 3: slot byte 0, blank
        Assert.DoesNotContain(box, l => l.Row == 21);
    }
}
