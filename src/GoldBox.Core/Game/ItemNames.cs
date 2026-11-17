using System.Globalization;
using System.Text;

namespace GoldBox.Core.Game;

/// <summary>
/// The item-name routine (START.EXE 0x0FE2; docs/re/item-format.md "Name composition"), without its "readied"
/// prefix (the caller draws that column). The words come from the program's word table (<c>PorProgram.ItemWord</c>).
/// The stored name in the record is a cache and is never shown.
/// </summary>
public static class ItemNames
{
    private const int Type = 0x2E, Plus = 0x32, SaveBonus = 0x33, Hidden = 0x35, Cursed = 0x36, Count = 0x39;

    /// <summary>
    /// The name the game shows for <paramref name="item"/>. <paramref name="detect"/>: a party member has effect 5
    /// (detect magic), which adds "* " before a magical item (plus or save bonus &gt; 0, or cursed). A missing word
    /// shows as "#id". The result ends in a space as the program's does, and is cut to 40 characters.
    /// </summary>
    public static string Compose(byte[] item, Func<int, string?> word, bool detect = false)
    {
        var s = new StringBuilder();
        if (detect && ((sbyte)item[Plus] > 0 || (sbyte)item[SaveBonus] > 0 || item[Cursed] != 0))
            s.Append("* ");
        int count = item[Count];
        if (count > 0)
            s.Append(count.ToString(CultureInfo.InvariantCulture)).Append(' ');
        int vis = 0;
        for (int k = 1; k <= 3; k++)
            if (item[Type + k] != 0 && (item[Hidden] >> (3 - k) & 1) == 0)
                vis |= 1 << (k - 1);
        bool done = false;
        byte type = item[Type];
        for (int k = 3; k >= 1; k--)
        {
            if ((vis >> (k - 1) & 1) == 0)
                continue;
            int w = item[Type + k];
            s.Append(word(w) ?? "#" + w.ToString(CultureInfo.InvariantCulture));
            bool plural = count >= 2 && !done && (
                1 << (k - 1) == vis
                || (k == 1 && vis > 4 && type != 0x56)
                || (k == 2 && (vis & 1) == 0)
                || (k == 3 && type == 0x56)
                || (type is 0x49 or 0x1C && item[0x31] != 0xB1));
            if (plural)
            {
                s.Append("s ");
                done = true;
            }
            else
                s.Append(' ');
        }
        return s.Length > 40 ? s.ToString(0, 40) : s.ToString();
    }
}
