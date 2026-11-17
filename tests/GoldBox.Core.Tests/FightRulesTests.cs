using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The fight's rules read from the program (docs/re/combat-format.md "The fight").</summary>
public class FightRulesTests
{
    /// <summary>Random(n) answers from a queue; records each range asked.</summary>
    private sealed class Rng(params ushort[] values)
    {
        public Queue<ushort> Values { get; } = new(values);
        public List<ushort> Ranges { get; } = [];
        public ushort Next(ushort n)
        {
            Ranges.Add(n);
            return Values.Count > 0 ? Values.Dequeue() : (ushort)0;
        }
    }

    private static readonly BaseItemTable NoItems = new(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]);

    private static EclCharacter Creature(string name, bool hostile = false, byte hp = 10, byte thac0 = 40, byte ac = 50)
    {
        var c = new EclCharacter { Name = name };
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = (byte)(hostile ? 1 : 0);
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[FightRules.Thac0] = thac0;
        c[FightRules.Ac] = ac;
        c[FightRules.AcRear] = ac;
        c[FightRules.Movement] = 9;
        c[FightRules.Dex] = 10;
        c[FightRules.BaseAttacksPrimary] = 2;
        c[0x115] = 1; // 1d6 phase 1
        c[0x117] = 6;
        return c;
    }

    // ---- Start of round ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, -4)]
    [InlineData(2, -4)]
    [InlineData(3, -3)]
    [InlineData(5, -1)]
    [InlineData(6, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(18, 3)]
    [InlineData(19, 3)]
    [InlineData(21, 4)]
    [InlineData(25, 5)]
    [InlineData(26, 0)]
    public void Dexterity_adjusts_initiative(byte dex, int adj) => Assert.Equal(adj, FightRules.DexAdjust(dex));

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(3, 0, 1)]
    [InlineData(3, 1, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(255, 1, 0)] // byte add wraps
    public void Half_attacks_round_up_on_odd_rounds(byte rate, byte round, byte attacks) =>
        Assert.Equal(attacks, FightRules.AttacksFromRate(rate, round));

    private static (FightState Fight, CombatRecord Rec, EclMemory Memory) Round(EclCharacter c, Rng rng, ushort surprise = 0, ushort partyMove = 0)
    {
        var f = new FightState();
        var m = new EclMemory();
        m.Write(FightRules.SurpriseCell, surprise);
        m.Write(FightRules.PartyMoveCell, partyMove);
        var rec = f.Record(c);
        FightRules.BeginRound(c, rec, f, NoItems, m, rng.Next, NoFightEffects.Instance);
        return (f, rec, m);
    }

    [Fact]
    public void Start_of_round_rolls_1d6_plus_dexterity_once()
    {
        var c = Creature("A");
        c[FightRules.Dex] = 17; // +2
        var rng = new Rng(3); // 1d6 = 4
        var (_, rec, _) = Round(c, rng);
        Assert.Equal(6, rec.InitiativeValue);
        Assert.Equal([6], rng.Ranges);
        Assert.Equal(1, rec[CombatRecord.CastAllowed]);
        Assert.Equal(2, rec[CombatRecord.AttackPhase]);
        Assert.Equal(18, rec[CombatRecord.Movement]); // 9 × 2
        Assert.Equal(1, c[0x113]); // base 2 halves → 1 attack
    }

    [Fact]
    public void Initiative_is_at_least_1_then_surprise_takes_6_and_out_of_range_is_0()
    {
        var slow = Creature("S");
        slow[FightRules.Dex] = 1; // −4
        Assert.Equal(1, Round(slow, new Rng(0)).Rec.InitiativeValue);
        var surprised = Creature("P"); // party: bit 0
        Assert.Equal(0, Round(surprised, new Rng(5), surprise: 1).Rec.InitiativeValue); // 6 − 6
        var monster = Creature("M", hostile: true); // monsters: bit 1
        Assert.Equal(6, Round(monster, new Rng(5), surprise: 1).Rec.InitiativeValue);
        Assert.Equal(0, Round(Creature("Q"), new Rng(2), surprise: 1).Rec.InitiativeValue); // 3 − 6 < 0
    }

    [Fact]
    public void Out_of_play_creatures_get_initiative_0_and_no_draw()
    {
        var c = Creature("D");
        c[FightRules.InPlay] = 0;
        var rng = new Rng();
        Assert.Equal(0, Round(c, rng).Rec.InitiativeValue);
        Assert.Empty(rng.Ranges);
    }

    [Fact]
    public void Movement_adds_the_party_modifier_and_clamps_to_1_to_96()
    {
        var c = Creature("A");
        Assert.Equal(24, Round(c, new Rng(0), partyMove: 3).Rec[CombatRecord.Movement]);
        c[FightRules.Movement] = 0;
        Assert.Equal(2, Round(c, new Rng(0)).Rec[CombatRecord.Movement]);
        c[FightRules.Movement] = 97;
        Assert.Equal(2, Round(c, new Rng(0)).Rec[CombatRecord.Movement]);
        var m = Creature("M", hostile: true);
        Assert.Equal(18, Round(m, new Rng(0), partyMove: 3).Rec[CombatRecord.Movement]); // monsters: no modifier
    }

    // ---- Attack roll, damage ----------------------------------------------------------------------------------

    private static bool Roll(EclCharacter a, EclCharacter d, byte defense, ushort raw, EclMemory? m = null)
    {
        var f = new FightState();
        return FightRules.AttackRoll(a, d, defense, f, m ?? new EclMemory(), new Rng(raw).Next, NoFightEffects.Instance);
    }

    [Fact]
    public void Attack_roll_compares_roll_plus_thac0_with_defense()
    {
        var a = Creature("A", thac0: 40); // THAC0 20
        var d = Creature("D", hostile: true);
        Assert.True(Roll(a, d, 50, 9));   // 10 + 40 ≥ 50 (AC 10: roll ≥ 10)
        Assert.False(Roll(a, d, 50, 8));  // 9
        Assert.False(Roll(a, d, 0, 0));   // a natural 1 always misses
        Assert.True(Roll(a, d, 140, 19)); // 20 counts as 100
        var m = new EclMemory();
        m.Write(FightRules.PartyThac0Cell, 0xFF); // −1 for the party side
        Assert.False(Roll(a, d, 50, 9, m));
        m.Write(FightRules.MonsterThac0Cell, 5); // not the party's
        Assert.False(Roll(a, d, 50, 9, m));
    }

    [Fact]
    public void Attack_roll_removes_every_invisibility_of_the_attacker()
    {
        var a = Creature("A");
        a.Effects.Add([FightRules.InvisibilityEffect, 0, 0, 0, 0, 0, 0, 0, 0]);
        a.Effects.Add([FightRules.InvisibilityEffect, 0, 0, 0, 0, 0, 0, 0, 0]);
        Roll(a, Creature("D", hostile: true), 50, 5);
        Assert.Empty(a.Effects); // every instance (0x2BEBC loops)
    }

    [Fact]
    public void Damage_loses_the_defenders_spell_and_the_current_characters_memorised_copy()
    {
        var attacker = Creature("A");
        var caster = Creature("C", hostile: true);
        attacker[FightSpells.Memorised] = 5;
        caster[FightSpells.Memorised] = 5;
        var fight = new FightState { Current = attacker };
        fight.Record(caster)[CombatRecord.SpellCast] = 5;
        FightRules.ApplyAttackResult(caster, 2, true, fight, NoFightEffects.Instance);
        Assert.Equal(0, fight.Record(caster)[CombatRecord.SpellCast]);
        Assert.Equal(0, attacker[FightSpells.Memorised]); // START 0x202B works on DS:5D92
        Assert.Equal(5, caster[FightSpells.Memorised]);
    }

    [Fact]
    public void Negative_damage_wraps_to_a_byte_and_backstab_multiplies()
    {
        var a = Creature("A");
        a[0x115] = 1;
        a[0x117] = 2;
        a[0x119] = unchecked((byte)-2);
        var d = Creature("D", hostile: true);
        var f = new FightState();
        Assert.Equal(255, FightRules.WeaponDamage(a, d, 1, false, f, new Rng(0).Next, NoFightEffects.Instance)); // 1 − 2
        a[0x119] = 0;
        a[FightRules.ThiefLevel] = 9; // × (9 / 4 + 2) = × 4
        Assert.Equal(8, FightRules.WeaponDamage(a, d, 1, true, f, new Rng(1).Next, NoFightEffects.Instance));
        Assert.Equal(1, f.DamageDice);
    }

    [Theory]
    [InlineData(10, 3, CreatureStatus.Okay, 7, 0)]
    [InlineData(10, 10, CreatureStatus.Unconscious, 0, 0)]
    [InlineData(10, 15, CreatureStatus.Dying, 0, 5)]
    [InlineData(10, 20, CreatureStatus.Dead, 0, 0)]  // over 10 > 9
    [InlineData(10, 19, CreatureStatus.Dying, 0, 9)]
    public void Damage_sets_the_status(byte hp, byte damage, CreatureStatus status, byte hpAfter, byte dying)
    {
        var c = Creature("C", hostile: true, hp: hp);
        var f = new FightState();
        f.TeamCount[1] = 3;
        f.Record(c).InitiativeValue = 4;
        FightRules.InflictDamage(c, damage, f);
        Assert.Equal(status, (CreatureStatus)c[FightRules.Status]);
        Assert.Equal(hpAfter, c[FightRules.Hp]);
        Assert.Equal(dying, f.Record(c)[CombatRecord.Dying]);
        bool down = status != CreatureStatus.Okay;
        Assert.Equal(down ? 0 : 1, c[FightRules.InPlay]);
        Assert.Equal(down ? 2 : 3, f.TeamCount[1]);
        Assert.Equal(down ? 0 : 4, f.Record(c).InitiativeValue);
    }

    [Fact]
    public void An_animated_creature_dies_at_0()
    {
        var c = Creature("Z", hp: 5);
        c[FightRules.Status] = (byte)CreatureStatus.Animated;
        FightRules.InflictDamage(c, 5, null);
        Assert.Equal(CreatureStatus.Dead, (CreatureStatus)c[FightRules.Status]);
    }

    // ---- The attack routine -----------------------------------------------------------------------------------

    [Fact]
    public void Phase_2_attacks_come_first_and_draws_are_roll_then_damage()
    {
        var a = Creature("A");
        a[0x113] = 1;
        a[0x114] = 1;
        a[0x116] = 1; // phase 2: 1d4
        a[0x118] = 4;
        var d = Creature("D", hostile: true, hp: 20);
        var fight = new FightState();
        fight.Record(a)[CombatRecord.AttackPhase] = 2;
        var geo = new GridGeometry();
        geo.Place(a, 0, 0);
        geo.Place(d, 1, 0);
        var rng = new Rng(14, 2, 14, 5); // hit, 3 damage (phase 2: d4), hit, 6 damage (phase 1: d6)
        var o = FightRules.RollAttacks(a, d, false, fight, NoItems, new EclMemory(), rng.Next, NoFightEffects.Instance, geo);
        Assert.Equal([20, 4, 20, 6], rng.Ranges);
        Assert.Equal(11, d[FightRules.Hp]);
        Assert.Equal(2, o.Hits);
        Assert.True(o.ActionsDone);
        Assert.Equal(0, fight.Record(a).InitiativeValue);
        Assert.Equal(1, fight.HitsMade[1]);
        Assert.Equal(1, fight.HitsMade[2]);
    }

    [Fact]
    public void A_helpless_defender_is_slain_without_a_roll()
    {
        var a = Creature("A");
        a[0x113] = 2;
        var d = Creature("D", hostile: true, hp: 30);
        d.Effects.Add([0x33, 0, 0, 0, 0, 0, 0, 0, 0]);
        var fight = new FightState();
        fight.Record(a)[CombatRecord.AttackPhase] = 2;
        var rng = new Rng();
        var o = FightRules.RollAttacks(a, d, false, fight, NoItems, new EclMemory(), rng.Next, NoFightEffects.Instance, new GridGeometry());
        Assert.Empty(rng.Ranges);
        Assert.Equal(CreatureStatus.Dying, (CreatureStatus)d[FightRules.Status]); // 35 damage: over 5
        Assert.Equal(0, a[0x113]);
        Assert.True(o.ActionsDone);
    }

    [Fact]
    public void A_large_defender_swaps_in_the_weapons_large_dice()
    {
        var items = new BaseItemTable(new byte[2048]);
        items.Set(5, BaseItemTable.LargeDice, 1);
        items.Set(5, BaseItemTable.LargeSides, 12);
        items.Set(5, BaseItemTable.LargeBonus, 1);
        items.Set(5, BaseItemTable.SmallBonus, 0);
        var a = Creature("A");
        var sword = new byte[EclCharacter.ItemRecordSize];
        sword[0x2E] = 5;
        sword[0x34] = 1;
        a.Items.Add(sword);
        a[0x113] = 1;
        var d = Creature("D", hostile: true, hp: 30);
        d[FightRules.Size] = 2; // 1×2 footprint
        var fight = new FightState();
        fight.Record(a)[CombatRecord.AttackPhase] = 1;
        var rng = new Rng(14, 7);
        FightRules.RollAttacks(a, d, false, fight, items, new EclMemory(), rng.Next, NoFightEffects.Instance, new GridGeometry());
        Assert.Equal([20, 12], rng.Ranges);
        Assert.Equal(30 - 9, d[FightRules.Hp]); // 8 + 1
    }

    // ---- Thrown weapons and DS:5D9A -----------------------------------------------------------------------------

    [Fact]
    public void A_spent_thrown_weapon_drops_into_the_treasure_and_stops_the_plus_experience()
    {
        var items = new BaseItemTable(new byte[2048]);
        items.Set(9, BaseItemTable.Range, 4);
        items.Set(9, BaseItemTable.Flags, 0x14);
        var a = Creature("A");
        var dart = new byte[EclCharacter.ItemRecordSize];
        dart[0x2E] = 9;
        dart[0x34] = 1;
        dart[0x39] = 1;
        dart[CombatRules.ItemPlus] = 1;
        a.Items.Add(dart);
        var state = new EncounterState();
        var placed = new byte[EclCharacter.ItemRecordSize];
        placed[CombatRules.ItemPlus] = 2; // a TREASURE item from before the fight
        state.TreasureItems.Add(placed);
        var fight = new FightState();
        fight.AttacksMade[1] = 1;
        FightRules.DropSpentWeapon(a, dart, fight, state, items);
        Assert.Empty(a.Items);
        Assert.Equal(2, state.TreasureItems.Count);
        Assert.Same(state.TreasureItems[0], state.LastThrownItem);
        Assert.Equal(0, state.LastThrownItem![CombatRules.ItemEquipped]);

        // After the fight a monster's +3 item becomes the new head: it counts, the dart and the placed item do not.
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(a);
        a[CombatRules.Status] = 0;
        var m = Creature("M", hostile: true);
        m[CombatRules.Status] = (byte)CreatureStatus.Dead;
        m[FightRules.InPlay] = 0;
        var loot = new byte[EclCharacter.ItemRecordSize];
        loot[CombatRules.ItemValue] = 1;
        loot[CombatRules.ItemPlus] = 3;
        m.Items.Add(loot);
        memory.Party.Members.Add(m);
        memory.Write(AfterCombat.PartySize, 1);
        state.NotParty.Add(m);
        var o = new AfterCombat(memory, state, _ => 0, []).Run();
        Assert.Equal(1200, o.ExperienceShare);
    }

    // ---- NPC treasure share (0x6866) -------------------------------------------------------------------------

    [Fact]
    public void Npcs_take_their_shares_of_each_coin()
    {
        var pc = Creature("P");
        var npc = Creature("N");
        npc[CombatRules.Npc] = 0xB2;
        npc[FightRules.NpcShare] = 2;
        var pool = new uint[] { 0, 0, 0, 100, 0, 0, 7 };
        var named = FightRules.NpcTreasureShare([pc, npc], pool);
        // total = 1 + 2 = 3 shares; gold: cut 33, the NPC takes 66; jewellery: cut 2, takes 4
        Assert.Equal(34u, pool[3]);
        Assert.Equal(3u, pool[6]);
        Assert.Equal([npc], named);
    }

    [Fact]
    public void The_npc_cut_is_a_byte()
    {
        var npc = Creature("N");
        npc[CombatRules.Npc] = 0x80;
        npc[FightRules.NpcShare] = 1;
        var pool = new uint[] { 1000, 0, 0, 0, 0, 0, 0 };
        FightRules.NpcTreasureShare([Creature("P"), npc], pool);
        Assert.Equal(1000u - 244, pool[0]); // 1000 / 2 = 500 → byte 244
    }

    [Fact]
    public void Npcs_out_of_play_take_nothing()
    {
        var npc = Creature("N");
        npc[CombatRules.Npc] = 0xB2;
        npc[FightRules.NpcShare] = 2;
        npc[FightRules.Status] = (byte)CreatureStatus.Unconscious;
        var pool = new uint[] { 0, 0, 0, 100, 0, 0, 0 };
        Assert.Empty(FightRules.NpcTreasureShare([Creature("P"), npc], pool));
        Assert.Equal(100u, pool[3]);
    }

    // ---- Equipment, end of round, the loop -------------------------------------------------------------------

    [Fact]
    public void Equipment_slots_follow_the_readied_items()
    {
        var items = new BaseItemTable(new byte[2048]);
        items.Set(1, BaseItemTable.Slot, 0);
        items.Set(2, BaseItemTable.Slot, 9);
        items.Set(0x49, BaseItemTable.Slot, 10);
        var c = Creature("C");
        byte[] It(byte type, bool ready) { var b = new byte[63]; b[0x2E] = type; b[0x34] = (byte)(ready ? 1 : 0); c.Items.Add(b); return b; }
        It(1, false);
        var w = It(1, true);
        var r1 = It(2, true);
        var r2 = It(2, true);
        It(2, true); // a third ring: no slot
        var arrows = It(0x49, true);
        var eq = FightRules.Equipment(c, items);
        Assert.Same(w, eq[0]);
        Assert.Same(r1, eq[9]);
        Assert.Same(r2, eq[10]);
        Assert.Same(arrows, eq[FightRules.SlotArrows]);
    }

    [Fact]
    public void Dying_creatures_die_after_ten_rounds_at_the_end_of_round()
    {
        var c = Creature("C", hp: 0);
        c[FightRules.Status] = (byte)CreatureStatus.Dying;
        var f = new FightState();
        f.Record(c)[CombatRecord.Dying] = 8;
        FightRules.EndOfRoundCreature(c, f, NoFightEffects.Instance);
        Assert.Equal(CreatureStatus.Dying, (CreatureStatus)c[FightRules.Status]);
        FightRules.EndOfRoundCreature(c, f, NoFightEffects.Instance);
        Assert.Equal(CreatureStatus.Dead, (CreatureStatus)c[FightRules.Status]);
    }

    [Fact]
    public void Hostile_hp_percentage_is_in_steps_of_5()
    {
        var a = Creature("A", hostile: true, hp: 10);
        var b = Creature("B", hostile: true, hp: 10);
        b[FightRules.Hp] = 3;
        var f = new FightState();
        FightRules.UpdateHostileHpPercent([a, b, Creature("P")], f);
        Assert.Equal(65, f.HostileHpPercent); // 20·13 / 20 = 13 → 65
    }

    private sealed class AttackFirstEnemy : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto)
        {
            var enemy = fight.List.FirstOrDefault(c => c[FightRules.Hostile] != creature[FightRules.Hostile] && c[FightRules.InPlay] != 0);
            if (enemy is null)
                fight.EndActions(creature);
            else
                fight.Attack(creature, enemy);
        }
    }

    [Fact]
    public void A_fight_runs_rounds_until_one_side_is_down()
    {
        var hero = Creature("H", hp: 30, thac0: 60); // hits on anything but a 1
        hero[0x117] = 8;
        var goblin = Creature("G", hostile: true, hp: 6, thac0: 0); // never hits AC 10
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(hero);
        memory.Party.Members.Add(goblin);
        var state = new EncounterState();
        state.NotParty.Add(goblin);
        var geo = new GridGeometry();
        geo.Place(hero, 0, 0);
        geo.Place(goblin, 1, 0);
        var rng = new TurboPascalRandom(12345);
        int minutes = 0;
        var fight = new Fight(memory, state, NoItems, rng.Next, new AttackFirstEnemy(), geo) { MinutePassed = () => minutes++ };
        int rounds = fight.Run(50);
        Assert.InRange(rounds, 1, 50);
        Assert.Equal(rounds, minutes);
        Assert.Equal(0, goblin[FightRules.InPlay]);
        Assert.Equal(1, hero[FightRules.InPlay]);
        Assert.Equal(0, fight.State.TeamCount[1]);
        Assert.Empty(fight.Diagnostics);
    }

    [Fact]
    public void A_fight_with_an_empty_side_ends_at_once()
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(Creature("H"));
        var rng = new Rng();
        var fight = new Fight(memory, new EncounterState(), NoItems, rng.Next, new EndActionsController(), new GridGeometry());
        Assert.Equal(0, fight.Run());
        Assert.Empty(rng.Ranges);
    }
}
