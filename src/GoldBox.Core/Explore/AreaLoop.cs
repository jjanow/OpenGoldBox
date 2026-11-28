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
    /// <summary>
    /// Overland (DS:49FA 2–4) arrows (0x17390–0x173BD): Up / Right / Down / Left set the facing to 0 / 2 / 4 / 6 and
    /// end the command; vector 0, the overland step (<see cref="Overland.Step"/>), the redraw and vector 1 follow.
    /// Ignored in the 3D view; the 3D commands Forward, the turns and A are ignored on the overland (its command
    /// line, DS:04DF, has no A, and its arrows do not turn).
    /// </summary>
    TravelNorth,
    TravelEast,
    TravelSouth,
    TravelWest,
    /// <summary>V: View 0x21AED, on both lines (<see cref="AreaLoop.MenuCommand"/>; not through <see cref="AreaLoop.Execute"/>).</summary>
    View,
    /// <summary>C: Cast 0x17CD2, on both lines (<see cref="AreaLoop.MenuCommand"/>; not through <see cref="AreaLoop.Execute"/>).</summary>
    Cast,
}

/// <summary>Messages the explore loop itself shows (the program's own fixed texts are not reproduced).</summary>
public enum AreaMessage
{
    /// <summary>A pressed where $49FB ≠ 0 (cs:0x97B, shown in colour 14).</summary>
    NoAreaMapHere,
    /// <summary>An overland step onto a river tile while $4AA2 = 0 (GAME.OVR cs:0x7F4 of the unit at 0x166CB, colour 14).</summary>
    RiverBlocks,
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

    /// <summary>The command line's V: View 0x21AED on the current member; its exit draws START 0x3379 (0x21D4F).</summary>
    void View() { }

    /// <summary>The command line's C: Cast 0x17CD2 (the camp Magic menu's routine) outside the camp.</summary>
    void Cast() { }
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

    /// <summary>
    /// VM $4AFF (word [DS:49D2]+0x3FE; the save's third wall-set slot cell, $4AFC + 3): the menu routine 0x2CC8C animates
    /// the picture's frames when its own flag is set or this cell is not 0 (0x2CD5D–0x2CD6D, with frames in the table,
    /// DS:6ABF), and clears the cell at its end (0x2CFAD); nothing else in START.EXE or GAME.OVR writes it (P s41). A
    /// Load brings the save's value (non-zero after any save: the wall-set slots), so the first menu after a Load with
    /// no command line between animates: after a training hall's Load of a camp save the script's HMENU animated the
    /// camp picture START 0x3379 case 2 had loaded (DOSBox-X s39 / s41), while after a start-up Load the command line
    /// clears it first (an HMENU in the first step's vector 0 stays on its first frame, DOSBox-X s24 / s41), and a loaded
    /// game's vector 4 runs before any menu (PICTURE 2 + HMENU there animates, DOSBox-X s41). The engine keeps the
    /// program's zero apart from the cell (<see cref="EclMemory.MenuAnimates"/>). Every menu of the routine clears it, the
    /// camp's, hall's and venues' too (the Client's front end after each answered menu, s42). While it is set the routine
    /// moves to the next frame on every pass without waiting for the frame's delay (0x2CDD6; the Client one update per
    /// frame, user s42), and it draws the loaded frame table over whatever page is up: DOSBox-X s43, PICTURE 2 over the
    /// hall's page after each hall Save while the picture is in the table (the Client's <c>ClientPlayer.MenuAsked</c>).
    /// </summary>
    public const ushort MenuAnimateCell = 0x4AFF;

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
    /// The program's sound calls here (docs/re/sound.md §2.4): the step cue in the move (0x16E09), on turning left or
    /// right (0x171DB / 0x17220) and after a command whose redraw finds x or y changed (0x4215). The bump is
    /// <see cref="IAreaUi.Bump"/>. None by default.
    /// </summary>
    public Sound.ISoundSink Sound { get; set; } = GoldBox.Core.Sound.NoSound.Instance;

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

    /// <summary>The overland map (<see cref="Overland"/>), or null when the content has none (no overland step then).</summary>
    public OverlandMap? OverlandMap { get; set; }

    /// <summary>
    /// The column where the overland x = 0 of view mode m (2–4) sits: the program's DS:3C76[m], a module's area
    /// <c>overland.column</c>. Null: no overland step.
    /// </summary>
    public Func<int, int?>? OverlandColumn { get; set; }

    /// <summary>Reads GEO&lt;disk&gt; block id; null when there is none.</summary>
    public Func<int, int, AreaMap?>? MapSource { get; set; }

    /// <summary>Called after a new map is in memory (the Client re-renders, the encounter host gets the planes).</summary>
    public event Action<AreaMap>? MapLoaded;

    /// <summary>The door-menu flags DS:6DDE–6DE0 (reset by every completed step).</summary>
    public DoorMenuFlags DoorFlags { get; } = new();

    /// <summary>DS:6AAC: the area map is shown instead of the 3D view.</summary>
    public bool ShowAreaMap
    {
        get => Memory.AreaMapShown;
        private set => Memory.AreaMapShown = value;
    }

    /// <summary>Every VM run of the last command (for diagnostics: a fault or an unhandled opcode ends one).</summary>
    public List<EclRunResult> Runs { get; } = [];

    /// <summary>DS:49FF: the party was defeated; the area loop ends (the caller leaves the area).</summary>
    public bool Defeated => Vm.ExternalStop || Ended;

    /// <summary>
    /// The redraw 0x2DCF4 draws the 3D view: DS:49FA = 1 or $49E6 ≠ 0 (0x2DCFA–0x2DD15: $49E6 ≠ 0 makes the mode 1);
    /// modes 2–4 with $49E6 = 0 are the overland (<see cref="Overland.Shown"/>), any other mode draws nothing.
    /// </summary>
    public static bool Draws3D(EclMemory m) => m.ViewMode == 1 || m.Read(View3DCell) != 0;

    /// <summary>
    /// The 3D view is up (<see cref="Draws3D"/>) and a map is in memory. A loaded game always has one: a Load that reads
    /// no map leaves the program's buffer as it was (PorGame.Loaded, view3d.md "No map loaded: the map buffer").
    /// </summary>
    public bool InView3D => Map is not null && Draws3D(Memory);

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

    /// <summary>
    /// Whether off-map squares read 0 instead of wrapping while script <c>s</c> runs (<see cref="AreaMap.Bounded"/>).
    /// Default: PoR's program rule (GAME.OVR 0x2EC5D, scripts 0 and 10); a module sets it from its areas' <c>noWrapOffMap</c>.
    /// </summary>
    public Func<int, bool> NoWrapOffMap { get; set; } = s => s is 0 or 10;

    private void UpdateBounded()
    {
        if (Map is not null)
            Map.Bounded = NoWrapOffMap(Vm.CurrentScript);
    }

    /// <summary>The event accessor 0x2EE71 for the party's square (0 with no map).</summary>
    public byte EventHere() => Map is null ? (byte)0 : (byte)Map.Event(Memory.Position.X, Memory.Position.Y);

    /// <summary>Raised at each view redraw (after the step or its bump, and the other callers of <see cref="Redraw"/>).</summary>
    public event Action? Redrawn;

    /// <summary>
    /// Raised when a turn (0x2ED72, then 0x2EEF4) or the area map toggle (0x2EEF4) draws the 88 × 88 view (or the area
    /// map) into the window: whatever picture was there is gone, as at a redraw, but nothing else of 0x2DCF4 runs (no
    /// event byte, no area-map switch-off). DOSBox-X (s42): after a hall Load of a camp save, View and Escape left the
    /// box blank (START 0x3379 case 2), and Left drew the view there, CAMPING kept.
    /// </summary>
    public event Action? ViewDrawn;

    /// <summary>
    /// The view redraw's state part (0x2DCF4): the event byte of the party's square is read again; the area map is
    /// switched off where $49FB ≠ 0. The program does this when DS:49FA = 1 or $49E6 ≠ 0 (0x2DCFA–0x2DD15).
    /// </summary>
    public void Redraw()
    {
        Redrawn?.Invoke();
        if (Overland.Shown(Memory))
        {
            // 0x2DD10 → 0x2DE39 (the tiles) and 0x2DB87 (the marker, which advances DS:6DDC).
            Overland.Redrawn(Memory);
            return;
        }
        if (Map is null || !Draws3D(Memory))
            return;
        if (InterfaceMarker is not null)
            InterfaceMarkerCovered = true;
        Memory.Position.EventByte = EventHere();
        if (Memory.Read(NoAreaMap) != 0)
            ShowAreaMap = false;
    }

    /// <summary>
    /// DS:6DDC, the overland marker's animation counter (0–5): the frame the next overland redraw draws
    /// (<see cref="Overland.MarkerFrame"/>); each redraw advances it. The frame on screen is the one before it, with
    /// the frame <see cref="MarkerCounter"/> itself drawn over it while <see cref="OverlandInterfacePending"/>.
    /// Held in <see cref="EclMemory.MarkerCounter"/> (a load keeps the session's value, see PorGame's loaded entry).
    /// </summary>
    public int MarkerCounter
    {
        get => Memory.MarkerCounter;
        set => Memory.MarkerCounter = value;
    }

    /// <summary>
    /// The overland interface is drawn when the command routine starts in modes 2–4 with DS:49F9 &lt; 2 (0x17285, START
    /// 0x36FE): the ICON frames are loaded, the view is redrawn (tiles and marker, frame c) and the marker drawn once
    /// more alone (frame c + 1, over the first). True while that is still to come for the next command: a Client
    /// shows the view as it will look then (DOSBox-X: frames 2 and 3 after a load).
    /// </summary>
    public bool OverlandInterfacePending => Overland.Commands(Memory) && Memory.PreviousViewMode < 2 && !_interfaceDrawn;

    /// <summary>
    /// The overland interface was drawn in this run of the command routine. Only the keys that end the routine reach
    /// 0x173E7 (DS:49F9 := DS:49FA: the arrows, L, E); S, C, V and the member keys loop back to its key read (0x1738A /
    /// 0x173D5 → 0x172E3), so the interface is drawn once per routine entry, not once per key.
    /// </summary>
    private bool _interfaceDrawn;

    /// <summary>The command routine's start (0x17054): on the overland with DS:49F9 &lt; 2, the interface (START 0x36FE).</summary>
    private void EnterCommandRoutine()
    {
        if (!OverlandInterfacePending)
            return;
        // START 0x370B redraws (0x2DCF4; the tiles and a marker, 0x2DB87, only when it draws the overland), then 0x3710
        // draws the marker alone; each 0x2DB87 advances DS:6DDC. With $49E6 != 0 the redraw draws the 3D view, so the
        // lone marker is frame c and the counter goes on by one (DOSBox-X s43: DS:6DDC 1 after a start-up Load).
        bool tiles = Overland.Shown(Memory);
        int c = MarkerCounter;
        MarkerCounter = (c + (tiles ? 2 : 1)) % 6;
        Memory.MarkerFacing = Memory.Position.Facing; // drawn before the command's key sets a new facing
        Memory.MarkerDrawnTwice = tiles; // frame c + 1 over frame c, until the next redraw
        MarkerSlots[0] = 0; // 0x17285–0x172D5: ICON 0, 1, 2 into the animation slots 9, 10, 11
        MarkerSlots[1] = 1;
        MarkerSlots[2] = 2;
        Memory.InterfaceState = 3; // START 0x3701
        _interfaceDrawn = true;
        OverlandLayout = true;
        // With $49E6 != 0 the interface's redraw draws the 3D view (from the map buffer) and its lone marker goes over it.
        InterfaceMarker = tiles ? null : new OverlandMarkerDraw(Overland.Position(Memory), c, Memory.Position.Facing);
        InterfaceMarkerCovered = false;
    }

    /// <summary>
    /// The overland interface (START 0x36FE) drew its window layout (GAME.OVR 0x2E03F: windows (1, 1)–(15, 15),
    /// (17, 1)–(38, 15), (1, 17)–(38, 22) in that order, so the text box's top edge runs through cell (16, 16) with no
    /// joint and there is no inner box; DOSBox-X s43: no joint at (16, 16) on the overland). Cleared when START 0x3379
    /// draws the 3D layout (<see cref="DrawInterface"/>: 0x32CA ends with the (1, 1)–(15, 15) window, joint at (16, 16)).
    /// </summary>
    public bool OverlandLayout { get; private set; }

    /// <summary>
    /// The interface's lone marker (0x2DB87 after the redraw, START 0x371C) when the redraw drew the 3D view instead of
    /// the tiles (DS:49FA 2–4 with $49E6 != 0, e.g. a save of location type 2 with $49E6 = 1: DOSBox-X s43 showed the 3D
    /// view of the map buffer with the horseman over its top-left corner). It stays on the screen: a later 3D redraw
    /// draws the 88 × 88 view over the part inside it (<see cref="InterfaceMarkerCovered"/>; DOSBox-X: after a step the
    /// part outside the view was still there). Null when the tiles were drawn, or after START 0x3379.
    /// </summary>
    public OverlandMarkerDraw? InterfaceMarker { get; private set; }

    /// <summary>A screen with its own windows (the camp, a venue, a fight) was drawn: the overland interface's layout and
    /// its lone marker are gone.</summary>
    public void OtherLayoutDrawn()
    {
        OverlandLayout = false;
        InterfaceMarker = null;
    }

    /// <summary>A 3D redraw since <see cref="InterfaceMarker"/> was drawn (the view is over its inner part).</summary>
    public bool InterfaceMarkerCovered { get; private set; }

    /// <summary>0x173E7: the command routine ends with DS:49F9 := DS:49FA (the next entry draws no overland interface).</summary>
    private void LeaveCommandRoutine()
    {
        Memory.PreviousViewMode = Memory.ViewMode;
        _interfaceDrawn = false;
    }

    /// <summary>
    /// START 0x3379 while exploring, by DS:49F3 (<see cref="EclMemory.InterfaceState"/>): the window layout (0x32CA:
    /// cases 2 and 4 with the 3D view's box, 3 without), then for 3 and 4 the view redraw 0x2DCF4 (<see cref="Redraw"/>),
    /// for 2 the camp's picture (PIC 0x1D, 0x2E170); then the party list (0x1307) and the position line (0x34AB).
    /// <see cref="InterfaceDrawn"/> is raised first (the windows come before the view).
    /// </summary>
    public void DrawInterface()
    {
        OverlandLayout = false;
        InterfaceMarker = null;
        byte state = Memory.InterfaceState;
        InterfaceDrawn?.Invoke(state);
        if (state is 3 or 4)
            Redraw();
    }

    /// <summary>
    /// START 0x3379 drew the screen for the interface state passed (<see cref="DrawInterface"/>): every window again
    /// (the text box blanked, the cursor kept), the party list and the position line; raised before the view redraw.
    /// </summary>
    public event Action<byte>? InterfaceDrawn;

    /// <summary>
    /// C and V on the command line (0x17054: 3D 0x170F8 / 0x1710F, overland 0x1731C / 0x17327): Cast 0x17CD2 (3D only
    /// when the current member's status 0x10C is 0; the overland branch has no check) and View 0x21AED. Neither ends the
    /// routine: no vector, no time, no step; the position line is drawn again (0x17266 / 0x173D9) and the key read
    /// loops. View's exit (0x21D4F) and Cast's end after a list (0x17D96) draw START 0x3379 (<see cref="DrawInterface"/>,
    /// called by the screens). On the overland the interface comes first as for every key (<see cref="EnterCommandRoutine"/>).
    /// </summary>
    public void MenuCommand(AreaCommand command)
    {
        if (Ended || command is not (AreaCommand.View or AreaCommand.Cast))
            return;
        Memory.MenuAnimates = false; // the command line's menu 0x2CC8C ended (0x2CFAD: $4AFF := 0)
        EnterCommandRoutine();
        if (command == AreaCommand.View)
            Ui.View();
        else if (Overland.Commands(Memory) || Memory.Party?.Current is not { } who || who[0x10C] == 0)
            Ui.Cast();
    }

    /// <summary>
    /// The ICON blocks in the marker's animation slots 9, 10, 11 (each slot holds block b and b + 128): 0, 1, 2 as the
    /// overland interface loads them (0x17285–0x172D5: ICON 0 → 9, 1 → 10, 2 → 11). The icon editor changes slot 11
    /// on the overland (<see cref="IconEditorDone"/>) until the next interface load.
    /// </summary>
    public int[] MarkerSlots { get; } = [0, 1, 2];

    /// <summary>The overland interface's slot load (0x17285–0x172D5): ICON 0, 1, 2 into marker slots 9, 10, 11.</summary>
    public void ReloadMarkerSlots()
    {
        MarkerSlots[0] = 0;
        MarkerSlots[1] = 1;
        MarkerSlots[2] = 2;
    }

    /// <summary>
    /// The end of the icon editor 0x1D1AC (the camp's Alter Icon): with DS:49FA &gt; 1 (0x1D843) it loads ICON block 0
    /// into icon slot 11 and block 1 into slot 12 (0x1D84A–0x1D879; the editor itself used slot 12). Slot 11 is the
    /// marker's third animation slot, which held block 2: until the overland interface loads the slots again (an entry
    /// from a 3D area), marker counters 4 and 5 draw ICON blocks 0 and 128 instead of 2 and 130 (DOSBox-X s25: after
    /// Alter Icon on the overland and a walk the marker went 0, 128, 0, 128, 1, 129, 0 for counters 4, 5, 0 … 4, a
    /// program bug, kept). Slot 12 is not a marker slot: nothing to show.
    /// </summary>
    public void IconEditorDone()
    {
        if (Memory.ViewMode > 1)
            MarkerSlots[2] = 0;
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
        if (command is AreaCommand.View or AreaCommand.Cast)
        {
            MenuCommand(command); // no vector, no step: the routine reads the next key
            return;
        }
        Runs.Clear();
        if (Ended)
            return;
        Memory.MenuAnimates = false; // the command line's menu 0x2CC8C ended (0x2CFAD: $4AFF := 0)
        Vm.CommandRead();
        Memory.Write(CancelStep, 0); // 0x17060: every command read starts with $6DC9 = 0
        bool overland = Overland.Commands(Memory);
        if (overland ? command is AreaCommand.Forward or AreaCommand.TurnLeft or AreaCommand.TurnRight
                or AreaCommand.TurnAround or AreaCommand.AreaMap
            : command is AreaCommand.TravelNorth or AreaCommand.TravelEast or AreaCommand.TravelSouth
                or AreaCommand.TravelWest)
            return; // not on this mode's command line (0x17054: DS:49FA = 1 or 2–4 branch)
        EnterCommandRoutine();
        switch (command)
        {
            case AreaCommand.TravelNorth: Memory.Position.Facing = Facing.North; break;
            case AreaCommand.TravelEast: Memory.Position.Facing = Facing.East; break;
            case AreaCommand.TravelSouth: Memory.Position.Facing = Facing.South; break;
            case AreaCommand.TravelWest: Memory.Position.Facing = Facing.West; break;
            case AreaCommand.TurnLeft:
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Step);
                Turn(Facing.Left(Memory.Position.Facing));
                return;
            case AreaCommand.TurnRight:
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Step);
                Turn(Facing.Right(Memory.Position.Facing));
                return;
            case AreaCommand.TurnAround: Turn(Facing.Back(Memory.Position.Facing)); return;
            case AreaCommand.AreaMap:
                if (Memory.Read(NoAreaMap) == 0)
                {
                    ShowAreaMap = !ShowAreaMap;
                    ViewDrawn?.Invoke(); // 0x2EEF4
                }
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

        LeaveCommandRoutine(); // 0x173E7: the command routine ends with DS:49F9 := DS:49FA
        // The area loop (0x40DB–0x40FA): $49F2 = the current block after the command read, except for the read that
        // follows a camp or a Look (0x4172–0x418F reads the next command and goes straight back to 0x40FF; s52).
        if (!_afterCampOrLook)
            Memory.Write(PreviousScript, (ushort)Vm.CurrentScript);
        _afterCampOrLook = false;
        try
        {
            if (command == AreaCommand.Encamp)
            {
                Encamp();
                _afterCampOrLook = true;
                return;
            }
            if (Memory.Read(SearchMode) > 1)
            {
                LookRun();
                _afterCampOrLook = true;
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
            if (Memory.Position.X != Memory.Read(PreviousX) || Memory.Position.Y != Memory.Read(PreviousY))
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Step); // 0x4215
            // The pass right before vector 1: the close-up flag DS:84E0 cleared (0x421E) and DS:84E1, the picture's
            // animation arm, set (0x4223); HMENU / ENCMENU animate only while both are set. Vector 0 runs before it, so a
            // PICTURE there after a start-up load (DS:84E1 still 0) does not animate (DOSBox-X s24).
            Memory.PictureShown = false;
            Memory.PictureArmed = true;
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

    /// <summary>0x4172: the camp or Look path ended; the next command read does not write $49F2.</summary>
    private bool _afterCampOrLook;

    /// <summary>
    /// The camp routine 0x3B19: vector 2, the camp menu (0x196A0), vector 3 if the rest was interrupted, redraw. Both
    /// vectors run through the run loop 0x3EB7 alone: a NEWECL there is not followed here (no 0x3EFA); the script-changed
    /// flag stays set and the next command's vector 0 (0x41A1) or a Look's vector 1 (0x4159) follows it (s52: Core had
    /// followed it at once, vectors 4, 0, 1 before the camp menu). The camp menu opens after a NEWECL too, and after a
    /// vector 2 that destroyed the party (DS:49FF set by DAMAGE): 0x3B19 opens 0x196A0 whatever, and neither tests
    /// DS:49FF (s60). Vector 3 then runs nothing (0x3EB7 tests DS:49FF before the first opcode), and the area loop
    /// ends at 0x4172. Only the engine's own stops (a fault, the step budget, a missing input answer) skip the menu:
    /// the program has no such run.
    /// </summary>
    private void Encamp()
    {
        var r = Vm.RunVector(2);
        Runs.Add(r);
        if (r.Reason is EclStopReason.Exit or EclStopReason.NewScript or EclStopReason.ExternalStop && Ui.Camp())
            Runs.Add(Vm.RunVector(3));
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
        ViewDrawn?.Invoke(); // 0x2EEF4
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
    /// The step after vector 0 (0x17438, read whole s52): only in the 3D view (DS:49FA = 1) or on the overland (2–4);
    /// there it is cancelled when the script set $6DC9 ≥ 255 ($6DC9 back to 0; in any other mode $6DC9 is left as it
    /// is); else by the party's own face: 1 moves, 2/3 open the door menu (one try), 0 refuses. A refused step or
    /// failed try bumps.
    /// </summary>
    public void Step()
    {
        if (Memory.ViewMode != 1 && !Overland.Commands(Memory))
            return;
        if (Memory.Read(CancelStep) >= 255)
        {
            Memory.Write(CancelStep, 0);
            return;
        }
        if (Overland.Commands(Memory))
        {
            // 0x1771B: DS:49FA 2–4 → the overland step 0x16EE3 (DS:49FA other than 1–4: no step at all).
            if (OverlandMap is { } om && OverlandColumn?.Invoke(Memory.ViewMode) is { } column
                && Overland.Step(Memory, om, column, AdvanceClock) == OverlandStep.River)
                Ui.Message(AreaMessage.RiverBlocks);
            return;
        }
        if (Memory.ViewMode != 1)
            return;
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
                // 0x16D13 only clears the slot and 0x17438 steps with no face set, so the program leaves the door
                // locked in the map. Original bug, fixed (user, s53): Knock sets both faces as Bash and Pick (0x167D4)
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
        Sound.Cue(GoldBox.Core.Sound.SoundCue.Step); // 0x16E09
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

/// <summary>A marker the overland interface drew alone: the overland square, the frame (counter 0–5) and the facing.</summary>
public sealed record OverlandMarkerDraw((int X, int Y) Square, int Frame, byte Facing);
