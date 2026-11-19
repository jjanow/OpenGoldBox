using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>
/// The combat map and moves on made-up tables (no game files): lines, distance, direction, steps, free attacks,
/// guards and the flee draw (docs/re/combat-map.md). The program-checked cases are in CombatMapProgramTests and
/// FightStartCaptureTests.
/// </summary>
public class CombatMapTests
{
    /// <summary>Terrain 23: open floor (cost 1, tile 22). Terrain 2: wall (cost 255, blocking 2). Terrain 40: cost 4
    /// (rough). Icon sizes 1 (one cell) and 4 (2×2).</summary>
    private static CombatTables Tables()
    {
        var terrain = new byte[66 * 4];
        for (int t = 0; t < 66; t++)
        {
            terrain[t * 4] = 1;
            terrain[t * 4 + 3] = (byte)t;
        }
        terrain[23 * 4 + 3] = 22;
        terrain[2 * 4] = 255;
        terrain[2 * 4 + 2] = 2;
        terrain[40 * 4] = 4;
        var icon = new byte[64];
        for (int i = 0; i < 64; i++)
            icon[i] = 0xFF;
        icon[8] = 0; icon[9] = 0; // size 1
        byte[] big = [0, 0, 1, 0, 0, 1, 1, 1];
        big.CopyTo(icon, 32); // size 4
        return new CombatTables(terrain, icon, new byte[16], new byte[16], [7, 2, 3, 6], new byte[8], new byte[8], new byte[60]);
    }

    private static (Fight Fight, CombatMap Map, FightMoves Moves) Setup(params (int X, int Y, int Hostile, int Size)[] cs)
    {
        var map = new CombatMap(Tables());
        Array.Fill(map.Arena.Bytes, (byte)23, CombatArena.HeaderSize, CombatArena.Width * CombatArena.Height);
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        int k = 0;
        foreach (var (x, y, h, size) in cs)
        {
            var c = new EclCharacter();
            c[FightRules.Hostile] = (byte)h;
            c[FightRules.InPlay] = 1;
            c[FightRules.Hp] = 10;
            c[FightRules.Movement] = 12;
            c[FightRules.Size] = (byte)size;
            memory.Party.Members.Add(c);
            k++;
            map.SetSlotCreature(k, c);
            map.SetSlotPosition(k, x, y);
            map.SetSlotSize(k, size);
        }
        map.SlotCount = k + 1;
        map.Rebuild();
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable(new byte[2048]), new TurboPascalRandom(1).Next,
            new EndActionsController(), map);
        var moves = new FightMoves(map);
        fight.Moves = moves;
        foreach (var c in fight.List)
            fight.Record(c)[CombatRecord.Movement] = 24;
        return (fight, map, moves);
    }

    [Fact]
    public void Direction_is_the_first_cone_and_0_on_the_same_cell()
    {
        var (_, map, _) = Setup();
        Assert.Equal(0, map.DirectionToward(10, 10, 10, 10));
        Assert.Equal(0, map.DirectionToward(10, 10, 10, 5));
        Assert.Equal(2, map.DirectionToward(10, 10, 15, 10));
        Assert.Equal(1, map.DirectionToward(10, 10, 12, 8));
        Assert.Equal(4, map.DirectionToward(10, 10, 10, 11));
        // a knight's move east-north-east: the cones of 1 and 2 both hold it; 1 comes first
        Assert.Equal(1, map.DirectionToward(10, 10, 12, 9));
    }

    [Fact]
    public void Distance_counts_two_per_straight_and_three_per_diagonal_step_then_halves()
    {
        var (_, map, _) = Setup((10, 10, 0, 1), (13, 10, 1, 1), (13, 13, 1, 1), (14, 11, 1, 1));
        var a = map.SlotCreature(1)!;
        Assert.Equal(3, map.Distance(a, map.SlotCreature(2)!));  // 3 straight: 6 → 3
        Assert.Equal(4, map.Distance(a, map.SlotCreature(3)!));  // 3 diagonal: 9 → 4
        Assert.Equal(4, map.Distance(a, map.SlotCreature(4)!));  // 1 diagonal + 3 straight: 9 → 4
        Assert.Equal(1, map.Distance(10, 10, 11, 11));
    }

    [Fact]
    public void A_wall_blocks_the_line_but_not_the_distance()
    {
        var (_, map, _) = Setup((10, 10, 0, 1), (14, 10, 1, 1));
        map.Arena[12, 10] = 2;
        Assert.Equal(0, map.CountTargets(map.SlotCreature(1)!, 255));
        Assert.Equal(4, map.Distance(map.SlotCreature(1)!, map.SlotCreature(2)!));
    }

    [Fact]
    public void Adjacent_means_one_step_with_a_clear_line_and_footprints_count()
    {
        var (_, map, _) = Setup((10, 10, 0, 1), (11, 11, 1, 1), (12, 10, 1, 1), (8, 8, 1, 4));
        Assert.Equal(2, map.CountTargets(map.SlotCreature(1)!, 1)); // the diagonal one and the 2×2 one at 9,9
        Assert.Equal([2, 4], map.PotentialTargets.Order().ToArray());
    }

    [Fact]
    public void A_step_costs_2_or_3_and_sets_the_facing()
    {
        var (fight, map, moves) = Setup((10, 10, 0, 1));
        var c = fight.List[0];
        Assert.Equal(StepResultMoved, moves.Step(fight, c, 3));
        Assert.Equal((11, 11), map.Position(c));
        Assert.Equal(21, fight.Record(c)[CombatRecord.Movement]);
        Assert.Equal(3, fight.Record(c)[CombatRecord.Facing]);
        Assert.Same(c, map.At(11, 11));
        Assert.Null(map.At(10, 10));
    }

    private static readonly Game.StepResult StepResultMoved = Game.StepResult.Moved;

    [Fact]
    public void Walls_creatures_and_the_edge_block_the_step_and_cost_gates_it()
    {
        var (fight, map, moves) = Setup((0, 10, 0, 1), (1, 10, 1, 1));
        var c = fight.List[0];
        Assert.Equal(Game.StepResult.Blocked, moves.Step(fight, c, 2)); // a creature: the menu attacks instead
        map.Arena[0, 9] = 2;
        Assert.Equal(Game.StepResult.Blocked, moves.Step(fight, c, 0));
        map.Arena[0, 11] = 40;
        fight.Record(c)[CombatRecord.Movement] = 3;
        Assert.Equal(Game.StepResult.Blocked, moves.Step(fight, c, 4)); // cost 4 > 3 left
        Assert.Equal(4, fight.Record(c)[CombatRecord.Facing]); // each try turns the creature
        Assert.Equal((0, 10), map.Position(c));
    }

    [Fact]
    public void Not_enough_movement_for_a_diagonal_still_draws_the_free_attacks_and_ends_the_movement()
    {
        var (fight, map, moves) = Setup((10, 10, 0, 1));
        var c = fight.List[0];
        fight.Record(c)[CombatRecord.Movement] = 2;
        Assert.Equal(Game.StepResult.NoMovement, moves.Step(fight, c, 1));
        Assert.Equal(0, fight.Record(c)[CombatRecord.Movement]);
        Assert.Equal((10, 10), map.Position(c));
    }

    [Fact]
    public void Leaving_an_enemy_draws_one_free_attack_from_behind_staying_next_to_it_does_not()
    {
        // Mover at 10,10; enemy A at 11,10 (left behind by a step west); enemy B at 9,11 (still adjacent after it).
        var (fight, map, moves) = Setup((10, 10, 0, 1), (11, 10, 1, 1), (9, 11, 1, 1));
        var mover = fight.List[0];
        foreach (var e in fight.List.Skip(1))
        {
            e[0xA1] = 2;          // base attacks
            e[0x113] = 0;         // none left this round: the free attack gets one
            e[0x110] = 0;         // THAC0 byte 0: misses whatever the roll (except 20)
            fight.Record(e).InitiativeValue = 5;
        }
        mover[0x111] = 60;
        var a = fight.Record(fight.List[1]);
        var panels = new List<EclCharacter>();
        fight.PanelShown = panels.Add; // 0x13429: the mover's panel again after the free attack (DOSBox-X s30)
        Assert.Equal(Game.StepResult.Moved, moves.Step(fight, mover, 6));
        Assert.Same(mover, Assert.Single(panels));
        Assert.Equal(1, a[CombatRecord.MadeAttack]); // A attacked (the attack routine sets +8)
        Assert.Equal(0, fight.Record(fight.List[2])[CombatRecord.MadeAttack]);
        Assert.Equal(0, fight.Record(mover)[CombatRecord.FlankCount]); // no 0x13FF1 note, and a move clears it anyway
        Assert.Equal(1, a[CombatRecord.AttackPhase]);
        Assert.Equal(0, a.InitiativeValue); // its attack used up: its actions end (START 0x31D7)
    }

    [Fact]
    public void A_guard_attacks_a_creature_that_steps_next_to_it_and_stops_guarding()
    {
        var (fight, map, moves) = Setup((10, 10, 0, 1), (12, 10, 1, 1));
        var g = fight.List[1];
        g[0x113] = 1;
        fight.Record(g)[CombatRecord.Guarding] = 1;
        Assert.Equal(Game.StepResult.Moved, moves.Step(fight, fight.List[0], 2));
        Assert.Equal(0, fight.Record(g)[CombatRecord.Guarding]);
        Assert.Equal(1, fight.Record(g)[CombatRecord.MadeAttack]);
        Assert.Equal(1, fight.Record(fight.List[0])[CombatRecord.FlankCount]); // the guard's attack is noted (0x13FF1)
    }

    [Fact]
    public void A_step_off_the_edge_is_the_flee()
    {
        var (fight, map, moves) = Setup((0, 10, 0, 1));
        var c = fight.List[0];
        Assert.Equal(Game.StepResult.Fled, moves.Step(fight, c, 6));
        Assert.Equal((byte)CreatureStatus.Fled, c[FightRules.Status]);
        Assert.Equal(0, fight.Record(c)[CombatRecord.Movement]);
    }

    [Fact]
    public void Fleeing_with_no_enemy_in_sight_needs_no_draw_and_leaves_status_3()
    {
        var (fight, map, moves) = Setup((10, 10, 0, 1), (20, 10, 1, 1));
        map.Arena[15, 10] = 2;
        var c = fight.List[0];
        var r = new TurboPascalRandom(7);
        var f2 = new Fight(fight.Memory, fight.Encounter, fight.Items, r.Next, fight.Controller, map);
        Assert.True(moves.Flee(f2, c));
        Assert.Equal(0, r.Draws);
        Assert.Equal((byte)CreatureStatus.Fled, c[FightRules.Status]);
        Assert.Equal(0, c[FightRules.InPlay]);
        Assert.Equal(10, c[FightRules.Hp]);
        Assert.False(map.OnMap(c));
    }

    [Fact]
    public void Fleeing_from_an_equally_fast_enemy_draws_one_1d2()
    {
        var (fight, map, moves) = Setup((10, 10, 0, 1), (20, 10, 1, 1));
        var r = new TurboPascalRandom(7);
        var f2 = new Fight(fight.Memory, fight.Encounter, fight.Items, r.Next, fight.Controller, map);
        var probe = new TurboPascalRandom(7);
        bool expected = probe.Next(2) == 0;
        Assert.Equal(expected, moves.Flee(f2, fight.List[0]));
        Assert.Equal(1, r.Draws);
        fight.List[1][FightRules.Movement] = 13; // faster enemy: never away, no draw
        var (f3, m3, mv3) = Setup((10, 10, 0, 1), (20, 10, 1, 1));
        f3.List[1][FightRules.Movement] = 13;
        var r3 = new TurboPascalRandom(7);
        Assert.False(mv3.Flee(new Fight(f3.Memory, f3.Encounter, f3.Items, r3.Next, f3.Controller, m3), f3.List[0]));
        Assert.Equal(0, r3.Draws);
    }

    [Fact]
    public void The_flee_shows_its_message_while_the_creature_is_still_on_the_map()
    {
        // 0x134CE–0x13502 (s30): away → 0x2BDF0's message before the status change and the map removal; not away →
        // START 0x1693's message.
        var (fight, map, moves) = Setup((10, 10, 0, 1), (20, 10, 1, 1));
        map.Arena[15, 10] = 2;
        var c = fight.List[0];
        var seen = new List<(bool Away, int InPlay, bool OnMap)>();
        fight.State.FleeMessage = (x, away) => seen.Add((away, x[FightRules.InPlay], map.OnMap(x)));
        Assert.True(moves.Flee(fight, c));
        Assert.Equal([(true, 1, true)], seen);

        var (f3, m3, mv3) = Setup((10, 10, 0, 1), (20, 10, 1, 1));
        f3.List[1][FightRules.Movement] = 13; // a faster enemy: not away
        seen.Clear();
        f3.State.FleeMessage = (x, away) => seen.Add((away, x[FightRules.InPlay], m3.OnMap(x)));
        Assert.False(mv3.Flee(f3, f3.List[0]));
        Assert.Equal([(false, 1, true)], seen);
    }

    [Fact]
    public void A_party_member_who_drops_leaves_a_body_on_the_map()
    {
        var (fight, map, _) = Setup((10, 10, 0, 1));
        var c = fight.List[0];
        c[FightRules.InPlay] = 0;
        map.Downed(c, fight.State);
        Assert.False(map.OnMap(c));
        Assert.Equal(ArenaBuilder.Body, map.Arena[10, 10]);
        Assert.Single(map.Bodies);
    }
}
