using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>The camp through the explore screen and the action layer, on save A (skipped without GOLDBOX_POR_DIR).</summary>
public class CampPlayTests
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

    private static Rig Start(string? save = null, string? saveDir = null)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        game = PorGame.FromSave(dir, art, save ?? dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        player.SaveFolder = saveDir;
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
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

    /// <summary>Frames with no input until the rest's polls are answered and something else waits.</summary>
    private static void Idle(Rig r)
    {
        for (int guard = 0; r.Runner.Pending is DelayRequest && guard < 2000; guard++)
        {
            Thread.Sleep(5);
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            r.Screen.Update(r.Input);
        }
    }

    [Fact]
    public void Rest_with_actions_then_save_with_keys_and_load_it_again()
    {
        if (Dir() is null)
            return;
        var tmp = Directory.CreateTempSubdirectory("goldbox-camp-");
        try
        {
            var r = Start(saveDir: tmp.FullName);
            long t0 = GameClock.TotalMinutes(r.Game.Memory);
            Press(r, GameAction.Encamp);
            Key(r, Keys.R);                                        // Rest (the prep time is 0)
            var rest = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            Assert.Equal(7, rest.Items.Count);
            Press(r, GameAction.MenuUp);                           // Inc: 5 minutes (minutes highlighted first)
            Press(r, GameAction.MenuUp);
            Press(r, GameAction.Confirm);                          // REST (the first item)
            Idle(r);
            Assert.Equal(t0 + 10, GameClock.TotalMinutes(r.Game.Memory));
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);        // back at the camp menu
            Assert.Same(r.Game.Camp, r.Player.CampState);          // the session's program variables are the game's
            r.Game.Memory.GameSpeed = 5;                           // DS:49E2: the save copies it into $49FC
            Key(r, Keys.S);                                        // Save
            Key(r, Keys.C);                                        // slot C
            Key(r, Keys.N);                                        // Quit to DOS? No
            Press(r, GameAction.Cancel);                           // leave the camp
            Assert.Null(r.Runner.Pending);
            var file = Path.Combine(tmp.FullName, "SAVGAMC.DAT");
            Assert.True(File.Exists(file));
            var names = r.Game.Memory.Party!.Members.Select(m => m.Name).ToList();
            r.Runner.Dispose();

            var back = Start(file, tmp.FullName);
            Assert.Equal(names, back.Game.Memory.Party!.Members.Select(m => m.Name));
            Assert.Equal(t0 + 10, GameClock.TotalMinutes(back.Game.Memory));
            var written = new PorSaveGame(File.ReadAllBytes(file));
            Assert.Equal(2, written.InterfaceState);
            Assert.Equal(r.Game.Memory.ViewMode, written.LocationType);   // DS:49FA
            Assert.Equal(5, back.Game.Memory.GameSpeed);
            back.Runner.Dispose();
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_camp_uses_the_games_state_and_the_level_seams()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Assert.Same(r.Game.Camp, r.Player.CampState);
        var effects = r.Player.CampEffects(r.Game.Memory.Party!);
        Assert.NotNull(effects.LevelStats);                        // FightKit.WireLevels with the program's tables
        Assert.NotNull(effects.RaceLevelLimit);
        Assert.NotNull(effects.ExperienceTable);
        Assert.NotSame(r.Game.OutsideEffects, effects);            // its own engine: the camp's spells hook into it
        r.Runner.Dispose();
    }

    /// <summary>One left click at cell (col, row) through the same path as the window (MapClick, Inject, Update).</summary>
    private static void Click(Rig r, int col, int row)
    {
        var down = new MouseState(col * 8 + 3, row * 8 + 3, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
        r.Input.Update(default, default, down, TimeSpan.Zero, p => p);
        foreach (var c in r.Input.Clicks.ToList())
            if (r.Screen.MapClick(c) is { } a)
                r.Input.Inject(a);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, p => p);
    }

    [Fact]
    public void Camp_by_mouse_rest_stop_and_place_order()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Click(r, 16, 24);                                          // ENCAMP on the command line (AREA CAST VIEW ENCAMP)
        Assert.True(r.Player.Camping);
        Click(r, 22, 24);                                          // REST ("CAMP: " then SAVE VIEW MAGIC REST)
        Assert.Equal(7, Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items.Count);
        for (int i = 0; i < 6; i++)
            Click(r, 22, 24);                                      // INC ×6: 30 minutes (REST DAYS HOURS MINS INC)
        Click(r, 1, 24);                                           // REST
        Assert.IsType<DelayRequest>(r.Runner.Pending);             // the first key poll, after 4 ticks
        Click(r, 10, 20);                                          // a click anywhere: stop resting?
        Click(r, 14, 24);                                          // YES ("STOP RESTING? " then YES)
        Assert.Equal(t0 + 20, GameClock.TotalMinutes(r.Game.Memory));

        var first = party.Members[0];
        Assert.Equal("CAMP:", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
        Click(r, 28, 24);                                          // ALTER (… REST ALTER)
        Assert.Equal("ALTER:", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
        Click(r, 8, 24);                                           // ORDER ("ALTER: " then ORDER)
        Assert.Equal("SELECT", Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items[0]);
        Click(r, 14, 24);                                          // SELECT → place ("PARTY ORDER: " SELECT)
        Assert.Equal("PLACE", Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items[0]);
        Click(r, 20, 5);                                         // a name below the current one: down one place
        Assert.Same(first, party.Members[1]);
        Assert.Same(first, party.Current);
        Click(r, 20, 0);                                           // nothing there
        Press(r, GameAction.Cancel);
        Press(r, GameAction.Cancel);
        Press(r, GameAction.Cancel);
        Assert.Null(r.Runner.Pending);
        r.Runner.Dispose();
    }

    [Fact]
    public void Alter_order_by_pad_actions_moves_the_member()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        var first = party.Members[0];
        Press(r, GameAction.Encamp);
        Key(r, Keys.A);                                            // Alter
        Press(r, GameAction.Confirm);                              // ORDER (first item)
        Press(r, GameAction.Confirm);                              // SELECT → place mode
        Press(r, GameAction.NextMember);                           // RB / End: one place down
        Assert.Same(first, party.Members[1]);
        Assert.Same(first, party.Current);
        Press(r, GameAction.Cancel);                               // leave order
        Press(r, GameAction.Cancel);                               // leave alter
        Press(r, GameAction.Cancel);                               // leave the camp
        Assert.Null(r.Runner.Pending);
        r.Runner.Dispose();
    }
}
