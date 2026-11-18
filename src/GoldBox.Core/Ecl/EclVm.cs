using System.Globalization;
using GoldBox.Core.Ecl.Profiles;

namespace GoldBox.Core.Ecl;

/// <summary>Supplies script blocks (whole DAX blocks, prefix included) by disk and script id.</summary>
public interface IEclScriptSource
{
    /// <summary>The block, or null when <c>ECL&lt;disk&gt;.DAX</c> has no block <paramref name="script"/>.</summary>
    byte[]? LoadBlock(int disk, int script);
}

/// <summary>Why a run ended.</summary>
public enum EclStopReason
{
    /// <summary>EXIT (0x00), or PROGRAM 9 (which ends the run like EXIT).</summary>
    Exit,
    /// <summary>NEWECL (0x20): the new script is loaded; its vectors 4, 0, 1 are next (<see cref="EclVm.FollowScriptChanges"/>).</summary>
    NewScript,
    /// <summary>The party-defeated flag DS:49FF was set (<see cref="EclVm.ExternalStop"/>).</summary>
    ExternalStop,
    /// <summary>The step budget ran out.</summary>
    Budget,
    /// <summary>A VM fault: PC outside the script area, a decode past the end, a missing script, DIV by zero.</summary>
    Fault,
    /// <summary>Opcode 0x1F or ≥ 0x3E with <see cref="UnhandledOpcodeMode.StopWithDiagnostic"/>.</summary>
    UnhandledOpcode,
    /// <summary>The host had no answer for an input request (<see cref="EclAwaitingInput"/>); the game would be waiting.</summary>
    AwaitingInput,
}

public sealed record EclRunResult(EclStopReason Reason, int Steps, string? Message = null);

/// <summary>A condition the original program does not survive or leaves undefined.</summary>
public sealed class EclVmFault(string message) : Exception(message);

/// <summary>
/// Headless ECL virtual machine for Pool of Radiance (DOS v1.3), built from docs/re/ecl-format.md. Instructions are
/// decoded at run time from the live script area at the PC (scripts may write into it). Operand slots and string
/// slots persist from one instruction to the next as in the program, so a type-0 operand used as an address keeps
/// the previous high byte. Every effect taken from Lee only is marked <c>[?]</c> with its opcode row; the choices
/// the spec leaves open live in <see cref="EclVmPolicy"/>. The dialect's data (script layout, opcode and IF-skip
/// tables, PROGRAM ids that end the run) comes from the <see cref="Profile"/>; the handlers here are its
/// <see cref="EclHandlerSet.Por13"/> code.
/// </summary>
public sealed class EclVm
{
    /// <summary>Flag indices, in DS:6F78..6F7D order.</summary>
    public const int FlagEq = 0, FlagNe = 1, FlagLt = 2, FlagGt = 3, FlagLe = 4, FlagGe = 5;

    private const int SlotCount = 3 + 255 + 2; // largest fixed part + largest count + spare
    private readonly byte[] _slotType = new byte[SlotCount];
    private readonly byte[] _slotLo = new byte[SlotCount];
    private readonly byte[] _slotHi = new byte[SlotCount];
    /// <summary>String slot used by each operand (0 = none): the k-th text operand of the instruction fills slot k.</summary>
    private readonly int[] _slotString = new int[SlotCount];
    /// <summary>String slots DS:6FC8 + 256·k (k from 1); they keep their text until a later text operand replaces it.</summary>
    private readonly string[] _strings = Enumerable.Repeat("", SlotCount + 1).ToArray();
    private int _textOperands;
    /// <summary>Operands loaded by the current instruction, as loaded (a variadic tail reuses slots 1..n).</summary>
    private readonly List<EclTraceOperand> _loaded = [];

    private readonly Stack<ushort> _gosub = new();
    /// <summary>Opcode dispatched by the current and by the previous step (not the IF-skipped one; CLOCK uses it).</summary>
    private byte _lastHandler = 0xFF, _previousHandler = 0xFF;
    /// <summary>Value of the last memory read made by a numeric COMPARE operand fetch: the reader's stack slot.</summary>
    private ushort? _compareReadResidue;
    private EclTraceStep? _step;
    private long _seq;
    private bool _pendingNotLoaded;

    public EclMemory Memory { get; }
    public IEclHost Host { get; }
    public EclVmPolicy Policy => Memory.Policy;
    /// <summary>The VM profile (docs/module-format.md §5.1): the memory's, so the two never disagree.</summary>
    public EclProfile Profile => Memory.Profile;
    public IEclScriptSource? Scripts { get; set; }
    public IEclTraceSink? Trace { get; set; }

    /// <summary>Program counter (DS:49ED).</summary>
    public ushort Pc { get; set; }
    /// <summary>The five entry vectors (DS:49E3..49EB), raw 16-bit fields.</summary>
    public ushort[] Vectors { get; }
    /// <summary>Compare flags = ≠ &lt; &gt; ≤ ≥ (DS:6F78..6F7D).</summary>
    public bool[] Flags { get; } = new bool[6];
    public int StackDepth => _gosub.Count;
    public IReadOnlyCollection<ushort> GosubStack => _gosub;

    /// <summary>String slot <paramref name="k"/> (1-based), as the last text operand that used it left it.</summary>
    public string StringSlot(int k) => _strings[k];

    /// <summary>Script id now loaded.</summary>
    public int CurrentScript { get; private set; } = -1;
    /// <summary>Disk number the current script was loaded from.</summary>
    public int CurrentDisk { get; set; }
    /// <summary>Script id given to the last NEWECL, until <see cref="FollowScriptChanges"/> runs its vectors.</summary>
    public int? PendingScript { get; private set; }

    /// <summary>Stop flag (DS:442E).</summary>
    public bool StopFlag { get; private set; }
    /// <summary>
    /// Script-changed flag (DS:442F): set by NEWECL and, as in the program, not cleared by the run loop; only
    /// <see cref="FollowScriptChanges"/> (0x3FFA) and <see cref="RunLoadedGame"/> (area entry, 0x4062) clear it. After
    /// running vector 0 or 1 for a command, a caller follows the switch whenever this is set (0x41A1, 0x4230, 0x4159).
    /// </summary>
    public bool ScriptChanged { get; private set; }
    private bool _newEclThisRun;
    /// <summary>Between a Load (0x403E) and the first command read: DS:4436 is stale.</summary>
    private bool _afterLoadBeforeCommand;
    /// <summary>DS:49FF "party defeated": while set, the run loop stops (set by DAMAGE and the after-combat check in the game).</summary>
    public bool ExternalStop { get; set; }
    /// <summary>
    /// DS:49F8 "loading a save": set by the main menu's Load command; the next script init then keeps $4A00–$4A1F and
    /// $6E79–$6E82, and clears this flag.
    /// </summary>
    public bool LoadingSave { get; set; }

    /// <summary>Opcode now executing / previous (DS:6F7F / 6F7E).</summary>
    public byte Opcode { get; set; }
    public byte PreviousOpcode { get; set; }
    public int CurrentVector { get; private set; } = -1;

    /// <summary>Diagnostics that occurred outside a traced step.</summary>
    public List<string> Diagnostics { get; } = [];
    /// <summary>Count of all diagnostics since construction.</summary>
    public int DiagnosticCount { get; private set; }

    /// <summary>The party opcodes read from the program (LOADCHAR, CHKPARTY, SPELL, ROB, DAMAGE); draws go through the host.</summary>
    public EclPartyRules PartyRules { get; }

    /// <summary>The effect handlers DAMAGE runs (<see cref="EclPartyRules.EffectTrigger"/>).</summary>
    public Game.EffectHandlers Effects { get; }

    /// <param name="memory">The VM memory; it carries the profile (<see cref="EclMemory.Profile"/>).</param>
    /// <param name="host">The game side.</param>
    /// <param name="profile">The VM profile, when the caller names one: it must be the memory's.</param>
    public EclVm(EclMemory memory, IEclHost host, EclProfile? profile = null)
    {
        if (profile is not null && !ReferenceEquals(profile, memory.Profile))
            throw new ArgumentException(
                $"VM profile {profile.Name} differs from the memory's ({memory.Profile.Name})", nameof(profile));
        Memory = memory;
        Host = host;
        Vectors = new ushort[Profile.VectorCount];
        PartyRules = new EclPartyRules(memory, range =>
        {
            ushort r = 0;
            HostCall("Random", [Num(range)], () => Num(r), () => r = Host.Random((ushort)range));
            return r;
        });
        PartyRules.Diagnostic += Diag;
        // DAMAGE's saving throw (class 12) and to-hit (class 16) effects: the program's handlers (0x2B1E4).
        Effects = new Game.EffectHandlers(n => (ushort)PartyRules.Random(n));
        Effects.AttachTo(PartyRules);
        PartyRules.Damaged += d => HostCall("DamageReport", [d.Target.Name, Num(d.Damage), d.Dies ? "dies" : "hit"], null,
            () => Host.DamageReport(d));
        Memory.Written += w => _step?.Writes.Add(w);
        Memory.Diagnostic += Diag;
        Memory.WallSetRequested += (slot, set) =>
            HostCall("LoadWallSet", [Num(slot), Num(set)], null, () => Host.LoadWallSet(slot, set));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Script loading
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Disk number for the next script load: DS:5376 (= VM $6E12).</summary>
    public int DiskForLoad() => Memory.Disk;

    /// <summary>Loads script <paramref name="script"/> from <see cref="Scripts"/> (disk from <see cref="DiskForLoad"/>) and runs the script init.</summary>
    public void LoadScript(int script, int? disk = null)
    {
        int d = disk ?? DiskForLoad();
        if (Scripts is null)
            throw new EclVmFault($"no script source to load script {script}");
        var block = Scripts.LoadBlock(d, script)
            ?? throw new EclVmFault($"no script {script} on disk {d}");
        LoadScriptBlock(block, script, d);
    }

    /// <summary>Copies a block (minus its prefix) to $9900 and runs the script init (<see cref="InitScript"/>).</summary>
    public void LoadScriptBlock(ReadOnlySpan<byte> block, int script, int disk, bool? loadingSave = null)
    {
        Memory.LoadScriptBlock(block);
        CurrentScript = script;
        CurrentDisk = disk;
        if (loadingSave is { } l)
            LoadingSave = l;
        InitScript();
        Trace?.Event("load", new Dictionary<string, string>
        {
            ["script"] = script.ToString(CultureInfo.InvariantCulture),
            ["disk"] = disk.ToString(CultureInfo.InvariantCulture),
            ["vectors"] = string.Join(",", Vectors.Select(JsonlTraceWriter.Hex)),
        });
    }

    /// <summary>
    /// Script init (GAME.OVR 0x7689) on the script already in the area: PC = $9900, GOSUB stack and the six flags
    /// cleared, $6DE1 = 255, $6DD2 = $6DD3 = 0, $49E5 = 0, the five vectors read, $49E6 = 1; unless
    /// <see cref="LoadingSave"/> (DS:49F8) is set, $4A00–$4A1F and $6E79–$6E82 are zeroed; DS:49F8 is cleared. It also
    /// resets the 3D-view colour bytes behind $C059 / $C05F (DS:6F68 = 0x41, DS:6F69 = 9, 0x76A3).
    /// </summary>
    public void InitScript()
    {
        _gosub.Clear();
        Memory.Ds84D2 = false; // 0x7699
        Memory.Ds84D3 = false;
        Array.Clear(Flags);
        Memory.SpecialWriteValues[0xC059] = 0x41;
        Memory.SpecialWriteValues[0xC05F] = 0x09;
        Memory.Write(0x6DE1, 255);
        Memory.Write(0x6DD2, 0);
        Memory.Write(0x6DD3, 0);
        Memory.Write(0x49E5, 0);
        ReadVectors();
        Memory.Write(0x49E6, 1);
        if (!LoadingSave)
        {
            for (ushort a = 0x4A00; a <= 0x4A1F; a++)
                Memory.Write(a, 0);
            for (ushort a = 0x6E79; a <= 0x6E82; a++)
                Memory.Write(a, 0);
        }
        LoadingSave = false;
    }

    /// <summary>
    /// Reads the vectors from the script area. The loader sets PC = $9900 and five times loads one operand starting
    /// one byte further on (that byte is ignored).
    /// </summary>
    public void ReadVectors()
    {
        Pc = Profile.ScriptBase;
        for (int k = 0; k < Profile.VectorCount; k++)
        {
            _textOperands = 0;
            LoadOperands(1, 1);
            Vectors[k] = RawWord(1); // raw 16-bit field, not dereferenced
        }
    }

    /// <summary>Marks a script as current without loading (state taken from a save or a capture).</summary>
    public void SetCurrentScript(int script, int disk)
    {
        CurrentScript = script;
        CurrentDisk = disk;
    }

    /// <summary>
    /// The main menu's "Load game" (save already copied into memory): DS:49F8 set, the script init on the saved
    /// script (no file read), then vector 4.
    /// </summary>
    public EclRunResult RunLoadedGame(int maxSteps = 100_000)
    {
        // Area entry 0x403E: DS:4430 and DS:442F cleared (0x404E, 0x4062). A NEWECL in this vector 4 runs nothing
        // further now: the program goes on to read a command with DS:442F still set, so the next command's vector 0
        // (or 1) is followed by FollowScriptChanges (ecl-format.md "Steps, doors and script switches").
        if (Memory.Party is { } party)
        {
            party.ChangedByScript = false;
            party.Selected = null; // DS:4436 is stale until the command read after vector 4 (CommandRead)
        }
        _afterLoadBeforeCommand = true;
        ScriptChanged = false;
        Memory.FilesLoadedSinceSwitch = false; // DS:4431 := 0 (0x4053)
        LoadingSave = true;
        InitScript();
        Trace?.Event("restore", new Dictionary<string, string>
        {
            ["script"] = CurrentScript.ToString(CultureInfo.InvariantCulture),
            ["disk"] = CurrentDisk.ToString(CultureInfo.InvariantCulture),
            ["vectors"] = string.Join(",", Vectors.Select(JsonlTraceWriter.Hex)),
        });
        var r = RunVector(Profile.InitVector, maxSteps);
        CommandRead();
        return r;
    }

    /// <summary>
    /// The area loop reads a command (0x40DB / 0x4179): the current character is remembered as the selected one
    /// (DS:4436), which EXIT and PROGRAM restore after a LOADCHAR. Call before running vector 0 or 1 for a key.
    /// </summary>
    public void CommandRead()
    {
        _afterLoadBeforeCommand = false;
        if (Memory.Party is { } party)
            party.Selected = party.Current;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Running
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Run loop (GAME.OVR 0x3EB7): PC = vector, execute until the stop flag or the budget.</summary>
    public EclRunResult RunVector(int vector, int maxSteps = 100_000)
    {
        CurrentVector = vector;
        return RunFrom(Vectors[vector], maxSteps);
    }

    /// <summary>Runs from an address until stopped.</summary>
    public EclRunResult RunFrom(ushort pc, int maxSteps = 100_000)
    {
        Pc = pc;
        StopFlag = false;
        _newEclThisRun = false;
        Trace?.Event("run", new Dictionary<string, string>
        {
            ["script"] = CurrentScript.ToString(CultureInfo.InvariantCulture),
            ["vec"] = CurrentVector.ToString(CultureInfo.InvariantCulture),
            ["pc"] = JsonlTraceWriter.Hex(pc),
        });
        int steps = 0;
        EclRunResult result;
        while (true)
        {
            if (StopFlag)
            {
                result = new EclRunResult(_newEclThisRun ? EclStopReason.NewScript : EclStopReason.Exit, steps);
                break;
            }
            if (ExternalStop)
            {
                result = new EclRunResult(EclStopReason.ExternalStop, steps);
                break;
            }
            if (steps >= maxSteps)
            {
                result = new EclRunResult(EclStopReason.Budget, steps);
                break;
            }
            steps++;
            var r = Step();
            if (r is not null)
            {
                result = r with { Steps = steps };
                break;
            }
        }
        Trace?.Event("end", new Dictionary<string, string>
        {
            ["reason"] = result.Reason.ToString(),
            ["steps"] = result.Steps.ToString(CultureInfo.InvariantCulture),
            ["pc"] = JsonlTraceWriter.Hex(Pc),
        });
        return result;
    }

    /// <summary>VM $6DD5: set by the forward-step key when the step leaves the 16×16 map (geo-format.md "Edges").</summary>
    public const ushort EdgeStepCell = 0x6DD5;

    /// <summary>
    /// The event accessor (0x2EE71) for the party's square on the map in memory, for the two re-reads at a script
    /// switch: at the start of each pass (argument false: the old map is still in memory) and in the view redraw after
    /// vector 4 (argument true: vector 4's LOADFILES has loaded the new map; 0x3FF5 → 0x2DD22). Null leaves
    /// <see cref="EclPartyPosition.EventByte"/> as it is. The second read happens only when the redraw runs
    /// (DS:49F9 / DS:49FA / DS:4431 test at 0x3FD2–0x3FF5) and the view is 3D (0x2DCF4's own test).
    /// </summary>
    public Func<bool, byte>? ScriptSwitchEventByte { get; set; }

    /// <summary>
    /// After a run that switched scripts (routine 0x3EFA), while the script-changed flag DS:442F is set: the new script
    /// is already loaded and initialised by NEWECL. Each pass reads the event byte of the party's square again
    /// (<see cref="ScriptSwitchEventByte"/>), clears the edge-step cell $6DD5 (0x3F00–0x3F16), saves the current
    /// character, sets the view mode DS:49FA = 1 and runs vector 4. The flag is **not** tested after vector 4: the view
    /// mode becomes 2/3/4 for blocks 25/26/27 with $49E6 = 0, the redraw re-reads the event byte, the flag is cleared
    /// (0x3FFA) and vector 0 of the script now loaded runs (after a NEWECL in vector 4 that is the newest script's, whose
    /// vector 4 is skipped); unless vector 0 set the flag again, vector 1 runs and the saved current character is
    /// restored (0x4016). Repeats while the flag is set (at most <paramref name="maxSwitches"/> passes).
    /// </summary>
    public EclRunResult FollowScriptChanges(int maxStepsPerRun = 100_000, int maxSwitches = 32)
    {
        var last = new EclRunResult(EclStopReason.Exit, 0);
        for (int i = 0; i < maxSwitches && ScriptChanged; i++)
        {
            LoadPending();
            if (ScriptSwitchEventByte?.Invoke(false) is { } ev)
                Memory.Position.EventByte = ev;
            Memory.Write(EdgeStepCell, 0);
            var saved = Memory.Party?.Current;
            Memory.PreviousViewMode = Memory.ViewMode; // DS:49F9 := DS:49FA (0x3F28)
            Memory.ViewMode = 1;
            last = RunVector(Profile.InitVector, maxStepsPerRun);
            if (last.Reason is not (EclStopReason.Exit or EclStopReason.NewScript))
                return last;
            LoadPending();
            if (ReadMem(0x49E6) == 0 && CurrentScript is >= 25 and <= 27)
                Memory.ViewMode = (byte)(CurrentScript - 23);
            // The redraw 0x2DCF4 runs (0x3FD2–0x3FF5) when the view mode stayed 1, or when the old mode was outdoors or
            // the new one is 1 and vector 4 loaded files (DS:4431); it re-reads the event byte only in the 3D view
            // (DS:49FA = 1 or $49E6 ≠ 0, 0x2DCFA–0x2DD27).
            byte before = Memory.PreviousViewMode, now = Memory.ViewMode;
            bool redraw = (before == 1 && now == 1) || ((before > 1 || now == 1) && Memory.FilesLoadedSinceSwitch);
            if (redraw && (now == 1 || ReadMem(0x49E6) != 0) && ScriptSwitchEventByte?.Invoke(true) is { } ev2)
                Memory.Position.EventByte = ev2;
            ScriptChanged = false;
            last = RunVector(0, maxStepsPerRun);
            if (last.Reason is not (EclStopReason.Exit or EclStopReason.NewScript))
                return last;
            if (ScriptChanged)
                continue;
            last = RunVector(1, maxStepsPerRun);
            if (Memory.Party is { } party)
                party.SetCurrent(saved);
            if (last.Reason is not (EclStopReason.Exit or EclStopReason.NewScript))
                return last;
        }
        return last;
    }

    /// <summary>Loads the script a NEWECL named when no script source was attached at the time.</summary>
    private void LoadPending()
    {
        if (PendingScript is { } next)
        {
            PendingScript = null;
            if (_pendingNotLoaded)
            {
                _pendingNotLoaded = false;
                LoadScript(next);
            }
        }
    }

    /// <summary>Executes one instruction. Returns a result when the step ended the run abnormally, else null.</summary>
    public EclRunResult? Step()
    {
        var step = new EclTraceStep
        {
            Seq = _seq++,
            Script = CurrentScript,
            Disk = CurrentDisk,
            Vector = CurrentVector,
            Pc = Pc,
        };
        Array.Copy(Flags, step.FlagsIn, 6);
        _step = step;
        _loaded.Clear();
        _textOperands = 0;
        EclRunResult? result = null;
        try
        {
            var op = FetchByte(Pc, "opcode");
            PreviousOpcode = Opcode;
            Opcode = op;
            _previousHandler = _lastHandler;
            _lastHandler = op;
            step.Op = op;
            result = Execute(op);
        }
        catch (EclVmFault f)
        {
            step.Diagnostics.Add("fault: " + f.Message);
            DiagnosticCount++;
            StopFlag = true;
            result = new EclRunResult(EclStopReason.Fault, 0, f.Message);
        }
        catch (EclAwaitingInput w)
        {
            result = new EclRunResult(EclStopReason.AwaitingInput, 0, w.Message);
        }
        step.Operands.AddRange(_loaded);
        Array.Copy(Flags, step.Flags, 6);
        step.StackDepth = _gosub.Count;
        step.NextPc = Pc;
        if (result is not null)
            step.End = result.Reason.ToString();
        else if (StopFlag)
            step.End = _newEclThisRun ? nameof(EclStopReason.NewScript) : nameof(EclStopReason.Exit);
        _step = null;
        Trace?.Step(step);
        return result;
    }

    private EclRunResult? Execute(byte op)
    {
        var info = Profile.Opcodes.Get(op);
        if (info is null)
        {
            // [?] ecl-format.md: 0x1F and >= 0x3E have no dispatcher case; the PC is not advanced (T item).
            Diag($"[?] opcode {op:X2} has no handler");
            if (Policy.UnhandledOpcode == UnhandledOpcodeMode.Hang)
                return null; // PC stays: the next step executes the same byte (until the budget runs out)
            return new EclRunResult(EclStopReason.UnhandledOpcode, 0, $"opcode {op:X2} at {Pc:X4}");
        }

        if (info.Variadic == EclVariadic.None)
            LoadOperands(1, info.FixedCount);

        switch (op)
        {
            // ---- Control flow (P) --------------------------------------------------------------------------------
            case EclOpcodes.Exit:
                DoExit();
                return null;
            case EclOpcodes.Goto:
                Pc = TargetWord(1);
                return null;
            case EclOpcodes.Gosub:
            {
                var target = TargetWord(1);
                _gosub.Push(Pc);
                Pc = target;
                return null;
            }
            case EclOpcodes.Return:
                // P: steps past the opcode (done by the loader); empty stack falls through
                // (EclReturnOnEmptyStack.FallsThrough, the one rule the Por13 handlers have).
                if (_gosub.Count > 0)
                    Pc = _gosub.Pop();
                return null;
            case EclOpcodes.Compare:
                DoCompare();
                return null;
            case >= EclOpcodes.IfEq and <= EclOpcodes.IfGe:
            {
                bool taken = Flags[op - EclOpcodes.IfEq];
                _step!.IfTaken = taken;
                if (!taken)
                    SkipNextInstruction();
                return null;
            }
            case EclOpcodes.OnGoto:
            case EclOpcodes.OnGosub:
            {
                // P (0x263E): selector and count are the low bytes of their values, fetched before the targets are
                // loaded into slots 1..count.
                int sel = 0;
                int count = LoadVariadic(info, () => sel = Value(1) & 0xFF);
                if (sel < count)
                {
                    var target = TargetWord(1 + sel);
                    if (op == EclOpcodes.OnGosub)
                        _gosub.Push(Pc);
                    Pc = target;
                }
                return null;
            }
            case EclOpcodes.NewEcl:
            {
                // P (0x1917): $49F2 = current script id, load ECL<disk>.DAX block v and run the script init at once;
                // the run stops and the caller runs the new script's vectors 4, 0, 1.
                int script = Value(1);
                Memory.Write(0x49F2, (ushort)CurrentScript);
                Memory.FilesLoadedSinceSwitch = false; // DS:4431 := 0 (0x19AA)
                HostCall("ScriptChanging", [Num(script)], null, () => Host.ScriptChanging((ushort)script));
                if (Scripts is null)
                {
                    Diag($"no script source: script {script} not loaded until FollowScriptChanges");
                    _pendingNotLoaded = true;
                }
                else
                {
                    LoadScript(script);
                    _pendingNotLoaded = false;
                }
                PendingScript = script;
                StopFlag = true;
                ScriptChanged = true;
                _newEclThisRun = true;
                return null;
            }
            case EclOpcodes.Save:
                // P (0xFD8): a = v; a source of type >= 0x80 (0x80, 0x81, and any other high type) stores string
                // slot 1 at a (SAVE-text rules, EclMemory.WriteText).
                if (_slotType[1] >= 0x80)
                    Memory.WriteText(DestWord(2), _strings[1]);
                else
                    Memory.Write(DestWord(2), Value16(1));
                return null;

            // ---- Arithmetic, logic, tables (P) --------------------------------------------------------------------
            case EclOpcodes.Add:
                Memory.Write(DestWord(3), (ushort)(Value(1) + Value(2))); // 16-bit wrap
                return null;
            case EclOpcodes.Sub:
            {
                int v1 = Value(1), v2 = Value(2);
                Memory.Write(DestWord(3), (ushort)(v2 - v1)); // P: second minus first, 16-bit wrap
                return null;
            }
            case EclOpcodes.Mul:
                Memory.Write(DestWord(3), (ushort)(Value(1) * Value(2))); // low 16 bits
                return null;
            case EclOpcodes.Div:
            {
                int v1 = Value(1), v2 = Value(2);
                var dst = DestWord(3);
                if (v2 == 0)
                {
                    // P: an 8086 div by zero (CPU interrupt 0; Turbo Pascal run-time error 200). [?] What the game's
                    // handler then does is not traced; the VM stops the run as a fault.
                    throw new EclVmFault("DIV by zero: CPU divide fault (run-time error 200) [?] handler not traced");
                }
                Memory.Write(dst, (ushort)(v1 / v2)); // unsigned quotient
                return null;
            }
            case EclOpcodes.And:
            case EclOpcodes.Or:
            {
                // P (0x1B35): result = low byte of v1 AND/OR v2; flags from the flag setter on (0, result).
                int v1 = Value(1), v2 = Value(2);
                int r = (op == EclOpcodes.And ? v1 & v2 : v1 | v2) & 0xFF;
                SetFlags(0.CompareTo(r));
                Memory.Write(DestWord(3), (ushort)r);
                return null;
            }
            case EclOpcodes.CmpAnd:
            {
                // P (0x1833): all six flags cleared, then "=" if both pairs are equal (16-bit), else "≠".
                int a1 = Value(1), b1 = Value(2), a2 = Value(3), b2 = Value(4);
                Array.Clear(Flags);
                if (a1 == b1 && a2 == b2)
                    Flags[FlagEq] = true;
                else
                    Flags[FlagNe] = true;
                return null;
            }
            case EclOpcodes.GetTable:
            {
                // P (0x1BA4): value at (a1 + low byte of v2), 16-bit add.
                var table = DestWord(1);
                int index = Value(2) & 0xFF;
                var dst = DestWord(3);
                Memory.Write(dst, ReadMem((ushort)(table + index)));
                return null;
            }
            case EclOpcodes.SaveTable:
            {
                // P (0x1C02): store v1 at (a2 + v3), full 16-bit index.
                int v = Value(1);
                var table = DestWord(2);
                int index = Value(3);
                Memory.Write((ushort)(table + index), (ushort)v);
                return null;
            }
            case EclOpcodes.Random:
            {
                // P (0xF7A): n = low byte of v, plus 1 unless 255; result = Random(n), stored as a byte.
                int n = Value(1) & 0xFF;
                if (n < 255)
                    n++;
                var dst = DestWord(2);
                ushort r = 0;
                HostCall("Random", [Num(n)], () => Num(r), () => r = Host.Random((ushort)n));
                if (r >= n)
                    Diag($"host returned {r} for Random({n}) (range 0..{n - 1})");
                Memory.Write(dst, (byte)r);
                return null;
            }

            // ---- Party (P) -----------------------------------------------------------------------------------------
            case EclOpcodes.PartyStr:
            {
                var dst = DestWord(1);
                ushort r = 0;
                if (Memory.Party is { } party)
                    r = PartyStrength(party);
                else
                {
                    Diag("PARTYSTR with no party attached: host answer used");
                    HostCall("PartyStrength", [], () => Num(r), () => r = Host.PartyStrength());
                }
                Memory.Write(dst, r);
                return null;
            }
            case EclOpcodes.PSurprise:
            {
                // P: a1 = 1 if a party member's class (record 0x2F) is ranger (4) or cleric/ranger (10), else 0; a2 = 0.
                ushort ranger = 0;
                if (Memory.Party is { } party)
                    ranger = (ushort)(party.Members.Any(m => m[0x2F] is 4 or 10) ? 1 : 0);
                else
                    Diag("PSURPRISE with no party attached: no ranger assumed");
                Memory.Write(DestWord(1), ranger);
                Memory.Write(DestWord(2), 0);
                return null;
            }
            case EclOpcodes.Surprise:
            {
                // P (0x240F): X = b4 + 2 − b1, Y = b2 + 2 − b3 (signed bytes), r1, r2 = 1d6; 2 if r2 ≤ Y, else 1 if
                // r1 ≤ X, else 0, written to $6DCB. [?] Which side each value describes is Lee's reading.
                var b = Values(1, 4).Select(x => x & 0xFF).ToArray();
                int x = (sbyte)(byte)(b[3] + 2 - b[0]);
                int y = (sbyte)(byte)(b[1] + 2 - b[2]);
                int r1 = DieRoll(6), r2 = DieRoll(6);
                int result = r2 <= y ? 2 : r1 <= x ? 1 : 0;
                Memory.Write(0x6DCB, (ushort)result);
                return null;
            }
            case EclOpcodes.FindItem:
            {
                // P (0x33B0): all flags cleared and "≠" set; "=" (and "≠" cleared) if any party member carries an
                // item whose byte 0x2E equals the low byte of v ([?] byte 0x2E presumably the item type).
                int item = Value(1) & 0xFF;
                bool found = false;
                if (Memory.Party is { } party)
                    found = party.Members.Any(m => m.Items.Any(it => it.Length > 0x2E && it[0x2E] == item));
                else
                {
                    Diag("FINDITEM with no party attached: host answer used");
                    HostCall("FindItem", [Num(item)], () => found ? "1" : "0", () => found = Host.FindItem((ushort)item));
                }
                Array.Clear(Flags);
                Flags[found ? FlagEq : FlagNe] = true;
                return null;
            }

            // ---- Menus (shape P, meaning of the result [?]) -------------------------------------------------------
            case EclOpcodes.VMenu:
            {
                // P (0x1C4E): the destination and the prompt (string slot 1) are taken before the tail load; the
                // choices then fill operand and string slots 1..count.
                ushort dst = 0;
                string prompt = "";
                int count = LoadVariadic(info, () => { dst = DestWord(1); prompt = _strings[1]; });
                var choices = Enumerable.Range(1, count).Select(Text).ToList();
                ushort r = 0;
                HostCall("VerticalMenu", [prompt, .. choices], () => Num(r), () => r = Host.VerticalMenu(prompt, choices));
                Memory.Write(dst, r);
                return null;
            }
            case EclOpcodes.HMenu:
            {
                // P (0x1E17): destination taken before the tail load; the choices fill slots 1..count.
                ushort dst = 0;
                int count = LoadVariadic(info, () => dst = DestWord(1));
                var choices = Enumerable.Range(1, count).Select(Text).ToList();
                ushort r = 0;
                HostCall("HorizontalMenu", [.. choices], () => Num(r), () => r = Host.HorizontalMenu(choices));
                Memory.Write(dst, r);
                return null;
            }
            case EclOpcodes.InputNum:
            {
                // P (0x1697): the length operand is loaded but never fetched; low 16 bits of the number stored.
                var dst = DestWord(2);
                int r = 0;
                HostCall("InputNumber", [], () => Num(r), () => r = Host.InputNumber());
                Memory.Write(dst, (ushort)r);
                return null;
            }
            case EclOpcodes.InputStr:
            {
                // P (0x16DA): length operand never fetched; 0x20–0x7A, at most 40, upper-cased, empty = one space.
                var dst = DestWord(2);
                string r = "";
                HostCall("InputString", [], () => r, () => r = Host.InputString());
                Memory.WriteText(dst, NormaliseInput(r));
                return null;
            }

            // ---- Host-delegated, Lee only (each row [?] in ecl-format.md) ------------------------------------------
            case EclOpcodes.Print:
            case EclOpcodes.PrintClr:
            {
                // [?] op 11/12 (L) for the display. P (0x174F): an operand of type < 0x80 is fetched as a number,
                // converted with Str (START.EXE 0x1C13: unsigned 16-bit, decimal, no padding) into string slot 1,
                // and printed from there (DOSBox: PRINTCLR 12345 then PRINT 5 shows "123455").
                string text;
                if (_slotType[1] < 0x80)
                    _strings[1] = text = Value16(1).ToString(CultureInfo.InvariantCulture);
                else
                    text = _strings[1];
                bool clear = op == EclOpcodes.PrintClr;
                HostCall(clear ? "PrintClear" : "Print", [text], null, () => Host.Print(text, clear));
                return null;
            }
            case EclOpcodes.PrintRet:
                HostCall("PrintReturn", [], null, Host.PrintReturn); // [?] op 33 (L)
                return null;
            case EclOpcodes.ClearBox:
                HostCall("ClearBox", [], null, Host.ClearBox); // [?] op 3D (L)
                return null;
            case EclOpcodes.Delay:
                HostCall("Delay", [], null, Host.Delay); // P: game speed × 225 ms (host)
                return null;
            case EclOpcodes.Who:
            {
                // P (0x3879): the pick menu writes the chosen record into DS:5D92 (DS:4430 not set: survives EXIT).
                var prompt = Text(1);
                int? pick = null;
                HostCall("PickCharacter", [prompt], () => pick?.ToString(CultureInfo.InvariantCulture) ?? "none",
                    () => pick = Host.PickCharacter(prompt));
                if (pick is { } k && Memory.Party is { } party)
                {
                    if (k == IEclHost.NoCharacter)
                    {
                        // Escape, then Select: DS:5D92 = nil (START 0x3976–0x3993).
                        party.CurrentIndex = -1;
                        Diag("WHO: Escape left no current character (DS:5D92 = nil)");
                    }
                    else if (k >= 0 && k < party.Members.Count)
                        party.CurrentIndex = k;
                    else
                        Diag($"WHO: host picked {k}, not a party-list member");
                }
                return null;
            }
            case EclOpcodes.Parley:
            {
                // [?] op 2C (L)
                var v = Values(1, 5);
                var dst = DestWord(6);
                ushort r = 0;
                HostCall("Parley", v.Select(Num).ToArray(), () => Num(r), () => r = Host.Parley(v[0], v[1], v[2], v[3], v[4]));
                Memory.Write(dst, r);
                return null;
            }
            case EclOpcodes.EncMenu:
            {
                // [?] op 29 (L): every field meaning from Lee.
                var menu = new EclEncounterMenu(Value16(1), Value16(2), Value16(3), DestWord(4), Values(5, 5),
                    [Text(10), Text(11), Text(12)], Value16(13), Value16(14));
                ushort? r = null;
                HostCall("EncounterMenu",
                    [Num(menu.Monster), Num(menu.Distance), Num(menu.Picture), JsonlTraceWriter.Hex(menu.DestinationAddress),
                     .. menu.Reactions.Select(Num), .. menu.DistanceTexts, Num(menu.Move1), Num(menu.Move2)],
                    () => r is { } x ? Num(x) : "none", () => r = Host.EncounterMenu(menu));
                if (r is { } result) // P (0x2C3E): a reaction value of 5 or more writes nothing
                    Memory.Write(menu.DestinationAddress, result);
                return null;
            }
            case EclOpcodes.LoadChar:
            {
                // P (0x1026): select list member v AND 0x7F; bit 0x80 with DS:84D2/84D3 removes it (EclPartyRules).
                var v = Value16(1);
                if (Memory.Party is not null)
                {
                    var removed = PartyRules.LoadCharacter(v & 0xFF);
                    if (removed is not null)
                        _step?.HostCalls.Add(new EclTraceHostCall("RemoveFromParty", [removed.Name], null));
                }
                else
                {
                    Diag("LOADCHAR with no party attached: host call only");
                    HostCall("LoadCharacter", [Num(v)], null, () => Host.LoadCharacter(v));
                }
                return null;
            }
            case EclOpcodes.LoadMon:
            {
                var v = Values(1, 3); // [?] op 0B (L)
                HostCall("LoadMonster", v.Select(Num).ToArray(), null, () => Host.LoadMonster(v[0], v[1], v[2]));
                return null;
            }
            case EclOpcodes.SetupMon:
            {
                var v = Values(1, 3); // [?] op 0C (L)
                HostCall("SetupMonster", v.Select(Num).ToArray(), null, () => Host.SetupMonster(v[0], v[1], v[2]));
                return null;
            }
            case EclOpcodes.Approach:
                HostCall("Approach", [], null, Host.Approach); // [?] op 0D (L)
                return null;
            case EclOpcodes.ClrMon:
                HostCall("ClearMonsters", [], null, Host.ClearMonsters); // [?] op 1C (L)
                return null;
            case EclOpcodes.ChkParty:
            {
                // P (0x217B): the selector is the raw field for a type-1 operand, else its value; e = low byte of v2.
                ushort field = _slotType[1] == 1 ? RawWord(1) : Value16(1);
                ushort effect = (ushort)(Value(2) & 0xFF);
                var dsts = new[] { DestWord(3), DestWord(4), DestWord(5), DestWord(6) };
                EclPartyCheck r = default;
                if (Memory.Party is not null)
                {
                    if (PartyRules.CheckParty(field, effect) is not { } c)
                        return null; // other selectors write nothing
                    r = c;
                }
                else
                {
                    Diag("CHKPARTY with no party attached: host answer used");
                    HostCall("CheckParty", [Num(field), Num(effect)], () => $"{r.Min},{r.Max},{r.Average},{r.Any}",
                        () => r = Host.CheckParty(field, effect));
                }
                Memory.Write(dsts[0], r.Min);
                Memory.Write(dsts[1], r.Max);
                Memory.Write(dsts[2], r.Average);
                Memory.Write(dsts[3], r.Any);
                return null;
            }
            case EclOpcodes.Combat:
                HostCall("Combat", [], null, Host.Combat); // [?] op 24 (L)
                return null;
            case EclOpcodes.Treasure:
            {
                var v = Values(1, 8); // [?] op 27 (L)
                HostCall("Treasure", v.Select(Num).ToArray(), null, () => Host.Treasure(v));
                return null;
            }
            case EclOpcodes.Rob:
            {
                // P (0x2AF7): v1 who, v2 the money percentage and the item chance (operand 2 fetched twice); v3 unused.
                var v = Values(1, 2);
                if (Memory.Party is not null)
                    PartyRules.Rob(v[0] & 0xFF, v[1] & 0xFF);
                else
                {
                    Diag("ROB with no party attached: host call only");
                    HostCall("Rob", [Num(v[0]), Num(v[1]), Num(v[1])], null, () => Host.Rob(v[0], v[1], v[1]));
                }
                return null;
            }
            case EclOpcodes.Damage:
            {
                // P (0x34BB): flags, dice count, sides, bonus, to-hit / save type (low bytes); EclPartyRules.Damage.
                var v = Values(1, 5);
                if (Memory.Party is not null)
                {
                    bool killed = PartyRules.Damage(v[0] & 0xFF, v[1] & 0xFF, v[2] & 0xFF, v[3] & 0xFF, v[4] & 0xFF);
                    if (killed)
                        ExternalStop = true; // DS:49FF: no list member left in play
                    HostCall("DamageEnd", [killed ? "killed" : "alive"], null, () => Host.DamageEnd(killed));
                }
                else
                {
                    Diag("DAMAGE with no party attached: host call only");
                    HostCall("Damage", v.Select(Num).ToArray(), null, () => Host.Damage(v[0], v[1], v[2], v[3], v[4]));
                }
                return null;
            }
            case EclOpcodes.SpriteOff:
                HostCall("SpriteOff", [], null, Host.SpriteOff); // [?] op 31 (L)
                return null;
            case EclOpcodes.AddNpc:
                DoAddNpc();
                return null;
            case EclOpcodes.Spell:
            {
                // P (0x3949): both destinations keep only the low byte of their raw field ($00xx: outside every VM
                // area), so v1.3's use writes nothing; the search covers the first member only (residue byte, policy).
                int spell = Value(1) & 0xFF;
                ushort d1 = (ushort)(RawWord(2) & 0xFF), d2 = (ushort)(RawWord(3) & 0xFF);
                (ushort, ushort)? r = null;
                if (Memory.Party is not null)
                {
                    var (index, count) = PartyRules.Spell(spell, Policy.SpellFoundResidue);
                    r = (index, count);
                }
                else
                    HostCall("Spell", [Num(spell)], () => r is { } x ? $"{x.Item1},{x.Item2}" : "none", () => r = Host.Spell((ushort)spell));
                if (r is { } res)
                {
                    Memory.Write(d1, res.Item1);
                    Memory.Write(d2, res.Item2);
                }
                return null;
            }
            case EclOpcodes.Picture:
            {
                var v = Value16(1); // [?] op 0E (L)
                HostCall("Picture", [Num(v)], null, () => Host.Picture(v));
                return null;
            }
            case EclOpcodes.LoadFiles:
                Memory.FilesLoadedSinceSwitch = true; // DS:4431 := 1 (0x19C8)
                DoLoadFiles();
                return null;
            case EclOpcodes.LoadPieces:
                Memory.FilesLoadedSinceSwitch = true;
                DoLoadPieces();
                return null;
            case EclOpcodes.Clock:
            {
                // P (0x3807): loads its operand, then fetches the amount with an uninitialised local as the operand
                // index: the high byte of the stack word the previous handler left there. After GOSUB (the only v1.3
                // case, ECL7 block 17 $9D37) that byte is 0. Operand index 0 aliases the bytes in front of the
                // operand arrays: type = DS:7005 (never written: 0), low byte = DS:7085 = high byte of operand slot
                // 64. So the amount is slot 64's high byte, 0 unless an instruction loaded 64 operands. DOSBox
                // (2026-11-17): +0 minutes, then +5 after an ON GOTO whose 64th target had high byte 5.
                if (_previousHandler != EclOpcodes.Gosub)
                    Diag($"[?] CLOCK after opcode {_previousHandler:X2}, not GOSUB: the operand index is other stack residue; index 0 assumed");
                ushort v = _slotHi[64];
                HostCall("Clock", [Num(v)], null, () => Host.Clock(v));
                return null;
            }
            case EclOpcodes.Call:
            {
                var routine = DestWord(1); // raw address picks the routine (P ids); effects [?]
                HostCall("Call", [JsonlTraceWriter.Hex(routine)], null, () => Host.Call(routine));
                return null;
            }
            case EclOpcodes.Program:
            {
                RestoreSelected(); // 0x3B5C, before the operand is loaded
                var v = Value16(1); // ids P; effects [?]
                HostCall("Program", [Num(v)], null, () => Host.Program(v));
                if (Profile.ProgramIdsThatExit.Contains(v))
                    DoExit(); // P: camp (0x3B56), then EXIT (0x3BBC): the run ends
                return null;
            }
            case EclOpcodes.Protect:
            {
                // P (0x3BC3): glyph g = (VM read at a + i) + 1 as a byte, drawn while g > 1.
                var a = RawWord(1);
                var glyphs = new List<byte>();
                for (int i = 0; i < 256; i++)
                {
                    byte g = (byte)(ReadMem((ushort)(a + i)) + 1);
                    if (g <= 1)
                        break;
                    glyphs.Add(g);
                }
                HostCall("DrawGlyphs", [JsonlTraceWriter.Hex(a), string.Join(" ", glyphs)], null, () => Host.DrawGlyphs(a, glyphs));
                return null;
            }
        }
        throw new InvalidOperationException($"opcode {op:X2} has a table row but no case");
    }

    /// <summary>
    /// ADDNPC (P, 0x38C4): with $6E3E ≤ 7 the host builds the record (0x1F248: monster loader, Random(256), portrait
    /// rolls), which is appended to the party list and becomes current: icon slot 0xBF = lowest of 0..7 unused,
    /// $6E3E + 1, then the level stats 0x2AA87 on it (0x1F55A; <see cref="Game.EffectHandlers.LevelStats"/> of
    /// <see cref="Effects"/>, wired by <see cref="Game.FightKit.WireLevels"/>; unwired: skipped with a diagnostic). Then,
    /// added or not, the current record gets 0x84 = (low byte of v2 / 2) OR 0x80 and 0x10E = 1 when the monster is 24,
    /// else 0.
    /// </summary>
    private void DoAddNpc()
    {
        int monster = Value(1) & 0xFF;
        var party = Memory.Party;
        if (party is null)
        {
            var v = Values(1, 2);
            Diag("ADDNPC with no party attached: host call only");
            HostCall("AddNpc", v.Select(Num).ToArray(), null, () => Host.AddNpc(v[0], v[1]));
            return;
        }
        if (ReadMem(EclPartyRules.PartySizeCell) <= 7)
        {
            EclCharacter? npc = null;
            HostCall("CreateNpc", [Num(monster)], () => npc?.Name ?? "none", () => npc = Host.CreateNpc((ushort)monster));
            if (npc is null)
            {
                var v = Values(1, 2);
                Diag("ADDNPC: the host built no record; AddNpc called instead");
                HostCall("AddNpc", v.Select(Num).ToArray(), null, () => Host.AddNpc(v[0], v[1]));
            }
            else
            {
                var used = party.Members.Select(m => (int)m[0xBF]).Where(x => x < 8).ToHashSet();
                byte slot = 0;
                while (slot < 8 && used.Contains(slot))
                    slot++;
                npc[0xBF] = slot;
                party.Members.Add(npc);
                party.CurrentIndex = party.Members.Count - 1;
                Memory.Write(EclPartyRules.PartySizeCell, (ushort)(ReadMem(EclPartyRules.PartySizeCell) + 1));
                if (Effects.LevelStats is { } levelStats)
                    levelStats(npc);
                else
                    Diag("ADDNPC: level stats 0x2AA87 not wired (no tables); the record keeps its loaded bytes");
            }
        }
        int morale = Value(2) & 0xFF;
        if (party.Current is { } cur)
        {
            cur[EclPartyRules.Morale] = (byte)((morale >> 1) | 0x80);
            cur[EclPartyRules.Hostile] = (byte)(monster == 24 ? 1 : 0);
        }
        else
            Diag("ADDNPC: no current character for the morale byte");
    }

    private void DoExit()
    {
        RestoreSelected(); // 0xDAC
        _gosub.Clear();
        StopFlag = true;
        Host.TextHome(); // display only: the text cursor back to column 1, row 17 (not traced)
    }

    /// <summary>EXIT (0xDAC) and PROGRAM (0x3B5C): after a LOADCHAR (DS:4430) the selected character (DS:4436) is current again.</summary>
    private void RestoreSelected()
    {
        if (Memory.Party is { ChangedByScript: true } party)
        {
            if (party.Selected is { } sel && party.Members.Contains(sel))
                party.SetCurrent(sel);
            else if (_afterLoadBeforeCommand)
            {
                // After a Load, DS:4436 still holds a pointer from before the load until the first command read, so
                // the restore leaves no party record current (DOSBox s11, party_opcodes).
                Diag("EXIT/PROGRAM after LOADCHAR before the first command after a Load: DS:4436 is stale; no current character");
                party.CurrentIndex = -1;
            }
            else
                Diag("EXIT/PROGRAM after LOADCHAR with no selected character noted (CommandRead not called): current kept");
            party.ChangedByScript = false;
        }
    }

    /// <summary>
    /// LOADFILES (P, handler 0x19BA): if o1 is not 255 or 127 and $49E6 ≠ 0, $49C5 = o1, GEO block o1 is loaded and
    /// $6DC9 = 0; if o3 ≠ 255 and $49E6 = 0, the fixed BACPAC picture is loaded. o2 is not used.
    /// </summary>
    private void DoLoadFiles()
    {
        int o1 = Value(1) & 0xFF, o3 = Value(3) & 0xFF;
        bool view3d = ReadMem(0x49E6) != 0;
        if (o1 is not (255 or 127) && view3d)
        {
            Memory.Write(0x49C5, (ushort)o1);
            HostCall("LoadMap", [Num(o1)], null, () => Host.LoadMap(o1));
            Memory.Write(0x6DC9, 0);
        }
        if (o3 != 255 && !view3d)
            HostCall("LoadOutdoorPicture", [], null, Host.LoadOutdoorPicture);
    }

    /// <summary>
    /// LOADPIECES (P, handler 0x19BA): o1 = 127 loads wall set 0 into slot 1 only. Else, if $49E7 and $49E8 are both
    /// non-zero, o1 goes to slot 1 and o3 to slot 3. Else each slot k gets set o_k (255 empties it).
    /// </summary>
    private void DoLoadPieces()
    {
        int[] o = [Value(1) & 0xFF, Value(2) & 0xFF, Value(3) & 0xFF];
        if (o[0] == 127)
        {
            WallSet(1, 0);
            return;
        }
        int[] slots = ReadMem(0x49E7) != 0 && ReadMem(0x49E8) != 0 ? [1, 3] : [1, 2, 3];
        if (slots.Length == 2)
            Diag("[?] LOADPIECES with $49E7/$49E8 set: whether 255 empties the slot here is not read");
        foreach (var k in slots)
            WallSet(k, o[k - 1] == 255 ? null : o[k - 1]);
    }

    private void WallSet(int slot, int? set) =>
        HostCall("LoadWallSet", [Num(slot), set is { } s ? Num(s) : "empty"], null, () => Host.LoadWallSet(slot, set));

    /// <summary>
    /// PARTYSTR (P, 0x200C): sum over members of (HP + 5·max(AC − 60, 0) + 5·max(THAC0 − 39, 0) + 8·MU level + 4·cleric
    /// level) / 10, record bytes 0x11B, 0x111, 0x110, 0x9B, 0x96; the low byte of the total is stored.
    /// [?] Levels are signed bytes in the record; the VM reads them unsigned (party members never hold −1).
    /// </summary>
    private static ushort PartyStrength(EclParty party)
    {
        int total = 0;
        foreach (var m in party.Members)
        {
            int s = m[0x11B] + 5 * Math.Max(m[0x111] - 60, 0) + 5 * Math.Max(m[0x110] - 39, 0) + 8 * m[0x9B] + 4 * m[0x96];
            total += s / 10;
        }
        return (ushort)(total & 0xFF);
    }

    /// <summary>One die: Random(sides) + 1 (dice routine 0x2BCCD with count 1).</summary>
    private int DieRoll(int sides)
    {
        ushort r = 0;
        HostCall("Random", [Num(sides)], () => Num(r), () => r = Host.Random((ushort)sides));
        return r + 1;
    }

    /// <summary>INPUTSTR rules (P): characters 0x20–0x7A, at most 40, upper-cased; empty input is stored as one space.</summary>
    public static string NormaliseInput(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (ch < 0x20 || ch > 0x7A)
                continue;
            if (sb.Length == 40)
                break;
            sb.Append(ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch);
        }
        return sb.Length == 0 ? " " : sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Operand loading (GAME.OVR 0x74D3) and value fetch (0x7609)
    // ---------------------------------------------------------------------------------------------------------------

    private byte FetchByte(int address, string what)
    {
        var b = Memory.FetchCode(address);
        if (b is null)
            throw new EclVmFault($"{what} fetch at {address & 0xFFFF:X4} is outside the script area");
        return b.Value;
    }

    /// <summary>
    /// Loads <paramref name="count"/> operands into slots <paramref name="first"/>.. . Like the program's loader it
    /// first steps the PC past one byte (the opcode) and leaves the PC on the byte after the last operand. The k-th
    /// text operand (type 0x80 or 0x81) of the instruction fills string slot k; a 0x81 text is read from VM memory now.
    /// </summary>
    private void LoadOperands(int first, int count)
    {
        int pc = Pc + 1;
        for (int i = first; i < first + count; i++)
        {
            if (i >= SlotCount)
                throw new EclVmFault($"operand index {i} beyond the slot arrays");
            byte t = FetchByte(pc++, "operand type");
            _slotType[i] = t;
            _slotString[i] = 0;
            switch (t)
            {
                case 1 or 2 or 3:
                    _slotLo[i] = FetchByte(pc++, "operand");
                    _slotHi[i] = FetchByte(pc++, "operand");
                    break;
                case 0x81:
                    _slotLo[i] = FetchByte(pc++, "operand");
                    _slotHi[i] = FetchByte(pc++, "operand");
                    _slotString[i] = ++_textOperands;
                    _strings[_textOperands] = Memory.ReadText(RawWord(i));
                    break;
                case 0x80:
                {
                    int n = FetchByte(pc++, "text length");
                    var packed = new byte[n];
                    for (int k = 0; k < n; k++)
                        packed[k] = FetchByte(pc++, "text");
                    _slotString[i] = ++_textOperands;
                    _strings[_textOperands] = EclText.Unpack(packed);
                    // P: the loader stores the length byte in lo and leaves hi as it was (value fetch, 0x764D).
                    _slotLo[i] = (byte)n;
                    break;
                }
                default:
                    // Type 0 writes only lo; hi keeps the previous instruction's value (spec, "Jump targets").
                    if (t != 0)
                        Diag($"[?] operand type {t:X2}: loader takes one byte, value undefined");
                    _slotLo[i] = FetchByte(pc++, "operand");
                    break;
            }
            _loaded.Add(new EclTraceOperand(t, _slotLo[i], HasHi(t) ? _slotHi[i] : null,
                t == 0x80 ? _strings[_slotString[i]] : null));
        }
        Pc = (ushort)pc;
    }

    /// <summary>
    /// Variadic opcodes (P: ON 0x263E, VMENU 0x1C4E, HMENU 0x1E17): load the fixed part, take the low byte of the
    /// last fixed operand's value (value fetch) as the count, let the handler read what it needs from the fixed
    /// operands (<paramref name="beforeTail"/>), then step the PC back one byte and call the loader again for the
    /// tail. That second load starts over: the tail fills operand slots 1..count and, as the loader's text counter
    /// starts at 0 on every call (0x74D3), string slots from 1. So after the instruction the fixed operands are gone
    /// from the slots.
    /// </summary>
    private int LoadVariadic(EclOpcodeInfo info, Action? beforeTail = null)
    {
        LoadOperands(1, info.FixedCount);
        int count = Value(info.FixedCount) & 0xFF;
        if (_slotType[info.FixedCount] != 0)
            Diag($"[?] variadic count from operand type {_slotType[info.FixedCount]:X2} (never in v1.3)");
        if (count >= SlotCount)
            throw new EclVmFault($"variadic count {count} too large");
        beforeTail?.Invoke();
        Pc--;
        _textOperands = 0;
        LoadOperands(1, count);
        return count;
    }

    private static bool HasHi(byte type) => type is 1 or 2 or 3 or 0x81;

    /// <summary>Raw 16-bit field lo | hi &lt;&lt; 8 (hi may be stale for one-byte types).</summary>
    private ushort RawWord(int i) => (ushort)(_slotLo[i] | (_slotHi[i] << 8));

    /// <summary>A destination address: the raw field whatever the type (P for SAVE).</summary>
    private ushort DestWord(int i)
    {
        if (!HasHi(_slotType[i]))
            Diag($"[?] operand {i} of type {_slotType[i]:X2} used as an address: high byte is left over from an earlier instruction");
        return RawWord(i);
    }

    /// <summary>A jump target: the raw field (P); a type-0 target is undefined in the program.</summary>
    private ushort TargetWord(int i)
    {
        if (!HasHi(_slotType[i]))
            Diag($"[?] jump target operand {i} has type {_slotType[i]:X2}: high byte is left over, target undefined");
        return RawWord(i);
    }

    /// <summary>Value fetch: types 0/2 immediate, 1/3 memory (width by region), 0x81 the raw field, 0x80 like type 1.</summary>
    private int Value(int i)
    {
        switch (_slotType[i])
        {
            case 0: return _slotLo[i];
            case 2: return RawWord(i);
            case 0x81: return RawWord(i);
            case 1 or 3: return ReadMem(RawWord(i));
            case 0x80:
                // P (0x764D): a text fetched as a value is read like type 1 at (length | stale hi << 8).
                Diag($"text operand {i} used as a number: VM read at ${RawWord(i):X4} (length | old high byte); never in v1.3");
                return ReadMem(RawWord(i));
            default:
                Diag($"[?] value of operand type {_slotType[i]:X2} is undefined, using {Policy.UndefinedValue}");
                return Policy.UndefinedValue;
        }
    }

    private ushort Value16(int i) => (ushort)Value(i);

    private ushort[] Values(int first, int count)
    {
        var v = new ushort[count];
        for (int k = 0; k < count; k++)
            v[k] = (ushort)Value(first + k);
        return v;
    }

    private ushort ReadMem(ushort a)
    {
        var v = Memory.Read(a);
        _step?.Reads.Add(new EclTraceRead(a, v));
        return v;
    }

    /// <summary>
    /// Text of an operand: its string slot for types 0x80/0x81. Other types: the decimal value, logged ([?] what PRINT
    /// does with a number is not traced; its string slot would hold an older text).
    /// </summary>
    private string Text(int i)
    {
        if (_slotString[i] > 0)
            return _strings[_slotString[i]];
        Diag($"[?] operand {i} of type {_slotType[i]:X2} used as text: printed as its value");
        return Value(i).ToString(CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Flags and IF-skip
    // ---------------------------------------------------------------------------------------------------------------

    private void DoCompare()
    {
        int c;
        if (_slotType[1] >= 0x80 || _slotType[2] >= 0x80)
        {
            // P (0xE6C / 0x8CE8): string slot 1 against slot 2, bytes unsigned, shorter is less, case-sensitive.
            if (_slotType[1] < 0x80 || _slotType[2] < 0x80)
                Diag("COMPARE of a text with a number: string slot 2 holds an older text (never in v1.3)");
            c = CompareText(_strings[1], _strings[2]);
        }
        else
        {
            // P: unsigned 16-bit, operand 1 against operand 2 (flag setter 0x8DBE).
            // The reader leaves an undefined read's value as whatever its stack slot holds. In COMPARE that slot is
            // the one the previous COMPARE operand read used, so an undefined read returns that value (DOSBox,
            // 2026-11-17: COMPARE [$035F] after COMPARE [x], #8 compared as x). Other deep handlers in between may
            // overwrite the slot `[?]`.
            int a = CompareOperand(1), b = CompareOperand(2);
            c = a.CompareTo(b);
        }
        SetFlags(c);
    }

    private int CompareOperand(int i)
    {
        if (_slotType[i] is not (1 or 3))
            return Value(i);
        Memory.UndefinedReadResidue = _compareReadResidue;
        try
        {
            int v = Value(i);
            _compareReadResidue = (ushort)v;
            return v;
        }
        finally
        {
            Memory.UndefinedReadResidue = null;
        }
    }

    /// <summary>Turbo Pascal string order: byte by byte (unsigned) over the shorter length, then the shorter is less.</summary>
    public static int CompareText(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int d = (a[i] & 0xFF) - (b[i] & 0xFF);
            if (d != 0)
                return d;
        }
        return a.Length - b.Length;
    }

    private void SetFlags(int c)
    {
        Flags[FlagEq] = c == 0;
        Flags[FlagNe] = c != 0;
        Flags[FlagLt] = c < 0;
        Flags[FlagGt] = c > 0;
        Flags[FlagLe] = c <= 0;
        Flags[FlagGe] = c >= 0;
    }

    /// <summary>
    /// IF false (GAME.OVR 0x9379): skip the next instruction using the separate skip-count table. For 0C, 36 and the
    /// variadic opcodes the count is short, so execution resumes inside the operands (reproduced on purpose).
    /// [?] Whether the skip routine also fills the operand slots is not traced; the VM only advances the PC.
    /// </summary>
    private void SkipNextInstruction()
    {
        byte next = FetchByte(Pc, "skipped opcode");
        int n = Profile.Opcodes.SkipCount(next);
        int p = Pc + 1;
        for (int k = 0; k < n; k++)
        {
            byte t = FetchByte(p, "skipped operand type");
            p += t switch
            {
                1 or 2 or 3 or 0x81 => 3,
                0x80 => 2 + FetchByte(p + 1, "skipped text length"),
                _ => 2,
            };
        }
        var full = Profile.Opcodes.Get(next);
        if (full is not null && n != full.FixedCount)
            Diag($"IF skips {Profile.Opcodes.Name(next)} with a short operand count: resumes inside its operands");
        // DOSBox (load_saveA / load_saveB captures): after an IF skipped a GOTO, the next instruction's "previous
        // opcode" (DS:6F7E) was the skipped GOTO, not the IF: the skip leaves the skipped opcode in DS:6F7F.
        Opcode = next;
        Pc = (ushort)p;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Host calls and diagnostics
    // ---------------------------------------------------------------------------------------------------------------

    private void HostCall(string name, string[] args, Func<string>? result, Action call)
    {
        try
        {
            call();
        }
        catch (EclAwaitingInput)
        {
            _step?.HostCalls.Add(new EclTraceHostCall(name, args, "(awaiting input)"));
            throw;
        }
        _step?.HostCalls.Add(new EclTraceHostCall(name, args, result?.Invoke()));
    }

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Num(ushort v) => v.ToString(CultureInfo.InvariantCulture);

    private void Diag(string message)
    {
        DiagnosticCount++;
        if (_step is not null)
            _step.Diagnostics.Add(message);
        else
            Diagnostics.Add(message);
    }
}
