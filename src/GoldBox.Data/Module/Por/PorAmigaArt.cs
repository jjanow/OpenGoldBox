using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>What the Amiga overlay import wrote.</summary>
public sealed class PorAmigaReport
{
    public int Pictures { get; set; }
    /// <summary>The title painting was imported (disk A's <c>intro</c> was found; it is optional).</summary>
    public bool Title { get; set; }
    /// <summary>Blocks left out on purpose (placeholders, incomplete sprites), as "file id".</summary>
    public List<string> Skipped { get; } = [];
    /// <summary>Blocks that are not pictures in a picture file, as "file id" (kept out of the overlay).</summary>
    public List<string> NotPictures { get; } = [];
    public List<ModuleInput> Inputs { get; } = [];
}

/// <summary>
/// The Amiga art overlay of a PoR module (docs/module-format.md §10, PLAN §4.3): the Ubi Soft Amiga v1.0 data files
/// (<c>pic.dax</c>, <c>head.dax</c>, … on disk B) become <c>art/amiga/&lt;container&gt;/0/&lt;id&gt;</c>, with the
/// palette each file is drawn with taken from the player's <c>program</c> (hunk #23, five 16-colour tables;
/// docs/re/por-art-formats.md §4). Amiga files have no disk digit, so the overlay uses bank 0 and serves every DOS bank
/// of the same container. Items stored as two tiles side by side are split to DOS's item shape; other shapes stay as
/// the Amiga stores them. Only art is read: the Amiga's scripts, maps, records and WALLDEF are not used (DOS is the
/// rules reference).
/// </summary>
public static class PorAmigaArt
{
    public const string Variant = "amiga";

    /// <summary>The hunk of <c>program</c> whose data starts with the five palette tables (docs/re/por-art-formats.md §4).</summary>
    public const int PaletteHunk = 23;
    public const int PaletteTables = 5;

    /// <summary>Tiles per item in the packed files (docs/re/por-art-formats.md §3: 16×8 and 48×24 items).</summary>
    private const int PackedPerItem = 2;

    /// <summary>
    /// The picture files of the overlay: which palette table the program sets before it draws them, the width of the
    /// tiles packed side by side in one item (0 = none), and the art category <c>module.json</c>'s
    /// <c>art.defaults</c> uses. Tables from docs/re/por-art-formats.md §4: PIC → 4 (FINAL block 2 → 4, the other
    /// FINAL blocks → 3); SPRIT, the 3D view and combat module → 3. [?] HEAD/BODY/ICON → 2 (only table 2 gives skin
    /// tones; no call site found); CPIC/CHEAD/CBODY/COMSPR and the combat grounds → 3 like the rest of combat.
    /// </summary>
    private static readonly Dictionary<string, (int Table, int TileWidth, string Category)> Files = new(StringComparer.Ordinal)
    {
        ["pic"] = (4, 0, "picture"),
        ["final"] = (3, 0, "picture"),
        ["head"] = (2, 0, "portrait"),
        ["body"] = (2, 0, "portrait"),
        ["sprit"] = (3, 0, "sprite"),
        ["8x8d"] = (3, 8, "wall"),
        ["cpic"] = (3, 0, "icon"),
        ["chead"] = (3, 0, "icon"),
        ["cbody"] = (3, 0, "icon"),
        ["comspr"] = (3, 0, "icon"),
        ["icon"] = (2, 0, "icon"),
        ["sqrpaci"] = (3, 24, "combatGround"),
        ["bacpac"] = (3, 24, "combatGround"),
        ["dungcom"] = (3, 24, "combatGround"),
        ["randcom"] = (3, 24, "combatGround"),
        ["wildcom"] = (3, 24, "combatGround"),
    };

    /// <summary>
    /// Amiga blocks left out, so the DOS asset is used (docs/re/por-art-formats.md §6; PLAN: DOS fills what the Amiga
    /// lacks). SPRIT 1 and 9 are placeholders (a line of text naming the block, no figure). SPRIT 120 has only its near frame drawn (the
    /// far frames are 8×1 stubs) while DOS has all three; a sprite is drawn with one palette, so DOS serves all of it.
    /// SPRIT 10, 67, 74, 113, 116 and 119 also have stub far frames, but their DOS blocks are blank, so the Amiga's
    /// near frame stays.
    /// </summary>
    private static readonly HashSet<(string, int)> LeftOut = [("sprit", 1), ("sprit", 9), ("sprit", 120)];

    /// <summary>
    /// The title painting from disk A's <c>intro</c> (docs/re/por-art-formats.md §5) is written as
    /// <c>art/amiga/intro/0/1</c>, category <c>title</c>. DOS has no such asset (its TITLE.DAX screens are other pictures).
    /// </summary>
    /// <summary>
    /// The 3D view's backdrop under the Amiga walls (read s19 in the Amiga <c>program</c>, hunks numbered from 0 in file
    /// order): DrawView (hunk 25 +0x292E) fills the same four bands as DOS through hunk 25 +0x2438 with pens of the
    /// palette the walls use (table 3, set at hunk 25 +0x358E). The sky pen comes from a 16-byte list at hunk 8 +0x35F
    /// indexed by $49FD / $49FE as on DOS; horizon and ground from the same $C05F / $C059 rule (hunk 7 +0x38A2,
    /// +0x38E2, +0x3922). Each Amiga value is the DOS colour of the same entry moved to a table-3 pen; this gives that
    /// move for the twelve colours the DOS rule can produce (sky list 0, 15, 12, 11, 13, 10, 9, 14; horizon 0, 15, 8,
    /// 7; ground 8, 1, 6). Null for any other colour.
    /// </summary>
    public static byte? BackdropPen(byte dosColour) => dosColour switch
    {
        0 => 4, 15 => 15, 12 => 6, 11 => 11, 13 => 7, 10 => 10, 9 => 9, 14 => 8, 8 => 12, 7 => 14, 6 => 5, 1 => 1,
        _ => null,
    };

    public const string IntroContainer = "intro";
    public const int TitleId = 1;
    public const string TitleCategory = "title";

    /// <summary>The art category of a container (DAX name without disk digit, any case), or null when it has none.</summary>
    public static string? Category(string container) =>
        container.Equals(IntroContainer, StringComparison.OrdinalIgnoreCase) ? TitleCategory
        : Files.TryGetValue(container.ToLowerInvariant(), out var f) ? f.Category : null;

    /// <summary>The categories and the variant the "Best" mix uses for each (PLAN §4.3: Amiga, decided s5; DOS fills the gaps).</summary>
    public static Dictionary<string, string> BestDefaults() =>
        Files.Values.Select(f => f.Category).Append(TitleCategory).Distinct().Order(StringComparer.Ordinal)
            .ToDictionary(c => c, _ => Variant);

    /// <summary>The palette table the program sets for block <paramref name="id"/> of <paramref name="container"/>.</summary>
    public static int TableFor(string container, int id) =>
        container == "final" && id == 2 ? 4 : Files[container].Table;

    /// <summary>
    /// Finds the Amiga files under <paramref name="amigaDir"/> (the unpacked disks, any case, up to three folders
    /// deep): the <c>program</c> executable and the folder holding <c>pic.dax</c>.
    /// </summary>
    public static (string Program, string DataDir) Locate(string amigaDir)
    {
        var all = Directory.EnumerateFiles(amigaDir, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive,
        }).ToList();
        var program = all.Where(p => Path.GetFileName(p).Equals("program", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new FileNotFoundException($"{amigaDir}: no Amiga 'program' file (disk A of Pool of Radiance)");
        var pic = all.Where(p => Path.GetFileName(p).Equals("pic.dax", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new FileNotFoundException($"{amigaDir}: no Amiga 'pic.dax' (disk B of Pool of Radiance)");
        return (program, Path.GetDirectoryName(pic)!);
    }

    /// <summary>
    /// Disk A's <c>intro</c> under <paramref name="amigaDir"/> (any case, up to three folders deep), or null: it is
    /// optional (without it the overlay has no title painting).
    /// </summary>
    public static string? LocateIntro(string amigaDir) =>
        Directory.EnumerateFiles(amigaDir, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive,
        }).Where(p => Path.GetFileName(p).Equals("intro", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// The files the overlay import reads (<c>program</c>, the overlay's <c>.dax</c> files, <c>intro</c> when present),
    /// sorted: what a cache of an imported module must be keyed on.
    /// </summary>
    public static IReadOnlyList<string> InputFiles(string amigaDir)
    {
        var (program, dataDir) = Locate(amigaDir);
        var files = new List<string> { program };
        files.AddRange(DataFiles(dataDir).Values);
        if (LocateIntro(amigaDir) is { } intro)
            files.Add(intro);
        return [.. files.Distinct().Order(StringComparer.Ordinal)];
    }

    /// <summary>The program's five palette tables, each 16 colours as RGB triplets.</summary>
    public static byte[][] Palettes(byte[] program)
    {
        var hunks = AmigaHunks.Parse(program);
        if (hunks.Count <= PaletteHunk || hunks[PaletteHunk] is not { Type: AmigaHunks.Data, DataOffset: { } at } h
            || h.Size < PaletteTables * 32)
            throw new DaxFormatException($"'program' has no DATA hunk #{PaletteHunk} with the palette tables: not Amiga Pool of Radiance v1.0");
        return [.. Enumerable.Range(0, PaletteTables).Select(k => AmigaHunks.Rgb12To24(program.AsSpan(at + 32 * k), 16))];
    }

    /// <summary>The data files of the overlay present in <paramref name="dataDir"/>: container → path.</summary>
    public static SortedDictionary<string, string> DataFiles(string dataDir)
    {
        var o = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Directory.EnumerateFiles(dataDir))
        {
            var n = Path.GetFileName(p).ToLowerInvariant();
            if (n.EndsWith(".dax", StringComparison.Ordinal) && Files.ContainsKey(n[..^4]))
                o[n[..^4]] = p;
        }
        return o;
    }

    /// <summary>Writes the overlay into <paramref name="moduleRoot"/> (an imported DOS module).</summary>
    public static PorAmigaReport Import(string amigaDir, string moduleRoot)
    {
        var (programPath, dataDir) = Locate(amigaDir);
        var programBytes = File.ReadAllBytes(programPath);
        var palettes = Palettes(programBytes);
        var report = new PorAmigaReport();
        report.Inputs.Add(new ModuleInput { File = "amiga/program", Sha256 = Sha256(programBytes) });
        foreach (var (container, path) in DataFiles(dataDir))
        {
            var bytes = File.ReadAllBytes(path);
            report.Inputs.Add(new ModuleInput { File = "amiga/" + container + ".dax", Sha256 = Sha256(bytes) });
            var dax = new AmigaDaxFile(bytes, path);
            foreach (var e in dax.Entries)
            {
                if (LeftOut.Contains((container, e.Id)))
                {
                    report.Skipped.Add($"{container} {e.Id}");
                    continue;
                }
                var block = dax.Decode(e);
                if (Frames(block, container) is not { } pic)
                {
                    report.NotPictures.Add($"{container} {e.Id}");
                    continue;
                }
                Write(moduleRoot, container, e.Id, pic.Frames, pic.Animation, palettes[TableFor(container, e.Id)],
                    TableFor(container, e.Id), Files[container].TileWidth);
                report.Pictures++;
            }
        }
        if (LocateIntro(amigaDir) is { } introPath)
        {
            var intro = File.ReadAllBytes(introPath);
            report.Inputs.Add(new ModuleInput { File = "amiga/" + IntroContainer, Sha256 = Sha256(intro) });
            var (pixels, palette) = AmigaIntro.Title(intro);
            Write(moduleRoot, IntroContainer, TitleId,
                [DaxPicture.Create(AmigaIntro.Width, AmigaIntro.Height, 0, 0, [pixels])], false, palette, null, 0);
            report.Title = true;
        }
        return report;
    }

    /// <summary>The block's frames, split to DOS's item shape where packed, when it decodes and encodes back to the same bytes.</summary>
    private static (IReadOnlyList<DaxPicture> Frames, bool Animation)? Frames(byte[] block, string container)
    {
        try
        {
            bool anim = AmigaPicture.IsAnimation(block);
            var frames = AmigaPicture.DecodeFrames(block);
            var back = anim ? AmigaPicture.EncodeAnimation(frames) : AmigaPicture.Encode(frames[0]);
            if (!back.AsSpan().SequenceEqual(block))
                return null;
            int tw = Files[container].TileWidth;
            if (tw == 0)
                return (frames, anim);
            if (frames.Any(f => f.Width != PackedPerItem * tw))
                return null;
            return ([.. frames.Select(f => AmigaPicture.SplitTiles(f, tw))], anim);
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    private static void Write(string moduleRoot, string container, int id, IReadOnlyList<DaxPicture> frames, bool animation,
        byte[] palette, int? table, int tileWidth)
    {
        var rel = ArtDocument.PathOf(Variant, container, 0, id);
        string Out(string r) => Path.Combine(moduleRoot, r.Replace('/', Path.DirectorySeparatorChar));
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            if (f.Items.Count == 0 || f.Width == 0 || f.Height == 0)
                continue;
            var png = Out(PorArtCodec.FramePath(rel, animation, i));
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, IndexedPng.Encode(f.Width, f.Height * f.Items.Count, [.. f.Items.SelectMany(x => x)], palette));
        }
        ModuleJson.Write(Out(rel + ".json"), new ArtDocument
        {
            Animation = animation,
            Frames = [.. frames.Select(f => new ArtFrame
            {
                Width = f.Width, Height = f.Height, Items = f.Items.Count, X = f.X, Y = f.Y,
                Delay = animation ? f.Delay : null,
            })],
            PackedTiles = tileWidth > 0 ? PackedPerItem : null,
            PaletteTable = table,
            Source = new SourceRef { Container = container == IntroContainer ? container : container + ".dax", Block = id },
        });
    }

    /// <summary>The Amiga block bytes an overlay document stands for (round trip): tiles joined back, planar encoding.</summary>
    public static byte[] Block(ArtDocument doc, string jsonPath)
    {
        var frames = PorArtCodec.ReadFrames(doc, jsonPath)
            ?? throw new FormatException($"{jsonPath}: an Amiga art document must be a picture");
        if (doc.PackedTiles is { } per)
            frames = [.. frames.Select(f => AmigaPicture.JoinTiles(f, per))];
        return doc.Animation ? AmigaPicture.EncodeAnimation(frames) : AmigaPicture.Encode(frames[0]);
    }

    /// <summary>
    /// Overlay round trip: every block the overlay holds rebuilds to the decoded Amiga block, and every picture block
    /// of the Amiga files is in the overlay unless it is a placeholder.
    /// </summary>
    public static RoundTripReport Check(string amigaDir, PorModule module)
    {
        var r = new RoundTripReport();
        var (_, dataDir) = Locate(amigaDir);
        foreach (var (container, path) in DataFiles(dataDir))
        {
            var dax = AmigaDaxFile.Open(path);
            foreach (var e in dax.Entries)
            {
                if (LeftOut.Contains((container, e.Id)))
                    continue;
                var block = dax.Decode(e);
                var rel = ArtDocument.PathOf(Variant, container, 0, e.Id) + ".json";
                var file = module.Modules.Main.File(rel);
                r.Count("AMIGA ART");
                if (file is null)
                {
                    if (Frames(block, container) is not null)
                        r.Mismatches.Add($"amiga {container} block {e.Id}: missing in the module");
                    continue;
                }
                var rebuilt = Block(ModuleJson.Read<ArtDocument>(file), file);
                if (!rebuilt.AsSpan().SequenceEqual(block))
                    r.Mismatches.Add($"amiga {container} block {e.Id}: {rebuilt.Length} bytes rebuilt, {block.Length} original");
            }
        }
        if (LocateIntro(amigaDir) is { } introPath)
        {
            r.Count("AMIGA TITLE");
            var (pixels, palette) = AmigaIntro.Title(File.ReadAllBytes(introPath));
            if (module.Art(IntroContainer, TitleId, GoldBox.Data.Por.PorArtChoice.Amiga) is not { Variant: Variant, Frames: [{ Items: [var px] }] } img)
                r.Mismatches.Add("amiga intro title: missing in the module");
            else if (!px.AsSpan().SequenceEqual(pixels) || !img.PaletteRgb.AsSpan().SequenceEqual(palette))
                r.Mismatches.Add("amiga intro title: pixels or palette differ from the intro file");
        }
        return r;
    }

    private static string Sha256(byte[] b) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(b));
}
