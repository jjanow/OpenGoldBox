using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>
/// The SotSB handler set (docs/re/survey-sotsb.md §10): each handler that differs from PoR's, from the program
/// reading (SotSB v1.30 GAME.OVR offsets in the comments), and (with <c>GOLDBOX_SOTSB_DIR</c>) every vector of every
/// SotSB block run headless.
/// </summary>
public class SotsbVmTests(ITestOutputHelper output)
{
    private static EclProfile S => EclProfiles.Sotsb13;
    private const int F = 0x4C40; // a free global word

    private static VmRig Rig(EclAsm asm, Action<EclMemory>? setup = null, EclVmPolicy? policy = null) =>
        VmRig.Of(asm, policy, setup, S);

    private static EclAsm Asm() => new(0x8000);

    /// <summary>A 439-byte record named <paramref name="name"/>.</summary>
    private static EclCharacter Member(string name, Action<EclCharacter>? setup = null)
    {
        var c = new EclCharacter(null, EclCharacter.SotsbRecordSize) { Name = name };
        setup?.Invoke(c);
        return c;
    }

    private static EclParty Party(params EclCharacter[] members)
    {
        var p = new EclParty();
        p.Members.AddRange(members);
        p.CurrentIndex = 0;
        return p;
    }

    private static IEnumerable<EclTraceHostCall> HostCalls(VmRig rig) => rig.Trace.Steps.SelectMany(s => s.HostCalls);

    // ---- Memory: regions, special cells, character view ------------------------------------------------------------

    [Fact]
    public void Memory_runs_the_profile_with_its_moved_areas()
    {
        var m = new EclMemory(profile: S);
        m.Write(0x4B00, 1);
        m.Write(0x4EFF, 2);
        m.Write(0x7A00, 3);
        m.Write(0x7BFF, 4);
        m.Write(0x7C20, 5);
        m.Write(0x9DFF, 0x1234);
        Assert.Equal([1, 2], new[] { m.Cells4900[0], m.Cells4900[0x3FF] }.Select(x => (int)x));
        Assert.Equal([3, 4], new[] { m.Cells9700[0], m.Cells9700[0x1FF] }.Select(x => (int)x));
        Assert.Equal(5, m.Cells6B00[0x20]);
        Assert.Equal(0x34, m.Read(0x9DFF)); // script byte
        Assert.Equal((byte)0x34, m.FetchCode(0x9DFF)!.Value);
        Assert.Null(m.FetchCode(0x9E00));
        Assert.Equal(["4B00", "7A00", "7C00", "script", "special", "special"],
            new ushort[] { 0x4B10, 0x7A10, 0x7C10, 0x8010, 0x4900, 0x6B00 }.Select(m.Region));
        Assert.Null(m.Cell(0x4900)); // PoR's globals are no cells here
        Assert.Null(m.Cell(0x6B00));
    }

    [Fact]
    public void Facing_cells_follow_the_view_flag()
    {
        var m = new EclMemory(profile: S);
        m.Position.Facing = 6;
        m.Write(0x4BEA, 99);
        Assert.Equal(6, m.Read(0x4BEA)); // OVR 0xA10C: the facing byte, not the cell
        Assert.Equal(6, m.Read(0xC04D)); // $4BE6 = 0: raw facing (OVR 0xA21B)
        m.Write(0x4BE6, 1);
        Assert.Equal(3, m.Read(0xC04D)); // halved
        Assert.Equal(6, m.Read(0x033D));
    }

    [Fact]
    public void A_changed_view_flag_switches_the_game_mode()
    {
        var m = new EclMemory(profile: S) { GameMode = 2 };
        m.Write(0x4BE6, 0); // unchanged: nothing
        Assert.Equal((2, 0), (m.GameMode, m.PreviousGameMode));
        m.Write(0x4BE6, 1); // OVR 0x9F68
        Assert.Equal((4, 2), (m.GameMode, m.PreviousGameMode));
        m.Write(0x4BFD, 3); // sky cell: redraw flag (OVR 0x9F61)
        Assert.True(m.ViewRedrawFlags.HasFlag(ViewRedraw.Sky));
    }

    [Fact]
    public void Disk_cell_halves_the_written_value()
    {
        var m = new EclMemory(profile: S) { Disk = 1 };
        m.Write(0x7F12, 5); // START 0640:0AB0: (5 + 1) div 2
        Assert.Equal((3, 1), (m.Disk, m.PreviousDisk));
        Assert.Equal(3, m.Read(0x7F12));
        m.Write(0x7F12, 2);
        Assert.Equal(1, m.Disk);
    }

    [Fact]
    public void Wall_set_cells_load_a_set_above_0x80()
    {
        var m = new EclMemory(profile: S);
        var got = new List<(int, int)>();
        m.WallSetRequested += (slot, set) => got.Add((slot, set));
        m.Write(0x7F22, 0x80); // not above 0x80
        m.Write(0x7F24, 0x85);
        m.Write(0x7F26, 0x81);
        Assert.Equal([(2, 5), (3, 1)], got);
    }

    [Fact]
    public void Character_view_reads_map_into_the_439_byte_record()
    {
        var c = Member("A", r =>
        {
            r[0x13] = 7; r[0x19] = 8; r[0x1B] = 13; r[0x6B] = 0xFE; r[0x6C] = 4; r[0x70] = 9; r[0xE9] = 10;
            r[0xEE] = 0xFF; r[0xF3] = 11; r[0xFA] = 12; r[0xFF] = 13; r.SetWord(0x103, 0x1234); r.SetWord(0x10D, 0x5678);
            r[0x11F] = 0x80; r[0x120] = 1; r.SetWord(0x14E, 0xBEEF); r[0x150] = 14; r[0x1B5] = 15; r[0x1B6] = 16;
            r[0x1A6] = 3; r[0x1A8] = 0; r[0x1A9] = 1;
        });
        var m = new EclMemory(profile: S) { Party = Party(Member("first"), c) };
        m.Party.CurrentIndex = 1;
        ushort R(int off) => m.Read((ushort)(0x7C00 + off));
        Assert.Equal(7, R(0x15));
        Assert.Equal(8, R(0x18));
        Assert.Equal(13, R(0x19));
        Assert.Equal(0xFFFE, R(0x72)); // signed
        Assert.Equal(4, R(0x73));
        Assert.Equal(9, R(0x76));
        Assert.Equal(10, R(0x9B));
        Assert.Equal(0xFFFF, R(0xA0));
        Assert.Equal(11, R(0xA5)); // record 0xF3 (OVR 0x98FA: 0xF2 + k − 0xA4)
        Assert.Equal(12, R(0xAC));
        Assert.Equal(13, R(0xB8));
        Assert.Equal(0x1234, R(0xBB));
        Assert.Equal(0x5678, R(0xC5));
        Assert.Equal(0xFF80, R(0xD6));
        Assert.Equal(1, R(0xD8));
        Assert.Equal(0xBEEF, R(0xF7));
        Assert.Equal(14, R(0xF9));
        Assert.Equal(6, R(0x100)); // record 0x1A6 = 3 → 6
        Assert.Equal(0x80, R(0x10C));
        Assert.Equal(15, R(0x119));
        Assert.Equal(16, R(0x11B));
        Assert.Equal(1, R(0x2B1)); // list position of the current record
        Assert.Equal(1, R(0x2B4));
        Assert.Equal(30, R(0x2CF)); // record 0x1B = 13 → 30
        m.Disk = 2;
        Assert.Equal(2, R(0x312));
        m.Write(0x7F3E, 2);
        Assert.Equal(2, R(0x33E));
        // An offset with no case reads the cell; 0x20 (a PoR case) is none here.
        m.Write(0x7C20, 77);
        Assert.Equal(77, R(0x20));
        // No current record: the list length, as 0x97CF walks to the end.
        m.Party.CurrentIndex = -1;
        Assert.Equal(2, R(0x2B1));
    }

    [Fact]
    public void Level_pairs_use_the_second_class_flag()
    {
        // OVR 0x3CBB6: record 0x6B = 6, first non-zero of 0x111–0x116 is 8 (at 0x114), record 0xEF = 5 → flag 1.
        var c = Member("D", r => { r[0x6B] = 6; r[0x114] = 8; r[0xEF] = 5; r[0x11B] = 3; r[0x111] = 0; r[0x118] = 2; });
        var m = new EclMemory(profile: S) { Party = Party(c) };
        Assert.Equal(1, EclMemory.SotsbSecondClassActive(c));
        Assert.Equal(3 + 8, m.Read(0x7CCF)); // 1 · record 0x11B + record 0x114
        Assert.Equal(2 + 0, m.Read(0x7CCA)); // 1 · record 0x118 + record 0x111
        c[0xEF] = 8; // not greater
        Assert.Equal(0, EclMemory.SotsbSecondClassActive(c));
        Assert.Equal(8, m.Read(0x7CCF));
        c[0xEF] = 5;
        c[0x6B] = 5; // not class 6
        Assert.Equal(0, EclMemory.SotsbSecondClassActive(c));
        // 0xCD and 0xCE have no case: cells.
        m.Write(0x7CCD, 44);
        Assert.Equal(44, m.Read(0x7CCD));
    }

    [Fact]
    public void Member_missing_flag_reads_7D00_as_0()
    {
        var c = Member("A", r => r[0x1A6] = 0);
        var m = new EclMemory(profile: S) { Party = Party(c) };
        Assert.Equal(1, m.Read(0x7D00));
        m.MemberMissing = true;
        Assert.Equal(0, m.Read(0x7D00));
    }

    [Fact]
    public void Character_view_writes_set_record_fields()
    {
        var c = Member("A");
        var m = new EclMemory(profile: S) { Party = Party(c) };
        m.Write(0x7C1B, 0x41);
        Assert.Equal(0x41, c[0x1E]); // OVR 0x9D4F: record 0x1E + (k − 0x1B)
        m.Write(0x7C65, 0x42);
        Assert.Equal(0x42, c[0x68]);
        m.Write(0x7CB8, 0xC0);
        Assert.Equal(0xC0 - 0x32, c[0xFF]);
        m.Write(0x7CC5, 0x1234);
        Assert.Equal(0x1234, c.Word(0x10D));
        m.Write(0x7CF7, 0xABCD);
        Assert.Equal(0xABCD, c.Word(0x14E));
        m.Write(0x7CF9, 9);
        Assert.Equal(9, c[0x150]);
        c[0x1A6] = 5;
        m.Write(0x7D00, 2); // only 1 acts
        Assert.Equal(5, c[0x1A6]);
        m.Write(0x7D00, 1);
        Assert.Equal((0, 1), (c[0x1A6], c[0x1A7]));
        m.Write(0x7D0C, 0x81);
        Assert.Equal((1, 1), (c[0x1A8], c[0x1A9]));
        m.Write(0x7D19, 0x33);
        Assert.Equal(0x33, c[0x1B5]);
        // PoR's $6C00-style removal flag has no case: only $7C00 = 0 sets DS:A4B7.
        m.Write(0x7C00, 0);
        Assert.True(m.Ds84D3);
        Assert.False(m.Ds84D2);
    }

    [Fact]
    public void Name_cell_and_texts_use_the_7C00_rules()
    {
        var c = Member("ALIAS");
        var m = new EclMemory(profile: S) { Party = Party(c) };
        Assert.Equal("ALIAS", m.ReadText(0x7C00));
        m.WriteText(0x7C00, "NEW NAME");
        Assert.Equal("NEW NAME", c.Name);
        m.WriteText(0x4C40, "AB");
        Assert.Equal("AB", m.ReadText(0x4C40));
        m.Write(0x4C41, 0x0100); // a word with a 0 low byte does not end the text (OVR 0xA664 tests the word)
        Assert.Equal("A\0", m.ReadText(0x4C40));
        m.Write(0x4C50, 7);
        m.Write(0x4C51, 8);
        m.WriteText(0x4C50, ""); // OVR 0xA29A: only the terminator
        Assert.Equal([0, 8], new[] { m.Read(0x4C50), m.Read(0x4C51) }.Select(x => (int)x));
    }

    // ---- Control flow and script init -------------------------------------------------------------------------------

    [Fact]
    public void Return_with_an_empty_stack_ends_the_run()
    {
        var asm = Asm()
            .Op(EclOpcodes.Return)
            .Op(EclOpcodes.Save, Imm8(1), Mem(F));
        var rig = Rig(asm);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Exit, r.Reason);
        Assert.Equal(1, r.Steps);
        Assert.Equal(0, rig[F]);
    }

    [Fact]
    public void Return_with_a_frame_returns()
    {
        var asm = Asm()
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.Save, Imm8(2), Mem(F))
            .Op(EclOpcodes.Exit)
            .Label("sub")
            .Op(EclOpcodes.Return);
        var rig = Rig(asm);
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        Assert.Equal(2, rig[F]);
    }

    [Fact]
    public void Opcode_3C_is_no_handler()
    {
        var rig = Rig(Asm().Op(0x3C, Imm8(1)));
        var r = rig.Run();
        Assert.Equal(EclStopReason.UnhandledOpcode, r.Reason);
        Assert.Equal((ushort)0x8014, rig.Vm.Pc); // the PC is not moved
    }

    [Fact]
    public void Script_init_writes_sotsbs_cells_and_no_view_flag()
    {
        var rig = Rig(Asm().Op(EclOpcodes.Exit), m =>
        {
            m.Write(0x4BE6, 7);
            m.Write(0x4C00, 1);
            m.Write(0x4C1F, 1);
            m.Write(0x7F79, 1);
            m.Write(0x7F82, 1);
            m.Write(0x4C20, 1);
        });
        Assert.Equal(255, rig[0x7EE1]);
        Assert.Equal(0, rig[0x7ED2]);
        Assert.Equal(0, rig[0x7ED3]);
        Assert.Equal(0, rig[0x4BE5]);
        Assert.Equal(7, rig[0x4BE6]); // OVR 0x922C has no $4BE6 write
        Assert.Equal([0, 0, 0, 0, 1], new[] { 0x4C00, 0x4C1F, 0x7F79, 0x7F82, 0x4C20 }.Select(a => (int)rig[a]));
        Assert.Equal((ushort)0x8014, rig.Vm.Vectors[4]);
    }

    [Fact]
    public void Newecl_stores_the_old_id_and_loads_the_new_script()
    {
        var target = Asm().Op(EclOpcodes.Exit).Build();
        var asm = Asm().Op(EclOpcodes.NewEcl, Imm8(17));
        var rig = Rig(asm);
        rig.Vm.Scripts = new OneScript(17, target);
        rig.Vm.SetCurrentScript(3, 1);
        var r = rig.Run();
        Assert.Equal(EclStopReason.NewScript, r.Reason);
        Assert.Equal(3, rig[0x4BF2]);
        Assert.Equal(17, rig.Vm.CurrentScript);
        Assert.True(rig.Vm.ScriptChanged);
    }

    private sealed class OneScript(int id, byte[] block) : IEclScriptSource
    {
        public byte[]? LoadBlock(int disk, int script) => script == id ? block : null;
    }

    // ---- Opcodes ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Clock_passes_amount_and_unit()
    {
        var rig = Rig(Asm().Op(EclOpcodes.Clock, Imm8(30), Imm8(2)).Op(EclOpcodes.Exit));
        rig.Run();
        Assert.Contains("AdvanceClock 30 2", rig.Host.Calls);
    }

    [Fact]
    public void Program_compares_the_low_byte_and_9_exits_only_when_4C38_is_0()
    {
        var asm = Asm()
            .Op(EclOpcodes.Program, Imm16(0x109)) // low byte 9
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.Exit);
        var rig = Rig(asm);
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        Assert.Equal(0, rig[F]);
        Assert.Contains("Program 9", rig.Host.Calls);
        var rig2 = Rig(asm, m => m.Write(0x4C38, 1));
        rig2.Run();
        Assert.Equal(1, rig2[F]); // $4C38 ≠ 0: no EXIT (OVR 0x422E)
    }

    [Fact]
    public void Surprise_result_is_written_to_02CB_and_lost()
    {
        var rig = Rig(Asm().Op(EclOpcodes.Surprise, Imm8(0), Imm8(9), Imm8(0), Imm8(0)).Op(EclOpcodes.Exit));
        rig.Host.RandomValues.Enqueue(0);
        rig.Host.RandomValues.Enqueue(0);
        rig.Run();
        Assert.Contains(rig.AllDiagnostics, d => d.Contains("$02CB") && d.Contains("unlisted special"));
        Assert.Equal(0, rig[0x7ECB]);
    }

    [Fact]
    public void Loadfiles_uses_the_moved_cells_and_has_no_outdoor_branch()
    {
        var asm = Asm()
            .Op(EclOpcodes.LoadFiles, Imm8(49), Imm8(0), Imm8(1))
            .Op(EclOpcodes.Exit);
        var rig = Rig(asm, m => m.Write(0x7EC9, 5));
        rig.Memory.Write(0x4BE6, 1);
        rig.Run();
        Assert.Equal(["LoadMap 49"], rig.Host.Calls);
        Assert.Equal(49, rig[0x4BC5]);
        Assert.Equal(0, rig[0x7EC9]);
        var rig2 = Rig(asm); // $4BE6 = 0: nothing (no BACPAC picture either)
        rig2.Run();
        Assert.Empty(rig2.Host.Calls);
    }

    [Theory]
    [InlineData(0, 0, 0, 10, 20, 30, "LoadWallSet 1 10|LoadWallSet 2 20|LoadWallSet 3 30")]
    [InlineData(0, 0, 0, 255, 127, 30, "LoadWallSet 1 empty|LoadWallSet 2 empty|LoadWallSet 3 30")]
    [InlineData(0, 0, 0, 127, 20, 30, "LoadWallSet 1 0")]
    [InlineData(1, 1, 0, 255, 20, 127, "LoadWallSet 1 empty|LoadWallSet 3 empty")]
    [InlineData(1, 1, 0, 10, 20, 30, "LoadWallSet 1 10|LoadWallSet 3 30")]
    [InlineData(1, 1, 1, 10, 255, 127, "LoadWallSet 1 10")]
    [InlineData(1, 1, 1, 10, 20, 30, "LoadWallSet 1 10|LoadWallSet 2 20|LoadWallSet 3 30")]
    [InlineData(1, 0, 1, 10, 255, 30, "LoadWallSet 1 10|LoadWallSet 2 empty|LoadWallSet 3 30")]
    public void Loadpieces_follows_the_three_flags(int f1, int f2, int f3, int o1, int o2, int o3, string expected)
    {
        var rig = Rig(Asm().Op(EclOpcodes.LoadPieces, Imm8(o1), Imm8(o2), Imm8(o3)).Op(EclOpcodes.Exit), m =>
        {
            m.Write(0x4BE7, (ushort)f1);
            m.Write(0x4BE8, (ushort)f2);
            m.Write(0x4BE9, (ushort)f3);
        });
        rig.Run();
        Assert.Equal(expected.Split('|'), rig.Host.Calls);
    }

    [Fact]
    public void Loadchar_sets_the_missing_flag()
    {
        var p = Party(Member("A"), Member("B"));
        var rig = Rig(Asm().Op(EclOpcodes.LoadChar, Imm8(1)).Op(EclOpcodes.LoadChar, Imm8(5)).Op(EclOpcodes.Exit),
            m => m.Party = p);
        rig.Vm.CommandRead();
        rig.Vm.Step();
        Assert.Equal(1, p.CurrentIndex);
        Assert.False(rig.Memory.MemberMissing);
        rig.Vm.Step();
        Assert.Equal(1, p.CurrentIndex); // unchanged
        Assert.True(rig.Memory.MemberMissing);
        Assert.Equal(0, rig[0x7D00]);
    }

    [Fact]
    public void Dump_removes_the_current_character_and_selects_the_new_one()
    {
        var p = Party(Member("A"), Member("B"), Member("C"));
        p.CurrentIndex = 1;
        var rig = Rig(Asm().Op(EclOpcodes.Dump).Op(EclOpcodes.Exit), m =>
        {
            m.Party = p;
            m.Write(0x7F3E, 3);
        });
        rig.Run();
        Assert.Equal(["A", "C"], p.Members.Select(x => x.Name));
        Assert.Equal("A", p.Current!.Name); // the predecessor (OVR 0x20EEE)
        Assert.Same(p.Current, p.Selected);
        Assert.Equal(2, rig[0x7F3E]);
        Assert.Contains(HostCalls(rig), h => h.Name == "RemoveFromParty" && h.Args[0] == "B");
    }

    [Fact]
    public void Findspecial_tests_the_current_characters_effects()
    {
        var c = Member("A");
        c.Effects.Add([0x2A, 0, 0, 0, 0, 0, 0, 0, 0]);
        var asm = Asm()
            .Op(EclOpcodes.FindSpecial, Imm8(0x2A))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.Save, Imm8(1), Mem(F))
            .Op(EclOpcodes.FindSpecial, Imm8(0x27))
            .Op(EclOpcodes.IfNe)
            .Op(EclOpcodes.Save, Imm8(2), Mem(F + 1))
            .Op(EclOpcodes.Exit);
        var rig = Rig(asm, m => m.Party = Party(c));
        rig.Run();
        Assert.Equal((1, 2), (rig[F], rig[F + 1]));
    }

    [Fact]
    public void Destroyitems_removes_matching_items_from_every_member()
    {
        static byte[] Item(byte type, byte readied = 0)
        {
            var it = new byte[EclCharacter.ItemRecordSize];
            it[0x2E] = type;
            it[0x34] = readied;
            return it;
        }
        var a = Member("A");
        a.Items.AddRange([Item(5), Item(6), Item(5, 1)]);
        var b = Member("B");
        b.Items.Add(Item(6));
        var p = Party(a, b);
        p.CurrentIndex = 1;
        var rig = Rig(Asm().Op(EclOpcodes.DestroyItems, Imm8(5)).Op(EclOpcodes.Exit), m => m.Party = p);
        rig.Run();
        Assert.Equal([6], a.Items.Select(i => (int)i[0x2E]));
        Assert.Single(b.Items);
        Assert.Equal(1, p.CurrentIndex); // restored
        Assert.Contains(rig.AllDiagnostics, d => d.Contains("0x28D70"));
    }

    [Fact]
    public void Addep_splits_over_classes_and_needs_record_1A7()
    {
        EclCharacter Mk(string n, byte flag, params int[] levels) => Member(n, r =>
        {
            for (int i = 0; i < levels.Length; i++)
                r[0x111 + i] = (byte)levels[i];
            r[0x1A7] = flag;
            r.SetWord(0x12C, 0xFFFF);
        });
        var a = Mk("A", 1, 3);                         // one class
        var b = Mk("B", 1, 2, 0, 0, 0, 0, 0, 4);       // 0x111 and 0x117: two classes (k = 0 … 6)
        var c = Mk("C", 0, 5);                         // 0x1A7 = 0: nothing added
        var d = Mk("D", 1, 0xFF, 2);                   // −1 is not above 0
        var rig = Rig(Asm().Op(EclOpcodes.AddEp, Imm8(1), Imm16(1000)).Op(EclOpcodes.Exit), m => m.Party = Party(a, b, c, d));
        rig.Run();
        uint Exp(EclCharacter x) => (uint)(x.Word(0x12C) | (x.Word(0x12E) << 16));
        Assert.Equal(0xFFFFu + 1000, Exp(a)); // dword add with carry
        Assert.Equal(0xFFFFu + 500, Exp(b));
        Assert.Equal(0xFFFFu, Exp(c));
        Assert.Equal(0xFFFFu + 1000, Exp(d));
        Assert.Contains("ExperienceMessage (party)", rig.Host.Calls);

        // who = 0: the current character only, with its name in the message.
        var p2 = Party(Mk("E", 1, 1), Mk("F", 1, 1));
        p2.CurrentIndex = 1;
        var rig2 = Rig(Asm().Op(EclOpcodes.AddEp, Imm8(0), Imm16(10)).Op(EclOpcodes.Exit), m => m.Party = p2);
        rig2.Run();
        Assert.Equal(0xFFFFu, Exp(p2.Members[0]));
        Assert.Equal(0xFFFFu + 10, Exp(p2.Members[1]));
        Assert.Contains("ExperienceMessage F", rig2.Host.Calls);
    }

    [Fact]
    public void Addep_with_no_class_level_is_a_divide_fault()
    {
        var c = Member("Z", r => r[0x1A7] = 0); // the div comes before the 0x1A7 test
        var rig = Rig(Asm().Op(EclOpcodes.AddEp, Imm8(1), Imm16(10)).Op(EclOpcodes.Exit), m => m.Party = Party(c));
        var r = rig.Run();
        Assert.Equal(EclStopReason.Fault, r.Reason);
        Assert.Contains("divide by zero", r.Message);
    }

    [Fact]
    public void Record_opcodes_that_were_not_read_go_to_the_host()
    {
        var asm = Asm()
            .Op(EclOpcodes.PartyStr, Mem(F))
            .Op(EclOpcodes.ChkParty, Imm8(0), Imm8(1), Mem(F), Mem(F + 1), Mem(F + 2), Mem(F + 3))
            .Op(EclOpcodes.Rob, Imm8(1), Imm8(50), Imm8(0))
            .Op(EclOpcodes.Damage, Imm8(1), Imm8(1), Imm8(6), Imm8(0), Imm8(0))
            .Op(EclOpcodes.AddNpc, Imm8(3), Imm8(80))
            .Op(EclOpcodes.Exit);
        var rig = Rig(asm, m => m.Party = Party(Member("A")));
        rig.Run();
        Assert.Equal(["PartyStrength", "CheckParty 0 1", "Rob 1 50 50", "Damage 1 1 6 0 0", "AddNpc 3 80"],
            rig.Host.Calls.Where(c => !c.StartsWith("Random", StringComparison.Ordinal)));
        Assert.Equal(5, rig.AllDiagnostics.Count(d => d.Contains("was not read for its 439-byte record")));
    }

    // ---- Every block, every vector, headless -------------------------------------------------------------------------

    /// <summary>Finds a block on any disk (the program's loader retries until the disk with the block is in).</summary>
    private sealed class AnyDisk(GoldBox.Data.Por.PorGameDirectory dir) : IEclScriptSource
    {
        public byte[]? LoadBlock(int disk, int script) =>
            dir.LoadBlock(disk, script) ?? Enumerable.Range(1, 3).Select(d => dir.LoadBlock(d, script)).FirstOrDefault(b => b is not null);
    }

    /// <summary>
    /// The shipped SAVGAMA.DAT (survey-sotsb.md §6): disk byte, the three word areas ($4B00, $7C00, $7A00), the
    /// position, the mode bytes; the party from CHRDATA1–6.SAV / .SFX.
    /// </summary>
    private static void ApplyShippedSave(EclMemory m, string dir)
    {
        var d = File.ReadAllBytes(Path.Combine(dir, "SAVGAMA.DAT"));
        Assert.Equal(5469, d.Length);
        m.Disk = d[0];
        for (int i = 0; i < 1024; i++)
            m.Cells4900[i] = BitConverter.ToUInt16(d, 1 + 2 * i);
        for (int i = 0; i < 1024; i++)
            m.Cells6B00[i] = BitConverter.ToUInt16(d, 2049 + 2 * i);
        for (int i = 0; i < 512; i++)
            m.Cells9700[i] = BitConverter.ToUInt16(d, 4097 + 2 * i);
        m.Position.X = d[5121];
        m.Position.Y = d[5122];
        m.Position.Facing = d[5123];
        m.Position.WallAhead = d[5124];
        m.Position.EventByte = d[5125];
        m.PreviousGameMode = d[5126];
        m.GameMode = d[5127];
        var party = new EclParty();
        for (int k = 0; k < d[5140]; k++)
        {
            int o = 5141 + 41 * k;
            string name = System.Text.Encoding.ASCII.GetString(d, o + 1, d[o]);
            var c = new EclCharacter(File.ReadAllBytes(Path.Combine(dir, name + ".SAV")), EclCharacter.SotsbRecordSize);
            var sfx = Path.Combine(dir, name + ".SFX");
            if (File.Exists(sfx))
                c.Effects.AddRange(File.ReadAllBytes(sfx).Chunk(EclCharacter.EffectRecordSize));
            party.Members.Add(c);
        }
        party.CurrentIndex = 0;
        m.Party = party;
    }

    private static bool PorOnly(ushort a) => a is (>= 0x4900 and <= 0x4AFF) or (>= 0x6B00 and <= 0x6EFF);

    /// <summary>
    /// Collects every access to PoR's areas that SotSB does not have ($4900–$4AFF, $6B00–$6EFF): stored writes (the
    /// Written event), and through the memory's diagnostics the reads and writes of unlisted special addresses, which
    /// is what such an address is under SotSB, also outside a traced step (script init, party rules). The caller adds
    /// the traced reads.
    /// </summary>
    private static void WatchPorAddresses(EclMemory m, ISet<ushort> leaked)
    {
        m.Written += w =>
        {
            if (w.Region != "char" && PorOnly(w.Address))
                leaked.Add(w.Address);
        };
        m.Diagnostic += d =>
        {
            if (!d.Contains("unlisted special address", StringComparison.Ordinal))
                return;
            foreach (System.Text.RegularExpressions.Match x in System.Text.RegularExpressions.Regex.Matches(d, @"\$([0-9A-F]{4})"))
            {
                var a = Convert.ToUInt16(x.Groups[1].Value, 16);
                if (PorOnly(a))
                    leaked.Add(a);
            }
        };
    }

    [Fact]
    public void The_por_address_watch_catches_a_leak()
    {
        // Negative control for the headless checks: a write to PoR's $6DCB and a read of PoR's $49E6 are caught.
        var leaked = new SortedSet<ushort>();
        var rig = Rig(Asm().Op(EclOpcodes.Save, Imm8(1), Mem(0x6DCB)).Op(EclOpcodes.Add, Mem(0x49E6), Imm8(0), Mem(F)).Op(EclOpcodes.Exit),
            m => WatchPorAddresses(m, leaked));
        rig.Run();
        Assert.Equal([0x49E6, 0x6DCB], leaked.Select(a => (int)a));
    }

    /// <summary>
    /// Every vector of every SotSB v1.30 block, headless from the shipped save's state with the scripted host (menu
    /// choice 0, random 0) and a step budget: no fault, no unhandled opcode. No access, traced or not, falls in PoR's
    /// areas that SotSB does not have ($4900–$4AFF, $6B00–$6EFF), which would be a PoR cell leaking into the SotSB
    /// handlers (<see cref="WatchPorAddresses"/>).
    /// </summary>
    [SotsbFact]
    public void Every_vector_runs_headless_without_faults()
    {
        const int Budget = 5000;
        var dir = SotsbData.Open();
        var source = new AnyDisk(dir);
        var reasons = new Dictionary<EclStopReason, int>();
        int runs = 0, steps = 0, diagnostics = 0;
        var faults = new List<string>();
        var special = new SortedDictionary<ushort, int>();
        var leaked = new SortedSet<ushort>();
        var diag = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                for (int v = 0; v < S.VectorCount; v++)
                {
                    var host = new ScriptedEclHost();
                    var memory = new EclMemory(profile: S);
                    var trace = new ListTraceSink();
                    var vm = new EclVm(memory, host) { Scripts = source, Trace = trace };
                    var touched = new List<ushort>();
                    memory.Written += w => { if (w.Region != "char") touched.Add(w.Address); };
                    WatchPorAddresses(memory, leaked);
                    ApplyShippedSave(memory, SotsbData.Dir!);
                    memory.Disk = (byte)disk;
                    vm.LoadScript(e.Id, disk);
                    var r = vm.RunVector(v, Budget);
                    runs++;
                    steps += r.Steps;
                    diagnostics += vm.DiagnosticCount;
                    reasons[r.Reason] = reasons.GetValueOrDefault(r.Reason) + 1;
                    if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                        faults.Add($"ECL{disk} block {e.Id} vector {v}: {r.Reason} {r.Message}");
                    if (r.Reason == EclStopReason.Budget)
                        output.WriteLine($"budget: ECL{disk} block {e.Id} vector {v}");
                    foreach (var d in trace.Steps.SelectMany(s => s.Diagnostics.Select(x => $"block {e.Id} {s.Pc:X4}: {x}")).Concat(vm.Diagnostics))
                        diag[d] = diag.GetValueOrDefault(d) + 1;
                    touched.AddRange(trace.Steps.SelectMany(s => s.Reads).Select(x => x.Address));
                    foreach (var a in touched)
                    {
                        if (PorOnly(a))
                            leaked.Add(a);
                        if (memory.Region(a) == "special")
                            special[a] = special.GetValueOrDefault(a) + 1;
                    }
                }
            }
        }
        output.WriteLine($"runs {runs}, steps {steps}, diagnostics {diagnostics}, " +
                         string.Join(", ", reasons.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}")));
        output.WriteLine("special addresses: " + string.Join(" ", special.Select(x => $"${x.Key:X4}×{x.Value}")));
        foreach (var f in faults)
            output.WriteLine(f);
        foreach (var (d, n) in diag)
            output.WriteLine($"{n}× {d}");
        Assert.Equal(22 * 5, runs);
        Assert.Empty(faults);
        Assert.Empty(leaked);
    }

    /// <summary>
    /// Exploration as PoR's (PorScriptTests): seeded host answers, a random position and random quest flags
    /// ($4C00–$4CFF, the cells at PoR's $4A00–$4AFF offset) over the shipped save, several seeds per vector. Checks no
    /// fault, no unhandled opcode, no PoR-only address, and that every executed instruction is one the static
    /// analysis decoded (ON GOTO falling into a table aside).
    /// </summary>
    [SotsbFact]
    public void Seeded_exploration_runs_without_faults()
    {
        const int Budget = 5000, Seeds = 16;
        var dir = SotsbData.Open();
        var source = new AnyDisk(dir);
        var faults = new List<string>();
        var leaked = new SortedSet<ushort>();
        int runs = 0, reached = 0, decoded = 0, offBoundary = 0;
        long steps = 0;
        var ops = new SortedDictionary<byte, int>();
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                var an = new EclAnalysis(file.Decode(e), S);
                var known = an.Instructions.Keys.Select(o => S.OffsetToAddress(o)).ToHashSet();
                var seen = new HashSet<ushort>();
                for (int v = 0; v < S.VectorCount; v++)
                {
                    for (int seed = 0; seed < Seeds; seed++)
                    {
                        var rng = new Random(seed * 1000 + e.Id * 10 + v);
                        var host = new ScriptedEclHost { AnswerFallback = rng, RandomFallback = rng };
                        var memory = new EclMemory(profile: S);
                        var trace = new ListTraceSink();
                        var vm = new EclVm(memory, host) { Scripts = source };
                        ApplyShippedSave(memory, SotsbData.Dir!);
                        memory.Disk = (byte)disk;
                        vm.LoadScript(e.Id, disk);
                        if (seed > 0)
                        {
                            memory.Position.X = (byte)rng.Next(16);
                            memory.Position.Y = (byte)rng.Next(16);
                            memory.Position.Facing = (byte)(2 * rng.Next(4));
                            memory.Position.EventByte = (byte)rng.Next(256);
                            memory.Position.WallAhead = (byte)rng.Next(16);
                            for (int a = 0x100; a <= 0x1FF; a++)
                                memory.Cells4900[a] = (ushort)(rng.Next(4) == 0 ? rng.Next(256) : 0);
                        }
                        WatchPorAddresses(memory, leaked);
                        vm.Trace = trace;
                        var r = vm.RunVector(v, Budget);
                        runs++;
                        steps += r.Steps;
                        if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                            faults.Add($"ECL{disk} block {e.Id} vector {v} seed {seed}: {r.Reason} {r.Message}");
                        leaked.UnionWith(trace.Steps.SelectMany(s => s.Reads).Select(x => x.Address).Where(PorOnly));
                        bool afterOnFallThrough = false;
                        foreach (var s in trace.Steps.Where(s => s.Script == e.Id))
                        {
                            ops[s.Op] = ops.GetValueOrDefault(s.Op) + 1;
                            seen.Add(s.Pc);
                            if (!known.Contains(s.Pc))
                            {
                                if (!afterOnFallThrough)
                                {
                                    offBoundary++;
                                    faults.Add($"ECL{disk} block {e.Id} vector {v} seed {seed}: executed {s.Op:X2} at {s.Pc:X4}, not a decoded instruction");
                                }
                                break;
                            }
                            afterOnFallThrough = s.Op == EclOpcodes.OnGoto && s.HostCalls.Count == 0
                                && s.NextPc == s.Pc + an.Instructions[S.AddressToOffset(s.Pc)].Size;
                        }
                    }
                }
                reached += seen.Count(known.Contains);
                decoded += known.Count;
            }
        }
        output.WriteLine($"runs {runs}, steps {steps}, instructions reached {reached} of {decoded} decoded, off-boundary {offBoundary}");
        output.WriteLine("opcodes executed: " + string.Join(" ", ops.Select(x => $"{x.Key:X2}×{x.Value}")));
        foreach (var f in faults.Take(20))
            output.WriteLine(f);
        Assert.Empty(faults);
        Assert.Empty(leaked);
    }
}
