using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The training hall (PROGRAM 0) and the end game (PROGRAM 8) through the explore screen and the action layer, on save
/// A (skipped without GOLDBOX_POR_DIR): keyboard, mouse and pad.
/// </summary>
public class HallPlayTests
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

    private static Rig Start()
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory) { Resources = ClientResources.Load(dir), Game = () => game };
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), 7);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
    }

    private static void Frame(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
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

    private static void Settle(Rig r)
    {
        for (int guard = 0; (r.Runner.Pending is null && r.Runner.Busy || r.Runner.Pending is DelayRequest) && guard < 4000; guard++)
        {
            Thread.Sleep(2);
            Frame(r);
        }
    }

    private static void SetXp(Core.Ecl.EclCharacter c, int xp)
    {
        for (int i = 0; i < 4; i++)
            c[0xAC + i] = (byte)(xp >> (8 * i));
    }

    [Fact]
    public void Hall_trains_a_thief_with_keys_and_leaves_with_the_pad()
    {
        if (Dir() is null)
            return;
        var r = Start();
        var party = r.Game.Memory.Party!;
        var thief = party.Members[5];
        SetXp(thief, 3000);
        thief.SetWord(0x90, 1000);                       // 1000 platinum
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 116);
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.Equal(10, menu.Entries.Count);            // all but Load (a party is there)
        Assert.NotNull(r.Player.Hall);
        Press(r, GameAction.PreviousMember);             // Home: the head wraps to the last member
        Settle(r);
        Assert.Same(thief, party.Current);
        Key(r, Keys.T);
        Settle(r);
        var ask = Assert.IsType<ChoiceRequest>(r.Runner.Pending);   // "will become", the question
        Assert.NotNull(r.Player.Hall!.Page);
        Key(r, Keys.Y);
        Settle(r);
        Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, thief[0x9C]);                    // thief level 2
        Assert.Equal(800, thief.Word(0x90));             // 200 platinum paid
        Assert.Equal(2500, thief[0xAC] | thief[0xAD] << 8);   // capped at the level-3 threshold − 1
        // the pad: Down to Begin (entry 8 of 10), Confirm
        for (int i = 0; i < 8; i++)
            Press(r, GameAction.MenuDown);
        Press(r, GameAction.Confirm);
        Settle(r);
        Assert.False(r.Runner.Busy);
        Assert.Null(r.Player.Hall);
        _ = ask;
    }

    [Fact]
    public void Hall_refuses_with_a_message_and_a_click_picks_an_entry()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.Write(TrainingHall.HallMaskCell, 116);  // thieves only: the dwarf fighter is refused
        r.Runner.Start(() => r.Player.TrainingHall());
        Settle(r);
        var menu = Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        int train = menu.Entries.ToList().FindIndex(e => e.Key == 'T');
        Assert.Null(r.Screen.MapClick(new ScreenClick(HallView.MenuColumn * 8 + 4, (HallView.MenuRow + train) * 8 + 4, false)));
        Frame(r);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.NotNull(r.Player.Hall!.Message);
        Settle(r);
        Assert.IsType<HallMenuRequest>(r.Runner.Pending);
        Key(r, Keys.B);
        Settle(r);
        Assert.False(r.Runner.Busy);
    }

    [Fact]
    public void End_game_plays_its_steps_and_clears_the_scene()
    {
        if (Dir() is null)
            return;
        var r = Start();
        r.Game.Memory.GameSpeed = 9;   // 2 loops per animation
        r.Runner.Start(() => r.Player.EndGameSequence());
        bool sawScene = false, sawSpirit = false;
        int frames = 0;
        for (int guard = 0; r.Runner.Busy && guard < 20000; guard++)
        {
            if (r.Player.Scene is { } s)
            {
                sawScene = true;
                sawSpirit |= s.Layers.Any(l => l.Block == 7);
            }
            Thread.Sleep(1);
            if (r.Runner.Pending is DelayRequest)
            {
                frames++;
                Press(r, GameAction.Confirm);   // Enter ends a Client delay early
            }
        }
        Assert.False(r.Runner.Busy);
        Assert.True(sawScene && sawSpirit);
        Assert.Equal(2 * 4 + 2 * 6 + 6 + 3, frames);   // dragon 2 × 4 frames, spirit 2 × 6, block 8 once, three pauses
        Assert.Null(r.Player.Scene);
    }
}
