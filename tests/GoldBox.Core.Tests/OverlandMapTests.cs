using GoldBox.Core.Explore;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The overland's draw rule (GAME.OVR 0x2DE39) and its module codec, on made-up data (no game files).</summary>
public class OverlandMapTests
{
    /// <summary>A 4 × 3 map of tile 10 with nine special locations, location i on (i mod 4, i div 4) with tile 100 + i.</summary>
    private static OverlandMap Sample()
    {
        var tiles = Enumerable.Repeat((byte)10, 12).ToArray();
        tiles[11] = 0; // a stored 0 draws 255
        var sites = Enumerable.Range(1, 9).Select(i => new OverlandSite((ushort)(i % 4), (ushort)(i / 4), (ushort)(100 + i))).ToList();
        return new OverlandMap(4, 3, tiles, sites);
    }

    private static Func<ushort, ushort> Cells(params (ushort Cell, ushort Value)[] values)
    {
        var d = values.ToDictionary(v => v.Cell, v => v.Value);
        return c => d.GetValueOrDefault(c);
    }

    [Fact]
    public void The_drawn_tile_is_the_stored_one_minus_one()
    {
        var m = Sample();
        Assert.Equal(9, m.DrawnTile(0, 0, Cells()));
        Assert.Equal(255, m.DrawnTile(3, 2, Cells()));
    }

    [Fact]
    public void Each_special_location_follows_its_traced_rule()
    {
        var m = Sample();
        byte At(int i, Func<ushort, ushort> cells) => m.DrawnTile(i % 4, i / 4, cells);
        byte Plain = 9;
        byte Site(int i) => (byte)(100 + i - 1);
        var zero = Cells();
        // 1 and 2: never; 6: always.
        Assert.Equal(Plain, At(1, zero));
        Assert.Equal(Plain, At(2, zero));
        Assert.Equal(Site(6), At(6, zero));
        // 3: $4A8C ≠ 0xFF.
        Assert.Equal(Site(3), At(3, zero));
        Assert.Equal(Plain, At(3, Cells((OverlandMap.Cell4A8C, 0xFF))));
        // 4: $4AD2 ≠ 0, or $4A7C bit 3, or $4A9F = 0.
        Assert.Equal(Site(4), At(4, zero));
        Assert.Equal(Plain, At(4, Cells((OverlandMap.Cell4A9F, 1))));
        Assert.Equal(Site(4), At(4, Cells((OverlandMap.Cell4A9F, 1), (OverlandMap.Cell4A7C, 8))));
        Assert.Equal(Site(4), At(4, Cells((OverlandMap.Cell4A9F, 1), (OverlandMap.Cell4AD2, 1))));
        // 5 and 7: bits 1 and 0 of $4AA0 clear.
        Assert.Equal(Site(5), At(5, Cells((OverlandMap.Cell4AA0, 1))));
        Assert.Equal(Plain, At(5, Cells((OverlandMap.Cell4AA0, 2))));
        Assert.Equal(Site(7), At(7, Cells((OverlandMap.Cell4AA0, 2))));
        Assert.Equal(Plain, At(7, Cells((OverlandMap.Cell4AA0, 1))));
        // 8 and 9: $4AB3 above 0xFD (unsigned).
        Assert.Equal(Plain, At(8, Cells((OverlandMap.Cell4AB3, 0xFD))));
        Assert.Equal(Site(8), At(8, Cells((OverlandMap.Cell4AB3, 0xFE))));
        Assert.Equal(Site(9), At(9, Cells((OverlandMap.Cell4AB3, 0xFFFF))));
    }

    [Fact]
    public void A_later_location_on_the_same_square_wins()
    {
        var sites = new List<OverlandSite> { new(0, 0, 50), new(0, 0, 51), new(0, 0, 52), new(1, 0, 53), new(1, 0, 54), new(0, 0, 56) };
        var m = new OverlandMap(2, 1, [1, 1], sites);
        Assert.Equal(55, m.DrawnTile(0, 0, Cells()));                                  // 3 shown, then 6 over it
        Assert.Equal(51, new OverlandMap(2, 1, [1, 1], sites[..3]).DrawnTile(0, 0, Cells())); // 3 only (1, 2 never)
        Assert.Equal(53, m.DrawnTile(1, 0, Cells()));                                  // 4 shown, then 5 over it
        Assert.Equal(52, m.DrawnTile(1, 0, Cells((OverlandMap.Cell4AA0, 2))));        // 5 hidden: 4 stays
    }

    [Fact]
    public void The_module_document_keeps_the_stored_values()
    {
        var m = Sample();
        var doc = PorModuleCodec.Overland(m, new PorOverlandOffsets(0x100, 0x200, 0x300));
        Assert.Equal((0x100, 0x200), (doc.Source!.Tiles, doc.Source.SpecialLocations));
        var back = PorModuleCodec.Overland(doc);
        Assert.Equal(m.Tiles, back.Tiles);
        Assert.Equal(m.Sites, back.Sites);
        Assert.Equal(PorOverland.SiteBytes(m.Sites), PorOverland.SiteBytes(back.Sites));
        Assert.Throws<FormatException>(() => PorModuleCodec.Overland(doc with { Tiles = [.. doc.Tiles[..^1], 256] }));
        Assert.Throws<ArgumentException>(() => PorModuleCodec.Overland(doc with { Width = 5 }));
    }

    [Fact]
    public void Only_the_wilderness_scripts_have_a_view_mode()
    {
        Assert.Equal([2, 3, 4], new[] { 25, 26, 27 }.Select(s => PorOverland.ViewMode(s)!.Value));
        Assert.Null(PorOverland.ViewMode(24));
        Assert.Null(PorOverland.ViewMode(28));
    }
}
