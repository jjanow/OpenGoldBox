using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>The Amiga intro's page sequence (hunk 4 of the decrunched <c>intro</c>): waits, fades, holds, the button.</summary>
public class AmigaIntroSequenceTests
{
    private static readonly TimeSpan Fade = AmigaIntroSequence.FadeStepTime * AmigaIntroSequence.FadeSteps;
    private static readonly TimeSpan Page = AmigaIntroSequence.PageTime;
    private static readonly TimeSpan In = AmigaIntroSequence.FadeInStart;
    private static readonly TimeSpan Close = AmigaIntroSequence.ClosingTime;
    private static readonly TimeSpan Eps = TimeSpan.FromTicks(1);

    private static (int, AmigaIntroPhase, int) At(AmigaIntroSequence s, TimeSpan t)
    {
        s.Advance(t);
        return (s.Page, s.Phase, s.Divisor);
    }

    [Fact]
    public void Each_page_waits_fades_in_holds_and_fades_out_and_the_title_closes_after_its_hold()
    {
        var s = new AmigaIntroSequence(3);
        s.Advance(TimeSpan.Zero);
        Assert.Equal((0, AmigaIntroPhase.Gap, 16), At(s, TimeSpan.Zero));
        var g = AmigaIntroSequence.GapTime;
        var step = AmigaIntroSequence.FadeStepTime;
        Assert.Equal((0, AmigaIntroPhase.Setup, 16), At(s, g));                  // clear and blit, still black
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 16), At(s, In));                 // first step: everything / 16 = black
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 15), At(s, In + step));
        Assert.Equal((0, AmigaIntroPhase.FadeIn, 1), At(s, In + Fade - Eps));
        Assert.Equal((0, AmigaIntroPhase.Hold, 1), At(s, In + Fade));
        Assert.Equal((0, AmigaIntroPhase.FadeOut, 1), At(s, In + Fade + AmigaIntroSequence.HoldTime));
        Assert.Equal((0, AmigaIntroPhase.FadeOut, 16), At(s, Page - Eps));
        Assert.Equal((1, AmigaIntroPhase.Gap, 16), At(s, Page));
        // the last page (the title): a set-up one frame longer, no fade out; the closing black screen, then the end
        var title = AmigaIntroSequence.TitleFadeInStart;
        Assert.Equal((2, AmigaIntroPhase.Setup, 16), At(s, Page * 2 + In));
        Assert.Equal((2, AmigaIntroPhase.FadeIn, 16), At(s, Page * 2 + title));
        Assert.Equal((2, AmigaIntroPhase.FadeIn, 15), At(s, Page * 2 + title + step));
        var end = Page * 2 + title + Fade + AmigaIntroSequence.HoldTime;
        Assert.Equal((2, AmigaIntroPhase.Hold, 1), At(s, end - Eps));
        Assert.Equal(AmigaIntroPhase.Closing, At(s, end).Item2);
        Assert.Equal(16, s.Divisor);
        Assert.Equal(AmigaIntroPhase.Closing, At(s, end + Close - Eps).Item2);
        Assert.Equal(AmigaIntroPhase.Done, At(s, end + Close).Item2);
    }

    [Fact]
    public void The_button_ends_the_pages_in_a_wait_or_hold_only_and_the_closing_screen_still_follows()
    {
        var s = new AmigaIntroSequence(9);
        s.Advance(TimeSpan.Zero);
        var inFade = In + AmigaIntroSequence.FadeStepTime * 3;
        s.Key(inFade);
        Assert.Equal(AmigaIntroPhase.FadeIn, s.Phase);                            // the fades read no input
        s.Advance(In + Fade);
        Assert.Equal(AmigaIntroPhase.Closing, s.Phase);                           // the hold sees it
        s.Advance(In + Fade + Close - Eps);
        Assert.Equal(AmigaIntroPhase.Closing, s.Phase);
        s.Advance(In + Fade + Close);
        Assert.Equal(AmigaIntroPhase.Done, s.Phase);

        var t = new AmigaIntroSequence(9);
        t.Advance(TimeSpan.Zero);
        var k = TimeSpan.FromMilliseconds(10);
        t.Key(k);                                                                 // in the first wait: at once
        Assert.Equal(AmigaIntroPhase.Closing, t.Phase);
        t.Key(k + Close - Eps);                                                   // nothing is read in the Delay
        Assert.Equal(AmigaIntroPhase.Closing, t.Phase);
        t.Advance(k + Close);
        Assert.Equal(AmigaIntroPhase.Done, t.Phase);

        var u = new AmigaIntroSequence(9);                                        // in the set-up: at the hold
        u.Advance(TimeSpan.Zero);
        u.Key(AmigaIntroSequence.GapTime);
        Assert.Equal(AmigaIntroPhase.Setup, u.Phase);
        u.Advance(In + Fade);
        Assert.Equal(AmigaIntroPhase.Closing, u.Phase);

        var v = new AmigaIntroSequence(9);                                        // in a fade out: at the next wait
        v.Advance(TimeSpan.Zero);
        v.Key(Page - Eps);
        Assert.Equal(AmigaIntroPhase.FadeOut, v.Phase);
        v.Advance(Page);
        Assert.Equal(AmigaIntroPhase.Closing, v.Phase);
        Assert.Equal(AmigaIntroPhase.Done, new AmigaIntroSequence(0).Phase);
    }

    [Fact]
    public void A_button_in_the_titles_longer_set_up_is_seen_at_its_hold()
    {
        var s = new AmigaIntroSequence(2);
        s.Advance(TimeSpan.Zero);
        var k = Page + In;                                                        // a panel would be fading in here
        s.Key(k);
        Assert.Equal((1, AmigaIntroPhase.Setup, 16), (s.Page, s.Phase, s.Divisor));
        var hold = Page + AmigaIntroSequence.TitleFadeInStart + Fade;
        s.Advance(hold - Eps);
        Assert.Equal(AmigaIntroPhase.FadeIn, s.Phase);
        s.Advance(hold);
        Assert.Equal(AmigaIntroPhase.Closing, s.Phase);
        s.Advance(hold + Close);
        Assert.Equal(AmigaIntroPhase.Done, s.Phase);

        var one = new AmigaIntroSequence(1);                                      // the title alone
        one.Advance(TimeSpan.Zero);
        Assert.Equal(AmigaIntroPhase.Setup, At(one, In).Item2);
        Assert.Equal(AmigaIntroPhase.Hold, At(one, AmigaIntroSequence.TitleFadeInStart + Fade).Item2);
    }

    [Fact]
    public void Fades_divide_each_component_and_the_timing_is_the_documented_one()
    {
        Assert.Equal(0, AmigaIntroSequence.Faded(15, 16));
        Assert.Equal(1, AmigaIntroSequence.Faded(15, 15));
        Assert.Equal(7, AmigaIntroSequence.Faded(15, 2));
        Assert.Equal(9, AmigaIntroSequence.Faded(9, 1));
        Assert.Equal([0, 17, 34, 85, 0, 0], AmigaIntroSequence.FadedRgb([0, 34, 68, 170, 17, 17], 2));
        // the PAL frame (313 × 227 colour clocks at 3,546,895 Hz); 4 wait passes a frame on a 68000
        Assert.Equal(20.0319, AmigaIntroSequence.Frame.TotalMilliseconds, 3);
        Assert.Equal(5013, AmigaIntroSequence.HoldTime.TotalMilliseconds, 0);   // 1001 passes, 250.25 frames
        Assert.Equal(406, AmigaIntroSequence.GapTime.TotalMilliseconds, 0);     // 81 passes, 20.25 frames
        Assert.Equal(AmigaIntroSequence.Frame * 2, AmigaIntroSequence.FadeStepTime);
        Assert.Equal(AmigaIntroSequence.Frame * 2, AmigaIntroSequence.SetupTime);      // panels (page 1: [?])
        Assert.Equal(AmigaIntroSequence.Frame * 3, AmigaIntroSequence.TitleSetupTime); // the 16,000-word blit
        Assert.Equal(TimeSpan.FromSeconds(3), AmigaIntroSequence.ClosingTime);    // Delay(150), 50 ticks a second
    }
}
