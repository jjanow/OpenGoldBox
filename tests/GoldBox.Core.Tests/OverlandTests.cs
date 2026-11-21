using System.Security.Cryptography;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The overland rules (GAME.OVR 0x16EE3 step, 0x2DD9B window, 0x2DB87 marker; program read s19), headless.</summary>
public class OverlandRulesTests
{
    /// <summary>A 44 × 36 map of grass (2) with a few tiles set.</summary>
    private static OverlandMap Map(params (int X, int Y, byte Tile)[] set)
    {
        var t = Enumerable.Repeat((byte)2, 44 * 36).ToArray();
        foreach (var (x, y, v) in set)
            t[y * 44 + x] = v;
        return new OverlandMap(44, 36, t, []);
    }

    private static EclMemory At(int mode, int x, int y, int facing)
    {
        var m = new EclMemory { ViewMode = (byte)mode };
        m.Write(Overland.CellX, (ushort)x);
        m.Write(Overland.CellY, (ushort)y);
        m.Position.Facing = (byte)facing;
        return m;
    }

    [Fact]
    public void A_step_moves_one_square_and_takes_twelve_hours()
    {
        var m = At(3, 5, 10, Facing.East);
        var calls = new List<(int Unit, int Count)>();
        Assert.Equal(OverlandStep.Moved, Overland.Step(m, Map(), 13, (u, c) => calls.Add((u, c))));
        Assert.Equal((6, 10), Overland.Position(m));
        Assert.Equal([(GameClock.Hours, 12)], calls);
    }

    [Theory]
    [InlineData(0x4E)]
    [InlineData(0x7E)]
    [InlineData(0x80)]
    [InlineData(0x82)]
    [InlineData(0xAC)]
    [InlineData(0xAE)]
    [InlineData(0xB3)]
    [InlineData(0xBC)]
    [InlineData(0xDD)]
    [InlineData(0xE1)]
    [InlineData(0xE9)]
    [InlineData(0xF5)]
    public void Impassable_tiles_refuse_without_time(byte tile)
    {
        // Column 13 + x 6 = map column 19.
        var m = At(3, 5, 10, Facing.East);
        int clock = 0;
        Assert.Equal(OverlandStep.Blocked, Overland.Step(m, Map((19, 10, tile)), 13, (_, _) => clock++));
        Assert.Equal((5, 10), Overland.Position(m));
        Assert.Equal(0, clock);
    }

    [Theory]
    [InlineData(0x7D, false)]
    [InlineData(0x81, false)]
    [InlineData(0xAD, false)]
    [InlineData(0xF4, false)]
    [InlineData(0x66, true)]
    [InlineData(0x6C, true)]
    [InlineData(0x72, true)]
    [InlineData(0x6D, false)]
    public void Tile_classes_are_the_traced_ranges(byte tile, bool river)
    {
        Assert.False(Overland.Impassable(tile));
        Assert.Equal(river, Overland.River(tile));
    }

    [Fact]
    public void A_river_turns_the_party_back_until_4AA2_is_set()
    {
        var map = Map((19, 10, 0x68));
        var m = At(3, 5, 10, Facing.East);
        Assert.Equal(OverlandStep.River, Overland.Step(m, map, 13, (_, _) => throw new InvalidOperationException()));
        Assert.Equal((5, 10), Overland.Position(m));
        m.Write(Overland.RiverCell, 1);
        Assert.Equal(OverlandStep.Moved, Overland.Step(m, map, 13, (_, _) => { }));
        Assert.Equal((6, 10), Overland.Position(m));
    }

    [Fact]
    public void Steps_are_clamped_to_the_scripts_window()
    {
        var m = At(2, 0, 0, Facing.West);
        Assert.Equal(OverlandStep.Moved, Overland.Step(m, Map(), 0, (_, _) => { }));
        Assert.Equal((0, 0), Overland.Position(m));
        m = At(4, 15, 35, Facing.South);
        Assert.Equal(OverlandStep.Moved, Overland.Step(m, Map(), 26, (_, _) => { }));
        Assert.Equal((15, 35), Overland.Position(m));
        // The clamped square is the one tested: east of x 15 in mode 3 is column 28, not 29.
        m = At(3, 15, 4, Facing.East);
        Assert.Equal(OverlandStep.Blocked, Overland.Step(m, Map((28, 4, 0x4E), (29, 4, 2)), 13, (_, _) => { }));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0, 0)]
    [InlineData(13, 10, 20, 21, 18, 2, 2)]
    [InlineData(26, 15, 0, 39, 0, 2, 0)]
    [InlineData(0, 1, 35, 0, 31, 1, 4)]
    [InlineData(0, 0, 34, 0, 31, 0, 3)]
    [InlineData(0, 5, 33, 3, 31, 2, 2)]
    public void Window_and_marker_follow_the_clamps(int column, int x, int y, int left, int top, int col, int row)
    {
        Assert.Equal((left, top), Overland.ViewOrigin(column, x, y));
        Assert.Equal((col, row), Overland.MarkerCell(column, x, y));
    }

    [Fact]
    public void The_marker_columns_past_41_follow_the_program_thresholds()
    {
        Assert.Equal(3, Overland.MarkerCell(26, 16, 5).Col); // column 42
        Assert.Equal(4, Overland.MarkerCell(26, 17, 5).Col); // column 43
    }

    [Fact]
    public void Marker_frames_cycle_through_three_icon_blocks_and_mirror_south_and_west()
    {
        Assert.Equal([0, 128, 1, 129, 2, 130], Enumerable.Range(0, 6).Select(c => Overland.MarkerFrame(c, 0).IconBlock));
        Assert.False(Overland.MarkerFrame(0, Facing.East).Mirrored);
        Assert.True(Overland.MarkerFrame(0, Facing.South).Mirrored);
        Assert.True(Overland.MarkerFrame(0, Facing.West).Mirrored);
    }

    [Theory]
    [InlineData(2, 4, 7, 4, 7)]
    [InlineData(3, 10, 20, 23, 20)]
    [InlineData(4, 15, 0, 41, 0)]
    public void The_position_line_adds_the_modes_immediates(int mode, int x, int y, int sx, int sy)
    {
        Assert.Equal((sx, sy), Overland.StatusPosition(At(mode, x, y, 0)));
    }

    private sealed class Ui : IAreaUi
    {
        public List<AreaMessage> Messages { get; } = [];
        public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices) => null;
        public void Bump() { }
        public void Message(AreaMessage message) => Messages.Add(message);
        public bool Camp() => false;
    }

    /// <summary>An area loop on an empty script (every vector EXITs) on the overland.</summary>
    private static (AreaLoop Loop, Ui Ui) Loop(OverlandMap map, int mode, int x, int y)
    {
        var m = new EclMemory();
        var vm = new EclVm(m, new ScriptedEclHost());
        vm.LoadScriptBlock(new EclAsm().Op(EclOpcodes.Exit).Build(), 26, 7);
        m.ViewMode = (byte)mode;
        m.Write(AreaLoop.View3DCell, 0); // the script init set it to 1; the wilderness scripts write 0 outdoors
        m.Write(Overland.CellX, (ushort)x);
        m.Write(Overland.CellY, (ushort)y);
        var ui = new Ui();
        var loop = new AreaLoop(vm, new TurboPascalRandom(1), ui) { OverlandMap = map, OverlandColumn = md => md == 3 ? 13 : null };
        return (loop, ui);
    }

    [Fact]
    public void Arrows_set_the_facing_and_step_through_the_vectors()
    {
        var (loop, _) = Loop(Map(), 3, 5, 10);
        long before = GameClock.TotalMinutes(loop.Memory);
        loop.Execute(AreaCommand.TravelSouth);
        Assert.Equal(Facing.South, loop.Memory.Position.Facing);
        Assert.Equal((5, 11), Overland.Position(loop.Memory));
        Assert.Equal(12 * 60, GameClock.TotalMinutes(loop.Memory) - before);
        Assert.Equal(3, loop.Memory.PreviousViewMode);
    }

    [Fact]
    public void The_3D_commands_do_nothing_on_the_overland_and_travel_does_nothing_in_3D()
    {
        var (loop, _) = Loop(Map(), 3, 5, 10);
        loop.Execute(AreaCommand.Forward);
        loop.Execute(AreaCommand.TurnLeft);
        loop.Execute(AreaCommand.AreaMap);
        Assert.Equal((5, 10), Overland.Position(loop.Memory));
        Assert.Equal(0, loop.Memory.Position.Facing);
        Assert.False(loop.ShowAreaMap);
        loop.Memory.ViewMode = 1;
        loop.Execute(AreaCommand.TravelEast);
        Assert.Equal(0, loop.Memory.Position.Facing);
    }

    [Fact]
    public void A_cancelled_step_and_a_river_take_no_time()
    {
        var (loop, ui) = Loop(Map((19, 10, 0x6A)), 3, 5, 10); // x 6 of mode 3 is column 19
        long t0 = GameClock.TotalMinutes(loop.Memory);
        loop.Execute(AreaCommand.TravelEast);
        Assert.Equal([AreaMessage.RiverBlocks], ui.Messages);
        Assert.Equal(t0, GameClock.TotalMinutes(loop.Memory));
        loop.Memory.Write(AreaLoop.CancelStep, 255);
        loop.Step();
        Assert.Equal(0, loop.Memory.Read(AreaLoop.CancelStep));
        Assert.Equal((5, 10), Overland.Position(loop.Memory));
    }

    [Fact]
    public void The_interface_draw_and_each_redraw_advance_the_marker_counter()
    {
        var (loop, _) = Loop(Map(), 3, 5, 10);
        loop.Memory.PreviousViewMode = 1; // just arrived
        loop.MarkerCounter = 2;
        Assert.True(loop.OverlandInterfacePending);
        loop.Execute(AreaCommand.TravelEast);
        Assert.False(loop.OverlandInterfacePending);
        Assert.Equal(5, loop.MarkerCounter); // interface 2 + 3, the step's redraw 4
        loop.Execute(AreaCommand.Search);
        Assert.Equal(5, loop.MarkerCounter);
        loop.Execute(AreaCommand.TravelWest);
        Assert.Equal(0, loop.MarkerCounter);
    }
}

/// <summary>
/// The overland view against DOSBox-X (PoR v1.3, EGA, 2026-11-18, s19): save A's party put on the overland with
/// <see cref="PorSaveGame.PlaceOnOverland"/> (tool <c>save-overland</c>), loaded from the main menu, screenshotted,
/// then after each arrow key. Hashes are of DOSBox-X's 120 × 120 view at pixel (8, 8) (<c>overland-compare</c>: 0
/// differing pixels, marker included); the position line (x, y, facing, time) matched too. Cases: script 26 at 10,20
/// and a walk E S W N (one redraw per step: the marker frames 4, 5, 0, 1 after the load's 2 + 3, mirrored S and W);
/// a river refused ($4AA2 = 0, no time); an impassable tile; the corner 0,0 of script 25 (a step north off the edge
/// is cancelled by the script's vector 0, no time); script 27's east edge; the bottom-left clamp (window top 31,
/// marker row 4); the nomad camp shown ($4A9F = 1); the pyramid site with $4AB3 = 254.
/// </summary>
public class OverlandViewTests
{
    public static TheoryData<int, string, string, string, string> Cases() => new()
    {
        { 26, "10,20,N", "", "", "5e06d1726763c19f0485b0838325d1809860cc0eeaec6a76668934ddc570e985" },
        { 26, "10,20,N", "", "E", "6136dd3d233991a781550b165d66d90fe9baf6cc181724278bc2ec4842dedab2" },
        { 26, "10,20,N", "", "ES", "da1261df83e04cb912235e711ea700a3d019c442c8f2c4b96bd5a466d946fa03" },
        { 26, "10,20,N", "", "ESW", "93e9a66c0d9397fb8c62e31d56aff4daeb378815b1e1ecb7916c1739d2fa95b7" },
        { 26, "10,20,N", "", "ESWN", "06434e8a1c60b1b8ed5722a685a247535224c11dec3b3a4b2aff4e14434a60e3" },
        { 26, "5,19,E", "", "", "08906acf91dee81093c80ad26755e6d1128f41616aaf76fa8b72dbbf929be103" },
        { 26, "5,19,E", "", "E", "abf63ec14d62ef1c2e8200dbf81b216599addf72472af22c2a1325d280ab2b6b" },
        { 25, "14,29,S", "", "", "d74b5fe5b4c9a5d61a55850459ec066b7d1fb35486f9da495825c8f44670a9a4" },
        { 25, "14,29,S", "", "S", "502bdcc9468d3730912925e84e16511fe159946c4561e0cbc4098d8e55c2f468" },
        { 25, "0,0,N", "", "", "757e762d13eab33d4a35fd40e85b65b55155ca0300530886b968ed0e7b60578a" },
        { 25, "0,0,N", "", "N", "6c3f359eb44815d3f33a0698f8746735ddd1b03744528d4a616027f7431ab5c8" },
        { 27, "15,0,E", "", "", "e1fd9fe68ba7565213dd86fc76a2d0a93659a37d5bd9cd22bd53317d035c40ba" },
        { 27, "15,0,E", "", "E", "dc995855dec9901735c30b1a883beef7976f8f4e07440f6e68cd1f129bed524e" },
        { 25, "0,35,N", "", "", "b76abd055ea49e6ae9916904b76126882e350e229c4431b77049ed534f486890" },
        { 25, "0,35,N", "", "N", "451938656e67cfb9949e977cfb34293b708eb53668e8116f1c0ffab5f788bd0c" },
        { 26, "12,13,N", "4A9F=1", "", "2a04c52b16790eac1027b7448f6e5f7f71b929ba6c386e17bb10b6618fca45ee" },
        { 26, "6,18,N", "4AB3=254", "", "8d8d70cb10ec421f91570bfb8bcbbaf4c99bd09b9b9c38511e2b2c24b6da4464" },
    };

    private sealed class Ui : IAreaUi
    {
        public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices) => null;
        public void Bump() { }
        public void Message(AreaMessage message) { }
        public bool Camp() => false;
    }

    internal static PorGame Load(int script, string where, string cells, ScriptedEclHost? host = null, int markerCounter = 0, uint seed = 0)
    {
        var dir = PorData.Open();
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var p = where.Split(',');
        int facing = p[2] switch { "N" => 0, "E" => 2, "S" => 4, _ => 6 };
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, script, int.Parse(p[0]), int.Parse(p[1]), facing);
        foreach (var kv in cells.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = kv.Split('=');
            int o = PorSaveGame.Off4900 + 2 * (Convert.ToInt32(c[0], 16) - 0x4900);
            save[o] = (byte)int.Parse(c[1]);
            save[o + 1] = (byte)(int.Parse(c[1]) >> 8);
        }
        var player = new PorPlayer(host ?? new ScriptedEclHost { StopWhenInputMissing = true },
            new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), new Ui());
        return PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!, player, seed,
            markerCounter: markerCounter);
    }

    [PorTheory]
    [MemberData(nameof(Cases))]
    public void View_matches_dosbox(int script, string where, string cells, string keys, string sha256)
    {
        // Seed 1: no random encounter on the walk, as in DOSBox-X (seed 0 meets one at the second step, whose SPRITEOFF
        // redraw draws the marker once more).
        var game = Load(script, where, cells, seed: 1);
        foreach (char k in keys)
            game.Loop.Execute(k switch
            {
                'N' => AreaCommand.TravelNorth,
                'E' => AreaCommand.TravelEast,
                'S' => AreaCommand.TravelSouth,
                _ => AreaCommand.TravelWest,
            });
        Assert.True(Overland.Shown(game.Memory));
        var art = PorOverlandArt.Load(game.Content);
        var frame = art.Render(game.Loop, game.Content.Overland()!, game.Content.OverlandColumn(game.Memory.ViewMode - Overland.FirstMode + PorOverland.FirstScript)!.Value);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(frame.Pixels)));
    }

    /// <summary>
    /// The icon editor's end on the overland (0x1D84A) loads ICON block 0 into the marker's animation slot 11 (DOSBox-X
    /// s25: camp, Alter Icon, then a walk drew blocks 0 / 128 for counters 4 / 5) until the overland interface loads the
    /// slots again; in 3D it changes nothing.
    /// </summary>
    [PorFact]
    public void Icon_editor_on_the_overland_puts_icon_0_into_the_third_marker_slot()
    {
        var game = OverlandViewTests.Load(26, "10,20,N", "");
        game.Loop.Execute(AreaCommand.TravelEast); // the interface loads the slots
        Assert.Equal([0, 1, 2], game.Loop.MarkerSlots);
        game.Loop.IconEditorDone();
        Assert.Equal([0, 1, 0], game.Loop.MarkerSlots);
        Assert.Equal(128, Overland.MarkerFrame(5, 0, game.Loop.MarkerSlots).IconBlock);

        var art = PorOverlandArt.Load(game.Content);
        var map = game.Content.Overland()!;
        int column = game.Content.OverlandColumn(game.Memory.ViewMode - Overland.FirstMode + PorOverland.FirstScript)!.Value;
        IndexedFrame Frame(int counter, IReadOnlyList<int>? slots)
        {
            var f = new IndexedFrame(Overland.ViewPixels, Overland.ViewPixels);
            art.Draw(f, 0, 0, game.Memory, map, column, counter, slots);
            return f;
        }
        Assert.Equal(Frame(0, null).Pixels, Frame(4, game.Loop.MarkerSlots).Pixels);
        Assert.NotEqual(Frame(4, null).Pixels, Frame(4, game.Loop.MarkerSlots).Pixels);

        game.Memory.PreviousViewMode = 1; // an entry from a 3D area: the interface loads them again
        game.Loop.Execute(AreaCommand.TravelWest);
        Assert.Equal([0, 1, 2], game.Loop.MarkerSlots);
        game.Memory.ViewMode = 1;
        game.Loop.IconEditorDone();
        Assert.Equal([0, 1, 2], game.Loop.MarkerSlots);
    }

    /// <summary>
    /// What runs on a special square is the wilderness script's vector 1 (ECL7 block 26 $9B0A: rows $B04A, counts
    /// $B061, columns $B053, cases $B06A → ONGOTO): south onto 12,26 is Phlan's north edge; CITY ($49E6 = 1, LOADFILES
    /// #127, x 4, y 0, facing S, disk 3, NEWECL 14) leaves the overland for the 3D map of block 14.
    /// </summary>
    [PorFact]
    public void Entering_phlan_from_the_overland_switches_to_its_3D_map()
    {
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        host.MenuChoices.Enqueue(0); // CITY
        var game = Load(26, "12,25,S", "", host);
        game.Loop.Execute(AreaCommand.TravelSouth);
        Assert.Equal(14, game.Vm.CurrentScript);
        Assert.Equal(1, game.Memory.ViewMode);
        Assert.False(Overland.Shown(game.Memory));
        Assert.NotNull(game.Loop.Map);
        var p = game.Memory.Position;
        Assert.Equal((4, 0, Facing.South), (p.X, p.Y, (int)p.Facing)); // $C04D = 2: facing 2 × 2
        Assert.Equal((12, 26), Overland.Position(game.Memory));
    }

    [PorFact]
    public void The_nomad_camp_is_hidden_until_4A9F_is_set()
    {
        var shown = Load(26, "12,13,N", "4A9F=1");
        var hidden = Load(26, "12,13,N", "");
        var map = shown.Content.Overland()!;
        // Special location 4 at (25, 11): the camp's own tile while found, its grass tile (1) before.
        Assert.Equal(map.Tile(25, 11) - 1, map.DrawnTile(25, 11, shown.Memory.Read));
        Assert.Equal(0, map.DrawnTile(25, 11, hidden.Memory.Read));
    }

    /// <summary>
    /// DS:6DDC after a load (program read s25): not saved, so it goes on from the session's value. The game loop's pass
    /// draws twice (START 0x3379 case 4 and 0x40C8) only when $49F2 is not 25–27 (DS:49F3 stays 4): save-overland's
    /// saves keep save A's $49F2 = 20 (DOSBox-X: frames 2 + 3); a save made on the overland has $49F2 = its block.
    /// </summary>
    [PorFact]
    public void The_marker_counter_after_a_load_follows_the_game_loops_first_pass()
    {
        Assert.Equal(2, Load(26, "10,20,N", "").Loop.MarkerCounter);         // $49F2 = 20: 0 + 2
        Assert.Equal(0, Load(26, "10,20,N", "49F2=26").Loop.MarkerCounter); // a real overland save: nothing drawn
        Assert.Equal(0, Load(25, "14,29,S", "49F2=25").Loop.MarkerCounter);
        // A load in the middle of a session keeps the counter it had (5, then + 2 here).
        Assert.Equal(1, Load(26, "10,20,N", "", markerCounter: 5).Loop.MarkerCounter);
    }

    /// <summary>
    /// A script switch from one overland window to the next redraws once (0x3FD2–0x3FF5: old mode outdoors and vector 4's
    /// LOADFILES set DS:4431), which draws the marker; the next command has no interface (DS:49F9 = the old mode).
    /// </summary>
    [PorFact]
    public void Crossing_into_the_next_window_redraws_once()
    {
        var game = Load(26, "15,20,E", "49F2=26");
        Assert.Equal(0, game.Loop.MarkerCounter);
        game.Loop.Execute(AreaCommand.TravelEast); // first command: interface (0, 1); vector 0 → NEWECL 27
        Assert.Equal(27, game.Vm.CurrentScript);
        Assert.Equal(4, game.Memory.ViewMode);
        Assert.Equal(3, game.Loop.MarkerCounter); // + the switch's redraw
        Assert.False(game.Loop.OverlandInterfacePending);
    }

    [PorFact]
    public void Bacpac_holds_sqrpacis_first_forty_tiles()
    {
        var dir = PorData.Open();
        var sqr = PorOverlandArt.Tiles(dir.Picture("SQRPACI", 1));
        var bac = PorOverlandArt.Tiles(dir.Picture("BACPAC", 1));
        Assert.Equal(128, sqr.Count);
        Assert.Equal(PorOverlandArt.BacpacTiles, bac.Count);
        for (int i = 0; i < bac.Count; i++)
            Assert.Equal(sqr[i]!.Pixels, bac[i]!.Pixels);
        Assert.Equal(128, PorOverlandArt.Tiles(dir.Picture("SQRPACI", 2)).Count);
        var art = PorOverlandArt.Load(dir);
        Assert.All(PorOverlandArt.MarkerBlocks, b => Assert.NotNull(art.Marker(b)));
    }

    [PorFact]
    public void A_module_draws_the_same_view()
    {
        var game = Load(26, "10,20,N", "");
        IPorContent module = PorModuleData.Open();
        var art = PorOverlandArt.Load(module);
        var frame = art.Render(game.Loop, module.Overland()!, module.OverlandColumn(26)!.Value);
        Assert.Equal("5e06d1726763c19f0485b0838325d1809860cc0eeaec6a76668934ddc570e985",
            Convert.ToHexStringLower(SHA256.HashData(frame.Pixels)));
    }
}
