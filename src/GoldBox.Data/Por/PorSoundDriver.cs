using System.Buffers.Binary;
using GoldBox.Core.Sound;

namespace GoldBox.Data.Por;

/// <summary>
/// Finds PoR's PC-speaker / Tandy sound driver in the player's START.EXE load image (docs/re/sound.md §2.1) and reads
/// its data: located by the driver's code (the timer install, the two tune programmers, the two tick routines), not by
/// fixed offsets. The same signatures as tools/scripts/por_sound_oracle.py. Nothing from the program is in our code.
/// </summary>
public static class PorSoundDriver
{
    /// <summary>The driver's data and offsets, or null when the image holds no such driver.</summary>
    public static PcSoundDriverImage? Read(byte[] image)
    {
        var img = image.AsSpan();
        // Timer install: mov al,0B6h / out 43h,al / mov ax,divisor / out 40h,al / mov al,ah / out 40h,al
        int sig = Find(img, 0, [0xB0, 0xB6, 0xE6, 0x43, 0xB8, -1, -1, 0xE6, 0x40, 0x8A, 0xC4, 0xE6, 0x40]);
        if (sig < 0 || Find(img, sig + 1, [0xB0, 0xB6, 0xE6, 0x43, 0xB8, -1, -1, 0xE6, 0x40]) >= 0)
            return null;
        int divisor = U16(img, sig + 5);
        // The install begins push ds / push es / mov ax,CODE: the driver's segment.
        int install = LastBefore(img, sig, [0x1E, 0x06, 0xB8]);
        if (install < 0)
            return null;
        int seg = U16(img, install + 3);
        int baseLin = seg * 16;
        if (install < baseLin || baseLin + 0x10000 > img.Length + 0x10000)
            return null;
        var cs = img[baseLin..Math.Min(img.Length, baseLin + 0x10000)];
        int installOff = install - baseLin;

        // The interrupt handler: mov dx,isr / mov ax,2508h / int 21h inside the install.
        int setVec = Find(cs, installOff, [0xBA, -1, -1, 0xB8, 0x08, 0x25, 0xCD, 0x21]);
        if (setVec < 0 || setVec > sig - baseLin)
            return null;
        int isr = U16(cs, setVec + 1);
        // In it: dec byte [count] / jnz / mov byte [count],n (the BIOS chain).
        int dec = Find(cs, isr, [0xFE, 0x0E, -1, -1, 0x75, -1, 0xC6, 0x06, -1, -1, -1]);
        if (dec < 0 || dec > isr + 0x80 || U16(cs, dec + 2) != U16(cs, dec + 8))
            return null;
        int origin = U16(cs, dec + 2);
        int chain = cs[dec + 10];

        // Tick routines: mov si,notes / mov [freqBase],si / mov si,voices / mov [voiceBase],si; the one before the
        // first out 42h is the speaker's. Voice size: add si,imm8 in the voice loop.
        int out42 = Find(cs, isr, [0xE6, 0x42]);
        var ticks = new List<(int At, int Notes, int Voices)>();
        for (int p = Find(cs, isr, [0xBE, -1, -1, 0x89, 0x36, -1, -1, 0xBE, -1, -1, 0x89, 0x36]); p >= 0 && ticks.Count < 2;
             p = Find(cs, p + 1, [0xBE, -1, -1, 0x89, 0x36, -1, -1, 0xBE, -1, -1, 0x89, 0x36]))
            ticks.Add((p, U16(cs, p + 1), U16(cs, p + 8)));
        if (ticks.Count != 2 || out42 < 0)
            return null;
        var spk = ticks[0].At < out42 && ticks[1].At > out42 ? ticks[0] : ticks[1].At < out42 && ticks[0].At > out42 ? ticks[1] : default;
        if (spk == default)
            return null;
        var tdy = spk == ticks[0] ? ticks[1] : ticks[0];
        int add = Find(cs, spk.At, [0x83, 0xC6, -1]);
        if (add < 0)
            return null;
        int voiceSize = cs[add + 2];

        // Tune programmers: push bp … mov si,CODE / mov ds,si; then mov di,voices / mov ax,[si+table].
        byte lo = (byte)seg, hi = (byte)(seg >> 8);
        int? spkTable = null, tdyTable = null;
        for (int p = Find(cs, 0, [0x55, 0x8B, 0xEC, 0x50, 0x53, 0x57, 0x56, 0x1E, 0xBE, lo, hi, 0x8E, 0xDE]); p >= 0;
             p = Find(cs, p + 1, [0x55, 0x8B, 0xEC, 0x50, 0x53, 0x57, 0x56, 0x1E, 0xBE, lo, hi, 0x8E, 0xDE]))
        {
            int m = Find(cs, p, [0xBF, -1, -1, 0x8B, 0x84, -1, -1]);
            if (m < 0 || m > p + 64)
                continue;
            int voices = U16(cs, m + 1), table = U16(cs, m + 5);
            if (voices == spk.Voices)
                spkTable = table;
            else if (voices == tdy.Voices)
                tdyTable = table;
        }
        if (spkTable is not { } a || tdyTable is not { } b || b <= a || (b - a) % 8 != 0)
            return null;
        int tunes = (b - a) / 8;
        if (installOff <= origin)
            return null;
        var d = new PcSoundDriverImage(origin, cs[origin..installOff].ToArray(), a, b, spk.Notes, tdy.Notes,
            spk.Voices, tdy.Voices, voiceSize, tunes, divisor, chain);
        try
        {
            d.Validate();
        }
        catch (FormatException)
        {
            return null;
        }
        return d;
    }

    /// <summary>The sound cue table (docs/re/sound.md §2.4): the 16 command words at DS:2612 in v1.3, found by its
    /// shape (255, 0, 1, 2, then ascending tune commands), or null.</summary>
    public static int? CueTable(ReadOnlySpan<byte> ds)
    {
        for (int p = 0; p + 32 <= ds.Length; p += 2)
        {
            if (U16(ds, p) != 0xFF || U16(ds, p + 2) != 0 || U16(ds, p + 4) != 1 || U16(ds, p + 6) != 2)
                continue;
            bool ok = true;
            for (int k = 4; k < 16 && ok; k++)
                ok = U16(ds, p + 2 * k) == U16(ds, p + 2 * k - 2) + 1;
            if (ok)
                return p;
        }
        return null;
    }

    private static int U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16LittleEndian(s[at..]);

    private static int Find(ReadOnlySpan<byte> s, int from, ReadOnlySpan<int> pat)
    {
        for (int i = Math.Max(0, from); i + pat.Length <= s.Length; i++)
        {
            int k = 0;
            while (k < pat.Length && (pat[k] < 0 || s[i + k] == pat[k]))
                k++;
            if (k == pat.Length)
                return i;
        }
        return -1;
    }

    private static int LastBefore(ReadOnlySpan<byte> s, int before, ReadOnlySpan<int> pat)
    {
        for (int i = before - pat.Length; i >= 0 && i > before - 256; i--)
        {
            int k = 0;
            while (k < pat.Length && (pat[k] < 0 || s[i + k] == pat[k]))
                k++;
            if (k == pat.Length)
                return i;
        }
        return -1;
    }
}
