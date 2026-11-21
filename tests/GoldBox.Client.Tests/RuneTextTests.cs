using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// PROTECT's glyph lines (GAME.OVR 0x3BC3, the ECL host's DrawGlyphs) on the explore screen, run from the game's own
/// scripts (save A, the player's files; skipped without GOLDBOX_POR_DIR): the glyphs are the text font's (8X8D1 block
/// 201) raw numbers in colour 15, in the cleared picture window after CLEARBOX, else at the text cursor in the text box.
/// </summary>
public class RuneTextTests
{
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ClientPlayer Player, PorGameDirectory Dir, ActionInput Input, ScriptRunner Runner);

    private static Rig Start()
    {
        var dir = new PorGameDirectory(TestRig.PorDir()!);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost()), game, player, dir, new ActionInput(InputBindings.Defaults()), runner);
    }

    /// <summary>Runs <paramref name="steps"/> instructions of ECL<paramref name="file"/> block <paramref name="block"/> from <paramref name="pc"/> with the Client as host.</summary>
    private static byte[] Run(Rig r, int file, int block, ushort pc, int steps)
    {
        var data = DaxFile.Open(r.Dir.Find($"ECL{file}.DAX")!).Block(block)!;
        var vm = new EclVm(r.Game.Memory, r.Player);
        vm.Memory.LoadScriptBlock(data);
        var result = vm.RunFrom(pc, steps);
        Assert.Equal(EclStopReason.Budget, result.Reason);
        return data[2..]; // the script as loaded at $9900
    }

    /// <summary>The glyphs PROTECT reads at <paramref name="a"/>: script bytes + 1 up to a byte 0 or 255.</summary>
    private static List<byte> Glyphs(byte[] script, int a)
    {
        var list = new List<byte>();
        for (int i = a - 0x9900; ; i++)
        {
            byte g = (byte)(script[i] + 1);
            if (g <= 1)
                return list;
            list.Add(g);
        }
    }

    /// <summary>The cell's pixels are glyph <paramref name="g"/> of 8X8D1 block 201 in colour 15 on black.</summary>
    private static void AssertGlyphCell(IndexedFrame f, byte[] font, int col, int row, byte g)
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                bool on = (font[g * 8 + y] & (0x80 >> x)) != 0;
                Assert.Equal(on ? 15 : 0, f[col * 8 + x, row * 8 + y]);
            }
    }

    private static byte[] FontData(Rig r) => DaxFile.Open(r.Dir.Find("8X8D1.DAX")!).Block(201)!;

    [Fact]
    public void The_parchment_runes_go_into_the_cleared_picture_window()
    {
        if (TestRig.PorDir() is null)
            return;
        var r = Start();
        r.Player.Print("A", clearFirst: true); // the PRINTCLR before them leaves the cursor in the text box (row 17)
        // ECL4 block 21 $9BD7: CLEARBOX, PROTECT, PRINTRET, PROTECT, PRINTRET, PROTECT.
        var script = Run(r, 4, 21, 0x9BD7, 6);
        var lines = new[] { Glyphs(script, 0xAF1E), Glyphs(script, 0xAF25), Glyphs(script, 0xAF34) };
        Assert.All(lines, l => Assert.NotEmpty(l));
        // Below row 13 after CLEARBOX the first line starts at column 2, row 5; PROTECT ends one row down and PRINTRET
        // goes to column 1 of the next row: rows 7 and 9.
        var expected = new List<(int, int, char)>();
        foreach (var (line, col0, row) in new[] { (lines[0], 2, 5), (lines[1], 1, 7), (lines[2], 1, 9) })
            for (int i = 0; i < line.Count; i++)
                expected.Add((col0 + i, row, TextFont.RawGlyph(line[i])));
        Assert.Equal(expected, r.Player.ViewGlyphs);
        Assert.Equal((lines[2].Count + 1, 10), (r.Player.Text.Column, r.Player.Text.Row));

        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var font = FontData(r);
        var drawn = new HashSet<(int, int)>();
        foreach (var (c, row, g) in expected)
        {
            AssertGlyphCell(f, font, c, row, (byte)(g - 0xE000));
            drawn.Add((c, row));
        }
        // The rest of cells (1, 1)–(15, 15) is blank: CLEARBOX's clear takes the view's own box too.
        for (int row = 1; row <= 15; row++)
            for (int c = 1; c <= 15; c++)
                if (!drawn.Contains((c, row)))
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                            Assert.Equal(0, f[c * 8 + x, row * 8 + y]);

        // EXIT (with DS:84E2 set) draws the window again: the runes are gone.
        r.Player.TextHome();
        Assert.Empty(r.Player.ViewGlyphs);
    }

    [Fact]
    public void The_password_runes_follow_the_text_on_its_row_in_the_text_box()
    {
        if (TestRig.PorDir() is null)
            return;
        var r = Start();
        // ECL5 block 4 $A45E: PRINTCLR (empty), PROTECT (empty line), PRINTCLR, PROTECT, PRINTRET, PRINT, PROTECT.
        var script = Run(r, 5, 4, 0xA45E, 7);
        Assert.Empty(Glyphs(script, 0xA4BE));
        var first = Glyphs(script, 0xA4AA);
        var second = Glyphs(script, 0xA4B7);
        Assert.Empty(r.Player.ViewGlyphs);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var font = FontData(r);
        // Each line of glyphs starts right after the text printed on its row (rows 17 and 19), in colour 15.
        foreach (var (line, row) in new[] { (first, 17), (second, 19) })
        {
            int start = Enumerable.Range(TextWindow.Left, TextWindow.Right).First(c => r.Player.Text.Cell(row, c).Char >= '\uE000');
            Assert.NotEqual('\0', r.Player.Text.Cell(row, start - 1).Char); // the text's last cell
            for (int i = 0; i < line.Count; i++)
            {
                Assert.Equal((TextFont.RawGlyph(line[i]), (byte)15), r.Player.Text.Cell(row, start + i));
                AssertGlyphCell(f, font, start + i, row, line[i]);
            }
        }
    }

    [Fact]
    public void Glyph_lines_below_the_text_box_land_on_rows_23_and_24_until_drawn_over()
    {
        // P, s30 (no v1.3 script does this): PROTECT draws at the cursor on any row up to 24 and column up to 39 (START
        // 0x726F); past row 22 the lines go over the box's bottom frame (row 23) and the bottom row (24).
        if (TestRig.PorDir() is null)
            return;
        var r = Start();
        r.Game.Memory.GameSpeed = 2;
        r.Player.Text.MoveTo(5, 22);
        byte[] line = [66, 67, 68];
        r.Runner.Start(() =>
        {
            r.Player.DrawGlyphs(0, line); // row 22: in the text box
            r.Player.DrawGlyphs(0, line); // row 23
            r.Player.DrawGlyphs(0, line); // row 24
            r.Player.DrawGlyphs(0, line); // row 25: not drawn
            r.Player.Delay();             // the script goes on: a DELAY, nothing on row 24
            r.Runner.Ask(new KeyRequest()); // then something that writes row 24 (a key prompt)
        });
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal((17, 26), (r.Player.Text.Column, r.Player.Text.Row)); // the column runs on: 5, 8, 11, 14, 17
        Assert.Equal([23, 24], r.Player.ScreenGlyphs.Select(g => g.Row).Distinct().Order());
        var f = new IndexedFrame(320, 200);
        r.Screen.Update(r.Input);
        r.Screen.Draw(f, r.Input);
        var font = FontData(r);
        foreach (int row in new[] { 22, 23, 24 })
            for (int i = 0; i < line.Length; i++)
                AssertGlyphCell(f, font, 5 + 3 * (row - 22) + i, row, line[i]);
        // The key prompt is drawn on row 24 (as a menu or a message would be): the glyphs there are gone; the frame
        // row keeps its own until a redraw.
        r.Runner.Answer(null);
        Assert.IsType<KeyRequest>(r.Runner.Pending);
        r.Screen.Update(r.Input);
        Assert.Equal([23], r.Player.ScreenGlyphs.Select(g => g.Row).Distinct());
        r.Runner.Answer(null);
        r.Player.CommandRedrawn();
        Assert.Empty(r.Player.ScreenGlyphs);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_long_line_in_the_cleared_window_runs_over_its_frame_into_the_party_list_side()
    {
        // P, s30: from (2, 5) a 20-glyph line fills columns 2–15, the window's frame column 16 and 17–21 beside it.
        // EXIT's redraw of the window (1, 1)–(15, 15) with its frame takes columns 0–16 back; the rest stays [?] until
        // the Client's next command redraw.
        if (TestRig.PorDir() is null)
            return;
        var r = Start();
        r.Player.Print("A", clearFirst: true);
        r.Player.ClearBox();
        r.Player.DrawGlyphs(0, [.. Enumerable.Repeat((byte)66, 20)]);
        Assert.Equal(14, r.Player.ViewGlyphs.Length);
        Assert.Equal([16, 17, 18, 19, 20, 21], r.Player.ScreenGlyphs.Select(g => g.Col).Order());
        Assert.All(r.Player.ScreenGlyphs, g => Assert.Equal(5, g.Row));
        // A line with the cursor below row 13 goes back to (2, 5) while CLEARBOX stands (PROTECT 0x3BC3), so in the window
        // no line reaches rows 14–16 (row 16, the frame row, takes a glyph only from the text-box side: never).
        r.Player.Text.MoveTo(1, 14);
        r.Player.DrawGlyphs(0, [67]);
        Assert.Contains((2, 5, TextFont.RawGlyph(67)), r.Player.ViewGlyphs);
        r.Player.TextHome(); // EXIT with DS:84E2 set: the window (1, 1)–(15, 15) and its frame drawn again
        Assert.Empty(r.Player.ViewGlyphs);
        Assert.Equal([17, 18, 19, 20, 21], r.Player.ScreenGlyphs.Select(g => g.Col).Order());
    }
}

/// <summary>A CLEARBOX blank under the view redraw (DOSBox-X s39, save A: CLEARBOX; NEWECL 20).</summary>
public class ClearBoxRedrawTests
{
    [Fact]
    public void The_view_comes_back_alone_and_its_box_stays_blank()
    {
        if (TestRig.PorDir() is null)
            return;
        var dir = new PorGameDirectory(TestRig.PorDir()!);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        var screen = new ExploreScreen(game, player, runner, new TestRig.NoHost());
        var input = new ActionInput(InputBindings.Defaults());
        var f = new IndexedFrame(320, 200);
        screen.Draw(f, input);
        Assert.Contains(Enumerable.Range(16, 8), x => f[x, 60] != 0); // the view's box frame in column 2
        player.ClearBox();
        player.SwitchRedrawn(); // 0x3FF5 → 0x2DCF4: the 3D view alone
        Assert.False(player.ViewBlanked);
        Assert.True(player.ViewBoxBlank);
        f = new IndexedFrame(320, 200);
        screen.Draw(f, input);
        for (int y = 8; y < 128; y++)
            for (int x = 8; x < 128; x++)
                if (x is < 24 or >= 112 || y is < 24 or >= 112)
                    Assert.Equal(0, f[x, y]);
        Assert.Contains(Enumerable.Range(24, 88), x => f[x, 60] != 0); // the view itself
        runner.Dispose();
    }
}

/// <summary>PROTECT on row 23 (DOSBox-X s30: a PRINTCLR, six PRINTRETs, then PROTECT).</summary>
public class RuneEdgeRowTests
{
    [Fact]
    public void A_glyph_line_on_row_23_goes_over_the_frame_and_stays_after_exit()
    {
        var memory = new EclMemory { TypesText = false };
        using var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => memory);
        runner.Start(() =>
        {
            player.Print("ROWS.", true);
            for (int i = 0; i < 6; i++)
                player.PrintReturn();
            player.DrawGlyphs(0x9E00, [0x56, 0x57, 0x6B]);
            player.TextHome(); // EXIT
        });
        for (int guard = 0; runner.Pending is not null && guard < 200; guard++)
            runner.Answer(null);
        Assert.Equal([(1, 23), (2, 23), (3, 23)], player.ScreenGlyphs.Select(g => (g.Col, g.Row)));
        Assert.Empty(player.ViewGlyphs);
        player.CommandRedrawn();
        Assert.Empty(player.ScreenGlyphs);
    }
}

/// <summary>The text window types while DS:4A00 is set (<see cref="EclMemory.TypesText"/>), whoever prints.</summary>
public class TextTypingTests
{
    /// <summary>The requests the encounter menu's "both wait" message makes, each answered in turn.</summary>
    private static List<PlayerRequest> Requests(bool typing)
    {
        var memory = new EclMemory { TypesText = typing, GameSpeed = 2 };
        using var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => memory);
        runner.Start(() => player.ShowEncounterMessage(GoldBox.Core.Game.EncounterMessage.BothSidesWait));
        var seen = new List<PlayerRequest>();
        for (int guard = 0; runner.Pending is { } p && guard < 200; guard++)
        {
            seen.Add(p);
            runner.Answer(null);
        }
        Assert.False(runner.Busy);
        return seen;
    }

    [Fact]
    public void An_encounter_message_types_while_ds_4a00_is_set_and_shows_at_once_when_not()
    {
        var chained = Requests(typing: true).OfType<DelayRequest>().Where(d => d.Chained).ToList();
        Assert.NotEmpty(chained);
        Assert.All(chained, d => Assert.Equal(TimeSpan.FromMilliseconds(20), d.Duration)); // DS:49E2 × 10 ms
        Assert.DoesNotContain(Requests(typing: false), p => p is DelayRequest { Chained: true });
    }
}

/// <summary>
/// PROTECT's glyphs under Best / Amiga (docs/re/ecl-format.md "PROTECT"; P, s38): the Amiga program draws them in its
/// rune font (disk A's <c>fonts/protect/8</c>, module <c>art/amiga/protect/0/8</c>) in pen 28, set bits only (JAM1).
/// Needs GOLDBOX_POR_DIR and GOLDBOX_POR_AMIGA_DIR with disk A's <c>fonts</c> folder; returns without them.
/// </summary>
public class AmigaRuneTests
{
    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;

    private static readonly Lazy<GoldBox.Data.Module.Por.PorModule?> Module = new(() =>
        Env("GOLDBOX_POR_DIR") is { } dos && Env("GOLDBOX_POR_AMIGA_DIR") is { } amiga
            ? GoldBox.Data.Module.Por.PorModule.Open(GoldBox.Data.Module.Por.PorModuleCache.Ensure(dos,
                Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-amiga-cache"), amiga))
            : null);

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ClientPlayer Player, PorGameDirectory Dir, ActionInput Input,
        ScriptRunner Runner, ClientArt Art, ScreenPalette Palette, DaxPicture Font);

    private static Rig? Start(PorArtChoice choice)
    {
        if (TestRig.PorDir() is null || Module.Value is not { } module
            || module.Art(GoldBox.Data.Module.Por.PorAmigaRunes.Container, GoldBox.Data.Module.Por.PorAmigaRunes.Id, PorArtChoice.Amiga)
                is not { Variant: "amiga", Frames: [var font] })
            return null;
        var dir = new PorGameDirectory(TestRig.PorDir()!);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        var palette = new ScreenPalette();
        var art = new ClientArt(module, null, palette, choice);
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), art: art), game, player, dir,
            new ActionInput(InputBindings.Defaults()), runner, art, palette, font);
    }

    /// <summary>Glyph <paramref name="g"/>'s cell: the Amiga font's set bits in its pen, the rest as <paramref name="under"/> had it.</summary>
    private static void AssertAmigaCell(Rig r, IndexedFrame f, IndexedFrame? under, int col, int row, int g)
    {
        int b = r.Palette.Base(Module.Value!.Art(GoldBox.Data.Module.Por.PorAmigaRunes.Container, GoldBox.Data.Module.Por.PorAmigaRunes.Id, PorArtChoice.Amiga)!.PaletteRgb);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                byte v = r.Font.Items[g][y * 8 + x];
                Assert.Equal(v != 0 ? b + v : under?[col * 8 + x, row * 8 + y] ?? 0, f[col * 8 + x, row * 8 + y]);
            }
    }

    [Fact]
    public void The_parchment_runes_are_drawn_in_the_amiga_rune_font_and_pen()
    {
        if (Start(PorArtChoice.Amiga) is not { } r)
            return;
        r.Player.Print("A", clearFirst: true);
        var data = DaxFile.Open(r.Dir.Find("ECL4.DAX")!).Block(21)!;
        var vm = new EclVm(r.Game.Memory, r.Player);
        vm.Memory.LoadScriptBlock(data);
        Assert.Equal(EclStopReason.Budget, vm.RunFrom(0x9BD7, 6).Reason);   // CLEARBOX and the three lines
        Assert.NotEmpty(r.Player.ViewGlyphs);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        foreach (var (c, row, ch) in r.Player.ViewGlyphs)
            AssertAmigaCell(r, f, null, c, row, TextFont.RawGlyphNumber(ch)!.Value);
        // Pen 28 is register 28 of the program's interface colours (index 12 of the PNG's palette).
        var setup = GoldBox.Data.Module.Por.PorAmigaRunes.Read(File.ReadAllBytes(
            GoldBox.Data.Module.Por.PorAmigaArt.Locate(Env("GOLDBOX_POR_AMIGA_DIR")!).Program))!;
        int b = r.Palette.Base(setup.InterfaceRgb);
        Assert.Equal(new Microsoft.Xna.Framework.Color(setup.InterfaceRgb[36], setup.InterfaceRgb[37], setup.InterfaceRgb[38]),
            r.Palette.Colours[b + 12]);
        // Glyph 79 (the third line) is one the Amiga artist redrew: its cell is not the DOS glyph.
        var dos = DaxFile.Open(r.Dir.Find("8X8D1.DAX")!).Block(201)!;
        var (c79, r79, _) = r.Player.ViewGlyphs.First(g => g.Glyph == TextFont.RawGlyph(79));
        Assert.Contains(Enumerable.Range(0, 64), i =>
            (f[c79 * 8 + i % 8, r79 * 8 + i / 8] != 0) != ((dos[79 * 8 + i / 8] & (0x80 >> (i % 8))) != 0));
        // Original DOS: the DOS font's glyph in colour 15 again.
        r.Art.Set(PorArtChoice.Dos);
        r.Screen.Draw(f, r.Input);
        for (int i = 0; i < 64; i++)
            Assert.Equal((dos[79 * 8 + i / 8] & (0x80 >> (i % 8))) != 0 ? 15 : 0, f[c79 * 8 + i % 8, r79 * 8 + i / 8]);
        r.Runner.Dispose();
    }

    [Fact]
    public void Amiga_runes_in_the_text_box_and_over_the_frame_draw_their_set_bits_only()
    {
        if (Start(PorArtChoice.Best) is not { } r)
            return;
        Assert.NotNull(r.Art.Runes);
        // ECL5 block 4 $A45E: the runes after the text on rows 17 and 19.
        var data = DaxFile.Open(r.Dir.Find("ECL5.DAX")!).Block(4)!;
        var vm = new EclVm(r.Game.Memory, r.Player);
        vm.Memory.LoadScriptBlock(data);
        Assert.Equal(EclStopReason.Budget, vm.RunFrom(0xA45E, 7).Reason);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        int cells = 0;
        foreach (int row in new[] { 17, 19 })
            for (int c = TextWindow.Left; c <= TextWindow.Right; c++)
                if (TextFont.RawGlyphNumber(r.Player.Text.Cell(row, c).Char) is { } g)
                {
                    AssertAmigaCell(r, f, null, c, row, g);
                    cells++;
                }
        Assert.Equal(12 + 6, cells); // the two lines at $A4AA and $A4B7
        // A line on row 23 lies over the text box's bottom frame: where a glyph has no bit, the frame stays (JAM1).
        r.Player.Text.MoveTo(5, 23);
        r.Player.DrawGlyphs(0, [79, 80]);
        var with = new IndexedFrame(320, 200);
        r.Screen.Draw(with, r.Input);
        r.Player.CommandRedrawn();
        var without = new IndexedFrame(320, 200);
        r.Screen.Draw(without, r.Input);
        AssertAmigaCell(r, with, without, 5, 23, 79);
        AssertAmigaCell(r, with, without, 6, 23, 80);
        Assert.Contains(Enumerable.Range(0, 64), i => r.Font.Items[79][i] == 0 && without[5 * 8 + i % 8, 23 * 8 + i / 8] != 0);
        r.Runner.Dispose();
    }

    /// <summary>
    /// The text under the Amiga art (docs/re/por-art-formats.md §4 "Text font"; P, s39): disk A's <c>poolrad.font</c>,
    /// drawn twice as the program's string routine does, in the pen PRINT uses for DOS colour 10 (28) and in pen 29 one
    /// pixel to the right. Best and Original DOS keep the DOS text font. Returns without the player's files.
    /// </summary>
    [Fact]
    public void Text_is_drawn_in_the_amiga_text_font_and_pens_under_amiga_only()
    {
        if (TestRig.PorDir() is null || Module.Value is not { } module || module.AmigaTextPens() is not { } pens
            || module.Art(GoldBox.Data.Module.Por.PorAmigaText.Container, GoldBox.Data.Module.Por.PorAmigaText.Id, PorArtChoice.Amiga)
                is not { Variant: "amiga", Frames: [var cells] } img)
            return;
        Assert.Equal(28, pens[Chrome.Green]);
        var view = PorViewArt.Load(module);
        var palette = new ScreenPalette();
        var art = new ClientArt(module, view, palette, PorArtChoice.Amiga);
        Assert.True(art.AmigaTextFont);
        int b = palette.Base(img.PaletteRgb);
        // Pens 28 and 29 are registers 28 and 29 of the program's interface colours.
        for (int p = 28; p <= 29; p++)
            Assert.Equal(new Microsoft.Xna.Framework.Color(img.PaletteRgb[3 * (p - 16)], img.PaletteRgb[3 * (p - 16) + 1],
                img.PaletteRgb[3 * (p - 16) + 2]), palette.Colours[b + p - 16]);
        // The line's pixel x (0–15: "PA") is a set bit of its character.
        bool Bit(int x, int y) => x is >= 0 and < 16 && cells.Items["PA"[x / 8]][y * 8 + x % 8] != 0;
        var f = new IndexedFrame(320, 200);
        Chrome.Text(f, view.Font, 1, 17, "PA", Chrome.Green);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 24; x++)
                // Pass 2 (pen 29) lands last, one pixel right of every set bit, the cell after the line too; pass 1
                // (pen 28) on the other set bits; nothing elsewhere.
                Assert.Equal(Bit(x - 1, y) ? b + 13 : Bit(x, y) ? b + 12 : 0, f[8 + x, 17 * 8 + y]);
        Assert.Contains(Enumerable.Range(0, 64), i => Bit(i % 16, i / 16) && !Bit(i % 16 - 1, i / 16));
        // Best and Original DOS: the DOS text font (8X8D1 block 201, 'P' is glyph 16) in EGA colour 10 again.
        var dosFont = DaxFile.Open(new PorGameDirectory(TestRig.PorDir()!).Find("8X8D1.DAX")!).Block(201)!;
        foreach (var choice in new[] { PorArtChoice.Best, PorArtChoice.Dos })
        {
            art.Set(choice);
            Assert.False(art.AmigaTextFont);
            f.Clear();
            Chrome.Text(f, view.Font, 1, 17, "P", Chrome.Green);
            for (int i = 0; i < 64; i++)
                Assert.Equal((dosFont[16 * 8 + i / 8] & (0x80 >> (i % 8))) != 0 ? Chrome.Green : 0, f[8 + i % 8, 17 * 8 + i / 8]);
        }
        art.Set(PorArtChoice.Amiga);
        Assert.True(art.AmigaTextFont);
        // The lit entry: the pen-28 bar, the text in pens 14 and 15 of the picture half (the frame's palette run).
        if (module.AmigaTextHighlight() is [28, 14, 15]
            && module.Art(GoldBox.Data.Module.Por.PorAmigaFrame.Container, GoldBox.Data.Module.Por.PorAmigaFrame.FirstId, PorArtChoice.Amiga)
                is { } frame)
        {
            int lo = palette.Base(frame.PaletteRgb);
            bool PBit(int x, int y) => x is >= 0 and < 8 && y < 8 && cells.Items['P'][y * 8 + x] != 0;
            f.Clear();
            Chrome.Lit(f, view.Font, 1, 4, "P", Chrome.White, listRow: false);
            for (int y = 0; y <= 8; y++)
                for (int x = 0; x <= 8; x++)
                    Assert.Equal(PBit(x - 1, y) ? lo + 15 : PBit(x, y) ? lo + 14 : b + 12, f[8 + x, 32 + y]);
            Assert.Equal(0, f[8 + 9, 32]);

            // The current menu entry as the pressed gadget (decided s39): its box (8 · length + 2 wide, 8 high) complemented,
            // pen p → 31 − p in row 24's colours: the blank (pen 0) shows register 31's row-24 colour, the pen-28 letters
            // register 3's, the pass-2 pen 29 register 2's.
            Assert.NotNull(art.Gadget);
            var row24 = module.AmigaRow24()!;
            Microsoft.Xna.Framework.Color Row24(int reg)
            {
                int i = Array.IndexOf(row24.Where((_, k) => k % 2 == 0).ToArray(), reg);
                var rgb = GoldBox.Data.Dax.AmigaHunks.Rgb12To24([(byte)(row24[2 * i + 1] >> 8), (byte)row24[2 * i + 1]], 1);
                return new Microsoft.Xna.Framework.Color(rgb[0], rgb[1], rgb[2]);
            }
            f.Clear();
            Chrome.Text(f, view.Font, 1, 24, "P", Chrome.Green);
            var before = (byte[])f.Pixels.Clone();
            art.Gadget!.Entry(f, 1, 24, "P");
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 11; x++)
                {
                    byte was = before[(192 + y) * 320 + 8 + x], now = f[8 + x, 192 + y];
                    if (x >= 10)
                        Assert.Equal(was, now);                       // past the box (8 + 2 pixels)
                    else if (was == 0)
                        Assert.Equal(Row24(31), palette.Colours[now]);
                    else if (was == b + 12)
                        Assert.Equal(Row24(3), palette.Colours[now]);
                    else if (was == b + 13)
                        Assert.Equal(Row24(2), palette.Colours[now]);
                }
            art.Set(PorArtChoice.Best);
            Assert.Null(art.Gadget);
            art.Set(PorArtChoice.Amiga);
        }
        else
            Assert.Fail("the module has no highlight pens");
    }

    /// <summary>
    /// The Amiga window frame (docs/re/por-art-formats.md §4 "Window frame"; P, s39): hunk 25 +0x16D4 copies the top
    /// and bottom strips, the sides and the corner from their own top-left corners around the window's cells. Under the
    /// Amiga art only. Returns without the player's files.
    /// </summary>
    [Fact]
    public void Windows_are_framed_with_the_amiga_strips_under_amiga_only()
    {
        if (Module.Value is not { } module
            || Enumerable.Range(0, 5).Select(i => module.Art(GoldBox.Data.Module.Por.PorAmigaFrame.Container,
                GoldBox.Data.Module.Por.PorAmigaFrame.FirstId + i, PorArtChoice.Amiga)).ToList() is not { } imgs
            || imgs.Any(i => i is not { Variant: "amiga" }))
            return;
        var palette = new ScreenPalette();
        var art = new ClientArt(module, null, palette, PorArtChoice.Amiga);
        Assert.NotNull(art.Frame);
        int b = palette.Base(imgs[0]!.PaletteRgb);
        byte Px(int piece, int x, int y) => (byte)(b + imgs[piece]!.Frames[0].Items[0][y * imgs[piece]!.Frames[0].Width + x]);
        var f = new IndexedFrame(320, 200);
        f.Clear(7);
        art.Frame!.Window(f, 17, 1, 38, 15);                     // the party list's window
        for (int x = 0; x < 22 * 8; x++)
        {
            Assert.Equal(Px(0, x, 3), f[17 * 8 + x, 3]);          // top strip from its own x 0
            Assert.Equal(Px(1, x, 5), f[17 * 8 + x, 16 * 8 + 5]); // bottom strip
        }
        for (int y = 0; y < 15 * 8; y++)
        {
            Assert.Equal(Px(2, 6, y), f[16 * 8 + 6, 8 + y]);     // left side from its own row 0
            Assert.Equal(Px(3, 1, y), f[39 * 8 + 1, 8 + y]);     // right side
        }
        foreach (var (cx, cy) in new[] { (16, 0), (39, 0), (16, 16), (39, 16) })
            for (int i = 0; i < 64; i++)
                Assert.Equal(Px(4, i % 8, i / 8), f[cx * 8 + i % 8, cy * 8 + i / 8]);
        Assert.Equal(7, f[20 * 8, 5 * 8]);                        // the inside is not the frame's
        foreach (var choice in new[] { PorArtChoice.Best, PorArtChoice.Dos })
        {
            art.Set(choice);
            Assert.Null(art.Frame);
        }
    }
}
