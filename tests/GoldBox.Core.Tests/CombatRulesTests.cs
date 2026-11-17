using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>Combat and after-combat rules (docs/re/combat-format.md) and the CALL ids the encounter host handles.</summary>
public class CombatRulesTests
{
    private static EclCharacter Member(string name, byte cls = 2, CreatureStatus status = CreatureStatus.Okay)
    {
        var c = new EclCharacter { Name = name };
        c[CombatRules.Class] = cls;
        c[CombatRules.Status] = (byte)status;
        c[CombatRules.InPlay] = 1;
        return c;
    }

    private static EclCharacter Monster(ushort xpBase, byte perHp, byte hpRolled, CreatureStatus status = CreatureStatus.Dead)
    {
        var c = new EclCharacter { Name = "M" };
        c[CombatRules.Hostile] = 1;
        c[CombatRules.Status] = (byte)status;
        c[CombatRules.InPlay] = (byte)(status == CreatureStatus.Okay ? 1 : 0);
        c.SetWord(CombatRules.XpBase, xpBase);
        c[CombatRules.XpPerRolledHp] = perHp;
        c[CombatRules.HpRolled] = hpRolled;
        return c;
    }

    private static byte[] Item(ushort value, sbyte plus = 0, bool equipped = true)
    {
        var b = new byte[EclCharacter.ItemRecordSize];
        b[CombatRules.ItemValue] = (byte)value;
        b[CombatRules.ItemValue + 1] = (byte)(value >> 8);
        b[CombatRules.ItemPlus] = (byte)plus;
        b[CombatRules.ItemEquipped] = (byte)(equipped ? 1 : 0);
        return b;
    }

    private sealed class Rig
    {
        public EclMemory Memory { get; } = new();
        public EncounterState State { get; } = new();
        public Queue<ushort> Rolls { get; } = new();
        public List<ushort> Ranges { get; } = [];
        public List<string> Diagnostics { get; } = [];

        public Rig(params EclCharacter[] list)
        {
            var p = new EclParty { CurrentIndex = 0 };
            p.Members.AddRange(list);
            Memory.Party = p;
        }

        public void MarkFight(int partySize)
        {
            Memory.Write(AfterCombat.PartySize, (ushort)partySize);
            for (int i = partySize; i < Memory.Party!.Members.Count; i++)
                State.NotParty.Add(Memory.Party.Members[i]);
        }

        public AfterCombatOutcome Run()
        {
            var step = new AfterCombat(Memory, State, n => { Ranges.Add(n); return Rolls.Dequeue(); }, Diagnostics);
            var o = step.Run();
            step.Finish(o);
            return o;
        }

        public List<EclCharacter> List => Memory.Party!.Members;
    }

    private static uint Xp(EclCharacter c) =>
        (uint)(c[0xAC] | c[0xAD] << 8 | c[0xAE] << 16 | c[0xAF] << 24);

    // ---- The fight (0x9881) ------------------------------------------------------------------------------------

    [Fact]
    public void Next_creature_rolls_for_every_member_and_takes_the_highest_initiative()
    {
        var rolls = new Queue<int>([50, 10, 90]);
        int calls = 0;
        Assert.Equal(1, CombatRules.NextCreature([3, 5, 4], () => { calls++; return rolls.Dequeue(); }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Next_creature_breaks_ties_on_the_roll_and_full_ties_go_to_the_later_one()
    {
        Assert.Equal(2, CombatRules.NextCreature([4, 4, 4], new Queue<int>([30, 20, 70]).Dequeue));
        Assert.Equal(0, CombatRules.NextCreature([4, 4, 4], new Queue<int>([60, 20, 59]).Dequeue));
        Assert.Equal(1, CombatRules.NextCreature([4, 4], new Queue<int>([60, 60]).Dequeue));
    }

    [Fact]
    public void Next_creature_is_none_without_positive_initiative()
    {
        Assert.Null(CombatRules.NextCreature([0, -3, 0], new Queue<int>([99, 99, 99]).Dequeue));
        Assert.Null(CombatRules.NextCreature([], () => 1));
    }

    // ---- Experience (0x557E / 0x58D3) --------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 16, 0, 0, 0, 99)] // fighter, Strength 16
    [InlineData(2, 15, 0, 0, 0, 90)] // fighter, Strength 15: no bonus
    [InlineData(0, 0, 0, 17, 0, 99)] // cleric, Wisdom
    [InlineData(5, 0, 18, 0, 0, 99)] // magic-user, Intelligence
    [InlineData(6, 0, 0, 0, 16, 99)] // thief, Dexterity
    [InlineData(3, 18, 18, 18, 18, 90)] // paladin: never a bonus
    [InlineData(13, 18, 18, 18, 18, 45)] // fighter/magic-user
    [InlineData(16, 0, 0, 0, 0, 45)] // magic-user/thief
    [InlineData(9, 0, 0, 0, 0, 30)] // cleric/fighter/magic-user
    [InlineData(15, 0, 0, 0, 0, 30)] // fighter/magic-user/thief
    public void Experience_by_class(byte cls, byte str, byte intel, byte wis, byte dex, int expected)
    {
        var c = new EclCharacter();
        c[CombatRules.Class] = cls;
        c[CombatRules.Str] = str;
        c[CombatRules.Int] = intel;
        c[CombatRules.Wis] = wis;
        c[CombatRules.Dex] = dex;
        Assert.Equal(expected, CombatRules.ExperienceFor(c, 90));
    }

    [Fact]
    public void Award_skips_members_out_of_play_and_animated_ones()
    {
        var a = Member("A");
        var b = Member("B");
        b[CombatRules.InPlay] = 0;
        var d = Member("D", status: CreatureStatus.Animated);
        CombatRules.AwardExperience([a, b, d], 50);
        Assert.Equal(50u, Xp(a));
        Assert.Equal(0u, Xp(b));
        Assert.Equal(0u, Xp(d));
    }

    /// <summary>
    /// The whole after-combat step on the real Slums fight (`combat_win`: 8 goblin guards, set 2 id 2, and the
    /// leader, id 12, all killed by the save-A party): share 90, $6DC8 = 9 (the capture's diff), the monsters gone and
    /// the party's experience equal to save B, whatever the 1d10 item rolls give.
    /// </summary>
    [CaptureTheory]
    [InlineData(0)]
    [InlineData(9)]
    public void After_combat_on_the_slums_goblin_fight_matches_the_capture_and_save_B(ushort roll)
    {
        var game = PorData.Open();
        var party = PorParty.Load(PorSaveGame.Open(game.Find("SAVGAMA.DAT")!), game.Root);
        var after = PorParty.Load(PorSaveGame.Open(Path.Combine(Captures.Dir!, "saves", "SAVGAMB.DAT")), Path.Combine(Captures.Dir!, "saves"));
        var data = new PorEncounterData(game);
        var memory = new EclMemory { Party = party };
        memory.Write(AfterCombat.PartySize, 6);
        var state = new EncounterState();
        foreach (var id in Enumerable.Repeat(2, 8).Append(12))
        {
            var m = data.Monster(2, id)!;
            var c = new EclCharacter(m.Record);
            c.Items.AddRange(m.Items.Select(i => (byte[])i.Clone()));
            c[CombatRules.Status] = (byte)CreatureStatus.Dead;
            c[CombatRules.InPlay] = 0;
            party.Members.Add(c);
            state.Monsters.Add(c);
            state.NotParty.Add(c);
        }
        var step = new AfterCombat(memory, state, _ => roll, []);
        var o = step.Run();
        Assert.Equal(90, o.ExperienceShare);
        Assert.Equal(9, o.MonstersDefeated);
        Assert.Equal(9, memory.Read(AfterCombat.MonstersDefeated));
        Assert.Equal(6, party.Members.Count);
        Assert.Equal(after.Members.Select(Xp), party.Members.Select(Xp));
    }

    [Fact]
    public void Experience_pieces_follow_the_program_arithmetic()
    {
        Assert.Equal(15, CombatRules.CreatureExperience(Monster(10, 1, 5)));
        Assert.Equal(400, CombatRules.ItemExperience(Item(0, 1)));
        Assert.Equal(0, CombatRules.ItemExperience(Item(0, -2)));
        // 16-bit products are sign-extended: plus 82 × 400 = 32800 counts as −32736.
        Assert.Equal(-32736, CombatRules.ItemExperience(Item(0, 82)));
        Assert.Equal(1 + 1 + 1 + 1 + 5 + 250 + 2200, CombatRules.PoolExperience([200, 20, 2, 1, 1, 1, 1]));
    }

    /// <summary>
    /// The Slums goblin fight from save A (character-format.md "XP award", DOSBox 2026-11-17): share 90 shown on
    /// screen, and the save made after it (fixtures <c>saves/CHRDATB*</c>) holds each character's new experience.
    /// </summary>
    [CaptureFact]
    public void Award_of_90_reproduces_the_save_after_the_goblin_fight()
    {
        var game = PorData.Open();
        var before = PorParty.Load(PorSaveGame.Open(game.Find("SAVGAMA.DAT")!), game.Root);
        var after = PorParty.Load(PorSaveGame.Open(Path.Combine(Captures.Dir!, "saves", "SAVGAMB.DAT")), Path.Combine(Captures.Dir!, "saves"));
        CombatRules.AwardExperience(before.Members, 90);
        Assert.Equal(after.Members.Select(Xp), before.Members.Select(Xp));
    }

    // ---- After combat (0x6AA5) ---------------------------------------------------------------------------------

    [Fact]
    public void Won_fight_shares_experience_takes_items_and_removes_the_monsters()
    {
        var m1 = Monster(10, 1, 5);
        m1.SetWord(CombatRules.Money + 2, 18); // silver
        m1.Items.Add(Item(0)); // value 0: taken on 1d10 ≤ 3
        m1.Items.Add(Item(5, plus: 1)); // value > 0: always taken, +400
        var m2 = Monster(10, 1, 7, CreatureStatus.Okay); // still standing: removed but not counted
        var rig = new Rig(Member("A"), Member("B"), m1, m2);
        rig.MarkFight(2);
        rig.Rolls.Enqueue(2); // 1d10 = 3
        var o = rig.Run();

        // (15 + 17 + 18 / 20 + 400) / 2 = 216
        Assert.True(o.Won);
        Assert.True(o.HostileFound);
        Assert.Equal(216, o.ExperienceShare);
        Assert.Equal([(ushort)10], rig.Ranges);
        Assert.Equal(216u, Xp(rig.List[0]));
        Assert.Equal(2, rig.List.Count);
        Assert.Equal(1, o.MonstersDefeated);
        Assert.Equal(1, rig.Memory.Read(AfterCombat.MonstersDefeated));
        Assert.Equal(2, rig.Memory.Read(AfterCombat.PartySize));
        Assert.Equal(0, rig.Memory.Read(AfterCombat.CombatResult));
        Assert.Equal(18u, rig.State.Money[1]);
        Assert.Empty(rig.State.TreasureItems); // freed after the loot step
        Assert.Empty(rig.State.NotParty);
    }

    [Fact]
    public void Items_are_copied_unequipped_newest_first_and_no_items_with_6DE3()
    {
        var m = Monster(0, 0, 0);
        m.Items.Add(Item(1, plus: 1));
        m.Items.Add(Item(2, plus: 2));
        var rig = new Rig(Member("A"), m);
        rig.MarkFight(1);
        var step = new AfterCombat(rig.Memory, rig.State, _ => 0, rig.Diagnostics);
        var o = step.Run();
        Assert.Equal(1200, o.ExperienceShare);
        Assert.Equal([2, 1], rig.State.TreasureItems.Select(i => (int)i[CombatRules.ItemValue]));
        Assert.All(rig.State.TreasureItems, i => Assert.Equal(0, i[CombatRules.ItemEquipped]));

        var m2 = Monster(0, 0, 0);
        m2.Items.Add(Item(1, plus: 1));
        var rig2 = new Rig(Member("A"), m2);
        rig2.MarkFight(1);
        rig2.Memory.Write(AfterCombat.NoCombatItems, 1);
        Assert.Equal(0, rig2.Run().ExperienceShare);
        Assert.Equal(0, rig2.Memory.Read(AfterCombat.NoCombatItems)); // cleared at the end
    }

    [Fact]
    public void At_most_eight_items_are_taken_on_the_1d10()
    {
        var m = Monster(0, 0, 0);
        for (int i = 0; i < 10; i++)
            m.Items.Add(Item(0));
        var rig = new Rig(Member("A"), m);
        rig.MarkFight(1);
        var step = new AfterCombat(rig.Memory, rig.State, _ => 0, rig.Diagnostics);
        step.Run();
        Assert.Equal(8, rig.State.TreasureItems.Count);
    }

    [Fact]
    public void Fled_party_keeps_the_runners_and_loses_the_others()
    {
        var rig = new Rig(Member("A", status: CreatureStatus.Fled), Member("B", status: CreatureStatus.Dead),
            Member("C", status: CreatureStatus.Fled), Monster(10, 0, 0, CreatureStatus.Okay));
        rig.MarkFight(3);
        var o = rig.Run();
        Assert.True(o.PartyFled);
        Assert.False(o.Won);
        Assert.False(o.PartyDefeated);
        Assert.Equal(AfterCombat.ResultFled, rig.Memory.Read(AfterCombat.CombatResult));
        Assert.Equal(["A", "C"], rig.List.Select(c => c.Name));
        Assert.All(rig.List, c => Assert.Equal(CreatureStatus.Okay, (CreatureStatus)c[CombatRules.Status]));
        Assert.Equal(2, rig.Memory.Read(AfterCombat.PartySize));
    }

    [Fact]
    public void Survivors_recover_after_a_won_fight()
    {
        var dying = Member("D", status: CreatureStatus.Dying);
        var out1 = Member("U", status: CreatureStatus.Unconscious);
        out1[CombatRules.HpCurrent] = 3;
        var out0 = Member("Z", status: CreatureStatus.Unconscious);
        var rig = new Rig(Member("A"), Member("R", status: CreatureStatus.Fled), dying, out1, out0);
        rig.MarkFight(5);
        var o = rig.Run();
        Assert.True(o.Won);
        Assert.False(o.PartyFled); // an okay member cancels the flight
        Assert.Equal([CreatureStatus.Okay, CreatureStatus.Okay, CreatureStatus.Unconscious, CreatureStatus.Okay, CreatureStatus.Unconscious],
            rig.List.Select(c => (CreatureStatus)c[CombatRules.Status]));
    }

    [Fact]
    public void Destroyed_party_is_removed_and_the_combat_result_is_0x80()
    {
        var rig = new Rig(Member("A", status: CreatureStatus.Dead), Member("B", status: CreatureStatus.Unconscious),
            Monster(10, 0, 0, CreatureStatus.Okay));
        rig.MarkFight(2);
        var o = rig.Run();
        Assert.True(o.PartyDefeated);
        Assert.Empty(rig.List);
        Assert.Equal(0, rig.Memory.Read(AfterCombat.PartySize));
        Assert.Equal(AfterCombat.ResultDestroyed, rig.Memory.Read(AfterCombat.CombatResult));
    }

    [Fact]
    public void A_hostile_party_member_leaves_and_lowers_the_party_size()
    {
        var turncoat = Member("T");
        turncoat[CombatRules.Hostile] = 1;
        turncoat[CombatRules.InPlay] = 0;
        var rig = new Rig(Member("A"), turncoat);
        rig.MarkFight(2);
        var o = rig.Run();
        Assert.Equal(["A"], rig.List.Select(c => c.Name));
        Assert.Equal(1, rig.Memory.Read(AfterCombat.PartySize));
        Assert.Equal(1, o.MonstersDefeated);
    }

    [Fact]
    public void After_combat_clears_the_one_fight_cells()
    {
        var rig = new Rig(Member("A"));
        rig.MarkFight(1);
        foreach (var a in AfterCombat.ClearedAtEnd)
            rig.Memory.Write(a, 7);
        rig.Run();
        Assert.All(AfterCombat.ClearedAtEnd, a => Assert.Equal(0, rig.Memory.Read(a)));
    }

    // ---- CALL ids (0x3A15) -------------------------------------------------------------------------------------

    private static (EncounterHost Host, EclMemory Memory, ScriptedEclHost Inner) Host(byte[]? planes = null)
    {
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(Member("A"));
        memory.Party.Members.Add(Member("B"));
        var inner = new ScriptedEclHost();
        var host = new EncounterHost(memory, inner, new ScriptedEncounterUi(), new RecordingCombatRunner())
        {
            Map = planes is null ? null : new MapPlanes(planes),
            CurrentBlock = () => 20,
        };
        return (host, memory, inner);
    }

    [Theory]
    [InlineData(5, 5, 0, 5, 4)]
    [InlineData(5, 0, 0, 5, 15)]
    [InlineData(15, 4, 2, 0, 4)]
    [InlineData(5, 15, 4, 5, 0)]
    [InlineData(0, 4, 6, 15, 4)]
    [InlineData(5, 5, 3, 5, 5)] // odd facing: no move
    public void Call_C01E_steps_one_square_forward_with_wrap(int x, int y, int facing, int nx, int ny)
    {
        var (host, memory, _) = Host();
        memory.Position.X = (byte)x;
        memory.Position.Y = (byte)y;
        memory.Position.Facing = (byte)facing;
        host.Call(EncounterHost.CallStepForward);
        Assert.Equal((nx, ny), ((int)memory.Position.X, (int)memory.Position.Y));
    }

    [Fact]
    public void Call_C01E_reads_the_event_byte_and_the_wall_ahead_again()
    {
        var planes = new byte[MapPlanes.Size];
        planes[512 + 4 * 16 + 0] = 0x82; // event at (0, 4)
        planes[4 * 16 + 0] = 0x05; // N face 0, E face 5 of (0, 4)
        var (host, memory, inner) = Host(planes);
        memory.Position.X = 15;
        memory.Position.Y = 4;
        memory.Position.Facing = 2;
        host.Call(EncounterHost.CallStepForward);
        Assert.Equal(0x82, memory.Position.EventByte);
        Assert.Equal(5, memory.Position.WallAhead);
        Assert.DoesNotContain(inner.Calls, c => c.StartsWith("Call", StringComparison.Ordinal));
    }

    [Fact]
    public void Other_call_ids_go_to_the_inner_host()
    {
        var (host, _, inner) = Host();
        host.Call(0xBA03);
        Assert.Contains("Call BA03", inner.Calls);
    }

    [Fact]
    public void Call_2C90_reads_the_event_byte_again_and_redraws()
    {
        var planes = new byte[MapPlanes.Size];
        planes[512 + 6 * 16 + 14] = 0x21;
        var (host, memory, inner) = Host(planes);
        memory.Position.X = 14;
        memory.Position.Y = 6;
        host.Call(EncounterHost.CallRedraw);
        Assert.Equal(0x21, memory.Position.EventByte);
        Assert.Contains("Call 2C90", inner.Calls);
    }

    [Fact]
    public void Call_8000_sets_up_the_mirror_duel_and_its_share_is_100_per_level()
    {
        var (host, memory, _) = Host();
        var me = memory.Party!.Members[0];
        me[CombatRules.LevelHighest] = 3;
        me.Items.Add(Item(4));
        host.Call(EncounterHost.CallMirrorDuel);
        Assert.True(host.State.Duel);
        Assert.Equal(1, memory.Read(AfterCombat.MirrorDuel));
        var list = memory.Party.Members;
        Assert.Equal(3, list.Count);
        Assert.Equal(0, list[1][CombatRules.InPlay]); // B is out of the fight
        var copy = list[2];
        Assert.Equal(1, copy[CombatRules.Hostile]);
        Assert.Equal(0xB2, copy[CombatRules.Npc]);
        Assert.Equal(1, copy[CombatRules.AutoCombat]);
        Assert.Single(copy.Items);

        // The duel is won by A: the share is 100 × level 3, and only creatures in play get it.
        copy[CombatRules.Status] = (byte)CreatureStatus.Dead;
        memory.Write(AfterCombat.PartySize, 2);
        host.State.NotParty.Add(copy);
        var step = new AfterCombat(memory, host.State, _ => 0, host.Diagnostics);
        var o = step.Run();
        Assert.True(o.Won);
        Assert.False(o.PartyDefeated);
        Assert.Equal(300, o.ExperienceShare);
        Assert.Equal(300u, Xp(me));
        Assert.Equal(0u, Xp(list[1]));
        Assert.Equal(1, list[1][CombatRules.InPlay]); // okay members are back in play
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Call_8001_is_a_solo_fight_without_a_copy()
    {
        var (host, memory, _) = Host();
        host.Call(EncounterHost.CallSoloFight);
        Assert.True(host.State.Duel);
        Assert.Equal(0, memory.Read(AfterCombat.MirrorDuel));
        Assert.Equal(2, memory.Party!.Members.Count);
    }
}
