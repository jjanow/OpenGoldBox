using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// Handlers 74–77, 84 and 121 (docs/re/spells-effects.md "Handlers 73–139"), which need combat slots or the fight and
/// are not in the emulated fixture: hand-made fights on the player's combat tables.
/// </summary>
public class EffectHandlersHoldsTests
{
    private sealed class Script(params ushort[] values)
    {
        private int _i;
        public ushort Next(ushort sides) => _i < values.Length ? values[_i++] : (ushort)0;
    }

    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);

    private static EclCharacter Creature(byte hostile, byte hp = 20)
    {
        var c = new EclCharacter();
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = hostile;
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[FightRules.Size] = 1;
        for (int i = 0; i < 5; i++)
            c[EffectHandlers.Saves + i] = 15;
        return c;
    }

    /// <summary>Two creatures side by side on an open arena (slots 1 and 2), the engine attached.</summary>
    private static (Fight Fight, EffectHandlers Fx, EclCharacter A, EclCharacter B) Setup(Script s, int bx = 11)
    {
        var map = new CombatMap(Tables.Value);
        for (int y = 0; y < CombatArena.Height; y++)
            for (int x = 0; x < CombatArena.Width; x++)
                map.Arena[x, y] = 23;
        var a = Creature(1);
        var b = Creature(0);
        map.SetSlotCreature(1, a);
        map.SetSlotPosition(1, 10, 10);
        map.SetSlotSize(1, 1);
        map.SetSlotCreature(2, b);
        map.SetSlotPosition(2, bx, 10);
        map.SetSlotSize(2, 1);
        map.SlotCount = 3;
        map.Rebuild();
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange([b, a]);
        var fx = new EffectHandlers(s.Next);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), s.Next, new EndActionsController(), map, fx);
        fx.Attach(fight);
        fight.Record(a).Other = b;
        return (fight, fx, a, b);
    }

    [PorFact]
    public void Blood_drain_attaches_drains_1d4_each_action_and_lets_go_when_full()
    {
        // 76: attach (0x4B, 207 minutes = 12 drains' worth, param = slot 2) and hold the victim (0x3A, movement 0)
        var (fight, fx, a, b) = Setup(new Script(3, 3, 3));
        EffectHandlers.AddEffect(a, 76, 0, 0xFF, false);
        fx.Trigger(2, a, fight.State);
        var node = EffectHandlers.FindEffect(a, 0x4B)!;
        Assert.Equal(0xCF, node[1]);
        Assert.Equal(2, node[3]);
        Assert.NotNull(EffectHandlers.FindEffect(b, 0x3A));
        // 75 at its action start: 1d4 = 4 non-weapon damage, minutes − 4 × 16
        fx.Trigger(FightRules.TriggerActionStart, a, fight.State);
        Assert.Equal(16, b[FightRules.Hp]);
        Assert.Equal(0xCF - 0x40, node[1]);
        // 0x8F ÷ 16 = 8 > 4: again; then 0x4F ÷ 16 = 4, not above 4: the drain ends and the victim is let go
        fx.Trigger(FightRules.TriggerActionStart, a, fight.State);
        fx.Trigger(FightRules.TriggerActionStart, a, fight.State);
        Assert.Equal(8, b[FightRules.Hp]);
        Assert.Null(EffectHandlers.FindEffect(a, 0x4B));
        Assert.Null(EffectHandlers.FindEffect(b, 0x3A));
        Assert.Equal(1, fight.Record(a)[CombatRecord.Turned]);
    }

    [PorFact]
    public void A_hold_takes_only_a_victim_weaker_than_Strength_19()
    {
        var (fight, fx, a, b) = Setup(new Script());
        EffectHandlers.AddEffect(a, 77, 0, 0xFF, false);
        b[EffectHandlers.Str] = 19;
        fx.CallHandler(77, a, EffectHandlers.FindEffect(a, 77), false);
        Assert.Null(EffectHandlers.FindEffect(a, 0x4A));
        b[EffectHandlers.Str] = 18;
        fx.CallHandler(77, a, EffectHandlers.FindEffect(a, 77), false);
        Assert.Equal(2, EffectHandlers.FindEffect(a, 0x4A)![3]);
        Assert.NotNull(EffectHandlers.FindEffect(b, 0x3A));
        // the victim drops: the hold ends at the holder's next action start (removal call frees the victim)
        b[FightRules.InPlay] = 0;
        fx.Trigger(FightRules.TriggerActionStart, a, fight.State);
        Assert.Null(EffectHandlers.FindEffect(a, 0x4A));
        Assert.Null(EffectHandlers.FindEffect(b, 0x3A));
    }

    [PorFact]
    public void Acid_breath_fires_once_on_a_quarter_chance_within_4_squares()
    {
        // 1d100 = 26: nothing
        var (fight, fx, a, b) = Setup(new Script(25));
        EffectHandlers.AddEffect(a, 0x50, 0, 0xFF, false);
        EffectHandlers.AddEffect(a, 121, 0, 0xFF, false);
        fx.Trigger(0x0E, a, fight.State);
        Assert.NotNull(EffectHandlers.FindEffect(a, 121));
        // 1d100 = 25, 8d4 = 8 × 4 = 32, save 1d20 = 20 (made): halved to 16; record and the acid bite 0x50 go
        (fight, fx, a, b) = Setup(new Script(24, 3, 3, 3, 3, 3, 3, 3, 3, 19), 12);
        b[FightRules.Hp] = 40;
        EffectHandlers.AddEffect(a, 0x50, 0, 0xFF, false);
        EffectHandlers.AddEffect(a, 121, 0, 0xFF, false);
        fx.Trigger(0x0E, a, fight.State);
        Assert.Equal(24, b[FightRules.Hp]);
        Assert.Null(EffectHandlers.FindEffect(a, 121));
        Assert.Null(EffectHandlers.FindEffect(a, 0x50));
    }

    [PorFact]
    public void Charm_gaze_sets_spell_10_and_charms_a_target_that_fails_its_save()
    {
        // save 1d20 = 2 (fails with −2): charm 0x0B, param = hostile 1 × 128 + 12, and the charm handler takes it
        var (fight, fx, a, b) = Setup(new Script(1), 14);
        EffectHandlers.AddEffect(a, 84, 0, 0xFF, false);
        fx.Trigger(0x0E, a, fight.State);
        Assert.Equal(10, fx.Cells.Spell);
        var charm = EffectHandlers.FindEffect(b, 0x0B)!;
        Assert.Equal(0x8C + 0x20, charm[3]); // the charm handler's first call adds 0x20 + hostile × 64 (0 here)
        Assert.Equal(1, b[FightRules.Hostile]);
    }
}
