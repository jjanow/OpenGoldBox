namespace GoldBox.Core.Ecl.Profiles;

/// <summary>One region of the VM address space (docs/re/ecl-format.md "VM memory map").</summary>
/// <param name="Name">Region name, as <see cref="EclMemory.RegionOf"/> gives it.</param>
/// <param name="Start">First address.</param>
/// <param name="End">Last address.</param>
/// <param name="CellBits">Width of one cell: 16 (word cells) or 8 (the script bytes).</param>
/// <param name="Saved">Whether a save stores the region.</param>
public sealed record EclMemoryRegion(string Name, ushort Start, ushort End, int CellBits, bool Saved)
{
    public int Cells => End - Start + 1;
}

/// <summary>Where a memory read takes its width from.</summary>
public enum EclOperandWidthRule
{
    /// <summary>PoR: from the address region (16-bit cell areas, byte script area), never from the operand type.</summary>
    FromAddressRegion,
}

/// <summary>What a read of an address outside every region and special-address list returns.</summary>
public enum EclUnmappedRead
{
    /// <summary>
    /// PoR: stack residue (an uninitialised local of the reader). The VM uses <see cref="EclVmPolicy.UndefinedValue"/>
    /// (or the COMPARE residue it models) and logs the read.
    /// </summary>
    StackResidue,
}

/// <summary>What RETURN does when the GOSUB stack is empty.</summary>
public enum EclReturnOnEmptyStack
{
    /// <summary>PoR: nothing; the PC is already past the opcode, so the run goes on with the next instruction.</summary>
    FallsThrough,
    /// <summary>
    /// Curse / SotSB: the run ends as at EXIT (SotSB OVR 0x170A calls the EXIT handler 0x0C0B when the stack is empty;
    /// docs/re/survey-sotsb.md §3.4).
    /// </summary>
    Exits,
}

/// <summary>How scripts see the current character.</summary>
public enum EclCharacterView
{
    /// <summary>
    /// PoR: plain $6B00-area cells, some of them redirected to the current character's record or to program variables
    /// on read, with side effects on write (routines 0x7C8F / 0x7F47). The list is code in <see cref="EclMemory"/>.
    /// </summary>
    Por6B00Redirection,
    /// <summary>
    /// SotSB: PoR's redirection scheme at $7C00 into the 439-byte record (reader OVR 0x9830, writer OVR 0x9D20;
    /// docs/re/survey-sotsb.md §3.3). The case list is not modelled yet `[?]`.
    /// </summary>
    Sotsb7C00Redirection,
}

/// <summary>The opcode handlers (code). A profile names its set; the VM runs only the sets it has.</summary>
public enum EclHandlerSet
{
    /// <summary>PoR v1.3: <c>EclVm.Execute</c> and the reader/writer of <see cref="EclMemory"/>.</summary>
    Por13,
    /// <summary>
    /// SotSB v1.00–v1.30 (docs/re/survey-sotsb.md §3.5): PoR's handlers plus RETURN-exits, two-operand CLOCK, the 0x3C
    /// stub, opcodes 3E–41 and SotSB's CALL / PROGRAM ids. Not implemented: <see cref="EclMemory"/> refuses it, so a
    /// SotSB profile decodes (<see cref="EclDecoder"/>, <see cref="EclAnalysis"/>) but does not run.
    /// </summary>
    Sotsb,
}

/// <summary>
/// A VM profile (docs/module-format.md §5.1): the choices of one ECL dialect, named by a module's <c>vm</c>. Choices
/// that are data are fields here; choices that are code (the opcode handlers, the character view) are named by
/// <see cref="Handlers"/> and <see cref="CharacterView"/> and live in <see cref="EclVm"/> and <see cref="EclMemory"/>.
/// The undecided `[?]` switches stay in <see cref="EclVmPolicy"/>, which is per VM instance, not per profile.
/// Profiles come from <see cref="EclProfiles"/>.
/// </summary>
public sealed class EclProfile
{
    /// <summary>The name a module's manifest uses (<c>vm</c>).</summary>
    public required string Name { get; init; }

    /// <summary>VM address of the first loaded script byte.</summary>
    public required ushort ScriptBase { get; init; }
    /// <summary>Block bytes in front of the script that are not loaded.</summary>
    public required int ScriptPrefix { get; init; }
    /// <summary>Size of the script area: longer blocks are cut to it.</summary>
    public required int ScriptAreaSize { get; init; }
    /// <summary>Entry vectors at the start of a script.</summary>
    public required int VectorCount { get; init; }
    /// <summary>The vector run on a script switch and after a load.</summary>
    public required int InitVector { get; init; }

    /// <summary>The memory regions, in address order.</summary>
    public required IReadOnlyList<EclMemoryRegion> Regions { get; init; }
    public required EclOperandWidthRule OperandWidth { get; init; }
    public required EclUnmappedRead UnmappedRead { get; init; }

    /// <summary>Opcode table: handler rows, operand counts, variadic count position, and the IF-skip counts.</summary>
    public required EclOpcodeTable Opcodes { get; init; }
    public required EclReturnOnEmptyStack ReturnOnEmptyStack { get; init; }

    /// <summary>CALL ids (raw operand address) the program implements, with what each does. Other ids do nothing.</summary>
    public required IReadOnlyDictionary<ushort, string> CallIds { get; init; }
    /// <summary>PROGRAM ids the program implements, with what each does. Other ids do nothing.</summary>
    public required IReadOnlyDictionary<ushort, string> ProgramIds { get; init; }
    /// <summary>PROGRAM ids after which the run ends like EXIT (subject to <see cref="ProgramExitGuardCell"/>).</summary>
    public required IReadOnlySet<ushort> ProgramIdsThatExit { get; init; }
    /// <summary>
    /// When set, a PROGRAM id of <see cref="ProgramIdsThatExit"/> ends the run only while this VM cell is 0 (SotSB:
    /// $4C38, OVR 0x422E). Null: it always does (PoR).
    /// </summary>
    public ushort? ProgramExitGuardCell { get; init; }

    public required EclCharacterView CharacterView { get; init; }
    public required EclHandlerSet Handlers { get; init; }

    /// <summary>Whether a save stores the script bytes (else the script is reloaded by id).</summary>
    public required bool ScriptBytesSaved { get; init; }

    /// <summary>The VM cell that holds the current disk, or null for a dialect with no disks.</summary>
    public required ushort? DiskCell { get; init; }
    /// <summary>Resource kinds (module-format.md §2 names) whose lookups go through the bank = current disk.</summary>
    public required IReadOnlySet<string> BankedKinds { get; init; }
    /// <summary>
    /// Resource kinds kept in bank 0 whose <c>meta.disk</c> is checked against the current disk (the program opens the
    /// current disk's file and fails when the block is not there).
    /// </summary>
    public required IReadOnlySet<string> DiskCheckedKinds { get; init; }

    /// <summary>Last script address.</summary>
    public ushort ScriptEnd => (ushort)(ScriptBase + ScriptAreaSize - 1);

    /// <summary>Block offset (prefix included) of VM script address <paramref name="addr"/>.</summary>
    public int AddressToOffset(int addr) => addr - ScriptBase + ScriptPrefix;
    /// <summary>VM script address of block offset <paramref name="off"/> (prefix included).</summary>
    public ushort OffsetToAddress(int off) => (ushort)(ScriptBase + off - ScriptPrefix);
    /// <summary>Block offset of the first byte after the vector table.</summary>
    public int FirstCodeOffset => ScriptPrefix + 4 * VectorCount;

    public override string ToString() => Name;
}
