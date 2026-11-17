using GoldBox.Core.Ecl;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Por;

/// <summary>The player's Pool of Radiance (DOS) folder. File names are matched case-insensitively.</summary>
public sealed class PorGameDirectory : IEclScriptSource
{
    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<int, DaxFile?> _ecl = [];

    public string Root { get; }

    public PorGameDirectory(string root)
    {
        Root = root;
        _files = Directory.EnumerateFiles(root)
            .GroupBy(f => System.IO.Path.GetFileName(f).ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Full path of a file by name (any case), or null.</summary>
    public string? Find(string name) => _files.TryGetValue(name.ToUpperInvariant(), out var p) ? p : null;

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
}
