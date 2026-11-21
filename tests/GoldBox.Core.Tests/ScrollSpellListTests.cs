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
}
