using GoldBox.Core.Explore;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The Amiga window frame (docs/re/por-art-formats.md §4 "Window frame"; <see cref="PorAmigaFrame"/>): the program's
/// frame routine (hunk 25 +0x16D4) copies a top and a bottom strip, two sides and a corner, each from its own top-left
/// corner, around a window's cells. Under the Amiga art only (as the text font, decided s39); Original DOS and Best, or
/// a module without it, keep the 8X8D frame symbols.
/// </summary>
public sealed class AmigaFrame
{
    private readonly (int W, int H, byte[] Px)[] _pieces;

    private AmigaFrame((int, int, byte[])[] pieces, byte zero) => (_pieces, Zero) = (pieces, zero);

    /// <summary>The screen-palette index the frame's pen 0 lands on (its palette run's first index).</summary>
    public byte Zero { get; }

    /// <summary>The frame under <paramref name="choice"/>, its pens moved into the screen palette's run, or null.</summary>
    public static AmigaFrame? Load(IPorContent? content, PorArtChoice choice, Func<byte[], int> paletteBase)
    {
        if (choice != PorArtChoice.Amiga || content is null)
            return null;
        var pieces = new (int, int, byte[])[5];
        int zero = 0;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (content.Art(PorAmigaFrame.Container, PorAmigaFrame.FirstId + i, choice) is not
                { Variant: PorAmigaArt.Variant, Frames: [{ Items: [var px] } f] } img)
                return null;
            int b = zero = paletteBase(img.PaletteRgb);
            pieces[i] = (f.Width, f.Height, [.. px.Select(v => (byte)(b + v))]);
        }
        return new AmigaFrame(pieces, (byte)zero);
    }

    private void Blit(IndexedFrame f, PorAmigaFrame.Piece piece, int dx, int dy, int w, int h, int sx = 0, int sy = 0)
    {
        var (pw, ph, px) = _pieces[(int)piece];
        for (int y = 0; y < h && sy + y < ph; y++)
            for (int x = 0; x < w && sx + x < pw; x++)
                if (dx + x is var xx && dy + y is var yy && xx >= 0 && yy >= 0 && xx < f.Width && yy < f.Height)
                    f[xx, yy] = px[(sy + y) * pw + sx + x];
    }

    /// <summary>
    /// The frame of window (<paramref name="x0"/>, <paramref name="y0"/>)–(<paramref name="x1"/>, <paramref name="y1"/>)
    /// in text cells, as hunk 25 +0x16D4 blits it: top strip, bottom strip, left side, right side, then the four corners.
    /// </summary>
    public void Window(IndexedFrame f, int x0, int y0, int x1, int y1)
    {
        int w = (x1 - x0 + 1) * 8, h = (y1 - y0 + 1) * 8;
        Blit(f, PorAmigaFrame.Piece.Top, x0 * 8, (y0 - 1) * 8, w, 8);
        Blit(f, PorAmigaFrame.Piece.Bottom, x0 * 8, (y1 + 1) * 8, w, 8);
        Blit(f, PorAmigaFrame.Piece.Left, (x0 - 1) * 8, y0 * 8, 8, h);
        Blit(f, PorAmigaFrame.Piece.Right, (x1 + 1) * 8, y0 * 8, 8, h);
        foreach (var (cx, cy) in new[] { (x0 - 1, y0 - 1), (x1 + 1, y0 - 1), (x0 - 1, y1 + 1), (x1 + 1, y1 + 1) })
            Blit(f, PorAmigaFrame.Piece.Corner, cx * 8, cy * 8, 8, 8);
    }

    /// <summary>
    /// A divider the Client draws cell by cell (no Amiga window routine call read for it, <c>[?]</c>): a strip from its
    /// own start, a side from its own top, a corner piece for a joint.
    /// </summary>
    public void HLine(IndexedFrame f, int row, int col0, int col1) =>
        Blit(f, PorAmigaFrame.Piece.Top, col0 * 8, row * 8, (col1 - col0 + 1) * 8, 8);

    public void VLine(IndexedFrame f, int col, int row0, int row1) =>
        Blit(f, PorAmigaFrame.Piece.Left, col * 8, row0 * 8, 8, (row1 - row0 + 1) * 8);

    public void Corner(IndexedFrame f, int col, int row) => Blit(f, PorAmigaFrame.Piece.Corner, col * 8, row * 8, 8, 8);
}
