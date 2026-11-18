using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>The game-side encounter host (docs/re/ecl-format.md "Encounters").</summary>
public class EncounterHostTests
{
    private const int F = 0x4A20;

    /// <summary>A map with no walls, plus the walls a test sets.</summary>
    private sealed class TestMap : IWallMap
    {
        public Dictionary<(int, int, int), int> Walls { get; } = [];
        public int WallType(int x, int y, int face) => Walls.TryGetValue((x, y, face), out var t) ? t : 0;
    }

    private sealed class FakeData : IEncounterData
    {
        public MonsterData? Monster(int disk, int id)
        {
            var r = new byte[EclCharacter.RecordSize];
            r[0x2E] = 2; // race → tall icon
            r[EncounterRules.MovementOffset] = (byte)(6 + id);
            return new MonsterData(r, [new byte[EclCharacter.ItemRecordSize]], [[0x27, 0, 0, 255, 0, 0, 0, 0, 0]]);
        }
        public IReadOnlyList<byte[]>? ItemList(int disk, int id) =>
            id == 9 ? [Item(1), Item(2), Item(3)] : null;
        private static byte[] Item(byte tag) { var b = new byte[EclCharacter.ItemRecordSize]; b[0] = tag; return b; }
    }

    private sealed class Rig
    {
        public EclMemory Memory { get; }
        public ScriptedEclHost Inner { get; } = new();
        public ScriptedEncounterUi Ui { get; } = new();
        public RecordingCombatRunner Combat { get; } = new();
        public EncounterHost Host { get; }
        public EclVm Vm { get; }
        public TestMap Map { get; } = new();

        public Rig(EclAsm asm, bool outdoors = false, int partyMovement = 9, ICombatRunner? combat = null)
        {
            Memory = new EclMemory();
            var party = new EclParty();
            for (int i = 0; i < 2; i++)
            {
                var c = new EclCharacter();
                c[EncounterRules.MovementOffset] = (byte)(partyMovement + i * 3);
                c[CombatRules.InPlay] = 1;
                party.Members.Add(c);
            }
            party.CurrentIndex = 0;
            Memory.Party = party;
            Host = new EncounterHost(Memory, Inner, Ui, combat ?? Combat, new FakeData()) { Map = Map, CurrentBlock = () => 20 };
            Vm = new EclVm(Memory, Host);
            Host.Vm = Vm;
            Vm.LoadScriptBlock(asm.Build(), 1, 1); // script init sets $49E6 = 1 (3D view)
            if (outdoors)
                Memory.Write(EncounterHost.OutdoorsFlag, 0);
            Memory.Position.X = 5;
            Memory.Position.Y = 5;
            Memory.Position.Facing = 6; // west
            Memory.Write(AfterCombat.PartySize, 2);
        }

        public EclRunResult Run() => Vm.RunVector(0, 1000);
        public ushort this[int a] => Memory.Read((ushort)a);
    }

    // ---- View distance -----------------------------------------------------------------------------------------

    [Fact]
    public void View_walks_up_to_two_open_faces()
    {
        var map = new TestMap();
        Assert.Equal(2, EncounterRules.ViewDistance(map, false, 5, 5, 6, 20));
        map.Walls[(4, 5, 6)] = 3; // wall on the west face of the next square
        Assert.Equal(1, EncounterRules.ViewDistance(map, false, 5, 5, 6, 20));
        map.Walls[(5, 5, 6)] = 1; // wall on the party's own face
        Assert.Equal(0, EncounterRules.ViewDistance(map, false, 5, 5, 6, 20));
        Assert.Equal(2, EncounterRules.ViewDistance(map, true, 5, 5, 6, 20)); // outdoors: always 2
        Assert.Equal(2, EncounterRules.ViewDistance(null, false, 5, 5, 6, 20));
    }

    [Fact]
    public void View_wraps_at_the_edge_except_in_blocks_0_and_10()
    {
        var map = new TestMap();
        map.Walls[(15, 5, 6)] = 2; // west face of the square across the west edge
        Assert.Equal(1, EncounterRules.ViewDistance(map, false, 0, 5, 6, 20));
        Assert.Equal(2, EncounterRules.ViewDistance(map, false, 0, 5, 6, 0));
        Assert.Equal(2, EncounterRules.ViewDistance(map, false, 0, 5, 6, 10));
    }

    [Fact]
    public void Map_planes_hold_the_four_faces_in_nibbles()
    {
        var planes = new byte[MapPlanes.Size];
        planes[3 * 16 + 2] = 0x12;       // (2,3): N = 1, E = 2
        planes[256 + 3 * 16 + 2] = 0x34; // S = 3, W = 4
        var m = new MapPlanes(planes);
        Assert.Equal([1, 2, 3, 4], new[] { 0, 2, 4, 6 }.Select(f => m.WallType(2, 3, f)));
    }

    // ---- SETUPMON / APPROACH -----------------------------------------------------------------------------------

    [Fact]
    public void Setupmon_limits_the_distance_to_the_view_and_approach_closes_in()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(7))
            .Op(EclOpcodes.Save, Mem(0x6DC1), Mem(F))
            .Op(EclOpcodes.Approach)
            .Op(EclOpcodes.Save, Mem(0x6DC1), Mem(F + 1))
            .Op(EclOpcodes.Approach)
            .Op(EclOpcodes.Exit));
        rig.Map.Walls[(4, 5, 6)] = 1; // view 1
        rig.Run();
        Assert.Equal(2, rig[0x6DC0]);
        Assert.Equal(1, rig[F]);
        Assert.Equal(0, rig[F + 1]);
        Assert.Equal(0, rig[0x6DC1]); // APPROACH at 0 does nothing
        Assert.Equal(["ShowEncounter 4 7 1", "ShowEncounter 4 7 0"], rig.Ui.Calls);
    }

    [Fact]
    public void Setupmon_outdoors_starts_at_the_requested_distance()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.SetupMon, Imm8(4), Imm8(1), Imm8(7)).Op(EclOpcodes.Exit), outdoors: true);
        rig.Run();
        Assert.Equal(1, rig[0x6DC1]);
    }

    // ---- ENCMENU -----------------------------------------------------------------------------------------------

    private static EclAsm EncMenu(int distance, int[] reactions, int move1 = 6, int move2 = 6, string[]? texts = null)
    {
        texts ??= ["NEAR", "", "FAR"];
        return new EclAsm()
            .Op(EclOpcodes.Save, Imm8(77), Mem(F))
            .Op(EclOpcodes.EncMenu, Imm8(4), Imm8(distance), Imm8(7), Mem(F),
                Imm8(reactions[0]), Imm8(reactions[1]), Imm8(reactions[2]), Imm8(reactions[3]), Imm8(reactions[4]),
                Text(texts[0]), Text(texts[1]), Text(texts[2]), Imm8(move1), Imm8(move2))
            .Op(EclOpcodes.Exit);
    }

    [Fact]
    public void Encmenu_advances_then_offers_parley_at_distance_0()
    {
        var rig = new Rig(EncMenu(2, [0, 1, 1, 1, 4]));
        rig.Ui.Choices.Enqueue(3); // advance (2 → 1)
        rig.Ui.Choices.Enqueue(3); // advance (1 → 0)
        rig.Ui.Choices.Enqueue(3); // 4th item is now parley
        rig.Run();
        Assert.Equal((ushort)EncounterResult.Parley, rig[F]);
        Assert.Equal(2, rig[0x6DC0]);
        Assert.Equal(0, rig[0x6DC1]);
        Assert.Equal(
        [
            "ShowEncounter 4 7 2", "EncounterMenu CombatWaitFleeAdvance 2", "ShowEncounter 4 7 1",
            "EncounterMenu CombatWaitFleeAdvance 1", "ShowEncounter 4 7 0", "EncounterMenu CombatWaitFleeParley 0",
        ], rig.Ui.Calls);
    }

    [Fact]
    public void Encmenu_waits_until_the_party_stops_waiting()
    {
        var rig = new Rig(EncMenu(1, [0, 1, 1, 1, 4]));
        rig.Ui.Choices.Enqueue(1); // wait: both sides wait, menu again
        rig.Ui.Choices.Enqueue(0); // combat
        rig.Run();
        Assert.Equal((ushort)EncounterResult.Combat, rig[F]);
        Assert.Contains("Message BothSidesWait", rig.Ui.Calls);
    }

    [Fact]
    public void Encmenu_ends_where_the_game_waits_with_the_distance_already_set()
    {
        var rig = new Rig(EncMenu(2, [0, 1, 1, 1, 4]));
        rig.Map.Walls[(4, 5, 6)] = 1; // view 1
        rig.Ui.StopWhenInputMissing = true;
        Assert.Equal(EclStopReason.AwaitingInput, rig.Run().Reason);
        Assert.Equal(2, rig[0x6DC0]);
        Assert.Equal(1, rig[0x6DC1]);
        Assert.Equal(77, rig[F]); // nothing written yet
    }

    [Fact]
    public void Encmenu_reaction_5_or_more_writes_nothing()
    {
        var rig = new Rig(EncMenu(0, [5, 5, 5, 5, 5]));
        rig.Ui.Choices.Enqueue(0);
        rig.Run();
        Assert.Equal(77, rig[F]);
    }

    [Fact]
    public void Encmenu_flee_needs_the_slowest_member_fast_enough()
    {
        var fast = new Rig(EncMenu(0, [0, 0, 0, 0, 0], move1: 9), partyMovement: 9); // slowest 9 ≥ 9
        fast.Ui.Choices.Enqueue(2);
        fast.Run();
        Assert.Equal((ushort)EncounterResult.PartyFlees, fast[F]);
        var slow = new Rig(EncMenu(0, [0, 0, 0, 0, 0], move1: 10), partyMovement: 9);
        slow.Ui.Choices.Enqueue(2);
        slow.Run();
        Assert.Equal((ushort)EncounterResult.Combat, slow[F]);
    }

    [Fact]
    public void Monsters_flee_unless_attacked_by_a_faster_party()
    {
        Assert.Equal(EncounterResult.MonstersFlee, EncounterRules.Resolve(2, EncounterChoice.Wait, 0, 12, 6, 6, 6).Result);
        Assert.Equal(EncounterResult.Combat, EncounterRules.Resolve(2, EncounterChoice.Combat, 0, 12, 6, 6, 12).Result);
        var escape = EncounterRules.Resolve(2, EncounterChoice.Combat, 0, 12, 6, 6, 13);
        Assert.Equal(EncounterResult.MonstersFlee, escape.Result);
        Assert.Equal(EncounterMessage.MonstersFlee, escape.Message);
    }

    [Fact]
    public void Reactions_3_and_4_close_in_or_parley()
    {
        Assert.True(EncounterRules.Resolve(3, EncounterChoice.Wait, 1, 9, 9, 6, 6).Approach);
        Assert.Equal(EncounterMessage.BothSidesWait, EncounterRules.Resolve(3, EncounterChoice.Wait, 0, 9, 9, 6, 6).Message);
        Assert.Equal(EncounterResult.Parley, EncounterRules.Resolve(3, EncounterChoice.Parley, 0, 9, 9, 6, 6).Result);
        Assert.Equal(EncounterResult.Parley, EncounterRules.Resolve(4, EncounterChoice.Wait, 0, 9, 9, 6, 6).Result);
        Assert.True(EncounterRules.Resolve(4, EncounterChoice.Advance, 2, 9, 9, 6, 6).Approach);
        Assert.Equal(EncounterResult.PartyFlees, EncounterRules.Resolve(4, EncounterChoice.Flee, 2, 9, 9, 6, 6).Result);
        Assert.Equal(EncounterResult.PartyFlees, EncounterRules.Resolve(1, EncounterChoice.Flee, 2, 9, 1, 6, 6).Result);
    }

    [Fact]
    public void Text_for_a_distance_falls_back_to_the_next_farther_slot()
    {
        string[] t = ["NEAR", "", "FAR"];
        Assert.Equal("NEAR", EncounterRules.PickText(t, 0));
        Assert.Equal("FAR", EncounterRules.PickText(t, 1));
        Assert.Equal("FAR", EncounterRules.PickText(t, 2));
        Assert.Equal("NEAR", EncounterRules.PickText(["NEAR", "", ""], 2)); // wraps to slot 1
        Assert.Null(EncounterRules.PickText(["", "", ""], 0));
    }

    [Fact]
    public void Party_speed_applies_effects_but_starts_from_the_first_raw_value()
    {
        var a = new EclCharacter(); a[EncounterRules.MovementOffset] = 12; a.Effects.Add([EncounterRules.EffectDoubleMove]);
        var b = new EclCharacter(); b[EncounterRules.MovementOffset] = 9;
        var c = new EclCharacter(); c[EncounterRules.MovementOffset] = 18; c.Effects.Add([EncounterRules.EffectHalveMove]);
        Assert.Equal(((byte)24, (byte)9), EncounterRules.PartySpeed([a, b, c]));
        Assert.Equal(((byte)24, (byte)12), EncounterRules.PartySpeed([a])); // min keeps the raw 12
    }

    // ---- LOADMON / CLRMON --------------------------------------------------------------------------------------

    [Fact]
    public void Loadmon_appends_copies_with_five_icon_rolls_per_load()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.ClrMon)
            .Op(EclOpcodes.LoadMon, Imm8(2), Imm8(3), Imm8(5))
            .Op(EclOpcodes.LoadMon, Imm8(4), Imm8(0), Imm8(6))
            .Op(EclOpcodes.Exit));
        for (int i = 0; i < 10; i++)
            rig.Inner.RandomValues.Enqueue((ushort)(i % 6));
        rig.Run();
        Assert.Equal(10, rig.Inner.RandomLog.Count);
        Assert.All(rig.Inner.RandomLog, r => Assert.Equal(6, r.Range));
        var s = rig.Host.State;
        Assert.Equal(4, s.MonsterCount); // 3 + (count 0 → 1)
        Assert.True(s.MonstersLoaded);
        Assert.Equal(10, s.IconSlot);
        Assert.Equal(6, rig.Memory.Party!.Members.Count);
        var m = s.Monsters[0];
        Assert.Equal(8, m[0xBF]);
        Assert.Equal(2, m[0xC0]);
        Assert.Equal(EncounterRules.IconColour(1), m[0xC1]);
        Assert.Equal(0xC4, m[0xC4]);
        Assert.Equal(m.Record, s.Monsters[2].Record); // copies, not reloads
        Assert.Equal(9, s.Monsters[3][0xBF]);
        Assert.Equal(["LoadCombatIcon 5 8", "LoadCombatIcon 6 9"], rig.Ui.Calls);

        rig.Host.ClearMonsters();
        Assert.Equal(0, s.MonsterCount);
        Assert.False(s.MonstersLoaded);
        Assert.Equal(8, s.IconSlot);
        Assert.Equal(4, s.Monsters.Count); // CLRMON does not remove them
    }

    [Fact]
    public void Loadmon_stops_at_63_monsters()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.LoadMon, Imm8(1), Imm8(250), Imm8(1)).Op(EclOpcodes.LoadMon, Imm8(1), Imm8(1), Imm8(1)).Op(EclOpcodes.Exit));
        rig.Run();
        Assert.Equal(63, rig.Host.State.MonsterCount);
        Assert.Equal(5, rig.Inner.RandomLog.Count); // the second LOADMON loads nothing
    }

    // ---- COMBAT ------------------------------------------------------------------------------------------------

    [Fact]
    public void Combat_without_monsters_enters_shop_then_temple_then_after_combat()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x6E6C)).Op(EclOpcodes.Save, Imm8(1), Mem(0x6DE2))
            .Op(EclOpcodes.Save, Imm8(3), Mem(0x6DCA))
            .Op(EclOpcodes.Combat).Op(EclOpcodes.Combat).Op(EclOpcodes.Combat).Op(EclOpcodes.Exit));
        rig.Run();
        Assert.Equal(["Shop", "Temple", "Loot 0"], rig.Combat.Calls);
        Assert.Equal(0, rig[0x6E6C]);
        Assert.Equal(0, rig[0x6DE2]);
        Assert.Equal(1, rig[0x6DCA]);
    }

    [Fact]
    public void Combat_with_monsters_limits_the_distance_then_fights()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.LoadMon, Imm8(1), Imm8(2), Imm8(1))
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x6E6C)) // ignored while monsters are loaded
            .Op(EclOpcodes.Save, Imm8(2), Mem(0x6DC1))
            .Op(EclOpcodes.Combat).Op(EclOpcodes.Exit));
        rig.Map.Walls[(4, 5, 6)] = 1; // view 1
        rig.Run();
        Assert.Equal(["Fight 2", "Loot 0"], rig.Combat.Calls);
        Assert.Equal(1, rig[0x6DC1]);
        Assert.Equal(1, rig[0x6E6C]);
        // The after-combat step removed the two monsters (out of play: counted in $6DC8); the party size stays.
        Assert.Equal(2, rig.Memory.Party!.Members.Count);
        Assert.Empty(rig.Host.State.Monsters);
        Assert.Equal(2, rig[0x6DC8]);
        Assert.Equal(2, rig[0x6E3E]);
    }

    [Fact]
    public void Combat_with_a_fight_builder_hands_the_built_fight_to_the_runner()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.LoadMon, Imm8(1), Imm8(2), Imm8(1)).Op(EclOpcodes.Combat).Op(EclOpcodes.Exit));
        Fight? built = null;
        rig.Host.FightBuilder = s => built = new Fight(rig.Memory, s, new BaseItemTable([]), _ => 0,
            new EndActionsController(), new GridGeometry());
        rig.Run();
        Assert.NotNull(built);
        Assert.Equal(["Loot 0"], rig.Combat.Calls); // not the stand-in Fight(state)
    }

    /// <summary>A fight that leaves every party member dead.</summary>
    private sealed class PartyDiesRunner : ICombatRunner
    {
        public void Fight(EncounterState state)
        {
            foreach (var c in state.Monsters)
                c[CombatRules.InPlay] = 1;
        }
        public void Loot(EncounterState state, AfterCombatOutcome outcome) => throw new InvalidOperationException("no loot after a defeat");
        public void PartyDestroyed(EncounterState state) { }
        public void Shop(EncounterState state) { }
        public void Temple(EncounterState state) { }
        public byte[] MakeRandomItem(byte type) => new byte[EclCharacter.ItemRecordSize];
    }

    [Fact]
    public void A_destroyed_party_stops_the_run_after_COMBAT()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.LoadMon, Imm8(1), Imm8(1), Imm8(1))
            .Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Save, Imm8(9), Mem(0x4A00)) // not reached: DS:49FF stops the run loop
            .Op(EclOpcodes.Exit), combat: new PartyDiesRunner());
        foreach (var c in rig.Memory.Party!.Members.ToList())
            c[CombatRules.Status] = (byte)CreatureStatus.Dead;
        var r = rig.Run();
        Assert.Equal(EclStopReason.ExternalStop, r.Reason);
        Assert.True(rig.Vm.ExternalStop);
        Assert.Equal(0, rig[0x4A00]);
        Assert.Equal(AfterCombat.ResultDestroyed, rig[AfterCombat.CombatResult]);
    }

    [Fact]
    public void A_won_fight_clears_the_defeated_flag()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.LoadMon, Imm8(1), Imm8(1), Imm8(1))
            .Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Save, Imm8(9), Mem(0x4A00))
            .Op(EclOpcodes.Exit));
        var r = rig.Run();
        Assert.Equal(EclStopReason.Exit, r.Reason);
        Assert.False(rig.Vm.ExternalStop);
        Assert.Equal(9, rig[0x4A00]);
    }

    [Fact]
    public void Combat_outdoors_leaves_distance_2()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.LoadMon, Imm8(1), Imm8(1), Imm8(1)).Op(EclOpcodes.Combat).Op(EclOpcodes.Exit), outdoors: true);
        rig.Run();
        Assert.Equal(2, rig[0x6DC1]);
    }

    // ---- TREASURE, PARLEY, PICTURE, SPRITEOFF ------------------------------------------------------------------

    [Fact]
    public void Treasure_sets_money_and_prepends_the_item_list()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Treasure, Imm16(500), Imm8(2), Imm8(3), Imm8(4), Imm8(5), Imm8(6), Imm8(7), Imm8(9))
            .Op(EclOpcodes.Treasure, Imm8(1), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(255))
            .Op(EclOpcodes.Exit));
        rig.Run();
        var s = rig.Host.State;
        Assert.Equal([1u, 0, 0, 0, 0, 0, 0], s.Money); // set, not added
        Assert.Equal([3, 2, 1], s.TreasureItems.Select(i => (int)i[0]));
    }

    [Fact]
    public void Treasure_random_items_roll_their_types()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Treasure, Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(130))
            .Op(EclOpcodes.Exit));
        // item 1: 1d100 = 70 (61..85 → type 61), then the scroll: 1d3 = 1 spell, 1d3 = level 1, 1d13 = 5 (→ spell 13);
        // item 2: 1d100 = 10, then 1d100 = 45 (→ 59), then the plus 1d20 = 15 (→ +2)
        foreach (var v in new ushort[] { 69, 0, 0, 4, 9, 44, 14 })
            rig.Inner.RandomValues.Enqueue(v);
        rig.Run();
        Assert.Empty(rig.Inner.RandomValues);
        var items = rig.Host.State.TreasureItems; // head first: the last item made
        Assert.Equal(2, items.Count);
        Assert.Equal(59, items[0][0x2E]);
        Assert.Equal(2, items[0][0x32]);
        Assert.Equal(5000, items[0][0x3A] | items[0][0x3B] << 8);
        Assert.Equal(61, items[1][0x2E]);
        Assert.Equal(13, items[1][0x3C]);
        Assert.Equal(300, items[1][0x3A] | items[1][0x3B] << 8);
    }

    /// <summary>
    /// A second roll of 48/49 leaves the type byte as it was: for the first item that is the residue DOSBox-X read
    /// at the v1.3 sites (0: a blank type-0 item, no generator draw), later the previous item's type (s18 probe).
    /// </summary>
    [Fact]
    public void Treasure_second_roll_48_keeps_the_residue_then_the_previous_type()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Treasure, Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(0), Imm8(131))
            .Op(EclOpcodes.Exit));
        // item 1: 1d100 = 10, 1d100 = 48 → the residue (0): no further draw; item 2: 1d100 = 10, 1d100 = 20 (type 20),
        // plus 1d20 = 1; item 3: 1d100 = 10, 1d100 = 49 → type 20 again, plus 1d20 = 20 (+3)
        foreach (var v in new ushort[] { 9, 47, 9, 19, 0, 9, 48, 19 })
            rig.Inner.RandomValues.Enqueue(v);
        rig.Run();
        Assert.Empty(rig.Inner.RandomValues);
        Assert.Equal(0, rig.Host.FirstItemTypeResidue);
        var items = rig.Host.State.TreasureItems; // head first
        Assert.Equal([20, 20, 0], items.Select(i => (int)i[0x2E]));
        Assert.Equal(6, items[2][0x35]);              // blank: only `hidden` 6
        Assert.Equal([3, 1], items.Take(2).Select(i => (int)i[0x32]));
    }

    [Fact]
    public void Random_item_type_table()
    {
        static byte Type(params int[] dice)
        {
            int k = 0;
            return EncounterRules.RandomItemType(_ => dice[k++] - 1, 99);
        }
        Assert.Equal(20, Type(5, 20));
        Assert.Equal(99, Type(5, 48)); // unset in the program: previous type
        Assert.Equal(36, Type(5, 70, 4));
        Assert.Equal(38, Type(5, 70, 10));
        Assert.Equal(73, Type(5, 91));
        Assert.Equal(77, Type(5, 100));
        Assert.Equal(62, Type(90));
        Assert.Equal(67, Type(95, 16));
        Assert.Equal(69, Type(100));
    }

    [Fact]
    public void Parley_writes_the_value_of_the_attitude_picked()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Parley, Imm8(10), Imm8(11), Imm8(12), Imm8(13), Imm8(14), Mem(F))
            .Op(EclOpcodes.Exit));
        rig.Ui.ParleyChoices.Enqueue(3);
        rig.Run();
        Assert.Equal(13, rig[F]);
    }

    [Fact]
    public void Picture_uses_the_head_cell_and_255_restores_the_view()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Picture, Imm8(7))
            .Op(EclOpcodes.Save, Imm8(3), Mem(0x6DE1))
            .Op(EclOpcodes.Picture, Imm8(8))
            .Op(EclOpcodes.Picture, Imm8(255))
            .Op(EclOpcodes.SpriteOff)
            .Op(EclOpcodes.Exit));
        rig.Run();
        Assert.Equal(["ShowPicture 7 -", "ShowPicture 8 3", "RestoreView"], rig.Ui.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Call_2C90_takes_the_picture_off_only_after_files_were_loaded(bool filesLoaded)
    {
        // 0x3A54 (s21): the redraw needs DS:4431 and a redraw flag (here the picture's, DS:84E0).
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Picture, Imm8(7))
            .Op(EclOpcodes.Call, Mem(0x2C90))
            .Op(EclOpcodes.Exit));
        rig.Memory.FilesLoadedSinceSwitch = filesLoaded;
        rig.Run();
        Assert.Equal(filesLoaded ? ["ShowPicture 7 -", "RestoreView", "ViewRedrawn"] : ["ShowPicture 7 -"], rig.Ui.Calls);
    }

    [Fact]
    public void Call_2C90_redraws_after_a_position_write_once()
    {
        // 0x3A54–0x3A9C: DS:84E6 (set by the write to $C04B, 0x823F) is enough for the redraw, which draws the position
        // line (0x3A83) and clears the flags, so a second CALL $2C90 redraws nothing.
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm8(3), Mem(0xC04B))
            .Op(EclOpcodes.Call, Mem(0x2C90))
            .Op(EclOpcodes.Call, Mem(0x2C90))
            .Op(EclOpcodes.Exit));
        rig.Memory.FilesLoadedSinceSwitch = true;
        rig.Memory.ViewRedrawFlags = ViewRedraw.None;
        rig.Run();
        Assert.Equal(["ViewRedrawn"], rig.Ui.Calls);
        Assert.Equal(ViewRedraw.None, rig.Memory.ViewRedrawFlags);
    }

    // ---- Real data ---------------------------------------------------------------------------------------------

    [PorFact]
    public void Monster_files_load_records_items_and_effects()
    {
        var data = new PorEncounterData(PorData.Open());
        var goblin = data.Monster(2, 2);
        Assert.NotNull(goblin);
        Assert.Equal(EclCharacter.RecordSize, goblin.Record.Length);
        Assert.NotEmpty(goblin.Items);
        Assert.Null(data.Monster(2, 250));
    }
}
