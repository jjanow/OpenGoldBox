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

    /// <summary>The data segment from DS:0000 to the end of the load image.</summary>
    public ReadOnlySpan<byte> Ds => Image.AsSpan(DataSegment);

    /// <summary>A little-endian word at data-segment offset <paramref name="ds"/>.</summary>
    public ushort Word(int ds) => PorDsTables.Word(Ds, ds);

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

    /// <summary>
    /// DS offset of the AI's step pattern table (v1.3: 0x02AE; docs/re/combat-ai.md): the immediate of
    /// <c>mov al,es:[di+15h] / xor ah,ah / mov di,ax / mov si,di / shl di,1 / shl di,1 / add di,si / add di,dx /
    /// mov al,[di+imm]</c> (5 × combat record +0x15 + attempt) in GAME.OVR (0xB235, 0xB56D), or null.
    /// </summary>
    public int? AiStepOffsetsBase()
    {
        if (Overlay is null)
            return null;
        ReadOnlySpan<byte> pat = [0x26, 0x8A, 0x45, 0x15, 0x30, 0xE4, 0x8B, 0xF8, 0x8B, 0xF7, 0xD1, 0xE7, 0xD1, 0xE7, 0x01, 0xF7,
            0x03, 0xFA, 0x8A, 0x85];
        var o = Overlay.AsSpan();
        int i = o.IndexOf(pat);
        return i < 0 || i + pat.Length + 2 > o.Length ? null : BinaryPrimitives.ReadUInt16LittleEndian(o[(i + pat.Length)..]);
    }

    /// <summary>The AI's step pattern table: <paramref name="count"/> bytes from <see cref="AiStepOffsetsBase"/>
    /// (index 5 × row + attempt, rows 1–6, attempts 1–5; the program can also read attempt 6), or null.</summary>
    public byte[]? AiStepOffsets(int count = 40) =>
        AiStepOffsetsBase() is { } b ? PorDsTables.Bytes(Ds, b, count) : null;

    /// <summary>The words of the random item array from its base, as the item maker indexes them, or null.</summary>
    public ushort[]? RandomItemTable(int words = Core.Game.TreasureMaker.TableWords)
    {
        return RandomItemBase() is { } b ? PorDsTables.Words(Ds, b, words) : null;
    }

    /// <summary>
    /// DS offset of the turn-undead table base (v1.3: 0x0447; docs/re/combat-menu.md "Turn undead"): the immediate of
    /// <c>mov al,[di+imm]</c> in turn undead's row × 10 + column lookup (GAME.OVR 0x13A2B: <c>mov al,es:[di+76h]; cbw;
    /// mov dx,10; mul dx; mov di,ax; add di,cx; mov al,[di+imm]</c>), found once; null when absent or not unique.
    /// </summary>
    public int? TurnUndeadBase()
    {
        if (Overlay is null)
            return null;
        ReadOnlySpan<byte> pat = [0x8A, 0x45, 0x76, 0x98, 0xBA, 0x0A, 0x00, 0xF7, 0xE2, 0x8B, 0xF8, 0x03, 0xF9, 0x8A, 0x85];
        var o = Overlay.AsSpan();
        int i = o.IndexOf(pat);
        if (i < 0 || i + pat.Length + 2 > o.Length || o[(i + 1)..].IndexOf(pat) >= 0)
            return null;
        return BinaryPrimitives.ReadUInt16LittleEndian(o[(i + pat.Length)..]);
    }

    /// <summary>The turn-undead table's bytes from its base (<see cref="TurnUndeadBase"/>), or null.</summary>
    public Core.Game.TurnUndeadTable? TurnUndeadTable()
    {
        return TurnUndeadBase() is { } b ? PorDsTables.TurnUndead(Ds, b) : null;
    }

    /// <summary>
    /// The DS bases of ADDNPC's portrait tables (GAME.OVR 0x1F248; docs/re/ecl-format.md "ADDNPC"), found through the
    /// code that indexes them: <c>add di,dx; mov al,[di+imm]; les di,[bp-12h]; mov es:[di+0BBh|0BCh],al</c>. Head = the
    /// one base written to record 0xBB (v1.3: 0x08C7); bodies = the bases written to 0xBC in code order (v1.3: 0x08D7
    /// class 0, 0x08EB class 5, 0x08F5 class 6, 0x08E1 the others). Null when the code is not found as expected.
    /// </summary>
    public (int Head, int[] Bodies)? NpcPortraitBases()
    {
        if (Overlay is null)
            return null;
        ReadOnlySpan<byte> pre = [0x03, 0xFA, 0x8A, 0x85];
        ReadOnlySpan<byte> post = [0xC4, 0x7E, 0xEE, 0x26, 0x88, 0x85];
        var o = Overlay.AsSpan();
        var heads = new List<int>();
        var bodies = new List<int>();
        for (int i = o.IndexOf(pre); i >= 0 && i + 14 <= o.Length;)
        {
            if (o.Slice(i + 6, post.Length).SequenceEqual(post) && o[i + 13] == 0)
            {
                int imm = BinaryPrimitives.ReadUInt16LittleEndian(o[(i + 4)..]);
                if (o[i + 12] == 0xBB)
                    heads.Add(imm);
                else if (o[i + 12] == 0xBC)
                    bodies.Add(imm);
            }
            int next = o[(i + 1)..].IndexOf(pre);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return heads.Count == 1 && bodies.Count == 4 ? (heads[0], bodies.ToArray()) : null;
    }

    /// <summary>
    /// A byte of the NPC portrait tables at DS:<paramref name="ds"/> (<see cref="Core.Game.IEncounterData.NpcPortraitByte"/>),
    /// or null unless the program's tables sit at the v1.3 bases Core indexes (0x08C7 head; 0x08D7, 0x08E1, 0x08EB,
    /// 0x08F5 bodies). Any DS offset is answered, as the program reads whatever byte a gender outside 0/1 indexes.
    /// </summary>
    public byte? NpcPortraitByte(int ds)
    {
        if (NpcPortraitBases() is not { } t || !PorDsTables.IsV13NpcPortraits(t.Head, t.Bodies))
            return null;
        return PorDsTables.Byte(Ds, ds);
    }

    // ---- Text tables (found through the code that indexes them; nothing is stored in our code) --------------------

    /// <summary>A Pascal string of at most <paramref name="max"/> printable characters at DS:<paramref name="ds"/>, or null.</summary>
    public string? PascalString(int ds, int max = 255) => PorDsTables.PascalString(Ds, ds, max);

    /// <summary>
    /// A Pascal string of at least one printable character at GAME.OVR file offset <paramref name="offset"/> (the
    /// overlay units keep their texts in their code segments), or null.
    /// </summary>
    public string? OverlayString(int offset, int max = 255)
    {
        if (Overlay is null || offset < 0 || offset >= Overlay.Length)
            return null;
        int n = Overlay[offset];
        if (n == 0 || n > max || offset + 1 + n > Overlay.Length)
            return null;
        var b = Overlay.AsSpan(offset + 1, n);
        if (b.IndexOfAnyExceptInRange((byte)0x20, (byte)0x7E) >= 0)
            return null;
        return System.Text.Encoding.Latin1.GetString(b);
    }

    /// <summary>
    /// The immediates of the Turbo Pascal string-array index <c>mov dx,stride / mul dx / mov di,ax / add di,imm</c>
    /// in START.EXE and GAME.OVR, most common first.
    /// </summary>
    private List<int> StringArrayBases(int stride)
    {
        byte[] pat = [0xBA, (byte)stride, (byte)(stride >> 8), 0xF7, 0xE2, 0x8B, 0xF8, 0x81, 0xC7];
        var counts = new Dictionary<int, int>();
        void Scan(ReadOnlySpan<byte> code)
        {
            for (int i = code.IndexOf(pat); i >= 0 && i + pat.Length + 2 <= code.Length;)
            {
                int v = BinaryPrimitives.ReadUInt16LittleEndian(code[(i + pat.Length)..]);
                counts[v] = counts.GetValueOrDefault(v) + 1;
                int next = code[(i + 1)..].IndexOf(pat);
                i = next < 0 ? -1 : i + 1 + next;
            }
        }
        Scan(Image);
        if (Overlay is not null)
            Scan(Overlay);
        return [.. counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key)];
    }

    private int? _wordsBase;
    private bool _wordsRead;

    /// <summary>
    /// Item name word <paramref name="w"/> (1..255) from the program's word array (string[20], indexed DS:base + 21·w;
    /// docs/re/item-format.md), or null (word 0, or the table not found).
    /// </summary>
    public string? ItemWord(int w) => ItemWordsBase() is { } wb ? PorDsTables.ItemWord(Ds, wb, w) : null;

    /// <summary>DS base of the item name word array (the string[20] array, stride 21, with at least 250 of its 255
    /// words readable), or null.</summary>
    public int? ItemWordsBase()
    {
        if (!_wordsRead)
        {
            _wordsRead = true;
            foreach (var b in StringArrayBases(21))
            {
                int ok = 0;
                for (int k = 1; k <= 255; k++)
                    if (PascalString(b + 21 * k, 20) is not null)
                        ok++;
                if (ok >= 250)
                {
                    _wordsBase = b;
                    break;
                }
            }
        }
        return _wordsBase;
    }

    /// <summary>
    /// The temple's heal list (GAME.OVR 0x4F4E: names DS:base + 41·k, k = 1..10, the 10th leaves): the string[40]
    /// array indexed with stride 41 that holds exactly ten names. Null when not found.
    /// </summary>
    public IReadOnlyList<string>? TempleNames() => TempleNamesBase() is { } b ? PorDsTables.TempleNames(Ds, b) : null;

    /// <summary>DS base of the temple's heal list (the first stride-41 string array holding exactly ten names), or null.</summary>
    public int? TempleNamesBase()
    {
        foreach (var b in StringArrayBases(41))
            if (PorDsTables.TempleNames(Ds, b) is not null)
                return b;
        return null;
    }

    /// <summary>
    /// The camp menu line (GAME.OVR 0x196A0): the DS string the camp routine passes to the menu routine
    /// (<c>mov di,imm / push ds / push di</c> followed by the menu colours 13, 10, 15) after setting DS:49F3 = 2. Null
    /// when not found.
    /// </summary>
    public string? CampMenu() => CampMenuDs() is { } ds ? PascalString(ds) : null;

    /// <summary>DS offset of the camp menu line (see <see cref="CampMenu"/>), or null.</summary>
    public int? CampMenuDs()
    {
        if (Overlay is null)
            return null;
        var o = Overlay.AsSpan();
        ReadOnlySpan<byte> camp = [0xC6, 0x06, 0xF3, 0x49, 0x02];         // mov byte [49F3], 2
        ReadOnlySpan<byte> tail = [0x1E, 0x57, 0xB0, 0x0D, 0x50, 0xB0, 0x0A, 0x50, 0xB0, 0x0F, 0x50];
        for (int i = o.IndexOf(camp); i >= 0;)
        {
            var window = o.Slice(i, Math.Min(0xC0, o.Length - i));
            int t = window.IndexOf(tail);
            if (t >= 3 && window[t - 3] == 0xBF)
                return BinaryPrimitives.ReadUInt16LittleEndian(window[(t - 2)..]);
            int next = o[(i + 1)..].IndexOf(camp);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return null;
    }

    /// <summary>
    /// The text window's page prompt (START.EXE 0x77F8 → 0x7C85, a string in the text unit's code just before the
    /// routine): the printable Pascal string that ends right where the routine (<c>push bp / mov bp,sp / sub sp,0x126</c>)
    /// starts. Null when not found.
    /// </summary>
    public string? PagePrompt()
    {
        ReadOnlySpan<byte> start = [0x55, 0x89, 0xE5, 0x81, 0xEC, 0x26, 0x01, 0xC4, 0x7E, 0x06];
        var img = Image.AsSpan();
        for (int i = img.IndexOf(start); i > 0;)
        {
            for (int n = 8; n <= 40; n++)
            {
                int o = i - 1 - n;
                if (o >= 0 && img[o] == n && img.Slice(o + 1, n).IndexOfAnyExceptInRange((byte)0x20, (byte)0x7E) < 0)
                    return System.Text.Encoding.Latin1.GetString(img.Slice(o + 1, n));
            }
            int next = img[(i + 1)..].IndexOf(start);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return null;
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
