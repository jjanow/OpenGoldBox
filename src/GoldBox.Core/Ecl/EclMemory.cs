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
/// the special addresses. Reads take their width from the region, never from the operand type. Character-view reads
/// ($6B00 area in PoR, $7C00 in SotSB) go through the redirection list (PoR routine 0x7C8F, SotSB OVR 0x9830) first;
/// writes always store the cell, then apply the side effects (PoR 0x7F47, SotSB OVR 0x9D20). The areas and the
/// dialect's cases come from the <see cref="Profile"/>: the members named after PoR's areas (<see cref="Cells4900"/>
/// …) hold the same-role area of another dialect (SotSB: $4B00, $7C00, $7A00).
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

    private readonly ushort[] _cells4900;
    private readonly ushort[] _cells6B00;
    private readonly ushort[] _cells9700;
    private readonly byte[] _script;
    /// <summary>The areas by role (PoR / SotSB): globals $4900 / $4B00, character view $6B00 / $7C00, $9700 / $7A00, script.</summary>
    private readonly EclMemoryRegion _globals, _view, _third, _scriptRegion;
    private readonly bool _sotsb;

    public EclVmPolicy Policy { get; }
    public EclPartyPosition Position { get; } = new();

    /// <summary>The party and the current character (record redirection, PARTYSTR, PSURPRISE, FINDITEM).</summary>
    public EclParty? Party { get; set; }

    /// <summary>The spell routines' program globals that outlive a fight or a camp (DS:4844 / DS:4845); run time only,
    /// not saved (they are not in the save files).</summary>
    public Game.SpellGlobals SpellGlobals { get; } = new();

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
    /// DS:49F3, the interface state START 0x3379 draws the screen by (0 hall, 1 shop / temple, 2 camp, 3 overland, 4 3D,
    /// 5 fight, 6 loot, 7 end game). The exploring writers (P s41, every byte write in START.EXE and GAME.OVR): the
    /// game loop's first pass 4, or 3 for blocks 25–27 with $49E6 = 0 (0x405D / 0x408E); the script switch 0x3EFA the
    /// same (0x3F3B–0x3F99); COMBAT's end 4 with DS:49FA = 1, else 3 (0x25FE / 0x2605: every fight, shop, temple and
    /// after-combat step); the overland interface START 0x36FE 3 (0x3701); the camp 2 and back to the old value
    /// (0x196AC / 0x197F2); the training hall 0 (0x19AF9) and 4 at Begin (0x19DCF), while its Load copies the save's
    /// value (0x1F87B) and leaves the menu at once for a state ≠ 0 (so a camp save keeps 2: the camp screen and
    /// "CAMPING" until one of these writers, training.md). Not modelled: 1, 5, 6, 7 (the venues and the fight set them
    /// and COMBAT's end writes 3 / 4 after them). Saved at offset 12807 (PorSaveGame.InterfaceState).
    /// </summary>
    public byte InterfaceState { get; set; } = 4;

    /// <summary>
    /// VM $4AFF is not 0 as the menu routine 0x2CC8C reads it (<see cref="Explore.AreaLoop.MenuAnimateCell"/>): the next
    /// menu animates the picture's frames, and every menu clears the cell at its end (0x2CFAD). Kept apart from the cell
    /// itself, which the Client's save writes only for the wall-set slots it saw loaded (the program's save rewrites all
    /// six from its cache, so the menus' zero never reaches a save file). Set by a Load from the save's cell and by a save
    /// (it writes the cells first, 0x1FE70). Not saved.
    /// </summary>
    public bool MenuAnimates { get; set; }

    /// <summary>
    /// DS:6DDC, the overland marker's animation counter (0–5, <see cref="Explore.Overland.MarkerFrame"/>). Its only
    /// writers are the start-up (0 at 0xE9D3) and the marker routine 0x2DB87 (+1 mod 6 at 0x2DC42): not saved, and a
    /// load does not touch it (<see cref="Explore.Overland.Redrawn"/>).
    /// </summary>
    public int MarkerCounter { get; set; }

    /// <summary>
    /// The facing the overland marker was last drawn with (the marker routine 0x2DB87 reads DS:6AAF at 0x2DC14 when it
    /// draws). The screen keeps that drawing until the next redraw: the overland arrows set DS:6AAF in the command
    /// routine (0x17397…), and a river's row-24 message (0x16FEE, inside the step) comes before the step's redraw
    /// (0x41D8), so the old facing's marker stays up while it is shown (DOSBox-X s39). Null: none drawn yet (the live
    /// facing is drawn). Not saved; display bookkeeping.
    /// </summary>
    public byte? MarkerFacing { get; set; }

    /// <summary>
    /// The overland interface (START 0x36FE, at the first command read in modes 2–4 with DS:49F9 &lt; 2) drew the view
    /// with marker frame c and then frame c + 1 alone over it (0x3710); the screen keeps that composite until the next
    /// redraw (DOSBox-X s39: unchanged through a river's message after the load). Cleared by every overland redraw
    /// (<see cref="Explore.Overland.Redrawn"/>). Not saved; display bookkeeping.
    /// </summary>
    public bool MarkerDrawnTwice { get; set; }

    /// <summary>
    /// DS:4431: LOADFILES or LOADPIECES ran (handler 0x19BA sets it at 0x19C8) since the last NEWECL (cleared at
    /// 0x19AA) or area entry (0x4053). The script switch 0x3EFA uses it to decide the redraw after vector 4.
    /// </summary>
    public bool FilesLoadedSinceSwitch { get; set; }

    /// <summary>
    /// DS:84E0, a close-up is up (docs/re/por-art-formats.md §3 "Playback"): set by PICTURE (0x15F8, a PIC or a head +
    /// body) and by the encounter close-up (0x7B09); cleared by EXIT (0xDD7), the script init (0x768F), PROTECT
    /// (0x3BDA), COMBAT (0x2630), SPRITEOFF (0x3800), PICTURE 255 and CALL $2C90 when they redraw (0x167A, 0x3A97), the
    /// step's pass before vector 1 (0x421E) and the start-up (0xE9F7). With <see cref="PictureArmed"/> it makes HMENU
    /// (0x1E9E) and ENCMENU (0x2D8D) animate the picture; PICTURE 255 and CALL $2C90 redraw the view while it (or the
    /// sprite's DS:84E3) is set. Load (0x1F5E8) does not touch it. Not saved.
    /// </summary>
    public bool PictureShown { get; set; }

    /// <summary>
    /// DS:84E1, the picture animation's arm (por-art-formats.md §3): set by the step's pass before vector 1 (0x4223) and
    /// by PICTURE 255 when it redraws (0x1684); cleared by the head + body loader 0x79E4 (0x79E7, PICTURE with $6DE1 ≠
    /// 255 and the encounter close-up with a head, which clears it again at 0x7B77). Nothing else writes it: not the
    /// start-up, not Load (0x1F5E8), so a load keeps the value it had. Its start value is the program's uninitialised
    /// data (past the EXE's image, no clear in the start-up code): 0 in DOSBox-X (s24). Not saved.
    /// </summary>
    public bool PictureArmed { get; set; }

    /// <summary>
    /// DS:84C8 / DS:84C9, the encounter draw's two state bytes (the far pointer every caller of 0x7A21 passes: SETUPMON
    /// 0x1188, APPROACH 0x158C, ENCMENU 0x2D88 and its approaches 0x3033–0x32A8): 84C8 the sprite is loaded and up
    /// (set at 0x7A8F when the sprite's frames are loaded), 84C9 a close-up is up (0x7B3E / 0x7B72, and PICTURE v ≠ 255
    /// at 0x15F3). Both cleared (a two-byte fill, START 0AF8:16A8) by EXIT (0xDC6), the script init (0x76B2), PROTECT
    /// (0x3BC9), COMBAT (0x261F) and PICTURE 255 (0x1689–0x168E, whether it redraws or not); not by SPRITEOFF or CALL
    /// $2C90. So they last one ECL run at most. Not saved; display bookkeeping.
    /// </summary>
    public bool EncounterSpriteUp { get; set; }

    /// <summary>DS:84C9, a close-up is up (see <see cref="EncounterSpriteUp"/>).</summary>
    public bool CloseUpUp { get; set; }

    /// <summary>
    /// DS:84EA, $6DE1 (the head) as the last encounter close-up drew it (0x7B06); nothing else writes it (not PICTURE, not
    /// the start-up: 0 from the program's uninitialised data in DOSBox-X). With <see cref="CloseUpUp"/> set and $6DE1
    /// equal to it, the encounter draw draws nothing at all (0x7ABC–0x7AD6). Not saved.
    /// </summary>
    public byte CloseUpHead { get; set; }

    /// <summary>The SPRIT block whose frames the encounter draw loaded when it set <see cref="EncounterSpriteUp"/> (0x7A87):
    /// while that is set it draws these frames again, whatever sprite the opcode names. Not saved.</summary>
    public byte EncounterSprite { get; set; }

    /// <summary>The fills of DS:84C8–84C9 (see <see cref="EncounterSpriteUp"/>).</summary>
    public void ClearEncounterDraw()
    {
        EncounterSpriteUp = false;
        CloseUpUp = false;
    }

    /// <summary>
    /// DS:6AAC, the area map is shown instead of the 3D view: the A command toggles it (0x170BF–0x170C9), the start-up
    /// clears it (0xE9B9), the redraw 0x2DCF4 turns it off where $49FB ≠ 0 (0x2DD9F), and the encounter draw turns it
    /// off and redraws before it loads a sprite (0x7A5B–0x7A67). Not saved.
    /// </summary>
    public bool AreaMapShown { get; set; }

    /// <summary>
    /// The view's redraw flags besides the picture's and sprite's (DS:84E0 <see cref="PictureShown"/> / 84E3, <see cref="Game.EncounterState"/>):
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
    /// DS:4A00 (a program byte, not the script cell $4A00): while set, the text routine START 0x77F8 types its text
    /// (0x7737: Delay(DS:49E2 × 10) after each glyph, START 0x777B). The only writers (every direct write form searched
    /// in START.EXE and GAME.OVR, s29): the start-up 0xE974 sets 1; PRINT / PRINTCLR 1 before the text (0x1763) and 0
    /// after it (0x17DC); ENCMENU 1 on entry (0x2C4F) and 0 at its one exit (0x32E0); the after-combat step's end 1
    /// (0x6BAF, after the summary, loot or party-destroyed screens). So it is 1 from the start-up and after each fight
    /// until the next PRINT or ENCMENU ends. Not saved; Load does not touch it.
    /// </summary>
    public bool TypesText { get; set; } = true;

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
        _sotsb = Profile.Handlers == EclHandlerSet.Sotsb;
        var words = Profile.Regions.Where(r => r.CellBits == 16).ToList();
        static bool In(EclMemoryRegion r, ushort a) => a >= r.Start && a <= r.End;
        _view = words.Single(r => In(r, Profile.Cells.CharacterView));
        _globals = words.Single(r => In(r, Profile.Cells.ViewFlag));
        _third = words.Single(r => r != _view && r != _globals);
        _scriptRegion = Profile.Regions.Single(r => r.CellBits == 8);
        _cells4900 = new ushort[_globals.Cells];
        _cells6B00 = new ushort[_view.Cells];
        _cells9700 = new ushort[_third.Cells];
        _script = new byte[Profile.ScriptAreaSize];
    }

    public Span<byte> ScriptArea => _script;
    /// <summary>The globals area: PoR $4900–$4CFF, SotSB $4B00–$4EFF.</summary>
    public Span<ushort> Cells4900 => _cells4900;
    /// <summary>The character-view area: PoR $6B00–$6EFF, SotSB $7C00–$7FFF.</summary>
    public Span<ushort> Cells6B00 => _cells6B00;
    /// <summary>The third word area: PoR $9700–$98FF, SotSB $7A00–$7BFF.</summary>
    public Span<ushort> Cells9700 => _cells9700;

    /// <summary>
    /// SotSB DS:67E7, the game mode, and DS:67E8, the mode before it: a write to $4BE6 that changes its value sets the
    /// mode to 4 and keeps the old one (writer OVR 0x9F68). [?] The other writers and the start value were not read
    /// (survey-sotsb.md §2). PoR has no such write case.
    /// </summary>
    public byte GameMode { get; set; }
    /// <summary>SotSB DS:67E8: see <see cref="GameMode"/>.</summary>
    public byte PreviousGameMode { get; set; }

    /// <summary>
    /// SotSB DS:A4D1: set by LOADCHAR when the list has no member v, cleared when it has (also cleared at OVR 0x3E01,
    /// in ADDNPC). While set, a read of $7D00 gives 0 (OVR 0x9B40).
    /// </summary>
    public bool MemberMissing { get; set; }

    /// <summary>Region name of an address in this memory's profile ("4B00", "7C00", "script", … or "special").</summary>
    public string Region(ushort a)
    {
        foreach (var r in Profile.Regions)
            if (a >= r.Start && a <= r.End)
                return r.Name;
        return "special";
    }

    /// <summary>PoR region name of an address: "4900", "6B00", "9700", "script" or "special" (<see cref="Region"/> for any profile).</summary>
    public static string RegionOf(ushort a) => a switch
    {
        >= Area4900 and <= Area4900End => "4900",
        >= Area6B00 and <= Area6B00End => "6B00",
        >= Area9700 and <= Area9700End => "9700",
        >= EclLayout.Base and <= EclLayout.End => "script",
        _ => "special",
    };

    /// <summary>
    /// Script byte at VM address <paramref name="address"/> as the interpreter fetches code: offset A − base
    /// (PoR: buffer + (A + 0x6700) mod 0x10000; SotSB: buffer + A − 0x8000). Null when the offset is outside the area.
    /// </summary>
    public byte? FetchCode(int address)
    {
        int off = (address - Profile.ScriptBase) & 0xFFFF;
        return off < _script.Length ? _script[off] : null;
    }

    private bool InView(ushort a) => a >= _view.Start && a <= _view.End;

    /// <summary>The stored cell (or script byte) without redirection; null for special addresses.</summary>
    public ushort? Cell(ushort a)
    {
        if (a >= _globals.Start && a <= _globals.End)
            return _cells4900[a - _globals.Start];
        if (InView(a))
            return _cells6B00[a - _view.Start];
        if (a >= _third.Start && a <= _third.End)
            return _cells9700[a - _third.Start];
        if (a >= _scriptRegion.Start && a <= _scriptRegion.End)
            return _script[a - _scriptRegion.Start];
        return null;
    }

    /// <summary>VM read (PoR GAME.OVR 0x82DF; SotSB OVR 0xA0F1).</summary>
    public ushort Read(ushort a)
    {
        if (InView(a) && (_sotsb ? TryRedirectReadSotsb(a, out var r) : TryRedirectRead(a, out r)))
            return r;
        // SotSB OVR 0xA10C: a read of $4BEA gives the facing byte DS:89D9 instead of the cell.
        if (_sotsb && a == 0x4BEA)
            return Position.Facing;
        if (Cell(a) is { } c)
            return c;
        switch (a)
        {
            // P (reader 0x82DF): x and y are both sign-extended bytes (DS:6AAD / DS:6AAE).
            case 0xC04B: return (ushort)(sbyte)Position.X;
            case 0xC04C: return (ushort)(sbyte)Position.Y;
            // SotSB OVR 0xA20C: the raw facing while $4BE6 = 0, facing / 2 otherwise; PoR always halves it.
            case 0xC04D:
                return _sotsb && _cells4900[Profile.Cells.ViewFlag - _globals.Start] == 0
                    ? Position.Facing
                    : (ushort)(Position.Facing / 2);
            case 0xC04E: return Position.WallAhead;
            case 0xC04F: return Position.EventByte;
            // P: the 8-way facing byte DS:6AAF (0, 2, 4, 6), unsigned.
            case 0x033D: return Position.Facing;
            // P: DS:6F6E / DS:6F6A / DS:6F6C, which no program code writes (DOSBox: 0, also after a
            // script wrote these addresses). Their writes are ignored. SotSB: DS:8F52 / 8F4E / 8F50 (OVR 0xA1AC),
            // which no byte of GAME.OVR or START.EXE writes either; 0 as in PoR [?] (not run).
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

    /// <summary>
    /// VM write (PoR GAME.OVR 0x814A; SotSB OVR 0x9F3A, the same special cases). Writes to unlisted special addresses
    /// are ignored (P) and logged.
    /// </summary>
    public void Write(ushort a, ushort value)
    {
        if (_sotsb && a == Profile.Cells.ViewFlag && _cells4900[a - _globals.Start] != value)
        {
            // OVR 0x9F68: a changed $4BE6 keeps the game mode in DS:67E8 and sets DS:67E7 = 4 (before the store).
            PreviousGameMode = GameMode;
            GameMode = 4;
        }
        if (StoreCell(a, value))
        {
            int sky = _globals.Start + 0xFD; // PoR $49FD/$49FE, SotSB $4BFD/$4BFE
            if (a == sky || a == sky + 1)
            {
                ViewRedrawFlags |= ViewRedraw.Sky; // PoR 0x8182: DS:84E8 := 1; SotSB OVR 0x9F61: DS:A4CE := 1
                SkyCeilingWritten?.Invoke(a, value);
            }
            if (InView(a))
            {
                if (_sotsb)
                    WriteSideEffectsSotsb(a, value);
                else
                    WriteSideEffects(a, value);
            }
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
        if (a >= _globals.Start && a <= _globals.End)
        {
            old = _cells4900[a - _globals.Start];
            _cells4900[a - _globals.Start] = value;
            Written?.Invoke(new EclMemoryWrite(a, _globals.Name, old, value));
            return true;
        }
        if (InView(a))
        {
            old = _cells6B00[a - _view.Start];
            _cells6B00[a - _view.Start] = value;
            Written?.Invoke(new EclMemoryWrite(a, _view.Name, old, value));
            return true;
        }
        if (a >= _third.Start && a <= _third.End)
        {
            old = _cells9700[a - _third.Start];
            _cells9700[a - _third.Start] = value;
            Written?.Invoke(new EclMemoryWrite(a, _third.Name, old, value));
            return true;
        }
        if (a >= _scriptRegion.Start && a <= _scriptRegion.End)
        {
            // P: the 16-bit result goes through the writer and a script-area destination keeps the low byte.
            old = _script[a - _scriptRegion.Start];
            _script[a - _scriptRegion.Start] = (byte)value;
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

    private void SetRecordWord(EclCharacter c, ushort a, int field, ushort value)
    {
        var old = c.Word(field);
        c.SetWord(field, value);
        Written?.Invoke(new EclMemoryWrite(a, "char", old, value));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // SotSB $7C00-area redirection (survey-sotsb.md §10.2: reader OVR 0x9830, writer OVR 0x9D20)
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// SotSB reader cases (OVR 0x9830), by offset from $7C00. Each case reads the current character's record (the
    /// pointer DS:7D38); a case with no value set reads stack residue. An offset with no case reads the cell.
    /// </summary>
    private bool TryRedirectReadSotsb(ushort a, out ushort v)
    {
        v = 0;
        int k = a - _view.Start;
        switch (k)
        {
            case 0x312: // OVR 0x9CEE: the disk byte DS:7420
                v = Disk;
                return true;
            case 0x33E: // OVR 0x9CFD: the cell $7F3E itself, read through the area pointer
                return false;
            case 0x2B1 or 0x2B4:
            {
                // OVR 0x9C1F → 0x97CF: list members passed before the current record (the list length when it is
                // not listed or nil), as a byte.
                var p = Party;
                if (p is null)
                {
                    CurrentCharacter(a);
                    return false;
                }
                v = (byte)(p.Current is not null ? p.CurrentIndex : p.Members.Count);
                return true;
            }
            case 0x10D: // OVR 0x9BEB: the case is there, but no value is set
                v = UndefinedRead($"read of ${a:X4}: the reader has a case for it but sets no value (stack residue)");
                return true;
            case 0x100 when MemberMissing: // OVR 0x9B40: DS:A4D1 set by LOADCHAR → 0
                v = 0;
                return true;
        }
        bool record = k is 0x15 or 0x18 or 0x19 or 0x72 or 0x73 or 0x76 or 0x9B or 0xA0 or (>= 0xA5 and <= 0xAC) or 0xB8
            or 0xBB or 0xBD or 0xBF or 0xC1 or 0xC3 or 0xC5 or 0xC9 or 0xCA or 0xCB or 0xCC or 0xCF or 0xD0 or 0xD6
            or 0xD8 or 0xF7 or 0xF9 or 0x100 or 0x10C or 0x119 or 0x11B or 0x2CF;
        if (!record)
            return false;
        var c = CurrentCharacter(a);
        if (c is null)
            return false;
        v = k switch
        {
            0x15 => c[0x13],
            0x18 => c[0x19],
            0x19 => c[0x1B],
            0x72 => (ushort)(sbyte)c[0x6B],
            0x73 => (ushort)(sbyte)c[0x6C],
            0x76 => c[0x70],
            0x9B => c[0xE9],
            0xA0 => (ushort)(sbyte)c[0xEE],
            >= 0xA5 and <= 0xAC => c[0xF3 + (k - 0xA5)], // OVR 0x98FA: record 0xF2 + (k − 0xA4)
            0xB8 => c[0xFF],
            0xBB or 0xBD or 0xBF or 0xC1 or 0xC3 or 0xC5 => c.Word(0x103 + (k - 0xBB)),
            // OVR 0x99A9–0x9AE4: flag · record a + record b, signed bytes, 16-bit result.
            0xC9 => SotsbLevelPair(c, 0x11D, 0x116),
            0xCA => SotsbLevelPair(c, 0x118, 0x111),
            0xCB => SotsbLevelPair(c, 0x11E, 0x117),
            0xCC => SotsbLevelPair(c, 0x11A, 0x113),
            0xCF => SotsbLevelPair(c, 0x11B, 0x114),
            0xD0 => SotsbLevelPair(c, 0x11C, 0x115),
            0xD6 => (ushort)(sbyte)c[0x11F],
            0xD8 => (ushort)(sbyte)c[0x120],
            0xF7 => c.Word(0x14E),
            0xF9 => c[0x150],
            // OVR 0x9B4E: record 0x1A6 mapped; any other value 0.
            0x100 => c[0x1A6] switch { 0 => 1, 3 => 6, 4 => 5, 5 => 4, 6 => 3, 7 => 7, 8 => 2, _ => 0 },
            // OVR 0x9BB1: as PoR's $6C0C, from record 0x1A8 / 0x1A9.
            0x10C => (ushort)(c[0x1A8] == 0 && c[0x1A9] != 0 ? 0x80 : c[0x1A8] == 1 ? 0x81 : 0),
            0x119 => c[0x1B5],
            0x11B => c[0x1B6],
            _ => SotsbAgeTable(a, c), // 0x2CF
        };
        return true;
    }

    /// <summary>
    /// OVR 0x9C59: PoR's $6DCF table, keyed by record 0x1B; a value outside 3–25 leaves the result unset (residue).
    /// </summary>
    private ushort SotsbAgeTable(ushort a, EclCharacter c)
    {
        int? t = c[0x1B] switch
        {
            3 => 0, 4 => 5, 5 => 10, 6 => 15, 7 => 20, >= 8 and <= 12 => 25, 13 => 30, 14 => 35, 15 => 40,
            16 => 50, 17 => 55, >= 18 and <= 25 => 60, _ => null,
        };
        return t is { } x
            ? (ushort)x
            : UndefinedRead($"read of ${a:X4} for record 0x1B = {c[0x1B]}: the table sets no value (stack residue)");
    }

    /// <summary>
    /// The level pairs of $7CC9–$7CD0 (OVR 0x99A9): <see cref="SotsbSecondClassActive"/> (0 or 1) × record
    /// <paramref name="flagged"/> + record <paramref name="plain"/>, all signed bytes.
    /// </summary>
    private static ushort SotsbLevelPair(EclCharacter c, int flagged, int plain) =>
        (ushort)(SotsbSecondClassActive(c) * (sbyte)c[flagged] + (sbyte)c[plain]);

    /// <summary>
    /// OVR 0x3CBB6: 1 when 0x3CB44's level is greater than record 0xEF (signed bytes), else 0. 0x3CB44: when record
    /// 0x6B is 6, the first non-zero of record 0x111–0x116 (record 0x117 when all six are 0), else 0. [?] Meaning
    /// (presumably a dual-class human whose new class has passed the old one); the record layout was not read.
    /// </summary>
    public static int SotsbSecondClassActive(EclCharacter c)
    {
        int level = 0;
        if (c[0x6B] == 6)
        {
            int i = 0;
            while (i < 6 && c[0x111 + i] == 0)
                i++;
            level = (sbyte)c[0x111 + i];
        }
        return level > (sbyte)c[0xEF] ? 1 : 0;
    }

    /// <summary>SotSB writer cases (OVR 0x9D20), after the cell is stored. Record cases write the current character.</summary>
    private void WriteSideEffectsSotsb(ushort a, ushort value)
    {
        int k = a - _view.Start;
        switch (k)
        {
            case 0:
                if (value == 0)
                    Ds84D3 = true; // OVR 0x9D3D: DS:A4B7 := 1 (arms LOADCHAR's removal with DS:A4B6)
                return;
            case 0x312:
                // OVR 0x9EC6 → START 0640:0AB0: the old disk to DS:7421, then DS:7420 = (low byte + 1) div 2.
                PreviousDisk = Disk;
                Disk = (byte)(((value & 0xFF) + 1) / 2);
                return;
            case 0x322 or 0x324 or 0x326:
                if (value > 0x80) // OVR 0x9ED2: wall set (v AND 0x7F) into slot 1/2/3 (OVR 0x4077E)
                    WallSetRequested?.Invoke(1 + (k - 0x322) / 2, value & 0x7F);
                return;
        }
        bool hasEffect = k is (>= 0x1B and <= 0x65) or 0xB8 or 0xBB or 0xBD or 0xBF or 0xC1 or 0xC3 or 0xC5 or 0xF7
            or 0xF9 or 0x100 or 0x10C or 0x119;
        if (!hasEffect)
            return;
        var c = CurrentCharacter(a);
        if (c is null)
            return;
        switch (k)
        {
            case >= 0x1B and <= 0x65:
                SetRecordByte(c, a, 0x1E + (k - 0x1B), (byte)value); // OVR 0x9D4F
                return;
            case 0xB8:
                SetRecordByte(c, a, 0xFF, (byte)(value > 0xB2 ? value - 0x32 : value)); // OVR 0x9D72
                return;
            case 0xBB or 0xBD or 0xBF or 0xC1 or 0xC3 or 0xC5:
                SetRecordWord(c, a, 0x103 + (k - 0xBB), value);
                return;
            case 0xF7:
                SetRecordWord(c, a, 0x14E, value);
                return;
            case 0xF9:
                SetRecordByte(c, a, 0x150, (byte)value);
                return;
            case 0x100:
                if (value == 1) // OVR 0x9E31
                {
                    SetRecordByte(c, a, 0x1A6, 0);
                    SetRecordByte(c, a, 0x1A7, 1);
                }
                return;
            case 0x10C:
                switch (value) // OVR 0x9E55, as PoR's $6C0C
                {
                    case 0: SetRecordByte(c, a, 0x1A8, 0); SetRecordByte(c, a, 0x1A9, 0); break;
                    case 0x80: SetRecordByte(c, a, 0x1A8, 0); SetRecordByte(c, a, 0x1A9, 1); break;
                    case 0x81: SetRecordByte(c, a, 0x1A8, 1); SetRecordByte(c, a, 0x1A9, 1); break;
                }
                return;
            case 0x119:
                SetRecordByte(c, a, 0x1B5, (byte)value);
                return;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Text
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Text of a type-0x81 operand (PoR routine 0x87CE, SotSB OVR 0xA628): one character per cell (low byte of a word
    /// cell, or a script byte) until a cell that is 0 (PoR: a 0 low byte; SotSB: a 0 word, OVR 0xA664); the first
    /// character-view cell ($6B00 / $7C00) gives the current character's name instead.
    /// </summary>
    public string ReadText(ushort a)
    {
        ushort view = Profile.Cells.CharacterView;
        if (a == view && Party?.Current is { } ch)
            return ch.Name;
        if (a == view)
            CurrentCharacter(a);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Policy.MaxMemoryTextLength; i++)
        {
            var at = (ushort)(a + i);
            int c;
            if (Cell(at) is { } cell)
            {
                c = cell & 0xFF;
                if (_sotsb && cell != 0 && c == 0)
                {
                    // SotSB stops only on a 0 word; the 0 low byte is appended as a character.
                    sb.Append('\0');
                    continue;
                }
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
    /// SAVE of a text (PoR routine 0x8426, SotSB OVR 0xA264; INPUTSTR uses the same rules): one character per cell,
    /// then a 0 cell. The first character-view cell ($6B00 / $7C00) sets the current character's name (at most 15
    /// characters) instead. Writes go straight to the cells, so the character-view side effects do not run; special
    /// addresses: nothing.
    /// </summary>
    public void WriteText(ushort a, string text)
    {
        if (a == Profile.Cells.CharacterView)
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
        if (text.Length == 0 && !_sotsb) // SotSB (OVR 0xA29A) skips the copy loop for a length of 0: only the 0 is stored
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
