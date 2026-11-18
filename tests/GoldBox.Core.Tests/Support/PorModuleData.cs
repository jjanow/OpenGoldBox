using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests.Support;

/// <summary>
/// The player's PoR folder (<see cref="PorData"/>) imported once per test run into a new folder under
/// <c>&lt;temp&gt;/goldbox-tests/</c> (game data: never written into the repo). The tests do not delete it (the user's
/// rule: no deletes of computed paths); the temp folder's own cleanup removes it.
/// </summary>
public static class PorModuleData
{
    private static readonly Lazy<(string Dir, PorImportReport Report)> Imported = new(Import);

    public static string Dir => Imported.Value.Dir;
    public static PorImportReport Report => Imported.Value.Report;

    /// <summary>A fresh reader of the imported module (its caches are per instance).</summary>
    public static PorModule Open() => PorModule.Open(Dir);

    private static (string, PorImportReport) Import()
    {
        var dir = Path.Combine(Path.GetTempPath(), "goldbox-tests", $"por-module-{Guid.NewGuid():N}");
        var report = PorImporter.Import(PorData.Dir ?? throw new InvalidOperationException(PorData.SkipReason), dir);
        return (dir, report);
    }
}

/// <summary>An <see cref="IPorContent"/> that forwards to another and counts the calls (proves which source served a run).</summary>
public sealed class CountingContent(IPorContent inner) : IPorContent
{
    public Dictionary<string, int> Calls { get; } = [];

    private T Count<T>(string name, T value)
    {
        Calls[name] = Calls.GetValueOrDefault(name) + 1;
        return value;
    }

    public int this[string name] => Calls.GetValueOrDefault(name);

    public string Origin => inner.Origin;
    public byte[]? LoadBlock(int disk, int script) => Count(nameof(LoadBlock), inner.LoadBlock(disk, script));
    public AreaMap? LoadMap(int disk, int id) => Count(nameof(LoadMap), inner.LoadMap(disk, id));
    public int? ScriptDisk(int script) => Count(nameof(ScriptDisk), inner.ScriptDisk(script));
    public int? MapDisk(int id) => Count(nameof(MapDisk), inner.MapDisk(id));
    public MonsterData? Monster(int disk, int id) => Count(nameof(Monster), inner.Monster(disk, id));
    public IReadOnlyList<byte[]>? TreasureList(int disk, int id) => Count(nameof(TreasureList), inner.TreasureList(disk, id));
    public BaseItemTable? ItemTypes() => Count(nameof(ItemTypes), inner.ItemTypes());
    public PorProgramData? Program() => Count(nameof(Program), inner.Program());
    public IReadOnlyList<GoldBox.Data.Dax.DaxPicture>? Picture(string file, int id) => Count(nameof(Picture), inner.Picture(file, id));
    public byte[]? ArtData(string file, int id) => Count(nameof(ArtData), inner.ArtData(file, id));
    public string? SaveFolder => Count(nameof(SaveFolder), inner.SaveFolder);
    public GoldBox.Core.Ecl.Profiles.EclProfile VmProfile => inner.VmProfile;
    public bool NoWrapOffMap(int script) => Count(nameof(NoWrapOffMap), inner.NoWrapOffMap(script));
    public int? OverlandColumn(int script) => Count(nameof(OverlandColumn), inner.OverlandColumn(script));
    public OverlandMap? Overland() => Count(nameof(Overland), inner.Overland());
}
