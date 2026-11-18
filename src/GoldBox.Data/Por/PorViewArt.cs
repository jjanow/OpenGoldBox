using GoldBox.Core.Explore;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// The 3D view's art (docs/re/view3d.md) from the player's PoR DOS folder or a module: the program's view tables, the
/// 8×8 symbol blocks from the 8X8D files, the wall slots from WALLDEF, and the text font. Art source: DOS EGA, unless
/// <see cref="UseArt"/> picks another variant (the Amiga 8x8d tiles are the same drawings recoloured, in the same
/// order: docs/re/por-art-formats.md §3). Wall layouts (WALLDEF) and the font are always DOS.
/// </summary>
public sealed class PorViewArt
{
    /// <summary>Blocks of 8X8D1.DAX that hold the font, the area-map tiles and the common tiles (only in 8X8D1).</summary>
    public const int FontBlock = 201, AreaMapBlock = 202, CommonBlock = 203;

    private readonly IPorContent _dir;
    private readonly object _sync = new();

    /// <summary>The 8X8D blocks each symbol block holds (file, id), so a change of art can load them again.</summary>
    private readonly (string File, int Id)?[] _blockSource = new (string, int)?[SymbolSet.Blocks];

    /// <summary>The art the tiles come from (<see cref="UseArt"/>); DOS unless changed.</summary>
    public PorArtChoice Choice { get; private set; } = PorArtChoice.Dos;

    private Func<byte[], int>? _paletteBase;

    public PorViewTables Program { get; }
    public SymbolSet Symbols { get; }
    public WallBuffer Walls { get; } = new();
    public TextFont Font { get; }
    public ViewLayout Layout => Program.Layout;

    /// <summary>Requests the loader could not follow (see <see cref="LoadWallSet"/>).</summary>
    public List<string> Diagnostics { get; } = [];

    private PorViewArt(IPorContent dir, PorViewTables program, SymbolSet symbols, TextFont font)
    {
        _dir = dir;
        Program = program;
        Symbols = symbols;
        Font = font;
    }

    public static PorViewArt Load(IPorContent dir)
    {
        var exe = (dir.Program() ?? throw new FileNotFoundException($"START.EXE not in {dir.Origin}")).View
            ?? throw new FormatException("START.EXE: wall-piece table not found (not Pool of Radiance DOS?)");
        var common = Tiles(dir, "8X8D1", CommonBlock);
        var map = Tiles(dir, "8X8D1", AreaMapBlock);
        // A wall set's tile count fixes the size of each wall slot's symbol block (70 in PoR); 8X8D1 block 1 is the
        // first one-block wall set, present on every disk.
        var firstWall = Tiles(dir, "8X8D1", 1);
        var symbols = SymbolSet.FromCounts(common.Length / SymbolSet.TilePixels, firstWall.Length / SymbolSet.TilePixels);
        symbols.SetBlock(0, common);
        symbols.SetBlock(4, map);
        var font = new TextFont(dir.ArtData("8X8D1", FontBlock) ?? throw new FormatException($"8X8D1.DAX has no font block 201 in {dir.Origin}"));
        var art = new PorViewArt(dir, exe, symbols, font);
        art._blockSource[0] = ("8X8D1", CommonBlock);
        art._blockSource[4] = ("8X8D1", AreaMapBlock);
        return art;
    }

    private static byte[] Tiles(IPorContent dir, string file, int id) =>
        (dir.Picture(file, id) is [var p] ? p : throw new FormatException($"{file}.DAX has no picture block {id} in {dir.Origin}")).AsTiles();

    /// <summary>
    /// The tiles of an 8X8D block in the chosen art: DOS as stored; another variant's tiles (Amiga) with index 0, the
    /// Amiga's see-through colour (it stands where DOS has <see cref="IndexedFrame.MaskColour"/> 13 on every tile of
    /// the 23 wall sets and the common block), turned into 13, and every other index i into
    /// <paramref name="paletteBase"/>(palette) + i, so the frame keeps one byte per pixel. The Amiga holds one more
    /// (blank) tile in blocks 202 and 203 than DOS; only DOS's count is taken (the others match tile for tile).
    /// A variant tile set smaller than DOS's falls back to DOS.
    /// </summary>
    public static byte[] Tiles(IPorContent dir, string file, int id, PorArtChoice choice, Func<byte[], int>? paletteBase)
    {
        var dos = Tiles(dir, file, id);
        if (choice == PorArtChoice.Dos || paletteBase is null
            || dir.Art(file, id, choice) is not { Variant: not "dos", Frames: [var p] } img
            || p.Width != 8 || p.Height != 8 || p.Items.Count * SymbolSet.TilePixels < dos.Length)
            return dos;
        int b = paletteBase(img.PaletteRgb);
        var o = new byte[dos.Length];
        for (int i = 0; i < o.Length; i++)
        {
            byte v = p.Items[i / SymbolSet.TilePixels][i % SymbolSet.TilePixels];
            o[i] = v == 0 ? IndexedFrame.MaskColour : (byte)(b + v);
        }
        return o;
    }

    /// <summary>
    /// Draws the symbols from <paramref name="choice"/>'s art from now on (and loads the blocks held now again).
    /// <paramref name="paletteBase"/> gives the screen-palette index where a 16-colour palette (RGB triplets) starts.
    /// </summary>
    public void UseArt(PorArtChoice choice, Func<byte[], int>? paletteBase)
    {
        lock (_sync)
        {
            Choice = choice;
            _paletteBase = paletteBase;
            for (int b = 0; b < SymbolSet.Blocks; b++)
                if (_blockSource[b] is var (file, id))
                    Symbols.SetBlock(b, Tiles(_dir, file, id, Choice, _paletteBase));
        }
    }

    /// <summary>
    /// LoadWallSet for <c>WALLDEF&lt;disk&gt;.DAX</c> record <paramref name="set"/> into <paramref name="slot"/>, with its
    /// tiles: a one-block record id i uses 8X8D block i; a k-block record uses 8X8D blocks i·10+1 … i·10+k, id 0
    /// counted as 10 (geo-format.md "WALLDEF / 8X8D"). Null empties the slot.
    /// </summary>
    public void LoadWallSet(int disk, int slot, int? set)
    {
        lock (_sync)
            LoadWallSetLocked(disk, slot, set);
    }

    private void LoadWallSetLocked(int disk, int slot, int? set)
    {
        if (set is not { } id)
        {
            Walls.Clear(slot);
            Symbols.SetBlock(slot, null);
            _blockSource[slot] = null;
            return;
        }
        if (_dir.ArtData($"WALLDEF{disk}", id) is not { } record)
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
            _blockSource[slot + f - 1] = ($"8X8D{disk}", tileId);
            Symbols.SetBlock(slot + f - 1, Tiles(_dir, $"8X8D{disk}", tileId, Choice, _paletteBase));
        }
    }
}
