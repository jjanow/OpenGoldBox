namespace GoldBox.Core.Ecl;

/// <summary>What the VM does on an opcode with no handler (0x1F, ≥ 0x3E).</summary>
public enum UnhandledOpcodeMode
{
    /// <summary>
    /// Log a diagnostic and stop the run at once, with the PC still on the byte (default). The original never gets
    /// past it (see <see cref="Hang"/>); stopping reports that hang instead of spinning.
    /// </summary>
    StopWithDiagnostic,
    /// <summary>
    /// What the program does (GAME.OVR 0x3C71 has no case, the run loop 0x3EB7 re-executes the byte; DOSBox trace
    /// 2026-11-17): leave the PC on the byte and loop until the step budget runs out.
    /// </summary>
    Hang,
}

/// <summary>
/// The behaviours the spec still leaves undecided, in one place. Each property names the spec item it stands for. The
/// defaults are choices, not facts: change them here when a DOSBox trace settles an item. Items the spec has settled
/// (SUB order, flag rules, text compare, DIV by zero, disk byte, $6B00 redirection, $C04B sign, SAVE of a 0x81 text,
/// …) are fixed in the VM and have no switch here.
/// </summary>
public sealed class EclVmPolicy
{
    public static EclVmPolicy Default => new();

    /// <summary>
    /// Opcode 0x1F and ≥ 0x3E: the original hangs (settled, ecl-format.md "Instructions and control flow"). Not a
    /// [?] item: the switch only picks how the VM reports it.
    /// </summary>
    public UnhandledOpcodeMode UnhandledOpcode { get; set; } = UnhandledOpcodeMode.StopWithDiagnostic;

    /// <summary>
    /// [?] Value returned for reads the program leaves undefined. In the program these return stack residue (the
    /// reader's or the value fetch's uninitialised local, ecl-format.md "VM memory map"): reads of $035F, $C059 and
    /// other unlisted special addresses, operand types other than 0/1/2/3/0x80/0x81, $6C0D, $6DCF for out-of-table
    /// values, and record reads with no current character. The one residue the VM models is inside COMPARE (the
    /// value of the previous COMPARE operand read); everything else gets this value. Always logged.
    /// </summary>
    public ushort UndefinedValue { get; set; }

    /// <summary>
    /// Longest text read from VM memory for a type-0x81 operand. The string slot holds 255 characters (Pascal
    /// string); what the reader does past that is not read `[?]`.
    /// </summary>
    public int MaxMemoryTextLength { get; set; } = 255;

    /// <summary>
    /// [?] SPELL (GAME.OVR 0x3949) tests its "found" byte [bp−8] before ever setting it: stack residue. Non-zero
    /// (default, as DOSBox showed after a SAVE: results 1 and 0, capture <c>party_opcodes</c>) skips the search; 0 runs
    /// it over the first party member. The results go to $00xx addresses either way, so no v1.3 outcome depends on it.
    /// </summary>
    public byte SpellFoundResidue { get; set; } = 1;
}
