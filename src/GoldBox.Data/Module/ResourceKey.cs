using System.Globalization;

namespace GoldBox.Data.Module;

/// <summary>Resource kinds (docs/module-format.md §2). The spec name is what paths and JSON keys use.</summary>
public enum ResourceKind
{
    Script,
    Map,
    Area,
    Wallset,
    Picture,
    Sprite,
    Icon,
    Backdrop,
    CombatGround,
    Tile,
    Monster,
    ItemList,
    /// <summary>PoR's placed treasure (<c>ITEM&lt;d&gt;.DAX</c>): its ids collide with the monster item lists (§16).</summary>
    TreasureList,
    EffectList,
    ItemTemplate,
    Font,
    Sound,
    Music,
    Events,
    TextPool,
}

/// <summary>
/// A resource address (kind, bank, id) (docs/module-format.md §2). The bank is a namespace for ids the original reuses
/// (PoR: the disk digit of <c>MON&lt;d&gt;…</c>, <c>ITEM&lt;d&gt;</c>, <c>WALLDEF&lt;d&gt;</c>); 0 everywhere else.
/// </summary>
public readonly record struct ResourceKey(ResourceKind Kind, int Bank, int Id)
{
    public static ResourceKey Script(int id) => new(ResourceKind.Script, 0, id);
    public static ResourceKey Map(int id) => new(ResourceKind.Map, 0, id);
    public static ResourceKey Area(int id) => new(ResourceKind.Area, 0, id);
    public static ResourceKey Monster(int bank, int id) => new(ResourceKind.Monster, bank, id);
    public static ResourceKey ItemList(int bank, int id) => new(ResourceKind.ItemList, bank, id);
    public static ResourceKey TreasureList(int bank, int id) => new(ResourceKind.TreasureList, bank, id);
    public static ResourceKey EffectList(int bank, int id) => new(ResourceKind.EffectList, bank, id);

    /// <summary>The kind's spec name (<c>script</c>, <c>itemList</c>, …).</summary>
    public static string KindName(ResourceKind kind)
    {
        var s = kind.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    public static ResourceKind ParseKind(string name) =>
        Enum.TryParse<ResourceKind>(name, ignoreCase: true, out var k) && KindName(k) == name
            ? k
            : throw new FormatException($"unknown resource kind '{name}'");

    /// <summary>
    /// The folder of a kind inside a module (§3). Areas have no bank level (<c>areas/&lt;id&gt;.json</c>).
    /// [?] art kinds sit under <c>art/&lt;variant&gt;/…</c> and are not resolved by this method yet.
    /// </summary>
    public static string Folder(ResourceKind kind) => kind switch
    {
        ResourceKind.Script => "scripts",
        ResourceKind.Map => "maps",
        ResourceKind.Area => "areas",
        ResourceKind.Events => "events",
        ResourceKind.Monster => "records/monsters",
        ResourceKind.ItemList => "records/itemLists",
        ResourceKind.TreasureList => "records/treasureLists",
        ResourceKind.EffectList => "records/effectLists",
        _ => throw new NotSupportedException($"no module folder for {KindName(kind)} yet [?]"),
    };

    private static bool Unbanked(ResourceKind kind) => kind is ResourceKind.Area or ResourceKind.Events;

    /// <summary>Relative path of the resource file with extension <paramref name="ext"/> (".json", ".ecl").</summary>
    public string RelativePath(string ext)
    {
        string id = Id.ToString(CultureInfo.InvariantCulture);
        return Unbanked(Kind)
            ? $"{Folder(Kind)}/{id}{ext}"
            : $"{Folder(Kind)}/{Bank.ToString(CultureInfo.InvariantCulture)}/{id}{ext}";
    }

    public override string ToString() => $"{KindName(Kind)}/{Bank}/{Id}";
}
