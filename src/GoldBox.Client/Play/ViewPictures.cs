using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>What the 88×88 picture window shows during an encounter or a PICTURE command.</summary>
public enum ViewPictureKind
{
    /// <summary>A <c>PIC&lt;disk&gt;</c> block (the close-up), drawn over the whole window.</summary>
    Picture,
    /// <summary>A <c>HEAD&lt;disk&gt;</c> block over rows 0–39 and a <c>BODY&lt;disk&gt;</c> block over rows 40–87.</summary>
    Portrait,
    /// <summary>A <c>SPRIT&lt;disk&gt;</c> frame over the 3D view (the monster at distance 1–2).</summary>
    Sprite,
}

/// <summary>
/// An encounter picture request: <paramref name="Id"/> is the PIC, BODY or SPRIT block, <paramref name="Head"/> the
/// HEAD block of a portrait, <paramref name="Distance"/> the encounter distance ($6DC1), <paramref name="Disk"/> the
/// disk digit of the file names (DS:5376).
/// </summary>
public sealed record ViewPicture(ViewPictureKind Kind, int Id, int? Head, int Distance, int Disk);

/// <summary>
/// Draws <see cref="ViewPicture"/>s into the picture window from the content, in the chosen art (DOS, or a variant
/// with its own palette moved into a <see cref="ScreenPalette"/> run, DOS where the variant has no such block).
/// docs/re/ecl-format.md (SETUPMON): at distance 0 the close-up, from PIC or head + body; at distance 1–2 the sprite.
/// [?] Only frame 0 of an animated picture is drawn (the later frames are XOR deltas against frame 0 on both
/// platforms; the timing is not read). [?] The sprite frame for a distance (frame distance − 1, the nearest first) and
/// the frame's place (its block x, y in 8-pixel units inside the window) are from the art, not from the program. A
/// portrait draws both parts from one variant (one palette): when the variant lacks either part, both are DOS. The
/// Amiga HEAD is 56 px wide with no position; it is drawn at x = 16 [?] (outline matching, por-art-formats.md §3).
/// </summary>
public sealed class ViewPictures
{
    public const int Window = 88, HeadRows = 40;

    /// <summary>Where the Amiga HEAD goes in the window [?] (por-art-formats.md §3: best outline match 15–17 px, 16 most often).</summary>
    public const int AmigaHeadX = 16;

    private readonly IPorContent? _content;
    private readonly Dictionary<(string, int, int), PorArtImage?> _cache = [];
    private PorArtChoice _choice;
    private Func<byte[], int>? _paletteBase;

    public ViewPictures(IPorContent? content, PorArtChoice choice = PorArtChoice.Dos, Func<byte[], int>? paletteBase = null)
    {
        _content = content;
        UseArt(choice, paletteBase);
    }

    public PorArtChoice Choice => _choice;

    /// <summary>Takes the pictures from <paramref name="choice"/>'s art from now on.</summary>
    public void UseArt(PorArtChoice choice, Func<byte[], int>? paletteBase)
    {
        lock (_cache)
        {
            _choice = paletteBase is null ? PorArtChoice.Dos : choice;
            _paletteBase = paletteBase;
            _cache.Clear();
        }
    }

    /// <summary>
    /// The block in the chosen art from the file of <paramref name="disk"/>, else from the first disk that has it (the
    /// disks hold copies; the Amiga files serve every disk), or null.
    /// </summary>
    public PorArtImage? Find(string kind, int id, int disk, PorArtChoice? choice = null)
    {
        var c = choice ?? _choice;
        lock (_cache)
        {
            if (_cache.TryGetValue((kind, id, (int)c), out var hit))
                return hit;
            PorArtImage? img = null;
            if (_content is not null)
                foreach (int d in new[] { disk }.Concat(Enumerable.Range(1, 8)))
                    if (d is >= 1 and <= 8 && (img = _content.Art($"{kind}{d}", id, c)) is not null)
                        break;
            _cache[(kind, id, (int)c)] = img;
            return img;
        }
    }

    /// <summary>
    /// Draws <paramref name="v"/> into the window whose top-left pixel is (<paramref name="x"/>, <paramref name="y"/>);
    /// false (nothing drawn) when the content has no such art.
    /// </summary>
    public bool Draw(IndexedFrame f, int x, int y, ViewPicture v)
    {
        switch (v.Kind)
        {
            case ViewPictureKind.Picture:
            {
                if (Find("PIC", v.Id, v.Disk) is not { Frames: [var p, ..] } img)
                    return false;
                int b = Base(img);
                f.FillRect(x, y, Window, Window, (byte)b);
                Blit(f, x, y, 0, 0, p, b, transparent: false);
                return true;
            }
            case ViewPictureKind.Portrait:
            {
                var head = Find("HEAD", v.Head ?? 0, v.Disk);
                var body = Find("BODY", v.Id, v.Disk);
                if (head is null || body is null || head.Variant != body.Variant)
                {
                    head = Find("HEAD", v.Head ?? 0, v.Disk, PorArtChoice.Dos);
                    body = Find("BODY", v.Id, v.Disk, PorArtChoice.Dos);
                }
                if (head is not { Frames: [var h, ..] } || body is not { Frames: [var bd, ..] })
                    return false;
                int b = Base(head);
                f.FillRect(x, y, Window, Window, (byte)b);
                int hx = head.Variant == "dos" ? h.X * 8 : AmigaHeadX;
                Blit(f, x, y, hx, 0, h, b, transparent: false, clipRows: HeadRows);
                Blit(f, x, y, bd.X * 8, HeadRows, bd, Base(body), transparent: false);
                return true;
            }
            case ViewPictureKind.Sprite:
            {
                if (Find("SPRIT", v.Id, v.Disk) is not { Frames.Count: > 0 } img)
                    return false;
                var p = img.Frames[SpriteFrame(v.Distance, img.Frames.Count)];
                Blit(f, x, y, p.X * 8, p.Y * 8, p, Base(img), transparent: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>[?] The sprite frame for an encounter distance: distance − 1 (frame 0 is the nearest), within the frames.</summary>
    public static int SpriteFrame(int distance, int frames) => Math.Clamp(distance - 1, 0, Math.Max(0, frames - 1));

    private int Base(PorArtImage img) => img.Variant == "dos" || _paletteBase is null ? 0 : _paletteBase(img.PaletteRgb);

    /// <summary>
    /// Copies item 0 of <paramref name="p"/> to (<paramref name="dx"/>, <paramref name="dy"/>) inside the window at
    /// (<paramref name="x"/>, <paramref name="y"/>), its indices moved into the run at <paramref name="b"/>, at most
    /// <paramref name="clipRows"/> rows, clipped to the window; index 0 skipped when <paramref name="transparent"/> [?]
    /// (the sprites' background: every DOS and Amiga sprite frame has index 0 in its corners).
    /// </summary>
    private static void Blit(IndexedFrame f, int x, int y, int dx, int dy, DaxPicture p, int b, bool transparent,
        int clipRows = Window)
    {
        if (p.Items.Count == 0)
            return;
        var px = p.Items[0];
        for (int r = 0; r < p.Height && r < clipRows; r++)
            for (int c = 0; c < p.Width; c++)
            {
                byte v = px[r * p.Width + c];
                int wx = dx + c, wy = dy + r, xx = x + wx, yy = y + wy;
                if ((transparent && v == 0) || wx < 0 || wy < 0 || wx >= Window || wy >= Window
                    || xx < 0 || yy < 0 || xx >= f.Width || yy >= f.Height)
                    continue;
                f[xx, yy] = (byte)(b + v);
            }
    }
}
