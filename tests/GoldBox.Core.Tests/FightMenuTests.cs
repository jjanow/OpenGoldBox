using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The player's combat menu, sweep attacks and turn undead (docs/re/combat-menu.md), on test doubles.</summary>
public class FightMenuTests
{
    // ---- Test doubles ----------------------------------------------------------------------------------------------

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

    /// <summary>A 10×10 map: Chebyshev distance, 8 directions; off the map outside it.</summary>
    private sealed class TestMap : IFightMap
    {
        private readonly Dictionary<EclCharacter, (int X, int Y)> _at = new(ReferenceEqualityComparer.Instance);
        public List<EclCharacter> Removed { get; } = [];
        public void Put(EclCharacter c, int x, int y) => _at[c] = (x, y);
        public (int X, int Y) Position(EclCharacter c) => _at[c];
        public EclCharacter? At(int x, int y) => _at.FirstOrDefault(kv => kv.Value == (x, y)).Key;
        public bool OnMap(EclCharacter c) => _at.ContainsKey(c);
        public bool IsOffMap(int x, int y) => x is < 0 or >= 10 || y is < 0 or >= 10;
        public bool Place(EclCharacter c, int x, int y)
        {
            if (At(x, y) is { } o && !ReferenceEquals(o, c))
                return false;
            _at[c] = (x, y);
            return true;
        }
        public void Remove(EclCharacter c)
        {
            _at.Remove(c);
            Removed.Add(c);
        }
        public int DirectionToward(int fx, int fy, int tx, int ty) => (Math.Sign(tx - fx), Math.Sign(ty - fy)) switch
        {
            (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7,
            _ => 8,
        };
        public int Distance(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
        public int DirectionToward(EclCharacter from, EclCharacter to)
        {
            var (a, b) = (Position(from), Position(to));
            return DirectionToward(a.X, a.Y, b.X, b.Y);
        }
        public int Distance(EclCharacter a, EclCharacter b)
        {
            var (p, q) = (Position(a), Position(b));
            return Distance(p.X, p.Y, q.X, q.Y);
        }
    }

    /// <summary>Steps cost 2 half-squares; off the map = fled; a creature in the way = blocked.</summary>
    private sealed class TestMoves(TestMap map) : IFightMoves
    {
        public List<int> Steps { get; } = [];
        public StepResult Step(Fight fight, EclCharacter c, int direction)
        {
            Steps.Add(direction);
            var (x, y) = map.Position(c);
            var (dx, dy) = FightMenu.Steps[direction];
            if (map.IsOffMap(x + dx, y + dy))
            {
                map.Remove(c);
                return StepResult.Fled;
            }
            if (map.At(x + dx, y + dy) is not null)
                return StepResult.Blocked;
            var rec = fight.Record(c);
            if (rec[CombatRecord.Movement] < 2)
                return StepResult.NoMovement;
            rec[CombatRecord.Movement] -= 2;
            map.Put(c, x + dx, y + dy);
            return StepResult.Moved;
        }
    }

    private sealed class TestSpells(int castingTime) : IFightSpells
    {
        public List<(int Spell, EclCharacter? Target)> Casts { get; } = [];
        public IReadOnlyList<int> Castable(Fight fight, EclCharacter caster) => [7];
        public bool Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square)
        {
            Casts.Add((spell, target));
            return true;
        }
        public bool ContinueCasting(Fight fight, EclCharacter caster, IReadOnlyList<EclCharacter> targets, (int X, int Y)? square)
        {
            Casts.Add((fight.Record(caster)[CombatRecord.SpellCast], targets.Count > 0 ? targets[0] : null));
            FightRules.EndActions(fight.Record(caster));
            return true;
        }
        public bool UsableInCombat(int spell) => spell != 99;
        public int CastingTime(int spell) => castingTime;
    }

    private sealed class CountingEffects : IFightEffects
    {
        public List<(int Trigger, EclCharacter C)> Calls { get; } = [];
        public Func<int, EclCharacter, FightState, bool>? Block { get; init; }
        public void Trigger(int trigger, EclCharacter c, FightState fight)
        {
            Calls.Add((trigger, c));
            if (Block?.Invoke(trigger, c, fight) == true)
                fight.TargetBlocked = 1;
        }
        public void Removed(EclCharacter c, byte[] effect) { }
    }

    /// <summary>Answers from queues; records what it was shown.</summary>
    private sealed class Script : IFightMenuChooser
    {
        public Queue<FightCommand> Commands { get; } = new();
        public Queue<DoneCommand> Dones { get; } = new();
        public Queue<SpeedCommand> Speeds { get; } = new();
        public Queue<MoveInput> Moves { get; } = new();
        public Queue<AimKey> Aims { get; } = new();
        public Queue<bool> Answers { get; } = new();
        public int? Spell { get; set; }
        public List<IReadOnlyList<FightCommand>> Shown { get; } = [];
        public List<IReadOnlyList<DoneCommand>> DoneShown { get; } = [];
        public List<AimView> AimShown { get; } = [];
        public List<(FightNotice N, EclCharacter? Who)> Notices { get; } = [];
        public List<string> Questions { get; } = [];

        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries)
        {
            Shown.Add(entries);
            return Commands.Dequeue();
        }
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries)
        {
            DoneShown.Add(entries);
            return Dones.Dequeue();
        }
        public SpeedCommand ChooseSpeed(FightMenu menu, byte speed, IReadOnlyList<SpeedCommand> entries) => Speeds.Dequeue();
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => Moves.Dequeue();
        public AimKey ChooseAim(FightMenu menu, AimView view)
        {
            AimShown.Add(view);
            return Aims.Dequeue();
        }
        public bool ConfirmFlee(FightMenu menu)
        {
            Questions.Add("flee");
            return Answers.Dequeue();
        }
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally)
        {
            Questions.Add("ally " + ally.Name);
            return Answers.Dequeue();
        }
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => Spell;
        public void Notice(FightNotice notice, EclCharacter? who) => Notices.Add((notice, who));
    }

    private sealed class RecordingAi : IFightController
    {
        public List<EclCharacter> Acted { get; } = [];
        public void Act(Fight fight, EclCharacter creature, bool auto)
        {
            Acted.Add(creature);
            fight.EndActions(creature);
        }
    }

    // ---- Set-up ----------------------------------------------------------------------------------------------------

    private static BaseItemTable Items()
    {
        var t = new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]);
        t.Set(1, BaseItemTable.Range, 0);     // a sword
        t.Set(2, BaseItemTable.Range, 7);     // a bow: range 7, needs arrows
        t.Set(2, BaseItemTable.Flags, 0x09);
        t.Set(3, BaseItemTable.Range, 4);     // a throwable axe
        t.Set(3, BaseItemTable.Flags, 0x14);
        t.Set(3, BaseItemTable.RateOfFire, 2);
        t.Set(0x49, BaseItemTable.Slot, 11); // arrows: not a hand slot
        return t;
    }

    private static byte[] Item(byte type, byte count = 0)
    {
        var it = new byte[EclCharacter.ItemRecordSize];
        it[0x2E] = type;
        it[CombatRules.ItemEquipped] = 1;
        it[0x39] = count;
        return it;
    }

    private static EclCharacter Creature(string name, bool hostile = false, byte hd = 1, byte hp = 10)
    {
        var c = new EclCharacter { Name = name };
        c[FightRules.InPlay] = 1;
        c[FightRules.Hostile] = (byte)(hostile ? 1 : 0);
        c[CombatRules.AutoCombat] = (byte)(hostile ? 1 : 0);
        c[FightRules.Hp] = hp;
        c[FightRules.HpMax] = hp;
        c[FightRules.Thac0] = 60; // hits on anything but a 1
        c[FightRules.Ac] = 50;
        c[FightRules.AcRear] = 50;
        c[SweepAttack.HitDice] = hd;
        c[0x115] = 1;
        c[0x117] = 4;
        return c;
    }

    private sealed class Setup
    {
        public required Fight Fight { get; init; }
        public required TestMap Map { get; init; }
        public required TestMoves Moves { get; init; }
        public required Rng Rng { get; init; }
        public required Script Script { get; init; }
        public CountingEffects Effects { get; init; } = new();
        public FightMenu Menu(EclCharacter c, IFightController? ai = null, TurnUndeadTable? table = null) =>
            new(Fight, c, Script, ai, null, table);
    }

    private static Setup Make(IEnumerable<(EclCharacter C, int X, int Y)> creatures, CountingEffects? effects = null,
        params ushort[] rolls)
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var map = new TestMap();
        var state = new EncounterState();
        foreach (var (c, x, y) in creatures)
        {
            memory.Party.Members.Add(c);
            map.Put(c, x, y);
            if (c[FightRules.Hostile] != 0)
                state.NotParty.Add(c);
        }
        var rng = new Rng(rolls);
        effects ??= new CountingEffects();
        var fight = new Fight(memory, state, Items(), rng.Next, new EndActionsController(), map, effects);
        var moves = new TestMoves(map);
        fight.Moves = moves;
        foreach (var c in fight.List)
        {
            var rec = fight.Record(c);
            rec.InitiativeValue = 5;
            rec[CombatRecord.Movement] = 12;
            rec[CombatRecord.CastAllowed] = 1;
            rec[CombatRecord.NotParty] = (byte)(c[FightRules.Hostile] != 0 ? 1 : 0);
            rec[CombatRecord.AttackPhase] = 2;
            c[0x113] = 1;
        }
        FightRules.CountTeams(fight.List, fight.State);
        return new Setup { Fight = fight, Map = map, Moves = moves, Rng = rng, Script = new Script(), Effects = effects };
    }

    // ---- Menu entries ----------------------------------------------------------------------------------------------

    [Fact]
    public void Entries_follow_the_conditions_of_0x9E6D()
    {
        var hero = Creature("H");
        var s = Make([(hero, 1, 1)]);
        var menu = s.Menu(hero);
        Assert.Equal([FightCommand.Move, FightCommand.View, FightCommand.Aim, FightCommand.Quick, FightCommand.Done], menu.Entries());

        s.Fight.Record(hero)[CombatRecord.Movement] = 0;
        hero[FightMenu.ItemCount] = 2;
        hero[0x20] = 0x83; // a spell still being memorised counts (unsigned test)
        hero[TurnUndead.ClericLevel] = 1;
        Assert.Equal([FightCommand.View, FightCommand.Aim, FightCommand.Use, FightCommand.Cast, FightCommand.Turn,
            FightCommand.Quick, FightCommand.Done], menu.Entries());

        s.Fight.Memory.Write(FightMenu.NoMagicCell, 1);
        s.Fight.Record(hero)[CombatRecord.TriedTurning] = 1;
        Assert.DoesNotContain(FightCommand.Cast, menu.Entries());
        Assert.DoesNotContain(FightCommand.Turn, menu.Entries());
        s.Fight.Memory.Write(FightMenu.NoMagicCell, 0);
        s.Fight.Record(hero)[CombatRecord.CastAllowed] = 0;
        Assert.DoesNotContain(FightCommand.Cast, menu.Entries());
        hero[TurnUndead.ClericLevel] = 0x80; // signed: negative
        s.Fight.Record(hero)[CombatRecord.TriedTurning] = 0;
        Assert.DoesNotContain(FightCommand.Turn, menu.Entries());
    }

    [Fact]
    public void A_hidden_entry_is_asked_again_and_each_display_runs_trigger_0x0F()
    {
        var hero = Creature("H");
        var s = Make([(hero, 1, 1)]);
        s.Script.Commands.Enqueue(FightCommand.Cast); // not shown: rejected by the menu routine
        s.Script.Commands.Enqueue(FightCommand.ToggleQuickMagic); // accepted, not done
        s.Script.Commands.Enqueue(FightCommand.Done);
        s.Script.Dones.Enqueue(DoneCommand.Quit);
        var menu = s.Menu(hero);
        menu.Run();
        Assert.Equal(1, menu.Rejected);
        Assert.True(s.Fight.State.QuickMagic);
        Assert.Contains((FightNotice.MagicOn, (EclCharacter?)null), s.Script.Notices);
        Assert.Equal(2, s.Effects.Calls.Count(t => t.Trigger == FightRules.TriggerActionStart)); // two displays
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);
        Assert.Empty(s.Rng.Ranges);
    }

    [Fact]
    public void A_creature_out_of_play_ends_its_actions_at_once()
    {
        var hero = Creature("H");
        var s = Make([(hero, 1, 1)]);
        hero[FightRules.InPlay] = 0;
        s.Menu(hero).Run();
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);
        Assert.Empty(s.Script.Shown);
    }

    // ---- Done menu -------------------------------------------------------------------------------------------------

    [Fact]
    public void Guard_is_offered_without_a_ranged_weapon_or_with_a_throwable_one()
    {
        var hero = Creature("H");
        var s = Make([(hero, 1, 1)]);
        var menu = s.Menu(hero);
        Assert.Equal([DoneCommand.Guard, DoneCommand.Delay, DoneCommand.Quit, DoneCommand.Speed, DoneCommand.Exit], menu.DoneEntries());
        hero.Items.Add(Item(2));
        Assert.DoesNotContain(DoneCommand.Guard, menu.DoneEntries());
        hero.Items.Clear();
        hero.Items.Add(Item(3, 2));
        Assert.Contains(DoneCommand.Guard, menu.DoneEntries());
    }

    [Fact]
    public void Guard_Delay_Quit_and_Exit_do_what_the_done_menu_says()
    {
        var hero = Creature("H");
        var s = Make([(hero, 1, 1)]);
        var menu = s.Menu(hero);
        s.Script.Dones.Enqueue(DoneCommand.Guard);
        Assert.True(menu.DoneMenu());
        Assert.Equal(1, s.Fight.Record(hero)[CombatRecord.Guarding]);
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);

        s.Fight.Record(hero).InitiativeValue = 5;
        s.Script.Dones.Enqueue(DoneCommand.Delay);
        Assert.True(menu.DoneMenu());
        Assert.Equal(1, s.Fight.Record(hero).InitiativeValue);

        s.Script.Dones.Enqueue(DoneCommand.Exit);
        Assert.False(menu.DoneMenu());

        s.Script.Dones.Enqueue(DoneCommand.Speed);
        s.Script.Speeds.Enqueue(SpeedCommand.Faster); // hidden at speed 0: asked again
        s.Script.Speeds.Enqueue(SpeedCommand.Slower);
        s.Script.Speeds.Enqueue(SpeedCommand.Exit);
        s.Script.Dones.Enqueue(DoneCommand.Quit);
        Assert.True(menu.DoneMenu());
        Assert.Equal(1, menu.GameSpeed);
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);
    }

    [Fact]
    public void Bandage_saves_the_first_dying_party_member_anywhere_and_ends_the_turn()
    {
        var hero = Creature("H");
        var far = Creature("F");
        var second = Creature("S");
        var dyingFoe = Creature("G", hostile: true);
        var s = Make([(dyingFoe, 0, 0), (hero, 1, 1), (far, 9, 9), (second, 5, 5)]);
        foreach (var c in new[] { far, second, dyingFoe })
        {
            c[FightRules.Status] = (byte)CreatureStatus.Dying;
            s.Fight.Record(c)[CombatRecord.Dying] = 3;
        }
        var menu = s.Menu(hero);
        Assert.Contains(DoneCommand.Bandage, menu.DoneEntries());
        s.Script.Dones.Enqueue(DoneCommand.Bandage);
        Assert.True(menu.DoneMenu());
        Assert.Equal((byte)CreatureStatus.Unconscious, far[FightRules.Status]);
        Assert.Equal(0, s.Fight.Record(far)[CombatRecord.Dying]);
        Assert.Equal((byte)CreatureStatus.Dying, second[FightRules.Status]);
        Assert.Equal((byte)CreatureStatus.Dying, dyingFoe[FightRules.Status]);
        Assert.Contains((FightNotice.Bandaged, (EclCharacter?)far), s.Script.Notices);
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);
    }

    // ---- Quick, Alt-Q, space ---------------------------------------------------------------------------------------

    [Fact]
    public void Quick_hands_the_creature_to_the_AI_and_forgets_a_friendly_target()
    {
        var hero = Creature("H");
        var friend = Creature("F");
        var s = Make([(hero, 1, 1), (friend, 2, 1)]);
        s.Fight.Record(hero).Other = friend;
        var ai = new RecordingAi();
        s.Script.Commands.Enqueue(FightCommand.Quick);
        s.Menu(hero, ai).Run();
        Assert.Equal(1, hero[CombatRules.AutoCombat]);
        Assert.Null(s.Fight.Record(hero).Other);
        Assert.Equal([hero], ai.Acted);
    }

    [Fact]
    public void All_quick_sets_initiative_20_and_space_returns_the_party_to_manual()
    {
        var hero = Creature("H");
        var npc = Creature("N");
        npc[CombatRules.Npc] = 0xB2;
        var s = Make([(hero, 1, 1), (npc, 2, 1)]);
        var menu = s.Menu(hero);
        Assert.True(menu.Perform(FightCommand.AllQuick));
        Assert.Equal(20, s.Fight.Record(hero).InitiativeValue);
        Assert.Equal(1, hero[CombatRules.AutoCombat]);
        Assert.Equal(1, npc[CombatRules.AutoCombat]);
        Assert.False(menu.Perform(FightCommand.PartyToManual));
        Assert.Equal(0, hero[CombatRules.AutoCombat]);
        Assert.Equal(1, npc[CombatRules.AutoCombat]); // NPCs stay
    }

    [Fact]
    public void Alt_X_works_only_with_the_debugging_switch()
    {
        var hero = Creature("H");
        var foe = Creature("G", hostile: true);
        var s = Make([(hero, 1, 1), (foe, 5, 5)]);
        Assert.False(s.Menu(hero).Perform(FightCommand.GodsIntervene));
        Assert.Contains(FightNotice.DoesNotWork, s.Script.Notices.Select(n => n.N));
        Assert.Equal(1, foe[FightRules.InPlay]);
        Assert.True(new FightMenu(s.Fight, hero, s.Script) { CheatEnabled = true }.Perform(FightCommand.GodsIntervene));
        Assert.Equal(0, foe[FightRules.InPlay]);
        Assert.Equal((byte)CreatureStatus.Dead, foe[FightRules.Status]);
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);
    }

    [Fact]
    public void Alt_X_in_an_AI_turn_removes_the_hostiles_only_with_the_word()
    {
        // 0xBB81: scan 0x2D calls 0x1638E; without the word nothing happens (no refusal in the AI path).
        var hero = Creature("H");
        var foe = Creature("G", hostile: true);
        var s = Make([(hero, 1, 1), (foe, 5, 5)]);
        var said = new List<AiMessage>();
        bool word = false;
        var ai = new MonsterAi([]) { ReadKey = () => 0x2D, AltX = () => word, Message = (_, m) => said.Add(m) };
        Assert.False(ai.CheckKeystroke(s.Fight, hero));
        Assert.Equal(1, foe[FightRules.InPlay]);
        Assert.Empty(said);
        word = true;
        Assert.False(ai.CheckKeystroke(s.Fight, hero));
        Assert.Equal(0, foe[FightRules.InPlay]);
        Assert.Equal((byte)CreatureStatus.Dead, foe[FightRules.Status]);
        Assert.Equal([AiMessage.GodsIntervene], said);
    }

    // ---- Move ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Moving_steps_and_faces_and_Return_keeps_the_move()
    {
        var hero = Creature("H");
        var s = Make([(hero, 4, 4)]);
        s.Script.Moves.Enqueue(new MoveInput(4));
        s.Script.Moves.Enqueue(new MoveInput(2));
        s.Script.Moves.Enqueue(MoveInput.Return);
        Assert.False(s.Menu(hero).Move());
        Assert.Equal((5, 5), s.Map.Position(hero));
        Assert.Equal(8, s.Fight.Record(hero)[CombatRecord.Movement]);
        Assert.Equal(2, s.Fight.Record(hero)[CombatRecord.Facing]);
    }

    [Fact]
    public void Escape_undoes_the_whole_move()
    {
        var hero = Creature("H");
        var s = Make([(hero, 4, 4)]);
        s.Fight.Record(hero)[CombatRecord.Facing] = 7;
        s.Script.Moves.Enqueue(new MoveInput(4));
        s.Script.Moves.Enqueue(new MoveInput(4));
        s.Script.Moves.Enqueue(MoveInput.Escape);
        Assert.False(s.Menu(hero).Move());
        Assert.Equal((4, 4), s.Map.Position(hero));
        Assert.Equal(12, s.Fight.Record(hero)[CombatRecord.Movement]);
        Assert.Equal(7, s.Fight.Record(hero)[CombatRecord.Facing]);
    }

    [Fact]
    public void Moving_off_the_map_asks_to_flee_and_a_blocked_step_says_so()
    {
        var hero = Creature("H");
        var s = Make([(hero, 0, 4)]);
        s.Script.Moves.Enqueue(new MoveInput(6)); // off the map: question, no
        s.Script.Answers.Enqueue(false);
        s.Script.Moves.Enqueue(new MoveInput(6)); // again: yes
        s.Script.Answers.Enqueue(true);
        Assert.True(s.Menu(hero).Move());
        Assert.Equal(["flee", "flee"], s.Script.Questions);
        Assert.False(s.Map.OnMap(hero));
        Assert.Equal(0, s.Fight.Record(hero).InitiativeValue);

        var h2 = Creature("H2");
        var s2 = Make([(h2, 4, 4)]);
        s2.Fight.Record(h2)[CombatRecord.Movement] = 3;
        s2.Script.Moves.Enqueue(new MoveInput(0)); // 3 → 1 half-squares: the loop ends (needs > 1)
        Assert.False(s2.Menu(h2).Move());
        Assert.Equal(0, s2.Fight.Record(h2)[CombatRecord.Movement]); // below 2 → 0
    }

    [Fact]
    public void Moving_into_an_enemy_attacks_it_and_a_bow_cannot()
    {
        var hero = Creature("H");
        var foe = Creature("G", hostile: true, hp: 20);
        var s = Make([(hero, 4, 4), (foe, 4, 5)], null, 10, 2);
        s.Script.Moves.Enqueue(new MoveInput(4));
        Assert.True(s.Menu(hero).Move()); // one attack, actions over
        Assert.Equal([(ushort)20, (ushort)4], s.Rng.Ranges); // 1d20, then 1d4 damage
        Assert.Equal(17, foe[FightRules.Hp]);
        Assert.Same(foe, s.Fight.Record(hero).Other);

        var archer = Creature("A");
        archer.Items.Add(Item(2));
        var foe2 = Creature("G2", hostile: true);
        var s2 = Make([(archer, 4, 4), (foe2, 4, 5)]);
        s2.Script.Moves.Enqueue(new MoveInput(4));
        s2.Script.Moves.Enqueue(MoveInput.Return);
        Assert.False(s2.Menu(archer).Move());
        Assert.Contains(FightNotice.NotWithThatWeapon, s2.Script.Notices.Select(n => n.N));
        Assert.Empty(s2.Rng.Ranges);
    }

    [Fact]
    public void Attacking_an_ally_asks_and_yes_turns_the_NPCs_hostile()
    {
        var hero = Creature("H");
        var friend = Creature("F", hp: 30);
        var npc = Creature("N");
        npc[CombatRules.Npc] = 0xB2;
        var s = Make([(hero, 4, 4), (friend, 4, 5), (npc, 8, 8)], null, 10, 0);
        s.Script.Moves.Enqueue(new MoveInput(4));
        s.Script.Answers.Enqueue(false);
        s.Script.Moves.Enqueue(new MoveInput(4));
        s.Script.Answers.Enqueue(true);
        Assert.True(s.Menu(hero).Move());
        Assert.Equal(["ally F", "ally F"], s.Script.Questions);
        Assert.Equal(1, s.Fight.Memory.Read(FightMenu.AttackedAlliesCell));
        Assert.Equal(1, npc[FightRules.Hostile]);
        Assert.Equal(0, friend[FightRules.Hostile]);
        Assert.Equal(1, s.Fight.State.TeamCount[1]);
        Assert.Equal(29, friend[FightRules.Hp]);
    }

    // ---- Aim -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Aim_starts_on_the_creature_itself_and_offers_Target_within_the_weapon_range()
    {
        var hero = Creature("H");
        var near = Creature("G1", hostile: true);
        var far = Creature("G2", hostile: true);
        var s = Make([(hero, 4, 4), (far, 4, 7), (near, 4, 5)], null, 10, 1);
        s.Script.Aims.Enqueue(AimKey.Next);
        s.Script.Aims.Enqueue(AimKey.Next);
        s.Script.Aims.Enqueue(AimKey.Target); // far: not offered, asked again
        s.Script.Aims.Enqueue(AimKey.Prev);
        s.Script.Aims.Enqueue(AimKey.Target);
        var menu = s.Menu(hero);
        Assert.True(menu.Aim());
        Assert.Equal([hero, near, far, far, near], s.Script.AimShown.Select(v => v.Candidate));
        Assert.Equal([false, true, false, false, true], s.Script.AimShown.Select(v => v.TargetOffered));
        Assert.Equal(1, s.Script.AimShown[0].Range); // no weapon: range 1
        Assert.Equal(1, menu.Rejected);
        Assert.Equal(8, near[FightRules.Hp]);
    }

    [Fact]
    public void Aim_range_and_Target_rules_for_ranged_weapons()
    {
        var archer = Creature("A");
        archer.Items.Add(Item(2));
        var foe = Creature("G", hostile: true);
        var s = Make([(archer, 1, 1), (foe, 1, 5)]);
        var menu = s.Menu(archer);
        Assert.Equal(6, FightMenu.AimRange(archer, s.Fight.Items));
        Assert.False(menu.TargetOffered(foe, 6)); // a bow with no arrows
        archer.Items.Add(Item(0x49, 10)); // arrows (type 0x49 goes to the arrows slot)
        Assert.True(menu.TargetOffered(foe, 6));
        Assert.False(menu.TargetOffered(foe, 3)); // too far
        var adjacent = Creature("G2", hostile: true);
        s.Fight.List.Add(adjacent);
        s.Map.Put(adjacent, 2, 2);
        Assert.False(menu.TargetOffered(foe, 6)); // an enemy next to it, bow not throwable
        archer.Items.Clear();
        archer.Items.Add(Item(3, 2));
        Assert.True(menu.TargetOffered(foe, 6)); // throwable
        Assert.False(menu.TargetOffered(archer, 6)); // never itself
    }

    [Fact]
    public void A_thrown_weapon_at_distance_one_uses_no_ammunition()
    {
        var thrower = Creature("T");
        var axe = Item(3, 2);
        thrower.Items.Add(axe);
        var foe = Creature("G", hostile: true, hp: 30);
        var s = Make([(thrower, 4, 4), (foe, 4, 5)], null, 10, 0);
        s.Menu(thrower).TargetAttack(foe);
        Assert.Equal(2, axe[0x39]);
        var foe2 = Creature("G2", hostile: true, hp: 30);
        s.Fight.List.Add(foe2);
        s.Map.Put(foe2, 4, 8);
        s.Fight.Record(thrower).InitiativeValue = 5;
        thrower[0x113] = 1;
        s.Rng.Values.Enqueue(10);
        s.Rng.Values.Enqueue(0);
        s.Menu(thrower).TargetAttack(foe2);
        Assert.Equal(1, axe[0x39]);
    }

    [Fact]
    public void The_target_check_runs_trigger_1_then_trigger_0_with_the_target_remembered()
    {
        var hero = Creature("H");
        var foe = Creature("G", hostile: true);
        EclCharacter? seen = null;
        Fight? fight = null;
        var effects = new CountingEffects
        {
            Block = (t, c, f) =>
            {
                if (t == 0)
                    seen = fight!.Record(c).Other;
                return false;
            },
        };
        var s = Make([(hero, 1, 1), (foe, 2, 2)], effects);
        fight = s.Fight;
        Assert.True(FightMenu.Targetable(s.Fight, hero, foe));
        Assert.Same(foe, seen);
        Assert.Null(s.Fight.Record(hero).Other);
        Assert.Equal([(1, foe), (0, hero)], effects.Calls);

        var blocking = new CountingEffects { Block = (t, c, f) => t == 1 };
        var s2 = Make([(hero, 1, 1), (foe, 2, 2)], blocking);
        Assert.False(FightMenu.Targetable(s2.Fight, hero, foe));
        Assert.Single(blocking.Calls); // trigger 0 not run
        Assert.True(FightMenu.Targetable(s2.Fight, hero, hero));
    }

    // ---- Cast ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_spell_with_a_casting_time_waits_for_the_next_pick_and_is_cast_then()
    {
        var mage = Creature("M");
        var s = Make([(mage, 1, 1)]);
        var spells = new TestSpells(castingTime: 2);
        s.Fight.Spells = spells;
        s.Script.Spell = 7;
        Assert.True(s.Menu(mage).Cast());
        Assert.Equal(7, s.Fight.Record(mage)[CombatRecord.SpellCast]);
        Assert.Equal(3, s.Fight.Record(mage).InitiativeValue); // 5 − 2
        Assert.Empty(spells.Casts);
        s.Menu(mage).Run(); // the next pick: cast now, no menu
        Assert.Equal([(7, (EclCharacter?)null)], spells.Casts);
        Assert.Equal(0, s.Fight.Record(mage)[CombatRecord.SpellCast]);
        Assert.Equal(0, s.Fight.Record(mage).InitiativeValue);
        Assert.Empty(s.Script.Shown);

        s.Fight.Record(mage).InitiativeValue = 2;
        Assert.True(s.Menu(mage).Cast());
        Assert.Equal(1, s.Fight.Record(mage).InitiativeValue); // not larger than the casting time → 1
    }

    [Fact]
    public void A_spell_cast_at_once_ends_the_actions_and_a_camp_spell_is_refused()
    {
        var mage = Creature("M");
        var s = Make([(mage, 1, 1)]);
        var spells = new TestSpells(castingTime: 0);
        s.Fight.Spells = spells;
        s.Script.Spell = 99;
        Assert.False(s.Menu(mage).Cast());
        Assert.Contains(FightNotice.CampOnlySpell, s.Script.Notices.Select(n => n.N));
        s.Script.Spell = null;
        Assert.False(s.Menu(mage).Cast());
        s.Script.Spell = 7;
        Assert.True(s.Menu(mage).Cast());
        Assert.Single(spells.Casts);
        Assert.Equal(0, s.Fight.Record(mage).InitiativeValue);
    }

    [Fact]
    public void A_delayed_spell_survives_the_turn_loop()
    {
        var mage = Creature("M");
        var foe = Creature("G", hostile: true);
        var s = Make([(mage, 1, 1), (foe, 8, 8)]);
        var spells = new TestSpells(castingTime: 3);
        s.Fight.Spells = spells;
        s.Script.Spell = 7;
        s.Script.Commands.Enqueue(FightCommand.Cast);
        mage[0x17] = 7;
        var controller = new MenuController(s.Script);
        var fight = new Fight(s.Fight.Memory, s.Fight.Encounter, s.Fight.Items, s.Rng.Next, controller, s.Map) { Spells = spells };
        fight.Record(mage).InitiativeValue = 5;
        fight.Record(mage)[CombatRecord.CastAllowed] = 1;
        fight.Turn(mage);
        Assert.Equal(2, fight.Record(mage).InitiativeValue);
        Assert.Empty(fight.Diagnostics);
        fight.Turn(mage);
        Assert.Single(spells.Casts);
        Assert.Equal(0, fight.Record(mage).InitiativeValue);
    }

    // ---- Sweep -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_sweep_attacks_each_weak_enemy_next_to_the_fighter_once_defender_first()
    {
        var fighter = Creature("F");
        var g1 = Creature("G1", hostile: true, hd: 0, hp: 1);
        var g2 = Creature("G2", hostile: true, hd: 0, hp: 1);
        var g3 = Creature("G3", hostile: true, hd: 0, hp: 1);
        var ogre = Creature("O", hostile: true, hd: 4);
        var s = Make([(fighter, 4, 4), (g1, 3, 3), (ogre, 4, 3), (g2, 5, 3), (g3, 5, 4)], null,
            10, 0, 10, 0, 10, 0);
        s.Fight.Record(fighter)[CombatRecord.SweepBudget] = 3;
        Assert.True(SweepAttack.Try(s.Fight, fighter, g3));
        Assert.Equal([(ushort)20, (ushort)4, (ushort)20, (ushort)4, (ushort)20, (ushort)4], s.Rng.Ranges);
        Assert.All([g1, g2, g3], g => Assert.Equal(0, g[FightRules.InPlay]));
        Assert.Equal(1, ogre[FightRules.InPlay]);
        Assert.Equal(1, s.Fight.Record(g3)[CombatRecord.FlankCount]);
    }

    [Theory]
    [InlineData(3, 0, 3, true)]   // three weak foes, 1 attack left, budget 3
    [InlineData(3, 0, 1, false)]  // attacks left not below the budget
    [InlineData(3, 1, 3, false)]  // the defender has a hit die
    [InlineData(1, 0, 3, false)]  // weak foes not more than the attacks left
    public void Sweep_conditions(int weak, int defenderHd, int budget, bool sweeps)
    {
        var fighter = Creature("F");
        var foes = Enumerable.Range(0, weak).Select(i => Creature("G" + i, hostile: true, hd: 0)).ToList();
        foes[0][SweepAttack.HitDice] = (byte)defenderHd;
        var places = new[] { (3, 3), (4, 3), (5, 3) };
        var s = Make(new[] { (fighter, 4, 4) }.Concat(foes.Select((f, i) => (f, places[i].Item1, places[i].Item2))));
        s.Fight.Record(fighter)[CombatRecord.SweepBudget] = (byte)budget;
        Assert.Equal(sweeps, SweepAttack.Try(s.Fight, fighter, foes[0]));
        if (!sweeps)
            Assert.Empty(s.Rng.Ranges);
    }

    [Fact]
    public void Sweep_count_is_capped_by_the_budget_and_needs_distance_1()
    {
        var fighter = Creature("F");
        var foes = Enumerable.Range(0, 4).Select(i => Creature("G" + i, hostile: true, hd: 0, hp: 50)).ToList();
        var s = Make(new[] { (fighter, 4, 4), (foes[0], 3, 3), (foes[1], 4, 3), (foes[2], 5, 3), (foes[3], 5, 4) });
        s.Fight.Record(fighter)[CombatRecord.SweepBudget] = 2;
        Assert.True(SweepAttack.Try(s.Fight, fighter, foes[2]));
        Assert.Equal(2, s.Rng.Ranges.Count(r => r == 20));
        Assert.Equal(1, s.Fight.Record(foes[2])[CombatRecord.FlankCount]); // the defender, first
        Assert.Equal(1, s.Fight.Record(foes[1])[CombatRecord.FlankCount]); // then list order
        Assert.Equal(0, s.Fight.Record(foes[0])[CombatRecord.FlankCount]); // swapped to the defender's place

        var far = Creature("X", hostile: true, hd: 0);
        s.Fight.List.Add(far);
        s.Map.Put(far, 8, 8);
        s.Fight.Record(fighter)[CombatRecord.SweepBudget] = 5;
        fighter[0x113] = 1;
        Assert.False(SweepAttack.Try(s.Fight, fighter, far));
    }

    // ---- Turn undead -----------------------------------------------------------------------------------------------

    /// <summary>A made-up table: rows 1–10 × columns 1–10, value 10 × row + column for rows 1–4, then 0 (destroy),
    /// −1 for row 6, and 99 elsewhere.</summary>
    private static TurnUndeadTable Table()
    {
        var b = new byte[TurnUndeadTable.Length];
        for (int row = 1; row <= 10; row++)
            for (int col = 1; col <= 10; col++)
                b[row * 10 + col] = (byte)(sbyte)(row switch { <= 4 => 1 + col, 5 => 0, 6 => -1, _ => 99 });
        return new TurnUndeadTable(b);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(9, 9)]
    [InlineData(13, 9)]
    [InlineData(14, 10)]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    public void Turn_undead_column_by_cleric_level(int level, int column) =>
        Assert.Equal(column, TurnUndead.Column((sbyte)level));

    private static EclCharacter Undead(string name, byte row)
    {
        var u = Creature(name, hostile: true);
        u[TurnUndead.Row] = row;
        return u;
    }

    [Fact]
    public void Turn_undead_draws_1d12_then_1d20_and_turns_lowest_rows_first()
    {
        var cleric = Creature("C");
        cleric[TurnUndead.ClericLevel] = 3; // column 3: rows 1–4 need 4
        var zombie = Undead("Z", 2);
        var skel1 = Undead("S1", 1);
        var skel2 = Undead("S2", 1);
        var orc = Creature("O", hostile: true);
        var s = Make([(cleric, 4, 4), (zombie, 4, 6), (orc, 5, 5), (skel1, 8, 8), (skel2, 2, 2)], null,
            1, 9); // count 2, roll 10
        var r = TurnUndead.Run(s.Fight, cleric, Table());
        Assert.Equal([(ushort)12, (ushort)20], s.Rng.Ranges);
        Assert.Equal(2, r.Count);
        Assert.Equal(10, r.Roll);
        Assert.Equal([skel2, skel1], r.Turned); // row 1 before row 2; equal rows in search order (nearer first)
        Assert.Equal(0, s.Fight.Record(zombie)[CombatRecord.Turned]);
        Assert.Equal(1, s.Fight.Record(cleric)[CombatRecord.TriedTurning]);
        Assert.Equal(0, s.Fight.Record(cleric).InitiativeValue);
    }

    [Fact]
    public void A_failed_roll_stops_turning_and_nothing_happens_without_undead()
    {
        var cleric = Creature("C");
        cleric[TurnUndead.ClericLevel] = 3;
        var low = Undead("L", 7); // 99: fails
        var s = Make([(cleric, 4, 4), (low, 4, 6)], null, 11, 19);
        var r = TurnUndead.Run(s.Fight, cleric, Table());
        Assert.False(r.Affected);
        Assert.Equal(20, r.Roll);
        Assert.Equal(0, s.Fight.Record(low)[CombatRecord.Turned]);
        Assert.Equal(1, low[FightRules.InPlay]);

        var c2 = Creature("C2");
        var s2 = Make([(c2, 4, 4), (Creature("O", hostile: true), 5, 5)], null, 0, 0);
        var notices = new List<FightNotice>();
        var r2 = TurnUndead.Run(s2.Fight, c2, null, (n, _) => notices.Add(n)); // no table needed without undead
        Assert.Equal([(ushort)12, (ushort)20], s2.Rng.Ranges);
        Assert.Equal([FightNotice.TurnsUndead, FightNotice.NothingHappens], notices);
        Assert.False(r2.Affected);
    }

    [Fact]
    public void Destroyed_undead_leave_the_map_and_a_negative_value_keeps_the_count_up()
    {
        var cleric = Creature("C");
        cleric[TurnUndead.ClericLevel] = 3;
        var ghosts = Enumerable.Range(0, 8).Select(i => Undead("W" + i, 6)).ToList(); // −1: destroy
        var s = Make(new[] { (cleric, 0, 0) }.Concat(ghosts.Select((g, i) => (g, 2 + i, 2))), null, 0, 0); // count 1, roll 1
        var r = TurnUndead.Run(s.Fight, cleric, Table());
        // count 1 → 0 with 5 left and v < 0 → 1 again, until 6 are done; then count reaches 0.
        Assert.Equal(6, r.Destroyed.Count);
        Assert.All(r.Destroyed, g =>
        {
            Assert.Equal((byte)CreatureStatus.Gone, g[FightRules.Status]);
            Assert.Equal(0, g[FightRules.InPlay]);
            Assert.False(s.Map.OnMap(g));
        });
        Assert.Equal(2, s.Fight.State.TeamCount[1]);
    }
}
