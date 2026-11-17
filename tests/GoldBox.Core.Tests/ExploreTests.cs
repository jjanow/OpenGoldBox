using GoldBox.Core.Doors;
using GoldBox.Core.Explore;

namespace GoldBox.Core.Tests;

/// <summary>Builds the four GEO planes of a synthetic map.</summary>
internal sealed class MapBuilder
{
    private readonly byte[] _p = new byte[AreaMap.PlanesLength];

    /// <summary>Sets the wall type and barrier of one face.</summary>
    public MapBuilder Face(int x, int y, int facing, int wall, DoorBarrier barrier = DoorBarrier.Impassable)
    {
        int i = y * 16 + x;
        switch (facing)
        {
            case Facing.North: _p[i] = (byte)((_p[i] & 0x0F) | (wall << 4)); break;
            case Facing.East: _p[i] = (byte)((_p[i] & 0xF0) | wall); break;
            case Facing.South: _p[256 + i] = (byte)((_p[256 + i] & 0x0F) | (wall << 4)); break;
            case Facing.West: _p[256 + i] = (byte)((_p[256 + i] & 0xF0) | wall); break;
        }
        int s = facing;
        _p[768 + i] = (byte)((_p[768 + i] & ~(3 << s)) | ((int)barrier << s));
        return this;
    }

    public MapBuilder Event(int x, int y, byte e)
    {
        _p[512 + y * 16 + x] = e;
        return this;
    }

    public AreaMap Build(bool bounded = false) => new(_p) { Bounded = bounded };
}

public class AreaMapTests
{
    [Fact]
    public void Faces_decode_from_the_planes()
    {
        var m = new MapBuilder().Face(3, 4, Facing.North, 7).Face(3, 4, Facing.East, 2, DoorBarrier.Locked)
            .Face(3, 4, Facing.South, 15, DoorBarrier.Passable).Face(3, 4, Facing.West, 1, DoorBarrier.Barred).Build();
        Assert.Equal(7, m.Wall(3, 4, Facing.North));
        Assert.Equal(2, m.Wall(3, 4, Facing.East));
        Assert.Equal(15, m.Wall(3, 4, Facing.South));
        Assert.Equal(1, m.Wall(3, 4, Facing.West));
        Assert.Equal(DoorBarrier.Impassable, m.Passage(3, 4, Facing.North));
        Assert.Equal(DoorBarrier.Locked, m.Passage(3, 4, Facing.East));
        Assert.Equal(DoorBarrier.Passable, m.Passage(3, 4, Facing.South));
        Assert.Equal(DoorBarrier.Barred, m.Passage(3, 4, Facing.West));
    }

    [Fact]
    public void A_face_without_a_wall_is_passable_whatever_plane_3_stores()
    {
        var m = new MapBuilder().Face(1, 1, Facing.East, 0, DoorBarrier.Impassable).Build();
        Assert.Equal(DoorBarrier.Passable, m.Passage(1, 1, Facing.East));
    }

    [Fact]
    public void Off_map_squares_wrap_the_programs_way()
    {
        var m = new MapBuilder().Face(0, 3, Facing.North, 5).Face(15, 3, Facing.North, 9).Event(15, 15, 0x80).Build();
        Assert.Equal(5, m.Wall(16, 3, Facing.North));  // x > 15 → 0
        Assert.Equal(5, m.Wall(40, 3, Facing.North));  // any x > 15 → 0, not a modulo
        Assert.Equal(9, m.Wall(-1, 3, Facing.North));  // x < 0 → 15
        Assert.Equal(9, m.Wall(-2, 3, Facing.North));  // also 15
        Assert.Equal(0x80, m.Event(-1, -1));
    }

    [Fact]
    public void Bounded_maps_read_nothing_off_the_map()
    {
        var m = new MapBuilder().Face(0, 3, Facing.North, 5).Event(15, 0, 0x80).Build(bounded: true);
        Assert.Equal(0, m.Wall(16, 3, Facing.North));
        Assert.Equal(DoorBarrier.Impassable, m.Passage(16, 3, Facing.North));
        Assert.Equal(0, m.Event(-1, -1));
        Assert.Equal(5, m.Wall(0, 3, Facing.North));
    }
}

public class PartyPositionTests
{
    [Fact]
    public void Turns_follow_the_8_way_code()
    {
        var p = new PartyPosition(5, 5, Facing.West);
        p.TurnLeft();
        Assert.Equal(Facing.South, p.Facing);
        p.TurnRight();
        p.TurnRight();
        Assert.Equal(Facing.North, p.Facing);
        p.TurnAround();
        Assert.Equal(Facing.South, p.Facing);
        Assert.Equal("5,5 S", p.ToString());
    }

    [Fact]
    public void Open_faces_step_and_walls_block()
    {
        var m = new MapBuilder().Face(5, 5, Facing.North, 3).Build();
        var p = new PartyPosition(5, 5, Facing.North);
        Assert.Equal(StepOutcome.Blocked, p.StepForward(m).Outcome);
        Assert.Equal((5, 5), (p.X, p.Y));
        p.TurnRight();
        var r = p.StepForward(m);
        Assert.Equal(StepOutcome.Moved, r.Outcome);
        Assert.False(r.LeftMap);
        Assert.Equal((6, 5), (p.X, p.Y));
    }

    [Fact]
    public void Drawn_but_passable_faces_step()
    {
        var m = new MapBuilder().Face(5, 5, Facing.North, 3, DoorBarrier.Passable).Build();
        var p = new PartyPosition(5, 5, Facing.North);
        Assert.Equal(StepOutcome.Moved, p.StepForward(m).Outcome);
        Assert.Equal((5, 4), (p.X, p.Y));
    }

    [Fact]
    public void One_way_walls_block_from_the_walled_side_only()
    {
        // (4,4) has a wall on its E face; (5,4) has no wall on its W face: geo-format.md "One-way walls".
        var m = new MapBuilder().Face(4, 4, Facing.East, 2).Build();
        var east = new PartyPosition(4, 4, Facing.East);
        Assert.Equal(StepOutcome.Blocked, east.StepForward(m).Outcome);
        var west = new PartyPosition(5, 4, Facing.West);
        Assert.Equal(StepOutcome.Moved, west.StepForward(m).Outcome);
        Assert.Equal(4, west.X);
    }

    [Theory]
    [InlineData(DoorBarrier.Locked)]
    [InlineData(DoorBarrier.Barred)]
    public void Doors_stop_the_step_for_the_door_menu(DoorBarrier kind)
    {
        var m = new MapBuilder().Face(2, 2, Facing.South, 4, kind).Build();
        var p = new PartyPosition(2, 2, Facing.South);
        var r = p.StepForward(m);
        Assert.Equal(StepOutcome.Door, r.Outcome);
        Assert.Equal(kind, r.Barrier);
        Assert.Equal((2, 2), (p.X, p.Y));
        p.StepThrough();
        Assert.Equal((2, 3), (p.X, p.Y));
    }

    [Fact]
    public void Stepping_off_the_edge_wraps_and_flags_it()
    {
        // DOSBox-X edge_wrap_patched: 15,4 facing E, one step with the script not handling the exit → 0,4.
        var m = new MapBuilder().Build();
        var p = new PartyPosition(15, 4, Facing.East);
        var r = p.StepForward(m);
        Assert.Equal(StepOutcome.Moved, r.Outcome);
        Assert.True(r.LeftMap);
        Assert.Equal((0, 4), (p.X, p.Y));
    }

    [Fact]
    public void A_wall_at_the_edge_is_not_leaving_the_map()
    {
        var m = new MapBuilder().Face(0, 0, Facing.North, 1).Build();
        var r = new PartyPosition(0, 0, Facing.North).StepForward(m);
        Assert.Equal(StepOutcome.Blocked, r.Outcome);
        Assert.False(r.LeftMap);
    }

    [Fact]
    public void Diagonal_facings_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartyPosition(0, 0, 1));
}

public class ViewComposeTests
{
    /// <summary>A made-up piece layout with the program's structure (ten pieces tiling 156 bytes); not the game's values.</summary>
    internal static ViewLayout Layout(int[]? column = null) => new(
        start: [0, 12, 24, 36, 48, 60, 72, 84, 96, 108],
        width: [3, 3, 3, 3, 3, 3, 3, 3, 3, 6],
        height: [4, 4, 4, 4, 4, 4, 4, 4, 4, 8],
        column: column ?? [1, 1, 1, 1, 1, 1, 3, 0, 8, 1],
        row: [2, 2, 2, 2, 2, 2, 2, 1, 1, 2]);

    /// <summary>Wall slot 1 filled with symbols 1 + (byte index mod 40), so a symbol tells which byte was drawn.</summary>
    private static WallBuffer Walls(SymbolSet s)
    {
        var w = new WallBuffer();
        var rec = new byte[WallBuffer.SlotSize];
        for (int i = 0; i < rec.Length; i++)
            rec[i] = (byte)(1 + i % 40); // all < threshold 45: not rebased
        w.Load(1, 1, rec, s);
        return w;
    }

    private static readonly SymbolSet Symbols = SymbolSet.FromCounts(45, 70);

    [Fact]
    public void Layout_must_tile_the_wall_record()
    {
        Assert.Throws<FormatException>(() => new ViewLayout(
            [0, 12, 24, 36, 48, 60, 72, 84, 96, 108], [3, 3, 3, 3, 3, 3, 3, 3, 3, 6],
            [4, 4, 4, 4, 4, 4, 4, 4, 4, 7], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Throws<FormatException>(() => new ViewLayout(
            [0, 13, 24, 36, 48, 60, 72, 84, 96, 108], [3, 3, 3, 3, 3, 3, 3, 3, 3, 6],
            [4, 4, 4, 4, 4, 4, 4, 4, 4, 8], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void An_empty_map_draws_nothing()
    {
        Assert.Empty(View3D.Compose(new MapBuilder().Build(), 5, 5, Facing.North, Layout(), Walls(Symbols)));
    }

    [Fact]
    public void The_own_squares_front_wall_is_the_near_front_piece_drawn_from_both_sides()
    {
        // Near row: the left pass ends on the centre square (offset −7 + 7) and the right pass too (7 − 7).
        var m = new MapBuilder().Face(5, 5, Facing.North, 1).Build();
        var o = View3D.Compose(m, 5, 5, Facing.North, Layout(), Walls(Symbols));
        Assert.Equal(24, o.Count);
        Assert.Equal(new ViewSymbol(3, 2, 1 + 72 % 40), o[0]); // piece 6 starts at byte 72, column 3, row 2
        Assert.Equal(o.GetRange(0, 12), o.GetRange(12, 12));
    }

    [Fact]
    public void Facing_rotates_which_face_is_in_front()
    {
        var m = new MapBuilder().Face(5, 5, Facing.West, 1).Build();
        Assert.DoesNotContain(View3D.Compose(m, 5, 5, Facing.North, Layout(), Walls(Symbols)),
            s => s.Row == 2 && s.Column is >= 3 and < 6);
        var west = View3D.Compose(m, 5, 5, Facing.West, Layout(), Walls(Symbols));
        Assert.Equal(new ViewSymbol(3, 2, 1 + 72 % 40), west[0]);
    }

    [Fact]
    public void Clipped_cells_still_advance_through_the_piece()
    {
        // Piece 6 placed at column 9: of each 3-cell row only columns 9 and 10 are drawn, and row 2 starts with byte 3.
        int[] col = [1, 1, 1, 1, 1, 1, 9, 0, 8, 1];
        var m = new MapBuilder().Face(5, 5, Facing.North, 1).Build();
        var o = View3D.Compose(m, 5, 5, Facing.North, Layout(col), Walls(Symbols));
        Assert.Equal(16, o.Count); // 2 cells × 4 rows, twice
        Assert.Equal(new ViewSymbol(9, 2, 1 + 72 % 40), o[0]);
        Assert.Equal(new ViewSymbol(10, 2, 1 + 73 % 40), o[1]);
        Assert.Equal(new ViewSymbol(9, 3, 1 + 75 % 40), o[2]);
    }

    [Fact]
    public void Side_walls_of_the_party_square_use_the_near_side_pieces()
    {
        var m = new MapBuilder().Face(5, 5, Facing.West, 1).Face(5, 5, Facing.East, 1).Build();
        var o = View3D.Compose(m, 5, 5, Facing.North, Layout(), Walls(Symbols));
        Assert.Contains(new ViewSymbol(0, 1, 1 + 84 % 40), o); // piece 7 at column 0 + offset 0
        Assert.Contains(new ViewSymbol(8, 1, 1 + 96 % 40), o); // piece 8 at column 8 + offset 0
    }

    [Fact]
    public void Area_map_window_is_clamped_and_shows_the_party_arrow()
    {
        var m = new MapBuilder().Face(0, 0, Facing.North, 1).Face(0, 0, Facing.West, 1).Build();
        var o = View3D.ComposeAreaMap(m, 1, 2, Facing.East, 256);
        Assert.Equal(122, o.Count);
        Assert.Equal(new ViewSymbol(0, 0, 256 + 4 + (1 | 8)), o[0]);
        Assert.Equal(new ViewSymbol(1, 2, 256 + 1), o[^1]);
        var far = View3D.ComposeAreaMap(m, 15, 15, Facing.South, 256);
        Assert.Equal(new ViewSymbol(10, 10, 256 + 2), far[^1]); // window x0 = y0 = 5
    }
}

public class SymbolAndWallTests
{
    [Fact]
    public void Symbol_space_follows_the_tile_counts()
    {
        var s = SymbolSet.FromCounts(45, 70);
        Assert.Equal([1, 46, 116, 186, 256], s.Bases);
        Assert.Equal(45, s.RebaseThreshold);
        Assert.Equal(0, s.BlockOf(45));
        Assert.Equal(1, s.BlockOf(46));
        Assert.Equal(3, s.BlockOf(255));
        Assert.Equal(4, s.BlockOf(256));
        Assert.Equal(-1, s.BlockOf(0));
        Assert.Equal(70, s.SlotDelta(2));
    }

    [Fact]
    public void Tiles_are_found_by_symbol_number()
    {
        var s = SymbolSet.FromCounts(2, 3);
        var common = new byte[128];
        common[64] = 9; // tile 2 = symbol 2
        s.SetBlock(0, common);
        Assert.Equal(9, s.Tile(2)[0]);
        Assert.True(s.Tile(3).IsEmpty); // block 1 not loaded
        Assert.Throws<ArgumentException>(() => s.SetBlock(1, new byte[64 * 4])); // more than 3 tiles
    }

    [Fact]
    public void Wall_sets_are_rebased_from_the_threshold_on()
    {
        var s = SymbolSet.FromCounts(45, 70);
        var rec = new byte[WallBuffer.SlotSize];
        rec[0] = 44;
        rec[1] = 45;
        rec[2] = 115;
        rec[3] = 0;
        var w = new WallBuffer();
        Assert.Equal(1, w.Load(2, 9, rec, s));
        var wall = w.Wall(6); // wall type 6 = slot 2, wall 0
        Assert.Equal([44, 115, 185, 0], wall[..4].ToArray());
        Assert.Equal(9, w.SetIds[1]);
    }

    [Fact]
    public void Multi_block_records_use_the_first_slots_delta()
    {
        var s = SymbolSet.FromCounts(45, 70);
        var rec = new byte[2 * WallBuffer.SlotSize];
        rec[WallBuffer.SlotSize] = 120;
        var w = new WallBuffer();
        Assert.Equal(2, w.Load(1, 21, rec, s));
        Assert.Equal(120, w.Wall(6)[0]); // slot 1's delta is 0, also for the second block
        Assert.Throws<FormatException>(() => w.Load(3, 21, rec, s));
        w.Clear(2);
        Assert.Null(w.SetIds[1]);
        Assert.Equal(0, w.Wall(6)[0]);
    }

    [Fact]
    public void Masked_tiles_skip_colour_13()
    {
        var f = new IndexedFrame(8, 8);
        f.Clear(4);
        var tile = new byte[64];
        Array.Fill(tile, (byte)13);
        tile[0] = 2;
        f.DrawTile(0, 0, tile, masked: true);
        Assert.Equal(2, f[0, 0]);
        Assert.Equal(4, f[1, 0]);
        f.DrawTile(0, 0, tile, masked: false);
        Assert.Equal(13, f[1, 0]);
    }

    [Fact]
    public void Backdrop_bands_cover_the_view()
    {
        var f = new IndexedFrame(88, 88);
        ViewRaster.DrawBackdrop(f, 0, 0, new Backdrop(1, 2, 3, 4));
        Assert.Equal(1, f[0, 42]);
        Assert.Equal(2, f[87, 43]);
        Assert.Equal(3, f[5, 45]);
        Assert.Equal(4, f[5, 46]);
        Assert.Equal(4, f[5, 87]);
    }

    [Fact]
    public void Font_maps_ascii_and_letters()
    {
        var font = new TextFont(new byte[177 * 8]);
        Assert.Equal(1, font.GlyphOf('A'));
        Assert.Equal(1, font.GlyphOf('a'));
        Assert.Equal(26, font.GlyphOf('Z'));
        Assert.Equal(48, font.GlyphOf('0'));
        Assert.Equal(32, font.GlyphOf(' '));
        Assert.Equal(-1, font.GlyphOf('~'));
    }
}
