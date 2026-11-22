using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// What the screens read from the player's program and data files: the program's texts, the item name words, the
/// temple's heal list, the camp menu line, the text window's page prompt and the ITEMS table (for the scroll test).
/// Every part falls back to our wording when it is not found.
/// </summary>
public sealed class ClientResources
{
    public GameText Texts { get; init; } = new();
    /// <summary>The fight's texts (also the effect messages a camp Cast shows, <see cref="CampScreens"/>).</summary>
    public CombatText CombatTexts { get; init; } = new();
    public Func<int, string?> ItemWord { get; init; } = _ => null;
    public Func<byte, bool> IsScroll { get; init; } = _ => false;
    /// <summary>The ITEMS table (the stat recalculation START 0x1758 reads the readied items' base bytes), or null.</summary>
    public BaseItemTable? ItemTypes { get; init; }
    public IReadOnlyList<string>? TempleNames { get; init; }
    public string? CampMenu { get; init; }
    public string? PagePrompt { get; init; }

    /// <summary>The camp's Magic and Alter menu lines and the party order lines (Select / Place), original case.</summary>
    public string? MagicMenu { get; init; }
    public string? AlterMenu { get; init; }
    public IReadOnlyList<string>? OrderMenus { get; init; }

    /// <summary>
    /// The program's menu lines in their own case (the camp's, the fight's, View's and the Items page's words, the
    /// shop's, temple's and loot's lines, the explore commands, …): what <see cref="AmigaView.LearnMenuLines"/> learns
    /// the words' spelling from for the Amiga pages, which draw the bytes as they are. The Amiga's lines are the DOS
    /// lines byte for byte (s43, every one compared with the Amiga <c>program</c>); its own wordings come from
    /// <see cref="IPorContent.AmigaStrings"/>.
    /// </summary>
    public IReadOnlyList<string> MenuLines { get; init; } = [];

    /// <summary>The catalogue keys whose texts are menu lines (<see cref="MenuLines"/>), by prefix.</summary>
    public static readonly IReadOnlyList<string> MenuKeyPrefixes =
    [
        "CombatMsg.Menu", "CombatMsg.Done", "CombatMsg.SpeedExit", "CombatMsg.Aim", "CombatMsg.YesNo", "CombatMsg.View",
        "CombatMsg.Items", "Msg.EncounterMenu", "Msg.ParleyAttitudes", "Msg.RestMenu", "Msg.Monsters", "Msg.Portraits",
        "Msg.Faster", "Msg.Slower", "Msg.SpeedExit", "PartyMsg.KeepExit", "PartyMsg.AddExit", "PartyMsg.PortraitMenu",
        "PartyMsg.Icon", "Menu.",
    ];

    /// <summary>START's member pick's line (00BA:2C41 "Select" + 00BA:2C19 " Exit"), original case, or null.</summary>
    public string? TargetMenu { get; init; }

    /// <summary>The same pick's line without Exit (00BA:2C41 "Select" alone: WHO passes the Exit flag 0), or null.</summary>
    public string? SelectMenu { get; init; }

    /// <summary>Spell name 1..56 (DS:2912 + 41·(id − 1)) and level heading 1..9 (DS:0A79 + 41·level), or null.</summary>
    public Func<int, string?> SpellName { get; init; } = _ => null;
    public Func<int, string?> LevelName { get; init; } = _ => null;

    /// <summary>
    /// The character page's name tables (View 0x21084, read s18): gender DS:0FEC + 7·k (record 0x9E), race DS:0F03 +
    /// 10·k (0x2E), alignment DS:0F53 + 17·k (0xA0), class DS:0D38 + 27·k (0x2F), status DS:1047 + 13·k (0x10C) and the
    /// money lines' coin names DS:0FFA + 11·k (0x21547). Null where not read (another version: our words).
    /// </summary>
    public Func<int, string?> Gender { get; init; } = _ => null;
    public Func<int, string?> Race { get; init; } = _ => null;
    public Func<int, string?> Alignment { get; init; } = _ => null;
    public Func<int, string?> ClassName { get; init; } = _ => null;
    public Func<int, string?> StatusName { get; init; } = _ => null;
    public Func<int, string?> SheetCoin { get; init; } = _ => null;

    /// <summary>The spell table (Core's reader), or null without the program.</summary>
    public SpellTable? Spells { get; init; }

    /// <summary>The training hall's and end game's texts, the hall's tables and its menu's shown-by-default flags.</summary>
    public HallText HallTexts { get; init; } = new(null);
    public TrainingTables? Training { get; init; }
    public bool[]? HallDefaults { get; init; }

    /// <summary>The base item slot of an item record (ITEMS table, field 0 of the item's type), or −1.</summary>
    public Func<byte[], int> ItemSlot { get; init; } = _ => -1;

    /// <summary>
    /// DS offsets of the camp's strings in v1.3 (the camp line is at DS:0508: the others are only trusted when the
    /// string there is the one the camp routine passes, so another version falls back to our words).
    /// </summary>
    private const int CampMenuDs = 0x508, MagicMenuDs = 0x531, AlterMenuDs = 0x55A, OrderMenuDs = 0x584,
        SpellNamesDs = 0x2912, LevelNamesDs = 0xA79,
        GenderDs = 0xFEC, RaceDs = 0xF03, AlignmentDs = 0xF53, ClassDs = 0xD38, StatusDs = 0x1047, SheetCoinDs = 0xFFA;

    private static List<string> MenuLinesOf(PorProgramData? program, Func<int, string?> ds, IPorContent dir)
    {
        var lines = new List<string?> { ds(CampMenuDs), ds(MagicMenuDs), ds(AlterMenuDs), ds(OrderMenuDs), ds(OrderMenuDs + 41) };
        if (program?.View?.ExploreCommands is { } commands)
            lines.Add(string.Join(' ', commands));
        if (program is not null)
            lines.AddRange(program.Texts.Where(kv => MenuKeyPrefixes.Any(p => kv.Key.StartsWith(p, StringComparison.Ordinal)))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value));
        for (int i = 0; program is not null && i < 11; i++)
            lines.Add(program.PascalString(PartyMenuDs + 42 * i, 40));
        // The Amiga's own words (not its prompts, nor the camp's whole lines, which the camp screens use as they are,
        // nor creation's developer names and lines).
        lines.AddRange(dir.AmigaStrings()?.Where(kv => !kv.Key.Contains("Prompt", StringComparison.Ordinal)
            && !kv.Key.StartsWith(GoldBox.Data.Module.Por.PorAmigaText.CampLinePrefix, StringComparison.Ordinal)
            && !kv.Key.StartsWith(GoldBox.Data.Module.Por.PorAmigaText.DeveloperKey, StringComparison.Ordinal)).Select(kv => kv.Value) ?? []);
        return [.. lines.OfType<string>()];
    }

    /// <summary>The party menu's entries (string[41]s from DS:0619, <see cref="PartyMenuFlow.MenuItem"/>).</summary>
    private const int PartyMenuDs = 0x619;

    /// <summary>Reads everything from the player's folder or a module (missing pieces stay at their fallbacks).</summary>
    public static ClientResources Load(IPorContent dir)
    {
        var program = dir.Program();
        var items = dir.ItemTypes();
        string? camp = program?.CampMenu();
        bool v13 = camp is not null && program!.PascalString(CampMenuDs, 40) == camp;
        string? Ds(int off) => v13 ? program!.PascalString(off, 40) : null;
        // The page indexes these with the record byte unchecked (any byte reads whatever lies there, as the program does).
        Func<int, string?> Table(int b, int stride) =>
            k => v13 && k is >= 0 and <= 255 ? program!.PascalString(b + stride * k, stride - 1) : null;
        return new ClientResources
        {
            Texts = new GameText(program),
            CombatTexts = new CombatText(program),
            ItemWord = program is null ? _ => null : program.ItemWord,
            IsScroll = items is null ? _ => false : t => items[t, BaseItemTable.Slot] is >= 11 and <= 13,
            ItemTypes = items,
            TempleNames = program?.TempleNames(),
            CampMenu = camp,
            MagicMenu = Ds(MagicMenuDs),
            AlterMenu = Ds(AlterMenuDs),
            OrderMenus = v13 && Ds(OrderMenuDs) is { } o1 && Ds(OrderMenuDs + 41) is { } o2 ? [o1, o2] : null,
            SpellName = id => id is >= 1 and <= 56 ? Ds(SpellNamesDs + 41 * (id - 1)) : null,
            LevelName = l => l is >= 1 and <= 9 ? Ds(LevelNamesDs + 41 * l) : null,
            Gender = Table(GenderDs, 7),
            Race = Table(RaceDs, 10),
            Alignment = Table(AlignmentDs, 17),
            ClassName = Table(ClassDs, 27),
            StatusName = Table(StatusDs, 13),
            SheetCoin = Table(SheetCoinDs, 11),
            Spells = program?.Spells(),
            HallTexts = new HallText(program),
            Training = PorTrainingTables.From(program),
            HallDefaults = program is null ? null
                : [.. Enum.GetValues<TrainingHall.Entry>().Select(e => PorTrainingTables.MenuDefault(program, e) ?? true)],
            ItemSlot = items is null ? _ => -1 : it => items[it[0x2E], BaseItemTable.Slot],
            PagePrompt = program?.PagePrompt,
            TargetMenu = program?.Text("Pick.Select") is { } sel && program.Text("Pick.Exit") is { } ex ? sel + ex : null,
            SelectMenu = program?.Text("Pick.Select"),
            MenuLines = MenuLinesOf(program, v13 ? Ds : _ => null, dir),
        };
    }
}
