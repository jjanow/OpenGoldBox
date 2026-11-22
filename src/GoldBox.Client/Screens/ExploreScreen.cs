using System.Diagnostics;
using System.Globalization;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The exploring screen of a 3D area, laid out like the original (DOSBox-X, PoR v1.3 EGA): the 88×88 view at pixel
/// (24, 24), the party list on the right, the position line under it, the text window, and the command line at the
/// bottom. Commands go to the area loop (<see cref="AreaLoop"/>) on the logic thread (<see cref="ScriptRunner"/>);
/// while the logic waits for the player, the menu, text entry or pause it asked for is shown here. Every request
/// works with keyboard, mouse and pad alone.
/// </summary>
public sealed class ExploreScreen : IScreen
{
    private const int ViewX = 24, ViewY = 24;
    private const int StatusRow = 15, TextRow = 17, TextRows = 6, HelpRow = 21, MenuRow = 24;

    private readonly PorGame _game;
    private readonly ClientPlayer _player;
    private readonly ScriptRunner _runner;
    private readonly IScreenHost _host;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The command line, read from the player's START.EXE (the first word is the area map); our stand-in when not found.</summary>
    private readonly string[] _commands;

    /// <summary>
    /// The program's shared menu highlight (DS:6B2B, <see cref="MenuHighlight"/>): the command line's highlighted item
    /// and the combat screen's menus. A command taken by its hotkey moves it there (DOSBox-X: after S, SEARCH; arrows
    /// leave it); it starts where the load menu left it, the loaded slot's place among the saves present (DOSBox-X:
    /// saves A, B, J, Load B → CAST, then the first combat menu on View).
    /// When the Client started at the party menu, the cell is the one the menus share (the load menu left it on the
    /// slot's place); with --save it starts at the slot's place among the original saves present.
    /// </summary>
    private readonly MenuHighlight _highlight;

    // State of the request on screen.
    private PlayerRequest? _shown;
    private int _selected;
    private string _typed = "";
    private TimeSpan _delayEnd;
    private DelayRequest? _bottomLineDrawn;
    private readonly ListPage _list = new();
    /// <summary>The training hall's screen (PROGRAM 0), drawn while <see cref="ClientPlayer.Hall"/> is set.</summary>
    private readonly HallView _hall = new();
    /// <summary>A click already chose this answer (a party member, a key wait); given on the next update.</summary>
    private object? _clickAnswer;
    private bool _hasClickAnswer;
    /// <summary>The combat screen: it takes over drawing and input while a fight runs (<see cref="CombatView.Active"/>).</summary>
    private readonly CombatScreen _combat;

    /// <summary>The art setting and the art drawn through it (combat pictures, encounter pictures).</summary>
    private readonly ClientArt _art;

    /// <summary>The picture window's animation, played while a menu with the animate flag waits.</summary>
    private readonly PictureAnimation _anim = new();

    /// <summary>
    /// The camp's picture: on entering the camp (0x196A0 → START 0x27D9 with DS:49F3 = 2) the program loads PIC block
    /// 0x1D (START 0x2852) and its menus animate it.
    /// </summary>
    public const int CampPicture = 0x1D;

    /// <summary>What the picture window shows: the camp's picture while camping, else the encounter picture (or null).</summary>
    private ViewPicture? ShownPicture =>
        _player.Camping ? new ViewPicture(ViewPictureKind.Picture, CampPicture, null, 0, _game.Memory.Disk)
        : _player.Interface == Venue.Loot && _player.LootDrawn
            ? new ViewPicture(ViewPictureKind.Picture, LootPicture, null, 0, _game.Memory.Disk)
        : _player.ViewPicture;

    /// <summary>
    /// The loot menu's picture: 0x644B redraws the screen for DS:49F3 = 6 (START 0x3379, case 6 at 0x3458), which shows
    /// PIC block 1 through the same call as the camp's (0x3D0:0x2A) and draws no position line (read s21; DOSBox-X: the
    /// treasure chest).
    /// </summary>
    public const int LootPicture = 1;

    /// <summary>The combat screen this screen hands over to during a fight.</summary>
    public CombatScreen Combat => _combat;

    public ExploreScreen(PorGame game, ClientPlayer player, ScriptRunner runner, IScreenHost host, char lastLetter = 'A',
        ClientArt? art = null, MenuHighlight? highlight = null)
    {
        _highlight = highlight ?? new MenuHighlight { Index = LoadMenuIndex(game.Content.SaveFolder, char.ToUpperInvariant(lastLetter)) };
        _list.Shared = _highlight; // the list pages enter and page the list routine's global top (MenuHighlight.ListTop)
        _game = game;
        _player = player;
        game.Loop.Redrawn += () =>
        {
            player.PositionLineErased = false; // the step redraws the position line (0x17707)
            player.ViewDrawn(); // the view replaces the picture, sprite or blank on screen
            Interlocked.Increment(ref _viewRedraws); // and a picture kept on screen (DrawView)
        };
        game.Loop.ViewDrawn += () =>
        {
            player.ViewDrawn(); // a turn or the area map (0x2EEF4) draws over the picture or blank (DOSBox-X s42)
            Interlocked.Increment(ref _viewRedraws);
        };
        game.Vm.ScriptSwitchRedrawn += () =>
        {
            player.SwitchRedrawn(); // 0x3FF5: the new area's view over the old script's picture or blank
            Interlocked.Increment(ref _viewRedraws);
        };
        _runner = runner;
        _host = host;
        _art = art ?? new ClientArt(game.Content, null, new ScreenPalette(), Data.Por.PorArtChoice.Dos);
        _combat = new CombatScreen(player.FightView, _art.Combat, game.Art.Font, runner, player.Chooser.Text,
            player.Resources.ItemWord)
        {
            StatusName = s => player.Resources.StatusName(s),
            Report = Report,
            Highlight = _highlight,
            Pictures = _art.Pictures,
        };
        // The AI's key check (0xBB81) takes a key waiting from the AI turn's pauses (the logic thread runs only while
        // this screen's Update waits for it).
        if (game.Kit is { } kit)
        {
            kit.AiKey = () => _keys?.TakeAiKey() ?? 0;
            // Alt-X (0x1638E) works when the start word equals the program's copy of the debugging word (0x16374). The
            // Amiga program has no Alt-X (CombatScreen.AltKeys): under the Amiga art the AI's key check ignores it too.
            var program = game.Content.Program();
            kit.AltXEnabled = () => !AmigaView.On && program?.Text("Fight.Password") is { Length: > 0 } word
                && HallScreens.ParamStr1(player.ProgramWord) == word;
        }
        _commands = game.Art.Program.ExploreCommands is { Count: > 0 } c
            ? [.. c.Select(w => w.ToUpperInvariant())]
            : ["AREA", "CAST", "VIEW", "ENCAMP", "SEARCH", "LOOK"];
    }

    public PorGame Game => _game;

    /// <summary>The creation and roster pages over the training hall and the camp's icon editor (a <see cref="PartyMenuScreen"/>
    /// overlay), or null.</summary>
    public Func<IScreen>? PartyPages { get; init; }

    /// <summary>The party menu's page (rows 0–23) the Load came from, drawn under a Load's vector 4 while
    /// <see cref="ClientPlayer.PartyMenuUnder"/>.</summary>
    public byte[]? PartyMenuPage { get; set; }

    /// <summary>A save loaded in the training hall: the new game's screen (its entry run), or null when it failed.</summary>
    public Func<char, IScreen?>? LoadSave { get; init; }

    /// <summary>
    /// The party menu after the party is destroyed (QoL <see cref="ClientSettings.AfterDefeatMenu"/>); null: as the
    /// original, whose game loop returns and the program quits to DOS (START 0x142, docs/re/start-menu.md §8).
    /// </summary>
    public Func<IScreen?>? PartyMenu { get; init; }

    /// <summary>The on/off settings the controls screen shows (<see cref="SettingToggle"/>).</summary>
    public IReadOnlyList<SettingToggle>? Toggles { get; init; }
    public Audio.SoundSetting? Sound { get; init; }

    /// <summary>This screen handed over to another game or the menu.</summary>
    private bool _left;

    /// <summary>The action layer of the last update (the AI's key check reads its key buffer).</summary>
    private ActionInput? _keys;

    /// <summary>The shared menu highlight cell (0-based, DS:6B2B − 1).</summary>
    public int MenuHighlight => _highlight.Index;

    /// <summary>The highlighted entry of the bottom-line menu on screen.</summary>
    public int Selected => _selected;

    /// <summary>The picture window's animation frame (0-based).</summary>
    public int PictureFrame => _anim.Frame;

    /// <summary>The command line now: the overland's (<see cref="OverlandUi.CommandLine"/>) while DS:49FA is 2–4.</summary>
    private string[] Commands => OverlandUi.Commands(_game) ? OverlandUi.CommandLine(_commands) : _commands;

    public bool ShowingAreaMap => _game.Loop.ShowAreaMap;

    // ---------------------------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------------------------

    private void Run(AreaCommand command)
    {
        if (_runner.Busy || _game.Loop.Defeated || _player.Halted is not null)
            return;
        _player.Notice = null;
        if (_player.ViewBlanked && !OverlandUi.Shown(_game))
            _player.ViewBoxBlank = true; // the command's redraw draws the view alone (ClientPlayer.ViewBoxBlank)
        _player.ViewBlanked = false; // CLEARBOX's blank lasts until the next command's redraw (or a script switch's, SwitchRedrawn)
        char? letter = command switch
        {
            AreaCommand.AreaMap => 'A',
            AreaCommand.Encamp => 'E',
            AreaCommand.Search => 'S',
            AreaCommand.Look => 'L',
            _ => null,
        };
        if (letter is { } l)
            Took(l);
        // The position line as the program leaves it: A loops inside the command routine with no redraw; every other
        // command redraws it (a turn, the step 0x17438, the camp) unless its script switches (see StaleStatusLine).
        if (command != AreaCommand.AreaMap)
        {
            _player.PositionLineErased = false;
            _player.ForgetPagePrompt(); // the command line is drawn again over the bottom row
            _player.CommandRedrawn(); // [?] PROTECT's glyphs on the frame go with the redraw (ClientPlayer.ScreenGlyphs)
            _staleLine = null;
            _lineAtCommand = StatusLine(_game, _player.CampingLine);
            _scriptAtCommand = _game.Vm.CurrentScript;
        }
        if (command == AreaCommand.Forward)
            _player.Text.Clear(); // [?] when the original clears the text window is not read; we clear on a step
        _runner.Start(() => _game.Loop.Execute(command));
        if (_runner.Fault is { } e)
            Report(e);
        AfterCommand();
    }

    /// <summary>
    /// C and V (0x17054 → 0x17CD2 / 0x21AED, both lines): the routine reads the next key after them, so no vector runs and
    /// no time passes (<see cref="AreaLoop.MenuCommand"/>); the shared highlight moves to the word (the View page opens
    /// on that cell). A CLEARBOX blank is not touched here: View's exit and Cast's redraw draw START 0x3379
    /// (<see cref="ClientPlayer.InterfaceDrawn"/>), a Cast with nothing memorised draws nothing (P s41).
    /// </summary>
    private void RunMenu(AreaCommand command)
    {
        if (_runner.Busy || _game.Loop.Defeated || _player.Halted is not null)
            return;
        _player.Notice = null;
        Took(command == AreaCommand.View ? 'V' : 'C');
        _runner.Start(() => _game.Loop.MenuCommand(command));
        if (_runner.Fault is { } e)
            Report(e);
        AfterCommand();
    }

    /// <summary>The area loop ended (party destroyed, DS:49FF, after its screen and key): the program quits, or the
    /// party menu follows with the option on.</summary>
    private void AfterCommand()
    {
        if (_left)
            return;
        if (_game.Loop.Ended && PartyMenu is not null && PartyMenu() is { } menu)
            Leave(menu);
        else if (_game.Loop.Ended)
        {
            _left = true;
            _host.Quit();
        }
        else if (_player.Halted is { } halt)
            _player.Notice = ClientPlayer.HaltNotice(halt);
    }

    private void Leave(IScreen next)
    {
        _left = true;
        _host.Replace(next);
    }

    private void Report(Exception e)
    {
        if (e is HallLoadException load)
        {
            if (LoadSave?.Invoke(load.Slot) is { } next)
                Leave(next);
            else
                _player.Notice = $"SAVE {load.Slot} COULD NOT BE LOADED.";
            return;
        }
        if (e is ProgramHaltException halt)
        {
            // The original quits to DOS there (ClientPlayer.Halt showed it when the screen had the hook): the game is over.
            _player.Halted ??= halt;
            _player.Notice = ClientPlayer.HaltNotice(halt);
            return;
        }

        Console.Error.WriteLine("game logic: " + e);
        _player.Notice = "ERROR: " + e.Message.ToUpperInvariant();
    }

    /// <summary>A command-line word by its first letter (the original's hotkeys).</summary>
    private GameAction? CommandAction(string word) => word[0] switch
    {
        'A' => GameAction.AreaMap,
        'E' => GameAction.Encamp,
        'S' => GameAction.Search,
        'L' => GameAction.Look,
        'V' => GameAction.View,
        'C' => GameAction.Cast,
        _ => NotYet(word),
    };

    /// <summary>A command taken by its hotkey: the shared highlight moves to it.</summary>
    private void Took(char letter)
    {
        int at = Array.FindIndex(Commands, w => w[0] == letter);
        if (at >= 0)
            _highlight.Chosen(at);
    }

    /// <summary>
    /// The load menu's highlight after picking <paramref name="slot"/>: its place among the saves present (the menu
    /// lists the letters of the SAVGAM?.DAT files found, A to J; a module holds its shipped saves as engine saves
    /// <c>x.json</c>, which count the same). 0 when unknown.
    /// </summary>
    public static int LoadMenuIndex(string? saveFolder, char slot)
    {
        if (saveFolder is null || !Directory.Exists(saveFolder))
            return 0;
        var letters = Directory.EnumerateFiles(saveFolder)
            .Select(Path.GetFileName)
            .Select(n => n is { Length: 11 } && n.StartsWith("SAVGAM", StringComparison.OrdinalIgnoreCase) &&
                         n.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase) ? n[6]
                : n is { Length: 6 } && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? n[0]
                : '\0')
            .Select(char.ToUpperInvariant)
            .Where(ch => ch is >= 'A' and <= 'J')
            .Distinct()
            .Order()
            .ToList();
        int i = letters.IndexOf(slot);
        return i < 0 ? 0 : i;
    }

    private GameAction? NotYet(string command)
    {
        Took(command[0]);
        _player.Notice = command + ": NOT IN THIS BUILD YET.";
        return null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Mouse
    // ---------------------------------------------------------------------------------------------------------------

    public GameAction? MapClick(ScreenClick click)
    {
        if (_combat.Active)
            return _combat.MapClick(click);
        Sync();
        if (_runner.Pending is { } request)
            return RequestClick(request, click);
        if (click.Right)
            return null;
        int col = click.X / 8, row = click.Y / 8;
        if (OverlandUi.Commands(_game))
        {
            if (OverlandUi.Click(_game, click.X, click.Y) is { } travel)
                return travel;
        }
        else if (click.X >= ViewX && click.X < ViewX + 88 && click.Y >= ViewY && click.Y < ViewY + 88)
        {
            int vx = click.X - ViewX, vy = click.Y - ViewY;
            if (vx < 29) return GameAction.TurnLeft;
            if (vx >= 59) return GameAction.TurnRight;
            return vy >= 59 ? GameAction.TurnAround : GameAction.Forward;
        }
        if (col >= 17 && row >= 4 && _game.Memory.Party is { } party && row - 4 < Math.Min(8, party.Members.Count))
        {
            // A click on a name: the pick as Home / End would reach it (mouse stand-in for 0x173BF).
            party.CurrentIndex = row - 4;
            PositionLineDrawn();
            return null;
        }
        if (row == MenuRow && WordAt(Commands, col) is { } cmd)
            return CommandAction(Commands[cmd]);
        if (row == HelpRow + 1 && _player.Text.IsEmpty)
            return GameAction.Controls;
        return null;
    }

    public void Hover(int x, int y)
    {
        if (_combat.Active)
        {
            _combat.Hover(x, y);
            return;
        }
        Sync();
        if (_runner.Pending is HallMenuRequest hm)
            _hall.Hover(hm, x / 8, y / 8);
        else if (_runner.Pending is ListRequest l)
            _list.Hover(l, x / 8, y / 8);
        else if (_runner.Pending is ChoiceRequest c && ItemAt(c, x / 8, y / 8) is { } i)
            _selected = i;
        else if (_runner.Pending is TextRequest t && CellAt(t, x / 8, y / 8) is { } k)
            _selected = k;
    }

    private GameAction? RequestClick(PlayerRequest request, ScreenClick click)
    {
        switch (request)
        {
            case KeyRequest:
                return GameAction.Confirm;
            case HallMenuRequest hm:
                if (!click.Right && _hall.Click(hm, click.X / 8, click.Y / 8, _game.Memory.Party?.Members.Count ?? 0) is { } ha)
                {
                    _clickAnswer = ha;
                    if (ha is char hk && HallScreens.EntryOf(hk) is { } he)
                        _highlight.Chosen((int)he);
                    _hasClickAnswer = true;
                }
                return null;
            case ListRequest l:
                return _list.Click(l, click);
            case ChoiceRequest { ListRow: not null } list:
                if (click.Right)
                    return GameAction.Cancel; // Escape: the highlighted entry (VMENU stores the position)
                if (click.Y / 8 == MenuRow && WordAt([.. ListWords(list).Select(w => w.Word)], click.X / 8 - 1) is { } wi)
                {
                    _listWord = wi;
                    _listPageClick = ListWords(list)[wi].Down;
                    return null;
                }
                if (ItemAt(list, click.X / 8, click.Y / 8) is { } li)
                {
                    _selected = li;
                    return GameAction.Confirm;
                }
                return null;
            case ChoiceRequest c:
                if (click.Right)
                    return c.CancelIndex is not null ? GameAction.Cancel : null;
                if (ItemAt(c, click.X / 8, click.Y / 8) is { } i)
                {
                    _selected = i;
                    if (c.ConfirmAnswer is null)
                        return GameAction.Confirm;
                    // the clicked item itself where Confirm has its own answer (the rest-time menu's Enter = Rest)
                    _clickAnswer = i;
                    _hasClickAnswer = true;
                    return null;
                }
                if (c.PartySelect && RosterAt(click.X / 8, click.Y / 8) is { } m)
                {
                    _clickAnswer = TradeContext.MemberPick - m;
                    _hasClickAnswer = true;
                }
                return null;
            case TextRequest t:
                if (click.Right)
                {
                    Backspace();
                    return null;
                }
                if (CellAt(t, click.X / 8, click.Y / 8) is { } k)
                {
                    _selected = k;
                    return GameAction.Confirm;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>The word under a column of a line of words separated by one space.</summary>
    private static int? WordAt(IReadOnlyList<string> words, int col)
    {
        int c = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (col >= c && col < c + words[i].Length)
                return i;
            c += words[i].Length + 1;
        }
        return null;
    }

    private const int ListFirstRow = TextRow + 1, ListRows = TextRows - 1;

    /// <summary>A vertical list's first row and row count: VMENU's window (<see cref="ChoiceRequest.ListRow"/> to row 22)
    /// or the Client's own under the prompt.</summary>
    private static (int First, int Rows) ListWindow(ChoiceRequest c) =>
        c.ListRow is { } r ? (r, Math.Max(1, TextWindow.Bottom - r + 1)) : (ListFirstRow, ListRows);

    private int ListTop(ChoiceRequest c)
    {
        if (c.ListRow is not null)
            return _listTop;
        int rows = ListWindow(c).Rows;
        return Math.Clamp(_selected - rows + 1, 0, Math.Max(0, c.Items.Count - rows));
    }

    // ---- VMENU's list routine 0x2D669 (ChoiceRequest.ListRow; P s30, D s44) --------------------------------------

    /// <summary>The list window's first entry (DS:6B29, the shared <see cref="MenuHighlight.ListTop"/>): 0 when VMENU
    /// opens (its position 0, 0x2D766), moved only by Next / Prev, which leave it in the shared word.</summary>
    private int _listTop;

    /// <summary>The lit word of the list's menu line (the shared cell; 0x2D6B9 stores 1 on entry).</summary>
    private int _listWord;

    /// <summary>A click on Next (true) or Prev (false) waiting for the next update (ours).</summary>
    private bool? _listPageClick;

    /// <summary>The list's menu line: Next while entries remain below the window, Prev while the top is past 0
    /// (0x2D7E6–0x2D854; VMENU passes an empty word and no Exit). Each word with its direction (true = down).</summary>
    private List<(string Word, bool Down)> ListWords(ChoiceRequest c)
    {
        var words = new List<(string, bool)>();
        if (c.Paging is not { } p)
            return words;
        var (next, prev) = ListWindowSteps.Words(_listTop, ListWindow(c).Rows, c.Items.Count);
        if (next) words.Add((p.Next, true));
        if (prev) words.Add((p.Prev, false));
        return words;
    }

    /// <summary>The lit word as the menu routine shows it: a cell past the last entry goes back to the first (0x2CCE5).</summary>
    private int ListWord(ChoiceRequest c) => _listWord < ListWords(c).Count ? _listWord : 0;

    private void PageList(ChoiceRequest c, bool down)
    {
        (_selected, _listTop) = ListWindowSteps.Page(_selected, _listTop, ListWindow(c).Rows, c.Items.Count, down);
        _highlight.ListPaged(_listTop); // 0x2D502: the global word, kept for the next list
        _listWord = ListWord(c);
    }

    /// <summary>
    /// VMENU's keys as the list routine 0x2D669 takes them (read s44): Home / End (keypad 7 / 1; the Client's Up / Down
    /// too) move the highlight inside the window, wrapping there (0x2D594); N / P (the shown words' hotkeys) and PgDn /
    /// PgUp (keypad 3 / 9, only while the word shows) move the window by its rows, at most to the last full window
    /// (0x2D502); ',' / '.' (Left / Right) move the lit word; any other key the menu returns (Return, space) picks the
    /// highlighted entry, and so does Escape: 0x2D669 then returns no entry, but VMENU stores the position it leaves
    /// (0x1D9E). Other letters, E included, are no hotkey and do nothing (DOSBox-X s44). The pad pages with its
    /// shoulders / triggers as the spell lists (user s30). [?] The Amiga's list routine is not read: the same there.
    /// </summary>
    private Reply? ListAnswer(ChoiceRequest c, ActionInput input)
    {
        int n = c.Items.Count, rows = ListWindow(c).Rows;
        var words = ListWords(c);
        bool keys = input.LastDevice == InputDevice.Keyboard, pad = input.LastDevice == InputDevice.Gamepad;
        if (n > 0 && (input.Pressed(GameAction.MenuUp) || (keys && input.Pressed(GameAction.StepUpLeft))))
            _selected = ListWindowSteps.Step(_selected, _listTop, rows, n, down: false);
        if (n > 0 && (input.Pressed(GameAction.MenuDown) || (keys && input.Pressed(GameAction.StepDownLeft))))
            _selected = ListWindowSteps.Step(_selected, _listTop, rows, n, down: true);
        if (words.Count > 0 && input.Pressed(GameAction.MenuLeft))
            _listWord = (ListWord(c) + words.Count - 1) % words.Count;
        if (words.Count > 0 && input.Pressed(GameAction.MenuRight))
            _listWord = (ListWord(c) + 1) % words.Count;
        bool? page = _listPageClick;
        _listPageClick = null;
        if (input.Pressed(GameAction.StepDownRight) || (pad && input.Pressed(GameAction.NextMember)))
            page = true;
        if (input.Pressed(GameAction.StepUpRight) || (pad && input.Pressed(GameAction.PreviousMember)))
            page = false;
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < words.Count; i++)
                if (words[i].Word.Length > 0 && char.ToUpperInvariant(words[i].Word[0]) == char.ToUpperInvariant(key[0]))
                {
                    _listWord = i; // the hotkey lights its entry (0x2CF48)
                    page = words[i].Down;
                }
        if (page is { } down && words.Any(w => w.Down == down))
        {
            PageList(c, down);
            return null;
        }
        if (n > 0 && (input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel)))
            return new Reply(_selected);
        return null;
    }

    /// <summary>The party-list member under a cell (rows 4–11, columns 17–38).</summary>
    private int? RosterAt(int col, int row)
    {
        int n = _game.Memory.Party?.Members.Count ?? 0;
        int i = row - 4;
        return col is >= 17 and <= 38 && i >= 0 && i < Math.Min(n, 8) ? i : null;
    }

    /// <summary>First column of a horizontal menu's items: after the menu prompt and a space.</summary>
    private static int MenuStart(ChoiceRequest c) => c.MenuPrompt is { Length: > 0 } p ? p.Length + 1 : 0;

    private int? ItemAt(ChoiceRequest c, int col, int row)
    {
        if (!c.Vertical)
            return row != MenuRow ? null : c.Columns is { } cols
                ? Enumerable.Range(0, Math.Min(cols.Count, c.Items.Count)).Cast<int?>()
                    .FirstOrDefault(i => col - MenuStart(c) >= cols[i!.Value] && col - MenuStart(c) < cols[i.Value] + c.Items[i.Value].Length)
                : WordAt(c.Items, col - MenuStart(c));
        int top = ListTop(c);
        var (first, rows) = ListWindow(c);
        int i = top + row - first;
        return row >= first && row < first + rows && i < c.Items.Count && col >= 1 ? i : null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Text entry: typing on the keyboard, or a character grid for pad and mouse
    // ---------------------------------------------------------------------------------------------------------------

    private const string Digits = "0123456789";
    private const int GridCols = CharGrid.Columns;

    /// <summary>
    /// The grid's heading row: the text window's first (the grid over the text window), or for an area script's input
    /// with the Amiga QoL option (<see cref="GridOffText"/>) <see cref="OffTextRow"/>, over the view and the party
    /// list, so the script's text stays shown.
    /// </summary>
    private static int GridTop(TextRequest t) => GridOffText(t) ? OffTextRow : TextRow;

    /// <summary>The heading row of the grid placed off the text window (rows 2–7: the view and the party list).</summary>
    internal const int OffTextRow = 2;

    /// <summary>The columns the off-text grid's panel blanks (0–32: up to the party list's AC column).</summary>
    private const int OffTextColumns = 33;

    /// <summary>The QoL placement applies: an area script's input (<see cref="TextRequest.ScriptInput"/>) under the
    /// Amiga pages with <see cref="AmigaView.GridOffText"/> on.</summary>
    private static bool GridOffText(TextRequest t) => t.ScriptInput && AmigaView.On && AmigaView.GridOffText;

    private static int GridFirstRow(TextRequest t) => GridTop(t) + 1;

    private static string Charset(TextRequest t) => t.Numeric ? Digits : CharGrid.Letters;

    /// <summary>Grid cells (<see cref="CharGrid"/>'s layout): the characters, then DEL and OK.</summary>
    private static (int Col, int Row, string Label) Cell(TextRequest t, int k) => CharGrid.Cell(Charset(t), GridFirstRow(t), k);

    private static int CellCount(TextRequest t) => CharGrid.CellCount(Charset(t));

    private static int? CellAt(TextRequest t, int col, int row) => CharGrid.CellAt(Charset(t), GridFirstRow(t), col, row);

    private void Backspace()
    {
        if (_typed.Length > 0)
            _typed = _typed[..^1];
    }

    private void Type(TextRequest t, char ch)
    {
        if (t.Maximum is { } max)
        {
            // The money input 0x24F1D (Take's amount): digits only; a number above the maximum becomes it at once.
            if (ch is < '0' or > '9')
                return;
            string next = _typed + ch;
            _typed = next.Length > t.MaxLength || int.Parse(next, CultureInfo.InvariantCulture) > max
                ? max.ToString(CultureInfo.InvariantCulture)
                : next;
            return;
        }
        if (_typed.Length < t.MaxLength && CharGrid.Holds(Charset(t), ch))
            _typed += ch;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------------------------------------

    public void Update(ActionInput input)
    {
        _keys = input;
        if (input.Pressed(GameAction.Controls))
        {
            _host.Push(new ControlsScreen(_game.Art.Font, _host, _art, _player.ProgramWordSetting, Toggles, Sound));
            return;
        }
        if (_combat.Active)
        {
            _combat.Update(input);
            if (!_runner.Busy)
                AfterCommand();
            return;
        }
        Sync();
        var shown = ShownPicture;
        _anim.Show(shown);
        bool everyPass = _runner.Pending is ChoiceRequest { EveryPass: true } or HallMenuRequest { EveryPass: true }
            or KeyRequest { EveryPass: true };
        if ((everyPass || _runner.Pending is ChoiceRequest { Animates: true }) && shown is not null)
        {
            var delays = _art.Pictures.Delays(shown);
            if (everyPass) // $4AFF ≠ 0: a frame on every pass (0x2CDD6), one Client update each (ScriptRunner.EveryPass)
                delays = new uint[delays.Count];
            _anim.Tick(_clock.Elapsed, delays, _runner.AnimationSpeed);
            _player.PictureUndrawn = false; // the menu draws the frames (a camp picture loaded by START 0x3379 case 2 too)
        }
        if (_left)
            return;
        if (PartyPages is { } pages && PartyMenuScreen.IsPage(_runner.Pending))
        {
            _host.Push(pages());
            return;
        }
        // Keys pressed during a pause wait for the next key read (ActionInput.ReadingKeys); a party page reads them there.
        input.ReadingKeys(_runner.Pending is not DelayRequest { ReportInput: false },
            _runner.Pending is not (KeyRequest { AsMenu: false } or TextRequest));
        if (_runner.Pending is { } request)
        {
            var reply = Answer(request, input);
            FollowHighlight(request, reply, input.Pressed(GameAction.Cancel));
            if (reply is { } answer)
            {
                if (answer.Value is ChoiceRequest.PreviousMember or ChoiceRequest.NextMember)
                    _memberPicked = true; // the pick (0x23819) redraws the list with the new current member
                _bottomLineDrawn = null;
                _runner.Answer(answer.Value);
                if (_runner.Fault is { } e)
                    Report(e);
                if (!_runner.Busy)
                    AfterCommand();
            }
            return;
        }

        if (OverlandUi.Commands(_game))
        {
            // The overland's arrows set the direction and travel (0x17390); its line has no A.
            if (input.Pressed(GameAction.Search)) Run(AreaCommand.Search);
            else if (input.Pressed(GameAction.Look)) Run(AreaCommand.Look);
            else if (input.Pressed(GameAction.Encamp)) Run(AreaCommand.Encamp);
            else if (input.Pressed(GameAction.View)) RunMenu(AreaCommand.View);
            else if (input.Pressed(GameAction.Cast)) RunMenu(AreaCommand.Cast);
            else if (OverlandUi.Travel(input) is { } travel) Run(travel);
            else PickMember(input);
            return;
        }
        if (input.Pressed(GameAction.AreaMap)) Run(AreaCommand.AreaMap);
        else if (input.Pressed(GameAction.Search)) Run(AreaCommand.Search);
        else if (input.Pressed(GameAction.Look)) Run(AreaCommand.Look);
        else if (input.Pressed(GameAction.Encamp)) Run(AreaCommand.Encamp);
        else if (input.Pressed(GameAction.View)) RunMenu(AreaCommand.View);
        else if (input.Pressed(GameAction.Cast)) RunMenu(AreaCommand.Cast);
        else if (input.Pressed(GameAction.TurnLeft)) Run(AreaCommand.TurnLeft);
        else if (input.Pressed(GameAction.TurnRight)) Run(AreaCommand.TurnRight);
        else if (input.Pressed(GameAction.TurnAround)) Run(AreaCommand.TurnAround);
        else if (input.Pressed(GameAction.Forward)) Run(AreaCommand.Forward);
        else PickMember(input);
    }

    /// <summary>
    /// Home / End on the command line (0x173BF): the member pick 0x23819, the party list and the position line drawn
    /// again (0x173D0, 0x173D9); no command runs. Also on the overland's line (the same routine). The commands go
    /// first: the pad's shoulders are Look / Encamp here (its triggers pick the member).
    /// </summary>
    private void PickMember(ActionInput input)
    {
        if (!input.Pressed(GameAction.PreviousMember) && !input.Pressed(GameAction.NextMember))
            return;
        if (_game.Memory.Party is { } party)
        {
            GoldBox.Core.Game.PartyMenu.Select(party, input.Pressed(GameAction.NextMember) ? 1 : -1);
            PositionLineDrawn();
        }
    }

    /// <summary>
    /// A new request on screen starts with nothing typed and its first item selected, except the menus that share the
    /// program's highlight cell (DS:6B2B, <see cref="MenuHighlight"/>): a bottom-line menu (the menu routine 0x2CC8C)
    /// opens on the cell, a yes/no first sets it to No (<see cref="ChoiceRequest.Selected"/>, 0x2D9A0); a vertical list
    /// (VMENU and the other item lists 0x2D669, WHO's one-entry "Select" menu) sets it to the first entry; a full-screen
    /// list page is an item list whose menu starts where the page asks, and the View page (0x21AED,
    /// <see cref="ListRequest.SharedHighlight"/>) opens on the cell.
    /// </summary>
    private void Sync()
    {
        var request = _runner.Pending;
        if (ReferenceEquals(request, _shown))
            return;
        _shown = request;
        // Row 24 is drawn again by a menu (0x2CC8C fills the row), a message, a prompt or the command line once the run
        // ends: PROTECT's glyphs there are gone (P, s30).
        if (request is ChoiceRequest or KeyRequest or TextRequest or DelayRequest { BottomLine: not null })
        {
            _player.BottomRowDrawn();
            if (request is not KeyRequest { PagePrompt: true })
                _player.ForgetPagePrompt(); // the page prompt left on row 24 is drawn over
        }
        _selected = 0;
        _typed = "";
        _hasClickAnswer = false;
        if (request is ChoiceRequest { Animates: true })
            _anim.MenuOpened(_clock.Elapsed);
        switch (request)
        {
            case ChoiceRequest { Vertical: false } h:
                if (h.Selected != 0)
                    _highlight.Index = h.Selected;
                _selected = _highlight.Start(h.Items.Count);
                break;
            case ChoiceRequest v:
                _highlight.ItemList();
                _selected = Math.Clamp(v.Selected, 0, Math.Max(0, v.Items.Count - 1));
                // 0x2D766 with VMENU's position 0 (0x1D51): the global top to 0 (an empty list leaves it).
                _listTop = v.ListRow is not null && v.Items.Count > 0 ? _highlight.ListEntered(0, v.Items.Count) : 0;
                (_listWord, _listPageClick) = (0, null);
                break;
            case KeyRequest { AsMenu: true } km:
                _highlight.Start(km.MenuEntries);
                break;
            case ListRequest l:
                _list.Sync(l);
                if (l.SharedHighlight)
                    _list.SetAction(l, _highlight.Start(l.Actions.Count));
                else
                    _highlight.Index = _list.SelectedAction; // _list.Sync entered the list routine's global top (ListPage.Shared)
                break;
        }

        if (request is DelayRequest d)
        {
            // A chained pause starts where the last one ended, unless that was longer ago than the pause itself.
            var start = _clock.Elapsed;
            if (d.Chained && _delayEnd <= start && start - _delayEnd < d.Duration)
                start = _delayEnd;
            _delayEnd = start + d.Duration;
        }
    }

    private readonly record struct Reply(object? Value);

    /// <summary>
    /// The shared cell follows a menu's highlight while it is up (',' / '.' move the cell itself in 0x2CC8C) and takes
    /// the chosen entry (a hotkey moves it there, 0x2CF48); Escape leaves it.
    /// </summary>
    private void FollowHighlight(PlayerRequest request, Reply? reply, bool cancelled)
    {
        switch (request)
        {
            case ChoiceRequest { ListRow: not null } list:
                _highlight.Index = ListWord(list); // 0x2D669 leaves the cell where its menu left it
                break;
            case ChoiceRequest { Vertical: false } h:
                _highlight.Index = !cancelled && reply is { Value: int i } && i >= 0 && i < h.Items.Count ? i : _selected;
                break;
            case ListRequest l:
                _highlight.Index = !cancelled && l.Paging is null && reply is { Value: ListAnswer { Action: var a } } && a >= 0 && a < l.Actions.Count
                    ? a
                    : _list.SelectedAction;
                break;
        }
    }

    /// <summary>The answer to the request this frame, or null while the player has not answered.</summary>
    private Reply? Answer(PlayerRequest request, ActionInput input)
    {
        if (_hasClickAnswer)
        {
            _hasClickAnswer = false;
            return new Reply(_clickAnswer);
        }
        switch (request)
        {
            case KeyRequest:
                return input.NewInput is not null || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel)
                    ? new Reply(null)
                    : null;

            case HallMenuRequest hm:
                if (_hall.Answer(hm, input) is not { } ha)
                    return null;
                // The hall's menu runs through the shared menu routine: its hotkey sets the cell (DOSBox-X s22: after
                // Begin the area menu opens on its first word, the cell being past its last).
                if (ha is char hk && HallScreens.EntryOf(hk) is { } he)
                    _highlight.Chosen((int)he);
                return new Reply(ha);

            case ListRequest l:
                return _list.Update(l, input) is { } a ? new Reply(a) : null;

            case DelayRequest { ReportInput: true }:
                if (input.NewInput is not null || input.Clicks.Count > 0 || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel))
                    return new Reply(true);
                return _clock.Elapsed >= _delayEnd ? new Reply(false) : null;

            case DelayRequest { BottomLine: not null } d when d.Duration == TimeSpan.Zero
                && (_player.Hall is null || _player.PageUnder is not null) && !ReferenceEquals(d, _bottomLineDrawn):
                // A draw can be skipped between updates. Keep the line until it has had a frame on screen.
                return null;

            case DelayRequest:
                return _clock.Elapsed >= _delayEnd || input.SkipsPause ? new Reply(null) : null;

            case ChoiceRequest { ListRow: not null } list:
                return ListAnswer(list, input);

            case ChoiceRequest c:
            {
                int n = c.Items.Count;
                if (!c.Vertical && c.LeftAnswer is { } left && input.Pressed(GameAction.MenuLeft))
                    return new Reply(left);
                if (!c.Vertical && c.RightAnswer is { } right && input.Pressed(GameAction.MenuRight))
                    return new Reply(right);
                bool back = input.Pressed(c.Vertical ? GameAction.MenuUp : GameAction.MenuLeft);
                bool fwd = input.Pressed(c.Vertical ? GameAction.MenuDown : GameAction.MenuRight);
                if (back) _selected = (_selected + n - 1) % n;
                if (fwd) _selected = (_selected + 1) % n;
                if (input.Pressed(GameAction.Confirm))
                    return new Reply(c.ConfirmAnswer ?? _selected);
                if (input.Pressed(GameAction.Cancel) && c.CancelIndex is { } ci)
                    return new Reply(ci);
                if (!c.Vertical && c.UpAnswer is { } up && input.Pressed(GameAction.MenuUp))
                    return new Reply(up);
                if (!c.Vertical && c.DownAnswer is { } down && input.Pressed(GameAction.MenuDown))
                    return new Reply(down);
                if (c.PartySelect && input.Pressed(GameAction.PreviousMember))
                    return new Reply(ChoiceRequest.PreviousMember);
                if (c.PartySelect && input.Pressed(GameAction.NextMember))
                    return new Reply(ChoiceRequest.NextMember);
                if (c.MemberLetters is { Length: 2 } ml && input.NewInput is (InputDevice.Keyboard, { Length: 1 } mk))
                {
                    if (mk[0] == ml[0])
                        return new Reply(ChoiceRequest.PreviousMember);
                    if (mk[0] == ml[1])
                        return new Reply(ChoiceRequest.NextMember);
                }
                // The original's hotkeys: the first letter of an item.
                if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
                    for (int i = 0; i < n; i++)
                        if (c.HotkeyOf(i) == key[0])
                        {
                            if (!c.Vertical)
                                _keptSelected = i; // the hotkey lights its entry (0x2CF48) and the row stays so
                            return new Reply(i);
                        }
                return null;
            }

            case TextRequest t:
            {
                int cells = CellCount(t);
                if (input.NewInput is (InputDevice.Keyboard, var name))
                {
                    if (name == "Enter")
                        return new Reply(_typed);
                    if (name == "Escape" && t.Maximum is not null)
                        return new Reply(""); // 0x24F1D answers 0 to Escape
                    if (name == "Back")
                    {
                        Backspace();
                        return null;
                    }
                    if (CharGrid.KeyChar(name, input, t.KeepsCase) is { } ch)
                    {
                        Type(t, ch);
                        return null;
                    }
                    if (name is "Up" or "Down" or "Left" or "Right" or "Escape")
                    {
                        // fall through to the grid moves below
                    }
                    else
                        return null;
                }
                if (input.Pressed(GameAction.MenuLeft)) _selected = (_selected + cells - 1) % cells;
                if (input.Pressed(GameAction.MenuRight)) _selected = (_selected + 1) % cells;
                if (input.Pressed(GameAction.MenuUp)) _selected = Math.Max(0, _selected - GridCols);
                if (input.Pressed(GameAction.MenuDown)) _selected = Math.Min(cells - 1, _selected + GridCols);
                if (input.Pressed(GameAction.Cancel))
                    Backspace();
                if (input.Pressed(GameAction.Confirm))
                {
                    var cs = Charset(t);
                    if (_selected < cs.Length)
                        Type(t, cs[_selected]);
                    else if (_selected == cs.Length)
                        Backspace();
                    else
                        return new Reply(_typed);
                }
                return null;
            }

            default:
                return new Reply(null);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------------------------------------------------------

    private bool _commandLineDrawn;
    private int _bottomRowClearsSeen;

    /// <summary>
    /// The last menu line drawn on row 24 and its highlighted entry (the chosen one once answered: a hotkey moves the
    /// highlight, 0x2CF48), and whether the last frame showed it. The menu routine leaves its row as it is; text typed
    /// after a pick (START 0x77F8's delays) does not touch row 24, so the line stays until something writes there (D s33:
    /// the camp's Magic menu stays while Memorize types its slot message).
    /// </summary>
    private ChoiceRequest? _keptMenu;
    /// <summary>Row 24's pixels in the last combat frame drawn (the party-destroyed window keeps them).</summary>
    private byte[]? _combatRow24;
    private int _keptSelected;
    private bool _keptMenuDrawn;

    public void Draw(IndexedFrame f, ActionInput input)
    {
        bool commandLineWasDrawn = _commandLineDrawn;
        _commandLineDrawn = false;
        bool keptMenuWasDrawn = _keptMenuDrawn;
        _keptMenuDrawn = false;
        if (_player.BottomRowClears is var clears && clears != _bottomRowClearsSeen)
        {
            // NEWECL's script load cleared row 24 (0x7814 → 0x2CFCA): the line kept through typing is gone.
            _bottomRowClearsSeen = clears;
            commandLineWasDrawn = keptMenuWasDrawn = false;
        }
        if (_combat.Active)
        {
            _player.ViewBoxBlank = false; // COMBAT's end draws the layout again (0x2635 → START 0x3379)
            _player.ReleaseRosterNames();
            _combat.Draw(f, input);
            // Row 24 as the fight left it: nothing writes the row between the fight's end and the party-destroyed
            // window's key line (0x6AA5 → 0x6B37–0x6BAA; DOSBox-X s39: the vampire's move line under "THE END!").
            _combatRow24 ??= new byte[f.Width * 8];
            Array.Copy(f.Pixels, MenuRow * 8 * f.Width, _combatRow24, 0, Math.Min(_combatRow24.Length, f.Pixels.Length - MenuRow * 8 * f.Width));
            // The pixels stay; the palette goes back (the combat screen swaps EGA colours 0 and 8, CombatScreen.SwapGrey).
            for (int i = 0; i < _combatRow24.Length; i++)
                _combatRow24[i] = _combatRow24[i] switch { 0 => 8, 8 => 0, var v => v };
            return;
        }
        // A request the logic thread posted after this frame's Update: take its opening highlight before drawing it (D s39:
        // Drop's yes/no drew one frame with the previous menu's Yes lit before the yes/no routine's No, 0x2D9B9).
        Sync();
        var font = _game.Art.Font;
        var pending = _runner.Pending;
        // A full-screen page (a list, View, the hall, a window) covers the party list; the screen drawn after it draws
        // the list again (START 0x1307; D s38: the camp's View shows the revived name's colour again).
        if (pending is ListRequest page)
        {
            _player.ReleaseRosterNames();
            _list.Draw(f, font, page, input);
            return;
        }
        if (_player.PageUnder is { } under)
        {
            _player.ReleaseRosterNames();
            DrawOverPage(f, font, under, pending, input);
            return;
        }
        if (_player.Hall is { } hall)
        {
            _player.ReleaseRosterNames();
            _hall.Draw(f, font, hall, _game.Memory.Party, pending, _selected, input);
            // The frames a hall menu drew over the page while $4AFF was set, left until the loop draws the page again
            // (HallState.PictureOver; DOSBox-X s43).
            if (hall.PictureOver != HallPicture.None && hall.Page is null && ShownPicture is { } picture
                && _art.Pictures.Draw(f, ViewX, ViewY, picture, _anim.Frame) && hall.PictureOver == HallPicture.UnderList)
                HallView.DrawListOver(f, font, _game.Memory.Party);
            return;
        }
        if (_player.PartyMenuUnder && PartyMenuPage is { } menuPage && menuPage.Length <= f.Pixels.Length)
        {
            DrawOverPartyMenu(f, font, menuPage, pending, input);
            return;
        }
        f.Clear(0);
        if (_player.Window is { } window)
        {
            _player.ReleaseRosterNames();
            // A full-screen window (START 03F1:02E8 with 1, 1, 38, 22) and the bottom line.
            Chrome.Box(f, 0, 0, 39, 23);
            foreach (var line in window)
                if (line.Glyphs)
                    Chrome.Glyphs(f, font, line.Column, line.Row, Fit(line.Text, 39 - line.Column), line.Colour);
                else
                    Chrome.Text(f, font, line.Column, line.Row, Fit(line.Text, 39 - line.Column), line.Colour);
            // The press-enter menu with $4AFF set draws the loaded frames at cell (3, 3) over the window (KeyRequest.EveryPass).
            if (pending is KeyRequest { EveryPass: true } && ShownPicture is { } overWindow)
                _art.Pictures.Draw(f, ViewX, ViewY, overWindow, _anim.Frame);
            if (pending is KeyRequest wk)
                DrawKeyLine(f, font, wk, input);
            else if (pending is DelayRequest { BottomLine: not null } windowDelay)
                DrawBottomLine(f, font, windowDelay);
            else if (pending is DelayRequest or null && _player.PagePromptStands)
                DrawKeyLine(f, font, StalePagePrompt, input); // the wipe's message typed over the page prompt (FS-UAE s45)
            else if (_player.KeepsCombatRow24 && _combatRow24 is { } row24 && row24.Length == f.Width * 8)
                Array.Copy(row24, 0, f.Pixels, MenuRow * 8 * f.Width, row24.Length); // the fight's last row 24 (DOSBox-X s39)
            else if (pending is DelayRequest && !_player.WindowAfterFight)
                DrawLine(f, font, Commands, _highlight.Start(Commands.Length), 0); // the DAMAGE wipe's pause: row 24 kept (DOSBox-X s22)
            return;
        }
        // The area screen is up: a later window (a COMBAT with no monsters straight to the share) has no fight row 24.
        _combatRow24 = null;
        // Frame cells as on the original screen: outer box, the divider under the view and party list, the divider
        // between view and party list, the view's own box, the box over the command line.
        bool overlandBoxed = OverlandUi.Shown(_game) && OverlandBoxed;
        // The overland's layouts have no inner box (0x2E03F; START 0x3379 case 3), also when the redraw draws the 3D
        // view there ($49E6 != 0; PICTURE then draws no box either, 0x15B5).
        bool viewBox = !OverlandUi.Commands(_game) || overlandBoxed;
        bool overlandLayout = OverlandUi.Commands(_game) && (_game.Loop.OverlandInterfacePending || _game.Loop.OverlandLayout);
        if (Chrome.AmigaFrame is not null)
        {
            // The Amiga frames the screen as whole windows, later ones over earlier ones' shared cells. Seen in FS-UAE
            // (s39, the explore and camp screens after a load): the full window (1, 1)–(38, 22), the text box's
            // (1, 17)–(38, 22), the view's (1, 1)–(15, 15) and its box (3, 3)–(13, 13): rows 0, 16 and 23 are strips from
            // column 1, column 16 the view window's right side, the corners at (16, 0) and (16, 16) the view window's
            // (por-art-formats.md §4 "Window frame"). That is the 3D layout (hunk 13 +0x5A82, the copy of START 0x32CA,
            // also under the overland's venues). The overland interface (hunk 13 +0x5E80 → hunk 25 +0x1D4E, the copy of
            // 0x2E03F) draws three windows instead: the view's (1, 1)–(15, 15), the party list's (17, 1)–(38, 15) and the
            // text box's (1, 17)–(38, 22), so column 16 is the party window's left side, row 0 over it that window's top
            // and (16, 16) the text box's top edge (FS-UAE s45, block 26's overland after a Load).
            if (overlandLayout)
            {
                Chrome.Box(f, 0, 0, 16, 16);
                Chrome.Box(f, 16, 0, 39, 16);
                Chrome.Box(f, 0, 16, 39, 23);
            }
            else
            {
                Chrome.Box(f, 0, 0, 39, 23);
                Chrome.Box(f, 0, 16, 39, 23);
                Chrome.Box(f, 0, 0, 16, 16);
            }
            if (viewBox)
                Chrome.Box(f, 2, 2, 14, 14);
        }
        else
        {
            Chrome.Box(f, 0, 0, 39, 23);
            Chrome.HLine(f, 16, 1, 38);
            Chrome.VLine(f, 16, 1, 15);
            if (viewBox)
                Chrome.Box(f, 2, 2, 14, 14); // the 3D view's own box; the overland view fills cells 1–15
            foreach (var (c, r) in new[] { (16, 0), (0, 16), (16, 16), (39, 16) })
                if (!(overlandLayout && (c, r) == (16, 16))) // the overland interface's text box frame runs through it (AreaLoop.OverlandLayout)
                    Chrome.BraidCell(f, c, r, true);
        }
        if (_player.TextBoxFramed && Chrome.AmigaFrame is null) // [?] the Amiga camp keeps the view window's (16, 16) corner (FS-UAE s39)
            Chrome.Box(f, 0, 16, 39, 23); // START 0x16EC's camp branch drew the box's frame again (D s33: the joint goes)

        DrawView(f, font);
        DrawRoster(f, font);

        if (_player.Interface is not (Venue.Loot or Venue.Shop) && !_player.PositionLineErased) // START 0x3379 cases 1 and 6 draw no position line
            Chrome.Text(f, font, 17, StatusRow, ShownStatusLine(), Chrome.Green);
        if (pending is ChoiceRequest choice)
        {
            Chrome.Text(f, font, 17, 12, Fit(ChoiceHint(choice, input), 22), Chrome.Grey);
            if (choice.PartySelect)
                Chrome.Text(f, font, 17, 13, Fit(MemberHint(input), 22), Chrome.Grey);
        }

        switch (pending)
        {
            case ChoiceRequest { Vertical: true } v:
                DrawVertical(f, font, v);
                break;
            case TextRequest t:
                if (GridOffText(t))
                    DrawTextWindow(f, font); // the QoL option: the script's text stays, the grid goes above it
                DrawTextEntry(f, font, t);
                break;
            default:
                DrawTextWindow(f, font);
                if (pending is ChoiceRequest { Prompt: { } prompt })
                    Chrome.Text(f, font, 1, TextRow + TextRows - 1, Fit(prompt, 38), Chrome.White);
                if (pending is null && _player.Text.IsEmpty && _player.Notice is null)
                    DrawHelp(f, font, input, OverlandUi.Commands(_game));
                break;
        }
        if (_player.Notice is { } notice)
        {
            Chrome.Clear(f, 1, TextRow, 38);
            Chrome.Text(f, font, 1, TextRow, Fit(notice, 38), Chrome.Yellow);
        }

        if (pending is ChoiceRequest { Vertical: false } h)
        {
            int start = MenuStart(h);
            if (h.MenuPrompt is { Length: > 0 } mp)
                Chrome.MenuPrompt(f, font, 0, MenuRow, mp, h.MenuPromptColour ?? MenuPromptColour);
            DrawLine(f, font, h.Items, _selected, start, h.HotkeyOf, h.Columns, h.ScriptText, Pens(h), h.MenuColours);
            (_keptMenu, _keptSelected, _keptMenuDrawn) = (h, _selected, true);
        }
        else if (pending is DelayRequest { BottomLine: null } or null && _runner.Busy && _player.PagePromptStands)
            // The text after a page prompt is typed with the prompt still on row 24 (its key wait does not blank it).
            DrawKeyLine(f, font, StalePagePrompt, input);
        else if (pending is DelayRequest { BottomLine: null } keptDelay && (keptDelay.KeepCommandLine || _player.Camping)
            && keptMenuWasDrawn && _keptMenu is { } kept && !AmigaView.On)
            // typing keeps the row; in the camp a message's pause does too (D s33: the rest-time menu under the revive text).
            // The Amiga's menu routine blanks row 24 on every way out (hunk 19 +0x5372, +0x5560, +0x55DE, +0x564A, +0x56A6:
            // columns 0-39 of row 24 through hunk 25 +0x197A; FS-UAE s44: the row blank under the rest's interruption line).
        {
            if (kept.MenuPrompt is { Length: > 0 } kp)
                Chrome.MenuPrompt(f, font, 0, MenuRow, kp, kept.MenuPromptColour ?? MenuPromptColour);
            DrawLine(f, font, kept.Items, _keptSelected, MenuStart(kept), kept.HotkeyOf, kept.Columns, kept.ScriptText, Pens(kept), kept.MenuColours);
            _keptMenuDrawn = true;
        }
        else if (pending is null || pending is DelayRequest { BottomLine: null } delay
            && (_player.Scene is not null || (delay.KeepCommandLine && commandLineWasDrawn)))
        {
            // The end game (PROGRAM 8) does not draw on row 24: the command line stays (DOSBox-X s22).
            DrawLine(f, font, Commands, _highlight.Start(Commands.Length), 0);
            _commandLineDrawn = true;
        }
        else if (pending is TextRequest tr)
        {
            // Under the Amiga every line input is its input requester (hunk 25 +0xEC6: the area script's INPUTSTR at
            // hunk 16 +0x2486 and INPUTNUM through +0x1092 with an empty prompt, the amounts with theirs; s43).
            if (AmigaView.On && Chrome.AmigaGadget is { } gadget)
                gadget.Requester(f, font, AmigaView.Case(tr.Prompt), _typed, tr.MaxLength + 1);
            else
                DrawTypedLine(f, font, tr);
        }
        else if (pending is KeyRequest k)
            DrawKeyLine(f, font, k, input);
        else if (pending is DelayRequest { BottomLine: not null } bottomDelay)
            DrawBottomLine(f, font, bottomDelay);

        if (pending is null && !_runner.Busy)
            _player.BottomRowDrawn(); // the run ended: the command line is on row 24 again
        // PROTECT's glyphs outside the text box and the picture window (frame rows 16 / 23, row 24; P s30): drawn over
        // whatever is there, a glyph cell whole (black behind it), until something draws there again. The Amiga rune
        // font (Best / Amiga) draws its set bits only (JAM1, hunk 25 +0xBFC).
        foreach (var (c, r, g) in _player.ScreenGlyphs)
        {
            if (_art.Runes?.Draw(f, c, r, g) == true)
                continue;
            Chrome.Clear(f, c, r, 1);
            Chrome.Text(f, font, c, r, g.ToString(), Chrome.White);
        }
    }

    /// <summary>
    /// A party menu Load's vector 4 before START 0x3379 (<see cref="ClientPlayer.PartyMenuUnder"/>, DOSBox-X s42): the
    /// party menu's page stays, the picture goes at cell (3, 3) with no view box, the text box's text over the page, and
    /// row 24 holds the menu or key prompt asked (blank otherwise).
    /// </summary>
    private void DrawOverPartyMenu(IndexedFrame f, TextFont font, byte[] page, PlayerRequest? pending, ActionInput input)
    {
        f.Clear(0);
        Array.Copy(page, f.Pixels, page.Length);
        if (ShownPicture is { } picture && !_player.PictureUndrawn)
            _art.Pictures.Draw(f, ViewX, ViewY, picture, _anim.Frame);
        DrawTextWindow(f, font);
        switch (pending)
        {
            case ChoiceRequest { Vertical: false } h:
                if (h.MenuPrompt is { Length: > 0 } mp)
                    Chrome.MenuPrompt(f, font, 0, MenuRow, mp, h.MenuPromptColour ?? MenuPromptColour);
                DrawLine(f, font, h.Items, _selected, MenuStart(h), h.HotkeyOf, h.Columns, h.ScriptText, Pens(h), h.MenuColours);
                break;
            case KeyRequest k:
                DrawKeyLine(f, font, k, input);
                break;
            case DelayRequest { BottomLine: not null } d:
                DrawBottomLine(f, font, d);
                break;
            case ChoiceRequest { Vertical: true } v: // VMENU over the page (DOSBox-X s43: its rows in the text box)
                DrawVertical(f, font, v);
                break;
            case TextRequest t:
                DrawTextEntry(f, font, t);
                if (AmigaView.On && Chrome.AmigaGadget is { } gadget)
                    gadget.Requester(f, font, AmigaView.Case(t.Prompt), _typed, t.MaxLength + 1);
                else
                    DrawTypedLine(f, font, t);
                break;
        }
    }

    /// <summary>
    /// A list page left under the text box (<see cref="ClientPlayer.PageUnder"/>; the camp's Cast, 0x17D28 / 0x17D39,
    /// P s30 and DOSBox-X s29): the page as it was, the box's frame (one cell outside (1, 17)–(38, 22): rows 16 and 23,
    /// columns 0 and 39) over it with the inside blanked, the box's text, and on row 24 the list's menu line unless
    /// something else is asked there.
    /// </summary>
    private void DrawOverPage(IndexedFrame f, TextFont font, ListRequest under, PlayerRequest? pending, ActionInput input)
    {
        _list.Draw(f, font, under, input, highlight: false);
        if (_player.PageUnderBox)
        {
            for (int r = TextWindow.Top; r <= TextWindow.Bottom; r++)
                Chrome.Clear(f, TextWindow.Left, r, TextWindow.Right - TextWindow.Left + 1);
            Chrome.Box(f, TextWindow.Left - 1, TextWindow.Top - 1, TextWindow.Right + 1, TextWindow.Bottom + 1);
            switch (pending)
            {
                case ChoiceRequest { Vertical: true } v:
                    DrawVertical(f, font, v);
                    break;
                default:
                    DrawTextWindow(f, font);
                    break;
            }
        }
        if (pending is ChoiceRequest { Vertical: false } h)
        {
            Chrome.Clear(f, 0, MenuRow, 40);
            int start = MenuStart(h);
            if (h.MenuPrompt is { Length: > 0 } mp)
                Chrome.MenuPrompt(f, font, 0, MenuRow, mp, h.MenuPromptColour ?? MenuPromptColour);
            DrawLine(f, font, h.Items, _selected, start, h.HotkeyOf, h.Columns, h.ScriptText, Pens(h), h.MenuColours);
        }
        else if (pending is KeyRequest k)
        {
            Chrome.Clear(f, 0, MenuRow, 40);
            DrawKeyLine(f, font, k, input);
        }
        else if (pending is TextRequest t && !_player.PageUnderBox)
        {
            // Take's amount over its coin list (input 0x24F1D, colour 10; DOSBox-X s46): the question in green, a
            // space, the digits typed in white, no cursor; the pick grid for the pad and the mouse below the list.
            Chrome.Clear(f, 0, MenuRow, 40);
            if (AmigaView.On && Chrome.AmigaGadget is { } gadget)
                gadget.Requester(f, font, AmigaView.Case(t.Prompt), _typed, t.MaxLength + 1); // the Amiga's input requester (not seen here in FS-UAE)
            else
            {
                string question = Fit(AmigaView.Case(t.Prompt) + " ", 40);
                Chrome.Text(f, font, 0, MenuRow, question, Chrome.Green);
                Chrome.Text(f, font, question.Length, MenuRow, Fit(_typed, 40 - question.Length), Chrome.White);
            }
            if (input.LastDevice != InputDevice.Keyboard)
                DrawTextEntry(f, font, t);
        }
        else if (pending is DelayRequest { BottomLine: not null } delay)
        {
            Chrome.Clear(f, 0, MenuRow, 40);
            DrawBottomLine(f, font, delay);
        }
    }

    /// <summary>The text window's page prompt as its key wait drew it, left on row 24 (<see cref="ClientPlayer.PagePromptStands"/>).</summary>
    private static readonly KeyRequest StalePagePrompt = new() { PagePrompt = true };

    private void DrawBottomLine(IndexedFrame f, TextFont font, DelayRequest delay)
    {
        if (delay.BottomLine is not { } line)
            return;
        // Under the Amiga pages a timed line's DOS 14 is its caller's pen 24 (hunk 25 +0xE10's callers).
        if (!(AmigaView.On && AmigaView.TimedPen(line.Colour) is int pen && font.DrawAmiga(f, 0, MenuRow * 8, Fit(line.Text, 40), pen)))
            Chrome.Text(f, font, 0, MenuRow, Fit(line.Text, 40), line.Colour);
        _bottomLineDrawn = delay;
    }

    /// <summary>A key wait's line on row 24: at its column (<see cref="AmigaView.KeyColumn"/>), in the Amiga caller's own
    /// pen when it has one (<see cref="AmigaView.KeyPen"/>), else in the key colour.</summary>
    private void DrawKeyLine(IndexedFrame f, TextFont font, KeyRequest k, ActionInput input)
    {
        int col = AmigaView.KeyColumn(k);
        string text = Fit(KeyPrompt(k, input), 40 - col);
        if (AmigaView.KeyPen(k) is int pen && font.DrawAmiga(f, col * 8, MenuRow * 8, text, pen))
            return;
        Chrome.Text(f, font, col, MenuRow, text, KeyColour(k));
    }

    private static byte KeyColour(KeyRequest k) => AmigaView.KeyColour(k) ?? k.Colour ?? (k.AsMenu ? Chrome.White : MenuPromptColour);

    /// <summary>The menu routine's prompt colour and the page prompt's (0x2CC8C / START 0x7C85: 13).</summary>
    private const byte MenuPromptColour = 13;

    /// <summary>The script text window's cells (rows 17-22) as the program's routines left them, in their draw order
    /// (<see cref="Chrome.TextRuns"/>: whole strings and printed glyphs as the Amiga draws them), with the cells they drew
    /// over the frame: column 39, and under DOS's print row 23 and column 0 (TextWindow.PrintDos).</summary>
    private void DrawTextWindow(IndexedFrame f, TextFont font)
    {
        var t = _player.Text;
        foreach (var (r, c) in t.OutsideCells)
            if (r <= TextWindow.Bottom + 1)
                Chrome.Clear(f, c, r, 1); // a glyph cell is drawn whole: over the frame its background is black
        Chrome.TextRuns(f, font, t.Runs(TextWindow.Top, TextWindow.Bottom + 1),
            (c, r, ch) => _art.Runes?.Draw(f, c, r, ch) == true); // PROTECT's glyphs in the Amiga rune font
    }

    /// <summary>A key wait's line: the program's prompt for the keyboard, the button or click for pad and mouse.</summary>
    private string KeyPrompt(KeyRequest k, ActionInput input) => input.LastDevice switch
    {
        InputDevice.Gamepad => $"PRESS {PromptLabels.For(input.Bindings, GameAction.Confirm, InputDevice.Gamepad)} TO CONTINUE",
        InputDevice.Mouse => "CLICK TO CONTINUE",
        _ => AmigaView.KeyText(k) ?? (k.Prompt ?? _player.Resources.PagePrompt ?? "PRESS A KEY").ToUpperInvariant(),
    };

    /// <summary>How to switch the party member on the device used last (the original: Home / End).</summary>
    private static string ChoiceHint(ChoiceRequest c, ActionInput input)
    {
        var d = input.LastDevice;
        if (d == InputDevice.Mouse)
            return c.CancelIndex is null ? "CLICK: CHOOSE" : "CLICK / RIGHT CLICK";
        string L(GameAction a) => PromptLabels.For(input.Bindings, a, d);
        string pick = c.Vertical ? (d == InputDevice.Gamepad ? "DPAD " : "UP/DN ") : "";
        string s = $"{pick}{L(GameAction.Confirm)} OK";
        return c.CancelIndex is null ? s : $"{s} {L(GameAction.Cancel)} BACK";
    }

    private static string MemberHint(ActionInput input) => input.LastDevice switch
    {
        InputDevice.Mouse => "CLICK A NAME: WHO",
        var d => $"{PromptLabels.For(input.Bindings, GameAction.PreviousMember, d)}/{PromptLabels.For(input.Bindings, GameAction.NextMember, d)}: WHO",
    };

    private string? _staleLine, _lineAtCommand;
    private int _positionLineDrawsSeen;

    /// <summary>
    /// The program drew the position line now (START 0x34AB at a member pick 0x173D9 or a CALL $2C90 redraw 0x3A83):
    /// it shows the party's square again, whatever a script switch or a DAMAGE line left.
    /// </summary>
    private void PositionLineDrawn()
    {
        _player.PositionLineErased = false;
        _staleLine = null;
        _lineAtCommand = null;
    }
    private int _scriptAtCommand = int.MinValue;

    /// <summary>
    /// The position line on screen. The program draws it (START 0x34AB) only at fixed points: after a turn or a
    /// member pick in the command routine (GAME.OVR 0x17266, 0x173D9), after the step or its bump (0x17707, 0x17734),
    /// in the camp (0x197F5, rest 0x1804A), the full redraw of interface states 2–4 (START 0x3379; state 1 does not
    /// draw it) and ECL CALL $2C90 when it redraws the view (0x3A83, <see cref="ClientPlayer.ViewRedrawn"/>); read s21. A command whose script switches (an edge exit, NEWECL) skips the
    /// step, so the line keeps the old square until the next command (DOSBox-X s21: walking east off the slums at
    /// 15,4 into script 0 still shows "15,4 E"). Encounters and menus in the new area's entry leave it as well.
    /// </summary>
    private string ShownStatusLine()
    {
        if (_player.PositionLineDraws != _positionLineDrawsSeen)
        {
            _positionLineDrawsSeen = _player.PositionLineDraws;
            PositionLineDrawn();
        }
        if (_staleLine is null && _lineAtCommand is not null && _game.Vm.CurrentScript != _scriptAtCommand)
            _staleLine = _lineAtCommand;
        var line = _staleLine ?? StatusLine(_game, _player.CampingLine);
        if (!_player.PositionLineHeld)
        {
            _heldLine = null;
            return line;
        }
        return _heldLine ??= line;
    }

    /// <summary>The line shown when <see cref="ClientPlayer.PositionLineHeld"/> began.</summary>
    private string? _heldLine;

    /// <summary>
    /// The position line (START.EXE 0x34AB, program read s11): "x,y " only where the area map is allowed ($49FB = 0),
    /// the facing, the time as HH:MM (hours padded to two digits), then " CAMPING" in camp (DS:49F3 = 2) or " SEARCH"
    /// while Search is on ($6DCA bit 0). Drawn at row 15, column 17, in green.
    /// </summary>
    public static string StatusLine(PorGame game, bool camping)
    {
        var m = game.Memory;
        var (px, py) = Overland.StatusPosition(m); // the overland's x, y in modes 2–4 (START 0x34C2)
        string s = m.Read(AreaLoop.NoAreaMap) == 0 ? $"{px},{py} " : "";
        s += Facing.Letter(m.Position.Facing) + " " + GameClock.Format(m);
        if (camping)
            s += " CAMPING";
        else if (game.Loop.SearchOn)
            s += " SEARCH";
        return s;
    }

    /// <summary>
    /// The picture the window last showed, its frame and <see cref="_viewRedraws"/> then: kept while a later one cannot
    /// be drawn (another disk's), until the view is redrawn.
    /// </summary>
    private (ViewPicture Picture, int Frame, int Redraws)? _pictureOnScreen;
    /// <summary>View redraws so far (AreaLoop.Redrawn, on the logic thread).</summary>
    private int _viewRedraws;

    /// <summary>
    /// The overland interface's lone marker over a 3D view (<see cref="AreaLoop.InterfaceMarker"/>), or the one it will
    /// draw before the first command (<see cref="AreaLoop.OverlandInterfacePending"/>: frame c at the party's square).
    /// </summary>
    private (OverlandMarkerDraw Draw, bool Covered)? OverlandMarkerOverView()
    {
        var loop = _game.Loop;
        if (loop.OverlandInterfacePending)
            return (new OverlandMarkerDraw(Overland.Position(_game.Memory), loop.MarkerCounter,
                _game.Memory.Position.Facing), false);
        return loop.InterfaceMarker is { } m ? (m, loop.InterfaceMarkerCovered) : null;
    }

    private void DrawOverlandMarker(IndexedFrame f, OverlandMarkerDraw m)
    {
        if (OverlandUi.Column(_game) is { } column)
            _art.Overland.Art.DrawMarker(f, OverlandUi.ViewX, OverlandUi.ViewY, Overland.MarkerCell(column, m.Square.X, m.Square.Y),
                m.Frame, m.Facing);
    }

    /// <summary>
    /// The overland window blanked with the 3D view's box in it (<see cref="OverlandUi.Boxed"/>), and under START 0x3379's
    /// boxed layout (0x32CA(1): cases 1, 2, 4 and 6; case 3, the overland's, has no box): the camp (case 2, its picture in
    /// the box; DOSBox-X s43, Encamp on the overland at 10,20 of block 26), the shop and temple (case 1, the box empty) and
    /// the loot menu (case 6, the chest in the box): DOSBox-X s44 on the same square, the window blank around the box.
    /// </summary>
    private bool OverlandBoxed => OverlandUi.Boxed(_player) || _player.Interface is Venue.Camp or Venue.Shop or Venue.Loot;

    private void DrawView(IndexedFrame f, TextFont font)
    {
        if (_player.Scene is { } scene)
        {
            HallView.DrawScene(f, scene, _art.Pictures); // the end game (PROGRAM 8) over the view and its box
            return;
        }
        if (OverlandUi.Shown(_game))
        {
            // The overland view (OverlandUi); encounter pictures and the like go over it as over the 3D view
            // (DOSBox-X: a monster sprite at the 3D view's place over the tiles).
            if (OverlandBoxed)
            {
                OverlandUi.Blank(f); // a picture in the 3D view's box over a blank window (below)
                Chrome.Box(f, 2, 2, 14, 14);
            }
            else if (_player.ViewBlanked)
                OverlandUi.Blank(f); // CLEARBOX: cells 1–15 blank, no box (DOSBox-X s41)
            else if (!OverlandUi.Draw(f, _game, _art.Overland))
                Chrome.Text(f, font, 3, 7, "NO OVERLAND", Chrome.Cyan);
        }
        else if (!_game.Loop.InView3D)
        {
            // A mode the redraw 0x2DCF4 draws nothing for (DS:49FA 0 or 5+ with $49E6 = 0), or no map in memory (a game
            // the Client entered without one; a load always has the map buffer, PorGame.Loaded).
            Chrome.Text(f, font, 3, 7, "NO VIEW", Chrome.Cyan);
            return;
        }
        else if (OverlandUi.Commands(_game) && OverlandMarkerOverView() is { } marker)
        {
            // The overland interface with $49E6 != 0: the 3D view from the map buffer and the interface's lone marker
            // over it; a later 3D redraw covers the marker's inner part (AreaLoop.InterfaceMarker, DOSBox-X s43).
            if (marker.Covered)
                DrawOverlandMarker(f, marker.Draw);
            _game.DrawView(f, ViewX, ViewY, _game.Loop.ShowAreaMap);
            if (!marker.Covered)
                DrawOverlandMarker(f, marker.Draw);
        }
        else
            _game.DrawView(f, ViewX, ViewY, _game.Loop.ShowAreaMap);
        if (ShownPicture is null || _player.ViewBlanked || _player.Interface == Venue.Shop)
            _pictureOnScreen = null;
        if (_player.ViewBlanked && OverlandUi.Shown(_game))
        {
            // CLEARBOX blanked cells 1–15 (OverlandUi.Blank above)
        }
        // Shop/temple (START 0x3379 case 1): the view box blanked, a PICTURE's PIC not shown, also before CLRMON
        // (DOSBox-X s22) and with the picture still armed (no CLRMON; DOSBox-X s24: empty on entry and after View →
        // Exit, the 3D view back on leaving); only the loader's last head + body (ClientPlayer.VenuePortrait).
        else if (_player.Interface == Venue.Shop && _player.VenuePictureOver && ShownPicture is { } venuePicture
            && _art.Pictures.Draw(f, ViewX, ViewY, venuePicture, _anim.Frame))
        {
            // The first menu after a Save or a Load drew the loaded frames in the box ($4AFF; ClientPlayer.VenuePictureOver).
        }
        else if (_player.Interface == Venue.Shop)
        {
            for (int y = 0; y < 88; y++)
                for (int x = 0; x < 88; x++)
                    f[ViewX + x, ViewY + y] = 0;
            // ... and the last head + body given to the loader (DS:49FD / 49FE), the parts the files have.
            var (head, body) = _player.VenuePortrait;
            _art.Pictures.DrawVenuePortrait(f, ViewX, ViewY, head, body, _game.Memory.Disk);
        }
        else if (_player.ViewBlanked)
        {
            // CLEARBOX blanks cells (1, 1)–(15, 15) (START 0x7E63 from 0x386D), the view's own box too; PROTECT's runes
            // go over it (ClientPlayer.DrawGlyphs; glyph cells drawn whole).
            for (int r = 1; r <= 15; r++)
                Chrome.Clear(f, 1, r, 15);
            foreach (var (c, r, g) in _player.ViewGlyphs)
                if (_art.Runes?.Draw(f, c, r, g) != true)
                    Chrome.Text(f, font, c, r, g.ToString(), Chrome.White);
        }
        else if (_player.PictureUndrawn && !_player.Camping)
        {
            // START 0x3379 case 2 loaded the camp's frames without drawing them: the window stays blank until a menu
            // animates them (ClientPlayer.PictureUndrawn; DOSBox-X s41).
            for (int y = 0; y < 88; y++)
                for (int x = 0; x < 88; x++)
                    f[ViewX + x, ViewY + y] = 0;
        }
        else if (ShownPicture is { } picture && _art.Pictures.Draw(f, ViewX, ViewY, picture, _anim.Frame))
        {
            // the encounter picture, portrait or sprite in the chosen art (ViewPictures)
            _pictureOnScreen = (picture, _anim.Frame, Volatile.Read(ref _viewRedraws));
        }
        else if (ShownPicture is { } elsewhere && _art.Pictures.Exists(elsewhere))
        {
            // On another disk only: the program's loader finds no block and draws nothing, so the window keeps what it
            // showed: the view, or the picture drawn before (DOSBox-X s24: PICTURE 2, then PICTURE 3, which only PIC4
            // holds, leaves picture 2 up; the step's redraw then shows the view). [?] Whether the kept picture animates
            // in a later menu is not checked; it stays on the frame last drawn.
            if (_pictureOnScreen is { } kept && kept.Redraws == Volatile.Read(ref _viewRedraws))
                _art.Pictures.Draw(f, ViewX, ViewY, kept.Picture, kept.Frame);
        }
        else if (_player.ViewOverlay is { } lines)
        {
            for (int y = 0; y < 88; y++)
                for (int x = 0; x < 88; x++)
                    f[ViewX + x, ViewY + y] = 0;
            for (int i = 0; i < lines.Length && i < 11; i++)
                Chrome.Text(f, font, 3, 3 + i, Fit(lines[i], 11), Chrome.Yellow);
        }
        if (_player.Interface is not null || _player.Hall is not null)
            _player.ViewBoxBlank = false; // these screens draw the whole layout again (START 0x3379 → 0x272A)
        else if (_player.ViewBoxBlank && !_player.ViewBlanked && !OverlandUi.Shown(_game))
        {
            // A CLEARBOX blank followed by a view redraw (0x2DCF4 draws the 88 × 88 view alone): the window's cells 1–15
            // around the view, the view's box among them, stay blank (DOSBox-X s39).
            for (int r = 1; r <= 15; r++)
            {
                if (r is >= 3 and <= 13)
                {
                    Chrome.Clear(f, 1, r, 2);
                    Chrome.Clear(f, 14, r, 2);
                }
                else
                    Chrome.Clear(f, 1, r, 15);
            }
        }
    }

    /// <summary>A line of words on the bottom row as the original draws its menus: the highlighted item all white, the others with a white hotkey letter.</summary>
    internal static void DrawLine(IndexedFrame f, TextFont font, IReadOnlyList<string> items, int selected, int start,
        Func<int, char>? hotkey = null, IReadOnlyList<int>? columns = null, bool scriptText = false, AmigaMenuPens? amigaPens = null,
        (byte Words, byte Lit)? colours = null)
    {
        // The menu routine's word colours (ChoiceRequest.MenuColours): the words, the lit word and the hotkey letters.
        var (green, white) = colours is var (cw, cl) ? (cw, cl) : (Chrome.Green, Chrome.White);
        // Under Amiga the current entry is not lit: it is drawn as the others, then its gadget box complemented.
        var gadget = Chrome.AmigaGadget;
        int lit = selected;
        if (gadget is not null)
            selected = -1;
        // The Amiga menu routine draws the program's words as they are, each capital or digit in the hotkey pen and every
        // other character in the words' pen (hunk 19 +0x4FF6–0x5048). Script menus (upper case on both machines) keep
        // the DOS look [?] (not seen in FS-UAE).
        bool amiga = AmigaView.On && !scriptText;
        if (amiga)
            items = AmigaView.MenuCase(items);
        int col = start;
        for (int i = 0; i < items.Count; i++)
        {
            var w = items[i];
            if (columns is not null && i < columns.Count)
                col = start + columns[i];
            if (col + w.Length > 40)
                break;
            if (amiga)
            {
                for (int j = 0; j < w.Length; j++)
                    AmigaMenuCharacter(f, font, col + j, w[j], amigaPens);
                if (i == lit)
                    gadget?.Entry(f, col, MenuRow, w);
                col += w.Length + 1;
                continue;
            }
            int k = hotkey is null ? 0 : Math.Max(0, w.IndexOf(hotkey(i)));
            Chrome.Text(f, font, col, MenuRow, w, i == selected ? white : green);
            // The menu routine's entry runs from its capital (or digit) to two cells before the next one (0x2C99A, drawn
            // by 0x2CAA9; D s38, the rest-time menu "Rest   daYs": daYs lit shows "YS" white, Rest lit also lights the D;
            // D s39, the icon editor's "Parts color-1": Parts lit also lights COLOR). Every other line starts each
            // word with its hotkey, where this is the whole word.
            if (k > 0)
            {
                Chrome.Text(f, font, col, MenuRow, w[..k], green);
                if (i > 0 && i - 1 == selected && k > 1)
                    Chrome.Text(f, font, col, MenuRow, w[..(k - 1)], white);
            }
            Chrome.Text(f, font, col + k, MenuRow, w.Substring(k, 1), white);
            if (i == lit)
                gadget?.Entry(f, col, MenuRow, w);
            col += w.Length + 1;
        }
    }

    /// <summary>
    /// The three pens of an Amiga menu call when they are not the usual ones (words 28, capitals 16: DOS colours 10 / 15
    /// through the text pens): creation's portrait menu passes 16 and 28 (hunk 10 +0x3778–0x3790).
    /// </summary>
    internal sealed record AmigaMenuPens(int Words, int Capitals);

    private static AmigaMenuPens? Pens(ChoiceRequest c) => c.AmigaPens is var (w, k) ? new AmigaMenuPens(w, k) : null;

    /// <summary>One character of an Amiga menu line (<see cref="AmigaView.IsHotkeyCharacter"/>).</summary>
    private static void AmigaMenuCharacter(IndexedFrame f, TextFont font, int col, char ch, AmigaMenuPens? pens)
    {
        bool hot = AmigaView.IsHotkeyCharacter(ch);
        if (pens is not null && font.DrawAmiga(f, col * 8, MenuRow * 8, ch.ToString(), hot ? pens.Capitals : pens.Words))
            return;
        Chrome.Text(f, font, col, MenuRow, ch.ToString(), hot ? Chrome.White : Chrome.Green);
    }

    private void DrawVertical(IndexedFrame f, TextFont font, ChoiceRequest c)
    {
        if (c.ListRow is not null)
        {
            // VMENU: the prompt in the text window, the list routine 0x2D669's rows under it (ClientPlayer.VerticalMenu).
            DrawTextWindow(f, font);
            var (first, rows) = ListWindow(c);
            int from = ListTop(c);
            for (int r = 0; r < rows && from + r < c.Items.Count; r++)
            {
                int i = from + r;
                Chrome.Clear(f, TextWindow.Left, first + r, TextWindow.Right - TextWindow.Left + 1);
                string row = Fit(c.Items[i], TextWindow.Right - TextWindow.Left + 1);
                // The lit row: DOS white; the Amiga list routine's bar (hunk 13 +0x2B76 through hunk 4 +0x1A4C, FS-UAE s45).
                if (i == _selected)
                    Chrome.Lit(f, font, TextWindow.Left, first + r, row, Chrome.White, listRow: true);
                else
                    Chrome.Text(f, font, TextWindow.Left, first + r, row, Chrome.Green);
            }
            // Row 24: the menu line " Next" / " Prev" through 0x2CC8C from column 0, so the words from column 1; the row
            // is blanked past the line (DOSBox-X s44).
            var words = ListWords(c).Select(w => w.Word).ToList();
            Chrome.Clear(f, 0, MenuRow, 40);
            DrawLine(f, font, words, ListWord(c), 1, i => words[i].Length > 0 ? words[i][0] : '\0');
            return;
        }
        if (c.Prompt is { Length: > 0 } p)
            Chrome.Text(f, font, 1, TextRow, Fit(p, 38), Chrome.White);
        int top = ListTop(c);
        for (int r = 0; r < ListRows && top + r < c.Items.Count; r++)
        {
            int i = top + r;
            Chrome.Text(f, font, 2, ListFirstRow + r, Fit(c.Items[i], 37), i == _selected ? Chrome.White : Chrome.Green);
            if (i == _selected)
                Chrome.Text(f, font, 1, ListFirstRow + r, ">", Chrome.Yellow);
        }
    }

    /// <summary>
    /// The line input START 0x7A73 on row 24: the prompt at column 0, each typed character at the next column from the
    /// prompt's length + 1 (0x7B0E), so with INPUTSTR's and INPUTNUM's empty prompt from column 1 (DOSBox-X s43). The
    /// "_" after it is the Client's.
    /// </summary>
    private void DrawTypedLine(IndexedFrame f, TextFont font, TextRequest t) =>
        Chrome.Text(f, font, 0, MenuRow, Fit(t.Prompt + " " + _typed + "_", 40), Chrome.White);

    private void DrawTextEntry(IndexedFrame f, TextFont font, TextRequest t)
    {
        if (GridOffText(t))
        {
            // A black panel behind the grid (its heading, the character rows and DEL / OK), one row round, as wide as
            // the party list's name column (the lit name's bar ends there).
            int last = Cell(t, CellCount(t) - 1).Row;
            for (int r = GridTop(t) - 1; r <= last + 1; r++)
                Chrome.Clear(f, 0, r, OffTextColumns);
        }
        Chrome.Text(f, font, 1, GridTop(t), "TYPE, OR PICK BELOW:", Chrome.Grey);
        for (int k = 0; k < CellCount(t); k++)
        {
            var (c, r, label) = Cell(t, k);
            Chrome.Text(f, font, c, r, label, k == _selected ? Chrome.White : Chrome.Green);
            if (k == _selected)
                Chrome.Text(f, font, c - 1, r, ">", Chrome.Yellow);
        }
    }

    /// <summary>
    /// The party list as the program last drew it. LOADCHAR changes the current member without a redraw (START 0x1307
    /// runs at fixed points), and EXIT puts the player's pick (DS:4436) back, so while a script has changed it
    /// (DS:4430) the list still marks the player's pick (DOSBox-X s21: the Tyr temple's party walk, LOADCHAR 0…7 in
    /// ECL3 block 0 $AF56, leaves the first member white while its question waits).
    /// </summary>
    private bool _memberPicked;

    /// <summary>The area script the party list was last drawn in (a switch or a new area draws it again).</summary>
    private int _rosterScript = int.MinValue;

    /// <summary>The party's size when the list was last drawn.</summary>
    private int _rosterCount = -1;

    private void DrawRoster(IndexedFrame f, TextFont font)
    {
        var party = _game.Memory.Party;
        if (party is not { ChangedByScript: true })
            _memberPicked = false;
        int? shown = !_memberPicked && party is { ChangedByScript: true, Selected: { } pick } && party.Members.IndexOf(pick) is >= 0 and var i
            ? i : null;
        // LOADMON appends the monsters to the party list (DS:5D96), but the program draws the list (START 0x1307) only at
        // fixed points, none between LOADMON and the fight: DOSBox-X s29 (a vampire loaded by a step's script) shows the
        // six members through "A battle begins". [?] START 0x1307 is not read: whether a redraw there would list them.
        var monsters = _game.Encounter.State.Monsters;
        // Where the program draws the list while exploring (D s39, GAME.OVR read): an area's entry and a script switch
        // (LOADFILES + LOADPIECES 0x1B27 or the loop's 0x40BE, the switch's end 0x402B), a member picked with Home / End
        // (0x23819 then 0x0767), DAMAGE (0x96DD), the camp's entry and the full-screen pages. Not a step (0x17438), a
        // turn or the area map (0x2EEF4), the view's rebuild (0x2DCF4) or text and pictures: a held name stays.
        // A change of the party's size: every path that adds or removes a member draws the list (LOADCHAR's NPC removal
        // 0x10E7, ADDNPC 0x3940, the camp's Drop 0x19234).
        if (_game.Vm.CurrentScript != _rosterScript || (party?.Members.Count ?? 0) != _rosterCount)
        {
            _rosterScript = _game.Vm.CurrentScript;
            _rosterCount = party?.Members.Count ?? 0;
            _player.ReleaseRosterNames();
        }
        // LOADCHAR changes the current member without a redraw (the list keeps the player's pick): only the pick shown
        // counts.
        _player.ReleaseRosterNamesIfPicked(shown is int s ? party!.Members[s] : party?.Current);
        // START's member pick drew the list with its pick lit (or none); it stays so until the next redraw.
        if (_player.RosterLitOverride(out var lit) && party is not null)
            shown = lit is null ? -1 : party.Members.IndexOf(lit);
        Chrome.PartyList(f, font, party, 17, shown, monsters.Count > 0 ? monsters.Contains : null, _player.RosterNameHeld,
            _player.RosterAsDrawn);
    }

    /// <summary>Two help lines in the text window, in the device used last (keys, pad buttons, or mouse gestures).</summary>
    private static void DrawHelp(IndexedFrame f, TextFont font, ActionInput input, bool overland = false)
    {
        var d = input.LastDevice;
        var b = input.Bindings;
        string L(GameAction a) => PromptLabels.For(b, a, d);
        string move;
        if (d == InputDevice.Mouse)
            move = "MOVE: CLICK THE VIEW";
        else
        {
            var four = new[] { L(GameAction.Forward), L(GameAction.TurnLeft), L(GameAction.TurnRight), L(GameAction.TurnAround) };
            move = four.SequenceEqual(["UP", "LEFT", "RIGHT", "DOWN"]) ? "MOVE: ARROW KEYS"
                : four.SequenceEqual(["DPAD UP", "DPAD LEFT", "DPAD RIGHT", "DPAD DOWN"]) ? "MOVE: DPAD OR LEFT STICK"
                : "MOVE: " + string.Join("/", four);
        }
        string area = d == InputDevice.Mouse ? "CLICK AREA" : L(GameAction.AreaMap);
        string controls = d == InputDevice.Mouse ? "CLICK HERE" : L(GameAction.Controls);
        Chrome.Text(f, font, 1, HelpRow - 1, Fit(overland ? move.Replace("MOVE", "TRAVEL", StringComparison.Ordinal)
            : move + "  MAP: " + area, 38), Chrome.Grey);
        string cmds = d == InputDevice.Mouse
            ? "COMMANDS: CLICK THE BOTTOM LINE"
            : $"SEARCH {L(GameAction.Search)} LOOK {L(GameAction.Look)} CAMP {L(GameAction.Encamp)}";
        Chrome.Text(f, font, 1, HelpRow, Fit(cmds, 38), Chrome.Grey);
        string viewCast = d == InputDevice.Mouse ? "" : $"  VIEW {L(GameAction.View)} CAST {L(GameAction.Cast)}";
        Chrome.Text(f, font, 1, HelpRow + 1, Fit("CONTROLS: " + controls + viewCast, 38), Chrome.Grey);
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
