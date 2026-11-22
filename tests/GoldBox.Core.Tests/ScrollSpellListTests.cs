using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>
/// List kind 2, the spells of the scroll being used (0x275B4 → 0x27466(0), read s38; D s38: a magic-user scroll of
/// Charm Person, Protection from Evil and Strength in a fight showed "1st level", the two, "2nd level", Strength).
/// </summary>
public class ScrollSpellListTests
{
    private static readonly SpellTable Table = new(new Dictionary<int, SpellInfo>
    {
        [10] = new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        [16] = new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        [35] = new(1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
    });

    private static byte[] Scroll(byte a, byte b, byte c, byte hidden = 0)
    {
        var it = new byte[63];
        (it[0x3C], it[0x3D], it[0x3E], it[0x35]) = (a, b, c, hidden);
        return it;
    }

    [Fact]
    public void Bytes_in_order_with_level_headings_and_bit_7_pending()
    {
        var rows = CampSpells.ScrollList(Table, new EclCharacter(), Scroll(10, 0x80 | 16, 35), 11);
        Assert.Equal([10, 16, 35], rows.Select(r => r.Spell));
        Assert.Equal([1, null, 2], rows.Select(r => r.HeadingLevel));
        Assert.Equal([false, true, false], rows.Select(r => r.Pending));
    }

    [Fact]
    public void Zero_bytes_are_skipped()
    {
        var rows = CampSpells.ScrollList(Table, new EclCharacter(), Scroll(0, 35, 0), 11);
        Assert.Equal([35], rows.Select(r => r.Spell));
        Assert.Equal(2, rows[0].HeadingLevel);
    }

    [Fact]
    public void A_hidden_scroll_lists_nothing_unless_it_identifies_itself()
    {
        var c = new EclCharacter();
        var mu = Scroll(10, 16, 35, hidden: 1);
        Assert.Empty(CampSpells.ScrollList(Table, c, mu, 11));
        Assert.Equal(1, mu[0x35]);

        // a cleric level identifies a cleric scroll (slot 12) only
        c[CampSpells.ClericLevel] = 1;
        Assert.Empty(CampSpells.ScrollList(Table, c, mu, 11));
        var cl = Scroll(10, 0, 0, hidden: 1);
        Assert.Single(CampSpells.ScrollList(Table, c, cl, 12));
        Assert.Equal(0, cl[0x35]);

        // effect 0x10 (Read Magic) identifies any scroll
        var reader = new EclCharacter();
        reader.Effects.Add([0x10, 0, 0, 0, 0]);
        Assert.Equal(3, CampSpells.ScrollList(Table, reader, mu, 11).Count);
        Assert.Equal(0, mu[0x35]);
    }

    /// <summary>
    /// Original bugs, fixed (user, s50; item-format.md): the program's row buffer DS:476A holds 21 spells, so a 22nd
    /// overwrites the row → scroll table DS:477F, and the count DS:4843 grows over every mode-2 read until 49 rows
    /// overrun the spell globals. Core's lists have no bound: 8 scrolls of 3 spells list 24 rows, each row keeping
    /// its own scroll, and a scroll read alone (mode 2) any number of times lists the same rows.
    /// </summary>
    [Fact]
    public void Lists_past_21_spells_keep_every_row_and_scroll()
    {
        var c = new EclCharacter();
        var scrolls = Enumerable.Range(0, 8).Select(_ => Scroll(10, 16, 35)).ToList();
        foreach (var it in scrolls)
            c.Items.Add(it);
        var rows = new CampSpells(Table, _ => true).List(c, CampSpells.ListKind.OnScrolls, _ => 11);
        Assert.Equal(24, rows.Count);
        for (int i = 0; i < 24; i++)
        {
            Assert.Same(scrolls[i / 3], rows[i].Scroll);
            Assert.Equal(new[] { 10, 16, 35 }[i % 3], rows[i].Spell);
        }

        for (int k = 0; k < 30; k++) // 90 rows read through Use with no list in between
            Assert.Equal([10, 16, 35], CampSpells.ScrollList(Table, c, scrolls[0], 11).Select(r => r.Spell));
    }
}
