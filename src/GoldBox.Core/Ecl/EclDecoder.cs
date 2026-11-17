namespace GoldBox.Core.Ecl;

/// <summary>Constants of the script block layout (docs/re/ecl-format.md "Block layout").</summary>
public static class EclLayout
{
    /// <summary>VM address of block byte 2 (the first loaded byte).</summary>
    public const ushort Base = 0x9900;
    /// <summary>Block bytes in front of the script that are not loaded (u16 0x1388).</summary>
    public const int Prefix = 2;
    /// <summary>Size of the script area $9900–$B6FF.</summary>
    public const int AreaSize = 7680;
    /// <summary>Last script address.</summary>
    public const ushort End = 0xB6FF;
    /// <summary>Number of entry vectors.</summary>
    public const int VectorCount = 5;
    /// <summary>Lee's names of the five vectors (move, search, pre-camp, camp interrupted, init).</summary>
    public static readonly string[] VectorNames = ["move", "search", "precamp", "campint", "init"];

    public const int VectorInit = 4;

    public static int AddressToOffset(int addr) => addr - Base + Prefix;
    public static ushort OffsetToAddress(int off) => (ushort)(Base + off - Prefix);
}

/// <summary>A statically decoded operand (block-relative; see <see cref="EclDecoder"/>).</summary>
public sealed class EclOperand
{
    public byte Type { get; init; }
    public byte Lo { get; init; }
    /// <summary>High byte; null for types that carry one byte (0, unknown types) and for text.</summary>
    public byte? Hi { get; init; }
    /// <summary>Packed text bytes (type 0x80).</summary>
    public byte[]? Data { get; init; }
    /// <summary>Encoded size including the type byte.</summary>
    public int Size { get; init; }

    public ushort? Word => Hi is null ? null : (ushort)(Lo | (Hi.Value << 8));
    public bool IsMemory => Type is 1 or 3;
    public bool IsKnownType => Type is 0 or 1 or 2 or 3 or 0x80 or 0x81;
    public string? Text => Type == 0x80 && Data is not null ? EclText.Unpack(Data) : null;
}

/// <summary>A statically decoded instruction.</summary>
public sealed class EclInstruction
{
    public int Offset { get; init; }
    public byte Op { get; init; }
    public List<EclOperand> Operands { get; } = [];
    public int Size { get; set; } = 1;
    /// <summary>Bytes the IF-skip consumes for this instruction (opcode + SKIP-count operands).</summary>
    public int SkipSize { get; set; } = 1;
    public ushort Address => EclLayout.OffsetToAddress(Offset);
    public EclOpcodeInfo? Info => EclOpcodes.Get(Op);
    public bool Handled => Info is not null;
    public string Role(int i) => Info?.Role(i) ?? "?";
}

public sealed class EclDecodeException(string message) : Exception(message);

/// <summary>
/// Static decoder over a whole DAX block (prefix included), the same rules as <c>tools/scripts/ecl.py</c>. Used for
/// the flow analysis and disassembly; the VM decodes at run time from its live script area instead
/// (<see cref="EclVm"/>), because scripts may write into it.
/// </summary>
public static class EclDecoder
{
    public static EclOperand DecodeOperand(ReadOnlySpan<byte> b, int p)
    {
        if (p + 1 >= b.Length)
            throw new EclDecodeException("operand runs past end of block");
        byte t = b[p];
        if (t is 1 or 2 or 3 or 0x81)
        {
            if (p + 2 >= b.Length)
                throw new EclDecodeException("operand runs past end of block");
            return new EclOperand { Type = t, Lo = b[p + 1], Hi = b[p + 2], Size = 3 };
        }
        if (t == 0x80)
        {
            int n = b[p + 1];
            if (p + 2 + n > b.Length)
                throw new EclDecodeException("string runs past end of block");
            return new EclOperand { Type = t, Lo = (byte)n, Data = b.Slice(p + 2, n).ToArray(), Size = 2 + n };
        }
        // Type 0, or an unknown type: the loader takes one value byte.
        return new EclOperand { Type = t, Lo = b[p + 1], Size = 2 };
    }

    /// <summary>Decodes one instruction at block offset <paramref name="off"/>.</summary>
    public static EclInstruction DecodeInstruction(ReadOnlySpan<byte> b, int off)
    {
        if (off >= b.Length)
            throw new EclDecodeException("past end of block");
        var ins = new EclInstruction { Offset = off, Op = b[off] };
        var info = ins.Info;
        if (info is null)
            return ins; // no handler: size 1
        int p = off + 1;
        var sizes = new List<int>();
        for (int i = 0; i < info.FixedCount; i++)
        {
            var o = DecodeOperand(b, p);
            ins.Operands.Add(o);
            sizes.Add(o.Size);
            p += o.Size;
        }
        if (info.Variadic != EclVariadic.None)
        {
            var cnt = ins.Operands[info.CountOperandIndex];
            if (cnt.Type != 0)
                throw new EclDecodeException($"variadic count operand has type {cnt.Type}");
            for (int i = 0; i < cnt.Lo; i++)
            {
                var o = DecodeOperand(b, p);
                ins.Operands.Add(o);
                sizes.Add(o.Size);
                p += o.Size;
            }
        }
        ins.Size = p - off;
        ins.SkipSize = 1 + sizes.Take(info.SkipCount).Sum();
        return ins;
    }

    /// <summary>The five entry vectors: (ignored byte, operand).</summary>
    public static (byte Ignored, EclOperand Operand)[] Vectors(ReadOnlySpan<byte> b)
    {
        var v = new (byte, EclOperand)[EclLayout.VectorCount];
        for (int k = 0; k < EclLayout.VectorCount; k++)
        {
            int p = EclLayout.Prefix + 4 * k;
            v[k] = (b[p], DecodeOperand(b, p + 1));
        }
        return v;
    }
}
