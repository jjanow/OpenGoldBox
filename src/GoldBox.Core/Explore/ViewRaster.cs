namespace GoldBox.Core.Explore;

/// <summary>The four backdrop colours of the 3D view: sky, two horizon bands, ground (EGA indices).</summary>
public readonly record struct Backdrop(byte Sky, byte Horizon1, byte Horizon2, byte Ground);

/// <summary>Turns composed symbol lists (<see cref="View3D"/>) into pixels of an <see cref="IndexedFrame"/>.</summary>
public static class ViewRaster
{
    public const int ViewPixels = View3D.Cells * 8; // 88

    /// <summary>
    /// DrawBackdrop: four full-width bands over the 88 rows of the view: sky rows 0–42, horizon 1 row 43, horizon 2
    /// rows 44–45, ground rows 46–87 (FillPictureRect calls in Draw3DView's backdrop routine, docs/re/view3d.md).
    /// </summary>
    public static void DrawBackdrop(IndexedFrame f, int ox, int oy, Backdrop b)
    {
        f.FillRect(ox, oy, ViewPixels, 43, b.Sky);
        f.FillRect(ox, oy + 43, ViewPixels, 1, b.Horizon1);
        f.FillRect(ox, oy + 44, ViewPixels, 2, b.Horizon2);
        f.FillRect(ox, oy + 46, ViewPixels, 42, b.Ground);
    }

    /// <summary>Draws symbols at view cells, the view's top-left pixel at (ox, oy). Missing tiles draw nothing.</summary>
    public static void DrawSymbols(IndexedFrame f, int ox, int oy, IEnumerable<ViewSymbol> symbols, SymbolSet tiles, bool masked)
    {
        foreach (var s in symbols)
            f.DrawTile(ox + s.Column * 8, oy + s.Row * 8, tiles.Tile(s.Symbol), masked);
    }

    /// <summary>The 3D view: backdrop, then the wall symbols masked.</summary>
    public static void Draw3D(IndexedFrame f, int ox, int oy, Backdrop b, IEnumerable<ViewSymbol> symbols, SymbolSet tiles)
    {
        DrawBackdrop(f, ox, oy, b);
        DrawSymbols(f, ox, oy, symbols, tiles, masked: true);
    }

    /// <summary>The area map: its symbols drawn unmasked.</summary>
    public static void DrawAreaMap(IndexedFrame f, int ox, int oy, IEnumerable<ViewSymbol> symbols, SymbolSet tiles) =>
        DrawSymbols(f, ox, oy, symbols, tiles, masked: false);
}
