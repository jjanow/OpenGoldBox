using System.Security.Cryptography;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The explore screen on the overland, headless, on save A of the player's files put on the central wilderness at
/// 10,20 N (the DOSBox-X case of Core's OverlandViewTests; skipped without GOLDBOX_POR_DIR).
/// </summary>
public class OverlandScreenTests
{
    private static string? Dir() => TestRig.PorDir();

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static Rig Start(int script = 26, int x = 10, int y = 20, uint seed = 7)
    {
        var dir = new PorGameDirectory(Dir()!);
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, script, x, y, Facing.North);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), seed);
        var input = new ActionInput(InputBindings.Defaults());
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        // Loaded from slot A: the menus' shared highlight starts on A's place in the folder's save list
        // (ExploreScreen.LoadMenuIndex), 0 whatever other saves the folder holds.
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner, input, player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    /// <summary>Answers whatever an encounter on the way asks (backs out where it can, else the first choice).</summary>
    private static void Settle(Rig r)
    {
        for (int guard = 0; r.Runner.Pending is { } p && guard < 200; guard++)
            Press(r, p is ChoiceRequest { CancelIndex: not null } or ListRequest { CancelAction: not null }
                ? GameAction.Cancel : GameAction.Confirm);
    }

    private static (int, int) Where(Rig r) => Overland.Position(r.Game.Memory);

    [Fact]
    public void Arrows_travel_in_their_direction_and_the_3D_keys_do_nothing_else()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Assert.True(OverlandUi.Shown(r.Game));
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.TurnRight);                    // East
        Settle(r);
        Assert.Equal((11, 20), Where(r));
        Assert.Equal(Facing.East, r.Game.Memory.Position.Facing);
        Assert.Equal(t0 + 12 * 60, GameClock.TotalMinutes(r.Game.Memory));
        Press(r, GameAction.TurnAround);                   // South
        Settle(r);
        Assert.Equal((11, 21), Where(r));
        Press(r, GameAction.AreaMap);                      // not on the overland line
        Assert.False(r.Game.Loop.ShowAreaMap);
        Assert.Null(r.Runner.Pending);
        r.Runner.Dispose();
    }

    [Fact]
    public void The_pad_dpad_travels()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var pad = new GamePadState(default, default, default, new GamePadDPad(ButtonState.Released, ButtonState.Released,
            ButtonState.Pressed, ButtonState.Released)); // left
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Update(default, pad, default, TimeSpan.FromMilliseconds(16), _ => null);
        r.Screen.Update(r.Input);
        Settle(r);
        Assert.Equal((9, 20), Where(r));
        Assert.Equal(Facing.West, r.Game.Memory.Position.Facing);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_click_on_the_view_travels_towards_it()
    {
        if (Dir() is null)
            return;
        var r = Start();
        // The marker is in the window's middle square (2, 2): pixels 56–79 from the view at (8, 8).
        Assert.Equal(GameAction.TurnRight, r.Screen.MapClick(new ScreenClick(8 + 110, 8 + 60, false)));
        Assert.Equal(GameAction.Forward, r.Screen.MapClick(new ScreenClick(8 + 70, 8 + 5, false)));
        Assert.Equal(GameAction.TurnLeft, r.Screen.MapClick(new ScreenClick(8 + 2, 8 + 70, false)));
        Assert.Equal(GameAction.TurnAround, r.Screen.MapClick(new ScreenClick(8 + 66, 8 + 118, false)));
        Assert.Null(r.Screen.MapClick(new ScreenClick(8 + 66, 8 + 66, false))); // on the party
        r.Runner.Dispose();
    }

    [Fact]
    public void The_screen_draws_the_view_the_position_line_and_the_overland_command_line()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var view = new IndexedFrame(Overland.ViewPixels, Overland.ViewPixels);
        for (int y = 0; y < Overland.ViewPixels; y++)
            for (int x = 0; x < Overland.ViewPixels; x++)
                view[x, y] = f[OverlandUi.ViewX + x, OverlandUi.ViewY + y];
        // DOSBox-X's view after the load (Core OverlandViewTests, 26 at 10,20 N).
        Assert.Equal("5e06d1726763c19f0485b0838325d1809860cc0eeaec6a76668934ddc570e985",
            Convert.ToHexStringLower(SHA256.HashData(view.Pixels)));
        Assert.Equal("23,20 N 10:50", ExploreScreen.StatusLine(r.Game, false));
        Assert.Equal(["CAST", "VIEW", "ENCAMP", "SEARCH", "LOOK"],
            OverlandUi.CommandLine([.. r.Game.Art.Program.ExploreCommands!.Select(w => w.ToUpperInvariant())]));
        r.Runner.Dispose();
    }

    [Fact]
    public void A_river_turns_the_party_back_with_the_programs_message_flashed_on_row_24()
    {
        if (Dir() is null)
            return;
        var r = Start(26, 5, 19);                          // the river east of 18,19 ($4AA2 = 0)
        r.Game.Memory.GameSpeed = 4;
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.TurnRight);
        // START 0x7DF8 (0x16FEE): row 24 in colour 14 for DS:49E2 × 225 ms, nothing else on that row.
        var d = Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal(("THE RIVER'S STENCH DRIVES YOU BACK.", (byte)14), d.BottomLine);
        Assert.Equal(TimeSpan.FromMilliseconds(900), d.Duration);
        Assert.Null(r.Player.Notice);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(14, Row24Colour(f, 0));               // 'T' at column 0
        Assert.Equal(0, Row24Colour(f, 39));               // the command line is gone
        r.Runner.Answer(null);
        Settle(r);
        Assert.Null(r.Runner.Pending);
        Assert.Equal((5, 19), Where(r));
        Assert.Equal(t0, GameClock.TotalMinutes(r.Game.Memory));
        r.Runner.Dispose();
    }

    [Fact]
    public void The_old_facings_marker_stays_up_while_the_river_message_shows()
    {
        if (Dir() is null)
            return;
        // DOSBox-X (s39, 26 at 8,19 N, Left into the river at 7,19): during the row-24 message the marker is still the
        // north one (unmirrored); the step's redraw after it draws the west one (mirrored, the next frame).
        var r = Start(26, 8, 19);
        r.Game.Memory.GameSpeed = 4;
        var before = View(r);
        Press(r, GameAction.TurnLeft);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal(Facing.West, r.Game.Memory.Position.Facing);
        Assert.Equal(before, View(r));
        r.Runner.Answer(null);
        Settle(r);
        Assert.Equal((8, 19), Where(r));
        var after = View(r);
        Assert.NotEqual(before, after);
        // Only the marker's square (2, 2) changed.
        for (int y = 0; y < Overland.ViewPixels; y++)
            for (int x = 0; x < Overland.ViewPixels; x++)
                if (x is < 48 or >= 72 || y is < 48 or >= 72)
                    Assert.Equal(before[y * Overland.ViewPixels + x], after[y * Overland.ViewPixels + x]);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_sprite_goes_over_the_tiles_without_the_box_and_a_picture_in_the_box()
    {
        if (Dir() is null)
            return;
        // DOSBox-X s39 (26 at 10,20: SETUPMON 0, 2, 0; APPROACH): the sprite over the tiles. The stand-in text kept beside
        // it (for missing art) boxed it before s39.
        var r = Start();
        r.Player.ShowEncounter(0, 0, 2);
        Assert.NotNull(r.Player.ViewOverlay);
        Assert.False(OverlandUi.Boxed(r.Player));
        r.Player.ShowEncounterSprite(0, 0, 0); // distance 0 on the overland: the sprite, no close-up (0x7AE2)
        Assert.Equal(ViewPictureKind.Sprite, r.Player.ViewPicture?.Kind);
        Assert.False(OverlandUi.Boxed(r.Player));
        r.Player.ShowPicture(2, null);
        Assert.True(OverlandUi.Boxed(r.Player));
        r.Runner.Dispose();
    }

    private static byte[] View(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var view = new byte[Overland.ViewPixels * Overland.ViewPixels];
        for (int y = 0; y < Overland.ViewPixels; y++)
            for (int x = 0; x < Overland.ViewPixels; x++)
                view[y * Overland.ViewPixels + x] = f[OverlandUi.ViewX + x, OverlandUi.ViewY + y];
        return view;
    }

    [Fact]
    public void Speed_0_shows_no_river_message()
    {
        if (Dir() is null)
            return;
        var r = Start(26, 5, 19);
        r.Game.Memory.GameSpeed = 0;                       // Delay(0): printed and cleared at once
        Press(r, GameAction.TurnRight);
        Assert.Null(r.Runner.Pending);
        Assert.Equal((5, 19), Where(r));
        r.Runner.Dispose();
    }

    [Fact]
    public void A_where_there_is_no_area_map_flashes_not_here_on_row_24()
    {
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var savePath = dir.Find("SAVGAMA.DAT")!;           // save A: a 3D map
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), File.ReadAllBytes(savePath), Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var input = new ActionInput(InputBindings.Defaults());
        var r = new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A',
            new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos)), game, runner, input, player);
        game.Memory.Write(AreaLoop.NoAreaMap, 1);
        game.Memory.GameSpeed = 2;
        Press(r, GameAction.AreaMap);
        var d = Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal(("NOT HERE", (byte)14), d.BottomLine); // cs:0x97B of the unit at 0x166CB (0x170F1)
        Assert.Equal(TimeSpan.FromMilliseconds(450), d.Duration);
        r.Runner.Answer(null);
        Assert.Null(r.Runner.Pending);
        Assert.False(game.Loop.ShowAreaMap);
        r.Runner.Dispose();
    }

    /// <summary>
    /// Phlan's north edge: the site's text, picture and HMENU (CLEARBOX first), then CITY switches to block 14, whose
    /// vector 4 loads files, so the switch redraws the 3D view (0x3FF5) over the blanked window (live vpad run, s25:
    /// the window had stayed black until the next command).
    /// </summary>
    [Fact]
    public void Entering_phlan_shows_its_3d_view_at_once()
    {
        if (Dir() is null)
            return;
        var r = Start(26, 12, 25, 1);
        Press(r, GameAction.TurnAround);                   // South onto 12,26
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal("CITY", menu.Items[0]);
        Press(r, GameAction.Confirm);                      // CITY
        Settle(r);
        Assert.Equal(14, r.Game.Vm.CurrentScript);
        Assert.False(r.Player.ViewBlanked);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        int lit = 0;
        for (int y = 0; y < 88; y++)
            for (int x = 0; x < 88; x++)
                if (f[24 + x, 24 + y] != 0)
                    lit++;
        Assert.True(lit > 88 * 88 / 2, $"3D view drawn ({lit} lit pixels)");
        r.Runner.Dispose();
    }

    /// <summary>The colour of the first lit pixel in the 8 × 8 cell of row 24, column <paramref name="col"/> (0: none).</summary>
    private static int Row24Colour(IndexedFrame f, int col)
    {
        for (int y = 192; y < 200; y++)
            for (int x = col * 8; x < col * 8 + 8; x++)
                if (f[x, y] != 0)
                    return f[x, y];
        return 0;
    }
}
