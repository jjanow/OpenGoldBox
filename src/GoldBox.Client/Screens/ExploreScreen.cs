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
    /// </summary>
    private readonly MenuHighlight _highlight = new();

    // State of the request on screen.
    private PlayerRequest? _shown;
    private int _selected;
    private string _typed = "";
    private TimeSpan _delayEnd;
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
        _player.Camping ? new ViewPicture(ViewPictureKind.Picture, CampPicture, null, 0, _game.Memory.Disk) : _player.ViewPicture;

    /// <summary>The combat screen this screen hands over to during a fight.</summary>
    public CombatScreen Combat => _combat;

    public ExploreScreen(PorGame game, ClientPlayer player, ScriptRunner runner, IScreenHost host, char lastLetter = 'A',
        ClientArt? art = null)
    {
        _highlight.Index = LoadMenuIndex(game.Content.SaveFolder, char.ToUpperInvariant(lastLetter));
        _game = game;
        _player = player;
        _runner = runner;
        _host = host;
        _art = art ?? new ClientArt(game.Content, null, new ScreenPalette(), Data.Por.PorArtChoice.Dos);
        _combat = new CombatScreen(player.FightView, _art.Combat, game.Art.Font, runner, player.Chooser.Text,
            player.Resources.ItemWord)
        {
            Report = Report,
            Highlight = _highlight,
        };
        _commands = game.Art.Program.ExploreCommands is { Count: > 0 } c
            ? [.. c.Select(w => w.ToUpperInvariant())]
            : ["AREA", "CAST", "VIEW", "ENCAMP", "SEARCH", "LOOK"];
    }

    public PorGame Game => _game;

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
        _player.ViewBlanked = false; // [?] CLEARBOX's blank picture window lasts until the next command's redraw
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
        _player.PictureArmed = true; // the area loop's pass sets DS:84E1 (0x4223)
        if (command == AreaCommand.Forward)
            _player.Text.Clear(); // [?] when the original clears the text window is not read; we clear on a step
        _runner.Start(() => _game.Loop.Execute(command));
        if (_runner.Fault is { } e)
            Report(e);
        AfterCommand();
    }

    /// <summary>The area loop ended (party destroyed, DS:49FF): the main menu would follow; not built yet.</summary>
    private void AfterCommand()
    {
        if (_game.Loop.Ended)
            _player.Notice = "THE PARTY IS DESTROYED. RESTART THE GAME.";
        else if (_player.Halted is { } halt)
            _player.Notice = ClientPlayer.HaltNotice(halt);
    }

    private void Report(Exception e)
    {
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
    /// lists the letters of the SAVGAM?.DAT files found, A to J). 0 when unknown.
    /// </summary>
    public static int LoadMenuIndex(string? saveFolder, char slot)
    {
        if (saveFolder is null || !Directory.Exists(saveFolder))
            return 0;
        var letters = Directory.EnumerateFiles(saveFolder)
            .Select(Path.GetFileName)
            .Where(n => n is { Length: 11 } && n.StartsWith("SAVGAM", StringComparison.OrdinalIgnoreCase) &&
                        n.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase))
            .Select(n => char.ToUpperInvariant(n![6]))
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
                    _hasClickAnswer = true;
                }
                return null;
            case ListRequest l:
                return _list.Click(l, click);
            case ChoiceRequest c:
                if (click.Right)
                    return c.CancelIndex is not null ? GameAction.Cancel : null;
                if (ItemAt(c, click.X / 8, click.Y / 8) is { } i)
                {
                    _selected = i;
                    return GameAction.Confirm;
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

    private int ListTop(ChoiceRequest c) => Math.Clamp(_selected - ListRows + 1, 0, Math.Max(0, c.Items.Count - ListRows));

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
            return row == MenuRow ? WordAt(c.Items, col - MenuStart(c)) : null;
        int top = ListTop(c);
        int i = top + row - ListFirstRow;
        return row >= ListFirstRow && row < ListFirstRow + ListRows && i < c.Items.Count && col >= 1 ? i : null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Text entry: typing on the keyboard, or a character grid for pad and mouse
    // ---------------------------------------------------------------------------------------------------------------

    private const string Letters = "ABCDEFGHIJKLM" + "NOPQRSTUVWXYZ" + "0123456789 -.";
    private const string Digits = "0123456789";
    private const int GridCols = 13, GridFirstRow = TextRow + 1;

    private static string Charset(TextRequest t) => t.Numeric ? Digits : Letters;

    /// <summary>Grid cells: the characters, then DEL and OK. Cell k is at (column, row).</summary>
    private static (int Col, int Row, string Label) Cell(TextRequest t, int k)
    {
        var cs = Charset(t);
        if (k < cs.Length)
            return (2 + 2 * (k % GridCols), GridFirstRow + k / GridCols, cs[k] == ' ' ? "_" : cs[k].ToString());
        int lastRow = GridFirstRow + (cs.Length + GridCols - 1) / GridCols;
        return k == cs.Length ? (2, lastRow, "DEL") : (8, lastRow, "OK");
    }

    private static int CellCount(TextRequest t) => Charset(t).Length + 2;

    private static int? CellAt(TextRequest t, int col, int row)
    {
        for (int k = 0; k < CellCount(t); k++)
        {
            var (c, r, label) = Cell(t, k);
            if (row == r && col >= c && col < c + label.Length)
                return k;
        }
        return null;
    }

    private void Backspace()
    {
        if (_typed.Length > 0)
            _typed = _typed[..^1];
    }

    private void Type(TextRequest t, char ch)
    {
        if (_typed.Length < t.MaxLength && Charset(t).Contains(ch, StringComparison.Ordinal))
            _typed += ch;
    }

    private static char? KeyChar(string name) => name switch
    {
        { Length: 1 } when char.IsAsciiLetterUpper(name[0]) => name[0],
        { Length: 2 } when name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1],
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 => name[6],
        "Space" => ' ',
        "OemMinus" or "Subtract" => '-',
        "OemPeriod" or "Decimal" => '.',
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------------------------------------

    public void Update(ActionInput input)
    {
        if (input.Pressed(GameAction.Controls))
        {
            _host.Push(new ControlsScreen(_game.Art.Font, _host, _art));
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
        if (_runner.Pending is ChoiceRequest { Animates: true } && shown is not null)
            _anim.Tick(_clock.Elapsed, _art.Pictures.Delays(shown));
        if (_runner.Pending is { } request)
        {
            var reply = Answer(request, input);
            FollowHighlight(request, reply, input.Pressed(GameAction.Cancel));
            if (reply is { } answer)
            {
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
            else if (OverlandUi.Travel(input) is { } travel) Run(travel);
            return;
        }
        if (input.Pressed(GameAction.AreaMap)) Run(AreaCommand.AreaMap);
        else if (input.Pressed(GameAction.Search)) Run(AreaCommand.Search);
        else if (input.Pressed(GameAction.Look)) Run(AreaCommand.Look);
        else if (input.Pressed(GameAction.Encamp)) Run(AreaCommand.Encamp);
        else if (input.Pressed(GameAction.TurnLeft)) Run(AreaCommand.TurnLeft);
        else if (input.Pressed(GameAction.TurnRight)) Run(AreaCommand.TurnRight);
        else if (input.Pressed(GameAction.TurnAround)) Run(AreaCommand.TurnAround);
        else if (input.Pressed(GameAction.Forward)) Run(AreaCommand.Forward);
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
                break;
            case ListRequest l:
                _list.Sync(l);
                if (l.SharedHighlight)
                    _list.SetAction(l, _highlight.Start(l.Actions.Count));
                else
                    _highlight.Index = _list.SelectedAction;
                break;
        }

        if (request is DelayRequest d)
            _delayEnd = _clock.Elapsed + d.Duration;
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
            case ChoiceRequest { Vertical: false } h:
                _highlight.Index = !cancelled && reply is { Value: int i } && i >= 0 && i < h.Items.Count ? i : _selected;
                break;
            case ListRequest l:
                _highlight.Index = !cancelled && reply is { Value: ListAnswer { Action: var a } } && a >= 0 && a < l.Actions.Count
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
                return _hall.Answer(hm, input) is { } ha ? new Reply(ha) : null;

            case ListRequest l:
                return _list.Update(l, input) is { } a ? new Reply(a) : null;

            case DelayRequest { ReportInput: true }:
                if (input.NewInput is not null || input.Clicks.Count > 0 || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel))
                    return new Reply(true);
                return _clock.Elapsed >= _delayEnd ? new Reply(false) : null;

            case DelayRequest:
                return _clock.Elapsed >= _delayEnd || input.Pressed(GameAction.Confirm) ? new Reply(null) : null;

            case ChoiceRequest c:
            {
                int n = c.Items.Count;
                bool back = input.Pressed(c.Vertical ? GameAction.MenuUp : GameAction.MenuLeft);
                bool fwd = input.Pressed(c.Vertical ? GameAction.MenuDown : GameAction.MenuRight);
                if (back) _selected = (_selected + n - 1) % n;
                if (fwd) _selected = (_selected + 1) % n;
                if (input.Pressed(GameAction.Confirm))
                    return new Reply(_selected);
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
                            return new Reply(i);
                return null;
            }

            case TextRequest t:
            {
                int cells = CellCount(t);
                if (input.NewInput is (InputDevice.Keyboard, var name))
                {
                    if (name == "Enter")
                        return new Reply(_typed);
                    if (name == "Back")
                    {
                        Backspace();
                        return null;
                    }
                    if (KeyChar(name) is { } ch)
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

    public void Draw(IndexedFrame f, ActionInput input)
    {
        if (_combat.Active)
        {
            _combat.Draw(f, input);
            return;
        }
        var font = _game.Art.Font;
        var pending = _runner.Pending;
        if (pending is ListRequest page)
        {
            _list.Draw(f, font, page, input);
            return;
        }
        if (_player.Hall is { } hall)
        {
            _hall.Draw(f, font, hall, _game.Memory.Party, pending, _selected, input);
            return;
        }
        f.Clear(0);
        // Frame cells as on the original screen: outer box, the divider under the view and party list, the divider
        // between view and party list, the view's own box, the box over the command line.
        Chrome.Box(f, 0, 0, 39, 23);
        Chrome.HLine(f, 16, 1, 38);
        Chrome.VLine(f, 16, 1, 15);
        bool overlandBoxed = OverlandUi.Shown(_game) && OverlandUi.Boxed(_player);
        if (!OverlandUi.Shown(_game) || overlandBoxed)
            Chrome.Box(f, 2, 2, 14, 14); // the 3D view's own box; the overland view fills cells 1–15
        foreach (var (c, r) in new[] { (16, 0), (0, 16), (16, 16), (39, 16) })
            Chrome.BraidCell(f, c, r, true);

        DrawView(f, font);
        DrawRoster(f, font);

        Chrome.Text(f, font, 17, StatusRow, StatusLine(_game, _player.Camping), Chrome.Green);
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
                Chrome.Text(f, font, 0, MenuRow, mp, MenuPromptColour);
            DrawLine(f, font, h.Items, _selected, start, h.HotkeyOf);
        }
        else if (pending is null)
        {
            DrawLine(f, font, Commands, _highlight.Start(Commands.Length), 0);
        }
        else if (pending is TextRequest tr)
            Chrome.Text(f, font, 0, MenuRow, Fit(tr.Prompt + (tr.Prompt.Length > 0 ? " " : "") + _typed + "_", 40), Chrome.White);
        else if (pending is KeyRequest k)
            Chrome.Text(f, font, 0, MenuRow, Fit(KeyPrompt(k, input), 40), MenuPromptColour);
    }

    /// <summary>The menu routine's prompt colour and the page prompt's (0x2CC8C / START 0x7C85: 13).</summary>
    private const byte MenuPromptColour = 13;

    /// <summary>The script text window's cells (rows 17-22) as the program's text routine left them.</summary>
    private void DrawTextWindow(IndexedFrame f, TextFont font)
    {
        var t = _player.Text;
        for (int r = TextWindow.Top; r <= TextWindow.Bottom; r++)
            for (int c = TextWindow.Left; c <= TextWindow.Right; c++)
            {
                var (ch, colour) = t.Cell(r, c);
                if (ch != default)
                    Chrome.Text(f, font, c, r, ch.ToString(), colour);
            }
    }

    /// <summary>A key wait's line: the program's prompt for the keyboard, the button or click for pad and mouse.</summary>
    private string KeyPrompt(KeyRequest k, ActionInput input) => input.LastDevice switch
    {
        InputDevice.Gamepad => $"PRESS {PromptLabels.For(input.Bindings, GameAction.Confirm, InputDevice.Gamepad)} TO CONTINUE",
        InputDevice.Mouse => "CLICK TO CONTINUE",
        _ => (k.Prompt ?? _player.Resources.PagePrompt ?? "PRESS A KEY").ToUpperInvariant(),
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
            if (OverlandUi.Boxed(_player))
            {
                OverlandUi.Blank(f); // a picture in the 3D view's box over a blank window (below)
                Chrome.Box(f, 2, 2, 14, 14);
            }
            else if (!OverlandUi.Draw(f, _game, _art.Overland))
                Chrome.Text(f, font, 3, 7, "NO OVERLAND", Chrome.Cyan);
        }
        else if (!_game.Loop.InView3D)
        {
            // [?] No map in memory and not the overland (DS:49FA 5+, or $49E6 = 0 outside blocks 25–27): nothing drawn.
            Chrome.Text(f, font, 3, 7, "NO VIEW", Chrome.Cyan);
            return;
        }
        else
            _game.DrawView(f, ViewX, ViewY, _game.Loop.ShowAreaMap);
        if (_player.ViewBlanked && OverlandUi.Shown(_game))
        {
            // CLEARBOX blanked cells 1–15 (OverlandUi.Blank above)
        }
        else if (_player.ViewBlanked)
        {
            for (int y = 0; y < 88; y++)
                for (int x = 0; x < 88; x++)
                    f[ViewX + x, ViewY + y] = 0;
        }
        else if (ShownPicture is { } picture && _art.Pictures.Draw(f, ViewX, ViewY, picture, _anim.Frame))
        {
            // the encounter picture, portrait or sprite in the chosen art (ViewPictures)
        }
        else if (_player.ViewOverlay is { } lines)
        {
            for (int y = 0; y < 88; y++)
                for (int x = 0; x < 88; x++)
                    f[ViewX + x, ViewY + y] = 0;
            for (int i = 0; i < lines.Length && i < 11; i++)
                Chrome.Text(f, font, 3, 3 + i, Fit(lines[i], 11), Chrome.Yellow);
        }
    }

    /// <summary>A line of words on the bottom row as the original draws its menus: the highlighted item all white, the others with a white hotkey letter.</summary>
    private static void DrawLine(IndexedFrame f, TextFont font, IReadOnlyList<string> items, int selected, int start,
        Func<int, char>? hotkey = null)
    {
        int col = start;
        for (int i = 0; i < items.Count; i++)
        {
            var w = items[i];
            if (col + w.Length > 40)
                break;
            int k = hotkey is null ? 0 : Math.Max(0, w.IndexOf(hotkey(i)));
            Chrome.Text(f, font, col, MenuRow, w, i == selected ? Chrome.White : Chrome.Green);
            Chrome.Text(f, font, col + k, MenuRow, w.Substring(k, 1), Chrome.White);
            col += w.Length + 1;
        }
    }

    private void DrawVertical(IndexedFrame f, TextFont font, ChoiceRequest c)
    {
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

    private void DrawTextEntry(IndexedFrame f, TextFont font, TextRequest t)
    {
        Chrome.Text(f, font, 1, TextRow, "TYPE, OR PICK BELOW:", Chrome.Grey);
        for (int k = 0; k < CellCount(t); k++)
        {
            var (c, r, label) = Cell(t, k);
            Chrome.Text(f, font, c, r, label, k == _selected ? Chrome.White : Chrome.Green);
            if (k == _selected)
                Chrome.Text(f, font, c - 1, r, ">", Chrome.Yellow);
        }
    }

    private void DrawRoster(IndexedFrame f, TextFont font)
    {
        Chrome.Text(f, font, 17, 2, "NAME", Chrome.White);
        Chrome.TextRight(f, font, 34, 2, "AC", Chrome.White);
        Chrome.TextRight(f, font, 38, 2, "HP", Chrome.White);
        if (_game.Memory.Party is not { } party)
            return;
        int shown = 0;
        for (int i = 0; i < party.Members.Count && shown < 8; i++)
        {
            var m = party.Members[i];
            if (i >= 8)
                break;
            byte colour = i == party.CurrentIndex ? Chrome.White : Chrome.Cyan;
            var name = m.Name.Length > 15 ? m.Name[..15] : m.Name;
            Chrome.Text(f, font, 17, 4 + i, name, colour);
            // [?] AC shown as 60 − record byte 0x111 and HP as record byte 0x11B (PARTYSTR's fields, ecl-format.md);
            // checked only against the six members of save A.
            Chrome.TextRight(f, font, 34, 4 + i, (60 - m[0x111]).ToString(CultureInfo.InvariantCulture), Chrome.Green);
            Chrome.TextRight(f, font, 38, 4 + i, m[0x11B].ToString(CultureInfo.InvariantCulture), Chrome.Green);
            shown++;
        }
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
        Chrome.Text(f, font, 1, HelpRow + 1, Fit("CONTROLS: " + controls, 38), Chrome.Grey);
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
