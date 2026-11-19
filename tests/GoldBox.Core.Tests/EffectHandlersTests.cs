using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>The effect engine (docs/re/spells-effects.md): dispatcher 0x2B1E4, 0x2B04A, handlers, saves, damage.</summary>
public class EffectHandlersTests
{
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

    private static EclCharacter Creature(string name = "X", bool hostile = false, byte hp = 10)
    {
        var c = new EclCharacter { Name = name };
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = (byte)(hostile ? 1 : 0);
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[FightRules.Ac] = 50;
        c[FightRules.AcRear] = 50;
        for (int i = 0; i < 5; i++)
            c[EffectHandlers.Saves + i] = 12;
        return c;
    }

    private static (EffectHandlers Fx, FightState F) Engine(Rng rng, params EclCharacter[] list)
    {
        var fx = new EffectHandlers(rng.Next) { List = () => list };
        var f = new FightState();
        fx.Trigger(-1, list.Length > 0 ? list[0] : Creature(), f); // binds the scratch cells
        return (fx, f);
    }

    [Fact]
    public void Class_lists_match_the_dispatcher()
    {
        Assert.Equal(new byte[] { 0x01, 0x02, 0x21, 0x24, 0x31, 0x03, 0x06, 0x12, 0x1A }, EffectHandlers.Classes[10]);
        Assert.Equal(new byte[] { 0x33, 0x34, 0x35, 0x1F }, EffectHandlers.Classes[7]);
        Assert.Equal(new byte[] { 0x27, 0x2A, 0x3A }, EffectHandlers.Classes[18]);
        Assert.Equal(EclPartyRules.SaveTriggerEffects, EffectHandlers.Classes[12]);
        Assert.Equal(EclPartyRules.HitTriggerEffects, EffectHandlers.Classes[16]);
    }

    [Fact]
    public void Bless_adds_one_to_the_attack_roll_once_per_class_run()
    {
        var c = Creature();
        EffectHandlers.AddEffect(c, 1, 5, 1, false);
        EffectHandlers.AddEffect(c, 1, 5, 1, false); // a second Bless: only the first instance runs
        var (fx, f) = Engine(new Rng(), c);
        f.AttackRoll = 10;
        fx.Trigger(FightRules.TriggerAttackRollAttacker, c, f);
        Assert.Equal(11, f.AttackRoll);
        Assert.Equal(5, fx.Cells.Morale);
    }

    [Fact]
    public void Protection_from_evil_lowers_an_evil_attackers_roll()
    {
        var def = Creature("D");
        var att = Creature("A", hostile: true);
        att[EffectHandlers.Alignment] = 8; // chaotic evil
        EffectHandlers.AddEffect(def, 8, 10, 1, false);
        var (fx, f) = Engine(new Rng(), def, att);
        fx.CurrentCharacter = () => att;
        f.AttackRoll = 10;
        fx.Trigger(FightRules.TriggerDefenderAc, def, f);
        Assert.Equal(8, f.AttackRoll);
        att[EffectHandlers.Alignment] = 0;
        f.AttackRoll = 10;
        fx.Trigger(FightRules.TriggerDefenderAc, def, f);
        Assert.Equal(10, f.AttackRoll);
    }

    [Fact]
    public void Blind_lowers_AC_without_piling_up_over_attacks()
    {
        var c = Creature();
        EffectHandlers.AddEffect(c, 0x21, 0, 0xFF, false);
        var (fx, f) = Engine(new Rng(), c);
        fx.Trigger(FightRules.TriggerDefenderAc, c, f);
        fx.Trigger(FightRules.TriggerDefenderAc, c, f);
        Assert.Equal(46, c[0x111]);
        Assert.Equal(46, c[0x112]);
    }

    [Fact]
    public void Shield_sets_AC_3_and_stops_magic_missile()
    {
        var c = Creature();
        EffectHandlers.AddEffect(c, 0x11, 10, 1, false);
        var (fx, f) = Engine(new Rng(), c);
        fx.Cells.Spell = 0x0F;
        f.PendingDamage = 7;
        fx.Trigger(6, c, f);
        Assert.Equal(0x39, c[0x111]);
        Assert.Equal(0, f.PendingDamage);
    }

    [Fact]
    public void Sleep_ends_the_creatures_actions_at_its_turn()
    {
        var c = Creature();
        EffectHandlers.AddEffect(c, 0x35, 5, 1, false);
        var (fx, f) = Engine(new Rng(), c);
        var rec = f.Record(c);
        rec.InitiativeValue = 7;
        rec[CombatRecord.Movement] = 12;
        fx.Trigger(FightRules.TriggerTurnStart, c, f);
        Assert.Equal(0, rec.InitiativeValue);
        Assert.Equal(0, rec[CombatRecord.Movement]);
    }

    [Fact]
    public void Prayer_reaches_allies_in_range_only_through_the_map()
    {
        var caster = Creature("C");
        var ally = Creature("A");
        var foe = Creature("F", hostile: true);
        EffectHandlers.AddEffect(caster, 0x31, 1, 0x05, false); // param bit 4 clear: the party side
        var (fx, f) = Engine(new Rng(), caster, ally, foe);
        fx.InCombat = true;
        var range = new FakeAura { InRange = true };
        fx.AuraRange = range;
        f.AttackRoll = 10;
        fx.Trigger(10, ally, f);
        Assert.Equal(11, f.AttackRoll);
        f.AttackRoll = 10;
        fx.Trigger(10, foe, f);
        Assert.Equal(9, f.AttackRoll);
        Assert.Equal(6, range.LastRange);
        range.InRange = false;
        f.AttackRoll = 10;
        fx.Trigger(10, foe, f);
        Assert.Equal(10, f.AttackRoll);
    }

    private sealed class FakeAura : IFightAuraRange
    {
        public bool InRange;
        public int LastRange;
        public bool InAuraRange(EclCharacter carrier, EclCharacter creature, int range)
        {
            LastRange = range;
            return InRange;
        }
    }

    [Fact]
    public void Mirror_image_rolls_one_die_per_hit_and_loses_an_image_on_more_than_1()
    {
        var c = Creature();
        var e = EffectHandlers.AddEffect(c, 0x1C, 10, 2, false);
        var rng = new Rng(1, 0); // 1d3 = 2: image lost; then 1d2 = 1: no
        var (fx, f) = Engine(rng, c);
        fx.Cells.Spell = 0x0F;
        f.PendingDamage = 5;
        fx.Trigger(5, c, f);
        Assert.Equal(0, f.PendingDamage);
        Assert.Equal(1, e[3]);
        f.PendingDamage = 5;
        fx.Trigger(5, c, f);
        Assert.Equal(5, f.PendingDamage);
        Assert.Equal(new ushort[] { 3, 2 }, rng.Ranges);
    }

    [Fact]
    public void Saving_throw_rolls_1d20_then_adds_bonus_and_class_12()
    {
        var c = Creature();
        c[EffectHandlers.SaveBonus] = 1;
        EffectHandlers.AddEffect(c, 0x24, 0, 0xFF, false); // bestow curse: save − 4
        var (fx, _) = Engine(new Rng(13), c);           // 14 + 1 + 2 − 4 = 13 ≥ 12
        Assert.True(fx.SavingThrow(c, 3, 2));
        Assert.Equal(13, fx.Cells.SaveRoll);
        Assert.Equal(3, fx.Cells.SaveType);
        var (fx2, _) = Engine(new Rng(11), c);          // 12 + 1 + 2 − 4 = 11 < 12
        Assert.False(fx2.SavingThrow(c, 3, 2));
        var (fx3, _) = Engine(new Rng(0), c);           // natural 1 fails
        Assert.False(fx3.SavingThrow(c, 3, 50));
    }

    [Fact]
    public void Effect_with_message_replaces_a_shorter_one_and_respects_immunity_and_saves()
    {
        var c = Creature();
        var (fx, _) = Engine(new Rng(), c);
        Assert.True(fx.AddEffectWithMessage(c, 1, 5, 1, false, 0, false));
        Assert.True(fx.AddEffectWithMessage(c, 1, 3, 1, false, 0, false)); // shorter: the old one stays
        Assert.Equal(2, c.Effects.Count(e => e[0] == 1));
        Assert.True(fx.AddEffectWithMessage(c, 2, 5, 1, false, 0, false));
        Assert.True(fx.AddEffectWithMessage(c, 2, 9, 1, false, 0, false)); // longer: replaces
        Assert.Single(c.Effects, e => e[0] == 2);
        Assert.False(fx.AddEffectWithMessage(c, 0x35, 5, 1, false, 1, saved: true)); // save negates
        Assert.True(fx.AddEffectWithMessage(c, 0x35, 5, 1, false, 0, saved: true));  // save does not negate
    }

    [Fact]
    public void Effect_message_text_is_raised_only_when_added_with_a_text()
    {
        // 0x2C540: the text shows after the record is added and only when not empty (0x2C603); unaffected ignores it.
        var c = Creature();
        var (fx, _) = Engine(new Rng(), c);
        var seen = new List<(EffectEvent E, string? Key, int Effects)>();
        fx.Event += (who, e) => seen.Add((e, fx.EffectText, who.Effects.Count));
        fx.AddEffectWithMessage(c, 1, 5, 1, false, 0, false, text: EffectTexts.Asleep);
        fx.AddEffectWithMessage(c, 2, 5, 1, false, 0, false, EffectEvent.Charmed);
        fx.AddEffectWithMessage(c, 0x35, 5, 1, false, 1, saved: true, text: EffectTexts.Held);
        Assert.Equal([(EffectEvent.EffectGained, EffectTexts.Asleep, 1), (EffectEvent.Charmed, null, 2),
            (EffectEvent.Unaffected, null, 2)], seen);
        Assert.Null(fx.EffectText);
    }

    [Fact]
    public void Removal_runs_the_handler_only_when_the_record_says_so()
    {
        var c = Creature();
        c[EffectHandlers.Hostile] = 1;
        var (fx, f) = Engine(new Rng(), c);
        var charm = EffectHandlers.AddEffect(c, 0x0B, 10, 0x01, true);
        fx.Trigger(15, c, f);                      // charmed: param + 0x20 + 64, hostile = bit 7 = 0
        Assert.Equal(0x61, charm[3]);
        Assert.Equal(0, c[EffectHandlers.Hostile]);
        Assert.Equal(1, c[EffectHandlers.Auto]);
        fx.RemoveEffect(c, 0x0B);                  // removed: hostile = bit 6 again
        Assert.Equal(1, c[EffectHandlers.Hostile]);
        Assert.Empty(c.Effects);
        var quiet = EffectHandlers.AddEffect(c, 0x0B, 10, 0x01, false);
        c[EffectHandlers.Hostile] = 0;
        fx.Removed(c, quiet);
        Assert.Equal(0, c[EffectHandlers.Hostile]);
    }

    [Fact]
    public void Non_weapon_damage_halves_on_a_save_and_resist_fire_halves_again()
    {
        var c = Creature(hp: 20);
        EffectHandlers.AddEffect(c, 0x14, 10, 1, false);
        var (fx, f) = Engine(new Rng(), c);
        f.DamageFlags = 1;
        fx.NonWeaponDamage(c, 12, 2, saved: true); // 12 → 6 (resist fire) → 3 (save halves)
        Assert.Equal(17, c[FightRules.Hp]);
        fx.NonWeaponDamage(c, 12, 1, saved: true); // save cancels
        Assert.Equal(17, c[FightRules.Hp]);
    }

    [Fact]
    public void Healing_stops_at_maximum_and_skips_the_dead()
    {
        var c = Creature(hp: 10);
        c[FightRules.Hp] = 4;
        var (fx, _) = Engine(new Rng(), c);
        Assert.True(fx.RestoreHitPoints(c, 9, false));
        Assert.Equal(10, c[FightRules.Hp]);
        c[EffectHandlers.Status] = 6;
        Assert.False(fx.RestoreHitPoints(c, 1, false));
        c[EffectHandlers.Status] = 0;
        c[FightRules.Hp] = 4;
        EffectHandlers.AddEffect(c, 0x32, 0, 0xFF, false); // rot: only a bypassing heal below max
        Assert.False(fx.RestoreHitPoints(c, 1, false));
        Assert.True(fx.RestoreHitPoints(c, 1, true));
    }

    [Fact]
    public void Stinking_cloud_terrain_saves_then_rolls_1d4_for_helplessness()
    {
        var c = Creature();
        var rng = new Rng(2, 3); // save 1d20 = 3 (fails vs 12), then 1d4 = 4 → 5 minutes
        var (fx, f) = Engine(rng, c);
        fx.InCombat = true;
        fx.TerrainMap = new Cloud();
        fx.Terrain(c, f);
        Assert.Equal(new ushort[] { 20, 4 }, rng.Ranges);
        var h = Assert.Single(c.Effects);
        Assert.Equal(0x1F, h[0]);
        Assert.Equal(5, h[1]);
    }

    private sealed class Cloud : IFightTerrain
    {
        public int TerrainUnder(EclCharacter c) => 0x1E;
    }

    [Fact]
    public void Damage_opcode_hook_runs_the_save_and_hit_classes()
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var m = Creature();
        memory.Party.Members.Add(m);
        var rules = new EclPartyRules(memory, _ => 0);
        var fx = new EffectHandlers(n => 0);
        fx.AttachTo(rules);
        EffectHandlers.AddEffect(m, 0x24, 0, 0xFF, false); // bestow curse: save − 4
        Assert.Equal(6, rules.EffectTrigger!(m, 12, 10));
        EffectHandlers.AddEffect(m, 0x19, 1, 0x0C, false); // invisible: to-hit − 4
        Assert.Equal(16, rules.EffectTrigger!(m, 16, 20));
    }

    // ---- The captured round start with the party's racial effects -------------------------------------------------

    private static readonly string? Rounds = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_rounds") : null;

    public sealed class RoundFactAttribute : TheoryAttribute
    {
        public RoundFactAttribute()
        {
            if (Rounds is null || !Directory.Exists(Rounds))
                Skip = "needs the private DOSBox captures (fight_rounds)";
        }
    }

    /// <summary>
    /// The round start of the captured goblin fight (FightRoundCaptureTests) run with the real handlers and the effects
    /// save A's party carries (90, 97, 26, 47, 124, 107 — racial, none in classes 7, 0x0F or 0x12) on every party
    /// member: the same draws, initiatives and movement.
    /// </summary>
    [RoundFact]
    [InlineData(0)]
    [InlineData(3)]
    public void Round_start_with_real_handlers_matches_the_capture(int round)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Rounds!, $"round_{round}.json")));
        var root = doc.RootElement;
        var creatures = root.GetProperty("creatures").EnumerateArray()
            .Select(c => (Record: Convert.FromHexString(c.GetProperty("record").GetString()!),
                Combat: Convert.FromHexString(c.GetProperty("combat").GetString()!)))
            .ToList();
        uint captured = root.GetProperty("randseed").GetUInt32();
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        foreach (var (rec, _) in creatures)
            memory.Party.Members.Add(new EclCharacter(rec));
        var list = memory.Party.Members;
        foreach (var c in list.Where(c => c[FightRules.Hostile] == 0))
            foreach (byte id in new byte[] { 0x5A, 0x61, 0x1A, 0x2F, 0x7C, 0x6B })
                EffectHandlers.AddEffect(c, id, 0, 0xFF, false);
        memory.Write(FightRules.SurpriseCell, root.GetProperty("surprise_6dcb").GetUInt16());
        uint seed = captured;
        int draws = list.Count + list.Count(c => c[FightRules.InPlay] != 0);
        for (int i = 0; i < draws; i++)
            seed = unchecked((seed - 1) * Inverse());
        var rng = new TurboPascalRandom(seed);
        var fx = new EffectHandlers(rng.Next);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(),
            new GridGeometry(), fx);
        fx.Attach(fight);
        fight.State.RoundCounter = root.GetProperty("round_counter").GetByte();
        foreach (var c in list)
            FightRules.BeginRound(c, fight.Record(c), fight.State, fight.Items, memory, rng.Next, fx);
        var picked = fight.NextCreature();
        Assert.Equal(captured, rng.Seed);
        Assert.Same(list[root.GetProperty("current_index").GetInt32()], picked);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal((sbyte)creatures[i].Combat[CombatRecord.Initiative], fight.Record(list[i]).InitiativeValue);
            Assert.Equal(creatures[i].Combat[CombatRecord.Movement], fight.Record(list[i])[CombatRecord.Movement]);
        }
        Assert.Empty(fx.Diagnostics);
    }

    private static uint Inverse()
    {
        uint inv = TurboPascalRandom.Multiplier;
        for (int i = 0; i < 5; i++)
            inv = unchecked(inv * (2 - TurboPascalRandom.Multiplier * inv));
        return inv;
    }
}
