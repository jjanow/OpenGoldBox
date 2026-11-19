using System.Diagnostics;
using System.Globalization;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Screens;

/// <summary>
/// The combat screen (PoR v1.3 EGA, measured on DOSBox-X screenshots, s13): the map window of 7 × 7 squares of 24
/// pixels at (8, 8), the braid frame around it with a divider at column 22, the status panel in columns 23–38 (name
/// row 1, hit points row 3, AC row 5, weapon row 7, messages from row 10), a status line on row 23 and the menu line on
/// row 24. The program swaps EGA colours 0 and 8 for this screen (the ground and the background show dark grey, the
/// art's colour 8 black: the DUNGCOM tiles match the screen pixel for pixel only that way): the screen's setup
/// (START 0x3729) writes EGA palette registers 0 and 8 through GAME.OVR 0x3081D (EGA mode only). It answers the requests of the fight (<see cref="CombatChooser"/>) with keyboard, mouse or pad.
/// </summary>
public sealed class CombatScreen
{
    public const int MapX = 8, MapY = 8, Square = 24, Squares = CombatView.ViewSquares;
    public const int PanelCol = 23, StatusRow = 23, MenuRow = 24, MessageRow = 10;
    private const byte PromptColour = 13;

    private readonly CombatView _view;
    private readonly CombatArt _art;
    private readonly TextFont _font;
    private readonly ScriptRunner _runner;
    private readonly CombatText _text;
    private readonly Func<int, string?> _itemWord;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ListPage _list = new();

    private PlayerRequest? _shown;
    private int _selected;
    /// <summary>The digits typed into a number request (drop money's amount).</summary>
    private string _typed = "";
    private TimeSpan _delayEnd;
    /// <summary>The pending request's screen has been drawn at least once (a zero wait still shows one frame).</summary>
    private bool _drawn;
    private object? _clickAnswer;
    private bool _hasClickAnswer;

    public CombatScreen(CombatView view, CombatArt art, TextFont font, ScriptRunner runner, CombatText text, Func<int, string?> itemWord)
    {
        _view = view;
        _art = art;
        _font = font;
        _runner = runner;
        _text = text;
        _itemWord = itemWord;
    }

    public bool Active => _view.Active;

    /// <summary>The program's shared menu highlight (DS:6B2B), the same cell the area and camp menus use.</summary>
    public MenuHighlight Highlight { get; init; } = new();

    /// <summary>A fault of the logic, for the caller's report.</summary>
    public Action<Exception>? Report { get; init; }

    // ---------------------------------------------------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The arena square under a frame pixel, or null outside the map window.</summary>
    public (int X, int Y)? SquareAt(int px, int py)
    {
        if (px < MapX || py < MapY || px >= MapX + Squares * Square || py >= MapY + Squares * Square)
            return null;
        return (_view.ViewX + (px - MapX) / Square, _view.ViewY + (py - MapY) / Square);
    }

    /// <summary>The direction 0–7 (0 N, clockwise) from one square to a neighbour, or null when not adjacent.</summary>
    public static int? DirectionTo((int X, int Y) from, (int X, int Y) to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        for (int d = 0; d < 8; d++)
            if (FightMenu.Steps[d] == (dx, dy))
                return d;
        return null;
    }

    private (int X, int Y)? Position(EclCharacter? c) =>
        c is not null && _view.Fight?.Map is CombatMap m && m.OnMap(c) ? m.Position(c) : null;

    // ---------------------------------------------------------------------------------------------------------------
    // Mouse
    // ---------------------------------------------------------------------------------------------------------------

    public GameAction? MapClick(ScreenClick click)
    {
        Sync();
        int col = click.X / 8, row = click.Y / 8;
        switch (_runner.Pending)
        {
            case ListRequest l:
                return _list.Click(l, click);
            case KeyRequest:
                return GameAction.Confirm;
            case DelayRequest:
                return GameAction.Confirm;
            case CombatMoveRequest:
            {
                if (click.Right)
                    return GameAction.Cancel;
                if (SquareAt(click.X, click.Y) is { } sq && Position(_view.Current) is { } me)
                {
                    if (sq == me)
                        return GameAction.Confirm;
                    if (DirectionTo(me, sq) is { } d)
                        Answer(new MoveInput(d));
                }
                return null;
            }
            case CombatCursorRequest cr:
            {
                if (click.Right)
                    return GameAction.Cancel;
                if (SquareAt(click.X, click.Y) is { } sq)
                {
                    // A click on the cursor's square takes it (when offered); another square: the cursor walks there.
                    if (sq == (cr.View.X, cr.View.Y))
                    {
                        if (cr.View.TargetOffered)
                            Answer(ManualInput.Target);
                    }
                    else
                        Answer(new CursorWalk(sq.X, sq.Y));
                    return null;
                }
                if (row == MenuRow && CursorItemAt(cr, col) is { } item)
                    CursorItem(cr, item);
                return null;
            }
            case PageRequest pr:
                return PageClick(pr, click);
            case TextRequest:
                return NumberClick(click);
            case ChoiceRequest c:
                if (click.Right)
                    return c.CancelIndex is not null ? GameAction.Cancel : null;
                if (row == MenuRow && WordAt(c.Items, col - MenuStart(c)) is { } i)
                {
                    _selected = i;
                    return GameAction.Confirm;
                }
                if (row == StatusRow && IsMainMenu(c) && WordAt(AltWords, col - AltWordsStart) is { } alt)
                {
                    Answer(AltAnswers[alt]);
                    return null;
                }
                if (_view.Mode == CombatMode.Aim && SquareAt(click.X, click.Y) is { } at && _view.Fight?.Map?.At(at.X, at.Y) is not null)
                    Answer(new AimPick(at.X, at.Y));
                return null;
            default:
                return null;
        }
    }

    public void Hover(int x, int y)
    {
        Sync();
        switch (_runner.Pending)
        {
            case ListRequest l:
                _list.Hover(l, x / 8, y / 8);
                break;
            case PageRequest pr when _view.Page is { } page:
                if (PageRowAt(page, y / 8) is { } r)
                    page.Selected = r;
                else if (y / 8 == MenuRow && WordAt(pr.Entries, x / 8 - (pr.Prompt?.Length ?? 0)) is { } e)
                    _selected = e;
                break;
            case ChoiceRequest { Vertical: false } c when y / 8 == MenuRow && WordAt(c.Items, x / 8 - MenuStart(c)) is { } i:
                _selected = i;
                break;
        }
    }

    private void Answer(object? value)
    {
        _clickAnswer = value;
        _hasClickAnswer = true;
    }

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

    /// <summary>The menu routine (0x2CC8C) draws the items right after its prompt (DOSBox-X: no space after the aim prompt).</summary>
    private static int MenuStart(ChoiceRequest c) => c.MenuPrompt?.Length ?? 0;

    // ---------------------------------------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------------------------------------

    private void Sync()
    {
        var request = _runner.Pending;
        if (ReferenceEquals(request, _shown))
            return;
        _shown = request;
        // The menu routine 0x2CC8C opens on the shared highlight (MenuHighlight); a request may name its own start.
        _selected = request switch
        {
            ChoiceRequest { Vertical: false } c => c.Selected != 0 ? c.Selected : Highlight.Start(c.Items.Count),
            ChoiceRequest c => c.Selected,
            PageRequest { SharedHighlight: true } pr => Highlight.Start(pr.Entries.Count),
            _ => 0,
        };
        _typed = "";
        _hasClickAnswer = false;
        _drawn = false;
        if (request is ListRequest l)
            _list.Sync(l);
        if (request is DelayRequest d)
        {
            // A chained pause (typing, the moving icon) starts where the last one ended, unless that was longer ago
            // than the pause itself (as ExploreScreen).
            var start = _clock.Elapsed;
            if (d.Chained && _delayEnd <= start && start - _delayEnd < d.Duration)
                start = _delayEnd;
            _delayEnd = start + d.Duration;
        }
    }

    public void Update(ActionInput input)
    {
        Sync();
        if (_runner.Pending is not { } request)
            return;
        // Keys pressed during a pause wait for the next key read (ActionInput.ReadingKeys).
        input.ReadingKeys(request is not DelayRequest { ReportInput: false },
            request is not (KeyRequest { AsMenu: false } or TextRequest));
        object? answer;
        bool answered;
        if (_hasClickAnswer)
        {
            _hasClickAnswer = false;
            (answer, answered) = (_clickAnswer, true);
        }
        else
            answered = Decide(request, input, out answer);
        if (!answered)
            return;
        _runner.Answer(answer);
        if (_runner.Fault is { } e)
            Report?.Invoke(e);
    }

    /// <summary>A direction from the move keys this frame: the four arrows (or the pad's D-pad / stick, two at once =
    /// a diagonal) and the four diagonal actions (keypad Home / PgUp / End / PgDn, the right stick).</summary>
    public static int? Direction(ActionInput input)
    {
        bool up = input.Pressed(GameAction.MenuUp) || input.Pressed(GameAction.Forward);
        bool down = input.Pressed(GameAction.MenuDown) || input.Pressed(GameAction.TurnAround);
        bool left = input.Pressed(GameAction.MenuLeft) || input.Pressed(GameAction.TurnLeft);
        bool right = input.Pressed(GameAction.MenuRight) || input.Pressed(GameAction.TurnRight);
        if (input.Pressed(GameAction.StepUpRight)) return 1;
        if (input.Pressed(GameAction.StepDownRight)) return 3;
        if (input.Pressed(GameAction.StepDownLeft)) return 5;
        if (input.Pressed(GameAction.StepUpLeft)) return 7;
        if (!(up || down || left || right))
            return null;
        // A pad held diagonally gives two of the four at once (or one new and one still held).
        bool hu = up || input.Held(GameAction.MenuUp), hd = down || input.Held(GameAction.MenuDown);
        bool hl = left || input.Held(GameAction.MenuLeft), hr = right || input.Held(GameAction.MenuRight);
        if (hu && hr) return 1;
        if (hd && hr) return 3;
        if (hd && hl) return 5;
        if (hu && hl) return 7;
        if (up) return 0;
        if (right) return 2;
        if (down) return 4;
        return 6;
    }

    private bool Decide(PlayerRequest request, ActionInput input, out object? answer)
    {
        answer = null;
        switch (request)
        {
            case DelayRequest:
                return (_drawn && _clock.Elapsed >= _delayEnd) || input.SkipsPause;
            case KeyRequest:
                return input.NewInput is not null || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel);
            case ListRequest l:
                if (_list.Update(l, input) is { } a)
                {
                    answer = a;
                    return true;
                }
                return false;
            case CombatMoveRequest:
                if (Direction(input) is { } d)
                {
                    answer = new MoveInput(d);
                    return true;
                }
                if (input.Pressed(GameAction.Confirm))
                {
                    answer = MoveInput.Return;
                    return true;
                }
                if (input.Pressed(GameAction.Cancel))
                {
                    answer = MoveInput.Escape;
                    return true;
                }
                return false;
            case CombatCursorRequest cr:
                return DecideCursor(cr, input, out answer);
            case PageRequest pr:
                return DecidePage(pr, input, out answer);
            case TextRequest t:
                return DecideNumber(t, input, out answer);
            case ChoiceRequest c:
                return DecideChoice(c, input, out answer);
            default:
                return true;
        }
    }

    private bool DecideChoice(ChoiceRequest c, ActionInput input, out object? answer)
    {
        answer = null;
        int n = c.Items.Count;
        if (input.Pressed(GameAction.MenuLeft)) _selected = (_selected + n - 1) % n;
        if (input.Pressed(GameAction.MenuRight)) _selected = (_selected + 1) % n;
        if (input.Pressed(GameAction.Confirm))
        {
            answer = _selected;
            Chose(c, _selected);
            return true;
        }
        if (input.Pressed(GameAction.Cancel) && c.CancelIndex is { } ci)
        {
            answer = ci;
            return true;
        }
        // The main menu's Alt keys (0x9FF3–0xA057), accepted whatever entries are shown.
        if (_view.Mode == CombatMode.Menu && c.MenuPrompt is null)
        {
            int? alt = input.Pressed(GameAction.FightQuickMagic) ? CombatChooser.AnswerQuickMagic
                : input.Pressed(GameAction.FightAllQuick) ? CombatChooser.AnswerAllQuick
                : input.Pressed(GameAction.FightAltX) ? CombatChooser.AnswerAltX
                : null;
            if (alt is { } a)
            {
                answer = a;
                return true;
            }
        }
        // Aim: the pad's shoulders (keyboard Home / End) are Prev / Next, the first two entries after the prompt.
        if (_view.Mode == CombatMode.Aim && n >= 2)
        {
            if (input.Pressed(GameAction.NextMember))
            {
                answer = 0;
                Chose(c, 0);
                return true;
            }
            if (input.Pressed(GameAction.PreviousMember))
            {
                answer = 1;
                Chose(c, 1);
                return true;
            }
        }
        // The original's hotkeys: the first letter of an entry.
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < n; i++)
                if (c.Items[i].Length > 0 && c.Items[i][0] == key[0])
                {
                    answer = i;
                    Chose(c, i);
                    return true;
                }
        return false;
    }

    /// <summary>A menu entry was taken: the shared highlight follows it (0x2CF48; Escape leaves it).</summary>
    private void Chose(ChoiceRequest c, int entry)
    {
        if (!c.Vertical)
            Highlight.Chosen(entry);
    }


    // ---- Manual cursor (0x155F5) -------------------------------------------------------------------------------------

    /// <summary>The entries after the prompt: Target (when Core offers it on the cursor's square), Center, Exit.</summary>
    private List<(string Word, int Item)> CursorItems(CombatCursorRequest cr)
    {
        var l = new List<(string, int)>();
        if (cr.View.TargetOffered)
            l.Add((_text[CombatMsg.Target].Trim(), 0));
        var ce = _text.Words(CombatMsg.AimCenterExit);
        l.Add((ce.ElementAtOrDefault(0) ?? "CENTER", 1));
        l.Add((ce.ElementAtOrDefault(1) ?? "EXIT", 2));
        return l;
    }

    private int? CursorItemAt(CombatCursorRequest cr, int col)
    {
        var items = CursorItems(cr);
        int start = _text[CombatMsg.UseCursorKeys].Length;
        return WordAt(items.Select(i => i.Word).ToList(), col - start) is { } k ? items[k].Item : null;
    }

    /// <summary>An entry of the cursor's menu: Target, Center (the view re-centres on the cursor, which stays), Exit.</summary>
    private ManualInput CursorEntry(CombatCursorRequest cr, int item)
    {
        switch (item)
        {
            case 0:
                return ManualInput.Target;
            case 1:
                _view.Center(cr.View.X, cr.View.Y);
                return ManualInput.Center;
            default:
                return ManualInput.Exit;
        }
    }

    private void CursorItem(CombatCursorRequest cr, int item) => Answer(CursorEntry(cr, item));

    /// <summary>
    /// The keys 0x155F5 reads, each one pass of Core's cursor (<see cref="FightMenu.Manual"/>): a direction moves it one
    /// square; Return (Confirm) and T are Target, which ends Manual even where Target is not offered; E and Escape
    /// (Cancel) are Exit; C and the shoulders (Home / End) re-centre the view.
    /// </summary>
    private bool DecideCursor(CombatCursorRequest cr, ActionInput input, out object? answer)
    {
        answer = null;
        if (Direction(input) is { } d)
        {
            answer = new ManualInput(d);
            return true;
        }
        if (input.Pressed(GameAction.Confirm))
        {
            answer = ManualInput.Target;
            return true;
        }
        if (input.Pressed(GameAction.Cancel))
        {
            answer = ManualInput.Exit;
            return true;
        }
        if (input.Pressed(GameAction.NextMember) || input.Pressed(GameAction.PreviousMember))
        {
            answer = CursorEntry(cr, 1);
            return true;
        }
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            foreach (var (word, item) in CursorItems(cr))
                if (word.Length > 0 && word[0] == key[0])
                {
                    answer = CursorEntry(cr, item);
                    return true;
                }
        return false;
    }

    // ---- Character pages (View, Items, drop money) -------------------------------------------------------------------

    /// <summary>The first list row shown (the list scrolls to keep the selected row in its window).</summary>
    private static int PageTop(CombatPage p)
    {
        int visible = p.LastRow - p.FirstRow + 1;
        return Math.Clamp(p.Selected - visible + 1, 0, Math.Max(0, p.Rows.Count - visible));
    }

    private static int? PageRowAt(CombatPage p, int row)
    {
        int i = PageTop(p) + row - p.FirstRow;
        return row >= p.FirstRow && row <= p.LastRow && i >= 0 && i < p.Rows.Count ? i : null;
    }

    private GameAction? PageClick(PageRequest pr, ScreenClick click)
    {
        if (_view.Page is not { } page)
            return null;
        if (click.Right)
            return GameAction.Cancel;
        int col = click.X / 8, row = click.Y / 8;
        if (row == MenuRow && WordAt(pr.Entries, col - (pr.Prompt?.Length ?? 0)) is { } e)
        {
            Answer(new ListAnswer(e, page.Selected));
            if (pr.SharedHighlight)
                Highlight.Chosen(e);
            return null;
        }
        if (PageRowAt(page, row) is { } i)
        {
            // A click on the selected row: the coin list takes it; other pages run the highlighted entry on it.
            if (i == page.Selected)
                Answer(new ListAnswer(pr.PickRows ? PageRequest.RowPicked : _selected, i));
            page.Selected = i;
        }
        return null;
    }

    /// <summary>
    /// The page's menu: rows with Up / Down (and the original's Home / End: the party keys), entries with Left / Right
    /// and their hotkeys, Return / A runs the highlighted entry on the selected row (on the coin list: picks the row),
    /// Escape / B the cancel entry.
    /// </summary>
    private bool DecidePage(PageRequest pr, ActionInput input, out object? answer)
    {
        answer = null;
        if (_view.Page is not { } page)
        {
            answer = new ListAnswer(pr.CancelEntry, 0);
            return true;
        }
        int n = page.Rows.Count, m = pr.Entries.Count;
        if (n > 0 && (input.Pressed(GameAction.MenuUp) || input.Pressed(GameAction.PreviousMember)))
            page.Selected = (page.Selected + n - 1) % n;
        if (n > 0 && (input.Pressed(GameAction.MenuDown) || input.Pressed(GameAction.NextMember)))
            page.Selected = (page.Selected + 1) % n;
        if (m > 0 && input.Pressed(GameAction.MenuLeft)) _selected = (_selected + m - 1) % m;
        if (m > 0 && input.Pressed(GameAction.MenuRight)) _selected = (_selected + 1) % m;
        if (input.Pressed(GameAction.Confirm))
        {
            answer = new ListAnswer(pr.PickRows && n > 0 ? PageRequest.RowPicked : _selected, page.Selected);
            if (pr.SharedHighlight)
                Highlight.Chosen(_selected);
            return true;
        }
        if (input.Pressed(GameAction.Cancel))
        {
            answer = new ListAnswer(pr.CancelEntry, page.Selected);
            return true;
        }
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < m; i++)
                if (pr.Entries[i].Length > 0 && pr.Entries[i][0] == key[0])
                {
                    answer = new ListAnswer(i, page.Selected);
                    if (pr.SharedHighlight)
                        Highlight.Chosen(i);
                    return true;
                }
        return false;
    }

    /// <summary>The number buttons for pad and mouse (row 22): steps, then OK.</summary>
    private static readonly string[] NumberButtons = ["-10", "-1", "+1", "+10", "OK"];
    private const int NumberButtonRow = 22;

    /// <summary>The row of the pad / mouse hint and the number buttons: the page's free row (the View sheet uses 22).</summary>
    private int HintRow => _view.Page?.HintRow ?? NumberButtonRow;

    private static char? Digit(string name) => name switch
    {
        { Length: 2 } when name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1],
        { Length: 7 } when name.StartsWith("NumPad", StringComparison.Ordinal) && char.IsAsciiDigit(name[6]) => name[6],
        _ => null,
    };

    private void Step(TextRequest t, int delta)
    {
        long v = long.TryParse(_typed, NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : 0;
        long max = (long)Math.Pow(10, t.MaxLength) - 1;
        v = Math.Clamp(v + delta, 0, max);
        _typed = v.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A number (drop money's amount; the input routine 0x24F1D is not read [?]): keyboard digits, Backspace, Return,
    /// Escape (nothing); pad Up / Down ±1, Right / Left ±10, A done, B nothing; the mouse uses the buttons on row 22.
    /// </summary>
    private bool DecideNumber(TextRequest t, ActionInput input, out object? answer)
    {
        answer = null;
        if (input.NewInput is (InputDevice.Keyboard, var name))
        {
            if (Digit(name) is { } ch)
            {
                if (_typed.Length < t.MaxLength)
                    _typed = (_typed == "0" ? "" : _typed) + ch;
                return false;
            }
            if (name == "Back")
            {
                if (_typed.Length > 0)
                    _typed = _typed[..^1];
                return false;
            }
        }
        if (input.Pressed(GameAction.Confirm))
        {
            answer = _typed;
            return true;
        }
        if (input.Pressed(GameAction.Cancel))
        {
            answer = "";
            return true;
        }
        if (input.Pressed(GameAction.MenuUp)) Step(t, 1);
        if (input.Pressed(GameAction.MenuDown)) Step(t, -1);
        if (input.Pressed(GameAction.MenuRight)) Step(t, 10);
        if (input.Pressed(GameAction.MenuLeft)) Step(t, -10);
        return false;
    }

    private GameAction? NumberClick(ScreenClick click)
    {
        if (click.Right)
            return GameAction.Cancel;
        if (_runner.Pending is not TextRequest t || click.Y / 8 != HintRow)
            return null;
        switch (WordAt(NumberButtons, click.X / 8 - 1))
        {
            case 0: Step(t, -10); break;
            case 1: Step(t, -1); break;
            case 2: Step(t, 1); break;
            case 3: Step(t, 10); break;
            case 4: Answer(_typed); break;
        }
        return null;
    }

    private void DrawPage(IndexedFrame f, CombatPage p, ActionInput input)
    {
        f.Clear(0);
        int top = p.FrameRows.Min(), bottom = p.FrameRows.Max();
        foreach (var r in p.FrameRows)
            Chrome.HLine(f, r, 0, 39);
        Chrome.VLine(f, 0, top, bottom);
        Chrome.VLine(f, 39, top, bottom);
        foreach (var r in p.FrameRows)
        {
            Chrome.BraidCell(f, 0, r, true);
            Chrome.BraidCell(f, 39, r, true);
        }
        foreach (var (col, row, text, colour) in p.Texts)
            Text(f, col, row, Fit(text, 39 - col), colour);
        int first = PageTop(p);
        for (int k = 0; p.FirstRow + k <= p.LastRow && first + k < p.Rows.Count; k++)
        {
            int i = first + k;
            bool sel = i == p.Selected && p.ShowSelection;
            Text(f, p.RowCol, p.FirstRow + k, Fit(p.Rows[i], 39 - p.RowCol), sel ? Chrome.White : Chrome.Green);
        }
        for (int k = 0; k < p.Warning.Count; k++)
            Text(f, 1, 21 + k, Fit(p.Warning[k], 38), Chrome.Yellow);

        switch (_runner.Pending)
        {
            case PageRequest pr:
                if (pr.Prompt is { Length: > 0 } prompt)
                    Text(f, 0, MenuRow, prompt, PromptColour);
                DrawEntries(f, pr.Entries, pr.Prompt?.Length ?? 0, _selected);
                if (input.LastDevice != InputDevice.Keyboard)
                    Text(f, 1, HintRow, Fit(PageHint(input, p.Rows.Count > 0), 38), Chrome.Grey);
                break;
            case ChoiceRequest { Vertical: false } c:
                if (c.MenuPrompt is { Length: > 0 } mp)
                    Text(f, 0, MenuRow, mp, PromptColour);
                DrawEntries(f, c.Items, MenuStart(c), _selected);
                break;
            case DelayRequest when _view.PageMessage is { } msg:
                Text(f, 0, MenuRow, Fit(msg, 40), Chrome.Green);
                break;
            case TextRequest t:
                Text(f, 0, MenuRow, Fit(t.Prompt + " " + _typed + "_", 40), Chrome.Green);
                if (input.LastDevice != InputDevice.Keyboard)
                {
                    int col = 1;
                    foreach (var b in NumberButtons)
                    {
                        Text(f, col, HintRow, b, Chrome.White);
                        col += b.Length + 1;
                    }
                }
                break;
        }
    }

    private void DrawEntries(IndexedFrame f, IReadOnlyList<string> words, int start, int selected)
    {
        int col = start;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Length == 0 || col + w.Length > 40)
                break;
            Text(f, col, MenuRow, w[..1], Chrome.White);
            Text(f, col + 1, MenuRow, w[1..], i == selected ? Chrome.White : Chrome.Green);
            col += w.Length + 1;
        }
    }

    /// <summary>How to work a page on the pad or with the mouse (the keyboard uses the original's keys).</summary>
    private static string PageHint(ActionInput input, bool rows)
    {
        if (input.LastDevice == InputDevice.Mouse)
            return rows ? "CLICK A ROW, THEN A WORD BELOW" : "CLICK A WORD BELOW";
        var b = input.Bindings;
        string L(GameAction a) => PromptLabels.For(b, a, InputDevice.Gamepad);
        return (rows ? "UP/DOWN ROW " : "") + $"LEFT/RIGHT {L(GameAction.Confirm)} OK {L(GameAction.Cancel)} BACK";
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------------------------------------------------------

    public void Draw(IndexedFrame f, ActionInput input)
    {
        Sync();
        _drawn = true;
        if (_runner.Pending is ListRequest page)
        {
            _list.Draw(f, _font, page, input);
            return;
        }
        if (_view.Page is { } cp)
        {
            DrawPage(f, cp, input);
            if (cp.Grey)
                SwapGrey(f);
            return;
        }
        f.Clear(0);
        DrawFrame(f);
        DrawMap(f);
        DrawPanel(f);
        DrawLines(f, input);
        SwapGrey(f);
    }

    /// <summary>The combat palette: EGA colours 0 and 8 trade places (see the class summary). Exposed for tests.</summary>
    public static void SwapGrey(IndexedFrame f)
    {
        var p = f.Pixels;
        for (int i = 0; i < p.Length; i++)
            p[i] = p[i] switch { 0 => 8, 8 => 0, var v => v };
    }

    private static void DrawFrame(IndexedFrame f)
    {
        // DOSBox-X: braid on rows 0 and 22, columns 0, 22 and 39; yellow corners where they meet.
        Chrome.HLine(f, 0, 0, 39);
        Chrome.HLine(f, 22, 0, 39);
        Chrome.VLine(f, 0, 0, 22);
        Chrome.VLine(f, 22, 0, 22);
        Chrome.VLine(f, 39, 0, 22);
        foreach (var (c, r) in new[] { (0, 0), (22, 0), (39, 0), (0, 22), (22, 22), (39, 22) })
            Chrome.BraidCell(f, c, r, true);
    }

    private void DrawMap(IndexedFrame f)
    {
        var clip = (MapX, MapY, MapX + Squares * Square, MapY + Squares * Square);
        if (_view.Fight?.Map is not CombatMap map)
            return;
        var arena = map.Arena;
        for (int r = 0; r < Squares; r++)
            for (int c = 0; c < Squares; c++)
            {
                int x = _view.ViewX + c, y = _view.ViewY + r;
                if (!CombatArena.InArena(x, y))
                    continue; // [?] what the program draws off the arena is not seen
                if (_art.Ground(map.Tables.Tile(arena[x, y]), _view.Dungeon) is { } tile)
                    CombatArt.Draw(f, tile, MapX + c * Square, MapY + r * Square, false, clip, opaque: true);
            }
        // The box stays painted until its cell or the view is redrawn (CombatView.Cursor follows the program's redraws).
        if (_view.Cursor is { } cur)
            for (int i = 0; i < 4; i++)
                if (map.Tables.IconCell(_view.CursorSize, i, out int dx, out int dy) && _view.InView(cur.X + dx, cur.Y + dy))
                    DrawCursor(f, (cur.X + dx, cur.Y + dy), clip);
        foreach (var c in _view.Fight.List)
        {
            if (!map.OnMap(c))
                continue;
            var (x, y) = map.Position(c);
            if (Icon(c, _view.Attacking.Contains(c)) is not { } pic)
                continue;
            int px = MapX + (x - _view.ViewX) * Square, py = MapY + (y - _view.ViewY) * Square;
            CombatArt.Draw(f, pic, px, py, Mirrored(c), clip);
        }
        if (_view.HitMarker is { } hm && _art.Sprite(CombatArt.HitMarker) is { } skull)
            CombatArt.Draw(f, skull, MapX + (hm.X - _view.ViewX) * Square, MapY + (hm.Y - _view.ViewY) * Square, false, clip);
        if (_view.Flash is { } fl && _art.Sprite(fl.Sprite) is { } flash)
            CombatArt.Draw(f, flash, MapX + (fl.X - _view.ViewX) * Square, MapY + (fl.Y - _view.ViewY) * Square, fl.Mirror, clip);
        // The moving icon (START 0x25C9) at its position in thirds of a square. [?] The drawing routine 0x464:0xD71's
        // own offsets are not read: the view's origin is taken as for the squares.
        if (_view.Flying is { } fy && _art.Sprite(fy.Sprite) is { } flying)
            CombatArt.Draw(f, flying, MapX + fy.Cx * (Square / 3), MapY + fy.Cy * (Square / 3), fy.Mirror, clip);
    }

    /// <summary>
    /// Icons face right in the art; DOSBox-X shows the party (facing 7, NW) mirrored and the goblins (facing 3, SE)
    /// as stored: facings 5–7 are mirrored. s15 (save K, Rhiannon moved): facing 0 (a step north) and 4 (a step south
    /// after facing 6) show the stored icon, facing 6 the mirrored one. The drawing routine itself is not read [?].
    /// </summary>
    private bool Mirrored(EclCharacter c) =>
        _view.Fight is { } fight && fight.Record(c)[CombatRecord.Facing] is >= 5 and <= 7;

    /// <summary>
    /// A creature's icon: a player character (record 0x84 &lt; 0x80, as the menu's space key tells PCs from NPCs) gets
    /// the composed party icon, any other creature the CPIC loaded into its icon slot (record 0xBF). [?] An NPC that
    /// joined (ADDNPC: a slot of 0..7 and a CPIC) is not checked against DOSBox-X.
    /// </summary>
    private CombatPicture? Icon(EclCharacter c, bool attack)
    {
        if (c[CombatRules.Npc] < 0x80)
            return _art.Party(c, attack);
        return _view.IconPictures.TryGetValue(c[0xBF], out int pic) ? _art.Monster(pic, attack) ?? _art.Monster(pic, false) : null;
    }

    /// <summary>The cursor box (DOSBox-X, measured): light grey, white top row and left 2 columns, black right 2 columns and bottom row.</summary>
    private void DrawCursor(IndexedFrame f, (int X, int Y) sq, (int X0, int Y0, int X1, int Y1) clip)
    {
        int x0 = MapX + (sq.X - _view.ViewX) * Square, y0 = MapY + (sq.Y - _view.ViewY) * Square;
        for (int y = 0; y < Square; y++)
            for (int x = 0; x < Square; x++)
            {
                int px = x0 + x, py = y0 + y;
                if (px < clip.X0 || py < clip.Y0 || px >= clip.X1 || py >= clip.Y1)
                    continue;
                byte c;
                if (y == 0)
                    c = x == Square - 1 ? (byte)8 : (byte)15;
                else if (y == Square - 1)
                    c = x == 0 ? (byte)15 : (byte)8;
                else
                    c = x < 2 ? (byte)15 : x >= Square - 2 ? (byte)8 : (byte)7;
                f[px, py] = c; // colour 8 shows black once the combat palette swaps 0 and 8
            }
    }

    /// <summary>
    /// The creature the status panel shows: under the manual cursor the creature Core found on its square (DOSBox-X: an
    /// empty square leaves the panel blank; [?] blank too where the line from the aimer does not reach, as the
    /// program's creature variable is then empty), else the one the fight shows (the current creature, the aim candidate).
    /// </summary>
    public EclCharacter? PanelCreature() => _runner.Pending is CombatCursorRequest cr ? cr.View.Creature : _view.Shown;

    private void DrawPanel(IndexedFrame f)
    {
        if (PanelCreature() is { } c)
        {
            Text(f, PanelCol, 1, Fit(c.Name.ToUpperInvariant(), 16), CombatChooser.NameColour(c));
            Text(f, PanelCol, 3, Fit(_text[CombatMsg.PanelHitpoints].Trim() + " " + c[FightRules.Hp].ToString(CultureInfo.InvariantCulture), 16), Chrome.Green);
            // [?] AC as 60 − record byte 0x111, as the party list (ExploreScreen); DOSBox-X: Darkstar 7, Thrender 1.
            Text(f, PanelCol, 5, Fit(_text[CombatMsg.PanelAc].Trim() + " " + (60 - c[FightRules.Ac]).ToString(CultureInfo.InvariantCulture), 16), Chrome.Green);
            // START 0x1675: the weapon's name (ending in a space, as composed) word-wrapped into (23, 7)–(38, 9) through
            // 0x77F8 (DOSBox-X s19: "TWO-HANDED SWORD " takes two rows).
            if (_view.Fight is { } fight && FightRules.Equipment(c, fight.Items)[FightRules.SlotWeapon] is { } w)
            {
                var box = new TextWindow(PanelCol, 7, 38, 9);
                box.PrintIn(PanelCol, 7, 38, 9, ItemNames.Compose(w, _itemWord), true, Chrome.Green);
                for (int row = 7; row <= 9; row++)
                    for (int col = PanelCol; col <= 38; col++)
                        if (box.Cell(row, col) is ({ } ch and not '\0', var colour))
                            Text(f, col, row, ch.ToString(), colour);
            }
        }
        // The message area: the panel's cells as START 0x16EC / 0x77F8 wrote them.
        var panel = _view.Panel;
        for (int row = 1; row <= CombatView.PanelBottom; row++)
            for (int col = CombatView.PanelLeft; col <= CombatView.PanelRight; col++)
                if (panel.Cell(row, col) is ({ } ch and not '\0', var colour))
                    Text(f, col, row, ch.ToString(), colour);
    }

    private void DrawLines(IndexedFrame f, ActionInput input)
    {
        // Row 23: Aim's range line, and while watching the cast's "Spell:" line or an item use's "Item:" line.
        if (_view.StatusLine is { } s && _view.Mode is CombatMode.Aim or CombatMode.Manual or CombatMode.Watch)
            Text(f, 0, StatusRow, Fit(s, 40), Chrome.Green);
        // An AI creature's move prompt (AiMoveStep) stays on row 24 while nothing else is asked there.
        if (_view.MovePrompt is { } mp0 && _runner.Pending is null or DelayRequest)
            Text(f, 0, MenuRow, Fit(mp0, 40), Chrome.Green);
        switch (_runner.Pending)
        {
            case DelayRequest when _view.PageMessage is { } msg:
                // The message routine START 0x1693 over the map (Alt-M, Alt-X): row 24 in green during its pause.
                Text(f, 0, MenuRow, Fit(msg, 40), Chrome.Green);
                break;
            case CombatMoveRequest m:
                // DOSBox-X: Move's prompt (0xA1BB) and the moves left in green on the menu row.
                Text(f, 0, MenuRow, Fit(_text[CombatMsg.MoveLeft] + m.MovementLeft.ToString(CultureInfo.InvariantCulture), 40), Chrome.Green);
                if (input.LastDevice != InputDevice.Keyboard)
                    Text(f, 0, StatusRow, Fit(MoveHint(input), 40), Chrome.Grey);
                break;
            case CombatCursorRequest cr:
            {
                // Row 23's "Range = n" is the chooser's status line (attack mode, the line got through).
                string prompt = _text[CombatMsg.UseCursorKeys];
                Text(f, 0, MenuRow, prompt, PromptColour);
                int col = prompt.Length;
                foreach (var (word, _) in CursorItems(cr))
                {
                    if (col + word.Length > 40)
                        break;
                    Text(f, col, MenuRow, word[..1], Chrome.White);
                    Text(f, col + 1, MenuRow, word[1..], Chrome.Green);
                    col += word.Length + 1;
                }
                break;
            }
            case ChoiceRequest { Vertical: false } c:
            {
                int start = MenuStart(c);
                if (c.MenuPrompt is { Length: > 0 } mp)
                    Text(f, 0, MenuRow, mp, PromptColour);
                int col = start;
                for (int i = 0; i < c.Items.Count; i++)
                {
                    var w = c.Items[i];
                    if (col + w.Length > 40)
                        break;
                    Text(f, col, MenuRow, w[..1], Chrome.White);
                    Text(f, col + 1, MenuRow, w[1..], i == _selected ? Chrome.White : Chrome.Green);
                    col += w.Length + 1;
                }
                if (_view.Mode == CombatMode.Aim && input.LastDevice == InputDevice.Gamepad)
                    Text(f, 23, StatusRow, Fit("LB/RB: PREV/NEXT", 17), Chrome.Grey);
                if (IsMainMenu(c) && input.LastDevice != InputDevice.Keyboard)
                    Text(f, 0, StatusRow, Fit(AltHint(input), 40), Chrome.Grey);
                break;
            }
            case KeyRequest:
                Text(f, 0, MenuRow, input.LastDevice == InputDevice.Gamepad ? "PRESS A" : "PRESS A KEY", PromptColour);
                break;
        }
    }

    private bool IsMainMenu(ChoiceRequest c) => _view.Mode == CombatMode.Menu && c.MenuPrompt is null;

    /// <summary>The main menu's Alt keys on row 23 for the pad (their buttons) and the mouse (words to click).</summary>
    private static readonly string[] AltWords = ["MAGIC", "ALL-QUICK", "ALT-X"];
    private const int AltWordsStart = 5;
    private static readonly int[] AltAnswers = [CombatChooser.AnswerQuickMagic, CombatChooser.AnswerAllQuick, CombatChooser.AnswerAltX];

    private static string AltHint(ActionInput input)
    {
        if (input.LastDevice == InputDevice.Mouse)
            return "ALT: " + string.Join(' ', AltWords);
        string L(GameAction a) => PromptLabels.For(input.Bindings, a, InputDevice.Gamepad);
        return $"{L(GameAction.FightQuickMagic)} MAGIC {L(GameAction.FightAllQuick)} ALL QUICK {L(GameAction.FightAltX)} ALT-X";
    }

    /// <summary>How to move on the pad or with the mouse (the keyboard uses the original's keypad keys).</summary>
    private static string MoveHint(ActionInput input) => input.LastDevice == InputDevice.Mouse
        ? "CLICK A SQUARE BESIDE IT. RIGHT: UNDO"
        : $"{PromptLabels.For(input.Bindings, GameAction.Confirm, InputDevice.Gamepad)} END {PromptLabels.For(input.Bindings, GameAction.Cancel, InputDevice.Gamepad)} UNDO RSTICK DIAGONAL";

    private void Text(IndexedFrame f, int col, int row, string s, byte colour) => Chrome.Text(f, _font, col, row, s, colour);

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
