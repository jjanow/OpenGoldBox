namespace GoldBox.Core.Sound;

/// <summary>
/// Where the game logic sends the program's sound calls (docs/re/sound.md §2.4): a cue (sound command with a cue
/// table word) and the resident brackets (set auto-stop 0 … 10) the program puts around its fight animations. The
/// logic calls it at the same points as the program; what it sounds like is the front end's business.
/// </summary>
public interface ISoundSink
{
    void Cue(SoundCue cue);

    /// <summary>Set auto-stop(0): what follows plays until <see cref="EndResident"/> (or its own end).</summary>
    void BeginResident();

    /// <summary>Set auto-stop(10): uninstalls the timer, which silences what still sounds.</summary>
    void EndResident();

    /// <summary>
    /// A resident bracket with <paramref name="cue"/> and nothing drawn or waited between set auto-stop(0) and (10): a
    /// spell's flight whose path has under two entries (aimed at the caster's own square; the moving icon START 0x2688
    /// returns at once). The timer is uninstalled long before its first tick (the new rate only reaches PIT channel 0
    /// when the running BIOS period ends), so the cue sounds nothing there and whatever still sounded is cut; the tune
    /// is started in the driver all the same and ticks in the next bracket until that bracket's cue (DOSBox-X s43: a
    /// self-aimed cast is silent, an aimed one plays its tune through the flight; docs/re/sound.md §2.4). A front end
    /// must not render the tune between the three calls.
    /// </summary>
    void EmptyResident(SoundCue cue)
    {
        BeginResident();
        Cue(cue);
        EndResident();
    }
}

/// <summary>No sound (headless runs, tests).</summary>
public sealed class NoSound : ISoundSink
{
    public static readonly NoSound Instance = new();
    public void Cue(SoundCue cue) { }
    public void BeginResident() { }
    public void EndResident() { }
}

/// <summary>Records the calls (tests).</summary>
public sealed class SoundLog : ISoundSink
{
    public List<string> Calls { get; } = [];
    public void Cue(SoundCue cue) => Calls.Add(cue.ToString());
    public void BeginResident() => Calls.Add("(");
    public void EndResident() => Calls.Add(")");
}
