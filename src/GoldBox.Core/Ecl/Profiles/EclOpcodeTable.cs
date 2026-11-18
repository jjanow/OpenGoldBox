namespace GoldBox.Core.Ecl.Profiles;

/// <summary>
/// An opcode table as profile data: one row per handled opcode (operand names and count, variadic tail and the
/// position of its count, effect source) and the IF-skip table, which the program keeps apart from the handlers and
/// which may disagree with them.
/// </summary>
public sealed class EclOpcodeTable
{
    private readonly EclOpcodeInfo?[] _rows;
    private readonly int[] _skip;
    private readonly IReadOnlyDictionary<byte, string> _unhandledNames;

    /// <param name="rows">256 entries; null = no handler.</param>
    /// <param name="ifSkip">256 entries: operands the IF-skip consumes after each opcode.</param>
    /// <param name="unhandledNames">Mnemonics for opcodes with no handler that still have a name.</param>
    public EclOpcodeTable(IReadOnlyList<EclOpcodeInfo?> rows, IReadOnlyList<int> ifSkip,
        IReadOnlyDictionary<byte, string>? unhandledNames = null)
    {
        if (rows.Count != 256 || ifSkip.Count != 256)
            throw new ArgumentException("an opcode table has 256 rows and 256 skip counts");
        _rows = [.. rows];
        _skip = [.. ifSkip];
        _unhandledNames = unhandledNames ?? new Dictionary<byte, string>();
        MisSkips = [.. Enumerable.Range(0, 256).Select(i => (byte)i)
            .Where(op => _rows[op] is { } r && _skip[op] != r.FixedCount)];
    }

    /// <summary>Handler row, or null for an opcode with no handler.</summary>
    public EclOpcodeInfo? Get(byte op) => _rows[op];

    public bool IsHandled(byte op) => _rows[op] is not null;

    /// <summary>Operands the IF-skip consumes after opcode <paramref name="op"/>.</summary>
    public int SkipCount(byte op) => _skip[op];

    /// <summary>Mnemonic, also for opcodes with no handler.</summary>
    public string Name(byte op) =>
        _rows[op]?.Mnemonic ?? (_unhandledNames.TryGetValue(op, out var n) ? n : $"OP_{op:X2}");

    /// <summary>Handled opcodes whose IF-skip count differs from their fixed operand count (the skip lands inside them).</summary>
    public IReadOnlyList<byte> MisSkips { get; }
}
