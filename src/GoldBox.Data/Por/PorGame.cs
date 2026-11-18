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
                time: Camp);
        Loop.MapLoaded += m => Encounter.Map = new MapPlanes(m.PlanesArray);
        front.Target = Encounter;
    }

    /// <summary>
    /// Main menu → Load from a save file: an engine save (<c>.json</c>, docs/module-format.md §15), or an original
    /// <c>SAVGAMx.DAT</c>, converted first (<see cref="Module.Por.PorEngineSave"/>; its party files are looked up next
    /// to it, then in the content's save folder).
    /// </summary>
    public static PorGame FromSave(IPorContent content, PorViewArt art, string savePath, PorPlayer player, uint seed) =>
        Module.Por.PorEngineSave.IsEngineSave(savePath)
            ? FromSave(content, art, Module.Por.PorEngineSave.Read(savePath), player, seed)
            : FromSave(content, art, File.ReadAllBytes(savePath), Path.GetDirectoryName(Path.GetFullPath(savePath))!, player, seed,
                Path.GetFileName(savePath));

    /// <summary>
    /// An original save's bytes, converted to an engine save first; the party files are looked up in
    /// <paramref name="saveDir"/>, then in the content's save folder (a party that cannot be read is left out, with a
    /// diagnostic, as before).
    /// </summary>
    public static PorGame FromSave(IPorContent content, PorViewArt art, byte[] save, string saveDir, PorPlayer player, uint seed,
        string? fileName = null)
    {
        var module = Module.Por.PorEngineSave.Identity(content);
        Module.EngineSaveDocument doc;
        string? problem = null;
        try
        {
            doc = Module.Por.PorEngineSave.FromFolders(save, fileName, module, content.LoadBlock,
                [saveDir, .. content.SaveFolder is { } f ? new[] { f } : []]);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            problem = "party files not loaded: " + e.Message;
            doc = Module.Por.PorEngineSave.FromOriginal(save, readFile: null, module);
        }
        var g = FromSave(content, art, doc, player, seed);
        if (problem is not null)
            g.Vm.Diagnostics.Insert(0, problem);
        return g;
    }

    /// <summary>
    /// Main menu → Load from an engine save: the save's memory and script, map $49C5 read again, then vector 4 (and any
    /// script switch). The save must be for the content's VM profile and module id
    /// (<see cref="Module.Por.PorEngineSave.Check"/>); a different module version or content is noted in the diagnostics.
    /// </summary>
    public static PorGame FromSave(IPorContent content, PorViewArt art, Module.EngineSaveDocument save, PorPlayer player, uint seed)
    {
        var notes = Module.Por.PorEngineSave.Check(save, content.VmProfile, Module.Por.PorEngineSave.Identity(content));
        var sg = Module.Por.PorEngineSave.ToSaveGame(save);
        var g = new PorGame(content, art, player, seed);
        g.Vm.Diagnostics.AddRange(notes);
        if (Module.Por.PorEngineSave.Party(save) is { } party)
            g.Memory.Party = party;
        sg.ApplyTo(g.Memory);
        g.Vm.SetCurrentScript(sg.ScriptId, sg.Disk);
        // Load game (0x1FBB3–0x1FBC4) re-reads map $49C5 before vector 4 when DS:49FA (the save's location type) < 2.
        if (g.Memory.ViewMode < 2)
            g.Loop.LoadMap(g.Memory.Read(0x49C5));
        // A NEWECL in this vector 4 is followed only after the next command's vector (DS:442F stays set).
        g.EntryRun = g.Vm.RunLoadedGame();
        g.FinishEntry(follow: false);
        return g;
    }

    /// <summary>Enters area script <paramref name="script"/> with fresh VM memory at a square: script init, then vector 4.</summary>
    public static PorGame Enter(IPorContent content, PorViewArt art, int script, int x, int y, int facing, PorPlayer player, uint seed)
    {
        int disk = content.ScriptDisk(script) ?? throw new ArgumentException($"no ECL block {script} in {content.Origin}");
        var g = new PorGame(content, art, player, seed);
        g.Memory.Disk = (byte)disk;
        g.Memory.Position.X = (byte)x;
        g.Memory.Position.Y = (byte)y;
        g.Memory.Position.Facing = (byte)facing;
        g.Vm.LoadScript(script, disk);
        g.EntryRun = g.Vm.RunVector(EclLayout.VectorInit);
        g.Vm.CommandRead();
        g.FinishEntry(follow: true);
        return g;
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

    private void FinishEntry(bool follow)
    {
        if (follow && Vm.ScriptChanged)
            EntryRun = Vm.FollowScriptChanges();
        if (Loop.Map is { } m)
        {
            m.Bounded = Content.NoWrapOffMap(Vm.CurrentScript);
            var p = Memory.Position;
            p.WallAhead = (byte)m.Wall(p.X, p.Y, p.Facing);
        }
        Loop.Redraw();
    }

    /// <summary>The four backdrop colours now (<see cref="BackdropRule"/>).</summary>
    public Backdrop Backdrop() => BackdropRule.For(Memory, Art.Program.SkyColours, Loop.EventHere());

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
        public void TextHome() => Target.TextHome();
    }
}
