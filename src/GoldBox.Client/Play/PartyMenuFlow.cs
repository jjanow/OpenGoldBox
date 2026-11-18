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
/// into the menu. Train is the training
/// hall's (offered only when $6DA8 is set, never at start-up).
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
    /// (<see cref="PorGame.LoadAtPartyMenu"/>: its entry run unless the save's interface state is 0).</summary>
    public Func<IReadOnlyList<char>>? SaveSlots { get; init; }
    public Func<char, uint, PorGame>? LoadGame { get; init; }

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

    private string? _notice;
    private bool[]? _enabled;
    /// <summary>DS:557D: a save was written since the last menu key other than E and S (set at 0x2028F).</summary>
    private bool _saved;
    /// <summary>A save loaded with interface state 0: the menu goes on with its party; Begin runs its memory.</summary>
    private PorGame? _loaded;

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
        while (Started is null)
        {
            _enabled = PartyMenu.Enabled(Party.Current is not null, trainOn: false, _enabled);
            var shown = new List<(string, PartyMenuItem)>();
            for (int i = 0; i < PartyMenu.Count; i++)
                if (_enabled[i])
                    shown.Add((texts.MenuItem(i), (PartyMenuItem)i));
            var answer = ask(new PartyMenuRequest(shown) { Prompt = texts.ChooseFunction, Notice = _notice });
            _notice = null;
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
                    if (YesNo(texts.QuitToDos))
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

    private bool YesNo(string prompt)
    {
        Highlight.Index = 1;
        return ask(new ChoiceRequest(null, texts.YesNo, false, 1) { MenuPrompt = prompt }) is 0;
    }

    // ---- Create ---------------------------------------------------------------------------------------------------

    /// <summary>A vertical pick (0x2D669 with Exit): the list position, or null on Exit.</summary>
    private int? Pick(IReadOnlyList<string> rows)
    {
        var a = ask(new PickRequest(rows, [texts.Exit], 0) { Column = 3 }) as ListAnswer;
        return a is { Action: PickRequest.RowChosen } ? a.Row : null;
    }

    /// <summary>
    /// Create (0x19EEB): race, gender, class, alignment (Exit on any list leaves without a character), the rolls until
    /// the player keeps one, the name, the portrait and the icon, then the character is written to the roster.
    /// </summary>
    public void Create()
    {
        var cc = new CharacterCreation(tables, Random.Next);
        if (Pick([.. CharacterCreation.RaceCodes.Select(texts.Race)]) is not { } race)
            return;
        cc.PickRace(CharacterCreation.RaceCodes[race]);
        if (Pick([texts.Gender(0), texts.Gender(1)]) is not { } gender)
            return;
        cc.PickGender(gender);
        var classes = cc.ClassChoices();
        if (Pick([.. classes.Select(texts.ClassName)]) is not { } cls)
            return;
        cc.PickClass(classes[cls]);
        var aligns = cc.AlignmentChoices();
        if (Pick([.. aligns.Select(texts.Alignment)]) is not { } al)
            return;
        cc.PickAlignment(aligns[al]);
        var c = cc.Character;
        do
        {
            cc.Roll(items);
            Highlight.Index = 1;
        }
        while (ask(new SheetRequest(c, texts.YesNo) { Prompt = texts.KeepCharacter }) is not 0);

        string name;
        do
            name = (ask(new TextRequest(texts.CharacterName, false, EclCharacter.MaxNameLength)) as string ?? "").Trim();
        while (name.Length == 0);
        cc.SetName(name.ToUpperInvariant());
        Portrait(c);
        Icon(c);
        roster.Save(c, Party);
    }

    /// <summary>The portrait pick (0x1CD66): Head and Body step through 1..14 and 1..12; Keep is the only way out.</summary>
    private void Portrait(EclCharacter c)
    {
        while (true)
        {
            var a = ask(new SheetRequest(c, texts.PortraitMenu));
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
    /// the old icon. The new icon is drawn from the record, so it shows each step at once.
    /// </summary>
    private void Icon(EclCharacter c)
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
                return;
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

    /// <summary>View: the character page; Home / End switch members.</summary>
    private void View()
    {
        while (Party.Current is { } c)
        {
            var a = ask(new SheetRequest(c, [texts.Exit]) { PartySelect = true, CancelAction = 0 });
            if (a is int m && (m == ChoiceRequest.PreviousMember || m == ChoiceRequest.NextMember))
            {
                PartyMenu.Select(Party, m == ChoiceRequest.NextMember ? 1 : -1);
                continue;
            }
            return;
        }
    }

    /// <summary>Remove: SaveChar (0x1E684) of the current character, then 0x1CC10.</summary>
    public void Remove()
    {
        if (Party.Current is not { } cur)
            return;
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

    /// <summary>Add (0x1C9EC): the roster list; a picked name is marked and joins when the party rules allow.</summary>
    public void Add()
    {
        var entries = roster.Entries(Party);
        if (entries.Count == 0)
            return;
        int row = 0;
        while (true)
        {
            var a = ask(new PickRequest(entries, texts.AddExit, 1) { Prompt = texts.AddPrompt }) as ListAnswer;
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

    /// <summary>Load (0x1F5E8): a slot of the saves present; the game goes on from it.</summary>
    private void Load()
    {
        if (PickSlot() is not { } slot || LoadGame is null)
            return;
        PorGame g;
        try
        {
            g = LoadGame(slot, Random.Seed);
        }
        catch (Exception e) when (e is FormatException or IOException or System.Text.Json.JsonException)
        {
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

    /// <summary>DS:0619 + 42·i: the menu entries (v1.3).</summary>
    public string MenuItem(int i) =>
        program?.PascalString(0x619 + 42 * i, 40) is { Length: > 0 } s ? s : ((PartyMenuItem)i).ToString();

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
    public string Exit => MenuItem((int)PartyMenuItem.Exit).Split(' ')[0].ToUpperInvariant();

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
        line is { Length: > 0 } ? line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToUpperInvariant()).ToList() : fallback;
}
