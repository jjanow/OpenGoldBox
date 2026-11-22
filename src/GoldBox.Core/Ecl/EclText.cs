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

    /// <summary>A string slot's capacity: the slots are Pascal string[255].</summary>
    public const int SlotLength = 255;

    /// <summary>
    /// What a string slot keeps of <paramref name="text"/>: the unpacker (GAME.OVR 0x8626) appends each character with
    /// Turbo Pascal's Concat and stores the result with at most 255 characters (0x873D–0x8757), so a text of more than
    /// 255 characters (up to 340 fit the operand's 255 bytes) keeps its first 255 (P s47, por_print_oracle.py "decode").
    /// </summary>
    public static string Slot(string text) => text.Length > SlotLength ? text[..SlotLength] : text;

    /// <summary>
    /// The Amiga's slot (read s47): the unpacker (hunk 4 +0x13E8–0x1506) joins the slot and each character in a scratch
    /// buffer (hunk 27 +0x6F96: the first string copied for at most 255 bytes, then one byte back, the character, a NUL)
    /// and copies the result back (strncpy, 256). Once the slot holds 255 characters the copy stops without its NUL and
    /// the step back drops the 255th, so every later character replaces it: a longer text keeps its first 254 and its
    /// last character (the 256-byte copy also puts its NUL on the next slot's first byte, which a later operand of the
    /// same instruction overwrites). No v1.3 text is that long.
    /// </summary>
    public static string SlotAmiga(string text) =>
        text.Length > SlotLength ? text[..(SlotLength - 1)] + text[^1] : text;

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
