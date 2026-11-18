using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Explore;

/// <summary>A command of the 3D explore screen (command routine GAME.OVR 0x17054).</summary>
public enum AreaCommand
{
    /// <summary>Up arrow: the forward key (0x16D58), vector 0, the step (0x17438), vector 1.</summary>
    Forward,
    /// <summary>Left arrow: facing + 6. No script, no time.</summary>
    TurnLeft,
    /// <summary>Right arrow: facing + 2.</summary>
    TurnRight,
    /// <summary>Down arrow: facing + 4.</summary>
    TurnAround,
    /// <summary>A: area map on/off, unless $49FB ≠ 0.</summary>
    AreaMap,
    /// <summary>S: Search mode on/off ($6DCA bit 0). Steps then cost 10 minutes.</summary>
    Search,
    /// <summary>L: Look: $6DCA |= 2, 10 minutes, then vector 1 alone with $6DCA = 1.</summary>
    Look,
    /// <summary>E: Encamp (0x3B19): vector 2, the camp menu, vector 3 when a rest was interrupted.</summary>
    Encamp,
}

/// <summary>Messages the explore loop itself shows (the program's own fixed texts are not reproduced).</summary>
public enum AreaMessage
{
    /// <summary>A pressed where $49FB ≠ 0 (cs:0x97B, shown in colour 14).</summary>
    NoAreaMapHere,
}

/// <summary>The player-facing side of the explore loop that is not an ECL host call.</summary>
public interface IAreaUi
{
    /// <summary>
    /// The door menu (0x17480 / 0x175BF): one choice from <paramref name="choices"/>, or null for Exit (any other key).
    /// Throw to abandon the command.
    /// </summary>
    DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices);

    /// <summary>The step was refused or a door try failed (the program plays its bump sound, DS:2622).</summary>
    void Bump();

    void Message(AreaMessage message);

    /// <summary>
    /// [?] The camp menu (0x196A0: rest, memorise, …), not built: returns the "rest interrupted" flag the program's
    /// rest loop returns (true = vector 3 runs).
    /// </summary>
    bool Camp();
}

/// <summary>
/// The area loop of PoR DOS v1.3 (GAME.OVR 0x403E, with the command routine 0x17054, the forward key 0x16D58, the step
/// 0x17438 and the move 0x16E06), program read s11. It owns the map in memory and runs the area script's vectors in
/// the VM. The party position is the VM's (<see cref="EclMemory.Position"/>): scripts move the party too.
/// One generator (<see cref="Rng"/>) serves RANDOM and the door rolls, so the draws come in program order.
/// </summary>
public sealed class AreaLoop
{
    /// <summary>VM cells the loop uses: $6DC9 (step cancel, 255), $6DCA (search/look mode), $6DD5 (left the map),
    /// $49F0/$49F1 (previous x, y), $49F2 (previous script), $49FB (no area map), $49E6 (3D view).</summary>
    public const ushort CancelStep = 0x6DC9, SearchMode = 0x6DCA, PreviousX = 0x49F0, PreviousY = 0x49F1,
        PreviousScript = 0x49F2, NoAreaMap = 0x49FB, View3DCell = 0x49E6;

    public AreaLoop(EclVm vm, TurboPascalRandom rng, IAreaUi ui)
    {
        Vm = vm;
        Rng = rng;
        Ui = ui;
        vm.ScriptSwitchEventByte = _ => EventHere();
    }

    public EclVm Vm { get; }
    public EclMemory Memory => Vm.Memory;

    /// <summary>
    /// The effect timeout 0x23DCC (count, unit) that the clock routine 0x241A1 runs after every advance (a step, Look,
    /// the CLOCK opcode through <see cref="AreaHost"/>): <see cref="GoldBox.Core.Game.CampTime.TimeOutEffects(EclMemory, byte[], bool, int, int, Action{EclCharacter, byte[]})"/>
    /// with the session's flags. Null: the clock alone.
    /// </summary>
    public Action<int, int>? EffectTimeout { get; set; }

    /// <summary>0x241A1: the game clock (<see cref="GameClock.Advance"/>), then <see cref="EffectTimeout"/>.</summary>
    public void AdvanceClock(int unit, int count)
    {
        GameClock.Advance(Memory, unit, count);
        EffectTimeout?.Invoke(count, unit);
    }
    public TurboPascalRandom Rng { get; }
    public IAreaUi Ui { get; }

    /// <summary>The 3D map in memory (null until a LOADFILES loaded one, or outdoors).</summary>
    public AreaMap? Map { get; private set; }

    /// <summary>Reads GEO&lt;disk&gt; block id; null when there is none.</summary>
    public Func<int, int, AreaMap?>? MapSource { get; set; }

    /// <summary>Called after a new map is in memory (the Client re-renders, the encounter host gets the planes).</summary>
    public event Action<AreaMap>? MapLoaded;

    /// <summary>The door-menu flags DS:6DDE–6DE0 (reset by every completed step).</summary>
    public DoorMenuFlags DoorFlags { get; } = new();

    /// <summary>DS:6AAC: the area map is shown instead of the 3D view.</summary>
    public bool ShowAreaMap { get; private set; }

    /// <summary>Every VM run of the last command (for diagnostics: a fault or an unhandled opcode ends one).</summary>
    public List<EclRunResult> Runs { get; } = [];

    /// <summary>DS:49FF: the party was defeated; the area loop ends (the caller leaves the area).</summary>
    public bool Defeated => Vm.ExternalStop || Ended;

    /// <summary>The 3D view is up: $49E6 ≠ 0 and a map is loaded. Outdoors the program shows the wilderness picture.</summary>
    public bool InView3D => Map is not null && Memory.Read(View3DCell) != 0;

    public bool SearchOn => (Memory.Read(SearchMode) & 1) != 0;

    /// <summary>LOADFILES of map <paramref name="id"/> (the GEO loader 0x2F9BE always re-reads the block, undoing door edits).</summary>
    public void LoadMap(int id)
    {
        var map = MapSource?.Invoke(Memory.Disk, id);
        if (map is null)
        {
            Vm.Diagnostics.Add($"LOADFILES: no GEO{Memory.Disk} block {id}");
            return;
        }
        SetMap(map);
    }

    /// <summary>Puts a map in memory (also used when a save is loaded: the load re-reads map $49C5).</summary>
    public void SetMap(AreaMap map)
    {
        Map = map;
        UpdateBounded();
        MapLoaded?.Invoke(map);
    }

    private void UpdateBounded()
    {
        if (Map is not null)
            Map.Bounded = Vm.CurrentScript is 0 or 10;
    }

    /// <summary>The event accessor 0x2EE71 for the party's square (0 with no map).</summary>
    public byte EventHere() => Map is null ? (byte)0 : (byte)Map.Event(Memory.Position.X, Memory.Position.Y);

    /// <summary>
    /// The view redraw's state part (0x2DCF4): the event byte of the party's square is read again; the area map is
    /// switched off where $49FB ≠ 0. The program does this when DS:49FA = 1 or $49E6 ≠ 0 (0x2DCFA–0x2DD15).
    /// </summary>
    public void Redraw()
    {
        if (Map is null || (Memory.ViewMode != 1 && Memory.Read(View3DCell) == 0))
            return;
        Memory.Position.EventByte = EventHere();
        if (Memory.Read(NoAreaMap) != 0)
            ShowAreaMap = false;
    }

    /// <summary>Runs a vector; after a NEWECL, the new script's vectors (0x3EFA).</summary>
    public EclRunResult Run(int vector)
    {
        var r = Vm.RunVector(vector);
        Runs.Add(r);
        if (Vm.ScriptChanged) // DS:442F stays set until followed (0x41A1, 0x4230, 0x4159)
        {
            r = Vm.FollowScriptChanges();
            Runs.Add(r);
            UpdateBounded();
        }
        return r;
    }

    /// <summary>One command, as the area loop runs it after the command routine returns (or inside it, for turns).</summary>
    public void Execute(AreaCommand command)
    {
        Runs.Clear();
        if (Ended)
            return;
        Vm.CommandRead();
        Memory.Write(CancelStep, 0); // 0x17060: every command read starts with $6DC9 = 0
        switch (command)
        {
            case AreaCommand.TurnLeft: Turn(Facing.Left(Memory.Position.Facing)); return;
            case AreaCommand.TurnRight: Turn(Facing.Right(Memory.Position.Facing)); return;
            case AreaCommand.TurnAround: Turn(Facing.Back(Memory.Position.Facing)); return;
            case AreaCommand.AreaMap:
                if (Memory.Read(NoAreaMap) == 0)
                    ShowAreaMap = !ShowAreaMap;
                else
                    Ui.Message(AreaMessage.NoAreaMapHere);
                return;
            case AreaCommand.Search:
                Memory.Write(SearchMode, (ushort)(Memory.Read(SearchMode) ^ 1));
                return;
            case AreaCommand.Look:
                Memory.Write(SearchMode, (ushort)(Memory.Read(SearchMode) | 2));
                AdvanceClock(GameClock.TenMinutes, 1);
                break;
            case AreaCommand.Forward:
                ForwardKey();
                break;
        }

        // The area loop (0x40E3 on): $49F2 = the current block after every command read.
        Memory.Write(PreviousScript, (ushort)Vm.CurrentScript);
        try
        {
            if (command == AreaCommand.Encamp)
            {
                Encamp();
                return;
            }
            if (Memory.Read(SearchMode) > 1)
            {
                LookRun();
                return;
            }

            // Vector 0, then (unless the script changed or the party is defeated) the step and vector 1.
            var r = Vm.RunVector(0);
            Runs.Add(r);
            if (Vm.ScriptChanged)
            {
                Runs.Add(Vm.FollowScriptChanges());
                UpdateBounded();
                return;
            }
            if (Defeated || r.Reason is not EclStopReason.Exit)
                return;
            Memory.Write(PreviousX, Memory.Position.X);
            Memory.Write(PreviousY, Memory.Position.Y);
            Step();
            Redraw();
            Run(1);
        }
        finally
        {
            // 0x4245: when DS:49FF is set the area loop ends and clears it (the party was destroyed).
            if (Vm.ExternalStop)
            {
                Vm.ExternalStop = false;
                Ended = true;
            }
        }
    }

    /// <summary>The area loop has ended (the party was destroyed, DS:49FF); no more commands run.</summary>
    public bool Ended { get; private set; }

    /// <summary>The camp routine 0x3B19: vector 2, the camp menu, vector 3 if the rest was interrupted, redraw.</summary>
    private void Encamp()
    {
        var r = Run(2);
        if (r.Reason is EclStopReason.Exit && !Defeated && Ui.Camp())
            Run(3);
        Redraw();
    }

    /// <summary>0x4132: vector 1 alone with $6DCA = 1, then bit 0 of the old value back (Look, or $6DCA &gt; 1).</summary>
    private void LookRun()
    {
        ushort keep = (ushort)(Memory.Read(SearchMode) & 1);
        Memory.Write(SearchMode, 1);
        Redraw();
        Run(1);
        Memory.Write(SearchMode, keep);
    }

    private void Turn(int facing)
    {
        var p = Memory.Position;
        p.Facing = (byte)facing;
        p.WallAhead = (byte)(Map?.Wall(p.X, p.Y, facing) ?? 0);
    }

    /// <summary>
    /// The forward key (0x16D58): $6DD5 = 0; when the party's own face is not impassable and the square ahead is off
    /// the 16×16 grid, $6DD5 = 1 (the position is clamped to the edge it already is on). An impassable face on the map
    /// edge leaves $6DD5 = 0.
    /// </summary>
    public void ForwardKey()
    {
        Memory.Write(EclVm.EdgeStepCell, 0);
        var p = Memory.Position;
        if (Map is null || Map.Passage(p.X, p.Y, p.Facing) == DoorBarrier.Impassable)
            return;
        int nx = (sbyte)p.X + Facing.DeltaX(p.Facing), ny = (sbyte)p.Y + Facing.DeltaY(p.Facing);
        if (!AreaMap.InMap(nx, ny))
            Memory.Write(EclVm.EdgeStepCell, 1);
    }

    /// <summary>
    /// The step after vector 0 (0x17438): cancelled when the script set $6DC9 ≥ 255 ($6DC9 back to 0); else by the
    /// party's own face: 1 moves, 2/3 open the door menu (one try), 0 refuses. A refused step or failed try bumps.
    /// </summary>
    public void Step()
    {
        if (Memory.Read(CancelStep) >= 255)
        {
            Memory.Write(CancelStep, 0);
            return;
        }
        if (Map is null)
            return;
        var p = Memory.Position;
        var barrier = Map.Passage(p.X, p.Y, p.Facing);
        bool moved = barrier switch
        {
            DoorBarrier.Passable => true,
            DoorBarrier.Locked or DoorBarrier.Barred => DoorMenu(barrier),
            _ => false,
        };
        if (moved)
            Move();
        else
            Ui.Bump();
    }

    private bool DoorMenu(DoorBarrier barrier)
    {
        var p = Memory.Position;
        var members = Memory.Party?.Members ?? [];
        var party = members.Select(DoorMember.FromCharacter).ToList();
        var choices = DoorRules.MenuChoices(barrier, party, DoorFlags);
        if (choices.Count == 0)
        {
            DoorMap.Seal(Map!.Planes, p.X, p.Y, p.Facing); // 0x166CB: only Exit left: the face becomes a wall
            return false;
        }
        var choice = Ui.ChooseDoorAction(barrier, choices);
        if (choice is not { } c || !choices.Contains(c))
            return false;
        DoorAttempt a;
        switch (c)
        {
            case DoorAction.Bash:
                a = DoorRules.Bash(barrier, party, Rng, DoorFlags);
                break;
            case DoorAction.Pick:
                a = DoorRules.Pick(barrier, party, Rng, DoorFlags);
                break;
            default:
                a = DoorRules.Knock(party);
                if (a.Opened)
                    members[a.Member][DoorMember.SpellSlotsOffset + a.KnockSlot] = 0;
                break;
        }
        if (a.Opened)
            DoorMap.Open(Map!.Planes, p.X, p.Y, p.Facing);
        return a.Opened;
    }

    /// <summary>
    /// The move (0x16E06): one square ahead, wrapping 0 ↔ 15; wall ahead and event byte read again; the door flags
    /// reset; the clock advances 1 minute, or 10 with Search on.
    /// </summary>
    public void Move()
    {
        var p = Memory.Position;
        int x = (sbyte)p.X + Facing.DeltaX(p.Facing), y = (sbyte)p.Y + Facing.DeltaY(p.Facing);
        x = x < 0 ? 15 : x > 15 ? 0 : x;
        y = y < 0 ? 15 : y > 15 ? 0 : y;
        p.X = (byte)x;
        p.Y = (byte)y;
        p.WallAhead = (byte)(Map?.Wall(x, y, p.Facing) ?? 0);
        DoorFlags.Reset();
        p.EventByte = EventHere();
        if (SearchOn)
            AdvanceClock(GameClock.TenMinutes, 1);
        else
            AdvanceClock(GameClock.Minutes, 1);
    }
}
