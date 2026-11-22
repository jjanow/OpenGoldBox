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
    /// <summary>A letters input (the Amiga View's Rename, <see cref="AmigaView"/>): keyboard, the pad's / mouse's grid.</summary>
    private CharGrid? _letters;
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
        _list.Shared = _highlight;
    }

    public bool Active => _view.Active;

    /// <summary>The status words by record byte 0x10C (START DS:1047, 13 bytes each; the panel's out-of-play line).</summary>
    public Func<int, string?> StatusName { get; init; } = _ => null;

    /// <summary>The program's shared menu highlight (DS:6B2B), the same cell the area and camp menus use; its list
    /// top is the one the list pages enter and page (<see cref="ListPage.Shared"/>).</summary>
    public MenuHighlight Highlight
    {
        get => _highlight;
        init => _list.Shared = _highlight = value;
    }

    private readonly MenuHighlight _highlight = new();

    /// <summary>A fault of the logic, for the caller's report.</summary>
    public Action<Exception>? Report { get; init; }

    /// <summary>The pictures (the Amiga View page's portrait, <see cref="AmigaView"/>), or null.</summary>
    public ViewPictures? Pictures { get; init; }

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
                        {
                            _cursorReturn = true;   // as Return: the lit entry stays
                            Answer(ManualInput.Target);
                        }
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
            case TextRequest { Numeric: false }:
                if (_letters is { } lg && lg.Click(click) == true)
                    Answer(lg.Typed);
                return null;
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
        // The shared highlight (MenuHighlight, DS:6B2B), as ExploreScreen: the menu routine 0x2CC8C opens on the cell;
        // the yes/no routine 0x2D9A0 first sets it to No (ChoiceRequest.Selected, 0x2D9B9); the item list routine
        // 0x2D669 (the Items page, the coin list, the spell lists through 0x270A2) sets it to the first entry
        // (0x2D6B9). FollowHighlight keeps it on the menu's entry while the menu is up.
        _selected = 0;
        _typed = "";
        _letters = request is TextRequest { Numeric: false } tr
            ? new CharGrid(CharGrid.Letters, LettersRow, tr.MaxLength) { KeepsCase = tr.KeepsCase }
            : null;
        // Another request that draws row 24 (or a page over the screen) replaces the menu kept there.
        if (request is not (null or DelayRequest or ChoiceRequest { Vertical: false }))
            _staleMenu = null;
        if (request is not (null or DelayRequest))
            _staleCursor = null;
        _hasClickAnswer = false;
        _drawn = false;
        switch (request)
        {
            case ChoiceRequest { Vertical: false } h:
                if (h.Selected != 0)
                    Highlight.Index = h.Selected;
                _selected = Highlight.Start(h.Items.Count);
                break;
            case ChoiceRequest v:
                Highlight.ItemList();
                _selected = Math.Clamp(v.Selected, 0, Math.Max(0, v.Items.Count - 1));
                break;
            case PageRequest pr:
                if (!pr.SharedHighlight)
                {
                    Highlight.ItemList();
                    // 0x2D766: the Items page (position 0 on entry, 0x21F5F, then the row kept) and the coin list (0,
                    // 0x2365B) enter the routine's global window top.
                    if (_view.Page is { } entered)
                        Highlight.ListEntered(entered.Selected, entered.Rows.Count);
                }
                _selected = Highlight.Start(pr.Entries.Count);
                break;
            case KeyRequest { AsMenu: true } km:
                Highlight.Start(km.MenuEntries);
                break;
            case CombatCursorRequest cr:
                _selected = Highlight.Start(CursorItems(cr).Count);
                break;
            case ListRequest l:
                _list.Sync(l);
                if (l.SharedHighlight)
                    _list.SetAction(l, Highlight.Start(l.Actions.Count));
                else
                    Highlight.Index = _list.SelectedAction;
                break;
        }
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
        FollowHighlight(request, answered, answer, input.Pressed(GameAction.Cancel));
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
            case TextRequest { Numeric: false } when _letters is { } letters:
                if (letters.Edit(input) is not { } done)
                    return false;
                answer = done ? letters.Typed : null;   // given up: no answer (Rename keeps the name)
                return true;
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
                : input.Pressed(GameAction.FightAltX) && !AmigaView.On ? CombatChooser.AnswerAltX // none on the Amiga (AltKeys)
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
                return true;
            }
            if (input.Pressed(GameAction.PreviousMember))
            {
                answer = 1;
                return true;
            }
        }
        // The original's hotkeys: the first letter of an entry.
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            for (int i = 0; i < n; i++)
                if (c.Items[i].Length > 0 && char.ToUpperInvariant(c.Items[i][0]) == key[0])   // a key is upper case (DosKeys); an Amiga entry may not be
                {
                    answer = i;
                    return true;
                }
        return false;
    }

    /// <summary>
    /// The shared cell (<see cref="MenuHighlight"/>) follows a menu's highlight while it is up (',' / '.' write the cell
    /// itself in 0x2CC8C: 0x2CEA2, 0x2CEC4, 0x2CECB) and takes the chosen entry (a hotkey moves it there, 0x2CF48);
    /// Escape leaves it. Bottom-line menus, the item screens' pages (both the menu routine's and the item list
    /// routine's: 0x2D669 runs its menu through 0x2CC8C) and the full-screen lists. A row picked on the coin list
    /// (Return, 0x234E1 takes the row whatever the key) leaves the cell on the highlighted entry.
    /// </summary>
    private void FollowHighlight(PlayerRequest request, bool answered, object? answer, bool cancelled)
    {
        bool chosen = answered && !cancelled;
        switch (request)
        {
            case ChoiceRequest { Vertical: false } h:
                Highlight.Index = chosen && answer is int i && i >= 0 && i < h.Items.Count ? i : _selected;
                // The menu routine draws its row once more with the chosen entry lit (0x2CF48), and that row stays up
                // until something else writes row 24 (DOSBox-X s29: Aim's A, then Next's N, lit during the typing).
                if (_staleMenu is var (sm, _, seq) && ReferenceEquals(sm, h))
                    _staleMenu = (sm, Highlight.Index, seq);
                break;
            case PageRequest pr:
                Highlight.Index = chosen && answer is ListAnswer { Action: var a } && a >= 0 && a < pr.Entries.Count ? a : _selected;
                break;
            case CombatCursorRequest cr when answered && answer is ManualInput { IsDirection: false } m:
            {
                // Manual's menu (0x2CC8C at 0x1596E) is the menu routine on the shared cell: a hotkey moves the cell to
                // its entry and the row stays up with it lit until row 24 is written again (DOSBox-X s41: Darkstar's
                // Sleep, Manual's line with Target lit through the flight and Rhiannon's
                // panel). Escape leaves the cell. Return gives 13 (the call passes colours 10 / 15, so 0x2CE7D takes
                // it) without writing the cell or the row (0x2CE88), and 0x15979 takes 13 as Target: the lit entry stays.
                var items = CursorItems(cr);
                int entry = m == ManualInput.Target ? 0 : m == ManualInput.Center ? 1 : 2;
                int at = _cursorReturn ? -1 : items.FindIndex(e => e.Item == entry);
                if (chosen && at >= 0)
                    Highlight.Index = at;
                _staleCursor = (cr, chosen && at >= 0 ? at : _selected, _view.Row24Writes);
                break;
            }
            case ListRequest l:
                Highlight.Index = chosen && l.Paging is null && answer is ListAnswer { Action: var la } && la >= 0 && la < l.Actions.Count
                    ? la
                    : _list.SelectedAction;
                break;
        }
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

    private void CursorItem(CombatCursorRequest cr, int item)
    {
        _cursorReturn = false;
        Answer(CursorEntry(cr, item));
    }

    /// <summary>Manual's last answer came by Return (the pad's A, a click on the cursor's square): Target with the
    /// cell left as it was (0x2CE7D), not moved there as by T or a click on the word.</summary>
    private bool _cursorReturn;

    /// <summary>
    /// The keys 0x155F5 reads, each one pass of Core's cursor (<see cref="FightMenu.Manual"/>): a direction moves it one
    /// square; Return (Confirm) and T are Target, which ends Manual even where Target is not offered; E and Escape
    /// (Cancel) are Exit; C and the shoulders (Home / End) re-centre the view. ',' / '.' light the previous / next
    /// entry inside the menu routine (0x2CE93 / 0x2CEB7: the cell, no answer).
    /// </summary>
    private bool DecideCursor(CombatCursorRequest cr, ActionInput input, out object? answer)
    {
        answer = null;
        _cursorReturn = false;
        if (Direction(input) is { } d)
        {
            answer = new ManualInput(d);
            return true;
        }
        if (input.Pressed(GameAction.Confirm))
        {
            _cursorReturn = true;
            answer = ManualInput.Target;
            return true;
        }
        int count = CursorItems(cr).Count;
        if (input.NewInput is (InputDevice.Keyboard, "OemComma" or "OemPeriod") nk)
        {
            _selected = nk.Name == "OemComma" ? (_selected + count - 1) % count : (_selected + 1) % count;
            Highlight.Index = _selected;
            return false;
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

    private static int PageTop(CombatPage p) => PageChrome.PageTop(p);

    private static int? PageRowAt(CombatPage p, int row) => PageChrome.RowAt(p, row);

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
        // The list routine 0x2D669 moves its highlight with Home / End and the keypad's 7 / 1, which come as the step
        // bindings' NumPad7 / NumPad1 (D s33: keypad 1 on the Items page moved it in DOSBox-X).
        bool keys = input.LastDevice == InputDevice.Keyboard;
        if (n > 0 && (input.Pressed(GameAction.MenuUp) || input.Pressed(GameAction.PreviousMember) || (keys && input.Pressed(GameAction.StepUpLeft))))
            page.Selected = (page.Selected + n - 1) % n;
        if (n > 0 && (input.Pressed(GameAction.MenuDown) || input.Pressed(GameAction.NextMember) || (keys && input.Pressed(GameAction.StepDownLeft))))
            page.Selected = (page.Selected + 1) % n;
        // ',' / '.' are the menu routine's own keys for the lit word (0x2CE93 / 0x2CEB7); Left / Right the Client's.
        if (m > 0 && (input.Pressed(GameAction.MenuLeft) || input.NewInput is (InputDevice.Keyboard, "OemComma")))
            _selected = (_selected + m - 1) % m;
        if (m > 0 && (input.Pressed(GameAction.MenuRight) || input.NewInput is (InputDevice.Keyboard, "OemPeriod")))
            _selected = (_selected + 1) % m;
        // View's menu (0x2CC8C called by 0x21AED at 0x21CD1) and the Items page (0x2D669 called by 0x21F32 at 0x222C9):
        // Return gives 13, which neither loop acts on (DOSBox-X s39 / s39: View at the party menu and the venues, the
        // camp's Items page only redraws); the pad's A and a click still take the lit word. The coin list takes the row
        // whatever the key (0x234E1), so Return picks there.
        if (!pr.PickRows && input.Pressed(GameAction.Confirm) && input.NewInput is (InputDevice.Keyboard, _))
            return false;
        if (input.Pressed(GameAction.Confirm))
        {
            answer = new ListAnswer(pr.PickRows && n > 0 ? PageRequest.RowPicked : _selected, page.Selected);
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
                    _pageEntry = i; // a hotkey lights its entry (0x2CF48)
                    answer = new ListAnswer(i, page.Selected);
                    return true;
                }
        return false;
    }

    /// <summary>The number buttons for pad and mouse (row 22): steps, then OK.</summary>
    private static readonly string[] NumberButtons = ["-10", "-1", "+1", "+10", "OK"];
    private const int NumberButtonRow = 22;

    /// <summary>The row of the pad / mouse hint and the number buttons: the page's free row (the View sheet uses 22).</summary>
    private int HintRow => _view.Page?.HintRow ?? NumberButtonRow;

    /// <summary>The letters grid's first row (as the party screen's, over the page's middle rows).</summary>
    private const int LettersRow = 14;

    private static char? Digit(string name) => name switch
    {
        { Length: 2 } when name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1],
        { Length: 7 } when name.StartsWith("NumPad", StringComparison.Ordinal) && char.IsAsciiDigit(name[6]) => name[6],
        _ => null,
    };

    private void Step(TextRequest t, int delta)
    {
        long v = long.TryParse(_typed, NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : 0;
        long max = t.Maximum ?? (long)Math.Pow(10, t.MaxLength) - 1;
        v = Math.Clamp(v + delta, 0, max);
        _typed = v.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A number (drop money's amount, the input 0x24F1D: a value above the maximum becomes the maximum as it is typed):
    /// keyboard digits, Backspace, Return, Escape (0); pad Up / Down ±1, Right / Left ±10, A done, B 0; the mouse uses the
    /// buttons on row 22.
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
                if (t.Maximum is { } most && long.Parse(_typed, CultureInfo.InvariantCulture) > most)
                    _typed = most.ToString(CultureInfo.InvariantCulture);
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

    /// <summary>The last page menu drawn and its lit entry (the chosen one once answered).</summary>
    private PageRequest? _pageMenu;
    private int _pageEntry;

    private void DrawPage(IndexedFrame f, CombatPage p, ActionInput input)
    {
        // the list routine draws its rows again without the highlight when it returns (D s33: every row green under
        // handler 131's text; D s39: the coin rows while Drop's amount is asked)
        PageChrome.DrawBody(f, _font, p, showSelection: _view.ItemEffectText is null && _runner.Pending is not TextRequest);
        AmigaView.DrawExtras(f, _font, p, Pictures);

        switch (_runner.Pending)
        {
            case PageRequest pr:
                if (pr.Prompt is { Length: > 0 } prompt)
                    Chrome.MenuPrompt(f, _font, 0, MenuRow, prompt, PromptColour);
                DrawEntries(f, pr.Entries, pr.Prompt?.Length ?? 0, _selected);
                (_pageMenu, _pageEntry) = (pr, _selected);
                if (input.LastDevice != InputDevice.Keyboard)
                    Text(f, 1, HintRow, Fit(PageHint(input, p.Rows.Count > 0), 38), Chrome.Grey);
                break;
            case DelayRequest when _view.ItemEffectText is not null && _pageMenu is { } kept && !AmigaView.On:
                // Handler 131's text over the Items page (D s33): the menu row stays as the pick left it. The Amiga's
                // menu routine blanks row 24 on every way out (hunk 19 +0x5372 … +0x5702; FS-UAE s47: row 24 blank under
                // "is stronger").
                if (kept.Prompt is { Length: > 0 } keptPrompt)
                    Chrome.MenuPrompt(f, _font, 0, MenuRow, keptPrompt, PromptColour);
                DrawEntries(f, kept.Entries, kept.Prompt?.Length ?? 0, _pageEntry);
                break;
            case ChoiceRequest { Vertical: false } c:
                if (c.MenuPrompt is { Length: > 0 } mp)
                    Chrome.MenuPrompt(f, _font, 0, MenuRow, mp, c.MenuPromptColour ?? PromptColour);
                DrawEntries(f, c.Items, MenuStart(c), _selected);
                break;
            case DelayRequest when _view.PageMessage is { } msg:
                Text(f, 0, MenuRow, Fit(msg, 40), Chrome.Green);
                break;
            case TextRequest { Numeric: false } lt when _letters is { } letters:
                // A letters input: the Amiga's requester (hunk 25 +0xEC6) under the Amiga art, else the bottom line;
                // the grid for the pad and the mouse over the page's middle rows.
                Chrome.Clear(f, 0, MenuRow, 40);
                if (AmigaView.On && Chrome.AmigaGadget is { } gadget)
                    gadget.Requester(f, _font, lt.Prompt.ToUpperInvariant(), letters.Typed, lt.MaxLength + 1);
                else
                    Text(f, 0, MenuRow, Fit(lt.Prompt.ToUpperInvariant() + " " + letters.Typed + "_", 40), Chrome.White);
                if (input.LastDevice != InputDevice.Keyboard)
                {
                    for (int r = LettersRow - 1; r <= LettersRow + 4; r++)
                        Chrome.Clear(f, 1, r, 38);
                    letters.Draw(f, _font);
                }
                break;
            case TextRequest t:
            {
                // Drop's amount (input 0x24F1D; DOSBox-X s39, the camp's View): the question in green, a space, the
                // digits typed in white, no cursor.
                string question = Fit(t.Prompt + " ", 40);
                Text(f, 0, MenuRow, question, Chrome.Green);
                Text(f, question.Length, MenuRow, Fit(_typed, 40 - question.Length), Chrome.White);
            }
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
            Text(f, col + 1, MenuRow, w[1..], i == selected && Chrome.AmigaGadget is null ? Chrome.White : Chrome.Green);
            if (i == selected)
                Chrome.AmigaGadget?.Entry(f, col, MenuRow, w);   // under Amiga: the pressed gadget, not a lit word
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
            if (page.Grey)
                Palette(f);
            return;
        }
        if (_view.Page is { } cp)
        {
            DrawPage(f, cp, input);
            DrawItemEffectText(f);
            if (cp.Grey)
                Palette(f);
            return;
        }
        f.Clear(0);
        DrawFrame(f);
        DrawMap(f);
        DrawPanel(f);
        DrawLines(f, input);
        DrawItemEffectText(f);
        Palette(f);
    }

    /// <summary>
    /// The combat screen's palette: DOS swaps EGA colours 0 and 8 (<see cref="SwapGrey"/>). Under the Amiga art the
    /// program loads palette table 0 instead (hunk 13 +0x5F0E), whose pen 0 is the dark ground: the blank cells (index 0)
    /// and the frame's pen 0 take it; EGA 8 (DOS-filled art) still becomes 0 `[?]`.
    /// </summary>
    private void Palette(IndexedFrame f)
    {
        if (_art.AmigaGround is not { } g)
        {
            SwapGrey(f);
            return;
        }
        int frameZero = Chrome.AmigaFrame?.Zero ?? -1;
        var p = f.Pixels;
        for (int i = 0; i < p.Length; i++)
            p[i] = p[i] switch { 0 => g, 8 => 0, var v when v == frameZero => g, var v => v };
    }

    private void DrawItemEffectText(IndexedFrame f)
    {
        if (_view.ItemEffectText is not { } window)
            return;
        // DOS START 0x16EC with DS:49F3 = 2: the frame (03F1:0000 with 1, 17, 38, 22), (1, 17)–(38, 22) blanked, the name
        // (START 0x1816), the text printed. The Amiga's copy (hunk 13 +0x49FE–0x4A74, s46): the frame blit (hunk 25
        // +0x16D4 with 1, 17, 38, 22; none while resting, hunk 32 +0x1A32, as DOS's DS:6DDA), the blank (+0x197A), the
        // name through the name routine (+0x4AD4: the string routine, whole), the text through the print (+0x10EC, glyph
        // by glyph) in pen 28; so the window's draws are replayed in order (Chrome.TextRuns).
        Chrome.Box(f, TextWindow.Left - 1, TextWindow.Top - 1, TextWindow.Right + 1, TextWindow.Bottom + 1);
        for (int row = TextWindow.Top; row <= TextWindow.Bottom; row++)
            Chrome.Clear(f, TextWindow.Left, row, TextWindow.Right - TextWindow.Left + 1);
        Chrome.TextRuns(f, _font, window.Runs(TextWindow.Top, TextWindow.Bottom));
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
        if (Chrome.AmigaFrame is not null)
        {
            // The Amiga's combat screen is two windows, the map (1, 1)–(21, 21), then the panel (23, 1)–(38, 21) over
            // their shared column 22 (hunk 13 +0x5EB0–0x5EF4).
            Chrome.Box(f, 0, 0, 22, 22);
            Chrome.Box(f, 22, 0, 39, 22);
            return;
        }
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
            // On every cell of the creature's footprint (0x315FE–0x31682: 0x3073A for i = 0..3; DOSBox-X s38/s39: both
            // cells of the wild boar's). The whole picture, colour 0 included (DOSBox-X s30: the skull's three colour-0
            // pixels show colour 0 — grey under the combat palette — whatever lies under them; s29's "3 skull pixels").
            // [?] A cell outside the view: the program draws at the slot's view position + the cell offset unclipped by
            // this loop; it is always in view here (0x315CD scrolls first), the Client draws only cells in view.
            for (int i = 0; i < 4; i++)
                if (map.Tables.IconCell(_view.HitMarkerSize, i, out int dx, out int dy) && _view.InView(hm.X + dx, hm.Y + dy))
                    CombatArt.Draw(f, skull, MapX + (hm.X + dx - _view.ViewX) * Square, MapY + (hm.Y + dy - _view.ViewY) * Square,
                        false, clip, opaque: true);
        if (_view.Flash is { } fl && _art.Sprite(fl.Sprite) is { } flash)
            CombatArt.Draw(f, flash, MapX + (fl.X - _view.ViewX) * Square, MapY + (fl.Y - _view.ViewY) * Square, fl.Mirror, clip);
        // The moving icon (START 0x25C9) at its position in thirds of a square (8 px): 0x464:0xD71 draws it into the view
        // buffer DS:4A20 in the same units as the map's cells (0x31679 / 0x32097 at cell × 3), so from the same origin.
        if (_view.Flying is { } fy && _art.Sprite(fy.Sprite) is { } flying)
            CombatArt.Draw(f, flying, MapX + fy.Cx * (Square / 3), MapY + fy.Cy * (Square / 3), fy.Mirror, clip);
    }

    /// <summary>
    /// Icons face right in the art. The icon drawer GAME.OVR 0x31F68 (far 03E9:0039, called by 0x31232 with the record's
    /// +9) copies the picture mirrored (START 0x464:0x15C7) when the direction is above 3, as stored otherwise (read s29):
    /// facings 4–7 are mirrored. DOSBox-X s29: a vampire attacking the member straight below it (facing 4) in its
    /// mirrored attack pose; the party (facing 7) mirrored and goblins (3) as stored at a fight's start (s15).
    /// </summary>
    private bool Mirrored(EclCharacter c) =>
        _view.Fight is { } fight && fight.Record(c)[CombatRecord.Facing] is >= 4 and <= 7;

    /// <summary>
    /// A creature's icon: a player character (record 0x84 &lt; 0x80, as the menu's space key tells PCs from NPCs) gets
    /// the composed party icon, any other creature the CPIC loaded into its icon slot (record 0xBF). An NPC that joined
    /// the party (ADDNPC, NPC byte 0x84 ≥ 0x80) gets the composed icon too: the party join 0x1F42C gives it a free
    /// slot of 0..7 and 0x1E468 builds that slot's picture from its record as for a player character (CHEAD 0xBD,
    /// CBODY 0xBE, size 0xC0, the colours 0xC1–0xC6 through DS:3CF5; 0x1E4BE / 0x1E506, 0x1E610 / 0x1E63D). The icon
    /// drawer only reads the slot, so a party-side creature (combat record +0x13 = 0) draws the composed icon (DOSBox-X
    /// s39: the level 3 magic-user of ADDNPC 94).
    /// </summary>
    private CombatPicture? Icon(EclCharacter c, bool attack)
    {
        // A charmed member (0xF05B sets the NPC byte to 0xB3, the mark Animate Dead also gives a member) keeps the
        // party icon: DOSBox-X s29, Brother Sean charmed by a vampire's gaze, then casting from his own square.
        // [?] An animated member's icon is not seen.
        if (c[CombatRules.Npc] < 0x80 || c[CombatRules.Npc] == EffectHandlers.AnimatedMember
            || _view.Fight is { } fight && fight.Record(c)[CombatRecord.NotParty] == 0)
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
            var lines = _view.ShownLines is { } sl && ReferenceEquals(sl.Creature, c) ? sl : (c, CombatChooser.NameColour(c), (int)c[FightRules.Hp]);
            // The Amiga's panel (hunk 13 +0x3E4A, s46): (23, 1)–(38, 21) blanked, the name through the creature message
            // routine (+0x4970 → the name routine +0x4AD4: the string routine, pens 28 / 24 / 18 for DOS 11 / 12 / 14),
            // "Hitpoints" and "AC" at (23, 3) / (23, 5) and the AC number at column 26 (+0x3D38) through the string routine
            // in pen 28, the HP number at column 33 (+0x3DD4), the weapon name printed (+0x10EC) into (23, 7)–(38, 9) in
            // pen 28. Strings are drawn whole (Whole), the printed weapon name glyph by glyph.
            Whole(f, PanelCol, 1, Fit(AmigaView.Case(c.Name), 16), lines.Item2);   // the Amiga draws the name as stored
            // The HP number as the party list (START 0x1513): yellow below the maximum 0x32 (DOSBox-X s29: 185 of 200).
            string hpLabel = _text[CombatMsg.PanelHitpoints].Trim() + " ";
            Whole(f, PanelCol, 3, Fit(hpLabel.TrimEnd(), 16), Chrome.Green);
            // Amiga: the party list's hit-point routine (hunk 13 +0x3EB8 → +0x3DD4): colour 8 below the maximum, else 10.
            if (hpLabel.Length < 16
                && !_font.DrawAmiga(f, (PanelCol + hpLabel.Length) * 8, 3 * 8,
                    Fit(lines.Item3.ToString(CultureInfo.InvariantCulture), 16 - hpLabel.Length), lines.Item3 < c[0x32] ? 8 : 10))
                Text(f, PanelCol + hpLabel.Length, 3, Fit(lines.Item3.ToString(CultureInfo.InvariantCulture), 16 - hpLabel.Length),
                    lines.Item3 < c[0x32] ? Chrome.Yellow : Chrome.Green);
            // [?] AC as 60 − record byte 0x111, as the party list (ExploreScreen); DOSBox-X: Darkstar 7, Thrender 1.
            // The label and the number are two strings (Amiga +0x3EC8 / +0x3D38), one cell apart.
            string acLabel = _text[CombatMsg.PanelAc].Trim();
            Whole(f, PanelCol, 5, Fit(acLabel, 16), Chrome.Green);
            if (acLabel.Length + 1 < 16)
                Whole(f, PanelCol + acLabel.Length + 1, 5,
                    Fit((60 - c[FightRules.Ac]).ToString(CultureInfo.InvariantCulture), 15 - acLabel.Length), Chrome.Green);
            // START 0x1675: the weapon's name (ending in a space, as composed) word-wrapped into (23, 7)–(38, 9) through
            // 0x77F8 (DOSBox-X s19: "TWO-HANDED SWORD " takes two rows).
            // The cursor row the status line follows (DS:5E25; Amiga hunk 32 +0x2D0): the AC line's 5, or where the
            // weapon's print left it.
            int cursorRow = 5;
            if (_view.Fight is { } fight && FightRules.Equipment(c, fight.Items)[FightRules.SlotWeapon] is { } w)
            {
                var box = new TextWindow(PanelCol, 7, 38, 9);
                box.PrintIn(PanelCol, 7, 38, 9, ItemNames.Compose(w, _itemWord), true, Chrome.Green);
                cursorRow = box.Row;
                int left = _view.WeaponTyped ?? int.MaxValue; // typed while DS:4A00 is set (CombatChooser.ShowPanel)
                for (int row = 7; row <= 9; row++)
                    for (int col = PanelCol; col <= 38; col++)
                        if (box.Cell(row, col) is ({ } ch and not '\0', var colour) && left-- > 0)
                            Chrome.Glyphs(f, _font, col, row, ch.ToString(), colour);   // printed: one glyph at a time
            }
            // START 0x16A0–0x170D (Amiga hunk 13 +0x3F46–0x3FB4): two rows under the cursor, a fixed-cell string in
            // colour 15 (Amiga pen 16, the string routine): out of play (0x10D = 0; Amiga 0x10F) the status word of
            // 0x10C (Amiga 0x10E, its table hunk 31 +0x323), else when helpless (START 0x1713) "(HELPLESS)". FS-UAE s46:
            // "(Helpless)" on row 9 under a one-row weapon for a sleeping cleric, grey.
            string? status = c[FightRules.InPlay] == 0
                ? StatusName(c[FightRules.Status]) is { } st ? AmigaView.Case(st) : null
                : FightRules.IsHelpless(c) ? _text[CombatMsg.PanelHelpless] : null;
            if (status is not null && cursorRow + 2 <= CombatView.PanelBottom)
                Text(f, PanelCol, cursorRow + 2, Fit(status, 16), Chrome.White);
        }
        // The message area: the panel's draws as START 0x16EC / 0x77F8 made them, in their order (the names and fixed
        // lines whole, the printed text glyph by glyph: Chrome.TextRuns).
        Chrome.TextRuns(f, _font, _view.Panel.Runs(1, CombatView.PanelBottom));
    }

    /// <summary>A fixed-cell string as the string routine draws it (Amiga hunk 25 +0xC3C: whole, JAM2 then pass 2); the
    /// DOS font as before.</summary>
    private void Whole(IndexedFrame f, int col, int row, string s, byte colour)
    {
        if (!_font.DrawString(f, col * 8, row * 8, s, colour, 0))
            Text(f, col, row, s, colour);
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
            case DelayRequest when _view.MovePrompt is null && _staleMenu is var (sm, sel, seq) && seq == _view.Row24Writes
                && !AmigaView.On:
                // Row 24 keeps the last menu drawn (with the answered entry lit) until something else writes it
                // (DOSBox-X s29: Aim's and Next's menus while the panel types, a Done menu through the next creature's
                // panel until the AI's start clears the row). The Amiga's menu routine blanks the row as it returns.
                DrawMenu(f, sm, sel);
                break;
            case DelayRequest when _view.MovePrompt is null && _staleCursor is var (sc, lit, cseq) && cseq == _view.Row24Writes
                && !AmigaView.On:
                DrawCursorMenu(f, sc, lit);
                break;
            case CombatMoveRequest m:
                // DOSBox-X: Move's prompt (0xA1BB) and the moves left in green on the menu row.
                Text(f, 0, MenuRow, Fit(_text[CombatMsg.MoveLeft] + m.MovementLeft.ToString(CultureInfo.InvariantCulture), 40), Chrome.Green);
                if (input.LastDevice != InputDevice.Keyboard)
                    Text(f, 0, StatusRow, Fit(MoveHint(input), 40), Chrome.Grey);
                break;
            case CombatCursorRequest cr:
                // Row 23's "Range = n" is the chooser's status line (attack mode, the line got through).
                DrawCursorMenu(f, cr, _selected);
                break;
            case ChoiceRequest { Vertical: false } c:
            {
                DrawMenu(f, c, _selected);
                _staleMenu = (c, _selected, _view.Row24Writes);
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

    /// <summary>The last horizontal menu drawn, its highlight and <see cref="CombatView.Row24Writes"/> then (see DrawLines).</summary>
    private (ChoiceRequest Menu, int Selected, int Row24Writes)? _staleMenu;

    /// <summary>Manual's menu as answered (Target, Center or Exit lit), kept on row 24 like <see cref="_staleMenu"/>.</summary>
    private (CombatCursorRequest Request, int Selected, int Row24Writes)? _staleCursor;

    /// <summary>
    /// Manual's menu line (0x1596E, the menu routine on the shared cell): the prompt, then Target (when offered), Center
    /// and Exit, the entry on the cell lit (DOSBox-X s41: Exit lit after Manual's M, the cell's third entry).
    /// </summary>
    private void DrawCursorMenu(IndexedFrame f, CombatCursorRequest cr, int selected)
    {
        string prompt = _text[CombatMsg.UseCursorKeys];
        Chrome.MenuPrompt(f, _font, 0, MenuRow, prompt, PromptColour);
        int col = prompt.Length;
        var items = CursorItems(cr);
        if (AmigaView.On)
        {
            // The Amiga menu routine: the program's words, capitals and digits in the hotkey pen (ExploreScreen.DrawLine).
            ExploreScreen.DrawLine(f, _font, [.. items.Select(it => it.Word)], selected, col);
            return;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var word = items[i].Word;
            if (col + word.Length > 40)
                break;
            Text(f, col, MenuRow, word[..1], Chrome.White);
            Text(f, col + 1, MenuRow, word[1..], i == selected && Chrome.AmigaGadget is null ? Chrome.White : Chrome.Green);
            if (i == selected)
                Chrome.AmigaGadget?.Entry(f, col, MenuRow, word);
            col += word.Length + 1;
        }
    }

    /// <summary>A horizontal menu on row 24: the prompt, then each entry with a white hotkey, the highlighted one white.</summary>
    private void DrawMenu(IndexedFrame f, ChoiceRequest c, int selected)
    {
        int start = MenuStart(c);
        if (c.MenuPrompt is { Length: > 0 } mp)
            Chrome.MenuPrompt(f, _font, 0, MenuRow, mp, c.MenuPromptColour ?? PromptColour);
        if (AmigaView.On)
        {
            ExploreScreen.DrawLine(f, _font, c.Items, selected, start, scriptText: c.ScriptText);
            return;
        }
        int col = start;
        for (int i = 0; i < c.Items.Count; i++)
        {
            var w = c.Items[i];
            if (col + w.Length > 40)
                break;
            Text(f, col, MenuRow, w[..1], Chrome.White);
            Text(f, col + 1, MenuRow, w[1..], i == selected && Chrome.AmigaGadget is null ? Chrome.White : Chrome.Green);
            if (i == selected)
                Chrome.AmigaGadget?.Entry(f, col, MenuRow, w);   // under Amiga: the pressed gadget, not a lit word
            col += w.Length + 1;
        }
    }

    private bool IsMainMenu(ChoiceRequest c) => _view.Mode == CombatMode.Menu && c.MenuPrompt is null;

    /// <summary>The main menu's Alt keys on row 23 for the pad (their buttons) and the mouse (words to click).</summary>
    private static string[] AltWords => AltKeys(AmigaView.On).Words;
    /// <summary>Row 23's label before the words: DOS's Alt keys; the Amiga's are F8 / ';' and F6 (ActionInput.AmigaFightKeys).</summary>
    private static string AltPrefix => AmigaView.On ? "KEYS: " : "ALT: ";
    private static int AltWordsStart => AltPrefix.Length;
    private static int[] AltAnswers => AltKeys(AmigaView.On).Answers;

    /// <summary>
    /// The main menu's Alt keys: DOS takes Alt-M, Alt-Q and Alt-X (0x9FF3–0xA057). The Amiga program has no Alt-X: its
    /// menu key loop (hunk 1 +0x1C42–0x1DF4) takes M V A U C T Q D, the magic toggle (key 0x3B, +0x1D5A), space and the
    /// all-quick key (−26, +0x1DBA), and its AI key check (hunk 1 +0x127E) only the magic toggle and space; there is no
    /// Alt-X routine, so no refusal either (s45 A1, s46 A2). Under the Amiga art (<paramref name="amiga"/>, as every
    /// Amiga-program difference) the Client offers no Alt-X. The magic toggle 0x3B is ';' or F8 and the all-quick key
    /// F6 there (s47: the program's raw-key table, <see cref="ActionInput.AmigaFightKeys"/>), not Alt-M / Alt-Q.
    /// </summary>
    internal static (string[] Words, int[] Answers) AltKeys(bool amiga) => amiga
        ? (["MAGIC", "ALL-QUICK"], [CombatChooser.AnswerQuickMagic, CombatChooser.AnswerAllQuick])
        : (["MAGIC", "ALL-QUICK", "ALT-X"], [CombatChooser.AnswerQuickMagic, CombatChooser.AnswerAllQuick, CombatChooser.AnswerAltX]);

    private static string AltHint(ActionInput input)
    {
        if (input.LastDevice == InputDevice.Mouse)
            return AltPrefix + string.Join(' ', AltWords);
        string L(GameAction a) => PromptLabels.For(input.Bindings, a, InputDevice.Gamepad);
        string hint = $"{L(GameAction.FightQuickMagic)} MAGIC {L(GameAction.FightAllQuick)} ALL QUICK";
        return AmigaView.On ? hint : $"{hint} {L(GameAction.FightAltX)} ALT-X";
    }

    /// <summary>How to move on the pad or with the mouse (the keyboard uses the original's keypad keys).</summary>
    private static string MoveHint(ActionInput input) => input.LastDevice == InputDevice.Mouse
        ? "CLICK A SQUARE BESIDE IT. RIGHT: UNDO"
        : $"{PromptLabels.For(input.Bindings, GameAction.Confirm, InputDevice.Gamepad)} END {PromptLabels.For(input.Bindings, GameAction.Cancel, InputDevice.Gamepad)} UNDO RSTICK DIAGONAL";

    private void Text(IndexedFrame f, int col, int row, string s, byte colour) => Chrome.Text(f, _font, col, row, s, colour);

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
