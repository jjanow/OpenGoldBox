using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Monster and treasure-list data for <see cref="EncounterHost"/>: <c>MON&lt;d&gt;CHA/ITM/SPC</c>
/// (docs/re/character-format.md) and <c>ITEM&lt;d&gt;</c> (docs/re/item-format.md) from the content (the player's
/// DAX files or an imported module); the random item array and ADDNPC's portrait tables from the program's tables
/// (<see cref="IPorContent.Program"/>). The disk digit is the current disk (DS:5376), as in the program.
/// </summary>
public sealed class PorEncounterData(IPorContent content) : IEncounterData
{

    public MonsterData? Monster(int disk, int id) => content.Monster(disk, id);

    private ushort[]? _randomTable;
    private bool _randomTableRead;

    /// <summary>The random item array (<see cref="PorProgramData.RandomItemTable"/>), or null.</summary>
    public IReadOnlyList<ushort>? RandomItemTable
    {
        get
        {
            if (!_randomTableRead)
            {
                _randomTableRead = true;
                _randomTable = content.Program()?.RandomItemTable();
            }
            return _randomTable;
        }
    }

    public IReadOnlyList<byte[]>? ItemList(int disk, int id) => content.TreasureList(disk, id);

    /// <summary>ADDNPC's portrait table bytes (<see cref="PorProgramData.NpcPortraitByte"/>), or null.</summary>
    public byte? NpcPortraitByte(int dsOffset) => content.Program()?.NpcPortraitByte(dsOffset);
}
