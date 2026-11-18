namespace GoldBox.Core.Game;

/// <summary>Where a page of the Amiga intro is: the black wait before it, its fade in, its hold, its fade out.</summary>
public enum AmigaIntroPhase
{
    Gap,
    FadeIn,
    Hold,
    FadeOut,
    Done,
}

/// <summary>
/// The Amiga v1.0 intro (disk A's <c>intro</c>, run by the program after the code wheel; hunk 4 +0 of the decrunched
/// file, read s24, docs/re/start-menu.md §1a and por-art-formats.md §5): pages shown one after another, the credits
/// panels and last the title painting. Each page: a black wait (+0xB14), the screen cleared and the page blitted, a
/// fade in (+0x844 / +0x994: 16 steps, each colour component divided by 16, 15 … 1), a hold (+0xAE4), then a fade out
/// (+0x8EC / +0xA3C: divided by 1 … 16); the title page has no fade out (+0x26C ends the intro at once). The left mouse
/// button (CIA-A bit 6) is read only in the waits and holds and ends the whole intro; nothing else is read.
/// <para>
/// Timing `[?]`: every wait is a count of passes of a loop that returns while the beam is on line 128, so several
/// passes fall in one frame. The pass counts are the program's (gap 81, hold 1001, fade step 9); how many passes a
/// frame holds depends on the CPU. Estimated from 68000 cycle counts on a 7.09 MHz PAL machine (a 454-cycle line): the
/// first pass returns within 42 cycles of the line's start (the busy poll's period), and a wait pass (jsr, the beam
/// test, the CIA test, dbra) takes about 132 cycles, so passes return at about t, t + 132, t + 264 and t + 396: four a
/// frame; a fade pass takes about 90 cycles, five a line, so a step's 9 passes span two frames. Not measured on an
/// emulator.
/// </para>
/// </summary>
public sealed class AmigaIntroSequence
{
    /// <summary>The program's pass counts: +0xB14 <c>move.l #$50,d6</c>, +0xAE4 <c>#$3E8</c>, the fades' <c>#8</c> (dbra: n + 1).</summary>
    public const int GapPasses = 81, HoldPasses = 1001, FadeStepPasses = 9, FadeSteps = 16;

    /// <summary>[?] Wait passes per frame (estimate, see the class remarks) and the PAL frame.</summary>
    public const double WaitPassesPerFrame = 4;
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(20);

    /// <summary>[?] The black wait before a page, the hold, one fade step (two frames).</summary>
    public static readonly TimeSpan GapTime = Frame * (GapPasses / WaitPassesPerFrame);
    public static readonly TimeSpan HoldTime = Frame * (HoldPasses / WaitPassesPerFrame);
    public static readonly TimeSpan FadeStepTime = Frame * 2;

    private readonly int _pages;
    private TimeSpan? _start;
    private bool _skip;

    /// <param name="pages">How many pages (the last one is the title: no fade out).</param>
    public AmigaIntroSequence(int pages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 0);
        _pages = pages;
        Phase = pages == 0 ? AmigaIntroPhase.Done : AmigaIntroPhase.Gap;
    }

    public int Page { get; private set; }
    public AmigaIntroPhase Phase { get; private set; }

    /// <summary>
    /// What every colour component (0–15) is divided by now: 16 (black) in the gap, 16 … 1 through the fade in, 1 in the
    /// hold, 1 … 16 through the fade out.
    /// </summary>
    public int Divisor { get; private set; } = FadeSteps;

    /// <summary>A key, button or click: the intro ends at the next wait or hold (at once if one is running).</summary>
    public void Key(TimeSpan now)
    {
        _skip = true;
        Advance(now);
    }

    /// <summary>Runs the clock to <paramref name="now"/>.</summary>
    public void Advance(TimeSpan now)
    {
        if (Phase == AmigaIntroPhase.Done)
            return;
        _start ??= now;
        var t = now - _start.Value;
        var fade = FadeStepTime * FadeSteps;
        var page = GapTime + fade + HoldTime + fade;
        int p = (int)Math.Min(_pages, t.Ticks / page.Ticks);
        var inPage = t - page * p;
        // The title (last page) ends after its hold.
        if (p >= _pages - 1 && (p >= _pages || inPage >= GapTime + fade + HoldTime))
        {
            Phase = AmigaIntroPhase.Done;
            return;
        }
        Page = p;
        if (inPage < GapTime)
        {
            Phase = AmigaIntroPhase.Gap;
            Divisor = FadeSteps;
        }
        else if (inPage < GapTime + fade)
        {
            Phase = AmigaIntroPhase.FadeIn;
            Divisor = FadeSteps - (int)((inPage - GapTime).Ticks / FadeStepTime.Ticks);
        }
        else if (inPage < GapTime + fade + HoldTime)
        {
            Phase = AmigaIntroPhase.Hold;
            Divisor = 1;
        }
        else
        {
            Phase = AmigaIntroPhase.FadeOut;
            Divisor = 1 + (int)((inPage - GapTime - fade - HoldTime).Ticks / FadeStepTime.Ticks);
        }
        if (_skip && Phase is AmigaIntroPhase.Gap or AmigaIntroPhase.Hold)
            Phase = AmigaIntroPhase.Done;
    }

    /// <summary>A 4-bit colour component as a fade step shows it (the fades' <c>divu</c>: integer division).</summary>
    public static int Faded(int component, int divisor) => component / divisor;

    /// <summary>
    /// 16 RGB triplets (8-bit, each a 4-bit Amiga component × 17) as the fade shows them at <paramref name="divisor"/>.
    /// </summary>
    public static byte[] FadedRgb(byte[] rgb, int divisor)
    {
        var o = new byte[rgb.Length];
        for (int i = 0; i < rgb.Length; i++)
            o[i] = (byte)(Faded(rgb[i] / 17, divisor) * 17);
        return o;
    }
}
