using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The monster specials' own messages (class 0x0E handlers, docs/re/spells-effects.md "Handler messages", read s27):
/// each handler raises its <see cref="EffectEvent"/> where the program calls START 0x16EC, so the event comes before or
/// after the moving icon as in the program. Hand-made fights on the player's combat tables; the effect events and the
/// flights are recorded in one list.
/// </summary>
public class HandlerMessageTests
{
    private sealed class Script(params ushort[] values)
    {
        private int _i;
        public ushort Next(ushort sides) => _i < values.Length ? values[_i++] : (ushort)0;
    }

    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);
    private static SpellTable? _spells;
    private static SpellTable Spells => _spells ??= PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!;

    private sealed record Rig(Fight Fight, EffectHandlers Fx, EclCharacter A, EclCharacter B, List<string> Log);

    /// <summary>A (hostile, at (10, 10)) with the handler, its last target B (at (bx, 10)); events and flights logged.</summary>
    private static Rig Setup(Script s, byte handler, int bx)
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
        memory.Party.Members.Add(b);
        memory.Party.Members.Add(a);
        var fx = new EffectHandlers(s.Next);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), s.Next, new EndActionsController(), map, fx);
        fx.Attach(fight);
        var log = new List<string>();
        string Name(EclCharacter c) => ReferenceEquals(c, a) ? "A" : ReferenceEquals(c, b) ? "B" : "?";
        fx.Event += (c, e) => log.Add(e + " " + Name(c));
        fight.State.Flight = f => log.Add($"fly {f.FromX}>{f.ToX}");
        fight.Record(a).Other = b;
        EffectHandlers.AddEffect(a, handler, 0, 0xFF, false);
        return new Rig(fight, fx, a, b, log);
    }

    private static EclCharacter Creature(byte hostile)
    {
        var c = new EclCharacter();
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = hostile;
        c[FightRules.Hp] = 20;
        c[FightRules.HpMax] = 20;
        c[FightRules.Size] = 1;
        for (int i = 0; i < 5; i++)
            c[EffectHandlers.Saves + i] = 15;
        return c;
    }

    [PorFact]
    public void Gaze_names_the_gazer_before_its_flight_and_the_stoning_names_the_victim()
    {
        var r = Setup(new Script(0), 83, 13); // save 1d20 = 1: fails
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal(["Gazes A", "fly 10>13", "Stoned B"], r.Log);
        Assert.Equal(7, r.B[FightRules.Status]);

        r = Setup(new Script(19), 83, 13); // 20: saved, no stoning message
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal(["Gazes A", "fly 10>13"], r.Log);
    }

    [PorFact]
    public void A_reflected_gaze_names_the_mirror_carrier_then_stones_the_gazer()
    {
        var r = Setup(new Script(0), 83, 13);
        EffectHandlers.AddEffect(r.A, 0x7F, 0, 0xFF, false);
        var mirror = new byte[EclCharacter.ItemRecordSize];
        mirror[0x34] = 1;
        mirror[0x31] = 0x76;
        r.B.Items.Add(mirror);
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal(["Gazes A", "fly 10>13", "GazeReflected B", "fly 13>10", "Stoned A"], r.Log);
    }

    [PorFact]
    public void Charm_gaze_names_the_gazer_before_its_flight_and_the_charm_message()
    {
        var r = Setup(new Script(1), 84, 14);
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal(["CharmGazes A", "fly 10>14", "Charmed B"], r.Log);
    }

    [PorFact]
    public void Acid_breath_names_the_breather_only_when_it_breathes()
    {
        var r = Setup(new Script(25), 121, 12); // 1d100 = 26: nothing
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Empty(r.Log);

        r = Setup(new Script(24, 3, 3, 3, 3, 3, 3, 3, 3, 19), 121, 12); // 25: it breathes
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal(["SpitsAcid A", "fly 10>12"], r.Log.Take(2));
        Assert.Contains("NonWeaponDamage B", r.Log);
    }

    [PorFact]
    public void Lightning_breath_names_the_breather_before_the_pick_and_the_flight()
    {
        var r = Setup(new Script(), 0x58, 14);
        r.Fight.Spells = new FightSpells(Spells, r.Fx, new Script().Next);
        r.Fight.State.Current = r.A;
        r.Fight.Record(r.A).InitiativeValue = 5;
        r.Fx.Trigger(0x0E, r.A, r.Fight.State);
        Assert.Equal("Breathes A", r.Log[0]);
        Assert.StartsWith("fly ", r.Log[1]);
        Assert.Single(r.Log, e => e == "Breathes A");
    }
}
