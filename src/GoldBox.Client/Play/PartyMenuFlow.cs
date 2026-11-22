using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>The party menu's main screen: the entries shown (enabled ones, in order) under the party list.</summary>
/// <param name="Items">The entries' names (the program's DS:0619 list) and which menu item each is.</param>
public sealed record PartyMenuRequest(IReadOnlyList<(string Name, PartyMenuItem Item)> Items) : PlayerRequest
{
    /// <summary>The bottom line's prompt (colour 13).</summary>
    public string Prompt { get; init; } = "";
    /// <summary>A message on the line above the prompt (the program prints Drop's farewell on the bottom line; [?] where).</summary>
    public string? Notice { get; init; }
}

/// <summary>
/// A list in the menu window (the program's vertical menu 0x2D669: rows from row 2, the picked one white, the others
/// green) with words on the bottom line. The answer is a <see cref="ListAnswer"/>: <see cref="RowChosen"/> when a row was
/// taken (Return, a click on it), else the word's index; Cancel answers <see cref="CancelAction"/>.
/// </summary>
public sealed record PickRequest(IReadOnlyList<string> Rows, IReadOnlyList<string> Actions, int CancelAction) : PlayerRequest
{
    public const int RowChosen = -1;

    public string? Prompt { get; init; }
    /// <summary>Column of the rows (the creation lists put two spaces before each name: column 3; Add's names: column 1).</summary>
    public int Column { get; init; } = 1;

    /// <summary>
    /// The list's label row ("Pick Race" …: creation's lists start with a flagged entry, +0x29 = 1, Amiga hunk 10
    /// +0xB00–0xB14 as DOS GAME.OVR 0x19FC4), drawn at (1, 2) in the list routine's flagged-row colour (DOS 13), the
    /// rows from row 3. Shown only when the list routine's window top is 0 on entry (<see cref="MenuHighlight.ListTop"/>):
    /// from the start on the Amiga (FS-UAE s43), on DOS once a list has taken the top to 0 (DOSBox-X s44, after camp
    /// Display); otherwise null and the rows start on row 2 (DOSBox-X s44, a fresh start).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The first row's screen row.</summary>
    public int FirstRow => Title is null ? 2 : 3;

    /// <summary>
    /// The pick as the list routine's page (<see cref="GoldBox.Client.Screens.ListPage"/>: its window, Next / Prev and the
    /// shared top), drawn and answered instead of the rows above (Add's roster); its answer is that page's
    /// <see cref="ListAnswer"/>.
    /// </summary>
    public ListRequest? List { get; init; }
}

/// <summary>
/// The character page (View 0x21084: name, race, abilities, money, level, combat values, portrait) with words on the
/// bottom line after <see cref="Prompt"/>. The answer is the word's index, or a member switch
/// (<see cref="ChoiceRequest.PreviousMember"/> / <see cref="ChoiceRequest.NextMember"/>) when <see cref="PartySelect"/>.
/// </summary>
public sealed record SheetRequest(EclCharacter Character, IReadOnlyList<string> Actions) : PlayerRequest
{
    public string? Prompt { get; init; }
    public int? CancelAction { get; init; }
    public bool PartySelect { get; init; }

    /// <summary>
    /// View's menu (0x21AED's call of the menu routine 0x2CC8C at 0x21CD1, keypad flag 0): Return gives 13, which the
    /// loop takes for nothing, so a keyboard Return does nothing (the pad's A and a click take the highlighted word);
    /// the camp, shop, temple and loot open it over their own screens, and it ends when the page does.
    /// </summary>
    public bool IsView { get; init; }

    /// <summary>Creation's portrait menu (0x1CD66): the Amiga passes its own pens (<see cref="AmigaView.PortraitMenuPens"/>).</summary>
    public bool IsPortraitMenu { get; init; }
}

/// <summary>
/// A page View opens through the item list routine 0x2D669: the Items page (0x21F32) or the coin list of drop money
/// (0x234E1). <see cref="Page"/> holds its texts and rows; <see cref="Sheet"/>, when set, is the character whose View
/// page stays under the list (the coin list: the money lines are the rows). The answer is a <see cref="ListAnswer"/>:
/// the entry and the selected row, or <see cref="PageRequest.RowPicked"/> when <see cref="PickRows"/> and a row was taken.
/// </summary>
public sealed record CharacterPageRequest(CombatPage Page, IReadOnlyList<string> Entries, string? Prompt, int CancelEntry)
    : PlayerRequest
{
    public EclCharacter? Sheet { get; init; }
    /// <summary>The coin list: any key takes the row (0x234E1 takes it whatever the key), so Return picks it.</summary>
    public bool PickRows { get; init; }
}

/// <summary>The icon editor's page (0x1D1AC): the old and the new combat icon, ready and action frames.</summary>
public sealed record IconRequest(EclCharacter Character, byte[] OldRecord, IReadOnlyList<string> Actions) : PlayerRequest
{
    /// <summary>The answer for Escape in the editor's menus (the menu routine's 0).</summary>
    public const int Escape = -1;

    public string? Prompt { get; init; }
    public int? CancelAction { get; init; }
    /// <summary>The hotkey of each word (the menu routine 0x2CC8C takes a word's first capital letter or digit:
    /// "color-1" answers 1); null = first letters.</summary>
    public string? Hotkeys { get; init; }
}

/// <summary>
/// Modify Character's page (0x1BA1E): the character page with the field <see cref="Field"/> (0..5 an ability, 6 the
/// hit points) drawn in colour 13, and the words (Keep, Exit) after <see cref="Prompt"/>. The answer is a
/// <see cref="ModifyKey"/>, or an int: the field to move the cursor to (a mouse click on another field).
/// </summary>
public sealed record ModifyRequest(EclCharacter Character, int Field, IReadOnlyList<string> Actions) : PlayerRequest
{
    public string? Prompt { get; init; }
}

/// <summary>
/// The party menu (GAME.OVR 0x19AE0) and what it runs: Create (0x19EEB), Drop, View, Add, Remove, Load, Begin and Exit,
/// as game logic on the <see cref="ScriptRunner"/> thread. The rules are Core's (<see cref="PartyMenu"/>,
/// <see cref="CharacterCreation"/>); the characters go to the roster files (<see cref="PorRoster"/>) as the program's do.
/// Modify (0x1BA1E). Save writes the unstarted (or loaded) game (<see cref="SaveGame"/>), Load takes such a save back
/// into the menu. J (0x19E2C) and Train (0x1C32D) are the training hall's (<see cref="HallScreens.Password"/>,
/// <see cref="HallScreens.Train"/>) on the menu's party and generator: Train is offered once J saw the program's word
/// (the debug flag DS:470C) or when a loaded save's $6DA8 is set (DOSBox-X s24, <c>START STING</c>: J, then Train with
/// no gold or experience checks, the page, the question, "Congratulations" and the hit points raised).
/// </summary>
public sealed class PartyMenuFlow(Func<PlayerRequest, object?> ask, PartyMenuTexts texts, CreationTables tables,
    BaseItemTable items, PorRoster roster, uint seed)
{
    /// <summary>The party the entries work on: the menu's own before a game, the game's in the training hall.</summary>
    public EclParty Party { get; set; } = new();

    /// <summary>
    /// The program's shared menu highlight (DS:6B2B, <see cref="MenuHighlight"/>): the word menus on the bottom line
    /// open on it and a choice moves it; the main menu's hotkeys move it to their entry; the yes/no routine 0x2D9A0
    /// sets it to its second word (No) first (DOSBox-X s19: Keep and Is-this-icon-ok open on NO).
    /// </summary>
    public MenuHighlight Highlight { get; init; } = new();

    /// <summary>The game's generator: creation draws from it, and a new game goes on with its seed.</summary>
    public TurboPascalRandom Random { get; set; } = new(seed);

    /// <summary>A new game for the party (not run yet), when Begin is chosen.</summary>
    public Func<EclParty, uint, PorGame>? NewGame { get; init; }

    /// <summary>The save slots present (letters, in order) and a game loaded from one
    /// (<see cref="PorGame.LoadAtPartyMenu"/>: its entry run unless the save's interface state is 0). The third
    /// argument is the load's <c>started</c> hook, to call with the game before its entry runs.</summary>
    public Func<IReadOnlyList<char>>? SaveSlots { get; init; }
    public Func<char, uint, Action<PorGame>, PorGame>? LoadGame { get; init; }

    /// <summary>Exit to DOS.</summary>
    public Action? Quit { get; init; }

    /// <summary>The game Begin or Load started, set before its entry runs (the screen hands over to it).</summary>
    public PorGame? Started { get; private set; }

    /// <summary>The level stats 0x2AA87 the join runs.</summary>
    private void LevelStatsOf(EclCharacter c) => LevelStats.Apply(c, tables.Levels);

    /// <summary>
    /// The party menu's Save (0x19D9C → 0x1FD3E) before Begin: writes the game with the menu's party to slot A..J,
    /// interface state 0: the unstarted game, or the game a Load left in the menu (the third argument, else null); null
    /// when written, else what went wrong.
    /// </summary>
    public Func<char, EclParty, PorGame?, string?>? SaveGame { get; init; }

    /// <summary>The resources Train reads (the training tables, class and spell names, the hall's words); null: no Train.</summary>
    public ClientResources? Resources { get; init; }

    /// <summary>The word after START on the DOS command line (ParamStr(1)), or null: J compares it.</summary>
    public Func<string?> ProgramWord { get; init; } = () => null;

    /// <summary>The Amiga song hook, when the module and presentation setting provide it.</summary>
    public Action<int>? PlaySong { get; init; }

    /// <summary>Where the original stops on a run-time error (Train's payment; not reached with the debug flag).</summary>
    public Action<ProgramHaltException>? Halt { get; init; }

    /// <summary>J's message and Train's page, drawn by the screen while they are up.</summary>
    public HallState Hall { get; } = new();

    /// <summary>DS:470C, the debug flag J sets (cleared when the menu routine is entered, 0x19AF4).</summary>
    public bool DebugFlag { get; private set; }

    /// <summary>An answer to the menu: the hidden J (0x19E2C; the screen gives it for the action <c>HallJ</c>).</summary>
    public const char PasswordAnswer = HallScreens.PasswordKey;

    private string? _notice;
    private bool[]? _enabled;
    /// <summary>DS:557D: a save was written since the last menu key other than E and S (set at 0x2028F).</summary>
    private bool _saved;
    /// <summary>A save loaded with interface state 0: the menu goes on with its party; Begin runs its memory.</summary>
    private PorGame? _loaded;

    /// <summary>The game's memory when the menu runs in the training hall (PROGRAM 0); null at start-up.</summary>
    public EclMemory? GameMemory { get; set; }

    /// <summary>
    /// Whether the character page (View 0x21084, also Modify's) draws the portrait window: only with the portraits flag
    /// DS:49F5 set ($49FF bit 0) and the disk byte DS:5376 = 3 (0x21530–0x2153E). At start-up both hold (0xE965,
    /// 0x19AE7); a Load leaves the save's disk byte and flags (DOSBox-X s25: a state-0 save of disk 2, or with portraits
    /// off, shows Modify's page with no portrait and no box); the hall sets the disk to 3.
    /// </summary>
    public bool PortraitShown => (GameMemory ?? _loaded?.Memory) is not { } m || (CampSettings.Portraits(m) && (m.Disk == 3 || AmigaView.On));

    /// <summary>A save the hall's Load could not read (not in the original: its saves always fit).</summary>
    public void CouldNotLoad(char slot) => _notice = texts.CouldNotLoad(slot);

    /// <summary>The message an entry left for the bottom line (Drop's farewell, no saves), taken once.</summary>
    public string? TakeNotice()
    {
        var n = _notice;
        _notice = null;
        return n;
    }

    /// <summary>Runs the menu until a game starts (Begin, Load) or Exit is confirmed.</summary>
    public void Run()
    {
        DebugFlag = false;
        while (Started is null)
        {
            _enabled = PartyMenu.Enabled(Party.Current is not null, trainOn: DebugFlag || HallMask > 0, _enabled);
            var shown = new List<(string, PartyMenuItem)>();
            for (int i = 0; i < PartyMenu.Count; i++)
                if (_enabled[i])
                    shown.Add((texts.MenuItem(i), (PartyMenuItem)i));
            var answer = ask(new PartyMenuRequest(shown) { Prompt = texts.ChooseFunction, Notice = _notice });
            _notice = null;
            if (answer is PasswordAnswer)
            {
                // Not an entry: no prompt, no echo; the word's message when it matches; the menu drawn again. A key
                // other than E and S, so DS:557D is cleared first (0x19CB3).
                _saved = false;
                if (Trainer() is { } h && HallScreens.Password(h))
                    DebugFlag = true;
                continue;
            }
            if (answer is not int pick)
                continue;
            if (pick == ChoiceRequest.PreviousMember || pick == ChoiceRequest.NextMember)
            {
                PartyMenu.Select(Party, pick == ChoiceRequest.NextMember ? 1 : -1);
                continue;
            }
            if (pick < 0 || pick >= shown.Count)
                continue;
            // 0x19CB3: any key but E and S clears DS:557D (Home / End, extended, branch off before).
            if (shown[pick].Item2 is not (PartyMenuItem.Save or PartyMenuItem.Exit))
                _saved = false;
            Highlight.Chosen((int)shown[pick].Item2);
            switch (shown[pick].Item2)
            {
                case PartyMenuItem.Create:
                    Create();
                    break;
                case PartyMenuItem.Drop:
                    Drop();
                    break;
                case PartyMenuItem.View:
                    View();
                    break;
                case PartyMenuItem.Modify:
                    Modify();
                    // The refusal goes through the message routine START 0x7DF8: the bottom line in yellow for game
                    // speed × 225 ms, then the menu's prompt again without a key (DOSBox-X s25).
                    if (_notice is { } refusal && Trainer() is { } h)
                    {
                        _notice = null;
                        HallScreens.Message(h, refusal, 14);
                    }
                    break;
                case PartyMenuItem.Train:
                    if (Trainer() is { } trainer)
                        HallScreens.Train(trainer, DebugFlag);
                    break;
                case PartyMenuItem.Add:
                    Add();
                    break;
                case PartyMenuItem.Remove:
                    Remove();
                    break;
                case PartyMenuItem.Load:
                    Load();
                    break;
                case PartyMenuItem.Begin:
                    if (Party.Members.Count > 0 && (_loaded ?? NewGame?.Invoke(Party, Random.Seed)) is { } g)
                    {
                        // A save loaded here with interface state 0 goes on from its memory, with the menu's party and
                        // generator (one generator in the program).
                        if (_loaded is not null)
                            g.Rng.Seed = Random.Seed;
                        Started = g;
                        g.RunNewGameEntry(Party);
                    }
                    break;
                case PartyMenuItem.Save:
                    if (Party.Members.Count > 0)
                        _saved = SaveMenu();
                    break;
                case PartyMenuItem.Exit:
                    if (YesNo(AmigaView.HallQuit(texts.QuitToDos))) // the Amiga names the Workbench (s48 fix)
                    {
                        // 0x19E0E: with a party, the save (slot menu, Escape asks again) until one is written, unless
                        // one was since the last other key (DS:557D); then the quit 0899:0000.
                        while (!_saved && Party.Members.Count > 0 && SaveGame is not null)
                            _saved = SaveMenu();
                        Quit?.Invoke();
                        return;
                    }
                    break;
                default:
                    _notice = texts.NotBuilt(shown[pick].Item1);
                    break;
            }
        }
    }

    /// <summary>VM $6DA8 as the menu reads it: a save loaded here (interface state 0) brings its memory; else 0.</summary>
    private ushort HallMask => (GameMemory ?? _loaded?.Memory)?.Read(TrainingHall.HallMaskCell) ?? 0;

    /// <summary>J and Train on the menu's party, generator and (after a load here) the save's speed and hall mask.</summary>
    private HallTrainer? Trainer() => Resources is not { } r ? null : new HallTrainer
    {
        Ask = ask,
        State = Hall,
        Text = r.HallTexts,
        Resources = r,
        Party = Party,
        Tables = r.Training,
        HallMask = HallMask,
        GameSpeed = (GameMemory ?? _loaded?.Memory)?.GameSpeed ?? 1,
        Random = n => Random.Next(n),
        Halt = Halt,
        ProgramWord = ProgramWord,
        YesNo = q => YesNo(q, 13), // "Do you wish to train?" (0x1C80E: 13)
    };

    /// <summary>The yes/no 0x2D9A0; every party-menu question passes prompt colour 14 (the quit 0x19DF7, Drop's two
    /// 0x1B8F7 / 0x1B91C; the Amiga pen 24, hunk 10 +0xA38, +0x23D6, +0x23F2), the training question 13.</summary>
    private bool YesNo(string prompt, byte promptColour = 14)
    {
        Highlight.Index = 1;
        return ask(new ChoiceRequest(null, texts.YesNo, false, 1) { MenuPrompt = prompt, MenuPromptColour = AmigaView.YesNoPrompt(promptColour) }) is 0;
    }

    // ---- Create ---------------------------------------------------------------------------------------------------

    /// <summary>A vertical pick (0x2D669 with Exit): the list position, or null on Exit.</summary>
    private int? Pick(IReadOnlyList<string> rows, string titleKey)
    {
        // The list's entry 0 is the label, the rows 1..n, the position 1 (0x1A09B): the label shows when the list
        // routine's global top is 0 (MenuHighlight.ListTop), on DOS and the Amiga alike.
        int top = Highlight.ListEntered(1, rows.Count + 1);
        var a = ask(new PickRequest(rows, [texts.Exit], 0) { Column = 3, Title = top == 0 ? texts.Text(titleKey) : null }) as ListAnswer;
        return a is { Action: PickRequest.RowChosen } ? a.Row : null;
    }

    /// <summary>
    /// Create (0x19EEB): race, gender, class, alignment (Exit on any list leaves without a character), the rolls until
    /// the player keeps one, the name, the portrait and the icon, then the character is written to the roster.
    /// </summary>
    public void Create()
    {
        var cc = new CharacterCreation(tables, Random.Next);
        if (Pick([.. CharacterCreation.RaceCodes.Select(texts.Race)], "PickRace") is not { } race)
            return;
        cc.PickRace(CharacterCreation.RaceCodes[race]);
        if (Pick([texts.Gender(0), texts.Gender(1)], "PickGender") is not { } gender)
            return;
        cc.PickGender(gender);
        var classes = cc.ClassChoices();
        if (Pick([.. classes.Select(texts.ClassName)], "PickClass") is not { } cls)
            return;
        cc.PickClass(classes[cls]);
        var aligns = cc.AlignmentChoices();
        if (Pick([.. aligns.Select(texts.Alignment)], "PickAlignment") is not { } al)
            return;
        cc.PickAlignment(aligns[al]);
        var c = cc.Character;
        do
        {
            cc.Roll(items);
            Highlight.Index = 1;
        }
        while (ask(new SheetRequest(c, texts.YesNo) { Prompt = texts.KeepCharacter }) is not 0);

        // The Amiga asks through its requester with MaxChars 15 (hunk 10 +0x2114: 14 characters, DOS 15), the letters
        // as its keymap types them (TextRequest.KeepsCase), and keeps the name as typed (strncpy, hunk 10 +0x212E) until
        // the character save at the end; DOS upper-cases it at once. AmigaView.CreationName.
        string name, typed;
        do
        {
            typed = ask(new TextRequest(texts.CharacterName, false, AmigaView.NameLength(AmigaView.On)) { KeepsCase = AmigaView.On }) as string ?? "";
            name = typed.Trim();
        }
        while (name.Length == 0);
        cc.SetName(AmigaView.On ? name : AmigaView.CreationName(name, false));
        // The Amiga then compares the name with its developer names (hunk 10 +0x2148–0x2330): each match's lines on
        // row 24, each held Delay(100), the row blanked after the block (AmigaView.DeveloperBlocks). DOS has none.
        if (AmigaView.CallPen(GoldBox.Data.Module.Por.PorAmigaText.DeveloperPen) is int devPen)
            foreach (var block in AmigaView.DeveloperBlocks(typed, AmigaView.On, AmigaView.Strings))
            {
                for (int j = 0; j < block.Count; j++)
                {
                    DeveloperRow = (block.GetRange(0, j + 1), devPen);
                    ask(new DelayRequest(AmigaView.DeveloperHold));
                }
                DeveloperRow = null;
            }
        PlaySong?.Invoke(3);
        Portrait(c);
        EditIcon(c);
        // The Amiga's character save (hunk 10 +0x2380 → hunk 16 +0x49C) writes the name back upper case first.
        if (AmigaView.On)
            c.Name = AmigaView.CreationName(name, true);
        roster.Save(c, Party);
    }

    /// <summary>The portrait pick (0x1CD66): Head and Body step through 1..14 and 1..12; Keep is the only way out.</summary>
    private void Portrait(EclCharacter c)
    {
        while (true)
        {
            var a = ask(new SheetRequest(c, texts.PortraitMenu) { IsPortraitMenu = true });
            switch (a)
            {
                case 0:
                    c[CharacterCreation.PortraitHead] = (byte)(c[CharacterCreation.PortraitHead] < 14 ? c[CharacterCreation.PortraitHead] + 1 : 1);
                    break;
                case 1:
                    c[CharacterCreation.PortraitBody] = (byte)(c[CharacterCreation.PortraitBody] < 12 ? c[CharacterCreation.PortraitBody] + 1 : 1);
                    break;
                case 2:
                    return;
            }
        }
    }

    /// <summary>
    /// The icon editor (0x1D1AC) with the program's menus (Core's <see cref="IconEditor"/>): Parts (head 0..13, weapon
    /// 0..31: Next, Prev, Keep, Exit), color-1 / color-2 (the low / high nibble of a part's colour byte 0xC1..0xC6), Size,
    /// Exit; then the IconOk question (PartyMsg.IconOk). No starts again from the icon as it is (0x1D1DE), which becomes
    /// the old icon. The new icon is drawn from the record, so it shows each step at once. Create's last step and the
    /// camp's Alter Icon (0x1951F, <see cref="CampContext.EditIcon"/>) run the same routine; it asks only
    /// <see cref="IconRequest"/>s.
    /// </summary>
    public void EditIcon(EclCharacter c)
    {
        var ed = new IconEditor(c);
        var old = (byte[])c.Record.Clone();
        while (true)
        {
            var (items, keys) = texts.IconMenu(ed.State, ed.HighNibble, ed.OffersSmall);
            if (ask(new IconRequest(c, old, items) { Hotkeys = keys, CancelAction = IconRequest.Escape }) is not int i)
                continue;
            if (!ed.Key(i >= 0 && i < keys.Length ? keys[i] : '\0'))
                continue;
            Highlight.Index = 1;
            if (ask(new IconRequest(c, old, texts.YesNo) { Prompt = texts.IconOk }) is 0)
            {
                PlaySong?.Invoke(3);
                return;
            }
            ed.Restart();
            old = (byte[])c.Record.Clone();
        }
    }

    // ---- Modify -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Modify Character (0x1BA1E) on the current character (<see cref="CharacterModify"/>): refused with the name and
    /// <c>PartyMsg.CantModify</c> when it has experience; else the page with the field cursor, Up / Down move it, Left /
    /// Right step the value, Keep keeps, Exit (or Escape) puts the old values back. Nothing is written to the roster:
    /// the party's record changes.
    /// </summary>
    public void Modify()
    {
        if (Party.Current is not { } c)
            return;
        if (!CharacterModify.CanModify(c))
        {
            _notice = texts.CantModify(c.Name);
            return;
        }
        var m = new CharacterModify(tables, items, c);
        while (true)
        {
            var a = ask(new ModifyRequest(c, m.Field, texts.KeepExit) { Prompt = texts.ModifyPrompt });
            if (a is int target and >= 0 and < CharacterModify.FieldCount)
            {
                // A click on another field: the cursor steps down to it, as Down would.
                while (m.Field != target)
                    m.Press(ModifyKey.Down);
                continue;
            }
            if (m.Press(a is ModifyKey k ? k : ModifyKey.Other))
                return;
        }
    }

    // ---- the others -------------------------------------------------------------------------------------------------

    /// <summary>
    /// View (V at 0x19D13, shown with a current character: 0x19D23 calls 0x21AED, the routine the camp's V calls at
    /// 0x197B3): the character page (0x21084, with the portrait rule of <see cref="PortraitShown"/>) and the View menu
    /// after its prompt: Items, Spells, Trade, Drop as the character has them (<see cref="FightItemScreens.ViewEntries"/>;
    /// DS:49F3 is 0 here) and Exit. Home / End do nothing (the loop has no member selection); Exit or Escape go back to
    /// the party menu, where Home / End pick the member. The entries run
    /// <see cref="CharacterView"/>'s screens, the camp's.
    /// </summary>
    public void View()
    {
        // The camp's screens on the menu's party. The party menu draws no text window: our notices (commands not built
        // yet) go on its bottom line. No pool or treasure list here.
        var memory = GameMemory ?? _loaded?.Memory;
        // The Items page's box (1, 21)–(38, 22) is the program's print (Trade's scribe warning, START 0x77F8 / Amiga
        // hunk 10 +0x5616; Drop's, Sell's and Id's where built): the Amiga's waits speed × 2 ticks after each word (hunk
        // 25 +0x1286, read s47: no typing flag), DOS's types per character only while DS:4A00 is set (only PRINT sets
        // it). Its text is drawn over the page (TextOverPage) while the command talks.
        var box = new TextWindow
        {
            WordTyped = () =>
            {
                if (memory?.GameSpeed is byte s and > 0)
                    ask(new DelayRequest(TextWindow.AmigaWordDelay(s)) { Chained = true });
            },
            Typed = () =>
            {
                if (memory is { TypesText: true, GameSpeed: byte s and > 0 })
                    ask(new DelayRequest(TimeSpan.FromMilliseconds(10 * s)) { Chained = true });
            },
        };
        var t = new TradeContext
        {
            Dialog = new Dialog(ask, box),
            TalkOverItemsPage = on => TextOverPage = on ? box : null,
            Text = Resources?.Texts ?? new GameText(),
            Party = Party,
            Pool = new uint[7],
            Treasure = [],
            Random = n => Random.Next((ushort)n),
            Word =Resources?.ItemWord ?? (_ => null),
            IsScroll = Resources?.IsScroll ?? (_ => false),
            ItemTypes = items,
            Halt = Halt,
            MessagesOnBottomLine = true,
            ItemsPage = (c, entries, row, name) => ItemsPage(c, entries, row, name),
            Coins = Coins,
            Memory = memory,
            PickMenu = Resources?.TargetMenu,
            // Trade's START 0x3379 with DS:49F3 = 0 (case 0: everything inside the outer frame blanked); the member pick
            // then draws the party list in the party menu's layout with its pick lit (DOSBox-X s42, the hall of slot C).
            InterfaceRedraw = () => PickScreen = true,
            RosterDrawnLit = who => PickLit = who,
        };
        try
        {
            CharacterView.Run(t, Venue.PartyMenu, ViewPage);
        }
        finally
        {
            PickScreen = false;
            TextOverPage = null;
        }
    }

    private CombatText CombatTexts => Resources?.CombatTexts ?? new CombatText();

    private string ItemName(byte[] item) =>
        AmigaView.Case(ItemNames.Compose(item, Resources?.ItemWord ?? (_ => null), Party.Members.Any(m => m.HasEffect(5))).Trim());

    /// <summary>
    /// View's Items (0x21F32): its page (<see cref="ItemsPages"/>, black outside a fight) with the list routine 0x2D669,
    /// which puts the shared cell on the first entry on entry (0x2D6B9). The entry picked and the row it applies to.
    /// </summary>
    public ListAnswer ItemsPage(EclCharacter c, IReadOnlyList<ItemsEntry> entries, int row, Func<byte[], string>? itemName = null)
    {
        var text = CombatTexts;
        var page = ItemsPages.Build(text, c, c.Items, itemName ?? ItemName, row, grey: false);
        Highlight.ItemList();
        // 0x2D766 with the position word (0 on entry, 0x21F5F; then the row, kept through 0x21F32's loop): the list
        // routine's global top goes to the row at most, so after View's Items a Create shows its list labels.
        Highlight.ListEntered(page.Selected, page.Rows.Count);
        int exit = entries.Count - 1;
        return ask(new CharacterPageRequest(page, [.. entries.Select(e => ItemsPages.Word(text, e))], null, exit))
            as ListAnswer ?? new ListAnswer(exit, row);
    }

    /// <summary>
    /// START 0x3379 drew case 0 for Trade's member pick (the party menu and the hall, DS:49F3 = 0): the screen inside
    /// the outer frame blank but for the party list (the party menu's layout) with <see cref="PickLit"/> lit, until the
    /// next page. Set by the item and money Trade (<see cref="TradeContext.InterfaceRedraw"/>).
    /// </summary>
    public bool PickScreen { get; set; }

    /// <summary>The member the pick lights (START 00BA:2CA1), or null for nobody.</summary>
    public EclCharacter? PickLit { get; set; }

    /// <summary>
    /// The venue's text window while an Items command (Sell, Id) talks over the Items page: its box (1, 21)–(38, 22) is
    /// drawn over the page, and the overlay stays up for the command's yes/no and row-24 messages. Null otherwise.
    /// </summary>
    public TextWindow? TextOverPage { get; set; }

    /// <summary>Creation's developer lines drawn on row 24 so far in their block, in order, and the program's pen
    /// (<see cref="AmigaView.DeveloperBlocks"/>), during their pauses; null otherwise.</summary>
    public (List<string> Lines, int Pen)? DeveloperRow { get; private set; }

    /// <summary>
    /// Drop money's coin pick and amount (0x234E1): the coin list over View's page (the money lines are its rows, the
    /// coin-type prompt in colour 13, then Exit; 0x2D669 stores 1 in the cell), then the amount (input 0x24F1D) on row
    /// 24 (0x24F1D: digits only, clamped to the count held as they are typed, Backspace, Return; Escape answers 0).
    /// Null when the player leaves the list. <paramref name="words"/>: the money Trade's texts (0x23190), null Drop's.
    /// </summary>
    public (int Kind, ushort Amount)? Coins(EclCharacter c, IReadOnlyList<int> kinds, CoinTexts? words = null)
    {
        var text = CombatTexts;
        var r = Resources ?? new ClientResources();
        var order = CombatSheet.MoneyKinds(c).Where(kinds.Contains).ToList();
        int width = order.Count == 0 ? 0 : order.Max(k => CombatSheet.CoinName(r, k).Length);
        var page = new CombatPage
        {
            FrameRows = [0, 23],
            Rows = [.. order.Select(k => CombatSheet.CoinName(r, k).PadLeft(width) + " " +
                                         FightItemScreens.Coins(c, k).ToString(System.Globalization.CultureInfo.InvariantCulture))],
            RowCol = CombatSheet.MoneyNameEnd + 1 - width,
            FirstRow = CombatSheet.MoneyFirstRow,
            LastRow = Math.Max(CombatSheet.MoneyFirstRow, CombatSheet.MoneyFirstRow + order.Count - 1),
        };
        Highlight.ItemList();
        Highlight.ListEntered(0, order.Count); // the position word 0 before the call (Drop 0x2365B, the money Trade 0x2335A)
        var a = ask(new CharacterPageRequest(page, [text[CombatMsg.ViewExit].Trim()], (words?.Title ?? text[CombatMsg.SelectCoin]).Trim() + " ", 0)
        {
            Sheet = c,
            PickRows = true,
        }) as ListAnswer;
        if (a is null || a.Action != PageRequest.RowPicked || a.Row < 0 || a.Row >= order.Count)
            return null;
        int kind = order[a.Row];
        string prompt = AmigaView.AmountPrompt(GameText.Cat(GameText.Cat(words?.HowMuch ?? text[CombatMsg.HowMuch], CombatSheet.CoinName(r, kind)),
            words?.WillYou ?? text[CombatMsg.WillYouDrop]));
        // The input 0x24F1D: digits up to the count held (a larger number becomes the count as it is typed), Escape 0.
        var typed = AmigaView.Number(ask, new TextRequest(prompt, true, 5) { Maximum = FightItemScreens.Coins(c, kind) });
        ushort amount = ushort.TryParse(typed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : (ushort)0;
        return (kind, amount);
    }

    /// <summary>
    /// View's page (0x21084) and menu (prompt 0x21AC7 "View:", the words 0x21AA6–0x21AC2) on <paramref name="c"/>: the
    /// entry picked, or the last (Exit) on Escape. The camp (0x197B3), shop (0x730D), temple (0x5341) and loot menu
    /// (0x65B4) call the same 0x21AED (stub 0085:0039), so their View asks this page too
    /// (<see cref="TradeContext.ViewPage"/>).
    /// </summary>
    public int ViewPage(EclCharacter c, IReadOnlyList<ViewEntry> entries) =>
        ask(new SheetRequest(c, [.. entries.Select(texts.ViewWord)])
        {
            Prompt = texts.ViewPrompt,
            // 0x21AED's loop takes I, S, T, D and Exit only: no member selection (no 0x23819 call) in View.
            PartySelect = false,
            CancelAction = entries.Count - 1,
            IsView = true,
        }) is int a ? a : entries.Count - 1;

    /// <summary>Remove: SaveChar (0x1E684) of the current character, then 0x1CC10.</summary>
    public void Remove()
    {
        if (Party.Current is not { } cur)
            return;
        AmigaView.SaveNames([cur]); // the Amiga's Remove saves through hunk 16 +0x49C (hunk 10 +0x9AC)
        roster.Save(cur, Party);
        PartyMenu.RemoveCurrent(Party);
    }

    /// <summary>Drop (0x1B8B6): asked twice, then the character leaves for good (its file stays, off the list).</summary>
    public void Drop()
    {
        if (Party.Current is not { } c)
            return;
        if (YesNo(texts.DropForever(c.Name)) && YesNo(texts.AreYouSure))
        {
            _notice = c[PartyMenu.InPlay] == 0 ? texts.DumpOutBack(c.Name) : texts.BidsFarewell(c.Name);
            PartyMenu.RemoveCurrent(Party);
        }
        else
            _notice = texts.SighOfRelief(c.Name);
    }

    /// <summary>
    /// Add (0x1C9EC): the roster list; a picked name is marked and joins when the party rules allow. P + D s46: the
    /// inside of (1, 1)–(38, 22) cleared (START 03F1:0137, the menu's frame stays), then the list routine 0x2D669 with the
    /// window (1, 2)–(38, 22) (21 rows, paged by Next / Prev through the shared top), the prompt "Add a character: " and
    /// the word "Add Exit" with no Exit flag, so DOS shows "Add Exit Next"; the Amiga passes the word "Add" with the
    /// flag (hunk 10 +0x34BE–0x34F8: "Add Next Exit"). The position word is 0 once (0x1CA19) and then as the routine
    /// leaves it (the loop goes back to 0x1CA2E). Return or A adds, Escape / E end, any other key shows the list again.
    /// </summary>
    public void Add()
    {
        var entries = roster.Entries(Party);
        if (entries.Count == 0)
            return;
        int row = 0;
        var words = CombatTexts.Words(CombatMsg.AimNextPrevManual);
        var paging = new ListPaging(words.ElementAtOrDefault(0) ?? "NEXT", words.ElementAtOrDefault(1) ?? "PREV", AddRows,
            AmigaView.On ? 1 : texts.AddExit.Count);
        while (true)
        {
            var list = new ListRequest([], [.. entries.Select(AmigaView.Case)], texts.AddExit, texts.AddExit.Count - 1,
                FirstRow: AddFirstRow)
            {
                Selected = row,
                RowActions = new HashSet<int> { 0 },
                Paging = paging,
                Spell = new SpellPageLayout("", 15, "") { CentredTitle = true, Prompt = AmigaView.Case(texts.AddPrompt) },
            };
            Highlight.ListEntered(row, entries.Count); // the entry (0x2D766) as the page's own sync does it (ListPage.Shared)
            var a = ask(new PickRequest(entries, texts.AddExit, 1) { Prompt = texts.AddPrompt, List = list }) as ListAnswer;
            if (a is null || a.Action is not (PickRequest.RowChosen or 0))
                break;
            row = a.Row;
            int pcs = 0;
            if (row >= 0 && row < entries.Count && !entries[row].StartsWith('*') && roster.Load(entries[row]) is { } c)
            {
                entries[row] = PorRoster.Mark + entries[row];
                pcs = PartyMenu.TryAdd(Party, c, LevelStatsOf).PlayerCharacters;
            }
            else
                pcs = Party.Members.Count(PartyMenu.IsPlayerCharacter);
            if (PartyMenu.AddFull(Party, pcs))
                break;
        }
        roster.WriteEntries(entries);
    }

    /// <summary>Add's list window (1, 2)–(38, 22): its first row and its rows.</summary>
    public const int AddFirstRow = 2, AddRows = 21;

    /// <summary>Load (0x1F5E8): a slot of the saves present; the game goes on from it.</summary>
    private void Load()
    {
        if (PickSlot() is not { } slot || LoadGame is null)
            return;
        PorGame g;
        try
        {
            // A game that goes on (interface state not 0) is started before its vector 4 runs, as Begin's is: the
            // entry may open the game's screens (the training hall), which the screen shows once it has handed over.
            g = LoadGame(slot, Random.Seed, s =>
            {
                if (s.LoadedInterfaceState != 0)
                    Started = s;
            });
        }
        catch (Exception e) when (e is FormatException or IOException or System.Text.Json.JsonException)
        {
            Started = null;
            // Not in the original (its saves always fit): a save this module refuses (another module or version).
            Console.Error.WriteLine($"load {slot}: {e.Message}");
            _notice = texts.CouldNotLoad(slot);
            return;
        }
        if (g.LoadedInterfaceState != 0)
        {
            Started = g;
            return;
        }
        // 0x19D8A: a save with interface state 0 (written here or in the training hall) leaves the menu up with its
        // party, the first member current (0x1FB99).
        _loaded = g;
        Party.Members.Clear();
        if (g.Memory.Party is { } p)
            Party.Members.AddRange(p.Members);
        Party.CurrentIndex = Party.Members.Count > 0 ? 0 : -1;
    }

    /// <summary>Load's slot menu: the letters A–J of the saves present and no Exit word (0x1F5FF–0x1F6C3 builds the list
    /// from the letters only; DOSBox-X s21); Escape gives up. The slot, or null (none, Escape).</summary>
    public char? PickSlot()
    {
        var slots = SaveSlots?.Invoke() ?? [];
        if (slots.Count == 0 || LoadGame is null)
        {
            _notice = texts.NoSaves;
            return null;
        }
        var words = slots.Select(s => s.ToString()).ToList();
        if (ask(new ChoiceRequest(null, words, false, words.Count) { MenuPrompt = texts.LoadWhich }) is not int k
            || k < 0 || k >= slots.Count)
            return null;
        return slots[k];
    }

    /// <summary>
    /// The save 0x1FD3E from the menu: the slot menu (the camp's words, <see cref="PartyMenuTexts.SaveSlots"/>; Escape
    /// gives up), then <see cref="SaveGame"/>. Returns whether a save was written (DS:557D := 1 at 0x2028F).
    /// </summary>
    private bool SaveMenu()
    {
        if (SaveGame is null)
        {
            _notice = texts.NotBuilt(texts.MenuItem((int)PartyMenuItem.Save));
            return false;
        }
        var (items, keys) = texts.SaveSlots;
        if (ask(new ChoiceRequest(null, items, false, -1) { MenuPrompt = texts.SaveWhich, Hotkeys = keys }) is not int i
            || i < 0 || i >= items.Count || keys[i] is < 'A' or > 'J')
            return false;
        if (SaveGame(keys[i], Party, _loaded) is { } problem)
        {
            _notice = problem;
            return false;
        }
        return true;
    }
}

/// <summary>
/// The party menu's words: the program's own (DS tables and GAME.OVR strings, <see cref="PorTextCatalog"/> keys
/// <c>PartyMsg.*</c>), or short wordings of ours where the program or module does not hold them (an older module).
/// </summary>
public sealed class PartyMenuTexts(PorProgramData? program, ClientResources resources)
{
    private string? T(string key) => program?.Text("PartyMsg." + key);

    /// <summary>A PartyMsg text in the program's case, or null.</summary>
    public string? Text(string key) => T(key);

    /// <summary>DS:0619 + 42·i: the menu entries (v1.3).</summary>
    public string MenuItem(int i) =>
        AmigaView.On && AmigaView.Strings.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.PartyMenuKey + i, out var amiga) ? amiga
        : program?.PascalString(0x619 + 42 * i, 40) is { Length: > 0 } s ? s : ((PartyMenuItem)i).ToString();

    public string ChooseFunction => T("ChooseFunction") ?? "PARTY: ";
    public string QuitToDos => T("QuitToDos") ?? "QUIT? ";
    public string KeepCharacter => T("KeepCharacter") ?? "KEEP? ";
    public string CharacterName => T("CharacterName") ?? "NAME: ";
    public string AreYouSure => T("AreYouSure") ?? "SURE? ";
    public string AddPrompt => T("AddPrompt") ?? "ADD: ";
    public string LoadWhich => T("LoadWhich") ?? "LOAD: ";
    public string IconOk => T("IconOk") ?? "ICON OK? ";
    public string ModifyPrompt => T("ModifyPrompt") ?? "MODIFY: ";
    /// <summary>The save's slot menu (0x1FCAB, 0x1FCBD), the camp's words.</summary>
    public string SaveWhich => resources.Texts[Msg.SaveWhich].TrimEnd();
    public (List<string> Items, string Hotkeys) SaveSlots => CampScreens.ParseMenu(resources.Texts.Original(Msg.SaveSlots));

    public IReadOnlyList<string> AddExit => Words(T("AddExit"), ["ADD", "EXIT"]);
    public IReadOnlyList<string> KeepExit => Words(T("KeepExit"), ["KEEP", "EXIT"]);
    public IReadOnlyList<string> PortraitMenu => Words(T("PortraitMenu"), ["HEAD", "BODY", "KEEP"]);
    public IReadOnlyList<string> YesNo => Words(program?.Text("CombatMsg.YesNo"), ["YES", "NO"]);
    /// <summary>
    /// The icon editor's menu line (<see cref="IconEditor.MenuLine"/>, DS:07BF + 41·state) as the routine builds it: the
    /// colour line's third word (characters 13..16) replaced by the hair word (color-1) or the face word (color-2), the
    /// size line after the other size's word; split into words and hotkeys as the menu routine reads them. Without the
    /// program, short wordings of ours with the same hotkeys.
    /// </summary>
    public (List<string> Items, string Hotkeys) IconMenu(IconEditor.Menu m, bool highNibble, bool offersSmall)
    {
        string line = program?.PascalString(IconEditor.MenuLine(m), 40) is { Length: > 0 } s ? s : m switch
        {
            IconEditor.Menu.Main => "Parts tint-1 tint-2 Size Exit",
            IconEditor.Menu.Parts => "Head Weapon Exit",
            IconEditor.Menu.Colours => "Weapon Body .... Shield Arm Leg Exit",
            IconEditor.Menu.Size => " Keep Exit",
            _ => "Next Prev Keep Exit",
        };
        if (m == IconEditor.Menu.Colours && line.Length >= IconEditor.ColourWordAt - 1 + IconEditor.ColourWordLength)
        {
            string word = highNibble ? T("IconFace") ?? "Face" : T("IconHair") ?? "Hair";
            line = line.Remove(IconEditor.ColourWordAt - 1, IconEditor.ColourWordLength).Insert(IconEditor.ColourWordAt - 1, word);
        }
        if (m == IconEditor.Menu.Size)
            line = (offersSmall ? T("IconSmall") ?? "Small" : T("IconLarge") ?? "Large") + line;
        return CampScreens.ParseMenu(line);
    }
    /// <summary>The lists' Exit word: the first word of the menu's Exit entry.</summary>
    public string Exit => AmigaView.Case(MenuItem((int)PartyMenuItem.Exit).Split(' ')[0]);

    /// <summary>View's prompt (0x21AC7, "View:": the words follow at once, as in the fight's View).</summary>
    public string ViewPrompt => AmigaView.Case(program?.Text("CombatMsg.ViewPrompt") ?? "VIEW:");

    /// <summary>View's words (0x21AA6–0x21AC2, the program's, each with its trailing space trimmed).</summary>
    public string ViewWord(ViewEntry e) =>
        AmigaView.Case((e == ViewEntry.Rename ? AmigaView.RenameWord : null) ?? (program?.Text("CombatMsg.View" + e) ?? e.ToString()).Trim());

    public string DropForever(string name) => (T("Drop") ?? "DROP ") + name + (T("Forever") ?? "? ");
    public string DumpOutBack(string name) => (T("YouDump") ?? "") + name + (T("OutBack") ?? " IS GONE.");
    public string BidsFarewell(string name) => name + (T("BidsFarewell") ?? " IS GONE.");
    public string SighOfRelief(string name) => name + (T("SighOfRelief") ?? " STAYS.");
    public string CantModify(string name) => name + (T("CantModify") ?? " HAS EXPERIENCE: NO CHANGES.");
    public string NotBuilt(string what) => what.ToUpperInvariant() + ": NOT IN THIS BUILD YET.";
    public string NoSaves => "NO SAVED GAMES FOUND.";
    /// <summary>Client only: a save the module refuses (<see cref="GoldBox.Data.Module.Por.PorEngineSave.Check"/>).</summary>
    public string CouldNotLoad(char slot) => $"SAVE {slot} COULD NOT BE LOADED.";

    public string Race(int code) => resources.Race(code) ?? $"RACE {code}";
    public string Gender(int g) => resources.Gender(g) ?? (g == 0 ? "MALE" : "FEMALE");
    public string ClassName(int code) => resources.ClassName(code) ?? $"CLASS {code}";
    public string Alignment(int a) => resources.Alignment(a) ?? $"ALIGNMENT {a}";

    private static IReadOnlyList<string> Words(string? line, string[] fallback) =>
        line is { Length: > 0 } ? line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(AmigaView.Case).ToList() : fallback;
}
