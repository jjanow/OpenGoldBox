using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Tests on the player's PoR v1.3 files (env <c>GOLDBOX_POR_DIR</c>; skipped without). The decode invariants are the
/// ones <c>tools/scripts/ecl.py check</c> reports (docs/re/ecl-format.md "Verification").
/// </summary>
public class PorScriptTests(ITestOutputHelper output)
{
    [PorFact]
    public void Every_block_decodes_like_ecl_py_check()
    {
        var dir = PorData.Open();
        int blocks = 0, insns = 0;
        var types = new Dictionary<byte, int>();
        var ids = new List<int>();
        foreach (var (_, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                var b = file.Decode(e);
                Assert.Equal(0x1388, b[0] | (b[1] << 8));
                var an = new EclAnalysis(b);
                Assert.Empty(an.HardErrors);
                Assert.Empty(an.Errors);
                Assert.Empty(an.Overlaps);
                Assert.Empty(an.Unhandled);
                Assert.Empty(an.UndefinedTargets);
                Assert.Empty(an.Hazards);
                Assert.All(EclDecoder.Vectors(b), v => Assert.Equal((byte)1, v.Operand.Type));
                blocks++;
                ids.Add(e.Id);
                insns += an.Instructions.Count;
                foreach (var o in an.Instructions.Values.SelectMany(i => i.Operands))
                    types[o.Type] = types.GetValueOrDefault(o.Type) + 1;
            }
        }
        output.WriteLine($"blocks {blocks}, instructions {insns}, types {string.Join(" ", types.OrderBy(t => t.Key).Select(t => $"{t.Key:X2}:{t.Value}"))}");
        Assert.Equal(29, blocks);
        Assert.Equal(Enumerable.Range(0, 30).Where(i => i != 12), ids.Order());
        Assert.Equal(16015, insns);
        Assert.Equal(new Dictionary<byte, int> { [0] = 9176, [1] = 13099, [2] = 43, [3] = 75, [0x80] = 2556, [0x81] = 263 }, types);
    }

    [PorFact]
    public void Shipped_saves_hold_ECL2_block_20()
    {
        var dir = PorData.Open();
        var block = dir.LoadBlock(2, 20)!;
        foreach (var name in new[] { "SAVGAMA.DAT", "SAVGAMJ.DAT" })
        {
            var save = PorSaveGame.Open(dir.Find(name)!);
            Assert.Equal(2, save.Disk);
            Assert.Equal(20, save.ScriptId);
            Assert.Equal(0, save.ScriptAreaMismatches(block));
        }
    }

    /// <summary>
    /// Runs every vector of every block headless from save A's state with the scripted host (menu choice 0,
    /// random 0, nothing found) for a step budget. A run must end with EXIT, NEWECL or the budget: no fault and no
    /// unhandled opcode. Diagnostics (e.g. undefined special reads) are counted, not failed.
    /// </summary>
    [PorFact]
    public void Every_vector_runs_headless_without_faults()
    {
        const int Budget = 5000;
        var dir = PorData.Open();
        var saveA = PorSaveGame.Open(dir.Find("SAVGAMA.DAT")!);
        var reasons = new Dictionary<EclStopReason, int>();
        int runs = 0, steps = 0, diagnostics = 0, hostCalls = 0;
        var faults = new List<string>();
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                for (int v = 0; v < EclLayout.VectorCount; v++)
                {
                    var host = new ScriptedEclHost();
                    var vm = new EclVm(new EclMemory(), host) { Scripts = dir };
                    saveA.ApplyTo(vm);
                    vm.Memory.Write(0x6E12, (ushort)disk);
                    vm.LoadScript(e.Id, disk);
                    var r = vm.RunVector(v, Budget);
                    runs++;
                    steps += r.Steps;
                    diagnostics += vm.DiagnosticCount;
                    hostCalls += host.Calls.Count;
                    reasons[r.Reason] = reasons.GetValueOrDefault(r.Reason) + 1;
                    if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                        faults.Add($"ECL{disk} block {e.Id} vector {v}: {r.Reason} {r.Message}");
                }
            }
        }
        output.WriteLine($"runs {runs}, steps {steps}, host calls {hostCalls}, diagnostics {diagnostics}, " +
                         string.Join(", ", reasons.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}")));
        Assert.Equal(29 * 5, runs);
        Assert.Empty(faults);
    }

    /// <summary>
    /// Exploration: the same headless runs with seeded random host answers (menu picks, random rolls, found / not
    /// found) and a random party position and quest flags on top of save A, several seeds per vector, to reach more of each script. Checks
    /// no faults and that every instruction the VM executed is one the static analysis decoded (same boundaries).
    /// </summary>
    [PorFact]
    public void Seeded_exploration_runs_without_faults_on_decoded_boundaries()
    {
        const int Budget = 5000, Seeds = 24;
        var dir = PorData.Open();
        var saveA = PorSaveGame.Open(dir.Find("SAVGAMA.DAT")!);
        var faults = new List<string>();
        int runs = 0, offBoundary = 0, onFallThroughIntoData = 0;
        long steps = 0;
        int reached = 0, decoded = 0;
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                var an = new EclAnalysis(file.Decode(e));
                var known = an.Instructions.Keys.Select(o => EclLayout.OffsetToAddress(o)).ToHashSet();
                var seen = new HashSet<ushort>();
                for (int v = 0; v < EclLayout.VectorCount; v++)
                {
                    for (int seed = 0; seed < Seeds; seed++)
                    {
                        var rng = new Random(seed * 1000 + e.Id * 10 + v);
                        var host = new ScriptedEclHost { AnswerFallback = rng, RandomFallback = rng };
                        var trace = new ListTraceSink();
                        var vm = new EclVm(new EclMemory(), host) { Scripts = dir };
                        saveA.ApplyTo(vm);
                        vm.Memory.Write(0x6E12, (ushort)disk);
                        vm.LoadScript(e.Id, disk);
                        if (seed > 0)
                        {
                            // Random position and quest flags (seed 0 keeps save A's); scripts dispatch on both.
                            vm.Memory.Position.X = (byte)rng.Next(16);
                            vm.Memory.Position.Y = (byte)rng.Next(16);
                            vm.Memory.Position.Facing = (byte)(2 * rng.Next(4));
                            vm.Memory.Position.EventByte = (byte)rng.Next(256);
                            vm.Memory.Position.WallAhead = (byte)rng.Next(16);
                            for (ushort a = 0x4A00; a <= 0x4AFF; a++)
                                vm.Memory.Cells4900[a - EclMemory.Area4900] = (ushort)(rng.Next(4) == 0 ? rng.Next(256) : 0);
                        }
                        vm.Trace = trace;
                        var r = vm.RunVector(v, Budget);
                        runs++;
                        steps += r.Steps;
                        if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                            faults.Add($"ECL{disk} block {e.Id} vector {v} seed {seed}: {r.Reason} {r.Message}");
                        bool afterOnFallThrough = false;
                        foreach (var s in trace.Steps.Where(s => s.Script == e.Id))
                        {
                            seen.Add(s.Pc);
                            if (!known.Contains(s.Pc))
                            {
                                // The analysis cuts speculative paths where an ON GOTO/GOSUB falls through into a
                                // table; random state can take that path at run time. Anything else is a failure.
                                if (afterOnFallThrough)
                                    onFallThroughIntoData++;
                                else
                                {
                                    offBoundary++;
                                    faults.Add($"ECL{disk} block {e.Id} vector {v} seed {seed}: executed {EclOpcodes.Name(s.Op)} at {s.Pc:X4}, not a decoded instruction");
                                }
                                break; // the rest of this run is outside the decoded code
                            }
                            afterOnFallThrough = s.Op is EclOpcodes.OnGoto or EclOpcodes.OnGosub
                                && s.HostCalls.Count == 0 && s.NextPc == FallThrough(an, s.Pc);
                        }
                    }
                }
                reached += seen.Count(known.Contains);
                decoded += known.Count;
            }
        }
        output.WriteLine($"runs {runs}, steps {steps}, instructions reached {reached} of {decoded} decoded, " +
                         $"ON fall-through into data {onFallThroughIntoData}, other off-boundary {offBoundary}, faults {faults.Count}");
        foreach (var f in faults.Take(20))
            output.WriteLine(f);
        Assert.Empty(faults);
        Assert.Equal(0, offBoundary);
    }

    private static ushort FallThrough(EclAnalysis an, ushort pc)
    {
        var ins = an.Instructions[EclLayout.AddressToOffset(pc)];
        return (ushort)(pc + ins.Size);
    }

    /// <summary>The script switch path (NEWECL → init, move, search) over every block, following the chain.</summary>
    [PorFact]
    public void Script_switches_load_and_run_without_faults()
    {
        var dir = PorData.Open();
        var saveA = PorSaveGame.Open(dir.Find("SAVGAMA.DAT")!);
        var faults = new List<string>();
        int switches = 0;
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                for (int v = 0; v < 2; v++)
                {
                    var vm = new EclVm(new EclMemory(), new ScriptedEclHost()) { Scripts = dir };
                    saveA.ApplyTo(vm);
                    vm.Memory.Write(0x6E12, (ushort)disk);
                    vm.LoadScript(e.Id, disk);
                    var r = vm.RunVector(v, 5000);
                    if (r.Reason != EclStopReason.NewScript)
                        continue;
                    switches++;
                    try
                    {
                        r = vm.FollowScriptChanges(5000, 8);
                    }
                    catch (EclVmFault f)
                    {
                        // A switch to a script not on the disk $6E12 names: a data question, reported not hidden.
                        faults.Add($"ECL{disk} block {e.Id} vector {v}: {f.Message}");
                        continue;
                    }
                    if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                        faults.Add($"ECL{disk} block {e.Id} vector {v} after switch: {r.Reason} {r.Message}");
                }
            }
        }
        output.WriteLine($"switch chains followed: {switches}; problems: {faults.Count}");
        foreach (var f in faults)
            output.WriteLine(f);
        Assert.Empty(faults);
    }

    [PorFact]
    public void Wilderness_035F_tables_match_neither_the_residue_nor_zero()
    {
        // ecl-format.md "VM memory map": the three wilderness scripts compare an undefined read of $035F with a table
        // in a loop. In COMPARE that read returns the previous COMPARE read: the loop index (block 25) or the loop
        // bound $6E7B (blocks 26: 28 or 19, 27: 8). No table holds that value or 0 (the VM's default), so the
        // compare never matches in the game or in the VM.
        var dir = PorData.Open();
        (int Disk, int Block, ushort Table, int Length, int[] Residues)[] uses =
        [
            (6, 25, 0xADCE, 8, [0, 1, 2, 3, 4, 5, 6, 7]),
            (7, 26, 0xB019, 29, [28]),
            (7, 26, 0xB036, 20, [19]),
            (8, 27, 0xAB9E, 8, [8]),
        ];
        foreach (var (disk, block, table, length, residues) in uses)
        {
            var b = dir.LoadBlock(disk, block)!;
            var values = b.Skip(2 + table - EclLayout.Base).Take(length).Select(x => (int)x).ToList();
            Assert.DoesNotContain(0, values);
            Assert.Empty(values.Intersect(residues));
        }
    }
}
