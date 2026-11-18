using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The Amiga intro's page sequence (hunk 4 of the decrunched <c>intro</c>): waits, fades, holds, the button.</summary>
public class AmigaIntroSequenceTests
{
    private static readonly TimeSpan Fade = AmigaIntroSequence.FadeStepTime * AmigaIntroSequence.FadeSteps;
    private static readonly TimeSpan Page = AmigaIntroSequence.GapTime + Fade + AmigaIntroSequence.HoldTime + Fade;
    private static readonly TimeSpan Eps = TimeSpan.FromTicks(1);

    private static (int, AmigaIntroPhase, int) At(AmigaIntroSequence s, TimeSpan t)
    {
        s.Advance(t);
        return (s.Page, s.Phase, s.Divisor);
    }

    [Fact]
    public void Each_page_waits_fades_in_holds_and_fades_out_and_the_title_ends_after_its_hold()
    {
        var s = new AmigaIntroSequence(3);
        s.Advance(TimeSpan.Zero);
        Assert.Equal((0, AmigaIntroPhase.Gap, 16), At(s, TimeSpan.Zero));
        var g = AmigaIntroSequence.GapTime;
        var step = AmigaIntroSequence.FadeStepTime;
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 16), At(s, g));                  // first step: everything / 16 = black
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 15), At(s, g + step));
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 1), At(s, g + Fade - Eps));
        Assert.Equal((0, AmigaIntroPhase.Hold, 1), At(s, g + Fade));
        Assert.Equal((0, AmigaIntroPhase.FadeOut, 1), At(s, g + Fade + AmigaIntroSequence.HoldTime));
        Assert.Equal((0, AmigaIntroPhase.FadeOut, 16), At(s, Page - Eps));
        Assert.Equal((1, AmigaIntroPhase.Gap, 16), At(s, Page));
        // the last page (the title) has no fade out
        Assert.Equal((2, AmigaIntroPhase.Hold, 1), At(s, Page * 2 + g + Fade + AmigaIntroSequence.HoldTime - Eps));
        Assert.Equal(AmigaIntroPhase.Done, At(s, Page * 2 + g + Fade + AmigaIntroSequence.HoldTime).Item2);
    }

    [Fact]
    public void The_button_ends_the_whole_intro_in_a_wait_or_hold_only()
    {
        var s = new AmigaIntroSequence(9);
        s.Advance(TimeSpan.Zero);
        var inFade = AmigaIntroSequence.GapTime + AmigaIntroSequence.FadeStepTime * 3;
        s.Key(inFade);
        Assert.Equal(AmigaIntroPhase.FadeIn, s.Phase);                            // the fades read no input
        s.Advance(AmigaIntroSequence.GapTime + Fade);
        Assert.Equal(AmigaIntroPhase.Done, s.Phase);                              // the hold sees it: the end

        var t = new AmigaIntroSequence(9);
        t.Advance(TimeSpan.Zero);
        t.Key(TimeSpan.FromMilliseconds(10));                                     // in the first wait: at once
        Assert.Equal(AmigaIntroPhase.Done, t.Phase);
        Assert.Equal(AmigaIntroPhase.Done, new AmigaIntroSequence(0).Phase);
    }

    [Fact]
    public void Fades_divide_each_component_and_the_timing_estimate_is_the_documented_one()
    {
        Assert.Equal(0, AmigaIntroSequence.Faded(15, 16));
        Assert.Equal(1, AmigaIntroSequence.Faded(15, 15));
        Assert.Equal(7, AmigaIntroSequence.Faded(15, 2));
        Assert.Equal(9, AmigaIntroSequence.Faded(9, 1));
        Assert.Equal([0, 17, 34, 85, 0, 0], AmigaIntroSequence.FadedRgb([0, 34, 68, 170, 17, 17], 2));
        // [?] 4 wait passes a frame at 50 Hz: hold 1001 passes ≈ 5.0 s, gap 81 ≈ 0.4 s, a fade step two frames
        Assert.Equal(5005, AmigaIntroSequence.HoldTime.TotalMilliseconds, 0);
        Assert.Equal(405, AmigaIntroSequence.GapTime.TotalMilliseconds, 0);
        Assert.Equal(TimeSpan.FromMilliseconds(40), AmigaIntroSequence.FadeStepTime);
    }
}
