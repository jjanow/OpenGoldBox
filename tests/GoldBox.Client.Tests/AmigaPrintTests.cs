using GoldBox.Client.Play;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// s46: the Amiga's print (hunk 25 +0x10EC–0x12A6) waits after every word, and its NUL-only last word can move the
/// cursor; the yes/no question's pens read from <c>program</c>. Static <see cref="AmigaView.On"/> is not touched (tests
/// run in parallel): the windows take <see cref="TextWindow.Amiga"/>.
/// </summary>
public class AmigaPrintTests
{
    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;

    /// <summary>
    /// hunk 25 +0x1286–0x1292: Delay(speed × 2 ticks) after each word, the last word (the NUL alone after a delimiter or
    /// space) included; nothing per character. DOS (START 0x777B) waits per character instead.
    /// </summary>
    [Fact]
    public void The_amiga_print_waits_after_every_word_and_the_nul()
    {
        int words = 0, chars = 0;
        var amiga = new TextWindow { Amiga = true, WordTyped = () => words++, Typed = () => chars++ };
        amiga.Print("Hello. World", true);
        Assert.Equal(3, words);   // "Hello." / " " / "World" + NUL
        Assert.Equal(0, chars);
        words = 0;
        amiga.Print("Hello.", true);
        Assert.Equal(2, words);   // "Hello." / the NUL alone
        Assert.Equal(18, amiga.Printed);   // 12 cells, then 6
        var dos = new TextWindow { Amiga = false, WordTyped = () => words++, Typed = () => chars++ };
        words = 0;
        dos.Print("Hello. World", true);
        Assert.Equal(0, words);
        Assert.Equal(12, chars);
        Assert.Equal(TimeSpan.FromMilliseconds(120), TextWindow.AmigaWordDelay(3));
    }

    /// <summary>The panel's weapon name on the Amiga (hunk 13 +0x3F3E: the print into (23, 7)–(38, 9)) shows word by
    /// word: the cells after each wait, the name's trailing space making the last wait the NUL's.</summary>
    [Fact]
    public void The_amiga_weapon_name_shows_word_by_word()
    {
        Assert.Equal([5, 11, 11], CombatChooser.WeaponWords("LONG SWORD "));
        Assert.Equal(11, CombatChooser.WeaponWords("LONG SWORD ")[^1]);
    }

    /// <summary>
    /// DAMAGE (hunk 4 +0x2378–0x239E) sets the cursor column to 38 and prints "  name …" through the print. A line whose
    /// last character lands on column 38 leaves the cursor at 39, so its NUL-only last word (count 1) passes right + 1
    /// and moves the cursor to the next row, drawing nothing; the next line's first space then prints at (38, that row)
    /// and its second wraps: a blank row between them (FS-UAE s45). DOS moves a cursor past the edge to the next row
    /// once, and its next line's column 38 then wraps at once: no blank row.
    /// </summary>
    [Fact]
    public void A_damage_line_ending_on_column_38_leaves_a_blank_row_on_the_amiga()
    {
        // The program's words (hunk 5 +0x3C / +0x40 / +0x4E): "  ABCDE is hit FOR 12 points of Damage." from column 1
        // after the wrap fills columns 1–38.
        const string full = "  ABCDE is hit FOR 12 points of Damage.", next = "  FGH is hit FOR 3 points of Damage.";
        var amiga = new TextWindow { Amiga = true };
        Damage(amiga, full);
        Assert.Equal(19, amiga.Row);
        Assert.Equal(1, amiga.Column);
        Damage(amiga, next);
        // (AmigaView.On is off in tests, so the cells hold DOS's upper-case glyphs.)
        Assert.Equal(["", " ABCDE IS HIT FOR 12 POINTS OF DAMAGE.", "", " FGH IS HIT FOR 3 POINTS OF DAMAGE."], amiga.Lines.Take(4));
        // A shorter line: no blank row.
        var shorter = new TextWindow { Amiga = true };
        Damage(shorter, "  ABCD is hit FOR 12 points of Damage.");
        Damage(shorter, next);
        Assert.Equal(["", " ABCD IS HIT FOR 12 POINTS OF DAMAGE.", " FGH IS HIT FOR 3 POINTS OF DAMAGE."], shorter.Lines.Take(3));
        // DOS: the same two lines on consecutive rows (spaces after a wrap skipped).
        var dos = new TextWindow { Amiga = false };
        Damage(dos, full);
        Damage(dos, next);
        Assert.Equal(["", "ABCDE IS HIT FOR 12 POINTS OF DAMAGE.", "FGH IS HIT FOR 3 POINTS OF DAMAGE."], dos.Lines.Take(3));
    }

    /// <summary>
    /// The print checks the bottom row for every word, the NUL's too (+0x11E8): a DAMAGE line ending on (38, 22) shows
    /// the page prompt and blanks the box inside its own print, so the next line goes on at row 17 (after its space at
    /// (38, 17)) without DAMAGE's own press-enter.
    /// </summary>
    [Fact]
    public void A_damage_line_ending_on_the_last_cell_pages_inside_its_print()
    {
        int pauses = 0;
        var amiga = new TextWindow { Amiga = true, Pause = () => pauses++ };
        amiga.MoveTo(1, 21);
        amiga.Print("x", false);   // the cursor on row 21
        Damage(amiga, "  ABCDE is hit FOR 12 points of Damage.");
        Assert.Equal(1, pauses);
        Assert.Equal((1, 17), (amiga.Column, amiga.Row));
        Assert.All(amiga.Lines, l => Assert.Equal("", l));
        Damage(amiga, "  FGH is hit FOR 3 points of Damage.");
        Assert.Equal(1, pauses);
        Assert.Equal(["", " FGH IS HIT FOR 3 POINTS OF DAMAGE."], amiga.Lines.Take(2));
    }

    // DAMAGE's caller (hunk 4 +0x234E–0x239E, DOS 0x959F): below row 22 back to row 17 (the press-enter, not modelled
    // here), the column to 38, the line printed in the box (1, 17)–(38, 22).
    private static void Damage(TextWindow w, string line)
    {
        if (w.Row > TextWindow.Bottom)
            w.MoveTo(w.Column, TextWindow.Top);
        w.MoveTo(TextWindow.Right, w.Row);
        w.Print(line, false, 15);
    }

    /// <summary>
    /// The yes/no question (hunk 19 +0x58C8: text, prompt, other letters, capitals, passed to the menu routine as they
    /// came). Where DOS's 0x2D9A0 callers pass prompt colour 14 the Amiga's pass pen 24 (red; the text pens give 14 the
    /// yellow 18), read at "Memorize These Spells?" (hunk 1 +0x3E9C); "pay for cure" (hunk 25 +0x3A72) passes the
    /// letters' pens swapped (16 / 28), as DOS 0x46CE passes 15 / 10.
    /// </summary>
    [Fact]
    public void The_yes_no_pens_come_from_the_amiga_callers()
    {
        if (Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var bytes = File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program);
        var pens = PorAmigaText.ReadCalls(bytes)!.Pens;
        Assert.Equal(24, pens[PorAmigaText.YesNoWarnPen]);
        Assert.Equal(16, pens[PorAmigaText.YesNoSwappedWordsPen]);
        Assert.Equal(28, pens[PorAmigaText.YesNoSwappedCapitalsPen]);
        Assert.Equal(26, PorAmigaText.Read(bytes)!.Pens[13]);   // DOS 13's pen: every other yes/no prompt
    }

    /// <summary>
    /// s47: the timed row-24 message (hunk 25 +0xE10) is passed pen 24 where DOS passes 14 ("Not Here", hunk 19
    /// +0x2864); the loot's take passes the list routine "Items:" with no space (hunk 19 +0x862 → hunk 20 +0xE6), Buy
    /// "Items: " as DOS.
    /// </summary>
    [Fact]
    public void The_timed_message_pen_and_the_take_prompt_come_from_the_program()
    {
        if (Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var calls = PorAmigaText.ReadCalls(File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program))!;
        Assert.Equal(24, calls.Pens[PorAmigaText.TimedWarnPen]);
        Assert.Equal("Items:", calls.Strings[PorAmigaText.LootItemsPromptKey]);
    }

    /// <summary>
    /// s47: every Amiga timed pause read waits Delay(speed · 5 ticks) (hunk 25 +0xD7A: 100 ms per step), DOS's
    /// PauseOnly 225 ms per step; speed 0 none.
    /// </summary>
    [Fact]
    public void Timed_messages_wait_100_ms_per_step_on_the_amiga()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(900), AmigaView.MessagePause(9, amiga: true));
        Assert.Equal(TimeSpan.FromMilliseconds(675), AmigaView.MessagePause(3, amiga: false));
        Assert.Equal(TimeSpan.Zero, AmigaView.MessagePause(0, amiga: true));
    }

    /// <summary>
    /// s47: the Amiga's number input (hunk 25 +0x1092) asks again while stcd_i took no character, so an empty line is
    /// never an answer; DOS takes it (as 0).
    /// </summary>
    [Fact]
    public void The_amiga_number_input_asks_again_for_an_empty_line()
    {
        var answers = new Queue<object?>(["", "", "12"]);
        int asked = 0;
        object? Ask(PlayerRequest _)
        {
            asked++;
            return answers.Dequeue();
        }
        var r = new TextRequest("", true, 5);
        Assert.Equal("12", AmigaView.Number(Ask, r, amiga: true));
        Assert.Equal(3, asked);
        answers = new Queue<object?>([""]);
        Assert.Equal("", AmigaView.Number(Ask, r, amiga: false));
    }
}
