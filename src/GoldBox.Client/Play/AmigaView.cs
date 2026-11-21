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

    /// <summary>A menu-style key line's text in the program's case under the Amiga pages (keyboard), or null.</summary>
    public static string? KeyText(KeyRequest k) => On && k.AsMenu ? k.Original : null;

    /// <summary>The Rename prompt (hunk 10 +0x5506–0x5516: the text, then the name).</summary>
    public static string RenamePrompt(EclCharacter c) => "NEW NAME FOR " + c.Name.ToUpperInvariant();

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
