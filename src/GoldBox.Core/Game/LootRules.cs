using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The loot menu's rules (GAME.OVR 0x644B, DS:49F3 = 6; docs/re/combat-format.md "Loot menu"), read whole s51 and run
/// under emulation (<c>tools/scripts/por_venue_oracle.py</c> "loot", test <c>VenueProgramTests</c>). Every pass of
/// the loop (0x6465) asks 0x25C07 for the money flag (any pool word DS:67F4 + 4k, k = 1..7, non-zero) and the items
/// flag (the treasure list DS:6810 not nil), looks for the detect spell on the current character and builds the
/// line; nothing redraws the screen after the entry's START 0x3379 (0x645C).
/// </summary>
public static class LootRules
{
    /// <summary>The spell ids the loot menu's D casts: Detect Magic, cleric (5) and magic-user (11).</summary>
    public const int ClericDetect = 5, MageDetect = 11;

    private const int Memorised = 0x17, Slots = 21;

    /// <summary>
    /// 0x64A6–0x64FD: only when there are items, the memorised bytes 0x17 + k (k = 0..20) of the current character
    /// DS:5D92 are walked until the first one equal to 5 or 11 (a spell still to be memorised, id + 0x80, does not
    /// count); its id is the spell D casts ([bp−0x10B]). 0 = none: the line has no Detect and D is no hotkey.
    /// </summary>
    public static int DetectSpell(EclCharacter? current, bool items)
    {
        if (!items || current is null)
            return 0;
        for (int k = 0; k < Slots; k++)
            if (current[Memorised + k] is ClericDetect or MageDetect)
                return current[Memorised + k];
        return 0;
    }

    /// <summary>
    /// The item line (0x6474–0x657C, the program's words): with money "View Take Pool Share", else with items "View Take
    /// Pool", else "View Pool"; then " Detect Exit" when <paramref name="detect"/>, else " Exit".
    /// </summary>
    public static IReadOnlyList<string> MenuWords(bool money, bool items, bool detect)
    {
        var w = new List<string>();
        if (money)
            w.AddRange(["View", "Take", "Pool", "Share"]);
        else if (items)
            w.AddRange(["View", "Take", "Pool"]);
        else
            w.AddRange(["View", "Pool"]);
        if (detect)
            w.Add("Detect");
        w.Add("Exit");
        return w;
    }

    /// <summary>
    /// What an extended key does in the loot menu. The program: the menu routine runs with the keypad flag, so it
    /// returns any zero-prefixed key's scan code, and the loop (0x65A6–0x6717) compares that byte with its letters
    /// without looking at the extended cell (only P does, 0x65D5) nor at the line: Shift-F3 (0x56) = V, View; Shift-F1
    /// (0x54) = T, Take (0x62B6, whatever the line: with neither money nor items it runs the item take 0x6184 on the
    /// empty list); Del (0x53) = S, Share (0x252C3, with or without money); F10 (0x44) = D, the cast with the spell byte
    /// [bp−0x10B], which is only written when a detect spell is found (with none found in this menu an uninitialised
    /// stack byte, else the id last found: an original bug); Home / End (0x47 / 0x4F) = G / O (0x23819, the party
    /// selection: the caller's member keys); Down (0x50) = P is refused by the cell test. <b>The engine's rule (user,
    /// s51):</b> Del, Shift-F1 and Shift-F3 act as Share, Take and View, as the program; F10 casts only when Detect is
    /// on the line (<paramref name="detectOnLine"/>), with the spell found on this pass, else nothing. Returns the word
    /// ("View", "Take", "Share", "Detect") or null for nothing.
    /// </summary>
    public static string? ExtendedKey(byte scan, bool detectOnLine) => scan switch
    {
        0x56 => "View",
        0x54 => "Take",
        0x53 => "Share",
        0x44 => detectOnLine ? "Detect" : null,
        _ => null,
    };

    /// <summary>
    /// D (0x65F0–0x6605): the cast 0x27C38(spell, ai 0, announce 0, var done) with the spell found by
    /// <see cref="DetectSpell"/>, done being the menu's extended cell [bp−0x106] (0 for a letter; nothing reads it
    /// after). Detect Magic's target mode is 1 (the caster), so no pick, no question and no combat-only branch: the
    /// caster's invisibility goes, the memorised copy is removed, and the spell's routine 0x28101 adds effect 5 (the
    /// naming routine's "* " marker on magical items) through 0x2C540 with its message. The loop then runs again
    /// (no redraw). Returns the cast's outcome.
    /// </summary>
    public static CampCast.Outcome Detect(CampCast cast, int spell, CampCast.Hooks? hooks = null)
    {
        bool done = false;
        return cast.Run(spell, announce: false, hooks ?? new CampCast.Hooks(), ref done);
    }
}
