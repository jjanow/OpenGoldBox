using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// Pool of Radiance's content as the engine and the Client read it: scripts, maps, monsters, placed treasure, the item
/// types, the program's tables and texts, the art and the shipped saves. Two sources implement it: the player's DOS
/// folder (<see cref="PorGameDirectory"/>, the DAX files and the program) and an imported module
/// (<see cref="Module.Por.PorModule"/>), which needs no game folder. Lookups take the disk number (DS:5376, VM $6E12) as the program does;
/// both sources answer null where the program's file for that disk has no such block.
/// </summary>
public interface IPorContent : IEclScriptSource, IAreaMapSource
{
    /// <summary>For messages: the folder or module the content comes from.</summary>
    string Origin { get; }

    /// <summary>The disk whose ECL file holds <paramref name="script"/> (ids are unique over the disks), or null.</summary>
    int? ScriptDisk(int script);

    /// <summary>The disk whose GEO file holds map <paramref name="id"/> (ids are unique over the disks), or null.</summary>
    int? MapDisk(int id);

    /// <summary><c>MON&lt;disk&gt;CHA/ITM/SPC</c> block <paramref name="id"/>, or null when the disk has no such monster.</summary>
    MonsterData? Monster(int disk, int id);

    /// <summary><c>ITEM&lt;disk&gt;</c> block <paramref name="id"/> (placed treasure) as 63-byte records, or null.</summary>
    IReadOnlyList<byte[]>? TreasureList(int disk, int id);

    /// <summary>The base item table (<c>ITEMS</c>), or null when absent.</summary>
    BaseItemTable? ItemTypes();

    /// <summary>The program's tables and texts (START.EXE / GAME.OVR, or the module's rules and text), or null.</summary>
    PorProgramData? Program();

    /// <summary>
    /// Art block <paramref name="id"/> of DAX file <paramref name="file"/> (name without <c>.DAX</c>, e.g. <c>CPIC3</c>,
    /// <c>8X8D1</c>, <c>DUNGCOM</c>) as a picture's frames (one frame unless the block is an animation), or null when
    /// absent or not a picture.
    /// </summary>
    IReadOnlyList<DaxPicture>? Picture(string file, int id);

    /// <summary>
    /// Art block <paramref name="id"/> of <paramref name="file"/> in the variant <paramref name="choice"/> asks for, with
    /// its palette; DOS art (EGA palette) when the variant has no such asset or the source has DOS art only (a game
    /// folder). <see cref="Picture"/> is always the DOS art, which parity screens draw.
    /// </summary>
    PorArtImage? Art(string file, int id, PorArtChoice choice) =>
        Picture(file, id) is { } f ? new PorArtImage("dos", f, Module.IndexedPng.EgaPalette) : null;

    /// <summary>A data block that is not a picture (the 8X8D1 font, WALLDEF records) as its bytes, or null.</summary>
    byte[]? ArtData(string file, int id);

    /// <summary>
    /// Whether off-map squares read 0 instead of wrapping while area script <paramref name="script"/> runs: the
    /// program's rule for the folder (GAME.OVR 0x2EC5D: scripts 0 and 10), the area's <c>noWrapOffMap</c> in a module.
    /// </summary>
    bool NoWrapOffMap(int script);

    /// <summary>The folder holding the shipped saves (<c>SAVGAM?.DAT</c>, <c>CHRDAT*</c>), or null.</summary>
    string? SaveFolder { get; }

    /// <summary>
    /// The VM profile the scripts run under (docs/module-format.md §5.1): the module's <c>vm</c>; a DOS folder is
    /// v1.3 (<see cref="EclProfiles.Default"/>).
    /// </summary>
    EclProfile VmProfile => EclProfiles.Default;
}
