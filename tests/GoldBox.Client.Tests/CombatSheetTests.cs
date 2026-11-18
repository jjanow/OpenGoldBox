using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using Xunit;

namespace GoldBox.Client.Tests;

/// <summary>
/// The View page's layout rules (GAME.OVR 0x21084, <see cref="CombatSheet"/>), on hand-made records with our fallback
/// words (no game files). The page itself was checked pixel for pixel against DOSBox-X on the six members of save K
/// (s18).
/// </summary>
public class CombatSheetTests
{
    private static (int Col, int Row, string Text, byte Colour) At(List<(int Col, int Row, string Text, byte Colour)> l, int col, int row) =>
        Assert.Single(l, t => t.Col == col && t.Row == row);

    private static EclCharacter Fighter()
    {
        var c = new EclCharacter { Name = "Tester" };
        c[0x10D] = 1;           // in play
        c[0x10] = 18;
        c[0x16] = 5;            // 18/05
        c[0x11] = 9;
        c.SetWord(0x88 + 2 * 0, 7);   // copper
        c.SetWord(0x88 + 2 * 4, 300); // platinum
        c[0x96 + 2] = 3;        // fighter 3
        c[0x96 + 5] = 2;        // magic-user 2
        c[0x111] = 62;          // AC −2
        c[0x110] = 41;          // THAC0 19
        c[0x11B] = 4;
        c[0x32] = 9;            // hurt: yellow
        c[0x115] = 2;
        c[0x117] = 4;
        c[0x119] = 0xFF;        // −1
        c[0x11C] = 12;
        return c;
    }

    [Fact]
    public void The_page_puts_each_value_where_the_program_does()
    {
        var c = Fighter();
        var l = CombatSheet.Build(new ClientResources(), c, null, null, _ => "X");

        Assert.Equal(("TESTER", (byte)11), (At(l, 1, 1).Text, At(l, 1, 1).Colour));
        Assert.Equal("18", At(l, 5, 7).Text);
        Assert.Equal("(05)", At(l, 7, 7).Text);
        Assert.Equal("9", At(l, 6, 8).Text);      // one digit: one column on
        // Money: kinds 6 down to 0, names right-aligned to column 19, amounts at column 21.
        var pp = CombatSheet.CoinName(new ClientResources(), 4);
        Assert.Equal(pp, At(l, 20 - pp.Length, 7).Text);
        Assert.Equal("300", At(l, 21, 7).Text);
        Assert.Equal("7", At(l, 21, 8).Text);
        Assert.Equal("3/2", At(l, 7, 15).Text);
        Assert.Equal("-2", At(l, 4, 17).Text);
        Assert.Equal(("4", (byte)14), (At(l, 4, 18).Text, At(l, 4, 18).Colour));
        Assert.Equal("19", At(l, 15, 17).Text);
        Assert.Equal("2D4-1", At(l, 15, 18).Text);
        Assert.Equal("12", At(l, 34, 18).Text);
        // No weapon or armour: rows 20 and 21 empty; the status on row 22.
        Assert.DoesNotContain(l, t => t.Row is 20 or 21);
        Assert.Contains(l, t => t.Row == 22 && t.Col == 8);
    }

    [Fact]
    public void Strength_100_shows_00_and_npcs_get_their_tag()
    {
        var c = Fighter();
        c[0x16] = 100;
        c[0x84] = 0x80;
        c[0x10E] = 1;
        c[0x11B] = 9;
        var l = CombatSheet.Build(new ClientResources(), c, new byte[63], new byte[63], _ => "ITEM");
        Assert.Equal("(00)", At(l, 7, 7).Text);
        Assert.Equal(14, At(l, 1, 1).Colour);                        // hostile: yellow
        Assert.Equal(10, At(l, "TESTER".Length + 3, 1).Colour);      // the NPC tag, green
        Assert.Equal(10, At(l, 4, 18).Colour);                       // full hit points: green
        Assert.Equal("ITEM", At(l, 8, 20).Text);
        Assert.Equal("ITEM", At(l, 8, 21).Text);
        Assert.Equal(15, At(l, 2, 21).Colour);                       // the armour label at column 2
    }

    [Fact]
    public void Movement_doubles_and_halves_with_effects_0x2A_and_0x27()
    {
        var c = Fighter();
        c.Effects.Add([0x2A, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal("24", At(CombatSheet.Build(new ClientResources(), c, null, null, _ => ""), 34, 18).Text);
        c.Effects.Add([0x27, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.Equal("12", At(CombatSheet.Build(new ClientResources(), c, null, null, _ => ""), 34, 18).Text);
    }

    [Fact]
    public void The_shared_highlight_carries_over_and_resets_past_the_last_entry()
    {
        var h = new MenuHighlight();
        Assert.Equal(0, h.Start(7));
        h.Chosen(6);
        Assert.Equal(0, h.Start(4));  // Done after the main menu's Done: past the end → first
        h.Chosen(1);
        Assert.Equal(1, h.Start(7));
        Assert.Equal(1, h.Start(3));
        h.Chosen(-1);                 // a cancel leaves it
        Assert.Equal(1, h.Index);
    }

    [Fact]
    public void The_load_menu_leaves_the_highlight_on_the_slots_place_among_the_saves()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (d is null || !Directory.Exists(d))
            return;
        // The shipped folder holds saves A, B and J (DOSBox-X: "A B J"; Load B → the area line on Cast).
        var letters = Directory.EnumerateFiles(d, "SAVGAM?.DAT").Select(f => char.ToUpperInvariant(Path.GetFileName(f)[6])).Order().ToList();
        foreach (var (ch, i) in letters.Select((ch, i) => (ch, i)))
            Assert.Equal(i, GoldBox.Client.Screens.ExploreScreen.LoadMenuIndex(d, ch));
        Assert.Equal(0, GoldBox.Client.Screens.ExploreScreen.LoadMenuIndex(d, 'Z'));
    }

    [Fact]
    public void Gender_2_reads_past_the_gender_table_as_the_program_does()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (d is null || !Directory.Exists(d))
            return;
        // Restoration at the race limit adds 1 to the gender byte (spells-effects.md); DOSBox-X's View showed a female
        // elf after it as "COPPER ELF" (fixture restoration_limit): entry 2 of the 7-byte table at DS:0FEC is the
        // first coin name.
        var c = Fighter();
        c[0x9E] = 2;
        c[0x2E] = 2;
        var l = CombatSheet.Build(ClientResources.Load(new GoldBox.Data.Por.PorGameDirectory(d)), c, null, null, _ => "");
        Assert.Equal("COPPER", At(l, 1, 3).Text);
        Assert.Equal("ELF", At(l, 8, 3).Text);
    }

    [Fact]
    public void A_modules_engine_saves_count_as_the_load_menus_slots()
    {
        // A module keeps its shipped saves only as engine saves (saves/a.json, j.json; docs/module-format.md §15).
        var tmp = Directory.CreateTempSubdirectory("goldbox-loadmenu-");
        try
        {
            foreach (var f in new[] { "a.json", "j.json", "SAVGAMB.DAT", "notes.txt" })
                File.WriteAllText(Path.Combine(tmp.FullName, f), "");
            Assert.Equal(0, GoldBox.Client.Screens.ExploreScreen.LoadMenuIndex(tmp.FullName, 'A'));
            Assert.Equal(1, GoldBox.Client.Screens.ExploreScreen.LoadMenuIndex(tmp.FullName, 'B'));
            Assert.Equal(2, GoldBox.Client.Screens.ExploreScreen.LoadMenuIndex(tmp.FullName, 'J'));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }
}
