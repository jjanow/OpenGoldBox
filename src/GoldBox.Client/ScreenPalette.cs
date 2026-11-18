using Microsoft.Xna.Framework;

namespace GoldBox.Client;

/// <summary>
/// The 256 colours a frame's bytes index: 0–15 are the EGA colours the DOS art and the interface use; every other
/// 16-colour palette an asset brings (the Amiga pictures each carry the program's table for their file,
/// docs/module-format.md §10) gets the next free run of 16 (16–31, 32–47, …), so pictures with different palettes
/// can share one frame. Index 0 of a run is a real colour; see-through pixels are decided before an asset's indices
/// are moved into its run. Safe to call from the logic thread (a wall set loads there).
/// </summary>
public sealed class ScreenPalette
{
    public const int Size = 256, RunLength = 16;

    private readonly Color[] _colours = new Color[Size];
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _next = RunLength;

    public ScreenPalette()
    {
        FrameTexture.Ega.CopyTo(_colours, 0);
        for (int i = RunLength; i < Size; i++)
            _colours[i] = FrameTexture.Ega[i & 15]; // unset runs: as the EGA-only upload (index & 15) showed them
    }

    /// <summary>The colours, index → colour (read every frame).</summary>
    public IReadOnlyList<Color> Colours => _colours;

    /// <summary>
    /// The first index of the run holding <paramref name="rgb"/> (16 RGB triplets): 0 for the EGA colours themselves,
    /// else the run given to that palette the first time it was asked for. Throws when all 15 runs are taken.
    /// </summary>
    public int Base(byte[] rgb)
    {
        if (rgb.Length < RunLength * 3)
            throw new ArgumentException("a palette of 16 RGB triplets expected", nameof(rgb));
        lock (_sync)
        {
            var key = Convert.ToHexString(rgb, 0, RunLength * 3);
            if (IsEga(rgb))
                return 0;
            if (_runs.TryGetValue(key, out int b))
                return b;
            if (_next + RunLength > Size)
                throw new InvalidOperationException("screen palette full: more than 15 distinct 16-colour palettes");
            b = _next;
            _next += RunLength;
            for (int i = 0; i < RunLength; i++)
                _colours[b + i] = new Color(rgb[3 * i], rgb[3 * i + 1], rgb[3 * i + 2]);
            _runs[key] = b;
            return b;
        }
    }

    private static bool IsEga(byte[] rgb)
    {
        for (int i = 0; i < RunLength; i++)
            if (FrameTexture.Ega[i] != new Color(rgb[3 * i], rgb[3 * i + 1], rgb[3 * i + 2]))
                return false;
        return true;
    }
}
