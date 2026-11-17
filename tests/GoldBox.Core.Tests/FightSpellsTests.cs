using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>Spells in a fight (docs/re/spells-effects.md "Spells"), on the player's spell table.</summary>
public class FightSpellsTests
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

    /// <summary>A test geometry whose target list is a fixed set (the map's rule is not ours).</summary>
    private sealed class Area(params EclCharacter[] inside) : IFightGeometry, IFightTargetList
    {
        public int Distance(EclCharacter a, EclCharacter b) => 1;
        public int DirectionToward(EclCharacter from, EclCharacter to) => 0;
        public IReadOnlyList<EclCharacter> Around(int x, int y, int radius) => inside;
        public IReadOnlyList<EclCharacter> AroundCreature(EclCharacter c, int radius) => [];
    }

    private static SpellTable? _table;
    private static SpellTable Table => _table ??= PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!;

    private static EclCharacter Creature(string name, bool hostile = false, byte hp = 10, byte hd = 1)
    {
        var c = new EclCharacter { Name = name };
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = (byte)(hostile ? 1 : 0);
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[FightRules.Ac] = 50;
        c[FightRules.AcRear] = 50;
        c[FightSpells.HitDice] = hd;
        for (int i = 0; i < 5; i++)
            c[EffectHandlers.Saves + i] = 12;
        return c;
    }

    private static (Fight Fight, FightSpells Spells, Rng Rng) Setup(IFightGeometry geometry, Rng rng, params EclCharacter[] list)
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange(list);
        var fx = new EffectHandlers(rng.Next);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(), geometry, fx);
        var spells = new FightSpells(Table, fx, rng.Next);
        fight.Spells = spells;
        foreach (var c in list)
            fight.Record(c)[CombatRecord.CastAllowed] = 1;
        return (fight, spells, rng);
    }

    [PorFact]
    public void Spell_table_and_name_lists_are_found_in_the_program()
    {
        var p = PorProgram.Open(PorData.Open())!;
        Assert.Equal(0, Table[1].CastClass);    // spell 1 is a cleric spell of level 1
        Assert.Equal(1, Table[1].Level);
        Assert.Equal(1, Table[15].CastClass);   // spell 15 a magic-user one
        var names = PorSpellData.Names(p)!;
        Assert.Equal(4, names.GnomeFoes.Count);
        Assert.Equal(8, names.DwarfFoes.Count);
        Assert.Equal(2, names.GnomeDefenseFoes.Count);
        Assert.All(names.GnomeFoes, n => Assert.InRange(n[0], 1, 15));
    }

    [PorFact]
    public void Sleep_pays_hit_dice_from_4d4_and_puts_the_paid_targets_to_sleep()
    {
        var mage = Creature("M");
        mage[FightSpells.MageLevel] = 1;
        mage[FightSpells.Memorised] = 21;
        var g1 = Creature("G1", hostile: true, hd: 1);
        var g2 = Creature("G2", hostile: true, hd: 3); // costs 4
        var g3 = Creature("G3", hostile: true, hd: 1);
        var rng = new Rng(0, 0, 1, 0); // 4d4 = 1 + 1 + 2 + 1 = 5: g1 (1), g2 (4); g3 no longer fits
        var (fight, spells, _) = Setup(new Area(g1, g2, g3), rng, mage, g1, g2, g3);
        fight.Record(mage).InitiativeValue = 5;
        Assert.Contains(21, spells.Castable(fight, mage));
        Assert.True(spells.Cast(fight, mage, 21, null, (3, 3)));
        Assert.NotNull(EffectHandlers.FindEffect(g1, 0x35));
        Assert.NotNull(EffectHandlers.FindEffect(g2, 0x35));
        Assert.Null(EffectHandlers.FindEffect(g3, 0x35));
        Assert.Equal(new ushort[] { 4, 4, 4, 4 }, rng.Ranges);   // no saving throw for Sleep
        Assert.Equal(0, mage[FightSpells.Memorised]);             // the memorised copy is gone
        Assert.Equal(0, fight.Record(mage).InitiativeValue);      // cast at once: actions end
        var e = EffectHandlers.FindEffect(g1, 0x35)!;
        Assert.Equal(Table[21].DurVar * 1 + Table[21].DurFixed, e[1] | (e[2] << 8));
    }

    [PorFact]
    public void Magic_missile_deals_half_level_d4_plus_half_level()
    {
        var mage = Creature("M");
        mage[FightSpells.MageLevel] = 3;
        var orc = Creature("O", hostile: true, hp: 20);
        var rng = new Rng(2); // 1d4 = 3 → 3 + 1 = 4
        var (fight, spells, _) = Setup(new Area(), rng, mage, orc);
        Assert.True(spells.Cast(fight, mage, 15, orc, null));
        Assert.Equal(16, orc[FightRules.Hp]);
        Assert.Equal(new ushort[] { 4 }, rng.Ranges);
    }

    [PorFact]
    public void Cure_light_wounds_heals_1d8()
    {
        var cleric = Creature("C");
        cleric[FightSpells.ClericLevel] = 1;
        var friend = Creature("F", hp: 10);
        friend[FightRules.Hp] = 2;
        var rng = new Rng(4);
        var (fight, spells, _) = Setup(new Area(), rng, cleric, friend);
        Assert.True(spells.CastOn(fight, cleric, 3, [friend]) || fight.Record(cleric)[CombatRecord.SpellCast] == 3);
        if (fight.Record(cleric)[CombatRecord.SpellCast] == 3) // a casting time: cast on its next turn
            Assert.True(spells.ContinueCasting(fight, cleric, [friend], null));
        Assert.Equal(7, friend[FightRules.Hp]);
    }

    [PorFact]
    public void Bless_reaches_only_the_casters_side()
    {
        var cleric = Creature("C");
        cleric[FightSpells.ClericLevel] = 2;
        var friend = Creature("F");
        var foe = Creature("E", hostile: true);
        var (fight, spells, _) = Setup(new Area(cleric, friend, foe), new Rng(), cleric, friend, foe);
        fight.Record(cleric).InitiativeValue = 10;
        Assert.True(spells.Cast(fight, cleric, 1, null, (1, 1)));
        if (fight.Record(cleric)[CombatRecord.SpellCast] == 1)
            spells.ContinueCasting(fight, cleric, [], (1, 1));
        Assert.NotNull(EffectHandlers.FindEffect(cleric, 1));
        Assert.NotNull(EffectHandlers.FindEffect(friend, 1));
        Assert.Null(EffectHandlers.FindEffect(foe, 1));
    }

    [PorFact]
    public void A_spell_with_casting_time_is_begun_and_lowers_initiative()
    {
        int spell = Enumerable.Range(1, 56).First(s => Table[s].WhenCast != 0 && Table[s].CastTime / 3 > 0 &&
                                                         FightSpells.Modelled.Contains(s));
        int delay = Table[spell].CastTime / 3;
        var caster = Creature("C");
        var (fight, spells, _) = Setup(new Area(), new Rng(), caster);
        fight.Record(caster).InitiativeValue = 15;
        Assert.True(spells.Cast(fight, caster, spell, caster, null));
        Assert.Equal(spell, fight.Record(caster)[CombatRecord.SpellCast]);
        Assert.Equal(15 > delay ? 15 - delay : 1, fight.Record(caster).InitiativeValue);
    }

    [PorFact]
    public void A_damaged_caster_loses_the_spell_and_the_current_character_its_memorised_copy()
    {
        var caster = Creature("C");
        caster[FightSpells.Memorised] = 23;
        var attacker = Creature("A", hostile: true);
        attacker[FightSpells.Memorised] = 23;
        var (fight, spells, _) = Setup(new Area(), new Rng(), caster, attacker);
        spells.Effects.Attach(fight);
        fight.Record(caster)[CombatRecord.SpellCast] = 23;
        fight.State.Current = attacker;
        spells.Effects.NonWeaponDamage(caster, 3, 0, false);
        Assert.Equal(0, fight.Record(caster)[CombatRecord.SpellCast]);
        Assert.Equal(23, caster[FightSpells.Memorised]);   // START 0x202B works on the current character
        Assert.Equal(0, attacker[FightSpells.Memorised]);
    }

    [PorFact]
    public void Shocking_grasp_needs_an_attack_roll()
    {
        var mage = Creature("M");
        mage[FightSpells.MageLevel] = 2;
        mage[FightRules.Thac0] = 40;
        var orc = Creature("O", hostile: true, hp: 30);
        var rng = new Rng(4, 1); // 1d8 = 5 damage rolled first, then the attack 1d20 = 2: a miss (2 + 40 < 50)
        var (fight, spells, _) = Setup(new Area(), rng, mage, orc);
        Assert.Equal(0xFF, Table[20].RangeFixed);
        Assert.True(spells.Cast(fight, mage, 20, orc, null));
        if (fight.Record(mage)[CombatRecord.SpellCast] == 20)
            spells.ContinueCasting(fight, mage, [orc], null);
        Assert.Equal(30, orc[FightRules.Hp]);
        Assert.Equal(new ushort[] { 8, 20 }, rng.Ranges);
    }

    // ---- DOSBox: a Sleep cast in the goblin fight (fixtures fight_moves/s02..s04) -------------------------------------

    private static readonly string? Moves = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } fx
        ? Path.Combine(fx, "fight_moves") : null;

    private static (List<EclCharacter> List, uint Seed) LoadState(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Moves!, name, "fight.json")));
        var list = new List<EclCharacter>();
        foreach (var c in doc.RootElement.GetProperty("creatures").EnumerateArray())
        {
            var ch = new EclCharacter(Convert.FromHexString(c.GetProperty("record").GetString()!));
            foreach (var e in c.GetProperty("effects").EnumerateArray())
                ch.Effects.Add(Convert.FromHexString(e.GetProperty("record").GetString()!));
            list.Add(ch);
        }
        return (list, doc.RootElement.GetProperty("vars").GetProperty("randseed").GetUInt32());
    }

    /// <summary>
    /// The elf magic-user of level 1 (list index 2) casts Sleep at the goblin at 26,12 (radius 1: the 3 × 3 squares around
    /// it, from the captured positions: list indexes 0, 2, 6, 7, 9–12, 14). DOSBox: 5 draws to the end of the cast
    /// (4d4 = 11, then the caster's elf resistance roll, 107: 1d100 = 17 ≤ 90, immune), every other target asleep
    /// (0x35, 5 minutes, param 1, no removal call), her memorised Sleep gone, her actions ended (state s04).
    /// </summary>
    public sealed class MovesFactAttribute : FactAttribute
    {
        public MovesFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Moves is null || !Directory.Exists(Moves))
                Skip = "needs the private DOSBox captures (fight_moves)";
        }
    }

    [MovesFact]
    public void Sleep_cast_matches_DOSBox()
    {
        var (list, seed) = LoadState("s02_sleep_target");
        var (after, _) = LoadState("s04_sleep_cast_2");
        using (var d3 = JsonDocument.Parse(File.ReadAllText(Path.Combine(Moves!, "s03_sleep_cast_1", "fight.json"))))
            _expectedSeed = d3.RootElement.GetProperty("vars").GetProperty("randseed").GetUInt32();
        var rng = new TurboPascalRandom(seed);
        int[] inside = [0, 2, 6, 7, 9, 10, 11, 12, 14]; // list indexes on x 25..27, y 11..13
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 2 } };
        memory.Party.Members.AddRange(list);
        var fx = new EffectHandlers(rng.Next);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(),
            new Area(inside.Select(i => list[i]).ToArray()), fx);
        var spells = new FightSpells(Table, fx, rng.Next);
        var caster = list[2];
        fight.Record(caster)[CombatRecord.CastAllowed] = 1;
        fight.Record(caster).InitiativeValue = 6;
        Assert.True(spells.Cast(fight, caster, 21, null, (26, 12)));
        Assert.Equal(_expectedSeed, rng.Seed);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal(after[i].Effects.Count, list[i].Effects.Count);
            for (int k = 0; k < after[i].Effects.Count; k++)
                Assert.Equal(after[i].Effects[k][..5], list[i].Effects[k][..5]);
            Assert.Equal(after[i].Record.AsSpan(0x17, 21).ToArray(), list[i].Record.AsSpan(0x17, 21).ToArray());
        }
        Assert.Equal(0, fight.Record(caster).InitiativeValue);
    }

    private static uint _expectedSeed;
}
