using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>Tests that set the static <see cref="AmigaView.On"/>: run alone, after the parallel ones.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AmigaViewStatic
{
    public const string Name = "AmigaView.On set";
}

/// <summary>
/// Decided (user, s48): the Amiga program's empty rake / hold handlers (73 rake, 74 the hold's attack, and with them
/// 77 the hold; por-art-formats.md "Fight texts") are not followed: DOS rules under every art setting. The handlers are
/// Core rules with no art input; this pins that a fight under the Amiga pages (<see cref="AmigaView.On"/>) rakes and
/// holds exactly as under DOS.
/// </summary>
[Collection(AmigaViewStatic.Name)]
public class AmigaHandlerRulesTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private static EclCharacter Creature(byte hostile)
    {
        var c = new EclCharacter();
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = hostile;
        c[FightRules.Hp] = 40;
        c[FightRules.HpMax] = 40;
        c[FightRules.Size] = 1;
        for (int i = 0; i < 5; i++)
            c[EffectHandlers.Saves + i] = 15;
        return c;
    }

    /// <summary>The creature a (slot 1) beside b (slot 2) on an open arena; every die roll 1.</summary>
    private static (Fight Fight, EffectHandlers Fx, EclCharacter A, EclCharacter B) Setup(CombatTables tables)
    {
        var map = new CombatMap(tables);
        for (int y = 0; y < CombatArena.Height; y++)
            for (int x = 0; x < CombatArena.Width; x++)
                map.Arena[x, y] = 23;
        var a = Creature(1);
        var b = Creature(0);
        map.SetSlotCreature(1, a);
        map.SetSlotPosition(1, 10, 10);
        map.SetSlotSize(1, 1);
        map.SetSlotCreature(2, b);
        map.SetSlotPosition(2, 11, 10);
        map.SetSlotSize(2, 1);
        map.SlotCount = 3;
        map.Rebuild();
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange([b, a]);
        var fx = new EffectHandlers(_ => 0);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), _ => 0, new EndActionsController(), map, fx);
        fx.Attach(fight);
        fight.Record(a).Other = b;
        return (fight, fx, a, b);
    }

    /// <summary>What handlers 77, 74 and 73 leave, as bytes to compare between the arts.</summary>
    private static List<int> Run(CombatTables tables)
    {
        var seen = new List<int>();
        // 77: a victim with Strength 18 is held (0x4A on the holder with the victim's slot, 0x3A on the victim).
        var (fight, fx, a, b) = Setup(tables);
        b[EffectHandlers.Str] = 18;
        EffectHandlers.AddEffect(a, 77, 0, 0xFF, false);
        fx.CallHandler(77, a, EffectHandlers.FindEffect(a, 77), false);
        var hold = EffectHandlers.FindEffect(a, 0x4A);
        Assert.NotNull(hold);
        Assert.NotNull(EffectHandlers.FindEffect(b, 0x3A));
        seen.Add(hold![3]);
        // 74 (the hold's attack, from 0x4A): one 3d4 attack in phase 1, the holder's actions end.
        fx.CallHandler(74, a, hold, false);
        Assert.Equal(3, a[0x115]);
        Assert.Equal(4, a[0x117]);
        seen.AddRange([a[0x113], a[0x114], a[0x115], a[0x117], b[FightRules.Hp]]);
        // 73 (rake): two hits in phase 1 and the target in play → two 2d4 attacks now.
        (fight, fx, a, b) = Setup(tables);
        fight.State.HitsMade[1] = 2;
        EffectHandlers.AddEffect(a, 73, 0, 0xFF, false);
        var events = new List<EffectEvent>();
        fx.Event += (_, e) => events.Add(e);
        fx.CallHandler(73, a, EffectHandlers.FindEffect(a, 73), false);
        Assert.Contains(EffectEvent.Rakes, events);       // the handler ran (an empty one says nothing)
        Assert.Equal(2, a[0x116]);                        // 2d4: two dice …
        Assert.Equal(4, a[0x118]);                        // … of 4 sides
        seen.AddRange([a[0x113], a[0x114], a[0x116], a[0x118], b[FightRules.Hp], events.Count]);
        return seen;
    }

    [Fact]
    public void Rake_and_hold_follow_the_DOS_rules_under_the_Amiga_art()
    {
        if (Dir() is not { } dir)
            return;
        var tables = PorCombatTables.Open(new PorGameDirectory(dir))!;
        bool was = AmigaView.On;
        try
        {
            AmigaView.On = false;
            var dos = Run(tables);
            AmigaView.On = true;
            var amiga = Run(tables);
            Assert.Equal(dos, amiga);
        }
        finally
        {
            AmigaView.On = was;
        }
    }
}
