namespace GoldBox.Core.Ecl;

/// <summary>
/// Static flow analysis of one script block, the same as <c>tools/scripts/ecl.py</c> <c>Analysis</c>: pass 1 follows
/// hard edges from the five vectors; pass 2 adds speculative edges. The instruction after a GOSUB or ON GOSUB is hard
/// (the game resumes there when the routine returns, and ON GOSUB falls through when the selector is out of range);
/// the ON GOTO fall-through and the IF-skip past a mis-skipped instruction are speculative. A speculative path stops where it reaches a byte some
/// script operand addresses (a table); pass 2 repeats until that set is stable.
/// </summary>
public sealed class EclAnalysis
{
    public enum Reach { Hard, Speculative }

    public sealed record Error(Reach Reach, int Offset, int Source, string Edge, string Message);

    private const int Terminator = -1;

    public byte[] Block { get; }
    public IReadOnlyDictionary<int, EclInstruction> Instructions { get; }
    public IReadOnlyDictionary<int, Reach> Reached { get; }
    public IReadOnlyList<Error> Errors { get; }
    /// <summary>(IF offset, next offset, next opcode) where the next instruction is mis-skipped by the IF-skip table.</summary>
    public IReadOnlyList<(int If, int Next, byte Op)> Hazards { get; }
    /// <summary>(instruction offset, operand index) of jump targets given as a type-0 operand.</summary>
    public IReadOnlyList<(int Offset, int Operand)> UndefinedTargets { get; }
    /// <summary>Reached opcodes with no handler.</summary>
    public IReadOnlyList<(int Offset, Reach Reach)> Unhandled { get; }
    /// <summary>(previous instruction, overlapping instruction) offsets.</summary>
    public IReadOnlyList<(int Previous, int Offset)> Overlaps { get; }
    public IReadOnlyList<int> SpeculativeIntoData { get; }
    public IReadOnlyCollection<int> DataRefs { get; }
    public int[] VectorOffsets { get; }

    public IEnumerable<Error> HardErrors => Errors.Where(e => e.Reach == Reach.Hard);

    public EclAnalysis(byte[] block)
    {
        Block = block;
        var roots = EclDecoder.Vectors(block)
            .Select(v => (Off: EclLayout.AddressToOffset(v.Operand.Word ?? 0), Edge: "vector", Src: Terminator))
            .ToList();
        VectorOffsets = roots.Select(r => r.Off).ToArray();
        var hard = Walk(roots, false, [], null);
        var stop = new HashSet<int>(hard.Refs);
        Pass full = hard;
        for (int i = 0; i < 20; i++)
        {
            full = Walk(roots, true, stop, hard.Insns);
            var next = new HashSet<int>(stop);
            next.UnionWith(full.Refs);
            if (next.SetEquals(stop))
                break;
            stop = next;
        }
        Instructions = full.Insns;
        Reached = full.Insns.Keys.ToDictionary(o => o, o => hard.Insns.ContainsKey(o) ? Reach.Hard : Reach.Speculative);
        var hardKeys = hard.Errors.Select(e => (e.Offset, e.Message)).ToHashSet();
        Errors = hard.Errors.Select(e => e with { Reach = Reach.Hard })
            .Concat(full.Errors.Where(e => !hardKeys.Contains((e.Offset, e.Message))).Select(e => e with { Reach = Reach.Speculative }))
            .ToList();
        Hazards = full.Hazards;
        UndefinedTargets = full.Undefined;
        Unhandled = full.Unhandled.Select(o => (o, Reached[o])).ToList();
        SpeculativeIntoData = full.IntoData;
        DataRefs = full.Refs;
        var overlaps = new List<(int, int)>();
        int lastEnd = -1, last = -1;
        foreach (var off in full.Insns.Keys.Order())
        {
            if (off < lastEnd)
                overlaps.Add((last, off));
            int end = off + full.Insns[off].Size;
            if (end > lastEnd)
            {
                lastEnd = end;
                last = off;
            }
        }
        Overlaps = overlaps;
    }

    /// <summary>
    /// Script bytes (block minus the 2-byte prefix) by reach, as <c>ecl.py check</c> counts them: bytes of hard
    /// instructions plus the 20-byte vector table, bytes of instructions reached only on speculative paths, the rest.
    /// </summary>
    public (int Hard, int Speculative, int Unreached) ByteCoverage()
    {
        var cov = new byte[Block.Length];
        foreach (var (off, ins) in Instructions)
            for (int k = off; k < off + ins.Size && k < cov.Length; k++)
                cov[k] = Reached[off] == Reach.Hard ? (byte)1 : (byte)2;
        for (int k = EclLayout.Prefix; k < EclLayout.Prefix + 4 * EclLayout.VectorCount; k++)
            cov[k] = 1;
        int hard = 0, spec = 0, none = 0;
        for (int k = EclLayout.Prefix; k < cov.Length; k++)
        {
            if (cov[k] == 1) hard++;
            else if (cov[k] == 2) spec++;
            else none++;
        }
        return (hard, spec, none);
    }

    private sealed class Pass
    {
        public Dictionary<int, EclInstruction> Insns { get; } = [];
        public HashSet<int> Refs { get; } = [];
        public List<Error> Errors { get; } = [];
        public List<(int, int, byte)> Hazards { get; } = [];
        public List<(int, int)> Undefined { get; } = [];
        public List<int> Unhandled { get; } = [];
        public List<int> IntoData { get; } = [];
    }

    private static readonly HashSet<byte> Terminators = [EclOpcodes.Exit, EclOpcodes.Goto, EclOpcodes.Return, EclOpcodes.NewEcl];

    private IEnumerable<(int Off, string Edge, bool Hard)> Edges(EclInstruction ins, Pass st)
    {
        var outs = new List<(int, string, bool)>();
        for (int i = 0; i < ins.Operands.Count; i++)
        {
            if (ins.Role(i) != "target")
                continue;
            var o = ins.Operands[i];
            if (o.Type == 0)
            {
                st.Undefined.Add((ins.Offset, i));
                continue;
            }
            string kind = ins.Op switch
            {
                EclOpcodes.Goto => "goto",
                EclOpcodes.Gosub => "gosub",
                EclOpcodes.OnGoto => "on-goto",
                _ => "on-gosub",
            };
            outs.Add((EclLayout.AddressToOffset(o.Word ?? 0), kind, true));
        }
        int next = ins.Offset + ins.Size;
        if (EclOpcodes.IsIf(ins.Op))
        {
            outs.Add((next, "if-true", true));
            EclInstruction? n = null;
            try { n = EclDecoder.DecodeInstruction(Block, next); }
            catch (EclDecodeException) { }
            if (n is not null && n.Handled)
            {
                if (n.SkipSize == n.Size)
                    outs.Add((next + n.Size, "if-skip", true));
                else
                {
                    st.Hazards.Add((ins.Offset, next, n.Op));
                    outs.Add((next + n.Size, "if-skip-intended", false));
                }
            }
            return outs;
        }
        if (Terminators.Contains(ins.Op))
            return outs;
        if (ins.Op == EclOpcodes.Gosub)
            outs.Add((next, "after-gosub", true));
        else if (ins.Op == EclOpcodes.OnGosub)
            outs.Add((next, "after-on-gosub", true));
        else if (ins.Op == EclOpcodes.OnGoto)
            outs.Add((next, "on-fallthrough", false));
        else
            outs.Add((next, "fall", true));
        return outs;
    }

    private Pass Walk(List<(int Off, string Edge, int Src)> roots, bool spec, HashSet<int> stop, Dictionary<int, EclInstruction>? hardInsns)
    {
        var st = new Pass();
        var work = new Stack<(int Off, string Edge, int Src)>(roots);
        var seen = new HashSet<int>();
        int n = Block.Length;
        int firstCode = EclLayout.Prefix + 4 * EclLayout.VectorCount;
        while (work.Count > 0)
        {
            var (off, edge, src) = work.Pop();
            if (!seen.Add(off))
                continue;
            if (!(firstCode <= off && off < n))
            {
                st.Errors.Add(new Error(Reach.Hard, off, src, edge, "target outside script"));
                continue;
            }
            EclInstruction ins;
            try { ins = EclDecoder.DecodeInstruction(Block, off); }
            catch (EclDecodeException e)
            {
                st.Errors.Add(new Error(Reach.Hard, off, src, edge, e.Message));
                continue;
            }
            if (spec && hardInsns is not null && !hardInsns.ContainsKey(off)
                && Enumerable.Range(off, ins.Size).Any(stop.Contains))
            {
                st.IntoData.Add(off);
                continue;
            }
            st.Insns[off] = ins;
            if (!ins.Handled)
            {
                st.Unhandled.Add(off);
                continue;
            }
            for (int i = 0; i < ins.Operands.Count; i++)
            {
                var o = ins.Operands[i];
                if (o.IsMemory && ins.Role(i) != "target" && o.Word is { } w && w >= EclLayout.Base && w <= EclLayout.End)
                    st.Refs.Add(EclLayout.AddressToOffset(w));
            }
            foreach (var (t, kind, isHard) in Edges(ins, st))
                if (isHard || spec)
                    work.Push((t, kind, off));
        }
        return st;
    }
}
