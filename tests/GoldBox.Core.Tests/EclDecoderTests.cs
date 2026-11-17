using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

public class EclTextTests
{
    [Fact]
    public void Unpack_maps_codes_and_drops_code_zero()
    {
        // 4 codes in 3 bytes: 1 ('A'), 0 (nothing), 32 (' '), 63 ('?'), MSB first.
        int bits = (1 << 18) | (0 << 12) | (32 << 6) | 63;
        var packed = new[] { (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
        Assert.Equal("A ?", EclText.Unpack(packed));
    }

    [Fact]
    public void Trailing_partial_group_yields_whole_codes_only()
    {
        // One byte = 8 bits -> one 6-bit code (code 2 = 'B'), the last 2 bits are dropped.
        Assert.Equal("B", EclText.Unpack([2 << 2 | 3]));
    }

    [Theory]
    [InlineData("HELLO, WORLD 123!")]
    [InlineData("[\\]^_ ?:")]
    [InlineData("")]
    public void Pack_round_trips(string text) => Assert.Equal(text, EclText.Unpack(EclText.Pack(text)));

    [Fact]
    public void Pack_rejects_lower_case_and_at_sign()
    {
        Assert.Throws<ArgumentException>(() => EclText.Pack("a"));
        Assert.Throws<ArgumentException>(() => EclText.Pack("@"));
    }
}

public class EclDecoderTests
{
    private static byte[] Block(Action<EclAsm> build)
    {
        var a = new EclAsm();
        build(a);
        return a.Build();
    }

    [Fact]
    public void Operand_sizes_per_type()
    {
        byte[] b = [0, 7, 1, 0x34, 0x12, 2, 0xFF, 0xFF, 3, 0, 0x4A, 0x81, 0, 0x97, 0x80, 2, 0, 0, 0x42, 9];
        int p = 0;
        var sizes = new List<(byte, int)>();
        while (p < b.Length - 1)
        {
            var o = EclDecoder.DecodeOperand(b, p);
            sizes.Add((o.Type, o.Size));
            p += o.Size;
        }
        Assert.Equal([(0, 2), (1, 3), (2, 3), (3, 3), (0x81, 3), (0x80, 4), (0x42, 2)], sizes);
    }

    [Fact]
    public void Operand_fields()
    {
        var mem = EclDecoder.DecodeOperand([1, 0x20, 0x4A], 0);
        Assert.True(mem.IsMemory);
        Assert.Equal((ushort)0x4A20, mem.Word);
        var imm = EclDecoder.DecodeOperand([0, 5], 0);
        Assert.Null(imm.Word);
        Assert.Equal(5, imm.Lo);
        var unknown = EclDecoder.DecodeOperand([0x42, 9], 0);
        Assert.False(unknown.IsKnownType);
    }

    [Fact]
    public void Operand_past_end_throws()
    {
        Assert.Throws<EclDecodeException>(() => EclDecoder.DecodeOperand([1, 0x20], 0));
        Assert.Throws<EclDecodeException>(() => EclDecoder.DecodeOperand([0x80, 5, 1, 2], 0));
    }

    [Fact]
    public void Vectors_are_raw_fields()
    {
        var b = Block(a => a.Label("x").Op(EclOpcodes.Exit).Vector(3, "x"));
        var v = EclDecoder.Vectors(b);
        Assert.All(v, x => Assert.Equal((byte)1, x.Ignored));
        Assert.All(v, x => Assert.Equal((byte)1, x.Operand.Type));
        Assert.Equal((ushort)0x9914, v[3].Operand.Word);
    }

    [Fact]
    public void Fixed_and_variadic_instruction_sizes()
    {
        var b = Block(a => a
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x4A20))                                     // 1 + 2 + 3
            .Op(EclOpcodes.OnGoto, Mem(0x4A20), Imm8(2), L("t"), L("t"))                    // 1 + 3 + 2 + 3 + 3
            .Op(EclOpcodes.HMenu, Mem(0x4A21), Imm8(2), Text("YES"), Text("NO"))
            .Label("t").Op(EclOpcodes.Exit));
        int off = EclLayout.Prefix + 20;
        var save = EclDecoder.DecodeInstruction(b, off);
        Assert.Equal(6, save.Size);
        Assert.Equal(6, save.SkipSize);
        var on = EclDecoder.DecodeInstruction(b, off + 6);
        Assert.Equal(12, on.Size);
        Assert.Equal(4, on.Operands.Count);
        Assert.Equal(1, on.SkipSize); // IF-skip table: 0 operands
        Assert.Equal("target", on.Role(3));
        var menu = EclDecoder.DecodeInstruction(b, off + 18);
        Assert.Equal("NO", menu.Operands[3].Text);
    }

    [Fact]
    public void Skip_table_is_short_for_six_opcodes()
    {
        Assert.Equal(2, EclOpcodes.SkipCount(EclOpcodes.SetupMon));
        Assert.Equal(1, EclOpcodes.SkipCount(EclOpcodes.AddNpc));
        foreach (var op in new[] { EclOpcodes.VMenu, EclOpcodes.OnGoto, EclOpcodes.OnGosub, EclOpcodes.HMenu })
            Assert.Equal(0, EclOpcodes.SkipCount(op));
        Assert.Equal(2, EclOpcodes.SkipCount(EclOpcodes.Undefined1F));
        Assert.Equal(0, EclOpcodes.SkipCount(0x3E));
        for (int op = 0; op < 0x3E; op++)
        {
            var info = EclOpcodes.Get((byte)op);
            if (info is null || op is 0x0C or 0x36 or 0x15 or 0x25 or 0x26 or 0x2B)
                continue;
            Assert.Equal(info.FixedCount, info.SkipCount);
        }
    }

    [Fact]
    public void Table_covers_00_to_3D_except_1F()
    {
        for (int op = 0; op < 256; op++)
            Assert.Equal(op <= 0x3D && op != 0x1F, EclOpcodes.IsHandled((byte)op));
    }

    [Fact]
    public void Variadic_count_must_be_immediate_for_static_decode()
    {
        var b = Block(a => a.Op(EclOpcodes.OnGoto, Imm8(0), Mem(0x4A20)));
        Assert.Throws<EclDecodeException>(() => EclDecoder.DecodeInstruction(b, EclLayout.Prefix + 20));
    }

    [Fact]
    public void Analysis_follows_hard_paths_and_finds_hazards()
    {
        var b = Block(a => a
            .Label("s")
            .Op(EclOpcodes.Compare, Mem(0x4A20), Imm8(0))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.SetupMon, Imm8(1), Imm8(0), Imm8(2)) // mis-skipped
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.Exit)
            .Label("sub").Op(EclOpcodes.Return));
        var an = new EclAnalysis(b);
        Assert.Empty(an.Errors);
        Assert.Empty(an.Overlaps);
        Assert.Single(an.Hazards);
        Assert.Equal(6, an.Instructions.Count);
    }

    [Fact]
    public void Analysis_reports_overlap_unhandled_and_target_outside()
    {
        var b = Block(a => a
            .Op(EclOpcodes.Goto, Mem(0x9916))); // lands inside its own operand (0x16 = IFEQ, then 0x99: no handler)
        var an = new EclAnalysis(b);
        Assert.NotEmpty(an.Overlaps);
        Assert.NotEmpty(an.Unhandled);

        var outside = new EclAnalysis(Block(a => a.Op(EclOpcodes.Goto, Mem(0x1234))));
        Assert.Contains(outside.HardErrors, e => e.Message == "target outside script");
    }
}
