using GoldBox.Core.Explore;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>A 24 × 24 picture of frame indices (row-major) with the pixels it covers when drawn.</summary>
public sealed record OverlandPicture(byte[] Pixels, bool[] Opaque);

/// <summary>
/// The overland view's art (docs/re/geo-format.md "Wilderness (overland)", program read s19):
/// <list type="bullet">
/// <item>Tiles: two sheets of 128 tiles, 24 × 24. The game init loads <c>SQRPACI.DAX</c> block 1 into the low sheet
/// (DS:5E52, tiles 0–127) and block 2 into the high sheet (DS:5E56, tiles 128–255) (GAME.OVR 0xEAA9 / 0xEACB). The
/// tile draw 0x31B5D takes tile t &lt; 128 from the low sheet, else t &amp; 0x7F from the high one, drawn whole.</item>
/// <item>Outdoors, LOADFILES (o3 ≠ 255, $49E6 = 0; 0x1A49) copies <c>BACPAC.DAX</c> block 1's 40 tiles over low tiles
/// 0–39 (0x31A8E: start 0; it copies the block's own item count, the 0x28 passed with it is not read). The combat
/// ground loads (0xD82C–0xD86D: DUNGCOM's 25 or WILDCOM's 34 tiles at 0, RANDCOM's 6 at 34) overwrite the same low
/// tiles; BACPAC puts back what they overwrote (its 40 tiles equal SQRPACI's 0–39 in v1.3, tested).</item>
/// <item>The party marker: <c>ICON.DAX</c> blocks 0, 1, 2 and 128, 129, 130 (animation slots 9–11, loaded on the first
/// overland command, 0x17285–0x172D5), colour 0 see-through, then light magenta (13) recoloured to black and kept
/// opaque (0x31E22: the remap tables DS:0CD2 → DS:0CF2). Drawn clipped over the tiles, mirrored for facings above 3
/// (0x31FD2).</item>
/// </list>
/// The view is 120 × 120 pixels: tile (dx, dy) at pixel (24·dx, 24·dy) (character cells 3·dx, 3·dy, 0x2DE39).
/// </summary>
public sealed class PorOverlandArt
{
    public const int SheetTiles = 128, TileCount = 2 * SheetTiles, BacpacTiles = 40;
    private const int Size = Overland.TilePixels;

    private readonly OverlandPicture?[] _tiles;
    private readonly OverlandPicture?[] _outdoor;
    private readonly Dictionary<int, OverlandPicture> _marker;

    /// <param name="tiles">Tiles 0–255 as the game init loads them (null where missing).</param>
    /// <param name="bacpac">The outdoor LOADFILES' tiles for low tiles 0–39 (null or short: the rest stay).</param>
    /// <param name="marker">The marker frames by ICON block (0, 1, 2, 128, 129, 130).</param>
    public PorOverlandArt(IReadOnlyList<OverlandPicture?> tiles, IReadOnlyList<OverlandPicture?>? bacpac,
        IReadOnlyDictionary<int, OverlandPicture> marker)
    {
        _tiles = new OverlandPicture?[TileCount];
        for (int i = 0; i < TileCount && i < tiles.Count; i++)
            _tiles[i] = tiles[i];
        _outdoor = (OverlandPicture?[])_tiles.Clone();
        for (int i = 0; bacpac is not null && i < BacpacTiles && i < bacpac.Count; i++)
            if (bacpac[i] is { } b)
                _outdoor[i] = b;
        _marker = new Dictionary<int, OverlandPicture>(marker);
    }

    /// <summary>Whether the tile sheets were found.</summary>
    public bool HasTiles => _tiles.Any(t => t is not null);

    /// <summary>The ICON blocks of the marker's six frames, in counter order (DS:6DDC 0–5).</summary>
    public static readonly int[] MarkerBlocks = [0, 128, 1, 129, 2, 130];

    /// <summary>The DOS art from the content (SQRPACI 1/2, BACPAC 1, ICON); parts that are missing stay empty.</summary>
    public static PorOverlandArt Load(IPorContent content) =>
        new(Sheets(Tiles(content.Picture("SQRPACI", 1)), Tiles(content.Picture("SQRPACI", 2))),
            Tiles(content.Picture("BACPAC", 1)), Markers(b => content.Picture("ICON", b)));

    /// <summary>The two sheets side by side (128 + 128 tiles; a short sheet leaves its rest empty).</summary>
    public static OverlandPicture?[] Sheets(IReadOnlyList<OverlandPicture?> low, IReadOnlyList<OverlandPicture?> high)
    {
        var all = new OverlandPicture?[TileCount];
        for (int i = 0; i < SheetTiles && i < low.Count; i++)
            all[i] = low[i];
        for (int i = 0; i < SheetTiles && i < high.Count; i++)
            all[SheetTiles + i] = high[i];
        return all;
    }

    /// <summary>A block's 24 × 24 items as opaque tiles (a picture of another size gives none).</summary>
    public static List<OverlandPicture?> Tiles(IReadOnlyList<DaxPicture>? frames) =>
        frames is [{ Width: Size, Height: Size } p]
            ? [.. p.Items.Select(px => (OverlandPicture?)new OverlandPicture(px, Opaque(px.Length)))]
            : [];

    /// <summary>The marker frames: colour 0 see-through, then 13 → 0 kept opaque (the ICON load's remap).</summary>
    public static Dictionary<int, OverlandPicture> Markers(Func<int, IReadOnlyList<DaxPicture>?> block)
    {
        var d = new Dictionary<int, OverlandPicture>();
        foreach (int b in MarkerBlocks)
            if (block(b) is [{ Width: Size, Height: Size, Items.Count: > 0 } p])
                d[b] = MarkerPicture(p.Items[0], 0);
        return d;
    }

    /// <summary>A marker frame from DOS indices, moved to the palette run at <paramref name="paletteBase"/> (0: DOS).</summary>
    public static OverlandPicture MarkerPicture(byte[] px, int paletteBase)
    {
        var o = new byte[px.Length];
        var m = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            m[i] = px[i] != 0;
            byte v = px[i] == IndexedFrame.MaskColour ? (byte)0 : px[i];
            o[i] = paletteBase == 0 || !m[i] ? v : (byte)(paletteBase + v);
        }
        return new OverlandPicture(o, m);
    }

    private static bool[] Opaque(int n)
    {
        var m = new bool[n];
        Array.Fill(m, true);
        return m;
    }

    /// <summary>Tile <paramref name="index"/> as drawn (outdoors: with the BACPAC tiles), or null when missing.</summary>
    public OverlandPicture? Tile(int index, bool outdoor = true) => (outdoor ? _outdoor : _tiles)[index & 0xFF];

    public OverlandPicture? Marker(int block) => _marker.GetValueOrDefault(block);

    /// <summary>
    /// Draws the 120 × 120 view with its top-left pixel at (<paramref name="x"/>, <paramref name="y"/>): the 25 tile
    /// indices (<see cref="Overland.ComposeView"/>), then the marker frame of <paramref name="counter"/> at
    /// <paramref name="marker"/> (<see cref="Overland.MarkerCell"/>). A missing tile is drawn black.
    /// </summary>
    public void Draw(IndexedFrame f, int x, int y, byte[] tiles, (int Col, int Row) marker, int counter, int facing,
        bool outdoor = true, IReadOnlyList<int>? slots = null)
    {
        for (int i = 0; i < tiles.Length && i < Overland.ViewTiles * Overland.ViewTiles; i++)
        {
            int tx = x + Size * (i % Overland.ViewTiles), ty = y + Size * (i / Overland.ViewTiles);
            if (Tile(tiles[i], outdoor) is { } t)
                Blit(f, t, tx, ty, false, x, y);
            else
                f.FillRect(tx, ty, Size, Size, 0);
        }
        DrawMarker(f, x, y, marker, counter, facing, slots);
    }

    /// <summary>The marker frame of <paramref name="counter"/> alone, over what the view at (x, y) holds; the animation
    /// slots hold <paramref name="slots"/> (<see cref="AreaLoop.MarkerSlots"/>; null: as loaded on entry).</summary>
    public void DrawMarker(IndexedFrame f, int x, int y, (int Col, int Row) marker, int counter, int facing,
        IReadOnlyList<int>? slots = null)
    {
        var (block, mirrored) = Overland.MarkerFrame(((counter % 6) + 6) % 6, facing, slots);
        if (Marker(block) is { } m)
            Blit(f, m, x + Size * marker.Col, y + Size * marker.Row, mirrored, x, y);
    }

    /// <summary>
    /// The 120 × 120 view of the party's overland square now (memory <paramref name="m"/>, view mode column
    /// <paramref name="column"/>), with marker frame <paramref name="counter"/>.
    /// </summary>
    public IndexedFrame Render(Core.Ecl.EclMemory m, OverlandMap map, int column, int counter)
    {
        var f = new IndexedFrame(Overland.ViewPixels, Overland.ViewPixels);
        Draw(f, 0, 0, m, map, column, counter);
        return f;
    }

    /// <summary>
    /// The view as the area loop left it (<see cref="AreaLoop.MarkerCounter"/>, DS:6DDC): the last redraw's frame
    /// (counter − 1), or while the overland interface is still to be drawn (<see cref="AreaLoop.OverlandInterfacePending"/>)
    /// the interface's: frame counter with the tiles, frame counter + 1 over it.
    /// </summary>
    public IndexedFrame Render(AreaLoop loop, OverlandMap map, int column)
    {
        var f = new IndexedFrame(Overland.ViewPixels, Overland.ViewPixels);
        Draw(f, 0, 0, loop, map, column);
        return f;
    }

    /// <summary>Draws <see cref="Render(AreaLoop, OverlandMap, int)"/>'s view at (x, y).</summary>
    public void Draw(IndexedFrame f, int x, int y, AreaLoop loop, OverlandMap map, int column)
    {
        var m = loop.Memory;
        int c = loop.MarkerCounter;
        if (!loop.OverlandInterfacePending)
        {
            Draw(f, x, y, m, map, column, c + 5, loop.MarkerSlots);
            return;
        }
        // The interface loads the animation slots afresh first (0x17285).
        Draw(f, x, y, m, map, column, c);
        var (px, py) = Overland.Position(m);
        DrawMarker(f, x, y, Overland.MarkerCell(column, px, py), c + 1, m.Position.Facing);
    }

    /// <summary>Draws the party's view now at (x, y) (see <see cref="Render"/>).</summary>
    public void Draw(IndexedFrame f, int x, int y, Core.Ecl.EclMemory m, OverlandMap map, int column, int counter,
        IReadOnlyList<int>? slots = null)
    {
        var (px, py) = Overland.Position(m);
        var (left, top) = Overland.ViewOrigin(column, px, py);
        Draw(f, x, y, Overland.ComposeView(map, left, top, m.Read), Overland.MarkerCell(column, px, py), counter,
            m.Position.Facing, slots: slots);
    }

    /// <summary>Draws a picture's opaque pixels, mirrored when asked, clipped to the view at (x0, y0).</summary>
    private static void Blit(IndexedFrame f, OverlandPicture p, int x, int y, bool mirrored, int x0, int y0)
    {
        for (int r = 0; r < Size; r++)
        {
            int yy = y + r;
            if (yy < y0 || yy >= y0 + Overland.ViewPixels || yy < 0 || yy >= f.Height)
                continue;
            for (int c = 0; c < Size; c++)
            {
                int xx = x + c;
                if (xx < x0 || xx >= x0 + Overland.ViewPixels || xx < 0 || xx >= f.Width)
                    continue;
                int i = r * Size + (mirrored ? Size - 1 - c : c);
                if (p.Opaque[i])
                    f[xx, yy] = p.Pixels[i];
            }
        }
    }
}
