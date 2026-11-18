using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The introduction (GAME.OVR 0x6FB): page times, keys, the lightning model, the credits page.</summary>
public class IntroTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));

    // Random(n) answering n − 1 every time: the counter grows by 3 a pass, bursts have 4 flashes.
    private static int Max(int n) => n - 1;

    [Fact]
    public void Pages_follow_the_clock()
    {
        var intro = new Intro(true, true, false, true, Max);
        intro.Advance(Ms(0));
        Assert.Equal(IntroStep.Logo, intro.Step);
        intro.Advance(Ms(4999));
        Assert.Equal(IntroStep.Logo, intro.Step);
        intro.Advance(Ms(5000));
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(9999));
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(10000));
        Assert.Equal(IntroStep.Credits, intro.Step);
        intro.Advance(Ms(19999));
        Assert.Equal(IntroStep.Credits, intro.Step);
        intro.Advance(Ms(20000));
        Assert.Equal(IntroStep.Done, intro.Step);
        Assert.Equal(0, intro.FlashesShown);
    }

    [Fact]
    public void A_late_frame_catches_up_every_page()
    {
        var intro = new Intro(true, true, false, true, Max);
        intro.Advance(Ms(0));
        intro.Advance(Ms(25000));
        Assert.Equal(IntroStep.Done, intro.Step);
    }

    [Fact]
    public void A_key_ends_the_whole_intro()
    {
        // The flush START 0x8A94 does nothing: the key stays and every later wait ends at once (DOSBox-X s22).
        var intro = new Intro(true, true, false, true, Max);
        intro.Advance(Ms(0));
        intro.Key(Ms(100));
        Assert.Equal(IntroStep.Done, intro.Step);
        intro.Key(Ms(200));
        Assert.Equal(IntroStep.Done, intro.Step);
        var credits = new Intro(true, true, false, true, Max);
        credits.Advance(Ms(0));
        credits.Advance(Ms(10000));
        Assert.Equal(IntroStep.Credits, credits.Step);
        credits.Key(Ms(10000));
        Assert.Equal(IntroStep.Done, credits.Step);
    }

    [Fact]
    public void Missing_pages_are_left_out()
    {
        Assert.Equal(IntroStep.Title, new Intro(false, true, true, true, Max).Step);
        Assert.Equal(IntroStep.Credits, new Intro(false, false, true, true, Max).Step);
        Assert.Equal(IntroStep.Done, new Intro(false, false, false, false, Max).Step);
        var intro = new Intro(true, false, false, true, Max);
        intro.Advance(Ms(0));
        intro.Advance(Ms(5000));
        Assert.Equal(IntroStep.Credits, intro.Step);
    }

    [Fact]
    public void Lightning_bursts_come_from_the_counter_and_can_outlast_the_title()
    {
        var intro = new Intro(false, true, true, true, Max);     // the title from 0 ms
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(0));
        // 3 a pass: above 50000 after 16667 passes of 1666 ticks (1/6 ms, truncated) = 2776.7222 ms.
        double b = 16667 * 1666 / 10000.0;
        intro.Advance(Ms(b - 0.01));
        Assert.Equal(0, intro.FlashesShown);
        Assert.Null(intro.FlashColour(Ms(b - 0.01)));
        intro.Advance(Ms(b));
        Assert.Equal(4, intro.FlashesShown);                     // Random(3) + 2
        Assert.Equal((byte)9, intro.FlashColour(Ms(b)));
        Assert.Equal((byte)11, intro.FlashColour(Ms(b + 150)));
        Assert.Equal((byte)15, intro.FlashColour(Ms(b + 250)));
        Assert.Equal((byte)11, intro.FlashColour(Ms(b + 300)));
        Assert.Equal((byte)9, intro.FlashColour(Ms(b + 400)));
        Assert.Equal((byte)9, intro.FlashColour(Ms(b + 550)));   // the next flash
        Assert.Equal((byte)15, intro.FlashColour(Ms(b + 3 * 550 + 260)));
        Assert.Null(intro.FlashColour(Ms(b + 4 * 550 + 1)));
        // A key during a burst is seen once the burst is over; then it ends the intro.
        intro.Key(Ms(b + 100));
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(b + 2200 - 1));
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(b + 2200));
        Assert.Equal(IntroStep.Done, intro.Step);
    }

    [Fact]
    public void The_clock_is_read_after_a_burst()
    {
        // At 6 passes a millisecond even the fastest counter (3 a pass) bursts only once in the title: 2776.72 ms to
        // 4976.72 ms (4 flashes); the next would need another 2.8 s, so the title ends on the clock at 5 s.
        var intro = new Intro(false, true, true, false, Max);
        intro.Advance(Ms(0));
        intro.Advance(Ms(4976));
        Assert.Equal(IntroStep.Title, intro.Step);
        Assert.Equal(4, intro.FlashesShown);
        intro.Advance(Ms(4999.9));
        Assert.Equal(IntroStep.Title, intro.Step);
        intro.Advance(Ms(5000));
        Assert.Equal(IntroStep.Done, intro.Step);
        Assert.Equal(4, intro.FlashesShown);
    }

    [Fact]
    public void No_lightning_without_flashes()
    {
        var intro = new Intro(false, true, false, false, Max);
        intro.Advance(Ms(0));
        intro.Advance(Ms(2000));
        Assert.Null(intro.FlashColour(Ms(2000)));
        Assert.Equal(0, intro.FlashesShown);
    }

    [Fact]
    public void Credit_lines_sit_inside_the_windows_and_use_every_text()
    {
        Assert.Equal(33, Intro.CreditLines.Count);
        Assert.Equal(Enumerable.Range(0, Intro.CreditTexts), Intro.CreditLines.Select(l => l.Text).Distinct().Order());
        foreach (var l in Intro.CreditLines)
            Assert.Contains(Intro.CreditWindows, w => l.Column >= w.Left && l.Column <= w.Right && l.Row >= w.Top && l.Row <= w.Bottom);
        Assert.All(Intro.CreditLines, l => Assert.Contains(l.Colour, new byte[] { 10, 11, 14 }));
        for (int i = 0; i < Intro.CreditTexts; i++)
            Assert.True(PorTextCatalog.Overlay.ContainsKey($"Intro.{i}"), $"Intro.{i}");
    }

    // ---- the player's program ----

    [PorFact]
    public void Credit_texts_fit_their_lines()
    {
        var p = PorProgramData.Open(PorData.Open())!;
        foreach (var l in Intro.CreditLines)
        {
            var s = p.Text($"Intro.{l.Text}");
            Assert.False(string.IsNullOrEmpty(s), $"Intro.{l.Text}");
            Assert.True(l.Column + s!.Length <= 39, $"Intro.{l.Text} runs into the frame");
        }
    }

    /// <summary>
    /// The credits routine (GAME.OVR 0x2DA, unit base 0xD4) read from the player's program: each call is
    /// <c>mov al,a / push ax</c> × n, <c>lea di,[bp-x] / push ss / push di / mov di,string / push cs / push di</c>,
    /// StrLoad (0AF8:0B0B), then the window routine (03F1:02E8) or the text routine (0709:0626); the page ends with
    /// <c>mov ax,seconds / push ax / call 0xD4</c>. The windows, lines and wait are those of <see cref="Intro"/>.
    /// </summary>
    [PorFact]
    public void Credits_page_matches_the_program()
    {
        var game = PorData.Open();
        var o = File.ReadAllBytes(game.Find("GAME.OVR")!);
        const int unit = 0xD4;
        int i = 0x2E0;
        var windows = new List<(int, int, int, int)>();
        var lines = new List<IntroLine>();
        var textOf = PorTextCatalog.Overlay.Where(kv => kv.Key.StartsWith("Intro.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Value, kv => int.Parse(kv.Key["Intro.".Length..]));
        while (o[i] != 0xB8)
        {
            var args = new List<int>();
            while (o[i] == 0xB0 && o[i + 2] == 0x50)
            {
                args.Add(o[i + 1]);
                i += 3;
            }
            Assert.Equal([0x8D, 0x7E], o[i..(i + 2)]);
            Assert.Equal(0xBF, o[i + 5]);
            int str = unit + (o[i + 6] | o[i + 7] << 8);
            i += 10;
            Assert.Equal([0x9A, 0x0B, 0x0B, 0xF8, 0x0A], o[i..(i + 5)]);
            var call = o[(i + 5)..(i + 10)];
            i += 10;
            if (call.SequenceEqual(new byte[] { 0x9A, 0xE8, 0x02, 0xF1, 0x03 }))
            {
                Assert.Equal([0, 0], args[4..]);                 // style 0, colour 0
                Assert.Equal(0, o[str]);                         // no title
                windows.Add((args[0], args[1], args[2], args[3]));
            }
            else
            {
                Assert.Equal([0x9A, 0x26, 0x06, 0x09, 0x07], call);
                lines.Add(new IntroLine(args[0], args[1], (byte)args[2], textOf[str]));
            }
        }
        Assert.Equal(Intro.CreditWindows, windows);
        Assert.Equal(Intro.CreditLines, lines);
        Assert.Equal((int)Intro.CreditsTime.TotalSeconds, o[i + 1]);
        Assert.Equal([0x00, 0x50, 0x0E, 0xE8], o[(i + 2)..(i + 6)]);
    }
}
