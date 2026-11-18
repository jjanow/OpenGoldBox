using GoldBox.Core.Sound;

namespace GoldBox.Data.Module;

/// <summary>
/// <c>audio/dos/pcsound.json</c> (docs/module-format.md §10a, docs/re/sound.md §4): the data of the program's
/// PC-speaker / Tandy sound driver, read from the player's START.EXE. <see cref="Data"/> (base64) is the driver's data
/// as it sits in its code segment from <see cref="Origin"/>; the other numbers are segment offsets the driver's code
/// uses, and its timer settings. <see cref="CueTable"/> is the data-segment offset of the cue table in
/// <c>rules/program.json</c> (the 16 command words the program's sound calls push).
/// </summary>
public sealed record PcSoundDocument
{
    public const string RelativePath = "audio/dos/pcsound.json";

    /// <summary>The driver dialect the engine runs (<c>por-1.3</c>: PoR v1.3's sequencer, docs/re/sound.md §2).</summary>
    public required string Driver { get; init; }
    public int Origin { get; init; }
    public required byte[] Data { get; init; }
    public int SpeakerSongs { get; init; }
    public int TandySongs { get; init; }
    public int SpeakerNotes { get; init; }
    public int TandyNotes { get; init; }
    public int SpeakerVoices { get; init; }
    public int TandyVoices { get; init; }
    public int VoiceSize { get; init; }
    public int TuneCount { get; init; }
    public int TimerDivisor { get; init; }
    public int BiosChainTicks { get; init; }
    public int? CueTable { get; init; }
    public SourceRef? Source { get; init; }

    public const string DriverPor13 = "por-1.3";

    public static PcSoundDocument From(PcSoundDriverImage d, int? cueTable) => new()
    {
        Driver = DriverPor13,
        Origin = d.Origin, Data = d.Bytes,
        SpeakerSongs = d.SpeakerSongs, TandySongs = d.TandySongs,
        SpeakerNotes = d.SpeakerNotes, TandyNotes = d.TandyNotes,
        SpeakerVoices = d.SpeakerVoices, TandyVoices = d.TandyVoices,
        VoiceSize = d.VoiceSize, TuneCount = d.TuneCount,
        TimerDivisor = d.TimerDivisor, BiosChainTicks = d.BiosChainTicks,
        CueTable = cueTable,
        Source = new SourceRef { Container = "START.EXE" },
    };

    /// <summary>The driver image; an unknown dialect or a table outside the data fails.</summary>
    public PcSoundDriverImage ToImage()
    {
        if (Driver != DriverPor13)
            throw new FormatException($"{RelativePath}: unknown sound driver '{Driver}'");
        var d = new PcSoundDriverImage(Origin, Data, SpeakerSongs, TandySongs, SpeakerNotes, TandyNotes, SpeakerVoices,
            TandyVoices, VoiceSize, TuneCount, TimerDivisor, BiosChainTicks);
        d.Validate();
        return d;
    }
}
