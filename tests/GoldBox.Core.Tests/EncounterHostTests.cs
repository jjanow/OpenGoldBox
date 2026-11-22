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
    public void A_second_close_up_in_the_same_run_is_skipped_while_the_head_is_the_same()
    {
        // 0x7ABC–0x7AD6 (DOSBox-X s39, save A: SETUPMON 1, 0, 2; SETUPMON 1, 0, 25 left PIC 2 up).
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(0), Imm8(7))
            .Op(EclOpcodes.SetupMon, Imm8(5), Imm8(0), Imm8(8))
            .Op(EclOpcodes.Exit));
        rig.Memory.Write(EncounterHost.HeadPicture, 255);
        rig.Run();
        Assert.Equal(["ShowEncounter 4 7 0"], rig.Ui.Calls);
        Assert.Equal(255, rig.Memory.CloseUpHead);
        Assert.False(rig.Memory.CloseUpUp); // EXIT cleared DS:84C8–84C9 (0xDC6)
        Assert.False(rig.Memory.EncounterSpriteUp);
    }

    [Fact]
    public void A_close_up_with_another_head_is_drawn_again_but_no_sprite_after_one()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(0), Imm8(7))
            .Op(EclOpcodes.Save, Imm8(3), Mem(EncounterHost.HeadPicture))
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(0), Imm8(8))
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(8)) // a close-up is up: no sprite (0x7A2A, 0x7A4B)
            .Op(EclOpcodes.Exit));
        rig.Memory.Write(EncounterHost.HeadPicture, 255);
        rig.Run();
        Assert.Equal(["ShowEncounter 4 7 0", "ShowEncounter 4 8 0"], rig.Ui.Calls);
        Assert.Equal(3, rig.Memory.CloseUpHead);
    }

    [Fact]
    public void After_PICTURE_a_sprite_in_the_same_run_is_not_drawn()
    {
        // PICTURE sets DS:84C9 (0x15F3) but not DS:84EA (DOSBox-X s39, save A: PICTURE 2; SETUPMON 1, 2, 25 left PIC 2).
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Picture, Imm8(2))
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(7))
            .Op(EclOpcodes.Exit));
        rig.Memory.Write(EncounterHost.HeadPicture, 255);
        rig.Run();
        Assert.Equal(["ShowPicture 2 -"], rig.Ui.Calls);
    }

    [Fact]
    public void PICTURE_255_ends_the_skip_and_the_next_sprite_is_loaded_again()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(0), Imm8(7))
            .Op(EclOpcodes.Picture, Imm8(255))
            .Op(EclOpcodes.SetupMon, Imm8(5), Imm8(2), Imm8(8))
            .Op(EclOpcodes.Exit));
        rig.Memory.Write(EncounterHost.HeadPicture, 255);
        rig.Run();
        Assert.Equal(["ShowEncounter 4 7 0", "RestoreView", "ShowEncounter 5 8 2"], rig.Ui.Calls);
    }

    [Fact]
    public void While_the_sprite_is_loaded_a_new_sprite_number_draws_the_first_sprite()
    {
        // 0x7A48–0x7A93: the frames are loaded only while DS:84C8 is clear; SPRITEOFF does not clear it.
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(7))
            .Op(EclOpcodes.SpriteOff)
            .Op(EclOpcodes.SetupMon, Imm8(5), Imm8(2), Imm8(8))
            .Op(EclOpcodes.Exit));
        rig.Run();
        Assert.Equal(["ShowEncounter 4 7 2", "RestoreView", "ShowEncounter 4 8 2"], rig.Ui.Calls);
    }

    [Fact]
    public void On_the_overland_approach_and_combat_redraw_the_marker()
    {
        // 0x7A3A (DOSBox-X s39, save-overland 26 at 10,20: SETUPMON 0, 2, 0; APPROACH drew the next marker frame under
        // the sprite) and COMBAT's START 0x3379 (case 3, 0x2635).
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(7))
            .Op(EclOpcodes.Approach)
            .Op(EclOpcodes.Approach)
            .Op(EclOpcodes.ClrMon)
            .Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Exit), outdoors: true);
        rig.Memory.ViewMode = 3;
        rig.Memory.PreviousViewMode = 3;
        rig.Run();
        Assert.Equal(3, rig.Memory.MarkerCounter);
        // Distance 0 on the overland: the sprite (slot 2), no close-up (0x7AE2).
        Assert.Equal(["ShowEncounter 4 7 2", "ShowEncounter 4 7 1", "ShowEncounter 4 7 0"], rig.Ui.Calls.Take(3));
        Assert.False(rig.Memory.PictureShown);
    }

    [Fact]
    public void The_encounter_draw_turns_the_area_map_off()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.SetupMon, Imm8(4), Imm8(2), Imm8(7)).Op(EclOpcodes.Exit));
        rig.Memory.AreaMapShown = true;
        rig.Run();
        Assert.False(rig.Memory.AreaMapShown); // 0x7A62
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

    // ---- DS:4A00, the text routine's typing flag ------------------------------------------------------------------

    [Fact]
    public void Typing_is_on_from_the_start_up_and_print_and_encmenu_leave_it_off()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Print, Text("A")).Op(EclOpcodes.Exit));
        Assert.True(rig.Memory.TypesText); // the start-up 0xE974
        rig.Run();
        Assert.False(rig.Memory.TypesText); // PRINT: 1 for its text (0x1763), 0 after it (0x17DC)

        var menu = new Rig(EncMenu(1, [0, 1, 1, 1, 4]));
        menu.Memory.TypesText = false;
        menu.Ui.Choices.Enqueue(0); // combat
        menu.Run();
        Assert.False(menu.Memory.TypesText); // ENCMENU: 1 on entry (0x2C4F), 0 at its exit (0x32E0)
    }

    [Fact]
    public void Typing_is_on_after_every_fight()
    {
        var memory = new EclMemory { TypesText = false };
        var step = new AfterCombat(memory, new EncounterState(), _ => 0, []);
        step.Finish(new AfterCombatOutcome(false, false, true, false, 0, 0));
        Assert.True(memory.TypesText); // the after-combat end 0x6BAF
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
    public void Every_combat_ends_with_the_interface_drawn_for_the_explore_state()
    {
        // GAME.OVR 0x25F7–0x2635: DS:49F3 := 4 (3D) or 3, then START 0x3379 after the shop, the temple and a loot alike
        // (DOSBox-X s47: a loot's exit question is gone before the next COMBAT's shop draws).
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x6E6C)).Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Save, Imm8(1), Mem(0x6DE2)).Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Combat).Op(EclOpcodes.Exit));
        var drawn = new List<string>();
        rig.Host.InterfaceRedraw = () => drawn.Add($"{rig.Combat.Calls.Count}:{rig.Memory.InterfaceState}");
        rig.Memory.ViewMode = 1;
        rig.Run();
        Assert.Equal(["1:4", "2:4", "3:4"], drawn);
        Assert.Equal(0, rig.Memory.MarkerCounter); // the hook draws; the host adds no marker step of its own
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
    public void Picture_sets_the_close_up_flag_and_255_rearms_the_animation_only_when_it_redraws()
    {
        // DS:84E0 / 84E1 (por-art-formats.md §3): PICTURE sets 84E0 (0x15F8); a head + body clears 84E1 (0x79E7);
        // PICTURE 255 redraws, clears 84E0 and sets 84E1 only while a picture or the sprite is up (0x1667–0x1684).
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Exit));
        var m = rig.Memory;
        m.PictureArmed = true;
        rig.Host.Picture(7);
        Assert.True(m.PictureShown);
        Assert.True(m.PictureArmed);
        m.Write(0x6DE1, 3);
        rig.Host.Picture(8);
        Assert.True(m.PictureShown);
        Assert.False(m.PictureArmed);
        rig.Host.Picture(255);
        Assert.False(m.PictureShown);
        Assert.True(m.PictureArmed);
        m.PictureArmed = false;
        rig.Host.Picture(255); // nothing up: no redraw, the arm stays as it was
        Assert.False(m.PictureArmed);
        Assert.Equal(["ShowPicture 7 -", "ShowPicture 8 3", "RestoreView"], rig.Ui.Calls);
    }

    [Fact]
    public void Picture_255_right_after_a_switch_to_the_overland_leaves_the_flags()
    {
        // 0x1659–0x1665: with DS:49F9 ≤ 1 and DS:49FA ≠ 1 the redraw branch is skipped.
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Exit));
        var m = rig.Memory;
        rig.Host.Picture(7);
        m.PreviousViewMode = 1;
        m.ViewMode = 2;
        rig.Host.Picture(255);
        Assert.True(m.PictureShown);
        Assert.Equal(["ShowPicture 7 -"], rig.Ui.Calls);
        m.PreviousViewMode = 2;
        rig.Host.Picture(255);
        Assert.False(m.PictureShown);
        Assert.True(m.PictureArmed);
    }

    [Fact]
    public void Exit_clears_the_close_up_flag_but_not_the_arm()
    {
        // EXIT 0xDD7: DS:84E0 := 0; DS:84E1 is not written.
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Picture, Imm8(7)).Op(EclOpcodes.Exit));
        rig.Memory.PictureArmed = true;
        rig.Run();
        Assert.False(rig.Memory.PictureShown);
        Assert.True(rig.Memory.PictureArmed);
    }

    [Fact]
    public void Protect_clears_the_close_up_flag_so_picture_255_does_not_redraw()
    {
        // PROTECT 0x3BDA: DS:84E0 := 0, so the PICTURE 255 after it finds nothing up (0x1667) and arms nothing.
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Picture, Imm8(7))
            .Op(EclOpcodes.Protect, Mem(0x9F00))
            .Op(EclOpcodes.Picture, Imm8(255))
            .Op(EclOpcodes.Exit));
        rig.Run();
        Assert.DoesNotContain("RestoreView", rig.Ui.Calls);
        Assert.False(rig.Memory.PictureArmed);
    }

    [Fact]
    public void Script_init_and_call_2C90_clear_the_close_up_flag_without_arming()
    {
        var rig = new Rig(new EclAsm()
            .Op(EclOpcodes.Picture, Imm8(7))
            .Op(EclOpcodes.Call, Mem(0x2C90))
            .Op(EclOpcodes.Exit));
        rig.Memory.FilesLoadedSinceSwitch = true;
        rig.Memory.PictureArmed = false;
        rig.Run();
        Assert.False(rig.Memory.PictureShown);
        Assert.False(rig.Memory.PictureArmed); // 0x3A97 clears DS:84E0 only
        rig.Memory.PictureShown = true;
        rig.Vm.InitScript(); // 0x768F
        Assert.False(rig.Memory.PictureShown);
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(3, false)]
    public void The_encounter_close_up_sets_the_close_up_flag_and_a_head_clears_the_arm(int head, bool armed)
    {
        // 0x7B09: DS:84E0 := 1 at distance 0 in the 3D view; with a head 0x79E4 / 0x7B77 clear DS:84E1.
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Exit));
        rig.Memory.Write(0x6DE1, (ushort)head);
        rig.Memory.PictureArmed = true;
        rig.Host.SetupMonster(4, 0, 7);
        Assert.True(rig.Memory.PictureShown);
        Assert.Equal(armed, rig.Memory.PictureArmed);
    }

    [Fact]
    public void The_encounter_sprite_at_a_distance_leaves_the_close_up_flag()
    {
        var rig = new Rig(new EclAsm().Op(EclOpcodes.Exit));
        rig.Host.SetupMonster(4, 2, 7);
        Assert.False(rig.Memory.PictureShown);
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
