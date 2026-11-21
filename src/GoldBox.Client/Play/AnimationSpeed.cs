namespace GoldBox.Client.Play;

/// <summary>
/// QoL "Animation speed" (<see cref="ClientSettings.AnimationSpeed"/>; off = <see cref="Original"/>): scales the fixed
/// program delays that the original's game speed (DS:49E2, camp Alter &gt; Speed, the fight's Done &gt; Speed) does not
/// cover. The program never scales these (docs/re/combat-menu.md "Animation speed"); the game speed's own pauses and
/// typing stay as they are. Scaled (<see cref="DelayRequest.Animation"/>): the moving icon (missile, spell, breath and
/// gaze flights, START 0x25C9's Delay), the hit and effect pictures (Delay(70), START 0x2CCB), the attack pose
/// (Delay(100), 0x1412E), the Client's one-frame display points of an AI creature's steps and turn start, the Amiga end
/// game's frame holds (Delay(100) ticks), and the picture window's looping animation (<see cref="PictureAnimation"/>).
/// Not scaled: every game-speed pause and typing, the camp's key poll, the fixed message pauses (encounter line 900 ms,
/// party killed 3 s), the DOS end game's frames ((speed + 3) × delay, already game speed), the intro and title (a key
/// ends them).
/// </summary>
public enum AnimationSpeed
{
    Slowest,
    Slower,
    Original,
    Faster,
    Fastest,
    Instant,
}

/// <summary>The scaling rules of <see cref="AnimationSpeed"/>, in one place (applied by <see cref="ScriptRunner.Ask"/>).</summary>
public static class AnimationTiming
{
    /// <summary>One Client update (60 Hz): the length of a zero-delay display point at Original.</summary>
    public static readonly TimeSpan OneUpdate = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>The factor on a fixed delay: 3, 2, 1, 0.5, 0.25, 0.</summary>
    public static double Multiplier(AnimationSpeed s) => s switch
    {
        AnimationSpeed.Slowest => 3,
        AnimationSpeed.Slower => 2,
        AnimationSpeed.Faster => 0.5,
        AnimationSpeed.Fastest => 0.25,
        AnimationSpeed.Instant => 0,
        _ => 1,
    };

    /// <summary>
    /// A tagged pause at speed <paramref name="s"/>: the same request at Original (untouched, so every timing stays the
    /// program's); null at Instant (the pause is skipped, not shown: the sequence runs on to its last frame, which the
    /// next request shows). A zero-length display point (one update) stays one update when faster, and is held for one
    /// update × the factor when slower. Untagged requests are returned as they are.
    /// </summary>
    public static PlayerRequest? Apply(PlayerRequest request, AnimationSpeed s)
    {
        if (request is not DelayRequest { Animation: true } d || s == AnimationSpeed.Original)
            return request;
        double m = Multiplier(s);
        if (m == 0)
            return null;
        if (d.Duration == TimeSpan.Zero)
            return m > 1 ? d with { Duration = OneUpdate * m } : d;
        return d with { Duration = d.Duration * m };
    }

    /// <summary>
    /// The hold of a looping picture frame (<see cref="PictureAnimation"/>, in centiseconds of the DOS clock): the
    /// program's own at Original; at Instant the Fastest factor, since a loop has no last frame to jump to (changing it
    /// on every update would only flicker).
    /// </summary>
    public static double LoopHold(long centiseconds, AnimationSpeed s) =>
        s == AnimationSpeed.Original ? centiseconds
        : centiseconds * Multiplier(s == AnimationSpeed.Instant ? AnimationSpeed.Fastest : s);

    /// <summary>The settings word (<c>"original"</c> …), or null for an unknown word.</summary>
    public static AnimationSpeed? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "slowest" => AnimationSpeed.Slowest,
        "slower" => AnimationSpeed.Slower,
        "original" => AnimationSpeed.Original,
        "faster" => AnimationSpeed.Faster,
        "fastest" => AnimationSpeed.Fastest,
        "instant" => AnimationSpeed.Instant,
        _ => null,
    };

    /// <summary>The settings word of <paramref name="s"/> (lower case).</summary>
    public static string Word(AnimationSpeed s) => s.ToString().ToLowerInvariant();

    /// <summary>The controls screen's word (upper case).</summary>
    public static string Label(AnimationSpeed s) => s.ToString().ToUpperInvariant();
}
