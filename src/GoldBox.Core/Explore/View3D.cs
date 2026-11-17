namespace GoldBox.Core.Explore;

/// <summary>One 8×8 symbol placed in the 11×11 view grid (column, row 0..10).</summary>
public readonly record struct ViewSymbol(int Column, int Row, int Symbol);

/// <summary>
/// The first-person 3D view and the area map, composed as the program draws them (docs/re/view3d.md): a list of 8×8
/// symbols in drawing order. Wall pieces are drawn masked (colour 13 shows what is under it), so order matters. No
/// pixels here: <see cref="ViewRaster"/> turns the list into an image.
/// </summary>
public static class View3D
{
    public const int Cells = 11;

    /// <summary>
    /// Draw3DView: the walls from two squares ahead back to the party's square (far to near). Far row: the fronts of
    /// four squares to each side with the corner piece between runs, then the side walls of three squares each way.
    /// Middle row (one ahead): squares −2..0 with their fronts and left sides, then +2..0 with fronts and right sides.
    /// Near row (the party's row): −1..0 and +1..0 the same way.
    /// </summary>
    public static List<ViewSymbol> Compose(AreaMap map, int x, int y, int facing, ViewLayout layout, WallBuffer walls)
    {
        var o = new List<ViewSymbol>(256);
        int left = Facing.Left(facing), right = Facing.Right(facing), back = Facing.Back(facing);
        int lx = Facing.DeltaX(left), ly = Facing.DeltaY(left), rx = Facing.DeltaX(right), ry = Facing.DeltaY(right);
        int cx = x + 2 * Facing.DeltaX(facing), cy = y + 2 * Facing.DeltaY(facing);

        void Piece(int col, int row, int wallType, int piece) => DrawPiece(o, col, row, wallType, piece, layout, walls);

        for (int dist = 2; dist >= 0; dist--)
        {
            switch (dist)
            {
                case 2:
                    FarFronts(+1);
                    FarFronts(-1);
                    FarSides(left, 1, -1);
                    FarSides(right, 2, +1);
                    break;
                case 1:
                    Row(cx + 2 * lx, cy + 2 * ly, -6, +3, rx, ry, left, 3, 4);
                    Row(cx + 2 * rx, cy + 2 * ry, +6, -3, lx, ly, right, 3, 5);
                    break;
                case 0:
                    Row(cx + lx, cy + ly, -7, +7, rx, ry, left, 6, 7, count: 2);
                    Row(cx + rx, cy + ry, +7, -7, lx, ly, right, 6, 8, count: 2);
                    break;
            }
            cx += Facing.DeltaX(back);
            cy += Facing.DeltaY(back);
        }
        return o;

        // Far fronts: side = +1 walks to the left (column offset 0, −2, −4, −6), −1 walks to the right (+2 each).
        // A run of front walls gets the corner piece (9) where it ends next to an open square whose side wall
        // continues, and between two walls.
        void FarFronts(int side)
        {
            int dir = side > 0 ? left : right, other = side > 0 ? right : left;
            int dx = Facing.DeltaX(dir), dy = Facing.DeltaY(dir);
            int sx = cx, sy = cy, off = 0, edge = 0;
            int cornerShift = side > 0 ? 1 : -1;
            for (int i = 0; i < 4; i++)
            {
                int w = map.Wall(sx, sy, facing);
                if (!AreaMap.InMap(sx, sy) && map.Wall(sx, sy, other) == 0)
                    edge = 0;
                if (w != 0)
                {
                    if (edge != 0)
                        Piece(layout.Column[9] + off + cornerShift, layout.Row[9], edge, 9);
                    edge = w;
                    Piece(layout.Column[0] + off, layout.Row[0], w, 0);
                }
                else
                {
                    if (edge != 0 && map.Wall(sx - dx, sy - dy, dir) != 0)
                        Piece(layout.Column[9] + off + cornerShift, layout.Row[9], edge, 9);
                    edge = 0;
                }
                off += side > 0 ? -2 : 2;
                sx += dx;
                sy += dy;
            }
        }

        // Far side walls of the squares 0, 1, 2 across: piece 1 (left) or 2 (right); after the first, one column further out.
        void FarSides(int dir, int piece, int step)
        {
            int dx = Facing.DeltaX(dir), dy = Facing.DeltaY(dir);
            int sx = cx, sy = cy, off = 0;
            for (int i = 0; i < 3; i++)
            {
                int w = map.Wall(sx, sy, dir);
                if (w != 0)
                    Piece(layout.Column[piece] + off + (i == 0 ? 0 : step), layout.Row[piece], w, piece);
                off += 2 * step;
                sx += dx;
                sy += dy;
            }
        }

        // Middle and near rows: from the outermost square towards the centre, the front then the side wall.
        void Row(int sx, int sy, int off, int offStep, int dx, int dy, int sideDir, int frontPiece, int sidePiece, int count = 3)
        {
            for (int i = 0; i < count; i++)
            {
                int w = map.Wall(sx, sy, facing);
                if (w != 0)
                    Piece(layout.Column[frontPiece] + off, layout.Row[frontPiece], w, frontPiece);
                w = map.Wall(sx, sy, sideDir);
                if (w != 0)
                    Piece(layout.Column[sidePiece] + off, layout.Row[sidePiece], w, sidePiece);
                off += offStep;
                sx += dx;
                sy += dy;
            }
        }
    }

    /// <summary>
    /// DrawWallPiece: piece <paramref name="piece"/> of a wall type as a block of symbols with its top-left cell at
    /// (col, row). Cells outside the grid are clipped, symbol 0 is not drawn, and the byte index advances for every
    /// cell, drawn or not.
    /// </summary>
    private static void DrawPiece(List<ViewSymbol> o, int col, int row, int wallType, int piece, ViewLayout layout, WallBuffer walls)
    {
        var bytes = walls.Wall(wallType);
        int k = layout.Start[piece];
        for (int r = row; r < row + layout.Height[piece]; r++)
            for (int c = col; c < col + layout.Width[piece]; c++, k++)
                if (r is >= 0 and < Cells && c is >= 0 and < Cells && bytes[k] != 0)
                    o.Add(new ViewSymbol(c, r, bytes[k]));
    }

    /// <summary>
    /// DrawAreaMap: an 11×11 window of the map (clamped to the map's edges: its left/top square is 0..5), one symbol
    /// per square from the area-map block chosen by which of the four faces have a wall (bit 0 N, 1 E, 2 S, 3 W),
    /// then the party arrow (facing / 2) on the party's square. Drawn unmasked.
    /// </summary>
    /// <param name="mapBase">First symbol of the area-map tiles (block 4's base): arrows at +0..3, squares at +4..19.</param>
    public static List<ViewSymbol> ComposeAreaMap(AreaMap map, int x, int y, int facing, int mapBase)
    {
        var o = new List<ViewSymbol>(Cells * Cells + 1);
        int x0 = Math.Clamp(x - 5, 0, 5), y0 = Math.Clamp(y - 5, 0, 5);
        for (int r = 0; r < Cells; r++)
            for (int c = 0; c < Cells; c++)
            {
                int s = 0;
                if (map.Wall(x0 + c, y0 + r, Facing.North) != 0) s |= 1;
                if (map.Wall(x0 + c, y0 + r, Facing.East) != 0) s |= 2;
                if (map.Wall(x0 + c, y0 + r, Facing.South) != 0) s |= 4;
                if (map.Wall(x0 + c, y0 + r, Facing.West) != 0) s |= 8;
                o.Add(new ViewSymbol(c, r, mapBase + 4 + s));
            }
        o.Add(new ViewSymbol(x - x0, y - y0, mapBase + (facing >> 1)));
        return o;
    }
}
