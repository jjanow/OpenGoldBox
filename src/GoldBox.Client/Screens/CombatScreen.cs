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
/// art's colour 8 black: the DUNGCOM tiles match the screen pixel for pixel only that way); the palette write itself is
/// not located [?]. It answers the requests of the fight (<see cref="CombatChooser"/>) with keyboard, mouse or pad.
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
    private TimeSpan _delayEnd;
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
                    if (_view.Cursor == sq && TargetOffered(cr, sq))
                        Answer(((int, int))sq);
                    _view.Cursor = sq;
                    return null;
                }
                if (row == MenuRow && CursorItemAt(cr, col) is { } item)
                    CursorItem(cr, item);
                return null;
            }
            case ChoiceRequest c:
                if (click.Right)
                    return c.CancelIndex is not null ? GameAction.Cancel : null;
                if (row == MenuRow && WordAt(c.Items, col - MenuStart(c)) is { } i)
                {
                    _selected = i;
                    return GameAction.Confirm;
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
            case ChoiceRequest { Vertical: false } c when y / 8 == MenuRow && WordAt(c.Items, x / 8 - MenuStart(c)) is { } i:
                _selected = i;
                break;
            case CombatCursorRequest when SquareAt(x, y) is { } sq:
                _view.Cursor = sq;
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
        _selected = 0;
        _hasClickAnswer = false;
        if (request is ListRequest l)
            _list.Sync(l);
        if (request is DelayRequest d)
            _delayEnd = _clock.Elapsed + d.Duration;
    }

    public void Update(ActionInput input)
    {
        Sync();
        if (_runner.Pending is not { } request)
            return;
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
                return _clock.Elapsed >= _delayEnd || input.Pressed(GameAction.Confirm);
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
                if (c.Items[i].Length > 0 && c.Items[i][0] == key[0])
                {
                    answer = i;
                    return true;
                }
        return false;
    }

    // ---- Manual cursor (0x155F5) -------------------------------------------------------------------------------------

    /// <summary>
    /// The square accepts Target when a creature other than the aiming one stands on it within range (line distance).
    /// [?] The program also tests reachability, path distance (0x2FF74), ground cost, the target check, downed bodies
    /// and the ranged-weapon rule (combat-menu.md "Manual"); the Core aim makes those choices once the creature is picked.
    /// </summary>
    public bool TargetOffered(CombatCursorRequest cr, (int X, int Y) sq)
    {
        if (_view.Fight?.Map is not CombatMap m || _view.Current is not { } me || m.At(sq.X, sq.Y) is not { } c || (cr.Attack && ReferenceEquals(c, me)))
            return false;
        return Distance(sq) <= cr.Range;
    }

    private int Distance((int X, int Y) sq) =>
        _view.Fight?.Map is CombatMap m && Position(_view.Current) is { } me ? m.Distance(me.X, me.Y, sq.X, sq.Y) : 0;

    /// <summary>The entries after the prompt: Target (when offered), Center, Exit.</summary>
    private List<(string Word, int Item)> CursorItems(CombatCursorRequest cr)
    {
        var l = new List<(string, int)>();
        if (_view.Cursor is { } sq && TargetOffered(cr, sq))
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

    private void CursorItem(CombatCursorRequest cr, int item)
    {
        switch (item)
        {
            case 0 when _view.Cursor is { } sq && TargetOffered(cr, sq):
                Answer(((int, int))sq);
                break;
            case 1 when _view.Cursor is { } c:
                _view.Center(c.X, c.Y);
                break;
            case 2:
                Answer(null);
                break;
        }
    }

    private bool DecideCursor(CombatCursorRequest cr, ActionInput input, out object? answer)
    {
        answer = null;
        var cur = _view.Cursor ?? Position(_view.Current) ?? (_view.ViewX + 3, _view.ViewY + 3);
        if (Direction(input) is { } d)
        {
            var (dx, dy) = FightMenu.Steps[d];
            int x = Math.Clamp(cur.X + dx, 0, CombatArena.Width - 1), y = Math.Clamp(cur.Y + dy, 0, CombatArena.Height - 1);
            _view.Cursor = (x, y);
            _view.Follow(x, y);
            return false;
        }
        if (input.Pressed(GameAction.Confirm))
        {
            if (TargetOffered(cr, cur))
            {
                answer = ((int, int))cur;
                return true;
            }
            return false;
        }
        if (input.Pressed(GameAction.Cancel))
            return true;
        if (input.Pressed(GameAction.NextMember) || input.Pressed(GameAction.PreviousMember))
        {
            _view.Center(cur.X, cur.Y);
            return false;
        }
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            foreach (var (word, item) in CursorItems(cr))
                if (word.Length > 0 && word[0] == key[0])
                {
                    if (item == 0)
                    {
                        answer = ((int, int))cur;
                        return true;
                    }
                    if (item == 2)
                        return true;
                    _view.Center(cur.X, cur.Y);
                    return false;
                }
        return false;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------------------------------------------------------

    public void Draw(IndexedFrame f, ActionInput input)
    {
        Sync();
        if (_runner.Pending is ListRequest page)
        {
            _list.Draw(f, _font, page, input);
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
        bool box = _view.Mode is CombatMode.Menu or CombatMode.Aim or CombatMode.Manual or CombatMode.Done
            or CombatMode.Speed or CombatMode.Question;
        if (box && _view.Cursor is { } cur)
            DrawCursor(f, cur, clip);
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
    }

    /// <summary>
    /// [?] Icons face right in the art; DOSBox-X shows the party (facing 7, NW) mirrored and the goblins (facing 3,
    /// SE) as stored: we mirror facings 5–7. Which way N and S face is not seen.
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
    /// The creature the status panel shows: under the manual cursor the creature on its square (DOSBox-X: an empty
    /// square leaves the panel blank), else the one the fight shows (the current creature, the aim candidate).
    /// </summary>
    public EclCharacter? PanelCreature() => _runner.Pending is CombatCursorRequest
        ? (_view.Cursor is { } cur && _view.Fight?.Map is { } map ? map.At(cur.X, cur.Y) : null)
        : _view.Shown;

    private void DrawPanel(IndexedFrame f)
    {
        if (PanelCreature() is { } c)
        {
            Text(f, PanelCol, 1, Fit(c.Name.ToUpperInvariant(), 16), CombatChooser.NameColour(c));
            Text(f, PanelCol, 3, Fit(_text[CombatMsg.PanelHitpoints].Trim() + " " + c[FightRules.Hp].ToString(CultureInfo.InvariantCulture), 16), Chrome.Green);
            // [?] AC as 60 − record byte 0x111, as the party list (ExploreScreen); DOSBox-X: Darkstar 7, Thrender 1.
            Text(f, PanelCol, 5, Fit(_text[CombatMsg.PanelAc].Trim() + " " + (60 - c[FightRules.Ac]).ToString(CultureInfo.InvariantCulture), 16), Chrome.Green);
            if (_view.Fight is { } fight && FightRules.Equipment(c, fight.Items)[FightRules.SlotWeapon] is { } w)
                Text(f, PanelCol, 7, Fit(ItemNames.Compose(w, _itemWord).Trim(), 16), Chrome.Green);
        }
        for (int i = 0; i < _view.Messages.Count && MessageRow + i <= 21; i++)
            Text(f, PanelCol, MessageRow + i, Fit(_view.Messages[i].Text, 16), _view.Messages[i].Colour);
    }

    private void DrawLines(IndexedFrame f, ActionInput input)
    {
        if (_view.StatusLine is { } s && _view.Mode is CombatMode.Aim or CombatMode.Manual)
            Text(f, 0, StatusRow, Fit(s, 40), Chrome.Green);
        switch (_runner.Pending)
        {
            case CombatMoveRequest m:
                // DOSBox-X: Move's prompt (0xA1BB) and the moves left in green on the menu row.
                Text(f, 0, MenuRow, Fit(_text[CombatMsg.MoveLeft] + m.MovementLeft.ToString(CultureInfo.InvariantCulture), 40), Chrome.Green);
                if (input.LastDevice != InputDevice.Keyboard)
                    Text(f, 0, StatusRow, Fit(MoveHint(input), 40), Chrome.Grey);
                break;
            case CombatCursorRequest cr:
            {
                Text(f, 0, StatusRow, Fit(_text[CombatMsg.Range] + Distance(_view.Cursor ?? (0, 0)).ToString(CultureInfo.InvariantCulture), 40), Chrome.Green);
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
                break;
            }
            case KeyRequest:
                Text(f, 0, MenuRow, input.LastDevice == InputDevice.Gamepad ? "PRESS A" : "PRESS A KEY", PromptColour);
                break;
        }
    }

    /// <summary>How to move on the pad or with the mouse (the keyboard uses the original's keypad keys).</summary>
    private static string MoveHint(ActionInput input) => input.LastDevice == InputDevice.Mouse
        ? "CLICK A SQUARE BESIDE IT. RIGHT: UNDO"
        : $"{PromptLabels.For(input.Bindings, GameAction.Confirm, InputDevice.Gamepad)} END {PromptLabels.For(input.Bindings, GameAction.Cancel, InputDevice.Gamepad)} UNDO RSTICK DIAGONAL";

    private void Text(IndexedFrame f, int col, int row, string s, byte colour) => Chrome.Text(f, _font, col, row, s, colour);

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;
}
