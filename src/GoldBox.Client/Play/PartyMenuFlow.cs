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
    public string? Prompt { get; init; }
    public int? CancelAction { get; init; }
}

/// <summary>
/// The party menu (GAME.OVR 0x19AE0) and what it runs: Create (0x19EEB), Drop, View, Add, Remove, Load, Begin and Exit,
/// as game logic on the <see cref="ScriptRunner"/> thread. The rules are Core's (<see cref="PartyMenu"/>,
/// <see cref="CharacterCreation"/>); the characters go to the roster files (<see cref="PorRoster"/>) as the program's do.
/// Not built yet (a notice says so): Modify, Save before the game starts, the icon colours. Train is the training
/// hall's (offered only when $6DA8 is set, never at start-up).
/// </summary>
public sealed class PartyMenuFlow(Func<PlayerRequest, object?> ask, PartyMenuTexts texts, CreationTables tables,
    BaseItemTable items, PorRoster roster, uint seed)
{
    public EclParty Party { get; } = new();

    /// <summary>
    /// The program's shared menu highlight (DS:6B2B, <see cref="MenuHighlight"/>): the word menus on the bottom line
    /// open on it and a choice moves it; the main menu's hotkeys move it to their entry; the yes/no routine 0x2D9A0
    /// sets it to its second word (No) first (DOSBox-X s19: Keep and Is-this-icon-ok open on NO).
    /// </summary>
    public MenuHighlight Highlight { get; } = new();

    /// <summary>The game's generator: creation draws from it, and a new game goes on with its seed.</summary>
    public TurboPascalRandom Random { get; } = new(seed);

    /// <summary>A new game for the party (not run yet), when Begin is chosen.</summary>
    public Func<EclParty, uint, PorGame>? NewGame { get; init; }

    /// <summary>The save slots present (letters, in order) and a game loaded from one (its entry run).</summary>
    public Func<IReadOnlyList<char>>? SaveSlots { get; init; }
    public Func<char, uint, PorGame>? LoadGame { get; init; }

    /// <summary>Exit to DOS.</summary>
    public Action? Quit { get; init; }

    /// <summary>The game Begin or Load started, set before its entry runs (the screen hands over to it).</summary>
    public PorGame? Started { get; private set; }

    /// <summary>The level stats 0x2AA87 the join runs.</summary>
    private void LevelStatsOf(EclCharacter c) => LevelStats.Apply(c, tables.Levels);

    private string? _notice;
    private bool[]? _enabled;

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
                case PartyMenuItem.Add:
                    Add();
                    break;
                case PartyMenuItem.Remove:
                    if (Party.Current is { } cur)
                    {
                        roster.Save(cur, Party);
                        PartyMenu.RemoveCurrent(Party);
                    }
                    break;
                case PartyMenuItem.Load:
                    Load();
                    break;
                case PartyMenuItem.Begin:
                    if (Party.Members.Count > 0 && NewGame is { } make)
                    {
                        var g = make(Party, Random.Seed);
                        Started = g;
                        g.RunNewGameEntry();
                    }
                    break;
                case PartyMenuItem.Exit:
                    if (YesNo(texts.QuitToDos))
                    {
                        // [?] With a party the program saves until a save succeeds (0x19E0E); no game exists before
                        // Begin here, so nothing is saved.
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
    /// The icon editor (0x1D1AC), the parts and the size: Head steps the head picture 0..13 (CHEAD), Weapon the body
    /// picture 0..31 (CBODY), Size small / large; the IconOk question (PartyMsg.IconOk) ends it. [?] The colour menus (color-1, color-2)
    /// are not built.
    /// </summary>
    private void Icon(EclCharacter c)
    {
        var old = (byte[])c.Record.Clone();
        while (true)
        {
            var a = ask(new IconRequest(c, old, texts.IconMenu) { CancelAction = texts.IconMenu.Count - 1 });
            switch (a)
            {
                case 0:
                    c[CharacterCreation.IconHead] = (byte)((c[CharacterCreation.IconHead] + 1) % 14);
                    break;
                case 1:
                    c[CharacterCreation.IconBody] = (byte)((c[CharacterCreation.IconBody] + 1) % 32);
                    break;
                case 2:
                    c[CharacterCreation.IconSize] = (byte)(c[CharacterCreation.IconSize] == 2 ? 1 : 2);
                    break;
                default:
                    Highlight.Index = 1;
                    if (ask(new IconRequest(c, old, texts.YesNo) { Prompt = texts.IconOk }) is 0)
                        return;
                    break;
            }
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

    /// <summary>Drop (0x1B8B6): asked twice, then the character leaves for good (its file stays, off the list).</summary>
    private void Drop()
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
    private void Add()
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
        var slots = SaveSlots?.Invoke() ?? [];
        if (slots.Count == 0 || LoadGame is null)
        {
            _notice = texts.NoSaves;
            return;
        }
        var words = slots.Select(s => s.ToString()).ToList();
        words.Add(texts.Exit);
        if (ask(new ChoiceRequest(null, words, false, words.Count - 1) { MenuPrompt = texts.LoadWhich }) is not int k
            || k < 0 || k >= slots.Count)
            return;
        var g = LoadGame(slots[k], Random.Seed);
        Started = g;
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

    public IReadOnlyList<string> AddExit => Words(T("AddExit"), ["ADD", "EXIT"]);
    public IReadOnlyList<string> PortraitMenu => Words(T("PortraitMenu"), ["HEAD", "BODY", "KEEP"]);
    public IReadOnlyList<string> YesNo => Words(program?.Text("CombatMsg.YesNo"), ["YES", "NO"]);
    /// <summary>The icon editor's words: ours (the program's menus are DS strings with colour parts, not read yet [?]).</summary>
    public IReadOnlyList<string> IconMenu { get; } = ["HEAD", "WEAPON", "SIZE", "EXIT"];
    /// <summary>The lists' Exit word: the first word of the menu's Exit entry.</summary>
    public string Exit => MenuItem((int)PartyMenuItem.Exit).Split(' ')[0].ToUpperInvariant();

    public string DropForever(string name) => (T("Drop") ?? "DROP ") + name + (T("Forever") ?? "? ");
    public string DumpOutBack(string name) => (T("YouDump") ?? "") + name + (T("OutBack") ?? " IS GONE.");
    public string BidsFarewell(string name) => name + (T("BidsFarewell") ?? " IS GONE.");
    public string SighOfRelief(string name) => name + (T("SighOfRelief") ?? " STAYS.");
    public string NotBuilt(string what) => what.ToUpperInvariant() + ": NOT IN THIS BUILD YET.";
    public string NoSaves => "NO SAVED GAMES FOUND.";

    public string Race(int code) => resources.Race(code) ?? $"RACE {code}";
    public string Gender(int g) => resources.Gender(g) ?? (g == 0 ? "MALE" : "FEMALE");
    public string ClassName(int code) => resources.ClassName(code) ?? $"CLASS {code}";
    public string Alignment(int a) => resources.Alignment(a) ?? $"ALIGNMENT {a}";

    private static IReadOnlyList<string> Words(string? line, string[] fallback) =>
        line is { Length: > 0 } ? line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToUpperInvariant()).ToList() : fallback;
}
