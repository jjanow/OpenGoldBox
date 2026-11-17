namespace GoldBox.Core.Explore;

/// <summary>
/// Where the ten pieces of a wall picture sit in the 156-byte wall record and in the 3D view (docs/re/view3d.md). The
/// values are tables of the game program; the importer reads them from the player's START.EXE
/// (<c>GoldBox.Data.Por.PorStartExe</c>), so none are written into this code. The view is an 11×11 grid of 8×8 cells.
/// </summary>
public sealed class ViewLayout
{
    public const int Pieces = 10;
    public const int WallRecordSize = 156;

    /// <summary>First byte of each piece inside a wall's 156 bytes (DS:0C8C).</summary>
    public IReadOnlyList<int> Start { get; }
    /// <summary>Width of each piece in cells (DS:0C96).</summary>
    public IReadOnlyList<int> Width { get; }
    /// <summary>Height of each piece in cells (DS:0CA0).</summary>
    public IReadOnlyList<int> Height { get; }
    /// <summary>View column of each piece's left edge, before the per-square offset (DS:0CAA, words).</summary>
    public IReadOnlyList<int> Column { get; }
    /// <summary>View row of each piece's top edge (DS:0CBE, words).</summary>
    public IReadOnlyList<int> Row { get; }

    public ViewLayout(IReadOnlyList<int> start, IReadOnlyList<int> width, IReadOnlyList<int> height,
        IReadOnlyList<int> column, IReadOnlyList<int> row)
    {
        Start = Check(start, nameof(start));
        Width = Check(width, nameof(width));
        Height = Check(height, nameof(height));
        Column = Check(column, nameof(column));
        Row = Check(row, nameof(row));
        // The pieces must tile the wall record exactly, in order: start[0] = 0, start[p+1] = start[p] + w·h, Σ = 156.
        int k = 0;
        for (int p = 0; p < Pieces; p++)
        {
            if (Start[p] != k)
                throw new FormatException($"wall piece {p} starts at {Start[p]}, expected {k}");
            if (Width[p] is < 1 or > 11 || Height[p] is < 1 or > 11)
                throw new FormatException($"wall piece {p} is {Width[p]}×{Height[p]} cells");
            k += Width[p] * Height[p];
        }
        if (k != WallRecordSize)
            throw new FormatException($"wall pieces cover {k} bytes, expected {WallRecordSize}");
    }

    private static int[] Check(IReadOnlyList<int> v, string name) =>
        v.Count == Pieces ? [.. v] : throw new ArgumentException($"{name}: {Pieces} values expected", name);
}
