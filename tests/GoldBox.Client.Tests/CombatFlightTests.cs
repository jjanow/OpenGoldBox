using GoldBox.Client.Play;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>The moving icon START 0x25C9 (read s25) on the combat screen's view: path, frames, waits and scrolling.</summary>
public class CombatFlightTests
{
    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 1 };
        public readonly List<(int Ms, (int Cx, int Cy, int Sprite, bool Mirror) Icon, int ViewX, int ViewY)> Draws = [];
        public readonly CombatChooser Chooser;

        public Rig()
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest d && View.Flying is { } f)
                    Draws.Add(((int)d.Duration.TotalMilliseconds, f, View.ViewX, View.ViewY));
                return null;
            }, View);
        }
    }

    [Fact]
    public void A_flight_inside_the_view_steps_a_third_of_a_square_at_a_time_and_cycles_its_frames()
    {
        var r = new Rig();
        r.Chooser.Flight(FlightAnimation.Spell(1, 1, 4, 1));
        // The line (3, 3) → (12, 3): 9 steps, 10 path entries; the walk draws on 3..12, then once more on the target.
        Assert.Equal([3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12], r.Draws.Select(d => d.Icon.Cx));
        Assert.All(r.Draws, d => Assert.Equal((30, 3, 0, 0), (d.Ms, d.Icon.Cy, d.ViewX, d.ViewY)));
        // 0x257E's frames: first, first mirrored, second (+128) mirrored, second; the frame advances per drawing.
        Assert.Equal([(5, false), (5, true), (133, true), (133, false), (5, false)],
            r.Draws.Take(5).Select(d => (d.Icon.Sprite, d.Icon.Mirror)));
        Assert.Equal((133, true), (r.Draws[^1].Icon.Sprite, r.Draws[^1].Icon.Mirror)); // the 11th: frame 10 mod 4 = 2
        Assert.Null(r.View.Flying);
    }

    [Fact]
    public void A_long_flight_moves_the_view_three_squares_past_the_icon_when_it_leaves_it()
    {
        var r = new Rig();
        r.Chooser.Flight(FlightAnimation.Bolt(1, 1, 20, 1));
        Assert.All(r.Draws, d => Assert.InRange(d.Icon.Cx, 0, 18));
        Assert.All(r.Draws, d => Assert.Equal(50, d.Ms));
        // View 0: the icon reaches cell 18 (square 6); the next step leaves the view, whose centre goes to 6 + 3.
        var first = r.Draws.TakeWhile(d => d.ViewX == 0).ToList();
        Assert.Equal(18, first[^1].Icon.Cx);
        Assert.Equal(6, r.Draws[first.Count].ViewX);
        Assert.Equal(0, r.Draws[first.Count].Icon.Cx); // redrawn from square 6 in the new view
        // The last drawing is on the target, inside the view.
        var last = r.Draws[^1];
        Assert.Equal((20 - last.ViewX) * 3, last.Icon.Cx);
        Assert.True(r.View.InView(20, 1));
    }

    [Fact]
    public void The_aim_cursor_flight_waits_nothing_but_scrolls_the_view_to_the_target()
    {
        // 0x15D02: delay 0, the cursor box (COMSPR 25). No position waits; the view follows it out of the view and
        // ends with the target in it (ScrollView(target, 3) when still out).
        var r = new Rig();
        r.Chooser.Flight(FlightAnimation.AimCursor(1, 1, 20, 1));
        Assert.Empty(r.Draws);
        Assert.True(r.View.InView(20, 1));
        Assert.Null(r.View.Flying);
    }

    [Fact]
    public void A_flight_to_its_own_square_draws_nothing()
    {
        var r = new Rig();
        r.Chooser.Flight(FlightAnimation.Spell(2, 2, 2, 2));
        Assert.Empty(r.Draws);
    }

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(4, 0, true, false)]
    [InlineData(2, 2, false, false)]
    [InlineData(6, 2, true, false)]
    [InlineData(1, 1, false, false)]
    [InlineData(3, 1, true, false)]
    [InlineData(5, 1, true, true)]
    [InlineData(7, 1, false, true)]
    public void An_arrow_takes_its_picture_by_direction(int direction, int block, bool second, bool mirror)
    {
        var a = FlightAnimation.Missile(0, 0, 3, 3, 0x49, direction);
        Assert.Equal(10, a.DelayMs);
        Assert.Equal(new FlightFrame(block, second, mirror), Assert.Single(a.Frames));
    }

    [Fact]
    public void Every_flight_picture_is_in_the_art()
    {
        // COMSPR 0–8 and their second pictures (+128): the arrows (0–2), the missiles (3, 4, 7, 8), the spell (5) and
        // the bolt (6); DOS art and Best (the Amiga with DOS filling what it lacks).
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return;
        var dir = new GoldBox.Data.Por.PorGameDirectory(d);
        foreach (var art in new[] { new CombatArt(dir) })
            for (int b = 0; b <= 8; b++)
            {
                Assert.NotNull(art.Sprite(b));
                Assert.NotNull(art.Sprite(b + 128));
            }
    }

    [Fact]
    public void Other_missiles_take_their_type_frames_and_pace()
    {
        Assert.Equal((4, 50, 3), Shape(FlightAnimation.Missile(0, 0, 1, 1, 7, 2)));
        Assert.Equal((4, 50, 4), Shape(FlightAnimation.Missile(0, 0, 1, 1, 0x55, 2)));
        Assert.Equal((2, 20, 8), Shape(FlightAnimation.Missile(0, 0, 1, 1, 0x2F, 2)));
        Assert.Equal((2, 20, 7), Shape(FlightAnimation.Missile(0, 0, 1, 1, 0x30, 2)));
        static (int, int, int) Shape(FlightAnimation a) => (a.Frames.Count, a.DelayMs, a.Frames[0].Block);
    }
}
