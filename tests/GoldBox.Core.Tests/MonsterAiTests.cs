using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>
/// A combat map for tests: squares in a dictionary, Chebyshev distance, the sign of the offset as the direction,
/// steps that move the creature and spend 2 (3 diagonally) of combat record +6. Not the program's rules
/// (0x14E38, START 0x30FB, 0x12F0D are read by the combat-map work).
/// </summary>
public sealed class TestFightMap : IFightMap, IFightMoves
{
    public readonly Dictionary<EclCharacter, (int X, int Y)> At_ = new(ReferenceEqualityComparer.Instance);
    public bool LineOk = true;
    /// <summary>The area search in list (slot) order, as 0x3045B walks the map slots DS:5F27 (creatures with a
    /// footprint, i.e. on the map), by Chebyshev distance.</summary>
    public IReadOnlyList<EclCharacter> Search(Fight fight, EclCharacter from, int range) =>
        fight.List.Where(c => OnMap(c) && Distance(from, c) <= range).ToList();
    public HashSet<(int, int)> Edge = [];
    public List<(EclCharacter, int)> Steps = [];

    public void Place(EclCharacter c, int x, int y) => At_[c] = (x, y);
    public (int X, int Y) Position(EclCharacter c) => At_[c];
    public EclCharacter? At(int x, int y) => At_.FirstOrDefault(kv => kv.Value == (x, y)).Key;
    public bool OnMap(EclCharacter c) => At_.ContainsKey(c);
    public int DirectionToward(int fx, int fy, int tx, int ty) => (Math.Sign(tx - fx), Math.Sign(ty - fy)) switch
    {
        (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => 8,
    };
    public int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
    public int DirectionToward(EclCharacter from, EclCharacter to) =>
        DirectionToward(At_[from].X, At_[from].Y, At_[to].X, At_[to].Y);
    public int Distance(EclCharacter a, EclCharacter b) => Distance(At_[a].X, At_[a].Y, At_[b].X, At_[b].Y);
    public bool Line(Fight fight, int fx, int fy, int tx, int ty, int range, out int length)
    {
        length = 2 * Distance(fx, fy, tx, ty);
        return LineOk && length <= 2 * range + 1;
    }

    private static readonly int[] Dx = [0, 1, 1, 1, 0, -1, -1, -1], Dy = [-1, -1, 0, 1, 1, 1, 0, -1];
    public (byte Ground, EclCharacter? Occupant) Look(EclCharacter c, int direction)
    {
        var (x, y) = At_[c];
        int nx = x + Dx[direction], ny = y + Dy[direction];
        if (Edge.Contains((nx, ny)))
            return (0, null);
        return (1, At(nx, ny));
    }

    public StepResult Step(Fight fight, EclCharacter c, int direction)
    {
        Steps.Add((c, direction));
        var (x, y) = At_[c];
        At_[c] = (x + Dx[direction], y + Dy[direction]);
        var rec = fight.Record(c);
        rec[CombatRecord.Movement] = (byte)(rec[CombatRecord.Movement] - ((direction & 1) != 0 ? 3 : 2));
        return StepResult.Moved;
    }

    public bool Fled;
    /// <summary>The flee fails (the creature stays); <see cref="FleeTries"/> counts the calls.</summary>
    public bool FleeFails;
    public int FleeTries;
    public bool Flee(Fight fight, EclCharacter c)
    {
        FleeTries++;
        if (FleeFails)
            return false;
        Fled = true;
        At_.Remove(c);
        c[FightRules.InPlay] = 0;
        return true;
    }
}

public class MonsterAiTests
{
    /// <summary>A step table for tests (not the program's): row r offsets 8, 1, 7, 2, 6.</summary>
    private static readonly byte[] Steps = [.. Enumerable.Range(0, 40).Select(i => (byte)((i % 5) switch { 1 => 8, 2 => 1, 3 => 7, 4 => 2, 0 => 6, _ => 0 }))];

    /// <summary>A random source that returns scripted Random(n) values (0-based) and logs each n.</summary>
    private sealed class Script(params int[] values)
    {
        private int _i;
        public List<int> Sides { get; } = [];
        public ushort Next(ushort n)
        {
            Sides.Add(n);
            int v = _i < values.Length ? values[_i] : 0;
            _i++;
            return (ushort)(v % n);
        }
    }

    private static EclCharacter Creature(byte hostile, byte hp = 8)
    {
        var c = new EclCharacter();
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = hostile;
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[CombatRules.AutoCombat] = 1;
        c[FightRules.Thac0] = 40;
        c[FightRules.Ac] = 50;
        c[0x113] = 1;
        c[FightRules.BaseAttacksPrimary] = 2;
        c[0x115] = 1;
        c[0x117] = 4;
        c[FightRules.Movement] = 6;
        return c;
    }

    private static (Fight, TestFightMap) Setup(Script rng, params EclCharacter[] list)
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange(list);
        var map = new TestFightMap();
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(), map)
            { Moves = map };
        FightRules.CountTeams(list, fight.State);
        foreach (var c in list)
        {
            fight.Record(c).InitiativeValue = 5;
            fight.Record(c)[CombatRecord.Movement] = 12;
            fight.Record(c)[CombatRecord.AttackPhase] = 2;
            fight.Record(c)[CombatRecord.CastAllowed] = 1;
            fight.Record(c)[CombatRecord.ItemUseAllowed] = 1;
        }
        return (fight, map);
    }

    [Theory]
    [InlineData(0, new[] { 2, 3 }, new[] { 8, 4 }, 4)]        // row 0: 1d8 = 3, 1d4 = 4
    [InlineData(0, new[] { 7, 1 }, new[] { 8, 2 }, 6)]        // 1d8 = 8 → 1d2 + 4
    [InlineData(3, new[] { 2 }, new[] { 4 }, 3)]              // row 3 kept: 1d4 ≠ 1
    [InlineData(3, new[] { 0, 0, 0 }, new[] { 4, 8, 4 }, 1)]  // 1d4 = 1 → 1d8, 1d4
    [InlineData(6, new[] { 1, 0 }, new[] { 8, 4 }, 1)]        // row 5–6: no 1d4 first
    public void Opening_dice(int row, int[] values, int[] sides, int expected)
    {
        var rng = new Script(values);
        var c = Creature(1);
        var (fight, _) = Setup(rng, c);
        fight.Record(c)[MonsterAi.StepRow] = (byte)row;
        MonsterAi.OpeningDice(fight, c);
        Assert.Equal(sides, rng.Sides);
        Assert.Equal(expected, fight.Record(c)[MonsterAi.StepRow]);
    }

    [Fact]
    public void Make_attacks_display_hooks_come_in_the_programs_order_with_the_facings_of_each_moment()
    {
        // MakeAttack 0x1407F (read s39): entry; the defender turns (+9, 0x14085–0x140E1); the panel (0x140FE) with the
        // attacker's old facing; the attacker faces the defender and poses (0x31232 at 0x14113); +0x0A (0x14118).
        var a = Creature(0);
        var d = Creature(1);
        var (fight, map) = Setup(new Script(19, 2), a, d);
        map.Place(a, 5, 5);
        map.Place(d, 4, 5);
        fight.Record(a)[CombatRecord.Facing] = 2;
        fight.Record(d)[CombatRecord.Facing] = 6;
        var log = new List<string>();
        string F(EclCharacter c) => fight.Record(c)[CombatRecord.Facing].ToString();
        fight.AttackStarting = (x, y) => log.Add($"start a{F(a)} d{F(d)}");
        fight.DefenderTurned = x => log.Add($"turn d{F(d)}");
        fight.AttackPanel = x => log.Add($"panel a{F(a)} other {(fight.Record(a).Other is null ? "none" : "set")}");
        fight.AttackPosed = x => log.Add($"pose a{F(a)}");
        fight.MakeAttack(a, d);
        Assert.Equal(["start a2 d6", "turn d2", "panel a2 other none", "pose a6"], log);
        Assert.Same(d, fight.Record(a).Other);

        // From behind (or a defender already noted three times) it does not turn: no hook.
        log.Clear();
        fight.Record(d)[CombatRecord.Facing] = 6;
        fight.MakeAttack(a, d, fromBehind: true);
        Assert.DoesNotContain(log, l => l.StartsWith("turn"));
    }

    [Fact]
    public void Readying_the_best_weapon_redraws_the_panel_when_anything_changed()
    {
        // 0xBF8A's end (0xC4CB–0xC4D7): START 0x1583 when [bp−0x1D] is set; with no weapon readied the readying branch
        // always sets it (DOSBox-X s39: the panel cleared and the weapon typed again at the AI's start after Quick).
        var gob = Creature(1);
        var (fight, _) = Setup(new Script(), gob);
        var redrawn = new List<EclCharacter>();
        new MonsterAi(Steps) { PanelRedrawn = redrawn.Add }.EquipWeapon(fight, gob);
        Assert.Equal([gob], redrawn);
    }

    [Fact]
    public void Readying_skips_the_unready_when_two_cursed_items_leave_no_weapon_chosen()
    {
        // 0xBF8A (0xC41A–0xC452): with 0x100 above 2 and the off-hand item cursed, the program unreadies the chosen
        // weapon through 0x2250A even when none was chosen (a nil pointer, 0000:0000). Original bug, fixed (user, s53):
        // the call is skipped; both cursed items stay readied.
        var table = new byte[BaseItemTable.EntrySize * BaseItemTable.Count];
        var items = new BaseItemTable(table);
        items.Set(1, BaseItemTable.Slot, 0); // two-handed weapon
        items.Set(1, 1, 2);
        items.Set(1, 13, 0xFF);
        items.Set(2, BaseItemTable.Slot, 1); // shield
        items.Set(2, 1, 1);
        items.Set(2, 13, 0xFF);
        var gob = Creature(1);
        gob[MonsterAi.ClassMask] = 0xFF;
        byte[] Item(byte type)
        {
            var it = new byte[EclCharacter.ItemRecordSize];
            it[0x2E] = type;
            it[CombatRules.ItemEquipped] = 1;
            it[0x36] = 1; // cursed: worth 0, never the chosen one
            gob.Items.Add(it);
            return it;
        }
        var sword = Item(1);
        var shield = Item(2);
        gob[MonsterAi.HandsUsed] = 3;
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(gob);
        var fight = new Fight(memory, new EncounterState(), items, new Script().Next, new EndActionsController(),
            new TestFightMap());
        new MonsterAi(Steps).EquipWeapon(fight, gob);
        Assert.Equal(1, sword[CombatRules.ItemEquipped]);
        Assert.Equal(1, shield[CombatRules.ItemEquipped]);
    }

    [Fact]
    public void Adjacent_enemy_is_attacked_with_the_program_draw_order()
    {
        // 1d8, 1d4 (opening), 1d7 (items), 1d7 (spells), 1d2 (target search among 2), 1d1 (adjacent pick), 1d20, 1d4.
        var rng = new Script(2, 3, 0, 0, 0, 0, 19, 2);
        var gob = Creature(1);
        var a = Creature(0);
        var b = Creature(0);
        var (fight, map) = Setup(rng, a, b, gob);
        map.Place(gob, 5, 5);
        map.Place(a, 5, 6);
        map.Place(b, 9, 9);
        new MonsterAi(Steps).Turn(fight, gob);
        Assert.Equal(new[] { 8, 4, 7, 7, 2, 1, 20, 4 }, rng.Sides);
        Assert.Equal(5, a[FightRules.Hp]); // 3 damage
        Assert.Equal(0, fight.Record(gob).InitiativeValue);
        Assert.Same(a, fight.Record(gob).Other);
        // The remembered target is dropped by 0xB85C (hostile 0), so the adjacent list is drawn from.
    }

    [Fact]
    public void Creature_steps_towards_its_target_with_the_row_offsets()
    {
        var rng = new Script(2, 3, 0, 0, 0);
        var gob = Creature(1);
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, gob);
        map.Place(gob, 5, 5);
        map.Place(a, 5, 9);
        map.Place(Creature(1), 5, 6); // an ally blocks straight ahead (offset 8 ≡ 0)
        int posedAtStep = -1;
        fight.State.Posed = c => { if (posedAtStep < 0) posedAtStep = map.Steps.Count; }; // 0x31232 before the step's attacks of opportunity
        new MonsterAi(Steps).Turn(fight, gob);
        // Row 4: attempt 1 = 8 (south, blocked: occupied), attempt 2 = +1 → 5 (SW).
        Assert.Equal((gob, 5), map.Steps[0]);
        Assert.Equal(0, posedAtStep);
    }

    [Fact]
    public void Magic_user_does_not_advance_and_ends_its_turn()
    {
        var rng = new Script(2, 3, 0, 0, 0);
        var mu = Creature(1);
        mu[MonsterAi.Class] = 5;
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, mu);
        map.Place(mu, 5, 5);
        map.Place(a, 5, 9);
        new MonsterAi(Steps).Turn(fight, mu);
        Assert.Empty(map.Steps);
        Assert.Equal(0, fight.Record(mu).InitiativeValue);
        Assert.Equal(1, fight.Record(mu)[CombatRecord.Guarding]); // no ranged weapon → guard
    }

    [Fact]
    public void Npc_with_low_hp_and_no_faster_enemies_flees()
    {
        // Morale: 0x84 = 0x80 + 10 → m = 20; HP 1/8 → lost 88 ≥ m → not steady; enemy side at 50 % < 100 − 30.
        var rng = new Script(2, 3, 0, 0, 0, 0);
        var gob = Creature(1);
        gob[CombatRules.Npc] = 0x8A;
        gob[FightRules.Hp] = 1;
        gob[FightRules.Movement] = 12;
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, gob);
        fight.Memory.Write(MonsterAi.MoraleThresholdCell, 30);
        fight.State.HostileHpPercent = 50;
        map.Place(gob, 5, 5);
        map.Place(a, 5, 6);
        bool done = new MonsterAi(Steps).CheckMorale(fight, gob);
        Assert.False(done);
        Assert.Equal(1, fight.Record(gob)[MonsterAi.Fleeing]);
    }

    [Fact]
    public void Npc_outrun_by_its_enemies_surrenders_when_intelligent()
    {
        var gob = Creature(1);
        gob[CombatRules.Npc] = 0x8A;
        gob[FightRules.Hp] = 1;
        gob[FightRules.Movement] = 3;
        gob[MonsterAi.Int] = 9;
        var a = Creature(0);
        a[FightRules.Movement] = 12;
        var (fight, map) = Setup(new Script(), a, gob);
        map.Place(gob, 5, 5);
        map.Place(a, 5, 6);
        var said = new List<AiMessage>();
        var ai = new MonsterAi(Steps) { Message = (_, m) => said.Add(m) };
        Assert.True(ai.CheckMorale(fight, gob));
        Assert.Equal(0, gob[FightRules.InPlay]);
        Assert.Equal((byte)CreatureStatus.Unconscious, gob[FightRules.Status]);
        Assert.Contains(AiMessage.Surrenders, said);
    }

    [Fact]
    public void A_surrender_message_comes_while_the_creature_is_still_on_the_map_and_none_when_out_of_play()
    {
        // 0x2BDF0: out of play (0x2BE0C) → nothing at all; else the view and the message (0x2BE31, 0x2BE47) before
        // in play 0, the status and the removal.
        foreach (bool inPlay in new[] { true, false })
        {
            var gob = Creature(1);
            gob[CombatRules.Npc] = 0x8A;
            gob[FightRules.Hp] = 1;
            gob[FightRules.Movement] = 3;
            gob[MonsterAi.Int] = 9;
            var a = Creature(0);
            a[FightRules.Movement] = 12;
            var (fight, map) = Setup(new Script(), a, gob);
            map.Place(gob, 5, 5);
            map.Place(a, 5, 6);
            gob[FightRules.InPlay] = (byte)(inPlay ? 1 : 0);
            var said = new List<(AiMessage M, byte InPlay, bool OnMap)>();
            var ai = new MonsterAi(Steps) { Message = (c, m) => said.Add((m, c[FightRules.InPlay], map.OnMap(c))) };
            Assert.True(ai.CheckMorale(fight, gob));
            if (inPlay)
            {
                Assert.Equal((AiMessage.Surrenders, (byte)1, true), Assert.Single(said));
                Assert.Equal(0, gob[FightRules.InPlay]);
            }
            else
            {
                Assert.Empty(said);
                Assert.NotEqual((byte)CreatureStatus.Unconscious, gob[FightRules.Status]);
            }
            Assert.Equal(0, fight.Record(gob)[CombatRecord.Movement]); // actions ended either way (0xBE36)
        }
    }

    [Fact]
    public void A_charmed_monster_is_steady_through_the_charm_handlers_morale()
    {
        // 0xBC97 with 0x84 = 0xFF (m = 0) runs trigger 0x11; the charm handler 11 (0xF05B) writes 100 into DS:6825,
        // which the check then reads: steady, no flight (DOSBox-X s30: a charmed orc at 4 / 5 HP fought on; the Client
        // used to show the panic message because the handler wrote a cell the check did not read).
        var orc = Creature(0, hp: 5);
        orc[CombatRules.Npc] = 0xFF;
        orc[FightRules.Hp] = 4;
        orc[FightRules.Movement] = 9;
        var enemy = Creature(1);
        enemy[FightRules.Movement] = 9;
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange([orc, enemy]);
        var map = new TestFightMap();
        var list = new[] { orc, enemy };
        var fx = new EffectHandlers(new Script().Next) { List = () => list };
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), new Script().Next,
            new EndActionsController(), map, fx) { Moves = map };
        FightRules.CountTeams(list, fight.State);
        map.Place(orc, 5, 5);
        map.Place(enemy, 5, 6);
        EffectHandlers.AddEffect(orc, 0x0B, 0, 0x20, false); // charm already applied (param bit 5)
        Assert.False(new MonsterAi(Steps).CheckMorale(fight, orc));
        Assert.Equal(0, fight.Record(orc)[MonsterAi.Fleeing]);
        Assert.Equal(100, fight.State.Morale);
    }

    [Fact]
    public void The_ais_finish_guards_with_the_guard_message()
    {
        // 0xBB0F → START 0x3226: actions end, +7 = 1 and its message (DOSBox-X s30: a fleeing spectre's turn ended
        // with the guard word on row 24).
        var gob = Creature(1);
        var (fight, _) = Setup(new Script(), Creature(0), gob);
        var guarded = new List<EclCharacter>();
        fight.Guarded = guarded.Add;
        Assert.True(MonsterAi.FinishRound(fight, gob));
        Assert.Equal(1, fight.Record(gob)[CombatRecord.Guarding]);
        Assert.Same(gob, Assert.Single(guarded));
    }

    [Fact]
    public void Steady_monster_side_keeps_fighting()
    {
        var gob = Creature(1);
        gob[CombatRules.Npc] = 0xFF; // 2 × 0x7F > 102 → m = 0
        var (fight, _) = Setup(new Script(), Creature(0), gob);
        fight.State.HostileHpPercent = 100;
        Assert.False(new MonsterAi(Steps).CheckMorale(fight, gob));
        Assert.Equal(0, fight.Record(gob)[MonsterAi.Fleeing]);
    }

    [Fact]
    public void Fleeing_creature_at_the_edge_flees()
    {
        var rng = new Script(0); // 1d2 for the row
        var gob = Creature(1);
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, gob);
        fight.Memory.Position.Facing = 2; // monsters flee towards 2 − ((2 + 2) mod 4) / 2 = 2 (east)
        map.Place(gob, 5, 5);
        map.Place(a, 0, 0);
        fight.Record(gob)[MonsterAi.Fleeing] = 1;
        foreach (var d in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            map.Edge.Add((5 + new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[d], 5 + new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[d]));
        new MonsterAi(Steps).MoveStep(fight, gob);
        Assert.True(map.Fled);
        Assert.Equal(0, fight.Record(gob)[MonsterAi.Fleeing]);
        Assert.Equal(0, fight.Record(gob).InitiativeValue);
    }

    /// <summary>s52: 0x13465 returns START 0x31D7's 1 whether the creature got away or not, so the AI loop 0xB4EB
    /// ends after one flee: movement and the fleeing flag cleared, actions over.</summary>
    [Fact]
    public void A_failed_flee_at_the_edge_ends_the_move_after_one_try()
    {
        var rng = new Script(0); // 1d2 for the row
        var gob = Creature(1);
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, gob);
        fight.Memory.Position.Facing = 2;
        map.Place(gob, 5, 5);
        map.Place(a, 0, 0);
        map.FleeFails = true;
        fight.Record(gob)[MonsterAi.Fleeing] = 1;
        foreach (var d in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            map.Edge.Add((5 + new[] { 0, 1, 1, 1, 0, -1, -1, -1 }[d], 5 + new[] { -1, -1, 0, 1, 1, 1, 0, -1 }[d]));
        new MonsterAi(Steps).MoveStep(fight, gob);
        Assert.Equal(1, map.FleeTries);
        Assert.False(map.Fled);
        Assert.Equal(0, fight.Record(gob)[MonsterAi.Fleeing]);
        Assert.Equal(0, fight.Record(gob)[CombatRecord.Movement]);
        Assert.Equal(0, fight.Record(gob).InitiativeValue);
    }

    [Fact]
    public void Target_search_strikes_out_refused_targets()
    {
        var rng = new Script(0, 1);
        var gob = Creature(1);
        var a = Creature(0);
        var b = Creature(0);
        var (fight, map) = Setup(rng, gob, a, b);
        map.Place(gob, 5, 5);
        map.Place(a, 5, 7);
        map.Place(b, 6, 7);
        var effects = new RefuseEffects(a);
        var f2 = new Fight(fight.Memory, fight.Encounter, fight.Items, rng.Next, fight.Controller, map, effects) { Moves = map };
        FightRules.CountTeams(f2.List, f2.State);
        Assert.True(MonsterAi.AcquireTarget(f2, gob, 0xFF, true, false));
        Assert.Same(b, f2.Record(gob).Other);
        Assert.Equal(new[] { 2, 2 }, rng.Sides); // a refused → struck out, the second draw takes the other entry
    }

    private sealed class RefuseEffects(EclCharacter refused) : IFightEffects
    {
        public void Trigger(int trigger, EclCharacter c, FightState fight)
        {
            if (trigger == MonsterAi.TriggerTargetDefender && ReferenceEquals(c, refused))
                fight.TargetBlocked = 1;
        }
        public void Removed(EclCharacter c, byte[] effect) { }
    }

    [Fact]
    public void Ai_picks_a_spell_by_priority_and_casts_it()
    {
        // Spells 5 and 9 memorised; 5 has priority 2, 9 priority 7 with no target check.
        var rng = new Script(0, 1);
        var mage = Creature(1);
        mage[CombatRules.Npc] = 0x80;
        mage[0x17] = 5;
        mage[0x18] = 9;
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, mage);
        map.Place(mage, 1, 1);
        map.Place(a, 8, 8);
        var spells = new TestSpells();
        fight.Spells = spells;
        Assert.True(new MonsterAi(Steps).CastSpell(fight, mage));
        Assert.Equal(9, spells.Cast);
        Assert.Equal(new[] { 7, 2 }, rng.Sides);
    }

    /// <summary>
    /// The original bug fixed (user, s50; spells-effects.md "Combat cast"): 0xB105 takes every byte above 0, so a spell
    /// still to be memorised (id + 0x80) could be drawn and read past the spell table. The engine's list skips it: the
    /// draw is 1d2 over spells 5 and 9 only, never the pending 0x89.
    /// </summary>
    [Fact]
    public void Ai_list_skips_spells_still_to_be_memorised()
    {
        var rng = new Script(0, 1);
        var mage = Creature(1);
        mage[CombatRules.Npc] = 0x80;
        mage[0x17] = 0x89;
        mage[0x18] = 5;
        mage[0x19] = 9;
        var a = Creature(0);
        var (fight, map) = Setup(rng, a, mage);
        map.Place(mage, 1, 1);
        map.Place(a, 8, 8);
        var spells = new TestSpells();
        fight.Spells = spells;
        Assert.True(new MonsterAi(Steps).CastSpell(fight, mage));
        Assert.Equal(9, spells.Cast);
        Assert.Equal(new[] { 7, 2 }, rng.Sides);
    }

    private sealed class TestSpells : IFightSpells
    {
        public int Cast = -1;
        public IReadOnlyList<int> Castable(Fight fight, EclCharacter caster) => [];
        bool IFightSpells.Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square) => false;
        public (byte Priority, byte Targeted, byte Area) AiInfo(int spell) => spell == 9 ? ((byte)7, (byte)0, (byte)0) : ((byte)2, (byte)1, (byte)0);
        public bool CastAuto(Fight fight, EclCharacter caster, int spell)
        {
            Cast = spell;
            return true;
        }
    }
}
