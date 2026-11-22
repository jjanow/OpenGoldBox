using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// The Amiga program's own View page (hunk 10 +0x48E0, the copy of DOS 0x21084) and View menu (hunk 10 +0x52E8, the copy
/// of 0x21AED), used under the Amiga art only (read s41; docs/re/por-art-formats.md §4 "View page"). Best and Original
/// DOS keep the DOS page. What differs from DOS:
/// <list type="bullet">
/// <item>the name at row 2 (hunk 13 +0x4AD4 called with column 1, row 2, hunk 10 +0x4918–0x4928); the NPC tag stays at
/// (name length + 3, 1) (hunk 10 +0x4948–0x4958), one row above the name `[?]` (not seen: no NPC in FS-UAE yet);</item>
/// <item>the hit points through the party list's routine (hunk 13 +0x3DD4, called at hunk 10 +0x4F44 with flag 0):
/// colour 8 below the maximum, else 10 (pens 24 / 26), not DOS's yellow / green;</item>
/// <item>the portrait window (hunk 19 +0x14C4, called at the page's end, hunk 10 +0x4DA2) whenever the portraits flag
/// (hunk 32 +0xBC, the Alter menu's "Portraits on / off", hunk 1 +0x4ECA) is set and the head and body bytes are not 0:
/// no disk test (DOS: also the disk byte 3, 0x21530); the window (28, 1)–(38, 11), the head at column 30 row 1, the
/// body at column 28 row 6 (hunk 19 +0x113C / +0x1176), picture table 2 (hunk 22 +0x172, hunk 10 +0x4900);</item>
/// <item>Rename (<see cref="FightItemScreens.WithAmigaRename"/>): "NEW NAME FOR " and the name in the input requester
/// (hunk 25 +0xEC6, at most 16 bytes with the end mark: 15 characters), copied into the name as typed, an empty
/// answer included (FS-UAE s41: the name becomes empty); R is not one of the keys that draw the page again
/// (hunk 10 +0x5540–0x5550: I, T, S), so the page keeps showing the old name (FS-UAE s41).</item>
/// </list>
/// </summary>
public static class AmigaView
{
    /// <summary>Whether the Amiga pages are drawn (the Client's Amiga art with the module's Amiga frames); set by the
    /// Client with the art setting, false in tests.</summary>
    public static bool On { get; set; }

    public const int NameRow = 2, NpcTagRow = 1;
    /// <summary>The requester's MaxChars for Rename (hunk 10 +0x551A: 16, the end mark included).</summary>
    public const int RenameLength = 15;
    /// <summary>The portrait window's frame cells (the window (28, 1)–(38, 11), hunk 19 +0x1560–0x157C).</summary>
    public const int PortraitCol0 = 27, PortraitRow0 = 0, PortraitCol1 = 39, PortraitRow1 = 12;

    /// <summary>The Amiga's head picture per head choice 1–14 (hunk 31 +0x1A4F; the same as DOS's DS:286F).</summary>
    private static readonly byte[] HeadIds = [0x00, 0x08, 0x09, 0x0D, 0x10, 0x12, 0x16, 0x22, 0x2D, 0x33, 0x35, 0x39, 0x43, 0x44];

    /// <summary>
    /// The portrait the Amiga View page draws for <paramref name="c"/> (head and body pictures), or null: only with the
    /// Amiga pages on, the portraits flag set and head (record 0xBB, Amiga 0xBD) and body (0xBC, Amiga 0xBE) not 0
    /// (hunk 19 +0x14D2–0x14E8). For pages without the program's tables (the fight's View).
    /// </summary>
    public static (int Head, int Body)? Portrait(EclCharacter c, bool portraitsFlag)
    {
        int head = c[CharacterCreation.PortraitHead], body = c[CharacterCreation.PortraitBody];
        if (!On || !portraitsFlag || head is < 1 or > 14 || body is < 1 or > 12)
            return null;
        return (HeadIds[head - 1], BodyIds[body - 1]);
    }

    /// <summary>A page's Amiga additions (<see cref="CombatPage.AmigaTexts"/>, <see cref="CombatPage.Portrait"/>) over
    /// the page <see cref="GoldBox.Client.Screens.PageChrome.DrawBody"/> drew.</summary>
    public static void DrawExtras(IndexedFrame f, TextFont font, CombatPage p, ViewPictures? pictures)
    {
        foreach (var (col, row, text, colour) in p.AmigaTexts)
            if (!font.DrawAmiga(f, col * 8, row * 8, text, colour))
                GoldBox.Client.Screens.Chrome.Text(f, font, col, row, text, colour == 8 ? (byte)14 : (byte)10);
        if (p.Portrait is not { } portrait)
            return;
        GoldBox.Client.Screens.Chrome.Box(f, PortraitCol0, PortraitRow0, PortraitCol1, PortraitRow1);
        pictures?.Draw(f, (PortraitCol0 + 1) * 8, (PortraitRow0 + 1) * 8,
            new ViewPicture(ViewPictureKind.Portrait, portrait.Body, portrait.Head, 0, PortraitDisk));
    }

    /// <summary>The disk digit of the portrait files (HEAD3 / BODY3; the Amiga has one HEAD and one BODY file).</summary>
    public const int PortraitDisk = 3;

    /// <summary>The Amiga's body picture per body choice 1–12 (hunk 31 +0x1A5D): DOS's (DS:287D) but choice 8 → 5,
    /// not 0x18. The head table (hunk 31 +0x1A4F) is DOS's.</summary>
    private static readonly byte[] BodyIds = [0x01, 0x02, 0x03, 0x04, 0x07, 0x08, 0x12, 0x05, 0x1A, 0x21, 0x23, 0x25];

    /// <summary>The body picture the Amiga draws for body choice <paramref name="body"/>, or <paramref name="dosId"/>.</summary>
    public static int BodyId(int body, int dosId) => On && body is >= 1 and <= 12 ? BodyIds[body - 1] : dosId;

    /// <summary>The View menu's entries: under the Amiga pages with Rename (<see cref="FightItemScreens.WithAmigaRename"/>).</summary>
    public static IReadOnlyList<ViewEntry> Entries(IReadOnlyList<ViewEntry> entries, EclCharacter c) =>
        On ? FightItemScreens.WithAmigaRename(entries, c) : entries;

    /// <summary>
    /// A text as the page draws it: DOS upper-cases every character (glyph UpCase(c) mod 64), the Amiga draws the byte as
    /// it is, and its font's lower-case e, g, h, j, m, n and o differ from the capitals (FS-UAE s41: "Level", "Weapon",
    /// "Encumbrance" on the View page). The program's texts are mixed case in both versions.
    /// </summary>
    public static string Case(string s) => On ? s : s.ToUpperInvariant();

    /// <summary>Pens single Amiga callers pass (<see cref="GoldBox.Data.Module.Por.PorAmigaText.Calls"/>), from the
    /// module; empty without them (then the Client keeps the DOS colours' pens).</summary>
    public static IReadOnlyDictionary<string, int> CallPens { get; set; } = new Dictionary<string, int>();

    /// <summary>Texts the Amiga words otherwise than DOS (<see cref="GoldBox.Data.Module.Por.PorAmigaText.Calls"/>), from
    /// the module; empty without them.</summary>
    public static IReadOnlyDictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();

    /// <summary>A camp line the Amiga words otherwise than DOS (<see cref="GoldBox.Data.Module.Por.PorAmigaText.RestMenuKey"/>,
    /// …), under the Amiga pages only; null otherwise or when the module has none.</summary>
    public static string? CampLine(string key) => On && Strings.TryGetValue(key, out var s) ? s : null;

    /// <summary>
    /// The Amiga's Pics line for the two flags (hunk 1 +0x4E94–0x4F36: the monsters word, the portraits word, the exit
    /// word, joined as the program's strcpy / strcat do), or null.
    /// </summary>
    public static string? PicsLine(bool monsters, bool portraits) =>
        CampLine(monsters ? GoldBox.Data.Module.Por.PorAmigaText.MonstersOnKey : GoldBox.Data.Module.Por.PorAmigaText.MonstersOffKey) is { } m
        && CampLine(portraits ? GoldBox.Data.Module.Por.PorAmigaText.PortraitsOnKey : GoldBox.Data.Module.Por.PorAmigaText.PortraitsOffKey) is { } p
        && CampLine(GoldBox.Data.Module.Por.PorAmigaText.PicsExitKey) is { } e
            ? m + p + e : null;

    /// <summary>
    /// An Amiga menu line as its menu routine splits it (hunk 19 +0x4F88–0x5170: entries end at a space or '_'; the key
    /// test +0x57D0 and a gadget's answer +0x573E take each entry's first capital or digit): the entries in the
    /// program's case, the 0xFF that joins two words of one entry kept (the font's 0xFF is blank, and the entry's
    /// gadget runs over it: FS-UAE s44, "Monsters on" pressed as one), and each entry's hotkey.
    /// </summary>
    public static (List<string> Items, string Hotkeys) ParseLine(string line)
    {
        var items = new List<string>();
        var keys = new System.Text.StringBuilder();
        foreach (var w in line.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            items.Add(w);
            int k = w.AsSpan().IndexOfAny("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
            keys.Append(k >= 0 ? w[k] : char.ToUpperInvariant(w[0]));
        }
        return (items, keys.ToString());
    }

    /// <summary>The View menu's Rename word as the Amiga program holds it (hunk 11 +0x840, trimmed), or null.</summary>
    public static string? RenameWord =>
        Strings.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.ViewRenameKey, out var s) ? Case(s.Trim()) : null;

    /// <summary>HMENU's pens under the Amiga pages: the '~' helper's own (hunk 4 +0x195A–0x1962: words 28, capitals 16;
    /// FS-UAE s44), or null.</summary>
    public static (int Words, int Capitals)? HmenuPens =>
        On && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.HmenuWordsPen, out int words)
            && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.HmenuCapitalsPen, out int capitals)
            ? (words, capitals) : null;

    /// <summary>
    /// The pause of the Amiga's timed messages (hunk 25 +0xD7A: Delay(speed byte · 5) in AmigaDOS ticks of 1/50 s, so
    /// 100 ms per game-speed step; none at speed 0). The camp rest's heal, interruption and memorise / scribe lines
    /// wait so on the Amiga (hunk 1 +0x320E, +0x3668; hunk 22 +0x5060), where DOS waits for a key.
    /// </summary>
    public static TimeSpan TimedPause(int speed) => TimeSpan.FromMilliseconds(100 * speed);

    /// <summary>
    /// The game-speed pause of a timed message: DOS's PauseOnly (START 0x7E4E: speed × 225 ms) or, under the Amiga
    /// pages, <see cref="TimedPause"/> (read s47): every Amiga copy read waits Delay(speed · 5 ticks) at hunk 25 +0xD7A:
    /// the timed row-24 messages (hunk 25 +0xE10, DOS 0x7DF8: "Not Here", the river, the hall's refusals), the green
    /// row-24 message (hunk 13 +0x4928, DOS START 0x1693 / 0x2233: View's "Must be unreadied", "Overloaded", "Sold!"
    /// …), View's Sell / Id pauses (hunk 10 +0x6096, +0x64A0) and the creature message routine's pause (hunk 13
    /// +0x4A7C, DOS START 0x16EC). Speed 0 (or above 9) waits nothing.
    /// </summary>
    public static TimeSpan MessagePause(int speed) => MessagePause(speed, On);

    /// <summary><see cref="MessagePause(int)"/> with the switch given.</summary>
    public static TimeSpan MessagePause(int speed, bool amiga) =>
        speed is <= 0 or > 9 ? TimeSpan.Zero : amiga ? TimedPause(speed) : TimeSpan.FromMilliseconds(225 * speed);

    /// <summary>
    /// A timed row-24 message's Amiga pen for its DOS colour, or null (the colour's own pen): DOS 14 is the caller's
    /// pen 24 (red; <see cref="GoldBox.Data.Module.Por.PorAmigaText.TimedWarnPen"/>, read s47), not the text pens' 18.
    /// </summary>
    public static int? TimedPen(byte dosColour) =>
        dosColour == 14 && CallPen(GoldBox.Data.Module.Por.PorAmigaText.TimedWarnPen) is int pen ? pen : null;

    /// <summary>The loot's item take prompt under the Amiga pages ("Items:", no space; read from the program at import:
    /// <see cref="GoldBox.Data.Module.Por.PorAmigaText.LootItemsPromptKey"/>), or null (DOS's "Items: ").</summary>
    public static string? LootItemsPrompt =>
        On && Strings.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.LootItemsPromptKey, out var s) ? s : null;

    /// <summary>An Amiga caller's pen read from the program (<see cref="CallPens"/>) under the Amiga pages, or null.</summary>
    public static int? CallPen(string key) => On && CallPens.TryGetValue(key, out int pen) ? pen : null;

    /// <summary>
    /// The yes/no question's prompt colour (DOS 0x2D9A0 takes it from its caller: 13 or 14): under the Amiga pages DOS's
    /// 14 is the Amiga caller's pen 24 (<see cref="GoldBox.Data.Module.Por.PorAmigaText.YesNoWarnPen"/>, red; read s46),
    /// not the yellow 18 the text pens give 14 elsewhere; any other colour as it is.
    /// </summary>
    public static byte YesNoPrompt(byte dosColour) =>
        dosColour == 14 && CallPen(GoldBox.Data.Module.Por.PorAmigaText.YesNoWarnPen) is int pen and >= 16 and <= 30
            ? (byte)pen : dosColour;

    /// <summary>The yes/no questions with their letters' pens swapped (pay for cure, Lose it?, Use it?: words 16,
    /// capitals 28, read s46) under the Amiga pages, or null.</summary>
    public static (int Words, int Capitals)? YesNoSwappedPens =>
        On && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.YesNoSwappedWordsPen, out int words)
            && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.YesNoSwappedCapitalsPen, out int capitals)
            ? (words, capitals) : null;

    /// <summary>Creation's portrait menu's pens under the Amiga pages (words 16, capitals 28: hunk 10 +0x3778–0x3790),
    /// or null (the usual menu pens).</summary>
    internal static GoldBox.Client.Screens.ExploreScreen.AmigaMenuPens? PortraitMenuPens =>
        On && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.PortraitWordsPen, out int words)
            && CallPens.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.PortraitCapitalsPen, out int capitals)
            ? new(words, capitals) : null;

    /// <summary>
    /// How the program spells each menu word (upper-case key → the program's spelling), learnt from its menu lines
    /// (<see cref="ClientResources.MenuLines"/>; the Amiga's lines are the DOS lines byte for byte, s43). Set by the
    /// Client with the resources; empty in tests.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MenuSpelling { get; private set; } = new Dictionary<string, string>();

    /// <summary>Learns the spelling of every word of <paramref name="lines"/> (the first spelling of a word wins; '~'
    /// hotkey marks dropped; words that are upper case already are skipped).</summary>
    public static void LearnMenuLines(IEnumerable<string?> lines) => MenuSpelling = Spelling(lines);

    /// <summary>The spelling table <see cref="LearnMenuLines"/> keeps (upper-case word → the program's spelling).</summary>
    public static Dictionary<string, string> Spelling(IEnumerable<string?> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
            foreach (var raw in (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw.Contains('~') ? TildeCase(raw) : raw;
                if (w.Length > 0 && w != w.ToUpperInvariant())
                    d.TryAdd(w.ToUpperInvariant(), w);
            }
        return d;
    }

    /// <summary>
    /// A '~' menu line as the menu's set-up passes it on (DOS 0x8A40, shop-format.md; the Amiga's copy hunk 4
    /// +0x17A0–0x1830): every capital lower-cased, each '~' deleted and the character after it upper-cased, so a
    /// choice keeps one capital, its hotkey ("~COMBAT ~WAIT" → "Combat Wait"; HMENU builds its line so).
    /// </summary>
    public static string TildeCase(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        bool up = false;
        foreach (char ch in line)
        {
            if (ch == '~')
            {
                up = true;
                continue;
            }
            char c = ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch;
            sb.Append(up && c is >= 'a' and <= 'z' ? (char)(c - 32) : c);
            up = false;
        }
        return sb.ToString();
    }

    /// <summary>An HMENU choice as the Amiga draws it: "~" + the choice through <see cref="TildeCase"/> (the area
    /// script's words are upper case); unchanged off the Amiga pages.</summary>
    public static string HmenuCase(string choice) => On ? TildeCase("~" + choice) : choice;

    /// <summary>
    /// A menu entry as the Amiga draws it: the program's own spelling of each word (<see cref="MenuSpelling"/>) under
    /// the Amiga pages, which draw the bytes as they are (FS-UAE s43: the explore command line, the View and Items
    /// pages' menus, all in their stored mixed case); unchanged otherwise, and for a word
    /// the program's menu lines do not hold (script menus are upper case on both machines).
    /// </summary>
    public static string MenuCase(string entry) => On ? MenuCase(entry, MenuSpelling) : entry;

    /// <summary><see cref="MenuCase(string)"/> with a given spelling table (the whole entry first, else word by word).</summary>
    public static string MenuCase(string entry, IReadOnlyDictionary<string, string> spelling)
    {
        if (spelling.Count == 0 || entry.Length == 0)
            return entry;
        if (spelling.TryGetValue(entry, out var whole))
            return whole;
        var words = entry.Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (spelling.TryGetValue(words[i], out var w))
                words[i] = w;
        return string.Join(' ', words);
    }

    /// <summary>The entries of a program menu as the Amiga draws them (<see cref="MenuCase(string)"/>).</summary>
    public static IReadOnlyList<string> MenuCase(IReadOnlyList<string> entries) =>
        On && MenuSpelling.Count > 0 ? [.. entries.Select(MenuCase)] : entries;

    /// <summary>
    /// The Amiga menu routine's colour test (hunk 19 +0x4FF6–0x5014): a capital letter or a digit takes the hotkey
    /// pen (d5), every other character the words' pen (d6), letter by letter. DOS's 0x2CC8C upper-cases the text and
    /// lights the entry's hotkey only.
    /// </summary>
    public static bool IsHotkeyCharacter(char c) => c is >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>
    /// The column of a key wait's line on row 24: DOS's 0x7C85 writes at column 0; the Amiga's copy (hunk 25 +0xE78:
    /// 39 blanks from column 1, the line through the string routine at (1, 24), then the key) at column 1. Every key
    /// wait found goes through it (the party's defeat "Press any key…", hunk 19 +0x103C; the ECL's "press
    /// &lt;enter&gt;…", hunk 16 +0x46DC and hunk 4 +0x236E; …); the timed row-24 messages go through hunk 25 +0xE10 at
    /// column 0 ("Not Here", the hall's refusals …, drawn, Delay(speed · 5 ticks), cleared). A menu-style key line
    /// (the menu routine) keeps column 0.
    /// </summary>
    public static int KeyColumn(KeyRequest k) => On && !k.AsMenu ? 1 : 0;

    /// <summary>
    /// A menu-style key line's colour under the Amiga pages: the press-enter lines after the fight summary and after the
    /// thief's share call the menu routine with pens 28 / 28 / 28 (hunk 19 +0x78E–0x7AA, +0xF28–0xF46; DOS 0x6071: every
    /// colour 15, white), so green (DOS 10's pen 28); null otherwise.
    /// </summary>
    public static byte? KeyColour(KeyRequest k) => On && k.AsMenu ? (byte)10 : null;

    /// <summary>
    /// A key line's text under the Amiga pages (keyboard), or null: the text window's page prompt is the Amiga's own
    /// (hunk 25 +0x1200: its own upper-case string, hunk 26 +0x176; DOS: START's longer one); other prompts keep
    /// the program's case (<see cref="KeyRequest.Original"/>).
    /// </summary>
    public static string? KeyText(KeyRequest k) => KeyText(k, On, Strings);

    /// <summary><see cref="KeyText(KeyRequest)"/> with the switch and the strings given.</summary>
    public static string? KeyText(KeyRequest k, bool on, IReadOnlyDictionary<string, string> strings) =>
        !on ? null
        : k.PagePrompt && k.Prompt is null && strings.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.PagePromptKey, out var page) ? page
        : k.Original;

    /// <summary>
    /// A key wait's own Amiga pen, or null (the DOS colour's pen): the page prompt's (hunk 25 +0x1206: 18, yellow; DOS 13)
    /// and DAMAGE's press-enter lines (hunk 16 +0x46D8, hunk 4 +0x236A: 28, green; DOS 15), read from the program
    /// (<see cref="CallPens"/>). Row 24's key wait (hunk 25 +0xE78) draws through the string routine in that pen.
    /// </summary>
    public static int? KeyPen(KeyRequest k) => KeyPen(k, On, CallPens);

    /// <summary><see cref="KeyPen(KeyRequest)"/> with the switch and the pens given.</summary>
    public static int? KeyPen(KeyRequest k, bool on, IReadOnlyDictionary<string, int> pens)
    {
        if (!on || k.AsMenu)
            return null;
        string? key = k.PagePrompt && k.Prompt is null ? GoldBox.Data.Module.Por.PorAmigaText.KeyPagePen : k.AmigaPen;
        return key is not null && pens.TryGetValue(key, out int pen) ? pen : null;
    }

    /// <summary>
    /// The area script's line inputs under the Amiga: INPUTSTR (hunk 16 +0x2448–0x248A) asks the input requester with an
    /// empty prompt and MaxChars 40 (the end mark included: 39 characters; DOS 40), INPUTNUM (hunk 16 +0x23E8–0x2428,
    /// the number input +0x1092) with an empty prompt and MaxChars 6 (5 digits). DOS: our "NUMBER:" stand-in and 6.
    /// </summary>
    public const int InputStringLength = 39, InputNumberLength = 5;

    /// <summary>
    /// A number input's answer (<paramref name="request"/>, numeric) as the program reads it. DOS (START 0x7BF6, the
    /// money input GAME.OVR 0x24F1D): the line once, empty or Escape = 0. The Amiga's number input (hunk 25 +0x1092,
    /// read s47: INPUTNUM hunk 16 +0x2428, the money input hunk 19 +0x3306 for View's Drop and Trade, hunk 10 +0x66E2
    /// / +0x6950, and Take's coins, hunk 19 +0x3BF8) copies the requester's line and parses it with stcd_i (hunk 0
    /// +0x894: an optional sign, then digits), and asks again while that took no character: an empty line (the
    /// requester has no Escape) is never an answer. Under the Amiga pages an empty answer is asked again; null (no
    /// answer) as it is.
    /// </summary>
    public static string? Number(Func<PlayerRequest, object?> ask, TextRequest request) => Number(ask, request, On);

    /// <summary><see cref="Number(Func{PlayerRequest, object?}, TextRequest)"/> with the switch given.</summary>
    public static string? Number(Func<PlayerRequest, object?> ask, TextRequest request, bool amiga)
    {
        while (true)
        {
            var typed = ask(request) as string;
            if (!amiga || typed is not "")
                return typed;
        }
    }

    /// <summary>
    /// How long the party-killed window holds before DAMAGE's press-enter line: DOS Delay(3000) (GAME.OVR 0x375A–0x37E4,
    /// row 24 keeping what was there); the Amiga none (hunk 16 +0x4674–0x46DC: the window (1, 1)–(38, 22) with its title
    /// through hunk 25 +0x19F4, the message through the text routine hunk 25 +0x10EC with the clear, PlaySong(5) at
    /// hunk 27 +0x6E54, which only starts the song (hunk 33 +0xE6 → hunk 27 +0x78A0), then the key wait hunk 25 +0xE78
    /// with the press-enter line in pen 28: 39 blank glyphs from column 1, which draw nothing (JAM1), then the line
    /// through the string routine at (1, 24)). Null: no hold.
    /// </summary>
    public static TimeSpan? PartyKilledHold(bool amiga) => amiga ? null : TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// An amount question's prompt ("How much " + coin + "will you take? ", the program's strings ending in a space): DOS
    /// draws it on row 24 with the typed text after one more space (so the Client trims it); the Amiga hands the whole
    /// string to the input requester (hunk 25 +0x1092 → +0xEC6), whose width counts that last space (FS-UAE s47, Take's
    /// "How much Copper will you take? ": 31 cells, 252 pixels wide).
    /// </summary>
    public static string AmountPrompt(string prompt) => AmountPrompt(prompt, On);

    /// <summary><see cref="AmountPrompt(string)"/> for a given art.</summary>
    public static string AmountPrompt(string prompt, bool amiga) => amiga ? prompt : prompt.TrimEnd();

    /// <summary>The Rename prompt (hunk 10 +0x5506–0x5516: the text, then the name as it is stored; the requester draws
    /// the bytes, so a mixed-case name keeps its case).</summary>
    public static string RenamePrompt(EclCharacter c) => "NEW NAME FOR " + c.Name;

    /// <summary>
    /// Creation's name length: the Amiga's requester gets MaxChars 15 (hunk 10 +0x2114–0x2122, "Character name: ",
    /// the end mark included: 14 characters; Rename's 16 gives 15, <see cref="RenameLength"/>); DOS 0x1B7BE takes 15.
    /// </summary>
    public const int CreationNameLength = 14;

    /// <summary>The characters creation's name prompt takes: <see cref="CreationNameLength"/> under the Amiga pages,
    /// else DOS's <see cref="EclCharacter.MaxNameLength"/>.</summary>
    public static int NameLength(bool amiga) => amiga ? CreationNameLength : EclCharacter.MaxNameLength;

    /// <summary>
    /// The created character's name. The Amiga copies the requester's text as typed (strncpy, hunk 10 +0x212E; asked
    /// again while empty, +0x2136–0x2146), compares that with its developer names (<see cref="DeveloperBlocks"/>), stores
    /// each space as 0xFF (+0x2332–0x2356; drawn blank, its own record's matter: the Client keeps the space), then saves
    /// the character (+0x2380 → hunk 16 +0x49C), which first writes the name back as <see cref="SavedName"/>: so the
    /// name ends upper case (s46: typed "ldvorldv", the POOLSAVE disk's file and record both "LDVORLDV"). DOS stores it
    /// upper case at once.
    /// </summary>
    public static string CreationName(string typed, bool amiga) => amiga ? SavedName(typed) : typed.ToUpperInvariant();

    /// <summary>The characters the Amiga's character save drops from a name (hunk 11 +0x4C, nine bytes, the space first:
    /// spaces are 0xFF by then, so the Client, which keeps them as spaces, never drops one).</summary>
    private const string SaveDropped = " .*,?/:;|";

    /// <summary>
    /// The name as the Amiga's character save (hunk 16 +0x49C–0x4DA; run by creation's end, Remove and Save) writes it
    /// back into the record before the file is written: strncpy(name, H10+0x1B8(H10+0xE8(name, ".")), 40).
    /// H10 +0xE8 deletes a '.' only where the byte before it is a '.' too, or at the start (it compares the
    /// one-byte copy at index − 1, hunk 27 +0x7080, clamped to 0). H10 +0x1B8 walks the name once and, at each
    /// index, tries the nine bytes of <see cref="SaveDropped"/> in order, deleting the byte on a match (the next byte
    /// moves in and meets only the rest of the table), then upper-cases a–z (hunk 0 +0x6DC). Rename (hunk 10 +0x5532)
    /// does not save, so a renamed member keeps the typed case until the next Save or Remove. Under the Amiga pages
    /// only: the saved members' names (<see cref="SavedName"/>).
    /// </summary>
    public static void SaveNames(IEnumerable<EclCharacter> members)
    {
        if (!On)
            return;
        foreach (var c in members)
            c.Name = SavedName(c.Name);
    }

    /// <summary>One name as <see cref="SaveNames"/> writes it (DOS names are upper case already).</summary>
    public static string SavedName(string name)
    {
        var s = new System.Text.StringBuilder(name);
        for (int i = 0, end = s.Length; i < end && i < s.Length; i++)
            if (s[i] == '.' && s[Math.Max(i - 1, 0)] == '.')
                s.Remove(i, 1);
        for (int i = 0; i < s.Length; i++)
            for (int k = 1; k < SaveDropped.Length && i < s.Length; k++)
                if (s[i] == SaveDropped[k])
                    s.Remove(i, 1);
        for (int i = 0; i < s.Length; i++)
            if (s[i] is >= 'a' and <= 'z')
                s[i] = (char)(s[i] - 32);
        return s.ToString();
    }

    /// <summary>
    /// Creation's developer lines (hunk 10 +0x2148–0x2330, <see cref="GoldBox.Data.Module.Por.PorAmigaText.DeveloperKey"/>):
    /// for each block of the program whose name equals the typed name byte for byte (strcmp: case and length exact), in
    /// the program's order, its lines. Each is drawn at (0, 24) over the one before it (the string routine's JAM2 pass
    /// covers only its own cells, so a longer line's tail stays: FS-UAE s45) and held <see cref="DeveloperHold"/>; row 24
    /// is blanked after each block. Empty off the Amiga pages, for any other name, or when the module holds no blocks;
    /// DOS (0x1B7BE–0x1B80F) goes from the name to the portrait.
    /// </summary>
    public static List<List<string>> DeveloperBlocks(string typed, bool amiga, IReadOnlyDictionary<string, string> strings)
    {
        var blocks = new List<List<string>>();
        const string Key = GoldBox.Data.Module.Por.PorAmigaText.DeveloperKey;
        for (int i = 0; amiga && strings.TryGetValue(Key + i, out var name); i++)
        {
            if (!string.Equals(name, typed, StringComparison.Ordinal))
                continue;
            var lines = new List<string>();
            for (int j = 0; strings.TryGetValue(Key + i + "." + j, out var line); j++)
                lines.Add(line);
            blocks.Add(lines);
        }
        return blocks;
    }

    /// <summary>How long each developer line stays (Delay(100): AmigaDOS ticks of 1/50 s, not scaled by the game speed).</summary>
    public static TimeSpan DeveloperHold => TimeSpan.FromSeconds(GoldBox.Data.Module.Por.PorAmigaText.DeveloperTicks / 50.0);

    /// <summary>
    /// The page's texts as the Amiga draws them: the DOS layout of <see cref="CombatSheet.Build"/> with the name on
    /// <see cref="NameRow"/>, and the hit points taken out into <paramref name="amiga"/> with their Amiga colour
    /// (for <see cref="GoldBox.Core.Explore.TextFont.DrawAmiga"/>). Unchanged when the Amiga pages are off.
    /// </summary>
    public static List<(int Col, int Row, string Text, byte Colour)> Sheet(List<(int Col, int Row, string Text, byte Colour)> texts,
        EclCharacter c, List<(int Col, int Row, string Text, int AmigaColour)> amiga)
    {
        if (!On)
            return texts;
        string name = Case(c.Name);
        string hp = c[0x11B].ToString(System.Globalization.CultureInfo.InvariantCulture);
        var l = new List<(int, int, string, byte)>();
        foreach (var t in texts)
        {
            if (t.Col == 1 && t.Row == 1 && t.Text == name)
                l.Add((t.Col, NameRow, t.Text, t.Colour));
            else if (t.Col == 4 && t.Row == 18 && t.Text == hp)
                amiga.Add((t.Col, t.Row, t.Text, c[0x11B] < c[0x32] ? 8 : 10));
            else
                l.Add(t);
        }
        return l;
    }
}
