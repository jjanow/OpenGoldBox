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

/// <summary>
/// <c>audio/amiga/sound.json</c> (docs/module-format.md §10a, docs/re/sound.md §5): the data of the Amiga version's
/// music and effect players, read from the player's <c>program</c> by the Amiga overlay import. Bytes are base64 and
/// unrelocated; <see cref="MusicRelocs"/> and <see cref="EffectRelocs"/> list the longs that are pointers.
/// </summary>
public sealed record AmigaSoundDocument
{
    public const string RelativePath = "audio/amiga/sound.json";

    /// <summary>The player dialect the engine runs (<c>amiga-por-1.0</c>: PoR Amiga v1.0's players, docs/re/sound.md §5).</summary>
    public required string Driver { get; init; }

    /// <summary>The music player's data hunk (hunk 28): tables, state, songs, patterns and samples.</summary>
    public required byte[] Music { get; init; }

    /// <summary>Offsets in <see cref="Music"/> of the longs that point into it.</summary>
    public required int[] MusicRelocs { get; init; }

    /// <summary>The effect player's tables (hunk 33 from <see cref="EffectsOrigin"/>).</summary>
    public required byte[] Effects { get; init; }

    public int EffectsOrigin { get; init; }

    /// <summary>Hunk 33 offsets of the longs that point into <see cref="EffectSamples"/>.</summary>
    public required int[] EffectRelocs { get; init; }

    /// <summary>The effects' samples (hunk 29).</summary>
    public required byte[] EffectSamples { get; init; }

    /// <summary>The 13 cue words at hunk 31 +0x18A8 (the effect each sound call plays; the last: the battle song).</summary>
    public required int[] Cues { get; init; }

    public SourceRef? Source { get; init; }

    public const string DriverPor10 = "amiga-por-1.0";

    public static AmigaSoundDocument From(AmigaSoundImage d) => new()
    {
        Driver = DriverPor10,
        Music = d.Music, MusicRelocs = d.MusicRelocs,
        Effects = d.Effects, EffectsOrigin = d.EffectsOrigin, EffectRelocs = d.EffectRelocs,
        EffectSamples = d.EffectSamples, Cues = d.Cues,
        Source = new SourceRef { Container = "program" },
    };

    /// <summary>The players' data; an unknown dialect or a table outside the data fails.</summary>
    public AmigaSoundImage ToImage()
    {
        if (Driver != DriverPor10)
            throw new FormatException($"{RelativePath}: unknown sound driver '{Driver}'");
        var d = new AmigaSoundImage(Music, MusicRelocs, Effects, EffectsOrigin, EffectRelocs, EffectSamples, Cues);
        d.Validate();
        return d;
    }
}
