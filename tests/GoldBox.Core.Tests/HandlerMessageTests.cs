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
        Assert.Equal(["Gazes A", "fly 10>13", "Stoned B", "StatusDone B"], r.Log);
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
        Assert.Equal(["Gazes A", "fly 10>13", "GazeReflected B", "fly 13>10", "Stoned A", "StatusDone A"], r.Log);
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

    // ---- 0xECB3's callers (s29, read: each passes its own text; the end only when the status is set) ---------------

    [PorFact]
    public void A_poisoned_hit_shows_the_poison_then_the_death_and_its_end()
    {
        // Handler 64 (0x102B8 → 0x101A0): save vs. 0 fails (1d20 = 1): the poisoned message (0x101F2), 0x37 added,
        // then 0xECB3 with its own text (0x10230) and, the status set, its end.
        var r = Setup(new Script(0), 0x40, 11);
        r.Fx.TriggerEffect(r.A, 0x40);
        Assert.Equal(["Poisoned B", "PoisonKilled B", "StatusDone B"], r.Log);
        Assert.Equal((6, 0, 0), (r.B[FightRules.Status], r.B[FightRules.InPlay], r.B[FightRules.Hp]));
        Assert.NotNull(EffectHandlers.FindEffect(r.B, 0x37));

        r = Setup(new Script(19), 0x40, 11); // saved: nothing
        r.Fx.TriggerEffect(r.A, 0x40);
        Assert.Empty(r.Log);
    }

    [PorFact]
    public void Each_caller_of_0xECB3_raises_its_own_message()
    {
        // Handler 22 (0xF406): poisoned when it runs.
        var r = Setup(new Script(), 0x16, 11);
        EffectHandlers.AddEffect(r.A, 0x37, 0, 0xFF, false);
        r.Fx.TriggerEffect(r.A, 0x16);
        Assert.Equal(["PoisonDeath A", "StatusDone A"], r.Log);
        Assert.Equal(6, r.A[FightRules.Status]);

        // Handler 57 (0xFFF4): the rot message (pause flag), then at the last stage (param >> 4 ≤ 1) 0xECB3 status 8.
        r = Setup(new Script(), 0x39, 11);
        EffectHandlers.FindEffect(r.A, 0x39)![3] = 0x10;
        r.Fx.TriggerEffect(r.A, 0x39);
        Assert.Equal(["Rots A", "RotsAway A", "StatusDone A"], r.Log);
        Assert.Equal(8, r.A[FightRules.Status]);

        // Handler 95 (0x1126B): a creature in play falls dead.
        r = Setup(new Script(), 0x5F, 11);
        r.Fx.TriggerEffect(r.A, 0x5F);
        Assert.Equal(["FallsDead A", "StatusDone A"], r.Log);

        // Handler 86 (0x1101D → 0x10D7D, count 2): the drained message, then an animated target is gone (0x10E15).
        r = Setup(new Script(), 0x56, 11);
        r.B[FightRules.Status] = 1;
        r.Fx.TriggerEffect(r.A, 0x56);
        Assert.Equal(["Drained B", "DeadForever B", "StatusDone B"], r.Log);
        Assert.Equal(2, r.Fx.DrainCount);
        Assert.Equal((8, 0), (r.B[FightRules.Status], r.B[FightRules.HpMax]));
    }

    [PorFact]
    public void A_creature_already_dead_gets_the_message_and_nothing_more()
    {
        // 0xECDD shows the text before the dead / stone / gone test (0xECE2–0xECF6), so the end does not follow.
        var r = Setup(new Script(), 0x5F, 11);
        r.A[FightRules.Status] = 6;
        r.Fx.TriggerEffect(r.A, 0x5F);
        Assert.Equal(["FallsDead A"], r.Log);
    }

    [PorFact]
    public void An_animated_creature_downed_by_spell_damage_collapses_between_the_down_words_and_their_end()
    {
        // 0x2C44A: the down words, then 0x2BEF4 removes Animate Dead's 0x20 whose handler 32 calls 0xECB3 (0xF7A4), then
        // the end (CreatureDowned or the pause, START 0x17E3). The animated creature is dead already (START 0x2DC8), so
        // 0xECB3 stops after its text.
        var r = Setup(new Script(), 0x20, 11);
        r.B[FightRules.Status] = 1;
        EffectHandlers.AddEffect(r.B, 0x20, 0, 0x00, true);
        r.Log.Clear();
        r.Fx.NonWeaponDamage(r.B, 20, 0, false);
        Assert.Equal(["NonWeaponDamage B", "GoesDown B", "Collapses B", "GoesDownDone B"], r.Log);

        // More damage gives the same: START 0x2DC8 makes an animated creature dead whenever nothing is left (over > 0
        // leaves remain 0), so handler 32's 0xECB3 never gets past its text on these paths.
        r = Setup(new Script(), 0x20, 11);
        r.B[FightRules.Status] = 1;
        EffectHandlers.AddEffect(r.B, 0x20, 0, 0x00, true);
        r.Log.Clear();
        r.Fx.NonWeaponDamage(r.B, 23, 0, false);
        Assert.Equal(["NonWeaponDamage B", "GoesDown B", "Collapses B", "GoesDownDone B"], r.Log);
        Assert.Equal(6, r.B[FightRules.Status]);
    }

    [PorFact]
    public void An_attack_shows_its_message_before_the_drop_and_ends_after_it()
    {
        // 0x12AF4: the attack and down / killed words, then 0x2BEF4 and trigger 0x0D (here handler 32's text through
        // 0xECB3), then CreatureDowned and START 0x17E3 (0x12DE1–0x12E17). A helpless animated defender is slain
        // (HP + 5: animated with 0 left → dead).
        var r = Setup(new Script(), 0x20, 11);
        r.A[0x113] = 1;
        r.B[FightRules.Status] = 1;
        EffectHandlers.AddEffect(r.B, 0x20, 0, 0x00, true);
        EffectHandlers.AddEffect(r.B, FightRules.HelplessEffects[0], 0, 0xFF, false);
        r.Log.Clear();
        r.Fight.State.AttackMessage = m => r.Log.Add($"attack {m.Manner} {m.Defender[FightRules.Status]}");
        r.Fight.State.AttackEnd = (m, down) => r.Log.Add("end " + down);
        r.Fight.MakeAttack(r.A, r.B);
        Assert.Equal(["attack 3 6", "Collapses B", "end True"], r.Log);
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
