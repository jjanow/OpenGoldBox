namespace GoldBox.Core.Explore;

/// <summary>
/// The program's 8×8 symbol space (docs/re/view3d.md "Symbols"): five blocks of tiles numbered globally. Block 0 holds
/// the common tiles (1..45), blocks 1–3 the tiles of wall slots 1–3 (70 each), block 4 the area-map tiles. The
/// block bases and the rebasing threshold are program data read from START.EXE (<c>PorStartExe</c>).
/// </summary>
public sealed class SymbolSet
{
    public const int Blocks = 5;
    public const int TilePixels = 64;

    private readonly int[] _bases;
    private readonly byte[]?[] _tiles = new byte[]?[Blocks];
    private readonly int[] _counts = new int[Blocks];

    /// <summary>First symbol number of blocks 0..4 (DS:2722, five words).</summary>
    public IReadOnlyList<int> Bases => _bases;

    /// <summary>WALLDEF bytes ≥ this value are shifted by the slot's base when a wall set is loaded (DS:272C).</summary>
    public int RebaseThreshold { get; }

    /// <param name="bases">First symbol of blocks 0..4 (5 values, ascending).</param>
    /// <param name="rebaseThreshold">DS:272C.</param>
    public SymbolSet(IReadOnlyList<int> bases, int rebaseThreshold)
    {
        if (bases.Count != Blocks)
            throw new ArgumentException($"{Blocks} block bases expected", nameof(bases));
        for (int b = 1; b < Blocks; b++)
            if (bases[b] <= bases[b - 1])
                throw new FormatException("symbol block bases must ascend");
        if (bases[0] < 1)
            throw new FormatException("symbol 0 is never a tile");
        _bases = [.. bases];
        RebaseThreshold = rebaseThreshold;
    }

    /// <summary>
    /// The program's layout from the tile counts of the data: symbol 0 is "none", the common block holds
    /// <paramref name="commonTiles"/> symbols from 1, each wall slot <paramref name="slotTiles"/>, block 4 follows
    /// slot 3; the rebasing threshold is the last common symbol. With the PoR data (45 common tiles, 70 per wall set)
    /// this gives the program's DS:2722 table 1, 46, 116, 186, 256 and DS:272C = 45, as read in v1.1 and v1.2's
    /// START.EXE (v1.3 packs that part of its data segment).
    /// </summary>
    public static SymbolSet FromCounts(int commonTiles, int slotTiles)
    {
        int b1 = 1 + commonTiles;
        return new SymbolSet([1, b1, b1 + slotTiles, b1 + 2 * slotTiles, b1 + 3 * slotTiles], commonTiles);
    }

    /// <summary>Tiles in block <paramref name="block"/> may number at most the gap to the next block (block 4: 31 by the data).</summary>
    public int Capacity(int block) => block < Blocks - 1 ? _bases[block + 1] - _bases[block] : 255;

    /// <summary>Sets the tiles of one block: <paramref name="pixels"/> holds count × 64 EGA indices, tile after tile.</summary>
    public void SetBlock(int block, byte[]? pixels)
    {
        if (pixels is not null && pixels.Length % TilePixels != 0)
            throw new ArgumentException("tile data must be whole 8×8 tiles", nameof(pixels));
        int n = pixels is null ? 0 : pixels.Length / TilePixels;
        if (n > Capacity(block))
            throw new ArgumentException($"block {block} holds at most {Capacity(block)} tiles, got {n}", nameof(pixels));
        _tiles[block] = pixels;
        _counts[block] = n;
    }

    /// <summary>Delta added to a slot's WALLDEF bytes ≥ <see cref="RebaseThreshold"/>: base(slot) − base(1).</summary>
    public int SlotDelta(int slot) => _bases[slot] - _bases[1];

    /// <summary>Which block a symbol number falls in, or −1 for a number the program rejects ("Bad symbol number").</summary>
    public int BlockOf(int symbol)
    {
        if (symbol <= 0)
            return -1;
        for (int b = Blocks - 1; b >= 0; b--)
            if (symbol >= _bases[b])
                return b == Blocks - 1 && symbol - _bases[b] >= Capacity(b) ? -1 : b;
        return -1;
    }

    /// <summary>
    /// The 64 pixels of a symbol, or an empty span when its block is not loaded or the tile is missing (the program
    /// draws nothing when the block's picture pointer is nil).
    /// </summary>
    public ReadOnlySpan<byte> Tile(int symbol)
    {
        int b = BlockOf(symbol);
        if (b < 0 || _tiles[b] is not { } t)
            return [];
        int i = symbol - _bases[b];
        return i < _counts[b] ? t.AsSpan(i * TilePixels, TilePixels) : [];
    }
}
