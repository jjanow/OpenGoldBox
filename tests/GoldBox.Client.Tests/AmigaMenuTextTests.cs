using GoldBox.Client.Play;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// s43: the Amiga program draws its menu, item, coin, gender and race words as the bytes are (DOS upper-cases them),
/// and its menu routine gives every capital and digit the hotkey pen (hunk 19 +0x4FF6–0x5014). The Client learns the
/// words' spelling from the program's menu lines (<see cref="ClientResources.MenuLines"/>), the same bytes in both
/// programs. Static <see cref="AmigaView.On"/> is not touched here (tests run in parallel): the pure helpers are tested.
/// </summary>
public class AmigaMenuTextTests
{
    [Fact]
    public void A_tilde_line_keeps_one_capital_per_choice()
    {
        Assert.Equal("Alpha Beta", AmigaView.TildeCase("~ALPHA ~BETA"));
        Assert.Equal("Foo Bar", AmigaView.TildeCase("~Foo ~Bar"));
        Assert.Equal("Up and away", AmigaView.TildeCase("~UP AND AWAY"));
        Assert.Equal("GAMMA", AmigaView.HmenuCase("GAMMA"));   // off the Amiga pages unchanged
    }

    [Fact]
    public void Menu_words_take_the_spelling_of_the_programs_lines()
    {
        var spelling = AmigaView.Spelling(["Foo   baR Qux", "Lorem Ipsum Dolor", "~ALPHA ~BETA", null, "ALL CAPS"]);
        Assert.Equal("baR", AmigaView.MenuCase("BAR", spelling));
        Assert.Equal("Lorem Ipsum", AmigaView.MenuCase("LOREM IPSUM", spelling));
        Assert.Equal("Alpha", AmigaView.MenuCase("ALPHA", spelling));
        Assert.Equal("GAMMA", AmigaView.MenuCase("GAMMA", spelling));   // a word no line holds stays
        Assert.False(spelling.ContainsKey("ALL"));                      // upper-case words teach nothing
        Assert.Equal("LOREM", AmigaView.MenuCase("LOREM"));             // AmigaView.On is off in tests
    }

    [Fact]
    public void Capitals_and_digits_take_the_hotkey_pen()
    {
        Assert.Equal("RY1", string.Concat("daRk-Y1 x".Where(AmigaView.IsHotkeyCharacter)));
        Assert.False(AmigaView.IsHotkeyCharacter('~'));
    }

    [Fact]
    public void Key_waits_take_the_amiga_callers_pens_and_page_prompt()
    {
        var pens = new Dictionary<string, int> { [PorAmigaText.KeyPagePen] = 18, [PorAmigaText.KeyDamagePen] = 28 };
        var strings = new Dictionary<string, string> { [PorAmigaText.PagePromptKey] = "PAGE" };
        var page = new KeyRequest { PagePrompt = true };
        var damage = new KeyRequest("LINE") { AmigaPen = PorAmigaText.KeyDamagePen, Original = "line" };
        Assert.Equal(18, AmigaView.KeyPen(page, true, pens));
        Assert.Equal(28, AmigaView.KeyPen(damage, true, pens));
        Assert.Null(AmigaView.KeyPen(new KeyRequest("X"), true, pens));          // the DOS colour's pen
        Assert.Null(AmigaView.KeyPen(damage with { AsMenu = true }, true, pens)); // menu-style lines keep theirs
        Assert.Null(AmigaView.KeyPen(page, false, pens));                        // Original DOS / Best
        Assert.Equal("PAGE", AmigaView.KeyText(page, true, strings));
        Assert.Equal("line", AmigaView.KeyText(damage, true, strings));
        Assert.Null(AmigaView.KeyText(damage, false, strings));
    }

    [Fact]
    public void Creation_lists_start_on_row_2_without_their_label()
    {
        Assert.Equal(2, new PickRequest(["a"], ["x"], 0).FirstRow);
        Assert.Equal(3, new PickRequest(["a"], ["x"], 0) { Title = "t" }.FirstRow);
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;

    /// <summary>
    /// Every DOS menu line the Client learns from is in the Amiga <c>program</c> byte for byte (a C string there), but
    /// the Amiga's own wordings (rest-time menu, the Alter pictures' and monsters' lines, Add's line), which the Client
    /// does not take for the Amiga; and the spelling the Client learns reaches the words it draws.
    /// </summary>
    [Fact]
    public void The_dos_menu_lines_are_the_amiga_programs()
    {
        if (Env("GOLDBOX_POR_DIR") is not { } dos || Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        var program = new PorGameDirectory(dos).Program()!;
        var bytes = File.ReadAllBytes(PorAmigaArt.Locate(amiga).Program);
        string[] amigaOwn = ["Msg.RestMenu", "Msg.MonstersOn", "Msg.MonstersOff", "Msg.PortraitsOn", "Msg.PortraitsOff", "PartyMsg.AddExit"];
        var missing = new List<string>();
        foreach (var (key, text) in program.Texts.Where(kv => ClientResources.MenuKeyPrefixes.Any(p => kv.Key.StartsWith(p, StringComparison.Ordinal))))
        {
            byte[] c = [.. System.Text.Encoding.Latin1.GetBytes(text), 0];
            if (bytes.AsSpan().IndexOf(c) < 0)
                missing.Add(key);
        }
        Assert.Equal(amigaOwn.Order(), missing.Order());

        var module = PorModule.Open(PorModuleCache.Ensure(dos, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-amiga-cache"), amiga));
        var spelling = AmigaView.Spelling(ClientResources.Load(module).MenuLines);
        foreach (var w in new[] { "ENCAMP", "BUY", "APPRAISE", "COMBAT", "RENAME", "HALVE", "MOVE", "ITEMS" })
        {
            Assert.True(spelling.TryGetValue(w, out var s), w);
            Assert.Equal(w, s!.ToUpperInvariant());
            Assert.NotEqual(w, s);
        }
        // The Amiga's own strings and pens reached the module.
        Assert.Equal(11, module.AmigaStrings()!.Keys.Count(k => k.StartsWith(PorAmigaText.PartyMenuKey, StringComparison.Ordinal)));
        Assert.Equal(18, module.AmigaCallPens()![PorAmigaText.KeyPagePen]);
        Assert.Equal(28, module.AmigaCallPens()![PorAmigaText.HmenuPen]);
    }
}
