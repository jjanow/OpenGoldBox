using GoldBox.Core.Explore;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// The 3D view's art from the player's PoR DOS folder (docs/re/view3d.md): the program tables from START.EXE, the
/// 8×8 symbol blocks from the 8X8D files, the wall slots from WALLDEF, and the text font. Art source: DOS EGA (the
/// Amiga walls are the same drawings recoloured, PLAN.md 4.2; walls and UI stay DOS for now).
/// </summary>
public sealed class PorViewArt
{
    /// <summary>Blocks of 8X8D1.DAX that hold the font, the area-map tiles and the common tiles (only in 8X8D1).</summary>
    public const int FontBlock = 201, AreaMapBlock = 202, CommonBlock = 203;

    private readonly PorGameDirectory _dir;

    public PorStartExe Program { get; }
    public SymbolSet Symbols { get; }
    public WallBuffer Walls { get; } = new();
    public TextFont Font { get; }
    public ViewLayout Layout => Program.Layout;

    /// <summary>Requests the loader could not follow (see <see cref="LoadWallSet"/>).</summary>
    public List<string> Diagnostics { get; } = [];

    private PorViewArt(PorGameDirectory dir, PorStartExe program, SymbolSet symbols, TextFont font)
    {
        _dir = dir;
        Program = program;
        Symbols = symbols;
        Font = font;
    }

    public static PorViewArt Load(PorGameDirectory dir)
    {
        var exe = PorStartExe.Open(dir.Find("START.EXE") ?? throw new FileNotFoundException($"START.EXE not in {dir.Root}"));
        var d1 = dir.Dax("8X8D1.DAX") ?? throw new FileNotFoundException($"8X8D1.DAX not in {dir.Root}");
        var common = Tiles(d1, CommonBlock);
        var map = Tiles(d1, AreaMapBlock);
        // A wall set's tile count fixes the size of each wall slot's symbol block (70 in PoR); 8X8D1 block 1 is the
        // first one-block wall set, present on every disk.
        var firstWall = Tiles(d1, 1);
        var symbols = SymbolSet.FromCounts(common.Length / SymbolSet.TilePixels, firstWall.Length / SymbolSet.TilePixels);
        symbols.SetBlock(0, common);
        symbols.SetBlock(4, map);
        var font = new TextFont(d1.Block(FontBlock) ?? throw new FormatException("8X8D1.DAX has no font block 201"));
        return new PorViewArt(dir, exe, symbols, font);
    }

    private static byte[] Tiles(DaxFile f, int id) =>
        DaxPicture.Decode(f.Block(id) ?? throw new FormatException($"{System.IO.Path.GetFileName(f.Path)} has no block {id}")).AsTiles();

    /// <summary>
    /// LoadWallSet for <c>WALLDEF&lt;disk&gt;.DAX</c> record <paramref name="set"/> into <paramref name="slot"/>, with its
    /// tiles: a one-block record id i uses 8X8D block i; a k-block record uses 8X8D blocks i·10+1 … i·10+k, id 0
    /// counted as 10 (geo-format.md "WALLDEF / 8X8D"). Null empties the slot.
    /// </summary>
    public void LoadWallSet(int disk, int slot, int? set)
    {
        if (set is not { } id)
        {
            Walls.Clear(slot);
            Symbols.SetBlock(slot, null);
            return;
        }
        var walldef = _dir.Dax($"WALLDEF{disk}.DAX") ?? throw new FileNotFoundException($"WALLDEF{disk}.DAX not in {_dir.Root}");
        var tiles = _dir.Dax($"8X8D{disk}.DAX") ?? throw new FileNotFoundException($"8X8D{disk}.DAX not in {_dir.Root}");
        if (walldef.Block(id) is not { } record)
        {
            // The program stops with a fatal error here, so in play the request never names a missing block: the
            // area expects the two-flag rule ($49E7/$49E8 set by earlier play) that skips slot 2 after a two-block
            // set. Entered with fresh memory (no save) the flags are clear; the slot keeps what it holds. [?]
            Diagnostics.Add($"WALLDEF{disk}.DAX has no block {id} for slot {slot}: slot left as it was");
            return;
        }
        int blocks = Walls.Load(slot, id, record, Symbols);
        for (int f = 1; f <= blocks; f++)
        {
            int tileId = blocks == 1 ? id : (id == 0 ? 10 : id) * 10 + f;
            Symbols.SetBlock(slot + f - 1, Tiles(tiles, tileId));
        }
    }
}
