using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The overland view's art under the art setting (<see cref="ClientArt"/>): the DOS art of
/// <see cref="PorOverlandArt.Load"/> (SQRPACI blocks 1 and 2, BACPAC block 1, ICON blocks 0–2 and 128–130), or each
/// file's variant (the Amiga overlay has its own SQRPACI, BACPAC and ICON, por-art-formats.md §3) with its palette run,
/// DOS where the variant has none or fewer tiles than DOS.
/// <para>
/// [?] The Amiga sets are taken tile for tile in the DOS order (the tile bytes of the overland table number DOS's
/// sheets; the Amiga program's own numbering is not read). [?] The Amiga marker frames are drawn with colour 0
/// see-through and no recolouring: the DOS ICON load's light magenta → black remap (DS:0CD2 → 0CF2) is DOS-palette
/// specific, and the Amiga program's icon handling is not read.
/// </para>
/// </summary>
public sealed class OverlandArt
{
    private readonly IPorContent? _content;

    public OverlandArt(IPorContent? content)
    {
        _content = content;
        Art = Empty;
    }

    private static readonly PorOverlandArt Empty = new([], null, new Dictionary<int, OverlandPicture>());

    /// <summary>The art drawn now.</summary>
    public PorOverlandArt Art { get; private set; }

    private bool _lowDos, _highDos;

    /// <summary>
    /// The land's recovery ran this session (<see cref="Core.Explore.OverlandRecovery"/>): the program recolours its tile
    /// sheets in memory, loaded once a session, so the change stays (an earlier save's Load included). An art change
    /// rebuilds the tiles and recolours them again.
    /// </summary>
    public bool Recovered { get; private set; }

    /// <summary>
    /// Tile t's pixels for the recolour when its sheet is the DOS one (EGA indices), else null. [?] The Amiga program
    /// holds the same tile lists and pan but one colour map of its own (00 01 02 03 04 01 06 07 08 09 0A 0B 02 02 0A 0F,
    /// next to the lists in <c>program</c>); its routine is not read, so Amiga tiles are not recoloured.
    /// </summary>
    public byte[]? DosTile(int t) =>
        (t < PorOverlandArt.SheetTiles ? _lowDos : _highDos) ? Art.Tile(t, outdoor: false)?.Pixels : null;

    /// <summary>Marks the recovery as run (the tiles were recoloured in place by the caller).</summary>
    public void MarkRecovered() => Recovered = true;

    /// <summary>Takes the art from <paramref name="choice"/>'s variant (see the class summary).</summary>
    public void UseArt(PorArtChoice choice, Func<byte[], int>? paletteBase)
    {
        if (_content is null)
        {
            Art = Empty;
            return;
        }
        var dos = PorOverlandArt.Load(_content);
        if (choice == PorArtChoice.Dos || paletteBase is null)
        {
            Art = dos;
            _lowDos = _highDos = true;
            Recolour();
            return;
        }
        var low = Tiles("SQRPACI", 1, choice, paletteBase);
        var high = Tiles("SQRPACI", 2, choice, paletteBase);
        var bac = Tiles("BACPAC", 1, choice, paletteBase);
        var dosLow = PorOverlandArt.Tiles(_content.Picture("SQRPACI", 1));
        var dosHigh = PorOverlandArt.Tiles(_content.Picture("SQRPACI", 2));
        var dosBac = PorOverlandArt.Tiles(_content.Picture("BACPAC", 1));
        var markers = new Dictionary<int, OverlandPicture>();
        foreach (int b in PorOverlandArt.MarkerBlocks)
        {
            if (Single("ICON", b, choice, paletteBase) is var (p, bs) && bs != 0 && p.Items.Count > 0
                && p is { Width: Core.Explore.Overland.TilePixels, Height: Core.Explore.Overland.TilePixels })
                markers[b] = Marker(p.Items[0], bs);
            else if (dos.Marker(b) is { } d)
                markers[b] = d;
        }
        Art = new PorOverlandArt(PorOverlandArt.Sheets(SameOrDos(low, dosLow), SameOrDos(high, dosHigh)),
            SameOrDos(bac, dosBac), markers);
        _lowDos = low.Count < dosLow.Count;
        _highDos = high.Count < dosHigh.Count;
        Recolour();
    }

    private void Recolour()
    {
        if (Recovered)
            Core.Explore.OverlandRecovery.ApplyFinal(DosTile);
    }

    /// <summary>The variant's tiles cut to DOS's count, or DOS's when it has fewer (as <see cref="CombatArt.SameOrDos"/>).</summary>
    public static List<OverlandPicture?> SameOrDos(List<OverlandPicture?> chosen, List<OverlandPicture?> dos) =>
        chosen.Count >= dos.Count ? chosen.GetRange(0, dos.Count) : dos;

    /// <summary>A variant marker frame: colour 0 see-through, the rest moved to the palette run (no recolouring [?]).</summary>
    public static OverlandPicture Marker(byte[] px, int paletteBase)
    {
        var o = new byte[px.Length];
        var m = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            m[i] = px[i] != 0;
            o[i] = m[i] ? (byte)(paletteBase + px[i]) : (byte)0;
        }
        return new OverlandPicture(o, m);
    }

    private List<OverlandPicture?> Tiles(string file, int id, PorArtChoice choice, Func<byte[], int> paletteBase)
    {
        if (Single(file, id, choice, paletteBase) is not var (p, b) || b == 0
            || p is not { Width: Core.Explore.Overland.TilePixels, Height: Core.Explore.Overland.TilePixels })
            return [];
        var all = new bool[p.Width * p.Height];
        Array.Fill(all, true);
        return [.. p.Items.Select(px => (OverlandPicture?)new OverlandPicture(CombatArt.Rebase(px, b, opaque: true), all))];
    }

    /// <summary>A one-picture block in the variant (not DOS) with its palette run's start, or null.</summary>
    private (DaxPicture P, int Base)? Single(string file, int id, PorArtChoice choice, Func<byte[], int> paletteBase) =>
        _content?.Art(file, id, choice) is { Variant: not "dos", Frames: [{ Delay: null } v] } img
            ? (v, paletteBase(img.PaletteRgb))
            : null;
}
