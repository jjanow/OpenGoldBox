using GoldBox.Core.Sound;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// Reads Pool of Radiance Amiga v1.0's music and effect data from its <c>program</c> (docs/re/sound.md §5): hunk 28
/// (music tables and samples), hunk 33's effect tables, hunk 29 (effect samples) and the cue words at hunk 31 +0x18A8.
/// It first checks the players' code: the relocated operands of the instructions that address each table must point
/// where the engine's player (<see cref="AmigaMusicPlayer"/>) expects them, the same entry points as
/// tools/scripts/por_amiga_sound_oracle.py. Nothing from the program is in our code.
/// </summary>
public static class PorAmigaSound
{
    public const int CodeHunk = 27, MusicHunk = 28, EffectSampleHunk = 29, CueHunk = 31, EffectHunk = 33;
    public const int EffectsOrigin = 0x574, CueTable = 0x18A8, CueCount = 13;

    /// <summary>(hunk, operand offset, target hunk, target offset): the table operands the players' code must have.</summary>
    private static readonly (int Hunk, int At, int Target, int Value)[] Operands =
    [
        (CodeHunk, 0x78F2, MusicHunk, 0x197C), // song table (start a song)
        (CodeHunk, 0x790A, MusicHunk, 0x1750), // voice state
        (CodeHunk, 0x7992, MusicHunk, 0x19DC), // pattern table
        (CodeHunk, 0x7D04, MusicHunk, 0x16E8), // period table (vibrato)
        (CodeHunk, 0x7FFC, MusicHunk, 0x18B8), // the one-shot repeat pointer
        (CodeHunk, 0x81A0, MusicHunk, 0x192C), // envelopes
        (CodeHunk, 0x820C, MusicHunk, 0x18C4), // instruments
        (CodeHunk, 0x826C, MusicHunk, 0x187C), // sample pointers
        (EffectHunk, 0x31C, EffectHunk, 0x574), // effect period table
        (EffectHunk, 0x1E4, EffectHunk, 0x662), // effect definitions
        (EffectHunk, 0x24A, EffectHunk, 0x6DA), // effect sample pointers
        (EffectHunk, 0x266, EffectHunk, 0x716), // effect sample lengths
        (13, 0x24DC, CueHunk, 0x18C0),          // the battle song word
        (22, 0x2B6A, CueHunk, CueTable),        // the cast cue word (the table's first)
    ];

    /// <summary>The players' data, or null when <paramref name="program"/> is not Amiga PoR v1.0's.</summary>
    public static AmigaSoundImage? Read(byte[] program)
    {
        IReadOnlyList<AmigaHunk> hunks;
        IReadOnlyList<AmigaReloc> relocs;
        try
        {
            hunks = AmigaHunks.Parse(program);
            relocs = AmigaHunks.Relocations(program);
        }
        catch (DaxFormatException)
        {
            return null;
        }
        if (hunks.Count <= EffectHunk)
            return null;
        byte[]? Bytes(int h) => hunks[h].DataOffset is { } at && at + hunks[h].Size <= program.Length
            ? program.AsSpan(at, hunks[h].Size).ToArray()
            : null;
        var set = relocs.ToHashSet();
        foreach (var (h, at, target, value) in Operands)
        {
            if (Bytes(h) is not { } b || at + 4 > b.Length || !set.Contains(new AmigaReloc(h, target, at))
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at)) != value)
                return null;
        }
        if (Bytes(MusicHunk) is not { } music || Bytes(EffectHunk) is not { } fxHunk || Bytes(EffectSampleHunk) is not { } fxSamples
            || Bytes(CueHunk) is not { } cueHunk || fxHunk.Length <= EffectsOrigin || cueHunk.Length < CueTable + 2 * CueCount)
            return null;
        var image = new AmigaSoundImage(
            music,
            [.. relocs.Where(r => r.Hunk == MusicHunk && r.Target == MusicHunk).Select(r => r.Offset).Order()],
            fxHunk[EffectsOrigin..],
            EffectsOrigin,
            [.. relocs.Where(r => r.Hunk == EffectHunk && r.Target == EffectSampleHunk).Select(r => r.Offset).Order()],
            fxSamples,
            [.. Enumerable.Range(0, CueCount).Select(k => cueHunk[CueTable + 2 * k] << 8 | cueHunk[CueTable + 2 * k + 1])]);
        try
        {
            image.Validate();
        }
        catch (FormatException)
        {
            return null;
        }
        return image;
    }
}
