using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

public class ScriptRunnerTests
{
    [Fact]
    public void Worker_parks_on_each_request_and_gets_the_answers()
    {
        using var r = new ScriptRunner();
        var got = new List<object?>();
        r.Start(() =>
        {
            got.Add(r.Ask(new ChoiceRequest(null, ["A", "B"], false)));
            got.Add(r.Ask(new TextRequest("", false, 5)));
        });
        Assert.True(r.Busy);
        Assert.IsType<ChoiceRequest>(r.Pending);
        r.Answer(1);
        Assert.IsType<TextRequest>(r.Pending);
        r.Answer("OK");
        Assert.False(r.Busy);
        Assert.Null(r.Pending);
        Assert.Equal([1, "OK"], got);
    }

    [Fact]
    public void A_fault_ends_the_command_and_is_kept()
    {
        using var r = new ScriptRunner();
        r.Start(() => throw new InvalidOperationException("boom"));
        Assert.False(r.Busy);
        Assert.Equal("boom", r.Fault?.Message);
    }

    [Fact]
    public void Dispose_stops_a_parked_worker()
    {
        var r = new ScriptRunner();
        bool after = false;
        r.Start(() =>
        {
            r.Ask(new DelayRequest(TimeSpan.FromHours(1)));
            after = true;
        });
        r.Dispose();
        Assert.False(r.Busy);
        Assert.False(after);
        Assert.Null(r.Fault);
    }
}

public class TextWindowTests
{
    [Fact]
    public void Prints_from_the_cursor_and_wraps_whole_words()
    {
        var t = new TextWindow();
        t.Print("ab");
        t.Print("cd");
        Assert.Equal("ABCD", t.Lines[0]);
        t.Print(new string('x', 32) + " yy", clear: true);   // 33 cells with the space, then "YY" needs 2 more of 38: fits
        Assert.Equal(new string('X', 32) + " YY", t.Lines[0]);
        t.Print(new string('x', 36) + " bb", clear: true);   // "BB" would end in column 39: next row
        Assert.Equal([new string('X', 36), "BB"], t.Lines.Take(2));
    }

    [Fact]
    public void Breaks_after_a_delimiter_too()
    {
        var t = new TextWindow();
        t.Print(new string('a', 34) + "-bbbb");                 // the word ends with the hyphen: "BBBB" goes down
        Assert.Equal([new string('A', 34) + "-", "BBBB"], t.Lines.Take(2));
    }

    [Fact]
    public void A_full_box_waits_for_a_key_then_starts_again_at_the_top()
    {
        int pauses = 0;
        var t = new TextWindow { Pause = () => pauses++ };
        t.Print(string.Join(" ", Enumerable.Range(1, 7).Select(i => new string((char)('a' + i), 30))));
        Assert.Equal(1, pauses);
        Assert.Equal(new string('H', 30), t.Lines[0]);          // the 7th word on a cleared box
        Assert.All(t.Lines.Skip(1), l => Assert.Equal("", l));
    }

    [Fact]
    public void Print_return_and_exit_move_the_cursor_without_clearing()
    {
        var t = new TextWindow();
        t.Print("one");
        t.NewLine();
        t.Print("two");
        Assert.Equal(["ONE", "TWO"], t.Lines.Take(2));
        t.Home();
        t.Print("X");
        Assert.Equal(["XNE", "TWO"], t.Lines.Take(2));
        Assert.False(t.IsEmpty);
        t.Clear();
        Assert.True(t.IsEmpty);
    }
}

/// <summary>The explore screen headless, on save A of the player's files (skipped without GOLDBOX_POR_DIR).</summary>
public class ExploreScreenTests
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

    private static Rig Start(uint seed = 7)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory);
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!, new PorPlayer(player, player, player, player), seed);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    /// <summary>
    /// Answer whatever the game waits for: back out where the request allows it (the loot, shop and camp menus loop
    /// until Exit), else take the first choice.
    /// </summary>
    private static void Settle(Rig r)
    {
        for (int guard = 0; r.Runner.Pending is { } p && guard < 200; guard++)
            Press(r, p is ChoiceRequest { CancelIndex: not null } or ListRequest { CancelAction: not null }
                ? GameAction.Cancel : GameAction.Confirm);
        Assert.Null(r.Runner.Pending);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    [Fact]
    public void Walks_to_the_door_and_the_door_menu_waits_for_the_player()
    {
        if (Dir() is null)
            return;
        var r = Start();
        // Save A at 15,4 W; the route of the door captures to 10,2 facing N.
        GameAction[] route =
        [
            GameAction.Forward, GameAction.TurnLeft, GameAction.Forward, GameAction.Forward, GameAction.Forward,
            GameAction.TurnRight, GameAction.Forward, GameAction.TurnRight, GameAction.Forward, GameAction.Forward,
            GameAction.Forward, GameAction.Forward, GameAction.Forward, GameAction.TurnLeft, GameAction.Forward,
            GameAction.Forward, GameAction.Forward, GameAction.TurnRight,
        ];
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        int steps = 0;
        foreach (var a in route)
        {
            Press(r, a);
            // An event on the way (text, menus) may wait for the player.
            Settle(r);
            if (a == GameAction.Forward)
                steps++;
        }
        var p = r.Game.Memory.Position;
        Assert.Equal((10, 2, Facing.North), (p.X, p.Y, (int)p.Facing));
        Assert.Equal(t0 + steps, GameClock.TotalMinutes(r.Game.Memory));

        Press(r, GameAction.Forward);
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(["BASH", "PICK", "EXIT"], menu.Items);
        Press(r, GameAction.Cancel);                        // Exit: no try, the step is refused, vector 1 runs
        Settle(r);
        Assert.Equal((10, 2), (r.Game.Memory.Position.X, r.Game.Memory.Position.Y));
        r.Runner.Dispose();
    }

    [Fact]
    public void Search_and_look_take_the_original_time()
    {
        if (Dir() is null)
            return;
        var r = Start();
        long t0 = GameClock.TotalMinutes(r.Game.Memory);
        Press(r, GameAction.Search);
        Assert.True(r.Game.Loop.SearchOn);
        Press(r, GameAction.Forward);
        Settle(r);
        Assert.Equal(t0 + 10, GameClock.TotalMinutes(r.Game.Memory));
        Press(r, GameAction.Look);
        Settle(r);
        Assert.Equal(t0 + 20, GameClock.TotalMinutes(r.Game.Memory));
        r.Runner.Dispose();
    }

    [Fact]
    public void Encamp_opens_the_programs_camp_menu_and_view()
    {
        if (Dir() is null)
            return;
        var r = Start();
        Press(r, GameAction.Encamp);
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(6, menu.Items.Count);                 // the program's camp line (DS:0508 in v1.3)
        Assert.NotNull(menu.MenuPrompt);
        Assert.True(r.Player.Camping);
        Assert.Contains(" CAMPING", ExploreScreen.StatusLine(r.Game, r.Player.Camping));
        Key(r, Keys.V);                                    // View: the character page
        var page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.Equal(r.Game.Memory.Party!.Current!.Name.ToUpperInvariant(), page.Lines[0].Text);
        Press(r, GameAction.NextMember);                   // the next member's page
        page = Assert.IsType<ListRequest>(r.Runner.Pending);
        Assert.Equal(r.Game.Memory.Party!.Members[1].Name.ToUpperInvariant(), page.Lines[0].Text);
        Press(r, GameAction.Cancel);                       // leave the view
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Press(r, GameAction.Cancel);                       // Exit the camp
        Settle(r);
        Assert.False(r.Player.Camping);
        r.Runner.Dispose();
    }

    [Fact]
    public void Text_entry_works_with_keys_and_with_the_grid()
    {
        if (Dir() is null)
            return;
        var r = Start();
        object? answer = null;
        r.Runner.Start(() => answer = r.Runner.Ask(new TextRequest("", false, 10)));
        Key(r, Keys.H);
        Key(r, Keys.I);
        Key(r, Keys.Back);
        Press(r, GameAction.MenuRight);                    // grid: A → B
        Press(r, GameAction.Confirm);                      // adds B
        Key(r, Keys.Enter);
        Assert.Equal("HB", answer);
        r.Runner.Dispose();
    }

    [Fact]
    public void Clicking_a_menu_item_chooses_it()
    {
        if (Dir() is null)
            return;
        var r = Start();
        object? answer = null;
        r.Runner.Start(() => answer = r.Runner.Ask(new ChoiceRequest(null, ["YES", "NO"], false)));
        // "YES NO" on the bottom row: NO starts at column 4.
        var click = r.Screen.MapClick(new ScreenClick(4 * 8 + 2, 24 * 8 + 3, false));
        Assert.Equal(GameAction.Confirm, click);
        Press(r, GameAction.Confirm);
        Assert.Equal(1, answer);
        _ = new Point(0, 0);
        r.Runner.Dispose();
    }
}
