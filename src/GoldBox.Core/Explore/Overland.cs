using GoldBox.Core.Ecl;

namespace GoldBox.Core.Explore;

/// <summary>What an overland step did (GAME.OVR 0x16EE3).</summary>
public enum OverlandStep
{
    /// <summary>The party moved ($49C3/$49C4 written) and 12 hours passed.</summary>
    Moved,
    /// <summary>An impassable tile ahead: nothing happens (no message, no time).</summary>
    Blocked,
    /// <summary>A river tile while $4AA2 = 0: the river message (cs:0x7F4, colour 14), no move, no time.</summary>
    River,
}

/// <summary>
/// PoR DOS v1.3's overland (wilderness travel; docs/re/geo-format.md "Wilderness (overland)", program read s19): the
/// rules of view modes 2–4 (DS:49FA, set by the script switch 0x3EFA for blocks 25 / 26 / 27 with $49E6 = 0). The
/// party's overland square is VM $49C3 (x, 0–15 within the script's 16 columns) and $49C4 (y, 0–35); the facing is the
/// 3D facing DS:6AAF (<see cref="EclPartyPosition.Facing"/>). The map is <see cref="OverlandMap"/>; x = 0 of a script
/// sits at the column DS:3C76[mode] (0 / 13 / 26 in v1.3; a module's <c>overland.column</c>).
/// </summary>
public static class Overland
{
    /// <summary>VM cells: the overland x and y ([DS:49D2]+0x186 / +0x188), the river flag ([DS:49D2]+0x344).</summary>
    public const ushort CellX = 0x49C3, CellY = 0x49C4, RiverCell = 0x4AA2;

    /// <summary>The view modes of the three wilderness windows (DS:49FA).</summary>
    public const int FirstMode = 2, LastMode = 4;

    /// <summary>The move clamps x to 0–15 and y to 0–35 (0x16F2F–0x16F53).</summary>
    public const int MaxX = 15, MaxY = 35;

    /// <summary>The view: 5 × 5 tiles of 24 × 24 pixels (0x2DE39, each tile at character cell (3·dx, 3·dy)).</summary>
    public const int ViewTiles = 5, TilePixels = 24, ViewPixels = ViewTiles * TilePixels;

    /// <summary>The window's top-left column and row are clamped to 0–39 and 0–31 (0x2DD9B–0x2DDF0).</summary>
    public const int MaxLeft = 39, MaxTop = 31;

    /// <summary>A step takes 12 hours (clock 0x241A1 with count 12, unit 3; 0x1700F), with or without Search.</summary>
    public const int StepHours = 12;

    public static bool IsMode(int mode) => mode is >= FirstMode and <= LastMode;

    /// <summary>
    /// The command routine (0x17054) and the step (0x17438) take their overland branch: DS:49FA is 2–4. Only the view
    /// mode counts here; $49E6 is not read.
    /// </summary>
    public static bool Commands(EclMemory m) => IsMode(m.ViewMode);

    /// <summary>
    /// The redraw (0x2DCF4) draws the overland: DS:49FA is 2–4 and $49E6 = 0 ($49E6 ≠ 0 makes it draw the 3D view,
    /// 0x2DD00–0x2DD0E).
    /// </summary>
    public static bool Shown(EclMemory m) => IsMode(m.ViewMode) && m.Read(AreaLoop.View3DCell) == 0;

    /// <summary>
    /// Tiles the move refuses outright (0x16F86–0x16FC0, compares on the stored 1-based tile number): 0x4E, 0x7E–0x80,
    /// 0x82–0x84, 0xA8–0xAC, 0xAE–0xB3, 0xBC, 0xDD–0xDE, 0xE1–0xE9, 0xF5.
    /// </summary>
    public static bool Impassable(byte tile) => tile is 0x4E or >= 0x7E and <= 0x80 or >= 0x82 and <= 0x84
        or >= 0xA8 and <= 0xAC or >= 0xAE and <= 0xB3 or 0xBC or >= 0xDD and <= 0xDE or >= 0xE1 and <= 0xE9 or 0xF5;

    /// <summary>River tiles (0x16FC4–0x16FCE): 0x66–0x6C and 0x72, entered only while $4AA2 ≠ 0.</summary>
    public static bool River(byte tile) => tile is >= 0x66 and <= 0x6C or 0x72;

    /// <summary>The overland x and y now (VM $49C3 / $49C4, words).</summary>
    public static (int X, int Y) Position(EclMemory m) => (m.Read(CellX), m.Read(CellY));

    /// <summary>
    /// The overland step (GAME.OVR 0x16EE3; called by the step 0x17438 when DS:49FA is 2–4 and $6DC9 &lt; 255):
    /// x + DS:2736[facing] and y + DS:273F[facing] taken as signed bytes and clamped to 0–15 and 0–35; the stored tile
    /// at (column + x, y) decides: <see cref="Impassable"/> refuses; a <see cref="River"/> tile while $4AA2 = 0 refuses
    /// with the river message; otherwise $49C3/$49C4 = x, y (sign-extended words) and the clock advances 12 hours
    /// (<paramref name="clock"/>(unit, count)). The routine clamps, it does not refuse: a step off the window's edge
    /// reads the edge square itself and "moves" there (12 hours). In play the wilderness scripts' vector 0 cancels such
    /// steps first ($6DC9 = 255 by facing near the edges; DOSBox-X: north from 0,0 of script 25 takes no time).
    /// </summary>
    public static OverlandStep Step(EclMemory m, OverlandMap map, int column, Action<int, int> clock)
    {
        int f = m.Position.Facing;
        sbyte x = (sbyte)(m.Read(CellX) + Facing.DeltaX(f));
        sbyte y = (sbyte)(m.Read(CellY) + Facing.DeltaY(f));
        x = (sbyte)Math.Clamp((int)x, 0, MaxX);
        y = (sbyte)Math.Clamp((int)y, 0, MaxY);
        int col = (sbyte)(x + column);
        byte tile = map.Tile(col, y);
        if (Impassable(tile))
            return OverlandStep.Blocked;
        if (River(tile) && m.Read(RiverCell) == 0)
            return OverlandStep.River;
        m.Write(CellX, (ushort)(short)x);
        m.Write(CellY, (ushort)(short)y);
        clock(GameClock.Hours, StepHours);
        return OverlandStep.Moved;
    }

    /// <summary>
    /// The window's top-left square (0x2DD9B): column + x − 2 and y − 2, as signed bytes, clamped to 0–39 and 0–31.
    /// </summary>
    public static (int Left, int Top) ViewOrigin(int column, int x, int y) =>
        (Math.Clamp((int)(sbyte)(column + x - 2), 0, MaxLeft), Math.Clamp((int)(sbyte)(y - 2), 0, MaxTop));

    /// <summary>
    /// The party marker's square in the window (0x2DB8C): column c = DS:3C76[mode] + x (signed byte) → c below 2 as it
    /// is, then 2 below 42, 3 below 43, else 4; row from y (unsigned word) → y below 2 as it is, 2 below 34, 3 below
    /// 35, else 4. (Equivalent to the party's square minus <see cref="ViewOrigin"/> on the 44 × 36 map.)
    /// </summary>
    public static (int Col, int Row) MarkerCell(int column, int x, int y)
    {
        int c = (sbyte)(column + x);
        c = c < 2 ? c : c < 42 ? 2 : c < 43 ? 3 : 4;
        int r = (ushort)y;
        r = r < 2 ? r : r < 34 ? 2 : r < 35 ? 3 : 4;
        return (c, r);
    }

    /// <summary>
    /// The 25 tile indices the view draws (row-major, 5 × 5) for the window at <paramref name="left"/>,
    /// <paramref name="top"/>: <see cref="OverlandMap.DrawnTile"/> of each square (stored tile − 1, or a special
    /// location's), as the program pushes it (a byte; ≥ 128 is the second tile sheet).
    /// </summary>
    public static byte[] ComposeView(OverlandMap map, int left, int top, Func<ushort, ushort> cell)
    {
        var t = new byte[ViewTiles * ViewTiles];
        for (int dy = 0; dy < ViewTiles; dy++)
            for (int dx = 0; dx < ViewTiles; dx++)
                t[dy * ViewTiles + dx] = map.DrawnTile(left + dx, top + dy, cell);
        return t;
    }

    /// <summary>
    /// The marker's animation counter DS:6DDC (0–5): each overland redraw draws ICON block (counter / 2) (+ 128 when
    /// the counter is odd), then adds 1 mod 6 (0x2DC18–0x2DC42). Facings above 3 (S, W) draw the frame mirrored
    /// (0x31FD2). [?] The counter's value at a load (0 assumed: the game init's) and every redraw that advances it
    /// between commands (CALL $2C90 redraws too) are not traced; the Client advances it once per redraw it shows.
    /// </summary>
    public static (int IconBlock, bool Mirrored) MarkerFrame(int counter, int facing) =>
        (counter / 2 + (counter % 2 == 1 ? 128 : 0), facing > 3);

    /// <summary>
    /// The position line's x and y in an overland mode (START 0x34C2–0x34F3): $49C3 + 13 in mode 3, + 26 in mode 4
    /// (immediates in the code, not DS:3C76), y = $49C4; both as bytes. Mode 1: the 3D x, y.
    /// </summary>
    public static (int X, int Y) StatusPosition(EclMemory m)
    {
        if (m.ViewMode <= 1)
            return (m.Position.X, m.Position.Y);
        int x = (byte)m.Read(CellX), y = (byte)m.Read(CellY);
        if (m.ViewMode == 3)
            x = (byte)(x + 13);
        if (m.ViewMode == 4)
            x = (byte)(x + 26);
        return (x, y);
    }
}
