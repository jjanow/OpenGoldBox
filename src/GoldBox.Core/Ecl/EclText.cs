using System.Text;

namespace GoldBox.Core.Ecl;

/// <summary>Inline text of operand type 0x80 (docs/re/ecl-format.md "Operand encoding", text packing).</summary>
public static class EclText
{
    /// <summary>
    /// Unpacks 6-bit codes, most significant bit first (3 bytes → 4 codes; a trailing partial group yields as many
    /// whole codes as it has bits). Code 0 produces nothing, 1–31 → code + 64, 32–63 → code.
    /// </summary>
    public static string Unpack(ReadOnlySpan<byte> packed)
    {
        var sb = new StringBuilder(packed.Length * 4 / 3);
        int acc = 0, bits = 0;
        foreach (var b in packed)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 6)
            {
                bits -= 6;
                int c = (acc >> bits) & 63;
                if (c != 0)
                    sb.Append((char)(c < 32 ? c + 64 : c));
            }
            acc &= (1 << bits) - 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Packs upper-case text for synthetic scripts (tests, tools). Characters must be in 0x20–0x5F; the inverse of
    /// <see cref="Unpack"/> for such text (trailing bits are zero, i.e. code 0, which unpacks to nothing).
    /// </summary>
    public static byte[] Pack(string text)
    {
        var codes = new List<int>(text.Length);
        foreach (var ch in text)
        {
            if (ch < 0x20 || ch > 0x5F || ch == '@') // '@' would be code 0, which unpacks to nothing
                throw new ArgumentException($"character U+{(int)ch:X4} cannot be packed", nameof(text));
            codes.Add(ch >= 0x40 ? ch - 64 : ch);
        }
        int totalBits = codes.Count * 6;
        var outBytes = new byte[(totalBits + 7) / 8];
        int bit = 0;
        foreach (var c in codes)
        {
            for (int k = 5; k >= 0; k--, bit++)
                if (((c >> k) & 1) != 0)
                    outBytes[bit >> 3] |= (byte)(0x80 >> (bit & 7));
        }
        return outBytes;
    }
}
