using System.Globalization;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

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
    public string[]? ViewOverlay
    {
        get => _viewOverlay;
        private set
        {
            _viewOverlay = value;
            if (value is null)
                ViewPicture = null;
        }
    }

    private string[]? _viewOverlay;

    /// <summary>The encounter picture, sprite or portrait the view shows (drawn by <see cref="ViewPictures"/>; null = none).</summary>
    public ViewPicture? ViewPicture { get; private set; }

    /// <summary>A one-line message (the area loop's own, or a stand-in notice) shown on the first text row.</summary>
    public string? Notice { get; set; }

    /// <summary>Host calls that have no player-facing effect yet, for the log.</summary>
    public List<string> Log { get; } = [];

    private int Choose(string? prompt, IReadOnlyList<string> items, bool vertical = false, int? cancel = null,
        bool animates = false) =>
        runner.Ask(new ChoiceRequest(prompt, items, vertical, cancel) { Animates = animates }) is int i ? i : 0;

    /// <summary>
    /// DS:84E1 (read s19): cleared when a head and body are loaded (0x79E4, 0x7B77), set by PICTURE 255 (0x1684) and by
    /// the area loop's pass (0x4223). HMENU (0x1EB2) and ENCMENU (0x2DA1) animate only while it and DS:84E0 (a close-up
    /// loaded by PICTURE or the encounter, 0x15F8 / 0x7B0B; cleared with the view's redraw) are both set.
    /// </summary>
    /// <remarks>
    /// Clear until the first step's pass after a start or load (DOSBox-X s24: a PICTURE and HMENU in the first step's
    /// vector 0 after loading a save from the main menu show the first frame, still). [?] A load in mid-session keeps
    /// the program's value; the Client starts clear each time.
    /// </remarks>
    public bool PictureArmed { get; set; }

    /// <summary>HMENU's and ENCMENU's animate flag: DS:84E0 and DS:84E1.</summary>
    private bool CloseUpAnimates => PictureArmed && ViewPicture is { Kind: ViewPictureKind.Picture };

    /// <summary>A notice the player must acknowledge (stand-ins for screens not built yet).</summary>
    private void Acknowledge(string text)
    {
        Text.Print(text, clear: true, colour: 14);
        runner.Ask(new KeyRequest());
    }

    /// <summary>The text window waits for a key when a page is full (START.EXE 0x7A12).</summary>
    public void InstallPaging() => Text.Pause = () =>
    {
        _pagePrompted = true;
        runner.Ask(new KeyRequest());
    };

    /// <summary>Something else was drawn on the bottom row since the last page prompt.</summary>
    public void ForgetPagePrompt() => _pagePrompted = false;

    /// <summary>The position line was blanked by a DAMAGE line and not drawn again yet.</summary>
    public bool PositionLineErased { get; set; }

    /// <summary>The text window's page prompt was shown (it stays on the bottom line until something else is drawn there).</summary>
    private bool _pagePrompted;

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
    public ushort HorizontalMenu(IReadOnlyList<string> choices) =>
        (ushort)Choose(null, Upper(OneWordSwap(choices)), animates: CloseUpAnimates);

    /// <summary>
    /// HMENU (GAME.OVR 0x1E17, read s21): a menu of one word that equals the code string at 0x1DCB (exact compare,
    /// 0x1E76) is shown with the string at 0x1DF0 instead (0x1E8B), when the count operand is 1. DOSBox-X (s21, the
    /// harbor master in New Phlan) shows the second. Both are read from the player's program; without them nothing is
    /// swapped.
    /// </summary>
    private IReadOnlyList<string> OneWordSwap(IReadOnlyList<string> choices) =>
        choices.Count == 1 && Resources.Texts.FromFiles(Msg.HmenuButton) is { Length: > 0 } button
            && Resources.Texts.FromFiles(Msg.HmenuEnter) is { Length: > 0 } enter
            && string.Equals(choices[0], button, StringComparison.Ordinal)
            ? [enter]
            : choices;

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
    public void Call(ushort routine)
    {
        Log.Add($"CALL {routine:X4}");
        // CALL $BA03 (OVR 0x3AC6): the sound picked by the last write to $03DE (DS:84CC): 8 → step, 10 → cue 262A.
        if (routine == 0xBA03 && memory().SpecialWriteValues.TryGetValue(0x03DE, out var which))
        {
            if (which == 8)
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Step);
            else if (which == 10)
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Fireball);
        }
    }

    /// <summary>Where the program's sound calls go (the Client's audio, or none).</summary>
    public GoldBox.Core.Sound.ISoundSink Sound { get; set; } = GoldBox.Core.Sound.NoSound.Instance;
    public void Protect(ushort address) { } // the code wheel: imported modules skip it (PLAN 6)
    public ushort Random(ushort range) => 0; // AreaHost draws from the game's generator
    public void ScriptChanging(ushort script) => Log.Add($"NEWECL {script}");

    /// <summary>PROGRAM: 0 training hall (0x19AE0), 8 end game (0x20941), 9 camp (0x3B56: the camp menu, then the run ends).</summary>
    public void Program(ushort program)
    {
        switch (program)
        {
            case 0: TrainingHall(); break;
            case 8: EndGameSequence(); break;
            case 9: Camp(); break;
        }
    }

    /// <summary>The training hall's screen while it is open (<see cref="HallScreens"/>), else null.</summary>
    public HallState? Hall { get; private set; }

    /// <summary>The end game's pictures over the view while it runs, else null.</summary>
    public EndScene? Scene { get; set; }

    /// <summary>
    /// An end-game block in the chosen art (disk, block → the FINAL picture, from the disk's file or any disk's), or
    /// null: the art setting's source for <see cref="HallScreens.AmigaEndGame"/>. Unset: DOS only.
    /// </summary>
    public Func<int, int, PorArtImage?>? EndArt { get; set; }

    /// <summary>
    /// Under Best / Amiga with the Amiga blocks, play the Amiga program's end game (on, the default) rather than DOS's
    /// with the Amiga dragon and spirit over the DOS arena pictures (the user decision of s5 names the arena pictures
    /// as DOS fill-ins; the Amiga sequence has no place for them).
    /// </summary>
    public bool AmigaEndGame { get; set; } = true;

    /// <summary>The frame delays of an end-game animation block (FINAL of the disk, else of any disk), or null.</summary>
    public Func<int, int, IReadOnlyList<uint>>? EndArtDelays => (disk, block) =>
    {
        var content = Game()?.Content;
        if (content is null)
            return [];
        foreach (int d in new[] { disk }.Concat(Enumerable.Range(1, 8)))
            if (content.Picture(EndGame.FileName(d), block) is { Count: > 0 } frames)
                return [.. frames.Select(f => f.Delay ?? 0)];
        return [];
    };

    /// <summary>
    /// The training hall (PROGRAM 0, 0x19AE0; docs/re/training.md): its menu over the party, Train Character with
    /// Core's rules, View and Save as in camp; DS:49F3 is 0 meanwhile (saved as such), then the area view returns.
    /// </summary>
    public void TrainingHall()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return;
        var game = Game();
        var camp = CampContextFor(t);
        var hall = new HallState();
        var context = new HallContext
        {
            Camp = new CampContext
            {
                Trade = camp.Trade, State = camp.State, Spells = camp.Spells, Rest = camp.Rest, Memory = camp.Memory,
                Cast = camp.Cast, Resources = camp.Resources, Save = l => SaveGame(l, 0), Quit = camp.Quit,
                EditIcon = camp.EditIcon,
            },
            State = hall,
            Text = Resources.HallTexts,
            Memory = memory(),
            Tables = Resources.Training,
            Random = n => game is null ? (ushort)0 : game.Rng.Next(n),
            Defaults = Resources.HallDefaults,
            Hooks = HallHooks,
            ProgramWord = () => ProgramWord,
        };
        Hall = hall;
        try
        {
            HallScreens.Run(context);
            // Begin returns with DS:49F3 = 4 and the redraw START 0x3379 draws every window again, which blanks the
            // text box; the cursor DS:5E24/5E25 stays (DOSBox-X s22: a PRINT after PROGRAM 0 runs on after the
            // earlier text, on the empty row).
            Text.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
            PositionLineErased = false;
        }
        finally
        {
            Hall = null;
        }
    }

    /// <summary>The word after START on the DOS command line (ParamStr(1); the hall's J), or null.</summary>
    public string? ProgramWord { get; set; }

    /// <summary>The start word as a setting the controls screen edits (remembered through <see cref="ProgramWordChanged"/>).</summary>
    public Screens.WordSetting ProgramWordSetting => new(() => ProgramWord, w =>
    {
        ProgramWord = w;
        ProgramWordChanged?.Invoke(w);
    });

    /// <summary>Called when the controls screen changes the start word (the settings file keeps it).</summary>
    public Action<string?>? ProgramWordChanged { get; set; }

    /// <summary>The hall entries other screens provide (character creation, the roster); empty ones show a notice.</summary>
    public HallHooks HallHooks { get; } = new();

    /// <summary>The end game (PROGRAM 8, 0x20941): <see cref="HallScreens.EndGameSequence"/>.</summary>
    public void EndGameSequence() => HallScreens.EndGameSequence(this, Text, runner.Ask, memory());

    // ---- IEncounterUi ---------------------------------------------------------------------------------------------
    public void ShowEncounter(byte sprite, byte picture, int distance)
    {
        // The text stays the stand-in where the art is missing (ExploreScreen draws ViewPicture when it can).
        ViewBlanked = false; // a picture after CLEARBOX shows (DOSBox-X, s19: CLEARBOX, CALL $0806, PICTURE 40)
        ViewOverlay = distance == 0
            ? ["CLOSE-UP", $"PIC {picture}", "", "(ART NOT", "FOUND)"]
            : ["MONSTER", $"SPRITE {sprite}", $"DISTANCE {distance}", "", "(ART NOT", "FOUND)"];
        // At distance 0 the close-up: PIC when $6DE1 = 255, else head $6DE1 + body (ecl-format.md "SETUPMON"; DOSBox-X
        // s19: the new game's guide is a portrait).
        ushort head = memory().Read(GoldBox.Core.Game.EncounterHost.HeadPicture);
        ViewPicture = distance == 0
            ? head != 255
                ? new ViewPicture(ViewPictureKind.Portrait, picture, head, 0, memory().Disk)
                : new ViewPicture(ViewPictureKind.Picture, picture, null, 0, memory().Disk)
            : new ViewPicture(ViewPictureKind.Sprite, sprite, null, distance, memory().Disk);
    }

    public void ShowPicture(byte picture, byte? head)
    {
        ViewBlanked = false; // a picture after CLEARBOX shows (DOSBox-X, s19: CLEARBOX, CALL $0806, PICTURE 40)
        ViewOverlay = head is { } h ? ["PICTURE", $"HEAD {h}", $"BODY {picture}"] : ["PICTURE", $"{picture}"];
        if (head is not null)
            PictureArmed = false;
        ViewPicture = head is { } hd
            ? new ViewPicture(ViewPictureKind.Portrait, picture, hd, 0, memory().Disk)
            : new ViewPicture(ViewPictureKind.Picture, picture, null, 0, memory().Disk);
    }

    /// <summary>How often CALL $2C90 redrew the view and the position line (0x3A83); the explore screen follows it.</summary>
    public int PositionLineDraws => _positionLineDraws;
    private int _positionLineDraws;

    public void ViewRedrawn()
    {
        PositionLineErased = false;
        Interlocked.Increment(ref _positionLineDraws);
    }

    public void RestoreView()
    {
        ViewOverlay = null;
        PictureArmed = true;
    }

    public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
    {
        if (text is not null)
            Text.Print(text, clear: true);
        // The program's two menu lines (GAME.OVR 0x2BE1 / 0x2BFD; v1.3 spells the fourth word differently from our old stand-in).
        var items = Resources.Texts.Words(kind == EncounterMenuKind.CombatWaitFleeAdvance
            ? Msg.EncounterMenuAdvance : Msg.EncounterMenuParley);
        return Choose(null, items, animates: CloseUpAnimates);
    }

    public void ShowEncounterMessage(EncounterMessage message)
    {
        // The program's two fixed lines (GAME.OVR 0x2C1A / 0x2C2B).
        Text.Print(Resources.Texts[message == EncounterMessage.BothSidesWait ? Msg.BothSidesWait : Msg.MonstersFlee], clear: true);
        runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(900)));
    }

    public int ChooseParleyAttitude() => Choose(null, Resources.Texts.Words(Msg.ParleyAttitudes)); // GAME.OVR 0x32EE

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
        fight.State.Sound = Sound;
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
            Window = null;
            ViewOverlay = null; // [?] as after the shop: the area display is drawn again after the loot step
        }
    }

    /// <summary>
    /// The "party destroyed" screen after a fight (0x6B37–0x6BAA, DOSBox-X s22): a full-screen window (START 03F1:02E8
    /// with 1, 1, 38, 22) with its title centred on row 1 in white, the message through the text routine in the box
    /// (2, 5)–(37, 22), colour 10, cleared first (so it starts at column 2, row 5 and wraps at column 37), then the key
    /// line of START 0x7C85 in colour 13 and a key. Then the program quits (ExploreScreen).
    /// </summary>
    public void PartyDestroyed(EncounterState state)
    {
        var t = Resources.Texts;
        var box = new TextWindow(1, 1, 38, 22);
        string title = t[Msg.EndTitle];
        box.Put(1, Math.Max(1, (1 + 38 - title.Length) / 2), title, 15);
        box.PrintIn(2, 5, 37, 22, t[Msg.MonstersRejoice], clear: true, colour: 10);
        Window = box.WindowLines();
        try
        {
            runner.Ask(new KeyRequest(t[Msg.PressAnyKey]));
        }
        finally
        {
            Window = null;
        }
    }

    // ---- ECL DAMAGE (GAME.OVR 0x34BB, the line routine 0x959F; DOSBox-X s22) ----------------------------------------

    /// <summary>
    /// One DAMAGE line (0x959F, before the hit is applied): two spaces, the name and the dies text (HP + 10 below the
    /// damage) or the hit text, the damage and the points text. When the cursor row is already below 22 it goes to
    /// row 17 and the white press-enter line waits first (the box is then cleared by the print). The column is set to
    /// 38 so the line starts on a new row; it is printed in the text box (1, 17)–(38, 22) in colour 15, so a full box
    /// shows the text routine's page prompt. The party list is drawn again after each hit (00BA:0767; the Client draws
    /// it from memory every frame).
    /// </summary>
    public void DamageReport(EclDamageReport report)
    {
        var t = Resources.Texts;
        string line = "  " + report.Target.Name + (report.Dies
            ? t[Msg.DamageDies]
            : t[Msg.DamageHitFor] + report.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + t[Msg.DamagePoints]);
        bool clear = false;
        if (Text.Row > TextWindow.Bottom)
        {
            Text.MoveTo(Text.Column, TextWindow.Top);
            clear = true;
            runner.Ask(new KeyRequest(t[Msg.DamageHitContinue]) { Colour = 15 });
        }
        Text.MoveTo(TextWindow.Right, Text.Row);
        Text.Print(line, clear, colour: 15);
        // 0x96D0: cells (17, 1)–(38, 15) are blanked before the party list is drawn again, so the position line on
        // row 15 is gone until the next redraw point (DOSBox-X s22).
        PositionLineErased = true;
    }

    /// <summary>
    /// The end of DAMAGE (0x375A–0x37E4). Party killed: a full-screen window whose title the message's clear erases at
    /// once, the message at column 1, row 1 in colour 10, Delay(3000) (a fixed 3 s; the bottom line keeps what was
    /// there, e.g. the page prompt), then the program's end. Every DAMAGE then shows the white press-enter line on the
    /// bottom row (START 0x7C85, colour 15) and waits for a key; after a wipe the program exits to DOS.
    /// </summary>
    public void DamageEnd(bool partyKilled)
    {
        var t = Resources.Texts;
        if (partyKilled)
        {
            var box = new TextWindow(1, 1, 38, 22);
            box.PrintIn(1, 1, 38, 22, t[Msg.PartyKilled], clear: true, colour: 10);
            Window = box.WindowLines();
            // Row 24 keeps what was there: the page prompt, or else the command line (DOSBox-X s22, a one-line wipe).
            (string, byte)? stale = _pagePrompted ? ((Resources.PagePrompt ?? "PRESS A KEY").ToUpperInvariant(), (byte)13) : null;
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(3000)) { BottomLine = stale });
        }
        try
        {
            runner.Ask(new KeyRequest(t[Msg.DamageContinue]) { Colour = 15 });
        }
        finally
        {
            Window = null;
            _pagePrompted = false;
        }
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

    /// <summary>A full-screen window over the screen (the fight summary, 0x5E94), or null.</summary>
    public IReadOnlyList<WindowLine>? Window { get; set; }

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
            ShowWindow = w => Window = w,
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

    /// <summary>The bump of a refused step or failed door (OVR 0x176FE): cue DS:2622.</summary>
    public void Bump() => Sound.Cue(GoldBox.Core.Sound.SoundCue.Impact);

    public void Message(AreaMessage message)
    {
        if (message == AreaMessage.NoAreaMapHere)
            Notice = "NO AREA MAP HERE.";
        // [?] The program flashes it on row 24 in colour 14 for DS:49E2 × 225 ms (START 0x7DF8); shown as a notice.
        else if (message == AreaMessage.RiverBlocks)
            Notice = Resources.Texts[Msg.RiverBlocks];
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
    /// The icon editor 0x1D1AC on a character (the party menu's, <see cref="PartyMenuFlow.EditIcon"/>), run by the camp's
    /// Alter Icon on the logic thread; null: the camp says it is missing.
    /// </summary>
    public Action<EclCharacter>? IconEditor { get; set; }

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
            EditIcon = IconEditor,
        };
    }

    /// <summary>
    /// The camp's Save (0x1FD3E): the wall sets into $4AFA–$4AFF (the picture flags $49FF are already the cell here),
    /// then the save as the program builds <c>SAVGAMx.DAT</c> (interface state 2, camp) with the party, written as an
    /// engine save <c>x.json</c> into <see cref="SaveFolder"/> (docs/module-format.md §15; <c>goldbox-import
    /// save-export</c> writes the original files from it). The save takes the location type from DS:49FA
    /// (<see cref="EclMemory.ViewMode"/>) and writes the game speed DS:49E2 (<see cref="EclMemory.GameSpeed"/>) into
    /// $49FC, as the program does.
    /// </summary>
    public string? SaveGame(char letter) => SaveGame(letter, 2);

    /// <summary>The save with the interface state DS:49F3 the program writes (2 camp, 0 the training hall).</summary>
    public string? SaveGame(char letter, byte interfaceState)
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
            var bytes = GoldBox.Data.Por.PorSaveGame.Build(mem, party.Members.Count, letter, interfaceState);
            var content = Game()!.Content;
            var save = GoldBox.Data.Module.Por.PorEngineSave.FromOriginal(bytes, party,
                GoldBox.Data.Module.Por.PorEngineSave.Identity(content), content.LoadBlock, content.VmProfile);
            Directory.CreateDirectory(SaveFolder);
            var path = Path.Combine(SaveFolder, GoldBox.Data.Module.Por.PorEngineSave.FileName(letter));
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, save);
            Log.Add($"saved slot {letter} to {path}");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "SAVE FAILED: " + e.Message;
        }
    }

    /// <summary>
    /// The party menu's Save before Begin (0x19D9C → 0x1FD3E): the unstarted game as the program saves it
    /// (<see cref="GoldBox.Data.Por.PorGame.UnstartedSave"/>, interface state 0), written as an engine save
    /// <c>x.json</c> into <see cref="SaveFolder"/>; the menu's Load takes it back into the menu. After such a Load
    /// (<paramref name="loaded"/>) the loaded game's memory is saved instead
    /// (<see cref="GoldBox.Data.Por.PorGame.SaveAtPartyMenu"/>). Returns null when written, else what went wrong.
    /// </summary>
    public string? SaveAtPartyMenu(char letter, EclParty party, GoldBox.Data.Por.IPorContent content,
        GoldBox.Data.Por.PorGame? loaded = null)
    {
        if (SaveFolder is null)
            return "NO SAVE FOLDER.";
        try
        {
            var save = loaded is not null ? loaded.SaveAtPartyMenu(party, letter)
                : GoldBox.Data.Por.PorGame.UnstartedSave(content, party, letter);
            Directory.CreateDirectory(SaveFolder);
            var path = Path.Combine(SaveFolder, GoldBox.Data.Module.Por.PorEngineSave.FileName(letter));
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, save);
            Log.Add($"saved slot {letter} (party menu) to {path}");
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
