using System.Text.Json.Nodes;
using GoldBox.Core.Explore;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// A new folder under <c>&lt;temp&gt;/goldbox-tests/</c>. Never deleted by the tests (the user's rule: no deletes of
/// computed paths); the temp folder's own cleanup removes it.
/// </summary>
public sealed class TempFolder
{
    public static string Root => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "goldbox-tests");

    public string Path { get; } = System.IO.Path.Combine(Root, $"test-{Guid.NewGuid():N}");

    public TempFolder() => Directory.CreateDirectory(Path);
}

/// <summary>The module format pieces that need no game files: keys, manifest, resolver, codecs (docs/module-format.md).</summary>
public class ModuleFormatTests
{
    private static string NewModule(string parent, string folder, string id, string? version = null, params ModuleDependency[] deps)
    {
        var root = Path.Combine(parent, folder);
        Directory.CreateDirectory(root);
        new ModuleManifest { FormatVersion = "0.1", Id = id, Version = version, DependsOn = [.. deps] }.Save(root);
        return root;
    }

    private static void Put(string root, string rel, string text)
    {
        var p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public void Keys_map_to_the_spec_paths()
    {
        Assert.Equal("scripts/0/24.ecl", ResourceKey.Script(24).RelativePath(".ecl"));
        Assert.Equal("maps/0/31.json", ResourceKey.Map(31).RelativePath(".json"));
        Assert.Equal("areas/24.json", ResourceKey.Area(24).RelativePath(".json"));
        Assert.Equal("records/monsters/2/41.json", ResourceKey.Monster(2, 41).RelativePath(".json"));
        Assert.Equal("records/treasureLists/1/6.json", ResourceKey.TreasureList(1, 6).RelativePath(".json"));
        Assert.Equal("itemList", ResourceKey.KindName(ResourceKind.ItemList));
        Assert.Equal(ResourceKind.EffectList, ResourceKey.ParseKind("effectList"));
        Assert.Throws<FormatException>(() => ResourceKey.ParseKind("EffectList"));
        Assert.Equal("monster/3/7", ResourceKey.Monster(3, 7).ToString());
    }

    [Fact]
    public void Resolver_looks_in_the_module_then_its_dependencies_in_order()
    {
        var t = new TempFolder();
        var baseA = NewModule(t.Path, "a", "base-a", "1.2.0");
        var baseB = NewModule(t.Path, "b", "base-b", "2.0", new ModuleDependency { Id = "base-c" });
        var baseC = NewModule(t.Path, "c", "base-c");
        var top = NewModule(t.Path, "top", "top", "1.0",
            new ModuleDependency { Id = "base-a", Version = ">=1.1" }, new ModuleDependency { Id = "base-b" });
        Put(top, "maps/0/1.json", "{}");
        Put(baseA, "maps/0/1.json", "{}");
        Put(baseA, "maps/0/2.json", "{}");
        Put(baseB, "maps/0/2.json", "{}");
        Put(baseB, "maps/0/3.json", "{}");
        Put(baseC, "maps/0/4.json", "{}");

        var r = ModuleResolver.Open(top);
        Assert.Equal(["top", "base-a", "base-b", "base-c"], r.Chain.Select(m => m.Manifest.Id));
        Assert.Equal("top", r.Find(ResourceKey.Map(1), ".json")!.Value.Module.Manifest.Id);     // own module first
        Assert.Equal("base-a", r.Find(ResourceKey.Map(2), ".json")!.Value.Module.Manifest.Id);  // first dependency next
        Assert.Equal("base-b", r.Find(ResourceKey.Map(3), ".json")!.Value.Module.Manifest.Id);
        Assert.Equal("base-c", r.Find(ResourceKey.Map(4), ".json")!.Value.Module.Manifest.Id);  // a dependency's dependency
        Assert.Null(r.Find(ResourceKey.Map(5), ".json"));
    }

    [Fact]
    public void Resolver_checks_versions_cycles_and_the_format_version()
    {
        var t = new TempFolder();
        NewModule(t.Path, "a", "base-a", "1.0.0");
        var top = NewModule(t.Path, "top", "top", null, new ModuleDependency { Id = "base-a", Version = ">=1.1" });
        Assert.Throws<FileNotFoundException>(() => ModuleResolver.Open(top));

        var x = NewModule(t.Path, "x", "x", null, new ModuleDependency { Id = "y" });
        NewModule(t.Path, "y", "y", null, new ModuleDependency { Id = "x" });
        Assert.Throws<FormatException>(() => ModuleResolver.Open(x));

        var future = Path.Combine(t.Path, "future");
        Directory.CreateDirectory(future);
        File.WriteAllText(Path.Combine(future, "module.json"), """{ "formatVersion": "1.0", "id": "future" }""");
        Assert.Throws<FormatException>(() => new ModuleDirectory(future));

        Assert.True(ModuleResolver.VersionMatches("1.2", ">=1.1.5"));
        Assert.False(ModuleResolver.VersionMatches("1.1", ">=1.1.5"));
        Assert.True(ModuleResolver.VersionMatches("1.3.0", "1.3"));
        Assert.True(ModuleResolver.VersionMatches(null, null));
        Assert.False(ModuleResolver.VersionMatches(null, "1.0"));
    }

    [Fact]
    public void A_GEO_block_rebuilds_from_its_layers_including_barrier_bits_under_open_faces()
    {
        var block = new byte[1026];
        block[1] = 0x04;
        var rng = new Random(5);
        rng.NextBytes(block.AsSpan(2));
        var doc = PorModuleCodec.MapFromGeo(block, 3, 7);
        Assert.Equal(PorModuleCodec.MapLayers, doc.Layers.Keys);
        Assert.Equal(block, PorModuleCodec.GeoBlock(doc));
        // The layers hold the stored values: square (2, 1) is index 18.
        Assert.Equal(block[2 + 18] >> 4, doc.Layers["wallN"][18]);
        Assert.Equal((block[2 + 768 + 18] >> 6) & 3, doc.Layers["barrierW"][18]);
        Assert.Equal(3, doc.Meta!.Disk);
        var map = new AreaMap(PorModuleCodec.MapPlanes(doc), 7);
        Assert.Equal(block[2 + 512 + 18], map.Event(2, 1));
    }

    [Fact]
    public void A_script_block_splits_into_bytecode_and_prefix()
    {
        byte[] block = [0x88, 0x13, 1, 1, 0x14, 0x99, 0];
        var (ecl, side) = PorModuleCodec.SplitScript(block, 2, 20);
        Assert.Equal(block[2..], ecl);
        Assert.Equal(block, PorModuleCodec.ScriptBlock(ecl, side));
        Assert.Equal(2, side.Meta!.Disk);
        Assert.Equal("ECL2.DAX", side.Source!.Container);
    }

    [Fact]
    public void Record_fields_decode_and_encode_back()
    {
        var raw = new byte[63];
        raw[0] = 4;
        "Axe!"u8.CopyTo(raw.AsSpan(1));
        raw[0x2E] = 0x1F;
        raw[0x32] = 0xFE;                                      // plus −2
        raw[0x37] = 0x34; raw[0x38] = 0x12;                    // weight 0x1234
        raw[0x2A] = 0xAA;                                      // stale pointer: raw only
        var f = PorRecordFields.Item.Decode(raw);
        Assert.Equal("Axe!", f["name"]!.GetValue<string>());
        Assert.Equal(-2, f["plus"]!.GetValue<int>());
        Assert.Equal(0x1234, f["weight"]!.GetValue<int>());
        Assert.Equal(0x1F, f["type"]!.GetValue<int>());
        Assert.Equal(raw, PorRecordFields.Item.Encode(f, raw));
        f["plus"] = 3;
        var changed = PorRecordFields.Item.Encode(f, raw);
        Assert.Equal(3, changed[0x32]);
        Assert.Equal(0xAA, changed[0x2A]);

        var c = new byte[0x11D];
        c[0x2D] = 0x28;                                        // THAC0 20 stored as 60 − 20
        c[0xA9] = 0x3E;                                        // AC −2
        var cf = PorRecordFields.Creature.Decode(c);
        Assert.Equal(20, cf["thac0_base"]!.GetValue<int>());
        Assert.Equal(-2, cf["ac_base"]!.GetValue<int>());
        Assert.Equal(0, cf["abilities"]!["str"]!.GetValue<int>());
        Assert.IsType<JsonArray>(cf["spells"]!["known"]);
        Assert.Equal(c, PorRecordFields.Creature.Encode(cf, c));
    }

    [Fact]
    public void Item_types_keep_the_header_and_rebuild_the_file()
    {
        var file = new byte[2 + 128 * 16];
        new Random(9).NextBytes(file);
        var doc = PorModuleCodec.ItemTypes(file);
        Assert.Equal(128, doc.Types.Count);
        Assert.Equal(file[..2], doc.Header);
        Assert.Equal(file, PorModuleCodec.ItemsFile(doc));
    }
}
