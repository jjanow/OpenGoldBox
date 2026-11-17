namespace GoldBox.Core.Ecl;

/// <summary>What the VM does on an opcode with no handler (0x1F, ≥ 0x3E).</summary>
public enum UnhandledOpcodeMode
{
    /// <summary>Log a diagnostic and stop the run (a VM fault). Default.</summary>
    StopWithDiagnostic,
    /// <summary>Do what the program appears to do: leave the PC on the byte and loop until the step budget runs out.</summary>
    Hang,
}

/// <summary>
/// The behaviours the spec still leaves undecided, in one place. Each property names the spec item it stands for. The
/// defaults are choices, not facts: change them here when a DOSBox trace settles an item. Items the spec has settled
/// (SUB order, flag rules, text compare, DIV by zero, disk byte, $6B00 redirection, …) are fixed in the VM and have
/// no switch here.
/// </summary>
public sealed class EclVmPolicy
{
    public static EclVmPolicy Default => new();

    /// <summary>[?] ecl-format.md "Opcode 0x1F and ≥ 0x3E" (T item): the program never advances the PC.</summary>
    public UnhandledOpcodeMode UnhandledOpcode { get; set; } = UnhandledOpcodeMode.StopWithDiagnostic;

    /// <summary>
    /// [?] Value returned for reads the program leaves undefined: unlisted special addresses, operand types other
    /// than 0/1/2/3/0x80/0x81, $6C0D, $6DCF for out-of-table values, and record reads with no current character.
    /// Always logged.
    /// </summary>
    public ushort UndefinedValue { get; set; }

    /// <summary>
    /// [?] SAVE with a type-0x81 source (10 uses in v1.3): true = copy the text at that address (the spec's "if v is
    /// text"); false = store the raw 16-bit field (0x81 "as a value"). The spec does not say which types count as
    /// text for SAVE.
    /// </summary>
    public bool SaveCopiesTextFrom81 { get; set; } = true;

    /// <summary>
    /// Longest text read from VM memory for a type-0x81 operand. The string slot holds 255 characters (Pascal
    /// string); what the reader does past that is not read `[?]`.
    /// </summary>
    public int MaxMemoryTextLength { get; set; } = 255;

    /// <summary>
    /// [?] Read $C04B (x) sign-extended like $C04C (y). The spec marks only y as signed; save-format.md calls x a
    /// signed byte in the game.
    /// </summary>
    public bool SignExtendX { get; set; }

    /// <summary>
    /// [?] VMENU / HMENU: the variadic count is taken through the value fetch; the VM uses its low byte (settled for
    /// ON GOTO / ON GOSUB only, 0x263E).
    /// </summary>
    public bool VariadicCountLowByte { get; set; } = true;
}
