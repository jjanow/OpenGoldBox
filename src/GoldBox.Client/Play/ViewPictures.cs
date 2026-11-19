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
public sealed record ViewPicture(ViewPictureKind Kind, int Id, int? Head, int Distance, int Disk)
{
    /// <summary>Use the fixed encounter sprite frame for the overland view.</summary>
    public bool OnOverland { get; init; }
}

/// <summary>
/// Draws <see cref="ViewPicture"/>s into the picture window from the content, in the chosen art (DOS, or a variant
/// with its own palette moved into a <see cref="ScreenPalette"/> run, DOS where the variant has no such block).
/// docs/re/ecl-format.md (SETUPMON): at distance 0 the close-up, from PIC or head + body; at distance 1–2 the sprite.
/// An animated PIC is drawn at the frame <see cref="PictureAnimation"/> picks: the later frames are XOR deltas against
/// the first (the loader 0x2E170 XORs them in, read s19; the Amiga loader does the same, hunk 19 +0x13D6). [?] Amiga
/// timing: its program changes frames every 3000 polls of the menu wait (CPU speed), not by the stored delay; the
/// Client uses the delay with the DOS rule. The sprite frame follows the view mode and encounter distance. Its place
/// comes from the frame's block x, y in 8-pixel units inside the window. A
/// portrait draws both parts from one variant (one palette): when the variant lacks either part, both are DOS. The
/// Amiga HEAD is 56 px wide with no position; it is drawn at x = 16 [?] (outline matching, por-art-formats.md §3).
/// </summary>
public sealed class ViewPictures
{
    public const int Window = 88, HeadRows = 40;

    /// <summary>Where the Amiga HEAD goes in the window [?] (por-art-formats.md §3: best outline match 15–17 px, 16 most often).</summary>
    public const int AmigaHeadX = 16;

    private readonly IPorContent? _content;
    private readonly Dictionary<(string, int, int, int), PorArtImage?> _cache = [];
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
    /// The block in the chosen art from the file of <paramref name="disk"/> (disks 1–8: the program opens only the
    /// current disk's <c>PIC&lt;disk&gt;</c> / <c>HEAD</c> / <c>BODY</c> / <c>SPRIT</c>; a block that is not there draws
    /// nothing, PICTURE 0x159C → 0x3D0:2A, DOSBox-X s22), or null. The DOS block on that disk decides; the variant's
    /// art (the Amiga files serve every disk) is drawn in its place. With <paramref name="anyDisk"/> (the end game's
    /// FINAL, whose disk is the scene's), or a disk outside 1–8 (a hand-made module's), the first disk that has it serves.
    /// </summary>
    public PorArtImage? Find(string kind, int id, int disk, PorArtChoice? choice = null, bool anyDisk = false)
    {
        var c = choice ?? _choice;
        lock (_cache)
        {
            var key = (kind, id, (int)c, anyDisk ? -1 : disk);
            if (_cache.TryGetValue(key, out var hit))
                return hit;
            PorArtImage? img = null;
            if (_content is not null)
                foreach (int d in anyDisk || disk is < 1 or > 8 ? new[] { disk }.Concat(Enumerable.Range(1, 8)) : [disk])
                    if (d is >= 1 and <= 8 && _content.Picture($"{kind}{d}", id) is not null
                        && (img = _content.Art($"{kind}{d}", id, c)) is not null)
                        break;
            _cache[key] = img;
            return img;
        }
    }

    /// <summary>
    /// True when the art of <paramref name="v"/> is on some disk, though perhaps not on <see cref="ViewPicture.Disk"/>
    /// (the window then shows nothing new, as the program does); false when the content lacks it altogether (the Client's
    /// stand-in text shows instead).
    /// </summary>
    public bool Exists(ViewPicture v)
    {
        bool Any(string kind, int id) => Find(kind, id, v.Disk, PorArtChoice.Dos, anyDisk: true) is not null;
        return v.Kind switch
        {
            ViewPictureKind.Picture => Any("PIC", v.Id),
            ViewPictureKind.Portrait => Any("HEAD", v.Head ?? 0) && Any("BODY", v.Id),
            _ => Any("SPRIT", v.Id),
        };
    }

    /// <summary>
    /// Draws <paramref name="v"/> into the window whose top-left pixel is (<paramref name="x"/>, <paramref name="y"/>);
    /// false (nothing drawn) when the content has no such art.
    /// </summary>
    public bool Draw(IndexedFrame f, int x, int y, ViewPicture v, int frame = 0)
    {
        switch (v.Kind)
        {
            case ViewPictureKind.Picture:
            {
                if (Find("PIC", v.Id, v.Disk) is not { Frames: [var p, ..] } img)
                    return false;
                int b = Base(img);
                f.FillRect(x, y, Window, Window, (byte)b);
                if (frame > 0 && frame < img.Frames.Count)
                    p = Composed(p, img.Frames[frame]);
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
                var p = img.Frames[SpriteFrame(v.Distance, img.Frames.Count, v.OnOverland)];
                Blit(f, x, y, p.X * 8, p.Y * 8, p, Base(img), transparent: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The frame delays of the picture <paramref name="v"/> shows (units of 100 ms, <see cref="PictureAnimation"/>):
    /// one per frame of an animated PIC, empty otherwise.
    /// </summary>
    public IReadOnlyList<uint> Delays(ViewPicture v) =>
        v.Kind == ViewPictureKind.Picture && Find("PIC", v.Id, v.Disk) is { Frames.Count: > 1 } img
            ? [.. img.Frames.Select(p => p.Delay ?? 0)]
            : [];

    /// <summary>A later frame as the loader 0x2E170 makes it: its pixels XORed with the first frame's (item 0).</summary>
    public static DaxPicture Composed(DaxPicture first, DaxPicture delta)
    {
        if (first.Items.Count == 0 || delta.Items.Count == 0 || first.Width != delta.Width || first.Height != delta.Height)
            return delta;
        var a = first.Items[0];
        var d = delta.Items[0];
        var px = new byte[d.Length];
        for (int i = 0; i < px.Length; i++)
            px[i] = (byte)(a[i] ^ d[i]);
        return DaxPicture.Create(delta.Width, delta.Height, delta.X, delta.Y, [px], delay: delta.Delay);
    }

    /// <summary>
    /// The encounter renderer selects a one-based slot of distance + 1 in 3D, or slot 2 on the overland.
    /// Short art variants use their last available frame.
    /// </summary>
    public static int SpriteFrame(int distance, int frames, bool onOverland = false) =>
        Math.Clamp(onOverland ? 1 : distance, 0, Math.Max(0, frames - 1));

    private int Base(PorArtImage img) => img.Variant == "dos" || _paletteBase is null ? 0 : _paletteBase(img.PaletteRgb);

    /// <summary>The first screen index of <paramref name="img"/>'s palette run (0 for DOS art).</summary>
    public int RunBase(PorArtImage img) => Base(img);

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
