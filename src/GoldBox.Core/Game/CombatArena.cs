using GoldBox.Core.Doors;
using GoldBox.Core.Explore;

namespace GoldBox.Core.Game;

/// <summary>
/// The combat arena, the 0x4E9-byte block at DS:6716 (docs/re/combat-map.md "The arena"): a 7-byte header (+2/+3
/// the view's upper-left cell, +4 cursor shown, +5 cursor size, +6 line-walk mode) and 25 rows × 50 columns of
/// terrain indices at +7 (cell (x, y) at +7 + y × 50 + x). Allocated by 0xD815 and not cleared: every cell is then
/// written by the dungeon builder (0xCDE1) or filled by the wilderness builder (0xD785).
/// </summary>
public sealed class CombatArena
{
    public const int Width = 50, Height = 25, HeaderSize = 7, Size = HeaderSize + Width * Height;

    /// <summary>The block as the program holds it.</summary>
    public byte[] Bytes { get; } = new byte[Size];

    /// <summary>+2 / +3: the upper-left arena cell of the 7×7 view (signed).</summary>
    public sbyte ViewX { get => (sbyte)Bytes[2]; set => Bytes[2] = (byte)value; }
    public sbyte ViewY { get => (sbyte)Bytes[3]; set => Bytes[3] = (byte)value; }
    /// <summary>+4 cursor shown, +5 cursor size.</summary>
    public byte CursorOn { get => Bytes[4]; set => Bytes[4] = value; }
    public byte CursorSize { get => Bytes[5]; set => Bytes[5] = value; }
    /// <summary>+6: 0 = a line stops at blocking terrain; 1 = terrain ignored (the distance routine START 0x30FB).</summary>
    public byte LineMode { get => Bytes[6]; set => Bytes[6] = value; }

    public static bool InArena(int x, int y) => x is >= 0 and < Width && y is >= 0 and < Height;

    /// <summary>Terrain index of a cell; (x, y) must be in the arena.</summary>
    public byte this[int x, int y]
    {
        get => Bytes[HeaderSize + y * Width + x];
        set => Bytes[HeaderSize + y * Width + x] = value;
    }

    /// <summary>The 25 × 50 cells (a copy), row by row.</summary>
    public byte[] Cells() => Bytes.AsSpan(HeaderSize).ToArray();

    /// <summary>0xD815 after the allocation: cursor off, cursor size 1, line mode 0.</summary>
    public void ResetHeader()
    {
        CursorOn = 0;
        CursorSize = 1;
        LineMode = 0;
    }
}

/// <summary>
/// How the arena is built (fight start, GAME.OVR 0xD815; docs/re/combat-map.md "Building the arena"): from the 3D
/// area around the party (0xCDE1) or from the wilderness map (0xD785), then the rare features (0xC5D1).
/// Dice are <c>count × (random(sides) + 1)</c> (0x2BCCD), drawn in the order written here.
/// </summary>
public static class ArenaBuilder
{
    /// <summary>Terrain the dungeon builder plots: open floor (tile 22 + 1).</summary>
    public const byte Floor = 23;
    /// <summary>Terrain indices with special meaning: 0x1E the cloud of an area spell, 0x1F a body.</summary>
    public const byte Cloud = 0x1E, Body = 0x1F;

    /// <summary>
    /// 0xC5D1: every cell whose tile is 22 (open ground), columns 0–49 outer, rows 0–24 inner, rolls 1d100: 98 →
    /// terrain 26, 99 → 27, 100 → unless the current ECL block (DS:84DC) is 10, 1d100 and on a 1 a 1d10: 1–6 → 28,
    /// 7–10 → 29.
    /// </summary>
    public static void SprinkleRareFeatures(CombatArena arena, CombatTables t, int eclBlock, Func<ushort, ushort> random)
    {
        for (int x = 0; x < CombatArena.Width; x++)
            for (int y = 0; y < CombatArena.Height; y++)
            {
                if (t.Tile(arena[x, y]) != 22)
                    continue;
                byte roll = FightRules.Dice(random, 1, 100);
                if (roll == 98)
                    arena[x, y] = 26;
                else if (roll == 99)
                    arena[x, y] = 27;
                else if (roll == 100 && eclBlock != 10 && FightRules.Dice(random, 1, 100) == 1)
                {
                    byte r = FightRules.Dice(random, 1, 10);
                    if (r is >= 1 and <= 6)
                        arena[x, y] = 28;
                    else if (r is >= 7 and <= 10)
                        arena[x, y] = 29;
                }
            }
    }

    /// <summary>
    /// 0xC77E: the wall between 3D square (x, y) and its neighbour in cardinal direction <paramref name="dir"/>, from
    /// both sides OR-ed: 0 open, 1 solid, 3 a drawn wall that can be passed or opened. One side (0xC6FC): on the
    /// 16×16 map, barrier 0 (0x2EC47: impassable) → 1, else wall type 0 (0x2ED72) → 0, else 3; off the map, the
    /// east and west faces on the party's row (DS:6AAE) are open (0), every other face solid (1).
    /// </summary>
    public static int WallBetween(AreaMap map, int partyY, int x, int y, int dir)
    {
        int opp = (dir + 4) % 8;
        return WallKind(map, partyY, x, y, dir) | WallKind(map, partyY, x + CombatTables.DirDx[dir], y + CombatTables.DirDy[dir], opp);
    }

    private static int WallKind(AreaMap map, int partyY, int x, int y, int dir)
    {
        x = (sbyte)(byte)x;
        y = (sbyte)(byte)y;
        if (x is >= 0 and <= 15 && y is >= 0 and <= 15)
        {
            if (map.Passage(x, y, dir) == DoorBarrier.Impassable)
                return 1;
            return map.Wall(x, y, dir) == 0 ? 0 : 3;
        }
        return y == (sbyte)(byte)partyY && (dir == Facing.East || dir == Facing.West) ? 0 : 1;
    }

    /// <summary>
    /// 0xCDE1, the arena of a 3D area: the 13 × 5 squares around the party (dy −2..2 outer, dx −6..6 inner) each become
    /// a skewed block, spot (i, j) of square (dx, dy) at arena (6·dx + 5·dy + 21 + i, 5·dy + 10 + j), clipped; a spot
    /// gets terrain t + 1. Per square, in this order: floor (rows 2–4, columns 0–5) and the west wall (0xC88B), the north
    /// wall band (0xC948), the north-west corner (0xC9E0), the north-east corner (0xCBE6). Then
    /// <see cref="SprinkleRareFeatures"/>. North, east and west use <see cref="WallBetween"/>; the corners also look
    /// at the walls of the neighbouring squares.
    /// </summary>
    public static void BuildDungeon(CombatArena arena, CombatTables t, AreaMap map, int partyX, int partyY, int eclBlock,
        Func<ushort, ushort> random)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                int cx = (sbyte)(byte)(partyX + dx), cy = (sbyte)(byte)(partyY + dy);
                int wallW = WallBetween(map, partyY, cx, cy, Facing.West);
                int wallN = WallBetween(map, partyY, cx, cy, Facing.North);
                int wallE = WallBetween(map, partyY, cx, cy, Facing.East);
                void Plot(int i, int j, int terrain)
                {
                    int x = (sbyte)(byte)(dx * 6 + dy * 5 + 21 + i), y = (sbyte)(byte)(dy * 5 + 10 + j);
                    if (CombatArena.InArena(x, y))
                        arena[x, y] = (byte)(terrain + 1);
                }
                // 0xC88B: floor and the west wall.
                for (int r = 2; r <= 4; r++)
                    for (int c = 0; c <= 5; c++)
                        Plot(c, r, 22);
                if (wallW == 1)
                    for (int r = 2; r <= 4; r++)
                    {
                        Plot(r - 1, r, 4);
                        Plot(r, r, 3);
                        Plot(r + 1, r, 13);
                    }
                else if (wallW == 3)
                {
                    Plot(1, 2, 8);
                    Plot(5, 4, 0);
                }
                // 0xC948: the north wall band.
                int n0 = wallN == 1 ? 5 : 22, n1 = wallN == 1 ? 10 : 22;
                Plot(3, 0, n0);
                Plot(4, 0, n0);
                Plot(3, 1, n1);
                Plot(4, 1, n1);
                CornerNorthWest(map, partyY, cx, cy, wallN, wallW, Plot);
                CornerNorthEast(map, partyY, cx, cy, wallN, wallE, Plot);
            }
        SprinkleRareFeatures(arena, t, eclBlock, random);
    }

    /// <summary>0xC9E0. "open" = no wall west of the square to the north and none north of the square to the west.
    /// [?] Values stay unset (heap or stack residue) for wall kinds the switches don't name; WallBetween only gives 0, 1, 3.</summary>
    private static void CornerNorthWest(AreaMap map, int partyY, int cx, int cy, int wallN, int wallW, Action<int, int, int> plot)
    {
        bool open = WallBetween(map, partyY, cx, cy - 1, Facing.West) == 0 && WallBetween(map, partyY, cx - 1, cy, Facing.North) == 0;
        int topLeft = 0, topRight = 0, bottomLeft = 0, bottomRight = 0;
        if (wallN == 0)
            topLeft = wallW switch { 0 => 22, 3 => 13, 1 => open ? 0 : 13, _ => topLeft };
        else if (wallN is 3 or 1)
            topLeft = wallW == 0 ? (open ? 15 : 5) : (open ? 18 : 2);
        topRight = wallN switch { 0 => 22, 3 => 17, 1 => 5, _ => topRight };
        bottomLeft = wallW switch
        {
            0 => wallN == 0 ? 22 : open ? 16 : 10,
            3 => open ? 20 : 7,
            1 => open ? 1 : 3,
            _ => bottomLeft,
        };
        if (wallW is 0 or 3)
            bottomRight = wallN switch { 0 => 22, 3 => 23, 1 => 10, _ => bottomRight };
        else if (wallW == 1)
            bottomRight = wallN switch { 0 => 13, 3 => 21, 1 => 6, _ => bottomRight };
        plot(1, 0, topLeft);
        plot(2, 0, topRight);
        plot(1, 1, bottomLeft);
        plot(2, 1, bottomRight);
    }

    /// <summary>0xCBE6. kN = the east wall of the square to the north, kE = the north wall of the square to the east.</summary>
    private static void CornerNorthEast(AreaMap map, int partyY, int cx, int cy, int wallN, int wallE, Action<int, int, int> plot)
    {
        int kN = WallBetween(map, partyY, cx, cy - 1, Facing.East);
        int kE = WallBetween(map, partyY, cx + 1, cy, Facing.North);
        bool open = kN == 0 && kE == 0;
        int topLeft = 0, topRight = 0, bottomLeft = 0, bottomRight = 0;
        topLeft = wallN switch { 0 => kN == 1 ? 4 : 22, 3 => 15, 1 => 5, _ => topLeft };
        if (wallN == 0)
            topRight = kN switch
            {
                0 => 22,
                3 => wallE == 0 && kE != 0 ? 24 : 1,
                1 => wallE == 0 && kE != 0 ? 11 : 3,
                _ => topRight,
            };
        else
            topRight = wallE != 0 ? 9 : kE != 0 ? 5 : open ? 17 : 19;
        bottomLeft = wallN switch { 0 => 22, 3 => 16, 1 => 10, _ => bottomLeft };
        if (wallN == 0)
            bottomRight = kN == 0 ? 22 : wallE != 0 ? 4 : kE == 0 ? 8 : 12;
        else
            bottomRight = wallE != 0 ? 14 : kE == 0 ? 23 : 10;
        plot(5, 0, topLeft);
        plot(6, 0, topRight);
        plot(5, 1, bottomLeft);
        plot(6, 1, bottomRight);
    }

    // ---- Wilderness --------------------------------------------------------------------------------------------

    /// <summary>
    /// 0xCE75: the feature class of a wilderness terrain code (DS:465A), a bit mask: 1 roads/towns, 2 open land,
    /// 4 light woods/hills, 8 forest, 0x10 / 0x20 river chances, 0x40 rough, 0x80 swamp. Once VM $4AB3 = 255 a class with
    /// bit 7 loses it and gains bit 0. The first matching arm wins (some codes are listed twice); a code no arm names
    /// leaves the class unset [?] (stack residue; taken as 0).
    /// </summary>
    public static byte TerrainClass(byte code, bool yarashDone)
    {
        static bool In(byte v, params (int Lo, int Hi)[] r) => r.Any(p => v >= p.Lo && v <= p.Hi);
        byte cls = code switch
        {
            _ when In(code, (1, 2), (40, 41), (79, 86), (91, 92), (43, 43), (241, 241)) => 0x01,
            _ when In(code, (168, 177)) => 0x21,
            _ when In(code, (185, 185), (188, 192)) => 0x11,
            _ when In(code, (3, 4), (6, 7), (8, 9), (19, 21), (23, 25)) => 0x03,
            _ when In(code, (3, 4), (7, 7), (10, 18), (22, 22), (26, 36), (201, 208)) => 0x02,
            _ when In(code, (209, 213)) => 0x12,
            _ when In(code, (37, 37), (57, 57), (64, 64), (154, 158), (164, 165), (180, 180), (243, 243), (239, 240), (250, 250), (252, 252)) => 0x04,
            _ when In(code, (39, 44), (153, 153), (159, 159)) => 0x05,
            _ when In(code, (45, 46), (51, 56), (67, 77), (93, 97)) => 0x44,
            _ when In(code, (181, 184), (186, 186), (193, 200)) => 0x14,
            _ when In(code, (47, 50), (98, 101), (242, 242)) => 0x40,
            _ when In(code, (38, 38)) => 0x41,
            _ when In(code, (178, 179)) => 0x60,
            _ when In(code, (58, 60), (65, 66)) => 0x48,
            _ when In(code, (61, 63), (121, 124), (135, 137), (139, 139), (141, 141), (144, 147), (149, 149), (151, 152),
                (160, 162), (166, 167), (214, 216), (238, 238)) => 0x08,
            _ when In(code, (87, 90), (163, 163), (224, 224)) => 0x88,
            _ when In(code, (125, 125), (124, 124), (138, 138), (140, 140), (142, 143), (148, 148), (150, 150)) => 0x09,
            _ when In(code, (126, 133), (229, 231)) => 0x28,
            _ when In(code, (217, 223)) => 0x18,
            _ when In(code, (78, 78), (245, 245), (232, 233)) => 0x20,
            _ when In(code, (109, 109), (244, 246)) => 0x80,
            _ when In(code, (110, 113), (115, 120)) => 0x81,
            _ when In(code, (102, 108), (114, 114)) => 0x90,
            _ when In(code, (225, 228), (234, 237)) => 0xA0,
            _ => 0,
        };
        if ((cls & 0x80) != 0 && yarashDone)
            cls = (byte)((cls - 0x80) | 1);
        return cls;
    }

    /// <summary>
    /// 0xD785, the arena of the wilderness: every cell 23 (open ground); the terrain code under the party
    /// (DS:3648[y][x], x + 13 when DS:49FA = 3, + 26 when 4) gives the class (<see cref="TerrainClass"/>); then the
    /// river (0xD1AF), trees (0xD2E4), features (0xD46F) and rare features (0xC5D1), each drawing in that order.
    /// </summary>
    public static void BuildWilderness(CombatArena arena, CombatTables t, int partyX, int partyY, int viewMode,
        bool yarashDone, int eclBlock, Func<ushort, ushort> random)
    {
        Array.Fill(arena.Bytes, Floor, CombatArena.HeaderSize, CombatArena.Width * CombatArena.Height);
        int mx = (sbyte)(byte)partyX, my = (sbyte)(byte)partyY;
        if (viewMode == 3) mx = (sbyte)(byte)(mx + 13);
        if (viewMode == 4) mx = (sbyte)(byte)(mx + 26);
        byte cls = TerrainClass(t.WildTerrain(mx, my), yarashDone);
        PlaceRiver(arena, cls, random);
        PlaceTrees(arena, t, cls, random);
        ScatterFeatures(arena, t, cls, random);
        SprinkleRareFeatures(arena, t, eclBlock, random);
    }

    /// <summary>
    /// 0xD1AF: a river on 1d100 ≤ 35 with class bit 0x20, ≤ 75 with bit 0x10 (none otherwise: the roll is still made
    /// against 0). Start column 34 − 5d4, moved left until (column + 2) mod 7 = 0; each row 0–24, while the column is
    /// ≤ 49, terrain 50 and 51 at column and column + 1, and a ford (52, 53) when 1d20 = 1 or the ford count is odd;
    /// the column then moves right. No ford at all: row 12 − 1d9 + 4 gets one at start + row, and the row below one
    /// further right.
    /// </summary>
    private static void PlaceRiver(CombatArena a, byte cls, Func<ushort, ushort> random)
    {
        int thresh = 0, fords = 0;
        if ((cls & 0x20) != 0) thresh = 35;
        if ((cls & 0x10) != 0) thresh = 75;
        if (FightRules.Dice(random, 1, 100) > thresh)
            return;
        int col = (sbyte)(byte)(34 - FightRules.Dice(random, 5, 4));
        while ((col + 2) % 7 > 0)
            col = (sbyte)(byte)(col - 1);
        int start = col;
        void Ford(int r, int c)
        {
            Set(a, c, r, 52);
            Set(a, c + 1, r, 53);
            fords++;
        }
        for (int row = 0; row <= 24; row++)
        {
            if (col > 49)
                continue;
            Set(a, col, row, 50);
            Set(a, col + 1, row, 51);
            if (FightRules.Dice(random, 1, 20) == 1 || (fords & 1) != 0)
                Ford(row, col);
            col++;
        }
        if (fords == 0)
        {
            int row = (sbyte)(byte)(12 - FightRules.Dice(random, 1, 9) + 4);
            int c = (sbyte)(byte)(start + row);
            Ford(row, c);
            Ford(row + 1, c + 1);
        }
    }

    /// <summary>A cell write at +7 + y × 50 + x with no bounds check, as the program does; cells outside the block are
    /// dropped here [?] (the program would write the heap after it).</summary>
    private static void Set(CombatArena a, int x, int y, byte v)
    {
        int i = CombatArena.HeaderSize + y * CombatArena.Width + x;
        if (i >= CombatArena.HeaderSize && i < CombatArena.Size)
            a.Bytes[i] = v;
    }

    private static byte At(CombatArena a, int x, int y)
    {
        int i = CombatArena.HeaderSize + y * CombatArena.Width + x;
        return i >= 0 && i < CombatArena.Size ? a.Bytes[i] : (byte)0;
    }

    /// <summary>
    /// 0xD2E4: density 1 %, 3 % with class bit 4, 4 % with 0x40, 7 % with 8, 0 % with 0x80 (later bits win). Columns
    /// 0–49 outer, rows 1–24 inner: a cell of tile 22 with tile 22 above rolls 1d100 ≤ density (the roll is made for
    /// every such cell); a tree: h = 1d10, 9–10 → 1d2 + 4, else (h + 1) div 2; with class 0x40 h = 1d3 + 3 instead
    /// (an extra roll). Terrain h + 35; below height 5 the cell above gets h + 31.
    /// </summary>
    private static void PlaceTrees(CombatArena a, CombatTables t, byte cls, Func<ushort, ushort> random)
    {
        int dens = 1;
        if ((cls & 0x02) != 0) dens = 1;
        if ((cls & 0x04) != 0) dens = 3;
        if ((cls & 0x40) != 0) dens = 4;
        if ((cls & 0x08) != 0) dens = 7;
        if ((cls & 0x80) != 0) dens = 0;
        for (int col = 0; col <= 49; col++)
            for (int row = 1; row <= 24; row++)
            {
                if (t.Tile(a[col, row]) != 22 || t.Tile(a[col, row - 1]) != 22 || FightRules.Dice(random, 1, 100) > dens)
                    continue;
                int h = FightRules.Dice(random, 1, 10);
                h = h > 8 ? FightRules.Dice(random, 1, 2) + 4 : (h + 1) / 2;
                if ((cls & 0x40) != 0)
                    h = FightRules.Dice(random, 1, 3) + 3;
                a[col, row] = (byte)(h + 35);
                if (h < 5)
                    a[col, row - 1] = (byte)(h + 31);
            }
    }

    /// <summary>
    /// 0xD46F: bands swamp / rocks / bushes / scrub / mounds from the class (defaults 0, 6, 15, 0 and 40; bit 2: 45 and
    /// 10; bit 4: bushes 25; 0x40: mounds 5, scrub 30; 0x80: swamp 15, mounds 25, bushes 10). Every tile-22 cell,
    /// columns outer, rows inner, rolls 1d255 against the stacked bands; each band makes one more roll for the kind.
    /// </summary>
    private static void ScatterFeatures(CombatArena a, CombatTables t, byte cls, Func<ushort, ushort> random)
    {
        int tSwamp = 0, tA = 6, tB = 15, tC = 40, tD = 0;
        if ((cls & 0x02) != 0) { tB = 45; tC = 10; }
        if ((cls & 0x04) != 0) tB = 25;
        if ((cls & 0x40) != 0) { tC = 5; tD = 30; }
        if ((cls & 0x80) != 0) { tSwamp = 15; tC = 25; tB = 10; }
        for (int col = 0; col <= 49; col++)
            for (int row = 0; row <= 24; row++)
            {
                if (t.Tile(a[col, row]) != 22)
                    continue;
                int r = FightRules.Dice(random, 1, 255);
                if (r <= tSwamp)
                {
                    r = FightRules.Dice(random, 1, 4);
                    if (r == 4 && row > 0 && t.Tile(At(a, col, row - 1)) == 22)
                    {
                        a[col, row - 1] = 65;
                        a[col, row] = 64;
                    }
                    if (r < 4)
                        a[col, row] = (byte)(r + 60);
                }
                else if (r <= tA + tSwamp)
                    a[col, row] = (byte)(FightRules.Dice(random, 1, 3) + 54);
                else if (r <= tB + tSwamp + tA)
                    a[col, row] = (byte)(FightRules.Dice(random, 1, 4) + 41);
                else if (r <= tD + tB + tSwamp + tA)
                {
                    r = (FightRules.Dice(random, 1, 10) - 1) / 3 + 1;
                    a[col, row] = (byte)(r + 56);
                }
                else if (r <= tA + (tC + tD + tB + tSwamp))
                {
                    r = FightRules.Dice(random, 1, 4);
                    if (r == 4 && (cls & 0x40) == 0)
                        r = FightRules.Dice(random, 1, 3);
                    a[col, row] = (byte)(r + 45);
                }
            }
    }
}
