using System.Buffers.Binary;
using GoldBox.Core.Explore;

namespace GoldBox.Data.Por;

/// <summary>
/// DS offsets and counts of the land's recovery tables as GAME.OVR's 0x20D50 uses them (v1.3: list A DS:0900 × 11,
/// list B DS:090B × 18, from DS:091D, maps DS:092D / DS:093D, pan x DS:094D and y DS:095F × 9 words; the squares before
/// and after the frames are code immediates).
/// </summary>
public sealed record PorOverlandRecoveryOffsets(
    int ListA, int CountA, int ListB, int CountB, int From, int ToA, int ToB, int PanX, int PanY, int Frames,
    (int X, int Y) First, (int X, int Y) Last);

/// <summary>
/// The land's recovery tables (<see cref="OverlandRecoveryTables"/>, docs/re/geo-format.md "The land recovers"), read
/// from the player's program at the places GAME.OVR's code names (as <see cref="PorOverland.Locate"/> does for the
/// overland), so nothing from the program is kept in our code (s60; Core held them as constants after s58).
/// </summary>
public static class PorOverlandRecovery
{
    /// <summary>
    /// <c>cmp byte [di+list],7Fh / jbe +2Ch / mov di,from / push ds / push di / mov di,to / push ds / push di</c>: a
    /// list item's sheet test and the recolour's two maps (0x20D8A, 0x20DFB, 0x20E99, 0x20F0A).
    /// </summary>
    private static readonly byte?[] ListPattern =
        [0x80, 0xBD, null, null, 0x7F, 0x76, 0x2C, 0xBF, null, null, 0x1E, 0x57, 0xBF, null, null, 0x1E, 0x57];

    /// <summary><c>cmp byte [bp-1],n / jne</c>: a list loop's last index (0x20DE5, 0x20E56).</summary>
    private static readonly byte?[] ListEndPattern = [0x80, 0x7E, 0xFF, null, 0x75];

    /// <summary><c>mov di,ax / shl di,1 / push word [di+table]</c>: a pan square's x, then y (0x20E61, 0x20E6E).</summary>
    private static readonly byte?[] PanPattern = [0x8B, 0xF8, 0xD1, 0xE7, 0xFF, 0xB5, null, null];

    /// <summary><c>cmp byte [bp-2],n / je</c>: the frame loop's last index (0x20E80).</summary>
    private static readonly byte?[] FrameEndPattern = [0x80, 0x7E, 0xFE, null, 0x74];

    /// <summary><c>mov ax,x / push ax / mov ax,y / push ax / call far</c>: the view square before and after the frames (0x20D5F, 0x20F71).</summary>
    private static readonly byte?[] SquarePattern = [0xB8, null, null, 0x50, 0xB8, null, null, 0x50, 0x9A];

    /// <summary>How far before the first list test the routine starts (0x20D50 → 0x20D8A: 0x3A).</summary>
    private const int Reach = 0x80;

    /// <summary>The tables' places from GAME.OVR's code, or null when the code is not the routine's.</summary>
    public static PorOverlandRecoveryOffsets? Locate(byte[]? overlay)
    {
        if (overlay is null)
            return null;
        var lists = Find(overlay, ListPattern, 0, overlay.Length);
        if (lists.Count != 4)
            return null;
        int Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(overlay.AsSpan(at));
        int a = lists[0], b = lists[1];
        int listA = Word(a + 2), listB = Word(b + 2), from = Word(a + 8), toA = Word(a + 13), toB = Word(b + 13);
        // The whole-mode pass tests the same lists with the same maps (0x20E99, 0x20F0A).
        if (Word(lists[2] + 2) != listA || Word(lists[3] + 2) != listB || Word(b + 8) != from
            || Word(lists[2] + 8) != from || Word(lists[3] + 8) != from || Word(lists[2] + 13) != toA
            || Word(lists[3] + 13) != toB)
            return null;
        var endA = Find(overlay, ListEndPattern, a, b);
        var endB = Find(overlay, ListEndPattern, b, lists[2]);
        var pan = Find(overlay, PanPattern, b, lists[2]);
        var frames = Find(overlay, FrameEndPattern, b, lists[2]);
        var before = Find(overlay, SquarePattern, Math.Max(0, a - Reach), a);
        var after = Find(overlay, SquarePattern, lists[3], Math.Min(overlay.Length, lists[3] + Reach * 2));
        if (endA.Count == 0 || endB.Count == 0 || pan.Count != 2 || frames.Count != 1 || before.Count == 0 || after.Count == 0)
            return null;
        (int, int) Square(int at) => (Word(at + 1), Word(at + 5));
        return new PorOverlandRecoveryOffsets(listA, overlay[endA[^1] + 3] + 1, listB, overlay[endB[^1] + 3] + 1, from, toA,
            toB, Word(pan[0] + 6), Word(pan[1] + 6), overlay[frames[0] + 3] + 1, Square(before[^1]), Square(after[0]));
    }

    /// <summary>The tables from the data segment at <paramref name="o"/>, or null when they run past it.</summary>
    public static OverlandRecoveryTables? Read(ReadOnlySpan<byte> ds, PorOverlandRecoveryOffsets o)
    {
        const int c = OverlandRecovery.Colours;
        if (o.ListA + o.CountA > ds.Length || o.ListB + o.CountB > ds.Length || o.From + c > ds.Length
            || o.ToA + c > ds.Length || o.ToB + c > ds.Length || o.PanX + 2 * o.Frames > ds.Length
            || o.PanY + 2 * o.Frames > ds.Length)
            return null;
        var pan = new (int X, int Y)[o.Frames];
        for (int f = 0; f < o.Frames; f++)
            pan[f] = (BinaryPrimitives.ReadUInt16LittleEndian(ds[(o.PanX + 2 * f)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(ds[(o.PanY + 2 * f)..]));
        return new OverlandRecoveryTables(ds.Slice(o.ListA, o.CountA).ToArray(), ds.Slice(o.ListB, o.CountB).ToArray(),
            ds.Slice(o.From, c).ToArray(), ds.Slice(o.ToA, c).ToArray(), ds.Slice(o.ToB, c).ToArray(), o.First, o.Last, pan);
    }

    private static List<int> Find(byte[] code, byte?[] pattern, int from, int to)
    {
        var found = new List<int>();
        for (int i = from; i + pattern.Length <= to && i + pattern.Length <= code.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < pattern.Length && ok; k++)
                ok = pattern[k] is not { } v || code[i + k] == v;
            if (ok)
                found.Add(i);
        }
        return found;
    }
}
