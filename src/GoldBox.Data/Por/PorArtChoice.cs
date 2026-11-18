using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// Which art the player picked (PLAN §4.3): the original DOS art, the module's per-category mix ("Best",
/// <c>module.json</c> <c>art.defaults</c>), or the Amiga art wherever the module has it. Every choice falls back to DOS
/// where the chosen variant has no such asset.
/// </summary>
public enum PorArtChoice
{
    Dos,
    Best,
    Amiga,
}

/// <summary>
/// One art asset in the variant that was found: its frames (colour indices, as <see cref="DaxPicture"/>) and the
/// 16-colour palette they index (RGB triplets; the standard EGA colours for DOS art, the program's table for Amiga
/// art). An Amiga asset keeps its own shape: frame sizes and counts can differ from the DOS asset
/// (docs/re/por-art-formats.md §3, §6).
/// </summary>
public sealed record PorArtImage(string Variant, IReadOnlyList<DaxPicture> Frames, byte[] PaletteRgb);
