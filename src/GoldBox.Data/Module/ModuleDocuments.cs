using System.Text.Json.Nodes;

namespace GoldBox.Data.Module;

// The JSON documents of a module (docs/module-format.md §6–§11). byte[] properties are written as base64.
// Raw bytes are authoritative in format 0.x: the engine builds records from `raw`; `fields` is a decoded, read-only
// view for tools and people (§1.2, §11).

/// <summary>
/// <c>art/amiga/icon-colours.json</c> (docs/module-format.md §10, docs/re/por-art-formats.md §4 "Party icons"): the six
/// bytes the Amiga program's party-icon recolouring reads (hunk 31 +0x210F..+0x2114; byte k: its low nibble is the art
/// index the low nibble of colour byte k paints, its high nibble the index the high nibble paints), read from the
/// player's <c>program</c> at import. Written only when they pair the same art indices as the DOS colour pairs moved to
/// the Amiga pens (<see cref="Por.PorAmigaArt.IconPairsAgree"/>).
/// </summary>
public sealed record AmigaIconColoursDocument
{
    public const string RelativePath = "art/amiga/icon-colours.json";

    public required int[] Pairs { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary>
/// <c>art/amiga/overland-recovery.json</c> (docs/module-format.md §10, docs/re/geo-format.md "The land recovers",
/// Amiga; importer 0.7.42): the Amiga program's land's-recovery tables (hunk 13 +0x2872, recolour hunk 27 +0x7160,
/// generator +0x72C8), read from the player's <c>program</c> at import (<see cref="Por.PorAmigaOverlandRecovery"/>):
/// the two tile lists (bit 7: the high sheet), the one colour map (16 Amiga tile pens), the view squares before and
/// after the frames and one per frame (x, y), and the generator's two nibble tables (16 each).
/// </summary>
public sealed record AmigaOverlandRecoveryDocument
{
    public const string RelativePath = "art/amiga/overland-recovery.json";

    public required int[] ListA { get; init; }
    public required int[] ListB { get; init; }
    public required int[] Map { get; init; }
    public required int[] First { get; init; }
    public required int[] Last { get; init; }
    public required int[][] Pan { get; init; }
    public required int[] GeneratorHigh { get; init; }
    public required int[] GeneratorLow { get; init; }
    public SourceRef? Source { get; init; }

    public static AmigaOverlandRecoveryDocument From(GoldBox.Core.Explore.AmigaOverlandRecoveryTables t) => new()
    {
        ListA = [.. t.ListA.Select(b => (int)b)],
        ListB = [.. t.ListB.Select(b => (int)b)],
        Map = [.. t.Map.Select(b => (int)b)],
        First = [t.First.X, t.First.Y],
        Last = [t.Last.X, t.Last.Y],
        Pan = [.. t.Pan.Select(p => new[] { p.X, p.Y })],
        GeneratorHigh = [.. t.High.Select(b => (int)b)],
        GeneratorLow = [.. t.Low.Select(b => (int)b)],
        Source = new SourceRef { Container = "program" },
    };

    /// <summary>The tables, or null when a value is out of range or the shape is not the routine's.</summary>
    public GoldBox.Core.Explore.AmigaOverlandRecoveryTables? ToTables()
    {
        static bool Bytes(int[] a) => a.All(v => v is >= 0 and <= 255);
        if (!Bytes(ListA) || !Bytes(ListB) || !Bytes(Map) || !Bytes(GeneratorHigh) || !Bytes(GeneratorLow)
            || First is not [var fx, var fy] || Last is not [var lx, var ly] || Pan.Any(p => p.Length != 2))
            return null;
        var t = new GoldBox.Core.Explore.AmigaOverlandRecoveryTables([.. ListA.Select(v => (byte)v)],
            [.. ListB.Select(v => (byte)v)], [.. Map.Select(v => (byte)v)], (fx, fy), (lx, ly),
            [.. Pan.Select(p => (p[0], p[1]))], [.. GeneratorHigh.Select(v => (byte)v)], [.. GeneratorLow.Select(v => (byte)v)]);
        return t.Problem() is null ? t : null;
    }
}

/// <summary>
/// <c>art/amiga/text-pens.json</c> (docs/module-format.md §10, docs/re/por-art-formats.md §4 "Text font"): for each DOS
/// text colour 0–15, the screen pen the Amiga program draws that text in with its text font (<c>art/amiga/poolrad/0/8</c>);
/// the second pass, one pixel to the right, takes the next pen. Pens are registers 16–31 (the font PNG's palette).
/// Read from the player's <c>program</c> at import (<see cref="Por.PorAmigaText"/>).
/// </summary>
public sealed record AmigaTextPensDocument
{
    public const string RelativePath = "art/amiga/text-pens.json";

    public required int[] Pens { get; init; }

    /// <summary>
    /// The highlight of a lit party member or list row: the bar's pen (16–31), then the text's pen and its pass-2 pen
    /// (0–15, the picture half: the font PNG's palette does not hold them). Null in a module from before s39's follow-up.
    /// </summary>
    public int[]? Highlight { get; init; }

    /// <summary>
    /// Row 24's own colours (the Amiga's copper list changes some registers from screen line 192 on): register (0–31),
    /// 12-bit colour, register, colour, … Null in a module from before importer 0.7.29.
    /// </summary>
    public int[]? Row24 { get; init; }

    /// <summary>
    /// Pens single Amiga callers pass where DOS passes another colour (<see cref="Por.PorAmigaText.Calls"/>, s43): the text
    /// window's page prompt, DAMAGE's press-enter lines, the portrait menu's three pens. Null in an older module.
    /// </summary>
    public Dictionary<string, int>? CallPens { get; init; }

    /// <summary>Texts the Amiga words otherwise than DOS (its page prompt, its party menu entries), read from the
    /// player's <c>program</c>; null in an older module.</summary>
    public Dictionary<string, string>? Strings { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary>Where an imported resource came from (container file and block id).</summary>
public sealed record SourceRef
{
    public required string Container { get; init; }
    public int? Block { get; init; }
}

/// <summary>Game-specific facts about a resource that are VM state, not part of its key (PoR: the disk, §2).</summary>
public sealed record ResourceMeta
{
    public int? Disk { get; init; }
}

/// <summary>A resource key written inside a document (<c>{"kind": "itemList", "bank": 2, "id": 41}</c>).</summary>
public sealed record KeyRef
{
    public required string Kind { get; init; }
    public int Bank { get; init; }
    public int Id { get; init; }

    public static KeyRef Of(ResourceKey k) => new() { Kind = ResourceKey.KindName(k.Kind), Bank = k.Bank, Id = k.Id };
    public ResourceKey ToKey() => new(ResourceKey.ParseKind(Kind), Bank, Id);
}

/// <summary><c>scripts/&lt;bank&gt;/&lt;id&gt;.json</c>, the sidecar of the bytecode file (§8).</summary>
public sealed record ScriptSidecar
{
    /// <summary>The container prefix the loader drops (PoR: 88 13 = 0x1388).</summary>
    public byte[] Prefix { get; init; } = [];
    public ResourceMeta? Meta { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record MapRaw
{
    /// <summary>Bytes before the planes (PoR: the size word 00 04).</summary>
    public byte[]? Header { get; init; }
}

/// <summary><c>maps/&lt;bank&gt;/&lt;id&gt;.json</c> (§7).</summary>
public sealed record MapDocument
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary><c>wrap</c>, <c>solid</c> or <c>none</c>.</summary>
    public string Edges { get; init; } = "wrap";
    /// <summary>Named layers, each row-major width × height values.</summary>
    public Dictionary<string, int[]> Layers { get; init; } = [];
    public MapRaw? Raw { get; init; }
    public ResourceMeta? Meta { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary>One fixed-size record: its decoded view and its bytes.</summary>
public sealed record RecordDocument
{
    public JsonObject? Fields { get; init; }
    public required byte[] Raw { get; init; }
}

/// <summary><c>records/itemLists|treasureLists|effectLists/&lt;bank&gt;/&lt;id&gt;.json</c> (§11).</summary>
public sealed record RecordListDocument
{
    public int RecordSize { get; init; }
    public List<RecordDocument> Records { get; init; } = [];
    /// <summary>Bytes after the last whole record (none in PoR; kept so any block round-trips).</summary>
    public byte[]? Trailing { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary><c>records/monsters/&lt;bank&gt;/&lt;id&gt;.json</c> (§11).</summary>
public sealed record MonsterDocument
{
    public JsonObject? Fields { get; init; }
    public required byte[] Raw { get; init; }
    /// <summary>The monster's inventory (an <c>itemList</c> key), or null.</summary>
    public KeyRef? Items { get; init; }
    /// <summary>The monster's built-in effects (an <c>effectList</c> key), or null.</summary>
    public KeyRef? Effects { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record ItemTypeDocument
{
    public int Type { get; init; }
    public JsonObject? Fields { get; init; }
    public required byte[] Raw { get; init; }
}

/// <summary><c>records/itemTypes.json</c> (§11): PoR's <c>ITEMS</c>.</summary>
public sealed record ItemTypesDocument
{
    public const string RelativePath = "records/itemTypes.json";
    public int EntrySize { get; init; }
    /// <summary>File bytes before the table (PoR: 2 bytes the program skips).</summary>
    public byte[]? Header { get; init; }
    public List<ItemTypeDocument> Types { get; init; } = [];
    /// <summary>Bytes after the last whole entry.</summary>
    public byte[]? Trailing { get; init; }
    public SourceRef? Source { get; init; }
}

/// <summary><c>areas/&lt;id&gt;.json</c> (§6).</summary>
public sealed record AreaDocument
{
    public int Id { get; init; }
    public string? Name { get; init; }
    /// <summary>Views the area's script switches between: <c>3d</c>, <c>overland-tiles</c>, <c>overland-picture</c>; empty = none.</summary>
    public List<string> Views { get; init; } = [];
    public KeyRef? Script { get; init; }
    /// <summary>Maps the script is known to load (for tools and checks; the script decides at run time).</summary>
    public List<int> Maps { get; init; } = [];
    public bool NoWrapOffMap { get; init; }
    /// <summary>The overland this area shows in its <c>overland-tiles</c> view, or null.</summary>
    public AreaOverland? Overland { get; init; }
    /// <summary>Notes on how the fields were derived, with [?] where they are guesses.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// An area's place on the module's overland (§6; the table is <see cref="OverlandDocument.RelativePath"/>): the column
/// where the area's x = 0 sits.
/// </summary>
public sealed record AreaOverland
{
    /// <summary>The table column of the area's x = 0 (PoR: DS:3C76[view mode], 0 / 13 / 26 for scripts 25 / 26 / 27).</summary>
    public int Column { get; init; }
}

/// <summary>One frame of a picture (§10): the PNG holds its items stacked top to bottom.</summary>
public sealed record ArtFrame
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Items { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>The animation wrapper's delay ([?] unit), when the block is an animation.</summary>
    public uint? Delay { get; init; }
    /// <summary>The DOS header's CGA colour map (8 bytes), kept for the round trip.</summary>
    public byte[]? Cga { get; init; }
}

/// <summary>
/// <c>art/&lt;variant&gt;/&lt;container&gt;/&lt;bank&gt;/&lt;id&gt;.json</c> (§10): a picture (<see cref="Frames"/>, pixels in
/// <c>&lt;id&gt;.png</c>, or <c>&lt;id&gt;/&lt;frame&gt;.png</c> for an animation) or a data block that is not a picture
/// (<see cref="Raw"/>: the font, WALLDEF wall-set records).
/// </summary>
public sealed record ArtDocument
{
    public bool Animation { get; init; }
    public List<ArtFrame>? Frames { get; init; }
    public byte[]? Raw { get; init; }
    /// <summary>
    /// Amiga art: the source stored this many tiles side by side in one item; the module holds them split into DOS's
    /// item shape (8X8D: two 8×8 wall tiles per 16×8 item; the 24×24 combat tiles two per 48×24 item).
    /// </summary>
    public int? PackedTiles { get; init; }
    /// <summary>Amiga art: which of the program's five 16-colour tables the PNG palette was taken from (§10).</summary>
    public int? PaletteTable { get; init; }
    public SourceRef? Source { get; init; }

    /// <summary>The module-relative path of an art resource without extension (<c>art/dos/cpic/3/12</c>).</summary>
    public static string PathOf(string variant, string container, int bank, int id) =>
        $"art/{variant}/{container.ToLowerInvariant()}/{bank.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A DAX name without extension split into container and bank: a trailing digit is the bank (<c>CPIC3</c> → CPIC, 3).</summary>
    public static (string Container, int Bank) SplitName(string name) =>
        name.Length > 1 && char.IsAsciiDigit(name[^1]) ? (name[..^1], name[^1] - '0') : (name, 0);
}

/// <summary><c>rules/program.json</c> (§12): the program's data segment and the DS offsets of its tables.</summary>
public sealed record ProgramRulesDocument
{
    public const string RelativePath = "rules/program.json";
    public string Profile { get; init; } = "por-1.3";
    /// <summary>The data segment, DS:0000 to the end of START.EXE's load image (raw: the tables are indexed in it).</summary>
    public required byte[] DataSegment { get; init; }
    public required GoldBox.Data.Por.PorProgramOffsets Offsets { get; init; }
    /// <summary>Handler 48's two 16-byte name slots (from GAME.OVR's code segment).</summary>
    public List<byte[]>? GnomeDefenseNames { get; init; }
    public ViewTablesDocument? View { get; init; }
    public SourceRef? Source { get; init; }
}

public sealed record ViewTablesDocument
{
    public required int[] Start { get; init; }
    public required int[] Width { get; init; }
    public required int[] Height { get; init; }
    public required int[] Column { get; init; }
    public required int[] Row { get; init; }
    public required byte[] SkyColours { get; init; }
    public List<string>? ExploreCommands { get; init; }
}

/// <summary><c>text/&lt;lang&gt;.json</c> (§13): program strings by key, the spell names and the page prompt.</summary>
public sealed record TextDocument
{
    public const string RelativePath = "text/en.json";
    public Dictionary<string, string> Strings { get; init; } = [];
    public List<string>? SpellNames { get; init; }
    public string? PagePrompt { get; init; }
}

/// <summary>
/// <c>rules/overland.json</c> (§6, §12): an overland tile map, row-major, values as stored (PoR: 1-based tile numbers,
/// the program draws value − 1), and its special locations in stored order (the rules key them by 1-based index).
/// </summary>
public sealed record OverlandDocument
{
    public const string RelativePath = "rules/overland.json";
    public int Width { get; init; }
    public int Height { get; init; }
    public required int[] Tiles { get; init; }
    public List<OverlandSiteDocument> SpecialLocations { get; init; } = [];
    /// <summary>The land's recovery after Yarash (PoR GAME.OVR 0x20D50; s60), or null when the module has none.</summary>
    public OverlandRecoveryDocument? Recovery { get; init; }
    public OverlandSource? Source { get; init; }
}

/// <summary>
/// <c>rules/overland.json</c> "recovery" (§6): two tile lists (sheet item numbers, bit 7 the high sheet) recoloured by
/// <see cref="From"/> → <see cref="ToA"/> / <see cref="ToB"/> (16 EGA colours each), the view's top-left overland square
/// before the frames and after them, and one top-left square per animated frame (their count is the frame count).
/// </summary>
public sealed record OverlandRecoveryDocument
{
    public required int[] ListA { get; init; }
    public required int[] ListB { get; init; }
    public required int[] From { get; init; }
    public required int[] ToA { get; init; }
    public required int[] ToB { get; init; }
    public required OverlandSquareDocument First { get; init; }
    public required OverlandSquareDocument Last { get; init; }
    public required List<OverlandSquareDocument> Pan { get; init; }
    /// <summary>Where the importer read the tables: START.EXE data-segment offsets (the squares are GAME.OVR code immediates).</summary>
    public OverlandRecoverySource? Source { get; init; }
}

public sealed record OverlandSquareDocument
{
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed record OverlandRecoverySource
{
    public required string Container { get; init; }
    public int ListA { get; init; }
    public int ListB { get; init; }
    public int From { get; init; }
    public int ToA { get; init; }
    public int ToB { get; init; }
    public int PanX { get; init; }
    public int PanY { get; init; }
}

public sealed record OverlandSiteDocument
{
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>The tile drawn while the location's rule holds (1-based, as the table's).</summary>
    public int Tile { get; init; }
}

/// <summary>Where the importer read the overland: the container and the tables' data-segment offsets.</summary>
public sealed record OverlandSource
{
    public required string Container { get; init; }
    public int? Tiles { get; init; }
    public int? SpecialLocations { get; init; }
}
