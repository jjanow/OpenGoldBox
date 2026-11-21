using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>Move mode (0xA1E6): a direction 0–7, Return or Escape. The answer is a <see cref="MoveInput"/>.</summary>
public sealed record CombatMoveRequest(int MovementLeft) : PlayerRequest;

/// <summary>
/// One pass of the manual aim cursor (0x155F5, <see cref="FightMenu.Manual"/>): the cursor square as Core computed it
/// (<see cref="ManualView"/>). The answer is a <see cref="ManualInput"/> (a direction, Center, Target or Exit), or a
/// <see cref="CursorWalk"/> (a click on another square).
/// </summary>
public sealed record CombatCursorRequest(ManualView View) : PlayerRequest;

/// <summary>A click on a square while the manual cursor is up: the cursor walks there one square per pass.</summary>
public sealed record CursorWalk(int X, int Y);

/// <summary>A creature picked with the mouse on the map while the aim menu is up (answers that menu's request).</summary>
public sealed record AimPick(int X, int Y);

/// <summary>What the combat screen shows in each phase (the line on rows 23/24 and what the map highlights).</summary>
public enum CombatMode { Watch, Menu, Move, Aim, Manual, Done, Speed, Question, Spell, Page }

/// <summary>
/// The combat screen's state: the fight, the creature whose turn it is, the one the panel shows, the view's upper-left
/// cell, the cursor, the panel messages and the lines under the map. The logic thread writes it; the UI draws it only
/// while the logic is parked (<see cref="ScriptRunner"/>), so no locks.
/// </summary>
public sealed class CombatView
{
    public const int ViewSquares = 7;

    public Fight? Fight { get; set; }
    public bool Active => Fight is not null;
    public CombatMode Mode { get; set; }
    /// <summary>The creature whose turn it is (DS:5D92).</summary>
    public EclCharacter? Current { get; set; }
    /// <summary>The creature the status panel shows (the current one, or the aim candidate).</summary>
    public EclCharacter? Shown { get; set; }
    /// <summary>The view's upper-left arena cell (arena header +2/+3).</summary>
    public int ViewX { get; set; }
    public int ViewY { get; set; }
    /// <summary>
    /// The cursor box's square (null = none). DOSBox-X: a light-grey box with a bevel under the icon. The program paints
    /// it into the view (DrawCursorCell, through 0x310D0 while the arena's cursor flag +4 is set) and it stays until
    /// that cell or the whole view is redrawn.
    /// </summary>
    public (int X, int Y)? Cursor { get; set; }
    /// <summary>The cursor's footprint (arena +5: the creature's icon size for 0x319D2, 1 for the aim cursor).</summary>
    public int CursorSize { get; set; } = 1;
    /// <summary>The square of the downed picture (COMSPR 11, CreatureDowned 0x3154B), shown during its pause.</summary>
    public (int X, int Y)? HitMarker { get; set; }
    /// <summary>
    /// The downed creature's footprint (its slot's icon size DS:5F2A, read before 0x31772 takes it off the map):
    /// 0x315FE–0x31682 draws the picture on every footprint cell (0x3073A, i = 0..3).
    /// </summary>
    public int HitMarkerSize { get; set; } = 1;
    /// <summary>A COMSPR picture drawn over a square (PrintNameStringAnim's frames): square, block, mirrored.</summary>
    public (int X, int Y, int Sprite, bool Mirror)? Flash { get; set; }
    /// <summary>
    /// The moving icon (START 0x25C9): its position in thirds of a square (8 pixels) from the view's upper-left corner,
    /// the COMSPR picture (+ 128 for a cell's second picture) and mirrored. Null = none.
    /// </summary>
    public (int Cx, int Cy, int Sprite, bool Mirror)? Flying { get; set; }
    /// <summary>Creatures drawn in their attack pose.</summary>
    public HashSet<EclCharacter> Attacking { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>
    /// The status panel's text area (columns 23–38, rows 1–21) with the program's text cursor (DS:5E24 / DS:5E25):
    /// the messages START 0x16EC and 0x77F8 write from row 10 down. The creature's lines (rows 1–7) are drawn live.
    /// </summary>
    public TextWindow Panel { get; } = new(PanelLeft, 1, PanelRight, PanelBottom);
    public const int PanelLeft = 23, PanelRight = 38, PanelBottom = 21, MessageTop = 10;
    /// <summary>
    /// The panel's name colour and HP as START 0x1583 last drew them for <see cref="Shown"/>: the program does not redraw
    /// those lines when the creature takes damage or leaves play (DOSBox-X s37: a vampire killed by Magic Missile kept
    /// its yellow name and HP 1 through "takes … damage" and the death text). Null, or another creature, = drawn live.
    /// </summary>
    public (EclCharacter Creature, byte NameColour, int Hp)? ShownLines { get; set; }

    /// <summary>The panel shows <paramref name="c"/> (START 0x1583): its lines as they are now.</summary>
    public void ShowLines(EclCharacter c)
    {
        Shown = c;
        ShownLines = (c, CombatChooser.NameColour(c), c[FightRules.Hp]);
    }

    /// <summary>While the status panel's weapon name types: the cells shown so far (null = all).</summary>
    public int? WeaponTyped { get; set; }
    /// <summary>Row 23 (under the map frame): "RANGE = n", "MOVE … = n" and the like.</summary>
    public string? StatusLine { get; set; }
    /// <summary>Row 24 during an AI creature's move (0xB396 and its moves left, AiMoveStep), green; null = none.</summary>
    public string? MovePrompt { get => _movePrompt; set { _movePrompt = value; if (value is not null) Row24Written(); } }
    private string? _movePrompt;

    /// <summary>
    /// Counts the writes and clears of row 24 outside the menus (an AI's start 0x2CFCA, its move prompt, a START 0x1693
    /// message): the last menu drawn stays on the row until one of them (CombatScreen draws it during waits).
    /// </summary>
    public int Row24Writes { get; private set; }

    public void Row24Written() => Row24Writes++;
    /// <summary>Monster icon pictures by icon slot (record 0xBF), from LoadCombatIcon (LOADMON, ADDNPC, the duel).</summary>
    public Dictionary<int, int> IconPictures { get; } = [];
    /// <summary>The game speed DS:49E2 (0 fastest … 9), shared with the Done menu's Speed.</summary>
    public byte Speed { get; set; }
    /// <summary>A 3D area (DUNGCOM ground) or the wilderness (WILDCOM).</summary>
    public bool Dungeon { get; set; } = true;
    /// <summary>The character screen over the whole frame (View, Items, drop money), or null for the map.</summary>
    public CombatPage? Page { get; set; }
    /// <summary>The text window handler 131 draws over the current screen during its pause.</summary>
    public TextWindow? ItemEffectText { get; set; }
    /// <summary>The message routine's line (START 0x1693: row 24, green) while a page waits out its pause.</summary>
    public string? PageMessage { get => _pageMessage; set { _pageMessage = value; if (value is not null) Row24Written(); } }
    private string? _pageMessage;

    /// <summary>
    /// The view's band test and move (GAME.OVR 0x30F0E, read s19): the view's centre is (view x + 3, view y + 3); a
    /// point within <paramref name="delta"/> squares of it on both axes leaves the view alone (delta 255: never, with
    /// a band of 0). Otherwise, on each axis where the point is outside the band, the centre steps toward the point
    /// until it reaches it, x kept in 3..46 and y in 3..21 (the whole 7 × 7 view stays in the 50 × 25 arena). True when
    /// the view was redrawn.
    /// </summary>
    public bool Rebuild(int x, int y, int delta)
    {
        int cx = ViewX + 3, cy = ViewY + 3;
        int d = delta == 255 ? 0 : delta;
        int minX = cx - d, maxX = cx + d, minY = cy - d, maxY = cy + d;
        if (delta != 255 && x >= minX && x <= maxX && y >= minY && y <= maxY)
            return false;
        if (x < minX)
            while (x < cx && cx > 3) cx--;
        else if (x > maxX)
            while (x > cx && cx < 46) cx++;
        if (y < minY)
            while (y < cy && cy > 3) cy--;
        else if (y > maxY)
            while (y > cy && cy < 21) cy++;
        ViewX = cx - 3;
        ViewY = cy - 3;
        return true;
    }

    /// <summary>
    /// ScrollView (0x310D0, read s19): the target square is (<paramref name="x"/>, <paramref name="y"/>) plus one step
    /// in <paramref name="dir"/> (8 = none); the view moves for it (<see cref="Rebuild"/>; a redrawn view has lost its
    /// cursor box), the cell (x, y) is redrawn (a box there goes), and with <paramref name="cursorSize"/> (the arena's
    /// cursor flag set) the box is drawn on the target square with that footprint.
    /// The attack pose is not kept anywhere in the program: 0x31232 draws frame 1 into the view once. A rebuilt view
    /// draws every creature in play with frame 0 (0x3119F), and the cell redraw 0x30A0F draws the creature on (x, y)
    /// with frame 0 (0x30AB7), so both put an attacker back in its standing pose (read s38).
    /// </summary>
    public void Scroll(int x, int y, int delta, int dir = CombatTables.Center, int? cursorSize = null)
    {
        int tx = x + CombatTables.DirDx[dir], ty = y + CombatTables.DirDy[dir];
        if (Rebuild(tx, ty, delta))
        {
            Cursor = null;
            Attacking.Clear();
        }
        if (Fight?.Map is CombatMap map && map.At(x, y) is { } occupant)
            Attacking.Remove(occupant);
        if (Cursor == (x, y))
            Cursor = null;
        if (cursorSize is { } size)
        {
            Cursor = (Math.Clamp(tx, 0, CombatArena.Width - 1), Math.Clamp(ty, 0, CombatArena.Height - 1));
            CursorSize = size;
        }
    }

    /// <summary>Whether a square is inside the 7 × 7 view (ViewCoordsValid 0x30E23 on the view-relative square).</summary>
    public bool InView(int x, int y) =>
        x - ViewX is >= 0 and < ViewSquares && y - ViewY is >= 0 and < ViewSquares;

    /// <summary>Centre the view on a square (the aim menu's and the cursor's Center: ScrollView with delta 0).</summary>
    public void Center(int x, int y) => Scroll(x, y, 0);

    public void Clear()
    {
        Fight = null;
        Current = Shown = null;
        Cursor = HitMarker = null;
        HitMarkerSize = 1;
        Flash = null;
        Flying = null;
        CursorSize = 1;
        Attacking.Clear();
        Panel.Clear();
        StatusLine = null;
        MovePrompt = null;
        Page = null;
        ItemEffectText = null;
        PageMessage = null;
        Mode = CombatMode.Watch;
    }
}

/// <summary>
/// The player's side of the fight for the Client (<see cref="IFightMenuChooser"/>, on the logic thread): every
/// question the menu (GAME.OVR 0x9B21; docs/re/combat-menu.md) asks becomes a <see cref="PlayerRequest"/> the combat
/// screen answers with keyboard, mouse or pad. The menus are the program's (same words and order); the AI's and the
/// party's actions are shown through the fight's display hooks, each with a pause.
/// </summary>
public sealed partial class CombatChooser(Func<PlayerRequest, object?> ask, CombatView view) : IFightMenuChooser, IItemScreenChooser
{
    public CombatView View { get; } = view;
    public CombatText Text { get; set; } = new();
    /// <summary>Spell names by spell id (null = "SPELL n").</summary>
    public Func<int, string?> SpellName { get; set; } = _ => null;
    /// <summary>Pauses (the attack messages, a step, a turn change); false in tests.</summary>
    public bool Pauses { get; set; } = true;
    /// <summary>DS:4A00 (<see cref="GoldBox.Core.Ecl.EclMemory.TypesText"/>): the panel's texts type out while it is set.</summary>
    public Func<bool> Typing { get; set; } = () => true;

    /// <summary>The creature a click on the map picked while the aim menu is up: the aim menu steps to it (Next).</summary>
    private EclCharacter? _goal;
    private int _goalSteps;
    /// <summary>The square a click sent the manual cursor to (it walks there one square per pass).</summary>
    private (int X, int Y)? _cursorGoal;
    private int _cursorSteps;

    // ---- Wiring --------------------------------------------------------------------------------------------------

    /// <summary>Shows <paramref name="fight"/> on the combat screen: hooks for turns, attacks, steps and messages.</summary>
    public void Attach(Fight fight)
    {
        View.Fight = fight;
        View.Mode = CombatMode.Watch;
        if (fight.Map is CombatMap map)
        {
            View.ViewX = map.Arena.ViewX;
            View.ViewY = map.Arena.ViewY;
        }
        if (fight.Controller is MenuController mc)
            mc.GameSpeed = View.Speed;
        fight.TurnStarted = TurnStarted;
        fight.TurnEnded = TurnEnded;
        fight.AttackStarting = AttackStarting;
        fight.DefenderTurned = DefenderTurned;
        fight.AttackPanel = AttackPanel;
        fight.AttackPosed = AttackPosed;
        fight.AttackMade = AttackMade;
        fight.State.AttackMessage = AttackMessage;
        fight.State.AttackEnd = AttackEnd;
        fight.State.FrontEndPlaysDowned = true;
        fight.State.FleeMessage = FleeMessage;
        fight.State.Flight = Flight;
        fight.State.Posed = Posed;
        fight.PartyMemberDying = () => PageSay(Text[CombatMsg.TeammateDying]); // 0xA134 (DOSBox-X s29: row 24, pause)
        fight.PanelShown = c => ShowPanel(c);
        fight.Guarded = _ => PageSay(Text[CombatMsg.Guarding]); // START 0x3226 (DOSBox-X s30: row 24, pause, cleared)
        fight.Moves = new ShownMoves(fight.Moves, Stepping, Stepped);
        if (fight.Spells is FightSpells spells)
        {
            spells.Announce = Announce;
            spells.CastDone = CastDone;
            spells.ItemUsed = ItemUsed;
            spells.Event += SpellEvent;
            spells.Effects.Event += EffectMessage;
        }
        if (fight.Controller is MenuController { Ai: MonsterAi ai })
        {
            ai.Message = AiSays;
            // 0xABCF clears row 24 (0x2CFCA) at its start, also after the menu's Quick (D s33: the menu row goes)
            ai.Starting = _ =>
            {
                View.MovePrompt = null;
                View.Row24Written();
            };
            // 0xBF8A's redraw (START 0x1583 at 0xC4D7) when its readying changed anything: the panel cleared and drawn
            // again, the weapon typed from an empty line (DOSBox-X s39: after Quick, "11 DARTS" typed again).
            ai.PanelRedrawn = c => ShowPanel(c);
        }
        // Nothing in a fight writes DS:4A00 (the only writers are the start-up 0xE974, PRINT 0x1763 / 0x17DC, ENCMENU
        // 0x2C4F / 0x32E0 and the after-combat end 0x6BAF): the panel's texts through START 0x77F8 type out when the
        // fight starts with it set (after the start-up or a fight, no PRINT or ENCMENU since), else they show at once.
        View.Panel.Typed = TypeDelay;
    }

    /// <summary>
    /// The fight start's line (0xE430–0xE452), on the area screen before the combat screen comes: row 24 cleared
    /// (0x3C5:0x2F = 0x2CFCA, START 0x767B over columns 0–39), PauseOnly (Delay(DS:49E2 × 225) ms; speed 0 waits
    /// nothing) with row 24 blank, then the text at column 0, row 24, colour 10 through START 0x76B6 (one glyph call
    /// per character, not typed), and no pause after it: the program goes on to build the arena and draw the combat
    /// screen. Every fight passes here (0x9881's one call to 0xE402, before its "done at once" test). How long the line
    /// stays up is the time of those loads and builds on the machine `[?]`; the Client's take none, so the line holds
    /// one Client update (the runner hands it to the screen before that frame's draw). Call before <see cref="Attach"/>
    /// (the combat screen takes over drawing once attached).
    /// </summary>
    public void BattleBegins()
    {
        if (!Pauses)
            return; // the tests' setting: no waits at all
        if (View.Speed is > 0 and <= 9)
            ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * View.Speed)) { BottomLine = ("", StartLineColour) });
        ask(new DelayRequest(TimeSpan.Zero) { BottomLine = (Text[CombatMsg.BattleBegins], StartLineColour) });
    }

    /// <summary>The fight start's line colour (0xE440: 10, light green).</summary>
    public const byte StartLineColour = 10;

    /// <summary>The typing delay after each character (START 0x777B–0x778D): Delay(DS:49E2 × 10) ms; speed 0 waits nothing.</summary>
    private void TypeDelay()
    {
        if (View.Speed > 0 && Pauses && Typing())
            ask(new DelayRequest(TimeSpan.FromMilliseconds(10 * View.Speed)) { Chained = true });
    }

    public void Detach(Fight fight)
    {
        if (fight.Controller is MenuController mc)
            View.Speed = mc.GameSpeed;
        fight.TurnStarted = null;
        fight.TurnEnded = null;
        fight.AttackStarting = null;
        fight.DefenderTurned = null;
        fight.AttackPanel = null;
        fight.AttackPosed = null;
        fight.AttackMade = null;
        fight.State.AttackMessage = null;
        fight.State.AttackEnd = null;
        fight.State.FrontEndPlaysDowned = false;
        fight.State.FleeMessage = null;
        fight.State.Flight = null;
        fight.State.Posed = null;
        fight.PartyMemberDying = null;
        fight.Guarded = null;
        fight.PanelShown = null;
        if (fight.Spells is FightSpells spells)
        {
            spells.Announce = null;
            spells.CastDone = null;
            spells.ItemUsed = null;
            spells.Event -= SpellEvent;
            spells.Effects.Event -= EffectMessage;
        }
        if (fight.Controller is MenuController { Ai: MonsterAi ai })
        {
            ai.Message = null;
            ai.PanelRedrawn = null;
        }
        _menu = null;
        _announced = null;
        _goal = null;
        _cursorGoal = null;
        View.Panel.Typed = null;
        View.Clear();
    }

    private (int X, int Y)? Square(EclCharacter c) =>
        View.Fight?.Map is CombatMap m && m.OnMap(c) ? m.Position(c) : null;

    private int IconSize(EclCharacter c) => View.Fight?.Map is CombatMap m && m.OnMap(c) ? m.IconSize(c) : 1;

    /// <summary>Where each creature stood when last seen (a dropped monster leaves the map before the message).</summary>
    private readonly Dictionary<EclCharacter, (int X, int Y)?> _lastSquare = new(ReferenceEqualityComparer.Instance);
    /// <summary>Each creature's icon size when last seen on the map (the downed picture's footprint).</summary>
    private readonly Dictionary<EclCharacter, int> _lastSize = new(ReferenceEqualityComparer.Instance);

    private void Remember()
    {
        if (View.Fight is not { } f)
            return;
        foreach (var x in f.List)
            if (Square(x) is { } p)
            {
                _lastSquare[x] = p;
                _lastSize[x] = IconSize(x);
            }
    }

    /// <summary>
    /// DisplayMapViewCentered (0x319D2, read s19): ScrollView on the creature's square with the arena's cursor flag set
    /// to <paramref name="cursor"/> and the cursor footprint = its icon size.
    /// </summary>
    private void Centered(EclCharacter c, int delta, bool cursor)
    {
        if (Square(c) is { } p)
            View.Scroll(p.X, p.Y, delta, CombatTables.Center, cursor ? IconSize(c) : null);
    }

    /// <summary>
    /// The status panel (START 0x1583): the creature's lines and an empty message area (it clears columns 23–38, rows
    /// 1–21 first).
    /// </summary>
    private void ShowPanel(EclCharacter c, bool type = true)
    {
        View.ShowLines(c);
        View.Panel.Clear();
        View.WeaponTyped = null;
        // The weapon's name goes through 0x77F8, so it types while DS:4A00 is set (Delay(DS:49E2 × 10) after each
        // character, spaces too); the name, HP and AC lines are fixed-cell strings and show at once. DOSBox-X s29: the
        // weapon typed letter by letter on every panel (turns, attacks, each aim candidate) in a fight entered by a
        // step's COMBAT after a load.
        if (!type || View.Speed == 0 || !Pauses || !Typing() || View.Fight is not { } fight
            || FightRules.Equipment(c, fight.Items)[FightRules.SlotWeapon] is not { } w)
            return;
        int n = WeaponCells(ItemNames.Compose(w, Resources().ItemWord));
        for (int i = 1; i <= n; i++)
        {
            View.WeaponTyped = i;
            ask(new DelayRequest(TimeSpan.FromMilliseconds(10 * View.Speed)) { Chained = true });
        }
        View.WeaponTyped = null;
    }

    /// <summary>The cells the panel's weapon name fills when word-wrapped into (23, 7)–(38, 9), spaces included.</summary>
    internal static int WeaponCells(string name)
    {
        var box = new TextWindow(CombatView.PanelLeft, 7, CombatView.PanelRight, 9);
        box.PrintIn(CombatView.PanelLeft, 7, CombatView.PanelRight, 9, name, true, 10);
        int n = 0;
        for (int row = 7; row <= 9; row++)
            for (int col = CombatView.PanelLeft; col <= CombatView.PanelRight; col++)
                if (box.Cell(row, col) is ({ } ch and not '\0', _))
                    n++;
        return n;
    }

    /// <summary>
    /// The end of a cast (0x27F44–0x27F5C): rows 10–21 cleared, then row 23 (the spell label) cleared in a fight
    /// (DOSBox-X s29: Brother Sean's Bless, the label gone with the last blessed message).
    /// </summary>
    private void CastDone(EclCharacter caster)
    {
        ClearMessageWindow();
        View.StatusLine = null;
    }

    /// <summary>
    /// The cast's facing (0x27E00) draws frame 1 and clears the cursor. A travelling spell then rebuilds the view
    /// with frame 0 before its first delay (START 0x27BB), and 0x27EA4 restores frame 0 when the caster is visible.
    /// MakeAttack (0x14113) pauses on frame 1 before it rolls. 0x31232 first scrolls the view to the caster
    /// (ScrollView(its square, 3)) when it is not wholly in view, as for the attack pose: DOSBox-X s41, Darkstar's Sleep
    /// aimed by hand four squares off the view's edge — the view comes back onto Darkstar in the action pose before the
    /// flight (the pose itself stays up only for the machine's drawing time, no wait in the program).
    /// </summary>
    private void Posed(EclCharacter c)
    {
        if (Square(c) is not { } sq)
            return;
        if (!WhollyInView(c))
            View.Scroll(sq.X, sq.Y, 3);
        if (View.Cursor == sq)
            View.Cursor = null;
    }

    /// <summary>
    /// A creature's turn (0x99FD, read s19): 0x319D2(creature, 2, cursor on): the view keeps the creature within 2
    /// squares of its centre and the cursor box is drawn under it, for the AI's creatures too; then the panel (START
    /// 0x1583). The box stays until that cell is redrawn (a step, an attack pose, a scrolled view, the turn's end). The
    /// program waits nothing here.
    /// </summary>
    internal void TurnStarted(EclCharacter c)
    {
        Remember();
        View.Page = null;
        _announced = null; // a cast-now spell the list announced but never cast (no target) is forgotten
        View.Current = c;
        View.HitMarker = null;
        View.Attacking.Clear();
        View.StatusLine = null;
        Centered(c, 2, true);
        // A menu-driven creature's panel is drawn again by the menu loop (0x9B21) right away: it types once there
        // (DOSBox-X s29: one typing per turn).
        ShowPanel(c, c[CombatRules.AutoCombat] != 0);
        if (c[CombatRules.AutoCombat] != 0)
        {
            View.Mode = CombatMode.Watch;
            Frame();
            // The AI's start (0xABCF): row 24 (0x2CFCA) and the message area (START 0x2383) are cleared; until then the
            // last creature's move prompt is still up (DOSBox-X s19: Darkstar's box and panel under Rhiannon's prompt).
            View.MovePrompt = null;
            View.Row24Written();
            ClearMessageWindow();
        }
    }

    /// <summary>
    /// The turn's end (0x99FD, 0x9AD9–0x9AF1): the cell at the creature's square is drawn again, so its cursor box goes
    /// (DOSBox-X s30: no box under the last member when the continue-battle question comes).
    /// </summary>
    internal void TurnEnded(EclCharacter c)
    {
        if (Square(c) is { } sq && View.Cursor == sq)
            View.Cursor = null;
    }

    /// <summary>
    /// Before a step: an AI creature's move prompt (AiMoveStep prints 0xB396 and its movement left ÷ 2 on row 24 before
    /// it steps; the line stays until the screen's row 24 is written again).
    /// </summary>
    private void Stepping(EclCharacter c)
    {
        if (c[CombatRules.AutoCombat] != 0 && View.Fight is { } f)
            View.MovePrompt = Text[CombatMsg.AiMoveLeft] +
                (f.Record(c)[CombatRecord.Movement] / 2).ToString(CultureInfo.InvariantCulture) + " ";
    }

    /// <summary>
    /// A step (0x12F0D, read s19): the old cell is redrawn without the creature (a cursor box there goes); a player's
    /// creature (0x10F = 0) then gets ScrollView(new square, 1); an AI's creature first ScrollView(old square, 2) when the
    /// new square is off the view, then ScrollView(new square, 3). No wait of the program's own: the step is shown for
    /// one frame.
    /// </summary>
    private void Stepped(EclCharacter c, (int X, int Y)? from)
    {
        Remember();
        View.HitMarker = null;
        if (Square(c) is not { } p)
            return;
        if (from is { } old && View.Cursor == old)
            View.Cursor = null;
        bool ai = c[CombatRules.AutoCombat] != 0;
        if (ai && from is { } o && !View.InView(p.X, p.Y))
            View.Scroll(o.X, o.Y, 2);
        View.Scroll(p.X, p.Y, ai ? 3 : 1);
        if (ai)
            Frame();
    }

    private static string Name(EclCharacter c) => c.Name.ToUpperInvariant();

    /// <summary>
    /// The colour of a creature's name (START 0x23B6, read s19): out of play (0x10D = 0) light red, hostile (0x10E = 1)
    /// yellow, else cyan.
    /// </summary>
    public static byte NameColour(EclCharacter c) =>
        c[FightRules.InPlay] == 0 ? (byte)12 : c[FightRules.Hostile] == 1 ? (byte)14 : (byte)11;

    // ---- The panel's message routines -------------------------------------------------------------------------------

    /// <summary>
    /// START 0x16EC in a fight (read s19): columns 23–38 from <paramref name="row"/> to 21 cleared, the creature's name
    /// at (23, row) in its colour (<paramref name="nameColour"/>, else <see cref="NameColour"/>), the text word-wrapped
    /// into (23, row + 1)–(38, 21) in green (START 0x77F8, cleared first); with <paramref name="pause"/> the game-speed
    /// pause, then the message area is cleared (START 0x2383).
    /// </summary>
    private void PrintCreatureMessage(EclCharacter? c, string text, int row, bool pause, byte? nameColour = null)
    {
        var p = View.Panel;
        p.ClearRect(CombatView.PanelLeft, row, CombatView.PanelRight, CombatView.PanelBottom);
        if (c is not null)
            p.Put(row, CombatView.PanelLeft, Name(c), nameColour ?? NameColour(c));
        p.PrintIn(CombatView.PanelLeft, row + 1, CombatView.PanelRight, CombatView.PanelBottom, text, true, 10);
        if (pause)
        {
            Pause(MessageMs);
            ClearMessageWindow();
        }
    }

    /// <summary>START 0x2383 in a fight: columns 23–38, rows 10–21 cleared.</summary>
    private void ClearMessageWindow() =>
        View.Panel.ClearRect(CombatView.PanelLeft, CombatView.MessageTop, CombatView.PanelRight, CombatView.PanelBottom);

    /// <summary>A creature and a text in the panel from row 10 with the pause (START 0x16EC with pause on).</summary>
    /// <summary>START 0x2EEA's words: fully healed when HP 0x11B equals the maximum 0x32 (00BA:2353), else partially.</summary>
    private string HealedWords(EclCharacter c) => Text[c[0x11B] == c[0x32] ? CombatMsg.FullyHealed : CombatMsg.PartiallyHealed];

    private void Say(EclCharacter? who, string text)
    {
        View.Page = null;
        View.Mode = CombatMode.Watch;
        PrintCreatureMessage(who, text, CombatView.MessageTop, true);
    }

    // ---- Attacks (0x1407F, 0x12AF4) -----------------------------------------------------------------------------------

    /// <summary>
    /// MakeAttack's entry (0x1407F). An AI creature's attack has just scrolled toward the target (0xBA22: ScrollView(its
    /// square, 2, direction to the target); a cell redraw there, the cursor box goes). The facings are not changed yet.
    /// </summary>
    internal void AttackStarting(EclCharacter attacker, EclCharacter defender)
    {
        Remember();
        View.Page = null;
        View.Mode = CombatMode.Watch;
        View.StatusLine = null;
        if (attacker[CombatRules.AutoCombat] != 0 && Square(attacker) is { } a && View.Fight?.Map is CombatMap map)
            View.Scroll(a.X, a.Y, 2, map.DirectionToward(attacker, defender));
    }

    /// <summary>
    /// The defender turns to face the attacker (0x14085–0x140E1, read s39): when any of its cells is in view (0x30E54
    /// with 0), 0x31232 with frame 0 draws it, first scrolling the view to it (ScrollView(its square, 3)) when it is
    /// not wholly in view. DOSBox-X s39: Darkstar's darts at the wild boar half in view — the view moves onto the boar
    /// before Darkstar's panel types, and comes back with Darkstar's pose.
    /// </summary>
    private void DefenderTurned(EclCharacter defender)
    {
        if (AnyCellInView(defender))
            FacingDrawn(defender, 0);
    }

    /// <summary>MakeAttack's panel (START 0x1583 at 0x140FE): the attacker still in its old facing.</summary>
    private void AttackPanel(EclCharacter attacker) => ShowPanel(attacker);

    /// <summary>
    /// The attack pose (0x31232 with frame 1 at 0x14113, its facing now set: the cell is redrawn, a cursor box there
    /// goes; scrolled to first when not wholly in view) and Delay(100) (0x1412E).
    /// </summary>
    private void AttackPosed(EclCharacter attacker)
    {
        FacingDrawn(attacker, 1);
        Pause(AttackPoseMs, animation: true);
    }

    /// <summary>
    /// 0x31232(creature, facing, frame): when the creature is not wholly in view (0x30E54 with 1), ScrollView(its
    /// square, 3, none) first; then its cell is drawn with the frame (1 = the action pose).
    /// </summary>
    private void FacingDrawn(EclCharacter c, int frame)
    {
        if (Square(c) is not { } sq)
            return;
        if (!WhollyInView(c))
            View.Scroll(sq.X, sq.Y, 3);
        if (View.Cursor == sq)
            View.Cursor = null;
        if (frame == 1)
            View.Attacking.Add(c);
        else
            View.Attacking.Remove(c);
    }

    /// <summary>0x30E54(creature, 0): it has an icon on the map and at least one footprint cell is in the view.</summary>
    private bool AnyCellInView(EclCharacter c)
    {
        if (View.Fight?.Map is not CombatMap map || !map.OnMap(c))
            return false;
        var (x, y) = map.Position(c);
        for (int i = 0; i < 4; i++)
            if (map.Tables.IconCell(map.IconSize(c), i, out int dx, out int dy) && View.InView(x + dx, y + dy))
                return true;
        return false;
    }

    /// <summary>The fixed wait after the attack pose is drawn (Delay(100) at 0x1412E).</summary>
    private const int AttackPoseMs = 100;

    /// <summary>
    /// The attack message routine 0x12AF4 (read s19), one call per hit or for the miss: START 0x16EC(attacker, the
    /// attacks word (backstab and slain-helpless words by manner), row 10); the defender's name at (23, 12) in the
    /// colour it had before the damage; the result wrapped into (23, 13)–(38, 16) (START 0x77F8): "(from behind) " for
    /// manner 1, then the cruel-blow words (manner 3), or the hitting words, the number and the point / points and
    /// damage words, or the miss words. Then the pause (damage with a spell being cast: the pause, then the lost-spell
    /// message at row 12 with its pause). A defender out of play: START 0x16EC(defender, the down words) at the
    /// cursor row + 1, then two rows on the dying words at column 23 (status 5) or START 0x16EC(defender, the killed
    /// words) (status 6–8), then the pause (CreatureDowned 0x3154B's or its own); last the message area is cleared.
    /// </summary>
    internal void AttackMessage(AttackMessage m)
    {
        var t = Text;
        View.Page = null;
        View.Mode = CombatMode.Watch;
        string word = m.Manner switch
        {
            2 => t[CombatMsg.Backstabs],
            3 => t[CombatMsg.SlaysHelpless],
            _ => t[CombatMsg.Attacks],
        };
        PrintCreatureMessage(m.Attacker, word, CombatView.MessageTop, false);
        var p = View.Panel;
        byte defenderColour = !m.DefenderWasInPlay ? (byte)12 : m.Defender[FightRules.Hostile] == 1 ? (byte)14 : (byte)11;
        p.Put(12, CombatView.PanelLeft, Name(m.Defender), defenderColour);
        string text = m.Manner == 1 ? t[CombatMsg.FromBehind] : "";
        if (m.Hit)
        {
            if (m.Manner == 3)
                text = t[CombatMsg.CruelBlow];
            else
                text += t[CombatMsg.HittingFor] + m.Damage.ToString(CultureInfo.InvariantCulture) +
                    (m.Damage == 1 ? t[CombatMsg.Point] : t[CombatMsg.Points]) + t[CombatMsg.OfDamage];
        }
        else
            text += t[CombatMsg.AndMisses];
        p.PrintIn(CombatView.PanelLeft, 13, CombatView.PanelRight, 16, text, true, 10);
        int row = p.Row + 1;
        Pause(MessageMs);
        if (m.SpellLost)
            PrintCreatureMessage(m.Defender, t[CombatMsg.LostSpell], 12, true);
        if (m.Defender[FightRules.InPlay] == 0)
        {
            PrintCreatureMessage(m.Defender, t[CombatMsg.GoesDown], row, false);
            row += 2;
            byte status = m.Defender[FightRules.Status];
            if (status == 5)
                p.Put(row, CombatView.PanelLeft, t[CombatMsg.AndIsDying], 10);
            if (status is 6 or 7 or 8)
                PrintCreatureMessage(m.Defender, t[CombatMsg.IsKilled], row, false);
            Remember();
        }
    }

    /// <summary>
    /// The end of 0x12AF4 (0x12DE1–0x12E17, read s29), after Core's 0x2BEF4 and trigger 0x0D (whose handlers may print
    /// over the panel, e.g. handler 32's text through 0xECB3): a defender that went down and is still out of play gets
    /// CreatureDowned 0x3154B's picture and pause, one back in play PauseOnly; then START 0x17E3 clears rows 10–21 (a
    /// defender still up: only the clear).
    /// </summary>
    internal void AttackEnd(AttackMessage m, bool down)
    {
        if (down)
            DownedOrPause(m.Defender);
        ClearMessageWindow();
    }

    /// <summary>A creature still out of play: CreatureDowned's display (<see cref="Downed"/>); else PauseOnly.</summary>
    private void DownedOrPause(EclCharacter c)
    {
        if (c[FightRules.InPlay] == 0)
            Downed(c);
        else
            Pause(MessageMs);
    }

    /// <summary>
    /// CreatureDowned's display (0x3154B, read s19), in a fight: the downed picture (icon cell 24 = COMSPR 11, loaded at
    /// the fight start 0xEADC with COMSPR i into cell i + 13) on the creature's square, the pause, then the creature
    /// leaves the map (Core's <see cref="CombatMap.Downed"/>). First (0x315CD–0x315ED), when the creature is not wholly
    /// in view (0x30E54 with 1; false too once it has no icon on the map), ScrollView(its square, 3): a rebuilt view
    /// draws the attacker standing (<see cref="CombatView.Scroll"/>); otherwise nothing redraws the attacker, which keeps
    /// the pose MakeAttack drew until its end 0x142C5 (or the standing pose a missile's flight left, read s38).
    /// </summary>
    private void Downed(EclCharacter c)
    {
        var sq = Square(c) ?? (_lastSquare.TryGetValue(c, out var last) ? last : null);
        int size = Square(c) is not null ? IconSize(c) : _lastSize.GetValueOrDefault(c, 1);
        var sound = View.Fight?.State is { FrontEndPlaysDowned: true } st ? st.Sound : null;
        sound?.BeginResident(); // 0x31554: set auto-stop(0)
        if (sq is { } s && !WhollyInView(c))
            View.Scroll(s.X, s.Y, 3);
        View.HitMarker = sq;
        View.HitMarkerSize = size;
        sound?.Cue(GoldBox.Core.Sound.SoundCue.Body); // 0x31744, the picture drawn (DOSBox-X s39: the tune starts with it)
        Pause(MessageMs);
        View.HitMarker = null;
        // 0x3177B–0x3179C: after the pause and the map removal, ScrollView(view centre, 3, none): no rebuild, but the
        // cell redraw 0x30A0F of the centre square draws the creature there standing, so an attacker on the view's
        // centre loses its pose with the downed picture (DOSBox-X s39: the vampire at the centre over Rhiannon; beside
        // or above the centre it kept the pose until the attack's end).
        View.Scroll(View.ViewX + 3, View.ViewY + 3, 3);
        sound?.EndResident(); // 0x317D6: set auto-stop(10)
    }

    /// <summary>0x30E54(creature, 1): it has an icon on the map and every footprint cell is in the view.</summary>
    private bool WhollyInView(EclCharacter c)
    {
        if (View.Fight?.Map is not CombatMap map || !map.OnMap(c))
            return false;
        var (x, y) = map.Position(c);
        for (int i = 0; i < 4; i++)
            if (map.Tables.IconCell(map.IconSize(c), i, out int dx, out int dy) && !View.InView(x + dx, y + dy))
                return false;
        return true;
    }

    /// <summary>The end of MakeAttack (0x142C5–0x1430E): the attacker is drawn again in its standing pose.</summary>
    private void AttackMade(EclCharacter attacker, EclCharacter defender, FightRules.AttackOutcome outcome, int hpBefore)
    {
        View.Attacking.Remove(attacker);
        View.HitMarker = null;
    }

    // ---- Non-weapon damage and effects (0x2C22E, 0x2C540) -------------------------------------------------------------

    /// <summary>
    /// The effect engine's messages in a fight. Non-weapon damage (0x2C22E, read s19), before the damage is dealt: the
    /// text "takes n points of damage " (n = 1: its own string) plus the element by DS:6819 AND 0xF7 (1 fire, 2 cold, 4
    /// electricity, 0x10 acid) and the magic words when DS:6819 AND 8 = DS:6819 (also for 0), shown by 0x2B8B
    /// (<see cref="ShowHit"/>). A spell being cast lost: START 0x16EC at row 12 with the pause. Out of play (0x2C44A):
    /// the down words, ", and is dying" added for status 5, the killed words instead for 6–8, START 0x16EC at the cursor
    /// row + 1; its end (<see cref="EffectEvent.GoesDownDone"/>) brings the downed picture or the pause and the clear.
    /// An effect resisted (0x2C540): the unaffected words, START 0x16EC at row 10 with the pause. 0xECB3's callers and
    /// the messages before them: docs/re/spells-effects.md "Status changes".
    /// </summary>
    internal void EffectMessage(EclCharacter c, EffectEvent e)
    {
        if (View.Fight is not { } fight)
            return;
        var t = Text;
        if (fight.Spells is FightSpells { Effects.EffectText: { } key })
        {
            EffectTextMessage(c, key);
            return;
        }
        switch (e)
        {
            case EffectEvent.NonWeaponDamage:
            {
                byte n = fight.State.PendingDamage, mask = fight.State.DamageFlags;
                string s = n != 1
                    ? t[CombatMsg.Takes] + n.ToString(CultureInfo.InvariantCulture) + t[CombatMsg.PointsOfDamage]
                    : t[CombatMsg.TakesOnePoint];
                s += (mask & 0xF7) switch
                {
                    1 => t[CombatMsg.FromFire],
                    2 => t[CombatMsg.FromCold],
                    4 => t[CombatMsg.FromElectricity],
                    0x10 => t[CombatMsg.FromAcid],
                    _ => "",
                };
                if ((mask & 8) == mask)
                    s += t[CombatMsg.FromMagic];
                // START 0x2B8B copies its text into a string[40] (0x2B9B): longer damage lines are cut (DOSBox-X s29:
                // "takes 40 points of damage from electricity" ends after 40 characters).
                ShowHit(c, s.Length > 40 ? s[..40] : s);
                break;
            }
            case EffectEvent.CastingLost:
                View.Page = null;
                PrintCreatureMessage(c, t[CombatMsg.SpellLostSpell], 12, true);
                break;
            case EffectEvent.GoesDown:
            {
                string s = t[CombatMsg.SpellGoesDown];
                if (c[FightRules.Status] == 5)
                    s += t[CombatMsg.SpellAndIsDying];
                if (c[FightRules.Status] is 6 or 7 or 8)
                    s = t[CombatMsg.SpellIsKilled];
                Remember();
                PrintCreatureMessage(c, s, View.Panel.Row + 1, false);
                break;
            }
            case EffectEvent.DamageDone:
                ClearMessageWindow(); // 0x2C527: START 0x17E3
                break;
            case EffectEvent.GoesDownDone:
                // 0x2C4F3–0x2C527: after Core's 0x2BEF4, trigger 0x0D and map removal, the creature still out of play
                // goes through CreatureDowned (its picture and pause), else the pause alone; then START 0x17E3.
                DownedOrPause(c);
                ClearMessageWindow();
                break;
            case EffectEvent.PoisonDeath:
                StatusMessage(c, t[CombatMsg.StatusPoisonDeath]);
                break;
            case EffectEvent.Collapses:
                StatusMessage(c, t[CombatMsg.StatusCollapses]);
                break;
            case EffectEvent.RotsAway:
                StatusMessage(c, t[CombatMsg.StatusRotsAway]);
                break;
            case EffectEvent.PoisonKilled:
                StatusMessage(c, t[CombatMsg.StatusPoisonKilled]);
                break;
            case EffectEvent.DeadForever:
                StatusMessage(c, t[CombatMsg.StatusDeadForever]);
                break;
            case EffectEvent.DrainKilled:
                StatusMessage(c, t[CombatMsg.StatusDrainKilled]);
                break;
            case EffectEvent.FallsDead:
                StatusMessage(c, t[CombatMsg.StatusFallsDead]);
                break;
            case EffectEvent.StatusDone:
                // 0xED2E–0xED49: CreatureDowned when still out of play (its picture and pause), then PauseOnly whatever
                // happened, then START 0x17E3 (rows 10–21 cleared).
                if (c[FightRules.InPlay] == 0)
                    Downed(c);
                Pause(MessageMs);
                ClearMessageWindow();
                break;
            case EffectEvent.Poisoned:
                // 0x101D5–0x101F7: START 0x16EC from row 10 without the pause flag, then PauseOnly; nothing cleared.
                HandlerMessage(c, t[CombatMsg.Poisoned], CombatView.MessageTop, false);
                Pause(MessageMs);
                break;
            case EffectEvent.Rots:
                HandlerMessage(c, t[CombatMsg.Rots], CombatView.MessageTop, true); // 0x10004–0x1001F
                break;
            case EffectEvent.Drained:
            {
                // 0x10D84–0x10DEC: the drained words + (count > 1: Str(count) + the levels word; else the one-level words).
                int n = fight.Spells is FightSpells fs ? fs.Effects.DrainCount : 1;
                string s = t[CombatMsg.Drained] + (n > 1
                    ? n.ToString(CultureInfo.InvariantCulture) + t[CombatMsg.DrainedLevels]
                    : t[CombatMsg.DrainedOneLevel]);
                HandlerMessage(c, s, CombatView.MessageTop, true);
                break;
            }
            case EffectEvent.Unaffected:
                Say(c, t[CombatMsg.Unaffected]);
                break;
            case EffectEvent.Gazes:
                HandlerMessage(c, t[CombatMsg.HandlerGazes], CombatView.MessageTop, false);
                break;
            case EffectEvent.GazeReflected:
                HandlerMessage(c, t[CombatMsg.HandlerGazeReflected], 12, false);
                break;
            case EffectEvent.Stoned:
                StatusMessage(c, t[CombatMsg.HandlerStoned]);
                break;
            case EffectEvent.CharmGazes:
                HandlerMessage(c, t[CombatMsg.HandlerCharmGazes], CombatView.MessageTop, false);
                break;
            case EffectEvent.Breathes:
                HandlerMessage(c, t[CombatMsg.HandlerBreathes], CombatView.MessageTop, true);
                break;
            case EffectEvent.SpitsAcid:
                HandlerMessage(c, t[CombatMsg.HandlerSpitsAcid], CombatView.MessageTop, true);
                break;
            // s30 (docs/re/spells-effects.md "Status changes", "Handler messages").
            case EffectEvent.Revived:
                // 0x2C7A3–0x2C804: 0x319D2(creature, 3, 0), then START 0x16EC(row 10, pause 1); the text by 0x10E.
                StatusChangeScroll(c);
                Say(c, t[c[FightRules.Hostile] == 1 ? CombatMsg.StatusRevivedEnemy : CombatMsg.StatusRevived]);
                break;
            case EffectEvent.Healed:
                // Handlers 50 / 62 → START 0x2EEA: START 0x16EC(row 10, pause 1); in a fight (DS:49F3 = 5) no list.
                Say(c, HealedWords(c));
                break;
            case EffectEvent.EscapeView:
                StatusChangeScroll(c); // 0x11534
                break;
            case EffectEvent.Escapes:
                // 0x11534 and 0x2BE31: 0x319D2(creature, 3, 0) (twice), then START 0x16EC(row 10, pause 1); the icon
                // leaves the map after the message (Core).
                StatusChangeScroll(c);
                Say(c, t[CombatMsg.StatusEscapes]);
                break;
            case EffectEvent.Diseased:
                HandlerMessage(c, t[CombatMsg.HandlerDiseased], CombatView.MessageTop, true);
                break;
            case EffectEvent.Rakes:
                HandlerMessage(c, t[CombatMsg.HandlerRakes], CombatView.MessageTop, true);
                break;
            case EffectEvent.HeldFast:
                HandlerMessage(c, t[CombatMsg.HandlerHeldFast], CombatView.MessageTop, true);
                break;
            case EffectEvent.SucksBlood:
                HandlerMessage(c, t[CombatMsg.HandlerSucksBlood], CombatView.MessageTop, true);
                break;
            case EffectEvent.Stronger:
                ItemEffectTextWindow(c, t[CombatMsg.HandlerStronger]);
                break;
            case EffectEvent.AirClears:
                PageSay(t[CombatMsg.HandlerAirClears]);
                break;
            case EffectEvent.NeedsGiantStrength:
                PageSay(t[CombatMsg.HandlerNeedsGiantStrength]);
                break;
        }
    }

    /// <summary>
    /// The flee 0x13465's message (read s30): away, 0x2BDF0's (0x319D2(creature, 3, 0), then START 0x16EC(creature, the
    /// got-away words, row 10, pause 1)) before the icon leaves the map; not away, the message routine START 0x1693 with
    /// the escape-blocked words (row 24, the pause).
    /// </summary>
    internal void FleeMessage(EclCharacter c, bool away)
    {
        if (!away)
        {
            PageSay(Text[CombatMsg.EscapeBlocked]);
            return;
        }
        StatusChangeScroll(c);
        Say(c, Text[CombatMsg.GotAway]);
    }

    /// <summary>
    /// Handler 131's message in a fight (0x11C8A–0x11CB6): DS:49F3 = 2 around START 0x16EC(creature, text, row 10, pause
    /// 1), so the text window covers rows 17–22 of the current screen during the pause.
    /// </summary>
    private void ItemEffectTextWindow(EclCharacter c, string text)
    {
        var window = new TextWindow { Typed = TypeDelay };
        View.ItemEffectText = window;
        try
        {
            window.Put(TextWindow.Top + 1, TextWindow.Left, Name(c), NameColour(c));
            window.PrintIn(TextWindow.Left, TextWindow.Top + 2, TextWindow.Right, TextWindow.Bottom, text, true,
                TextWindow.ScriptColour);
            Pause(MessageMs);
        }
        finally
        {
            View.ItemEffectText = null;
        }
    }

    /// <summary>
    /// 0xECB3's message (0xECB9–0xECDD, read s29): the caller's text by START 0x16EC from row 10 without the pause flag,
    /// before the dead / stone / gone test. When the status is then set, <see cref="EffectEvent.StatusDone"/> brings the
    /// pauses and the clear; a creature already dead, stone or gone keeps the text in the panel until the next one.
    /// Its square is remembered first: the map removal comes before CreatureDowned's picture.
    /// </summary>
    private void StatusMessage(EclCharacter c, string text)
    {
        Remember();
        HandlerMessage(c, text, CombatView.MessageTop, false);
    }

    /// <summary>
    /// A monster special's own message (class 0x0E handlers 83, 84, 88, 121, read s27; the handlers before 0xECB3, s29):
    /// START 0x16EC(creature, text, row, pause flag) as the handler calls it. The gazes (row 10) and the reflection (row
    /// 12) pass no pause: the text stays in the panel through the flight that follows until the next text overwrites
    /// it. The two breaths (row 10), the rot and the drain pass the pause: PauseOnly, then rows 10–21 cleared (START
    /// 0x2383).
    /// </summary>
    private void HandlerMessage(EclCharacter c, string text, int row, bool pause)
    {
        View.Page = null;
        View.Mode = CombatMode.Watch;
        PrintCreatureMessage(c, text, row, pause);
    }

    /// <summary>
    /// An effect added with a text (0x2C540 at 0x2C603–0x2C61C, read s25): START 0x2B8B(creature, effect frames, text)
    /// (<see cref="ShowHit"/>: the text from row 10, icon cell 22 = COMSPR 9 in DS:49E2 + 1 rounds of four frames, no
    /// pause), then START 0x2383 clears rows 10–21. <paramref name="key"/> names the text (Core <see cref="EffectTexts"/>).
    /// </summary>
    internal void EffectTextMessage(EclCharacter c, string key)
    {
        ShowHit(c, Text.Effect(key), effect: true);
        ClearMessageWindow();
    }

    /// <summary>
    /// PrintNameStringAnim (START 0x2B8B, read s19) for damage (<paramref name="effect"/> false) and for an effect's
    /// message (true): a creature not wholly in the view brings the view to it (ScrollView(its square, 3)); START 0x16EC
    /// from row 10 without pause; the picture of icon cell 23 (damage: COMSPR 10) or 22 (effect: COMSPR 9) is drawn on
    /// its square in the four frames 0x257E builds (stored, mirrored, the + 128 picture mirrored, the + 128 picture),
    /// Delay(70) each (START 0x2CCB), once for damage and DS:49E2 + 1 times for an effect; damage then gets the pause.
    /// Sound (START 0x2BB1–0x2C18, 0x2D07; docs/re/sound.md §2.4): a resident bracket from before the scroll to the end
    /// of the frames, the cue 261A (damage, tune 2) or 261C (effect, tune 3) just before the text (DOSBox-X s41).
    /// </summary>
    private void ShowHit(EclCharacter c, string text, bool effect = false)
    {
        View.Page = null;
        View.Mode = CombatMode.Watch;
        var sound = View.Fight?.State.Sound;
        sound?.BeginResident(); // START 0x2BB1: set auto-stop(0)
        var sq = Square(c);
        if (sq is { } s0 && !WhollyInView(c)) // START 0x2BD7: 0x30E54(creature, 1)
            View.Scroll(s0.X, s0.Y, 3);
        sound?.Cue(effect ? GoldBox.Core.Sound.SoundCue.Effect : GoldBox.Core.Sound.SoundCue.Damage); // 0x2C09 / 0x2C14
        PrintCreatureMessage(c, text, CombatView.MessageTop, false);
        int reps = effect ? View.Speed : 0;
        int sprite = effect ? EffectSprite : DamageSprite;
        for (int i = 0; i <= reps; i++)
            for (int j = 0; j < 4; j++)
            {
                if (sq is { } s)
                    View.Flash = (s.X, s.Y, j >= 2 ? sprite + 128 : sprite, j is 1 or 2);
                Pause(HitFrameMs, animation: true);
            }
        View.Flash = null;
        sound?.EndResident(); // START 0x2D07: set auto-stop(10)
        if (!effect)
            Pause(MessageMs);
    }

    /// <summary>One frame of the hit picture (Delay(70) at START 0x2CCB).</summary>
    private const int HitFrameMs = 70;
    /// <summary>COMSPR blocks of icon cells 23 (damage) and 22 (an effect) (0xEADC: COMSPR i into cell i + 13).</summary>
    private const int DamageSprite = 10, EffectSprite = 9;

    /// <summary>
    /// PauseOnly, START 0x709:0x0DBE: Delay(DS:49E2 × 225) ms (the game speed; speed 0 does not wait), the routine the
    /// attack messages (0x12CFB, 0x12E12), the item use and the message routine 0x1693 call.
    /// </summary>
    private int MessageMs => 225 * View.Speed;

    private object? Ask(PlayerRequest request) => ask(request);

    /// <summary>A pause; <paramref name="animation"/>: a fixed delay the QoL animation speed scales
    /// (<see cref="DelayRequest.Animation"/>), not a game-speed one.</summary>
    private void Pause(int ms, bool animation = false)
    {
        if (Pauses)
            ask(new DelayRequest(TimeSpan.FromMilliseconds(ms)) { Animation = animation });
    }

    /// <summary>
    /// A display point where the program itself waits nothing (an AI step, a turn change): the screen is shown for one
    /// frame (<see cref="DelayRequest"/> of zero, which the combat screen answers after drawing it once).
    /// </summary>
    private void Frame() => Pause(0, animation: true);

    private int Choose(CombatMode mode, string? prompt, IReadOnlyList<string> items, int? cancel = null, int selected = 0)
    {
        View.Mode = mode;
        View.MovePrompt = null; // the menu is written over row 24
        var r = ask(new ChoiceRequest(null, items, false, cancel) { MenuPrompt = prompt, Selected = selected });
        return r is int i ? i : cancel ?? 0;
    }

    /// <summary>The yes/no routine 0x2D9A0: the shared highlight goes to No first (0x2D9B9), so the menu opens on No
    /// (DOSBox-X s21: "Continue battle", 0xA1A5).</summary>
    private bool YesNo(string prompt)
    {
        var words = Text.Words(CombatMsg.YesNo);
        if (words.Count < 2)
            words = ["YES", "NO"];
        return Choose(CombatMode.Question, prompt.TrimEnd(), words, 1, selected: 1) == 0;
    }

    /// <summary>
    /// The menu loop's redraw (0x9B21 after a command that did not end the turn, and the turn's start): 0x319D2(creature,
    /// 2, cursor on) and the panel.
    /// </summary>
    private void ShowCurrent(FightMenu menu)
    {
        View.Current = menu.Creature;
        Centered(menu.Creature, 2, true);
        ShowPanel(menu.Creature); // rows 23 and 24 keep what they showed while the weapon name types
        View.StatusLine = null;
        View.MovePrompt = null;
    }

    // ---- IFightMenuChooser -------------------------------------------------------------------------------------------

    /// <summary>The main menu's words, per entry (the program's strings 0x9E00–0x9E21).</summary>
    public string Word(FightCommand c)
    {
        IReadOnlyList<string> W(CombatMsg m) => Text.Words(m);
        string Pick(CombatMsg m, int i, string ours) => W(m) is var w && w.Count > i ? w[i] : ours;
        return c switch
        {
            FightCommand.Move => Pick(CombatMsg.MenuMove, 0, "MOVE"),
            FightCommand.View => Pick(CombatMsg.MenuViewAim, 0, "VIEW"),
            FightCommand.Aim => Pick(CombatMsg.MenuViewAim, 1, "AIM"),
            FightCommand.Use => Pick(CombatMsg.MenuUse, 0, "USE"),
            FightCommand.Cast => Pick(CombatMsg.MenuCast, 0, "CAST"),
            FightCommand.Turn => Pick(CombatMsg.MenuTurn, 0, "TURN"),
            FightCommand.Quick => Pick(CombatMsg.MenuQuickDone, 0, "QUICK"),
            FightCommand.Done => Pick(CombatMsg.MenuQuickDone, 1, "DONE"),
            _ => c.ToString().ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Answers of the main menu's request beyond its entries: the keys the menu takes whether or not anything is shown
    /// (0x9FF3–0xA057): Alt-M, Alt-Q, Alt-X (space and Alt-R are not offered: space is the AI turn's key, Alt-R does nothing).
    /// </summary>
    public const int AnswerQuickMagic = -10, AnswerAllQuick = -11, AnswerAltX = -12;

    public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries)
    {
        View.Page = null;
        ShowCurrent(menu);
        _goal = null;
        var words = entries.Select(Word).ToList();
        int i = Choose(CombatMode.Menu, null, words);
        switch (i)
        {
            case AnswerQuickMagic: return FightCommand.ToggleQuickMagic;
            case AnswerAllQuick: return FightCommand.AllQuick;
            case AnswerAltX: return FightCommand.GodsIntervene;
        }
        var cmd = entries[Math.Clamp(i, 0, entries.Count - 1)];
        // View (0x21AED) and Use (0x21F32) run as FightItemScreens, asking this chooser (CombatItemScreens.cs).
        _menu = menu;
        _fromView = false;
        _itemRow = 0;
        return cmd;
    }

    public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries)
    {
        var t = Text;
        string W(DoneCommand d) => d switch
        {
            DoneCommand.Guard => t.Words(CombatMsg.DoneGuard).FirstOrDefault() ?? "GUARD",
            DoneCommand.Delay => t.Words(CombatMsg.DoneDelayQuit).ElementAtOrDefault(0) ?? "DELAY",
            DoneCommand.Quit => t.Words(CombatMsg.DoneDelayQuit).ElementAtOrDefault(1) ?? "QUIT",
            DoneCommand.Bandage => t.Words(CombatMsg.DoneBandage).FirstOrDefault() ?? "BANDAGE",
            DoneCommand.Speed => t.Words(CombatMsg.DoneSpeedExit).ElementAtOrDefault(0) ?? "SPEED",
            _ => t.Words(CombatMsg.DoneSpeedExit).ElementAtOrDefault(1) ?? "EXIT",
        };
        int exit = entries.Count - 1;
        int i = Choose(CombatMode.Done, null, [.. entries.Select(W)], exit);
        return entries[Math.Clamp(i, 0, exit)];
    }

    public SpeedCommand ChooseSpeed(FightMenu menu, byte speed, IReadOnlyList<SpeedCommand> entries)
    {
        View.Speed = speed;
        var t = Text;
        string W(SpeedCommand s) => (s switch
        {
            SpeedCommand.Slower => t[CombatMsg.Slower],
            SpeedCommand.Faster => t[CombatMsg.Faster],
            _ => t[CombatMsg.SpeedExit],
        }).Trim();
        // DOSBox-X: the speed word (0xA8D6), the speed, ") : " in the prompt colour before the entries.
        string prompt = t[CombatMsg.GameSpeed] + speed.ToString(CultureInfo.InvariantCulture) + ") :";
        int exit = entries.Count - 1;
        int i = Choose(CombatMode.Speed, prompt, [.. entries.Select(W)], exit);
        var cmd = entries[Math.Clamp(i, 0, exit)];
        View.Speed = (byte)(cmd switch { SpeedCommand.Slower => speed + 1, SpeedCommand.Faster => speed - 1, _ => speed });
        return cmd;
    }

    /// <summary>
    /// Move's key loop (0xA1E6): only row 24's prompt changes between keys. DOSBox-X s37 (Phineas walking east, seeded):
    /// no panel redraw or typing and no cursor box at each step; the menu's box stays under the creature until the first
    /// step redraws that cell (<see cref="Stepped"/>).
    /// </summary>
    public MoveInput ChooseMove(FightMenu menu, int movementLeft)
    {
        View.Current = menu.Creature;
        View.StatusLine = null;
        View.MovePrompt = null;
        View.Mode = CombatMode.Move;
        var r = ask(new CombatMoveRequest(movementLeft));
        // The prompt stays on row 24 until something else writes there (DOSBox-X s37: through the menu's panel typing
        // after the last step, until the menu draws over it).
        View.MovePrompt = Text[CombatMsg.MoveLeft] + movementLeft.ToString(CultureInfo.InvariantCulture);
        View.HitMarker = null;
        return r is MoveInput m ? m : MoveInput.Escape;
    }

    public AimKey ChooseAim(FightMenu menu, AimView aim)
    {
        // 0x27C38 shows the cast message before its target pick, for a spell begun earlier too (combat record +0, cast
        // from the menu 0x9B21 when its turn comes again): announced at its first aim, the later Announce skipped.
        // DOSBox-X s41: Brother Sean's Cure Light Wounds: the begins-casting words, the casts-a-spell words, then the aim.
        if (menu.Record[CombatRecord.SpellCast] is var begun and > 0
            && !(_announced is { } an && ReferenceEquals(an.Caster, menu.Creature) && an.Spell == begun))
        {
            SayCast(menu.Creature, begun);
            _announced = (menu.Creature, begun);
        }
        var c = aim.Candidate;
        // A creature a click picked: step through the candidates to it, then show it.
        if (_goal is { } goal)
        {
            if (ReferenceEquals(c, goal))
                _goal = null;
            else if (++_goalSteps <= 256)
                return AimKey.Next;
            else
                _goal = null; // [?] not a candidate (fails the target check): back to the aim menu
        }
        // The aim is drawn on the combat screen: an item's aim from the Items page (a scroll, whose use prints no
        // message first) leaves that page.
        View.Page = null;
        View.Current = menu.Creature;
        // 0x154xx: 0x319D2(candidate, 3, cursor on), then the panel shows it (START 0x1583).
        Centered(c, 3, true);
        // DOSBox-X s29: the new range is on row 23 before the panel (its weapon typing) comes. Only in attack mode
        // (0x152CD tests the mode): a spell's aim leaves row 23 alone, so the cast's spell label stays (DOSBox-X s30).
        View.Mode = CombatMode.Aim;
        if (aim.Attack)
            View.StatusLine = Text[CombatMsg.Range] + aim.Distance.ToString(CultureInfo.InvariantCulture);
        ShowPanel(c);
        while (true)
        {
            var words = new List<string>(Text.Words(CombatMsg.AimNextPrevManual));
            if (words.Count < 3)
                words = ["NEXT", "PREV", "MANUAL"];
            var keys = new List<object> { AimKey.Next, AimKey.Prev, AimKey.Manual };
            if (aim.TargetOffered)
            {
                words.Add(Text[CombatMsg.Target].Trim());
                keys.Add(AimKey.Target);
            }
            var ce = Text.Words(CombatMsg.AimCenterExit);
            words.Add(ce.ElementAtOrDefault(0) ?? "CENTER");
            keys.Add("center");
            words.Add(ce.ElementAtOrDefault(1) ?? "EXIT");
            keys.Add(AimKey.Exit);
            View.Mode = CombatMode.Aim;
            var r = ask(new ChoiceRequest(null, words, false, words.Count - 1) { MenuPrompt = Text[CombatMsg.AimPrompt].TrimEnd() });
            object key = r switch
            {
                int i when i >= 0 && i < keys.Count => keys[i],
                AimPick pick => pick,
                _ => AimKey.Exit,
            };
            switch (key)
            {
                case AimKey k:
                    return k;
                case "center":
                    if (Square(c) is { } sq)
                        View.Center(sq.X, sq.Y);
                    continue;
                case AimPick ap:
                    // A click on the creature shown targets it (when offered); on another creature, shows that one.
                    if (View.Fight?.Map?.At(ap.X, ap.Y) is { } picked)
                    {
                        if (!ReferenceEquals(picked, c))
                            return StepTo(picked);
                        if (aim.TargetOffered)
                            return AimKey.Target;
                    }
                    continue;
            }
        }
    }

    private AimKey StepTo(EclCharacter goal)
    {
        _goal = goal;
        _goalSteps = 0;
        return AimKey.Next;
    }

    /// <summary>
    /// Manual (0x155F5), one pass per cursor square (<see cref="FightMenu.Manual"/>): Core moves the cursor, finds the
    /// creature under it and whether Target is offered; the screen shows it (the panel: the creature under the cursor,
    /// row 23: "Range = n" in attack mode when the line got through) and answers with a key. Return / T, E / Escape
    /// end Manual whether or not Target is offered, as in the program. A click on another square (our input mapping
    /// for the mouse) walks the cursor there one square per pass, each pass answered here as the arrow key would be,
    /// so the program's per-square work (the target check) runs on the squares a keyboard player would cross [?].
    /// </summary>
    public ManualInput ChooseManual(FightMenu menu, ManualView view)
    {
        View.Mode = CombatMode.Manual;
        View.Page = null;
        View.Current = menu.Creature;
        if (view.Creature is { } shown)
            View.ShowLines(shown);
        else
            View.Shown = null;
        View.Panel.Clear();
        // 0x1566C: ScrollView(cursor square, 3, its direction) with the cursor flag on and size 1; Core has already
        // moved the square, so the band test runs on it.
        View.Scroll(view.X, view.Y, 3, CombatTables.Center, 1);
        // 0x15722 / 0x15768: in attack mode "Range = n" when the line got through, else row 23 cleared; outside
        // attack mode row 23 is not written (a spell's label stays).
        if (view.Attack)
            View.StatusLine = view.LineReached
                ? Text[CombatMsg.Range] + view.Distance.ToString(CultureInfo.InvariantCulture)
                : null;
        if (_cursorGoal is { } g)
        {
            if (++_cursorSteps <= CombatArena.Width && StepToward((view.X, view.Y), g) is { } d)
                return new ManualInput(d);
            _cursorGoal = null;
        }
        while (true)
        {
            var r = ask(new CombatCursorRequest(view));
            switch (r)
            {
                case ManualInput key:
                    if (key == ManualInput.Target || key == ManualInput.Exit)
                        View.Mode = CombatMode.Aim;
                    return key;
                case CursorWalk w when StepToward((view.X, view.Y), (w.X, w.Y)) is { } d:
                    _cursorGoal = (w.X, w.Y);
                    _cursorSteps = 1;
                    return new ManualInput(d);
                case CursorWalk:
                    continue;
                default:
                    View.Mode = CombatMode.Aim;
                    return ManualInput.Exit;
            }
        }
    }

    /// <summary>The direction 0–7 (0 N, clockwise) of one square from <paramref name="from"/> toward <paramref name="to"/>; null when there.</summary>
    public static int? StepToward((int X, int Y) from, (int X, int Y) to)
    {
        int dx = Math.Sign(to.X - from.X), dy = Math.Sign(to.Y - from.Y);
        for (int d = 0; d < 8; d++)
            if (CombatTables.DirDx[d] == dx && CombatTables.DirDy[d] == dy)
                return d;
        return null;
    }

    public bool ConfirmFlee(FightMenu menu) => YesNo(Text[CombatMsg.Flee]);

    public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally)
    {
        View.ShowLines(ally);
        return YesNo(Text[CombatMsg.AttackAlly]);
    }

    public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable)
    {
        // 0x14C0F → 0x239D6 (kind 0, action 1 Cast; P, s30): the memorised list, every memorised spell with level
        // headings (a camp-only one is refused after the pick, FightMenu.Cast), in the list routine's window (rows 5–22)
        // with Next / Prev before Exit. An empty list returns at once (0x2D69C). The screen is the list's own
        // (SpellPageLayout: title, frames, the choose-spell prompt on row 24; D s33).
        var (rows, ids, labels) = MemorisedList(menu.Creature, menu.Fight);
        if (ids.All(s => s == 0))
            return null;
        View.Mode = CombatMode.Spell;
        string cast = Word(FightCommand.Cast), exit = Text.Words(CombatMsg.AimCenterExit).ElementAtOrDefault(1) ?? "EXIT";
        var req = new ListRequest([new ListLine(Name(menu.Creature), NameColour(menu.Creature))], rows, [cast, exit], 1,
            FirstRow: SpellLists.FirstRow)
        {
            RowActions = new HashSet<int> { 0 },
            Labels = labels,
            Paging = SpellPaging(2),
            Spell = SpellLists.Layout(menu.Creature, Resources().Texts, Msg.InMemory, prompt: true),
            Grey = true, // the menu's Cast keeps the combat palette (D s33), unlike View's Spells
        };
        var a = ask(req) as ListAnswer;
        View.Mode = CombatMode.Menu;
        if (a is null || a.Action != 0 || a.Row < 0 || a.Row >= ids.Count || ids[a.Row] == 0)
            return null;
        int spell = ids[a.Row];
        // 0x27C38 shows the cast message before its target pick; the Core menu aims first and casts after, so a spell
        // cast now is announced here (before the aim) and its Announce call is skipped. A begun spell is announced at
        // its first aim (ChooseAim; DOSBox-X s41).
        if (menu.Fight.Spells.UsableInCombat(spell) && menu.Fight.Spells.CastingTime(spell) == 0)
        {
            SayCast(menu.Creature, spell);
            _announced = (menu.Creature, spell);
        }
        return spell;
    }

    public void Notice(FightNotice notice, EclCharacter? who)
    {
        var t = Text;
        // Alt-M (0x9CD1 / 0x9CE7), Alt-X's refusal (0x9DC0) and Alt-X's own message (0x1638E) go through the message
        // routine START 0x1693: row 24 in green, the game-speed pause, row 24 cleared; the panel stays (DOSBox-X s24).
        if (notice is FightNotice.MagicOn or FightNotice.MagicOff or FightNotice.DoesNotWork or FightNotice.GodsIntervene)
        {
            PageSay(notice switch
            {
                FightNotice.MagicOn => t[CombatMsg.MagicOn],
                FightNotice.MagicOff => t[CombatMsg.MagicOff],
                FightNotice.DoesNotWork => t[CombatMsg.DoesNotWork],
                _ => t[CombatMsg.GodsIntervene],
            });
            if (notice == FightNotice.GodsIntervene)
                View.Cursor = null; // 0x1638E redraws the map after its message: the cursor box goes (DOSBox-X s24)
            return;
        }
        string text = notice switch
        {
            FightNotice.Blocked => t[CombatMsg.Blocked],
            FightNotice.NotWithThatWeapon => t[CombatMsg.NotWithThatWeapon],
            FightNotice.Bandaged => t[CombatMsg.IsBandaged],
            FightNotice.Sweeps => t[CombatMsg.Sweeps],
            FightNotice.TurnsUndead => t[CombatMsg.TurnsUndead],
            FightNotice.Turned => t[CombatMsg.IsTurned],
            FightNotice.Destroyed => t[CombatMsg.IsDestroyed],
            FightNotice.NothingHappens => t[CombatMsg.NothingHappens],
            FightNotice.CampOnlySpell => t[CombatMsg.CampOnlySpell],
            FightNotice.BeginsCasting => t[CombatMsg.BeginsCasting],
            FightNotice.AlreadyTargeted => t[CombatMsg.AlreadyTargeted],
            _ => "",
        };
        if (text.Length == 0 && who is null)
            return;
        Say(who, text);
    }

    /// <summary>The spell's aim picked nothing (0x27EF2): yes = the spell is lost.</summary>
    public bool ConfirmAbortSpell(FightMenu menu)
    {
        bool abort = YesNo(Text[CombatMsg.AbortSpell]);
        if (abort)
            Say(null, Text[CombatMsg.SpellAborted]);
        return abort;
    }

    public bool ContinueBattle()
    {
        ClearMessageWindow();
        return YesNo(Text[CombatMsg.ContinueBattle]);
    }

    /// <summary>
    /// An <see cref="IFightMoves"/> that tells the screen after each step (so the AI's and the player's steps are seen);
    /// the moves themselves are the wrapped ones'.
    /// </summary>
    public sealed class ShownMoves(IFightMoves inner, Action<EclCharacter> stepping, Action<EclCharacter, (int X, int Y)?> stepped)
        : IFightMoves
    {
        public IFightMoves Inner { get; } = inner;

        public StepResult Step(Fight fight, EclCharacter c, int direction)
        {
            (int X, int Y)? from = fight.Map is CombatMap m && m.OnMap(c) ? m.Position(c) : null;
            stepping(c);
            var r = Inner.Step(fight, c, direction);
            stepped(c, from);
            return r;
        }

        public bool Flee(Fight fight, EclCharacter c) => Inner.Flee(fight, c);
    }
}
