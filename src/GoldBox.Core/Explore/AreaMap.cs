using GoldBox.Core.Doors;

namespace GoldBox.Core.Explore;

/// <summary>
/// A 16×16 3D-area map as the engine holds it in memory (docs/re/geo-format.md "Layout"): four 256-byte planes, walls
/// N/E, walls S/W, event byte, barriers; square (x, y) at y·16 + x. The accessors follow the program's three map
/// readers (wall GAME.OVR 0x2ED72, barrier 0x2EC47, event 0x2EE71): a coordinate off the map wraps to the other side,
/// except while the current script is 0 or 10 (<see cref="Bounded"/>), when an off-map square reads 0.
/// </summary>
public sealed class AreaMap
{
    public const int Size = 16;
    public const int PlaneSize = 256;
    public const int PlanesLength = 4 * PlaneSize;

    private readonly byte[] _planes;

    /// <summary>The map's GEO block id (also the VM's $49C5 after LOADFILES).</summary>
    public int Id { get; }

    /// <summary>
    /// True while the current ECL script is 0 (New Phlan) or 10 (Valhingen Graveyard): squares off the map read as
    /// no wall, no barrier, event 0, instead of wrapping (GAME.OVR 0x2EC5D).
    /// </summary>
    public bool Bounded { get; set; }

    /// <param name="planes">The four planes (1024 bytes, without the GEO block's 2-byte header). Copied.</param>
    public AreaMap(ReadOnlySpan<byte> planes, int id = -1)
    {
        if (planes.Length != PlanesLength)
            throw new ArgumentException($"map planes are {planes.Length} bytes, expected {PlanesLength}", nameof(planes));
        _planes = planes.ToArray();
        Id = id;
    }

    /// <summary>The live planes (the door code edits plane 3; <see cref="DoorMap"/> works on this span).</summary>
    public Span<byte> Planes => _planes;

    /// <summary>The same live planes as an array (shared with the encounter host's <c>MapPlanes</c>, never copied).</summary>
    public byte[] PlanesArray => _planes;

    public static bool InMap(int x, int y) => x is >= 0 and < Size && y is >= 0 and < Size;

    /// <summary>
    /// The accessors' coordinate rule: on the map → itself; off the map → null when <see cref="Bounded"/>, else
    /// "wrapped" the program's way: any x &gt; 15 becomes 0 and any x &lt; 0 becomes 15 (likewise y), not a modulo.
    /// So the 3D view's far row, which looks up to 3 squares sideways, reads x = −2 as 15, not 14 (a quirk kept).
    /// </summary>
    private (int X, int Y)? Resolve(int x, int y)
    {
        if (InMap(x, y))
            return (x, y);
        if (Bounded)
            return null;
        // GetWallType b100:04a6 / GetPassage b100:037b: x > 15 → 0, x < 0 → 15 (likewise y). Not a modulo.
        if (x > 15) x = 0;
        if (x < 0) x = 15;
        if (y > 15) y = 0;
        if (y < 0) y = 15;
        return (x, y);
    }

    /// <summary>Wall type 0..15 of one face (0 = no wall). 1..15 pick a wall picture: slot (t−1)/5 + 1, wall (t−1)%5.</summary>
    public int Wall(int x, int y, int facing)
    {
        if (Resolve(x, y) is not var (cx, cy))
            return 0;
        int i = cy * Size + cx;
        return facing switch
        {
            Facing.North => _planes[i] >> 4,
            Facing.East => _planes[i] & 15,
            Facing.South => _planes[PlaneSize + i] >> 4,
            Facing.West => _planes[PlaneSize + i] & 15,
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "cardinal facings only"),
        };
    }

    /// <summary>
    /// The barrier of one face as the engine reads it: 1 (passable) where the face has no wall, else the stored two
    /// bits (0 impassable, 1 passable, 2 locked door, 3 harder door). Only this face is read (one-way walls).
    /// </summary>
    public DoorBarrier Passage(int x, int y, int facing)
    {
        if (Resolve(x, y) is not var (cx, cy))
            return DoorBarrier.Impassable;
        if (Wall(cx, cy, facing) == 0)
            return DoorBarrier.Passable;
        int shift = facing switch
        {
            Facing.North => 0,
            Facing.East => 2,
            Facing.South => 4,
            Facing.West => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "cardinal facings only"),
        };
        return (DoorBarrier)((_planes[3 * PlaneSize + cy * Size + cx] >> shift) & 3);
    }

    /// <summary>The event byte of a square (plane 2). Bit 7 picks the second sky colour; the rest is the script's.</summary>
    public int Event(int x, int y) => Resolve(x, y) is var (cx, cy) ? _planes[2 * PlaneSize + cy * Size + cx] : 0;
}
