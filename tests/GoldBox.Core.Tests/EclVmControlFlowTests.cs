using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

public class EclVmControlFlowTests
{
    private const int F = 0x4A20; // a persistent script flag cell used as scratch

    [Fact]
    public void Vectors_are_read_on_load()
    {
        var asm = new EclAsm().Op(EclOpcodes.Exit).Label("b").Op(EclOpcodes.Exit).Vector(2, "b");
        var rig = VmRig.Of(asm);
        Assert.Equal((ushort)0x9914, rig.Vm.Vectors[0]);
        Assert.Equal((ushort)0x9915, rig.Vm.Vectors[2]);
    }

    [Fact]
    public void Exit_stops_and_clears_the_stack()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.Save, Imm8(9), Mem(F))
            .Label("sub").Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Exit, r.Reason);
        Assert.Equal(2, r.Steps);
        Assert.Equal(0, rig.Vm.StackDepth);
        Assert.Equal(0, rig[F]);
    }

    [Fact]
    public void Goto_jumps()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Goto, L("t"))
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Label("t").Op(EclOpcodes.Save, Imm8(2), Mem(F + 1))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(0, rig[F]);
        Assert.Equal(2, rig[F + 1]);
    }

    [Fact]
    public void Gosub_and_return()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.Save, Imm8(2), Mem(F + 1))
            .Op(EclOpcodes.Exit)
            .Label("sub").Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.Return);
        var rig = VmRig.Of(asm);
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        Assert.Equal(1, rig[F]);
        Assert.Equal(2, rig[F + 1]);
        Assert.Equal(1, rig.Trace.Steps[1].StackDepth);
    }

    [Fact]
    public void Return_with_empty_stack_falls_through()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Return)
            .Op(EclOpcodes.Save, Imm8(5), Mem(F))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(5, rig[F]);
    }

    [Theory]
    [InlineData(1, 2, "011010")]
    [InlineData(2, 2, "100011")]
    [InlineData(3, 2, "010101")]
    [InlineData(0xFFFF, 1, "010101")] // unsigned
    public void Compare_sets_six_flags_unsigned(int a, int b, string flags)
    {
        var asm = new EclAsm().Op(EclOpcodes.Compare, Imm16(a), Imm16(b)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(flags, JsonlTraceWriter.FlagString(rig.Vm.Flags));
    }

    [Theory]
    [InlineData(EclOpcodes.IfEq, 1, 1, true)]
    [InlineData(EclOpcodes.IfEq, 1, 2, false)]
    [InlineData(EclOpcodes.IfNe, 1, 2, true)]
    [InlineData(EclOpcodes.IfLt, 1, 2, true)]
    [InlineData(EclOpcodes.IfLt, 2, 2, false)]
    [InlineData(EclOpcodes.IfGt, 3, 2, true)]
    [InlineData(EclOpcodes.IfLe, 2, 2, true)]
    [InlineData(EclOpcodes.IfLe, 3, 2, false)]
    [InlineData(EclOpcodes.IfGe, 2, 2, true)]
    [InlineData(EclOpcodes.IfGe, 1, 2, false)]
    public void If_runs_or_skips_the_next_instruction(byte ifOp, int a, int b, bool taken)
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Imm8(a), Imm8(b))
            .Op(ifOp)
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.Save, Imm8(2), Mem(F + 1))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(taken ? 1 : 0, rig[F]);
        Assert.Equal(2, rig[F + 1]);
        Assert.Equal(taken, rig.Trace.Steps[1].IfTaken);
    }

    [Fact]
    public void If_followed_by_if_skips_one_byte()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Imm8(1), Imm8(2))
            .Op(EclOpcodes.IfEq)   // false
            .Op(EclOpcodes.IfNe)   // skipped
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(1, rig[F]);
    }

    [Fact]
    public void If_skip_of_SETUPMON_resumes_inside_its_operands()
    {
        // SETUPMON has 3 operands, the IF-skip table says 2: execution resumes at the third operand's type byte.
        // Here that byte is 0x00 (type 0) = EXIT, so the run ends there without calling the host.
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Imm8(1), Imm8(2))
            .Op(EclOpcodes.IfEq);
        ushort setup = asm.Here;
        asm.Op(EclOpcodes.SetupMon, Imm8(1), Imm8(2), Imm8(3))
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Exit, r.Reason);
        Assert.Equal((ushort)(setup + 5), rig.Trace.Steps[1].NextPc);
        Assert.Equal(EclOpcodes.Exit, rig.Trace.Steps[2].Op);
        Assert.Empty(rig.Host.Calls);
        Assert.Equal(0, rig[F]);
        Assert.Contains(rig.Trace.Steps[1].Diagnostics, d => d.Contains("short operand count"));
    }

    [Fact]
    public void If_skip_of_ONGOTO_skips_only_the_opcode_byte()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Imm8(1), Imm8(2))
            .Op(EclOpcodes.IfEq);
        ushort on = asm.Here;
        asm.Op(EclOpcodes.OnGoto, Imm8(0), Imm8(1), L("t")).Label("t").Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run(maxSteps: 3);
        Assert.Equal((ushort)(on + 1), rig.Trace.Steps[1].NextPc);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 99)] // out of range: fall through
    public void OnGoto_jumps_or_falls_through(int sel, int expect)
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Save, Imm8(sel), Mem(F))
            .Op(EclOpcodes.OnGoto, Mem(F), Imm8(2), L("a"), L("b"))
            .Op(EclOpcodes.Save, Imm8(99), Mem(F + 1)).Op(EclOpcodes.Exit)
            .Label("a").Op(EclOpcodes.Save, Imm8(10), Mem(F + 1)).Op(EclOpcodes.Exit)
            .Label("b").Op(EclOpcodes.Save, Imm8(20), Mem(F + 1)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(expect, rig[F + 1]);
    }

    [Fact]
    public void OnGosub_pushes_return()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.OnGosub, Imm8(1), Imm8(2), L("a"), L("b"))
            .Op(EclOpcodes.Save, Imm8(7), Mem(F + 1)).Op(EclOpcodes.Exit)
            .Label("a").Op(EclOpcodes.Return)
            .Label("b").Op(EclOpcodes.Save, Imm8(20), Mem(F)).Op(EclOpcodes.Return);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(20, rig[F]);
        Assert.Equal(7, rig[F + 1]);
    }

    [Fact]
    public void OnGoto_selector_is_compared_as_a_byte()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.OnGoto, Imm16(0x0101), Imm8(2), L("a"), L("b"))
            .Op(EclOpcodes.Exit)
            .Label("a").Op(EclOpcodes.Exit)
            .Label("b").Op(EclOpcodes.Save, Imm8(1), Mem(F)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(1, rig[F]);
    }

    [Fact]
    public void Variadic_count_from_memory_is_dereferenced()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.OnGoto, Imm8(0), Mem(F), L("a"))
            .Op(EclOpcodes.Exit)
            .Label("a").Op(EclOpcodes.Save, Imm8(3), Mem(F + 1)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(3, rig[F + 1]);
        Assert.Contains(rig.AllDiagnostics, d => d.Contains("variadic count"));
    }

    [Fact]
    public void Type0_jump_target_uses_stale_high_byte_and_is_logged()
    {
        // SAVE leaves hi = 0x4A in slot 2 (from Mem(0x4A20)); GOTO's type-0 operand in slot 1 keeps slot 1's hi,
        // which is 0x99 from the previous instruction's operand 1 (Mem(0x99xx)).
        var asm = new EclAsm()
            .Op(EclOpcodes.Save, Mem(0x9914), Mem(F));
        asm.Op(EclOpcodes.Goto, Imm8(0x30));
        var rig = VmRig.Of(asm);
        var r = rig.Run(maxSteps: 2);
        Assert.Equal((ushort)0x9930, rig.Trace.Steps[1].NextPc);
        Assert.Contains(rig.Trace.Steps[1].Diagnostics, d => d.Contains("left over"));
        Assert.True(r.Reason is EclStopReason.Budget or EclStopReason.Exit);
    }

    [Fact]
    public void Unhandled_opcode_stops_by_default()
    {
        var asm = new EclAsm().Bytes(0x1F);
        var rig = VmRig.Of(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.UnhandledOpcode, r.Reason);
        Assert.Contains(rig.AllDiagnostics, d => d.Contains("no handler"));
    }

    [Fact]
    public void Unhandled_opcode_can_hang_until_budget()
    {
        var asm = new EclAsm().Bytes(0x3E);
        var rig = VmRig.Of(asm, new EclVmPolicy { UnhandledOpcode = UnhandledOpcodeMode.Hang });
        var r = rig.Run(maxSteps: 50);
        Assert.Equal(EclStopReason.Budget, r.Reason);
        Assert.All(rig.Trace.Steps, s => Assert.Equal((ushort)0x9914, s.Pc));
    }

    [Fact]
    public void Pc_outside_the_script_area_faults()
    {
        var asm = new EclAsm().Op(EclOpcodes.Goto, Mem(0x4A00));
        var rig = VmRig.Of(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Fault, r.Reason);
        Assert.Contains("outside the script area", r.Message);
    }

    [Fact]
    public void External_stop_ends_the_run()
    {
        var asm = new EclAsm().Label("l").Op(EclOpcodes.Goto, L("l"));
        var rig = VmRig.Of(asm);
        rig.Vm.ExternalStop = true;
        Assert.Equal(EclStopReason.ExternalStop, rig.Run().Reason);
    }

    [Fact]
    public void Budget_ends_an_endless_loop()
    {
        var asm = new EclAsm().Label("l").Op(EclOpcodes.Goto, L("l"));
        var r = VmRig.Of(asm).Run(maxSteps: 100);
        Assert.Equal(EclStopReason.Budget, r.Reason);
        Assert.Equal(100, r.Steps);
    }

    [Fact]
    public void Script_code_can_be_rewritten_at_run_time()
    {
        // The VM decodes from the live script area: overwrite the next opcode with EXIT before reaching it.
        var asm = new EclAsm();
        asm.Op(EclOpcodes.Save, Imm8(EclOpcodes.Exit), Mem(0));
        ushort patch = asm.Here;
        asm.Op(EclOpcodes.Save, Imm8(1), Mem(F)).Op(EclOpcodes.Exit);
        var block = asm.Build();
        // Fix the first SAVE's destination to the patch address.
        int destLo = EclLayout.AddressToOffset(0x9914) + 3 + 1;
        block[destLo] = (byte)patch;
        block[destLo + 1] = (byte)(patch >> 8);
        var rig = new VmRig(block);
        rig.Run();
        Assert.Equal(0, rig[F]);
        Assert.Equal(2, rig.Trace.Steps.Count);
    }
}
