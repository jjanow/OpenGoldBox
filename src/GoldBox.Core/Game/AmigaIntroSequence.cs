namespace GoldBox.Core.Game;

/// <summary>
/// Where the Amiga intro is: a page's black wait, the clear and blit after it, its fade in, its hold, its fade out; then
/// the closing black screen (intro hunk 1's Delay) and the end.
/// </summary>
public enum AmigaIntroPhase
{
    Gap,
    FadeIn,
    Hold,
    FadeOut,
    Done,
    /// <summary>The screen cleared and the page blitted (+0x7EE, +0x512 / +0x680): still black, no button read.</summary>
    Setup,
    /// <summary>Intro hunk 1's <c>Delay(150)</c> with the program's screen back: black, no input read.</summary>
    Closing,
}

/// <summary>
/// The Amiga v1.0 intro (disk A's <c>intro</c>, run by the program after the code wheel; hunk 4 +0 of the decrunched
/// file, read s24/s27, docs/re/start-menu.md §1a and por-art-formats.md §5): pages shown one after another, the
/// credits panels and last the title painting. Each page: a black wait (+0xB14), the screen cleared and the page
/// blitted (the blit first waits for beam line 240), a fade in (+0x844 / +0x994: 16 steps, each colour component
/// divided by 16, 15 … 1), a hold (+0xAE4), then a fade out (+0x8EC / +0xA3C: divided by 1 … 16); the title page has
/// no fade out (+0x26C ends hunk 4 at once). The left mouse button (CIA-A bit 6) is read only in the waits and holds;
/// a press jumps to +0x26C and back to hunk 1. Hunk 1 then always <c>Delay(150)</c>s (dos.library ticks, 50 a second
/// on PAL and NTSC: 3 s) with the system display back, which shows the program's screen: the code wheel left it
/// RectFilled with pen 0, black (hunk 25 +0x47B0). Nothing is read then.
/// <para>
/// Timing (s27, from 68000 cycle counts, not measured): every wait is a count of passes of a loop (+0x830) that
/// returns while the beam is on line 128, so several passes fall in one frame. A wait or hold pass (jsr, the beam
/// test, rts, the CIA test, dbra) is 118 cycles plus the CIA-A read, an E-clock-synchronous access; successive E-clock
/// accesses fall a multiple of 10 cycles apart, so the pass is 130 cycles (a 12-cycle CIA read: the 4-cycle read plus
/// the minimum 8 of E-clock wait). The first pass on line 128 returns within the 42-cycle period of the beam poll,
/// so the passes return at t, t + 130, t + 260 and t + 390 (at most 431) of a 454-cycle PAL line (455 NTSC) and the
/// next at t + 520 is on line 129: exactly four a frame on a 7.09 MHz 68000 (7.16 MHz NTSC). A fade pass (no CIA)
/// is 90 cycles: five or six on the line, so a step's 9 passes always span two frames. `[?]` A faster CPU gives more
/// passes a frame (shorter waits); an interrupt that lands on line 128 removes passes.
/// </para>
/// <para>
/// The frame is PAL's (313 lines of 227 colour clocks at 3,546,895 Hz, 20.03 ms; NTSC would give the same frame
/// counts at 16.72 ms, a hold of about 4.2 s). The closing 3 s is real time on both.
/// </para>
/// </summary>
public sealed class AmigaIntroSequence
{
    /// <summary>The program's pass counts: +0xB14 <c>move.l #$50,d6</c>, +0xAE4 <c>#$3E8</c>, the fades' <c>#8</c> (dbra: n + 1).</summary>
    public const int GapPasses = 81, HoldPasses = 1001, FadeStepPasses = 9, FadeSteps = 16;

    /// <summary>Wait and hold passes a frame on a 68000 (see the class remarks).</summary>
    public const double WaitPassesPerFrame = 4;

    /// <summary>The PAL frame: 313 lines × 227 colour clocks at 3,546,895 Hz.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromSeconds(313.0 * 227 / 3_546_895);

    /// <summary>The black wait before a page (81 passes, about 20 frames), the hold (1001, about 250 frames), one
    /// fade step (two frames).</summary>
    public static readonly TimeSpan GapTime = Frame * (GapPasses / WaitPassesPerFrame);
    public static readonly TimeSpan HoldTime = Frame * (HoldPasses / WaitPassesPerFrame);
    public static readonly TimeSpan FadeStepTime = Frame * 2;

    /// <summary>
    /// [?] The clear and the blit between the wait and the fade in: the blit waits for beam line 240 (+0x528) after a
    /// whole-screen blitter clear (16,000 words) begun on line 128, so the fade's first pass comes on line 128 two
    /// frames after the wait's last. Two frames rests on blitter speeds (about 2 colour clocks a word), not on the
    /// program; it would be one frame if the clear ended before line 240.
    /// </summary>
    public static readonly TimeSpan SetupTime = Frame * 2;

    /// <summary>Intro hunk 1: <c>pea $96.w</c>, <c>Delay</c> (dos.library ticks, 50 a second).</summary>
    public static readonly TimeSpan ClosingTime = TimeSpan.FromSeconds(150 / 50.0);

    /// <summary>When a page's fade in starts, from the start of its wait.</summary>
    public static TimeSpan FadeInStart => GapTime + SetupTime;

    private static TimeSpan FadeTime => FadeStepTime * FadeSteps;

    /// <summary>A whole page but the title: wait, set-up, fade in, hold, fade out.</summary>
    public static TimeSpan PageTime => FadeInStart + FadeTime + HoldTime + FadeTime;

    private readonly int _pages;
    private TimeSpan? _start, _keyAt;

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
    /// What every colour component (0–15) is divided by now: 16 (black) in the wait, the set-up and the closing
    /// screen, 16 … 1 through the fade in, 1 in the hold, 1 … 16 through the fade out.
    /// </summary>
    public int Divisor { get; private set; } = FadeSteps;

    /// <summary>
    /// A key, button or click: the pages end at the next wait or hold (at once if one is running), then the closing
    /// screen. Ignored once the pages are over.
    /// </summary>
    public void Key(TimeSpan now)
    {
        Advance(now);
        if (Phase is AmigaIntroPhase.Closing or AmigaIntroPhase.Done)
            return;
        _keyAt ??= now - _start!.Value;
        Advance(now);
    }

    /// <summary>Runs the clock to <paramref name="now"/>.</summary>
    public void Advance(TimeSpan now)
    {
        if (_pages == 0)
            return;
        _start ??= now;
        var t = now - _start.Value;
        // The pages end after the title's hold, or where a key is first seen in a wait or hold.
        var end = PageTime * (_pages - 1) + FadeInStart + FadeTime + HoldTime;
        if (_keyAt is { } k && SeenAt(k) < end)
            end = SeenAt(k);
        if (t >= end)
        {
            Phase = t >= end + ClosingTime ? AmigaIntroPhase.Done : AmigaIntroPhase.Closing;
            Divisor = FadeSteps;
            return;
        }
        int p = (int)Math.Min(_pages - 1, t.Ticks / PageTime.Ticks);
        var inPage = t - PageTime * p;
        Page = p;
        (Phase, Divisor) = Locate(inPage);
    }

    private static (AmigaIntroPhase, int) Locate(TimeSpan inPage)
    {
        if (inPage < GapTime)
            return (AmigaIntroPhase.Gap, FadeSteps);
        if (inPage < FadeInStart)
            return (AmigaIntroPhase.Setup, FadeSteps);
        if (inPage < FadeInStart + FadeTime)
            return (AmigaIntroPhase.FadeIn, FadeSteps - (int)((inPage - FadeInStart).Ticks / FadeStepTime.Ticks));
        if (inPage < FadeInStart + FadeTime + HoldTime)
            return (AmigaIntroPhase.Hold, 1);
        return (AmigaIntroPhase.FadeOut, 1 + (int)((inPage - FadeInStart - FadeTime - HoldTime).Ticks / FadeStepTime.Ticks));
    }

    /// <summary>When a key that came at <paramref name="k"/> is seen: at once in a wait or hold, else at the next one.</summary>
    private static TimeSpan SeenAt(TimeSpan k)
    {
        long p = k.Ticks / PageTime.Ticks;
        var pageStart = PageTime * p;
        var inPage = k - pageStart;
        return Locate(inPage).Item1 switch
        {
            AmigaIntroPhase.Gap or AmigaIntroPhase.Hold => k,
            AmigaIntroPhase.Setup or AmigaIntroPhase.FadeIn => pageStart + FadeInStart + FadeTime,
            _ => pageStart + PageTime,
        };
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
