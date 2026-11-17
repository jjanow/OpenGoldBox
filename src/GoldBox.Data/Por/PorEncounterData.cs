using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>
/// Monster and treasure-list data for <see cref="EncounterHost"/>, from the player's files:
/// <c>MON&lt;d&gt;CHA/ITM/SPC.DAX</c> (docs/re/character-format.md) and <c>ITEM&lt;d&gt;.DAX</c> (docs/re/item-format.md).
/// The disk digit is the current disk (DS:5376), as in the program.
/// </summary>
public sealed class PorEncounterData(PorGameDirectory game) : IEncounterData
{
    private readonly Dictionary<string, DaxFile?> _files = [];

    private DaxFile? File(string name)
    {
        if (!_files.TryGetValue(name, out var f))
        {
            var path = game.Find(name);
            f = path is null ? null : DaxFile.Open(path);
            _files[name] = f;
        }
        return f;
    }

    private static List<byte[]> Records(byte[]? block, int size)
    {
        var list = new List<byte[]>();
        if (block is null)
            return list;
        for (int k = 0; k + size <= block.Length; k += size)
            list.Add(block[k..(k + size)]);
        return list;
    }

    public MonsterData? Monster(int disk, int id)
    {
        var cha = File($"MON{disk}CHA.DAX")?.Block(id);
        if (cha is null)
            return null;
        return new MonsterData(cha,
            Records(File($"MON{disk}ITM.DAX")?.Block(id), EclCharacter.ItemRecordSize),
            Records(File($"MON{disk}SPC.DAX")?.Block(id), EclCharacter.EffectRecordSize));
    }

    private ushort[]? _randomTable;
    private bool _randomTableRead;

    /// <summary>The random item array from the player's START.EXE / GAME.OVR (<see cref="PorProgram"/>), or null.</summary>
    public IReadOnlyList<ushort>? RandomItemTable
    {
        get
        {
            if (!_randomTableRead)
            {
                _randomTableRead = true;
                _randomTable = PorProgram.Open(game)?.RandomItemTable();
            }
            return _randomTable;
        }
    }

    public IReadOnlyList<byte[]>? ItemList(int disk, int id)
    {
        var b = File($"ITEM{disk}.DAX")?.Block(id);
        return b is null ? null : Records(b, EclCharacter.ItemRecordSize);
    }
}
