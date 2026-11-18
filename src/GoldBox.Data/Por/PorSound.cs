using System.Buffers.Binary;
using GoldBox.Core.Sound;

namespace GoldBox.Data.Por;

/// <summary>
/// PoR's sounds as the engine plays them (docs/re/sound.md): the PC-speaker / Tandy driver's data and the cue table's
/// command words (DS:2612 in v1.3, indexed by <see cref="SoundCue"/>).
/// </summary>
public sealed record PorSound(PcSoundDriverImage Driver, IReadOnlyList<int> CueWords)
{
    /// <summary>Words in the cue table.</summary>
    public const int CueCount = 16;

    /// <summary>The cue words at <paramref name="at"/> in the data segment, or null when they do not fit.</summary>
    public static IReadOnlyList<int>? CueWordsAt(ReadOnlySpan<byte> ds, int at)
    {
        if (at < 0 || at + 2 * CueCount > ds.Length)
            return null;
        var words = new int[CueCount];
        for (int i = 0; i < CueCount; i++)
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(ds[(at + 2 * i)..]);
        return words;
    }

    /// <summary>Reads the driver and the cue table from the player's START.EXE, or null when either is missing.</summary>
    public static PorSound? FromProgram(byte[] startExe)
    {
        var (image, cs, ip) = PorProgram.LoadImage(startExe);
        if (PorSoundDriver.Read(image) is not { } driver || PorProgram.FindDataSegment(image, cs, ip) is not { } ds)
            return null;
        var dseg = image.AsSpan(ds);
        return PorSoundDriver.CueTable(dseg) is { } at && CueWordsAt(dseg, at) is { } words ? new PorSound(driver, words) : null;
    }

    /// <summary>A player for this sound on <paramref name="device"/>.</summary>
    public PcSoundPlayer Player(PcSoundDevice device, int sampleRate = 44100) => new(Driver, CueWords, device, sampleRate);
}
