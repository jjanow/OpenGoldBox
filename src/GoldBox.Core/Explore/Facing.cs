namespace GoldBox.Core.Explore;

/// <summary>
/// The Gold Box 8-way facing code (docs/re/geo-format.md "Coordinates and orientation"): 0 N, 2 E, 4 S, 6 W; odd
/// values are diagonals and are not used on 3D maps. x grows east, y grows south.
/// </summary>
public static class Facing
{
    public const int North = 0, East = 2, South = 4, West = 6;

    /// <summary>Step deltas per facing 0..7 (the program's tables DS:2736 x, DS:273F y; geo-format.md).</summary>
    private static readonly sbyte[] Dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly sbyte[] Dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static int DeltaX(int facing) => Dx[facing & 7];
    public static int DeltaY(int facing) => Dy[facing & 7];

    /// <summary>Left turn (the Left key: facing + 6 mod 8).</summary>
    public static int Left(int facing) => (facing + 6) & 7;
    /// <summary>Right turn (the Right key: facing + 2 mod 8).</summary>
    public static int Right(int facing) => (facing + 2) & 7;
    /// <summary>About-face (the Down key: facing + 4 mod 8).</summary>
    public static int Back(int facing) => (facing + 4) & 7;

    public static bool IsCardinal(int facing) => facing is North or East or South or West;

    /// <summary>The letter the status line shows (N, E, S, W).</summary>
    public static char Letter(int facing) => facing switch
    {
        North => 'N',
        East => 'E',
        South => 'S',
        West => 'W',
        _ => '?',
    };
}
