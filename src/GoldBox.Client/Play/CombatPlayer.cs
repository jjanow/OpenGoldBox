using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>Move mode (0xA1E6): a direction 0–7, Return or Escape. The answer is a <see cref="MoveInput"/>.</summary>
public sealed record CombatMoveRequest(int MovementLeft) : PlayerRequest;

/// <summary>
/// The manual aim cursor (0x155F5): the player moves a cursor over the squares and picks one. The answer is the square
/// as (int X, int Y), or null (Exit / Escape).
/// </summary>
public sealed record CombatCursorRequest(int Range) : PlayerRequest
{
    /// <summary>Attack mode (the menu's Aim); false for a spell's aim, where the caster itself may be picked.</summary>
    public bool Attack { get; init; } = true;
}

/// <summary>A creature picked with the mouse on the map while the aim menu is up (answers that menu's request).</summary>
public sealed record AimPick(int X, int Y);

/// <summary>What the combat screen shows in each phase (the line on rows 23/24 and what the map highlights).</summary>
public enum CombatMode { Watch, Menu, Move, Aim, Manual, Done, Speed, Question, Spell, Page }

/// <summary>One line of the status panel's message area (rows 10 and down): text and EGA colour.</summary>
public readonly record struct PanelLine(string Text, byte Colour);

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
    /// <summary>The cursor box's square (null = none). DOSBox-X: a light-grey box with a bevel under the icon.</summary>
    public (int X, int Y)? Cursor { get; set; }
    /// <summary>The hit marker's square (COMSPR 25), shown after a hit until the next action.</summary>
    public (int X, int Y)? HitMarker { get; set; }
    /// <summary>Creatures drawn in their attack pose.</summary>
    public HashSet<EclCharacter> Attacking { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>The message lines of the status panel (rows 10 …).</summary>
    public List<PanelLine> Messages { get; } = [];
    /// <summary>Row 23 (under the map frame): "RANGE = n", "MOVE … = n" and the like.</summary>
    public string? StatusLine { get; set; }
    /// <summary>Monster icon pictures by icon slot (record 0xBF), from LoadCombatIcon (LOADMON, ADDNPC, the duel).</summary>
    public Dictionary<int, int> IconPictures { get; } = [];
    /// <summary>The game speed DS:49E2 (0 fastest … 9), shared with the Done menu's Speed.</summary>
    public byte Speed { get; set; }
    /// <summary>A 3D area (DUNGCOM ground) or the wilderness (WILDCOM).</summary>
    public bool Dungeon { get; set; } = true;
    /// <summary>The character screen over the whole frame (View, Items, drop money), or null for the map.</summary>
    public CombatPage? Page { get; set; }
    /// <summary>The message routine's line (START 0x1693: row 24, green) while a page waits out its pause.</summary>
    public string? PageMessage { get; set; }

    /// <summary>
    /// [?] The view's scrolling (0x310D0) is not read. DOSBox-X: the fight starts with the first creature's cell − 3
    /// (centred) and the view later moved one square to keep the current creature off the edge row; we scroll by the
    /// least amount that keeps <paramref name="x"/>, <paramref name="y"/> in the inner 3 × 3 squares of the 7 × 7 view.
    /// </summary>
    public void Follow(int x, int y)
    {
        if (x < ViewX + 2) ViewX = x - 2;
        if (x > ViewX + 4) ViewX = x - 4;
        if (y < ViewY + 2) ViewY = y - 2;
        if (y > ViewY + 4) ViewY = y - 4;
        Clamp();
    }

    /// <summary>Centre the view on a square (the aim menu's Center).</summary>
    public void Center(int x, int y)
    {
        ViewX = x - 3;
        ViewY = y - 3;
        Clamp();
    }

    /// <summary>
    /// [?] The view stays inside the 50 × 25 arena. Whether the program clamps is not read; DOSBox-X never showed cells
    /// off the arena.
    /// </summary>
    private void Clamp()
    {
        ViewX = Math.Clamp(ViewX, 0, CombatArena.Width - ViewSquares);
        ViewY = Math.Clamp(ViewY, 0, CombatArena.Height - ViewSquares);
    }

    public void Clear()
    {
        Fight = null;
        Current = Shown = null;
        Cursor = HitMarker = null;
        Attacking.Clear();
        Messages.Clear();
        StatusLine = null;
        Page = null;
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

    /// <summary>The creature the manual cursor picked: the aim menu steps to it (Next) and targets it.</summary>
    private EclCharacter? _goal;
    private int _goalSteps;
    /// <summary>Target the goal once reached (the manual cursor's Target); false: only show it (a mouse click on it).</summary>
    private bool _goalTarget;

    // ---- Wiring --------------------------------------------------------------------------------------------------

    /// <summary>Shows <paramref name="fight"/> on the combat screen: hooks for turns, attacks and steps.</summary>
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
        fight.AttackMade = AttackMade;
        fight.Moves = new ShownMoves(fight.Moves, Stepped);
        if (fight.Spells is FightSpells spells)
        {
            spells.Announce = Announce;
            spells.ItemUsed = ItemUsed;
            spells.Event += SpellEvent;
        }
        if (fight.Controller is MenuController { Ai: MonsterAi ai })
            ai.Message = AiSays;
    }

    public void Detach(Fight fight)
    {
        if (fight.Controller is MenuController mc)
            View.Speed = mc.GameSpeed;
        fight.TurnStarted = null;
        fight.AttackMade = null;
        if (fight.Spells is FightSpells spells)
        {
            spells.Announce = null;
            spells.ItemUsed = null;
            spells.Event -= SpellEvent;
        }
        if (fight.Controller is MenuController { Ai: MonsterAi ai })
            ai.Message = null;
        _menu = null;
        _announced = null;
        View.Clear();
    }

    private (int X, int Y)? Square(EclCharacter c) =>
        View.Fight?.Map is CombatMap m && m.OnMap(c) ? m.Position(c) : null;

    /// <summary>Where each creature stood when last seen (a dropped monster leaves the map before the message).</summary>
    private readonly Dictionary<EclCharacter, (int X, int Y)?> _lastSquare = new(ReferenceEqualityComparer.Instance);

    private void Remember()
    {
        if (View.Fight is not { } f)
            return;
        foreach (var x in f.List)
            if (Square(x) is { } p)
                _lastSquare[x] = p;
    }

    private void TurnStarted(EclCharacter c)
    {
        Remember();
        View.Page = null;
        _announced = null; // a cast-now spell the list announced but never cast (no target) is forgotten
        View.Current = View.Shown = c;
        View.Messages.Clear();
        View.HitMarker = null;
        View.Attacking.Clear();
        View.StatusLine = null;
        if (Square(c) is { } p)
        {
            View.Follow(p.X, p.Y);
            View.Cursor = p;
        }
        if (c[CombatRules.AutoCombat] != 0)
        {
            View.Mode = CombatMode.Watch;
            Pause(StepMs);
        }
    }

    private void Stepped(EclCharacter c)
    {
        Remember();
        View.HitMarker = null;
        if (Square(c) is { } p)
        {
            View.Follow(p.X, p.Y);
            if (ReferenceEquals(c, View.Current))
                View.Cursor = p;
        }
        if (c[CombatRules.AutoCombat] != 0)
            Pause(StepMs);
    }

    private static string Name(EclCharacter c) => c.Name.ToUpperInvariant();

    /// <summary>The colour of a creature's name: party cyan, hostile yellow (DOSBox-X: the panel's first line).</summary>
    public static byte NameColour(EclCharacter c) => c[FightRules.Hostile] == 0 ? (byte)11 : (byte)14;

    /// <summary>
    /// The attack message in the panel (DOSBox-X: attacker, the attacks word, the defender, the damage words, then a
    /// blank line and the defender with the "goes down" word, then the dying or killed words by status: 0x12AF4,
    /// read in s15). [?] The line breaks of START 0x16EC are taken from the screen.
    /// </summary>
    private void AttackMade(EclCharacter attacker, EclCharacter defender, FightRules.AttackOutcome outcome, int hpBefore)
    {
        if (outcome.Attacks == 0 && !outcome.AnyHit)
            return;
        var t = Text;
        View.Shown = attacker;
        View.Mode = CombatMode.Watch;
        View.StatusLine = null;
        View.Messages.Clear();
        View.Messages.Add(new PanelLine(Name(attacker), NameColour(attacker)));
        View.Messages.Add(new PanelLine(t[CombatMsg.Attacks].Trim(), 10));
        View.Messages.Add(new PanelLine(Name(defender), NameColour(defender)));
        if (outcome.AnyHit)
        {
            int dmg = Math.Max(0, hpBefore - defender[FightRules.Hp]);
            if (defender[FightRules.InPlay] == 0 && dmg == 0)
                dmg = hpBefore;
            // DOSBox-X: the hitting word with the number, then the points and damage words on the next line.
            string unit = dmg == 1 ? t[CombatMsg.Point] : t[CombatMsg.Points];
            View.Messages.Add(new PanelLine((t[CombatMsg.HittingFor] + dmg.ToString(CultureInfo.InvariantCulture)).Trim(), 10));
            View.Messages.Add(new PanelLine((unit.Trim() + " " + t[CombatMsg.OfDamage].Trim()).Trim(), 10));
            if ((Square(defender) ?? (_lastSquare.TryGetValue(defender, out var last) ? last : null)) is { } d)
                View.HitMarker = d;
        }
        else
            View.Messages.Add(new PanelLine(t[CombatMsg.AndMisses].Trim(), 10));
        if (defender[FightRules.InPlay] == 0)
        {
            View.Messages.Add(new PanelLine("", 10));
            View.Messages.Add(new PanelLine(Name(defender), 12));
            // 0x12D4B: out of play → the defender and the down word (0x12AB3, START 0x16EC), two rows on; then status 5
            // (dying) → the dying words (0x12ABD) in green at column 23; status 6, 7 or 8 (the set at 0x12ACA) → the
            // defender and the killed words (0x12AEA). [?] The name's colour in START 0x16EC is the one DOSBox-X showed.
            View.Messages.Add(new PanelLine(t[CombatMsg.GoesDown].Trim(), 10));
            byte status = defender[FightRules.Status];
            if (status == 5)
                View.Messages.Add(new PanelLine(t[CombatMsg.AndIsDying].Trim(), 10));
            else if (status is 6 or 7 or 8)
            {
                View.Messages.Add(new PanelLine(Name(defender), 12));
                View.Messages.Add(new PanelLine(t[CombatMsg.IsKilled].Trim(), 10));
            }
        }
        View.Attacking.Add(attacker);
        Pause(MessageMs);
        View.Attacking.Remove(attacker);
        View.HitMarker = null;
    }

    /// <summary>
    /// The messages' pause, START 0x709:0x0DBE: Delay(DS:49E2 × 225) ms (the game speed; speed 0 does not wait), the
    /// routine the attack messages (0x12CFB, 0x12E12), the item use and the message routine 0x1693 call.
    /// </summary>
    private int MessageMs => 225 * View.Speed;
    /// <summary>[?] A step or a turn change: 60 ms + 30 ms per speed step.</summary>
    private int StepMs => 60 + 30 * View.Speed;

    private object? Ask(PlayerRequest request) => ask(request);

    private void Pause(int ms)
    {
        if (Pauses)
            ask(new DelayRequest(TimeSpan.FromMilliseconds(ms)));
    }

    private void Say(params PanelLine[] lines)
    {
        View.Messages.Clear();
        View.Messages.AddRange(lines);
        Pause(MessageMs);
    }

    private int Choose(CombatMode mode, string? prompt, IReadOnlyList<string> items, int? cancel = null)
    {
        View.Mode = mode;
        var r = ask(new ChoiceRequest(null, items, false, cancel) { MenuPrompt = prompt });
        return r is int i ? i : cancel ?? 0;
    }

    private bool YesNo(string prompt)
    {
        var words = Text.Words(CombatMsg.YesNo);
        if (words.Count < 2)
            words = ["YES", "NO"];
        return Choose(CombatMode.Question, prompt.TrimEnd(), words, 1) == 0;
    }

    private void ShowCurrent(FightMenu menu)
    {
        View.Current = View.Shown = menu.Creature;
        View.StatusLine = null;
        if (Square(menu.Creature) is { } p)
        {
            View.Cursor = p;
            View.Follow(p.X, p.Y);
        }
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

    public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries)
    {
        View.Page = null;
        ShowCurrent(menu);
        _goal = null;
        var words = entries.Select(Word).ToList();
        int i = Choose(CombatMode.Menu, null, words);
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

    public MoveInput ChooseMove(FightMenu menu, int movementLeft)
    {
        ShowCurrent(menu);
        View.Mode = CombatMode.Move;
        View.Cursor = null; // DOSBox-X: no box while moving
        var r = ask(new CombatMoveRequest(movementLeft));
        View.HitMarker = null;
        return r is MoveInput m ? m : MoveInput.Escape;
    }

    public AimKey ChooseAim(FightMenu menu, AimView aim)
    {
        var c = aim.Candidate;
        // A creature the manual cursor or a click picked: step through the candidates to it, then (the cursor's
        // Target) Target when offered, or (a click) show it.
        if (_goal is { } goal)
        {
            if (ReferenceEquals(c, goal))
            {
                _goal = null;
                if (_goalTarget && aim.TargetOffered)
                    return AimKey.Target;
            }
            else if (++_goalSteps <= 256)
                return AimKey.Next;
            else
                _goal = null; // [?] not a candidate (fails the target check): back to the aim menu
        }
        View.Current = menu.Creature;
        View.Shown = c;
        View.Messages.Clear();
        if (Square(c) is { } p)
        {
            View.Cursor = p;
            View.Follow(p.X, p.Y);
        }
        View.StatusLine = Text[CombatMsg.Range] + aim.Distance.ToString(CultureInfo.InvariantCulture);
        while (true)
        {
            var words = new List<string>(Text.Words(CombatMsg.AimNextPrevManual));
            if (words.Count < 3)
                words = ["NEXT", "PREV", "MANUAL"];
            var keys = new List<object> { AimKey.Next, AimKey.Prev, "manual" };
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
                case "manual":
                    if (Manual(menu, aim.Range, aim.Attack) is { } goal2)
                    {
                        if (ReferenceEquals(goal2, c))
                        {
                            if (aim.TargetOffered)
                                return AimKey.Target;
                        }
                        else
                            return StepTo(goal2, true);
                    }
                    View.Shown = c;
                    View.Cursor = Square(c);
                    View.StatusLine = Text[CombatMsg.Range] + aim.Distance.ToString(CultureInfo.InvariantCulture);
                    continue;
                case AimPick ap:
                    // A click on the creature shown targets it (when offered); on another creature, shows that one.
                    if (View.Fight?.Map?.At(ap.X, ap.Y) is { } picked)
                    {
                        if (!ReferenceEquals(picked, c))
                            return StepTo(picked, false);
                        if (aim.TargetOffered)
                            return AimKey.Target;
                    }
                    continue;
            }
        }
    }

    private AimKey StepTo(EclCharacter goal, bool target)
    {
        _goal = goal;
        _goalSteps = 0;
        _goalTarget = target;
        return AimKey.Next;
    }

    /// <summary>
    /// Manual (0x155F5): the cursor starts on the aiming creature; the answer is the creature on the square picked.
    /// [?] The Core model aims only at its candidate list (combat-menu.md): a square with no creature, or one the list
    /// lacks, gives nothing, and the chosen creature is reached by stepping the list (each step runs the target check,
    /// which the program's Manual runs only on the chosen square).
    /// </summary>
    private EclCharacter? Manual(FightMenu menu, int range, bool attack)
    {
        View.Mode = CombatMode.Manual;
        var start = Square(menu.Creature) ?? (View.ViewX + 3, View.ViewY + 3);
        View.Cursor = start;
        var r = ask(new CombatCursorRequest(range) { Attack = attack });
        View.Mode = CombatMode.Aim;
        if (r is ValueTuple<int, int> sq && View.Fight?.Map?.At(sq.Item1, sq.Item2) is { } c && (!attack || !ReferenceEquals(c, menu.Creature)))
            return c;
        return null;
    }

    public bool ConfirmFlee(FightMenu menu) => YesNo(Text[CombatMsg.Flee]);

    public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally)
    {
        View.Shown = ally;
        return YesNo(Text[CombatMsg.AttackAlly]);
    }

    public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable)
    {
        if (castable.Count == 0)
            return null;
        View.Mode = CombatMode.Spell;
        var rows = castable.Select(s => (SpellName(s) ?? "SPELL " + s.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant()).ToList();
        string cast = Word(FightCommand.Cast), exit = Text.Words(CombatMsg.AimCenterExit).ElementAtOrDefault(1) ?? "EXIT";
        // [?] The spell list 0x239D6 is not read: a full-screen list of the castable spells (our layout).
        var req = new ListRequest([new ListLine(Name(menu.Creature), NameColour(menu.Creature))], rows, [cast, exit], 1)
        {
            RowActions = new HashSet<int> { 0 },
        };
        var a = ask(req) as ListAnswer;
        View.Mode = CombatMode.Menu;
        if (a is null || a.Action != 0 || a.Row < 0 || a.Row >= castable.Count)
            return null;
        int spell = castable[a.Row];
        // 0x27C38 shows the cast message before its target pick; the Core menu aims first and casts after, so a spell
        // cast now is announced here (before the aim) and its Announce call is skipped. A begun spell is announced
        // when it goes off, after its aim [?].
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
        string text = notice switch
        {
            FightNotice.MagicOn => t[CombatMsg.MagicOn],
            FightNotice.MagicOff => t[CombatMsg.MagicOff],
            FightNotice.DoesNotWork => t[CombatMsg.DoesNotWork],
            FightNotice.Blocked => t[CombatMsg.Blocked],
            FightNotice.NotWithThatWeapon => t[CombatMsg.NotWithThatWeapon],
            FightNotice.Bandaged => t[CombatMsg.IsBandaged],
            FightNotice.Guarding => "", // [?] the Guard message (START 0x3226) is not located
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
        var lines = new List<PanelLine>();
        if (who is not null)
            lines.Add(new PanelLine(Name(who), NameColour(who)));
        if (text.Length > 0)
            lines.Add(new PanelLine(text.Trim(), 10));
        Say([.. lines]);
    }

    /// <summary>The spell's aim picked nothing (0x27EF2): yes = the spell is lost.</summary>
    public bool ConfirmAbortSpell(FightMenu menu)
    {
        bool abort = YesNo(Text[CombatMsg.AbortSpell]);
        if (abort)
            Say(new PanelLine(Text[CombatMsg.SpellAborted].Trim(), 10));
        return abort;
    }

    public bool ContinueBattle()
    {
        View.Messages.Clear();
        return YesNo(Text[CombatMsg.ContinueBattle]);
    }

    /// <summary>
    /// An <see cref="IFightMoves"/> that tells the screen after each step (so the AI's and the player's steps are seen);
    /// the moves themselves are the wrapped ones'.
    /// </summary>
    public sealed class ShownMoves(IFightMoves inner, Action<EclCharacter> stepped) : IFightMoves
    {
        public IFightMoves Inner { get; } = inner;

        public StepResult Step(Fight fight, EclCharacter c, int direction)
        {
            var r = Inner.Step(fight, c, direction);
            stepped(c);
            return r;
        }

        public bool Flee(Fight fight, EclCharacter c) => Inner.Flee(fight, c);
    }
}
