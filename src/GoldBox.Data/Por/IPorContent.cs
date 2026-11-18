using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Pool of Radiance's content as the engine reads it: scripts, maps, monsters, placed treasure and the item types.
/// Two sources implement it: the player's DOS folder (<see cref="PorGameDirectory"/>, the DAX files) and an imported
/// module (<see cref="Module.Por.PorModule"/>). Lookups take the disk number (DS:5376, VM $6E12) as the program does;
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
}
