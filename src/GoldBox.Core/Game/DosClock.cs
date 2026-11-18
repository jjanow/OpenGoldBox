namespace GoldBox.Core.Game;

/// <summary>
/// The DOS clock the program's timed loops read (GetTime, START 0x7CCF: hundredths of a second from INT 21h 2Ch). DOS
/// derives the time from the BIOS tick count, so the hundredths move only once a tick (65536 / 1193182 s, about
/// 54.9 ms, 5.49 hundredths a tick: DOSBox-X computes ticks × 5 × 65536 / 59659). A loop that waits for a number of
/// hundredths to pass therefore waits whole ticks. The waits here are counted in whole ticks from a tick edge; a clock
/// reading can be one hundredth off that count depending on the tick's phase, which can make a wait whose length sits
/// within a hundredth of a tick multiple one tick longer or shorter (not modelled).
/// </summary>
public static class DosClock
{
    /// <summary>One BIOS tick: 65536 / 1193182 seconds.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromTicks(65536L * TimeSpan.TicksPerSecond / 1193182);

    /// <summary>The ticks until the clock has moved <b>more than</b> <paramref name="hundredths"/> (a test elapsed &gt; n).</summary>
    public static long TicksAbove(long hundredths) => hundredths < 0 ? 0 : hundredths * 59659 / 327680 + 1;

    /// <summary>The ticks until the clock has moved <b>at least</b> <paramref name="hundredths"/> (a test elapsed ≥ n).</summary>
    public static long TicksAtLeast(long hundredths) => hundredths <= 0 ? 0 : (hundredths * 59659 + 327679) / 327680;

    /// <summary>The time of <paramref name="ticks"/> BIOS ticks.</summary>
    public static TimeSpan Ticks(long ticks) => TimeSpan.FromTicks(ticks * Tick.Ticks);
}
