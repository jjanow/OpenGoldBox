using GoldBox.Core.Ecl;

namespace GoldBox.Core.Tests.Support;

/// <summary>One encoded operand for <see cref="EclAsm"/>; a label reference is resolved at build time.</summary>
public sealed class Opnd
{
    public byte Type { get; private init; }
    public byte[] Payload { get; private init; } = [];
    public string? Label { get; private init; }

    public int Size => 1 + (Label is not null ? 2 : Payload.Length);

    public static Opnd Imm8(int v) => new() { Type = 0, Payload = [(byte)v] };
    public static Opnd Imm16(int v) => new() { Type = 2, Payload = [(byte)v, (byte)(v >> 8)] };
    public static Opnd Mem(int addr) => new() { Type = 1, Payload = [(byte)addr, (byte)(addr >> 8)] };
    public static Opnd Mem3(int addr) => new() { Type = 3, Payload = [(byte)addr, (byte)(addr >> 8)] };
    public static Opnd TextAt(int addr) => new() { Type = 0x81, Payload = [(byte)addr, (byte)(addr >> 8)] };
    public static Opnd Text(string s)
    {
        var p = EclText.Pack(s);
        return new() { Type = 0x80, Payload = [(byte)p.Length, .. p] };
    }
    public static Opnd Raw(byte type, params byte[] payload) => new() { Type = type, Payload = payload };
    /// <summary>A jump target / address of a label, as a type-1 operand.</summary>
    public static Opnd L(string label) => new() { Type = 1, Label = label };
}

/// <summary>
/// Tiny assembler for synthetic test scripts: emits a full DAX-style block (prefix u16 0x1388, five vectors, code
/// from $9914) with labels. Used instead of hex literals so tests stay readable.
/// </summary>
public sealed class EclAsm
{
    private readonly List<byte> _code = [];
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int Pos, string Label)> _fixups = [];
    private readonly string?[] _vectors = new string?[EclLayout.VectorCount];

    public const ushort CodeStart = 0x9914;

    /// <summary>VM address of the next byte.</summary>
    public ushort Here => (ushort)(CodeStart + _code.Count);

    public EclAsm Label(string name)
    {
        _labels[name] = Here;
        return this;
    }

    /// <summary>Vector <paramref name="k"/> points at <paramref name="label"/> (unset vectors point at the code start).</summary>
    public EclAsm Vector(int k, string label)
    {
        _vectors[k] = label;
        return this;
    }

    public EclAsm Op(byte op, params Opnd[] operands)
    {
        _code.Add(op);
        foreach (var o in operands)
        {
            _code.Add(o.Type);
            if (o.Label is not null)
            {
                _fixups.Add((_code.Count, o.Label));
                _code.Add(0);
                _code.Add(0);
            }
            else
            {
                _code.AddRange(o.Payload);
            }
        }
        return this;
    }

    public EclAsm Bytes(params byte[] raw)
    {
        _code.AddRange(raw);
        return this;
    }

    public ushort AddressOf(string label) => (ushort)_labels[label];

    public byte[] Build()
    {
        var code = _code.ToArray();
        foreach (var (pos, label) in _fixups)
        {
            int a = _labels[label];
            code[pos] = (byte)a;
            code[pos + 1] = (byte)(a >> 8);
        }
        var block = new List<byte> { 0x88, 0x13 };
        for (int k = 0; k < EclLayout.VectorCount; k++)
        {
            int a = _vectors[k] is { } l ? _labels[l] : CodeStart;
            block.AddRange([0x01, 0x01, (byte)a, (byte)(a >> 8)]);
        }
        block.AddRange(code);
        return block.ToArray();
    }
}

/// <summary>A VM with a scripted host and a list trace, loaded with an assembled block.</summary>
public sealed class VmRig
{
    public EclMemory Memory { get; }
    public EclVm Vm { get; }
    public ScriptedEclHost Host { get; } = new();
    public ListTraceSink Trace { get; } = new();

    public VmRig(byte[] block, EclVmPolicy? policy = null, Action<EclMemory>? setup = null)
    {
        Memory = new EclMemory(policy);
        setup?.Invoke(Memory);
        Vm = new EclVm(Memory, Host) { Trace = Trace };
        Vm.LoadScriptBlock(block, 1, 1);
    }

    public static VmRig Of(EclAsm asm, EclVmPolicy? policy = null, Action<EclMemory>? setup = null) => new(asm.Build(), policy, setup);

    public EclRunResult Run(int vector = 0, int maxSteps = 1000) => Vm.RunVector(vector, maxSteps);

    public ushort this[int address] => Memory.Read((ushort)address);

    public IEnumerable<string> AllDiagnostics => Trace.Steps.SelectMany(s => s.Diagnostics).Concat(Vm.Diagnostics);
}
