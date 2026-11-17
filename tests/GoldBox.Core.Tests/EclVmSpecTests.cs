using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>Behaviour docs/re/ecl-format.md settles by program read or DOSBox (one test per item).</summary>
public class EclVmSpecTests
{
    private const int F = 0x4A20;

    private static VmRig RunOps(Action<EclAsm> body, Action<EclMemory>? setup = null, Action<ScriptedEclHost>? host = null)
    {
        var asm = new EclAsm();
        body(asm);
        asm.Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm, null, setup);
        host?.Invoke(rig.Host);
        rig.Run();
        return rig;
    }

    private static EclParty Party(params Action<EclCharacter>[] members)
    {
        var p = new EclParty();
        foreach (var m in members)
        {
            var c = new EclCharacter();
            m(c);
            p.Members.Add(c);
        }
        p.CurrentIndex = 0;
        return p;
    }

    // ---- RANDOM and the generator ------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 6)]
    [InlineData(254, 255)]
    [InlineData(255, 255)]
    [InlineData(0x105, 6)] // low byte of v
    public void Random_asks_for_range_v_plus_1_except_255(int v, int range)
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Random, Imm16(v), Mem(F)), host: h => h.RandomValues.Enqueue(3));
        Assert.Equal($"Random {range}", rig.Host.Calls.Single());
        Assert.Equal(3, rig[F]);
    }

    [Fact]
    public void Turbo_pascal_generator_steps_and_reduces_as_specified()
    {
        var r = new TurboPascalRandom(0);
        Assert.Equal(1u, r.NextSeed());
        Assert.Equal(0x08088406u, r.NextSeed());
        var g = new TurboPascalRandom(0x12345678);
        uint expect = unchecked(0x12345678u * 0x08088405u + 1);
        Assert.Equal((ushort)((expect >> 16) % 14), g.Next(14));
        Assert.Equal(1, g.Draws);
        Assert.Equal(3, TurboPascalRandom.StepsBetween(0, new TurboPascalRandom(0).Also(x => x.Skip(3)).Seed));
    }

    [Fact]
    public void Host_generator_feeds_random()
    {
        var rng = new TurboPascalRandom(777);
        var copy = new TurboPascalRandom(777);
        var rig = RunOps(a => a.Op(EclOpcodes.Random, Imm8(13), Mem(F)), host: h => h.Rng = rng);
        Assert.Equal(copy.Next(14), rig[F]);
        Assert.Equal(copy.Seed, rng.Seed);
    }

    // ---- Text COMPARE and string slots -------------------------------------------------------------------------

    [Theory]
    [InlineData("ABC", "ABC", "100011")]
    [InlineData("AB", "ABC", "011010")]   // shorter is less
    [InlineData("ABD", "ABC", "010101")]
    public void Text_compare_is_bytewise_shorter_is_less(string a, string b, string flags)
    {
        var rig = RunOps(x => x.Op(EclOpcodes.Compare, Text(a), Text(b)));
        Assert.Equal(flags, JsonlTraceWriter.FlagString(rig.Vm.Flags));
    }

    [Fact]
    public void Text_compare_is_case_sensitive()
    {
        // Lower case cannot be packed into a type-0x80 text; it comes from memory (a type-0x81 operand).
        var rig = RunOps(x => x.Op(EclOpcodes.Compare, TextAt(0x9700), Text("ABC")), m => m.WriteText(0x9700, "abc"));
        Assert.Equal("010101", JsonlTraceWriter.FlagString(rig.Vm.Flags)); // 'a' > 'A'
    }

    [Fact]
    public void Text_compare_with_a_number_uses_the_old_slot_2()
    {
        // Slot 2 gets "B" from the first COMPARE; the second COMPARE has one text (slot 1 = "B") and a number.
        var rig = RunOps(a => a
            .Op(EclOpcodes.Compare, Text("A"), Text("B"))
            .Op(EclOpcodes.Compare, Imm8(1), Text("B")));
        Assert.Equal("100011", JsonlTraceWriter.FlagString(rig.Vm.Flags));
        Assert.Equal("B", rig.Vm.StringSlot(1));
        Assert.Equal("B", rig.Vm.StringSlot(2));
    }

    [Fact]
    public void Text_slots_count_text_operands_only()
    {
        // SAVE's text is operand 1 but VMENU's prompt (operand 2) is still the first text: slot 1.
        var rig = RunOps(a => a.Op(EclOpcodes.VMenu, Mem(F), Text("PICK"), Imm8(1), Text("ONE")));
        Assert.Equal("PICK", rig.Vm.StringSlot(1));
        Assert.Equal("ONE", rig.Vm.StringSlot(2));
    }

    [Fact]
    public void Text_operand_as_a_value_reads_memory_at_length_and_old_high_byte()
    {
        // Operand slot 1 keeps hi = 0x97 from the COMPARE; the text "AB" (2 packed bytes) sets lo = 2 → read $9702.
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(55), Mem(0x9702))
            .Op(EclOpcodes.Compare, Mem(0x9700), Imm8(0))
            .Op(EclOpcodes.Add, Text("AB"), Imm8(0), Mem(F)));
        Assert.Equal(55, rig[F]);
    }

    // ---- SAVE of text ------------------------------------------------------------------------------------------

    [Fact]
    public void Text_into_the_script_area_is_bytes()
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Text("HI"), Mem(0xB000)));
        Assert.Equal((byte)'H', rig.Memory.ScriptArea[0xB000 - 0x9900]);
        Assert.Equal((byte)'I', rig.Memory.ScriptArea[0xB001 - 0x9900]);
        Assert.Equal(0, rig.Memory.ScriptArea[0xB002 - 0x9900]);
    }

    [Fact]
    public void Text_to_6B00_sets_the_name_and_6B00_text_reads_it()
    {
        var party = Party(c => c.Name = "OLD");
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Text("ABCDEFGHIJKLMNOPQ"), Mem(0x6B00))
            .Op(EclOpcodes.Print, TextAt(0x6B00)), m => m.Party = party);
        Assert.Equal("ABCDEFGHIJKLMNO", party.Members[0].Name);
        Assert.Contains("Print ABCDEFGHIJKLMNO", rig.Host.Calls);
        Assert.Equal(0, rig.Memory.Cells6B00[0]); // the cell is not used
    }

    [Fact]
    public void Text_into_the_6B00_area_skips_the_side_effects()
    {
        var party = Party(_ => { });
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Text("X"), Mem(0x6B20)), m => m.Party = party);
        Assert.Equal('X', rig[0x6B20]);
        Assert.Equal(0, party.Members[0][0x18]);
        RunOps(a => a.Op(EclOpcodes.Save, Imm8('X'), Mem(0x6B20)), m => m.Party = party);
        Assert.Equal((byte)'X', party.Members[0][0x18]);
    }

    [Fact]
    public void Text_to_a_special_address_writes_nothing()
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Text("X"), Mem(0xC04B)));
        Assert.Equal(0, rig.Memory.Position.X);
    }

    // ---- Input -------------------------------------------------------------------------------------------------

    [Fact]
    public void Input_rules()
    {
        var rig = RunOps(a => a
                .Op(EclOpcodes.InputStr, Mem(0x9000), Mem(0x9700))
                .Op(EclOpcodes.InputStr, Imm8(0), Mem(0x9780))
                .Op(EclOpcodes.InputNum, Mem(0x9000), Mem(F)),
            host: h =>
            {
                h.Strings.Enqueue("hello~ world " + new string('x', 50));
                h.Strings.Enqueue("");
                h.Numbers.Enqueue(65536);
            });
        Assert.Equal(("HELLO WORLD " + new string('X', 28)), rig.Memory.ReadText(0x9700));
        Assert.Equal(" ", rig.Memory.ReadText(0x9780));
        Assert.Equal(0, rig[F]); // low 16 bits of 65536
        // The length operand is never fetched: no read of $9000 in the trace.
        Assert.DoesNotContain(rig.Trace.Steps.SelectMany(s => s.Reads), r => r.Address == 0x9000);
    }

    // ---- ON, IF-skip, PROGRAM ----------------------------------------------------------------------------------

    [Fact]
    public void On_goto_selector_is_the_low_byte()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.OnGoto, Imm16(0x101), Imm8(2), L("a"), L("b"))
            .Op(EclOpcodes.Exit)
            .Label("a").Op(EclOpcodes.Save, Imm8(1), Mem(F)).Op(EclOpcodes.Exit)
            .Label("b").Op(EclOpcodes.Save, Imm8(2), Mem(F)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(2, rig[F]);
    }

    [Fact]
    public void A_skipped_instruction_becomes_the_previous_opcode()
    {
        // DOSBox: after IFNE skipped a GOTO, the EXIT that followed had previous opcode 01.
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Imm8(1), Imm8(1))
            .Op(EclOpcodes.IfNe)
            .Op(EclOpcodes.Goto, L("x"))
            .Label("x").Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Equal(EclOpcodes.Exit, rig.Vm.Opcode);
        Assert.Equal(EclOpcodes.Goto, rig.Vm.PreviousOpcode);
    }

    // ---- Disk byte, NEWECL -------------------------------------------------------------------------------------

    [Fact]
    public void Disk_byte_is_6E12()
    {
        var m = new EclMemory { Disk = 2 };
        Assert.Equal(2, m.Read(0x6E12));
        m.Write(0x6E12, 0x105);
        Assert.Equal(5, m.Disk);
        Assert.Equal(2, m.PreviousDisk);
        Assert.Equal(5, m.Read(0x6E12));
        Assert.Equal(0x105, m.Cells6B00[0x312]); // the cell is stored too
    }

    private sealed class OneScript(int disk, int script, byte[] block) : IEclScriptSource
    {
        public byte[]? LoadBlock(int d, int s) => d == disk && s == script ? block : null;
    }

    [Fact]
    public void NewEcl_records_the_old_script_and_initialises_the_new_one_at_once()
    {
        var second = new EclAsm().Op(EclOpcodes.Exit).Build();
        var asm = new EclAsm()
            .Op(EclOpcodes.Save, Imm8(4), Mem(0x6E12))
            .Op(EclOpcodes.Save, Imm8(7), Mem(0x4A00))
            .Op(EclOpcodes.NewEcl, Imm8(9));
        var rig = VmRig.Of(asm);
        rig.Vm.Scripts = new OneScript(4, 9, second);
        var r = rig.Run();
        Assert.Equal(EclStopReason.NewScript, r.Reason);
        Assert.Equal(1, rig[0x49F2]);            // the old script id
        Assert.Equal(9, rig.Vm.CurrentScript);   // loaded during the NEWECL step
        Assert.Equal(4, rig.Vm.CurrentDisk);
        Assert.Equal(0, rig[0x4A00]);            // init ran
        Assert.Equal(EclStopReason.Exit, rig.Vm.FollowScriptChanges().Reason);
    }

    // ---- $6B00-area redirection --------------------------------------------------------------------------------

    [Fact]
    public void Record_reads_and_side_effects()
    {
        var party = Party(c =>
        {
            c[0x14] = 17; c[0x6E] = 9; c[0x73] = 0xFF; c[0x77] = 45; c[0x84] = 0x80; c.SetWord(0x88, 1234);
            c[0xA0] = 8; c.SetWord(0xB8, 300); c[0xBA] = 7; c[0x10D] = 1; c[0x10E] = 1; c[0x10F] = 1; c[0x11C] = 12;
            c[0x15] = 16;
        }, _ => { });
        party.CurrentIndex = 1;
        party.CurrentIndex = 0;
        var m = new EclMemory { Party = party };
        Assert.Equal(17, m.Read(0x6B18));
        Assert.Equal(9, m.Read(0x6B9B));
        Assert.Equal(0xFFFF, m.Read(0x6BA0)); // signed
        Assert.Equal(45, m.Read(0x6BA5));
        Assert.Equal(1234, m.Read(0x6BBB));
        Assert.Equal(300, m.Read(0x6BF7));
        Assert.Equal(7, m.Read(0x6BF9));
        Assert.Equal(1, m.Read(0x6C00));
        Assert.Equal(0x81, m.Read(0x6C0C));
        Assert.Equal(12, m.Read(0x6C1B));
        Assert.Equal(50, m.Read(0x6DCF));
        Assert.Equal(0, m.Read(0x6DB1));

        // A read-only case returns the record, not what was written; the cell still holds the write.
        m.Write(0x6B18, 3);
        Assert.Equal(17, m.Read(0x6B18));
        Assert.Equal(3, m.Cells6B00[0x18]);

        m.Write(0x6B21, 0x1AB);
        Assert.Equal(0xAB, party.Members[0][0x19]);
        m.Write(0x6BB8, 0xE4); // > 0xB2: reduced by 0x32
        Assert.Equal(0xB2, party.Members[0][0x84]);
        m.Write(0x6BBD, 0x2345);
        Assert.Equal(0x2345, party.Members[0].Word(0x8A));
        m.Write(0x6C0C, 0x80);
        Assert.Equal((0, 1), (party.Members[0][0x10E], party.Members[0][0x10F]));
        m.Write(0x6C00, 0x87);
        Assert.Equal((0, 7), (party.Members[0][0x10D], party.Members[0][0x10C]));
        m.Write(0x6C00, 0);
        Assert.True(m.Ds84D2);
        m.Write(0x6B00, 0);
        Assert.True(m.Ds84D3);
    }

    [Fact]
    public void Record_addresses_without_a_character_use_the_cell_and_are_logged()
    {
        var m = new EclMemory();
        var diags = new List<string>();
        m.Diagnostic += diags.Add;
        m.Write(0x6B18, 4);
        Assert.Equal(4, m.Read(0x6B18));
        Assert.Single(diags);
        m.Write(0x6B10, 5); // not in the table: plain cell, no diagnostic
        Assert.Equal(5, m.Read(0x6B10));
        Assert.Single(diags);
    }

    [Fact]
    public void Wall_set_cells_load_a_set_above_0x80()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(0x85), Mem(0x6E24))
            .Op(EclOpcodes.Save, Imm8(0x80), Mem(0x6E26)));
        Assert.Equal(["LoadWallSet 2 5"], rig.Host.Calls);
    }

    // ---- Party opcodes -----------------------------------------------------------------------------------------

    [Fact]
    public void PartyStr_uses_the_record_formula()
    {
        // (HP 20 + 5·(64−60) + 5·(41−39) + 8·2 + 4·1) / 10 = 70/10 = 7; second member (9)/10 = 0.
        var party = Party(c => { c[0x11B] = 20; c[0x111] = 64; c[0x110] = 41; c[0x9B] = 2; c[0x96] = 1; },
                          c => { c[0x11B] = 9; c[0x111] = 50; c[0x110] = 30; });
        var rig = RunOps(a => a.Op(EclOpcodes.PartyStr, Mem(F)), m => m.Party = party);
        Assert.Equal(7, rig[F]);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(10, 1)]
    [InlineData(2, 0)]
    public void PSurprise_counts_a_ranger(byte cls, int expected)
    {
        var party = Party(c => c[0x2F] = 2, c => c[0x2F] = cls);
        var rig = RunOps(a => a.Op(EclOpcodes.Save, Imm8(5), Mem(F + 1)).Op(EclOpcodes.PSurprise, Mem(F), Mem(F + 1)),
            m => m.Party = party);
        Assert.Equal(expected, rig[F]);
        Assert.Equal(0, rig[F + 1]);
    }

    [Theory]
    // b1..b4, die rolls (0-based) r1, r2 → result. X = b4 + 2 − b1, Y = b2 + 2 − b3.
    [InlineData(0, 0, 0, 0, 0, 1, 2)] // Y = 2, r2 = 2 ≤ 2
    [InlineData(0, 0, 0, 0, 1, 2, 1)] // r2 = 3 > 2, r1 = 2 ≤ X = 2
    [InlineData(0, 0, 0, 0, 2, 2, 0)]
    [InlineData(3, 0, 5, 0, 0, 0, 0)] // X = −1, Y = −3: nothing
    public void Surprise_rolls_two_d6(int b1, int b2, int b3, int b4, int r1, int r2, int result)
    {
        var rig = RunOps(a => a.Op(EclOpcodes.Surprise, Imm8(b1), Imm8(b2), Imm8(b3), Imm8(b4)),
            host: h => { h.RandomValues.Enqueue((ushort)r1); h.RandomValues.Enqueue((ushort)r2); });
        Assert.Equal(result, rig[0x6DCB]);
        Assert.Equal(["Random 6", "Random 6"], rig.Host.Calls);
    }

    [Fact]
    public void FindItem_searches_byte_2E_of_the_items()
    {
        var item = new byte[63];
        item[0x2E] = 12;
        var party = Party(_ => { }, c => c.Items.Add(item));
        var found = RunOps(a => a.Op(EclOpcodes.Compare, Imm8(1), Imm8(1)).Op(EclOpcodes.FindItem, Imm16(0x10C)), m => m.Party = party);
        Assert.Equal("100000", JsonlTraceWriter.FlagString(found.Vm.Flags));
        var missing = RunOps(a => a.Op(EclOpcodes.FindItem, Imm8(13)), m => m.Party = party);
        Assert.Equal("010000", JsonlTraceWriter.FlagString(missing.Vm.Flags));
    }

    // ---- LOADFILES / LOADPIECES --------------------------------------------------------------------------------

    [Fact]
    public void LoadFiles_in_3d_sets_the_map_cell()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(7), Mem(0x6DC9))
            .Op(EclOpcodes.LoadFiles, Imm8(18), Imm8(0), Imm8(255))
            .Op(EclOpcodes.LoadFiles, Imm8(255), Imm8(0), Imm8(255))
            .Op(EclOpcodes.LoadFiles, Imm8(127), Imm8(0), Imm8(1)));
        Assert.Equal(18, rig[0x49C5]);
        Assert.Equal(0, rig[0x6DC9]);
        Assert.Equal(["LoadMap 18"], rig.Host.Calls);
    }

    [Fact]
    public void LoadFiles_outdoors_loads_the_picture()
    {
        var rig = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(0), Mem(0x49E6))
            .Op(EclOpcodes.LoadFiles, Imm8(4), Imm8(0), Imm8(1))
            .Op(EclOpcodes.LoadFiles, Imm8(4), Imm8(0), Imm8(255)));
        Assert.Equal(["LoadOutdoorPicture"], rig.Host.Calls);
        Assert.Equal(0, rig[0x49C5]);
    }

    [Fact]
    public void LoadPieces_slot_rules()
    {
        var plain = RunOps(a => a.Op(EclOpcodes.LoadPieces, Imm8(1), Imm8(255), Imm8(5)));
        Assert.Equal(["LoadWallSet 1 1", "LoadWallSet 2 empty", "LoadWallSet 3 5"], plain.Host.Calls);
        var wall0 = RunOps(a => a.Op(EclOpcodes.LoadPieces, Imm8(127), Imm8(1), Imm8(2)));
        Assert.Equal(["LoadWallSet 1 0"], wall0.Host.Calls);
        var two = RunOps(a => a
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x49E7))
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x49E8))
            .Op(EclOpcodes.LoadPieces, Imm8(3), Imm8(4), Imm8(6)));
        Assert.Equal(["LoadWallSet 1 3", "LoadWallSet 3 6"], two.Host.Calls);
    }

    // ---- Awaiting input ----------------------------------------------------------------------------------------

    [Fact]
    public void A_menu_with_no_answer_ends_the_run_after_its_operands()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.HMenu, Mem(F), Imm8(1), Text("GO"))
            .Label("next").Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Host.StopWhenInputMissing = true;
        var r = rig.Run();
        Assert.Equal(EclStopReason.AwaitingInput, r.Reason);
        Assert.Equal(EclOpcodes.HMenu, rig.Vm.Opcode);
        Assert.Equal(rig.Trace.Steps[0].NextPc, rig.Vm.Pc);
        Assert.Equal(EclOpcodes.Exit, rig.Memory.FetchCode(rig.Vm.Pc));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T x, Action<T> f)
    {
        f(x);
        return x;
    }
}
