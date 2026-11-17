using GoldBox.Core.Ecl;

namespace GoldBox.Core.Explore;

/// <summary>
/// The game clock in VM cells $49C6–$49CC (save-format.md): minute segment (unused), minutes ones digit, minutes tens
/// digit, hour, day, month, years elapsed. The clock routine GAME.OVR 0x241A1 (count, unit) adds 1 to cell
/// <c>unit</c> <c>count</c> times, carrying after each add with the limits table DS:363A of START.EXE.
/// Callers (program read, s11): a step costs 1 minute (unit 1), or 10 minutes (unit 2) with Search on ($6DCA bit 0,
/// step routine 0x16E06); Look costs 10 minutes (0x1715D); a combat round 1 minute (0xA087); a rest tick 5 minutes
/// (0x24B80); CLOCK adds n minutes (0x3828).
/// </summary>
public static class GameClock
{
    public const ushort FirstCell = 0x49C6;
    public const int Cells = 7;

    /// <summary>Units (cell index): 1 = minutes, 2 = ten minutes, 3 = hours, 4 = days, 5 = months, 6 = years.</summary>
    public const int Minutes = 1, TenMinutes = 2, Hours = 3, Days = 4, Months = 5, Years = 6;

    /// <summary>Carry limits per cell, DS:363A in START.EXE v1.3 (read 2026-11-17, s11): 10, 10, 6, 24, 30, 12, 256.</summary>
    public static readonly ushort[] Limits = [10, 10, 6, 24, 30, 12, 256];

    /// <summary>Record word the year carry increments for every member of the party list (character-format.md 0x30, age).</summary>
    public const int AgeOffset = 0x30;

    /// <summary>
    /// 0x241A1: add <paramref name="count"/> (a byte) to cell <paramref name="unit"/> one at a time, carrying after each
    /// add (0x240BE: one pass over cells 0..6; a cell at or over its limit loses the limit and the next cell gains 1; a
    /// year overflow does not wrap, it ages every creature of the party list DS:5D96 by one, record word 0x30).
    /// [?] The routine then calls 0x23DCC(count, unit), not read (probably spell effect durations); not modelled.
    /// </summary>
    public static void Advance(EclMemory memory, int unit, int count)
    {
        var t = new ushort[Cells];
        for (int i = 0; i < Cells; i++)
            t[i] = memory.Read((ushort)(FirstCell + i));
        count &= 0xFF;
        for (int k = 1; k <= count; k++)
        {
            t[unit & 0xFF]++;
            Normalise(t, memory.Party);
        }
        for (int i = 0; i < Cells; i++)
            memory.Write((ushort)(FirstCell + i), t[i]);
    }

    private static void Normalise(ushort[] t, EclParty? party)
    {
        for (int i = 0; i < Cells; i++)
        {
            if (t[i] < Limits[i])
                continue;
            if (i != Cells - 1)
            {
                t[i + 1]++;
                t[i] -= Limits[i];
            }
            else if (party is not null)
            {
                foreach (var c in party.Members)
                    c.SetWord(AgeOffset, (ushort)(c.Word(AgeOffset) + 1));
            }
        }
    }

    /// <summary>Hours and minutes as the position line shows them (START.EXE 0x34AB): "HH:MM", both padded to two digits.</summary>
    public static string Format(EclMemory memory)
    {
        int h = memory.Read(FirstCell + Hours), tens = memory.Read(FirstCell + TenMinutes), ones = memory.Read(FirstCell + Minutes);
        return $"{h:00}:{tens * 10 + ones:00}";
    }

    /// <summary>Total minutes since day 0 of year 0 (for tests and displays; 30-day months, 12-month years).</summary>
    public static long TotalMinutes(EclMemory memory)
    {
        long m = memory.Read(FirstCell + Minutes) + 10L * memory.Read(FirstCell + TenMinutes);
        m += 60L * memory.Read(FirstCell + Hours);
        m += 60L * 24 * memory.Read(FirstCell + Days);
        m += 60L * 24 * 30 * memory.Read(FirstCell + Months);
        m += 60L * 24 * 30 * 12 * memory.Read(FirstCell + Years);
        return m;
    }
}

/// <summary>
/// The 3D view's four backdrop colours (redraw GAME.OVR 0x2DCF4, program read s11). The sky is the sky list entry
/// (DS:0C26) of $49FD, or of $49FE when the party's square has an event byte ≥ 0x80. The other three come from the
/// colour bytes behind $C05F (DS:6F69: both horizon bands) and $C059 (DS:6F68: the ground) through three small maps,
/// recomputed while DS:84E5 is set. The script init (0x76A3) and every write to $C059/$C05F set the flag, CALL $2C90
/// clears it after its redraw (0x3A8D), and nothing else writes the backdrop (only 0x2DD63/6F/7B store it), so the
/// colours always follow the current bytes. Script init: 0x41 / 9 → 15, 7, 6 (the Slums).
/// </summary>
public static class BackdropRule
{
    /// <summary>Horizon 1 (row 43) from DS:6F69 (0x2DC4E): 8 → 0, 0x41 → 0, else 15.</summary>
    public static byte Horizon1(byte c05F) => c05F is 8 or 0x41 ? (byte)0 : (byte)15;

    /// <summary>Horizon 2 (rows 44–45) from DS:6F69 (0x2DC82): 8 → 8, else 7.</summary>
    public static byte Horizon2(byte c05F) => c05F == 8 ? (byte)8 : (byte)7;

    /// <summary>Ground (rows 46–87) from DS:6F68 (0x2DCB6): 0x0B → 8, 6 → 1, else (9, 0xDB, …) 6.</summary>
    public static byte Ground(byte c059) => c059 switch { 0x0B => 8, 6 => 1, _ => 6 };

    /// <summary>The bytes the script init sets (0x76A3): DS:6F68 = 0x41, DS:6F69 = 9.</summary>
    public const byte InitC059 = 0x41, InitC05F = 0x09;

    /// <summary>The backdrop for the VM state: sky list, cells $49FD/$49FE, the event byte of the party's square, $C059/$C05F.</summary>
    public static Backdrop For(EclMemory memory, IReadOnlyList<byte> skyColours, int eventByte)
    {
        int index = eventByte >= 0x80 ? memory.Read(0x49FE) : memory.Read(0x49FD);
        byte c059 = (byte)(memory.SpecialWriteValues.TryGetValue(0xC059, out var a) ? a : InitC059);
        byte c05F = (byte)(memory.SpecialWriteValues.TryGetValue(0xC05F, out var b) ? b : InitC05F);
        return new Backdrop(skyColours[index & 15], Horizon1(c05F), Horizon2(c05F), Ground(c059));
    }
}
