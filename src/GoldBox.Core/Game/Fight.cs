using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Who decides a creature's actions: the player's combat menu (GAME.OVR 0x9B21) or the monster AI (0xABCF). Neither is
/// read [?]; a controller acts through <see cref="Fight"/>'s action methods (<see cref="Fight.Attack"/>,
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
/// side is empty. The arena, placement, movement on the map, spells and the AI are not read [?] and come from
/// <see cref="IFightGeometry"/> and <see cref="IFightController"/>. Random draws follow the program's order for the
/// parts modelled here.
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
    public FightState State { get; } = new();

    /// <summary>One game minute passes per round (clock routine 0x241A1, unit 1); the caller's clock.</summary>
    public Action? MinutePassed { get; set; }
    /// <summary>DS:49F4: when set, the "continue battle" question is not asked.</summary>
    public bool NoContinueQuestion { get; set; }

    public List<EclCharacter> List => Memory.Party?.Members ?? [];
    public CombatRecord Record(EclCharacter c) => State.Record(c);

    /// <summary>Rounds fought by <see cref="Run"/>.</summary>
    public int Rounds { get; private set; }

    /// <summary>Runs the fight to its end. Returns the number of rounds.</summary>
    public int Run(int maxRounds = 1000)
    {
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
    /// [?] its stats are recalculated (START 0x1758, not modelled: the derived bytes stand), trigger 0x0F, and with
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
        Effects.Trigger(FightRules.TriggerActionStart, c, State);
        if (rec.InitiativeValue > 0)
        {
            _delayed = false;
            Controller.Act(this, c, c[CombatRules.AutoCombat] != 0);
            // The program's menu loops until an action ends the turn (or Delay / Alt-Q set initiative 1 / 20). A
            // controller that returns without either would be picked again at once; end its actions instead.
            if (rec.InitiativeValue > 0 && !_delayed && rec.InitiativeValue != 20)
            {
                Diagnostics.Add($"controller left {c.Name} with initiative {rec.InitiativeValue}; actions ended");
                FightRules.EndActions(rec);
            }
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
        FightRules.CountTeams(List, State);
        done = State.TeamCount[0] == 0 || State.TeamCount[1] == 0;
        if (State.TeamCount[0] > 0 && State.TeamCount[1] == 0 && !NoContinueQuestion && Controller.ContinueBattle())
            done = false;
    }

    // ---- Actions a controller can take --------------------------------------------------------------------------

    /// <summary>START 0x31D7.</summary>
    public void EndActions(EclCharacter c) => FightRules.EndActions(Record(c));

    /// <summary>Done menu, Guard (START 0x3200 area): actions end and +7 = 1.</summary>
    public void Guard(EclCharacter c)
    {
        FightRules.EndActions(Record(c));
        Record(c)[CombatRecord.Guarding] = 1;
    }

    /// <summary>Done menu, Delay: initiative 1 (acts again after everyone else this round).</summary>
    public void Delay(EclCharacter c)
    {
        Record(c).InitiativeValue = 1;
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
        var dRec = Record(defender);
        var aRec = Record(attacker);
        if (dRec[CombatRecord.FlankCount] < 3 && !fromBehind)
            dRec[CombatRecord.Facing] = (byte)Geometry.DirectionToward(defender, attacker);
        aRec[CombatRecord.Facing] = (byte)Geometry.DirectionToward(attacker, defender);
        aRec.Other = defender;
        var outcome = new FightRules.AttackOutcome(false, defender[FightRules.InPlay] == 0, true, 0, 0);
        if (attacker[0x113] > 0 || attacker[0x114] > 0)
        {
            var saved = State.Current;
            State.Current = attacker;
            outcome = FightRules.RollAttacks(attacker, defender, fromBehind, State, Items, Memory, Random, Effects, Geometry);
            if (ammo is not null)
                FightRules.DropSpentWeapon(attacker, ammo, State, Encounter, Items);
            State.Current = saved;
        }
        if (outcome.ActionsDone)
            FightRules.EndActions(aRec);
        return outcome;
    }
}

/// <summary>
/// A test <see cref="IFightGeometry"/> on fixed squares, not the program's: Chebyshev distance and the sign of the
/// offset as one of 8 directions (0 N, clockwise; 8 for the same square). [?] 0x14E38 and START 0x30FB are not read.
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
