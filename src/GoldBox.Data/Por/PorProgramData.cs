using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>The 3D view's program tables (<see cref="PorStartExe"/>): wall-piece layout, sky colours, command line.</summary>
public sealed record PorViewTables(ViewLayout Layout, IReadOnlyList<byte> SkyColours, IReadOnlyList<string>? ExploreCommands);

/// <summary>DS offsets of the program's tables, located through the code (<see cref="PorProgram"/>, <see cref="PorSpellData"/>).</summary>
public sealed record PorProgramOffsets
{
    public int? Spells { get; init; }
    public int? Experience { get; init; }
    /// <summary>Handlers 18, 26 and 47's name lists.</summary>
    public int[]? EffectNames { get; init; }
    public int? AiSteps { get; init; }
    public int? RandomItems { get; init; }
    public int? TurnUndead { get; init; }
    public int? ItemWords { get; init; }
    public int? TempleNames { get; init; }
    public int? CampMenu { get; init; }
    public LevelStatsOffsets? LevelStats { get; init; }
    /// <summary>ADDNPC's portrait tables: head base, then the body bases in code order.</summary>
    public int? NpcHead { get; init; }
    public int[]? NpcBodies { get; init; }
}

/// <summary>
/// Everything the engine and the Client read from the player's program (START.EXE and GAME.OVR), in one place: the data
/// segment, the DS offsets of its tables, the view tables, the strings the code segments hold and the spell names.
/// Built from the program files (<see cref="FromProgram"/>) or from a module's <c>rules/program.json</c> and
/// <c>text/en.json</c> (<see cref="Module.Por.PorModule"/>); both are read by the same code (<see cref="PorDsTables"/>),
/// so a module gives the same tables and texts as the folder it was imported from. Nothing of the program is in our code.
/// </summary>
public sealed class PorProgramData
{
    /// <summary>The data segment, DS:0000 to the end of the START.EXE load image.</summary>
    public required byte[] Ds { get; init; }
    public required PorProgramOffsets Offsets { get; init; }
    /// <summary>Handler 48's two 16-byte name slots (GAME.OVR code segment), or null.</summary>
    public IReadOnlyList<byte[]>? GnomeDefenseNames { get; init; }
    /// <summary>The view tables, or null when START.EXE's piece table was not found.</summary>
    public PorViewTables? View { get; init; }
    /// <summary>Code-segment strings by <see cref="PorTextCatalog"/> key (only for the v1.3 GAME.OVR the offsets belong to).</summary>
    public IReadOnlyDictionary<string, string> Texts { get; init; } = new Dictionary<string, string>();
    /// <summary>Spell names 1.. (<see cref="PorSpellNames"/>), or null.</summary>
    public IReadOnlyList<string>? SpellNames { get; init; }
    /// <summary>The text window's page prompt (<see cref="PorProgram.PagePrompt"/>), or null.</summary>
    public string? PagePrompt { get; init; }
    /// <summary>
    /// The overland tile map and special locations (<see cref="PorOverland"/>; a module's <c>rules/overland.json</c>), or
    /// null. The combat arena's wilderness terrain reads it (<see cref="CombatTables"/>).
    /// </summary>
    public OverlandMap? Overland { get; init; }
    /// <summary>Where the overland's tables were found in the program (a folder only; a module has none), or null.</summary>
    public PorOverlandOffsets? OverlandOffsets { get; init; }
    /// <summary>Where the land's recovery tables were found (<see cref="PorOverlandRecovery"/>; a folder only), or null.</summary>
    public PorOverlandRecoveryOffsets? RecoveryOffsets { get; init; }

    /// <summary>The program in <paramref name="dir"/>, or null without START.EXE.</summary>
    public static PorProgramData? Open(PorGameDirectory dir)
    {
        if (dir.Find("START.EXE") is not { } exePath)
            return null;
        var exe = File.ReadAllBytes(exePath);
        var ovr = dir.Find("GAME.OVR") is { } o ? File.ReadAllBytes(o) : null;
        PorViewTables? view = null;
        try
        {
            var s = new PorStartExe(exe);
            view = new PorViewTables(s.Layout, s.SkyColours, s.ExploreCommands);
        }
        catch (FormatException)
        {
        }
        return FromProgram(new PorProgram(exe, ovr), view);
    }

    public static PorProgramData FromProgram(PorProgram p, PorViewTables? view)
    {
        var npc = p.NpcPortraitBases();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (p.Overlay is { Length: PorTextCatalog.V13OverlaySize })
        {
            foreach (var (key, off) in PorTextCatalog.Overlay)
                if (p.OverlayString(off) is { } s)
                    texts[key] = s;
            foreach (var (key, off) in PorTextCatalog.Image)
                if (PorTextCatalog.ImageString(p.Image, off) is { } s)
                    texts[key] = s;
        }
        var overland = PorOverland.Locate(p.Overlay);
        var recovery = PorOverlandRecovery.Locate(p.Overlay);
        return new PorProgramData
        {
            Ds = p.Ds.ToArray(),
            OverlandOffsets = overland,
            RecoveryOffsets = recovery,
            Overland = overland is null ? null : PorOverland.Read(p.Ds, overland) is { } map
                ? new OverlandMap(map.Width, map.Height, map.Tiles, map.Sites)
                {
                    Recovery = recovery is null ? null : PorOverlandRecovery.Read(p.Ds, recovery),
                }
                : null,
            Offsets = new PorProgramOffsets
            {
                Spells = PorSpellData.SpellTableBase(p),
                Experience = PorSpellData.ExperienceBase(p),
                EffectNames = PorSpellData.NameTableBases(p),
                AiSteps = p.AiStepOffsetsBase(),
                RandomItems = p.RandomItemBase(),
                TurnUndead = p.TurnUndeadBase(),
                ItemWords = p.ItemWordsBase(),
                TempleNames = p.TempleNamesBase(),
                CampMenu = p.CampMenuDs(),
                LevelStats = PorSpellData.LevelTableOffsets(p),
                NpcHead = npc?.Head,
                NpcBodies = npc?.Bodies,
            },
            GnomeDefenseNames = PorSpellData.GnomeDefenseNames(p),
            View = view,
            Texts = texts,
            SpellNames = PorSpellNames.Scan(p.Image) is { Count: > 0 } names ? names : null,
            PagePrompt = p.PagePrompt(),
        };
    }

    // ---- tables -----------------------------------------------------------------------------------------------------

    public string? PascalString(int ds, int max = 255) => PorDsTables.PascalString(Ds, ds, max);

    public SpellTable? Spells() => Offsets.Spells is { } b ? PorDsTables.Spells(Ds, b) : null;

    public int[]? ExperienceTable() => Offsets.Experience is { } b ? PorDsTables.Experience(Ds, b) : null;

    public EffectNameTables? EffectNames() =>
        Offsets.EffectNames is { Length: 3 } t ? PorDsTables.EffectNames(Ds, t, GnomeDefenseNames) : null;

    public LevelStatsTables? LevelTables() => Offsets.LevelStats?.Tables(Ds);

    /// <summary>
    /// The party menu's Create tables (<see cref="CreationTables"/>, fixed v1.3 DS offsets), or null when the level-stats
    /// tables were not found at their v1.3 places (THAC0 DS:3C7C, spells DS:31FA), i.e. not Pool of Radiance DOS v1.3.
    /// </summary>
    public CreationTables? CreationTables() =>
        LevelTables() is { Thac0: 0x3C7C, SpellTable: 0x31FA } levels ? new CreationTables { Data = Ds, Levels = levels } : null;

    public byte[]? AiSteps(int count = 40) => Offsets.AiSteps is { } b ? PorDsTables.Bytes(Ds, b, count) : null;

    public ushort[]? RandomItemTable(int words = TreasureMaker.TableWords) =>
        Offsets.RandomItems is { } b ? PorDsTables.Words(Ds, b, words) : null;

    public TurnUndeadTable? TurnUndeadTable() => Offsets.TurnUndead is { } b ? PorDsTables.TurnUndead(Ds, b) : null;

    /// <summary>As <see cref="PorProgram.NpcPortraitByte"/>.</summary>
    public byte? NpcPortraitByte(int ds) =>
        Offsets.NpcHead is { } h && Offsets.NpcBodies is { } bodies && PorDsTables.IsV13NpcPortraits(h, bodies)
            ? PorDsTables.Byte(Ds, ds) : null;

    public string? ItemWord(int w) => Offsets.ItemWords is { } b ? PorDsTables.ItemWord(Ds, b, w) : null;

    public IReadOnlyList<string>? TempleNames() => Offsets.TempleNames is { } b ? PorDsTables.TempleNames(Ds, b) : null;

    public string? CampMenu() => Offsets.CampMenu is { } ds ? PascalString(ds) : null;

    /// <summary>
    /// The combat map's tables (fixed v1.3 DS offsets, checked; <see cref="PorCombatTables"/>). The wilderness terrain is
    /// <see cref="Overland"/>'s tiles when there is one (a module: its <c>rules/overland.json</c>), else DS:3648.
    /// </summary>
    public CombatTables CombatTables() => PorCombatTables.Load(Ds, Overland?.Tiles);

    /// <summary>A code-segment string by <see cref="PorTextCatalog"/> key, or null.</summary>
    public string? Text(string key) => Texts.TryGetValue(key, out var s) ? s : null;

    /// <summary>The fight's tables (null when one is not found), with the item types from the content.</summary>
    public FightKit? FightKit(BaseItemTable items)
    {
        if (Spells() is not { } spells || AiSteps() is not { } steps)
            return null;
        return new FightKit
        {
            Tables = CombatTables(),
            Spells = spells,
            Items = items,
            AiSteps = steps,
            Names = EffectNames(),
            Experience = ExperienceTable(),
            TurnUndead = TurnUndeadTable(),
            LevelTables = LevelTables(),
        };
    }
}

/// <summary>
/// The spell names in START.EXE: an array of fixed-size Pascal strings found by its shape (the first run of at least
/// <see cref="MinRun"/> consecutive names with one stride in the unpacked image). [?] The table's address is not read
/// from the code: the run with the widest entries is taken, starting at spell 1 (Lee's spell numbering).
/// </summary>
public static class PorSpellNames
{
    public const int MinRun = 50;

    public static List<string> Scan(byte[] image)
    {
        // The run with the widest entries (the item-name words and the item kinds form runs with narrower ones).
        int best = 0, bestO = 0, bestS = 0;
        for (int stride = 12; stride <= 48; stride++)
            for (int o = 0; o + stride * MinRun < image.Length; o++)
            {
                int n = Run(image, o, stride);
                if (n >= MinRun && stride >= bestS)
                    (best, bestO, bestS) = (n, o, stride);
                if (n >= MinRun)
                    o += (n - 1) * stride;
            }
        var names = new List<string>();
        if (best < MinRun)
            return names;
        for (int k = 0; k < best; k++)
            names.Add(System.Text.Encoding.Latin1.GetString(image, bestO + k * bestS + 1, image[bestO + k * bestS]));
        return names;
    }

    private static int Run(byte[] b, int o, int stride)
    {
        int n = 0;
        for (int p = o; p + stride <= b.Length; p += stride, n++)
        {
            int len = b[p];
            if (len < 3 || len >= stride || !char.IsAsciiLetterUpper((char)b[p + 1]))
                return n;
            for (int i = 1; i <= len; i++)
            {
                byte c = b[p + i];
                if (!(char.IsAsciiLetter((char)c) || c is (byte)' ' or (byte)'\'' or (byte)',' or (byte)'-' || char.IsAsciiDigit((char)c)))
                    return n;
            }
        }
        return n;
    }
}
