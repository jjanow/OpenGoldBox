using System.Buffers.Binary;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// The spell table and the monster-name lists of the effect handlers, read from the player's program (nothing from the
/// program is copied into our code). Located through code patterns (docs/re/spells-effects.md "Where the data is").
/// </summary>
public static class PorSpellData
{
    /// <summary>
    /// The spell table: its DS offset is the immediate of START 0x3262's <c>mov cl,4 / shl di,cl / mov al,[di+imm] /
    /// cmp al,0 / jne</c> (the casting-level routine reads the class byte, field 0). Null when not found.
    /// </summary>
    public static SpellTable? Spells(PorProgram p)
    {
        ReadOnlySpan<byte> pat = [0xB1, 0x04, 0xD3, 0xE7, 0x8A, 0x85];
        var img = p.Image.AsSpan();
        for (int i = img.IndexOf(pat); i >= 0;)
        {
            int at = i + pat.Length;
            if (at + 5 <= img.Length && img[at + 2] == 0x3C && img[at + 3] == 0x00 && img[at + 4] == 0x75)
            {
                int ds = BinaryPrimitives.ReadUInt16LittleEndian(img[at..]);
                int start = p.DataSegment + ds + SpellTable.EntrySize;
                if (start + SpellTable.Count * SpellTable.EntrySize > img.Length)
                    return null;
                return SpellTable.FromBytes(img.Slice(start, SpellTable.Count * SpellTable.EntrySize));
            }
            int next = img[(i + 1)..].IndexOf(pat);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return null;
    }

    /// <summary>
    /// The name lists of handlers 18, 26, 47 (DS tables found through their <c>mov cl,4 / shl di,cl / add di,imm /
    /// push ds / push di / lcall</c> (the name compare) in GAME.OVR, in handler order) and 48 (two strings in the code segment right before the handler).
    /// Each slot is read as the program compares it: 16 bytes, the first the length.
    /// </summary>
    public static EffectNameTables? Names(PorProgram p)
    {
        if (p.Overlay is not { } o)
            return null;
        var ov = o.AsSpan();
        ReadOnlySpan<byte> pat = [0xB1, 0x04, 0xD3, 0xE7, 0x81, 0xC7];
        var tables = new List<int>();
        for (int i = ov.IndexOf(pat); i >= 0 && tables.Count < 3;)
        {
            int at = i + pat.Length;
            if (at + 5 <= ov.Length && ov[at + 2] == 0x1E && ov[at + 3] == 0x57 && ov[at + 4] == 0x9A)
            {
                int v = BinaryPrimitives.ReadUInt16LittleEndian(ov[at..]);
                if (!tables.Contains(v))
                    tables.Add(v);
            }
            int next = ov[(i + 1)..].IndexOf(pat);
            i = next < 0 ? -1 : i + 1 + next;
        }
        if (tables.Count < 3)
            return null;
        List<byte[]> Slots(int ds, int n)
        {
            var l = new List<byte[]>();
            for (int k = 1; k <= n; k++)
            {
                int off = p.DataSegment + ds + 16 * k;
                l.Add(off + 16 <= p.Image.Length ? p.Image.AsSpan(off, 16).ToArray() : new byte[16]);
            }
            return l;
        }
        return new EffectNameTables
        {
            GnomeFoes = Slots(tables[0], 4),
            DwarfFoes = Slots(tables[1], 8),
            GiantFoes = Slots(tables[2], 8),
            GnomeDefenseFoes = CodeNames(ov) ?? [],
        };
    }

    /// <summary>Handler 48's two names: the Pascal strings right before its code (current character's type byte test).</summary>
    private static List<byte[]>? CodeNames(ReadOnlySpan<byte> ov)
    {
        ReadOnlySpan<byte> code = [0x55, 0x89, 0xE5, 0xC4, 0x3E, 0x92, 0x5D, 0x26, 0x80, 0xBD, 0x9F, 0x00, 0x01, 0x75];
        for (int x = ov.IndexOf(code); x >= 0;)
        {
            int imm1At = x + code.Length + 1 + 6 + 1;
            if (imm1At + 2 <= ov.Length && ov[imm1At - 1] == 0xBF)
            {
                int imm1 = BinaryPrimitives.ReadUInt16LittleEndian(ov[imm1At..]);
                int second = ov[imm1At..].IndexOf((byte)0xBF);
                if (second > 0 && imm1At + second + 3 <= ov.Length)
                {
                    int imm2 = BinaryPrimitives.ReadUInt16LittleEndian(ov[(imm1At + second + 1)..]);
                    int gap = imm2 - imm1;
                    for (int len2 = 0; len2 < 16 && gap > 0; len2++)
                    {
                        int s2 = x - 1 - len2;
                        int s1 = s2 - gap;
                        if (s1 >= 0 && ov[s2] == len2 && ov[s1] == gap - 1)
                            return [Slot(ov, s1), Slot(ov, s2)];
                    }
                }
            }
            int next = ov[(x + 1)..].IndexOf(code);
            x = next < 0 ? -1 : x + 1 + next;
        }
        return null;
    }

    private static byte[] Slot(ReadOnlySpan<byte> b, int at)
    {
        var s = new byte[16];
        int n = Math.Min(16, Math.Min(b[at] + 1, b.Length - at));
        b.Slice(at, n).CopyTo(s);
        return s;
    }
}
