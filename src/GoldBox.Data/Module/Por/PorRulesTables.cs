using System.Buffers.Binary;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>The manifest names a rules profile the engine does not have.</summary>
public sealed class UnknownRulesProfileException(string name)
    : Exception($"unknown rules profile '{name}' (known: {string.Join(", ", PorRulesProfiles.All.Select(p => p.Name))})")
{
    public string Name { get; } = name;
}

/// <summary>A decoded rules file is missing, names another profile, or does not encode (docs/module-format.md §12).</summary>
public sealed class RulesTablesException(string message) : FormatException(message);

/// <summary>
/// A rules profile (docs/module-format.md §5.2): its name and the decoded tables (<c>rules/*.json</c>) it needs. The
/// rule code itself is Core's; the profile only fixes which tables a module must bring.
/// </summary>
public sealed record PorRulesProfile(string Name, IReadOnlyList<string> RequiredFiles);

public static class PorRulesProfiles
{
    /// <summary>Pool of Radiance DOS v1.3: every decoded table the importer writes.</summary>
    public static PorRulesProfile Por13 { get; } = new("por-1.3",
    [
        SpellsDocument.RelativePath, ExperienceDocument.RelativePath, LevelStatsDocument.RelativePath,
        TurnUndeadDocument.RelativePath, AiStepsDocument.RelativePath, RandomItemsDocument.RelativePath,
        TempleDocument.RelativePath, CombatMapDocument.RelativePath,
    ]);

    public static IReadOnlyList<PorRulesProfile> All { get; } = [Por13];

    /// <summary>The profile <paramref name="name"/> names; null = <c>por-1.3</c> `[?]` (as a module with no <c>vm</c>).</summary>
    /// <exception cref="UnknownRulesProfileException">No such profile.</exception>
    public static PorRulesProfile ForRules(string? name) =>
        name is null ? Por13 : All.FirstOrDefault(p => p.Name == name) ?? throw new UnknownRulesProfileException(name);
}

/// <summary>The decoded rules files of a module (each null when absent).</summary>
public sealed record PorRulesSet
{
    public SpellsDocument? Spells { get; init; }
    public ExperienceDocument? Experience { get; init; }
    public LevelStatsDocument? LevelStats { get; init; }
    public TurnUndeadDocument? TurnUndead { get; init; }
    public AiStepsDocument? AiSteps { get; init; }
    public RandomItemsDocument? RandomItems { get; init; }
    public TempleDocument? Temple { get; init; }
    public CombatMapDocument? CombatMap { get; init; }

    /// <summary>The files present, by module path.</summary>
    public IEnumerable<(string Path, IRulesDocument Doc)> Present()
    {
        foreach (var (p, d) in new (string, IRulesDocument?)[]
                 {
                     (SpellsDocument.RelativePath, Spells), (ExperienceDocument.RelativePath, Experience),
                     (LevelStatsDocument.RelativePath, LevelStats), (TurnUndeadDocument.RelativePath, TurnUndead),
                     (AiStepsDocument.RelativePath, AiSteps), (RandomItemsDocument.RelativePath, RandomItems),
                     (TempleDocument.RelativePath, Temple), (CombatMapDocument.RelativePath, CombatMap),
                 })
            if (d is not null)
                yield return (p, d);
    }

    public bool IsEmpty => !Present().Any();
}

/// <summary>
/// The decoded rules tables (docs/module-format.md §12): decode them from the program's data segment (the importer),
/// encode them back to their bytes (lossless: a file's fields give the bytes it was decoded from), and lay them over
/// <c>rules/program.json</c>'s data segment so the engine's readers (<see cref="PorDsTables"/>, Core's table classes)
/// read the decoded values, including where the program reads past a table's end. Each file owns the cells it names;
/// no two files own the same byte (checked when they are applied).
/// </summary>
public static class PorRulesTables
{
    /// <summary>Module path → document type of every decoded rules file.</summary>
    public static readonly IReadOnlyList<(string Path, Type Type)> Files =
    [
        (SpellsDocument.RelativePath, typeof(SpellsDocument)),
        (ExperienceDocument.RelativePath, typeof(ExperienceDocument)),
        (LevelStatsDocument.RelativePath, typeof(LevelStatsDocument)),
        (TurnUndeadDocument.RelativePath, typeof(TurnUndeadDocument)),
        (AiStepsDocument.RelativePath, typeof(AiStepsDocument)),
        (RandomItemsDocument.RelativePath, typeof(RandomItemsDocument)),
        (TempleDocument.RelativePath, typeof(TempleDocument)),
        (CombatMapDocument.RelativePath, typeof(CombatMapDocument)),
    ];

    private static readonly SourceRef StartExe = new() { Container = "START.EXE" };

    // ---- decode ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every table of <paramref name="profile"/> decoded from the data segment, or null when one of them cannot be (an
    /// offset not found, the combat tables not v1.3, a cell past the data segment): a module gets all of them or none.
    /// </summary>
    public static PorRulesSet? Decode(byte[] ds, PorProgramOffsets o, string profile = "por-1.3")
    {
        if (o.Spells is not { } sp || o.Experience is not { } xp || o.LevelStats is not { } ls || o.TurnUndead is not { } tu
            || o.AiSteps is not { } ai || o.RandomItems is not { } ri || o.TempleNames is not { } tn)
            return null;
        try
        {
            PorCombatTables.Load(ds);
            var set = new PorRulesSet
            {
                Spells = DecodeSpells(ds, sp, profile),
                Experience = DecodeExperience(ds, xp, profile),
                LevelStats = DecodeLevelStats(ds, ls, profile),
                TurnUndead = DecodeTurnUndead(ds, tu, profile),
                AiSteps = DecodeAiSteps(ds, ai, profile),
                RandomItems = DecodeRandomItems(ds, ri, profile),
                Temple = DecodeTemple(ds, tn, profile),
                CombatMap = DecodeCombatMap(ds, profile),
            };
            return set;
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static int U8(byte[] ds, int off) => ds[off];
    private static int S8(byte[] ds, int off) => (sbyte)ds[off];
    private static int[] U8s(byte[] ds, int off, int n) => [.. Enumerable.Range(0, n).Select(i => (int)ds[off + i])];
    private static int[] S8s(byte[] ds, int off, int n) => [.. Enumerable.Range(0, n).Select(i => (int)(sbyte)ds[off + i])];
    private static int U16(byte[] ds, int off) => BinaryPrimitives.ReadUInt16LittleEndian(ds.AsSpan(off));

    public static SpellsDocument DecodeSpells(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Spells = [.. Enumerable.Range(1, Core.Game.SpellTable.Count).Select(id =>
        {
            var e = U8s(ds, b + 16 * id, 16);
            return new SpellEntry
            {
                Id = id, CastClass = e[0], Level = e[1], RangeFixed = e[2], RangePerLevel = e[3], DurationFixed = e[4],
                DurationPerLevel = e[5], Area = e[6], Target = e[7], Save = e[8], SaveType = e[9], Effect = e[10],
                WhenCast = e[11], CastTime = e[12], AiPriority = e[13], Offensive = e[14], AiRadius = e[15],
            };
        })],
        Source = StartExe,
    };

    public static ExperienceDocument DecodeExperience(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Classes = [.. Enumerable.Range(0, 8).Select(c => new ExperienceClass
        {
            Class = c,
            Levels = [.. Enumerable.Range(ExperienceDocument.FirstLevel, ExperienceDocument.LastLevel - ExperienceDocument.FirstLevel + 1)
                .Select(l => BinaryPrimitives.ReadInt32LittleEndian(ds.AsSpan(b + 0x38 * c + 4 * l)))],
        })],
        Source = StartExe,
    };

    private static ThiefSkills Skills(int[] s) => new()
    {
        PickPockets = s[0], OpenLocks = s[1], FindTraps = s[2], MoveSilently = s[3], HideInShadows = s[4],
        HearNoise = s[5], ClimbWalls = s[6], ReadLanguages = s[7],
    };

    private static int[] Skills(ThiefSkills s) =>
        [s.PickPockets, s.OpenLocks, s.FindTraps, s.MoveSilently, s.HideInShadows, s.HearNoise, s.ClimbWalls, s.ReadLanguages];

    private static IEnumerable<int> Span(int first, int last) => Enumerable.Range(first, last - first + 1);

    private static SpellSlotTable Slots(byte[] ds, int b) => new()
    {
        DsOffset = b,
        Levels = [.. Span(SpellSlotTable.FirstLevel, SpellSlotTable.LastLevel)
            .Select(l => new SpellSlotLevel { Level = l, Slots = U8s(ds, b + 3 * l + 1, 3) })],
    };

    public static LevelStatsDocument DecodeLevelStats(byte[] ds, LevelStatsOffsets o, string profile) => new()
    {
        Profile = profile,
        Thac0 = new Thac0Table
        {
            DsOffset = o.Thac0,
            Classes = [.. Enumerable.Range(0, 8).Select(c => new Thac0Class
                { Class = c, Levels = U8s(ds, o.Thac0 + Thac0Table.Levels * c, Thac0Table.Levels) })],
        },
        ClericSlots = Slots(ds, o.ClericSlots),
        MageSlots = Slots(ds, o.MageSlots),
        Saves = new SaveTable
        {
            DsOffset = o.Saves,
            Classes = [.. Enumerable.Range(0, 8).Select(c => new SaveClass
            {
                Class = c,
                Levels = [.. Span(SaveTable.FirstLevel, SaveTable.LastLevel).Select(l =>
                {
                    var s = U8s(ds, o.Saves + 0x2D * c + 5 * l, 5);
                    return new SaveLevel
                        { Level = l, Paralyzation = s[0], Petrification = s[1], RodStaffWand = s[2], Breath = s[3], Spell = s[4] };
                })],
            })],
        },
        ThiefBase = new ThiefLevelTable
        {
            DsOffset = o.ThiefBase,
            Levels = [.. Span(ThiefLevelTable.FirstLevel, ThiefLevelTable.LastLevel)
                .Select(l => new ThiefLevel { Level = l, Skills = Skills(U8s(ds, o.ThiefBase + 8 * l + 1, 8)) })],
        },
        ThiefRace = new ThiefRaceTable
        {
            DsOffset = o.ThiefRace,
            Races = [.. Span(ThiefRaceTable.FirstRace, ThiefRaceTable.LastRace)
                .Select(r => new ThiefRace { Race = r, Skills = Skills(S8s(ds, o.ThiefRace + 8 * r + 1, 8)) })],
        },
        ThiefDex = new ThiefDexTable
        {
            DsOffset = o.ThiefDex,
            Dexterity = [.. Span(ThiefDexTable.FirstDexterity, ThiefDexTable.LastDexterity).Select(d =>
            {
                var s = S8s(ds, o.ThiefDex + 5 * d + 1, 5);
                return new ThiefDex
                    { Dexterity = d, PickPockets = s[0], OpenLocks = s[1], FindTraps = s[2], MoveSilently = s[3], HideInShadows = s[4] };
            })],
        },
        Source = StartExe,
    };

    public static TurnUndeadDocument DecodeTurnUndead(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Rows = [.. Span(1, 10).Select(r => new TurnUndeadRow { Row = r, Columns = S8s(ds, b + 10 * r + 1, 10) })],
        Source = StartExe,
    };

    public static AiStepsDocument DecodeAiSteps(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Rows = [.. Span(1, 6).Select(r => new AiStepRow { Row = r, Attempts = U8s(ds, b + 5 * r + 1, 5) })],
        Source = StartExe,
    };

    public static RandomItemsDocument DecodeRandomItems(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Items = [.. Enumerable.Range(0, RandomItemsDocument.Entries).Select(k =>
        {
            int n = 1 + 8 * k;
            int W(int f) => U16(ds, b + 2 * (n + f));
            return new RandomItemEntry
            {
                Magic = n, Word3 = W(0), Word2 = W(1), Word1 = W(2), Weight = W(3), Value = W(4), Props = [W(5), W(6), W(7)],
            };
        })],
        Source = StartExe,
    };

    public static TempleDocument DecodeTemple(byte[] ds, int b, string profile) => new()
    {
        Profile = profile,
        DsOffset = b,
        Heals = [.. Span(1, TempleDocument.Count).Select(k =>
        {
            int at = b + TempleDocument.SlotSize * k;
            int len = ds[at];
            if (len > TempleDocument.SlotSize - 1)
                throw new FormatException($"temple name {k}: length {len}");
            var pad = ds.AsSpan(at + 1 + len, TempleDocument.SlotSize - 1 - len);
            return new TempleEntry
            {
                Index = k,
                Name = System.Text.Encoding.Latin1.GetString(ds, at + 1, len),
                Padding = pad.ContainsAnyExcept((byte)0) ? pad.ToArray() : null,
            };
        })],
        Source = StartExe,
    };

    private static ByteGrid Grid(byte[] ds, int off, int rows, int cols, bool signed) => new()
    {
        DsOffset = off,
        Signed = signed,
        Rows = [.. Enumerable.Range(0, rows).Select(r => signed ? S8s(ds, off + cols * r, cols) : U8s(ds, off + cols * r, cols))],
    };

    public static CombatMapDocument DecodeCombatMap(byte[] ds, string profile) => new()
    {
        Profile = profile,
        Terrain = new TerrainTable
        {
            DsOffset = PorCombatTables.Terrain,
            Entries = [.. Enumerable.Range(0, TerrainTable.Count).Select(t =>
            {
                var e = U8s(ds, PorCombatTables.Terrain + 4 * t, 4);
                return new TerrainEntry { Terrain = t, MoveCost = e[0], SightHeight = e[1], BlockingHeight = e[2], Tile = e[3] };
            })],
        },
        IconCells = new IconCellTable
        {
            DsOffset = PorCombatTables.IconDelta,
            Sizes = [.. Span(IconCellTable.FirstSize, IconCellTable.LastSize).Select(s => new IconSize
            {
                Size = s,
                Cells = [.. Enumerable.Range(0, IconCellTable.Cells).Select(c => new CellDelta
                {
                    Dx = S8(ds, PorCombatTables.IconDelta + 8 * s + 2 * c),
                    Dy = S8(ds, PorCombatTables.IconDelta + 8 * s + 2 * c + 1),
                })],
            })],
        },
        Placement = new PlacementTables
        {
            RingDirection = Grid(ds, PorCombatTables.RingDir, 4, 4, false),
            RingStart = Grid(ds, PorCombatTables.RingStart, 4, 4, false),
            QuadDirection = Grid(ds, PorCombatTables.QuadDir, 1, 4, false),
            StartQuadX = Grid(ds, PorCombatTables.StartQuadX, 2, 4, true),
            StartQuadY = Grid(ds, PorCombatTables.StartQuadY, 2, 4, true),
            RowBand = Grid(ds, PorCombatTables.RowBand, 5, 12, true),
        },
        Source = StartExe,
    };

    // ---- encode ------------------------------------------------------------------------------------------------------

    /// <summary>The bytes a decoded file stands for, by DS offset (the cells it owns).</summary>
    public static SortedDictionary<int, byte> Encode(IRulesDocument doc)
    {
        var w = new Cells(doc switch
        {
            SpellsDocument => SpellsDocument.RelativePath,
            ExperienceDocument => ExperienceDocument.RelativePath,
            LevelStatsDocument => LevelStatsDocument.RelativePath,
            TurnUndeadDocument => TurnUndeadDocument.RelativePath,
            AiStepsDocument => AiStepsDocument.RelativePath,
            RandomItemsDocument => RandomItemsDocument.RelativePath,
            TempleDocument => TempleDocument.RelativePath,
            CombatMapDocument => CombatMapDocument.RelativePath,
            _ => throw new ArgumentException($"not a rules document: {doc.GetType().Name}", nameof(doc)),
        });
        switch (doc)
        {
            case SpellsDocument s:
                w.Count("spells", s.Spells.Count, Core.Game.SpellTable.Count);
                foreach (var e in s.Spells)
                {
                    w.Range("spell id", e.Id, 1, Core.Game.SpellTable.Count);
                    w.U8s(s.DsOffset + 16 * e.Id, [e.CastClass, e.Level, e.RangeFixed, e.RangePerLevel, e.DurationFixed,
                        e.DurationPerLevel, e.Area, e.Target, e.Save, e.SaveType, e.Effect, e.WhenCast, e.CastTime,
                        e.AiPriority, e.Offensive, e.AiRadius], $"spell {e.Id}");
                }
                break;
            case ExperienceDocument x:
                w.Count("classes", x.Classes.Count, 8);
                foreach (var c in x.Classes)
                {
                    w.Range("class", c.Class, 0, 7);
                    w.Count($"class {c.Class} levels", c.Levels.Length, ExperienceDocument.LastLevel - ExperienceDocument.FirstLevel + 1);
                    for (int i = 0; i < c.Levels.Length; i++)
                        w.I32(x.DsOffset + 0x38 * c.Class + 4 * (ExperienceDocument.FirstLevel + i), c.Levels[i]);
                }
                break;
            case LevelStatsDocument l:
                EncodeLevelStats(w, l);
                break;
            case TurnUndeadDocument t:
                w.Count("rows", t.Rows.Count, 10);
                foreach (var r in t.Rows)
                {
                    w.Range("row", r.Row, 1, 10);
                    w.Count($"row {r.Row} columns", r.Columns.Length, 10);
                    w.S8s(t.DsOffset + 10 * r.Row + 1, r.Columns, $"row {r.Row}");
                }
                break;
            case AiStepsDocument a:
                w.Count("rows", a.Rows.Count, 6);
                foreach (var r in a.Rows)
                {
                    w.Range("row", r.Row, 1, 6);
                    w.Count($"row {r.Row} attempts", r.Attempts.Length, 5);
                    w.U8s(a.DsOffset + 5 * r.Row + 1, r.Attempts, $"row {r.Row}");
                }
                break;
            case RandomItemsDocument ri:
                w.Count("items", ri.Items.Count, RandomItemsDocument.Entries);
                foreach (var e in ri.Items)
                {
                    if (e.Magic < 1 || (e.Magic - 1) % 8 != 0 || (e.Magic - 1) / 8 >= RandomItemsDocument.Entries)
                        throw w.Error($"magic {e.Magic} is not 1 + 8k with k < {RandomItemsDocument.Entries}");
                    w.Count($"item {e.Magic} props", e.Props.Length, 3);
                    int[] words = [e.Word3, e.Word2, e.Word1, e.Weight, e.Value, .. e.Props];
                    for (int f = 0; f < words.Length; f++)
                        w.U16(ri.DsOffset + 2 * (e.Magic + f), words[f]);
                }
                break;
            case TempleDocument t:
                w.Count("heals", t.Heals.Count, TempleDocument.Count);
                foreach (var h in t.Heals)
                {
                    w.Range("heal index", h.Index, 1, TempleDocument.Count);
                    var name = System.Text.Encoding.Latin1.GetBytes(h.Name);
                    int room = TempleDocument.SlotSize - 1;
                    if (name.Length > room)
                        throw w.Error($"heal {h.Index}: name longer than {room}");
                    var pad = h.Padding ?? new byte[room - name.Length];
                    if (name.Length + pad.Length != room)
                        throw w.Error($"heal {h.Index}: name + padding is {name.Length + pad.Length} bytes, not {room}");
                    int at = t.DsOffset + TempleDocument.SlotSize * h.Index;
                    w.Put(at, (byte)name.Length);
                    for (int i = 0; i < name.Length; i++)
                        w.Put(at + 1 + i, name[i]);
                    for (int i = 0; i < pad.Length; i++)
                        w.Put(at + 1 + name.Length + i, pad[i]);
                }
                break;
            case CombatMapDocument m:
                EncodeCombatMap(w, m);
                break;
        }
        return w.Bytes;
    }

    private static void EncodeLevelStats(Cells w, LevelStatsDocument l)
    {
        w.Count("thac0 classes", l.Thac0.Classes.Count, 8);
        foreach (var c in l.Thac0.Classes)
        {
            w.Range("thac0 class", c.Class, 0, 7);
            w.Count($"thac0 class {c.Class} levels", c.Levels.Length, Thac0Table.Levels);
            w.U8s(l.Thac0.DsOffset + Thac0Table.Levels * c.Class, c.Levels, $"thac0 class {c.Class}");
        }
        foreach (var (name, t) in new[] { ("clericSlots", l.ClericSlots), ("mageSlots", l.MageSlots) })
        {
            w.Count(name, t.Levels.Count, SpellSlotTable.LastLevel - SpellSlotTable.FirstLevel + 1);
            foreach (var s in t.Levels)
            {
                w.Range($"{name} level", s.Level, SpellSlotTable.FirstLevel, SpellSlotTable.LastLevel);
                w.Count($"{name} level {s.Level}", s.Slots.Length, 3);
                w.U8s(t.DsOffset + 3 * s.Level + 1, s.Slots, $"{name} level {s.Level}");
            }
        }
        w.Count("save classes", l.Saves.Classes.Count, 8);
        foreach (var c in l.Saves.Classes)
        {
            w.Range("save class", c.Class, 0, 7);
            w.Count($"save class {c.Class} levels", c.Levels.Count, SaveTable.LastLevel - SaveTable.FirstLevel + 1);
            foreach (var s in c.Levels)
            {
                w.Range("save level", s.Level, SaveTable.FirstLevel, SaveTable.LastLevel);
                w.U8s(l.Saves.DsOffset + 0x2D * c.Class + 5 * s.Level,
                    [s.Paralyzation, s.Petrification, s.RodStaffWand, s.Breath, s.Spell], $"saves class {c.Class} level {s.Level}");
            }
        }
        w.Count("thief levels", l.ThiefBase.Levels.Count, ThiefLevelTable.LastLevel - ThiefLevelTable.FirstLevel + 1);
        foreach (var t in l.ThiefBase.Levels)
        {
            w.Range("thief level", t.Level, ThiefLevelTable.FirstLevel, ThiefLevelTable.LastLevel);
            w.U8s(l.ThiefBase.DsOffset + 8 * t.Level + 1, Skills(t.Skills), $"thief level {t.Level}");
        }
        w.Count("thief races", l.ThiefRace.Races.Count, ThiefRaceTable.LastRace - ThiefRaceTable.FirstRace + 1);
        foreach (var r in l.ThiefRace.Races)
        {
            w.Range("thief race", r.Race, ThiefRaceTable.FirstRace, ThiefRaceTable.LastRace);
            w.S8s(l.ThiefRace.DsOffset + 8 * r.Race + 1, Skills(r.Skills), $"thief race {r.Race}");
        }
        w.Count("thief dexterity", l.ThiefDex.Dexterity.Count, ThiefDexTable.LastDexterity - ThiefDexTable.FirstDexterity + 1);
        foreach (var d in l.ThiefDex.Dexterity)
        {
            w.Range("dexterity", d.Dexterity, ThiefDexTable.FirstDexterity, ThiefDexTable.LastDexterity);
            w.S8s(l.ThiefDex.DsOffset + 5 * d.Dexterity + 1,
                [d.PickPockets, d.OpenLocks, d.FindTraps, d.MoveSilently, d.HideInShadows], $"dexterity {d.Dexterity}");
        }
    }

    private static void EncodeCombatMap(Cells w, CombatMapDocument m)
    {
        void Fixed(string name, int have, int want)
        {
            if (have != want)
                throw w.Error($"{name} at DS:{have:X4}: the engine reads it at DS:{want:X4} (v1.3)");
        }
        Fixed("terrain", m.Terrain.DsOffset, PorCombatTables.Terrain);
        Fixed("iconCells", m.IconCells.DsOffset, PorCombatTables.IconDelta);
        var p = m.Placement;
        Fixed("ringDirection", p.RingDirection.DsOffset, PorCombatTables.RingDir);
        Fixed("ringStart", p.RingStart.DsOffset, PorCombatTables.RingStart);
        Fixed("quadDirection", p.QuadDirection.DsOffset, PorCombatTables.QuadDir);
        Fixed("startQuadX", p.StartQuadX.DsOffset, PorCombatTables.StartQuadX);
        Fixed("startQuadY", p.StartQuadY.DsOffset, PorCombatTables.StartQuadY);
        Fixed("rowBand", p.RowBand.DsOffset, PorCombatTables.RowBand);

        w.Count("terrain entries", m.Terrain.Entries.Count, TerrainTable.Count);
        foreach (var t in m.Terrain.Entries)
        {
            w.Range("terrain", t.Terrain, 0, TerrainTable.Count - 1);
            w.U8s(m.Terrain.DsOffset + 4 * t.Terrain, [t.MoveCost, t.SightHeight, t.BlockingHeight, t.Tile], $"terrain {t.Terrain}");
        }
        w.Count("icon sizes", m.IconCells.Sizes.Count, IconCellTable.LastSize - IconCellTable.FirstSize + 1);
        foreach (var s in m.IconCells.Sizes)
        {
            w.Range("icon size", s.Size, IconCellTable.FirstSize, IconCellTable.LastSize);
            w.Count($"icon size {s.Size} cells", s.Cells.Count, IconCellTable.Cells);
            w.S8s(m.IconCells.DsOffset + 8 * s.Size, [.. s.Cells.SelectMany(c => new[] { c.Dx, c.Dy })], $"icon size {s.Size}");
        }
        void Grid(string name, ByteGrid g, int rows, int cols, bool signed)
        {
            if (g.Signed != signed)
                throw w.Error($"{name}: signed must be {signed}");
            w.Count($"{name} rows", g.Rows.Count, rows);
            for (int r = 0; r < rows; r++)
            {
                w.Count($"{name} row {r}", g.Rows[r].Length, cols);
                if (signed)
                    w.S8s(g.DsOffset + cols * r, g.Rows[r], $"{name} row {r}");
                else
                    w.U8s(g.DsOffset + cols * r, g.Rows[r], $"{name} row {r}");
            }
        }
        Grid("ringDirection", p.RingDirection, 4, 4, false);
        Grid("ringStart", p.RingStart, 4, 4, false);
        Grid("quadDirection", p.QuadDirection, 1, 4, false);
        Grid("startQuadX", p.StartQuadX, 2, 4, true);
        Grid("startQuadY", p.StartQuadY, 2, 4, true);
        Grid("rowBand", p.RowBand, 5, 12, true);
    }

    /// <summary>Collects a file's cells; a cell written twice or a value out of range is refused.</summary>
    private sealed class Cells(string file)
    {
        public SortedDictionary<int, byte> Bytes { get; } = [];

        public RulesTablesException Error(string what) => new($"{file}: {what}");

        public void Count(string what, int have, int want)
        {
            if (have != want)
                throw Error($"{what}: {have}, expected {want}");
        }

        public void Range(string what, int v, int lo, int hi)
        {
            if (v < lo || v > hi)
                throw Error($"{what} {v} outside {lo}..{hi}");
        }

        public void Put(int off, byte b)
        {
            if (off < 0 || !Bytes.TryAdd(off, b))
                throw Error($"DS:{off:X4} written twice (a row or entry repeated?)");
        }

        public void U8s(int off, int[] v, string what)
        {
            for (int i = 0; i < v.Length; i++)
            {
                Range($"{what} value {i}", v[i], 0, 255);
                Put(off + i, (byte)v[i]);
            }
        }

        public void S8s(int off, int[] v, string what)
        {
            for (int i = 0; i < v.Length; i++)
            {
                Range($"{what} value {i}", v[i], -128, 127);
                Put(off + i, (byte)(sbyte)v[i]);
            }
        }

        public void U16(int off, int v)
        {
            Range($"word at DS:{off:X4}", v, 0, 0xFFFF);
            Put(off, (byte)v);
            Put(off + 1, (byte)(v >> 8));
        }

        public void I32(int off, int v)
        {
            for (int i = 0; i < 4; i++)
                Put(off + i, (byte)(v >> (8 * i)));
        }
    }

    // ---- apply -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="ds"/> with every decoded file's cells written over it (a copy), and <paramref name="o"/> with the
    /// files' table offsets. Fails when two files own the same byte or a cell lies past the data segment.
    /// </summary>
    public static (byte[] Ds, PorProgramOffsets Offsets) Apply(byte[] ds, PorProgramOffsets o, PorRulesSet set)
    {
        var outDs = ds.ToArray();
        var owner = new Dictionary<int, string>();
        foreach (var (path, doc) in set.Present())
            foreach (var (off, b) in Encode(doc))
            {
                if (off >= outDs.Length)
                    throw new RulesTablesException($"{path}: DS:{off:X4} is past the data segment ({outDs.Length} bytes)");
                if (!owner.TryAdd(off, path))
                    throw new RulesTablesException($"{path}: DS:{off:X4} is also owned by {owner[off]}");
                outDs[off] = b;
            }
        int? spells = set.Spells?.DsOffset ?? o.Spells;
        var levels = o.LevelStats;
        if (set.LevelStats is { } l)
        {
            if ((levels?.SpellTable ?? spells) is not { } st)
                throw new RulesTablesException($"{LevelStatsDocument.RelativePath}: no spell table offset (needs {SpellsDocument.RelativePath})");
            levels = new LevelStatsOffsets
            {
                Thac0 = l.Thac0.DsOffset, ClericSlots = l.ClericSlots.DsOffset, SpellTable = set.Spells?.DsOffset ?? st,
                MageSlots = l.MageSlots.DsOffset, Saves = l.Saves.DsOffset, ThiefBase = l.ThiefBase.DsOffset,
                ThiefRace = l.ThiefRace.DsOffset, ThiefDex = l.ThiefDex.DsOffset,
            };
        }
        else if (levels is not null && set.Spells is { } sd)
            levels = levels with { SpellTable = sd.DsOffset };
        return (outDs, o with
        {
            Spells = spells,
            Experience = set.Experience?.DsOffset ?? o.Experience,
            LevelStats = levels,
            TurnUndead = set.TurnUndead?.DsOffset ?? o.TurnUndead,
            AiSteps = set.AiSteps?.DsOffset ?? o.AiSteps,
            RandomItems = set.RandomItems?.DsOffset ?? o.RandomItems,
            TempleNames = set.Temple?.DsOffset ?? o.TempleNames,
        });
    }

    // ---- module ------------------------------------------------------------------------------------------------------

    /// <summary>The decoded rules files the module (or a dependency) holds.</summary>
    public static PorRulesSet Read(ModuleResolver modules)
    {
        // Strict: a misspelt member would otherwise leave its cell 0.
        T? R<T>(string path) where T : class => modules.File(path) is { } p
            ? System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(p), ModuleSchemas.Strict)
              ?? throw new RulesTablesException($"{p}: empty JSON")
            : null;
        return new PorRulesSet
        {
            Spells = R<SpellsDocument>(SpellsDocument.RelativePath),
            Experience = R<ExperienceDocument>(ExperienceDocument.RelativePath),
            LevelStats = R<LevelStatsDocument>(LevelStatsDocument.RelativePath),
            TurnUndead = R<TurnUndeadDocument>(TurnUndeadDocument.RelativePath),
            AiSteps = R<AiStepsDocument>(AiStepsDocument.RelativePath),
            RandomItems = R<RandomItemsDocument>(RandomItemsDocument.RelativePath),
            Temple = R<TempleDocument>(TempleDocument.RelativePath),
            CombatMap = R<CombatMapDocument>(CombatMapDocument.RelativePath),
        };
    }

    /// <summary>
    /// The data segment and offsets the engine reads: <c>rules/program.json</c>'s (<paramref name="ds"/>,
    /// <paramref name="o"/>) with the decoded files laid over them. The manifest's rules profile checks the files: with
    /// none present the raw tables are used as they are; with any present, every table the profile needs must be there
    /// and name that profile.
    /// </summary>
    /// <exception cref="UnknownRulesProfileException">The manifest names an unknown rules profile.</exception>
    /// <exception cref="RulesTablesException">A needed file is missing, names another profile, or does not encode.</exception>
    public static (byte[] Ds, PorProgramOffsets Offsets) Load(ModuleResolver modules, byte[] ds, PorProgramOffsets o)
    {
        var profile = PorRulesProfiles.ForRules(modules.Main.Manifest.Rules);
        var set = Read(modules);
        if (set.IsEmpty)
            return (ds, o);
        var present = set.Present().ToDictionary(p => p.Path, p => p.Doc);
        var missing = profile.RequiredFiles.Where(f => !present.ContainsKey(f)).ToList();
        if (missing.Count > 0)
            throw new RulesTablesException($"rules profile {profile.Name}: missing {string.Join(", ", missing)}");
        foreach (var (path, doc) in present)
            if (doc.Profile != profile.Name)
                throw new RulesTablesException($"{path}: profile '{doc.Profile}', the module's rules profile is '{profile.Name}'");
        return Apply(ds, o, set);
    }

    /// <summary>Writes the decoded files of <paramref name="program"/> under <paramref name="outDir"/>; false (nothing
    /// written) when one of the tables cannot be decoded.</summary>
    public static bool Write(string outDir, PorProgramData program, string profile = "por-1.3")
    {
        if (Decode(program.Ds, program.Offsets, profile) is not { } set)
            return false;
        foreach (var (path, doc) in set.Present())
            ModuleJson.Write(Path.Combine(outDir, path.Replace('/', Path.DirectorySeparatorChar)), (object)doc);
        return true;
    }
}
