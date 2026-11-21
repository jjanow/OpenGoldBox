using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// A module opened as Pool of Radiance content names no VM profile (<c>vm</c>), itself or through a dependency. A
/// module without <c>vm</c> runs no ECL (docs/module-format.md §4: event-only modules); PoR's areas are ECL scripts.
/// </summary>
public sealed class ModuleHasNoVmException(string moduleId)
    : Exception($"module \"{moduleId}\" names no VM profile (\"vm\" in module.json, or in a module it depends on): " +
                $"Pool of Radiance content runs ECL scripts and needs one (this engine has: {string.Join(", ", EclProfiles.Names)})")
{
    public string ModuleId { get; } = moduleId;
}

/// <summary>
/// Pool of Radiance content read from an imported module (plus its dependencies, <see cref="ModuleResolver"/>).
/// Lookups bank by disk the kinds the VM profile names (<see cref="EclProfile.BankedKinds"/>; <c>ecl-por-1.3</c>:
/// monsters and placed treasure; a monster's item and effect lists are reached through its document's keys, which
/// carry their bank), and bank 0 the rest (docs/module-format.md §2, §16).
/// <para>
/// For the kinds in the profile's <see cref="EclProfile.DiskCheckedKinds"/> (<c>ecl-por-1.3</c>: scripts and maps), the
/// disk a resource was imported from (<c>meta.disk</c>) is checked against the disk the program would read, and a
/// mismatch answers null: the program opens <c>ECL&lt;disk&gt;</c> / <c>GEO&lt;disk&gt;</c> of the current disk and fails
/// when the block is not there, and the module must fail the same way. A resource without <c>meta.disk</c> (a
/// hand-made module) is found on any disk.
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

    /// <summary>The profile the module's <c>vm</c> names (<see cref="ModuleResolver.Vm"/>: its own, else a dependency's).</summary>
    public EclProfile VmProfile { get; }

    /// <exception cref="UnknownEclProfileException">The manifest names a VM profile the engine does not have.</exception>
    /// <exception cref="ModuleHasNoVmException">No module in the chain names a <c>vm</c>: PoR content is ECL scripts.</exception>
    /// <exception cref="EclProfileNotRunnableException">The profile is not PoR's (SotSB runs in the headless VM only).</exception>
    public PorModule(ModuleResolver modules)
    {
        Modules = modules;
        VmProfile = EclProfiles.ForVm(modules.Vm) ?? throw new ModuleHasNoVmException(modules.Main.Manifest.Id);
        if (VmProfile.Handlers != EclHandlerSet.Por13)
            throw new EclProfileNotRunnableException(VmProfile);
    }

    public static PorModule Open(string moduleRoot, IEnumerable<string>? searchFolders = null) =>
        new(ModuleResolver.Open(moduleRoot, searchFolders));

    /// <summary>
    /// The bank a lookup of <paramref name="kind"/> uses on disk <paramref name="disk"/>: the disk when the VM profile
    /// banks the kind (<see cref="EclProfile.BankedKinds"/>), else 0 (docs/module-format.md §2, §5.1).
    /// </summary>
    private int Bank(ResourceKind kind, int disk) => VmProfile.BankedKinds.Contains(ResourceKey.KindName(kind)) ? disk : 0;

    /// <summary>
    /// True when a resource of <paramref name="kind"/> imported from disk <c>meta.disk</c> may be read on
    /// <paramref name="disk"/>: always, unless the profile checks the kind's disk (<see cref="EclProfile.DiskCheckedKinds"/>);
    /// then a resource without <c>meta.disk</c> (a hand-made module) is found on any disk.
    /// </summary>
    private bool DiskMatches(ResourceKind kind, ResourceMeta? meta, int disk) =>
        !VmProfile.DiskCheckedKinds.Contains(ResourceKey.KindName(kind)) || meta?.Disk is not { } d || d == disk;

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
        Script(script) is { } s && DiskMatches(ResourceKind.Script, s.Sidecar.Meta, disk) ? PorModuleCodec.ScriptBlock(s.Ecl, s.Sidecar) : null;

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
        MapDocument(id) is { } m && DiskMatches(ResourceKind.Map, m.Meta, disk) ? new AreaMap(PorModuleCodec.MapPlanes(m), id) : null;

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
        var key = ResourceKey.Monster(Bank(ResourceKind.Monster, disk), id);
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
        RecordList(ResourceKey.TreasureList(Bank(ResourceKind.TreasureList, disk), id)) is { } l ? PorModuleCodec.Records(l) : null;

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

    // ---- program tables and texts ------------------------------------------------------------------------------------

    private PorProgramData? _program;
    private bool _programRead;

    /// <summary><c>rules/program.json</c> with <c>text/en.json</c>, or null when the module has no program rules.</summary>
    public PorProgramData? Program()
    {
        if (!_programRead)
        {
            _programRead = true;
            if (Modules.File(ProgramRulesDocument.RelativePath) is { } rp)
            {
                var r = ModuleJson.Read<ProgramRulesDocument>(rp);
                var t = Modules.File(TextDocument.RelativePath) is { } tp ? ModuleJson.Read<TextDocument>(tp) : new TextDocument();
                // The decoded rules files (rules/spells.json …), when present, are laid over the raw data segment.
                var (ds, offsets) = PorRulesTables.Load(Modules, r.DataSegment, r.Offsets);
                _program = new PorProgramData
                {
                    Ds = ds,
                    Offsets = offsets,
                    GnomeDefenseNames = r.GnomeDefenseNames,
                    View = r.View is { } v
                        ? new PorViewTables(new ViewLayout(v.Start, v.Width, v.Height, v.Column, v.Row), v.SkyColours, v.ExploreCommands)
                        : null,
                    Texts = t.Strings,
                    SpellNames = t.SpellNames,
                    PagePrompt = t.PagePrompt,
                    Overland = Modules.File(OverlandDocument.RelativePath) is { } op
                        ? PorModuleCodec.Overland(ModuleJson.Read<OverlandDocument>(op)) : null,
                };
            }
        }
        return _program;
    }

    // ---- sound -----------------------------------------------------------------------------------------------------

    private PorSound? _sound;
    private bool _soundRead;

    /// <summary><c>audio/dos/pcsound.json</c> with the cue words from the program's data segment, or null.</summary>
    public PorSound? Sound()
    {
        if (!_soundRead)
        {
            _soundRead = true;
            if (Modules.File(PcSoundDocument.RelativePath) is { } sp)
            {
                var doc = ModuleJson.Read<PcSoundDocument>(sp);
                if (doc.CueTable is { } at && Program() is { } prog && PorSound.CueWordsAt(prog.Ds, at) is { } words)
                    _sound = new PorSound(doc.ToImage(), words);
            }
        }
        return _sound;
    }

    private GoldBox.Core.Sound.AmigaSoundImage? _amigaSound;
    private bool _amigaSoundRead;

    /// <summary><c>audio/amiga/sound.json</c> (the Amiga players' data), or null.</summary>
    public GoldBox.Core.Sound.AmigaSoundImage? AmigaSound()
    {
        if (!_amigaSoundRead)
        {
            _amigaSoundRead = true;
            if (Modules.File(AmigaSoundDocument.RelativePath) is { } p)
                _amigaSound = ModuleJson.Read<AmigaSoundDocument>(p).ToImage();
        }
        return _amigaSound;
    }

    // ---- art -------------------------------------------------------------------------------------------------------

    private byte[]? _iconPairs;
    private bool _iconPairsRead;

    /// <summary><c>art/amiga/icon-colours.json</c>'s six pair bytes, or null (no Amiga overlay, or not six bytes).</summary>
    public byte[]? AmigaIconPairs()
    {
        if (!_iconPairsRead)
        {
            _iconPairsRead = true;
            if (Modules.File(AmigaIconColoursDocument.RelativePath) is { } p
                && ModuleJson.Read<AmigaIconColoursDocument>(p).Pairs is { Length: 6 } pairs && pairs.All(b => b is >= 0 and <= 255))
                _iconPairs = [.. pairs.Select(b => (byte)b)];
        }
        return _iconPairs;
    }

    private int[]? _textPens, _textHighlight, _row24;

    /// <summary><c>art/amiga/text-pens.json</c>'s row-24 colours (register, 12-bit colour pairs), or null.</summary>
    public int[]? AmigaRow24()
    {
        AmigaTextPens();
        return _row24;
    }
    private bool _textPensRead;

    /// <summary><c>art/amiga/text-pens.json</c>'s highlight (bar, text and pass-2 pen), or null.</summary>
    public int[]? AmigaTextHighlight()
    {
        AmigaTextPens();
        return _textHighlight;
    }

    /// <summary><c>art/amiga/text-pens.json</c>'s 16 pens (registers 16–30, even), or null (no Amiga text font).</summary>
    public int[]? AmigaTextPens()
    {
        if (!_textPensRead)
        {
            _textPensRead = true;
            if (Modules.File(AmigaTextPensDocument.RelativePath) is { } p
                && ModuleJson.Read<AmigaTextPensDocument>(p) is { Pens: { Length: 16 } pens } doc && pens.All(v => v is >= 16 and < 31))
            {
                _textPens = pens;
                if (doc.Highlight is [>= 16 and < 32, >= 0 and < 16, >= 0 and < 16] lit)
                    _textHighlight = lit;
                if (doc.Row24 is { Length: > 0 } r && r.Length % 2 == 0
                    && r.Select((v, i) => i % 2 == 0 ? v is >= 0 and < 32 : v is >= 0 and <= 0xFFF).All(b => b))
                    _row24 = r;
            }
        }
        return _textPens;
    }

    private readonly Dictionary<(string, int), (ArtDocument Doc, string Path)?> _art = [];

    private (ArtDocument Doc, string Path)? Art(string file, int id)
    {
        file = file.ToUpperInvariant();
        if (!_art.TryGetValue((file, id), out var a))
        {
            var (container, bank) = ArtDocument.SplitName(file);
            // Picture/ArtData are the DOS art (parity); the Amiga overlay is reached through Art(file, id, choice).
            a = Modules.File(ArtDocument.PathOf("dos", container, bank, id) + ".json") is { } p
                ? (ModuleJson.Read<ArtDocument>(p), p) : null;
            _art[(file, id)] = a;
        }
        return a;
    }

    public IReadOnlyList<DaxPicture>? Picture(string file, int id) =>
        Art(file, id) is { } a ? PorArtCodec.ReadFrames(a.Doc, a.Path) : null;

    public byte[]? ArtData(string file, int id) => Art(file, id) is { } a ? PorArtCodec.Block(a.Doc, a.Path) : null;

    private readonly Dictionary<(string, int), PorArtImage?> _amiga = [];

    /// <summary>
    /// The asset in the variant <paramref name="choice"/> picks (Best: <c>module.json</c> <c>art.defaults</c> per
    /// category), falling back to DOS. Amiga art has no disk digit: bank 0 serves every DOS bank (§10).
    /// </summary>
    public PorArtImage? Art(string file, int id, PorArtChoice choice)
    {
        file = file.ToUpperInvariant();
        var (container, _) = ArtDocument.SplitName(file);
        bool amiga = choice switch
        {
            PorArtChoice.Amiga => true,
            PorArtChoice.Best => PorAmigaArt.Category(container) is { } c
                && Modules.Main.Manifest.Art?.Defaults.GetValueOrDefault(c) == PorAmigaArt.Variant,
            _ => false,
        };
        if (amiga && AmigaArt(container, id) is { } a)
            return a;
        return Picture(file, id) is { } f ? new PorArtImage("dos", f, IndexedPng.EgaPalette) : null;
    }

    private PorArtImage? AmigaArt(string container, int id)
    {
        if (!_amiga.TryGetValue((container, id), out var img))
        {
            img = null;
            if (Modules.File(ArtDocument.PathOf(PorAmigaArt.Variant, container, 0, id) + ".json") is { } p
                && ModuleJson.Read<ArtDocument>(p) is { Frames: { Count: > 0 } } doc
                && PorArtCodec.ReadFrames(doc, p) is { } frames)
            {
                var rel = p[..^".json".Length];
                int first = Enumerable.Range(0, doc.Frames.Count).FirstOrDefault(i => doc.Frames[i].Items > 0);
                var png = PorArtCodec.FramePath(rel, doc.Animation, first);
                var palette = File.Exists(png) ? IndexedPng.Palette(File.ReadAllBytes(png)) : null;
                img = new PorArtImage(PorAmigaArt.Variant, frames, palette ?? IndexedPng.EgaPalette);
            }
            _amiga[(container, id)] = img;
        }
        return img;
    }

    // ---- areas -----------------------------------------------------------------------------------------------------

    private readonly Dictionary<int, AreaDocument?> _areas = [];

    public AreaDocument? AreaDocument(int id)
    {
        if (!_areas.TryGetValue(id, out var a))
        {
            a = Modules.Find(ResourceKey.Area(id), ".json") is { } f ? ModuleJson.Read<AreaDocument>(f.Path) : null;
            _areas[id] = a;
        }
        return a;
    }

    /// <summary>The area's <c>overland.column</c> (null for a script with no area document or no overland).</summary>
    public int? OverlandColumn(int script) => AreaDocument(script)?.Overland?.Column;

    /// <summary>The area's <c>noWrapOffMap</c> (false for a script with no area document).</summary>
    public bool NoWrapOffMap(int script) => AreaDocument(script)?.NoWrapOffMap ?? false;

    // ---- saves -----------------------------------------------------------------------------------------------------

    /// <summary>The module's <c>saves/</c> folder (the shipped saves as engine saves <c>x.json</c>, §15), or null.</summary>
    public string? SaveFolder =>
        Path.Combine(Modules.Main.Root, "saves") is var d && Directory.Exists(d) ? d : null;
}
