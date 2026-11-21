using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Sound;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The moving icon's callers (START 0x25C9, docs/re/combat-menu.md "Moving icon") beyond the missile and the casts: the
/// gazes (0x10987, 0x10A59, 0x10BC6), the acid breath (0x11930), the lightning breath of handler 88 (0x2A1E4), the
/// aim's Next / Prev (0x15D02) and the second missile flight of a type-0x2F weapon (0x1417E). Hand-made fights on the
/// player's combat tables; <see cref="FightState.Flight"/> and the sound sink record what happens, in order.
/// </summary>
public class MovingIconCallerTests
{
    private sealed class Script(params ushort[] values)
    {
        private int _i;
        public ushort Next(ushort sides) => _i < values.Length ? values[_i++] : (ushort)0;
    }

    private sealed class Log : ISoundSink
    {
        public List<string> Events { get; } = [];
        public List<FlightAnimation> Flights { get; } = [];
        public void Cue(SoundCue cue) => Events.Add("cue " + cue);
        public void BeginResident() => Events.Add("begin");
        public void EndResident() => Events.Add("end");
        public void Fly(FlightAnimation a)
        {
            Flights.Add(a);
            Events.Add($"fly {a.FromX},{a.FromY}>{a.ToX},{a.ToY}");
        }
    }

    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);
    private static SpellTable? _spells;
    private static SpellTable Spells => _spells ??= PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!;

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

    private sealed record Rig(Fight Fight, EffectHandlers Fx, CombatMap Map, Log Log, EclCharacter[] C);

    /// <summary>Creatures at the given squares on an open arena (slots 1..n), the engine attached; the first is
    /// hostile, the others not; the first one's last target (+0x0A) is the second.</summary>
    private static Rig Setup(Script s, BaseItemTable? items, params (int X, int Y)[] at)
    {
        var map = new CombatMap(Tables.Value);
        for (int y = 0; y < CombatArena.Height; y++)
            for (int x = 0; x < CombatArena.Width; x++)
                map.Arena[x, y] = 23;
        var list = new EclCharacter[at.Length];
        for (int i = 0; i < at.Length; i++)
        {
            list[i] = Creature(i == 0 ? (byte)1 : (byte)0);
            map.SetSlotCreature(i + 1, list[i]);
            map.SetSlotPosition(i + 1, at[i].X, at[i].Y);
            map.SetSlotSize(i + 1, 1);
        }
        map.SlotCount = at.Length + 1;
        map.Rebuild();
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange(list.Skip(1));
        memory.Party.Members.Add(list[0]);
        var fx = new EffectHandlers(s.Next);
        var fight = new Fight(memory, new EncounterState(), items ?? new BaseItemTable([]), s.Next, new EndActionsController(), map, fx);
        fx.Attach(fight);
        var log = new Log();
        fight.State.Sound = log;
        fight.State.Flight = log.Fly;
        if (list.Length > 1)
            fight.Record(list[0]).Other = list[1];
        return new Rig(fight, fx, map, log, list);
    }

    private static Rig Setup(Script s, params (int X, int Y)[] at) => Setup(s, null, at);

    // ---- The fight start's trigger-8 effects left to the run (s27) ---------------------------------------------------

    [PorFact]
    public void A_deferred_fight_start_runs_once_at_the_start_of_Run()
    {
        // FightKit.Create(deferStartEffects: true) leaves the trigger-8 loop to Fight.Run, so a display attached in
        // between shows the aura messages on the combat screen (DOSBox-X s27: Tyranithraxus's fear aura).
        var r = Setup(new Script(), (10, 10), (12, 10));
        int ran = 0;
        r.Fight.PendingStart = () => ran++;
        r.Fight.Run(0);
        Assert.Equal(1, ran);
        Assert.Null(r.Fight.PendingStart);
        r.Fight.Run(0);
        Assert.Equal(1, ran);
    }

    // ---- Gazes (handlers 83, 84) -----------------------------------------------------------------------------------

    [PorFact]
    public void Gaze_flies_to_the_target_and_back_when_a_mirror_reflects_it()
    {
        // 83 without 0x7F: one flight, gazer → target, 0x257E(cell 0x12) = COMSPR 5's four frames, 45 ms.
        var r = Setup(new Script(19), (10, 10), (13, 10));
        EffectHandlers.AddEffect(r.C[0], 83, 0, 0xFF, false);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        var f = Assert.Single(r.Log.Flights);
        Assert.Equal((10, 10, 13, 10, 45), (f.FromX, f.FromY, f.ToX, f.ToY, f.DelayMs));
        Assert.Equal(FlightAnimation.Burst(FlightAnimation.SpellBlock), f.Frames);

        // With 0x7F and the target's readied item named 0x76: the gaze comes back, target → gazer (0x10A59).
        r = Setup(new Script(19), (10, 10), (13, 10));
        EffectHandlers.AddEffect(r.C[0], 83, 0, 0xFF, false);
        EffectHandlers.AddEffect(r.C[0], 0x7F, 0, 0xFF, false);
        var mirror = new byte[EclCharacter.ItemRecordSize];
        mirror[0x34] = 1;
        mirror[0x30] = 0x76;
        r.C[1].Items.Add(mirror);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        Assert.Equal(["fly 10,10>13,10", "fly 13,10>10,10"], r.Log.Events);
        Assert.All(r.Log.Flights, a => Assert.Equal(45, a.DelayMs));
    }

    [PorFact]
    public void Charm_gaze_flies_once_before_the_charm_and_plays_no_sound()
    {
        var r = Setup(new Script(1), (10, 10), (14, 10));
        EffectHandlers.AddEffect(r.C[0], 84, 0, 0xFF, false);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        Assert.Equal(["fly 10,10>14,10"], r.Log.Events); // no resident bracket around it
        var f = r.Log.Flights[0];
        Assert.Equal((45, 4), (f.DelayMs, f.Frames.Count));
        Assert.NotNull(EffectHandlers.FindEffect(r.C[1], 0x0B));
    }

    // ---- Acid breath (handler 121) ----------------------------------------------------------------------------------

    [PorFact]
    public void Acid_breath_flies_only_when_it_breathes_with_one_frame_of_COMSPR_10()
    {
        var r = Setup(new Script(25), (10, 10), (12, 10)); // 1d100 = 26: nothing
        EffectHandlers.AddEffect(r.C[0], 121, 0, 0xFF, false);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        Assert.Empty(r.Log.Flights);

        r = Setup(new Script(24, 3, 3, 3, 3, 3, 3, 3, 3, 19), (10, 10), (12, 10)); // 1d100 = 25: it breathes
        EffectHandlers.AddEffect(r.C[0], 121, 0, 0xFF, false);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        var f = Assert.Single(r.Log.Flights);
        Assert.Equal((10, 10, 12, 10, 30), (f.FromX, f.FromY, f.ToX, f.ToY, f.DelayMs));
        Assert.Equal([new FlightFrame(FlightAnimation.AcidBlock, false, false)], f.Frames);
    }

    // ---- Lightning breath (handler 88, 0x2A0F1) ---------------------------------------------------------------------

    private static Rig BreathSetup(Script s)
    {
        var r = Setup(s, (10, 10), (14, 10));
        var spells = new FightSpells(Spells, r.Fx, s.Next);
        r.Fight.Spells = spells;
        r.Fight.State.Current = r.C[0];
        r.C[0][FightRules.HpMax] = 30; // byte 0x32: the breath's damage
        r.C[1][FightRules.Hp] = 60;
        EffectHandlers.AddEffect(r.C[0], 0x58, 0, 0xFF, false);
        r.Fight.Record(r.C[0]).InitiativeValue = 5;
        return r;
    }

    [PorFact]
    public void Lightning_breath_aims_two_squares_east_flies_there_and_bolts_three_times()
    {
        var r = BreathSetup(new Script());
        Assert.DoesNotContain((byte)88, EffectHandlers.UnmodelledHandlers);
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        Assert.DoesNotContain(r.Fx.Diagnostics, d => d.Contains("88"));
        // The breath's own flight: from the breather to its square + 1 east, + 1 more (the aim was east of it).
        var first = r.Log.Flights[0];
        Assert.Equal((10, 10, 12, 10, 50), (first.FromX, first.FromY, first.ToX, first.ToY, first.DelayMs));
        Assert.Equal(FlightAnimation.Burst(FlightAnimation.BoltBlock), first.Frames);
        // Then the bolt from (12, 10) onwards (0x29973): its first flight starts there, each later one on the cell the
        // previous one hit ([bp−0x37] / [bp−0x38] set again at 0x29A68; DOSBox-X s27), and one reaches the target.
        Assert.True(r.Log.Flights.Count > 2);
        Assert.Equal((12, 10), (r.Log.Flights[1].FromX, r.Log.Flights[1].FromY));
        for (int i = 2; i < r.Log.Flights.Count; i++)
            Assert.Equal((r.Log.Flights[i - 1].ToX, r.Log.Flights[i - 1].ToY), (r.Log.Flights[i].FromX, r.Log.Flights[i].FromY));
        Assert.Contains(r.Log.Flights, a => (a.ToX, a.ToY) == (14, 10));
        Assert.True(r.C[1][FightRules.Hp] < 60);
        Assert.Equal(0xFE, EffectHandlers.FindEffect(r.C[0], 0x58)![3]);
        Assert.Equal(0, r.Fight.Record(r.C[0]).InitiativeValue); // actions ended (START 0x2637)
    }

    [PorFact]
    public void Lightning_breath_counts_down_from_0xFF_and_goes_after_its_third_use()
    {
        var r = BreathSetup(new Script());
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(EffectHandlers.FindEffect(r.C[0], 0x58));
            r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        }
        Assert.Null(EffectHandlers.FindEffect(r.C[0], 0x58));
    }

    [PorFact]
    public void Lightning_breath_after_the_first_round_is_skipped_on_1d100_up_to_50()
    {
        var r = BreathSetup(new Script(49)); // 1d100 = 50: no breath
        r.Fight.State.RoundCounter = 1;
        r.Fx.Trigger(0x0E, r.C[0], r.Fight.State);
        Assert.Empty(r.Log.Flights);
        Assert.Equal(0xFF, EffectHandlers.FindEffect(r.C[0], 0x58)![3]);
        Assert.Equal(5, r.Fight.Record(r.C[0]).InitiativeValue);
    }

    // ---- Aim's Next / Prev (0x15B84, 0x15D02) ------------------------------------------------------------------------

    private sealed class Keys(params AimKey[] keys) : IFightMenuChooser
    {
        private readonly Queue<AimKey> _keys = new(keys);
        public List<EclCharacter> Shown { get; } = [];
        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) => FightCommand.Done;
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => DoneCommand.Exit;
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => MoveInput.Escape;
        public AimKey ChooseAim(FightMenu menu, AimView view)
        {
            Shown.Add(view.Candidate);
            return _keys.Count > 0 ? _keys.Dequeue() : AimKey.Exit;
        }
        public bool ConfirmFlee(FightMenu menu) => false;
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => false;
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => null;
        public void Notice(FightNotice notice, EclCharacter? who) { }
    }

    [PorFact]
    public void Aim_Next_and_Prev_fly_the_cursor_box_to_the_next_entry_with_no_wait()
    {
        var r = Setup(new Script(), (10, 10), (11, 10));
        var me = r.C[0];
        var keys = new Keys(AimKey.Next, AimKey.Prev, AimKey.Exit);
        new FightMenu(r.Fight, me, keys).Aim();
        Assert.Equal([me, r.C[1], me], keys.Shown);
        Assert.Equal(["fly 10,10>11,10", "fly 11,10>10,10"], r.Log.Events);
        Assert.All(r.Log.Flights, a =>
        {
            Assert.Equal(0, a.DelayMs);
            Assert.Equal([new FlightFrame(FlightAnimation.CursorBlock, false, false)], a.Frames);
        });
    }

    [PorFact]
    public void Aim_flies_to_the_raw_next_entry_and_a_skipped_candidate_flies_nothing_more()
    {
        // The middle entry cannot be targeted (effect 71 on it, trigger 1): Next flies to its square, then the skip to
        // the one after it has the fly flag clear (0x15E4D).
        var r = Setup(new Script(), (10, 10), (11, 10), (12, 10));
        var me = r.C[0];
        EffectHandlers.AddEffect(r.C[1], 71, 0, 0xFF, false);
        var list = FightSearch.Nearby(r.Fight, me, 0x7F);
        Assert.Equal([me, r.C[1], r.C[2]], list);
        var keys = new Keys(AimKey.Next, AimKey.Exit);
        new FightMenu(r.Fight, me, keys).Aim();
        Assert.Equal([me, r.C[2]], keys.Shown);
        Assert.Equal(["fly 10,10>11,10"], r.Log.Events);
    }

    // ---- A weapon of type 0x2F: the second missile flight (0x1417E) --------------------------------------------------

    private static (Rig R, byte[] Weapon) WeaponSetup()
    {
        var r = Setup(new Script(), new BaseItemTable([]), (10, 10), (11, 10));
        var weapon = new byte[EclCharacter.ItemRecordSize];
        weapon[0x2E] = 0x2F;
        weapon[CombatRules.ItemEquipped] = 1;
        r.C[0].Items.Add(weapon);
        r.C[0][0x113] = 0; // no attacks left: MakeAttack rolls nothing (the flights come before that test)
        r.C[0][0x114] = 0;
        return (r, weapon);
    }

    [PorFact]
    public void A_type_0x2F_weapon_flies_itself_in_a_melee_attack_in_its_own_sound_bracket()
    {
        var (r, _) = WeaponSetup();
        r.Fight.MakeAttack(r.C[0], r.C[1]);
        Assert.Equal(["begin", "cue MissileOut", "fly 10,10>11,10", "cue MissileIn", "end"], r.Log.Events);
        Assert.Equal([new FlightFrame(8, false, false), new FlightFrame(8, true, true)], r.Log.Flights[0].Frames);
        Assert.Equal(20, r.Log.Flights[0].DelayMs);
    }

    [PorFact]
    public void A_type_0x2F_weapon_adds_a_second_flight_after_the_missile()
    {
        var (r, _) = WeaponSetup();
        var arrow = new byte[EclCharacter.ItemRecordSize];
        arrow[0x2E] = 0x49;
        r.Fight.MakeAttack(r.C[0], r.C[1], false, arrow);
        Assert.Equal(2, r.Log.Flights.Count);
        Assert.Equal(10, r.Log.Flights[0].DelayMs); // the arrow
        Assert.Equal(20, r.Log.Flights[1].DelayMs); // the weapon (COMSPR 8)
        Assert.Equal(2, r.Log.Events.Count(e => e == "begin"));
    }

    // ---- A cast's flight bracket (0x27E05–0x27E56; DOSBox-X s43) -------------------------------------------------------

    private sealed class EmptyLog : ISoundSink
    {
        public List<string> Events { get; } = [];
        public void Cue(SoundCue cue) => Events.Add("cue " + cue);
        public void BeginResident() => Events.Add("begin");
        public void EndResident() => Events.Add("end");
        public void EmptyResident(SoundCue cue) => Events.Add("empty " + cue);
    }

    private static List<string> Cast(bool atSelf, out Rig rig)
    {
        var s = new Script();
        var r = Setup(s, (10, 10), (13, 10));
        Assert.True(r.Fx.InCombat);
        var sound = new EmptyLog();
        r.Fight.State.Sound = sound;
        var spells = new FightSpells(Spells, r.Fx, s.Next) { Aim = atSelf ? (10, 10) : (13, 10) };
        spells.CastNow(r.Fight, r.C[0], 47, () => true);
        rig = r;
        return sound.Events;
    }

    [PorFact]
    public void A_cast_aimed_at_its_own_square_has_an_empty_bracket_and_flies_nothing()
    {
        // The moving icon draws nothing for a path under two entries (START 0x2688), so the timer is uninstalled
        // before its first tick: DOSBox-X plays no cast tune for a Fireball aimed at the caster (s41's silent casts).
        var events = Cast(true, out var r);
        Assert.Equal("empty Fireball", events[0]);
        Assert.DoesNotContain("begin", events.Take(1));
        Assert.Empty(r.Log.Flights);
        // A sink without its own EmptyResident gets the program's three calls.
        var log = new SoundLog();
        ((ISoundSink)log).EmptyResident(SoundCue.Fireball);
        Assert.Equal(["(", "Fireball", ")"], log.Calls);
    }

    [PorFact]
    public void A_cast_aimed_elsewhere_plays_its_cue_through_the_flight()
    {
        var events = Cast(false, out var r);
        Assert.Equal(["begin", "cue Fireball", "end"], events.Take(3));
        Assert.Single(r.Log.Flights);
        Assert.Equal((10, 10, 13, 10), (r.Log.Flights[0].FromX, r.Log.Flights[0].FromY, r.Log.Flights[0].ToX, r.Log.Flights[0].ToY));
    }

    [PorFact]
    public void Another_weapon_in_melee_flies_nothing()
    {
        var (r, weapon) = WeaponSetup();
        weapon[0x2E] = 1;
        r.Fight.MakeAttack(r.C[0], r.C[1]);
        Assert.Empty(r.Log.Events);
    }
}
