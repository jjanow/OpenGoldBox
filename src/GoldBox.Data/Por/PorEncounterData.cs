using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Monster and treasure-list data for <see cref="EncounterHost"/>: <c>MON&lt;d&gt;CHA/ITM/SPC</c>
/// (docs/re/character-format.md) and <c>ITEM&lt;d&gt;</c> (docs/re/item-format.md) from the content (the player's
/// DAX files or an imported module); the random item array from the player's program. The disk digit is the current
/// disk (DS:5376), as in the program.
/// </summary>
public sealed class PorEncounterData(IPorContent content, PorGameDirectory? programFolder) : IEncounterData
{
    public PorEncounterData(PorGameDirectory game) : this(game, game)
    {
    }

    public MonsterData? Monster(int disk, int id) => content.Monster(disk, id);

    private ushort[]? _randomTable;
    private bool _randomTableRead;

    /// <summary>
    /// The random item array from the player's START.EXE / GAME.OVR (<see cref="PorProgram"/>), or null. [?] Not in
    /// a module yet (rules tables, docs/module-format.md §12): read from the program folder.
    /// </summary>
    public IReadOnlyList<ushort>? RandomItemTable
    {
        get
        {
            if (!_randomTableRead)
            {
                _randomTableRead = true;
                _randomTable = programFolder is null ? null : PorProgram.Open(programFolder)?.RandomItemTable();
            }
            return _randomTable;
        }
    }

    public IReadOnlyList<byte[]>? ItemList(int disk, int id) => content.TreasureList(disk, id);

    private PorProgram? _program;
    private bool _programRead;

    /// <summary>
    /// ADDNPC's portrait table bytes from the player's START.EXE (<see cref="PorProgram.NpcPortraitByte"/>), or null.
    /// [?] Not in a module yet (rules tables, docs/module-format.md §12): read from the program folder.
    /// </summary>
    public byte? NpcPortraitByte(int dsOffset)
    {
        if (!_programRead)
        {
            _programRead = true;
            _program = programFolder is null ? null : PorProgram.Open(programFolder);
        }
        return _program?.NpcPortraitByte(dsOffset);
    }
}
