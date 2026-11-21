using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// QoL animation speed (<see cref="AnimationTiming"/>): Original leaves every timing as the program's, Instant skips
/// the fixed animation pauses yet lands each sequence on its last frame; game-speed pauses are never touched.
/// </summary>
public class AnimationSpeedTests
{
    private static readonly AnimationSpeed[] All = Enum.GetValues<AnimationSpeed>();

    [Fact]
    public void Original_leaves_every_request_as_it_is()
    {
        PlayerRequest[] requests =
        [
            new DelayRequest(TimeSpan.FromMilliseconds(30)) { Animation = true, Chained = true },
            new DelayRequest(TimeSpan.Zero) { Animation = true },
            new DelayRequest(TimeSpan.FromMilliseconds(900)),
            new KeyRequest("X"),
        ];
        foreach (var q in requests)
            Assert.Same(q, AnimationTiming.Apply(q, AnimationSpeed.Original));
        Assert.Equal(1.0, AnimationTiming.Multiplier(AnimationSpeed.Original));
        Assert.Equal(AnimationSpeed.Original, new ClientSettings().AnimationSpeedChoice); // the default = off
        Assert.Equal(AnimationSpeed.Original, new ScriptRunner().AnimationSpeed);
    }

    [Fact]
    public void Untagged_pauses_are_never_scaled()
    {
        // Game-speed pauses, typing, the camp's key poll and the fixed message pauses keep their length.
        PlayerRequest[] requests =
        [
            new DelayRequest(TimeSpan.FromMilliseconds(225 * 4)),
            new DelayRequest(TimeSpan.FromMilliseconds(40)) { Chained = true },
            new DelayRequest(TimeSpan.FromMilliseconds(30)) { ReportInput = true },
            new DelayRequest(TimeSpan.Zero) { BottomLine = ("X", 10) },
        ];
        foreach (var s in All)
            foreach (var q in requests)
                Assert.Same(q, AnimationTiming.Apply(q, s));
    }

    [Fact]
    public void Tagged_pauses_scale_and_instant_skips_them()
    {
        var flight = new DelayRequest(TimeSpan.FromMilliseconds(100)) { Animation = true, Chained = true };
        Assert.Equal([300.0, 200.0, 100.0, 50.0, 25.0],
            All.Take(5).Select(s => ((DelayRequest)AnimationTiming.Apply(flight, s)!).Duration.TotalMilliseconds));
        Assert.True(((DelayRequest)AnimationTiming.Apply(flight, AnimationSpeed.Faster)!).Chained);
        Assert.Null(AnimationTiming.Apply(flight, AnimationSpeed.Instant));

        // A display point (zero: one update at Original) keeps its one update when faster, is held longer when slower.
        var point = new DelayRequest(TimeSpan.Zero) { Animation = true };
        Assert.Same(point, AnimationTiming.Apply(point, AnimationSpeed.Faster));
        Assert.Same(point, AnimationTiming.Apply(point, AnimationSpeed.Fastest));
        Assert.Equal(AnimationTiming.OneUpdate * 3, ((DelayRequest)AnimationTiming.Apply(point, AnimationSpeed.Slowest)!).Duration);
        Assert.Null(AnimationTiming.Apply(point, AnimationSpeed.Instant));
    }

    private static CombatChooser Chooser(ScriptRunner runner, CombatView view) => new(runner.Ask, view);

    [Fact]
    public void A_flight_at_original_parks_on_each_position_with_the_program_delay()
    {
        using var runner = new ScriptRunner();
        var view = new CombatView { Speed = 1 };
        var chooser = Chooser(runner, view);
        runner.Start(() => chooser.Flight(FlightAnimation.Spell(1, 1, 4, 1)));
        int parks = 0;
        while (runner.Pending is DelayRequest d)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(30), d.Duration);
            Assert.NotNull(view.Flying);
            parks++;
            runner.Answer(null);
        }
        Assert.Equal(11, parks); // CombatFlightTests: 10 positions and the end frame on the target
        Assert.False(runner.Busy);
        Assert.Null(view.Flying);
    }

    [Fact]
    public void A_flight_at_instant_takes_no_time_and_ends_where_the_original_ends()
    {
        // The same long flight (the view follows the icon) at Original and at Instant: the same final view.
        (int, int) Run(AnimationSpeed s, out int parks)
        {
            using var runner = new ScriptRunner { AnimationSpeed = s };
            var view = new CombatView { Speed = 1 };
            var chooser = Chooser(runner, view);
            runner.Start(() => chooser.Flight(FlightAnimation.Bolt(1, 1, 20, 1)));
            parks = 0;
            while (runner.Pending is DelayRequest)
            {
                parks++;
                runner.Answer(null);
            }
            Assert.False(runner.Busy);
            Assert.Null(runner.Fault);
            Assert.Null(view.Flying);
            return (view.ViewX, view.ViewY);
        }
        var original = Run(AnimationSpeed.Original, out int originalParks);
        var instant = Run(AnimationSpeed.Instant, out int instantParks);
        Assert.True(originalParks > 0);
        Assert.Equal(0, instantParks);
        Assert.Equal(original, instant);
    }

    [Fact]
    public void Instant_skips_only_animation_pauses_and_the_sequence_reaches_its_last_frame()
    {
        using var runner = new ScriptRunner { AnimationSpeed = AnimationSpeed.Instant };
        int frame = -1;
        runner.Start(() =>
        {
            for (int f = 0; f < 6; f++)
            {
                frame = f;
                runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(70)) { Animation = true });
            }
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(900))); // the game-speed pause after it still waits
        });
        Assert.Equal(5, frame);
        Assert.Equal(TimeSpan.FromMilliseconds(900), Assert.IsType<DelayRequest>(runner.Pending).Duration);
        runner.Answer(null);
        Assert.False(runner.Busy);
    }

    [Fact]
    public void Picture_loops_keep_the_program_hold_at_original_and_never_flicker_at_instant()
    {
        uint[] delays = [2, 2]; // the camp's PIC 29: 0.22 s each (4 ticks)
        int ChangesIn(AnimationSpeed? s, double seconds)
        {
            var a = new PictureAnimation();
            a.Show("pic");
            a.MenuOpened(TimeSpan.Zero);
            int changes = 0, last = 0;
            for (int u = 0; u <= seconds * 60; u++)
            {
                var now = TimeSpan.FromTicks(u * TimeSpan.TicksPerSecond / 60);
                if (s is { } sp)
                    a.Tick(now, delays, sp);
                else
                    a.Tick(now, delays);
                if (a.Frame != last)
                    (changes, last) = (changes + 1, a.Frame);
            }
            return changes;
        }
        // Original: the program's own hold (s19, DOSBox-X: a change every 0.22 s), as without the setting.
        Assert.Equal(20, ChangesIn(AnimationSpeed.Original, 4.5));
        Assert.Equal(ChangesIn(null, 4.5), ChangesIn(AnimationSpeed.Original, 4.5));
        Assert.Equal(10, ChangesIn(AnimationSpeed.Slower, 4.5));
        Assert.True(ChangesIn(AnimationSpeed.Fastest, 4.5) >= 60);
        // Instant on a loop is Fastest (a loop has no last frame to jump to).
        Assert.Equal(ChangesIn(AnimationSpeed.Fastest, 4.5), ChangesIn(AnimationSpeed.Instant, 4.5));
    }

    [Fact]
    public void The_setting_is_remembered_and_read_from_the_command_line()
    {
        var settings = new ClientSettings { AnimationSpeedChoice = AnimationSpeed.Instant };
        Assert.Equal("instant", settings.AnimationSpeed);
        Assert.Equal(AnimationSpeed.Instant, ClientSettings.FromJson(settings.ToJson()).AnimationSpeedChoice);
        Assert.Equal(AnimationSpeed.Original, new ClientSettings { AnimationSpeed = "warp" }.AnimationSpeedChoice);
        Assert.Equal(AnimationSpeed.Fastest, ClientOptions.Parse(["--anim-speed", "FASTEST"]).AnimationSpeed);
        Assert.Null(ClientOptions.Parse([]).AnimationSpeed);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--anim-speed", "warp"]));
    }

    private sealed class Host : IScreenHost
    {
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }

    private static void Press(IScreen s, ActionInput input, GameAction a)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        s.Update(input);
    }

    [Fact]
    public void The_controls_screen_steps_the_animation_row_both_ways_and_applies_it()
    {
        var settings = new ClientSettings();
        var applied = new List<AnimationSpeed>();
        int saves = 0;
        var toggle = SettingToggle.Animation(settings, applied.Add, () => saves++);
        Assert.Equal("ANIMATION: ORIGINAL", toggle.Row);
        Assert.All(toggle.Help, h => Assert.True(h.Length <= 37, h));
        Assert.Contains(toggle.Help, h => h.Contains("INSTANT") && h.Contains("SPEED 0"));
        var screen = new ControlsScreen(new TextFont(new byte[177 * 8]), new Host(), toggles: [SettingToggle.AfterDefeat(settings, () => { }), toggle]);
        var input = new ActionInput(InputBindings.Defaults());
        // Up from the first action wraps to QUIT, then BACK, RESET, ANIMATION.
        for (int i = 0; i < 4; i++)
            Press(screen, input, GameAction.MenuUp);
        Press(screen, input, GameAction.MenuRight);
        Press(screen, input, GameAction.Confirm);
        Assert.Equal(AnimationSpeed.Fastest, settings.AnimationSpeedChoice);
        Press(screen, input, GameAction.MenuRight);
        Assert.Equal("ANIMATION: INSTANT", toggle.Row);
        Press(screen, input, GameAction.MenuRight);                // wraps
        Assert.Equal(AnimationSpeed.Slowest, settings.AnimationSpeedChoice);
        Press(screen, input, GameAction.MenuLeft);                 // and back
        Assert.Equal(AnimationSpeed.Instant, settings.AnimationSpeedChoice);
        Assert.Equal([AnimationSpeed.Faster, AnimationSpeed.Fastest, AnimationSpeed.Instant, AnimationSpeed.Slowest, AnimationSpeed.Instant], applied);
        Assert.Equal(5, saves);
        Assert.False(settings.AfterDefeatMenu);                    // the row above is untouched
    }
}
