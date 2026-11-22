using System.Buffers.Binary;
using System.Text;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// The Amiga v1.0 text font and its pens (docs/re/por-art-formats.md §4 "Text font"; P, read s39 in the Amiga
/// <c>program</c>, hunks numbered from 0 in file order; no emulator run). Start-up opens the font from the TextAttr at
/// hunk 2 +0x4E (hunk 1 +0x1C4: OpenDiskFont, kept at hunk 32 +0x20) and sets it on the window's rastport (hunk 1
/// +0x35A–0x366: SetFont(hunk 32 +0x24, hunk 32 +0x20)); the rune routine puts it back after each rune (hunk 25 +0xC22).
/// Nearly all text goes through two routines that draw each line twice: the glyph routine's flag-1 path
/// (hunk 25 +0xA76–0xB6A; one character, count times) in JAM1 both passes, and the string routine (hunk 25 +0xC3C) in
/// JAM2 (background pen, never set by the program) then JAM1. Pass 1 draws the text in pen P at (column · 8, row · 8 + 6);
/// pass 2 in pen P + 1 one pixel to the right. A colour below 16 becomes P = colour + 16 − colour mod 2; most callers pass
/// a pen of the interface colours 16–31 themselves. Six direct Text calls draw otherwise: the menu routine's prompt
/// (hunk 19 +0x4F2C / +0x4F6A: pen + 1 at pixel 3, then the pen at pixel 4) and a highlighted list row or party member
/// (hunk 13 +0x322E / +0x3296, +0x3BFE / +0x3C4E: a pen-28 bar, the text in pens 14 then 15 of the picture half).
/// <para>DOS colour → pen: the callers pass their own pens, so each entry needs a DOS and an Amiga call site that do the
/// same thing. Read: PRINT's text window (DOS colour 10; hunk 16 +0x2544 / +0x257A: 28); the party list's name colours
/// (DOS START 0x23B6: 11 in play, 12 out of play, 14 hostile; hunk 13 +0x4AF2–0x4B08 on the same record flags two bytes
/// on: 28, 24, 18); the menu routine's three colours (DOS 0x2CC8C: prompt 13, words 10, hotkey 15; the area menu's call,
/// hunk 19 +0x27B2–0x27BC: 26, 28, 16, as every other menu and the list routine pass); row 24's key prompt (DOS START
/// 0x7C85: 13; hunk 19 +0x1038: 26). Colours 0–9 have no matching site (the Client draws none of them as game text but
/// its own grey 7) and take the glyph routine's rule <c>[?]</c>.</para>
/// <para>The import writes the font as <c>art/amiga/poolrad/0/8</c> (category <c>font</c>): 256 items of 8 × 8, item c
/// being character c as Text draws it at that position, 1 where a bit is set and 0 elsewhere; the PNG's palette is
/// registers 16–31. The pens go to <c>art/amiga/text-pens.json</c> (<see cref="AmigaTextPensDocument"/>). Nothing of the
/// game is in this file: the code bytes below are only checked; the font, colours and pens come from the player's disks.</para>
/// </summary>
public static class PorAmigaText
{
    /// <summary>The overlay's container and id: the font's name without <c>.font</c> and its height.</summary>
    public const string Container = "poolrad";
    public const int Id = 8;
    public const string Category = PorAmigaRunes.Category;

    /// <summary>The value of a set bit in the imported cells (pen − 16 for pen 17, the PNG's second colour).</summary>
    public const byte SetBit = 1;

    /// <summary>What the program says about the text font: its name and height, the pen per DOS colour 0–15, the interface colours.</summary>
    /// <param name="Highlight">The lit party member's and list row's pens (hunk 13 +0x3B64 / +0x3146): the bar (an
    /// interface pen), then the text and its pass-2 pen (pens 14 and 15 of the picture half).</param>
    /// <param name="Row24">Row 24's colours (start-up's copper list, hunk 1 +0x290–0x31A: from screen line 192):
    /// register, 12-bit colour, register, colour, …</param>
    public sealed record Setup(string FontName, int YSize, int[] Pens, byte[] InterfaceRgb, int[] Highlight, int[] Row24);

    /// <summary>The copper list's colour moves (pea colour.w; pea $DFF180 + 2 · register .l), from line 192 on.</summary>
    private static readonly int[] CopperMoves = [0x2A4, 0x2BA, 0x2D0, 0x2E6, 0x300, 0x316];

    // (hunk, offset of the pea's word, role): the highlight's pens, at the party list's lit name and the list routine's
    // lit row; both sites must agree.
    private static readonly (int Hunk, int At, int Role)[] HighlightSites =
    [
        (13, 0x3B66, 0), (13, 0x3BE4, 1), (13, 0x3C34, 2),     // the lit name
        (13, 0x3148, 0), (13, 0x3218, 1), (13, 0x3280, 2),     // the lit list row
    ];

    // (hunk, offset, bytes): the code the reading relies on, checked before anything is taken.
    private static readonly (int Hunk, int At, string Hex)[] CodeChecks =
    [
        (1, 0x1C4, "4879"),                        // pea TextAttr (hunk 2 +0x4E)
        (1, 0x1CA, "4eba511e23c0"),                // OpenDiskFont, store at hunk 32 +0x20
        (1, 0x25E, "23e80032"),                    // the window's RPort → hunk 32 +0x24
        (1, 0x35A, "2eb9"),                        // SetFont(hunk 32 +0x24, hunk 32 +0x20)
        (1, 0x360, "2f39"),
        (1, 0x366, "4eba4eb6"),                    // jsr the stub at 0x521E
        (1, 0x521E, "4ef9"),                       // jmp hunk 38 +0x1C (reloc below)
        (38, 0x2C, "4eaeffbe"),                    // graphics SetFont
        (1, 0x210, "7e007020be806c1c"),            // 32 colour words …
        (1, 0x21C, "41f9"),                        // … from hunk 2 +0
        (25, 0xA6C, "7001b02d001f660000fa"),       // glyph routine: flag 1 → the text path
        (25, 0xA76, "7010ba006416"),               // colour below 16: the remap
        (25, 0xADE, "e78072001206e7815c81"),       // pass 1: x = column · 8, y = row · 8 + 6
        (25, 0xAF6, "4297"),                       // SetDrMd(rp, 0): JAM1
        (25, 0xB14, "700010055280"),               // pass 2: pen + 1 …
        (25, 0xB2C, "e7805280"),                   // … x + 1
        (25, 0xC5E, "7010ba006416"),               // string routine: the remap
        (25, 0xCB6, "48780001"),                   // SetDrMd(rp, 1): JAM2
        (25, 0xD24, "1007e7805280"),               // pass 2: x + 1 …
        (25, 0xD40, "700010055280"),               // … pen + 1
        (25, 0xD52, "4297"),                       // … JAM1
        (16, 0x2544, "4878"),                      // PRINT: pea colour.w …
        (16, 0x2558, "4eba303e"),                  // … the text window (stub 0x5598)
        (16, 0x257A, "4878"),                      // PRINTCLR: pea colour.w …
        (16, 0x258C, "4eba300a"),                  // … the text window
        (16, 0x5598, "4ef9"),                      // jmp hunk 25 +0x10EC
        (13, 0x4AF2, "4a2b010f660478"),            // name: +0x10F = 0 → colour …
        (13, 0x4AFA, "600e7001b02b0110660478"),    // … else +0x110 = 1 → colour …
        (13, 0x4B06, "600278"),                    // … else colour
        (13, 0x4B1E, "4eba184a"),                  // the string routine (stub 0x636A)
        (13, 0x636A, "4ef9"),                      // jmp hunk 25 +0xC3C
        (19, 0x27B2, "4878"),                      // area menu: pea capitals.w, …
        (19, 0x27B6, "4878"),                      // … pea others.w, …
        (19, 0x27BA, "4878"),                      // … pea prompt.w, …
        (19, 0x27CA, "6100260a"),                  // … the menu routine (+0x4DD6)
        (19, 0x4DF0, "1e2d00131c2d00171a2d001b"),  // menu routine: d7 prompt, d6 others, d5 capitals
        (19, 0x4F0E, "700010075280"),              // prompt: pen d7 + 1 at x 3, …
        (19, 0x4F4E, "70001007"),                  // … then pen d7 at x 4
        (19, 0x501C, "74001405"),                  // a capital or digit (the hotkey): pen d5
        (19, 0x5048, "74001406"),                  // any other character: pen d6
        (19, 0x1038, "4878"),                      // key prompt: pea colour.w …
        (19, 0x103C, "4eba576c"),                  // … row 24's prompt (stub 0x67AA)
        (19, 0x67AA, "4ef9"),                      // jmp hunk 25 +0xE78
        (13, 0x3B64, "4878"),                      // lit name: SetAPen(bar), …
        (13, 0x3B72, "48780001"),                  // … JAM2, RectFill to x + 8 · length, y + 8 (+0x3BA4) …
        (13, 0x3BA4, "5083"),
        (13, 0x3BE2, "4878"),                      // … the text in one pen …
        (13, 0x3C32, "4878"),                      // … then the next, one pixel right
        (13, 0x3146, "4878"),                      // lit row: SetAPen(bar), JAM2, RectFill from x + 1 …
        (13, 0x3154, "48780001"),
        (13, 0x3170, "5280"),
        (13, 0x319E, "5381"),                      // … to x + 8 · length − 1 …
        (13, 0x31BC, "5c84"),                      // … and y + 6
        (13, 0x3216, "4878"),
        (13, 0x327E, "4878"),
        (1, 0x290, "4878000a487800c0"),            // copper: wait for line 192 (CWAIT, stub 0x52D8) …
        (1, 0x29A, "4eba503c"),
        (19, 0x50DE, "e782"),                      // menu gadget: LeftEdge = column · 8, …
        (19, 0x50E8, "337c00c00006"),              // … TopEdge 192 (row 24), …
        (19, 0x50F6, "e7825482"),                  // … Width 8 · length + 2, …
        (19, 0x5102, "337c0008000a"),              // … Height 8, …
        (19, 0x510C, "4269000c"),                  // … Flags 0 (GADGHCOMP: complemented when selected), …
        (19, 0x511E, "337c00010010"),              // … a BOOLGADGET
        (25, 0xE88, "1e2d000b"),                   // its colour …
        (25, 0xEA4, "70001007"),                   // … to the string routine at (1, 24)
        (25, 0xEB6, "6100fd84"),
    ];

    // (hunk, offset, target hunk, value): RELOC32 longs the reading relies on.
    private static readonly (int Hunk, int At, int Target, uint Value)[] RelocChecks =
    [
        (1, 0x1C6, 2, 0x4E),
        (1, 0x1D0, 32, 0x20),
        (1, 0x262, 32, 0x24),
        (1, 0x35C, 32, 0x20),
        (1, 0x362, 32, 0x24),
        (1, 0x5220, 38, 0x1C),
        (1, 0x21E, 2, 0),
        (16, 0x559A, 25, 0x10EC),
        (13, 0x636C, 25, 0xC3C),
        (19, 0x67AC, 25, 0xE78),
    ];

    private const int TextAttrHunk = 2, TextAttrAt = 0x4E, ColourHunk = 2;

    /// <summary>
    /// (DOS colour, hunk, offset, operand bytes): where the pen a DOS colour stands for is read. A <c>pea $n.w</c> has a
    /// word operand, a <c>moveq</c> a byte.
    /// </summary>
    private static readonly (int Dos, int Hunk, int At, int Bytes)[] PenSites =
    [
        (10, 16, 0x2546, 2),    // PRINT (DOS: the text window in colour 10, view3d.md "Text window")
        (10, 16, 0x257C, 2),    // PRINTCLR, the same pen
        (12, 13, 0x4AF9, 1),    // out of play (DOS 0x10D = 0: light red)
        (14, 13, 0x4B05, 1),    // hostile (DOS 0x10E = 1: yellow)
        (11, 13, 0x4B09, 1),    // else (DOS: cyan)
        (13, 19, 0x103A, 2),    // row 24's key prompt (DOS START 0x7C85: colour 13)
        (13, 19, 0x27BC, 2),    // the area menu's prompt (DOS 0x2CC8C colours 13 / 10 / 15: prompt, words, hotkey)
        (10, 19, 0x27B8, 2),    // its other letters
        (15, 19, 0x27B4, 2),    // its hotkeys (capitals and digits)
    ];

    /// <summary>The text font's set-up read from the player's <c>program</c>, or null when its code is not laid out so.</summary>
    public static Setup? Read(byte[] program)
    {
        try
        {
            var hunks = AmigaHunks.Parse(program);
            var relocs = AmigaHunks.Relocations(program).ToHashSet();
            int? Base(int h) => h < hunks.Count && hunks[h].DataOffset is { } at ? at : null;
            bool In(int h, int at, int length) => Base(h) is not null && at >= 0 && at + length <= hunks[h].Size;
            foreach (var (h, at, hex) in CodeChecks)
            {
                var want = Convert.FromHexString(hex);
                if (!In(h, at, want.Length) || !program.AsSpan(Base(h)!.Value + at, want.Length).SequenceEqual(want))
                    return null;
            }
            foreach (var (h, at, target, value) in RelocChecks)
                if (!In(h, at, 4) || !relocs.Contains(new AmigaReloc(h, target, at))
                    || BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(h)!.Value + at)) != value)
                    return null;
            int ta = Base(TextAttrHunk)!.Value + TextAttrAt;
            if (!In(TextAttrHunk, TextAttrAt, 8) || !relocs.Contains(new AmigaReloc(TextAttrHunk, TextAttrHunk, TextAttrAt)))
                return null;
            int nameAt = (int)BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(ta));
            if (!In(TextAttrHunk, nameAt, 1))
                return null;
            var name = program.AsSpan(Base(TextAttrHunk)!.Value + nameAt, hunks[TextAttrHunk].Size - nameAt);
            int z = name.IndexOf((byte)0);
            if (z <= 0)
                return null;
            int ysize = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(ta + 4));
            if (ysize != Id || !In(ColourHunk, 2 * PorAmigaRunes.FirstInterfacePen, 2 * PorAmigaRunes.InterfacePens))
                return null;
            var pens = Enumerable.Range(0, 16).Select(PorAmigaRunes.ScreenPen).ToArray();
            var read = new Dictionary<int, int>();
            foreach (var (dos, h, at, bytes) in PenSites)
            {
                int pen = bytes == 2 ? BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(Base(h)!.Value + at)) : program[Base(h)!.Value + at];
                // A pen of the interface pairs (pass 2 draws in pen + 1), the same at every site of one DOS colour.
                if (pen < PorAmigaRunes.FirstInterfacePen || pen % 2 != 0 || pen + 1 >= PorAmigaRunes.FirstInterfacePen + PorAmigaRunes.InterfacePens
                    || (read.TryGetValue(dos, out int seen) && seen != pen))
                    return null;
                read[dos] = pens[dos] = pen;
            }
            var rgb = AmigaHunks.Rgb12To24(program.AsSpan(Base(ColourHunk)!.Value + 2 * PorAmigaRunes.FirstInterfacePen), PorAmigaRunes.InterfacePens);
            var lit = new int[3];
            var litSeen = new bool[3];
            foreach (var (h, at, role) in HighlightSites)
            {
                int pen = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(Base(h)!.Value + at));
                if (pen >= PorAmigaRunes.FirstInterfacePen + PorAmigaRunes.InterfacePens || (litSeen[role] && lit[role] != pen))
                    return null;
                (lit[role], litSeen[role]) = (pen, true);
            }
            if (lit[0] < PorAmigaRunes.FirstInterfacePen || lit[1] >= PorAmigaRunes.FirstInterfacePen || lit[2] >= PorAmigaRunes.FirstInterfacePen)
                return null;
            var row24 = new List<int>();
            foreach (int at in CopperMoves)
            {
                var c = program.AsSpan(Base(1)!.Value + at, 10);
                if (c[0] != 0x48 || c[1] != 0x78 || c[4] != 0x48 || c[5] != 0x79)
                    return null;
                int colour = BinaryPrimitives.ReadUInt16BigEndian(c[2..]), reg = (int)BinaryPrimitives.ReadUInt32BigEndian(c[6..]) - 0xDFF180;
                if (reg < 0 || reg >= 64 || reg % 2 != 0 || colour > 0xFFF)
                    return null;
                row24.Add(reg / 2);
                row24.Add(colour);
            }
            return new Setup(Encoding.Latin1.GetString(name[..z]), ysize, pens, rgb, lit, [.. row24]);
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Amiga-only call-site facts read s43 (docs/re/por-art-formats.md §4 "Text font"): pens a few callers pass
    /// where DOS passes another colour, and strings the Amiga words otherwise than DOS.
    /// </summary>
    /// <param name="Pens">By name: <see cref="KeyPagePen"/>, <see cref="KeyDamagePen"/>, <see cref="PortraitPromptPen"/>,
    /// <see cref="PortraitWordsPen"/>, <see cref="PortraitCapitalsPen"/>.</param>
    /// <param name="Strings">By name: <see cref="PagePromptKey"/>, <see cref="ViewRenameKey"/>, <see cref="PartyMenuKey"/> + 0–10,
    /// <see cref="ShopMoneyLeftKey"/>, <see cref="TempleMoneyLeftKey"/>, the camp lines
    /// (<see cref="RestMenuKey"/>, <see cref="MonstersOnKey"/> … <see cref="PicsExitKey"/>; s44).</param>
    public sealed record Calls(IReadOnlyDictionary<string, int> Pens, IReadOnlyDictionary<string, string> Strings);

    /// <summary>The text window's page prompt (hunk 25 +0x1200–0x120A: its own upper-case string, hunk 26 +0x176, through
    /// row 24's key wait +0xE78 in pen 18; DOS START 0x77F8 → 0x7C85 with its own longer string in colour 13).</summary>
    public const string KeyPagePen = "keyPage", PagePromptKey = "PagePrompt";

    /// <summary>DAMAGE's press-enter key waits (hunk 16 +0x46D2–0x46DC after the party-killed window, hunk 4
    /// +0x2364–0x236E before a hit line when the box is full: pen 28; DOS 0x34BB / 0x959F: colour 15).</summary>
    public const string KeyDamagePen = "keyDamage";

    /// <summary>Creation's portrait menu (hunk 10 +0x3778–0x3790, the menu routine hunk 19 +0x4DD6):
    /// prompt pen 4 (the prompt is empty), other letters 16, capitals 28: the reverse of every other menu (28 / 16).</summary>
    public const string PortraitPromptPen = "portraitPrompt", PortraitWordsPen = "portraitWords", PortraitCapitalsPen = "portraitCapitals";

    /// <summary>
    /// The area script's HMENU (hunk 16 +0x2D2E–0x2DD6: the choices joined as a '~' line, then the '~' menu helper hunk 4
    /// +0x1910, which lowers the capitals and raises the letter after each '~', hunk 4 +0x17A0–0x1830). HMENU passes 28
    /// three times (hunk 16 +0x2DC6), but the helper reads only its two flags (+0x1922 / +0x1926) and calls the menu
    /// routine with its own pens (hunk 4 +0x1956–0x196E: prompt 26, words 28, capitals 16), the usual ones: each choice's
    /// capital grey, the rest green (FS-UAE s44: the city watch's two choices after an interrupted rest). s43 read the
    /// 28s as the line's pens.
    /// </summary>
    public const string HmenuWordsPen = "hmenuWords", HmenuCapitalsPen = "hmenuCapitals";

    /// <summary>
    /// The camp rest's interruption line (hunk 1 +0x3644–0x3668: "…rudely interrupted!" through the string routine at
    /// (1, 19) in pen 30, orange; DOS 0x24A45: white, 15) and its whole-party heal line (hunk 1 +0x31E4–0x320E: pen 28,
    /// DOS 10). Both then wait Delay(speed · 5 ticks) (hunk 25 +0xD7A), not a key as DOS does.
    /// </summary>
    public const string RestInterruptPen = "restInterrupt", RestHealPen = "restHeal";

    /// <summary>The party menu's entries (hunk 10 +0x672: 11 × 42 bytes copied from hunk 11 +0xD2; DOS DS:061A, the
    /// same stride): "PartyMenu.0" … "PartyMenu.10". The last is the Amiga's own exit entry.</summary>
    public const string PartyMenuKey = "PartyMenu.";

    /// <summary>The View menu's Amiga-only entry (hunk 10 +0x5440–0x544E, hunk 11 +0x840), appended to the line.</summary>
    public const string ViewRenameKey = "View.Rename";
    public const int PartyMenuEntries = 11, PartyMenuStride = 42;

    /// <summary>
    /// Program messages the Amiga words otherwise than DOS (s44, under the Client's <c>Msg</c> names): the shop's and the
    /// temple's money-left lines (hunk 22 +0x57F8 / hunk 25 +0x4472, each the text of the print in the box (1, 1)–(38,
    /// 22); quoted with ' where DOS 0x7194 / 0x5205 quote with ").
    /// </summary>
    public const string ShopMoneyLeftKey = "Msg.ShopMoneyLeft", TempleMoneyLeftKey = "Msg.TempleMoneyLeft";

    /// <summary>
    /// The camp's rest-time menu line (hunk 1 +0x302C: the menu routine with an empty prompt, hunk 2 +0x298, and the
    /// words at hunk 2 +0x29A, pens 26 / 28 / 16, animate flag 0): the Amiga's own wording, one space between entries,
    /// in the order of DOS's (0x244A5: Rest, Days, Hours, Mins, Inc, Dec, Exit) with other words for the last three
    /// but one and Days hot on its capital (DOS: Y). Its key test (hunk 1 +0x30D2–0x3176): R (and Enter) rests, D / H / M
    /// pick the word, A adds, S takes off, E leaves; keypad 8 / 2 add / take off, 4 / 6 move the word as DOS's Left /
    /// Right.
    /// </summary>
    public const string RestMenuKey = "Camp.RestMenu";

    /// <summary>
    /// The camp Alter menu's Pics line (hunk 1 +0x4E94–0x4F36): built in a buffer from the monsters word for the monster
    /// pictures flag (hunk 32 +0xBD: on, else off), the portraits word for the portraits flag (hunk 32 +0xBC), then the
    /// exit word, and asked through the menu routine with an empty prompt (hunk 2 +0x7C8), pens 26 / 28 / 16. Each
    /// on / off word holds a 0xFF between its two words, so the menu routine (which splits entries at a space or '_',
    /// hunk 19 +0x4F88–0x4FD0) keeps "Monsters on" one entry; the font draws 0xFF blank. One space between entries
    /// (DOS: two). M toggles the monster pictures, P the portraits, as DOS.
    /// </summary>
    public const string MonstersOnKey = "Camp.MonstersOn", MonstersOffKey = "Camp.MonstersOff",
        PortraitsOnKey = "Camp.PortraitsOn", PortraitsOffKey = "Camp.PortraitsOff", PicsExitKey = "Camp.PicsExit";

    /// <summary>The prefix of the strings that are whole camp lines, not words for the menu spelling table.</summary>
    public const string CampLinePrefix = "Camp.";

    /// <summary>The byte that joins two words of one menu entry in the Amiga's Pics line (drawn blank).</summary>
    public const char JoinedSpace = '\u00FF';

    // (hunk, offset, bytes): the code the call-site reads rely on.
    private static readonly (int Hunk, int At, string Hex)[] CallChecks =
    [
        (25, 0x1200, "4879"),              // page prompt: pea string.l, …
        (25, 0x1206, "4878"),              // … pea pen.w, …
        (25, 0x120A, "6100fc6c"),          // … bsr row 24's key wait (+0xE78)
        (16, 0x46D2, "4879"),              // DAMAGE's end: pea the press-enter line, …
        (16, 0x46D8, "4878"),              // … pea pen.w, …
        (16, 0x46DC, "4eba0f5c"),          // … jsr the stub at +0x563A …
        (16, 0x563A, "4ef9"),              // … jmp hunk 25 +0xE78
        (4, 0x2364, "4879"),               // DAMAGE's hit line, box full: pea the press-enter line, …
        (4, 0x236A, "4878"),               // … pea pen.w, …
        (4, 0x236E, "4eba3774"),           // … jsr the stub at +0x5AE4 …
        (4, 0x5AE4, "4ef9"),               // … jmp hunk 25 +0xE78
        (10, 0x3778, "4878"),              // portrait menu: pea capitals.w, …
        (10, 0x377C, "4878"),              // … pea others.w, …
        (10, 0x3780, "4878"),              // … pea prompt.w, …
        (10, 0x3784, "4879"),              // … pea words.l, …
        (10, 0x378A, "4879"),              // … pea prompt.l, …
        (10, 0x3790, "4eba3758"),          // … jsr the stub at +0x6EEA …
        (10, 0x6EEA, "4ef9"),              // … jmp the menu routine (hunk 19 +0x4DD6)
        (10, 0x672, "41f9"),               // party menu: lea table.l, a0; …
        (10, 0x678, "43edfe09303c01cd"),   // … 0x1CE + 1 bytes (11 × 42) to the frame
        (16, 0x2DD6, "4eba28da"),          // HMENU: jsr the stub at +0x56B2 …
        (16, 0x56B2, "4ef9"),              // … jmp the '~' menu helper (hunk 4 +0x1910), …
        (4, 0x1922, "1e2d001f1c2d0023"),   // … which reads only its two flag arguments, …
        (4, 0x195A, "4878"),               // … and passes its own pens: pea capitals.w, …
        (4, 0x195E, "4878"),               // … pea others.w, …
        (4, 0x1962, "4878"),               // … pea prompt.w, …
        (4, 0x196E, "4eba415c"),           // … jsr the stub at +0x5ACC …
        (4, 0x5ACC, "4ef9"),               // … jmp the menu routine (hunk 19 +0x4DD6)
        (1, 0x3654, "4878"),               // rest interrupted: pea pen.w, …
        (1, 0x3660, "4eba1b5c"),           // … the string routine at (1, 19) (stub +0x51BE), …
        (1, 0x3668, "4eba1d28"),           // … then Delay(speed · 5) (stub +0x5392)
        (1, 0x31EA, "4878"),               // whole party healed: pea pen.w, …
        (1, 0x31F6, "4eba1fc6"),           // … the string routine at (1, 19), …
        (1, 0x320E, "4eba2182"),           // … then Delay(speed · 5)
        (1, 0x51BE, "4ef9"),
        (1, 0x5392, "4ef9"),
        (10, 0x5434, "10280085"),          // View menu: Rename when record byte 0x85 < 0x80 (hunk 10 +0x542C–0x543E), …
        (10, 0x5444, "4879"),              // … pea the Rename word.l (appended to the line)
        (22, 0x57F8, "4879"),              // shop: pea the money-left line.l, then the box print's arguments
        (22, 0x57FE, "70012f00487800"),    // … moveq #1, d0; push d0 (clear); pea $1C.w (pen 28) …
        (25, 0x4472, "4879"),              // temple: the same
        (25, 0x4478, "70012f00487800"),
        (1, 0x305A, "4878001048"),         // rest-time menu: pea capitals.w (16), …
        (1, 0x305E, "4878001c48"),         // … pea others.w (28), …
        (1, 0x3062, "4878001a"),           // … pea prompt.w (26), …
        (1, 0x3066, "4879"),               // … pea words.l, …
        (1, 0x306C, "4879"),               // … pea prompt.l, …
        (1, 0x3072, "4eba2312"),           // … jsr the stub at +0x5386 …
        (1, 0x5386, "4ef9"),               // … jmp the menu routine (hunk 19 +0x4DD6)
        (1, 0x4E94, "4a39"),               // Pics: tst.b the monster pictures flag …
        (1, 0x4E9A, "6718"),               // … beq to the off word
        (1, 0x4EA0, "4879"),               // … pea the monsters-on word.l (strcpy into the line)
        (1, 0x4EB8, "4879"),               // … pea the monsters-off word.l
        (1, 0x4ECA, "4a39"),               // … tst.b the portraits flag …
        (1, 0x4ED0, "6718"),               // … beq to the off word
        (1, 0x4ED6, "4879"),               // … pea the portraits-on word.l (strcat)
        (1, 0x4EEE, "4879"),               // … pea the portraits-off word.l
        (1, 0x4F04, "4879"),               // … pea the exit word.l (strcat)
        (1, 0x4F30, "4879"),               // … pea prompt.l, …
        (1, 0x4F36, "4eba044e"),           // … jsr the stub at +0x5386
    ];

    // (hunk, offset, target hunk, value): RELOC32 longs of the call-site reads.
    private static readonly (int Hunk, int At, int Target, uint Value)[] CallRelocs =
    [
        (16, 0x563C, 25, 0xE78),
        (4, 0x5AE6, 25, 0xE78),
        (10, 0x6EEC, 19, 0x4DD6),
        (16, 0x56B4, 4, 0x1910),
        (4, 0x5ACE, 19, 0x4DD6),
        (1, 0x51C0, 25, 0xC3C),
        (1, 0x5394, 25, 0xD7A),
        (1, 0x5388, 19, 0x4DD6),
        (1, 0x4E96, 32, 0xBD),
        (1, 0x4ECC, 32, 0xBC),
    ];

    /// <summary>The call-site facts from the player's <c>program</c>, or null when its code is not laid out so.</summary>
    public static Calls? ReadCalls(byte[] program)
    {
        try
        {
            var hunks = AmigaHunks.Parse(program);
            var relocs = AmigaHunks.Relocations(program).ToHashSet();
            int? Base(int h) => h < hunks.Count && hunks[h].DataOffset is { } at ? at : null;
            bool In(int h, int at, int length) => Base(h) is not null && at >= 0 && at + length <= hunks[h].Size;
            foreach (var (h, at, hex) in CallChecks)
            {
                var want = Convert.FromHexString(hex);
                if (!In(h, at, want.Length) || !program.AsSpan(Base(h)!.Value + at, want.Length).SequenceEqual(want))
                    return null;
            }
            foreach (var (h, at, target, value) in CallRelocs)
                if (!In(h, at, 4) || !relocs.Contains(new AmigaReloc(h, target, at))
                    || BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(h)!.Value + at)) != value)
                    return null;
            int Word(int h, int at) => BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(Base(h)!.Value + at));
            // The target of the relocated long at (h, at), or null.
            (int Hunk, int At)? Pointer(int h, int at)
            {
                foreach (var r in relocs)
                    if (r.Hunk == h && r.Offset == at)
                    {
                        int to = (int)BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(h)!.Value + at));
                        return In(r.Target, to, 1) ? (r.Target, to) : null;
                    }
                return null;
            }
            string? CString((int Hunk, int At)? p, int max)
            {
                if (p is not var (h, at))
                    return null;
                var s = program.AsSpan(Base(h)!.Value + at, Math.Min(max, hunks[h].Size - at));
                int z = s.IndexOf((byte)0);
                // Printable ASCII, and 0xFF where the Pics words join two words into one entry.
                foreach (byte b in s[..Math.Max(z, 0)])
                    if (b is (< 0x20 or > 0x7E) and not 0xFF)
                        return null;
                if (z < 0)
                    return null;
                return Encoding.Latin1.GetString(s[..z]);
            }
            var pens = new Dictionary<string, int>
            {
                [KeyPagePen] = Word(25, 0x1208),
                [KeyDamagePen] = Word(16, 0x46DA),
                [PortraitCapitalsPen] = Word(10, 0x377A),
                [PortraitWordsPen] = Word(10, 0x377E),
                [PortraitPromptPen] = Word(10, 0x3782),
                [HmenuCapitalsPen] = Word(4, 0x195C),
                [HmenuWordsPen] = Word(4, 0x1960),
                [RestInterruptPen] = Word(1, 0x3656),
                [RestHealPen] = Word(1, 0x31EC),
            };
            // Both DAMAGE key waits pass the same pen; every pen is a register (the prompt's may be one of the low 16).
            if (Word(4, 0x236C) != pens[KeyDamagePen] || pens.Values.Any(p => p is < 0 or > 30))
                return null;
            var strings = new Dictionary<string, string>();
            if (CString(Pointer(25, 0x1202), 80) is not { Length: > 0 } page)
                return null;
            strings[PagePromptKey] = page;
            if (CString(Pointer(10, 0x5446), 40) is not { Length: > 0 } rename)
                return null;
            strings[ViewRenameKey] = rename;
            if (CString(Pointer(22, 0x57FA), 120) is not { Length: > 0 } shopLeft
                || CString(Pointer(25, 0x4474), 120) is not { Length: > 0 } templeLeft)
                return null;
            strings[ShopMoneyLeftKey] = shopLeft;
            strings[TempleMoneyLeftKey] = templeLeft;
            if (Pointer(10, 0x674) is not var (th, table) || !In(th, table, PartyMenuEntries * PartyMenuStride))
                return null;
            for (int i = 0; i < PartyMenuEntries; i++)
            {
                if (CString((th, table + PartyMenuStride * i), PartyMenuStride) is not { Length: > 0 } entry)
                    return null;
                strings[PartyMenuKey + i] = entry;
            }
            // The camp's Amiga lines (empty prompts checked: the menu routine draws no prompt for them).
            if (CString(Pointer(1, 0x306E), 4) != "" || CString(Pointer(1, 0x4F32), 4) != "")
                return null;
            foreach (var (key, at) in new[]
            {
                (RestMenuKey, 0x3068), (MonstersOnKey, 0x4EA2), (MonstersOffKey, 0x4EBA), (PortraitsOnKey, 0x4ED8),
                (PortraitsOffKey, 0x4EF0), (PicsExitKey, 0x4F06),
            })
            {
                if (CString(Pointer(1, at), 60) is not { Length: > 0 } line)
                    return null;
                strings[key] = line;
            }
            return new Calls(pens, strings);
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }

    /// <summary>The DOS colours whose pen was read from a call site (the others follow the glyph routine's rule, <c>[?]</c>).</summary>
    public static IReadOnlyList<int> ReadColours { get; } = [.. PenSites.Select(s => s.Dos).Distinct().Order()];

    /// <summary>
    /// The 256 cells (character c = item c, 8 × 8) as Text draws them at a text cell: a glyph's top line at row · 8 + 6 −
    /// baseline; <see cref="SetBit"/> where a bit is set, 0 elsewhere. Null for a font Text would space otherwise.
    /// </summary>
    public static byte[][]? Cells(AmigaDiskFont font) =>
        PorAmigaRunes.Cells(font, PorAmigaRunes.FirstInterfacePen + SetBit);
}
