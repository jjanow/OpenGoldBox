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

    /// <summary>Reads everything from the player's folder (missing pieces stay at their fallbacks).</summary>
    public static ClientResources Load(PorGameDirectory dir)
    {
        var program = PorProgram.Open(dir);
        BaseItemTable? items = null;
        if (dir.Find("ITEMS") is { } path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 2)
                items = BaseItemTable.FromItemsFile(bytes);
        }
        return new ClientResources
        {
            Texts = new GameText(program),
            ItemWord = program is null ? _ => null : program.ItemWord,
            IsScroll = items is null ? _ => false : t => items[t, BaseItemTable.Slot] is >= 11 and <= 13,
            TempleNames = program?.TempleNames(),
            CampMenu = program?.CampMenu(),
            PagePrompt = program?.PagePrompt(),
        };
    }
}
