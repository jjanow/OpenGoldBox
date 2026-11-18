using System.Globalization;
using System.Text;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por.Dosbox;

/// <summary>One thing the game did between the captures, replayed in the VM or its stand-in host.</summary>
public abstract record CaptureAction;

/// <summary>Main menu → Load: memory from a save file (fixture <c>saves/</c> first, then the game folder), then the script init with DS:49F8 set and vector 4.</summary>
public sealed record LoadSaveAction(string SaveFile) : CaptureAction;

/// <summary>Run one entry vector (area loop / camp routine).</summary>
public sealed record RunVectorAction(int Vector) : CaptureAction;

/// <summary>
/// The area loop's step between vectors 0 and 1: $49F0/$49F1 = x, y, then the program moves the party (0x17438). The
/// new position is taken from the "after" capture of <see cref="PositionFrom"/> (null = this capture): the step
/// itself is program code outside the VM.
/// </summary>
public sealed record StepAction(string? PositionFrom = null) : CaptureAction;

/// <summary>
/// The forward-step key before vector 0 (geo-format.md "Edges", 0x16D58): the program clears $6DD5 and sets it to 1
/// when the step would leave the 16×16 grid. The program sets it only when the face ahead is not impassable; the
/// harness does not read walls, so it matches the program whenever the step stays on the map (all captures so far,
/// including the refused steps <c>oneway_blocked</c> and <c>oneway_barrier_blocked</c>). An impassable face on the map
/// edge would give 1 here and 0 in the game (program read s11; <c>AreaLoop.ForwardKey</c> models it); no capture has one.
/// </summary>
public sealed record ForwardKeyAction : CaptureAction;

/// <summary>A position change by program code with no script involved (a turn): position from the "after" capture.</summary>
public sealed record MoveAction(string? PositionFrom = null) : CaptureAction;

/// <summary>
/// Random draws made by program code the VM does not model (door pick/bash rolls, the rest loop's 1d100). The
/// count is searched in <see cref="Min"/>..<see cref="Max"/>; the comparison reports the count that reproduces the
/// "after" state and RandSeed.
/// </summary>
public sealed record HostDrawsAction(int Min, int Max, string Why) : CaptureAction;

/// <summary>Menu choices the player made, in order.</summary>
public sealed record AnswersAction(params ushort[] Choices) : CaptureAction;

/// <summary>Which character files the party comes from.</summary>
public enum CaptureParty
{
    /// <summary>The shipped save A's party (<c>CHRDATA1-6</c> in the game folder).</summary>
    SaveA,
    /// <summary>The in-game save B's party (<c>CHRDATB1-6</c> in the fixture <c>saves/</c>).</summary>
    SaveB,
}

/// <summary>A golden capture and how to replay it.</summary>
/// <param name="Name">Fixture folder (state captures) or scenario name (trace captures).</param>
/// <param name="Input">What the player did, for the report.</param>
/// <param name="StartFromBefore">Start from the capture's <c>before/</c> dump; else from the first <see cref="LoadSaveAction"/>.</param>
/// <param name="Actions">What happened between the two states.</param>
/// <param name="TraceFiles">Port-E9 traces (fixture-relative) the VM's instruction stream must follow, concatenated.</param>
/// <param name="HostCells">Cells written by program code outside the VM (clock, wall-set slot, …): differences there are reported, not counted.</param>
/// <param name="AllHost">Every change between the captures is program code outside the VM (a capture taken inside one host call).</param>
/// <param name="BeforeFrom">Take the start state from this capture's <c>before/</c> dump instead of this capture's own
/// (for a capture whose own <c>before</c> is taken inside a host call, e.g. waiting in ENCMENU, which the VM cannot resume).</param>
public sealed record CaptureScenario(
    string Name,
    string Input,
    bool StartFromBefore,
    IReadOnlyList<CaptureAction> Actions,
    CaptureParty Party,
    IReadOnlyList<string> TraceFiles,
    IReadOnlyList<(ushort From, ushort To, string Why)> HostCells,
    bool AllHost = false,
    string? BeforeFrom = null)
{
    public bool IsTrace => TraceFiles.Count > 0;
}

/// <summary>Outcome class of one comparison.</summary>
public enum CaptureOutcome
{
    /// <summary>The VM reproduces the capture (state and/or trace), apart from listed host cells.</summary>
    Match,
    /// <summary>The VM differs; <see cref="CaptureReport.FirstDivergence"/> says where.</summary>
    Diverged,
    /// <summary>No ECL instruction ran between the captures; every change is outside the VM.</summary>
    NoVmSteps,
}

/// <summary>Result of comparing one capture.</summary>
public sealed class CaptureReport
{
    public required string Name { get; init; }
    public CaptureOutcome Outcome { get; set; }
    public int VmSteps { get; set; }
    public string? FirstDivergence { get; set; }
    public List<string> Divergences { get; } = [];
    public List<string> HostOwned { get; } = [];
    public List<string> Notes { get; } = [];
    /// <summary>Host draw count chosen (state captures with <see cref="HostDrawsAction"/>).</summary>
    public int? HostDraws { get; set; }
    /// <summary>Random answers the VM used, as "range→value".</summary>
    public List<string> Rolls { get; } = [];
    public int TraceMatched { get; set; }
    public int TraceTotal { get; set; }
    public string StopReason { get; set; } = "";

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Name}: {Outcome}");
        sb.Append(CultureInfo.InvariantCulture, $" ({VmSteps} VM steps, ended {StopReason}");
        if (TraceTotal > 0)
            sb.Append(CultureInfo.InvariantCulture, $", trace {TraceMatched}/{TraceTotal}");
        if (HostDraws is { } k)
            sb.Append(CultureInfo.InvariantCulture, $", host draws {k}");
        sb.Append(')').AppendLine();
        if (FirstDivergence is not null)
            sb.Append("  first divergence: ").AppendLine(FirstDivergence);
        foreach (var d in Divergences.Skip(FirstDivergence is null ? 0 : 1))
            sb.Append("  differs: ").AppendLine(d);
        if (Rolls.Count > 0)
            sb.Append("  rolls: ").AppendLine(string.Join(", ", Rolls));
        foreach (var h in HostOwned)
            sb.Append("  host-owned: ").AppendLine(h);
        foreach (var n in Notes)
            sb.Append("  note: ").AppendLine(n);
        return sb.ToString();
    }
}

/// <summary>
/// Replays golden DOSBox-X captures in the headless VM and reports the first divergence (docs/re/dosbox-trace-method.md
/// "Comparing a headless VM against a capture"). State captures start from the <c>before/</c> dump (or a save
/// load), take the RandSeed from it, replay the inputs and compare the VM areas, PC, opcodes, flags, GOSUB depth and
/// RandSeed with <c>after/</c>. Trace captures replay from a save load and align the VM's instruction stream with
/// the port-E9 trace (PC, opcode, flags before each instruction); their boot's RandSeed is unknown, so each roll is
/// searched: the first value whose run stays on the trace.
/// </summary>
/// <param name="game">The game the captures replay on: the player's folder or an imported module (its program tables
/// and shipped saves too).</param>
public sealed class EclCaptureComparer(string fixturesDir, IPorContent game)
{
    public string FixturesDir { get; } = fixturesDir;
    public IPorContent Content { get; } = game;

    /// <summary>Upper bound on replays for one roll search.</summary>
    public int MaxAttempts { get; set; } = 20_000;

    /// <summary>When set, the chosen replay writes its JSONL trace here.</summary>
    public TextWriter? TraceOut { get; set; }

    public CaptureReport Compare(CaptureScenario s) => s.IsTrace ? CompareTrace(s) : CompareState(s);

    // -----------------------------------------------------------------------------------------------------------------
    // One replay
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class RollNeeded(ushort range) : Exception
    {
        public ushort Range { get; } = range;
    }

    private sealed class Attempt
    {
        public required EclVm Vm { get; init; }
        public required ScriptedEclHost Host { get; init; }
        public required EncounterHost Encounter { get; init; }
        public required ListTraceSink Sink { get; init; }
        public List<string> Notes { get; } = [];
        public List<(ushort Range, ushort Value)> Rolls { get; } = [];
        public EclStopReason Stop { get; set; } = EclStopReason.Exit;
        public ushort? RollNeeded { get; set; }
    }

    private string CapturePath(string capture, string part) => Path.Combine(FixturesDir, capture, part);

    private string ResolveSave(string file)
    {
        var f = Path.Combine(FixturesDir, "saves", file);
        if (File.Exists(f))
            return f;
        var shipped = Content.SaveFolder is { } sf ? new PorGameDirectory(sf).Find(file) : null;
        return shipped ?? throw new FileNotFoundException($"save {file} not in the fixtures or {Content.Origin}");
    }

    private EclParty LoadParty(CaptureParty p) => p switch
    {
        CaptureParty.SaveA => PorParty.Load(PorSaveGame.Open(ResolveSave("SAVGAMA.DAT")), Content.SaveFolder ?? FixturesDir),
        _ => PorParty.Load(PorSaveGame.Open(ResolveSave("SAVGAMB.DAT")), Path.Combine(FixturesDir, "saves")),
    };

    private Attempt Run(CaptureScenario s, int hostDraws, IReadOnlyList<ushort>? rollPrefix, IEclTraceSink? extraSink = null)
    {
        var memory = new EclMemory(profile: Content.VmProfile) { Party = LoadParty(s.Party) };
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        // The encounter opcodes run in the game-side host (docs/re/ecl-format.md "Encounters"); its menu answers come
        // from the same queue as the other menus, in the order the player gave them.
        var encounter = new EncounterHost(memory, host,
            new ScriptedEncounterUi(host.MenuChoices) { StopWhenInputMissing = true }, new RecordingCombatRunner(),
            new PorEncounterData(Content));
        var sink = new ListTraceSink();
        var vm = new EclVm(memory, encounter) { Scripts = Content, Trace = extraSink is null ? sink : new TeeSink(sink, extraSink) };
        encounter.CurrentBlock = () => vm.CurrentScript;
        encounter.Vm = vm;
        vm.ScriptSwitchEventByte = afterInit =>
        {
            if (afterInit)
                encounter.Map = GeoMap(memory) ?? encounter.Map; // the new script's LOADFILES loaded GEO<disk> block $49C5
            return encounter.EventByteHere();
        };
        var at = new Attempt { Vm = vm, Host = host, Encounter = encounter, Sink = sink };

        if (rollPrefix is not null)
        {
            int next = 0;
            host.RandomOverride = range =>
            {
                if (next >= rollPrefix.Count)
                    throw new RollNeeded(range);
                var v = rollPrefix[next++];
                at.Rolls.Add((range, v));
                return v;
            };
        }

        if (s.StartFromBefore)
        {
            var before = new DosboxVmState(CapturePath(s.BeforeFrom ?? s.Name, "before"));
            if (s.BeforeFrom is not null)
                at.Notes.Add($"start state from {s.BeforeFrom}/before");
            at.Notes.AddRange(before.ApplyTo(vm));
            if (before.GeoPlanes is { } planes)
                encounter.Map = new MapPlanes(planes);
            if (rollPrefix is null)
                host.Rng = new TurboPascalRandom(before.RandSeed);
        }

        try
        {
            foreach (var a in s.Actions)
            {
                switch (a)
                {
                    case LoadSaveAction l:
                    {
                        var save = PorSaveGame.Open(ResolveSave(l.SaveFile));
                        var r = save.LoadGame(vm);
                        encounter.Map ??= GeoMap(memory);
                        at.Stop = r.Reason;
                        break;
                    }
                    case RunVectorAction rv:
                    {
                        if (rv.Vector is 0 or 1)
                            vm.CommandRead(); // 0x40E3 / 0x4181: DS:4436 = current before the key's vectors
                        var r = vm.RunVector(rv.Vector);
                        if (vm.ScriptChanged) // DS:442F, set by this run or left over from a vector 4 switch
                        {
                            at.Notes.Add($"vector {rv.Vector} switched scripts; following");
                            r = vm.FollowScriptChanges();
                        }
                        at.Stop = r.Reason;
                        break;
                    }
                    case StepAction st:
                        memory.Write(0x49F0, memory.Position.X);
                        memory.Write(0x49F1, memory.Position.Y);
                        SetPosition(memory, st.PositionFrom ?? s.Name);
                        break;
                    case ForwardKeyAction:
                    {
                        // 0x16D58: $6DD5 = 1 only for a step off the grid through a face that is not impassable.
                        var pos = memory.Position;
                        bool leaves;
                        if (encounter.Map is MapPlanes { Planes.Length: >= MapPlanes.Size } planes)
                            leaves = GoldBox.Core.Doors.DoorMap.LeavesMap(planes.Planes, pos.X, pos.Y, pos.Facing);
                        else
                        {
                            int x = pos.X + pos.Facing switch { 2 => 1, 6 => -1, _ => 0 };
                            int y = pos.Y + pos.Facing switch { 0 => -1, 4 => 1, _ => 0 };
                            leaves = x is < 0 or > 15 || y is < 0 or > 15;
                            at.Notes.Add("forward key: no map planes, $6DD5 from the grid alone");
                        }
                        memory.Write(0x6DD5, (ushort)(leaves ? 1 : 0));
                        break;
                    }
                    case MoveAction mv:
                        SetPosition(memory, mv.PositionFrom ?? s.Name);
                        break;
                    case HostDrawsAction:
                        if (host.Rng is { } rng)
                            rng.Skip(hostDraws);
                        break;
                    case AnswersAction ans:
                        foreach (var c in ans.Choices)
                            host.MenuChoices.Enqueue(c);
                        break;
                }
                if (at.Stop is not (EclStopReason.Exit or EclStopReason.NewScript))
                    break; // the game is waiting (or the VM failed): later inputs do not apply
            }
        }
        catch (RollNeeded n)
        {
            at.RollNeeded = n.Range;
        }
        return at;
    }

    /// <summary>The current map's planes from <c>GEO&lt;disk&gt;.DAX</c> (block $49C5, 2-byte header dropped), or null.</summary>
    private MapPlanes? GeoMap(EclMemory memory) =>
        Content.LoadMap(memory.Disk, memory.Read(0x49C5)) is { } m ? new MapPlanes(m.PlanesArray) : null;

    private void SetPosition(EclMemory memory, string capture)
    {
        var after = new DosboxVmState(CapturePath(capture, "after"));
        var p = after.Position;
        memory.Position.X = p.X;
        memory.Position.Y = p.Y;
        memory.Position.Facing = p.Facing;
        memory.Position.WallAhead = p.Wall;
        memory.Position.EventByte = p.Event;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // State captures
    // -----------------------------------------------------------------------------------------------------------------

    private CaptureReport CompareState(CaptureScenario s)
    {
        var after = new DosboxVmState(CapturePath(s.Name, "after"));
        var draws = s.Actions.OfType<HostDrawsAction>().FirstOrDefault();
        int lo = draws?.Min ?? 0, hi = draws?.Max ?? 0;

        (CaptureReport Report, int Score)? best = null;
        for (int k = lo; k <= hi; k++)
        {
            var at = Run(s, k, null);
            var rep = Evaluate(s, at, after);
            if (draws is not null)
                rep.HostDraws = k;
            int score = rep.Divergences.Count;
            if (best is null || score < best.Value.Score)
                best = (rep, score);
            if (score == 0)
                break;
        }
        var report = best!.Value.Report;
        if (draws is not null)
            report.Notes.Add($"host draws ({draws.Why}) searched in {lo}..{hi}");
        if (TraceOut is not null)
            Run(s, report.HostDraws ?? 0, null, new JsonlTraceWriter(TraceOut));
        return report;
    }

    private CaptureReport Evaluate(CaptureScenario s, Attempt at, DosboxVmState after)
    {
        var vm = at.Vm;
        var rep = new CaptureReport { Name = s.Name, VmSteps = at.Sink.Steps.Count, StopReason = at.Stop.ToString() };
        rep.Notes.AddRange(at.Notes);
        var clock = at.Host.Calls.Where(c => c.StartsWith("Clock ", StringComparison.Ordinal)).Select(c => c[6..]).ToList();
        if (clock.Count > 0)
            rep.Notes.Add("CLOCK minutes passed to the host: " + string.Join(", ", clock));
        foreach (var (range, value) in at.Host.RandomLog)
            rep.Rolls.Add($"{range}→{value}");

        if (s.AllHost)
        {
            var before = new DosboxVmState(CapturePath(s.Name, "before"));
            foreach (var a in DosboxVmState.Addresses())
                if (before.Cell(a) != after.Cell(a))
                    rep.HostOwned.Add($"${a:X4} {before.Cell(a)} → {after.Cell(a)} (inside the host call at PC {before.Pc:X4})");
            if (before.RandSeed != after.RandSeed)
                rep.HostOwned.Add($"RandSeed advanced {TurboPascalRandom.StepsBetween(before.RandSeed, after.RandSeed)?.ToString(CultureInfo.InvariantCulture) ?? "?"} draws");
            rep.Outcome = at.Sink.Steps.Count == 0 && before.Pc == after.Pc ? CaptureOutcome.NoVmSteps : CaptureOutcome.Diverged;
            return rep;
        }

        // Cells.
        foreach (var a in DosboxVmState.Addresses())
        {
            var want = after.Cell(a)!.Value;
            var got = vm.Memory.Cell(a)!.Value;
            if (want == got)
                continue;
            var host = s.HostCells.FirstOrDefault(h => a >= h.From && a <= h.To);
            var line = $"${a:X4} VM {got}, DOSBox {want} ({WhoWrote(at, a)})";
            if (host.Why is not null)
                rep.HostOwned.Add(line + " — " + host.Why);
            else
                rep.Divergences.Add(line);
        }

        // DS variables.
        void Check(string what, string vmValue, string boxValue)
        {
            if (vmValue != boxValue)
                rep.Divergences.Add($"{what}: VM {vmValue}, DOSBox {boxValue}");
        }
        Check("PC", vm.Pc.ToString("X4", CultureInfo.InvariantCulture), after.Pc.ToString("X4", CultureInfo.InvariantCulture));
        Check("opcode", vm.Opcode.ToString("X2", CultureInfo.InvariantCulture), after.Opcode.ToString("X2", CultureInfo.InvariantCulture));
        Check("previous opcode", vm.PreviousOpcode.ToString("X2", CultureInfo.InvariantCulture), after.PreviousOpcode.ToString("X2", CultureInfo.InvariantCulture));
        Check("flags", JsonlTraceWriter.FlagString(vm.Flags), JsonlTraceWriter.FlagString(after.Flags));
        Check("GOSUB stack", vm.StackDepth == 0 ? "empty" : "non-empty", after.GosubStackEmpty ? "empty" : "non-empty");
        Check("disk", vm.Memory.Disk.ToString(CultureInfo.InvariantCulture), after.Disk.ToString(CultureInfo.InvariantCulture));
        Check("script", vm.CurrentScript.ToString(CultureInfo.InvariantCulture), after.Script_.ToString(CultureInfo.InvariantCulture));
        var p = after.Position;
        var m = vm.Memory.Position;
        Check("position", $"{m.X},{m.Y} f{m.Facing}", $"{p.X},{p.Y} f{p.Facing}");
        Check("wall ahead / event byte ($C04E/$C04F)", $"{m.WallAhead}/{m.EventByte}", $"{p.Wall}/{p.Event}");
        if (at.Host.Rng is { } rng)
        {
            if (rng.Seed != after.RandSeed)
            {
                var extra = TurboPascalRandom.StepsBetween(rng.Seed, after.RandSeed, 100_000);
                rep.Divergences.Add($"RandSeed: VM {rng.Seed:X8} after {rng.Draws} draws, DOSBox {after.RandSeed:X8}" +
                                    (extra is { } e ? $" ({e} more draws)" : ""));
            }
        }
        else
        {
            rep.Notes.Add("no RandSeed in the starting state (save load): rolls not checked");
        }

        rep.FirstDivergence = rep.Divergences.FirstOrDefault();
        rep.Outcome = rep.Divergences.Count == 0
            ? (at.Sink.Steps.Count == 0 ? CaptureOutcome.NoVmSteps : CaptureOutcome.Match)
            : CaptureOutcome.Diverged;
        return rep;
    }

    private static string WhoWrote(Attempt at, ushort a)
    {
        for (int i = at.Sink.Steps.Count - 1; i >= 0; i--)
        {
            var st = at.Sink.Steps[i];
            if (st.Writes.Any(w => w.Address == a && w.Region != "char"))
                return $"last written by {EclOpcodes.Name(st.Op)} at {st.Pc:X4}";
        }
        return "not written by the VM";
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Trace captures
    // -----------------------------------------------------------------------------------------------------------------

    private CaptureReport CompareTrace(CaptureScenario s)
    {
        var trace = s.TraceFiles.SelectMany(f => E9TraceLine.ReadFile(Path.Combine(FixturesDir, f))).ToList();
        int attempts = 0;
        (List<ushort> Prefix, Attempt At, int Matched)? best = null;
        List<int> viableCounts = [];

        int Matched(Attempt at, out bool mismatch)
        {
            int n = 0;
            mismatch = false;
            foreach (var st in at.Sink.Steps)
            {
                if (n >= trace.Count)
                {
                    mismatch = true;
                    break;
                }
                var t = trace[n];
                if (t.Pc != st.Pc || t.Op != st.Op || t.Flags != E9TraceLine.PackFlags(st.FlagsIn))
                {
                    mismatch = true;
                    break;
                }
                n++;
            }
            return n;
        }

        bool Search(List<ushort> prefix)
        {
            if (++attempts > MaxAttempts)
                return false;
            var at = Run(s, 0, prefix);
            int n = Matched(at, out bool mismatch);
            if (best is null || n > best.Value.Matched)
                best = (new List<ushort>(prefix), at, n);
            if (mismatch)
                return false;
            if (at.RollNeeded is not { } range)
                return n == trace.Count; // complete run that follows the whole trace
            int viable = 0;
            for (ushort v = 0; v < range; v++)
            {
                prefix.Add(v);
                var probe = Run(s, 0, prefix);
                attempts++;
                Matched(probe, out bool bad);
                if (!bad)
                {
                    viable++;
                    if (Search(prefix))
                    {
                        viableCounts.Insert(0, viable);
                        return true;
                    }
                }
                prefix.RemoveAt(prefix.Count - 1);
                if (attempts > MaxAttempts)
                    return false;
            }
            return false;
        }

        bool found = Search([]);
        var (bestPrefix, bestAt, matched) = best!.Value;
        var rep = new CaptureReport
        {
            Name = s.Name,
            VmSteps = bestAt.Sink.Steps.Count,
            StopReason = bestAt.RollNeeded is { } rn ? $"needs a roll (range {rn})" : bestAt.Stop.ToString(),
            TraceMatched = matched,
            TraceTotal = trace.Count,
        };
        rep.Notes.AddRange(bestAt.Notes);
        foreach (var (range, value) in bestAt.Rolls)
            rep.Rolls.Add($"{range}→{value}");
        if (found && viableCounts.Count > 0)
            rep.Notes.Add("values that stay on the trace up to the next roll, per roll: " + string.Join(", ", viableCounts));
        if (attempts > MaxAttempts)
            rep.Notes.Add($"roll search stopped after {MaxAttempts} replays");

        if (found)
        {
            rep.Outcome = CaptureOutcome.Match;
        }
        else
        {
            rep.Outcome = CaptureOutcome.Diverged;
            rep.FirstDivergence = DescribeTraceDivergence(trace, bestAt.Sink.Steps, matched);
            rep.Divergences.Add(rep.FirstDivergence);
        }
        if (TraceOut is not null)
            Run(s, 0, bestPrefix, new JsonlTraceWriter(TraceOut));
        return rep;
    }

    private static string DescribeTraceDivergence(List<E9TraceLine> trace, List<EclTraceStep> steps, int n)
    {
        var sb = new StringBuilder();
        if (n < steps.Count && n < trace.Count)
        {
            var st = steps[n];
            sb.Append(CultureInfo.InvariantCulture,
                $"instruction {n}: VM {st.Pc:X4} {st.Op:X2} {JsonlTraceWriter.FlagString(st.FlagsIn)} ({EclOpcodes.Name(st.Op)}), " +
                $"DOSBox {trace[n].Pc:X4} {trace[n].Op:X2} {trace[n].FlagString}");
        }
        else if (n >= steps.Count)
        {
            sb.Append(CultureInfo.InvariantCulture, $"VM stopped after {steps.Count} instructions; DOSBox continues with {(n < trace.Count ? trace[n].ToString() : "(end)")}");
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"trace ends after {trace.Count} instructions; VM continues at {steps[n].Pc:X4}");
        }
        sb.Append("; context:");
        for (int i = Math.Max(0, n - 5); i <= n + 3; i++)
        {
            string t = i < trace.Count ? $"{trace[i].Pc:X4} {trace[i].Op:X2} {trace[i].FlagString}" : "-";
            string v = i < steps.Count ? $"{steps[i].Pc:X4} {steps[i].Op:X2} {JsonlTraceWriter.FlagString(steps[i].FlagsIn)} {EclOpcodes.Name(steps[i].Op)}" : "-";
            sb.Append(CultureInfo.InvariantCulture, $"\n    {(i == n ? ">" : " ")}{i,4}  DOSBox {t,-16} VM {v}");
        }
        return sb.ToString();
    }

    private sealed class TeeSink(IEclTraceSink a, IEclTraceSink b) : IEclTraceSink
    {
        public void Step(EclTraceStep step) { a.Step(step); b.Step(step); }
        public void Event(string kind, IReadOnlyDictionary<string, string> fields) { a.Event(kind, fields); b.Event(kind, fields); }
    }
}
