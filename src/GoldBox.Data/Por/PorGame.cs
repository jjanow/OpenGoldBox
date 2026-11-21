using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>What a game session talks to: the player-facing hosts (a Client's screens, or headless test hosts).</summary>
/// <param name="Host">Text, menus, input lines and the Lee-only opcodes (the innermost ECL host).</param>
/// <param name="Encounter">Encounter pictures and menus.</param>
/// <param name="Combat">The fight, loot, shops, temples (a placeholder until the fight is built).</param>
/// <param name="Area">The door menu, the bump, the camp menu.</param>
/// <param name="FightMenu">The player's combat menu. When set, COMBAT builds the real fight (<see cref="PorFight"/>) and
/// hands it to <see cref="ICombatRunner.Fight(EncounterState, Fight)"/>; when null, <paramref name="Combat"/> runs the
/// fight itself (<see cref="ICombatRunner.Fight(EncounterState)"/>).</param>
/// <param name="QuickFight">With no <paramref name="FightMenu"/>: build the real fight anyway, every party member acting
/// through the AI as if on Quick (a headless fight, for a front end without a combat screen).</param>
/// <param name="Camp">The session's program variables (<see cref="CampState"/>: the effect flags DS:471E the clock's
/// effect timeout keeps, the rest time…), shared by exploration, fights and the camp; null = the game makes its own
/// (<see cref="PorGame.Camp"/>).</param>
public sealed record PorPlayer(IEclHost Host, IEncounterUi Encounter, ICombatRunner Combat, IAreaUi Area,
    IFightMenuChooser? FightMenu = null, bool QuickFight = false, CampState? Camp = null);

/// <summary>
/// A running PoR game in a 3D area: the VM with the encounter host and the explore loop (<see cref="AreaLoop"/>), the
/// map in memory read from <c>GEO&lt;disk&gt;.DAX</c>, the wall sets in <see cref="PorViewArt"/>, one generator for
/// every roll. Host chain: VM → <see cref="EncounterHost"/> → <see cref="AreaHost"/> → the player's host.
/// </summary>
public sealed class PorGame
{
    /// <summary>Everything the game reads: the player's folder, or an imported module.</summary>
    public IPorContent Content { get; }
    public PorViewArt Art { get; }
    public EclVm Vm { get; }
    public EclMemory Memory => Vm.Memory;
    public AreaLoop Loop { get; }
    public EncounterHost Encounter { get; }
    public AreaHost AreaHost { get; }
    public TurboPascalRandom Rng { get; }
    /// <summary>
    /// The session's program variables (<see cref="PorPlayer.Camp"/>): the effect flags DS:471E are shared by the clock
    /// of exploration (<see cref="AreaLoop.EffectTimeout"/>), the fights' end of round and the camp's rest.
    /// </summary>
    public CampState Camp { get; }
    /// <summary>
    /// The effect engine outside a fight (DS:49F3 ≠ 5): the effect timeout's removals (0x2AF10) run their handlers here.
    /// </summary>
    public EffectHandlers OutsideEffects { get; }
    /// <summary>The fight's tables (the program's and the item types), or null when one is not found.</summary>
    public FightKit? Kit { get; }
    /// <summary>The run of the area entry (vector 4 after a load or an entry).</summary>
    public EclRunResult EntryRun { get; private set; } = new(EclStopReason.Exit, 0);

    private PorGame(IPorContent content, PorViewArt art, PorPlayer player, uint seed)
    {
        Content = content;
        Art = art;
        Rng = new TurboPascalRandom(seed);
        Camp = player.Camp ?? new CampState();
        var memory = new EclMemory(profile: content.VmProfile);
        var kit = Kit = PorFight.Kit(content);
        OutsideEffects = new EffectHandlers(Rng.Next, kit?.Names)
        {
            List = () => memory.Party?.Members ?? [],
            CurrentCharacter = () => memory.Party?.Current,
            ExperienceTable = kit?.Experience,
        };
        FightKit.WireLevels(OutsideEffects, kit?.LevelTables);
        // The VM is built on a forwarding host first: the chain needs the loop, the loop needs the VM.
        var front = new ForwardingHost();
        Vm = new EclVm(memory, front) { Scripts = content };
        // ADDNPC runs the level stats 0x2AA87 on the joined record (and DAMAGE's handlers see the race limit).
        FightKit.WireLevels(Vm.Effects, kit?.LevelTables);
        Loop = new AreaLoop(Vm, Rng, player.Area) { MapSource = content.LoadMap, NoWrapOffMap = content.NoWrapOffMap };
        // The overland step (view modes 2–4 → wilderness scripts 25–27): the content's map and columns.
        Loop.OverlandMap = content.Overland();
        Loop.OverlandColumn = mode => content.OverlandColumn(mode - Overland.FirstMode + PorOverland.FirstScript);
        // 0x241A1 → 0x23DCC outside camp (DS:49F3 = 3 or 4): every flag set, the whole list aged.
        Loop.EffectTimeout = (count, unit) => CampTime.TimeOutEffects(memory, Camp.EffectFlags, false, count, unit,
            (c, e) => OutsideEffects.RemoveEffect(c, e[0], e));
        AreaHost = new AreaHost(Loop, player.Host)
        {
            WallSetLoaded = (disk, slot, set) => art.LoadWallSet(disk == 0 ? Vm.CurrentDisk : disk, slot, set),
        };
        Encounter = new EncounterHost(memory, AreaHost, player.Encounter, player.Combat, new PorEncounterData(content))
        {
            CurrentBlock = () => Vm.CurrentScript,
        };
        // DS:49FF: the after-combat step sets it on the VM when the party is destroyed; the area loop clears it.
        Encounter.Vm = Vm;
        if ((player.FightMenu is not null || player.QuickFight) && kit is not null)
            Encounter.FightBuilder = state => kit.Create(memory, state, Rng.Next,
                PorFight.Area(memory, (Encounter.Map as MapPlanes)?.Planes, Vm.CurrentScript, state.Duel, content.NoWrapOffMap), player.FightMenu,
                time: Camp, deferStartEffects: player.FightMenu is not null); // the aura messages on the combat screen
        Loop.MapLoaded += m => Encounter.Map = new MapPlanes(m.PlanesArray);
        front.Target = Encounter;
    }

    /// <summary>
    /// Main menu → Load from a save file: an engine save (<c>.json</c>, docs/module-format.md §15), or an original
    /// <c>SAVGAMx.DAT</c>, converted first (<see cref="Module.Por.PorEngineSave"/>; its party files are looked up next
    /// to it, then in the content's save folder).
    /// </summary>
    /// <param name="started">Called with the game once the save is set up, before its entry (vector 4) runs: a front
    /// end whose hosts read the game (its memory, its generator) hands it over here, since the entry may open screens
    /// (the training hall, PROGRAM 0) that read it before this returns.</param>
    public static PorGame FromSave(IPorContent content, PorViewArt art, string savePath, PorPlayer player, uint seed,
        int markerCounter = 0, Action<PorGame>? started = null)
    {
        var (doc, problem) = ReadSave(content, savePath);
        var g = FromSave(content, art, doc, player, seed, markerCounter, started);
        if (problem is not null)
            g.Vm.Diagnostics.Insert(0, problem);
        return g;
    }

    /// <summary>
    /// An original save's bytes, converted to an engine save first; the party files are looked up in
    /// <paramref name="saveDir"/>, then in the content's save folder (a party that cannot be read is left out, with a
    /// diagnostic, as before).
    /// </summary>
    public static PorGame FromSave(IPorContent content, PorViewArt art, byte[] save, string saveDir, PorPlayer player, uint seed,
        string? fileName = null, int markerCounter = 0, Action<PorGame>? started = null)
    {
        var (doc, problem) = ConvertOriginal(content, save, saveDir, fileName);
        var g = StartLoaded(content, art, doc, player, seed, markerCounter, started, originalFile: true);
        if (problem is not null)
            g.Vm.Diagnostics.Insert(0, problem);
        return g;
    }

    /// <summary>A save file as an engine save: an engine save as it is, an original converted (<see cref="ConvertOriginal"/>).</summary>
    private static (Module.EngineSaveDocument Doc, string? Problem) ReadSave(IPorContent content, string savePath) =>
        Module.Por.PorEngineSave.IsEngineSave(savePath)
            ? (Module.Por.PorEngineSave.Read(savePath), null)
            : ConvertOriginal(content, File.ReadAllBytes(savePath), Path.GetDirectoryName(Path.GetFullPath(savePath))!,
                Path.GetFileName(savePath));

    private static (Module.EngineSaveDocument Doc, string? Problem) ConvertOriginal(IPorContent content, byte[] save, string saveDir,
        string? fileName)
    {
        var module = Module.Por.PorEngineSave.Identity(content);
        try
        {
            return (Module.Por.PorEngineSave.FromFolders(save, fileName, module, content.LoadBlock,
                [saveDir, .. content.SaveFolder is { } f ? new[] { f } : []]), null);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            return (Module.Por.PorEngineSave.FromOriginal(save, readFile: null, module), "party files not loaded: " + e.Message);
        }
    }

    /// <summary>
    /// Main menu → Load from an engine save: the save's memory and script, map $49C5 read again, then vector 4 (and any
    /// script switch). The save must be for the content's VM profile and module id
    /// and version (<see cref="Module.Por.PorEngineSave.Check"/>); different module content is noted in the diagnostics.
    /// </summary>
    /// <param name="markerCounter">DS:6DDC as the session left it (a load keeps it; 0 at the start-up).</param>
    /// <param name="started">Called with the loaded game before vector 4 runs (see the save-file overload).</param>
    public static PorGame FromSave(IPorContent content, PorViewArt art, Module.EngineSaveDocument save, PorPlayer player, uint seed,
        int markerCounter = 0, Action<PorGame>? started = null)
        => StartLoaded(content, art, save, player, seed, markerCounter, started, originalFile: false);

    private static PorGame StartLoaded(IPorContent content, PorViewArt art, Module.EngineSaveDocument save, PorPlayer player,
        uint seed, int markerCounter, Action<PorGame>? started, bool originalFile)
    {
        var g = Loaded(content, art, save, player, seed, originalFile);
        started?.Invoke(g);
        g.RunLoadedEntry(markerCounter);
        return g;
    }

    /// <summary>The interface state DS:49F3 Load read back from the save (0x1F87B): 0 for a save written at the party
    /// menu or in the training hall.</summary>
    public byte LoadedInterfaceState { get; private set; }

    /// <summary>
    /// The party menu's Load (0x1F5E8) of a save: as <see cref="FromSave(IPorContent, PorViewArt, string, PorPlayer, uint)"/>,
    /// except that the menu goes on to the game only when the interface state read back from the save is not 0
    /// (0x19D8A tests DS:49F3). A save written at the party menu or in the training hall (state 0) leaves the program in
    /// the party menu with the save's party and memory (DS:49F8 stays 0): nothing runs here, and the menu's Begin runs
    /// the game loop's first pass on that memory (<see cref="RunNewGameEntry"/>). <see cref="LoadedInterfaceState"/>
    /// tells which.
    /// </summary>
    /// <param name="started">Called with the loaded game (<see cref="LoadedInterfaceState"/> set) before its entry runs.</param>
    public static PorGame LoadAtPartyMenu(IPorContent content, PorViewArt art, string savePath, PorPlayer player, uint seed,
        int markerCounter = 0, Action<PorGame>? started = null)
    {
        var (doc, problem) = ReadSave(content, savePath);
        var g = Loaded(content, art, doc, player, seed);
        if (problem is not null)
            g.Vm.Diagnostics.Insert(0, problem);
        started?.Invoke(g);
        if (g.LoadedInterfaceState != 0)
            g.RunLoadedEntry(markerCounter);
        else
            g.Memory.MarkerCounter = markerCounter;
        return g;
    }

    /// <summary>The save's memory, party and script set up (Load 0x1F5E8 up to its return), nothing run.</summary>
    private static PorGame Loaded(IPorContent content, PorViewArt art, Module.EngineSaveDocument save, PorPlayer player,
        uint seed, bool originalFile = false)
    {
        var notes = Module.Por.PorEngineSave.Check(save, content.VmProfile, Module.Por.PorEngineSave.Identity(content));
        var sg = Module.Por.PorEngineSave.ToSaveGame(save);
        var g = new PorGame(content, art, player, seed);
        g.Vm.Diagnostics.AddRange(notes);
        if (g.ReadLoadedParty(save, originalFile) is { } party)
            g.Memory.Party = party;
        sg.ApplyTo(g.Memory);
        g.Memory.MenuAnimates = g.Memory.Read(AreaLoop.MenuAnimateCell) != 0; // the save's $4AFF (AreaLoop.MenuAnimateCell)
        g.LoadedInterfaceState = sg.InterfaceState;
        g.Vm.SetCurrentScript(sg.CurrentBlock, sg.Disk); // DS:84DC := $49F2 (0x4070)
        g.ReloadSavedMap();
        return g;
    }

    private EclParty? ReadLoadedParty(Module.EngineSaveDocument save, bool originalFile = false)
    {
        var party = Module.Por.PorEngineSave.Party(save);
        // Converted saves keep the original bytes for export. Apply the original Load's party updates on entering play.
        if (party is not null && (originalFile || save.Source is not null))
            PorParty.RefreshOnLoad(party, Content.Program()?.LevelTables());
        return party;
    }

    /// <summary>
    /// Load game (0x1FBB3–0x1FBC4) re-reads map $49C5 when DS:49FA (the save's location type) &lt; 2; then
    /// (0x1FBEE–0x1FC86, location type ≤ 1) for i = 1..3 the cache DS:6AAE+4i := $4AF9+i (id), DS:6AB0+4i := $4AFC+i
    /// (slot), and LoadWallSet(id, slot) when the id is not negative; a slot whose id is negative keeps what it held. So
    /// an area whose vector 4 loads no wall sets still shows the saved walls (DOSBox-X s22).
    /// </summary>
    private void ReloadSavedMap()
    {
        if (Memory.ViewMode >= 2)
            return;
        Loop.LoadMap(Memory.Read(0x49C5));
        for (int i = 1; i <= 3; i++)
        {
            short id = (short)Memory.Read((ushort)(0x4AF9 + i));
            int slot = Memory.Read((ushort)(0x4AFC + i)) & 0xFF;
            if (id >= 0)
                AreaHost.LoadWallSet(slot, id & 0xFF);
        }
    }

    /// <summary>
    /// The training hall's Load (0x1F5E8 from the hall menu 0x19AE0, ECL PROGRAM 0) of a save with interface state 0
    /// (written at the party menu or in a hall): the menu stays up (0x19D8A) and the program goes on in this game (see
    /// <see cref="LoadInHall"/>). False, with nothing changed, for a save of another interface state.
    /// </summary>
    public bool LoadInMenu(string savePath, EclParty party)
    {
        var (doc, problem) = ReadSave(Content, savePath);
        if (Module.Por.PorEngineSave.ToSaveGame(doc).InterfaceState != 0)
            return false;
        ApplyInHall(doc, problem, party);
        return true;
    }

    /// <summary>
    /// The training hall's Load (0x1F5E8 from the hall menu 0x19AE0, ECL PROGRAM 0): Load copies the save's memory (the
    /// four areas with the script area, the square, location type, speed, disk byte) and party over the running game's
    /// and re-reads the map and wall sets (<see cref="ReloadSavedMap"/>); it does not touch the ECL PC, the vectors
    /// DS:49E3–49EB or the current block DS:84DC. The party is <paramref name="party"/> (the hall's list, kept as the
    /// same object): its members are replaced by the save's, the first current (0x1FB99). With interface state 0 the
    /// menu stays up (0x19D8A; DOSBox-X s25: after Begin the script went on at its PC in the loaded script area). With
    /// any other state the program sets DS:49F8 := 1 (0x19D91, so the next script init keeps $4A00–$4A1F and
    /// $6E79–$6E82) and leaves the menu: PROGRAM 0 calls START 0x3379 (0x3B95) and returns into the running ECL run at
    /// its old PC in the loaded script area, with the old vectors and block; nothing enters the area again (the area
    /// loop 0x403E runs once a session). DOSBox-X s39: a step's PICTURE 2; PROGRAM 0 in save I, then Remove × 6 and Load
    /// of save J (same code, other text, its vectors 1 and 4 printing): J's text after PROGRAM 0, no vector 4, after the
    /// step I's vector 1 (EXIT); PC, vectors and DS:84DC 20 as before, DS:49F8 = 1.
    /// **Decided (user, s39):** a game save from another area (its block, $49F2 → DS:84DC after a load, or its disk byte
    /// differ from the running script's) is not copied in: the caller enters it as a new game (<see cref="HallLoad.EnterArea"/>,
    /// its entry and vector 4), instead of the program's run of the old PC over another area's code.
    /// </summary>
    public HallLoad LoadInHall(string savePath, EclParty party)
    {
        var (doc, problem) = ReadSave(Content, savePath);
        var sg = Module.Por.PorEngineSave.ToSaveGame(doc);
        if (sg.InterfaceState != 0 && (sg.CurrentBlock != Vm.CurrentScript || sg.Disk != Vm.CurrentDisk))
            return HallLoad.EnterArea;
        return ApplyInHall(doc, problem, party) == 0 ? HallLoad.StayInMenu : HallLoad.CarryOn;
    }

    private byte ApplyInHall(Module.EngineSaveDocument doc, string? problem, EclParty party)
    {
        var notes = Module.Por.PorEngineSave.Check(doc, Content.VmProfile, Module.Por.PorEngineSave.Identity(Content));
        var sg = Module.Por.PorEngineSave.ToSaveGame(doc);
        Vm.Diagnostics.AddRange(notes);
        if (problem is not null)
            Vm.Diagnostics.Add(problem);
        var members = ReadLoadedParty(doc)?.Members ?? [];
        sg.ApplyTo(Memory);
        Memory.MenuAnimates = Memory.Read(AreaLoop.MenuAnimateCell) != 0; // the save's $4AFF (AreaLoop.MenuAnimateCell)
        party.Members.Clear();
        party.Members.AddRange(members);
        party.CurrentIndex = party.Members.Count > 0 ? 0 : -1;
        Memory.Party = party;
        LoadedInterfaceState = sg.InterfaceState;
        Memory.InterfaceState = sg.InterfaceState; // Load reads DS:49F3 back (0x1F87B); a camp save keeps 2 after the hall
        if (sg.InterfaceState != 0)
            Vm.LoadingSave = true; // DS:49F8 := 1 (0x19D91)
        ReloadSavedMap();
        return sg.InterfaceState;
    }

    /// <summary>
    /// The game loop's first pass on a loaded game (0x403E). The overland marker counter DS:6DDC is not in the save and
    /// Load does not write it, so it goes on from <paramref name="markerCounter"/> (the session's; 0 after the start-up,
    /// 0xE9D3). The loop sets the interface state DS:49F3 := 4, or 3 when $49F2 (the block read at the last command)
    /// is 25–27 with $49E6 = 0 (0x405D–0x408E), before vector 4. At 3 nothing is drawn before the first command (whose
    /// overland interface, START 0x36FE, draws frames c and c + 1). At 4, START 0x3379 (case 4: the redraw, 0x341F)
    /// runs once, inside the first LOADFILES/LOADPIECES once both have run (0x1B05–0x1B2C) or else at 0x40BE, then the
    /// redraw 0x40C8: two marker draws when the overland is shown. A save made on the overland has $49F2 = 25–27, so
    /// its load draws nothing; `save-overland`'s test saves keep save A's $49F2 = 20, so 2 (DOSBox-X: frames 2 + 3).
    /// </summary>
    private void RunLoadedEntry(int markerCounter = 0)
    {
        Memory.MarkerCounter = ((markerCounter % 6) + 6) % 6;
        int block = Memory.Read(AreaLoop.PreviousScript) & 0xFF;
        bool outdoors = block is >= 25 and <= 27 && Memory.Read(AreaLoop.View3DCell) == 0; // DS:49F3 := 3 (0x408E)
        Memory.InterfaceState = (byte)(outdoors ? 3 : 4); // 0x405D / 0x408E, before vector 4
        // A NEWECL in this vector 4 is followed only after the next command's vector (DS:442F stays set).
        EntryRun = Vm.RunLoadedGame();
        // At DS:49F3 = 4, START 0x3379 case 4 (0x1B1A inside the LOADFILES / LOADPIECES pair, or 0x40BE after the vector)
        // draws every window and the view, then 0x40C8 redraws the view: a CLEARBOX blank of the old game is gone, the
        // view's box with it. At 3 neither runs (the window keeps what vector 4 drew). [?] Drawn here after vector 4:
        // a CLEARBOX in vector 4 after its LOADFILES pair would leave the box ring blank in the program.
        if (!outdoors)
            Loop.DrawInterface();
        FinishEntry(follow: false, redraw: !outdoors);
    }

    /// <summary>Enters area script <paramref name="script"/> with fresh VM memory at a square: script init, then vector 4.</summary>
    /// <param name="started">Called with the game before the script init runs (as for a load's entry).</param>
    public static PorGame Enter(IPorContent content, PorViewArt art, int script, int x, int y, int facing, PorPlayer player, uint seed,
        Action<PorGame>? started = null)
    {
        int disk = content.ScriptDisk(script) ?? throw new ArgumentException($"no ECL block {script} in {content.Origin}");
        var g = new PorGame(content, art, player, seed);
        g.Memory.Disk = (byte)disk;
        g.Memory.Position.X = (byte)x;
        g.Memory.Position.Y = (byte)y;
        g.Memory.Position.Facing = (byte)facing;
        g.Vm.LoadScript(script, disk);
        started?.Invoke(g);
        g.EntryRun = g.Vm.RunVector(EclLayout.VectorInit);
        g.Vm.CommandRead();
        g.FinishEntry(follow: true);
        return g;
    }

    /// <summary>The new game's script, disk and square: GAME.OVR 0xE600 (start-up) zeroes the VM and puts the party on
    /// 15,1 facing west (DS:6AAD–6AAF); the party menu (0x19AE7) sets the disk byte DS:5376 to 3; the game loop (0x403E)
    /// then loads ECL&lt;disk&gt; block $49F2 (0 after the start-up) and runs it (docs/re/start-menu.md "Begin").</summary>
    public const int NewGameScript = 0, NewGameDisk = 3, NewGameX = 15, NewGameY = 1, NewGameFacing = Facing.West;

    /// <summary>
    /// The party menu's Begin Adventuring (0x19DB8) on a new game: fresh VM memory with <paramref name="party"/> (as the
    /// menu's joins left it: order, current member) and the party size $6E3E, at the new game's square. Nothing runs yet:
    /// <see cref="RunNewGameEntry"/> runs the script, so a front end can show the game before the entry's first text.
    /// </summary>
    public static PorGame NewGame(IPorContent content, PorViewArt art, EclParty party, PorPlayer player, uint seed)
    {
        var g = new PorGame(content, art, player, seed);
        NewGameState(content, g.Memory, party);
        return g;
    }

    /// <summary>
    /// The state at the party menu before Begin: fresh VM memory (the start-up 0xE600 zeroes the zones) with the party
    /// and its size $6E3E, the disk byte, the square, the location type 1 and game speed 1 (<see cref="EclMemory"/>'s
    /// defaults, DS:49FA / DS:49E2 at 0xE9C3 / 0xE999), and the picture flags as the start-up sets them (DS:49F5 =
    /// DS:49F6 = 1 at 0xE965 / 0xE96A: portraits and monster pictures on; the engine keeps them in VM $49FF, the value the
    /// save writes, 0x1FEDC).
    /// </summary>
    private static void NewGameState(IPorContent content, EclMemory m, EclParty party)
    {
        m.Party = party;
        m.Write(EclPartyRules.PartySizeCell, (ushort)party.Members.Count);
        m.Disk = (byte)(content.ScriptDisk(NewGameScript) ?? NewGameDisk);
        m.Position.X = NewGameX;
        m.Position.Y = NewGameY;
        m.Position.Facing = NewGameFacing;
        m.Write(CampSettings.PicturesCell, 3);
    }

    /// <summary>
    /// The party menu's Save (0x19D9C → the save 0x1FD3E) before Begin: the save of the state <see cref="NewGame"/>
    /// starts from, as the program writes it there. Interface state 0 (the menu's DS:49F3, 0x19AE7), location type 1,
    /// disk 3 (also $6E12), game speed 1 into $49FC, picture flags 3 into $49FF, the wall-set cells from the start-up's
    /// DS:6AB2–6ABC (0xE926–0xE963 → the save's loop 0x1FE70: $4AFA = 0, $4AFD = 1, the others 0xFFFF), the party at
    /// 15,1 facing west, an empty script area; the party's records as they are. Load from the party menu
    /// (<see cref="LoadAtPartyMenu"/>) takes it back into the menu.
    /// </summary>
    public static Module.EngineSaveDocument UnstartedSave(IPorContent content, EclParty party, char letter)
    {
        var m = new EclMemory(profile: content.VmProfile);
        NewGameState(content, m, party);
        ushort[] wallSets = [0, 0xFFFF, 0xFFFF, 1, 0xFFFF, 0xFFFF];
        for (int k = 0; k < wallSets.Length; k++)
            m.Write((ushort)(0x4AFA + k), wallSets[k]);
        return MenuSave(content, m, party, letter);
    }

    /// <summary>
    /// The party menu's Save after a Load that stayed in the menu (<see cref="LoadAtPartyMenu"/>, interface state 0):
    /// the loaded memory as it is (its $49FF, wall-set cells and speed came back with the load) with the menu's party
    /// and its size $6E3E, interface state 0. DOSBox-X (s20): a save made there equals the loaded one but for the stem.
    /// </summary>
    public Module.EngineSaveDocument SaveAtPartyMenu(EclParty party, char letter)
    {
        Memory.Party = party;
        Memory.Write(EclPartyRules.PartySizeCell, (ushort)party.Members.Count);
        return MenuSave(Content, Memory, party, letter);
    }

    private static Module.EngineSaveDocument MenuSave(IPorContent content, EclMemory m, EclParty party, char letter)
    {
        var bytes = PorSaveGame.Build(m, party.Members.Count, letter, interfaceState: 0);
        return Module.Por.PorEngineSave.FromOriginal(bytes, party, Module.Por.PorEngineSave.Identity(content),
            content.LoadBlock, content.VmProfile);
    }

    /// <summary>
    /// The game loop's first pass with DS:49F8 = 0 (0x403E → 0x7814): ECL&lt;disk&gt; block $49F2 (low byte, 0x406B) loaded,
    /// its init, then vector 4. A new game has $49F2 = 0; a save loaded at the party menu with interface state 0 (the
    /// menu's or the training hall's, <see cref="LoadAtPartyMenu"/>) goes on from its own $49F2. The party is the menu's:
    /// <paramref name="party"/> (null: the one in memory) and its size $6E3E (the menu's joins and removals keep it).
    /// </summary>
    public void RunNewGameEntry(EclParty? party = null)
    {
        if (party is not null)
        {
            Memory.Party = party;
            Memory.Write(EclPartyRules.PartySizeCell, (ushort)party.Members.Count);
        }
        Vm.LoadScript(Memory.Read(AreaLoop.PreviousScript) & 0xFF, Memory.Disk);
        Memory.InterfaceState = 4; // 0x405D (the new game's block 0 is a 3D area)
        EntryRun = Vm.RunVector(EclLayout.VectorInit);
        Vm.CommandRead();
        FinishEntry(follow: true);
    }

    /// <summary>
    /// A session on a VM state set by <paramref name="setState"/> (e.g. a DOSBox capture's dump) with
    /// <paramref name="map"/> in memory; no script runs. For replays and tests.
    /// </summary>
    public static PorGame Restore(IPorContent content, PorViewArt art, PorPlayer player, uint seed, Action<EclVm> setState,
        AreaMap? map, EclParty? party = null)
    {
        var g = new PorGame(content, art, player, seed);
        g.Memory.Party = party;
        setState(g.Vm);
        if (map is not null)
            g.Loop.SetMap(map);
        return g;
    }

    private void FinishEntry(bool follow, bool redraw = true)
    {
        if (follow && Vm.ScriptChanged)
            EntryRun = Vm.FollowScriptChanges();
        if (Loop.Map is { } m)
        {
            m.Bounded = Content.NoWrapOffMap(Vm.CurrentScript);
            var p = Memory.Position;
            p.WallAhead = (byte)m.Wall(p.X, p.Y, p.Facing);
        }
        if (redraw)
            Loop.Redraw();
    }

    /// <summary>The four backdrop colours now (<see cref="BackdropRule"/>).</summary>
    public Backdrop Backdrop()
    {
        var d = BackdropRule.For(Memory, Art.Program.SkyColours, Loop.EventHere());
        if (Art.BackdropBase is not { } b)
            return d;
        // Under the Amiga walls the bands take the Amiga program's pens of the same palette (PorAmigaArt.BackdropPen).
        byte P(byte c) => Module.Por.PorAmigaArt.BackdropPen(c) is { } pen ? (byte)(b + pen) : c;
        return new Backdrop(P(d.Sky), P(d.Horizon1), P(d.Horizon2), P(d.Ground));
    }

    /// <summary>Draws the 88×88 view (3D or area map) with its top-left pixel at (x, y). Nothing without a 3D map.</summary>
    public void DrawView(IndexedFrame f, int x, int y, bool areaMap)
    {
        if (Loop.Map is not { } map)
            return;
        var p = Memory.Position;
        if (areaMap)
            ViewRaster.DrawAreaMap(f, x, y, View3D.ComposeAreaMap(map, p.X, p.Y, p.Facing, Art.Symbols.Bases[4]), Art.Symbols);
        else
            ViewRaster.Draw3D(f, x, y, Backdrop(), View3D.Compose(map, p.X, p.Y, p.Facing, Art.Layout, Art.Walls), Art.Symbols);
    }

    public IndexedFrame RenderView(bool areaMap)
    {
        var f = new IndexedFrame(ViewRaster.ViewPixels, ViewRaster.ViewPixels);
        DrawView(f, 0, 0, areaMap);
        return f;
    }

    /// <summary>A host that forwards every call to <see cref="Target"/> (set once the chain is built).</summary>
    private sealed class ForwardingHost : IEclHost
    {
        public IEclHost Target { get; set; } = null!;
        public void Print(string text, bool clearFirst) => Target.Print(text, clearFirst);
        public void PrintReturn() => Target.PrintReturn();
        public void ClearBox() => Target.ClearBox();
        public void Delay() => Target.Delay();
        public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices) => Target.VerticalMenu(prompt, choices);
        public ushort HorizontalMenu(IReadOnlyList<string> choices) => Target.HorizontalMenu(choices);
        public int InputNumber() => Target.InputNumber();
        public string InputString() => Target.InputString();
        public void Who(string prompt) => Target.Who(prompt);
        public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive) => Target.Parley(haughty, sly, nice, meek, abusive);
        public ushort? EncounterMenu(EclEncounterMenu menu) => Target.EncounterMenu(menu);
        public void LoadCharacter(ushort index) => Target.LoadCharacter(index);
        public void LoadMonster(ushort monster, ushort count, ushort picture) => Target.LoadMonster(monster, count, picture);
        public void SetupMonster(ushort monster, ushort distance, ushort picture) => Target.SetupMonster(monster, distance, picture);
        public void Approach() => Target.Approach();
        public void ClearMonsters() => Target.ClearMonsters();
        public ushort PartyStrength() => Target.PartyStrength();
        public EclPartyCheck CheckParty(ushort field, ushort effect) => Target.CheckParty(field, effect);
        public void Combat() => Target.Combat();
        public void Treasure(IReadOnlyList<ushort> values) => Target.Treasure(values);
        public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Target.Rob(wholeParty, moneyPercent, itemChance);
        public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) => Target.Damage(attack, dice, sides, bonus, extra);
        public void SpriteOff() => Target.SpriteOff();
        public bool FindItem(ushort item) => Target.FindItem(item);
        public void AddNpc(ushort monster, ushort morale) => Target.AddNpc(monster, morale);
        public (ushort Slot, ushort Character)? Spell(ushort spell) => Target.Spell(spell);
        public void Picture(ushort picture) => Target.Picture(picture);
        public void LoadMap(int map) => Target.LoadMap(map);
        public void LoadOutdoorPicture() => Target.LoadOutdoorPicture();
        public void LoadWallSet(int slot, int? set) => Target.LoadWallSet(slot, set);
        public void Clock(ushort minutes) => Target.Clock(minutes);
        public void Call(ushort routine) => Target.Call(routine);
        public void Program(ushort program) => Target.Program(program);
        public void Protect(ushort address) => Target.Protect(address);
        public ushort Random(ushort range) => Target.Random(range);
        public void ScriptChanging(ushort script) => Target.ScriptChanging(script);
        public int? PickCharacter(string prompt) => Target.PickCharacter(prompt);
        public void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs) => Target.DrawGlyphs(address, glyphs);
        public EclCharacter? CreateNpc(ushort monster) => Target.CreateNpc(monster);
        public void DamageReport(EclDamageReport report) => Target.DamageReport(report);
        public void DamageEnd(bool partyKilled) => Target.DamageEnd(partyKilled);
        public void TextHome() => Target.TextHome();
    }
}

/// <summary>What a Load in the training hall did (<see cref="PorGame.LoadInHall"/>).</summary>
public enum HallLoad
{
    /// <summary>A state-0 save copied in; the hall stays up (0x19D8A).</summary>
    StayInMenu,
    /// <summary>A game save of the running area copied in; the hall ends and the running script goes on at its PC.</summary>
    CarryOn,
    /// <summary>A game save of another area: nothing copied; the caller enters it as a loaded game (user decision s39).</summary>
    EnterArea,
}
