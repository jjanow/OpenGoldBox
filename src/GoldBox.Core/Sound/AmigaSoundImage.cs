namespace GoldBox.Core.Sound;

/// <summary>
/// The data of Pool of Radiance Amiga v1.0's two sound players (docs/re/sound.md §5), as the importer reads it from
/// the player's <c>program</c>: the music player's data hunk (hunk 28: its tables, state, songs, patterns and samples)
/// with the offsets of the longs in it that point into itself, and the effect player's tables (hunk 33 from
/// <see cref="EffectsOrigin"/>) with the offsets of its sample pointers into <see cref="EffectSamples"/> (hunk 29).
/// The bytes are kept as they are (unrelocated), like ECL bytecode; nothing of them is in the engine source.
/// </summary>
/// <param name="Music">Hunk 28, whole.</param>
/// <param name="MusicRelocs">Offsets in <paramref name="Music"/> of the longs relocated to hunk 28 itself.</param>
/// <param name="Effects">The effect player's tables: hunk 33 from <paramref name="EffectsOrigin"/> to its end.</param>
/// <param name="EffectsOrigin">Hunk 33 offset of <paramref name="Effects"/>[0] (v1.0: 0x574, the period table).</param>
/// <param name="EffectRelocs">Hunk 33 offsets of the longs relocated to hunk 29 (the effect sample pointers).</param>
/// <param name="EffectSamples">Hunk 29, whole: the effects' 8-bit samples.</param>
/// <param name="Cues">The 13 cue words at hunk 31 +0x18A8 (the last: the battle song).</param>
public sealed record AmigaSoundImage(
    byte[] Music, int[] MusicRelocs, byte[] Effects, int EffectsOrigin, int[] EffectRelocs, byte[] EffectSamples,
    int[] Cues)
{
    /// <summary>Hunk 28 offset where the samples start; everything the music player reads below it is tables and state.</summary>
    public const int MusicSamples = 0x2C60;

    /// <summary>Checks the sizes the players rely on.</summary>
    public void Validate()
    {
        if (Music.Length < MusicSamples)
            throw new FormatException($"Amiga sound: the music data is {Music.Length} bytes, under 0x{MusicSamples:X}");
        if (MusicRelocs.Any(o => o < 0 || o + 4 > Music.Length || (o & 1) != 0))
            throw new FormatException("Amiga sound: a music relocation lies outside the data");
        if (EffectRelocs.Any(o => o < EffectsOrigin || o + 4 > EffectsOrigin + Effects.Length))
            throw new FormatException("Amiga sound: an effect relocation lies outside the effect tables");
    }
}
