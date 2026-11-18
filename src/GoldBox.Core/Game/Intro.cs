namespace GoldBox.Core.Game;

/// <summary>The introduction's pages, in order.</summary>
public enum IntroStep
{
    /// <summary>TITLE block 1 (the publisher and licence logos).</summary>
    Logo,
    /// <summary>TITLE block 2 (the game's title screen), with the lightning.</summary>
    Title,
    /// <summary>The credits page (0x2DA).</summary>
    Credits,
    Done,
}

/// <summary>A line of the credits page: the program's text <c>Intro.n</c> (n = <paramref name="Text"/>) at a text cell.</summary>
public sealed record IntroLine(int Column, int Row, byte Colour, int Text);

/// <summary>
/// Pool of Radiance DOS v1.3: the introduction (GAME.OVR 0x6FB, called by START's main program before the code wheel;
/// program read, docs/re/start-menu.md §1a). The logo (TITLE block 1) for 5 seconds, the title (TITLE block 2) for 5
/// seconds with the lightning on EGA palette register 1, then the credits page for 10 seconds; a key ends the whole
/// intro (its key-buffer flush, START 0x8A94, does nothing, so every later wait ends at once). No sound. The clock is the caller's.
/// <para>
/// The lightning (0x7E2–0x8AE): each pass of the title's wait loop adds Random(4) to a counter; above 50000 the counter
/// is reset and a burst of Random(3) + 2 flashes runs (each: register 1 := 9, 11, 15, 11, 9 for 150, 100, 50, 100, 150
/// ms, then 1). Keys and the clock are only looked at after a burst, so a burst can run past the 5 seconds. How many
/// passes the loop makes per millisecond depends on the machine: <see cref="PassesPerMillisecond"/> (DOSBox-X at 3000 cycles).
/// </para>
/// </summary>
public sealed class Intro
{
    public static readonly TimeSpan LogoTime = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan TitleTime = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CreditsTime = TimeSpan.FromSeconds(10);

    /// <summary>The counter level above which a burst starts (0x808: a 32-bit counter compared with 50000).</summary>
    public const int FlashThreshold = 50000;

    /// <summary>The palette register the lightning sets (START 0x704D's first argument) and its usual colour.</summary>
    public const byte FlashRegister = 1;

    /// <summary>One flash: the colour register 1 shows and for how long (Delay START 0xA69C, milliseconds).</summary>
    public static readonly IReadOnlyList<(byte Colour, int Milliseconds)> Flash = [(9, 150), (11, 100), (15, 50), (11, 100), (9, 150)];

    /// <summary>One flash's length (550 ms).</summary>
    public static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(Flash.Sum(s => s.Milliseconds));

    /// <summary>
    /// Passes of the title's wait loop per millisecond at DOSBox-X's 3000 cycles per millisecond (measured s22: the first
    /// burst came 0.83 s into the title at 20000 cycles and 0.33 s at 50000, about 2 passes per 1000 cycles with the INT
    /// 16h and INT 21h calls; at 3000 cycles none came in the title's 5 seconds, as the average of 5.6 s predicts). On
    /// a faster machine the bursts come sooner; the original has no fixed rate.
    /// </summary>
    public const int PassesPerMillisecond = 6;

    /// <summary>The credits page's two windows (START 03F1:02E8: left, top, right, bottom inside the frame; style 0, no title).</summary>
    public static readonly IReadOnlyList<(int Left, int Top, int Right, int Bottom)> CreditWindows = [(1, 1, 38, 3), (1, 5, 38, 23)];

    /// <summary>The credits page's lines (START 0x76B6: column, row, colour, text), in the order the routine draws them.</summary>
    public static readonly IReadOnlyList<IntroLine> CreditLines =
    [
        new(5, 1, 10, 0), new(26, 1, 14, 1), new(15, 2, 10, 2), new(2, 3, 10, 3),
        new(1, 5, 10, 4), new(18, 5, 14, 5), new(14, 6, 11, 6),
        new(2, 7, 14, 7), new(20, 7, 14, 8), new(2, 8, 11, 9), new(20, 8, 14, 7),
        new(2, 9, 11, 10), new(20, 9, 11, 11), new(2, 10, 11, 12), new(20, 10, 11, 10), new(2, 11, 11, 13),
        new(2, 13, 14, 14), new(20, 13, 14, 15), new(2, 14, 11, 16), new(20, 14, 11, 17),
        new(2, 15, 11, 18), new(20, 15, 11, 12), new(2, 16, 11, 19), new(20, 16, 11, 20),
        new(2, 17, 11, 21), new(20, 17, 11, 22),
        new(2, 19, 14, 23), new(20, 19, 14, 24), new(2, 20, 11, 20), new(20, 20, 11, 25),
        new(2, 22, 14, 26), new(11, 22, 11, 27), new(2, 23, 11, 28),
    ];

    /// <summary>The number of distinct credit texts (<c>Intro.0</c> … <c>Intro.28</c>).</summary>
    public const int CreditTexts = 29;

    private static readonly long PassTicks = TimeSpan.TicksPerMillisecond / PassesPerMillisecond;

    private readonly bool _logo, _title, _flashes, _credits;
    private readonly Func<int, int> _random;
    private TimeSpan? _stepStart;
    private TimeSpan _passTime;
    private int _counter;
    private TimeSpan? _burstStart;
    private int _burstFlashes;
    private bool _keyPending;

    /// <param name="logo">TITLE block 1 is there.</param>
    /// <param name="title">A title picture is there.</param>
    /// <param name="flashes">The lightning runs (the DOS title in EGA; not the Amiga painting).</param>
    /// <param name="credits">The credits' texts are there.</param>
    /// <param name="random">Random(n): 0 … n − 1. The program draws from the game's generator; the Client gives the
    /// intro its own (how many draws the original makes depends on the machine's speed).</param>
    public Intro(bool logo, bool title, bool flashes, bool credits, Func<int, int> random)
    {
        _logo = logo;
        _title = title;
        _flashes = flashes;
        _credits = credits;
        _random = random;
        Step = IntroStep.Logo;
        Skip();
    }

    public IntroStep Step { get; private set; }

    /// <summary>Total flashes so far (tests).</summary>
    public int FlashesShown { get; private set; }

    /// <summary>The colour palette register 1 shows now (a burst running), or null (its own colour, 1).</summary>
    public byte? FlashColour(TimeSpan now)
    {
        if (Step != IntroStep.Title || _burstStart is not { } b || now < b)
            return null;
        var t = now - b;
        if (t >= FlashTime * _burstFlashes)
            return null;
        long ms = (long)t.TotalMilliseconds % (long)FlashTime.TotalMilliseconds;
        foreach (var (c, d) in Flash)
        {
            if (ms < d)
                return c;
            ms -= d;
        }
        return null;
    }

    /// <summary>
    /// A key, button or click at <paramref name="now"/>: ends the intro (after a running burst). The key stays in the
    /// buffer: the flush START 0x8A94 jumps straight to its end, so every later wait ends at once and the code wheel's
    /// input gets the key (DOSBox-X s22: one key on the logo shows the title and credits only while they are drawn).
    /// </summary>
    public void Key(TimeSpan now)
    {
        Advance(now);
        if (Step == IntroStep.Done)
            return;
        if (Step == IntroStep.Title && _burstStart is not null)
        {
            _keyPending = true;
            return;
        }
        Step = IntroStep.Done;
    }

    /// <summary>Runs the clock to <paramref name="now"/>.</summary>
    public void Advance(TimeSpan now)
    {
        while (Step != IntroStep.Done)
        {
            _stepStart ??= now;
            var start = _stepStart.Value;
            if (Step == IntroStep.Title)
            {
                if (!RunTitle(start, now))
                    return;
                continue;
            }
            var length = Step == IntroStep.Logo ? LogoTime : CreditsTime;
            if (now - start < length)
                return;
            Next(start + length);
        }
    }

    // The title's loop from the last pass up to now; true when the page ended (the next page starts at _stepStart).
    private bool RunTitle(TimeSpan start, TimeSpan now)
    {
        if (_passTime < start)
            _passTime = start;
        var end = start + TitleTime;
        while (true)
        {
            if (_burstStart is { } b)
            {
                var burstEnd = b + FlashTime * _burstFlashes;
                if (now < burstEnd)
                    return false;
                _burstStart = null;
                _passTime = burstEnd;
                // After the burst: KeyPressed (the key ends the intro, see Key), then the clock.
                if (_keyPending)
                {
                    Step = IntroStep.Done;
                    return true;
                }
                if (_passTime >= end)
                {
                    Next(_passTime);
                    return true;
                }
            }
            // A pass every 1/6 ms; the last one before the end lands on it (the clock test is only that precise).
            var step = TimeSpan.FromTicks(PassTicks);
            if (_passTime < end && _passTime + step > end)
                step = end - _passTime;
            if (_passTime + step > now)
                return false;
            _passTime += step;
            if (_flashes)
            {
                _counter += _random(4);
                if (_counter > FlashThreshold)
                {
                    _counter = 0;
                    _burstFlashes = _random(3) + 2;
                    FlashesShown += _burstFlashes;
                    _burstStart = _passTime;
                    continue;
                }
            }
            if (_passTime >= end)
            {
                Next(_passTime);
                return true;
            }
        }
    }

    private void Next(TimeSpan? at = null)
    {
        Step++;
        _stepStart = at;
        _burstStart = null;
        _keyPending = false;
        Skip();
    }

    // Pages without their art are left out (the program would stop on a missing file).
    private void Skip()
    {
        while (Step switch
        {
            IntroStep.Logo => !_logo,
            IntroStep.Title => !_title,
            IntroStep.Credits => !_credits,
            _ => false,
        })
            Step++;
    }
}
