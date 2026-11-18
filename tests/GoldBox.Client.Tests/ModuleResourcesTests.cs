using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The Client's screens read the same texts, tables and art from an imported module as from the game folder
/// (docs/module-format.md §10–§13). The screens fall back to our own wording when a text is missing, so a module that
/// lost a string would still run; these comparisons are what catch it. Skipped without <c>GOLDBOX_POR_DIR</c>.
/// </summary>
public class ModuleResourcesTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return d is not null && Directory.Exists(d) ? d : null;
    }

    private static readonly Lazy<string> Module = new(() =>
        PorModuleCache.Ensure(Dir()!, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-module-cache")));

    [Fact]
    public void Texts_and_tables_are_the_same_from_the_module()
    {
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var mod = PorModule.Open(Module.Value);
        Assert.Null(mod.SaveFolder is { } sf && sf.StartsWith(Dir()!, StringComparison.Ordinal) ? sf : null);

        var a = ClientResources.Load(dir);
        var b = ClientResources.Load(mod);
        Assert.Equal(a.CampMenu, b.CampMenu);
        Assert.Equal(a.MagicMenu, b.MagicMenu);
        Assert.Equal(a.AlterMenu, b.AlterMenu);
        Assert.Equal(a.OrderMenus, b.OrderMenus);
        Assert.Equal(a.PagePrompt, b.PagePrompt);
        Assert.Equal(a.TempleNames, b.TempleNames);
        Assert.NotNull(b.CampMenu);
        Assert.Equal(Enumerable.Range(0, 257).Select(a.ItemWord), Enumerable.Range(0, 257).Select(b.ItemWord));
        Assert.Equal(Enumerable.Range(0, 58).Select(a.SpellName), Enumerable.Range(0, 58).Select(b.SpellName));
        Assert.Equal(Enumerable.Range(0, 11).Select(a.LevelName), Enumerable.Range(0, 11).Select(b.LevelName));
        Assert.Equal(Enumerable.Range(0, 256).Select(t => a.IsScroll((byte)t)), Enumerable.Range(0, 256).Select(t => b.IsScroll((byte)t)));
        Assert.Equal(Enumerable.Range(0, 128).Select(t => a.ItemSlot(Item(t))), Enumerable.Range(0, 128).Select(t => b.ItemSlot(Item(t))));
        Assert.NotNull(b.Spells);

        var ga = a.Texts;
        var gb = b.Texts;
        Assert.Equal(Enum.GetValues<Msg>().Length, gb.FromGame);
        Assert.Equal(Enum.GetValues<Msg>().Select(m => ga.Original(m)), Enum.GetValues<Msg>().Select(m => gb.Original(m)));
        Assert.Equal(GoldBox.Core.Game.CampSpells.LabelledEffects.Select(ga.EffectLabel), GoldBox.Core.Game.CampSpells.LabelledEffects.Select(gb.EffectLabel));

        var ca = new CombatText(dir.Program());
        var cb = new CombatText(mod.Program());
        Assert.Equal(Enum.GetValues<CombatMsg>().Length, cb.FromGame);
        Assert.Equal(Enum.GetValues<CombatMsg>().Select(m => ca[m]), Enum.GetValues<CombatMsg>().Select(m => cb[m]));
        Assert.Equal(dir.Program()!.SpellNames, mod.Program()!.SpellNames);
    }

    private static byte[] Item(int type)
    {
        var b = new byte[EclCharacter.ItemRecordSize];
        b[0x2E] = (byte)type;
        return b;
    }

    [Fact]
    public void Combat_art_is_the_same_from_the_module()
    {
        if (Dir() is null)
            return;
        var a = new CombatArt(new PorGameDirectory(Dir()!));
        var b = new CombatArt(PorModule.Open(Module.Value));
        static string Key(CombatPicture? p) => p is null ? "-" : $"{p.Width}x{p.Height}:{Convert.ToHexString(p.Pixels)}";
        Assert.Equal(a.Dungeon.Select(Key), b.Dungeon.Select(Key));
        Assert.Equal(a.Wilderness.Select(Key), b.Wilderness.Select(Key));
        Assert.Equal(a.Rare.Select(Key), b.Rare.Select(Key));
        Assert.NotEmpty(b.Dungeon);
        for (int id = 0; id < 256; id++)
        {
            Assert.Equal(Key(a.Monster(id, false)), Key(b.Monster(id, false)));
            Assert.Equal(Key(a.Monster(id, true)), Key(b.Monster(id, true)));
            Assert.Equal(Key(a.Sprite(id)), Key(b.Sprite(id)));
        }
        // Party icons (CHEAD / CBODY) over every head, body and size.
        var c = new EclCharacter(new byte[EclCharacter.RecordSize]);
        for (int head = 0; head < 64; head += 3)
            for (int size = 1; size <= 2; size++)
            {
                c[CombatArt.IconHead] = (byte)head;
                c[CombatArt.IconBody] = (byte)(63 - head);
                c[CombatArt.IconSize] = (byte)size;
                Assert.Equal(Key(a.Party(c, false)), Key(b.Party(c, false)));
                Assert.Equal(Key(a.Party(c, true)), Key(b.Party(c, true)));
            }
    }

    [Fact]
    public void The_cache_imports_once_per_game_folder()
    {
        if (Dir() is null)
            return;
        var first = Module.Value;
        Assert.Equal(first, PorModuleCache.Ensure(Dir()!, Path.GetDirectoryName(first)!));
        Assert.Equal("por-" + PorModuleCache.Key(Dir()!), Path.GetFileName(first));
    }
}
