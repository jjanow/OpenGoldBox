namespace GoldBox.Core.Explore;

/// <summary>
/// The Amiga program's land's recovery (Pool of Radiance Amiga v1.0 <c>program</c>, hunk 13 +0x2872, read s60;
/// docs/re/geo-format.md "The land recovers", Amiga): the same callers (the script switch into block 26, hunk 16
/// +0x50F8; the game Load with $4AA2 ≠ 0, hunk 16 +0x1544) and the same order of views and pauses as GAME.OVR 0x20D50,
/// with its own tables (<see cref="AmigaOverlandRecoveryTables"/>, the module's <c>art/amiga/overland-recovery.json</c>,
/// read from the player's <c>program</c> at import) and three differences, all in the tiles alone:
/// <list type="bullet">
/// <item>One colour map for both lists (<see cref="AmigaOverlandRecoveryTables.Map"/>, hunk 14 +0x248), no "from" list.</item>
/// <item>The recolour (hunk 27 +0x7160, assembly) works on the 4 bitplanes in place, pixel by pixel in raster order;
/// in the random mode every pixel whose pen the map changes asks <see cref="Generator"/> for a byte and changes when
/// its bit 7 is clear (about half the pixels a pass, DOS about a quarter). In place equals DOS's copy while the map
/// changes no pen twice (map[map[c]] = map[c]; <see cref="AmigaOverlandRecoveryTables.Problem"/>).</item>
/// <item>The generator is not the game's RandSeed: it mixes a state byte of its own with the beam position (see
/// <see cref="Generator"/>), so the game's random numbers do not move. The DOS rules (and RandSeed's DOS draws) run
/// under every art setting; this pass only recolours the Amiga tile sheets.</item>
/// </list>
/// The sheets are loaded once a session as on DOS (hunk 25 +0x15F6, the game init hunk 25 +0x12A8); the combat grounds
/// and BACPAC overwrite the low sheet from tile 0 (hunk 22 +0x177A: at most 42 tiles), below every listed tile.
/// </summary>
public static class AmigaOverlandRecovery
{
    /// <summary>
    /// hunk 13 +0x2872: the views and pauses in DOS's order, the Amiga pass on <paramref name="tile"/> (tile t's pixels,
    /// 24 × 24, row-major, changed in place, each the tile pen plus Pen0, the host's palette run; null skips the tile).
    /// <paramref name="beam"/> gives a beam position byte (see <see cref="Generator"/>).
    /// </summary>
    public static void Run(AmigaOverlandRecoveryTables tables, bool animated, Func<int, (byte[] Px, int Pen0)?> tile,
        Generator generator, Func<byte> beam, Action<int, int>? frame = null, Action? pause = null)
    {
        if (animated)
        {
            frame?.Invoke(tables.First.X, tables.First.Y);
            foreach (var (x, y) in tables.Pan)
            {
                Pass(tables, tile, true, generator, beam);
                frame?.Invoke(x, y);
            }
        }
        Pass(tables, tile, false, generator, beam);
        if (animated)
        {
            frame?.Invoke(tables.Last.X, tables.Last.Y);
            pause?.Invoke();
            pause?.Invoke();
        }
    }

    /// <summary>Both lists once with the one map (hunk 13 +0x290A–0x29CA random, +0x29F8–0x2AB2 whole).</summary>
    public static void Pass(AmigaOverlandRecoveryTables tables, Func<int, (byte[] Px, int Pen0)?> tile, bool randomMode,
        Generator generator, Func<byte> beam)
    {
        foreach (byte t in tables.ListA)
            Recolour(tile(t), tables.Map, randomMode, generator, beam);
        foreach (byte t in tables.ListB)
            Recolour(tile(t), tables.Map, randomMode, generator, beam);
    }

    /// <summary>
    /// hunk 27 +0x7160 on one 24 × 24 item: each pixel in raster order (rows, then the three plane bytes, bit 7 the
    /// leftmost), its pen c = the four plane bits; when map[c] ≠ c, in the random mode a byte from
    /// <paramref name="generator"/> with bit 7 set keeps the pixel, else it becomes map[c] (in place). The generator
    /// is called only for pixels the map changes. A null item does nothing (the routine returns at once on a null
    /// block, and a missing tile is not drawn). <paramref name="pen0"/> is added to every pen (the host's palette run;
    /// a pixel outside the run's 16 is left).
    /// </summary>
    public static void Recolour(byte[]? px, IReadOnlyList<byte> map, bool randomMode, Generator generator,
        Func<byte> beam, int pen0 = 0)
    {
        if (px is null)
            return;
        for (int i = 0; i < px.Length; i++)
        {
            int c = px[i] - pen0;
            if (c is < 0 or > 15 || map[c] == c)
                continue;
            if (randomMode && (generator.Next(beam) & 0x80) != 0)
                continue;
            px[i] = (byte)(pen0 + map[c]);
        }
    }

    private static void Recolour((byte[] Px, int Pen0)? tile, IReadOnlyList<byte> map, bool randomMode,
        Generator generator, Func<byte> beam)
    {
        if (tile is var (px, pen0))
            Recolour(px, map, randomMode, generator, beam, pen0);
    }

    /// <summary>The recoloured state alone (the whole mode, no generator calls): what the tiles hold after either caller.</summary>
    public static void ApplyFinal(AmigaOverlandRecoveryTables tables, Func<int, (byte[] Px, int Pen0)?> tile) =>
        Pass(tables, tile, false, new Generator(tables), () => 0);

    /// <summary>
    /// hunk 27 +0x72C8: the state byte s (hunk 27 +0x7314 in the code hunk: 0 at load, kept for the session) becomes
    /// (High[s &amp; 15] &lt;&lt; 4 | Low[s &gt;&gt; 4]) + VHPOSR low byte ($DFF007, the beam's horizontal position) +
    /// VHPOSR high byte ($DFF006, the vertical position's low 8 bits), mod 256, and is returned (the two nibble tables
    /// are the module's, <see cref="AmigaOverlandRecoveryTables.High"/> / <see cref="AmigaOverlandRecoveryTables.Low"/>).
    /// [?] The beam bytes depend on the machine's timing (when each call runs against the display); the Client feeds
    /// them from its own random source, so the dissolve's pattern is not FS-UAE's.
    /// </summary>
    public sealed class Generator(AmigaOverlandRecoveryTables tables)
    {
        /// <summary>The state byte (hunk 27 +0x7314).</summary>
        public byte State { get; set; }

        /// <summary>The calls made.</summary>
        public int Calls { get; private set; }

        /// <summary>One call: <paramref name="beam"/> is read twice, $DFF007 then $DFF006.</summary>
        public byte Next(Func<byte> beam)
        {
            Calls++;
            int s = State;
            int d = tables.High[s & 0x0F] << 4 | tables.Low[(s >> 4) & 0x0F];
            d += beam();
            d += beam();
            State = (byte)d;
            return State;
        }
    }
}

/// <summary>
/// The Amiga land's recovery tables (a module's <c>art/amiga/overland-recovery.json</c>; PoR Amiga v1.0: read by the
/// importer from the player's <c>program</c>): the two tile lists (bit 7: the high sheet, item t &amp; 0x7F; hunk 14
/// +0x22B, +0x236), the one colour map (16 pens, hunk 14 +0x248), the view squares before and after the frames (code
/// immediates, hunk 13 +0x28EC, +0x2AB8) and one per frame (<see cref="Pan"/>, hunk 14 +0x258 / +0x26A; its length is
/// the frame count), and the generator's two nibble tables (16 entries each, hunk 27 +0x7315 / +0x7325).
/// </summary>
public sealed record AmigaOverlandRecoveryTables(
    IReadOnlyList<byte> ListA, IReadOnlyList<byte> ListB, IReadOnlyList<byte> Map, (int X, int Y) First,
    (int X, int Y) Last, IReadOnlyList<(int X, int Y)> Pan, IReadOnlyList<byte> High, IReadOnlyList<byte> Low)
{
    /// <summary>Null when the tables have the routine's shape, else what is wrong: 16-entry map and nibble tables (the
    /// nibbles 0–15), a frame, and a map that changes no pen twice (the in-place recolour relies on it).</summary>
    public string? Problem()
    {
        const int c = OverlandRecovery.Colours;
        if (Map.Count != c || High.Count != c || Low.Count != c)
            return "the map and the generator's tables need 16 entries each";
        if (Map.Any(p => p >= c) || High.Any(p => p >= c) || Low.Any(p => p >= c))
            return "pens and nibbles are 0–15";
        if (Pan.Count == 0)
            return "no pan frame";
        for (int p = 0; p < c; p++)
            if (Map[Map[p]] != Map[p])
                return "the map changes a pen twice";
        return null;
    }
}
