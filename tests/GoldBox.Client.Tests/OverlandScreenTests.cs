using System.Security.Cryptography;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
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
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class NoHost : IScreenHost
    {
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static Rig Start(int script = 26, int x = 10, int y = 20, uint seed = 7)
    {
        var dir = new PorGameDirectory(Dir()!);
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, script, x, y, Facing.North);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir) };
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), seed);
        var input = new ActionInput(InputBindings.Defaults());
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        return new Rig(new ExploreScreen(game, player, runner, new NoHost(), 'B', art), game, runner, input, player);
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
    public void A_river_turns_the_party_back_with_the_programs_message()
    {
        if (Dir() is null)
            return;
        var r = Start(26, 5, 19);                          // the river east of 18,19 ($4AA2 = 0)
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.TurnRight);
        Settle(r);
        Assert.Equal((5, 19), Where(r));
        Assert.Equal(t0, GameClock.TotalMinutes(r.Game.Memory));
        Assert.Equal("THE RIVER'S STENCH DRIVES YOU BACK.", r.Player.Notice);
        r.Runner.Dispose();
    }
}
