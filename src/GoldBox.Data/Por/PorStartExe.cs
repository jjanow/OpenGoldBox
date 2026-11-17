using System.Buffers.Binary;
using GoldBox.Core.Explore;

namespace GoldBox.Data.Por;

/// <summary>
/// Tables of the 3D view read from the player's <c>START.EXE</c> (docs/re/view3d.md "Program tables"). Nothing is
/// copied into our code: the tables are found by their structure, the wall-piece layout (DS:0C8C..0CD1), which must
/// tile the 156-byte wall record exactly. Found at the same DS offsets in v1.1, v1.2 and v1.3 (v1.3's START.EXE is
/// EXEPACKed, but this part of the data segment is stored literally).
/// </summary>
public sealed class PorStartExe
{
    /// <summary>DS:0C8C − DS:0C26: the sky-colour list sits 102 bytes before the piece table.</summary>
    private const int SkyListBefore = 0x0C8C - 0x0C26;
    private const int LayoutBytes = 3 * 10 + 2 * 20;

    public ViewLayout Layout { get; }

    /// <summary>
    /// DS:0C26, 16 bytes: the EGA colour of each sky index the scripts store in $49FD/$49FE (RefreshView looks up
    /// the cell's index here).
    /// </summary>
    public IReadOnlyList<byte> SkyColours { get; }

    /// <summary>File offset where the piece table was found (diagnostics).</summary>
    public int LayoutOffset { get; }

    public PorStartExe(ReadOnlySpan<byte> exe)
    {
        int found = -1;
        for (int p = SkyListBefore; p + LayoutBytes <= exe.Length; p++)
        {
            if (TryLayout(exe, p) is null)
                continue;
            if (found >= 0)
                throw new FormatException($"START.EXE: wall-piece table found twice ({found}, {p})");
            found = p;
        }
        if (found < 0)
            throw new FormatException("START.EXE: wall-piece table not found (not Pool of Radiance DOS?)");
        LayoutOffset = found;
        Layout = TryLayout(exe, found)!;
        var sky = exe.Slice(found - SkyListBefore, 16).ToArray();
        if (sky.Any(c => c > 15))
            throw new FormatException("START.EXE: sky colour list holds a non-EGA value");
        SkyColours = sky;
        ExploreCommands = FindCommandLine(exe);
    }

    public static PorStartExe Open(string path) => new(File.ReadAllBytes(path));

    /// <summary>
    /// The words of the 3D area's command line (DS:04B6 in v1.1/v1.2; moved by the packing in v1.3), or null when not
    /// found. Found by structure, not by text: a Pascal string of single-space-separated capitalised words, followed
    /// within 64 bytes by the overland command line, which is the same string without its first word (DS:04DF).
    /// </summary>
    public IReadOnlyList<string>? ExploreCommands { get; }

    public static IReadOnlyList<string>? FindCommandLine(ReadOnlySpan<byte> b)
    {
        for (int p = 0; p + 1 < b.Length; p++)
        {
            int n = b[p];
            if (n is < 8 or > 60 || p + 1 + n > b.Length || Words(b.Slice(p + 1, n)) is not { Length: >= 3 } words)
                continue;
            var rest = string.Join(' ', words[1..]);
            for (int q = p + 1 + n; q < Math.Min(b.Length - 1, p + 1 + n + 64); q++)
                if (b[q] == rest.Length && q + 1 + rest.Length <= b.Length &&
                    System.Text.Encoding.ASCII.GetString(b.Slice(q + 1, rest.Length)) == rest)
                    return words;
        }
        return null;
    }

    /// <summary>Single-space-separated words of a capital and lower-case letters, or null.</summary>
    private static string[]? Words(ReadOnlySpan<byte> s)
    {
        var text = System.Text.Encoding.ASCII.GetString(s);
        var words = text.Split(' ');
        foreach (var w in words)
            if (w.Length < 2 || !char.IsAsciiLetterUpper(w[0]) || !w[1..].All(char.IsAsciiLetterLower))
                return null;
        return words;
    }

    private static ViewLayout? TryLayout(ReadOnlySpan<byte> b, int p)
    {
        // Cheap prefilter: the piece table starts with 0 and its starts ascend.
        if (b[p] != 0)
            return null;
        var start = new int[10];
        var w = new int[10];
        var h = new int[10];
        var col = new int[10];
        var row = new int[10];
        int k = 0;
        for (int i = 0; i < 10; i++)
        {
            start[i] = b[p + i];
            w[i] = b[p + 10 + i];
            h[i] = b[p + 20 + i];
            if (start[i] != k || w[i] is < 1 or > 11 || h[i] is < 1 or > 11)
                return null;
            k += w[i] * h[i];
        }
        if (k != ViewLayout.WallRecordSize)
            return null;
        for (int i = 0; i < 10; i++)
        {
            col[i] = BinaryPrimitives.ReadInt16LittleEndian(b[(p + 30 + 2 * i)..]);
            row[i] = BinaryPrimitives.ReadInt16LittleEndian(b[(p + 50 + 2 * i)..]);
            // Every piece must start inside the 11×11 view.
            if (col[i] is < 0 or > 10 || row[i] is < 0 or > 10)
                return null;
        }
        return new ViewLayout(start, w, h, col, row);
    }
}
