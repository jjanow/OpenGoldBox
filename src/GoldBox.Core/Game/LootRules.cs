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

    /// <summary>The steps Take (0x62B6) calls; the caller answers the menu and carries them out.</summary>
    public sealed class TakeSteps
    {
        /// <summary>0x25C07: (any pool word non-zero, the treasure list not empty).</summary>
        public required Func<(bool Money, bool Items)> Flags { get; init; }

        /// <summary>The "Take:" menu (0x630A: "Money Items Exit", colours 13 / 10 / 15, keypad flag 1): the key byte the
        /// menu routine 0x2CC8C returns (a hotkey letter, 0 for Escape, an extended key's scan code).</summary>
        public required Func<byte> Menu { get; init; }

        /// <summary>The coin take 0x25974.</summary>
        public required Action Coins { get; init; }

        /// <summary>The item take 0x6184 (<see cref="TakeItems{T}"/>).</summary>
        public required Action Items { get; init; }

        /// <summary>The party selection 0x23819 with the key (G / O = Home / End scan codes 0x47 / 0x4F).</summary>
        public required Action<byte> Select { get; init; }

        /// <summary>START 0x3379: the screen redrawn for DS:49F3 (6: the loot screen, text window empty).</summary>
        public required Action Redraw { get; init; }

        /// <summary>START 0x1307 (00BA:0767): the party list drawn again.</summary>
        public required Action PartyList { get; init; }
    }

    /// <summary>
    /// Take (0x62B6, read whole and run under emulation s52: <c>por_venue_oracle.py</c> "lootTake"). No money
    /// (0x62C3): the item take 0x6184, whatever the items flag (with neither money nor items, which only the s51
    /// extended key Shift-F1 reaches, it runs on the empty list: <see cref="TakeItems{T}"/>). Money but no items
    /// (0x62CF): the coin take 0x25974, then START 0x3379. Both: the "Take:" menu until done: M → the coin take and
    /// START 0x3379; I → the item take; E or 0 (Escape) → done; G / O (Home / End: 0x47 / 0x4F, the letters are no
    /// hotkeys of the line) → 0x23819; any other key nothing. After every key the party list is drawn (START 0x1307),
    /// 0x25C07 is asked again and Take ends as soon as either flag is clear (0x6370–0x6387), back to the loot menu.
    /// The menu routine's other extended keys compare as their scan codes: Right (0x4D) = M, PgUp (0x49) = I.
    /// </summary>
    public static void Take(TakeSteps s)
    {
        var (money, items) = s.Flags();
        if (!money)
        {
            s.Items();
            return;
        }
        if (!items)
        {
            s.Coins();
            s.Redraw();
            return;
        }
        while (true)
        {
            byte key = s.Menu();
            bool done = false;
            switch (key)
            {
                case (byte)'M':
                    s.Coins();
                    s.Redraw();
                    break;
                case (byte)'I':
                    s.Items();
                    break;
                case (byte)'E' or 0:
                    done = true;
                    break;
                case (byte)'G' or (byte)'O':
                    s.Select(key);
                    break;
            }
            s.PartyList();
            (money, items) = s.Flags();
            if (done || !money || !items)
                return;
        }
    }

    /// <summary>
    /// The item take (0x6184, read whole and run under emulation s52). The position word starts at 0 (0x619F); each
    /// pass the page 0x6092 draws the window (1, 1)–(38, 22) titled "Loot", names every item and runs the list routine
    /// 0x2D669 ("Items: " / "Take", Exit) from the list head DS:6810. On the <b>empty list</b> 0x2D669 returns at once
    /// (0x2D69C: position 0, no item, key 0; no menu drawn, no key waited for, DS:6B2B untouched), so the take ends
    /// at once: the window shows for no more than the redraw takes. <paramref name="page"/> answers a non-empty list
    /// with the key and the row (its position). T or Return (0x61BC): the copy step 0x6F0D (<paramref name="give"/>,
    /// false = refused with its message, the page again); taken: the item is unlinked and freed (0x61EC–0x627A), and
    /// the take ends when the list is then empty. Any other key ends it. At the end START 0x3379 (<paramref
    /// name="redraw"/>).
    /// </summary>
    public static void TakeItems<T>(IList<T> treasure, Func<int, (byte Key, int Row)> page, Func<T, bool> give,
        Action redraw)
    {
        int position = 0;
        while (true)
        {
            byte key;
            int row;
            if (treasure.Count == 0)
                (key, row, position) = (0, -1, 0);
            else
                (key, row) = page(position);
            if (key is not ((byte)'T' or 13) || row < 0 || row >= treasure.Count)
                break;
            position = row;
            if (!give(treasure[row]))
                continue;
            treasure.RemoveAt(row);
            if (treasure.Count == 0)
                break;
        }
        redraw();
    }

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
