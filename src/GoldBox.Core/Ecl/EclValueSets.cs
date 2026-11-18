namespace GoldBox.Core.Ecl;

/// <summary>
/// A small, conservative static evaluator over a decoded script (<see cref="EclAnalysis"/>): the set of values a VM
/// cell can hold when an instruction runs, found by walking back along the straight line of code before it. Used to
/// list the maps a LOADFILES with a computed operand loads (docs/module-format.md §6). It answers null ("not known
/// statically") as soon as anything could bring in another value: an entry point or jump target on the way, a GOSUB or
/// an instruction whose memory effects are not listed below, an IF that mis-skips, or a write it cannot evaluate.
/// <para>
/// Writes it evaluates (ecl-format.md opcode table): SAVE, ADD, SUB, AND, RANDOM (0..n for n &lt; 255), GETTABLE from a table in
/// the script (its bytes) with a known index. Reads: immediates, the facing cell $C04D (facing ÷ 2, 0–3: the facing is
/// 0/2/4/6), and cells whose own values are found the same way. A write behind an IF adds its values to those the
/// cell had before the IF. Only the script temporaries are followed (<see cref="FirstTemporary"/>).
/// </para>
/// <para>
/// Assumptions: script tables are read as imported (self-modification is not followed; in PoR v1.3 the tables behind
/// the computed LOADFILES are referenced by their GETTABLE only, per <c>ecl.py dis</c>); the facing is cardinal; the
/// CALLs passed over run no script code (combat-format.md "CALL ids": $C01E steps and re-reads the square, $2C90
/// redraws, $C018 re-reads the wall, $BA03 plays a sound).
/// </para>
/// <para>PoR v1.3 only: the cells, CALL ids and layout above are PoR's (<see cref="EclLayout"/>); only the PoR importer uses it.</para>
/// </summary>
public static class EclValueSets
{
    /// <summary>Above this many values a set counts as unknown.</summary>
    public const int MaxValues = 256;

    /// <summary>Instructions that write VM memory only through their <c>dst</c> operands and run no other script code.</summary>
    private static readonly HashSet<byte> Plain =
    [
        EclOpcodes.Compare, EclOpcodes.CmpAnd, EclOpcodes.Save, EclOpcodes.Add, EclOpcodes.Sub, EclOpcodes.Mul,
        EclOpcodes.Div, EclOpcodes.And, EclOpcodes.Or, EclOpcodes.Random, EclOpcodes.GetTable, EclOpcodes.LoadFiles,
        EclOpcodes.LoadPieces, EclOpcodes.Print, EclOpcodes.PrintClr, EclOpcodes.Picture, EclOpcodes.SpriteOff,
        EclOpcodes.Delay, EclOpcodes.ClearBox, EclOpcodes.PrintRet,
        EclOpcodes.IfEq, EclOpcodes.IfNe, EclOpcodes.IfLt, EclOpcodes.IfGt, EclOpcodes.IfLe, EclOpcodes.IfGe,
    ];

    /// <summary>CALL ids that touch no script cell (combat-format.md "CALL ids"): redraw, wall ahead, step, sound.</summary>
    private static readonly HashSet<ushort> PlainCalls = [0x2C90, 0xC018, 0xC01E, 0xBA03];

    private const ushort FacingCell = 0xC04D;

    /// <summary>
    /// The cells it follows: the script temporaries $6E79–$6E82 (the script init zeroes them; ecl-format.md "On load").
    /// Engine-state cells are not followed, because the "plain" opcodes above may set some of them (LOADFILES $49C5).
    /// </summary>
    public const ushort FirstTemporary = 0x6E79, LastTemporary = 0x6E82;

    private sealed class Context(EclAnalysis an)
    {
        public EclAnalysis An { get; } = an;
        public Dictionary<int, EclInstruction> ByEnd { get; } = an.Instructions.Values
            .GroupBy(i => i.Offset + i.Size).ToDictionary(g => g.Key, g => g.First());
        public HashSet<int> Entries { get; } = Targets(an);
    }

    /// <summary>
    /// The values <paramref name="cell"/> can hold when the instruction at block offset <paramref name="offset"/> runs,
    /// or null when that is not known statically.
    /// </summary>
    public static IReadOnlySet<int>? Before(EclAnalysis an, int offset, ushort cell)
    {
        try
        {
            return Before(new Context(an), offset, cell, 0);
        }
        catch (MisSkip)
        {
            return null;
        }
    }

    /// <summary>
    /// The immediate values SAVE writes to <paramref name="cell"/> on the straight line before <paramref name="offset"/>
    /// (back to the first entry point or jump target; e.g. the disk cell $6E12 before a LOADFILES).
    /// </summary>
    public static IReadOnlySet<int> ImmediateWritesBefore(EclAnalysis an, int offset, ushort cell)
    {
        var found = new HashSet<int>();
        var ctx = new Context(an);
        try
        {
            for (int at = offset; !ctx.Entries.Contains(at) && Previous(ctx, at) is { } p; at = p.Offset)
                if (p.Op == EclOpcodes.Save && Dst(p) == cell && Immediate(p.Operands[0]) is { } v)
                    found.Add(v);
        }
        catch (MisSkip)
        {
        }
        return found;
    }

    private static IReadOnlySet<int>? Before(Context ctx, int offset, ushort cell, int depth)
    {
        if (depth > 64 || cell is < FirstTemporary or > LastTemporary)
            return null;
        int at = offset;
        while (true)
        {
            if (ctx.Entries.Contains(at))
                return null; // another path joins here
            var p = Previous(ctx, at);
            if (p is null)
                return null;
            if (!Plain.Contains(p.Op) && !(p.Op == EclOpcodes.Call && p.Operands.Count > 0 && p.Operands[0].Word is { } id && PlainCalls.Contains(id)))
                return null;
            var guard = Guard(ctx, p);
            if (Writes(p, cell))
            {
                var vals = Evaluate(ctx, p, depth);
                if (vals is null)
                    return null;
                if (cell is >= EclLayout.Base and <= EclLayout.End)
                    vals = vals.Select(v => v & 0xFF).ToHashSet(); // the script area holds bytes
                if (guard is null)
                    return vals;
                // Behind an IF: either the write ran, or the cell kept what it had before the IF.
                var before = Before(ctx, guard.Offset, cell, depth + 1);
                if (before is null)
                    return null;
                var all = new HashSet<int>(vals);
                all.UnionWith(before);
                return all.Count > MaxValues ? null : all;
            }
            at = p.Offset;
        }
    }

    /// <summary>
    /// The instruction that runs just before the one at <paramref name="offset"/> on the straight line: the one ending
    /// there, or, when that one is a GOTO / EXIT / RETURN / NEWECL behind an IF, the IF (which skips it).
    /// </summary>
    private static EclInstruction? Previous(Context ctx, int offset)
    {
        if (!ctx.ByEnd.TryGetValue(offset, out var p))
            return null;
        if (p.Op is EclOpcodes.Goto or EclOpcodes.Exit or EclOpcodes.Return or EclOpcodes.NewEcl)
            return Guard(ctx, p) is { } g ? g : null;
        if (p.Op is EclOpcodes.Gosub or EclOpcodes.OnGosub or EclOpcodes.OnGoto)
            return null;
        return p;
    }

    /// <summary>The IF right before <paramref name="p"/> when its skip lands exactly after <paramref name="p"/>; null otherwise.</summary>
    private static EclInstruction? Guard(Context ctx, EclInstruction p)
    {
        if (!ctx.ByEnd.TryGetValue(p.Offset, out var g) || !EclOpcodes.IsIf(g.Op))
            return null;
        return p.SkipSize == p.Size ? g : throw new MisSkip();
    }

    private sealed class MisSkip : Exception;

    private static ushort? Dst(EclInstruction p)
    {
        for (int i = 0; i < p.Operands.Count; i++)
            if (p.Role(i).StartsWith("dst", StringComparison.Ordinal) && p.Operands[i].IsMemory && p.Operands[i].Word is { } w)
                return w;
        return null;
    }

    private static bool Writes(EclInstruction p, ushort cell) => Dst(p) == cell;

    private static int? Immediate(EclOperand o) => o.IsMemory || !o.IsKnownType || o.Type >= 0x80 ? null : o.Word ?? o.Lo;

    private static IReadOnlySet<int>? Value(Context ctx, EclInstruction at, EclOperand o, int depth)
    {
        if (Immediate(o) is { } v)
            return new HashSet<int> { v };
        if (!o.IsMemory || o.Word is not { } a)
            return null;
        if (a == FacingCell)
            return new HashSet<int> { 0, 1, 2, 3 };
        return Before(ctx, at.Offset, a, depth + 1);
    }

    private static IReadOnlySet<int>? Evaluate(Context ctx, EclInstruction p, int depth)
    {
        IReadOnlySet<int>? Op(int i) => i < p.Operands.Count ? Value(ctx, p, p.Operands[i], depth) : null;
        IReadOnlySet<int>? Combine(Func<int, int, int> f)
        {
            if (Op(0) is not { } x || Op(1) is not { } y)
                return null;
            var r = new HashSet<int>();
            foreach (var a in x)
                foreach (var b in y)
                    r.Add(f(a, b) & 0xFFFF);
            return r.Count > MaxValues ? null : r;
        }
        switch (p.Op)
        {
            case EclOpcodes.Save:
                return Op(0);
            case EclOpcodes.Add:
                return Combine((a, b) => a + b);
            case EclOpcodes.Sub:
                return Combine((a, b) => b - a); // ecl-format.md: SUB a b → b − a
            case EclOpcodes.And:
                return Combine((a, b) => a & b & 0xFF);
            case EclOpcodes.Random:
            {
                if (Op(0) is not { } m)
                    return null;
                var r = new HashSet<int>();
                foreach (var v in m)
                {
                    int n = v & 0xFF;
                    if (n < 255)
                        n++;
                    for (int i = 0; i < n; i++)
                        r.Add(i);
                }
                return r;
            }
            case EclOpcodes.GetTable:
            {
                var t = p.Operands[0];
                if (!t.IsMemory || t.Word is not { } table || table < EclLayout.Base || Op(1) is not { } idx)
                    return null;
                var r = new HashSet<int>();
                foreach (var i in idx)
                {
                    int off = EclLayout.AddressToOffset(table + (i & 0xFF));
                    if (off >= ctx.An.Block.Length)
                        return null;
                    r.Add(ctx.An.Block[off]);
                }
                return r;
            }
            default:
                return null;
        }
    }

    /// <summary>Block offsets where code other than the straight line can arrive: vectors and jump targets.</summary>
    private static HashSet<int> Targets(EclAnalysis an)
    {
        var t = new HashSet<int>(an.VectorOffsets);
        foreach (var i in an.Instructions.Values)
        {
            if (i.Op is EclOpcodes.Goto or EclOpcodes.Gosub)
            {
                if (i.Operands.Count > 0 && i.Operands[0].Word is { } w)
                    t.Add(EclLayout.AddressToOffset(w));
            }
            else if (i.Op is EclOpcodes.OnGoto or EclOpcodes.OnGosub)
            {
                foreach (var o in i.Operands.Skip(2))
                    if (o.Word is { } w)
                        t.Add(EclLayout.AddressToOffset(w));
            }
            else if (EclOpcodes.IsIf(i.Op) && an.Instructions.TryGetValue(i.Offset + i.Size, out var next) && next.SkipSize != next.Size)
                t.Add(i.Offset + i.Size + next.SkipSize); // a mis-skip lands inside or after the next instruction
        }
        return t;
    }
}
