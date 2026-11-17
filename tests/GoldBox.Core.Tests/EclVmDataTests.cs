using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>SAVE, arithmetic, logic, tables and the memory model.</summary>
public class EclVmDataTests
{
    private const int F = 0x4A20;

    private static VmRig RunOps(Action<EclAsm> body, EclVmPolicy? policy = null, Action<EclMemory>? setup = null)
    {
        var asm = new EclAsm();
        body(asm);
        asm.Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm, policy, setup);
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        return rig;
    }

    [Theory]
    [InlineData(0x4900)]
    [InlineData(0x4CFF)]
    [InlineData(0x6E13)]
    [InlineData(0x9700)]
    [InlineData(0x98FF)]
    public void Save_to_word_areas_keeps_16_bits(int addr)
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Imm16(0x1234), Mem(addr)));
        Assert.Equal(0x1234, rig[addr]);
    }

    [Fact]
    public void Save_into_the_script_area_stores_the_low_byte()
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Imm16(0x1234), Mem(0xB000)));
        Assert.Equal(0x34, rig[0xB000]);
    }

    [Fact]
    public void Destination_type_is_ignored()
    {
        // Lee's "16-bit" type 3 and an immediate type 2 both address memory as destinations.
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(4), Mem3(F))
            .Op(EclOpcodes.Save, Imm8(5), Imm16(F + 1)));
        Assert.Equal(4, rig[F]);
        Assert.Equal(5, rig[F + 1]);
    }

    [Fact]
    public void Types_1_and_3_read_the_same_way()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm16(300), Mem(F))
            .Op(EclOpcodes.Save, Mem3(F), Mem(F + 1))
            .Op(EclOpcodes.Save, Mem(F), Mem(F + 2)));
        Assert.Equal(300, rig[F + 1]);
        Assert.Equal(300, rig[F + 2]);
    }

    [Fact]
    public void Save_of_text_stores_characters_and_terminator()
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Text("HI"), Mem(0x9700)));
        Assert.Equal('H', rig[0x9700]);
        Assert.Equal('I', rig[0x9701]);
        Assert.Equal(0, rig[0x9702]);
        Assert.Equal("HI", rig.Memory.ReadText(0x9700));
    }

    [Fact]
    public void Text_operand_from_memory_is_printed()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Text("OK"), Mem(0x9700))
            .Op(EclOpcodes.PrintClr, TextAt(0x9700)));
        Assert.Contains("PrintClear OK", rig.Host.Calls);
    }

    [Fact]
    public void Arithmetic_wraps_at_16_bits()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Add, Imm16(0xFFFF), Imm8(2), Mem(F))
            .Op(EclOpcodes.Mul, Imm16(0x100), Imm16(0x101), Mem(F + 1))
            .Op(EclOpcodes.Div, Imm8(17), Imm8(5), Mem(F + 2)));
        Assert.Equal(1, rig[F]);
        Assert.Equal(0x100, rig[F + 1]);
        Assert.Equal(3, rig[F + 2]);
    }

    [Fact]
    public void Sub_is_second_minus_first_and_wraps()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Sub, Imm8(3), Imm8(10), Mem(F))
            .Op(EclOpcodes.Sub, Imm8(10), Imm8(3), Mem(F + 1)));
        Assert.Equal(7, rig[F]);
        Assert.Equal(unchecked((ushort)-7), rig[F + 1]);
    }

    [Fact]
    public void Div_is_unsigned()
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Div, Imm16(0xFFFE), Imm8(2), Mem(F)));
        Assert.Equal(0x7FFF, rig[F]);
    }

    [Fact]
    public void Div_by_zero_is_a_fault()
    {
        var asm = new EclAsm().Op(EclOpcodes.Save, Imm8(9), Mem(F)).Op(EclOpcodes.Div, Imm8(4), Imm8(0), Mem(F)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Fault, r.Reason);
        Assert.Contains("DIV by zero", r.Message);
        Assert.Equal(9, rig[F]);
    }

    [Theory]
    [InlineData(EclOpcodes.And, 0x0C, 0x03, 0x00, "100011")]
    [InlineData(EclOpcodes.Or, 0x0C, 0x03, 0x0F, "011010")]
    [InlineData(EclOpcodes.And, 0x1FF, 0x1F0, 0xF0, "011010")] // low byte only
    public void And_or_store_the_low_byte_and_set_flags_from_zero_against_the_result(byte op, int v1, int v2, int r, string flags)
    {
        var rig = RunOps(a => a.Op(op, Imm16(v1), Imm16(v2), Mem(F)));
        Assert.Equal(r, rig[F]);
        Assert.Equal(flags, JsonlTraceWriter.FlagString(rig.Vm.Flags));
    }

    [Fact]
    public void And_or_store_and_set_equality_flags()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.And, Imm8(0x0C), Imm8(0x03), Mem(F))
            .Op(EclOpcodes.IfEq).Op(EclOpcodes.Save, Imm8(1), Mem(F + 2))
            .Op(EclOpcodes.Or, Imm8(0x0C), Imm8(0x03), Mem(F + 1))
            .Op(EclOpcodes.IfNe).Op(EclOpcodes.Save, Imm8(1), Mem(F + 3)));
        Assert.Equal(0, rig[F]);
        Assert.Equal(0x0F, rig[F + 1]);
        Assert.Equal(1, rig[F + 2]);
        Assert.Equal(1, rig[F + 3]);
    }

    [Theory]
    [InlineData(1, 1, 2, 2, true)]
    [InlineData(1, 1, 2, 3, false)]
    [InlineData(1, 0, 2, 2, false)]
    public void CmpAnd_sets_equal_when_both_pairs_match(int a1, int b1, int a2, int b2, bool equal)
    {
        // A COMPARE first sets ≤ and ≥; CMPAND clears all six before setting = or ≠.
        var rig = RunOps(a => a.Op(EclOpcodes.Compare, Imm8(1), Imm8(1)).Op(EclOpcodes.CmpAnd, Imm8(a1), Imm8(b1), Imm8(a2), Imm8(b2)));
        Assert.Equal(equal ? "100000" : "010000", JsonlTraceWriter.FlagString(rig.Vm.Flags));
    }

    [Fact]
    public void GetTable_uses_the_low_byte_of_the_index_SaveTable_the_full_word()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(42), Mem(0x9702))
            .Op(EclOpcodes.GetTable, Imm16(0x9700), Imm16(0x0102), Mem(F))       // index 0x102 → 2
            .Op(EclOpcodes.SaveTable, Imm8(7), Imm16(0x9600), Imm16(0x0105)));   // 0x9600 + 0x105 = 0x9705
        Assert.Equal(42, rig[F]);
        Assert.Equal(7, rig[0x9705]);
    }

    [Fact]
    public void GetTable_and_SaveTable_index_from_a_base_address()
    {
        var asm = new EclAsm();
        asm.Op(EclOpcodes.GetTable, L("tab"), Imm8(2), Mem(F))
            .Op(EclOpcodes.SaveTable, Imm8(77), L("tab"), Imm8(1))
            .Op(EclOpcodes.GetTable, L("tab"), Imm8(1), Mem(F + 1))
            .Op(EclOpcodes.Exit)
            .Label("tab").Bytes(10, 20, 30);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(30, rig[F]);
        Assert.Equal(77, rig[F + 1]);
    }

    [Fact]
    public void PSurprise_without_a_party_zeroes_two_cells()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(3), Mem(F))
            .Op(EclOpcodes.Save, Imm8(4), Mem(F + 1))
            .Op(EclOpcodes.PSurprise, Mem(F), Mem(F + 1)));
        Assert.Equal(0, rig[F]);
        Assert.Equal(0, rig[F + 1]);
    }

    [Fact]
    public void Script_init_sets_the_program_cells()
    {
        var block = new EclAsm().Op(EclOpcodes.Exit).Build();
        var mem = new EclMemory();
        foreach (var a in new ushort[] { 0x4A00, 0x4A1F, 0x4A20, 0x6E78, 0x6E79, 0x6E82, 0x6E83, 0x6DD2, 0x6DD3, 0x49E5 })
            mem.Write(a, 9);
        var vm = new EclVm(mem, new ScriptedEclHost());
        vm.LoadScriptBlock(block, 1, 1);
        Assert.Equal(0, mem.Read(0x4A00));
        Assert.Equal(0, mem.Read(0x4A1F));
        Assert.Equal(9, mem.Read(0x4A20));
        Assert.Equal(9, mem.Read(0x6E78));
        Assert.Equal(0, mem.Read(0x6E79));
        Assert.Equal(0, mem.Read(0x6E82));
        Assert.Equal(9, mem.Read(0x6E83));
        Assert.Equal(255, mem.Read(0x6DE1));
        Assert.Equal(0, mem.Read(0x6DD2));
        Assert.Equal(0, mem.Read(0x6DD3));
        Assert.Equal(0, mem.Read(0x49E5));
        Assert.Equal(1, mem.Read(0x49E6));
    }

    [Fact]
    public void Loading_a_save_keeps_4A00_and_6E79_once()
    {
        var block = new EclAsm().Op(EclOpcodes.Exit).Build();
        var mem = new EclMemory();
        mem.Write(0x4A00, 1);
        mem.Write(0x6E80, 2);
        var vm = new EclVm(mem, new ScriptedEclHost());
        vm.LoadScriptBlock(block, 1, 1, loadingSave: true);
        Assert.Equal(1, mem.Read(0x4A00));
        Assert.Equal(2, mem.Read(0x6E80));
        Assert.False(vm.LoadingSave); // DS:49F8 cleared by the init
        vm.LoadScriptBlock(block, 1, 1);
        Assert.Equal(0, mem.Read(0x4A00));
        Assert.Equal(0, mem.Read(0x6E80));
    }

    [Fact]
    public void Script_load_clears_stack_and_flags()
    {
        var asm = new EclAsm().Op(EclOpcodes.Compare, Imm8(1), Imm8(1)).Op(EclOpcodes.Gosub, L("s")).Label("s").Op(EclOpcodes.Goto, L("s"));
        var rig = VmRig.Of(asm);
        rig.Run(maxSteps: 3);
        Assert.Equal(1, rig.Vm.StackDepth);
        rig.Vm.LoadScriptBlock(asm.Build(), 1, 1);
        Assert.Equal(0, rig.Vm.StackDepth);
        Assert.All(rig.Vm.Flags, f => Assert.False(f));
    }
}

public class EclMemoryTests
{
    [Fact]
    public void Position_cells()
    {
        var m = new EclMemory();
        m.Position.X = 3;
        m.Position.Y = 0xFF; // -1
        m.Position.Facing = 6;
        m.Position.WallAhead = 9;
        m.Position.EventByte = 0x81;
        Assert.Equal(3, m.Read(0xC04B));
        Assert.Equal(0xFFFF, m.Read(0xC04C)); // y is signed
        Assert.Equal(3, m.Read(0xC04D));      // facing / 2
        Assert.Equal(9, m.Read(0xC04E));
        Assert.Equal(0x81, m.Read(0xC04F));
    }

    [Fact]
    public void Facing_write_takes_the_value_mod_4()
    {
        var m = new EclMemory();
        m.Write(0xC04D, 2);
        Assert.Equal(4, m.Position.Facing);
        m.Write(0xC04D, 7);
        Assert.Equal(6, m.Position.Facing);
        m.Write(0xC04D, 0x101);
        Assert.Equal(2, m.Position.Facing);
    }

    [Fact]
    public void Unlisted_special_read_is_undefined_and_logged()
    {
        var m = new EclMemory(new EclVmPolicy { UndefinedValue = 0xBEEF });
        var diags = new List<string>();
        m.Diagnostic += diags.Add;
        Assert.Equal(0xBEEF, m.Read(0x1234));
        Assert.Single(diags);
        Assert.Contains("[?]", diags[0]);
    }

    [Fact]
    public void Listed_special_reads_come_from_the_party_position_and_fixed_ds_words()
    {
        var m = new EclMemory(new EclVmPolicy { UndefinedValue = 0xBEEF });
        var diags = new List<string>();
        m.Diagnostic += diags.Add;
        m.Position.Facing = 6;
        m.Position.X = 0xFF;
        Assert.Equal(6, m.Read(0x033D));           // 8-way facing byte DS:6AAF
        Assert.Equal(0xFFFF, m.Read(0xC04B));      // x is sign-extended like y (0x82DF)
        // $00B1/$00FB/$00FC read DS words nothing writes; their writes are no-ops (DOSBox: 0 after writes).
        m.Write(0x00FB, 77);
        m.Write(0x00FC, 78);
        m.Write(0x00B1, 79);
        Assert.Equal(0, m.Read(0x00FB));
        Assert.Equal(0, m.Read(0x00FC));
        Assert.Equal(0, m.Read(0x00B1));
        Assert.Empty(diags);
        // $035F and $C059 have reader cases that set no value: undefined.
        Assert.Equal(0xBEEF, m.Read(0x035F));
        Assert.Equal(0xBEEF, m.Read(0xC059));
        Assert.Equal(2, diags.Count);
    }

    [Fact]
    public void View_colour_writes_keep_the_low_byte()
    {
        var m = new EclMemory();
        m.Write(0xC059, 0x1234);
        m.Write(0xC05F, 8);
        Assert.Equal(0x34, m.SpecialWriteValues[0xC059]);
        Assert.Equal(8, m.SpecialWriteValues[0xC05F]);
    }

    [Fact]
    public void Unlisted_special_write_is_ignored_and_listed_ones_kept()
    {
        var m = new EclMemory();
        var writes = new List<EclMemoryWrite>();
        m.Written += writes.Add;
        m.Write(0x1234, 5);
        Assert.Empty(writes);
        m.Write(0x03DE, 1);
        Assert.Equal(1, m.SpecialWriteValues[0x03DE]);
        Assert.Single(writes);
    }

    [Fact]
    public void Sky_and_ceiling_writes_are_signalled()
    {
        var m = new EclMemory();
        var seen = new List<ushort>();
        m.SkyCeilingWritten += (a, _) => seen.Add(a);
        m.Write(0x49FD, 1);
        m.Write(0x49FE, 2);
        m.Write(0x49FC, 3);
        Assert.Equal([0x49FD, 0x49FE], seen);
    }

    [Fact]
    public void Code_fetch_is_limited_to_the_script_area()
    {
        var m = new EclMemory();
        m.ScriptArea[0] = 0x42;
        m.ScriptArea[EclLayout.AreaSize - 1] = 0x43;
        Assert.Equal((byte)0x42, m.FetchCode(0x9900));
        Assert.Equal((byte)0x43, m.FetchCode(0xB6FF));
        Assert.Null(m.FetchCode(0xB700));
        Assert.Null(m.FetchCode(0x98FF));
    }
}
