namespace GoldBox.Core.Explore;

/// <summary>
/// The land's recovery after Yarash's pyramid (GAME.OVR 0x20D50, read s58; docs/re/geo-format.md "The land recovers").
/// Two callers: the script switch 0x3EFA into block 26 with $49E6 = 0, $4AB3 = 0xFE and $4AA2 = 0 calls it animated
/// (0x3F86: ECL7 block 26's search vector then prints the recovery text and sets $4AA2 = 255), and the game Load
/// 0x1F5E8 calls it still when the loaded $4AA2 ≠ 0 (0x1FC98).
/// <list type="bullet">
/// <item>Animated: the 5 × 5 tiles at the overland's top-left square (17, 15) (0x2DE38: tiles only, no marker) and the
/// screen copy (START 0x464:0x1B52); then 9 frames: every tile of <see cref="ListA"/> recoloured by
/// <see cref="ToA"/> and every tile of <see cref="ListB"/> by <see cref="ToB"/>, each in the random mode, then the
/// tiles at <see cref="Pan"/>[frame] and the screen copy. No wait between frames (machine time; the Client shows each
/// for one update, the delay-0 rule of s27).</item>
/// <item>Both: the two lists recoloured again in the whole mode (every pixel of a "from" colour changes).</item>
/// <item>Animated: the tiles at (23, 25), the screen copy and PauseOnly twice (START 0x709:0xDBE, DS:49E2 × 225 ms).</item>
/// </list>
/// The recolour (START 0x464:0x1E5C = 0x649C) works on the tile sheet in memory (DS:5E52 for tiles below 0x80, DS:5E56
/// for the rest, item t &amp; 0x7F), so the change stays for the session: the game init is the only load of
/// <c>SQRPACI.DAX</c> (BACPAC and the combat grounds overwrite tiles 0–39 only, below every listed tile), and a Load of
/// an earlier save ($4AA2 = 0) keeps the recoloured tiles. Its random mode draws Random(256) twice per 8-pixel group in
/// EGA mode (DS:4A1E = 1), which moves RandSeed: 9 × (11 × 2 + 18 × 1) colours × 24 rows × 3 groups × 2 =
/// <see cref="AnimatedDraws"/> draws.
/// </summary>
public static class OverlandRecovery
{
    /// <summary>The cells the callers test: Yarash beaten ($4AB3 = 0xFE, set at ECL7 block 23 $AC53) and the river flag $4AA2.</summary>
    public const ushort YarashCell = 0x4AB3, RiverCell = 0x4AA2;

    /// <summary>The script block whose switch runs the animation (DS:84DC = 0x1A at 0x3F5F).</summary>
    public const int Block = 26;

    /// <summary>$4AB3's value that runs it (0x3F71, a word compare).</summary>
    public const ushort YarashBeaten = 0xFE;

    /// <summary>DS:0900, 11 tile numbers recoloured by <see cref="ToA"/> (bit 7: the high sheet).</summary>
    public static readonly byte[] ListA = [0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x71, 0xE0, 0xE1, 0xE2];

    /// <summary>DS:090B, 18 tile numbers recoloured by <see cref="ToB"/>.</summary>
    public static readonly byte[] ListB =
        [0x56, 0x57, 0x58, 0x59, 0x6C, 0x6D, 0x6E, 0x6F, 0x70, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0xDF, 0xF3, 0xF5];

    /// <summary>DS:091D, the "from" colours of both calls: 0–15 in order.</summary>
    public static readonly byte[] From = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>DS:092D: colour 0 → 1, 7 → 2, the rest kept.</summary>
    public static readonly byte[] ToA = [1, 1, 2, 3, 4, 5, 6, 2, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>DS:093D: colour 7 → 2, the rest kept.</summary>
    public static readonly byte[] ToB = [0, 1, 2, 3, 4, 5, 6, 2, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>The view's top-left square before the frames (0x20D5F) and after them (0x20F71).</summary>
    public static readonly (int X, int Y) First = (17, 15), Last = (23, 25);

    /// <summary>DS:094D / DS:095F: the top-left square of frames 0–8.</summary>
    public static readonly (int X, int Y)[] Pan =
        [(17, 15), (17, 15), (17, 15), (19, 20), (19, 20), (19, 20), (23, 25), (23, 25), (23, 25)];

    /// <summary>The frame count (0x20E80: the frame byte runs 0–8).</summary>
    public const int Frames = 9;

    /// <summary>A sheet item's size: 24 × 24 pixels, 3 groups of 8 per row.</summary>
    public const int TilePixels = Overland.TilePixels;

    /// <summary>The random draws of the animated run in EGA mode.</summary>
    public static int AnimatedDraws => Frames * DrawsPerPass;

    /// <summary>The random draws of one pass over both lists in the random mode.</summary>
    public static int DrawsPerPass =>
        (ListA.Length * Changed(ToA) + ListB.Length * Changed(ToB)) * TilePixels * (TilePixels / 8) * 2;

    private static int Changed(byte[] to)
    {
        int n = 0;
        for (int c = 0; c < 16; c++)
            if (From[c] != to[c])
                n++;
        return n;
    }

    /// <summary>
    /// Runs 0x20D50. <paramref name="tile"/> gives tile t's pixels (24 × 24, row-major, EGA colour indices) to change
    /// in place, or null where the host keeps no DOS pixels (the draws still happen). <paramref name="frame"/> shows
    /// the 5 × 5 tiles with that top-left overland square; <paramref name="pause"/> is PauseOnly.
    /// </summary>
    public static void Run(bool animated, Func<int, byte[]?> tile, Func<ushort, ushort> random,
        Action<int, int>? frame = null, Action? pause = null)
    {
        if (animated)
        {
            frame?.Invoke(First.X, First.Y);
            for (int f = 0; f < Frames; f++)
            {
                Pass(tile, random, true);
                frame?.Invoke(Pan[f].X, Pan[f].Y);
            }
        }
        Pass(tile, random, false);
        if (animated)
        {
            frame?.Invoke(Last.X, Last.Y);
            pause?.Invoke();
            pause?.Invoke();
        }
    }

    /// <summary>Both lists once (0x20D71–0x20E5A random, 0x20E89–0x20F69 whole).</summary>
    public static void Pass(Func<int, byte[]?> tile, Func<ushort, ushort> random, bool randomMode)
    {
        foreach (byte t in ListA)
            Recolour(tile(t), From, ToA, randomMode, random);
        foreach (byte t in ListB)
            Recolour(tile(t), From, ToB, randomMode, random);
    }

    /// <summary>
    /// START 0x649C in EGA mode (DS:4A1E = 1, 0x6772–0x690D) for one 24 × 24 item: a copy of the item is made
    /// (0x6ACE); for each colour c = 0–15 with from[c] ≠ to[c] (0x65DE), each row, each group of 8 pixels: a mask of
    /// Random(256) AND Random(256) in the random mode, else 0xFF (0x680D–0x682E); a pixel whose colour in the
    /// <b>original</b> is from[c] and whose mask bit is set (bit 7 the leftmost, the EGA byte order) becomes to[c] in the
    /// copy (0x6832–0x68DE: the four plane bytes); the copy then replaces the item (0x6A7A). A null item still draws.
    /// [?] Modes 0 (CGA, the 0x46BB lookup, Random(4) per pixel) and 2 (Tandy, Random(4) per pixel) are not modelled:
    /// the Client draws EGA.
    /// </summary>
    public static void Recolour(byte[]? px, IReadOnlyList<byte> from, IReadOnlyList<byte> to, bool randomMode,
        Func<ushort, ushort> random)
    {
        const int groups = TilePixels / 8;
        byte[]? copy = px is null ? null : (byte[])px.Clone();
        for (int c = 0; c < 16; c++)
        {
            if (from[c] == to[c])
                continue;
            for (int row = 0; row < TilePixels; row++)
                for (int g = 0; g < groups; g++)
                {
                    int mask = 0xFF;
                    if (randomMode)
                    {
                        int a = random(256);
                        mask = random(256) & a;
                    }
                    if (px is null || copy is null)
                        continue;
                    for (int b = 0; b < 8; b++)
                    {
                        int i = row * TilePixels + g * 8 + b;
                        if ((mask & (0x80 >> b)) != 0 && px[i] == from[c])
                            copy[i] = to[c];
                    }
                }
        }
        if (px is not null && copy is not null)
            Array.Copy(copy, px, px.Length);
    }

    /// <summary>The recoloured state alone (the whole mode, no draws): what the tiles hold after either caller.</summary>
    public static void ApplyFinal(Func<int, byte[]?> tile) => Pass(tile, _ => 0, false);
}
