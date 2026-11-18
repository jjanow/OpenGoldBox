using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// Pool of Radiance content read from an imported module (plus its dependencies, <see cref="ModuleResolver"/>).
/// Scripts and maps are bank 0 (ids unique over the disks); monsters, their item and effect lists and placed
/// treasure are banked by disk (docs/module-format.md §2, §16).
/// <para>
/// The <c>ecl-por-1.3</c> profile checks the disk a script or map was imported from (<c>meta.disk</c>) against the
/// disk the program would read, and answers null on a mismatch: the program opens <c>ECL&lt;disk&gt;</c> /
/// <c>GEO&lt;disk&gt;</c> of the current disk and fails when the block is not there, and the module must fail the same
/// way. A resource without <c>meta.disk</c> (a hand-made module) is found on any disk.
/// </para>
/// </summary>
public sealed class PorModule : IPorContent
{
    public ModuleResolver Modules { get; }

    public string Origin => Modules.Main.Root;

    private readonly Dictionary<int, (byte[] Ecl, ScriptSidecar Sidecar)?> _scripts = [];
    private readonly Dictionary<int, MapDocument?> _maps = [];
    private readonly Dictionary<ResourceKey, RecordListDocument?> _lists = [];
    private readonly Dictionary<ResourceKey, MonsterDocument?> _monsters = [];
    private byte[]? _itemsFile;
    private bool _itemTypesRead;

    public PorModule(ModuleResolver modules) => Modules = modules;

    public static PorModule Open(string moduleRoot, IEnumerable<string>? searchFolders = null) =>
        new(ModuleResolver.Open(moduleRoot, searchFolders));

    private static bool DiskMatches(ResourceMeta? meta, int disk) => meta?.Disk is not { } d || d == disk;

    // ---- scripts ---------------------------------------------------------------------------------------------------

    private (byte[] Ecl, ScriptSidecar Sidecar)? Script(int id)
    {
        if (!_scripts.TryGetValue(id, out var s))
        {
            var key = ResourceKey.Script(id);
            if (Modules.Find(key, ".ecl") is { } ecl)
            {
                // The sidecar comes from the same module as the bytecode.
                var side = ecl.Module.Find(key, ".json") is { } sp ? ModuleJson.Read<ScriptSidecar>(sp) : new ScriptSidecar();
                s = (File.ReadAllBytes(ecl.Path), side);
            }
            _scripts[id] = s;
        }
        return s;
    }

    /// <summary>The script as the DAX block the VM loads (prefix + bytecode), or null (none, or not on this disk).</summary>
    public byte[]? LoadBlock(int disk, int script) =>
        Script(script) is { } s && DiskMatches(s.Sidecar.Meta, disk) ? PorModuleCodec.ScriptBlock(s.Ecl, s.Sidecar) : null;

    public int? ScriptDisk(int script) => Script(script) is { } s ? s.Sidecar.Meta?.Disk ?? 0 : null;

    // ---- maps ------------------------------------------------------------------------------------------------------

    public MapDocument? MapDocument(int id)
    {
        if (!_maps.TryGetValue(id, out var m))
        {
            m = Modules.Find(ResourceKey.Map(id), ".json") is { } f ? ModuleJson.Read<MapDocument>(f.Path) : null;
            _maps[id] = m;
        }
        return m;
    }

    public AreaMap? LoadMap(int disk, int id) =>
        MapDocument(id) is { } m && DiskMatches(m.Meta, disk) ? new AreaMap(PorModuleCodec.MapPlanes(m), id) : null;

    public int? MapDisk(int id) => MapDocument(id) is { } m ? m.Meta?.Disk ?? 0 : null;

    // ---- records ---------------------------------------------------------------------------------------------------

    public RecordListDocument? RecordList(ResourceKey key)
    {
        if (!_lists.TryGetValue(key, out var d))
        {
            d = Modules.Find(key, ".json") is { } f ? ModuleJson.Read<RecordListDocument>(f.Path) : null;
            _lists[key] = d;
        }
        return d;
    }

    public MonsterDocument? MonsterDocument(int disk, int id)
    {
        var key = ResourceKey.Monster(disk, id);
        if (!_monsters.TryGetValue(key, out var d))
        {
            d = Modules.Find(key, ".json") is { } f ? ModuleJson.Read<MonsterDocument>(f.Path) : null;
            _monsters[key] = d;
        }
        return d;
    }

    public MonsterData? Monster(int disk, int id)
    {
        if (MonsterDocument(disk, id) is not { } m)
            return null;
        List<byte[]> List(KeyRef? k) => k is not null && RecordList(k.ToKey()) is { } l ? PorModuleCodec.Records(l) : [];
        return new MonsterData([.. m.Raw], List(m.Items), List(m.Effects));
    }

    public IReadOnlyList<byte[]>? TreasureList(int disk, int id) =>
        RecordList(ResourceKey.TreasureList(disk, id)) is { } l ? PorModuleCodec.Records(l) : null;

    public ItemTypesDocument? ItemTypesDocument() =>
        Modules.File(Module.ItemTypesDocument.RelativePath) is { } p ? ModuleJson.Read<ItemTypesDocument>(p) : null;

    /// <summary>A fresh table on each call (the table is mutable), as the folder source reads the file each time.</summary>
    public BaseItemTable? ItemTypes()
    {
        if (!_itemTypesRead)
        {
            _itemTypesRead = true;
            if (ItemTypesDocument() is { } d)
                _itemsFile = PorModuleCodec.ItemsFile(d);
        }
        return _itemsFile is { Length: > 2 } f ? BaseItemTable.FromItemsFile(f) : null;
    }
}
