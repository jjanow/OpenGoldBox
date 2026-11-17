namespace GoldBox.Core.Game;

/// <summary>
/// The program tables the combat map reads (docs/re/combat-map.md "Tables"). They are game data of the player's
/// <c>START.EXE</c> data segment and are loaded from it (GoldBox.Data <c>PorCombatTables</c>); only the 9-entry
/// direction deltas (DS:2736 / DS:273F, the same as <see cref="Explore.Facing"/> plus the centre) are written here.
/// </summary>
public sealed class CombatTables
{
    /// <summary>Directions 0–7 (0 N, clockwise) and 8 = the centre (no step).</summary>
    public const int Center = 8;

    /// <summary>DS:2736 / DS:273F: x and y step of directions 0–8 (8 = no step).</summary>
    public static readonly sbyte[] DirDx = [0, 1, 1, 1, 0, -1, -1, -1, 0];
    public static readonly sbyte[] DirDy = [-1, -1, 0, 1, 1, 1, 0, -1, 0];

    /// <summary>Terrain entry size (DS:2744): move cost, sight height, blocking height, tile.</summary>
    public const int TerrainEntrySize = 4;

    /// <param name="terrain">DS:2744, 4 bytes per terrain index: [0] move cost (255 = impassable), [1] sight height of
    /// a viewer standing on it, [2] blocking height, [3] the 24×24 tile drawn.</param>
    /// <param name="iconDelta">DS:2844, 8 sizes × 4 cells × (dx, dy) signed; a negative dx ends the footprint.</param>
    /// <param name="ringDir">DS:02D2, 4 team directions × 4 rings: the 3D direction of each placement ring.</param>
    /// <param name="ringStart">DS:02E2, 4 team directions × 4 rings: the start corner (×2) of each ring.</param>
    /// <param name="quadDir">DS:02F2, 4 bytes: 3D facing ÷ 2 → arena direction.</param>
    /// <param name="startQuad">DS:02F6 (x) and DS:02FE (y): [ring &gt; 0] × 4 corners, signed.</param>
    /// <param name="rowBand">DS:0306, 5 grids × 6 rows × (lo, hi) signed: the open columns of each row.</param>
    /// <param name="wildMap">DS:3648, 44 bytes per row (36 rows): the wilderness terrain codes.</param>
    public CombatTables(byte[] terrain, byte[] iconDelta, byte[] ringDir, byte[] ringStart, byte[] quadDir,
        byte[] startQuadX, byte[] startQuadY, byte[] rowBand, byte[]? wildMap = null)
    {
        Check(terrain, 64 * TerrainEntrySize, nameof(terrain), atLeast: true);
        Check(iconDelta, 64, nameof(iconDelta));
        Check(ringDir, 16, nameof(ringDir));
        Check(ringStart, 16, nameof(ringStart));
        Check(quadDir, 4, nameof(quadDir));
        Check(startQuadX, 8, nameof(startQuadX));
        Check(startQuadY, 8, nameof(startQuadY));
        Check(rowBand, 60, nameof(rowBand));
        Terrain = terrain;
        IconDeltaBytes = iconDelta;
        RingDir = ringDir;
        RingStart = ringStart;
        QuadDir = quadDir;
        StartQuadX = startQuadX;
        StartQuadY = startQuadY;
        RowBand = rowBand;
        WildMap = wildMap;
    }

    private static void Check(byte[] b, int n, string name, bool atLeast = false)
    {
        if (atLeast ? b.Length < n : b.Length != n)
            throw new ArgumentException($"{name}: {b.Length} bytes, expected {(atLeast ? "at least " : "")}{n}", name);
    }

    public byte[] Terrain { get; }
    public byte[] IconDeltaBytes { get; }
    public byte[] RingDir { get; }
    public byte[] RingStart { get; }
    public byte[] QuadDir { get; }
    public byte[] StartQuadX { get; }
    public byte[] StartQuadY { get; }
    public byte[] RowBand { get; }
    public byte[]? WildMap { get; }

    /// <summary>Wilderness map row length (DS:3648 is indexed y × 44 + x).</summary>
    public const int WildMapStride = 44;

    /// <summary>Terrain byte <paramref name="field"/> of terrain index <paramref name="t"/>. Indices past the table read
    /// the bytes that follow it, as the program does (64 and 65 overlap the icon table; <see cref="Terrain"/> holds
    /// them when the loader read that far).</summary>
    public byte TerrainByte(int t, int field) => Terrain[(t & 0xFF) * TerrainEntrySize + field];

    /// <summary>Move cost of a terrain (byte 0; 255 = impassable).</summary>
    public byte Cost(int t) => TerrainByte(t, 0);
    /// <summary>Sight height of a viewer on this terrain (byte 1, WalkLine 0x2FF74).</summary>
    public byte SightHeight(int t) => TerrainByte(t, 1);
    /// <summary>Blocking height (byte 2): a line of sight stops on a cell whose blocking height is above the viewer's.</summary>
    public byte Blocking(int t) => TerrainByte(t, 2);
    /// <summary>Tile drawn for the terrain (byte 3). Tile 22 is open ground (floor, grass).</summary>
    public byte Tile(int t) => TerrainByte(t, 3);

    /// <summary>
    /// 0x3073A: cell <paramref name="idx"/> (0–3) of an icon of size <paramref name="size"/> (record 0x6C AND 7).
    /// False for size 0 or a cell whose dx is negative.
    /// </summary>
    public bool IconCell(int size, int idx, out int dx, out int dy)
    {
        dx = dy = 0;
        if ((byte)size == 0)
            return false;
        int o = ((byte)size << 3) + (idx << 1);
        if (o + 1 >= IconDeltaBytes.Length)
            return false; // [?] sizes past 7 read beyond the table; the callers mask with 7 or 0x7F
        dx = (sbyte)IconDeltaBytes[o];
        dy = (sbyte)IconDeltaBytes[o + 1];
        return dx >= 0;
    }

    public int RingDirection(int teamDir, int ring) => RingDir[teamDir * 4 + ring];
    public int RingStartCorner(int teamDir, int ring) => RingStart[teamDir * 4 + ring];
    public int StartQuadXAt(bool laterRing, int corner) => (sbyte)StartQuadX[(laterRing ? 4 : 0) + corner];
    public int StartQuadYAt(bool laterRing, int corner) => (sbyte)StartQuadY[(laterRing ? 4 : 0) + corner];
    public int RowBandLo(int grid, int row) => (sbyte)RowBand[grid * 12 + row * 2];
    public int RowBandHi(int grid, int row) => (sbyte)RowBand[grid * 12 + row * 2 + 1];

    /// <summary>DS:3648: the wilderness terrain code at (x, y), or 0 without the map. [?] no bounds check in the program.</summary>
    public byte WildTerrain(int x, int y)
    {
        if (WildMap is null)
            return 0;
        int i = y * WildMapStride + x;
        return i >= 0 && i < WildMap.Length ? WildMap[i] : (byte)0;
    }
}
