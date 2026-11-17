using System.Buffers.Binary;

namespace GoldBox.Data.Por;

/// <summary>
/// Rule tables read from the player's <c>START.EXE</c> data segment, located through code in <c>GAME.OVR</c>
/// (docs/re/item-format.md "Where the tables are found"; same method as <c>tools/scripts/gbitem.py</c>). Nothing from
/// the program is copied into our code.
/// </summary>
public sealed class PorProgram
{
    /// <summary>The START.EXE load image (EXEPACK expanded when packed).</summary>
    public byte[] Image { get; }
    /// <summary>Image offset of the data segment (DS × 16).</summary>
    public int DataSegment { get; }
    /// <summary>GAME.OVR, or null.</summary>
    public byte[]? Overlay { get; }

    public PorProgram(byte[] startExe, byte[]? gameOvr)
    {
        var (image, cs, ip) = LoadImage(startExe);
        Image = image;
        DataSegment = FindDataSegment(image, cs, ip)
            ?? throw new FormatException("START.EXE: data segment not found (not a Turbo Pascal program?)");
        Overlay = gameOvr;
    }

    public static PorProgram? Open(PorGameDirectory game)
    {
        var exe = game.Find("START.EXE");
        if (exe is null)
            return null;
        var ovr = game.Find("GAME.OVR");
        return new PorProgram(File.ReadAllBytes(exe), ovr is null ? null : File.ReadAllBytes(ovr));
    }

    /// <summary>A little-endian word at data-segment offset <paramref name="ds"/>.</summary>
    public ushort Word(int ds) => BinaryPrimitives.ReadUInt16LittleEndian(Image.AsSpan(DataSegment + ds));

    /// <summary>
    /// DS offset of the random item array base (v1.3: 0x0970): the immediate of the generator's
    /// <c>mov di,ax / shl di,1 / mov dl,[di+imm]</c> in GAME.OVR (most common one), or null.
    /// </summary>
    public int? RandomItemBase()
    {
        if (Overlay is null)
            return null;
        ReadOnlySpan<byte> pat = [0x8B, 0xF8, 0xD1, 0xE7, 0x8A, 0x95];
        var counts = new Dictionary<int, int>();
        var o = Overlay.AsSpan();
        for (int i = o.IndexOf(pat); i >= 0 && i + pat.Length + 2 <= o.Length;)
        {
            int v = BinaryPrimitives.ReadUInt16LittleEndian(o[(i + pat.Length)..]);
            counts[v] = counts.GetValueOrDefault(v) + 1;
            int next = o[(i + 1)..].IndexOf(pat);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return counts.Count == 0 ? null : counts.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>The words of the random item array from its base, as the item maker indexes them, or null.</summary>
    public ushort[]? RandomItemTable(int words = Core.Game.TreasureMaker.TableWords)
    {
        if (RandomItemBase() is not { } b)
            return null;
        var t = new ushort[words];
        for (int k = 0; k < words; k++)
            t[k] = Word(b + 2 * k);
        return t;
    }

    // ---- MZ / EXEPACK ---------------------------------------------------------------------------------------------

    /// <summary>The load module of an MZ file, expanded when EXEPACKed (documented Microsoft scheme; own code).</summary>
    public static (byte[] Image, int Cs, int Ip) LoadImage(byte[] raw)
    {
        if (raw.Length < 0x1C || !(raw[0] == 'M' && raw[1] == 'Z' || raw[0] == 'Z' && raw[1] == 'M'))
            throw new FormatException("not an MZ executable");
        int U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
        int cblp = U16(raw, 2), cp = U16(raw, 4), hdr = U16(raw, 8), ip = U16(raw, 0x14), cs = U16(raw, 0x16);
        int size = cblp != 0 ? (cp - 1) * 512 + cblp : cp * 512;
        var lm = raw[(hdr * 16)..Math.Min(size, raw.Length)];
        int eh = cs * 16;
        if (lm.Length < eh + 18)
            return (lm, cs, ip);
        int rip, rcs, dlen, skip;
        if (lm[eh + 16] == 'R' && lm[eh + 17] == 'B')
        {
            rip = U16(lm, eh); rcs = U16(lm, eh + 2); dlen = U16(lm, eh + 12); skip = U16(lm, eh + 14);
        }
        else if (lm[eh + 14] == 'R' && lm[eh + 15] == 'B')
        {
            rip = U16(lm, eh); rcs = U16(lm, eh + 2); dlen = U16(lm, eh + 12); skip = 1;
        }
        else
            return (lm, cs, ip);
        int srcEnd = eh - (skip - 1) * 16;
        var buf = new byte[Math.Max(dlen * 16, srcEnd)];
        Array.Copy(lm, buf, srcEnd);
        int s = srcEnd - 1, pad = 0;
        while (buf[s] == 0xFF && pad < 16) { s--; pad++; }
        int d = dlen * 16 - 1;
        while (true)
        {
            int op = buf[s];
            int len = buf[s - 1] << 8 | buf[s - 2];
            s -= 3;
            if ((op & 0xFE) == 0xB0)
            {
                byte fill = buf[s--];
                Array.Fill(buf, fill, d - len + 1, len);
                d -= len;
            }
            else if ((op & 0xFE) == 0xB2)
            {
                Array.Copy(buf, s - len + 1, buf, d - len + 1, len);
                d -= len;
                s -= len;
            }
            else
                throw new FormatException($"EXEPACK: bad opcode {op:X2}");
            if (s < -1 || d < -1)
                throw new FormatException("EXEPACK: stream overrun");
            if ((op & 1) != 0)
                break;
        }
        return (buf[..(dlen * 16)], rcs, rip);
    }

    /// <summary>
    /// Turbo Pascal: the entry point first far-calls System init, which starts with <c>mov dx,DATA / mov ds,dx</c>.
    /// </summary>
    public static int? FindDataSegment(byte[] img, int cs, int ip)
    {
        int e = cs * 16 + ip;
        if (e + 5 > img.Length || img[e] != 0x9A)
            return null;
        int off = BinaryPrimitives.ReadUInt16LittleEndian(img.AsSpan(e + 1));
        int seg = BinaryPrimitives.ReadUInt16LittleEndian(img.AsSpan(e + 3));
        int t = seg * 16 + off;
        for (int k = t; k < t + 16 && k + 5 <= img.Length; k++)
            if (img[k] == 0xBA && img[k + 3] == 0x8E && img[k + 4] == 0xDA)
                return BinaryPrimitives.ReadUInt16LittleEndian(img.AsSpan(k + 1)) * 16;
        return null;
    }
}
