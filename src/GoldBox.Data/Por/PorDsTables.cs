using System.Buffers.Binary;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Reads the program's tables from its data segment once their DS offsets are known. Shared by
/// <see cref="PorProgram"/> / <see cref="PorSpellData"/> (offsets located through the code in START.EXE and GAME.OVR)
/// and <see cref="PorProgramData"/> (offsets stored in a module's <c>rules/program.json</c>), so both read the same
/// bytes the same way. <c>ds</c> is the data segment from DS:0000 to the end of the load image; a read past it behaves
/// as the original reader did on the image.
/// </summary>
public static class PorDsTables
{
    public static ushort Word(ReadOnlySpan<byte> ds, int off) => BinaryPrimitives.ReadUInt16LittleEndian(ds[off..]);

    /// <summary>A Pascal string of at most <paramref name="max"/> printable characters at DS:<paramref name="off"/>, or null.</summary>
    public static string? PascalString(ReadOnlySpan<byte> ds, int off, int max = 255)
    {
        if (off < 0 || off >= ds.Length)
            return null;
        int n = ds[off];
        if (n > max || off + 1 + n > ds.Length)
            return null;
        var b = ds.Slice(off + 1, n);
        foreach (var c in b)
            if (c < 0x20 || c > 0x7E)
                return null;
        return System.Text.Encoding.Latin1.GetString(b);
    }

    /// <summary>The spell table at its DS base (entry 0 is skipped, as the program indexes from 1), or null when cut off.</summary>
    public static SpellTable? Spells(ReadOnlySpan<byte> ds, int @base)
    {
        int start = @base + SpellTable.EntrySize;
        if (start + SpellTable.Count * SpellTable.EntrySize > ds.Length)
            return null;
        return SpellTable.FromBytes(ds.Slice(start, SpellTable.Count * SpellTable.EntrySize));
    }

    /// <summary>Experience table entries (8 classes × 14 levels + 1, signed dwords), or null when cut off.</summary>
    public const int ExperienceEntries = 8 * 14 + 1;

    public static int[]? Experience(ReadOnlySpan<byte> ds, int @base)
    {
        if (@base < 0 || @base + ExperienceEntries * 4 > ds.Length)
            return null;
        var t = new int[ExperienceEntries];
        for (int k = 0; k < ExperienceEntries; k++)
            t[k] = BinaryPrimitives.ReadInt32LittleEndian(ds[(@base + 4 * k)..]);
        return t;
    }

    /// <summary>The name lists of handlers 18, 26, 47 (slots 1..n of 16 bytes at each base) and handler 48's two names.</summary>
    public static EffectNameTables EffectNames(ReadOnlySpan<byte> ds, IReadOnlyList<int> bases, IReadOnlyList<byte[]>? codeNames)
    {
        List<byte[]> Slots(ReadOnlySpan<byte> d, int b, int n)
        {
            var l = new List<byte[]>();
            for (int k = 1; k <= n; k++)
            {
                int off = b + 16 * k;
                l.Add(off + 16 <= d.Length ? d.Slice(off, 16).ToArray() : new byte[16]);
            }
            return l;
        }
        return new EffectNameTables
        {
            GnomeFoes = Slots(ds, bases[0], 4),
            DwarfFoes = Slots(ds, bases[1], 8),
            GiantFoes = Slots(ds, bases[2], 8),
            GnomeDefenseFoes = codeNames is null ? [] : [.. codeNames.Select(s => s.ToArray())],
        };
    }

    public static byte[] Bytes(ReadOnlySpan<byte> ds, int off, int count) => ds.Slice(off, count).ToArray();

    public static ushort[] Words(ReadOnlySpan<byte> ds, int off, int count)
    {
        var t = new ushort[count];
        for (int k = 0; k < count; k++)
            t[k] = Word(ds, off + 2 * k);
        return t;
    }

    public static TurnUndeadTable? TurnUndead(ReadOnlySpan<byte> ds, int @base) =>
        @base + TurnUndeadTable.Length > ds.Length ? null : new TurnUndeadTable(ds.Slice(@base, TurnUndeadTable.Length).ToArray());

    /// <summary>The v1.3 NPC portrait table bases Core indexes (head; bodies).</summary>
    public static bool IsV13NpcPortraits(int head, IEnumerable<int> bodies) =>
        head == 0x08C7 && bodies.Order().SequenceEqual([0x08D7, 0x08E1, 0x08EB, 0x08F5]);

    public static byte? Byte(ReadOnlySpan<byte> ds, int off) => off >= 0 && off < ds.Length ? ds[off] : null;

    /// <summary>Item name word <paramref name="w"/> from the string[20] array at <paramref name="base"/> (DS:base + 21·w).</summary>
    public static string? ItemWord(ReadOnlySpan<byte> ds, int @base, int w) =>
        w is < 1 or > 255 ? null : PascalString(ds, @base + 21 * w, 20);

    /// <summary>The temple's ten heal names from the string[40] array at <paramref name="base"/>, or null.</summary>
    public static IReadOnlyList<string>? TempleNames(ReadOnlySpan<byte> ds, int @base)
    {
        var names = new List<string>();
        for (int k = 1; k <= 11; k++)
            if (PascalString(ds, @base + 41 * k, 40) is { Length: > 0 } s)
                names.Add(s);
            else
                break;
        return names.Count == 10 ? names : null;
    }
}
