using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// s45: the Amiga draws a fixed-cell string (the string routine, hunk 25 +0xC3C) whole, pass 1 over the whole string in
/// JAM2 then pass 2, and the text window's print (hunk 25 +0x10EC) glyph by glyph through the glyph routine (+0xA4E,
/// both passes of one character before the next). <see cref="TextWindow"/> keeps which draw made each cell, and
/// <see cref="Chrome.TextRuns"/> repeats them in order.
/// </summary>
public class AmigaTextRunTests
{
    [Fact]
    public void Put_records_one_whole_run_and_print_one_run_per_glyph_in_draw_order()
    {
        var w = new TextWindow(1, 17, 38, 22);
        w.Print("ab c", colour: 10);
        w.Put(18, 1, "Lorem 4 ipsum", 10);
        w.Put(18, 3, "XY", 11);          // over the middle of the first string: it keeps its two ends
        var runs = w.Runs(17, 22);
        Assert.Equal(new[]
        {
            new TextRun(1, 17, "A", 10, false), new TextRun(2, 17, "B", 10, false), new TextRun(3, 17, " ", 10, false),
            new TextRun(4, 17, "C", 10, false),
            new TextRun(1, 18, "Lo", 10, true), new TextRun(5, 18, "m 4 ipsum", 10, true),
            new TextRun(3, 18, "XY", 11, true),
        }, runs);
        w.ClearRect(1, 18, 38, 18);
        Assert.Equal(4, w.Runs(17, 22).Count);
        // A full-screen window keeps the strings whole and marks the printed text as glyphs, in draw order.
        w.Put(19, 2, "Ipsum", 14);
        Assert.Equal(new[] { new WindowLine(1, 17, "AB C", 10) { Glyphs = true }, new WindowLine(2, 19, "Ipsum", 14) }, w.WindowLines());
    }

    [Fact]
    public void A_whole_string_keeps_each_pass_2_pixel_a_printed_glyph_loses_it_to_the_next_glyph()
    {
        var font = Font();
        var f = new IndexedFrame(64, 16);
        var w = new TextWindow(0, 0, 7, 1);
        w.Put(0, 0, "AB", 10);
        Chrome.TextRuns(f, font, w.Runs(0, 1));
        // 'A''s right pixel (x 7) spills to x 8 in pass 2, after 'B''s pass 1 drew its left pixel there.
        Assert.Equal((120, 121), (f[7, 2], f[8, 2]));
        f.Clear(0);
        w.Clear();
        w.Print("AB", colour: 10);
        Chrome.TextRuns(f, font, w.Runs(0, 1));
        Assert.Equal((120, 120), (f[7, 2], f[8, 2]));   // 'B''s pass 1 came after 'A''s pass 2: one pixel lost
    }

    [Fact]
    public void A_later_string_clears_its_cells_first_jam2_a_printed_glyph_does_not()
    {
        var font = Font();
        var f = new IndexedFrame(64, 16);
        var w = new TextWindow(0, 0, 7, 1);
        w.Put(0, 0, "A", 10);
        w.Put(0, 1, "C", 10);           // a string right after: its pass 1 blanks x 8, where 'A''s pass 2 fell
        Chrome.TextRuns(f, font, w.Runs(0, 1));
        Assert.Equal((120, 0), (f[7, 2], f[8, 2]));
        f.Clear(0);
        w.Clear();
        w.Put(0, 0, "A", 10);
        w.MoveTo(1, 0);
        w.Print("C", colour: 10);       // a printed glyph (JAM1) keeps the pass-2 pixel under its blank bits
        Chrome.TextRuns(f, font, w.Runs(0, 1));
        Assert.Equal(121, f[8, 2]);
    }

    [Fact]
    public void Pens_16_to_31_and_the_dos_font_draw_through_the_same_runs()
    {
        var font = Font();
        var f = new IndexedFrame(64, 16);
        var w = new TextWindow(0, 0, 7, 1);
        w.Put(0, 0, "AB", 28);
        Chrome.TextRuns(f, font, w.Runs(0, 1));
        Assert.Equal((50 + 12, 50 + 13), (f[7, 2], f[8, 2]));   // pen 28 = interface base + 12, its pass 2 pen 29
        // Without the Amiga face: one pass in the font's own glyphs, an Amiga pen in white.
        var own = new byte[177 * 8];
        own[1 * 8 + 2] = 0x01;          // own glyph 1 ('A')
        own[2 * 8 + 2] = 0x80;          // own glyph 2 ('B')
        f.Clear(0);
        Chrome.TextRuns(f, new TextFont(own), w.Runs(0, 1));
        Assert.Equal((15, 15, 0), (f[7, 2], f[8, 2], f[9, 2]));
    }

    /// <summary>
    /// The camp's Speed line (Amiga hunk 1 +0x4C56–0x4D10): "Game Speed = ", the number and the range text through the
    /// string routine at (1, 18) in pen 28; the menu " Faster" / " Slower" / " Exit" with "Game Speed:" (pens 26 / 28 /
    /// 16). The Amiga's strings are the DOS program's byte for byte, and DOS colour 10 is pen 28: the Client's line
    /// (<see cref="CampScreens.Speed"/>) needs only the program's case and the whole-string draw.
    /// </summary>
    [Fact]
    public void The_speed_line_is_the_dos_programs_and_one_string_in_pen_28()
    {
        if (Env("GOLDBOX_POR_DIR") is not { } dos || Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var program = new PorGameDirectory(dos).Program()!;
        var bytes = File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program);
        foreach (var key in new[] { "Msg.GameSpeedIs", "Msg.SpeedRange", "Msg.GameSpeedPrompt", "Msg.Faster", "Msg.Slower", "Msg.SpeedExit" })
        {
            var text = program.Text(key);
            Assert.False(string.IsNullOrEmpty(text), key);
            byte[] c = [.. System.Text.Encoding.Latin1.GetBytes(text!), 0];
            Assert.True(bytes.AsSpan().IndexOf(c) >= 0, key + ": " + text);
        }
        var module = PorModule.Open(PorModuleCache.Ensure(dos, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-amiga-cache"), amiga));
        Assert.Equal(28, module.AmigaTextPens()![10]);
        var w = new TextWindow();
        string line = program.Text("Msg.GameSpeedIs") + "4" + program.Text("Msg.SpeedRange");
        w.Put(18, 1, line, 10);
        Assert.Equal(new[] { new TextRun(1, 18, line, 10, true) }, w.Runs(17, 22));
        Assert.NotEqual(line.ToUpperInvariant(), line);
    }

    /// <summary>Face glyphs: 'A' a right pixel on row 2, 'B' a left pixel on row 2, 'C' nothing; DOS colour c is pen
    /// 100 + 2c (10 → 120), interface pen p is 50 + p − 16.</summary>
    private static TextFont Font()
    {
        var glyphs = new byte[256 * 8];
        glyphs['A' * 8 + 2] = 0x01;
        glyphs['B' * 8 + 2] = 0x80;
        var pens = Enumerable.Range(0, 16).Select(c => (byte)(100 + 2 * c)).ToArray();
        return new TextFont(new byte[177 * 8])
        {
            Face = new TextFont.TwoPassFace(glyphs, pens, [.. pens.Select(p => (byte)(p + 1))]) { InterfaceBase = 50 },
        };
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;
}
