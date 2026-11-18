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
    public Func<int, string?> ItemWord { get; init; } = _ => null;
    public Func<byte, bool> IsScroll { get; init; } = _ => false;
    public IReadOnlyList<string>? TempleNames { get; init; }
    public string? CampMenu { get; init; }
    public string? PagePrompt { get; init; }

    /// <summary>The camp's Magic and Alter menu lines and the party order lines (Select / Place), original case.</summary>
    public string? MagicMenu { get; init; }
    public string? AlterMenu { get; init; }
    public IReadOnlyList<string>? OrderMenus { get; init; }

    /// <summary>Spell name 1..56 (DS:2912 + 41·(id − 1)) and level heading 1..9 (DS:0A79 + 41·level), or null.</summary>
    public Func<int, string?> SpellName { get; init; } = _ => null;
    public Func<int, string?> LevelName { get; init; } = _ => null;

    /// <summary>The spell table (Core's reader), or null without the program.</summary>
    public SpellTable? Spells { get; init; }

    /// <summary>The base item slot of an item record (ITEMS table, field 0 of the item's type), or −1.</summary>
    public Func<byte[], int> ItemSlot { get; init; } = _ => -1;

    /// <summary>
    /// DS offsets of the camp's strings in v1.3 (the camp line is at DS:0508: the others are only trusted when the
    /// string there is the one the camp routine passes, so another version falls back to our words).
    /// </summary>
    private const int CampMenuDs = 0x508, MagicMenuDs = 0x531, AlterMenuDs = 0x55A, OrderMenuDs = 0x584,
        SpellNamesDs = 0x2912, LevelNamesDs = 0xA79;

    /// <summary>Reads everything from the player's folder or a module (missing pieces stay at their fallbacks).</summary>
    public static ClientResources Load(IPorContent dir)
    {
        var program = dir.Program();
        var items = dir.ItemTypes();
        string? camp = program?.CampMenu();
        bool v13 = camp is not null && program!.PascalString(CampMenuDs, 40) == camp;
        string? Ds(int off) => v13 ? program!.PascalString(off, 40) : null;
        return new ClientResources
        {
            Texts = new GameText(program),
            ItemWord = program is null ? _ => null : program.ItemWord,
            IsScroll = items is null ? _ => false : t => items[t, BaseItemTable.Slot] is >= 11 and <= 13,
            TempleNames = program?.TempleNames(),
            CampMenu = camp,
            MagicMenu = Ds(MagicMenuDs),
            AlterMenu = Ds(AlterMenuDs),
            OrderMenus = v13 && Ds(OrderMenuDs) is { } o1 && Ds(OrderMenuDs + 41) is { } o2 ? [o1, o2] : null,
            SpellName = id => id is >= 1 and <= 56 ? Ds(SpellNamesDs + 41 * (id - 1)) : null,
            LevelName = l => l is >= 1 and <= 9 ? Ds(LevelNamesDs + 41 * l) : null,
            Spells = program?.Spells(),
            ItemSlot = items is null ? _ => -1 : it => items[it[0x2E], BaseItemTable.Slot],
            PagePrompt = program?.PagePrompt,
        };
    }
}
