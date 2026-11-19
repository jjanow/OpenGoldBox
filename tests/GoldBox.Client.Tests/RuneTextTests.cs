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
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ClientPlayer Player, PorGameDirectory Dir, ActionInput Input);

    private static Rig Start()
    {
        var dir = new PorGameDirectory(TestRig.PorDir()!);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost()), game, player, dir, new ActionInput(InputBindings.Defaults()));
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
