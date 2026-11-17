namespace GoldBox.Core.Explore;

/// <summary>
/// An image of palette indices (EGA 0..15 for the DOS art), row-major. The front end maps indices to colours; nothing
/// here knows a palette or a graphics API.
/// </summary>
public sealed class IndexedFrame(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[width * height];

    /// <summary>The colour the DOS 8×8 symbol pictures treat as see-through when drawn masked (light magenta).</summary>
    public const byte MaskColour = 13;

    public byte this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public void Clear(byte colour = 0) => Array.Fill(Pixels, colour);

    public void FillRect(int x, int y, int w, int h, byte colour)
    {
        int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
        for (int yy = Math.Max(0, y); yy < y1; yy++)
            for (int xx = Math.Max(0, x); xx < x1; xx++)
                Pixels[yy * Width + xx] = colour;
    }

    /// <summary>Draws an 8×8 tile (64 indices). <paramref name="masked"/>: pixels of <see cref="MaskColour"/> are skipped.</summary>
    public void DrawTile(int x, int y, ReadOnlySpan<byte> tile, bool masked)
    {
        if (tile.Length < 64)
            return;
        for (int r = 0; r < 8; r++)
        {
            int yy = y + r;
            if (yy < 0 || yy >= Height)
                continue;
            for (int c = 0; c < 8; c++)
            {
                int xx = x + c;
                byte p = tile[r * 8 + c];
                if (xx < 0 || xx >= Width || (masked && p == MaskColour))
                    continue;
                Pixels[yy * Width + xx] = p;
            }
        }
    }

    /// <summary>Copies a region of another frame into this one.</summary>
    public void Blit(IndexedFrame src, int sx, int sy, int w, int h, int dx, int dy)
    {
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int x = dx + c, y = dy + r;
                if (x >= 0 && y >= 0 && x < Width && y < Height)
                    Pixels[y * Width + x] = src.Pixels[(sy + r) * src.Width + sx + c];
            }
    }
}
