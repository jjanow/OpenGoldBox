using GoldBox.Core.Game;
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
        Assert.Equal((28, 16), (module.AmigaCallPens()![PorAmigaText.HmenuWordsPen], module.AmigaCallPens()![PorAmigaText.HmenuCapitalsPen]));
        // s44: the camp's Amiga lines reached the module, and they stay out of the spelling table.
        Assert.True(module.AmigaStrings()!.ContainsKey(PorAmigaText.RestMenuKey));
        Assert.DoesNotContain(module.AmigaStrings()![PorAmigaText.MonstersOnKey], ClientResources.Load(module).MenuLines);
        var (items, keys, columns, roles) = CampScreens.RestUi.RestMenu(program.Text("Msg.RestMenu")!, module.AmigaStrings()![PorAmigaText.RestMenuKey]);
        Assert.Equal(7, items.Count);
        Assert.Null(columns);
        Assert.Equal(CampScreens.RestUi.EntryRoles, roles);
        Assert.Equal(7, keys.Distinct().Count());
        Assert.DoesNotContain('Y', keys);   // the Amiga's days word is hot on its first letter
        Assert.All(items, w => Assert.NotEqual(w.ToUpperInvariant(), w));
    }

    /// <summary>
    /// s44: the rest-time menu's entries keep the program's order on both machines (DOS 0x244A5, Amiga hunk 2 +0x29A), so
    /// the Amiga line's roles go by position: its second entry (days) is hot on its capital, its fifth and sixth (add,
    /// take off) on theirs, where DOS has Y, I and D; Up / Down still answer the fifth and sixth.
    /// </summary>
    [Fact]
    public void The_amiga_rest_menu_maps_its_entries_by_position()
    {
        var (items, keys, columns, roles) = CampScreens.RestUi.RestMenu("Rr   aYy Hh Mm   Ii Dd   Ee", "Rr Dd Hh Mm Aa Ss Ee");
        Assert.Equal(["Rr", "Dd", "Hh", "Mm", "Aa", "Ss", "Ee"], items);
        Assert.Equal("RDHMASE", keys);
        Assert.Null(columns);
        Assert.Equal(RestTimeMenu.Key.Days, roles[keys.IndexOf('D')]);
        Assert.Equal(RestTimeMenu.Key.Inc, roles[keys.IndexOf('A')]);
        Assert.Equal(RestTimeMenu.Key.Dec, roles[keys.IndexOf('S')]);
        // DOS (no Amiga line): roles by hotkey, the line's own spaces kept.
        var (dItems, dKeys, dColumns, dRoles) = CampScreens.RestUi.RestMenu("Rr   aYy Hh Mm   Ii Dd   Ee", null);
        Assert.Equal("RYHMIDE", dKeys);
        Assert.Equal(CampScreens.RestUi.EntryRoles, dRoles);
        Assert.NotNull(dColumns);
        Assert.Equal(7, dItems.Count);
        // A line of another length falls back to DOS's.
        Assert.Equal("RYHMIDE", CampScreens.RestUi.RestMenu("Rr   aYy Hh Mm   Ii Dd   Ee", "Rr Dd Ee").Keys);
    }

    /// <summary>
    /// s44: the Amiga's Pics line joins each on / off pair with 0xFF (one entry, the byte drawn blank) and puts one space
    /// between entries; DOS's words carry two spaces, which its menu routine draws as they are.
    /// </summary>
    [Fact]
    public void The_pics_line_keeps_each_machines_spacing()
    {
        string amiga = "Aaa" + PorAmigaText.JoinedSpace + "on Bbb" + PorAmigaText.JoinedSpace + "off Exit";
        var (items, keys, columns) = CampScreens.PicsLine("Aaa on  Bbb off  Exit", amiga);
        Assert.Equal(["Aaa" + PorAmigaText.JoinedSpace + "on", "Bbb" + PorAmigaText.JoinedSpace + "off", "Exit"], items);
        Assert.Equal("ABE", keys);
        Assert.Null(columns);
        var (dItems, dKeys, dColumns) = CampScreens.PicsLine("Aaa on  Bbb off  Exit", null);
        Assert.Equal(["AAA ON", "BBB OFF", "EXIT"], dItems);
        Assert.Equal("ABE", dKeys);
        Assert.Equal([0, 8, 17], dColumns);
        Assert.Equal('A', AmigaView.ParseLine("aAa").Hotkeys[0]);
        Assert.Equal(1, AmigaView.ParseLine("Aaa" + PorAmigaText.JoinedSpace + "on").Items.Count);
    }

    [Fact]
    public void Amiga_timed_messages_wait_100_ms_per_speed_step()
    {
        Assert.Equal(TimeSpan.Zero, AmigaView.TimedPause(0));
        Assert.Equal(TimeSpan.FromMilliseconds(300), AmigaView.TimedPause(3));
    }

    [Fact]
    public void Program_texts_and_text_window_cells_keep_their_case_on_the_Amiga()
    {
        // The Amiga's text routines draw the bytes (string routine hunk 25 +0xC3C, the text window's print +0x10EC);
        // DOS upper-cases (glyph UpCase(c) mod 64, print START 0x7737).
        Assert.Equal("Lorem ipsum dolor...", GameText.Cased("Lorem ipsum dolor...", amiga: true));
        Assert.Equal("LOREM IPSUM DOLOR...", GameText.Cased("Lorem ipsum dolor...", amiga: false));
        Assert.Equal('e', TextWindow.Glyph('e', amiga: true));
        Assert.Equal('E', TextWindow.Glyph('e', amiga: false));
        var w = new TextWindow(1, 17, 38, 22);
        w.Print("Lorem ipsum", colour: 10);   // AmigaView.On is off in tests: DOS's cells
        Assert.Equal("LOREM IPSUM", w.Lines[0]);
    }

    [Fact]
    public void Creation_takes_one_character_less_and_its_save_upper_cases_the_name_on_the_Amiga()
    {
        // hunk 10 +0x2114: the requester's MaxChars 15 (14 characters); DOS 15. strncpy at +0x212E keeps the case, but
        // the character save at the end (hunk 16 +0x49C) writes the name back upper case (s46, FS-UAE: "ldvorldv" saved
        // as "LDVORLDV").
        Assert.Equal(14, AmigaView.NameLength(amiga: true));
        Assert.Equal(15, AmigaView.NameLength(amiga: false));
        Assert.Equal(15, AmigaView.RenameLength);      // Rename's requester (hunk 10 +0x551A: 16) keeps 15
        Assert.Equal("LOREM IPSUM", AmigaView.CreationName("Lorem Ipsum", amiga: true));   // the space kept (0xFF there)
        Assert.Equal("LOREM IPSUM", AmigaView.CreationName("Lorem Ipsum", amiga: false));
        // H10 +0xE8 drops a '.' only after a '.' or at the start; H10 +0x1B8 drops ".*,?/:;|" once per index.
        Assert.Equal("AB", AmigaView.SavedName(".a.b"));
        Assert.Equal("AB", AmigaView.SavedName("a..b"));
        Assert.Equal("A-B", AmigaView.SavedName("a-b"));
        Assert.Equal("A.", AmigaView.SavedName("a.,."));   // ',' deleted at index 1 meets only the table after it
        var c = new Core.Ecl.EclCharacter { Name = "Lorem" };
        Assert.Equal("NEW NAME FOR Lorem", AmigaView.RenamePrompt(c));   // the requester draws the bytes
    }

    [Fact]
    public void Creation_names_matching_a_developer_block_show_its_lines_on_row_24()
    {
        // s45 (hunk 10 +0x2148–0x2330): strcmp per block, in order; a name in two blocks shows both.
        var k = PorAmigaText.DeveloperKey;
        var strings = new Dictionary<string, string>
        {
            [k + 0] = "ALPHA", [k + "0.0"] = "lorem ipsum dolor", [k + "0.1"] = "sit amet", [k + "0.2"] = "consectetur",
            [k + 1] = "BETA", [k + "1.0"] = "one",
            [k + 2] = "ALPHA", [k + "2.0"] = "again",
        };
        var alpha = AmigaView.DeveloperBlocks("ALPHA", amiga: true, strings);
        Assert.Equal(2, alpha.Count);
        Assert.Equal(["lorem ipsum dolor", "sit amet", "consectetur"], alpha[0]);
        Assert.Equal(["again"], alpha[1]);
        Assert.Equal([["one"]], AmigaView.DeveloperBlocks("BETA", amiga: true, strings));
        Assert.Empty(AmigaView.DeveloperBlocks("Beta", amiga: true, strings));    // case exact
        Assert.Empty(AmigaView.DeveloperBlocks("BETA ", amiga: true, strings));   // length exact
        Assert.Empty(AmigaView.DeveloperBlocks("BET", amiga: true, strings));
        Assert.Empty(AmigaView.DeveloperBlocks("BETA", amiga: false, strings));   // DOS: none
        Assert.Equal(TimeSpan.FromSeconds(2), AmigaView.DeveloperHold);         // Delay(100)
    }

    [Fact]
    public void The_party_killed_window_holds_three_seconds_on_DOS_only()
    {
        // DOS Delay(3000) (GAME.OVR 0x375A–0x37E4); the Amiga (hunk 16 +0x4674–0x46DC) starts song 5 and waits for the key.
        Assert.Equal(TimeSpan.FromMilliseconds(3000), AmigaView.PartyKilledHold(amiga: false));
        Assert.Null(AmigaView.PartyKilledHold(amiga: true));
    }
}
