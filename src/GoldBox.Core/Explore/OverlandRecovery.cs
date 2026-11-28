namespace GoldBox.Core.Explore;

/// <summary>
/// The land's recovery after Yarash's pyramid (GAME.OVR 0x20D50, read s58; docs/re/geo-format.md "The land recovers").
/// Two callers: the script switch 0x3EFA into block 26 with $49E6 = 0, $4AB3 = 0xFE and $4AA2 = 0 calls it animated
/// (0x3F86: ECL7 block 26's search vector then prints the recovery text and sets $4AA2 = 255), and the game Load
/// 0x1F5E8 calls it still when the loaded $4AA2 ≠ 0 (0x1FC98). Its tile lists, colour maps and view squares are the
/// module's (<see cref="OverlandRecoveryTables"/>, s60; Core held them as constants until then).
/// <list type="bullet">
/// <item>Animated: the 5 × 5 tiles at the overland's square <see cref="OverlandRecoveryTables.First"/> (0x2DE38: tiles
/// only, no marker) and the screen copy (START 0x464:0x1B52); then one frame per pan square: every tile of list A
/// recoloured by map A and every tile of list B by map B, each in the random mode, then the tiles at that pan square
/// and the screen copy. No wait between frames in the program (machine time; the Client's pace is the s59
/// decision).</item>
/// <item>Both: the two lists recoloured again in the whole mode (every pixel of a "from" colour changes).</item>
/// <item>Animated: the tiles at <see cref="OverlandRecoveryTables.Last"/>, the screen copy and PauseOnly twice (START
/// 0x709:0xDBE, DS:49E2 × 225 ms).</item>
/// </list>
/// The recolour (START 0x464:0x1E5C = 0x649C) works on the tile sheet in memory (DS:5E52 for tiles below 0x80, DS:5E56
/// for the rest, item t &amp; 0x7F), so the change stays for the session: the game init is the only load of
/// <c>SQRPACI.DAX</c> (BACPAC and the combat grounds overwrite tiles 0–39 only, below every listed tile), and a Load of
/// an earlier save ($4AA2 = 0) keeps the recoloured tiles. Its random mode draws Random(256) twice per 8-pixel group in
/// EGA mode (DS:4A1E = 1), which moves RandSeed (<see cref="AnimatedDraws"/>; PoR v1.3: 51,840 draws).
/// </summary>
public static class OverlandRecovery
{
    /// <summary>The cells the callers test: Yarash beaten ($4AB3 = 0xFE, set at ECL7 block 23 $AC53) and the river flag $4AA2.</summary>
    public const ushort YarashCell = 0x4AB3, RiverCell = 0x4AA2;

    /// <summary>The script block whose switch runs the animation (DS:84DC = 0x1A at 0x3F5F).</summary>
    public const int Block = 26;

    /// <summary>$4AB3's value that runs it (0x3F71, a word compare).</summary>
    public const ushort YarashBeaten = 0xFE;

    /// <summary>The colour maps' length: 0x649C runs c = 0–15 (0x65DE).</summary>
    public const int Colours = 16;

    /// <summary>A sheet item's size: 24 × 24 pixels, 3 groups of 8 per row.</summary>
    public const int TilePixels = Overland.TilePixels;

    /// <summary>The random draws of the animated run in EGA mode.</summary>
    public static int AnimatedDraws(OverlandRecoveryTables t) => t.Pan.Count * DrawsPerPass(t);

    /// <summary>The random draws of one pass over both lists in the random mode.</summary>
    public static int DrawsPerPass(OverlandRecoveryTables t) =>
        (t.ListA.Count * Changed(t.From, t.ToA) + t.ListB.Count * Changed(t.From, t.ToB)) * TilePixels * (TilePixels / 8) * 2;

    private static int Changed(IReadOnlyList<byte> from, IReadOnlyList<byte> to)
    {
        int n = 0;
        for (int c = 0; c < Colours; c++)
            if (from[c] != to[c])
                n++;
        return n;
    }

    /// <summary>
    /// Runs 0x20D50 with the module's <paramref name="tables"/>. <paramref name="tile"/> gives tile t's pixels (24 × 24,
    /// row-major, EGA colour indices) to change in place, or null where the host keeps no DOS pixels (the draws still
    /// happen). <paramref name="frame"/> shows the 5 × 5 tiles with that top-left overland square;
    /// <paramref name="pause"/> is PauseOnly. <paramref name="pass"/>, when given, runs after each pass with its random
    /// flag (the host's other tile sheets, e.g. <see cref="AmigaOverlandRecovery.Pass"/>: the Amiga routine has the same
    /// views and passes).
    /// </summary>
    public static void Run(OverlandRecoveryTables tables, bool animated, Func<int, byte[]?> tile,
        Func<ushort, ushort> random, Action<int, int>? frame = null, Action? pause = null, Action<bool>? pass = null)
    {
        if (animated)
        {
            frame?.Invoke(tables.First.X, tables.First.Y);
            foreach (var (x, y) in tables.Pan)
            {
                Pass(tables, tile, random, true);
                pass?.Invoke(true);
                frame?.Invoke(x, y);
            }
        }
        Pass(tables, tile, random, false);
        pass?.Invoke(false);
        if (animated)
        {
            frame?.Invoke(tables.Last.X, tables.Last.Y);
            pause?.Invoke();
            pause?.Invoke();
        }
    }

    /// <summary>Both lists once (0x20D71–0x20E5A random, 0x20E89–0x20F69 whole).</summary>
    public static void Pass(OverlandRecoveryTables tables, Func<int, byte[]?> tile, Func<ushort, ushort> random,
        bool randomMode)
    {
        foreach (byte t in tables.ListA)
            Recolour(tile(t), tables.From, tables.ToA, randomMode, random);
        foreach (byte t in tables.ListB)
            Recolour(tile(t), tables.From, tables.ToB, randomMode, random);
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
        for (int c = 0; c < Colours; c++)
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
    public static void ApplyFinal(OverlandRecoveryTables tables, Func<int, byte[]?> tile) =>
        Pass(tables, tile, _ => 0, false);
}

/// <summary>
/// The land's recovery tables (a module's <c>rules/overland.json</c> "recovery", docs/module-format.md §6; PoR v1.3: read
/// by the importer from the player's program): two tile lists (bit 7: the high sheet, item t &amp; 0x7F) with their
/// colour maps <see cref="From"/> → <see cref="ToA"/> / <see cref="ToB"/> (16 entries each), the view's top-left
/// overland square before the frames (<see cref="First"/>) and after them (<see cref="Last"/>), and one top-left square
/// per animated frame (<see cref="Pan"/>; its length is the frame count).
/// </summary>
public sealed record OverlandRecoveryTables(
    IReadOnlyList<byte> ListA, IReadOnlyList<byte> ListB, IReadOnlyList<byte> From, IReadOnlyList<byte> ToA,
    IReadOnlyList<byte> ToB, (int X, int Y) First, (int X, int Y) Last, IReadOnlyList<(int X, int Y)> Pan)
{
    /// <summary>Null when the tables have the routine's shape (three 16-entry colour maps, a frame), else what is wrong.</summary>
    public string? Problem() =>
        From.Count != OverlandRecovery.Colours || ToA.Count != OverlandRecovery.Colours || ToB.Count != OverlandRecovery.Colours
            ? "the colour maps need 16 entries each"
            : Pan.Count == 0 ? "no pan frame" : null;
}
