namespace GoldBox.Core.Game;

/// <summary>
/// The coin row parser GAME.OVR 0x2581C (docs/re/shop-format.md "The coin row parser 0x2581C"). The money Trade
/// (0x23190), Drop (0x234E1) and Take (0x25974) build their coin lists from the DS:0FFA names (copper … jewellery,
/// Trade and Drop left-padded to the longest, then the amount) and hand the picked row's text to 0x2581C, which gives
/// back the coin kind and that kind's label for the amount question (between its two halves; the labels
/// are the overlay strings 0x257E3–0x25813, <see cref="LabelKey"/>).
/// </summary>
public static class CoinRows
{
    /// <summary>
    /// The kind (0 copper … 6 jewellery) of a row, as 0x2581C reads it: leading spaces skipped (0x2583C), then the
    /// first character as it is (no case change): 'G' is gems when the next character, upper-cased (Turbo Pascal
    /// UpCase, ASCII letters only), is 'E', else gold; 'P' platinum, 'E' electrum, 'S' silver, 'C' copper, 'J'
    /// jewellery. Any other character: -1, where the program returns its uninitialised local [bp-1] and leaves the
    /// label unset `[?]` (no caller can get there: every row starts with a DS:0FFA name). An empty or all-space row
    /// reads past the string in the program (left out here; -1).
    /// </summary>
    public static int KindOf(string row)
    {
        int i = 0;
        while (i < row.Length && row[i] == ' ')
            i++;
        if (i >= row.Length)
            return -1;
        return row[i] switch
        {
            'G' => i + 1 < row.Length && UpCase(row[i + 1]) == 'E' ? 5 : 3,
            'P' => 4,
            'E' => 2,
            'S' => 1,
            'C' => 0,
            'J' => 6,
            _ => -1,
        };
    }

    /// <summary>The text key of kind <paramref name="kind"/>'s label (the strings 0x2581C copies), or null.</summary>
    public static string? LabelKey(int kind) => kind switch
    {
        0 => "Msg.CoinCopper",
        1 => "Msg.CoinSilver",
        2 => "Msg.CoinElectrum",
        3 => "Msg.CoinGold",
        4 => "Msg.CoinPlatinum",
        5 => "Msg.CoinGems",
        6 => "Msg.CoinJewelry",
        _ => null,
    };

    private static char UpCase(char c) => c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
}
