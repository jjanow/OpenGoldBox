using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;
using Xunit.Abstractions;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>
/// The Secret of the Silver Blades VM profile <c>ecl-sotsb-1.3</c> (docs/re/survey-sotsb.md §3, module-format.md §5.1):
/// its data, the decoder under it, and (with <c>GOLDBOX_SOTSB_DIR</c>) every SotSB script block.
/// </summary>
public class SotsbProfileTests(ITestOutputHelper output)
{
    private static EclProfile S => EclProfiles.Sotsb13;

    [Fact]
    public void Registry_has_the_sotsb_profile_by_name()
    {
        Assert.Same(S, EclProfiles.Get("ecl-sotsb-1.3"));
        Assert.Same(S, EclProfiles.ForVm("ecl-sotsb-1.3"));
        Assert.Equal("ecl-sotsb-1.3", S.Name);
        Assert.Same(EclProfiles.Por13, EclProfiles.Default); // code that names none still runs PoR
    }

    [Fact]
    public void Layout_and_regions_are_pors_moved()
    {
        Assert.Equal(0x8000, S.ScriptBase);
        Assert.Equal(2, S.ScriptPrefix);
        Assert.Equal(0x1E00, S.ScriptAreaSize);
        Assert.Equal(0x9DFF, S.ScriptEnd);
        Assert.Equal(5, S.VectorCount);
        Assert.Equal(4, S.InitVector);
        Assert.Equal(22, S.FirstCodeOffset);
        Assert.Equal(2, S.AddressToOffset(0x8000));
        Assert.Equal((ushort)0x8014, S.OffsetToAddress(22));

        Assert.Equal(["4B00", "7A00", "7C00", "script"], S.Regions.Select(r => r.Name));
        Assert.Equal([0x4B00, 0x7A00, 0x7C00, 0x8000], S.Regions.Select(r => (int)r.Start));
        Assert.Equal([0x4EFF, 0x7BFF, 0x7FFF, 0x9DFF], S.Regions.Select(r => (int)r.End));
        Assert.Equal([16, 16, 16, 8], S.Regions.Select(r => r.CellBits));
        Assert.Equal([true, true, true, false], S.Regions.Select(r => r.Saved)); // the save holds no script bytes
        for (int i = 1; i < S.Regions.Count; i++)
            Assert.True(S.Regions[i - 1].End < S.Regions[i].Start);
        // Same cell counts as PoR's regions: 1,024 / 512 / 1,024 words and the 7,680-byte script.
        Assert.Equal([1024, 512, 1024, 7680], S.Regions.Select(r => r.Cells));
        Assert.Equal(S.ScriptAreaSize, S.Regions[^1].Cells);

        Assert.Equal(EclOperandWidthRule.FromAddressRegion, S.OperandWidth);
        Assert.Equal(EclUnmappedRead.StackResidue, S.UnmappedRead);
        Assert.Equal(EclReturnOnEmptyStack.Exits, S.ReturnOnEmptyStack);
        Assert.Equal(EclCharacterView.Sotsb7C00Redirection, S.CharacterView);
        Assert.Equal(EclHandlerSet.Sotsb, S.Handlers);
        Assert.False(S.ScriptBytesSaved);
        Assert.Equal((ushort)0x7F12, S.DiskCell);
    }

    [Fact]
    public void Call_and_program_ids_are_sotsbs()
    {
        Assert.Equal([0x2DCB, 0x6803, 0xB203, 0xC018, 0xC01E], S.CallIds.Keys.Order().Select(k => (int)k));
        Assert.Equal([0, 9], S.ProgramIds.Keys.Order().Select(k => (int)k));
        Assert.Equal([9], S.ProgramIdsThatExit.Select(k => (int)k));
        Assert.Equal((ushort)0x4C38, S.ProgramExitGuardCell); // 9 exits only while $4C38 = 0
        Assert.Null(EclProfiles.Por13.ProgramExitGuardCell);   // PoR's 9 always exits
    }

    [Fact]
    public void Banked_kinds_are_spec_resource_kinds()
    {
        Assert.Equal(["tile", "treasureList", "wallset"], S.BankedKinds.Order(StringComparer.Ordinal));
        Assert.Equal(["map", "script"], S.DiskCheckedKinds.Order(StringComparer.Ordinal));
        foreach (var k in S.BankedKinds.Concat(S.DiskCheckedKinds))
            Assert.Equal(k, ResourceKey.KindName(ResourceKey.ParseKind(k)));
    }

    [Fact]
    public void Opcode_table_has_65_dispatched_opcodes_less_the_3C_stub()
    {
        var t = S.Opcodes;
        Assert.Same(EclOpcodes.Sotsb, t);
        // Dispatcher OVR 0x458B: 0x00–0x41 without 1F (65); 3C's handler loads nothing and leaves the PC: no row.
        for (int i = 0; i < 256; i++)
            Assert.Equal(i <= 0x41 && i != 0x1F && i != 0x3C, t.IsHandled((byte)i));
        Assert.Equal("PROTECT", t.Name(EclOpcodes.Protect));
        Assert.Equal("UNDEF1F", t.Name(EclOpcodes.Undefined1F));
        Assert.Equal(1, t.SkipCount(EclOpcodes.Protect));
        Assert.Equal(2, t.SkipCount(EclOpcodes.Undefined1F));

        Assert.Equal(["amount", "unit"], t.Get(EclOpcodes.Clock)!.Operands);
        Assert.Equal(("DUMP", 0), (t.Name(0x3E), t.Get(0x3E)!.FixedCount));
        Assert.Equal(("FINDSPECIAL", 1), (t.Name(0x3F), t.Get(0x3F)!.FixedCount));
        Assert.Equal(("DESTROYITEMS", 1), (t.Name(0x40), t.Get(0x40)!.FixedCount));
        Assert.Equal(("ADDEP", 2), (t.Name(0x41), t.Get(0x41)!.FixedCount));

        // IF-skip OVR 0xAFB0: SETUPMON's PoR mis-skip fixed, CLOCK's new one.
        Assert.Equal(3, t.SkipCount(EclOpcodes.SetupMon));
        Assert.Equal([0x15, 0x25, 0x26, 0x2B, 0x34, 0x36], t.MisSkips.Select(b => (int)b));

        // Every other row keeps PoR's operands.
        for (int i = 0; i <= 0x3D; i++)
        {
            byte op = (byte)i;
            if (op is EclOpcodes.Clock or EclOpcodes.Protect or EclOpcodes.Undefined1F)
                continue;
            Assert.Equal(EclOpcodes.Por13.Get(op)!.Operands, t.Get(op)!.Operands);
            Assert.Equal(EclOpcodes.Por13.Get(op)!.Variadic, t.Get(op)!.Variadic);
        }
    }

    [Theory]
    [InlineData(EclProfiles.Por13Name)]
    [InlineData(EclProfiles.Sotsb13Name)]
    public void Row_skip_counts_match_the_table(string name)
    {
        var t = EclProfiles.Get(name).Opcodes;
        for (int i = 0; i < 256; i++)
            if (t.Get((byte)i) is { } row)
                Assert.Equal(t.SkipCount((byte)i), row.SkipCount);
    }

    [Fact]
    public void Memory_refuses_to_run_it()
    {
        // Decoding only: the SotSB handlers (RETURN-exits, 3E–41, the 7C00 view) are not written.
        var e = Assert.Throws<EclProfileNotRunnableException>(() => new EclMemory(profile: S));
        Assert.Contains("ecl-sotsb-1.3", e.Message);
        Assert.Same(S, e.Profile);
    }

    [Fact]
    public void A_module_naming_it_is_refused_when_opened()
    {
        var tmp = new TempFolder();
        var root = Path.Combine(tmp.Path, "sotsb");
        Directory.CreateDirectory(root);
        new ModuleManifest { FormatVersion = "0.1", Id = "sotsb", Vm = "ecl-sotsb-1.3" }.Save(root);
        var e = Assert.Throws<EclProfileNotRunnableException>(() => GoldBox.Data.Module.Por.PorModule.Open(root));
        Assert.Same(S, e.Profile);
    }

    [Fact]
    public void Decoder_uses_the_profiles_base_and_table()
    {
        var b = new EclAsm(0x8000)
            .Vector(4, "init")
            .Label("init")
            .Op(EclOpcodes.Clock, Imm8(30), Imm8(1))           // 2 operands in SotSB
            .Op(EclOpcodes.Compare, Mem(0x4B20), Imm8(0))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.AddEp, Imm8(1), Imm16(500))         // skip 2 = its 2 operands
            .Op(EclOpcodes.IfNe)
            .Op(EclOpcodes.SetupMon, Imm8(1), Imm8(0), Imm8(2)) // skip 3: no hazard in SotSB
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.Exit)
            .Label("sub").Op(EclOpcodes.Dump).Op(EclOpcodes.Return)
            .Build();

        var v = EclDecoder.Vectors(b, S);
        Assert.Equal((ushort)0x8014, v[4].Operand.Word);

        var clock = EclDecoder.DecodeInstruction(b, S.FirstCodeOffset, S);
        Assert.Equal((ushort)0x8014, clock.Address);
        Assert.Equal("CLOCK", clock.Mnemonic);
        Assert.Equal(5, clock.Size);
        Assert.Equal(3, clock.SkipSize); // the IF-skip takes 1 of 2
        Assert.Same(S, clock.Profile);
        // Under PoR the same bytes are a 1-operand CLOCK.
        Assert.Equal(3, EclDecoder.DecodeInstruction(b, S.FirstCodeOffset).Size);

        var an = new EclAnalysis(b, S);
        Assert.Same(S, an.Profile);
        Assert.Empty(an.Errors);
        Assert.Empty(an.Overlaps);
        Assert.Empty(an.Unhandled);
        Assert.Empty(an.Hazards);
        Assert.Equal(10, an.Instructions.Count);
        var addEp = an.Instructions.Values.Single(i => i.Op == EclOpcodes.AddEp);
        Assert.Equal(["who", "amount"], Enumerable.Range(0, 2).Select(addEp.Role));
        Assert.Equal(0, an.ByteCoverage().Unreached);

        // PoR's table has no 3E/41 and its base is $9900: the same block does not decode.
        Assert.NotEmpty(new EclAnalysis(b).HardErrors);
    }

    [Fact]
    public void The_3C_stub_decodes_as_no_handler()
    {
        var b = new EclAsm(0x8000).Op(EclOpcodes.Protect, Imm8(1)).Build();
        var ins = EclDecoder.DecodeInstruction(b, S.FirstCodeOffset, S);
        Assert.False(ins.Handled);
        Assert.Equal(1, ins.Size);
        Assert.NotEmpty(new EclAnalysis(b, S).Unhandled);
    }

    /// <summary>The SotSB blocks (survey §1), the same ids in v1.00–v1.30.</summary>
    private static readonly int[] BlockIds = [3, 16, 17, 32, 33, 34, 48, 49, 50, 51, 52, 64, 65, 66, 68, 80, 81, 82, 96, 97, 98, 99];

    [SotsbFact]
    public void Every_sotsb_block_decodes_with_no_unknowns()
    {
        var dir = SotsbData.Open();
        int blocks = 0, insns = 0, bytes = 0, hardBytes = 0, porFailures = 0;
        var ids = new List<int>();
        var use = new int[256];
        var calls = new SortedSet<int>();
        var programs = new SortedSet<int>();
        foreach (var (disk, file) in dir.EclFiles())
        {
            Assert.InRange(disk, 1, 3);
            foreach (var e in file.Entries)
            {
                var b = file.Decode(e);
                Assert.Equal(0x1388, b[0] | (b[1] << 8));
                Assert.InRange(b.Length - S.ScriptPrefix, 1, S.ScriptAreaSize);
                var an = new EclAnalysis(b, S);
                Assert.Empty(an.Errors);
                Assert.Empty(an.Overlaps);
                Assert.Empty(an.Unhandled);
                Assert.Empty(an.UndefinedTargets);
                Assert.Empty(an.Hazards);
                var vec = EclDecoder.Vectors(b, S);
                Assert.All(vec, x => Assert.Equal((byte)1, x.Ignored));
                Assert.All(vec, x => Assert.Equal((byte)1, x.Operand.Type));
                Assert.All(vec, x => Assert.InRange(x.Operand.Word!.Value, S.ScriptBase, S.ScriptEnd));
                Assert.Equal((ushort)0x8014, vec[S.InitVector].Operand.Word);
                foreach (var ins in an.Instructions.Values)
                {
                    use[ins.Op]++;
                    for (int i = 0; i < ins.Operands.Count; i++)
                    {
                        Assert.True(ins.Operands[i].IsKnownType, $"block {e.Id} ${ins.Address:X4} operand {i}");
                        Assert.NotEqual("?", ins.Role(i));
                    }
                    if (ins.Op == EclOpcodes.Call)
                        calls.Add(ins.Operands[0].Word ?? ins.Operands[0].Lo);
                    if (ins.Op == EclOpcodes.Program)
                        programs.Add(ins.Operands[0].Word ?? ins.Operands[0].Lo);
                }
                var cov = an.ByteCoverage();
                bytes += cov.Hard + cov.Speculative + cov.Unreached;
                hardBytes += cov.Hard;
                insns += an.Instructions.Count;
                blocks++;
                ids.Add(e.Id);
                if (new EclAnalysis(b).HardErrors.Any())
                    porFailures++;
            }
        }
        output.WriteLine($"{blocks} blocks, {insns} instructions, {bytes} script bytes, {hardBytes} on hard paths; " +
                         $"CALL {string.Join(" ", calls.Select(c => $"${c:X4}"))}; PROGRAM {string.Join(" ", programs)}");

        Assert.Equal(BlockIds, ids.Order());
        // The survey's decoder check (§3.4, "Versions"): v1.20 / v1.30 (and the 1992 WizardWorks copy) have 137,918
        // script bytes and 15,322 instructions; v1.00 / v1.10 differ in ECL2 block 48 and ECL3 block 82 (6 bytes, one
        // instruction fewer). Both have 5,768 bytes off every path (tables and dead code).
        var (expInsns, expHard) = bytes switch
        {
            137_918 => (15_322, 132_135),
            137_912 => (15_321, 132_129),
            _ => throw new Xunit.Sdk.XunitException($"unexpected SotSB release: {bytes} script bytes"),
        };
        Assert.Equal(expInsns, insns);
        Assert.Equal(expHard, hardBytes);
        // Opcode use (survey §3.5 "Uses"): the new ones, CLOCK's one use, and no 3C.
        Assert.Equal(0, use[EclOpcodes.Protect]);
        Assert.Equal(1, use[EclOpcodes.Clock]);
        Assert.Equal(2, use[EclOpcodes.Dump]);
        Assert.Equal(0, use[EclOpcodes.FindSpecial]);
        Assert.Equal(1, use[EclOpcodes.DestroyItems]);
        Assert.Equal(40, use[EclOpcodes.AddEp]);
        Assert.Equal(152, use[EclOpcodes.Call]);
        // Every CALL id the scripts use is one the program implements.
        Assert.Equal([0x2DCB, 0xB203, 0xC01E], calls);
        Assert.Subset(S.CallIds.Keys.Select(k => (int)k).ToHashSet(), calls.ToHashSet());
        // PROGRAM 0 (block 16) and 3 (block 3, the start script, $82E3). The handler (OVR 0x41A1) has cases for 0 and 9
        // only, so 3 loads its operand and does nothing.
        Assert.Equal([0, 3], programs);
        Assert.False(S.ProgramIds.ContainsKey(3));
        // The PoR profile cannot read them (base $9900).
        Assert.Equal(blocks, porFailures);
    }
}
