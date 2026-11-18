using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The VM profile and its registry (docs/module-format.md §5.1).</summary>
public class EclProfileTests
{
    [Fact]
    public void Registry_finds_the_por_profile_by_its_manifest_name()
    {
        Assert.Same(EclProfiles.Por13, EclProfiles.Get("ecl-por-1.3"));
        Assert.Same(EclProfiles.Por13, EclProfiles.Default);
        Assert.Same(EclProfiles.Por13, EclProfiles.ForVm("ecl-por-1.3"));
        Assert.Null(EclProfiles.ForVm(null)); // no vm = no ECL VM (module-format.md §4)
        Assert.True(EclProfiles.TryGet("ecl-por-1.3", out var p));
        Assert.Same(EclProfiles.Por13, p);
        Assert.Equal(["ecl-por-1.3"], EclProfiles.Names);
    }

    [Theory]
    [InlineData("ecl-pod")]
    [InlineData("ECL-POR-1.3")] // names are exact
    [InlineData("")]
    public void Unknown_profile_names_fail_clearly(string name)
    {
        Assert.False(EclProfiles.TryGet(name, out _));
        var e = Assert.Throws<UnknownEclProfileException>(() => EclProfiles.Get(name));
        Assert.Equal(name, e.Name);
        Assert.Contains($"\"{name}\"", e.Message);
        Assert.Contains("ecl-por-1.3", e.Message);
    }

    [Fact]
    public void Por_profile_holds_the_constants_the_vm_was_written_with()
    {
        var p = EclProfiles.Por13;
        Assert.Equal(0x9900, p.ScriptBase);
        Assert.Equal(EclLayout.Base, p.ScriptBase);
        Assert.Equal(2, p.ScriptPrefix);
        Assert.Equal(EclLayout.Prefix, p.ScriptPrefix);
        Assert.Equal(7680, p.ScriptAreaSize);
        Assert.Equal(EclLayout.AreaSize, p.ScriptAreaSize);
        Assert.Equal(EclLayout.End, p.ScriptEnd);
        Assert.Equal(EclLayout.VectorCount, p.VectorCount);
        Assert.Equal(EclLayout.VectorInit, p.InitVector);

        Assert.Equal(["4900", "6B00", "9700", "script"], p.Regions.Select(r => r.Name));
        foreach (var r in p.Regions)
        {
            Assert.Equal(r.Name, EclMemory.RegionOf(r.Start));
            Assert.Equal(r.Name, EclMemory.RegionOf(r.End));
            Assert.NotEqual(r.Name, EclMemory.RegionOf((ushort)(r.Start - 1)));
            Assert.NotEqual(r.Name, EclMemory.RegionOf((ushort)(r.End + 1)));
            Assert.True(r.Saved);
        }
        var m = new EclMemory();
        Assert.Equal(m.Cells4900.Length, p.Regions[0].Cells);
        Assert.Equal(m.Cells6B00.Length, p.Regions[1].Cells);
        Assert.Equal(m.Cells9700.Length, p.Regions[2].Cells);
        Assert.Equal(m.ScriptArea.Length, p.Regions[3].Cells);
        Assert.Equal([16, 16, 16, 8], p.Regions.Select(r => r.CellBits));

        Assert.Equal(EclOperandWidthRule.FromAddressRegion, p.OperandWidth);
        Assert.Equal(EclUnmappedRead.StackResidue, p.UnmappedRead);
        Assert.Equal(EclReturnOnEmptyStack.FallsThrough, p.ReturnOnEmptyStack);
        Assert.Equal(EclCharacterView.Por6B00Redirection, p.CharacterView);
        Assert.Equal(EclHandlerSet.Por13, p.Handlers);
        Assert.True(p.ScriptBytesSaved);
        Assert.Equal((ushort)0x6E12, p.DiskCell);
        Assert.Equal([9], p.ProgramIdsThatExit.Select(k => (int)k));
        Assert.Equal([0, 8, 9], p.ProgramIds.Keys.Order().Select(k => (int)k));
        Assert.Equal([0x2C90, 0x8000, 0x8001, 0xBA03, 0xC018, 0xC01E], p.CallIds.Keys.Order().Select(k => (int)k));
    }

    [Fact]
    public void Por_opcode_table_is_the_vms_with_its_six_mis_skips()
    {
        var t = EclProfiles.Por13.Opcodes;
        Assert.Same(EclOpcodes.Por13, t);
        for (int i = 0; i < 256; i++)
        {
            byte op = (byte)i;
            Assert.Same(EclOpcodes.Get(op), t.Get(op));
            Assert.Equal(EclOpcodes.SkipCount(op), t.SkipCount(op));
            Assert.Equal(EclOpcodes.Name(op), t.Name(op));
            Assert.Equal(i <= 0x3D && i != 0x1F, t.IsHandled(op));
        }
        Assert.Equal(2, t.SkipCount(EclOpcodes.Undefined1F));
        Assert.Equal("UNDEF1F", t.Name(EclOpcodes.Undefined1F));
        Assert.Equal([0x0C, 0x15, 0x25, 0x26, 0x2B, 0x36], t.MisSkips.Select(b => (int)b));
        // Variadic count position: VMENU's 3rd fixed operand, ON GOTO / ON GOSUB / HMENU's 2nd.
        Assert.Equal(2, t.Get(EclOpcodes.VMenu)!.CountOperandIndex);
        Assert.Equal(1, t.Get(EclOpcodes.OnGoto)!.CountOperandIndex);
        Assert.Equal(1, t.Get(EclOpcodes.OnGosub)!.CountOperandIndex);
        Assert.Equal(1, t.Get(EclOpcodes.HMenu)!.CountOperandIndex);
    }

    [Fact]
    public void Banked_kinds_are_spec_resource_kinds()
    {
        var p = EclProfiles.Por13;
        Assert.Equal(["effectList", "itemList", "monster", "treasureList", "wallset"], p.BankedKinds.Order(StringComparer.Ordinal));
        Assert.Equal(["map", "script"], p.DiskCheckedKinds.Order(StringComparer.Ordinal));
        foreach (var k in p.BankedKinds.Concat(p.DiskCheckedKinds))
            Assert.Equal(k, ResourceKey.KindName(ResourceKey.ParseKind(k)));
    }

    [Fact]
    public void Vm_and_memory_share_one_profile()
    {
        var m = new EclMemory();
        Assert.Same(EclProfiles.Por13, m.Profile);
        var vm = new EclVm(m, new ScriptedEclHost(), EclProfiles.Por13);
        Assert.Same(m.Profile, vm.Profile);
        Assert.Equal(EclProfiles.Por13.VectorCount, vm.Vectors.Length);
        Assert.Same(m.Profile, new EclVm(m, new ScriptedEclHost()).Profile);
    }

    [Fact]
    public void A_folder_is_por_and_a_module_takes_its_manifest_vm()
    {
        var tmp = new TempFolder();
        Assert.Same(EclProfiles.Default, ((IPorContent)new PorGameDirectory(tmp.Path)).VmProfile);

        string Module(string folder, string? vm)
        {
            var root = Path.Combine(tmp.Path, folder);
            Directory.CreateDirectory(root);
            new ModuleManifest { FormatVersion = "0.1", Id = folder, Vm = vm }.Save(root);
            return root;
        }

        Assert.Same(EclProfiles.Por13, PorModule.Open(Module("named", "ecl-por-1.3")).VmProfile);
        var none = Assert.Throws<ModuleHasNoVmException>(() => PorModule.Open(Module("none", null)));
        Assert.Equal("none", none.ModuleId);
        Assert.Contains("ecl-por-1.3", none.Message);
        var e = Assert.Throws<UnknownEclProfileException>(() => PorModule.Open(Module("other", "ecl-dqk")));
        Assert.Equal("ecl-dqk", e.Name);
    }

    [Fact]
    public void A_module_without_vm_takes_the_first_vm_of_its_dependencies()
    {
        var tmp = new TempFolder();
        string Module(string folder, string? vm, params string[] deps)
        {
            var root = Path.Combine(tmp.Path, folder);
            Directory.CreateDirectory(root);
            new ModuleManifest
            {
                FormatVersion = "0.1", Id = folder, Vm = vm,
                DependsOn = [.. deps.Select(d => new ModuleDependency { Id = d })],
            }.Save(root);
            return root;
        }

        Module("game", "ecl-por-1.3");
        Module("events", null);
        // An add-on on a game runs under the game's VM; its own vm wins over a dependency's.
        Assert.Equal("ecl-por-1.3", ModuleResolver.Open(Module("addon", null, "events", "game")).Vm);
        Assert.Same(EclProfiles.Por13, PorModule.Open(Path.Combine(tmp.Path, "addon")).VmProfile);
        Assert.Equal("ecl-dqk", ModuleResolver.Open(Module("own", "ecl-dqk", "game")).Vm);
        // No module in the chain names one: no VM, and PoR content refuses it.
        Assert.Null(ModuleResolver.Open(Module("design", null, "events")).Vm);
        Assert.Throws<ModuleHasNoVmException>(() => PorModule.Open(Path.Combine(tmp.Path, "design")));
    }
}
