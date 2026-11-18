using System.Globalization;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// The Client's side of a running game, on the logic thread: the innermost ECL host (text, menus, input lines and the
/// opcodes whose effect is not read), the encounter screens, the stand-in fight, the loot, shop, temple and camp
/// screens (<see cref="VenueScreens"/>), the door menu. Every question goes
/// to the UI thread through <see cref="ScriptRunner.Ask"/>; text and pictures change state the UI draws while the
/// logic is parked.
/// </summary>
public sealed class ClientPlayer(ScriptRunner runner, Func<EclMemory> memory) : IEclHost, IEncounterUi, ICombatRunner, IAreaUi
{
    /// <summary>The program's text window (PRINT / PRINTCLR box, cursor, paging).</summary>
    public TextWindow Text { get; } = new();

    /// <summary>The program's texts for the screens (our wordings until <see cref="Resources"/> are set).</summary>
    public ClientResources Resources { get; set; } = new();

    /// <summary>The game (set once it is built): its random generator and encounter host.</summary>
    public Func<GoldBox.Data.Por.PorGame?> Game { get; set; } = () => null;

    /// <summary>CLEARBOX blanked the picture window (1,1)–(15,15) [?] until the next command (the redraw is not read).</summary>
    public bool ViewBlanked { get; set; }

    private Dialog? _dialog;
    private Dialog Dialog => _dialog ??= new Dialog(runner.Ask, Text);

    /// <summary>What the 3D view shows instead of the walls while an encounter or picture is up (null = the view).</summary>
    public string[]? ViewOverlay { get; private set; }

    /// <summary>A one-line message (the area loop's own, or a stand-in notice) shown on the first text row.</summary>
    public string? Notice { get; set; }

    /// <summary>Host calls that have no player-facing effect yet, for the log.</summary>
    public List<string> Log { get; } = [];

    private int Choose(string? prompt, IReadOnlyList<string> items, bool vertical = false, int? cancel = null) =>
        runner.Ask(new ChoiceRequest(prompt, items, vertical, cancel)) is int i ? i : 0;

    /// <summary>A notice the player must acknowledge (stand-ins for screens not built yet).</summary>
    private void Acknowledge(string text)
    {
        Text.Print(text, clear: true, colour: 14);
        runner.Ask(new KeyRequest());
    }

    /// <summary>The text window waits for a key when a page is full (START.EXE 0x7A12).</summary>
    public void InstallPaging() => Text.Pause = () => runner.Ask(new KeyRequest());

    // ---- Text ----------------------------------------------------------------------------------------------------
    /// <summary>PRINT / PRINTCLR (GAME.OVR 0x174F): PRINTCLR starts at column 1, row 17 and blanks the box; colour 10.</summary>
    public void Print(string text, bool clearFirst)
    {
        if (clearFirst)
            Text.Home();
        Text.Print(text, clearFirst);
    }
    public void PrintReturn() => Text.NewLine();
    /// <summary>CLEARBOX (0x385A): blanks the picture window, not the text box.</summary>
    public void ClearBox() => ViewBlanked = true;
    public void TextHome() => Text.Home();

    /// <summary>DELAY: game speed DS:49E2 (<see cref="EclMemory.GameSpeed"/>) × 225 ms; speed 0 does not wait.</summary>
    public void Delay()
    {
        int speed = memory().GameSpeed;
        if (speed is > 0 and <= 9)
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * speed)));
    }

    // ---- Menus and input -----------------------------------------------------------------------------------------
    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices) => (ushort)Choose(prompt, Upper(choices), vertical: true);
    public ushort HorizontalMenu(IReadOnlyList<string> choices) => (ushort)Choose(null, Upper(choices));

    private static string[] Upper(IReadOnlyList<string> s) => [.. s.Select(c => c.ToUpperInvariant())];

    public int InputNumber()
    {
        var s = runner.Ask(new TextRequest("NUMBER:", true, 6)) as string ?? "";
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public string InputString() => runner.Ask(new TextRequest("", false, 40)) as string ?? "";

    /// <summary>WHO: [?] Lee only (the player picks a character, who becomes current).</summary>
    public void Who(string prompt)
    {
        if (PickCharacter(prompt) is { } i && memory().Party is { } party)
            party.CurrentIndex = i;
    }

    /// <summary>WHO's pick (0x3879): a vertical list of the party under the prompt; [?] Escape is not offered (what it leaves is not read).</summary>
    public int? PickCharacter(string prompt)
    {
        var party = memory().Party;
        if (party is null || party.Members.Count == 0)
            return null;
        var names = party.Members.Select(m => m.Name.ToUpperInvariant()).ToList();
        return Math.Clamp(Choose(prompt.ToUpperInvariant(), names, vertical: true), 0, names.Count - 1);
    }

    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive) => 0; // EncounterHost answers
    public ushort? EncounterMenu(EclEncounterMenu menu) => null;                                       // EncounterHost answers

    // ---- Opcodes the encounter host or the VM answers; the rest are Lee-only stand-ins ----------------------------
    public void LoadCharacter(ushort index) => Log.Add($"LOADCHAR {index} [?]");
    public void LoadMonster(ushort monster, ushort count, ushort picture) { }
    public void SetupMonster(ushort monster, ushort distance, ushort picture) { }
    public void Approach() { }
    public void ClearMonsters() { }
    public ushort PartyStrength() => 0;
    public EclPartyCheck CheckParty(ushort field, ushort effect)
    {
        Log.Add($"CHKPARTY {field} {effect} [?] answered 0");
        return default;
    }
    public void Combat() { }
    public void Treasure(IReadOnlyList<ushort> values) { }
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Log.Add($"ROB {wholeParty} {moneyPercent} {itemChance} [?] not applied");
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) =>
        Log.Add($"DAMAGE {attack} {dice}d{sides}+{bonus} {extra} [?] not applied");
    public void SpriteOff() { }
    public bool FindItem(ushort item) => false;
    public void AddNpc(ushort monster, ushort morale) => Log.Add($"ADDNPC {monster} {morale} [?] not applied");
    public (ushort Slot, ushort Character)? Spell(ushort spell) => null;
    public void Picture(ushort picture) { }
    public void LoadMap(int map) { }
    public void LoadOutdoorPicture() => Log.Add("LOADFILES outdoors (wilderness picture)");
    /// <summary>The wall sets loaded into slots 1..3 (the program's cache the save copies into $4AFA–$4AFF); null = as the memory had them.</summary>
    private readonly int?[] _wallSets = new int?[3];
    private readonly bool[] _wallSetKnown = new bool[3];

    public void LoadWallSet(int slot, int? set)
    {
        if (slot is >= 1 and <= 3)
        {
            _wallSets[slot - 1] = set;
            _wallSetKnown[slot - 1] = true;
        }
    }
    public void Clock(ushort minutes) { }
    public void Call(ushort routine) => Log.Add($"CALL {routine:X4}");
    public void Protect(ushort address) { } // the code wheel: imported modules skip it (PLAN 6)
    public ushort Random(ushort range) => 0; // AreaHost draws from the game's generator
    public void ScriptChanging(ushort script) => Log.Add($"NEWECL {script}");

    /// <summary>PROGRAM: 0 training hall, 8 end game (not built yet), 9 camp (0x3B56: the camp menu, then the run ends).</summary>
    public void Program(ushort program)
    {
        if (program == 9)
        {
            Camp();
            return;
        }
        string what = program switch { 0 => "THE TRAINING HALL", 8 => "THE END GAME", _ => "" };
        if (what.Length > 0)
            Acknowledge(what + " IS NOT IN THIS BUILD YET.");
    }

    // ---- IEncounterUi ---------------------------------------------------------------------------------------------
    public void ShowEncounter(byte sprite, byte picture, int distance) =>
        ViewOverlay = distance == 0
            ? ["CLOSE-UP", $"PIC {picture}", "", "(ART NOT", "IN YET)"]
            : ["MONSTER", $"SPRITE {sprite}", $"DISTANCE {distance}", "", "(ART NOT", "IN YET)"];

    public void ShowPicture(byte picture, byte? head) =>
        ViewOverlay = head is { } h ? ["PICTURE", $"HEAD {h}", $"BODY {picture}"] : ["PICTURE", $"{picture}"];

    public void RestoreView() => ViewOverlay = null;

    public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
    {
        if (text is not null)
            Text.Print(text, clear: true);
        string[] items = kind == EncounterMenuKind.CombatWaitFleeAdvance
            ? ["COMBAT", "WAIT", "FLEE", "ADVANCE"]
            : ["COMBAT", "WAIT", "FLEE", "PARLEY"];
        return Choose(null, items);
    }

    public void ShowEncounterMessage(EncounterMessage message)
    {
        // Our wording; the program's two fixed lines (GAME.OVR unit offsets 0x1ED4 / 0x1EE5) are not reproduced.
        Text.Print(message == EncounterMessage.BothSidesWait ? "BOTH SIDES WAIT." : "THE MONSTERS FLEE.", clear: true);
        runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(900)));
    }

    public int ChooseParleyAttitude() => Choose(null, ["HAUGHTY", "SLY", "NICE", "MEEK", "ABUSIVE"]);

    /// <summary>LoadCombatIcon: the combat screen draws the monsters of icon slot <paramref name="slot"/> (record 0xBF) with this CPIC.</summary>
    public void LoadCombatIcon(byte picture, int slot) => FightView.IconPictures[slot] = picture;

    // ---- ICombatRunner ---------------------------------------------------------------------------------------------
    /// <summary>
    /// No fight was built (game files without the fight tables): a notice, then the creatures keep their status as
    /// loaded, so the after-combat step treats the fight as won. A stand-in, not the game's outcome.
    /// </summary>
    public void Fight(EncounterState state) =>
        Acknowledge($"A FIGHT WITH {state.Monsters.Count} CREATURES. THE FIGHT IS NOT IN THIS BUILD YET: IT COUNTS AS WON.");

    /// <summary>What the combat screen shows (<see cref="Screens.CombatScreen"/>); active while a fight runs.</summary>
    public CombatView FightView { get; } = new();

    private CombatChooser? _chooser;

    /// <summary>The player's side of the combat menu (passed to the game as <c>PorPlayer.FightMenu</c>).</summary>
    public CombatChooser Chooser => _chooser ??= new CombatChooser(runner.Ask, FightView) { Resources = () => Resources };

    /// <summary>
    /// The real fight (arena and placement done): shown on the combat screen while it runs; party members not on Quick
    /// get the combat menu (<see cref="Chooser"/>). The game speed is DS:49E2 (<see cref="EclMemory.GameSpeed"/>): the
    /// Done menu's Speed changes it for the rest of the game, as the camp's Alter Speed does.
    /// </summary>
    public void Fight(EncounterState state, GoldBox.Core.Game.Fight fight)
    {
        int speed = memory().GameSpeed;
        FightView.Speed = (byte)(speed <= 9 ? speed : 0);
        FightView.Dungeon = memory().ViewMode == 1; // [?] the tile set follows DS:49FA (combat-map.md 0xD815)
        Chooser.Attach(fight);
        try
        {
            fight.Run();
        }
        finally
        {
            Chooser.Detach(fight);                 // copies the menu's speed into the view
            memory().GameSpeed = FightView.Speed;
        }
    }

    /// <summary>The NPC share window, the summary and the loot menu (<see cref="VenueScreens.Loot"/>), DS:49F3 = 6.</summary>
    public void Loot(EncounterState state, AfterCombatOutcome outcome)
    {
        if (Trade(state) is not { } t)
            return;
        Interface = Venue.Loot;
        try
        {
            VenueScreens.Loot(t, outcome, state.Duel, Game()?.Encounter.NpcsTakingShare ?? []);
        }
        finally
        {
            Interface = null;
            ViewOverlay = null; // [?] as after the shop: the area display is drawn again after the loot step
        }
    }

    /// <summary>The "party destroyed" screen (0x6B37–0x6BAA): the program's message and a key.</summary>
    public void PartyDestroyed(EncounterState state)
    {
        Text.Print(Resources.Texts[Msg.MonstersRejoice], clear: true);
        runner.Ask(new KeyRequest());
    }

    public void Shop(EncounterState state) => InVenue(state, VenueScreens.Shop);
    public void Temple(EncounterState state) => InVenue(state, VenueScreens.Temple);

    /// <summary>Shop and temple: DS:49F3 = 1 while the menu runs.</summary>
    private void InVenue(EncounterState state, Action<TradeContext> screen)
    {
        if (Trade(state) is not { } t)
            return;
        Interface = Venue.Shop;
        try
        {
            screen(t);
        }
        finally
        {
            Interface = null;
            // [?] The shop and temple screens replace the area display; when the program draws the 3D view again is
            // not read. CLRMON before them clears the picture flag, so the script's PICTURE 255 does not restore it:
            // we go back to the view here.
            ViewOverlay = null;
        }
    }

    /// <summary>The interface state the screens are in (DS:49F3: 1 shop/temple, 2 camp, 6 loot), null while exploring.</summary>
    public Venue? Interface { get; private set; }

    /// <summary>The context of the money and item screens on <paramref name="state"/>'s pool and treasure list.</summary>
    private TradeContext? Trade(EncounterState? state)
    {
        if (memory().Party is not { } party)
            return null;
        var game = Game();
        return new TradeContext
        {
            Dialog = Dialog,
            Text = Resources.Texts,
            Party = party,
            Pool = state?.Money ?? new uint[7],
            Treasure = state?.TreasureItems ?? [],
            Random = n => game is null ? 0 : game.Rng.Next((ushort)n),
            Word = Resources.ItemWord,
            IsScroll = Resources.IsScroll,
            TempleNames = Resources.TempleNames,
            Halt = Halt,
        };
    }

    /// <summary>The run-time error the original program would have quit to DOS with; the game is over (null = running).</summary>
    public ProgramHaltException? Halted { get; set; }

    /// <summary>
    /// The original quits to DOS here (a Turbo Pascal run-time error, e.g. 200 in Share with no member to take a share):
    /// the game ends. Our message in the text window and a key, then the Client closes (<see cref="Quit"/>); the caller
    /// throws the halt on, so the logic stops where the program did.
    /// </summary>
    public void Halt(ProgramHaltException e)
    {
        Halted = e;
        Log.Add($"program halt: run-time error {e.Code} ({e.Message})");
        Console.Error.WriteLine($"game logic: the original program halts here with run-time error {e.Code}: {e.Message}");
        Text.Print(HaltNotice(e), clear: true, colour: 14);
        runner.Ask(new KeyRequest());
        Quit?.Invoke();
    }

    /// <summary>The line shown when the original program would quit to DOS (our wording).</summary>
    public static string HaltNotice(ProgramHaltException e) =>
        $"RUN-TIME ERROR {e.Code.ToString(CultureInfo.InvariantCulture)}: THE ORIGINAL GAME QUITS TO DOS HERE. THE GAME IS OVER.";

    /// <summary>[?] The item maker (GAME.OVR 0x25C99) is not read: a blank item record of the type, as the headless runner.</summary>
    public byte[] MakeRandomItem(byte type)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = type;
        return r;
    }

    // ---- IAreaUi ---------------------------------------------------------------------------------------------------
    public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices)
    {
        var items = choices.Select(c => c.ToString().ToUpperInvariant()).Append("EXIT").ToList();
        // Our wording for the menu's title; the program's (GAME.OVR cs:0xD64) is not reproduced.
        int i = Choose(barrier == DoorBarrier.Barred ? "A BARRED DOOR." : "A LOCKED DOOR.", items, cancel: items.Count - 1);
        return i >= 0 && i < choices.Count ? choices[i] : null;
    }

    public void Bump() { }

    public void Message(AreaMessage message)
    {
        if (message == AreaMessage.NoAreaMapHere)
            Notice = "NO AREA MAP HERE.";
    }

    /// <summary>
    /// The program's camp and rest variables (DS:6DC2, DS:6DD8, DS:49FB, DS:471E, DS:6DD0): the game's
    /// (<see cref="GoldBox.Data.Por.PorGame.Camp"/>, shared with the clock of exploration and the fights' end of round);
    /// a set of the Client's own only while no game is built.
    /// </summary>
    public CampState CampState => Game()?.Camp ?? _noGameCamp;
    private readonly CampState _noGameCamp = new();

    /// <summary>
    /// The camp's effect engine, set up as the game's outside a fight (<see cref="GoldBox.Data.Por.PorGame.OutsideEffects"/>):
    /// the race-name tables, the experience table and the level seams (<see cref="FightKit.WireLevels"/>: the race level
    /// limit, the level stats 0x2AA87). A fresh one per camp: the camp's spells hook their own caster into it.
    /// </summary>
    public EffectHandlers CampEffects(EclParty party)
    {
        var game = Game();
        Func<ushort, ushort> random = n => game is null ? (ushort)0 : game.Rng.Next(n);
        var kit = game?.Kit;
        var effects = new EffectHandlers(random, kit?.Names)
        {
            List = () => party.Members,
            CurrentCharacter = () => party.Current,
            ExperienceTable = kit?.Experience,
        };
        FightKit.WireLevels(effects, kit?.LevelTables);
        return effects;
    }

    /// <summary>Where the camp's Save writes (the player's save folder; the game folder is never written).</summary>
    public string? SaveFolder { get; set; }

    /// <summary>Quit to DOS (the camp's Save and Drop questions): closes the Client.</summary>
    public Action? Quit { get; set; }

    /// <summary>
    /// The camp menu (0x196A0, <see cref="VenueScreens.Camp"/>) after vector 2; true when a rest was interrupted (the
    /// area loop then runs vector 3).
    /// </summary>
    public bool Camp()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return false;
        var before = Interface;
        Interface = Venue.Camp;
        try
        {
            return VenueScreens.Camp(CampContextFor(t), Resources.CampMenu);
        }
        finally
        {
            Interface = before;
        }
    }

    /// <summary>The camp's rules on this game: spell table, scroll test, the effect engine out of combat, the generator.</summary>
    private CampContext CampContextFor(TradeContext t)
    {
        var mem = memory();
        var game = Game();
        Func<ushort, ushort> random = n => game is null ? (ushort)0 : game.Rng.Next(n);
        var table = Resources.Spells ?? new SpellTable(new Dictionary<int, SpellInfo>());
        var spells = new CampSpells(table, it => Resources.ItemSlot(it) is > 10 and < 14);
        var effects = CampEffects(t.Party);
        var state = CampState;
        var rest = new CampRest(mem, state, spells, random, (c, e) => effects.RemoveEffect(c, e[0], e),
            c => effects.RestoreHitPoints(c, 1, false));
        return new CampContext
        {
            Trade = t,
            State = state,
            Spells = spells,
            Rest = rest,
            Memory = mem,
            Cast = Resources.Spells is null ? null : new CampCast(new FightSpells(table, effects, random), t.Party),
            Resources = Resources,
            Save = SaveGame,
            Quit = Quit,
        };
    }

    /// <summary>
    /// The camp's Save (0x1FD3E): the wall sets into $4AFA–$4AFF (the picture flags $49FF are already the cell here),
    /// then <c>SAVGAMx.DAT</c> (interface state 2, camp) and the member files into <see cref="SaveFolder"/>. The save
    /// takes the location type from DS:49FA (<see cref="EclMemory.ViewMode"/>) and writes the game speed DS:49E2
    /// (<see cref="EclMemory.GameSpeed"/>) into $49FC, as the program does.
    /// </summary>
    public string? SaveGame(char letter)
    {
        if (SaveFolder is null || memory().Party is not { } party || Game() is null)
            return "NO SAVE FOLDER.";
        var mem = memory();
        for (int k = 0; k < 3; k++)
            if (_wallSetKnown[k])
            {
                mem.Write((ushort)(0x4AFA + k), (ushort)(_wallSets[k] is { } w ? w & 0xFFFF : 0xFFFF));
                mem.Write((ushort)(0x4AFD + k), (ushort)(k + 1));
            }
        try
        {
            var bytes = GoldBox.Data.Por.PorSaveGame.Build(mem, party.Members.Count, letter, 2);
            Directory.CreateDirectory(SaveFolder);
            File.WriteAllBytes(Path.Combine(SaveFolder, GoldBox.Data.Por.PorSaveGame.FileName(letter)), bytes);
            GoldBox.Data.Por.PorParty.Save(party, SaveFolder, letter);
            Log.Add($"saved slot {letter} to {SaveFolder}");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "SAVE FAILED: " + e.Message;
        }
    }

    /// <summary>The camp menu is up (the position line says CAMPING, DS:49F3 = 2).</summary>
    public bool Camping => Interface == Venue.Camp;
}
