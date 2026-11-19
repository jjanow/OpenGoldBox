using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Dax;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// A 24-pixel-wide picture of frame indices, row-major (a ground tile, an icon, a combat sprite): EGA 0–15 for DOS art,
/// a <see cref="ScreenPalette"/> run for art with its own palette (0 stays 0 = see-through on icons).
/// </summary>
public sealed record CombatPicture(int Width, int Height, byte[] Pixels);

/// <summary>
/// The combat screen's DOS art (docs/re/por-art-formats.md): the 24×24 ground tiles (DUNGCOM.DAX in a 3D area,
/// WILDCOM.DAX outdoors, RANDCOM.DAX for the rare features), the monster icons (CPIC&lt;disk&gt;.DAX: block id = the
/// picture, id + 128 = its attack pose), the party icons composed from CHEAD.DAX and CBODY.DAX, and COMSPR.DAX (block
/// 25: the hit marker). Missing files leave the parts empty and the screen draws without them.
/// <para>
/// Art setting (<see cref="UseArt"/>): the ground tiles, monster icons and COMSPR come from the chosen variant (the Amiga
/// overlay, each with its palette), DOS where it has none. The Amiga DUNGCOM holds DOS's 25 tiles in the same order
/// plus one (only DOS's count is used, so the rare-feature numbering after it stays DOS's); WILDCOM matches tile for
/// tile. RANDCOM (read s19): both programs load it at tile 0x22 (DOS GAME.OVR 0xD867, after DUNGCOM or WILDCOM at 0,
/// whose blocks hold 25 and 34 tiles: the loader 0x31A8E copies a block's own item count; Amiga hunk 13 +0x1706) and
/// share the terrain table, so the Amiga set (8 tiles; its 2 and 3 are DOS's 3 and
/// 2, its 6 and 7 never drawn) is used as it is, as the Amiga shows it. Party icons (<see cref="Party"/>) are built
/// the Amiga way from the Amiga CHEAD/CBODY when the module has the program's colour pairs
/// (<see cref="IPorContent.AmigaIconPairs"/>) and every colour nibble of the character has a pen
/// (<see cref="PorAmigaArt.IconPen"/>); otherwise DOS.
/// </para>
/// </summary>
public sealed class CombatArt
{
    /// <summary>COMSPR block of the marker drawn on a creature that is hit (DOSBox-X: a skull on red over the defender;
    /// [?] block 11 is that picture, which of 11 / 139 the program draws when is not read).</summary>
    public const int HitMarker = 11;

    private readonly IPorContent? _dir;
    private readonly Dictionary<int, CombatPicture?> _cpic = [];
    private readonly Dictionary<int, CombatPicture?> _head = [], _body = [], _sprites = [];
    private readonly Dictionary<int, (DaxPicture P, int Base)?> _amigaHead = [], _amigaBody = [];
    private PorArtChoice _choice = PorArtChoice.Dos;
    private Func<byte[], int>? _paletteBase;

    public IReadOnlyList<CombatPicture> Dungeon { get; private set; } = [];
    public IReadOnlyList<CombatPicture> Wilderness { get; private set; } = [];
    public IReadOnlyList<CombatPicture> Rare { get; private set; } = [];

    /// <summary>No art (tests, a folder without the files).</summary>
    public CombatArt()
    {
    }

    /// <param name="dir">The content.</param>
    /// <param name="choice">The art setting (DOS by default).</param>
    /// <param name="paletteBase">Where a 16-colour palette starts in the screen palette (<see cref="ScreenPalette.Base"/>); without it every picture is DOS.</param>
    public CombatArt(IPorContent dir, PorArtChoice choice = PorArtChoice.Dos, Func<byte[], int>? paletteBase = null)
    {
        _dir = dir;
        UseArt(choice, paletteBase);
    }

    /// <summary>The art the pictures come from (see the class summary).</summary>
    public PorArtChoice Choice => _choice;

    /// <summary>Takes the pictures from <paramref name="choice"/>'s art from now on (the caches are emptied).</summary>
    public void UseArt(PorArtChoice choice, Func<byte[], int>? paletteBase)
    {
        _choice = choice;
        _paletteBase = paletteBase;
        _cpic.Clear();
        _sprites.Clear();
        _amigaHead.Clear();
        _amigaBody.Clear();
        if (_dir is null)
            return;
        var dosDungeon = Items(Single(_dir, "DUNGCOM", 1, PorArtChoice.Dos, null), opaque: true);
        var dosWild = Items(Single(_dir, "WILDCOM", 1, PorArtChoice.Dos, null), opaque: true);
        Dungeon = SameOrDos(Items(Single(_dir, "DUNGCOM", 1, choice, paletteBase), opaque: true), dosDungeon);
        Wilderness = SameOrDos(Items(Single(_dir, "WILDCOM", 1, choice, paletteBase), opaque: true), dosWild);
        var dosRare = Items(Single(_dir, "RANDCOM", 1, PorArtChoice.Dos, null), opaque: true);
        Rare = SameOrDos(Items(Single(_dir, "RANDCOM", 1, choice, paletteBase), opaque: true), dosRare);
    }

    /// <summary>The variant's ground set cut to DOS's tile count (the tile bytes number DOS's set), or DOS's when it has fewer.</summary>
    public static List<CombatPicture> SameOrDos(List<CombatPicture> chosen, List<CombatPicture> dos) =>
        chosen.Count >= dos.Count ? chosen.GetRange(0, dos.Count) : dos;

    /// <summary>
    /// A one-picture block (not an animation) in <paramref name="choice"/>'s art, with the screen-palette index its
    /// colour 0 lands on (0 for DOS art), or null.
    /// </summary>
    private static (DaxPicture P, int Base)? Single(IPorContent? dir, string file, int id, PorArtChoice choice,
        Func<byte[], int>? paletteBase)
    {
        if (dir is null)
            return null;
        if (choice != PorArtChoice.Dos && paletteBase is not null
            && dir.Art(file, id, choice) is { Variant: not "dos", Frames: [{ Delay: null } v] } img)
            return (v, paletteBase(img.PaletteRgb));
        return dir.Picture(file, id) is [var p] && p.Delay is null ? (p, 0) : null;
    }

    /// <summary>
    /// Indices moved into the palette run starting at <paramref name="b"/>: every pixel of an opaque ground tile; on an
    /// icon 0 stays 0 (see-through on both platforms: the Amiga icons keep the DOS icons' background index 0).
    /// </summary>
    public static byte[] Rebase(byte[] px, int b, bool opaque)
    {
        if (b == 0)
            return px;
        var o = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
            o[i] = px[i] == 0 && !opaque ? (byte)0 : (byte)(b + px[i]);
        return o;
    }

    private static List<CombatPicture> Items((DaxPicture P, int Base)? s, bool opaque) =>
        s is var (p, b) ? [.. p.Items.Select(px => new CombatPicture(p.Width, p.Height, Rebase(px, b, opaque)))] : [];

    private static CombatPicture? One((DaxPicture P, int Base)? s) =>
        s is ({ Items.Count: > 0 } p, var b) ? new CombatPicture(p.Width, p.Height, Rebase(p.Items[0], b, opaque: false)) : null;

    /// <summary>A DOS picture (the party icons, see the class summary).</summary>
    private CombatPicture? Cached(Dictionary<int, CombatPicture?> cache, string file, int id)
    {
        if (!cache.TryGetValue(id, out var p))
            cache[id] = p = One(Single(_dir, file, id, PorArtChoice.Dos, null));
        return p;
    }

    /// <summary>The tile the rare-feature set starts at (the loader 0x31A8E called with 0x22 for RANDCOM, GAME.OVR 0xD867).</summary>
    public const int RareFirstTile = 0x22;

    /// <summary>
    /// A ground tile by the terrain table's tile byte (DS:2744 + 4·t + 3; 22 = open ground): an index into the area's
    /// set (DUNGCOM 0–0x18 when DS:49FA = 1, else WILDCOM 0–0x21; 0xD815), the rare-feature set from
    /// <see cref="RareFirstTile"/> (terrains 0x1A–0x1F carry tiles 0x22–0x27). The program's draws (0x30938 and three
    /// more → 0x31B5D) index the tile sheet by that byte with no case of their own (docs/re/combat-menu.md "Ground,
    /// program read"). A dungeon's tiles 0x19–0x21 (the sheet's leftovers in the program) are reached by no terrain.
    /// </summary>
    public CombatPicture? Ground(int tile, bool dungeon)
    {
        var set = dungeon ? Dungeon : Wilderness;
        if (tile >= 0 && tile < set.Count && tile < RareFirstTile)
            return set[tile];
        int r = tile - RareFirstTile;
        return r >= 0 && r < Rare.Count ? Rare[r] : null;
    }

    /// <summary>
    /// A monster icon: CPIC block <paramref name="picture"/> (+ 128 for the attack pose) from the first CPIC file that
    /// has it (the disks hold byte-identical copies, por-art-formats.md). The icon loader (GAME.OVR 0x31C34, read s19)
    /// recolours a CPIC picture on load through the tables DS:0CD2 → DS:0CE2: colour 13 (light magenta) becomes 8 (dark
    /// grey, black on the combat palette); the DOS art only ([?] what the Amiga art needs is not checked).
    /// </summary>
    public CombatPicture? Monster(int picture, bool attack)
    {
        int id = (picture & 0x7F) + (attack ? 128 : 0);
        if (!_cpic.TryGetValue(id, out var p))
        {
            p = null;
            for (int d = 1; d <= 8 && p is null; d++)
            {
                var s = Single(_dir, $"CPIC{d}", id, _choice, _paletteBase);
                p = One(s);
                if (p is not null && s is (_, 0))
                    p = p with { Pixels = MonsterColours(p.Pixels) };
            }
            _cpic[id] = p;
        }
        return p;
    }

    /// <summary>The CPIC load remap (DS:0CE2 against the identity DS:0CD2): 13 → 8, every other colour kept.</summary>
    public static byte[] MonsterColours(byte[] px) => [.. px.Select(v => v == 13 ? (byte)8 : v)];

    /// <summary>A COMSPR picture (missiles, the hit marker).</summary>
    public CombatPicture? Sprite(int id)
    {
        if (!_sprites.TryGetValue(id, out var p))
            _sprites[id] = p = One(Single(_dir, "COMSPR", id, _choice, _paletteBase));
        return p;
    }

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
        if (AmigaParty(c, attack) is { } amiga)
            return amiga;
        int extra = (c[IconSize] == 2 ? 64 : 0) + (attack ? 128 : 0);
        var body = Cached(_body, "CBODY", (c[IconBody] & 0x3F) + extra) ?? Cached(_body, "CBODY", c[IconBody] & 0x3F);
        var head = Cached(_head, "CHEAD", (c[IconHead] & 0x3F) + extra) ?? Cached(_head, "CHEAD", c[IconHead] & 0x3F);
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

    /// <summary>
    /// A party icon from the Amiga art (docs/re/por-art-formats.md §4 "Party icons", read in the Amiga program): the
    /// loader (hunk 22 +0x193A) takes CHEAD / CBODY block id + 0x40 for icon size 2 (the size letter T); the action
    /// picture is CBODY id + 0x80 but the same CHEAD id. The builder (hunk 16 +0x2D0) ORs the head's planes into the
    /// body's top rows (hunk 16 +0x210: indices ORed, not replaced as on DOS), then recolours both pictures through
    /// <see cref="PorAmigaArt.IconColourMap"/> with the character's colour bytes moved to pens
    /// (<see cref="PorAmigaArt.AmigaIconColours"/>). Null (the DOS icon) under Original DOS, without the pairs or the
    /// Amiga blocks, or when a colour nibble has no pen.
    /// </summary>
    private CombatPicture? AmigaParty(EclCharacter c, bool attack)
    {
        if (_choice == PorArtChoice.Dos || _paletteBase is null || _dir?.AmigaIconPairs() is not { } pairs
            || PorAmigaArt.AmigaIconColours(c.Record.AsSpan(IconColours, 6), pairs) is not { } colours)
            return null;
        int size = c[IconSize] == 2 ? 0x40 : 0;
        var body = Amiga(_amigaBody, "CBODY", (c[IconBody] & 0x3F) + size + (attack ? 0x80 : 0));
        var head = Amiga(_amigaHead, "CHEAD", (c[IconHead] & 0x3F) + size);
        if (body is not ({ Items.Count: > 0 } bp, var bb) || head is not ({ Items.Count: > 0 } hp, var hb) || bb != hb
            || bp.Width != hp.Width)
            return null;
        var px = (byte[])bp.Items[0].Clone();
        var hpx = hp.Items[0];
        for (int i = 0; i < hpx.Length && i < px.Length; i++)
            px[i] |= hpx[i];
        var map = PorAmigaArt.IconColourMap(colours, pairs);
        for (int i = 0; i < px.Length; i++)
            px[i] = map[px[i] & 15];
        return new CombatPicture(bp.Width, bp.Height, Rebase(px, bb, opaque: false));
    }

    /// <summary>A block of the chosen variant when it is not DOS (its pixels unmoved, with its palette run), else null.</summary>
    private (DaxPicture P, int Base)? Amiga(Dictionary<int, (DaxPicture P, int Base)?> cache, string file, int id)
    {
        if (!cache.TryGetValue(id, out var p))
            cache[id] = p = Single(_dir, file, id, _choice, _paletteBase) is (var pic, var b and > 0) ? (pic, b) : null;
        return p;
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

    /// <summary>The colour pairs of the party icons, in the order of the colour bytes 0xC1–0xC6 (DS:3CF5[1..6] in v1.3).</summary>
    private static readonly int[] PairLow = [1, 2, 3, 4, 6, 7];

    /// <summary>
    /// The recolouring of <see cref="Party"/>: index → index. P (the icon builder 0x1E585 and the icon editor 0x1D08E,
    /// s20): identity, then for k = 1..6 colour DS:3CF5[k] := the low nibble of byte 0xC0 + k and DS:3CF5[k] + 8 := its
    /// high nibble, a zero byte included.
    /// </summary>
    public static byte[] ColourMap(EclCharacter c)
    {
        var map = new byte[16];
        for (int i = 0; i < 16; i++)
            map[i] = (byte)i;
        for (int k = 0; k < PairLow.Length; k++)
        {
            byte b = c[IconColours + k];
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
