using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>The player's Pool of Radiance (DOS) folder. File names are matched case-insensitively.</summary>
public sealed class PorGameDirectory : IPorContent
{
    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<int, DaxFile?> _ecl = [];

    public string Root { get; }

    public string Origin => Root;

    public PorGameDirectory(string root)
    {
        Root = root;
        _files = Directory.EnumerateFiles(root)
            .GroupBy(f => System.IO.Path.GetFileName(f).ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Full path of a file by name (any case), or null.</summary>
    public string? Find(string name) => _files.TryGetValue(name.ToUpperInvariant(), out var p) ? p : null;

    private readonly Dictionary<string, DaxFile?> _dax = [];

    /// <summary>A DAX file by name (any case), read once and kept; null when the folder has no such file.</summary>
    public DaxFile? Dax(string name)
    {
        name = name.ToUpperInvariant();
        if (!_dax.TryGetValue(name, out var f))
        {
            var path = Find(name);
            f = path is null ? null : DaxFile.Open(path);
            _dax[name] = f;
        }
        return f;
    }

    /// <summary>ECL1.DAX .. ECL8.DAX that exist, as (disk, file).</summary>
    public IEnumerable<(int Disk, DaxFile File)> EclFiles()
    {
        for (int d = 1; d <= 8; d++)
            if (Ecl(d) is { } f)
                yield return (d, f);
    }

    public DaxFile? Ecl(int disk)
    {
        if (!_ecl.TryGetValue(disk, out var f))
        {
            var path = Find($"ECL{disk}.DAX");
            f = path is null ? null : DaxFile.Open(path);
            _ecl[disk] = f;
        }
        return f;
    }

    /// <inheritdoc />
    public byte[]? LoadBlock(int disk, int script) => Ecl(disk)?.Block(script);

    /// <summary>Disk whose ECL file holds <paramref name="script"/> (block ids are unique over the eight files).</summary>
    public int? DiskOf(int script) => EclFiles().Where(x => x.File.Contains(script)).Select(x => (int?)x.Disk).FirstOrDefault();

    public int? ScriptDisk(int script) => DiskOf(script);

    /// <summary>GEO&lt;disk&gt; block <paramref name="id"/>, or null when absent or not a 1026-byte map.</summary>
    public AreaMap? LoadMap(int disk, int id) =>
        Dax($"GEO{disk}.DAX")?.Block(id) is { Length: PorGeo.BlockSize } b ? PorGeo.Parse(b, id) : null;

    public int? MapDisk(int id) => PorGeo.DiskOf(this, id);

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
        var cha = Dax($"MON{disk}CHA.DAX")?.Block(id);
        if (cha is null)
            return null;
        return new MonsterData(cha,
            Records(Dax($"MON{disk}ITM.DAX")?.Block(id), EclCharacter.ItemRecordSize),
            Records(Dax($"MON{disk}SPC.DAX")?.Block(id), EclCharacter.EffectRecordSize));
    }

    public IReadOnlyList<byte[]>? TreasureList(int disk, int id)
    {
        var b = Dax($"ITEM{disk}.DAX")?.Block(id);
        return b is null ? null : Records(b, EclCharacter.ItemRecordSize);
    }

    public BaseItemTable? ItemTypes() =>
        Find("ITEMS") is { } path && File.ReadAllBytes(path) is { Length: > 2 } bytes ? BaseItemTable.FromItemsFile(bytes) : null;

    private PorProgramData? _program;
    private bool _programRead;

    public PorProgramData? Program()
    {
        if (!_programRead)
        {
            _programRead = true;
            _program = PorProgramData.Open(this);
        }
        return _program;
    }

    private PorSound? _sound;
    private bool _soundRead;

    public PorSound? Sound()
    {
        if (!_soundRead)
        {
            _soundRead = true;
            _sound = Find("START.EXE") is { } exe ? PorSound.FromProgram(File.ReadAllBytes(exe)) : null;
        }
        return _sound;
    }

    public IReadOnlyList<DaxPicture>? Picture(string file, int id)
    {
        if (Dax(file + ".DAX")?.Block(id) is not { } b)
            return null;
        try
        {
            return DaxPicture.DecodeFrames(b);
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    public byte[]? ArtData(string file, int id) => Dax(file + ".DAX")?.Block(id);

    public string? SaveFolder => Root;

    /// <summary>The program's rule (GAME.OVR 0x2EC5D).</summary>
    public bool NoWrapOffMap(int script) => script is 0 or 10;
}
