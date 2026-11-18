namespace GoldBox.Core.Explore;

/// <summary>
/// One special location of the overland (docs/re/geo-format.md "Wilderness (overland)"): a square whose drawn tile is
/// replaced while a rule says so. Values as stored (16-bit words); <see cref="Tile"/> is 1-based like the table's.
/// </summary>
public readonly record struct OverlandSite(ushort X, ushort Y, ushort Tile);

/// <summary>
/// PoR's overland (the wilderness tile map; docs/module-format.md §6, docs/re/geo-format.md): a grid of 1-based tile
/// numbers, row-major (x east, y south), plus the special locations in their stored order (the rules key them by their
/// 1-based index). The values are stored as the program holds them; the draw subtracts 1 (GAME.OVR 0x2DE87).
/// </summary>
public sealed class OverlandMap
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>The tile numbers as stored, <see cref="Width"/> × <see cref="Height"/> bytes.</summary>
    public byte[] Tiles { get; }
    public IReadOnlyList<OverlandSite> Sites { get; }

    public OverlandMap(int width, int height, byte[] tiles, IReadOnlyList<OverlandSite> sites)
    {
        if (width <= 0 || height <= 0 || tiles.Length != width * height)
            throw new ArgumentException($"overland: {tiles.Length} tiles for {width} × {height}", nameof(tiles));
        Width = width;
        Height = height;
        Tiles = tiles;
        Sites = sites;
    }

    /// <summary>The stored tile number at (x, y) (1-based).</summary>
    public byte Tile(int x, int y) => Tiles[y * Width + x];

    /// <summary>
    /// The tile index the overland view draws at (x, y) (GAME.OVR 0x2DE39, the 5 × 5 window): the table's value − 1,
    /// replaced by a special location's tile − 1 when the location is on (x, y) and its rule holds; the locations are
    /// tried in index order 1–9 and a later one wins. The rules read VM cells (<paramref name="cell"/>, 16-bit) as
    /// traced; what the cells mean is `[?]`:
    /// <list type="bullet">
    /// <item>1, 2: never (the routine has no arm for them);</item>
    /// <item>3: $4A8C ≠ 0xFF;</item>
    /// <item>4: $4AD2 ≠ 0, or $4A7C bit 3 set, or $4A9F = 0;</item>
    /// <item>5: $4AA0 bit 1 clear; 7: $4AA0 bit 0 clear;</item>
    /// <item>6: always;</item>
    /// <item>8, 9: $4AB3 &gt; 0xFD.</item>
    /// </list>
    /// The result is a byte, as the program pushes it (a stored 0 draws 255; a stored 256 draws 255).
    /// </summary>
    public byte DrawnTile(int x, int y, Func<ushort, ushort> cell)
    {
        byte tile = (byte)(Tile(x, y) - 1);
        for (int i = 1; i <= Math.Min(Sites.Count, SiteRuleCount); i++)
        {
            var s = Sites[i - 1];
            if (s.X != x || s.Y != y || !SiteShown(i, cell))
                continue;
            tile = (byte)(s.Tile - 1);
        }
        return tile;
    }

    /// <summary>The number of special locations the draw routine tries (its loop runs 1–9).</summary>
    public const int SiteRuleCount = 9;

    /// <summary>VM cells the special-location rules read (GAME.OVR 0x2DE39; zone 0 + 2 × (cell − $4900)).</summary>
    public const ushort Cell4A8C = 0x4A8C, Cell4AD2 = 0x4AD2, Cell4A7C = 0x4A7C, Cell4A9F = 0x4A9F, Cell4AA0 = 0x4AA0,
        Cell4AB3 = 0x4AB3;

    /// <summary>Whether special location <paramref name="index"/> (1-based) is drawn (see <see cref="DrawnTile"/>).</summary>
    public static bool SiteShown(int index, Func<ushort, ushort> cell) => index switch
    {
        3 => cell(Cell4A8C) != 0xFF,
        4 => cell(Cell4AD2) != 0 || (cell(Cell4A7C) & 8) != 0 || cell(Cell4A9F) == 0,
        5 => (cell(Cell4AA0) & 2) == 0,
        6 => true,
        7 => (cell(Cell4AA0) & 1) == 0,
        8 or 9 => cell(Cell4AB3) > 0xFD,
        _ => false,
    };
}
