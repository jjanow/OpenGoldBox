using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

public class EffectHandlersMoreTests
{
    /// <summary>Random that returns the scripted values (0-based, as Random(n)) and records the sides asked.</summary>
    private sealed class Script(params ushort[] values)
    {
        private int _i;
        public List<ushort> Sides { get; } = [];
        public ushort Next(ushort sides)
        {
            Sides.Add(sides);
            return _i < values.Length ? values[_i++] : (ushort)0;
        }
    }

    private static (EffectHandlers H, FightState F) Make(Script s)
    {
        var h = new EffectHandlers(s.Next);
        return (h, new FightState());
    }

    [Fact]
    public void Regeneration_adds_three_up_to_the_maximum()
    {
        var (h, f) = Make(new Script());
        var c = new EclCharacter();
        c[0x32] = 20;
        c[0x11B] = 18;
        EffectHandlers.AddEffect(c, 98, 0, 0xFF, false);
        h.Trigger(19, c, f);
        Assert.Equal(20, c[0x11B]);
    }

    [Fact]
    public void Half_fire_halves_fire_damage_only()
    {
        var (h, f) = Make(new Script());
        var c = new EclCharacter();
        EffectHandlers.AddEffect(c, 93, 0, 0xFF, false);
        f.PendingDamage = 9;
        f.DamageFlags = 1;
        h.Trigger(6, c, f);
        Assert.Equal(4, f.PendingDamage);
        f.PendingDamage = 9;
        f.DamageFlags = 2;
        h.Trigger(6, c, f);
        Assert.Equal(9, f.PendingDamage);
    }

    [Fact]
    public void Sleep_immunity_clears_the_pending_effect_so_it_is_not_added()
    {
        var (h, f) = Make(new Script());
        var c = new EclCharacter();
        EffectHandlers.AddEffect(c, 108, 0, 0xFF, false);
        Assert.False(h.AddEffectWithMessage(c, 0x35, 5, 1, false, 1, false));
        Assert.Null(EffectHandlers.FindEffect(c, 0x35));
        Assert.True(h.AddEffectWithMessage(c, 0x34, 5, 1, false, 1, false));
    }

    [Fact]
    public void Elf_resistance_rolls_1d100_against_90()
    {
        var s = new Script(89, 90); // 90 (resists), then 91 (does not)
        var (h, _) = Make(s);
        var c = new EclCharacter();
        EffectHandlers.AddEffect(c, 107, 0, 0xFF, false);
        Assert.False(h.AddEffectWithMessage(c, 0x35, 5, 1, false, 1, false));
        Assert.True(h.AddEffectWithMessage(c, 0x35, 5, 1, false, 1, false));
        Assert.Equal(new ushort[] { 100, 100 }, s.Sides);
    }

    [Fact]
    public void Poison_immunity_sets_the_paralysis_save_to_100()
    {
        var (h, f) = Make(new Script(9)); // 1d20 = 10
        var c = new EclCharacter();
        c[0x6D] = 30;
        EffectHandlers.AddEffect(c, 111, 0, 0xFF, false);
        Assert.True(h.SavingThrow(c, 0, 0));
        Assert.Equal(100, h.Cells.SaveRoll);
    }

    [Fact]
    public void Constitution_save_bonus_by_band()
    {
        var (h, _) = Make(new Script(9));
        var c = new EclCharacter();
        c[0x14] = 16;
        c[0x6D + 4] = 14;
        EffectHandlers.AddEffect(c, 97, 0, 0xFF, false);
        Assert.True(h.SavingThrow(c, 4, 0)); // 10 + 4 = 14
        Assert.Equal(14, h.Cells.SaveRoll);
    }

    [Fact]
    public void Troll_burn_adds_0x66_for_3d6_unless_fire_or_acid()
    {
        var s = new Script(0, 1, 2);
        var (h, f) = Make(s);
        var c = new EclCharacter();
        EffectHandlers.AddEffect(c, 100, 0, 0xFF, false);
        f.DamageFlags = 1;
        h.Trigger(13, c, f);
        Assert.Null(EffectHandlers.FindEffect(c, 0x66));
        f.DamageFlags = 0;
        h.Trigger(13, c, f);
        var e = EffectHandlers.FindEffect(c, 0x66)!;
        Assert.Equal(6, e[1]);
        Assert.Equal(1, e[4]);
    }

    [Fact]
    public void Can_fight_on_revives_a_dying_creature_and_removes_itself()
    {
        var s = new Script(2); // 1d4 = 3
        var (h, f) = Make(s);
        h.CanRevive = _ => true;
        var c = new EclCharacter();
        c[0x10C] = 5;
        f.Record(c)[CombatRecord.Dying] = 2;
        EffectHandlers.AddEffect(c, 99, 0, 0xFF, true);
        h.Trigger(13, c, f);
        Assert.Equal(0, c[0x10C]);
        Assert.Equal(1, c[0x10D]);
        Assert.Equal(4, c[0x11B]);
        Assert.Null(EffectHandlers.FindEffect(c, 99));
        var e = EffectHandlers.FindEffect(c, 0x5F)!;
        Assert.Equal(4, e[1]);
    }

    [Fact]
    public void Item_code_grants_and_removes_its_effect()
    {
        var (h, _) = Make(new Script());
        var c = new EclCharacter();
        var item = new byte[63];
        item[0x3D] = 61;
        h.CallHandler(129, c, item, false);
        var e = EffectHandlers.FindEffect(c, 61)!;
        Assert.Equal(0x0C, e[3]);
        h.CallHandler(129, c, item, true);
        Assert.Null(EffectHandlers.FindEffect(c, 61));
    }

    [Fact]
    public void Gauntlets_raise_strength_to_18_00_and_restore_it()
    {
        var (h, _) = Make(new Script());
        var c = new EclCharacter();
        c[0x10] = 16;
        h.CallHandler(131, c, new byte[63], false);
        Assert.Equal(18, c[0x10]);
        Assert.Equal(100, c[0x16]);
        var e = EffectHandlers.FindEffect(c, 0x26)!;
        Assert.Equal(EffectHandlers.StrengthCode(16, 0), e[3]);
        h.CallHandler(131, c, new byte[63], true);
        Assert.Null(EffectHandlers.FindEffect(c, 0x26));
    }

    [Fact]
    public void Strength_codes_round_trip()
    {
        Assert.Equal((byte)18, EffectHandlers.StrengthFromCode(EffectHandlers.StrengthCode(18, 50)).Base);
        Assert.Equal((byte)50, EffectHandlers.StrengthFromCode(EffectHandlers.StrengthCode(18, 50)).Exc);
        Assert.Equal(((byte)15, (byte)0), EffectHandlers.StrengthFromCode(EffectHandlers.StrengthCode(15, 0)));
    }

    [Fact]
    public void Displacement_spoils_the_first_attack_of_the_fight()
    {
        var (h, f) = Make(new Script());
        var c = new EclCharacter();
        var e = EffectHandlers.AddEffect(c, 89, 0, 0x1F, false);
        h.Trigger(8, c, f); // fight start: round 0, roll 0
        Assert.Equal(0x0F, e[3]);
        f.AttackRoll = 15;
        h.Trigger(16, c, f);
        Assert.Equal(-1, f.AttackRoll);
        Assert.Equal(0x1F, e[3]);
        f.AttackRoll = 15;
        h.Trigger(16, c, f);
        Assert.Equal(15, f.AttackRoll);
    }

    [Fact]
    public void Handler_88_without_the_fight_s_spells_reports_a_diagnostic_and_still_counts_down()
    {
        // The entry DS:6988 is written by the spell table builder (0x2A7B9); the breath itself needs FightSpells.
        var (h, f) = Make(new Script());
        var c = new EclCharacter();
        EffectHandlers.AddEffect(c, 88, 0, 0xFF, false);
        h.Trigger(14, c, f);
        Assert.Contains(h.Diagnostics, d => d.Contains("88"));
        Assert.Equal(0xFE, EffectHandlers.FindEffect(c, 88)![3]);
    }
}
