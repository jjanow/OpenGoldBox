using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// What a loaded game's entry shows on the explore screen (training.md "The party menu under a Load's vector 4",
/// view3d.md "Redraw"): VMENU and the ECL line inputs as the program draws them, and a load that reads no map drawing
/// the map buffer (DOSBox-X s43). Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class EntryScreenTests
{
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    /// <summary>Save A, its script all EXIT, with <paramref name="edit"/> applied to the bytes; loaded as a --save start.</summary>
    private static Rig Start(PorGameDirectory dir, Action<byte[]>? edit = null, AreaMap? mapBuffer = null)
    {
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var bytes = File.ReadAllBytes(savePath);
        var area = new List<byte>();
        for (int v = 0; v < 5; v++)
            area.AddRange([1, 1, 0x1F, 0x99]);
        area.AddRange(new byte[0x1F - 0x14]);
        area.Add(0);                                       // $991F: EXIT
        Array.Clear(bytes, PorSaveGame.OffScript, 7680);
        area.CopyTo(bytes, PorSaveGame.OffScript);
        edit?.Invoke(bytes);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), bytes, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7, mapBuffer: mapBuffer);
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner, input, player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    private static IndexedFrame Draw(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    /// <summary>The colours used in text cell (col, row).</summary>
    private static HashSet<byte> Colours(IndexedFrame f, int col, int row)
    {
        var set = new HashSet<byte>();
        for (int y = row * 8; y < row * 8 + 8; y++)
            for (int x = col * 8; x < col * 8 + 8; x++)
                set.Add(f[x, y]);
        return set;
    }

    private static PorGameDirectory? Dir() => TestRig.PorDir() is { } d ? new PorGameDirectory(d) : null;

    [Fact]
    public void A_vmenu_prints_its_prompt_in_the_box_and_lists_from_column_1_under_it()
    {
        // GAME.OVR 0x1C4E: the prompt through 0x77F8 (clear, colour 10) at (1, 17), the list 0x2D669 from the row after
        // the cursor's, colours 10 / 15, no cursor glyph; afterwards the box blanked and the cursor kept (DOSBox-X s43:
        // a PRINT after the menu went on at the prompt's end, column 12).
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        r.Runner.Start(() =>
        {
            r.Player.Print("ENTRY TEXT.", true);
            r.Player.VerticalMenu("PICK A WAY:", ["FIRST WAY", "SECOND WAY", "THIRD WAY"]);
            r.Player.Print("DONE.", false);
        });
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(18, menu.ListRow);
        Assert.Equal(('P', (byte)10), r.Player.Text.Cell(17, 1));
        var f = Draw(r);
        Assert.Contains((byte)15, Colours(f, 1, 18));      // the lit row, white, at column 1
        Assert.DoesNotContain((byte)10, Colours(f, 1, 18));
        Assert.Contains((byte)10, Colours(f, 1, 19));      // the others green
        for (int row = 17; row <= 22; row++)
            Assert.DoesNotContain((byte)14, Colours(f, 1, row)); // no cursor glyph
        Assert.DoesNotContain((byte)15, Colours(f, 2, 24)); // nothing on row 24
        Press(r, GameAction.Confirm);
        Assert.Null(r.Runner.Pending);
        Assert.Equal('\0', r.Player.Text.Cell(17, 1).Item1);
        Assert.Equal('D', r.Player.Text.Cell(17, 12).Item1);
        r.Runner.Dispose();
    }

    [Fact]
    public void The_ecl_inputs_have_no_prompt_and_type_from_column_1()
    {
        // INPUTNUM / INPUTSTR (0x1697 / 0x16DA): the line input START 0x7A73 with an empty prompt; each character at the
        // prompt's length + 1 on (0x7B0E): DOSBox-X s43 showed "12" from column 1 on row 24.
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        int typed = -1;
        r.Runner.Start(() => typed = r.Player.InputNumber());
        var t = Assert.IsType<TextRequest>(r.Runner.Pending);
        Assert.Equal("", t.Prompt);
        Assert.True(t.ScriptInput);                       // the s48 QoL grid placement applies to the script's inputs only
        Key(r, Keys.D1);
        var f = Draw(r);
        Assert.Equal([0], Colours(f, 0, 24));             // column 0 blank
        Assert.Contains((byte)15, Colours(f, 1, 24));      // the digit at column 1
        Key(r, Keys.Enter);
        Assert.Null(r.Runner.Pending);
        Assert.Equal(1, typed);
        r.Runner.Dispose();
    }

    /// <summary>Save A with location type 2 (Load reads no map, 0x1FBB3) and $49E6 = 1 (the redraw draws 3D).</summary>
    private static void NoMapRead(byte[] bytes)
    {
        bytes[PorSaveGame.OffLocationType] = 2;
        int o = PorSaveGame.Off4900 + 2 * (AreaLoop.View3DCell - 0x4900);
        bytes[o] = 1;
        bytes[o + 1] = 0;
    }

    [Fact]
    public void A_load_that_reads_no_map_draws_the_map_buffer_with_the_interfaces_marker()
    {
        // DOSBox-X s43 (scratch save H: save A, location type 2, script EXIT, start-up Load): the 3D view of an empty map
        // (the buffer DS:6A5C read all zeros), the horseman over its top-left corner, no inner box and no joint at
        // (16, 16) (the overland interface's layout 0x2E03F), DS:6DDC 1 afterwards.
        if (Dir() is not { } dir)
            return;
        var r = Start(dir, NoMapRead);
        Assert.NotNull(r.Game.Loop.Map);
        Assert.True(r.Game.Loop.InView3D);
        Assert.All(r.Game.Loop.Map!.PlanesArray, b => Assert.Equal(0, b));
        Assert.True(r.Game.Loop.OverlandInterfacePending);
        var f = Draw(r);
        Assert.Equal(r.Game.RenderView(false).Pixels[44 * 88 + 44], f[24 + 44, 24 + 44]); // the 3D view at (24, 24)
        bool marker = false;
        for (int y = 8; y < 24; y++)
            for (int x = 8; x < 24; x++)
                marker |= f[x, y] != 0;
        Assert.True(marker);                               // the marker in cells 1–2 (no box there)
        Assert.DoesNotContain((byte)14, Colours(f, 16, 16)); // no yellow joint at (16, 16)
        Assert.DoesNotContain((byte)14, Colours(f, 2, 2));   // no inner box corner
        r.Runner.Start(() => r.Game.Loop.Execute(AreaCommand.TravelEast));
        for (int guard = 0; r.Runner.Pending is not null && guard < 50; guard++)
            Press(r, GameAction.Confirm);
        Assert.Equal(1, r.Game.Loop.MarkerCounter);       // one 0x2DB87 (the lone marker, frame 0)
        Assert.Equal(0, r.Game.Loop.InterfaceMarker!.Frame);
        Assert.True(r.Game.Loop.InterfaceMarkerCovered);  // the step's 3D redraw went over its inner part
        r.Runner.Dispose();
    }

    [Fact]
    public void A_load_that_reads_no_map_keeps_the_sessions_map_in_the_buffer()
    {
        // DOSBox-X s43: save G (slums) loaded, its step's hall, Load of save H: the buffer held GEO2 block 20 byte for
        // byte, and the view showed the slums' corridor. The Client hands the running game's map to the load.
        if (Dir() is not { } dir)
            return;
        var slums = Start(dir).Game.Loop.Map!;
        var r = Start(dir, NoMapRead, slums);
        Assert.Same(slums, r.Game.Loop.Map);
        Assert.True(r.Game.Loop.InView3D);
        r.Runner.Dispose();
    }

    [Fact]
    public void An_overland_load_keeps_its_wall_ahead_byte_and_the_camp_brings_the_joint_back()
    {
        // The buffer a Load leaves is not read on the overland ($49E6 = 0): DS:6AB0 stays the saved byte. The camp draws
        // its own windows: DOSBox-X s43 (save-overland 26 10,20,N): no joint at (16, 16) on the overland, the joint in
        // the camp and after its Exit.
        if (Dir() is not { } dir)
            return;
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, 26, 10, 20, Facing.North);
        save[PorSaveGame.OffPosition + 3] = 0x5A;           // a wall-ahead byte no empty buffer would give
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        Assert.Equal(0x5A, game.Memory.Position.WallAhead);
        Assert.NotNull(game.Loop.Map);                      // the buffer (zeros), not drawn: the overland is up
        Assert.True(OverlandUi.Shown(game));
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        var r = new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner,
            new ActionInput(InputBindings.Defaults()), player);
        Assert.DoesNotContain((byte)14, Colours(Draw(r), 16, 16));
        r.Runner.Start(() => r.Game.Loop.Execute(AreaCommand.Look));
        for (int guard = 0; r.Runner.Pending is not null && guard < 50; guard++)
            Press(r, GameAction.Confirm);
        Assert.True(r.Game.Loop.OverlandLayout);
        r.Game.Loop.OtherLayoutDrawn();                     // ClientPlayer.Camp (and the venues and fights)
        Assert.Contains((byte)14, Colours(Draw(r), 16, 16));
        r.Runner.Dispose();
    }

    /// <summary>
    /// Save A on block 26's overland at 10,20 N (as goldbox-tools save-overland writes it), its vector 0 pointed at
    /// <paramref name="code"/> placed past the script's end ($B300) behind a run-once guard (a flag byte at $B6F0): the
    /// DOSBox-X s44 test saves.
    /// </summary>
    private static Rig StartOverland(PorGameDirectory dir, byte[] code)
    {
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, 26, 10, 20, Facing.North);
        byte[] guard = [0x03, 1, 0xF0, 0xB6, 0, 0, 0x17, 0x00, 0x09, 0, 1, 1, 0xF0, 0xB6]; // COMPARE; IFNE EXIT; SAVE 1
        save[PorSaveGame.OffScript + 2] = 0x00;            // vector 0 -> $B300
        save[PorSaveGame.OffScript + 3] = 0xB3;
        int o = PorSaveGame.OffScript + 0xB300 - 0x9900;
        guard.CopyTo(save, o);
        code.CopyTo(save, o + guard.Length);
        save[o + guard.Length + code.Length] = 0;          // EXIT
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner,
            new ActionInput(InputBindings.Defaults()), player);
    }

    private static void TravelNorth(Rig r) => r.Runner.Start(() => r.Game.Loop.Execute(AreaCommand.TravelNorth));

    /// <summary>The overland window around the box blank (cell 1, 8), the box's ring there (cell 2, 8), the joint.</summary>
    private static void AssertBlankWindowWithBox(IndexedFrame f)
    {
        Assert.Equal([0], Colours(f, 1, 8));
        Assert.NotEqual([0], Colours(f, 2, 8));
        Assert.Contains((byte)14, Colours(f, 16, 16));      // START 0x3379's layout: the joint
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shop_or_temple_on_the_overland_brings_the_joint_back_for_good(bool temple)
    {
        // START 0x3379 case 1 (0x32CA(1): every window blanked, the 3D view's box) and COMBAT's closing START 0x3379
        // case 3 (0x2635): DOSBox-X s44 (save-overland 26 10,20,N, vector 0 = CLRMON, SAVE 1 to $6E6C / $6DE2, COMBAT
        // once): the window blank with the empty box in it and the joint at (16, 16); after Exit the overland with the
        // joint, also after the next step (the interface, START 0x36FE, needs DS:49F9 < 2).
        if (Dir() is not { } dir)
            return;
        byte[] code = [0x1C, 0x09, 0, 1, 1, (byte)(temple ? 0xE2 : 0x6C), (byte)(temple ? 0x6D : 0x6E), 0x24];
        var r = StartOverland(dir, code);
        Assert.DoesNotContain((byte)14, Colours(Draw(r), 16, 16));
        TravelNorth(r);
        Assert.NotNull(r.Runner.Pending);
        Assert.Equal(Venue.Shop, r.Player.Interface);
        var f = Draw(r);
        AssertBlankWindowWithBox(f);
        Assert.Equal([0], Colours(f, 8, 8));                // the box empty
        Key(r, Keys.E);
        Assert.Null(r.Runner.Pending);
        f = Draw(r);
        Assert.NotEqual([0], Colours(f, 1, 8));             // the tiles again
        Assert.Contains((byte)14, Colours(f, 16, 16));
        TravelNorth(r);                                     // the guard: no second shop
        Assert.Null(r.Runner.Pending);
        Assert.False(r.Game.Loop.OverlandLayout);
        Assert.Contains((byte)14, Colours(Draw(r), 16, 16));
        r.Runner.Dispose();
    }

    [Fact]
    public void The_loot_menu_on_the_overland_shows_the_chest_in_the_box_and_the_joint_stays()
    {
        // START 0x3379 case 6 (0x32CA(1), PIC block 1 in the box): DOSBox-X s44 (vector 0 = CLRMON, TREASURE 100 copper,
        // COMBAT once): the summary, then the window blank with the chest in the box and the joint; Exit, No (treasure
        // left): the overland with the joint, also after the next step. A won fight ends the same way (DOSBox-X s44).
        if (Dir() is not { } dir)
            return;
        byte[] code = [0x1C, 0x27, 0, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 0x24];
        var r = StartOverland(dir, code);
        TravelNorth(r);
        Press(r, GameAction.Confirm);                       // the summary's key
        Assert.NotNull(r.Runner.Pending);
        Assert.Equal(Venue.Loot, r.Player.Interface);
        var f = Draw(r);
        AssertBlankWindowWithBox(f);
        Assert.NotEqual([0], Colours(f, 8, 8));             // the chest
        Key(r, Keys.E);
        for (int guard = 0; r.Runner.Pending is not null && guard < 5; guard++)
            Key(r, Keys.N);                                 // no, do not go back for the treasure
        Assert.Null(r.Runner.Pending);
        f = Draw(r);
        Assert.NotEqual([0], Colours(f, 1, 8));
        Assert.Contains((byte)14, Colours(f, 16, 16));
        TravelNorth(r);
        Assert.Null(r.Runner.Pending);
        Assert.Contains((byte)14, Colours(Draw(r), 16, 16));
        r.Runner.Dispose();
    }
}
