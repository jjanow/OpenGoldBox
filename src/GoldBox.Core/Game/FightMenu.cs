using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>The main combat menu's commands (GAME.OVR 0x9B21; docs/re/combat-menu.md "Menu").</summary>
public enum FightCommand
{
    // Entries (shown by 0x9E6D when their condition holds; a hidden entry can't be chosen).
    Move, View, Aim, Use, Cast, Turn, Quick, Done,
    // Keys accepted whether or not anything is shown: Alt-M, space, Alt-Q, Alt-R, Alt-X.
    ToggleQuickMagic, PartyToManual, AllQuick, AltR, GodsIntervene,
}

/// <summary>The Done menu's entries (0xA64C).</summary>
public enum DoneCommand { Guard, Delay, Quit, Bandage, Speed, Exit }

/// <summary>The game-speed menu's entries (0xA8FD).</summary>
public enum SpeedCommand { Slower, Faster, Exit }

/// <summary>Keys of the aim menu (0x1529B) the Core model takes (Center is screen-only). Manual opens the cursor on
/// squares (0x155F5, <see cref="IFightMenuChooser.ChooseManual"/>).</summary>
public enum AimKey { Next, Prev, Target, Exit, Manual }

/// <summary>
/// What Manual (0x155F5) reads: a cursor direction 0–7 (0 N, clockwise: the arrow, Home, PgUp, End, PgDn keys),
/// Center (re-centres the view, the cursor stays), Target (Return or T) or Exit (E or Escape).
/// </summary>
public readonly record struct ManualInput(int Direction)
{
    public static readonly ManualInput Center = new(8), Target = new(-1), Exit = new(-2);
    public bool IsDirection => Direction is >= 0 and <= 7;
}

/// <summary>
/// The Manual cursor as 0x155F5 shows it: the square (0..49 × 0..24), the creature under it (a live creature's slot,
/// or a body on ground 0x1F; null on an empty square or one the line from the aimer does not reach), the path
/// distance from the aimer in squares (0x2FF74, half squares ÷ 2; "Range = n" is shown in attack mode when the line
/// reaches the square), the aim's range, and whether Target is offered.
/// </summary>
public sealed record ManualView(int X, int Y, EclCharacter? Creature, int Distance, int Range, bool TargetOffered, bool Attack)
{
    /// <summary>The line 0x2FF74 from the aimer got through to the square (the "Range = n" line is shown then, in attack mode).</summary>
    public bool LineReached { get; init; } = true;
}

/// <summary>Messages the fight shows (paraphrased names; the text is the Client's).</summary>
public enum FightNotice
{
    MagicOn, MagicOff, DoesNotWork, Blocked, NotWithThatWeapon, Bandaged, Guarding, Sweeps, TurnsUndead, Turned,
    Destroyed, NothingHappens, CampOnlySpell, BeginsCasting, GodsIntervene,
    /// <summary>A several-target spell aimed at a creature already taken (0x14B79).</summary>
    AlreadyTargeted,
}

/// <summary>What the move mode (0xA1E6) reads: a direction 0–7 (0 N, clockwise), Return (keep the move) or Escape
/// (undo it).</summary>
public readonly record struct MoveInput(int Direction)
{
    public static readonly MoveInput Return = new(-1), Escape = new(-2);
    public bool IsDirection => Direction is >= 0 and <= 7;
}

/// <summary>One aim candidate as the aim menu shows it (0x1529B).</summary>
public sealed record AimView(EclCharacter Candidate, int Distance, int Range, bool TargetOffered)
{
    /// <summary>Attack mode (the menu's Aim); false for a spell's aim (0x14611), where Target only picks the creature.</summary>
    public bool Attack { get; init; } = true;
}

/// <summary>
/// The player's side of the menu: the Client's UI (or a test script) answers each question the program asks. Every
/// method is called only at the point where the program waits for a key.
/// </summary>
public interface IFightMenuChooser
{
    /// <summary>The main menu (0x9E6D): one of <paramref name="entries"/> or an always-accepted key.</summary>
    FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries);
    /// <summary>The Done menu (0xA64C).</summary>
    DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries);
    /// <summary>The speed menu (0xA8FD) with the game speed DS:49E2.</summary>
    SpeedCommand ChooseSpeed(FightMenu menu, byte speed, IReadOnlyList<SpeedCommand> entries) => SpeedCommand.Exit;
    /// <summary>Move mode, with the squares of movement left (+6 shr 1).</summary>
    MoveInput ChooseMove(FightMenu menu, int movementLeft);
    /// <summary>The aim menu on one candidate.</summary>
    AimKey ChooseAim(FightMenu menu, AimView view);
    /// <summary>Manual (0x155F5), once per cursor position; the default leaves at once (Exit).</summary>
    ManualInput ChooseManual(FightMenu menu, ManualView view) => ManualInput.Exit;
    /// <summary>The flee question (moving off the map).</summary>
    bool ConfirmFlee(FightMenu menu);
    /// <summary>The attack-ally question (0x15193).</summary>
    bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally);
    /// <summary>The spell list (0x239D6): a spell id from <paramref name="castable"/>, or null to go back.</summary>
    int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable);
    /// <summary>The spell's aim picked nothing: abort the spell (yes: it is lost; no: aim again; 0x27EF2).</summary>
    bool ConfirmAbortSpell(FightMenu menu) => true;
    /// <summary>A message (<paramref name="who"/> is the creature it names, if any).</summary>
    void Notice(FightNotice notice, EclCharacter? who) { }
    /// <summary>The end-of-round "continue battle" question (0xA087).</summary>
    bool ContinueBattle() => false;
}

/// <summary>
/// The character screens the menu opens: View (0x21AED) and Use (0x21F32), read in combat-menu.md "View / Use"
/// (<see cref="FightItemScreens"/>). Each returns the "done"
/// flag the screen reports (an action that used the turn).
/// </summary>
public interface IFightItemScreens
{
    bool View(Fight fight, EclCharacter c) => false;
    bool Use(Fight fight, EclCharacter c) => false;
    /// <summary>The same screens opened from the combat menu: an item's spell aims through the menu's aim
    /// (<see cref="FightMenu.PickForItem"/>).</summary>
    bool View(Fight fight, EclCharacter c, FightMenu menu) => View(fight, c);
    bool Use(Fight fight, EclCharacter c, FightMenu menu) => Use(fight, c);
}

/// <summary>Screens that do nothing.</summary>
public sealed class NoFightItemScreens : IFightItemScreens
{
    public static readonly NoFightItemScreens Instance = new();
}

/// <summary>
/// The player's combat menu for one creature's turn (GAME.OVR 0x9B21 and the routines it calls; docs/re/combat-menu.md),
/// headless: it asks <see cref="IFightMenuChooser"/> wherever the program reads a key and acts through
/// <see cref="Fight"/> and its seams (<see cref="Fight.Map"/>, <see cref="Fight.Moves"/>, <see cref="Fight.Spells"/>).
/// The menu itself draws no random numbers.
/// </summary>
public sealed class FightMenu
{
    /// <summary>VM cells: no magic here ($49E5, word [DS:49D2]+0x1CA), the party attacked an ally ($6E33).</summary>
    public const ushort NoMagicCell = 0x49E5, AttackedAlliesCell = 0x6E33;
    /// <summary>Creature bytes: memorised spells 0x17–0x2B, item count 0xC7.</summary>
    public const int SpellsFirst = 0x17, SpellsLast = 0x2B, ItemCount = 0xC7;
    /// <summary>Trigger classes of the target check 0x13880 (1 on the defender, 0 on the attacker).</summary>
    public const int TriggerTargetDefender = 1, TriggerTargetAttacker = 0;
    /// <summary>Aim's search range (0x15AFF) and the "from the weapon" range argument.</summary>
    public const int AimSearchRange = 0x7F, RangeFromWeapon = 0xFF;

    /// <summary>Step offsets of the 8 directions (0 N, clockwise; y grows downwards), as the combat map uses them.</summary>
    public static readonly (int Dx, int Dy)[] Steps = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    public FightMenu(Fight fight, EclCharacter creature, IFightMenuChooser chooser, IFightController? ai = null,
        IFightItemScreens? screens = null, TurnUndeadTable? turnTable = null)
    {
        Fight = fight;
        Creature = creature;
        Chooser = chooser;
        Ai = ai ?? new EndActionsController();
        Screens = screens ?? NoFightItemScreens.Instance;
        TurnTable = turnTable;
    }

    public Fight Fight { get; }
    public EclCharacter Creature { get; }
    public IFightMenuChooser Chooser { get; }
    /// <summary>The AI that acts after Quick (0xABCF).</summary>
    public IFightController Ai { get; }
    public IFightItemScreens Screens { get; }
    public TurnUndeadTable? TurnTable { get; }
    public CombatRecord Record => Fight.Record(Creature);
    /// <summary>Alt-X works (the program's debugging switch, set from its command line). Off = the original's normal play.</summary>
    public bool CheatEnabled { get; init; }
    /// <summary>DS:49E2: game speed (0 fastest … 9), changed by the Done menu's Speed.</summary>
    public byte GameSpeed { get; set; }
    /// <summary>Choices the chooser made that the program would not accept (hidden entries); each is asked again.</summary>
    public int Rejected { get; private set; }

    private const int MaxRejected = 1000;

    private void Notice(FightNotice n, EclCharacter? who = null) => Chooser.Notice(n, who);

    // ---- 0x9B21 -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x9B21: out of play → actions end. A spell begun earlier (+0 &gt; 0): +0 = 0 (0x9B59, before the cast, so the
    /// caster caught in its own area spell loses nothing: no lost-spell line, no second copy removed), cast now
    /// (0x27C38: aimed now, <see cref="IFightSpells.ContinueCasting"/>), actions end.
    /// Else menu loop until an action reports done; each display runs trigger 0x0F first (0x9E7D).
    /// </summary>
    /// <summary>The spell begun earlier that <see cref="Run"/> is casting now (0x9B21 passes it to 0x27C38 with +0
    /// already cleared), for the chooser's cast message before the aim; 0 otherwise.</summary>
    public int BegunSpell { get; private set; }

    public void Run()
    {
        var c = Creature;
        if (c[FightRules.InPlay] == 0)
        {
            Fight.EndActions(c);
            return;
        }
        var rec = Record;
        if (rec[CombatRecord.SpellCast] > 0)
        {
            // 0x9B4B–0x9B59: the spell taken from +0 and +0 cleared before 0x27C38 (the AI's 0xACEE does not clear it).
            int begun = rec[CombatRecord.SpellCast];
            rec[CombatRecord.SpellCast] = 0;
            BegunSpell = begun;
            try
            {
                CastNow(begun, true);
            }
            finally
            {
                BegunSpell = 0;
            }
            Fight.EndActions(c);
            return;
        }
        bool done = false;
        while (!done)
        {
            Fight.Effects.Trigger(FightRules.TriggerActionStart, c, Fight.State);
            var entries = Entries();
            FightCommand cmd;
            while (!Accepted(cmd = Chooser.Choose(this, entries), entries))
                if (Reject())
                    return;
            done = Perform(cmd);
        }
    }

    private bool Reject()
    {
        if (++Rejected < MaxRejected)
            return false;
        Fight.Diagnostics.Add($"menu chooser kept choosing hidden entries for {Creature.Name}; actions ended");
        Fight.EndActions(Creature);
        return true;
    }

    private static bool Accepted(FightCommand cmd, IReadOnlyList<FightCommand> entries) =>
        cmd >= FightCommand.ToggleQuickMagic || entries.Contains(cmd);

    /// <summary>
    /// 0x9E6D: Move when +6 &gt; 0; View, Aim; Use when the item count 0xC7 &gt; 0; Cast when a byte of 0x17–0x2B is non-zero,
    /// +1 ≠ 0 and VM $49E5 = 0; Turn when the cleric level 0x96 &gt; 0 (signed) and +0x11 = 0; Quick, Done.
    /// </summary>
    public IReadOnlyList<FightCommand> Entries()
    {
        var c = Creature;
        var rec = Record;
        var e = new List<FightCommand>();
        if (rec[CombatRecord.Movement] > 0)
            e.Add(FightCommand.Move);
        e.Add(FightCommand.View);
        e.Add(FightCommand.Aim);
        if (c[ItemCount] > 0)
            e.Add(FightCommand.Use);
        bool spells = false;
        for (int i = SpellsFirst; i <= SpellsLast; i++)
            if (c[i] > 0)
                spells = true;
        if (spells && rec[CombatRecord.CastAllowed] != 0 && Fight.Memory.Read(NoMagicCell) == 0)
            e.Add(FightCommand.Cast);
        if ((sbyte)c[TurnUndead.ClericLevel] > 0 && rec[CombatRecord.TriedTurning] == 0)
            e.Add(FightCommand.Turn);
        e.Add(FightCommand.Quick);
        e.Add(FightCommand.Done);
        return e;
    }

    /// <summary>
    /// 0x1638E with the start word (ParamStr(1) equal to its copy at GAME.OVR 0x16374, the hall's J word; read whole
    /// s51, every branch): after its message (0x1637A, START 0x2233), in list order every hostile creature (0x10E = 1)
    /// gets in play 0 and status 6 and its slot's icon size 0 (DS:5F2A + 4 × slot; no grid rebuild, no body, no body
    /// sound: not 0x3154B; a creature with no slot is skipped there, where the program zeroes the slot count: an
    /// original bug fixed, s51, <see cref="CombatMap.ClearIconSize"/>), and every creature's actions end (START 0x31D7, its result unused); then the view is
    /// redrawn in place (0x310D0(view x + 3, view y + 3, 255, 8): drawing only). A hostile party member (charmed) is
    /// treated the same. Called by Alt-X in the menu and by X in the AI's key check (0xBB81). Oracle:
    /// <c>por_altx_oracle.py</c> (AltXProgramTests).
    /// </summary>
    public static void GodsIntervene(Fight fight)
    {
        foreach (var x in fight.List)
        {
            if (x[FightRules.Hostile] == 1)
            {
                x[FightRules.InPlay] = 0;
                x[FightRules.Status] = (byte)CreatureStatus.Dead;
                fight.Map?.ClearIconSize(x);
            }
            fight.EndActions(x);
        }
    }

    /// <summary>Runs one command of the main menu (0x9BA1–0x9DC0). Returns "done".</summary>
    public bool Perform(FightCommand cmd)
    {
        var c = Creature;
        switch (cmd)
        {
            case FightCommand.Move:
                return Move();
            case FightCommand.View:
            {
                bool done = Screens.View(Fight, c, this);
                FightRules.RecalcAttacksLeft(c, Record, Fight.Items, Fight.State, Fight.Effects);
                return done;
            }
            case FightCommand.Aim:
                return Aim();
            case FightCommand.Use:
            {
                bool done = Screens.Use(Fight, c, this);
                FightRules.RecalcAttacksLeft(c, Record, Fight.Items, Fight.State, Fight.Effects);
                return done;
            }
            case FightCommand.Cast:
                return Cast();
            case FightCommand.Turn:
                TurnUndead.Run(Fight, c, TurnTable, Notice);
                Fight.EndActions(c);
                return true;
            case FightCommand.Quick:
                MakeQuick(Fight, c);
                Ai.Act(Fight, c, true);
                return true;
            case FightCommand.Done:
                return DoneMenu();
            case FightCommand.ToggleQuickMagic:
                Fight.State.QuickMagic = !Fight.State.QuickMagic;
                Notice(Fight.State.QuickMagic ? FightNotice.MagicOn : FightNotice.MagicOff);
                return false;
            case FightCommand.PartyToManual:
                foreach (var x in Fight.List)
                    if (x[CombatRules.Npc] < 0x80)
                        x[CombatRules.AutoCombat] = 0;
                return false;
            case FightCommand.AllQuick:
                Record.InitiativeValue = 20;
                foreach (var x in Fight.List)
                    MakeQuick(Fight, x);
                return true;
            case FightCommand.GodsIntervene:
                if (!CheatEnabled)
                {
                    Notice(FightNotice.DoesNotWork);
                    return false;
                }
                Notice(FightNotice.GodsIntervene);
                GodsIntervene(Fight);
                return true;
            default: // Alt-R: accepted, nothing happens
                return false;
        }
    }

    // ---- Done menu (0xA64C) ----------------------------------------------------------------------------------------

    /// <summary>0xA64C: Guard (no ranged weapon, or a throwable one), Delay, Quit, Bandage (a party member is dying),
    /// Speed, Exit.</summary>
    public IReadOnlyList<DoneCommand> DoneEntries()
    {
        var e = new List<DoneCommand>();
        if (!FightRules.HasRangedWeapon(Creature, Fight.Items) || FightRules.HasThrowableWeapon(Creature, Fight.Items))
            e.Add(DoneCommand.Guard);
        e.Add(DoneCommand.Delay);
        e.Add(DoneCommand.Quit);
        if (Bandage(Fight, false) is not null)
            e.Add(DoneCommand.Bandage);
        e.Add(DoneCommand.Speed);
        e.Add(DoneCommand.Exit);
        return e;
    }

    /// <summary>0xA64C: the Done menu until Exit or an entry that ends the turn. Returns "done".</summary>
    public bool DoneMenu()
    {
        var c = Creature;
        var entries = DoneEntries();
        while (true)
        {
            var cmd = Chooser.ChooseDone(this, entries);
            if (!entries.Contains(cmd))
            {
                if (Reject())
                    return true;
                continue;
            }
            switch (cmd)
            {
                case DoneCommand.Guard:
                    Fight.Guard(c); // its message through Fight.Guarded (START 0x3226)
                    return true;
                case DoneCommand.Delay:
                    Fight.Delay(c);
                    return true;
                case DoneCommand.Quit:
                    Fight.EndActions(c);
                    return true;
                case DoneCommand.Bandage:
                    if (Bandage(Fight, true) is { } b)
                        Notice(FightNotice.Bandaged, b);
                    Fight.EndActions(c);
                    return true;
                case DoneCommand.Speed:
                    SpeedMenu();
                    break;
                default:
                    return false;
            }
        }
    }

    /// <summary>0xA8FD: Slower (shown when DS:49E2 &lt; 9) adds 1, Faster (when &gt; 0) subtracts 1, until Exit.</summary>
    private void SpeedMenu()
    {
        while (true)
        {
            var e = new List<SpeedCommand>();
            if (GameSpeed < 9)
                e.Add(SpeedCommand.Slower);
            if (GameSpeed > 0)
                e.Add(SpeedCommand.Faster);
            e.Add(SpeedCommand.Exit);
            var cmd = Chooser.ChooseSpeed(this, GameSpeed, e);
            if (!e.Contains(cmd))
            {
                if (Reject())
                    return;
                continue;
            }
            if (cmd == SpeedCommand.Slower)
                GameSpeed++;
            else if (cmd == SpeedCommand.Faster)
                GameSpeed--;
            else
                return;
        }
    }

    /// <summary>
    /// 0xA806: the first creature in list order with combat record +0x13 = 0, hostile 0 and status 5 (dying). With
    /// <paramref name="bandage"/> it becomes unconscious (status 4) and its dying count +0x0E = 0. Null when none.
    /// </summary>
    public static EclCharacter? Bandage(Fight fight, bool bandage)
    {
        EclCharacter? first = null;
        foreach (var x in fight.List)
        {
            if (fight.Record(x)[CombatRecord.NotParty] != 0 || x[FightRules.Hostile] != 0 ||
                x[FightRules.Status] != (byte)CreatureStatus.Dying)
                continue;
            if (first is null)
            {
                first = x;
                if (bandage)
                {
                    x[FightRules.Status] = (byte)CreatureStatus.Unconscious;
                    fight.Record(x)[CombatRecord.Dying] = 0;
                }
            }
        }
        return first;
    }

    /// <summary>0xAA32: computer-controlled from now on (0x10F = 1); a remembered target (+0x0A) on its own side is forgotten.</summary>
    public static void MakeQuick(Fight fight, EclCharacter c)
    {
        c[CombatRules.AutoCombat] = 1;
        var rec = fight.Record(c);
        if (rec.Other is { } o && o[FightRules.Hostile] == c[FightRules.Hostile])
            rec.Other = null;
    }

    // ---- Move (0xA1E6) ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 0xA1E6: while movement +6 &gt; 1 and the last key was not Return / Escape: a direction faces that way (+9) and
    /// looks at the square there: a creature → attack it (<see cref="AttackByMove"/>); off the map → the flee question,
    /// yes → flee (<see cref="IFightMoves.Step"/> off the edge, 0x13465 always ends the actions, done); else a step
    /// (<see cref="IFightMoves.Step"/>: 0x130AD, 0x12F0D, the terrain effects 0x2B9D1), "blocked" when it can't; a
    /// creature out of play or helpless after it ends its actions. Escape undoes the move: +6 and facing restored and the
    /// creature put back (0x317E1; done when it can't be). At the end +6 &lt; 2 becomes 0. Returns "done".
    /// </summary>
    public bool Move()
    {
        var c = Creature;
        var rec = Record;
        byte savedMove = rec[CombatRecord.Movement], savedFacing = rec[CombatRecord.Facing];
        var map = Fight.Map;
        var start = map?.Position(c);
        bool done = false;
        var key = new MoveInput(8);
        while (rec[CombatRecord.Movement] > 1 && key != MoveInput.Return && key != MoveInput.Escape)
        {
            key = Chooser.ChooseMove(this, rec[CombatRecord.Movement] >> 1);
            if (key == MoveInput.Escape)
            {
                rec[CombatRecord.Movement] = savedMove;
                // 0x317E1 puts it back; done when that fails. With no combat map nothing moved: not done.
                done = map is not null && start is { } s && !map.Place(c, s.X, s.Y);
                rec[CombatRecord.Facing] = savedFacing;
                continue;
            }
            if (!key.IsDirection)
                continue;
            int dir = key.Direction;
            rec[CombatRecord.Facing] = (byte)dir;
            EclCharacter? occupant = null;
            bool offMap = false;
            if (map is not null)
            {
                var (x, y) = map.Position(c);
                int tx = x + Steps[dir].Dx, ty = y + Steps[dir].Dy;
                occupant = map.At(tx, ty);
                offMap = occupant is null && map.IsOffMap(tx, ty);
            }
            if (occupant is not null && !ReferenceEquals(occupant, c))
                AttackByMove(occupant, ref done);
            else if (offMap)
            {
                if (Chooser.ConfirmFlee(this))
                {
                    Fight.Moves.Step(Fight, c, dir);
                    Fight.EndActions(c);
                    done = true;
                }
                else
                    done = false;
            }
            else
            {
                // 0xA45B–0xA50F: only the terrain cost test before the step shows the message (0xA487); once the
                // step is taken (0x130AD, 0x12F0D, 0x2B9D1) the helpless test (START 0x1713) runs whether or not
                // 0x12F0D moved (it fails silently for want of movement, +6 = 0). s52: Core had shown the message for
                // that failure too and skipped the helpless test, so a creature made helpless by a free attack kept
                // its turn.
                var r = Fight.Moves.Step(Fight, c, dir);
                if (r is StepResult.Blocked)
                    Notice(FightNotice.Blocked);
                else if (c[FightRules.InPlay] == 0 || FightRules.IsHelpless(c))
                {
                    Fight.EndActions(c);
                    done = true;
                }
            }
        }
        if (rec[CombatRecord.Movement] < 2)
            rec[CombatRecord.Movement] = 0;
        return done;
    }

    /// <summary>
    /// 0xA54C: attack the creature moved into. A ranged weapon that is not throwable can't ("not with that weapon").
    /// Else the attack-ally check; then +0x0A = the defender, the sweep when it applies (done), else the note-direction
    /// + MakeAttack pair with no ammunition (done = actions over). <paramref name="done"/> is left alone otherwise.
    /// </summary>
    public void AttackByMove(EclCharacter defender, ref bool done)
    {
        var c = Creature;
        if (FightRules.HasRangedWeapon(c, Fight.Items) && !FightRules.HasThrowableWeapon(c, Fight.Items))
        {
            Notice(FightNotice.NotWithThatWeapon);
            return;
        }
        if (!CanAttack(defender))
            return;
        Record.Other = defender;
        if (SweepAttack.Try(Fight, c, defender, Notice))
        {
            done = true;
            return;
        }
        done = Fight.Attack(c, defender).ActionsDone;
    }

    /// <summary>
    /// 0x15193: a defender on the other side (START 0x2F57) or computer-controlled is attacked without a question. A
    /// player-controlled ally asks first; yes: VM $6E33 = 1 (DS:6DE2 = 1 too, a byte nothing reads), every okay NPC
    /// (status 0, 0x84 &gt; 0x7F) becomes hostile and forgets its target, the teams are counted again.
    /// </summary>
    public bool CanAttack(EclCharacter defender)
    {
        var c = Creature;
        if (defender[FightRules.Hostile] == FightSearch.EnemySide(c) || defender[CombatRules.AutoCombat] != 0)
            return true;
        if (!Chooser.ConfirmAttackAlly(this, defender))
            return false;
        Fight.Memory.Write(AttackedAlliesCell, 1);
        foreach (var x in Fight.List)
        {
            if (x[FightRules.Status] == 0 && x[CombatRules.Npc] > 0x7F)
            {
                x[FightRules.Hostile] = 1;
                Fight.Record(x).Other = null;
            }
        }
        FightRules.CountTeams(Fight.List, Fight.State);
        return true;
    }

    // ---- Aim (0x15D53) ---------------------------------------------------------------------------------------------

    /// <summary>Aim's range for "from the weapon" (0x15D83–0x15DD4): a readied weapon's base range (byte 12) − 1, no
    /// weapon 1; 0 or 0xFF become 1.</summary>
    public static int AimRange(EclCharacter c, BaseItemTable items)
    {
        byte r = FightRules.Equipment(c, items)[FightRules.SlotWeapon] is { } w
            ? (byte)(items[w[0x2E], BaseItemTable.Range] - 1)
            : (byte)1;
        return r is 0 or 0xFF ? 1 : r;
    }

    /// <summary>
    /// 0x13880, the target check: none → no; the attacker itself → yes; else DS:681E = 0, trigger 1 on the defender,
    /// and while still 0 trigger 0 on the attacker with its +0x0A set to the defender for the call; yes when DS:681E
    /// stayed 0.
    /// </summary>
    public static bool Targetable(Fight fight, EclCharacter attacker, EclCharacter? target)
    {
        if (target is null)
            return false;
        if (ReferenceEquals(target, attacker))
            return true;
        fight.State.TargetBlocked = 0;
        fight.Effects.Trigger(TriggerTargetDefender, target, fight.State);
        if (fight.State.TargetBlocked == 0)
        {
            var rec = fight.Record(attacker);
            var saved = rec.Other;
            rec.Other = target;
            fight.Effects.Trigger(TriggerTargetAttacker, attacker, fight.State);
            rec.Other = saved;
        }
        return fight.State.TargetBlocked == 0;
    }

    /// <summary>
    /// 0x1529B, attack mode: Target is offered when the distance (START 0x30FB) ≤ <paramref name="range"/>, the candidate
    /// is not the creature itself, and the creature has no ranged weapon, or has one with ammunition (START 0x3A2B) and
    /// no enemy next to it (START 0x2FD3 range 1) unless the weapon is throwable.
    /// </summary>
    public bool TargetOffered(EclCharacter candidate, int range)
    {
        var c = Creature;
        if (Fight.Geometry.Distance(c, candidate) > range)
            return false;
        if (ReferenceEquals(candidate, c))
            return false;
        if (!FightRules.HasRangedWeapon(c, Fight.Items))
            return true;
        if (!FightRules.FindRangedAmmo(c, Fight.Items, out _))
            return false;
        return FightSearch.EnemiesNear(Fight, c, 1).Count == 0 || FightRules.HasThrowableWeapon(c, Fight.Items);
    }

    /// <summary>
    /// 0x15D53 in attack mode with the weapon's range (the menu's Aim): the candidates are the area search from the
    /// creature (range 0x7F), walked from the first with Next / Prev (wrapping, 0x15B84), skipping those that fail the
    /// target check in the direction of travel. Target (when offered) attacks (<see cref="TargetAttack"/>) and rebuilds
    /// the list; Aim goes on while not done, until Exit. Returns "done". Manual (<see cref="Manual"/>) moves a cursor
    /// over squares (square mode 0 here, 0x9BEE: only a creature's square can be taken).
    /// </summary>
    public bool Aim() => AimAt(RangeFromWeapon, true, false, out _, out _);

    /// <summary>
    /// 0x15D53 (creature, range, attack mode, square mode, done, result) on the candidate list. <paramref name="range"/>
    /// 0xFF = from the weapon (<see cref="AimRange"/>); a range of 0 or 0xFF becomes 1 (0x15DC6). The result is cleared
    /// first (0x15D77). In attack mode Target attacks (<see cref="TargetAttack"/>); otherwise (0x15438 with attack mode
    /// 0) it only takes the candidate and its square as the result and reports done. Manual runs the cursor
    /// (<see cref="Manual"/>, starting on the shown candidate), then the list is built again (0x15AE1) and the walk
    /// goes on from the same place. Aim goes on while not done, until Exit (E or Escape, set CS:0x3538). Returns
    /// "done"; <paramref name="result"/> is the creature taken (null when none, or for a bare square taken by Manual
    /// in square mode) and <paramref name="square"/> the square taken (null when nothing was taken).
    /// </summary>
    private bool AimAt(int range, bool attack, bool squareMode, out EclCharacter? result, out (int X, int Y)? square)
    {
        var c = Creature;
        result = null;
        square = null;
        range = range == RangeFromWeapon ? AimRange(c, Fight.Items) : (byte)range;
        if (range is 0 or 0xFF)
            range = 1;
        var list = FightSearch.Nearby(Fight, c, AimSearchRange);
        if (list.Count == 0)
            return false;
        int idx = 0, dir = 1, skipped = 0; // dir: the last Next / Prev (bp-0xDC), the direction skips go
        bool done = false;
        while (true)
        {
            var cand = list[idx];
            if (!Targetable(Fight, c, cand))
            {
                if (++skipped > list.Count)
                    return done; // [?] nobody targetable (the program would loop); the creature itself always is
                idx = Wrap(idx + dir, list.Count);
                continue;
            }
            skipped = 0;
            bool offered = attack ? TargetOffered(cand, range) : Fight.Geometry.Distance(c, cand) <= range;
            var view = new AimView(cand, Fight.Geometry.Distance(c, cand), range, offered) { Attack = attack };
            var key = Chooser.ChooseAim(this, view);
            switch (key)
            {
                case AimKey.Next:
                    dir = 1;
                    idx = Wrap(idx + 1, list.Count);
                    AimFlight(cand, list[idx]);
                    break;
                case AimKey.Prev:
                    dir = -1;
                    idx = Wrap(idx - 1, list.Count);
                    AimFlight(cand, list[idx]);
                    break;
                case AimKey.Target:
                    if (!view.TargetOffered)
                    {
                        if (Reject())
                            return true;
                        break;
                    }
                    if (!attack)
                    {
                        result = cand; // 0x15438 with attack mode 0: result = the candidate and its square, done
                        square = Fight.Map is { } m && m.OnMap(cand) ? m.Position(cand) : null;
                        return true;
                    }
                    done = TargetAttack(cand);
                    if (done)
                        return true;
                    list = FightSearch.Nearby(Fight, c, AimSearchRange);
                    if (list.Count == 0)
                        return false;
                    idx = idx >= list.Count ? 0 : idx; // same place in the rebuilt list (step 0, 0x15B84 wraps)
                    break;
                case AimKey.Manual:
                    done = Manual(cand, range, attack, squareMode, out result, out square);
                    if (done)
                        return true;
                    list = FightSearch.Nearby(Fight, c, AimSearchRange); // 0x15AE1 again, step 0
                    if (list.Count == 0)
                        return false;
                    idx = idx >= list.Count ? 0 : idx;
                    break;
                default:
                    return done;
            }
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>
    /// 0x15B84 with its fly flag set (the call at 0x15FB4 after a key that is not Exit and left Aim not done): the cursor
    /// box flies (<see cref="FlightAnimation.AimCursor"/>, 0x15D02) from the shown candidate's square (DS:5F27 / 5F28)
    /// to the square of the list entry one step on, before that entry's target check (a skip, 0x15E4D, has the flag
    /// clear and flies nothing). Only Next / Prev step; the step-0 calls (Aim's start, after Target, Manual or Center)
    /// fly from a square to itself, which draws nothing. A display hook.
    /// </summary>
    private void AimFlight(EclCharacter from, EclCharacter to)
    {
        if (Fight.State.Flight is { } fly && Fight.Map is { } m && m.OnMap(from) && m.OnMap(to))
        {
            var (fx, fy) = m.Position(from);
            var (tx, ty) = m.Position(to);
            fly(FlightAnimation.AimCursor(fx, fy, tx, ty));
        }
    }

    /// <summary>
    /// The player's pick for an item's spell (0x22AC4 → 0x27C38 with ai = 0, the same pick loop as a cast from the
    /// menu): the aim (<see cref="AimSpell"/>: 0x148BE / 0x14611 / 0x15D53, Manual included) and the target pick
    /// (<see cref="FightSpells.Pick"/>); not picked → the abort question (0x27EF2): yes → not picked, no → aim again.
    /// Not picked still uses the item up in a fight when the spell's table byte 11 ≠ 0 (<see cref="FightSpells.UseItemNow"/>).
    /// Needs <see cref="FightSpells"/>.
    /// </summary>
    public bool PickForItem(int spell)
    {
        if (Fight.Spells is not FightSpells fs)
            return false;
        while (true)
        {
            var (picked, targets, square) = AimSpell(spell);
            if (picked)
                return fs.Pick(Fight, Creature, spell, targets, square);
            if (Chooser.ConfirmAbortSpell(this) || Reject())
                return false;
        }
    }

    /// <summary>
    /// 0x155F5, Manual: a cursor on squares, starting on <paramref name="start"/>'s square. Each pass moves it one square
    /// in the direction read (Center and other keys: none), kept within 0..49 × 0..24, then: the line from the aimer
    /// (0x2FF74, any length) to the cursor; when it reaches the square, the creature there is the live creature in its
    /// slot, else on a body (ground 0x1F) the last body of the list DS:66D6 lying there. Target is offered when the line
    /// reached it, the distance (half squares ÷ 2) ≤ <paramref name="range"/> and the ground's cost is not 0xFF; and
    /// with a creature: it passes the target check (0x13880), and in attack mode it is not the aimer, not a body with
    /// no live occupant, and the ranged-weapon rule of <see cref="TargetOffered"/> holds; with no creature: only in
    /// <paramref name="squareMode"/>. Target with Target offered: the result is the creature (or none) and the square;
    /// in attack mode the attack (0x15438, <see cref="TargetAttack"/>) gives "done", otherwise done = 1. Target
    /// otherwise, or not done, or Exit: the result is cleared, done 0. Manual ends after any Target or Exit (set
    /// CS:0x2DA7: Escape, Return, E, T). Needs the combat map (<see cref="CombatMap"/>); without one it ends at once
    /// [our choice].
    /// </summary>
    public bool Manual(EclCharacter start, int range, bool attack, bool squareMode, out EclCharacter? result, out (int X, int Y)? square)
    {
        var c = Creature;
        result = null;
        square = null;
        if (Fight.Geometry is not CombatMap map || !map.OnMap(start) || !map.OnMap(c))
            return false;
        var (x, y) = map.Position(start);
        int dir = CombatTables.Center;
        while (true)
        {
            x = Math.Clamp(x + CombatTables.DirDx[dir], 0, CombatArena.Width - 1);
            y = Math.Clamp(y + CombatTables.DirDy[dir], 0, CombatArena.Height - 1);
            var (slot, ground) = map.Contents(x, y);
            var (cx, cy) = map.Position(c);
            int tx = x, ty = y, dist = 0xFF;
            bool offered = map.WalkLine(cx, cy, ref tx, ref ty, ref dist);
            bool reached = offered;
            dist >>= 1;
            EclCharacter? who = null;
            if (offered)
            {
                if (slot > 0)
                    who = map.SlotCreature(slot);
                else if (ground == ArenaBuilder.Body)
                    foreach (var b in map.Bodies)
                        if (b is not null && (byte)b.X == x && (byte)b.Y == y)
                            who = b.Creature;
            }
            if ((byte)range < dist || map.Tables.Cost((byte)ground) == 0xFF)
                offered = false;
            if (who is not null)
            {
                if (!Targetable(Fight, c, who))
                    offered = false;
                if (attack)
                {
                    if (ReferenceEquals(who, c) || (slot == 0 && ground == ArenaBuilder.Body))
                        offered = false;
                    else if (FightRules.HasRangedWeapon(c, Fight.Items))
                    {
                        if (!FightRules.FindRangedAmmo(c, Fight.Items, out _))
                            offered = false;
                        else if (FightSearch.EnemiesNear(Fight, c, 1).Count > 0 && !FightRules.HasThrowableWeapon(c, Fight.Items))
                            offered = false;
                    }
                }
            }
            else if (!squareMode)
                offered = false;
            var key = Chooser.ChooseManual(this, new ManualView(x, y, who, dist, range, offered, attack) { LineReached = reached });
            if (key == ManualInput.Target)
            {
                bool done = false;
                if (offered)
                {
                    result = who;
                    square = (x, y);
                    done = !attack || (who is not null && TargetAttack(who));
                }
                if (!done)
                {
                    result = null;
                    square = null;
                }
                return done;
            }
            if (key == ManualInput.Exit)
                return false;
            dir = key.IsDirection ? key.Direction : CombatTables.Center;
        }
    }

    /// <summary>
    /// 0x15438, attack mode: the attack-ally check (not allowed → not done); the sweep when it applies → actions end,
    /// done; else the note-direction + MakeAttack pair (<see cref="Fight.Attack"/>) with the ranged weapon's
    /// ammunition (START 0x3A2B), none for a throwable weapon at distance 1. Returns "done".
    /// </summary>
    public bool TargetAttack(EclCharacter target)
    {
        var c = Creature;
        if (!CanAttack(target))
            return false;
        if (SweepAttack.Try(Fight, c, target, Notice))
        {
            Fight.EndActions(c);
            return true;
        }
        byte[]? ammo = null;
        if (FightRules.HasRangedWeapon(c, Fight.Items) && FightRules.FindRangedAmmo(c, Fight.Items, out var a))
        {
            ammo = a;
            if (FightRules.HasThrowableWeapon(c, Fight.Items) && Fight.Geometry.Distance(c, target) == 1)
                ammo = null;
        }
        return Fight.Attack(c, target, false, ammo).ActionsDone;
    }

    // ---- Cast (0x14C0F) --------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x14C0F(0, ai = 0) from the menu, ported whole (spells-effects.md "Combat cast"; checked under emulation,
    /// CombatCastProgramTests): the spell list 0x239D6(kind 0, action 1, position −1) picks a memorised spell
    /// (<see cref="IFightSpells.Castable"/>, the list's ids; Exit or an empty list → 0, not done). Then
    /// <see cref="FightSpells.CombatCastStep"/>: camp only → the message, not done; casting time 0 → cast now
    /// (<see cref="CastNow"/>: the player aims, then <see cref="IFightSpells.Cast"/> / <see cref="IFightSpells.CastOn"/>
    /// with what was aimed at) and the actions end (START 0x31D7); else "begins casting", +0 = the spell, initiative −
    /// the casting time when larger, else 1 (<see cref="Fight.Postpone"/>): cast at the creature's next pick this round.
    /// The redraw the program does for ai = 0 after the pick (START 0x3728, 0x319D2, START 0x1583) is the Client's.
    /// </summary>
    public bool Cast()
    {
        var c = Creature;
        var castable = Fight.Spells.Castable(Fight, c);
        if (Chooser.ChooseSpell(this, castable) is not { } spell || spell <= 0)
            return false;
        switch (FightSpells.CombatCastStep(Fight.Spells, Record, spell, out sbyte initiative))
        {
            case CombatCastOutcome.CastNow:
                CastNow(spell, false);
                Fight.EndActions(c);
                return true;
            case CombatCastOutcome.Begun:
                Notice(FightNotice.BeginsCasting, c);
                Fight.Postpone(c, initiative);
                return true;
            case CombatCastOutcome.CampOnly:
                Notice(FightNotice.CampOnlySpell);
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// 0x27C38 in a fight for the player's creature: the target pick (0x148BE with ai = 0, which aims through
    /// <see cref="AimSpell"/>) until a target is picked or the player answers yes to the abort question (0x27EF2; the
    /// spell is then lost: <see cref="IFightSpells"/> is called with no target, so its pick fails, the memorised copy
    /// goes). <paramref name="begun"/>: the spell begun earlier (<see cref="IFightSpells.ContinueCasting"/>).
    /// </summary>
    private void CastNow(int spell, bool begun)
    {
        var c = Creature;
        while (true)
        {
            var (picked, targets, square) = AimSpell(spell);
            if (!picked && !Chooser.ConfirmAbortSpell(this))
            {
                if (Reject())
                    return;
                continue;
            }
            if (begun)
                Fight.Spells.ContinueCasting(Fight, c, spell, targets, square);
            else if (targets.Count > 1)
                Fight.Spells.CastOn(Fight, c, spell, targets);
            else
                Fight.Spells.Cast(Fight, c, spell, targets.Count > 0 ? targets[0] : null, square);
            return;
        }
    }

    /// <summary>
    /// The aim part of the target pick 0x148BE for the player (0x14611 with ai = 0). Mode = spell table byte 6 AND 0x0F
    /// (<see cref="FightSpells"/>' table; another <see cref="IFightSpells"/> gives mode 0 [our choice]):
    /// 0 → no aim (the caster). 0x0F → one aim (square mode 0). 8–0x0E → one aim (square mode 1), the list is built
    /// around the aimed square. 1–7 → (byte 6 AND 3) + 1 aims (0x14AD1): an aim left with Exit uses up a pick, a
    /// creature already taken is refused with a message and aimed again. Each aim is 0x15D53 with the spell's range
    /// (0x27741), attack mode 0, and leaves the caster's +0x0A = the creature taken (none after Exit, 0x14648).
    /// Returns picked, the creatures taken and the aimed square (DS:6DB9/6DBA).
    /// </summary>
    public (bool Picked, List<EclCharacter> Targets, (int X, int Y)? Square) AimSpell(int spell)
    {
        var c = Creature;
        int mode = Fight.Spells is FightSpells fs ? fs.Table[spell].Area & 0x0F : 0;
        var targets = new List<EclCharacter>();
        if (mode == 0)
            return (true, targets, null);
        int range = Fight.Spells.Range(Fight, c, spell);
        (int X, int Y)? aimed = null;
        EclCharacter? AimOnce(bool squareMode, out bool done)
        {
            done = AimAt(range, false, squareMode, out var t, out aimed);
            Record.Other = t; // 0x14648: +0x0A = the aim's result
            return t;
        }
        if (mode == 0x0F)
        {
            if (AimOnce(false, out _) is not { } t)
                return (false, targets, null);
            targets.Add(t);
            return (true, targets, null);
        }
        if (mode >= 8)
        {
            // Square mode 1 (0x14A27): picked when the aim is done; the list is built around the aimed square
            // (DS:6DB9/6DBA = the result's square), a bare square taken by Manual included.
            var t = AimOnce(true, out bool done);
            if (!done || aimed is null)
                return (false, targets, null);
            if (t is not null)
                targets.Add(t);
            return (true, targets, aimed);
        }
        int picks = (Fight.Spells is FightSpells f2 ? f2.Table[spell].Area & 3 : 0) + 1;
        while (picks > 0)
        {
            if (AimOnce(false, out _) is not { } t)
            {
                picks--;
                continue;
            }
            if (targets.Contains(t))
            {
                Notice(FightNotice.AlreadyTargeted, t);
                if (Reject())
                    break;
                continue;
            }
            targets.Add(t);
            picks--;
        }
        return (targets.Count > 0, targets, null);
    }
}

/// <summary>
/// An <see cref="IFightController"/> for the player's side: computer-controlled creatures (record 0x10F ≠ 0) go to
/// <see cref="Ai"/> (the monster AI 0xABCF), the others get the menu (<see cref="FightMenu"/>, 0x9B21) answered by
/// <see cref="Chooser"/>.
/// </summary>
public sealed class MenuController(IFightMenuChooser chooser, IFightController? ai = null, IFightItemScreens? screens = null,
    TurnUndeadTable? turnTable = null) : IFightController
{
    public IFightMenuChooser Chooser { get; } = chooser;
    public IFightController Ai { get; } = ai ?? new EndActionsController();
    public IFightItemScreens Screens { get; } = screens ?? NoFightItemScreens.Instance;
    public TurnUndeadTable? TurnTable { get; } = turnTable;
    /// <summary>The program's Alt-X debugging switch (off in normal play).</summary>
    public bool CheatEnabled { get; init; }
    /// <summary>DS:49E2, the game speed the Done menu's Speed changes.</summary>
    public byte GameSpeed { get; set; }

    public void Act(Fight fight, EclCharacter creature, bool auto)
    {
        if (auto)
        {
            Ai.Act(fight, creature, true);
            return;
        }
        var menu = new FightMenu(fight, creature, Chooser, Ai, Screens, TurnTable)
        {
            CheatEnabled = CheatEnabled,
            GameSpeed = GameSpeed,
        };
        menu.Run();
        GameSpeed = menu.GameSpeed;
    }

    public bool ContinueBattle() => Chooser.ContinueBattle();
}
