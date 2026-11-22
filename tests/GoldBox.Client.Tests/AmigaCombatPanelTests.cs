using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Tests;

/// <summary>
/// s46: the fight screen's message panel and item-effect box draw as the Amiga program does (hunk 13 +0x3E4A, +0x4970,
/// +0x49FE): the names and fixed lines whole through the string routine, the printed text glyph by glyph
/// (<see cref="Chrome.TextRuns"/>); and the Amiga program has no Alt-X (hunk 1 +0x1C42–0x1DF4, +0x127E). Static
/// <see cref="AmigaView.On"/> is not touched (tests run in parallel): the pure helper is tested for the Alt keys.
/// </summary>
public class AmigaCombatPanelTests
{
    [Fact]
    public void The_message_panel_draws_a_name_whole_and_the_printed_text_glyph_by_glyph()
    {
        var (screen, view, _) = Screen();
        view.Panel.Put(12, CombatView.PanelLeft, "AB", 10);       // a name (the name routine: the string routine)
        view.Panel.MoveTo(CombatView.PanelLeft, 13);
        view.Panel.Print("AB", colour: 10);                         // printed text
        var f = new IndexedFrame(320, 200);
        screen.Draw(f, new ActionInput(InputBindings.Defaults()));
        int x = CombatView.PanelLeft * 8 + 7;
        // 'A''s pass-2 pixel (x + 1) stays over 'B''s pass 1 when drawn whole, and is lost when printed.
        Assert.Equal((120, 121), (f[x, 12 * 8 + 2], f[x + 1, 12 * 8 + 2]));
        Assert.Equal((120, 120), (f[x, 13 * 8 + 2], f[x + 1, 13 * 8 + 2]));
    }

    [Fact]
    public void The_item_effect_box_draws_its_name_whole_and_its_text_glyph_by_glyph()
    {
        var (screen, view, _) = Screen();
        var w = new TextWindow();
        w.Put(TextWindow.Top + 1, TextWindow.Left, "AB", 10);
        w.PrintIn(TextWindow.Left, TextWindow.Top + 2, TextWindow.Right, TextWindow.Bottom, "AB", true, 10);
        view.ItemEffectText = w;
        var f = new IndexedFrame(320, 200);
        screen.Draw(f, new ActionInput(InputBindings.Defaults()));
        int x = TextWindow.Left * 8 + 7;
        Assert.Equal((120, 121), (f[x, (TextWindow.Top + 1) * 8 + 2], f[x + 1, (TextWindow.Top + 1) * 8 + 2]));
        Assert.Equal((120, 120), (f[x, (TextWindow.Top + 2) * 8 + 2], f[x + 1, (TextWindow.Top + 2) * 8 + 2]));
    }

    [Fact]
    public void The_amiga_program_offers_no_alt_x()
    {
        var dos = CombatScreen.AltKeys(amiga: false);
        Assert.Equal(["MAGIC", "ALL-QUICK", "ALT-X"], dos.Words);
        Assert.Contains(CombatChooser.AnswerAltX, dos.Answers);
        var amiga = CombatScreen.AltKeys(amiga: true);
        Assert.Equal(["MAGIC", "ALL-QUICK"], amiga.Words);
        Assert.Equal([CombatChooser.AnswerQuickMagic, CombatChooser.AnswerAllQuick], amiga.Answers);
    }

    private static (CombatScreen, CombatView, TextFont) Screen()
    {
        var font = Font();
        var view = new CombatView();
        var screen = new CombatScreen(view, new CombatArt(), font, new ScriptRunner(), new CombatText(), _ => null);
        return (screen, view, font);
    }

    /// <summary>A face whose 'A' sets its rightmost pixel and 'B' its leftmost on row 2 (pens 100 + 2c, pass 2 + 1).</summary>
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
}
