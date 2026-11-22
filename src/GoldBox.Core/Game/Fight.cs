using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Who decides a creature's actions: the player's combat menu (GAME.OVR 0x9B21, <see cref="MenuController"/>) or the
/// monster AI (0xABCF, <see cref="MonsterAi"/>); a controller acts through <see cref="Fight"/>'s action methods (<see cref="Fight.Attack"/>,
/// <see cref="Fight.EndActions"/>, <see cref="Fight.Guard"/>, <see cref="Fight.Delay"/>).
/// </summary>
public interface IFightController
{
    /// <summary>The creature's turn after the turn-start bookkeeping; <paramref name="auto"/> = record 0x10F ≠ 0 (AI).</summary>
    void Act(Fight fight, EclCharacter creature, bool auto);
    /// <summary>The yes/no "continue battle" question at the end of a won round (0xA087); true = go on fighting.</summary>
    bool ContinueBattle() => false;
}

/// <summary>A controller that ends every creature's actions at once (the menu's Done, Quit: no dice).</summary>
public sealed class EndActionsController : IFightController
{
    public void Act(Fight fight, EclCharacter creature, bool auto) => fight.EndActions(creature);
}

/// <summary>
/// The fight loop (GAME.OVR 0x9881, docs/re/combat-format.md "The fight"), headless: rounds of start of round
/// (0x127FB) for every creature, picks by initiative (0x9938), turns (0x99FD) and the end of round (0xA087), until a
/// side is empty. The combat map, moves, spells, effects and the AI / menu come in through <see cref="IFightGeometry"/>
/// (<see cref="CombatMap"/>), <see cref="Moves"/>, <see cref="Spells"/>, <see cref="Effects"/> and
/// <see cref="IFightController"/>; <see cref="FightKit.Create"/> wires the program's parts. Random draws follow the
/// program's order (the fight_quick capture replays draw for draw, <c>FightQuickReplayTests</c>).
/// </summary>
public sealed class Fight
{
    public Fight(EclMemory memory, EncounterState encounter, BaseItemTable items, Func<ushort, ushort> random,
        IFightController controller, IFightGeometry geometry, IFightEffects? effects = null)
    {
        Memory = memory;
        Encounter = encounter;
        Items = items;
        Random = random;
        Controller = controller;
        Geometry = geometry;
        Effects = effects ?? NoFightEffects.Instance;
    }

    public EclMemory Memory { get; }
    public EncounterState Encounter { get; }
    public BaseItemTable Items { get; }
    public Func<ushort, ushort> Random { get; }
    public IFightController Controller { get; }
    public IFightGeometry Geometry { get; }
    public IFightEffects Effects { get; }
    /// <summary>Moves on the combat map (<see cref="IFightMoves"/>); none by default.</summary>
    public IFightMoves Moves { get; set; } = NoFightMoves.Instance;
    /// <summary>Spells in the fight (<see cref="IFightSpells"/>); none by default.</summary>
    public IFightSpells Spells { get; set; } = NoFightSpells.Instance;
    /// <summary>The combat map when <see cref="Geometry"/> is one, else null.</summary>
    public IFightMap? Map => Geometry as IFightMap;
    public FightState State { get; } = new();

    /// <summary>
    /// The stat recalculation START 0x1758 (<see cref="StatRecalc.Recalculate"/>, set by <see cref="FightKit"/>). Null:
    /// only the hands byte 0x100 is rebuilt (<see cref="MonsterAi.RecalcHands"/>) and the other derived bytes stand,
    /// for tests with hand-made records.
    /// </summary>
    public Action<EclCharacter>? Recalculate { get; set; }

    /// <summary>START 0x1758 on a creature (<see cref="Recalculate"/>).</summary>
    public void RecalculateStats(EclCharacter c)
    {
        if (Recalculate is { } r)
            r(c);
        else
            MonsterAi.RecalcHands(this, c);
    }

    /// <summary>
    /// One game minute passes per round (clock routine 0x241A1, count 1, unit 1, with the effect timeout 0x23DCC):
    /// <see cref="FightKit.WireClock"/> sets it. Null: no clock (tests with hand-made fights).
    /// </summary>
    public Action? MinutePassed { get; set; }
    /// <summary>DS:49F4: when set, the "continue battle" question is not asked.</summary>
    public bool NoContinueQuestion { get; set; }

    /// <summary>
    /// Display hook (no game state): called when a creature becomes current in <see cref="Turn"/>, before it acts. A
    /// front end shows the creature here. Draws nothing and changes nothing.
    /// </summary>
    public Action<EclCharacter>? TurnStarted { get; set; }

    /// <summary>
    /// Display hook (no game state): after an acting creature's turn, 0x99FD redraws the cell at its square
    /// (0x9AD9–0x9AF1: 0x3E0:0x34 with its x and y), so a cursor box there goes.
    /// </summary>
    public Action<EclCharacter>? TurnEnded { get; set; }

    /// <summary>
    /// Display hook (no game state): called at the end of <see cref="MakeAttack"/> with the attacker, the defender, the
    /// outcome and the defender's hit points before the attack. Draws nothing and changes nothing.
    /// </summary>
    public Action<EclCharacter, EclCharacter, FightRules.AttackOutcome, int>? AttackMade { get; set; }

    /// <summary>
    /// Display hooks (no game state) of <see cref="MakeAttack"/>, in the program's order (read s39):
    /// <see cref="AttackStarting"/> at its entry, before any facing changes (the screen's part of what came before: an
    /// AI creature's ScrollView 0xBA22);
    /// <see cref="DefenderTurned"/> when the defender turned to face the attacker (+9 set, 0x14085–0x140E1: drawn by
    /// 0x31232 with frame 0 when any of its cells is in view, 0x30E54(defender, 0); 0x31232 first scrolls the view to it,
    /// ScrollView(its square, 3), when it is not wholly in view);
    /// <see cref="AttackPanel"/> where the attacker shows in the panel (START 0x1583 at 0x140FE), its facing not yet
    /// changed;
    /// <see cref="AttackPosed"/> once the attacker's facing is set: its attack pose (0x31232 with frame 1 at 0x14113,
    /// with the same scroll when it is not wholly in view) and Delay(100) (0x1412E), before the attacks are rolled.
    /// </summary>
    public Action<EclCharacter, EclCharacter>? AttackStarting { get; set; }
    /// <inheritdoc cref="AttackStarting"/>
    public Action<EclCharacter>? DefenderTurned { get; set; }
    /// <inheritdoc cref="AttackStarting"/>
    public Action<EclCharacter>? AttackPanel { get; set; }
    /// <inheritdoc cref="AttackStarting"/>
    public Action<EclCharacter>? AttackPosed { get; set; }

    /// <summary>
    /// Display hook (no game state): the end of round, after each creature's end-of-round part, when 0xA806(0) finds a
    /// party member dying (<see cref="FightMenu.Bandage"/> without bandaging): START 0x1693 with the teammate-dying
    /// words (GAME.OVR 0xA05F; row 24, the pause, row 24 cleared) at 0xA11A–0xA134.
    /// </summary>
    public Action? PartyMemberDying { get; set; }

    public List<EclCharacter> List => Memory.Party?.Members ?? [];
    public CombatRecord Record(EclCharacter c) => State.Record(c);

    /// <summary>Rounds fought by <see cref="Run"/>.</summary>
    public int Rounds { get; private set; }

    /// <summary>
    /// The end of the fight start (0xE402) when the build deferred it (<see cref="FightPlacement.Start"/>): every
    /// creature's trigger-8 effects and the enemy HP percentage. <see cref="Run"/> runs it first, once.
    /// </summary>
    public Action? PendingStart { get; set; }

    /// <summary>Runs the fight to its end. Returns the number of rounds.</summary>
    public int Run(int maxRounds = 1000)
    {
        if (PendingStart is { } start)
        {
            PendingStart = null;
            start();
        }
        foreach (var c in List)
        {
            var rec = Record(c);
            if (Encounter.NotParty.Contains(c))
                rec[CombatRecord.NotParty] = 1;
        }
        FightRules.CountTeams(List, State);
        bool done = State.TeamCount[0] == 0 || State.TeamCount[1] == 0;
        while (!done && Rounds < maxRounds)
        {
            Round(out done);
            Rounds++;
        }
        return Rounds;
    }

    /// <summary>One round: start of round, turns in initiative order, end of round.</summary>
    public void Round(out bool done)
    {
        FightRules.CountTeams(List, State);
        foreach (var c in List)
            FightRules.BeginRound(c, Record(c), State, Items, Memory, Random, Effects);
        Memory.Write(FightRules.SurpriseCell, 0);
        while (NextCreature() is { } c)
            Turn(c);
        EndOfRound(out done);
    }

    /// <summary>0x9938: <see cref="CombatRules.NextCreature"/> over the list (1d100 per creature per pick).</summary>
    public EclCharacter? NextCreature()
    {
        var list = List;
        var i = CombatRules.NextCreature(list.Select(c => Record(c).InitiativeValue).ToList(),
            () => FightRules.Dice(Random, 1, 100));
        return i is { } k ? list[k] : null;
    }

    /// <summary>
    /// 0x99FD: +0x0F, +0x12, +0x07 cleared; trigger 7; with initiative &gt; 0: 20 → 19, the creature becomes current,
    /// its stats are recalculated (START 0x1758 at 0x9A8A, <see cref="RecalculateStats"/>), trigger 0x0F, and with
    /// initiative still &gt; 0 the AI or the menu acts.
    /// </summary>
    public void Turn(EclCharacter c)
    {
        var rec = Record(c);
        rec[CombatRecord.FlankCount] = 0;
        rec[CombatRecord.FlankSpread] = 0;
        rec[CombatRecord.Guarding] = 0;
        Effects.Trigger(FightRules.TriggerTurnStart, c, State);
        if (rec.InitiativeValue <= 0)
            return;
        if (rec.InitiativeValue == 20)
            rec.InitiativeValue = 19;
        State.Current = c;
        RecalculateStats(c);
        Effects.Trigger(FightRules.TriggerActionStart, c, State);
        if (rec.InitiativeValue > 0)
        {
            _delayed = false;
            TurnStarted?.Invoke(c);
            Controller.Act(this, c, c[CombatRules.AutoCombat] != 0);
            // The program's menu loops until an action ends the turn (or Delay / Alt-Q set initiative 1 / 20). A
            // controller that returns without either would be picked again at once; end its actions instead.
            if (rec.InitiativeValue > 0 && !_delayed && rec.InitiativeValue != 20)
            {
                Diagnostics.Add($"controller left {c.Name} with initiative {rec.InitiativeValue}; actions ended");
                FightRules.EndActions(rec);
            }
            TurnEnded?.Invoke(c);
        }
    }

    /// <summary>
    /// 0xA087: one minute, DS:6DE3 + 1, the enemy HP percentage (0x15074), per creature the end-of-round effects and the
    /// dying count, the teams counted again; done when a side is empty, unless the party side won and the player
    /// chooses to go on (asked unless DS:49F4).
    /// </summary>
    public void EndOfRound(out bool done)
    {
        MinutePassed?.Invoke();
        State.RoundCounter++;
        FightRules.UpdateHostileHpPercent(List, State);
        foreach (var c in List)
            FightRules.EndOfRoundCreature(c, State, Effects);
        if (PartyMemberDying is { } dying && FightMenu.Bandage(this, false) is not null)
            dying();
        FightRules.CountTeams(List, State);
        done = State.TeamCount[0] == 0 || State.TeamCount[1] == 0;
        if (State.TeamCount[0] > 0 && State.TeamCount[1] == 0 && !NoContinueQuestion && Controller.ContinueBattle())
            done = false;
    }

    // ---- Actions a controller can take --------------------------------------------------------------------------

    /// <summary>START 0x31D7.</summary>
    public void EndActions(EclCharacter c) => FightRules.EndActions(Record(c));

    /// <summary>
    /// Guard (START 0x3226; the Done menu's Guard and the AI's finish 0xBB0F): actions end and +7 = 1, then its
    /// message (the word at START CS:0x321D through the nested 0x2233; DOSBox-X s30: on row 24 at once, cleared after
    /// the game-speed pause), shown through <see cref="Guarded"/>.
    /// </summary>
    public void Guard(EclCharacter c)
    {
        FightRules.EndActions(Record(c));
        Record(c)[CombatRecord.Guarding] = 1;
        Guarded?.Invoke(c);
    }

    /// <summary>
    /// Display hook (no game state): the status panel START 0x1583 drawn for a creature outside the turn start, e.g.
    /// the mover's again after each attack of opportunity (0x13429; DOSBox-X s30: a fleeing spectre's panel back after
    /// Bakshi's free attack).
    /// </summary>
    public Action<EclCharacter>? PanelShown { get; set; }

    /// <summary>Display hook (no game state): START 0x3226's message after a creature guards.</summary>
    public Action<EclCharacter>? Guarded { get; set; }

    /// <summary>
    /// Display hook (no game state): the first half of the step's drawing inside 0x12F0D, before the slot moves
    /// (0x12FB4–0x13011): an AI mover's ScrollView(old square, 2) when the new square is off the view, then the old cell
    /// redrawn (0x30C65, a cursor box there goes). Arguments: the mover (still on its old square) and the new square.
    /// </summary>
    public Action<EclCharacter, (int X, int Y)>? StepDrawing { get; set; }

    /// <summary>
    /// Display hook (no game state): the second half, after the slot write and the grid rebuild (0x1303F–0x1304E):
    /// ScrollView(new square, 1, or 3 for a computer-controlled mover); before the step sound and the guards' attacks.
    /// Arguments: the mover and its old square.
    /// </summary>
    public Action<EclCharacter, (int X, int Y)>? StepDrawn { get; set; }

    /// <summary>
    /// Display hook (no game state): 0x12E22's ScrollView(mover's square, 2, 8) before each guard's attack after a step
    /// (0x12E9E–0x12EBC). Argument: the mover.
    /// </summary>
    public Action<EclCharacter>? GuardAttacking { get; set; }

    /// <summary>Done menu, Delay: initiative 1 (acts again after everyone else this round).</summary>
    public void Delay(EclCharacter c)
    {
        Record(c).InitiativeValue = 1;
        _delayed = true;
    }

    /// <summary>
    /// The creature's turn ends with its actions not over: initiative = <paramref name="initiative"/> and it is picked
    /// again later this round (a spell begun with a casting time, 0x14D33–0x14D64, or Delay).
    /// </summary>
    public void Postpone(EclCharacter c, sbyte initiative)
    {
        Record(c).InitiativeValue = initiative;
        _delayed = true;
    }

    private bool _delayed;

    /// <summary>Problems met (controllers that did not end a turn, …).</summary>
    public List<string> Diagnostics { get; } = [];

    /// <summary>
    /// 0x13FF1: the defender notes an attack: +0x0F + 1; angle = (direction from the defender to the attacker − its
    /// facing + 8) mod 8, folded to 0–4; +0x12 = (+0x12 + angle) mod 8.
    /// </summary>
    public void NoteAttackDirection(EclCharacter attacker, EclCharacter defender)
    {
        var d = Record(defender);
        d[CombatRecord.FlankCount]++;
        int angle = (Geometry.DirectionToward(defender, attacker) - d[CombatRecord.Facing] + 8) % 8;
        if ((byte)angle > 4)
            angle = 8 - angle;
        d[CombatRecord.FlankSpread] = (byte)((d[CombatRecord.FlankSpread] + angle) % 8);
    }

    /// <summary>
    /// A melee attack as the menu's move-into and the AI make it: <see cref="NoteAttackDirection"/>, then MakeAttack
    /// (0x1407F): a defender attacked fewer than 3 times this turn (and not from behind) turns to face the attacker; the
    /// attacker faces the defender ([?] facing via 0x31232, taken as +9) and records it (+0x0A); with attacks left in
    /// either phase the attack routine runs (<see cref="FightRules.RollAttacks"/>), and with
    /// <paramref name="ammo"/> the thrown-weapon rule (<see cref="FightRules.DropSpentWeapon"/>). Actions done →
    /// <see cref="EndActions"/>.
    /// </summary>
    public FightRules.AttackOutcome Attack(EclCharacter attacker, EclCharacter defender, bool fromBehind = false, byte[]? ammo = null)
    {
        NoteAttackDirection(attacker, defender);
        return MakeAttack(attacker, defender, fromBehind, ammo);
    }

    /// <summary>
    /// 0x14EAB: resident 0x15011, the cue as it leaves (0x15016), the flight from the attacker's square to the
    /// defender's (squares 0x31321 / 0x31349; 0x1501F–0x15057), its sprite by the item's type (+0x2E) and the direction
    /// 0x14E38, the cue as it arrives (0x1505C), 0x15069. The flight is a display hook; the cues always sound.
    /// </summary>
    private void MissileFlight(EclCharacter attacker, EclCharacter defender, byte[] item)
    {
        State.Sound.BeginResident();
        State.Sound.Cue(Sound.SoundCue.MissileOut);
        if (State.Flight is { } fly && Geometry is CombatMap fm && fm.OnMap(attacker) && fm.OnMap(defender))
        {
            var (ax, ay) = fm.Position(attacker);
            var (dx, dy) = fm.Position(defender);
            fly(FlightAnimation.Missile(ax, ay, dx, dy, item.Length > 0x2E ? item[0x2E] : (byte)0,
                Geometry.DirectionToward(attacker, defender)));
        }
        State.Sound.Cue(Sound.SoundCue.MissileIn);
        State.Sound.EndResident();
    }

    /// <summary>
    /// MakeAttack (0x1407F) alone, without <see cref="NoteAttackDirection"/>: the free attacks of a move (0x130AD) call
    /// it this way. A defender that drops leaves the combat map (0x3154B via 0x12AF4; <see cref="CombatMap.Downed"/>).
    /// </summary>
    public FightRules.AttackOutcome MakeAttack(EclCharacter attacker, EclCharacter defender, bool fromBehind = false, byte[]? ammo = null)
    {
        var dRec = Record(defender);
        var aRec = Record(attacker);
        AttackStarting?.Invoke(attacker, defender);
        if (dRec[CombatRecord.FlankCount] < 3 && !fromBehind)
        {
            dRec[CombatRecord.Facing] = (byte)Geometry.DirectionToward(defender, attacker); // 0x14085–0x140E1
            DefenderTurned?.Invoke(defender);
        }
        AttackPanel?.Invoke(attacker); // 0x140FE
        aRec[CombatRecord.Facing] = (byte)Geometry.DirectionToward(attacker, defender); // 0x14113 (0x31232)
        AttackPosed?.Invoke(attacker);
        aRec.Other = defender; // 0x14118
        var outcome = new FightRules.AttackOutcome(false, defender[FightRules.InPlay] == 0, true, 0, 0);
        int hpBefore = defender[FightRules.Hp];
        // The missile's flight (0x14152 → 0x14EAB, before the attacks-left test).
        if (ammo is not null)
            MissileFlight(attacker, defender, ammo);
        // 0x14155–0x1417E: a second 0x14EAB, whatever the ammunition (melee too), when the attacker's weapon (+0xCC)
        // has type 0x2F, with the weapon as the missile. With no weapon the program reads type byte 0000:002E, the low
        // byte of INT 0Bh's segment: 0x00 for the BIOS vector (DOSBox-X, a stock machine; MS-DOS STACKS leaves it) →
        // no flight, taken here. [?] An IRQ 3 driver's segment ending in 0x2F would give a flight (combat-menu.md).
        if (FightRules.Equipment(attacker, Items)[FightRules.SlotWeapon] is { Length: > 0x2E } weapon && weapon[0x2E] == 0x2F)
            MissileFlight(attacker, defender, weapon);
        if (attacker[0x113] > 0 || attacker[0x114] > 0)
        {
            var saved = State.Current;
            State.Current = attacker;
            outcome = FightRules.RollAttacks(attacker, defender, fromBehind, State, Items, Memory, Random, Effects, Geometry);
            if (defender[FightRules.InPlay] == 0 && Geometry is CombatMap map && map.OnMap(defender))
                map.Downed(defender, State, sound: !State.FrontEndPlaysDowned);
            if (ammo is not null)
                FightRules.DropSpentWeapon(attacker, ammo, State, Encounter, Items);
            RecalculateStats(attacker); // START 0x1758 at 0x14299: also undoes the large-creature dice of 0x13C01
            State.Current = saved;
        }
        if (outcome.ActionsDone)
            FightRules.EndActions(aRec);
        AttackMade?.Invoke(attacker, defender, outcome, hpBefore);
        return outcome;
    }
}

/// <summary>
/// A test <see cref="IFightGeometry"/> on fixed squares, not the program's: Chebyshev distance and the sign of the
/// offset as one of 8 directions (0 N, clockwise; 8 for the same square). The program's rules (0x14E38, START 0x30FB)
/// are <see cref="CombatMap"/>'s; this one is for tests that do not need them.
/// </summary>
public sealed class GridGeometry : IFightGeometry
{
    private readonly Dictionary<EclCharacter, (int X, int Y)> _at = new(ReferenceEqualityComparer.Instance);
    public void Place(EclCharacter c, int x, int y) => _at[c] = (x, y);
    public (int X, int Y) At(EclCharacter c) => _at.TryGetValue(c, out var p) ? p : (0, 0);

    public int Distance(EclCharacter a, EclCharacter b)
    {
        var (ax, ay) = At(a);
        var (bx, by) = At(b);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
    }

    public int DirectionToward(EclCharacter from, EclCharacter to)
    {
        var (fx, fy) = At(from);
        var (tx, ty) = At(to);
        int dx = Math.Sign(tx - fx), dy = Math.Sign(ty - fy);
        return (dx, dy) switch
        {
            (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7,
            _ => 8,
        };
    }
}
