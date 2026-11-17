using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>A 24-pixel-wide picture of EGA indices, row-major (a ground tile, an icon, a combat sprite).</summary>
public sealed record CombatPicture(int Width, int Height, byte[] Pixels);

/// <summary>
/// The combat screen's DOS art (docs/re/por-art-formats.md): the 24×24 ground tiles (DUNGCOM.DAX in a 3D area,
/// WILDCOM.DAX outdoors, RANDCOM.DAX for the rare features), the monster icons (CPIC&lt;disk&gt;.DAX: block id = the
/// picture, id + 128 = its attack pose), the party icons composed from CHEAD.DAX and CBODY.DAX, and COMSPR.DAX (block
/// 25: the hit marker). Missing files leave the parts empty and the screen draws without them.
/// </summary>
public sealed class CombatArt
{
    /// <summary>COMSPR block of the marker drawn on a creature that is hit (DOSBox-X: a skull on red over the defender;
    /// [?] block 11 is that picture, which of 11 / 139 the program draws when is not read).</summary>
    public const int HitMarker = 11;

    private readonly PorGameDirectory? _dir;
    private readonly Dictionary<int, CombatPicture?> _cpic = [];
    private readonly Dictionary<int, CombatPicture?> _head = [], _body = [], _sprites = [];

    public IReadOnlyList<CombatPicture> Dungeon { get; } = [];
    public IReadOnlyList<CombatPicture> Wilderness { get; } = [];
    public IReadOnlyList<CombatPicture> Rare { get; } = [];

    /// <summary>No art (tests, a folder without the files).</summary>
    public CombatArt()
    {
    }

    public CombatArt(PorGameDirectory dir)
    {
        _dir = dir;
        Dungeon = Items(dir.Dax("DUNGCOM.DAX"), 1);
        Wilderness = Items(dir.Dax("WILDCOM.DAX"), 1);
        Rare = Items(dir.Dax("RANDCOM.DAX"), 1);
    }

    private static List<CombatPicture> Items(DaxFile? f, int id)
    {
        if (f?.Block(id) is not { } b)
            return [];
        try
        {
            var p = DaxPicture.Decode(b);
            return [.. p.Items.Select(px => new CombatPicture(p.Width, p.Height, px))];
        }
        catch (DaxFormatException)
        {
            return [];
        }
    }

    private static CombatPicture? One(DaxFile? f, int id)
    {
        if (f?.Block(id) is not { } b)
            return null;
        try
        {
            var p = DaxPicture.Decode(b);
            return p.Items.Count > 0 ? new CombatPicture(p.Width, p.Height, p.Items[0]) : null;
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    private CombatPicture? Cached(Dictionary<int, CombatPicture?> cache, string file, int id)
    {
        if (!cache.TryGetValue(id, out var p))
            cache[id] = p = One(_dir?.Dax(file), id);
        return p;
    }

    /// <summary>
    /// A ground tile by the terrain table's tile byte (DS:2744 + 4·t + 3; 22 = open ground): an index into the area's
    /// set, the rare-feature set after it [?] (which tile bytes the rare terrains 26–29 carry is not checked).
    /// </summary>
    public CombatPicture? Ground(int tile, bool dungeon)
    {
        var set = dungeon ? Dungeon : Wilderness;
        if (tile >= 0 && tile < set.Count)
            return set[tile];
        int r = tile - set.Count;
        return r >= 0 && r < Rare.Count ? Rare[r] : null;
    }

    /// <summary>
    /// A monster icon: CPIC block <paramref name="picture"/> (+ 128 for the attack pose) from the first CPIC file that
    /// has it (the disks hold byte-identical copies, por-art-formats.md).
    /// </summary>
    public CombatPicture? Monster(int picture, bool attack)
    {
        int id = (picture & 0x7F) + (attack ? 128 : 0);
        if (!_cpic.TryGetValue(id, out var p))
        {
            p = null;
            for (int d = 1; d <= 8 && p is null; d++)
                p = One(_dir?.Dax($"CPIC{d}.DAX"), id);
            _cpic[id] = p;
        }
        return p;
    }

    /// <summary>A COMSPR picture (missiles, the hit marker).</summary>
    public CombatPicture? Sprite(int id) => Cached(_sprites, "COMSPR.DAX", id);

    /// <summary>Record bytes of the party icon (character-format.md): head 0xBD, body 0xBE, size 0xC0, colours 0xC1–0xC6.</summary>
    public const int IconHead = 0xBD, IconBody = 0xBE, IconSize = 0xC0, IconColours = 0xC1;

    /// <summary>
    /// A party member's icon: the body (CBODY) with the head (CHEAD, 24 wide, 10 rows tall or 8 short) over its top
    /// rows, both recoloured (the routine is not read). **D** (DOSBox-X, the six members of save A at the start of a
    /// fight, every icon pixel equal): CHEAD/CBODY id + 64 for icon size 2 (heads 8 rows, shoulders 2 rows higher), the
    /// plain id for size 1, the head at the top, and the recolouring: the art uses the colour pairs (1, 9), (2, 10),
    /// (3, 11), (4, 12), (6, 14), (7, 15); colour byte k of 0xC1–0xC6 replaces the pair's first colour with its low
    /// nibble and the second with its high nibble. [?] id + 128 as the attack pose (CBODY 128+ hold the weapon forward)
    /// is from the art only.
    /// </summary>
    public CombatPicture? Party(EclCharacter c, bool attack)
    {
        int extra = (c[IconSize] == 2 ? 64 : 0) + (attack ? 128 : 0);
        var body = Cached(_body, "CBODY.DAX", (c[IconBody] & 0x3F) + extra) ?? Cached(_body, "CBODY.DAX", c[IconBody] & 0x3F);
        var head = Cached(_head, "CHEAD.DAX", (c[IconHead] & 0x3F) + extra) ?? Cached(_head, "CHEAD.DAX", c[IconHead] & 0x3F);
        if (body is null && head is null)
            return null;
        int w = 24, h = 24;
        var px = new byte[w * h];
        if (body is not null)
            Copy(body, px, w, h, 0);
        if (head is not null)
            Copy(head, px, w, h, 0);
        var map = ColourMap(c);
        for (int i = 0; i < px.Length; i++)
            px[i] = map[px[i]];
        return new CombatPicture(w, h, px);
    }

    private static void Copy(CombatPicture src, byte[] dst, int w, int h, int top)
    {
        for (int y = 0; y < src.Height && y + top < h; y++)
            for (int x = 0; x < src.Width && x < w; x++)
            {
                byte p = src.Pixels[y * src.Width + x];
                if (p != 0)
                    dst[(y + top) * w + x] = p;
            }
    }

    /// <summary>The colour pairs of the party icons, in the order of the colour bytes 0xC1–0xC6.</summary>
    private static readonly int[] PairLow = [1, 2, 3, 4, 6, 7];

    /// <summary>The recolouring of <see cref="Party"/> [?]: index → index.</summary>
    public static byte[] ColourMap(EclCharacter c)
    {
        var map = new byte[16];
        for (int i = 0; i < 16; i++)
            map[i] = (byte)i;
        for (int k = 0; k < PairLow.Length; k++)
        {
            byte b = c[IconColours + k];
            if (b == 0)
                continue; // [?] an unset byte keeps the art's colours
            map[PairLow[k]] = (byte)(b & 15);
            map[PairLow[k] + 8] = (byte)(b >> 4);
        }
        return map;
    }

    /// <summary>
    /// Draws <paramref name="p"/> at (x, y), index 0 see-through [?] (DOSBox-X shows no box around icons over the
    /// ground), mirrored when <paramref name="mirror"/>, clipped to <paramref name="clip"/>.
    /// </summary>
    public static void Draw(IndexedFrame f, CombatPicture p, int x, int y, bool mirror, (int X0, int Y0, int X1, int Y1) clip,
        bool opaque = false)
    {
        for (int r = 0; r < p.Height; r++)
        {
            int yy = y + r;
            if (yy < clip.Y0 || yy >= clip.Y1)
                continue;
            for (int c = 0; c < p.Width; c++)
            {
                int xx = x + c;
                if (xx < clip.X0 || xx >= clip.X1)
                    continue;
                byte v = p.Pixels[r * p.Width + (mirror ? p.Width - 1 - c : c)];
                if (v != 0 || opaque)
                    f[xx, yy] = v;
            }
        }
    }
}
