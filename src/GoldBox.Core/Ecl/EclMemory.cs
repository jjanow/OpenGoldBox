using GoldBox.Core.Ecl.Profiles;

namespace GoldBox.Core.Ecl;

/// <summary>A write done through the VM writer (for the trace). Region "char" = a record field set by a side effect.</summary>
public readonly record struct EclMemoryWrite(ushort Address, string Region, ushort Old, ushort New);

/// <summary>Party position cells $C04B–$C04F (save offset 12801, docs/re/save-format.md "Party position").</summary>
public sealed class EclPartyPosition
{
    public byte X { get; set; }
    public byte Y { get; set; }
    /// <summary>8-way facing code (0 N, 2 E, 4 S, 6 W).</summary>
    public byte Facing { get; set; }
    public byte WallAhead { get; set; }
    public byte EventByte { get; set; }
}

/// <summary>
/// The VM address space (docs/re/ecl-format.md "VM memory map"): three 16-bit cell areas, the 8-bit script area and
/// the special addresses. Reads take their width from the region, never from the operand type. $6B00-area reads go
/// through the redirection list (routine 0x7C8F) first; writes always store the cell, then apply the side effects
/// (routine 0x7F47).
/// </summary>
public sealed class EclMemory
{
    public const ushort Area4900 = 0x4900, Area4900End = 0x4CFF;
    public const ushort Area6B00 = 0x6B00, Area6B00End = 0x6EFF;
    public const ushort Area9700 = 0x9700, Area9700End = 0x98FF;

    /// <summary>
    /// Special addresses with a defined read (reader GAME.OVR 0x82DF, ecl-format.md). The position cells and $033D
    /// are served from <see cref="Position"/>; $00B1/$00FB/$00FC read DS words the program never writes (0).
    /// </summary>
    public static readonly ushort[] SpecialReads = [0x00B1, 0x00FB, 0x00FC, 0x033D, 0xC04B, 0xC04C, 0xC04D, 0xC04E, 0xC04F];
    /// <summary>Special addresses whose write the program stores somewhere (writer 0x814A).</summary>
    public static readonly ushort[] SpecialWrites = [0x03DE, 0x00B8, 0x00B9, 0xC04B, 0xC04C, 0xC04D, 0xC059, 0xC05F];
    /// <summary>Special addresses with a write case that does nothing (writer 0x814A): $00FB, $00FC, $00B1, $D022.</summary>
    public static readonly ushort[] SpecialNoOpWrites = [0x00FB, 0x00FC, 0x00B1, 0xD022];

    private readonly ushort[] _cells4900 = new ushort[1024];
    private readonly ushort[] _cells6B00 = new ushort[1024];
    private readonly ushort[] _cells9700 = new ushort[512];
    private readonly byte[] _script = new byte[EclLayout.AreaSize];

    public EclVmPolicy Policy { get; }
    public EclPartyPosition Position { get; } = new();

    /// <summary>The party and the current character (record redirection, PARTYSTR, PSURPRISE, FINDITEM).</summary>
    public EclParty? Party { get; set; }

    /// <summary>Disk number byte DS:5376 (VM $6E12): the digit of ECL/GEO/WALLDEF file names.</summary>
    public byte Disk { get; set; }
    /// <summary>Previous disk number DS:5377 (set on a write to $6E12).</summary>
    public byte PreviousDisk { get; set; }

    /// <summary>DS:84D3, set by a write of 0 to $6B00; with DS:84D2 it arms LOADCHAR's removal (bit 0x80). Cleared by the script init.</summary>
    public bool Ds84D3 { get; set; }
    /// <summary>DS:84D2, set by a write of 0 to $6C00; see <see cref="Ds84D3"/>.</summary>
    public bool Ds84D2 { get; set; }

    /// <summary>
    /// DS:49FA, the view mode: 1 = 3D view (start-up, and set before every vector 4 of a script switch); 2/3/4 after a
    /// switch into block 25/26/27 with $49E6 = 0 (GAME.OVR 0x3F28–0x3F99). CALL $C018 and the step act only at 1.
    /// </summary>
    public byte ViewMode { get; set; } = 1;

    /// <summary>
    /// DS:49F9, the view mode before the last script switch: 0x3EFA copies DS:49FA here before setting it to 1; the new
    /// game sets 1 (0xE9BE). Load does not touch it.
    /// </summary>
    public byte PreviousViewMode { get; set; } = 1;

    /// <summary>
    /// DS:4431: LOADFILES or LOADPIECES ran (handler 0x19BA sets it at 0x19C8) since the last NEWECL (cleared at
    /// 0x19AA) or area entry (0x4053). The script switch 0x3EFA uses it to decide the redraw after vector 4.
    /// </summary>
    public bool FilesLoadedSinceSwitch { get; set; }

    /// <summary>
    /// The view's redraw flags besides the picture's and sprite's (DS:84E0 / 84E3, <see cref="Game.EncounterState"/>):
    /// DS:84E5 (backdrop: the script init 0x76AD and writes to $C059 / $C05F), DS:84E6 (position: writes to
    /// $C04B–$C04D), DS:84E8 (sky: writes to $49FD / $49FE). Only CALL $2C90 clears them (0x3A88–0x3A92), after it
    /// redraws the view and the position line. Not saved.
    /// </summary>
    public ViewRedraw ViewRedrawFlags { get; set; }

    /// <summary>
    /// DS:49E2, the game speed 0 (fastest) … 9: DELAY waits speed × 225 ms (START 0x7E4E) and PRINT text speed × 10 ms
    /// per character (START 0x7782), both through the Crt-style Delay(ms) START 0xA69C. The new game sets 1 (0xE99A);
    /// Save copies it into $49FC (0x1FECE) and Load takes $49FC's low byte back (0x1F8F1); only then does $49FC change.
    /// </summary>
    public byte GameSpeed { get; set; } = 1;

    /// <summary>
    /// Last value written to each special write address (writer 0x814A): $03DE → DS:84CC (sound picked by CALL
    /// $BA03: 8 or 10), $00B8 / $00B9 → DS:84CE / DS:84D0 (never read by the program), $C059 / $C05F → low byte to
    /// DS:6F68 / DS:6F69 (3D-view background colour codes, applied at the next redraw; the script init sets them to
    /// 0x41 / 9, <see cref="EclVm.InitScript"/>).
    /// </summary>
    public Dictionary<ushort, ushort> SpecialWriteValues { get; } = [];

    /// <summary>
    /// When set, the value an undefined read returns instead of <see cref="EclVmPolicy.UndefinedValue"/>: the VM sets
    /// it to the stack residue it models (the previous COMPARE operand read) while COMPARE fetches its operands.
    /// </summary>
    public ushort? UndefinedReadResidue { get; set; }

    /// <summary>Raised for every write through <see cref="Write"/> and for record fields changed by side effects.</summary>
    public event Action<EclMemoryWrite>? Written;
    /// <summary>Raised for undefined or unconfirmed accesses (never thrown).</summary>
    public event Action<string>? Diagnostic;
    /// <summary>Raised on writes to $49FD/$49FE (sky/ceiling colour: "extra handling in the writer" `[?]`).</summary>
    public event Action<ushort, ushort>? SkyCeilingWritten;
    /// <summary>A write of v &gt; 0x80 to $6E22/$6E24/$6E26 loads wall set (v AND 0x7F) into slot 1/2/3 (0x2F6FF).</summary>
    public event Action<int, int>? WallSetRequested;

    /// <summary>
    /// The VM profile (docs/module-format.md §5.1). This class is the memory map, reader, writer and character view of
    /// the <see cref="EclHandlerSet.Por13"/> handler set: it takes only profiles of that set.
    /// </summary>
    public EclProfile Profile { get; }

    public EclMemory(EclVmPolicy? policy = null, EclProfile? profile = null)
    {
        Policy = policy ?? EclVmPolicy.Default;
        Profile = profile ?? EclProfiles.Default;
        if (Profile.Handlers != EclHandlerSet.Por13)
            throw new EclProfileNotRunnableException(Profile);
    }

    public Span<byte> ScriptArea => _script;
    public Span<ushort> Cells4900 => _cells4900;
    public Span<ushort> Cells6B00 => _cells6B00;
    public Span<ushort> Cells9700 => _cells9700;

    /// <summary>Region name of an address: "4900", "6B00", "9700", "script" or "special".</summary>
    public static string RegionOf(ushort a) => a switch
    {
        >= Area4900 and <= Area4900End => "4900",
        >= Area6B00 and <= Area6B00End => "6B00",
        >= Area9700 and <= Area9700End => "9700",
        >= EclLayout.Base and <= EclLayout.End => "script",
        _ => "special",
    };

    /// <summary>
    /// Script byte at VM address <paramref name="address"/> as the interpreter fetches code: offset A − $9900
    /// (buffer + (A + 0x6700) mod 0x10000). Null when the offset is outside the 7680-byte area.
    /// </summary>
    public byte? FetchCode(int address)
    {
        int off = (address + 0x6700) & 0xFFFF;
        return off < _script.Length ? _script[off] : null;
    }

    /// <summary>The stored cell (or script byte) without redirection; null for special addresses.</summary>
    public ushort? Cell(ushort a) => a switch
    {
        >= Area4900 and <= Area4900End => _cells4900[a - Area4900],
        >= Area6B00 and <= Area6B00End => _cells6B00[a - Area6B00],
        >= Area9700 and <= Area9700End => _cells9700[a - Area9700],
        >= EclLayout.Base and <= EclLayout.End => _script[a - EclLayout.Base],
        _ => null,
    };

    /// <summary>VM read (GAME.OVR 0x82DF).</summary>
    public ushort Read(ushort a)
    {
        if (a is >= Area6B00 and <= Area6B00End && TryRedirectRead(a, out var r))
            return r;
        if (Cell(a) is { } c)
            return c;
        switch (a)
        {
            // P (reader 0x82DF): x and y are both sign-extended bytes (DS:6AAD / DS:6AAE).
            case 0xC04B: return (ushort)(sbyte)Position.X;
            case 0xC04C: return (ushort)(sbyte)Position.Y;
            case 0xC04D: return (ushort)(Position.Facing / 2);
            case 0xC04E: return Position.WallAhead;
            case 0xC04F: return Position.EventByte;
            // P: the 8-way facing byte DS:6AAF (0, 2, 4, 6), unsigned.
            case 0x033D: return Position.Facing;
            // P: DS:6F6E / DS:6F6A / DS:6F6C, which no program code writes (DOSBox: 0, also after a
            // script wrote these addresses). Their writes are ignored.
            case 0x00B1 or 0x00FB or 0x00FC: return 0;
        }
        return UndefinedRead(a == 0x035F || a == 0xC059
            ? $"read of ${a:X4}: the reader has a case for it but sets no value (stack residue)"
            : $"read of unlisted special address ${a:X4}: undefined (stack residue)");
    }

    /// <summary>A read the program leaves undefined: the modelled residue if the VM set one, else the policy value.</summary>
    private ushort UndefinedRead(string what)
    {
        var v = UndefinedReadResidue ?? Policy.UndefinedValue;
        Diag($"[?] {what}, using {v}{(UndefinedReadResidue is null ? "" : " (previous COMPARE read)")}");
        return v;
    }

    /// <summary>VM write (GAME.OVR 0x814A). Writes to unlisted special addresses are ignored (P) and logged.</summary>
    public void Write(ushort a, ushort value)
    {
        if (StoreCell(a, value))
        {
            if (a is 0x49FD or 0x49FE)
            {
                ViewRedrawFlags |= ViewRedraw.Sky; // 0x8182: DS:84E8 := 1
                SkyCeilingWritten?.Invoke(a, value);
            }
            if (a is >= Area6B00 and <= Area6B00End)
                WriteSideEffects(a, value);
            return;
        }
        ushort old;
        // The writer's redraw flags (0x814A): $C04B–$C04D set DS:84E6 (0x823F, 0x8258, 0x82A9), $C059 / $C05F DS:84E5
        // (0x82BB, 0x82CD); CALL $2C90 clears them after its redraw.
        if (a is 0xC04B or 0xC04C or 0xC04D)
            ViewRedrawFlags |= ViewRedraw.Position;
        else if (a is 0xC059 or 0xC05F)
            ViewRedrawFlags |= ViewRedraw.Backdrop;
        switch (a)
        {
            case 0xC04B:
                old = Position.X;
                Position.X = (byte)value;
                Written?.Invoke(new EclMemoryWrite(a, "special", old, Position.X));
                return;
            case 0xC04C:
                old = Position.Y;
                Position.Y = (byte)value;
                Written?.Invoke(new EclMemoryWrite(a, "special", old, Position.Y));
                return;
            case 0xC04D:
                // P: value mod 4 (repeated subtraction of 4, 0x8264) → facing 0/2/4/6.
                old = (ushort)(Position.Facing / 2);
                Position.Facing = (byte)(value % 4 * 2);
                Written?.Invoke(new EclMemoryWrite(a, "special", old, (ushort)(value % 4)));
                return;
            case 0x03DE or 0x00B8 or 0x00B9 or 0xC059 or 0xC05F:
                // P (0x814A): words to DS:84CC / 84CE / 84D0; $C059 / $C05F store the low byte (DS:6F68 / 6F69).
                if (a is 0xC059 or 0xC05F)
                    value &= 0xFF;
                SpecialWriteValues.TryGetValue(a, out old);
                SpecialWriteValues[a] = value;
                Written?.Invoke(new EclMemoryWrite(a, "special", old, value));
                return;
            case 0x00FB or 0x00FC or 0x00B1 or 0xD022:
                return; // P: the writer has a case for these that stores nothing (0x814A)
        }
        Diag($"write ${a:X4} = {value} to an unlisted special address ignored");
    }

    /// <summary>Stores a cell (word areas: 16 bits; script area: low byte). False for special addresses.</summary>
    private bool StoreCell(ushort a, ushort value)
    {
        ushort old;
        switch (a)
        {
            case >= Area4900 and <= Area4900End:
                old = _cells4900[a - Area4900];
                _cells4900[a - Area4900] = value;
                Written?.Invoke(new EclMemoryWrite(a, "4900", old, value));
                return true;
            case >= Area6B00 and <= Area6B00End:
                old = _cells6B00[a - Area6B00];
                _cells6B00[a - Area6B00] = value;
                Written?.Invoke(new EclMemoryWrite(a, "6B00", old, value));
                return true;
            case >= Area9700 and <= Area9700End:
                old = _cells9700[a - Area9700];
                _cells9700[a - Area9700] = value;
                Written?.Invoke(new EclMemoryWrite(a, "9700", old, value));
                return true;
            case >= EclLayout.Base and <= EclLayout.End:
                // P: the 16-bit result goes through the writer and a script-area destination keeps the low byte.
                old = _script[a - EclLayout.Base];
                _script[a - EclLayout.Base] = (byte)value;
                Written?.Invoke(new EclMemoryWrite(a, "script", old, (byte)value));
                return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // $6B00-area redirection (ecl-format.md "$6B00-area redirection", routines 0x7C8F / 0x7F47)
    // -----------------------------------------------------------------------------------------------------------------

    private EclCharacter? CurrentCharacter(ushort a)
    {
        var c = Party?.Current;
        if (c is null)
            Diag($"[?] ${a:X4} maps to the current character's record, but no character is current: cell used");
        return c;
    }

    private bool TryRedirectRead(ushort a, out ushort v)
    {
        v = 0;
        switch (a)
        {
            case 0x6E12:
                v = Disk;
                return true;
            case 0x6DB1 or 0x6DB4:
                if (Party is { Current: not null } p)
                {
                    v = (ushort)p.CurrentIndex;
                    return true;
                }
                CurrentCharacter(a);
                return false;
            case 0x6C0D:
                Diag($"[?] read of $6C0D: undefined in the program (case present, value never set), using {Policy.UndefinedValue}");
                v = Policy.UndefinedValue;
                return true;
        }
        int field = a switch
        {
            0x6B18 => 0x14,
            0x6B9B => 0x6E,
            0x6BA0 => 0x73,
            >= 0x6BA5 and <= 0x6BAC => 0x77 + (a - 0x6BA5),
            0x6BB8 => 0x84,
            0x6BBB or 0x6BBD or 0x6BBF or 0x6BC1 or 0x6BC3 => 0x88 + (a - 0x6BBB),
            0x6BD8 => 0xA0,
            0x6BF7 => 0xB8,
            0x6BF9 => 0xBA,
            0x6C00 or 0x6C0C or 0x6DCF => 0, // computed below
            0x6C1B => 0x11C,
            _ => -1,
        };
        if (field < 0)
            return false;
        var c = CurrentCharacter(a);
        if (c is null)
            return false;
        switch (a)
        {
            case 0x6BA0 or 0x6BD8:
                v = (ushort)(sbyte)c[field];
                return true;
            case 0x6BBB or 0x6BBD or 0x6BBF or 0x6BC1 or 0x6BC3 or 0x6BF7:
                v = c.Word(field);
                return true;
            case 0x6C00:
                v = (ushort)(c[0x10D] != 0 ? 1 : 0x80);
                return true;
            case 0x6C0C:
                v = (ushort)(c[0x10E] == 0 && c[0x10F] != 0 ? 0x80 : c[0x10E] == 1 ? 0x81 : 0);
                return true;
            case 0x6DCF:
                int? t = c[0x15] switch
                {
                    3 => 0, 4 => 5, 5 => 10, 6 => 15, 7 => 20, >= 8 and <= 12 => 25, 13 => 30, 14 => 35, 15 => 40,
                    16 => 50, 17 => 55, >= 18 and <= 25 => 60, _ => null,
                };
                if (t is null)
                    Diag($"[?] $6DCF for record 0x15 = {c[0x15]}: undefined, using {Policy.UndefinedValue}");
                v = (ushort)(t ?? Policy.UndefinedValue);
                return true;
            default:
                v = c[field];
                return true;
        }
    }

    private void WriteSideEffects(ushort a, ushort value)
    {
        switch (a)
        {
            case 0x6B00:
                if (value == 0)
                    Ds84D3 = true; // arms LOADCHAR bit 0x80 (removal)
                return;
            case 0x6E12:
                PreviousDisk = Disk;
                Disk = (byte)value;
                return;
            case 0x6E22 or 0x6E24 or 0x6E26:
                if (value > 0x80)
                    WallSetRequested?.Invoke(1 + (a - 0x6E22) / 2, value & 0x7F);
                return;
        }
        bool hasEffect = a is >= 0x6B20 and <= 0x6B70 or 0x6BB8 or 0x6BBB or 0x6BBD or 0x6BBF or 0x6BC1 or 0x6BC3
            or 0x6BF7 or 0x6BF9 or 0x6C00 or 0x6C0C;
        if (!hasEffect)
            return;
        if (a == 0x6C00 && value == 0)
            Ds84D2 = true; // arms LOADCHAR bit 0x80 (removal)
        var c = CurrentCharacter(a);
        if (c is null)
            return;
        switch (a)
        {
            case >= 0x6B20 and <= 0x6B70:
                SetRecordByte(c, a, 0x18 + (a - 0x6B20), (byte)value);
                return;
            case 0x6BB8:
                SetRecordByte(c, a, 0x84, (byte)(value > 0xB2 ? value - 0x32 : value));
                return;
            case 0x6BBB or 0x6BBD or 0x6BBF or 0x6BC1 or 0x6BC3 or 0x6BF7:
            {
                int f = a == 0x6BF7 ? 0xB8 : 0x88 + (a - 0x6BBB);
                var old = c.Word(f);
                c.SetWord(f, value);
                Written?.Invoke(new EclMemoryWrite(a, "char", old, value));
                return;
            }
            case 0x6BF9:
                SetRecordByte(c, a, 0xBA, (byte)value);
                return;
            case 0x6C00:
                if (value >= 0x80)
                {
                    SetRecordByte(c, a, 0x10D, 0);
                    if (value == 0x87)
                        SetRecordByte(c, a, 0x10C, 7);
                }
                return;
            case 0x6C0C:
                switch (value)
                {
                    case 0: SetRecordByte(c, a, 0x10E, 0); SetRecordByte(c, a, 0x10F, 0); break;
                    case 0x80: SetRecordByte(c, a, 0x10E, 0); SetRecordByte(c, a, 0x10F, 1); break;
                    case 0x81: SetRecordByte(c, a, 0x10E, 1); SetRecordByte(c, a, 0x10F, 1); break;
                }
                return;
        }
    }

    private void SetRecordByte(EclCharacter c, ushort a, int field, byte value)
    {
        var old = c[field];
        c[field] = value;
        Written?.Invoke(new EclMemoryWrite(a, "char", old, value));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Text
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Text of a type-0x81 operand (routine 0x87CE): one character per cell (low byte of a word cell, or a script
    /// byte) until a cell that is 0; $6B00 gives the current character's name instead.
    /// </summary>
    public string ReadText(ushort a)
    {
        if (a == Area6B00 && Party?.Current is { } ch)
            return ch.Name;
        if (a == Area6B00)
            CurrentCharacter(a);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Policy.MaxMemoryTextLength; i++)
        {
            var at = (ushort)(a + i);
            int c;
            if (Cell(at) is { } cell)
            {
                c = cell & 0xFF;
            }
            else
            {
                Diag($"[?] text read from special address ${at:X4}");
                c = Read(at) & 0xFF;
            }
            if (c == 0)
                return sb.ToString();
            sb.Append((char)c);
        }
        Diag($"[?] text at ${a:X4} has no terminator within {Policy.MaxMemoryTextLength} cells");
        return sb.ToString();
    }

    /// <summary>
    /// SAVE of a text (routine 0x8426; INPUTSTR uses the same rules): one character per cell, then a 0 cell. $6B00
    /// sets the current character's name (at most 15 characters) instead. Writes go straight to the cells, so the
    /// $6B00-area side effects do not run; special addresses: nothing.
    /// </summary>
    public void WriteText(ushort a, string text)
    {
        if (a == Area6B00)
        {
            if (CurrentCharacter(a) is { } ch)
            {
                ch.Name = text;
                Written?.Invoke(new EclMemoryWrite(a, "char", 0, (ushort)Math.Min(text.Length, EclCharacter.MaxNameLength)));
            }
            return;
        }
        if (Cell(a) is null)
        {
            Diag($"text store to special address ${a:X4}: nothing written");
            return;
        }
        if (text.Length == 0)
        {
            // [?] The program fills a+1..a+255 with leftover stack bytes (byte counter underflow) and puts the 0 at
            // a. The leftover bytes are undefined, so only the 0 is stored. Never done in v1.3.
            Diag($"[?] empty text stored at ${a:X4}: a+1..a+255 would get leftover bytes; only the 0 is stored");
            StoreCell(a, 0);
            return;
        }
        for (int i = 0; i < text.Length; i++)
            StoreCell((ushort)(a + i), (byte)text[i]);
        StoreCell((ushort)(a + text.Length), 0);
    }

    /// <summary>Clears the script area and copies a DAX block's script (block bytes from offset 2) into it.</summary>
    public void LoadScriptBlock(ReadOnlySpan<byte> block)
    {
        Array.Clear(_script);
        int prefix = Profile.ScriptPrefix;
        var body = block.Length > prefix ? block[prefix..] : [];
        if (body.Length > _script.Length)
        {
            Diag($"script block of {block.Length} bytes truncated to the {Profile.ScriptAreaSize}-byte area");
            body = body[.._script.Length];
        }
        body.CopyTo(_script);
    }

    private void Diag(string message) => Diagnostic?.Invoke(message);
}

/// <summary>The view redraw flags of <see cref="EclMemory.ViewRedrawFlags"/>.</summary>
[Flags]
public enum ViewRedraw : byte
{
    None = 0,
    /// <summary>DS:84E5.</summary>
    Backdrop = 1,
    /// <summary>DS:84E6.</summary>
    Position = 2,
    /// <summary>DS:84E8.</summary>
    Sky = 4,
}
