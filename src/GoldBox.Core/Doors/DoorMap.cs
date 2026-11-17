namespace GoldBox.Core.Doors;

/// <summary>
/// The in-memory 3D map as the door and step code reads and edits it: four 256-byte planes (walls N/E, walls S/W,
/// event, barrier), row-major, square (x, y) at y·16 + x (docs/re/geo-format.md). Facing uses the 8-way code
/// 0 N, 2 E, 4 S, 6 W.
/// </summary>
public static class DoorMap
{
    public const int Size = 16;
    public const int PlaneSize = 256;
    public const int BarrierPlane = 3 * PlaneSize;

    /// <summary>Step deltas per facing 0..7 (DS:2736 x, DS:273F y).</summary>
    private static readonly sbyte[] Dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly sbyte[] Dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static int DeltaX(int facing) => Dx[facing & 7];
    public static int DeltaY(int facing) => Dy[facing & 7];

    private static int Shift(int facing) => facing switch
    {
        0 => 0,
        2 => 2,
        4 => 4,
        6 => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "cardinal facings only (0, 2, 4, 6)"),
    };

    /// <summary>Wall type (0..15) of one face (accessor GAME.OVR 0x2ED72). Coordinates must be on the map.</summary>
    public static int Wall(ReadOnlySpan<byte> planes, int x, int y, int facing)
    {
        byte b = facing is 0 or 2 ? planes[y * Size + x] : planes[PlaneSize + y * Size + x];
        return facing is 0 or 4 ? b >> 4 : b & 15;
    }

    /// <summary>
    /// Barrier of one face as the engine sees it (accessor 0x2EC47): 1 (passable) when the face has no wall, else
    /// the stored 2 bits. Only this face is read: the face of the square on the other side never counts, which is
    /// what makes a one-sided wall one-way. Coordinates must be on the map ([?] the off-map wrap and the
    /// block-0/10 rule of the accessor are not modelled here).
    /// </summary>
    public static DoorBarrier FaceBarrier(ReadOnlySpan<byte> planes, int x, int y, int facing)
    {
        if (Wall(planes, x, y, facing) == 0)
            return DoorBarrier.Passable;
        return (DoorBarrier)((planes[BarrierPlane + y * Size + x] >> Shift(facing)) & 3);
    }

    /// <summary>
    /// The forward key's edge test (GAME.OVR 0x16D58), the value it leaves in VM $6DD5: 1 when the party's own face is
    /// not impassable (passable, a door, or no wall) and the step would leave the 16×16 grid; else 0. An impassable
    /// face on the map edge therefore leaves $6DD5 = 0 (docs/re/ecl-format.md "Steps, doors and script switches").
    /// </summary>
    public static bool LeavesMap(ReadOnlySpan<byte> planes, int x, int y, int facing)
    {
        if (FaceBarrier(planes, x, y, facing) == DoorBarrier.Impassable)
            return false;
        int nx = x + DeltaX(facing), ny = y + DeltaY(facing);
        return nx is < 0 or >= Size || ny is < 0 or >= Size;
    }

    /// <summary>Forward step test (0x17469): the step is taken directly only when the own face reads passable.</summary>
    public static bool CanStep(ReadOnlySpan<byte> planes, int x, int y, int facing) =>
        FaceBarrier(planes, x, y, facing) == DoorBarrier.Passable;

    /// <summary>Sets one face's stored barrier (0x167D4 with 1). Off-map squares are left alone (no wrap).</summary>
    private static void SetFace(Span<byte> planes, int x, int y, int facing, int value)
    {
        if (x is < 0 or >= Size || y is < 0 or >= Size)
            return;
        int s = Shift(facing);
        ref byte b = ref planes[BarrierPlane + y * Size + x];
        b = (byte)((b & ~(3 << s)) | (value << s));
    }

    /// <summary>
    /// Opens a door (0x16B4F / 0x16C1B): the party's face and the facing face of the next square become 1. The
    /// neighbour is not wrapped: at the map edge only the own face changes.
    /// </summary>
    public static void Open(Span<byte> planes, int x, int y, int facing)
    {
        SetFace(planes, x, y, facing, 1);
        SetFace(planes, x + DeltaX(facing), y + DeltaY(facing), (facing + 4) % 8, 1);
    }

    /// <summary>When the menu would offer only Exit, the party's face becomes impassable (0x166CB; own face only).</summary>
    public static void Seal(Span<byte> planes, int x, int y, int facing)
    {
        int s = Shift(facing);
        ref byte b = ref planes[BarrierPlane + y * Size + x];
        b = (byte)(b & ~(3 << s));
    }
}
