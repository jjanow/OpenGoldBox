namespace GoldBox.Core.Sound;

/// <summary>
/// The data of PoR's PC-speaker / Tandy sound driver (docs/re/sound.md §2), as the importer reads it from the player's
/// START.EXE: the driver's data bytes (song tables, note tables, LFO waveforms, tune streams and envelopes) placed at
/// their offset in the driver's code segment, plus the offsets the driver's code uses. Stream, envelope and LFO
/// pointers inside the bytes are offsets in that segment, so the bytes are kept as they are, like ECL bytecode.
/// Nothing of it is in the engine source.
/// </summary>
/// <param name="Origin">Segment offset of <paramref name="Bytes"/>[0] (v1.3: 0x01D7, the BIOS-chain counter).</param>
/// <param name="Bytes">The driver's data, from <paramref name="Origin"/> up to its timer-install routine.</param>
/// <param name="SpeakerSongs">Song table A (PC speaker): per tune four stream offsets, voice 0..3; 0 = keep the voice.</param>
/// <param name="TandySongs">Song table B (Tandy SN76489), same shape.</param>
/// <param name="SpeakerNotes">The speaker's 12 PIT divisors of its lowest octave.</param>
/// <param name="TandyNotes">The Tandy's 12 periods (shifted left 6) of its lowest octave.</param>
/// <param name="SpeakerVoices">The four speaker voice blocks.</param>
/// <param name="TandyVoices">The four Tandy voice blocks.</param>
/// <param name="VoiceSize">Bytes per voice block (v1.3: 0x30).</param>
/// <param name="TuneCount">Rows in each song table (v1.3: 21, tunes 0..20).</param>
/// <param name="TimerDivisor">PIT channel 0 divisor while the driver's timer runs (v1.3: 5041 → 236.7 Hz).</param>
/// <param name="BiosChainTicks">The driver chains the BIOS timer handler every this many ticks (v1.3: 13).</param>
public sealed record PcSoundDriverImage(
    int Origin, byte[] Bytes,
    int SpeakerSongs, int TandySongs,
    int SpeakerNotes, int TandyNotes,
    int SpeakerVoices, int TandyVoices,
    int VoiceSize, int TuneCount, int TimerDivisor, int BiosChainTicks)
{
    /// <summary>The PC's PIT input clock (Hz).</summary>
    public const double PitHz = 1_193_182.0;

    /// <summary>Driver ticks per second.</summary>
    public double TickHz => PitHz / (TimerDivisor == 0 ? 65536 : TimerDivisor);

    /// <summary>Checks that every table the driver reads lies inside <see cref="Bytes"/>.</summary>
    public void Validate()
    {
        int end = Origin + Bytes.Length;
        void In(string what, int at, int len)
        {
            if (at < Origin || at + len > end)
                throw new FormatException($"sound driver: {what} at 0x{at:X4} is outside the data 0x{Origin:X4}..0x{end:X4}");
        }
        if (VoiceSize < 0x26 || TuneCount < 1 || BiosChainTicks < 1)
            throw new FormatException("sound driver: bad voice size, tune count or BIOS chain count");
        In("speaker song table", SpeakerSongs, TuneCount * 8);
        In("Tandy song table", TandySongs, TuneCount * 8);
        In("speaker notes", SpeakerNotes, 24);
        In("Tandy notes", TandyNotes, 24);
        In("speaker voices", SpeakerVoices, 4 * VoiceSize);
        In("Tandy voices", TandyVoices, 4 * VoiceSize);
    }
}
