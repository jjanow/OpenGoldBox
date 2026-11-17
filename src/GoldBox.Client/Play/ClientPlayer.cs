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

    /// <summary>DELAY: game speed × 225 ms. [?] The speed is read from $49FC (saved from the options), not DS:49E2.</summary>
    public void Delay()
    {
        int speed = memory().Read(0x49FC) & 0xFF;
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
    public void LoadWallSet(int slot, int? set) { }
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

    public void LoadCombatIcon(byte picture, int slot) { }

    // ---- ICombatRunner ---------------------------------------------------------------------------------------------
    /// <summary>
    /// No fight was built (game files without the fight tables): a notice, then the creatures keep their status as
    /// loaded, so the after-combat step treats the fight as won. A stand-in, not the game's outcome.
    /// </summary>
    public void Fight(EncounterState state) =>
        Acknowledge($"A FIGHT WITH {state.Monsters.Count} CREATURES. THE FIGHT IS NOT IN THIS BUILD YET: IT COUNTS AS WON.");

    /// <summary>
    /// The real fight (built by the game with every party member on Quick), run headless: there is no combat screen
    /// yet, so a notice comes first and the after-combat step shows the outcome.
    /// </summary>
    public void Fight(EncounterState state, GoldBox.Core.Game.Fight fight)
    {
        Acknowledge($"A FIGHT WITH {state.Monsters.Count} CREATURES. NO COMBAT SCREEN YET: THE PARTY FIGHTS ON QUICK.");
        fight.Run();
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
        };
    }

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
    /// The camp menu (0x196A0, <see cref="VenueScreens.Camp"/>) after vector 2. Rest is not built, so no rest is ever
    /// interrupted (vector 3 does not run).
    /// </summary>
    public bool Camp()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return false;
        var before = Interface;
        Interface = Venue.Camp;
        try
        {
            return VenueScreens.Camp(t, Resources.CampMenu);
        }
        finally
        {
            Interface = before;
        }
    }

    /// <summary>The camp menu is up (the position line says CAMPING, DS:49F3 = 2).</summary>
    public bool Camping => Interface == Venue.Camp;
}
