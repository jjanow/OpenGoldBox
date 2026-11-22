using System.Globalization;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The Client's side of a running game, on the logic thread: the innermost ECL host (text, menus, input lines and the
/// opcodes whose effect is not read), the encounter screens, the stand-in fight, the loot, shop, temple and camp
/// screens (<see cref="VenueScreens"/>), the door menu. Every question goes
/// to the UI thread through <see cref="ScriptRunner.Ask"/>; text and pictures change state the UI draws while the
/// logic is parked.
/// </summary>
public sealed class ClientPlayer(ScriptRunner runner, Func<EclMemory> memory) : IEclHost, IEncounterUi, ICombatRunner, IAreaUi
{
    /// <summary>
    /// The program's text window (PRINT / PRINTCLR box, cursor, paging). It types its text while DS:4A00 is set
    /// (<see cref="EclMemory.TypesText"/>, <see cref="TypeDelay"/>), as START 0x77F8 does for every caller.
    /// </summary>
    public TextWindow Text
    {
        get
        {
            _text.Typed ??= TypeDelay;
            return _text;
        }
    }

    private readonly TextWindow _text = new();

    /// <summary>
    /// After each character the text routine draws (0x7737): when DS:4A00 is set (<see cref="EclMemory.TypesText"/>)
    /// Delay(DS:49E2 × 10) ms (START 0x777B–0x778D); speed 0 waits nothing. Off with <see cref="TypesText"/> (tests).
    /// </summary>
    /// <summary>
    /// A full-screen window's text through START 0x77F8 (the party-destroyed and party-killed messages): shown as it
    /// grows and typed as <see cref="TypeDelay"/> says.
    /// </summary>
    private void TypesInto(TextWindow box)
    {
        Window = box.WindowLines();
        box.Typed = () =>
        {
            Window = box.WindowLines();
            TypeDelay();
        };
    }

    private void TypeDelay()
    {
        if (!TypesText || _atOnce)
            return;
        var m = memory();
        if (m.TypesText && m.GameSpeed > 0)
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(10 * m.GameSpeed))
            {
                Chained = true,
                KeepCommandLine = true,
            });
    }

    /// <summary>The program's texts for the screens (our wordings until <see cref="Resources"/> are set).</summary>
    public ClientResources Resources { get; set; } = new();

    /// <summary>The game (set once it is built): its random generator and encounter host.</summary>
    public Func<GoldBox.Data.Por.PorGame?> Game { get; set; } = () => null;

    /// <summary>CLEARBOX blanked the picture window (1,1)–(15,15) until the next command, or the script switch's redraw (0x3FF5, <see cref="SwitchRedrawn"/>).</summary>
    public bool ViewBlanked { get; set; }

    /// <summary>
    /// After a CLEARBOX blank the view redraw 0x2DCF4 (a script switch's, 0x3FF5, or a command's) draws the 88 × 88 3D
    /// view alone: the rest of cells (1, 1)–(15, 15), the view's box among them, stays blank (DOSBox-X s39: CLEARBOX;
    /// NEWECL 20 from save A, still blank after the new script's text and two turns). The screens that draw the whole
    /// layout again end it: START 0x3379 (the camp, shop, temple, fight, hall, View, Cast, a load's entry: <see cref="InterfaceDrawn"/>)
    /// and EXIT's window redraw after a PROTECT (the full list: view3d.md "Text window", P s41).
    /// </summary>
    public bool ViewBoxBlank { get; set; }

    private Dialog? _dialog;
    private Dialog Dialog => _dialog ??= new Dialog(runner.Ask, Text);

    /// <summary>What the 3D view shows instead of the walls while an encounter or picture is up (null = the view).</summary>
    public string[]? ViewOverlay
    {
        get => _viewOverlay;
        private set
        {
            _viewOverlay = value;
            PictureUndrawn = false;
            if (value is null)
                ViewPicture = null;
        }
    }

    private string[]? _viewOverlay;

    /// <summary>The encounter picture, sprite or portrait the view shows (drawn by <see cref="ViewPictures"/>; null = none).</summary>
    public ViewPicture? ViewPicture { get; private set; }

    /// <summary>A one-line message (the area loop's own, or a stand-in notice) shown on the first text row.</summary>
    public string? Notice { get; set; }

    /// <summary>Host calls that have no player-facing effect yet, for the log.</summary>
    public List<string> Log { get; } = [];

    /// <summary>
    /// The request is a menu of the program's menu routine 0x2CC8C: every word menu and vertical menu (the camp's,
    /// hall's, party menu's, venues', the ECL menus, yes/no 0x2D9A0, the press-enter lines) and the pages whose menu line
    /// it draws (the item lists 0x2D669 call it at 0x2D8AC, View 0x21AED and its Items page, the icon editor, Modify). Not: a plain key wait
    /// (the page prompt, GetKey), a text input, a pause, the fight's move and aim cursor.
    /// </summary>
    public static bool IsProgramMenu(PlayerRequest request) => request is ChoiceRequest or ListRequest or KeyRequest { AsMenu: true }
        or PartyMenuRequest or PickRequest or SheetRequest or CharacterPageRequest or IconRequest or ModifyRequest or HallMenuRequest
        or PageRequest;

    /// <summary>
    /// Every call of the menu routine 0x2CC8C ends with VM $4AFF := 0 (0x2CFA7–0x2CFAD, its only exit; no other writer
    /// in START.EXE or GAME.OVR): <see cref="GoldBox.Core.Ecl.EclMemory.MenuAnimates"/> is cleared after each program
    /// menu the player answers (<see cref="ScriptRunner.Answered"/>), not only the ECL menus and the command line.
    /// DOSBox-X (s42): after a hall Save (which sets the cell) the hall's next menu ran before Begin, and the step's HMENU
    /// over PICTURE 2 after Begin stayed still.
    /// </summary>
    public void MenuEnded(PlayerRequest request)
    {
        if (IsProgramMenu(request) && Game() is { } g)
            g.Memory.MenuAnimates = false;
    }

    /// <summary>
    /// The menu routine 0x2CC8C draws the frame table at cell (3, 3) on every pass while VM $4AFF ≠ 0 and a picture is
    /// loaded (DS:6ABF > 0; 0x2CD5D–0x2CD9B), without waiting for the frames' delays (0x2CDD6), over whatever page is
    /// up. DOSBox-X (s43, a step's PICTURE 2 then PROGRAM 0): after a hall Save the hall's next menu drew the frames
    /// over its page; after a camp Save the quit question's yes/no ran the camp picture's frames on every pass (the camp
    /// menu after it held them 0.22 s again). A horizontal menu gets <see cref="ChoiceRequest.EveryPass"/> (and animates);
    /// the hall's menu leaves the picture on its page (<see cref="HallState.PictureOver"/>), a shop's or temple's in its
    /// view box (<see cref="VenuePictureOver"/>); the press-enter menu of the summary or NPC share window draws it over
    /// the window (<see cref="KeyRequest.EveryPass"/>). DOSBox-X (s44, a start-up Load whose vector 4 runs PICTURE 2 and
    /// then a shop, a temple, a COMBAT with no monsters or a VMENU): each first menu drew the frames on every pass. The
    /// other page menus (the item lists, View, Modify) are never the first menu after a Save or a Load in v1.3
    /// (training.md "The picture under $4AFF on other pages").
    /// </summary>
    public PlayerRequest MenuAsked(PlayerRequest request)
    {
        if (!IsProgramMenu(request) || Game() is not { } g || !g.Memory.MenuAnimates || !FramesLoaded)
            return request;
        switch (request)
        {
            case ChoiceRequest { EveryPass: false } c:
                if (Interface == Venue.Shop)
                    VenuePictureOver = true;
                return c with { Animates = true, EveryPass = true };
            case ChoiceRequest c:
                if (Interface == Venue.Shop)
                    VenuePictureOver = true;
                return c;
            case HallMenuRequest m:
                if (Hall is { } hall)
                    hall.PictureOver = HallPicture.Whole;
                return m with { EveryPass = true };
            case KeyRequest { AsMenu: true, EveryPass: false } k:
                return k with { EveryPass = true };
            default:
                return request;
        }
    }

    /// <summary>
    /// A picture is in the frame table DS:6ABE (loaded by 0x2E170: PICTURE, START 0x3379's camp and loot pictures) as
    /// far as the Client follows it: the camp's or loot's picture, or a script picture until the view is drawn again
    /// (the program frees the table at the command line's end, 0x17746). A block missing from the current disk leaves
    /// the program's table empty (0x2E24B; DOSBox-X s43: PICTURE 2 after the hall had set disk 3, nothing drawn); the
    /// Client keeps the picture and finds nothing to draw, the same screen.
    /// </summary>
    private bool FramesLoaded => Camping || (Interface == Venue.Loot && LootDrawn)
        || (ViewPicture is { Kind: ViewPictureKind.Picture } p && !ReferenceEquals(p, _freedPicture));

    /// <summary>
    /// The script picture the fight start freed (0xE402 frees the frame table DS:6ABE first, 0xE40D): no menu of the
    /// fight or after it draws it, even with $4AFF set (DOSBox-X s44: a start-up Load's vector 4 with PICTURE 2 and a
    /// COMBAT, the combat menu without it). A picture loaded later is another one.
    /// </summary>
    private ViewPicture? _freedPicture;

    /// <summary>
    /// A shop's or temple's first menu with $4AFF set drew the loaded picture in the view box (<see cref="MenuAsked"/>);
    /// its last frame stays until the loop draws its screen again (<see cref="TradeContext.VenueRedrawn"/>). DOSBox-X
    /// (s44): PICTURE 2 ran in the box under the shop's menu, stayed after Pool and was gone after View's exit.
    /// </summary>
    public bool VenuePictureOver { get; private set; }

    /// <summary>
    /// The loot menu drew its screen (START 0x3379 case 6 at 0x644B): the chest is in the frame table and the view box.
    /// Before it (the NPC share window and the summary) the table still holds what was loaded before the COMBAT.
    /// </summary>
    public bool LootDrawn { get; private set; }

    private int Choose(string? prompt, IReadOnlyList<string> items, bool vertical = false, int? cancel = null,
        bool animates = false, int? listRow = null, bool scriptText = false, (int Words, int Capitals)? amigaPens = null,
        ListPaging? paging = null)
    {
        // The menu routine 0x2CC8C also animates while VM $4AFF is not 0, and clears it at its end (AreaLoop.MenuAnimateCell).
        var mem = memory();
        bool everyPass = mem.MenuAnimates && FramesLoaded;
        animates |= everyPass;
        try
        {
            return runner.Ask(new ChoiceRequest(prompt, items, vertical, cancel)
                { Animates = animates, EveryPass = everyPass, ListRow = listRow, Paging = paging, ScriptText = scriptText, AmigaPens = amigaPens })
                is int i ? i : 0;
        }
        finally
        {
            mem.MenuAnimates = false;
        }
    }

    /// <summary>
    /// DS:84E1, the picture animation's arm (<see cref="EclMemory.PictureArmed"/>; set by the step's pass 0x4223 and by
    /// PICTURE 255's redraw 0x1684, cleared by a head + body load 0x79E7 / 0x7B77). It is kept across a load in the
    /// game: the program's Load does not touch it (the Client carries it into the loaded game).
    /// </summary>
    public bool PictureArmed
    {
        get => memory().PictureArmed;
        set => memory().PictureArmed = value;
    }

    /// <summary>The text window types its text while DS:4A00 is set (<see cref="TypeDelay"/>); off only in tests that step through scripts.</summary>
    public bool TypesText { get; set; } = true;

    /// <summary>
    /// HMENU's and ENCMENU's animate flag: DS:84E0 and DS:84E1 (0x1E9E–0x1EB2, 0x2D8D–0x2DA1). [?] Only over a PIC
    /// picture: with DS:84E0 still set under a sprite (PICTURE, then SETUPMON at a distance in the same run) the
    /// program would play its frame table, which then holds the sprite; not checked, the Client keeps the sprite still.
    /// </summary>
    private bool CloseUpAnimates => memory().PictureShown && PictureArmed && ViewPicture is { Kind: ViewPictureKind.Picture };

    /// <summary>A notice the player must acknowledge (stand-ins for screens not built yet).</summary>
    private void Acknowledge(string text)
    {
        PrintAtOnce(text);
        runner.Ask(new KeyRequest());
    }

    /// <summary>A notice in our words (not the program's text routine): shown at once, never typed.</summary>
    private void PrintAtOnce(string text)
    {
        _atOnce = true;
        try
        {
            Text.Print(text, clear: true, colour: 14);
        }
        finally
        {
            _atOnce = false;
        }
    }

    private bool _atOnce;

    /// <summary>The text window waits for a key when a page is full (START.EXE 0x7A12).</summary>
    public void InstallPaging() => Text.Pause = () =>
    {
        _pagePrompted = true;
        runner.Ask(new KeyRequest { PagePrompt = true });
    };

    /// <summary>Something else was drawn on the bottom row since the last page prompt.</summary>
    public void ForgetPagePrompt() => _pagePrompted = false;

    /// <summary>The position line was blanked by a DAMAGE line and not drawn again yet.</summary>
    public bool PositionLineErased { get; set; }

    /// <summary>The text window's page prompt was shown (it stays on the bottom line until something else is drawn there).</summary>
    private bool _pagePrompted;

    // ---- Text ----------------------------------------------------------------------------------------------------
    /// <summary>
    /// PRINT / PRINTCLR (GAME.OVR 0x174F): PRINTCLR starts at column 1, row 17 and blanks the box; colour 10. The
    /// handler sets DS:4A00 = 1 (0x1763) before the text and 0 after it (0x17DC; the VM sets
    /// <see cref="EclMemory.TypesText"/>), so the text is typed out: Delay(DS:49E2 × 10) ms after each character
    /// (START 0x777B).
    /// </summary>
    public void Print(string text, bool clearFirst)
    {
        if (clearFirst)
            Text.Home();
        Text.Print(text, clearFirst);
    }
    public void PrintReturn() => Text.NewLine();

    /// <summary>
    /// CLEARBOX (0x385A): blanks the picture window, cells (1, 1)–(15, 15) (START 0x7E63, 0x386D), not the text box,
    /// and sets DS:84DE (0x3872), which sends the next PROTECT below row 13 into the window.
    /// </summary>
    public void ClearBox()
    {
        ViewBlanked = true;
        ViewGlyphs = [];
        _windowCleared = true;
    }

    /// <summary>
    /// EXIT's display part (0xD4D–0xE31; also PROGRAM 9's end): with DS:84E2 set (a PROTECT went into the cleared
    /// window) two windows are drawn, (1, 1)–(15, 15) and, in the 3D view ($49E6 ≠ 0), (3, 3)–(13, 13) (START 0x41F8: a
    /// blank inside and a frame; no key wait), so the runes are gone; then the cursor goes to column 1, row 17 and
    /// DS:84DE and DS:84E2 are cleared. The Client's window stays blank until the next command's redraw.
    /// </summary>
    public void TextHome()
    {
        if (_glyphsInWindow)
        {
            ViewGlyphs = [];
            ViewBoxBlank = false; // the view's box is drawn again with the window (view3d.md "CLEARBOX")
            // The window (1, 1)–(15, 15) is drawn with its frame, cells 0–16 of rows 0 and 16 and columns 0 and 16.
            ScreenGlyphs = [.. ScreenGlyphs.Where(g => g.Col > 16 || g.Row > 16)];
        }
        _glyphsInWindow = false;
        _windowCleared = false;
        Text.Home();
    }

    /// <summary>DS:84DE: CLEARBOX ran since the last EXIT (0x3872; cleared by EXIT 0xE31 and the start-up 0xE9EF).</summary>
    private bool _windowCleared;

    /// <summary>DS:84E2: a PROTECT moved into the cleared window (0x3C17); EXIT then redraws it (0xD4D), the script init clears it (0x7696).</summary>
    private bool _glyphsInWindow;

    /// <summary>
    /// Glyph cells PROTECT drew in the picture window (cells 1–15), as (column, row, raw glyph character): drawn over
    /// the blank window while <see cref="ViewBlanked"/> holds. A new CLEARBOX or EXIT's redraw removes them.
    /// </summary>
    public (int Col, int Row, char Glyph)[] ViewGlyphs { get; private set; } = [];

    /// <summary>
    /// Glyph cells PROTECT drew outside the text box (rows 17–22, columns 1–39) and the picture window (cells 1–15):
    /// the frame rows 16 and 23, the frame columns, the party list's side of rows 0–15, and row 24 (P, s30; no v1.3
    /// script draws there). They are pixels in the program: they stay until something draws over them. Row 24: the
    /// next menu (0x2CC8C fills the row from its line's end to column 39 with blanks, 0x2CC16–0x2CC41), message (START
    /// 0x2233 clears the row) or prompt (<see cref="BottomRowDrawn"/>); cells 0–16 of rows 0–16: EXIT's window redraw
    /// (<see cref="TextHome"/>). [?] When the program draws the other frame cells again (a full-screen redraw, START
    /// 0x3379) is not followed: the Client keeps them until the next area command (<see cref="CommandRedrawn"/>).
    /// </summary>
    public (int Col, int Row, char Glyph)[] ScreenGlyphs { get; private set; } = [];

    /// <summary>Something was drawn on row 24 (a menu, a message, a prompt): the glyphs there are gone.</summary>
    public void BottomRowDrawn()
    {
        if (ScreenGlyphs.Any(g => g.Row == 24))
            ScreenGlyphs = [.. ScreenGlyphs.Where(g => g.Row != 24)];
    }

    /// <summary>An area command's redraw: the glyphs left on the frame and the bottom row are gone ([?] see <see cref="ScreenGlyphs"/>).</summary>
    public void CommandRedrawn() => ScreenGlyphs = [];

    /// <summary>
    /// PROTECT (GAME.OVR 0x3BC3): one line of raw glyphs at the text cursor DS:5E24 / 5E25. When CLEARBOX ran
    /// (DS:84DE) and the row is above 13, the cursor first goes to column 2, row 5 (inside the cleared window) and
    /// DS:84E2 := 1. Each glyph is drawn through the glyph routine with its flag clear (START 0x726F: the byte is the
    /// glyph number in the text font, colour 15; columns above 39 or rows above 24 are not drawn) and the column moves
    /// on; at the end the row goes down one, the column stays (an empty line moves the row too). The glyphs go into the
    /// text box when the cursor is in rows 17–22 (a later clear of the box removes them), into the picture window
    /// in rows 1–15 and anywhere else into <see cref="ScreenGlyphs"/> (rows 16, 23, 24 and the frame; P, s30).
    /// </summary>
    public void DrawGlyphs(ushort address, IReadOnlyList<byte> glyphs)
    {
        int col = Text.Column, row = Text.Row;
        if (_windowCleared && row > 13)
        {
            (col, row) = (2, 5);
            _glyphsInWindow = true;
        }
        var inWindow = new List<(int, int, char)>(ViewGlyphs);
        var elsewhere = new List<(int, int, char)>(ScreenGlyphs);
        foreach (var g in glyphs)
        {
            char ch = TextFont.RawGlyph(g);
            if (col <= 39 && row <= 24)
            {
                if (row is >= TextWindow.Top and <= TextWindow.Bottom && col >= TextWindow.Left)
                    Text.Put(row, col, ch.ToString(), 15);
                else if (row is >= 1 and <= 15 && col is >= 1 and <= 15)
                {
                    inWindow.RemoveAll(c => c.Item1 == col && c.Item2 == row);
                    inWindow.Add((col, row, ch));
                }
                else
                {
                    elsewhere.RemoveAll(c => c.Item1 == col && c.Item2 == row);
                    elsewhere.Add((col, row, ch));
                }
            }
            col = (col + 1) & 0xFF; // DS:5E24 is a byte
        }
        ViewGlyphs = [.. inWindow];
        ScreenGlyphs = [.. elsewhere];
        Text.MoveTo(col, row + 1);
    }

    /// <summary>DELAY: game speed DS:49E2 (<see cref="EclMemory.GameSpeed"/>) × 225 ms; speed 0 does not wait.</summary>
    public void Delay()
    {
        int speed = memory().GameSpeed;
        if (speed is > 0 and <= 9)
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * speed)));
    }

    // ---- Menus and input -----------------------------------------------------------------------------------------
    /// <summary>
    /// VMENU (GAME.OVR 0x1C4E, read s43): the cursor to (1, 17) (0x1CCE), the prompt through START 0x77F8 into the box
    /// (1, 17)–(38, 22) with clear, colour 10 (0x1CD8–0x1CF0; typed only while DS:4A00 stands, as every 0x77F8 text),
    /// then the list routine 0x2D669 (through 0x8C5B, 0x1D99) with the window from row DS:5E25 + 1 to row 22, columns
    /// 1–38, colours 13 / 10 / 15, keypad flag 1, an empty prompt and word and no Exit, the position 0 (0x1D51). After
    /// the pick the box is blanked (START 0x7E63 through 0709:0DD3, 0x1DC2) and the cursor stays after the prompt
    /// (DOSBox-X s43: a PRINT after the menu went on at the prompt's end). A list longer than its window (no v1.3
    /// VMENU has one: at most 5 rows with the prompt) pages with the routine's Next / Prev (read and seen in DOSBox-X
    /// s44, scratch saves; the words as the spell lists', <see cref="CampScreens.Paging"/>): the screen's
    /// <c>ExploreScreen.ListAnswer</c>. Escape picks the highlighted entry too (VMENU stores the position, 0x1D9E).
    /// </summary>
    public ushort VerticalMenu(string prompt, IReadOnlyList<string> choices)
    {
        Text.Home();
        Text.Print(prompt, clear: true);
        int first = Math.Min(Text.Row + 1, TextWindow.Bottom);
        var words = Resources.CombatTexts.Words(CombatMsg.AimNextPrevManual);
        var paging = new ListPaging(words.ElementAtOrDefault(0) ?? "NEXT", words.ElementAtOrDefault(1) ?? "PREV",
            TextWindow.Bottom - first + 1, 0);
        int pick = Choose(null, Upper(choices), vertical: true, listRow: first, scriptText: true, paging: paging);
        Text.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        return (ushort)pick;
    }
    public ushort HorizontalMenu(IReadOnlyList<string> choices) =>
        (ushort)Choose(null, [.. Upper(OneWordSwap(choices)).Select(AmigaView.HmenuCase)], animates: CloseUpAnimates,
            amigaPens: AmigaView.HmenuPens);

    /// <summary>
    /// HMENU (GAME.OVR 0x1E17, read s21): a menu of one word that equals the code string at 0x1DCB (exact compare,
    /// 0x1E76) is shown with the string at 0x1DF0 instead (0x1E8B), when the count operand is 1. DOSBox-X (s21, the
    /// harbor master in New Phlan) shows the second. Both are read from the player's program; without them nothing is
    /// swapped.
    /// </summary>
    private IReadOnlyList<string> OneWordSwap(IReadOnlyList<string> choices) =>
        choices.Count == 1 && Resources.Texts.FromFiles(Msg.HmenuButton) is { Length: > 0 } button
            && Resources.Texts.FromFiles(Msg.HmenuEnter) is { Length: > 0 } enter
            && string.Equals(choices[0], button, StringComparison.Ordinal)
            ? [enter]
            : choices;

    private static string[] Upper(IReadOnlyList<string> s) => [.. s.Select(c => c.ToUpperInvariant())];

    public int InputNumber()
    {
        // INPUTNUM (0x1697): the line input with an empty prompt (0x16B9) and 6 characters (START 0x7C1F); the Amiga's
        // requester takes 5 digits (hunk 16 +0x1092).
        var s = runner.Ask(new TextRequest("", true, AmigaView.On ? AmigaView.InputNumberLength : 6)) as string ?? "";
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public string InputString() =>
        runner.Ask(new TextRequest("", false, AmigaView.On ? AmigaView.InputStringLength : 40)) as string ?? "";

    /// <summary>WHO through the default host path (not used: <see cref="PickCharacter"/> answers).</summary>
    public void Who(string prompt)
    {
        if (PickCharacter(prompt) is { } i && i >= 0 && memory().Party is { } party)
            party.CurrentIndex = i;
    }

    /// <summary>
    /// WHO (GAME.OVR 0x3879, read s44): the operands, the text box (1, 17)–(38, 22) blanked (START 0x7E63 through
    /// 0709:0DD3; the cursor stays), then START's member pick 0x37E8 (00BA:2C48) with the prompt (string slot 1) and
    /// the Exit flag 0 on DS:5D92 itself: row 24 the prompt in colour 13 and "Select" alone, the party list drawn with
    /// the pick lit, Home / End move it (<see cref="MemberPick"/>). Escape clears the pick and the loop goes on; Select
    /// or Return then leaves nobody current (<see cref="IEclHost.NoCharacter"/>).
    /// </summary>
    public int? PickCharacter(string prompt)
    {
        var party = memory().Party;
        if (party is null || party.Members.Count == 0 || Trade(null) is not { } t)
            return null;
        Text.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        var pick = MemberPick.Run(t, AmigaView.Case(prompt), party.Current, Resources.SelectMenu ?? "Select");
        return pick is null ? IEclHost.NoCharacter : party.Members.IndexOf(pick);
    }

    public ushort Parley(ushort haughty, ushort sly, ushort nice, ushort meek, ushort abusive) => 0; // EncounterHost answers
    public ushort? EncounterMenu(EclEncounterMenu menu) => null;                                       // EncounterHost answers

    // ---- Opcodes the encounter host or the VM answers; the rest are Lee-only stand-ins ----------------------------
    public void LoadCharacter(ushort index) => Log.Add($"LOADCHAR {index} [?]");
    public void LoadMonster(ushort monster, ushort count, ushort picture) { }
    public void SetupMonster(ushort monster, ushort distance, ushort picture) { }
    public void Approach() { }
    public void ClearMonsters() { }
    public ushort PartyStrength() => 0;
    public EclPartyCheck CheckParty(ushort field, ushort effect)
    {
        Log.Add($"CHKPARTY {field} {effect} [?] answered 0");
        return default;
    }
    public void Combat() { }
    public void Treasure(IReadOnlyList<ushort> values) { }
    public void Rob(ushort wholeParty, ushort moneyPercent, ushort itemChance) => Log.Add($"ROB {wholeParty} {moneyPercent} {itemChance} [?] not applied");
    public void Damage(ushort attack, ushort dice, ushort sides, ushort bonus, ushort extra) =>
        Log.Add($"DAMAGE {attack} {dice}d{sides}+{bonus} {extra} [?] not applied");
    public void SpriteOff() { }
    public bool FindItem(ushort item) => false;
    public void AddNpc(ushort monster, ushort morale) => Log.Add($"ADDNPC {monster} {morale} [?] not applied");
    public (ushort Slot, ushort Character)? Spell(ushort spell) => null;
    public void Picture(ushort picture) { }
    public void LoadMap(int map) { }
    public void LoadOutdoorPicture() => Log.Add("LOADFILES outdoors (wilderness picture)");
    /// <summary>The wall sets loaded into slots 1..3 (the program's cache the save copies into $4AFA–$4AFF); null = as the memory had them.</summary>
    private readonly int?[] _wallSets = new int?[3];
    private readonly bool[] _wallSetKnown = new bool[3];

    public void LoadWallSet(int slot, int? set)
    {
        if (slot is >= 1 and <= 3)
        {
            _wallSets[slot - 1] = set;
            _wallSetKnown[slot - 1] = true;
        }
    }
    public void Clock(ushort minutes) { }
    public void Call(ushort routine)
    {
        Log.Add($"CALL {routine:X4}");
        // CALL $BA03 (OVR 0x3AC6): the sound picked by the last write to $03DE (DS:84CC): 8 → step, 10 → cue 262A.
        if (routine == 0xBA03 && memory().SpecialWriteValues.TryGetValue(0x03DE, out var which))
        {
            if (which == 8)
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Step);
            else if (which == 10)
                Sound.Cue(GoldBox.Core.Sound.SoundCue.Fireball);
        }
    }

    /// <summary>Where the program's sound calls go (the Client's audio, or none).</summary>
    public GoldBox.Core.Sound.ISoundSink Sound { get; set; } = GoldBox.Core.Sound.NoSound.Instance;
    public void Protect(ushort address) { } // PROTECT is drawn by DrawGlyphs (the VM passes the glyphs read)
    public ushort Random(ushort range) => 0; // AreaHost draws from the game's generator
    public void ScriptChanging(ushort script)
    {
        Log.Add($"NEWECL {script}");
        _glyphsInWindow = false; // the script init clears DS:84E2 (0x7696)
        // NEWECL's script load (0x1996 → 0x7814) clears row 24 (0x2CFCA), shows "Loading...Please Wait" there for the
        // disk's time (not shown: machine time) and clears it again; the row stays blank until the next menu or command
        // line (DOSBox-X s39: PICTURE; NEWECL 20 from save A, the new script's text typed under a blank row 24).
        Interlocked.Increment(ref _bottomRowClears);
        BottomRowDrawn();
    }

    /// <summary>How often NEWECL cleared row 24 (<see cref="ScriptChanging"/>); the explore screen drops the line it keeps.</summary>
    public int BottomRowClears => Volatile.Read(ref _bottomRowClears);
    private int _bottomRowClears;

    /// <summary>PROGRAM: 0 training hall (0x19AE0), 8 end game (0x20941), 9 camp (0x3B56: the camp menu, then the run ends).</summary>
    public void Program(ushort program)
    {
        switch (program)
        {
            case 0: TrainingHall(); break;
            case 8: EndGameSequence(); break;
            case 9: Camp(); break;
        }
    }

    /// <summary>The training hall's screen while it is open (<see cref="HallScreens"/>), else null.</summary>
    public HallState? Hall { get; private set; }

    /// <summary>The end game's pictures over the view while it runs, else null.</summary>
    public EndScene? Scene { get; set; }

    /// <summary>
    /// An end-game block in the chosen art (disk, block → the FINAL picture, from the disk's file or any disk's), or
    /// null: the art setting's source for <see cref="HallScreens.AmigaEndGame"/>. Unset: DOS only.
    /// </summary>
    public Func<int, int, PorArtImage?>? EndArt { get; set; }

    /// <summary>
    /// Under Best / Amiga with the Amiga blocks, play the Amiga program's end game (on, the default) rather than DOS's
    /// with the Amiga dragon and spirit over the DOS arena pictures (the user decision of s5 names the arena pictures
    /// as DOS fill-ins; the Amiga sequence has no place for them).
    /// </summary>
    public bool AmigaEndGame { get; set; } = true;

    /// <summary>The frame delays of an end-game animation block (FINAL of the disk, else of any disk), or null.</summary>
    public Func<int, int, IReadOnlyList<uint>>? EndArtDelays => (disk, block) =>
    {
        var content = Game()?.Content;
        if (content is null)
            return [];
        foreach (int d in new[] { disk }.Concat(Enumerable.Range(1, 8)))
            if (content.Picture(EndGame.FileName(d), block) is { Count: > 0 } frames)
                return [.. frames.Select(f => f.Delay ?? 0)];
        return [];
    };

    /// <summary>
    /// The training hall (PROGRAM 0, 0x19AE0; docs/re/training.md): its menu over the party, Train Character with
    /// Core's rules, View and Save as in camp; DS:49F3 is 0 meanwhile (saved as such), then the area view returns.
    /// </summary>
    public void TrainingHall()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return;
        var game = Game();
        var camp = CampContextFor(t);
        var hall = new HallState();
        var context = new HallContext
        {
            Camp = new CampContext
            {
                Trade = camp.Trade, State = camp.State, Spells = camp.Spells, Rest = camp.Rest, Memory = camp.Memory,
                Cast = camp.Cast, Resources = camp.Resources, Save = l => SaveGame(l, 0), Quit = camp.Quit,
                EditIcon = camp.EditIcon,
            },
            State = hall,
            Text = Resources.HallTexts,
            Memory = memory(),
            Tables = Resources.Training,
            Random = n => game is null ? (ushort)0 : game.Rng.Next(n),
            Defaults = Resources.HallDefaults,
            Hooks = HallHooks,
            ProgramWord = () => ProgramWord,
        };
        Hall = hall;
        var mem = memory();
        mem.InterfaceState = 0; // 0x19AF9
        try
        {
            HallScreens.Run(context);
            // Begin sets DS:49F3 := 4 (0x19DCF); a Load of a save with another state leaves the menu with the save's
            // value (0x1F87B, 0x19D91; PorGame.LoadInHall). PROGRAM 0 then calls START 0x3379 (0x3B95), which draws every
            // window again (the text box blanked, the cursor DS:5E24/5E25 kept: DOSBox-X s22, a PRINT after PROGRAM 0
            // runs on after the earlier text, on the empty row) and by the state the view (4, 3) or the camp's picture
            // (2: a camp save, the usual kind; DOSBox-X s39/s41).
            if (!hall.LeaveAfterLoad)
                mem.InterfaceState = 4;
            DrawInterface();
        }
        finally
        {
            Hall = null;
        }
    }

    /// <summary>
    /// A screen that draws its own windows (the camp, a shop or temple, the loot menu, a fight) replaces the overland
    /// interface's layout (<see cref="GoldBox.Core.Explore.AreaLoop.OverlandLayout"/>): each draws START 0x3379 (the
    /// shop and temple case 1, the loot menu case 6, the camp case 2), and COMBAT's handler ends with START 0x3379 case 3
    /// on the overland (GAME.OVR 0x25F7–0x2635) whatever ran, so the joint at (16, 16) is back for the screen after it.
    /// It stays: the interface (START 0x36FE, no joint) is drawn again only at a command routine entry with DS:49F9 &lt; 2
    /// (0x17285), and on the overland DS:49F9 is 2–4 after the first command (0x173E7) and after the area loop's restart
    /// (0x3F28). DOSBox-X s44 (save-overland 26 10,20,N, vector 0 = a fight, a loot, a shop, a temple once): the joint
    /// on each screen, after it and after the next step.
    /// </summary>
    private void OtherLayout() => Game()?.Loop.OtherLayoutDrawn();

    /// <summary>
    /// START 0x3379 drew the screen for interface state <paramref name="state"/> while exploring (<see
    /// cref="GoldBox.Core.Explore.AreaLoop.DrawInterface"/>: the hall's end, a load's entry, the command line's View and
    /// Cast; P s41). 0x32CA draws every window again, each blanked inside: the text box (the cursor stays), the picture
    /// window and, in cases 1, 2, 4 and 6 (0x32CA(1)), the 3D view's box (a CLEARBOX blank and its ring are gone, PROTECT's runes with
    /// them); the party list (0x1307) and the position line (0x34AB). Case 2 shows the camp's picture (PIC 0x1D,
    /// 0x2E170) until the next view redraw; cases 3 and 4 redraw the view next (0x2DCF4, the area loop's redraw).
    /// </summary>
    /// <summary>START 0x3379 by the game's DS:49F3 (<see cref="GoldBox.Core.Explore.AreaLoop.DrawInterface"/>, followed here).</summary>
    public void DrawInterface()
    {
        if (Game() is not { } g)
        {
            InterfaceDrawn(memory().InterfaceState);
            return;
        }
        FollowInterface(g);
        g.Loop.DrawInterface();
    }

    private readonly HashSet<GoldBox.Data.Por.PorGame> _interfaceFollowed = [];

    /// <summary>Follows the game's START 0x3379 draws (<see cref="InterfaceDrawn"/>), once per game.</summary>
    public void FollowInterface(GoldBox.Data.Por.PorGame g)
    {
        if (_interfaceFollowed.Add(g))
            g.Loop.InterfaceDrawn += InterfaceDrawn;
    }

    /// <summary>
    /// A party menu Load's vector 4 is running before START 0x3379 drew the game's screen (inside the LOADFILES /
    /// LOADPIECES pair, or after the vector): the party menu's page is still on screen and vector 4 draws over it, the
    /// picture at cell (3, 3) without the view's box, the text in the text box's place, menus on row 24 (DOSBox-X s42:
    /// PRINT, PICTURE 2, HMENU in vector 4, loaded from the start-up menu). Set by the front end for that load only.
    /// </summary>
    public bool PartyMenuUnder { get; set; }

    public void InterfaceDrawn(byte state)
    {
        PartyMenuUnder = false;
        if (state is not (2 or 3 or 4))
            return;
        ViewBlanked = false;
        ViewBoxBlank = false;
        TextBoxFramed = false; // the joints come back with the windows (D s41: a member message, then Cast's list and Escape)
        ViewGlyphs = [];
        _glyphsInWindow = false;
        ScreenGlyphs = [.. ScreenGlyphs.Where(g => g.Row == 24)];
        Text.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        PositionLineErased = false;
        ReleaseRosterNames();
        if (state == 2)
        {
            ShowPicture(Screens.ExploreScreen.CampPicture, null);
            PictureUndrawn = true;
        }
    }

    /// <summary>
    /// START 0x3379 case 2 loads the camp's PIC 0x1D into the frame table (0x2E170 with 0) without drawing it: the window
    /// (in the 3D view's box) stays blank until a menu animates the frames (HMENU / ENCMENU with DS:84E0 and 84E1 set,
    /// the camp's menus), which then stay drawn (DOSBox-X s41: after a hall Load of a camp save the HMENU drew the camp
    /// picture; View's exit there left the box blank). Any other picture or view redraw ends it.
    /// </summary>
    public bool PictureUndrawn { get; set; }

    /// <summary>
    /// The position line ends in "CAMPING" (START 0x366D: DS:49F3 = 2): in the camp, and after a training hall's Load of
    /// a camp save until DS:49F3 is written again (<see cref="GoldBox.Core.Ecl.EclMemory.InterfaceState"/>). The
    /// overland interface due at the next command routine (START 0x3701 sets 3, then draws the line) has written it
    /// already as far as the screen goes (DOSBox-X s43: a hall Load of a location-type-2 camp save, no CAMPING).
    /// </summary>
    public bool CampingLine => Camping || (Interface is null && Hall is null && memory().InterfaceState == 2
        && Game()?.Loop.OverlandInterfacePending != true);

    /// <summary>The word after START on the DOS command line (ParamStr(1); the hall's J), or null.</summary>
    public string? ProgramWord { get; set; }

    /// <summary>The start word as a setting the controls screen edits (remembered through <see cref="ProgramWordChanged"/>).</summary>
    public Screens.WordSetting ProgramWordSetting => new(() => ProgramWord, w =>
    {
        ProgramWord = w;
        ProgramWordChanged?.Invoke(w);
    });

    /// <summary>Called when the controls screen changes the start word (the settings file keeps it).</summary>
    public Action<string?>? ProgramWordChanged { get; set; }

    /// <summary>The hall entries other screens provide (character creation, the roster); empty ones show a notice.</summary>
    public HallHooks HallHooks { get; } = new();

    /// <summary>The end game (PROGRAM 8, 0x20941): <see cref="HallScreens.EndGameSequence"/>.</summary>
    public void EndGameSequence() => HallScreens.EndGameSequence(this, Text, runner.Ask, memory());

    // ---- IEncounterUi ---------------------------------------------------------------------------------------------
    public void ShowEncounter(byte sprite, byte picture, int distance)
    {
        // The text stays the stand-in where the art is missing (ExploreScreen draws ViewPicture when it can).
        ViewBlanked = false; // a picture after CLEARBOX shows (DOSBox-X, s19: CLEARBOX, CALL $0806, PICTURE 40)
        ViewOverlay = distance == 0
            ? ["CLOSE-UP", $"PIC {picture}", "", "(ART NOT", "FOUND)"]
            : ["MONSTER", $"SPRITE {sprite}", $"DISTANCE {distance}", "", "(ART NOT", "FOUND)"];
        // At distance 0 the close-up: PIC when $6DE1 = 255, else head $6DE1 + body (ecl-format.md "SETUPMON"; DOSBox-X
        // s19: the new game's guide is a portrait).
        ushort head = memory().Read(GoldBox.Core.Game.EncounterHost.HeadPicture);
        ViewPicture = distance == 0
            ? head != 255
                ? new ViewPicture(ViewPictureKind.Portrait, picture, head, 0, memory().Disk)
                : new ViewPicture(ViewPictureKind.Picture, picture, null, 0, memory().Disk)
            : new ViewPicture(ViewPictureKind.Sprite, sprite, null, distance, memory().Disk)
            {
                OnOverland = memory().ViewMode != 1,
            };
    }

    /// <summary>The encounter draw's sprite without a close-up (0x7A98–0x7AB7), at distance 0 too.</summary>
    public void ShowEncounterSprite(byte sprite, byte picture, int distance)
    {
        if (distance > 0)
        {
            ShowEncounter(sprite, picture, distance);
            return;
        }
        ViewBlanked = false;
        ViewOverlay = ["MONSTER", $"SPRITE {sprite}", $"DISTANCE {distance}", "", "(ART NOT", "FOUND)"];
        ViewPicture = new ViewPicture(ViewPictureKind.Sprite, sprite, null, 0, memory().Disk)
        {
            OnOverland = memory().ViewMode != 1,
        };
    }

    public void ShowPicture(byte picture, byte? head)
    {
        ViewBlanked = false; // a picture after CLEARBOX shows (DOSBox-X, s19: CLEARBOX, CALL $0806, PICTURE 40)
        ViewOverlay = head is { } h ? ["PICTURE", $"HEAD {h}", $"BODY {picture}"] : ["PICTURE", $"{picture}"];
        ViewPicture = head is { } hd
            ? new ViewPicture(ViewPictureKind.Portrait, picture, hd, 0, memory().Disk)
            : new ViewPicture(ViewPictureKind.Picture, picture, null, 0, memory().Disk);
    }

    /// <summary>How often CALL $2C90 redrew the view and the position line (0x3A83); the explore screen follows it.</summary>
    public int PositionLineDraws => _positionLineDraws;
    private int _positionLineDraws;

    public void ViewRedrawn()
    {
        PositionLineErased = false;
        Interlocked.Increment(ref _positionLineDraws);
    }

    /// <summary>
    /// The script switch's redraw (0x3FF5 → 0x2DCF4) drew the view over the window: a CLEARBOX blank or a picture of
    /// the old script's is gone from the screen (the flags DS:84E0 / 84E1 are not touched).
    /// </summary>
    public void SwitchRedrawn() => ViewDrawn();

    public void RestoreView() => ViewOverlay = null;

    /// <summary>
    /// The view redraw 0x2DCF4 (the step, Look, the camp's end, an area's entry: <c>AreaLoop.Redrawn</c>) drew the
    /// view over the window: a picture, sprite or CLEARBOX blank is gone from the screen (DOSBox-X s24: a picture stays
    /// up until the step's redraw). The flags DS:84E0 / 84E3 are not touched here.
    /// </summary>
    public void ViewDrawn()
    {
        if (ViewBlanked && !GoldBox.Core.Explore.Overland.Shown(memory()))
            ViewBoxBlank = true;
        ViewBlanked = false;
        ViewOverlay = null;
    }

    public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
    {
        if (text is not null)
            Text.Print(text, clear: true);
        // The program's two menu lines (GAME.OVR 0x2BE1 / 0x2BFD; v1.3 spells the fourth word differently from our old stand-in).
        var items = Resources.Texts.Words(kind == EncounterMenuKind.CombatWaitFleeAdvance
            ? Msg.EncounterMenuAdvance : Msg.EncounterMenuParley);
        return Choose(null, items, animates: CloseUpAnimates);
    }

    public void ShowEncounterMessage(EncounterMessage message)
    {
        // The program's two fixed lines (GAME.OVR 0x2C1A / 0x2C2B).
        Text.Print(Resources.Texts[message == EncounterMessage.BothSidesWait ? Msg.BothSidesWait : Msg.MonstersFlee], clear: true);
        runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(900)));
    }

    public int ChooseParleyAttitude() => Choose(null, Resources.Texts.Words(Msg.ParleyAttitudes)); // GAME.OVR 0x32EE

    /// <summary>LoadCombatIcon: the combat screen draws the monsters of icon slot <paramref name="slot"/> (record 0xBF) with this CPIC.</summary>
    public void LoadCombatIcon(byte picture, int slot) => FightView.IconPictures[slot] = picture;

    // ---- ICombatRunner ---------------------------------------------------------------------------------------------
    /// <summary>
    /// No fight was built (game files without the fight tables): a notice, then the creatures keep their status as
    /// loaded, so the after-combat step treats the fight as won. A stand-in, not the game's outcome.
    /// </summary>
    public void Fight(EncounterState state) =>
        Acknowledge($"A FIGHT WITH {state.Monsters.Count} CREATURES. THE FIGHT IS NOT IN THIS BUILD YET: IT COUNTS AS WON.");

    /// <summary>What the combat screen shows (<see cref="Screens.CombatScreen"/>); active while a fight runs.</summary>
    public CombatView FightView { get; } = new();

    /// <summary>The Amiga song hooks, when the module and presentation setting provide them.</summary>
    public Action<int>? PlaySong { get; init; }

    private CombatChooser? _chooser;

    /// <summary>The player's side of the combat menu (passed to the game as <c>PorPlayer.FightMenu</c>).</summary>
    public CombatChooser Chooser => _chooser ??= new CombatChooser(runner.Ask, FightView)
    {
        Resources = () => Resources,
        Typing = () => memory().TypesText,
        PortraitsOn = () => CampSettings.Portraits(memory()),
    };

    /// <summary>
    /// The real fight (arena and placement done): shown on the combat screen while it runs; party members not on Quick
    /// get the combat menu (<see cref="Chooser"/>). The game speed is DS:49E2 (<see cref="EclMemory.GameSpeed"/>): the
    /// Done menu's Speed changes it for the rest of the game, as the camp's Alter Speed does.
    /// </summary>
    public void Fight(EncounterState state, GoldBox.Core.Game.Fight fight)
    {
        OtherLayout();
        PartyMenuUnder = false; // the combat screen replaces a party menu page
        _freedPicture = ViewPicture; // 0xE40D: the fight start frees the frame table
        int speed = memory().GameSpeed;
        FightView.Speed = (byte)(speed <= 9 ? speed : 0);
        FightView.Dungeon = memory().ViewMode == 1; // DUNGCOM only when DS:49FA = 1, else WILDCOM (0xD81B; combat-menu.md "Ground")
        // The fight start's line on the area screen (0xE430–0xE452). The program shows it before the arena is built;
        // the build draws nothing and only the arena's own dice, so showing it after FightKit.Create changes nothing.
        PlaySong?.Invoke(2);
        Chooser.BattleBegins();
        Chooser.Attach(fight);
        fight.State.Sound = Sound;
        try
        {
            fight.Run();
        }
        finally
        {
            Chooser.Detach(fight);                 // copies the menu's speed into the view
            memory().GameSpeed = FightView.Speed;
        }
    }

    /// <summary>The NPC share window, the summary and the loot menu (<see cref="VenueScreens.Loot"/>), DS:49F3 = 6.</summary>
    public void Loot(EncounterState state, AfterCombatOutcome outcome)
    {
        if (Trade(state) is not { } t)
            return;
        OtherLayout();
        Interface = Venue.Loot;
        PartyMenuUnder = false; // the window 03F1:02E8 (1, 1, 38, 22) over the whole page, then START 0x3379 case 6
        LootDrawn = false;
        try
        {
            VenueScreens.Loot(t, outcome, state.Duel, Game()?.Encounter.NpcsTakingShare ?? []);
        }
        finally
        {
            Interface = null;
            LootDrawn = false;
            Window = null;
            WindowAfterFight = false;
            ViewOverlay = null; // [?] as after the shop: the area display is drawn again after the loot step
        }
    }

    /// <summary>
    /// The "party destroyed" screen after a fight (0x6B37–0x6BAA, DOSBox-X s22): a full-screen window (START 03F1:02E8
    /// with 1, 1, 38, 22) with its title centred on row 1 in white, the message through the text routine in the box
    /// (2, 5)–(37, 22), colour 10, cleared first (so it starts at column 2, row 5 and wraps at column 37), then the key
    /// line of START 0x7C85 in colour 13 and a key. Then the program quits (ExploreScreen).
    /// </summary>
    public void PartyDestroyed(EncounterState state)
    {
        PlaySong?.Invoke(5);
        var t = Resources.Texts;
        var box = new TextWindow(1, 1, 38, 22);
        string title = t[Msg.EndTitle];
        box.Put(1, Math.Max(1, (1 + 38 - title.Length) / 2), title, 15);
        WindowAfterFight = true;
        KeepsCombatRow24 = true;
        TypesInto(box); // before 0x6BAF: typed when DS:4A00 was set as the fight began
        try
        {
            box.PrintIn(2, 5, 37, 22, t[Msg.MonstersRejoice], clear: true, colour: 10);
            Window = box.WindowLines();
            runner.Ask(new KeyRequest(t[Msg.PressAnyKey]) { Original = t.Raw(Msg.PressAnyKey) });
        }
        finally
        {
            Window = null;
            WindowAfterFight = false;
            KeepsCombatRow24 = false;
        }
    }

    // ---- ECL DAMAGE (GAME.OVR 0x34BB, the line routine 0x959F; DOSBox-X s22) ----------------------------------------

    /// <summary>
    /// One DAMAGE line (0x959F, before the hit is applied): two spaces, the name and the dies text (HP + 10 below the
    /// damage) or the hit text, the damage and the points text. When the cursor row is already below 22 it goes to
    /// row 17 and the white press-enter line waits first (the box is then cleared by the print). The column is set to
    /// 38 so the line starts on a new row; it is printed in the text box (1, 17)–(38, 22) in colour 15, so a full box
    /// shows the text routine's page prompt. The party list is drawn again after each hit (00BA:0767; the Client draws
    /// it from memory every frame).
    /// </summary>
    public void DamageReport(EclDamageReport report)
    {
        var t = Resources.Texts;
        string line = "  " + report.Target.Name + (report.Dies
            ? t[Msg.DamageDies]
            : t[Msg.DamageHitFor] + report.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + t[Msg.DamagePoints]);
        bool clear = false;
        if (Text.Row > TextWindow.Bottom)
        {
            Text.MoveTo(Text.Column, TextWindow.Top);
            clear = true;
            runner.Ask(new KeyRequest(t[Msg.DamageHitContinue])
            {
                Colour = 15,
                Original = t.Raw(Msg.DamageHitContinue),
                AmigaPen = GoldBox.Data.Module.Por.PorAmigaText.KeyDamagePen,   // hunk 4 +0x2364–0x236E: pen 28
            });
        }
        Text.MoveTo(TextWindow.Right, Text.Row);
        Text.Print(line, clear, colour: 15);
        // 0x96D0: cells (17, 1)–(38, 15) are blanked before the party list is drawn again, so the position line on
        // row 15 is gone until the next redraw point (DOSBox-X s22).
        PositionLineErased = true;
        ReleaseRosterNames(); // 0x96DD: START 0x0767 after each line
    }

    /// <summary>
    /// The end of DAMAGE (0x375A–0x37E4). Party killed: a full-screen window whose title the message's clear erases at
    /// once, the message at column 1, row 1 in colour 10, Delay(3000) (a fixed 3 s; the bottom line keeps what was
    /// there, e.g. the page prompt), then the program's end. Every DAMAGE then shows the white press-enter line on the
    /// bottom row (START 0x7C85, colour 15) and waits for a key; after a wipe the program exits to DOS. The Amiga
    /// (hunk 16 +0x4674–0x46DC) draws the same window, title and message, starts song 5 and goes straight to its
    /// press-enter key wait (pen 28 at column 1): no Delay, so row 24 never shows the stale prompt.
    /// </summary>
    public void DamageEnd(bool partyKilled)
    {
        var t = Resources.Texts;
        if (partyKilled)
        {
            PlaySong?.Invoke(5);
            var box = new TextWindow(1, 1, 38, 22);
            TypesInto(box);
            box.PrintIn(1, 1, 38, 22, t[Msg.PartyKilled], clear: true, colour: 10);
            Window = box.WindowLines();
            // Row 24 keeps what was there: the page prompt, or else the command line (DOSBox-X s22, a one-line wipe).
            // The Amiga has no hold (AmigaView.PartyKilledHold): its press-enter line comes at once.
            if (AmigaView.PartyKilledHold(AmigaView.On) is { } hold)
            {
                (string, byte)? stale = _pagePrompted ? ((Resources.PagePrompt ?? "PRESS A KEY").ToUpperInvariant(), (byte)13) : null;
                runner.Ask(new DelayRequest(hold) { BottomLine = stale });
            }
        }
        try
        {
            runner.Ask(new KeyRequest(t[Msg.DamageContinue])
            {
                Colour = 15,
                Original = t.Raw(Msg.DamageContinue),
                AmigaPen = GoldBox.Data.Module.Por.PorAmigaText.KeyDamagePen,   // hunk 16 +0x46D2–0x46DC: pen 28
            });
        }
        finally
        {
            Window = null;
            _pagePrompted = false;
        }
    }

    public void Shop(EncounterState state) => InVenue(state, VenueScreens.Shop);
    public void Temple(EncounterState state) => InVenue(state, VenueScreens.Temple);

    /// <summary>Shop and temple: DS:49F3 = 1 while the menu runs.</summary>
    private void InVenue(EncounterState state, Action<TradeContext> screen)
    {
        if (Trade(state) is not { } t)
            return;
        OtherLayout();
        Interface = Venue.Shop;
        PartyMenuUnder = false; // the entry's START 0x3379 case 1 (0x721E, 0x528B) replaces a party menu page
        VenuePictureOver = false;
        try
        {
            screen(t);
        }
        finally
        {
            Interface = null;
            VenuePictureOver = false;
            // [?] The shop and temple screens replace the area display; when the program draws the 3D view again is
            // not read. CLRMON before them clears the picture flag, so the script's PICTURE 255 does not restore it:
            // we go back to the view here.
            ViewOverlay = null;
        }
    }

    /// <summary>A full-screen window over the screen (the fight summary, 0x5E94), or null.</summary>
    public IReadOnlyList<WindowLine>? Window { get; set; }

    /// <summary>
    /// A list page left on the screen under the text box (<see cref="TradeContext.ShowPageUnder"/>: the camp's spell list
    /// while a cast runs, P + D s29/s30), or null.
    /// </summary>
    public ListRequest? PageUnder { get; set; }

    /// <summary>With <see cref="PageUnder"/>: the text box is drawn over the page (false: the page alone, <see cref="TradeContext.ShowPageAlone"/>).</summary>
    public bool PageUnderBox { get; set; } = true;

    /// <summary>The text box's frame was drawn again over the screen's (<see cref="TradeContext.FrameTextBox"/>).</summary>
    public bool TextBoxFramed { get; set; }

    /// <summary>The position line is held as last drawn (<see cref="TradeContext.HoldPositionLine"/>).</summary>
    public bool PositionLineHeld { get; set; }

    /// <summary>
    /// The window came up over the combat screen (the party-destroyed page, the NPC share): no area command line on row
    /// 24 while its text types, unlike the DAMAGE wipe's window over the area screen, whose command line stays. The
    /// party-destroyed page shows the fight's last row 24 there (<see cref="KeepsCombatRow24"/>; blank in s29's run).
    /// </summary>
    public bool WindowAfterFight { get; set; }

    /// <summary>
    /// The party-destroyed or NPC share window is up: row 24 keeps what the combat screen last showed there until the
    /// key line or menu.
    /// From the party's defeat (0x6AA5: the results 0x5A47 and 0x672A call no screen unit, 0709 / 03F1 / 0464) to the window
    /// (03F1:02E8 over rows 0–23), its text (0709:0768 in (2, 5)–(37, 22)) and the key line (0709:0BF5 at 0x6BAA),
    /// nothing writes row 24 (DOSBox-X s39: the vampire's "MOVE/ATTACK, MOVE LEFT = 11" under the typed text; s29's run
    /// left the row blank because the fight had).
    /// </summary>
    public bool KeepsCombatRow24 { get; private set; }

    /// <summary>The interface state the screens are in (DS:49F3: 1 shop/temple, 2 camp, 6 loot), null while exploring.</summary>
    public Venue? Interface { get; private set; }

    /// <summary>The context of the money and item screens on <paramref name="state"/>'s pool and treasure list.</summary>
    internal TradeContext? Trade(EncounterState? state)
    {
        if (memory().Party is not { } party)
            return null;
        var game = Game();
        return new TradeContext
        {
            Dialog = Dialog,
            ShowWindow = w =>
            {
                Window = w;
                if (w is null)
                    WindowAfterFight = KeepsCombatRow24 = false;
            },
            TypesInto = box =>
            {
                // The NPC share window (0x6866 → 0x695E) comes after the removal 0x672A like the party-destroyed one:
                // only the window 03F1:02E8, its lines 0709:0768 and then the menu routine 03C5:002A (row 24), so row
                // 24 keeps the fight's last line while the lines type (DOSBox-X s39: "CONTINUE BATTLE:YES NO").
                WindowAfterFight = true;
                KeepsCombatRow24 = true;
                TypesInto(box);
            },
            ShowPageUnder = p => (PageUnder, PageUnderBox) = (p, true),
            ShowPageAlone = p => (PageUnder, PageUnderBox) = (p, false),
            FrameTextBox = on => TextBoxFramed = on,
            HoldRosterName = HoldRosterName,
            RosterDrawn = ReleaseRosterNames,
            VenueRedrawn = () =>
            {
                VenuePictureOver = false;
                if (Interface == Venue.Loot)
                    LootDrawn = true;
            },
            RosterDrawnLit = ReleaseRosterNamesLit,
            HoldPositionLine = on => PositionLineHeld = on,
            Text = Resources.Texts,
            Party = party,
            Pool = state?.Money ?? new uint[7],
            Treasure = state?.TreasureItems ?? [],
            Random = n => game is null ? 0 : game.Rng.Next((ushort)n),
            Word = Resources.ItemWord,
            IsScroll = Resources.IsScroll,
            ItemTypes = Resources.ItemTypes,
            TempleNames = Resources.TempleNames,
            Halt = Halt,
            ViewPage = ViewPage,
            ItemsPage = ItemsPage,
            TalkOverItemsPage = on => ItemsTalk?.Invoke(on ? Text : null),
            Coins = Coins,
            Memory = memory(),
            PickMenu = Resources.TargetMenu,
            // Trade's START 0x3379 (0x22781, 0x231A4): from the command line (no venue) the exploring layout for
            // DS:49F3 = 3 / 4, as View's exit and Cast draw it; in the camp, shop, temple and loot menu the text box
            // cleared and the party list drawn (as the camp's Cast target pick, 0x27AFA).
            InterfaceRedraw = () =>
            {
                if (Interface is null)
                {
                    DrawInterface();
                    return;
                }
                Text.Clear();
                TextBoxFramed = false;
                ReleaseRosterNames();
            },
        };
    }

    /// <summary>View's Items page and Drop's coin list (0x21F32, 0x234E1; the party menu's, <see cref="PartyMenuFlow"/>),
    /// set with <see cref="ViewPage"/>.</summary>
    public Func<EclCharacter, IReadOnlyList<ItemsEntry>, int, Func<byte[], string>, ListAnswer>? ItemsPage { get; set; }
    public Func<EclCharacter, IReadOnlyList<int>, CoinTexts?, (int Kind, ushort Amount)?>? Coins { get; set; }

    /// <summary>The text window Sell / Id talk in over <see cref="ItemsPage"/> (null when they are done), set with it.</summary>
    public Action<TextWindow?>? ItemsTalk { get; set; }

    /// <summary>
    /// View's page and menu as the program draws them (0x21AED; the party menu's, <see cref="PartyMenuFlow.ViewPage"/>),
    /// for the camp's, shop's, temple's and loot menu's View; null: <see cref="CharacterView"/>'s own page.
    /// </summary>
    public Func<EclCharacter, IReadOnlyList<ViewEntry>, int>? ViewPage { get; set; }

    /// <summary>The run-time error the original program would have quit to DOS with; the game is over (null = running).</summary>
    public ProgramHaltException? Halted { get; set; }

    /// <summary>
    /// The original quits to DOS here (a Turbo Pascal run-time error, e.g. 200 in Share with no member to take a share):
    /// the game ends. Our message in the text window and a key, then the Client closes (<see cref="Quit"/>); the caller
    /// throws the halt on, so the logic stops where the program did.
    /// </summary>
    public void Halt(ProgramHaltException e)
    {
        Halted = e;
        Log.Add($"program halt: run-time error {e.Code} ({e.Message})");
        Console.Error.WriteLine($"game logic: the original program halts here with run-time error {e.Code}: {e.Message}");
        PrintAtOnce(HaltNotice(e));
        runner.Ask(new KeyRequest());
        Quit?.Invoke();
    }

    /// <summary>The line shown when the original program would quit to DOS (our wording).</summary>
    public static string HaltNotice(ProgramHaltException e) =>
        $"RUN-TIME ERROR {e.Code.ToString(CultureInfo.InvariantCulture)}: THE ORIGINAL GAME QUITS TO DOS HERE. THE GAME IS OVER.";

    /// <summary>[?] The item maker (GAME.OVR 0x25C99) is not read: a blank item record of the type, as the headless runner.</summary>
    public byte[] MakeRandomItem(byte type)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = type;
        return r;
    }

    // ---- IAreaUi ---------------------------------------------------------------------------------------------------
    public DoorAction? ChooseDoorAction(DoorBarrier barrier, IReadOnlyList<DoorAction> choices)
    {
        var items = choices.Select(c => c.ToString().ToUpperInvariant()).Append("EXIT").ToList();
        // Our wording for the menu's title; the program's (GAME.OVR cs:0xD64) is not reproduced.
        int i = Choose(barrier == DoorBarrier.Barred ? "A BARRED DOOR." : "A LOCKED DOOR.", items, cancel: items.Count - 1);
        return i >= 0 && i < choices.Count ? choices[i] : null;
    }

    /// <summary>The bump of a refused step or failed door (OVR 0x176FE): cue DS:2622.</summary>
    public void Bump() => Sound.Cue(GoldBox.Core.Sound.SoundCue.Impact);

    /// <summary>
    /// The command routine's two messages (0x170F1: A where $49FB ≠ 0, 3D view only; 0x16FEE: the overland step onto a
    /// river while $4AA2 = 0) go through START 0x7DF8 with colour 14 (<see cref="Flash"/>).
    /// </summary>
    public void Message(AreaMessage message) =>
        Flash(Resources.Texts[message == AreaMessage.NoAreaMapHere ? Msg.NoAreaMapHere : Msg.RiverBlocks], 14);

    /// <summary>
    /// The bottom-line message START 0x7DF8 (0x709:0xD68; the combat routine 0x2233 with a colour argument): row 24
    /// cleared (0x767B over columns 0–39), the text at column 0 in <paramref name="colour"/> (0x76B6), PauseOnly
    /// (0x7E4E: Delay(DS:49E2 × 225) ms), row 24 cleared. The rest of the screen stays as it was; the command line comes
    /// back with the next command read. Speed 0 waits 0 ms, so nothing is seen.
    /// </summary>
    public void Flash(string text, byte colour)
    {
        int speed = memory().GameSpeed;
        if (speed is > 0 and <= 9)
            runner.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * speed)) { BottomLine = (text, colour) });
    }

    /// <summary>
    /// The program's camp and rest variables (DS:6DC2, DS:6DD8, DS:49FB, DS:471E, DS:6DD0): the game's
    /// (<see cref="GoldBox.Data.Por.PorGame.Camp"/>, shared with the clock of exploration and the fights' end of round);
    /// a set of the Client's own only while no game is built.
    /// </summary>
    public CampState CampState => Game()?.Camp ?? _noGameCamp;
    private readonly CampState _noGameCamp = new();

    /// <summary>
    /// The camp's effect engine, set up as the game's outside a fight (<see cref="GoldBox.Data.Por.PorGame.OutsideEffects"/>):
    /// the race-name tables, the experience table and the level seams (<see cref="FightKit.WireLevels"/>: the race level
    /// limit, the level stats 0x2AA87). A fresh one per camp: the camp's spells hook their own caster into it.
    /// </summary>
    public EffectHandlers CampEffects(EclParty party)
    {
        var game = Game();
        Func<ushort, ushort> random = n => game is null ? (ushort)0 : game.Rng.Next(n);
        var kit = game?.Kit;
        var effects = new EffectHandlers(random, kit?.Names)
        {
            List = () => party.Members,
            CurrentCharacter = () => party.Current,
            ExperienceTable = kit?.Experience,
        };
        FightKit.WireLevels(effects, kit?.LevelTables);
        return effects;
    }

    private readonly HashSet<GoldBox.Data.Por.PorGame> _effectsShown = [];

    /// <summary>
    /// Shows the handler messages of the game's effect engine outside a fight (<see cref="GoldBox.Data.Por.PorGame.OutsideEffects"/>:
    /// the effect timeout after each clock advance while exploring, read s30) on the area's text window, START 0x16EC's
    /// out-of-fight layout with DS:6DDA = 0 (<see cref="OutsideEffectMessages"/>). Once per game.
    /// </summary>
    public void ShowOutsideEffects(GoldBox.Data.Por.PorGame g)
    {
        if (!_effectsShown.Add(g))
            return;
        var shown = new OutsideEffectMessages(Text, runner.Ask, () => memory().GameSpeed, Resources.CombatTexts, () => false,
            cue => Sound.Cue(cue), revived: HoldRosterName, listDrawn: ReleaseRosterNames);
        g.OutsideEffects.Event += (c, e) => shown.Show(c, e, g.OutsideEffects);
        FollowInterface(g); // START 0x3379 while exploring (a load's entry, View, Cast)
    }

    private readonly HashSet<EclCharacter> _rosterHeld = [];
    private EclCharacter? _rosterDrawnCurrent;
    private Dictionary<EclCharacter, EclCharacter>? _rosterDrawn;

    /// <summary>The member the list shows as current: the player's pick while a script's LOADCHAR has changed DS:5D92.</summary>
    private EclCharacter? ShownCurrent() =>
        memory().Party is { } p && p.ChangedByScript && p.Selected is { } pick ? pick : memory().Party?.Current;

    /// <summary>A member the party list still shows out of play (light red) until it is drawn again (<see cref="TradeContext.HoldRosterName"/>).</summary>
    public void HoldRosterName(EclCharacter c)
    {
        lock (_rosterHeld)
            _rosterHeld.Add(c);
    }

    /// <summary>
    /// The party list is drawn again (START 0x0767 / 0x1307, or the screen redrawn after a full-screen page): held names
    /// show their colour again, and the list's values (names, colours, AC, HP) are taken from memory now and shown as
    /// drawn until the next redraw point (D s39: a heal's new HP shows only when the list is drawn after its message).
    /// </summary>
    public void ReleaseRosterNames() => DrawRoster(false, null);

    /// <summary>The party list drawn with <paramref name="lit"/> as its lit member (none for null) instead of the current
    /// one: START's member pick 0x37E8 passes its pick to 00BA:0767 (00BA:2CA1); DS:5D92 does not change.</summary>
    public void ReleaseRosterNamesLit(EclCharacter? lit) => DrawRoster(true, lit);

    /// <summary>The lit member of the last list drawn by the member pick, while that drawing is on screen.</summary>
    public bool RosterLitOverride(out EclCharacter? lit)
    {
        lock (_rosterHeld)
        {
            lit = _rosterLit;
            return _rosterLitSet;
        }
    }

    private EclCharacter? _rosterLit;
    private bool _rosterLitSet;

    private void DrawRoster(bool litSet, EclCharacter? lit)
    {
        Dictionary<EclCharacter, EclCharacter>? drawn = null;
        EclCharacter? current = null;
        try
        {
            if (memory().Party is { } p)
            {
                drawn = new Dictionary<EclCharacter, EclCharacter>(ReferenceEqualityComparer.Instance);
                foreach (var m in p.Members.ToArray())
                    drawn[m] = new EclCharacter(m.Record, m.Record.Length);
                current = ShownCurrent();
            }
        }
        catch (InvalidOperationException)
        {
            return; // the list changed while it was copied: the next redraw point takes it
        }
        lock (_rosterHeld)
        {
            _rosterHeld.Clear();
            _rosterDrawn = drawn;
            _rosterDrawnCurrent = current;
            _rosterLitSet = litSet;
            _rosterLit = lit;
        }
    }

    public bool RosterNameHeld(EclCharacter c)
    {
        lock (_rosterHeld)
            return _rosterHeld.Count > 0 && _rosterHeld.Contains(c);
    }

    /// <summary>A member as the party list last drew it (itself before the first redraw point, or when it joined since).</summary>
    public EclCharacter RosterAsDrawn(EclCharacter c)
    {
        lock (_rosterHeld)
            return _rosterDrawn is { } d && d.TryGetValue(c, out var shown) ? shown : c;
    }

    /// <summary>Another current member: picking one redraws the party list (0x23819; D s38: Home at the camp menu).</summary>
    public void ReleaseRosterNamesIfPicked(EclCharacter? current)
    {
        bool picked;
        lock (_rosterHeld)
            picked = _rosterDrawn is null || !ReferenceEquals(current, _rosterDrawnCurrent);
        if (picked)
            ReleaseRosterNames();
    }

    /// <summary>Where the camp's Save writes (the player's save folder; the game folder is never written).</summary>
    public string? SaveFolder { get; set; }

    /// <summary>Quit to DOS (the camp's Save and Drop questions): closes the Client.</summary>
    public Action? Quit { get; set; }

    /// <summary>
    /// The icon editor 0x1D1AC on a character (the party menu's, <see cref="PartyMenuFlow.EditIcon"/>), run by the camp's
    /// Alter Icon on the logic thread; null: the camp says it is missing.
    /// </summary>
    public Action<EclCharacter>? IconEditor { get; set; }

    /// <summary>
    /// The camp menu (0x196A0, <see cref="VenueScreens.Camp"/>) after vector 2; true when a rest was interrupted (the
    /// area loop then runs vector 3).
    /// </summary>
    public bool Camp()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return false;
        var before = Interface;
        OtherLayout(); // D s43: the camp on the overland shows the joint at (16, 16), and so does the screen after its Exit
        Interface = Venue.Camp;
        var mem = memory();
        byte state = mem.InterfaceState;
        mem.InterfaceState = 2; // 0x196AC; the old value back on the way out (0x197F2)
        try
        {
            PlaySong?.Invoke(1);
            return VenueScreens.Camp(CampContextFor(t), Resources.CampMenu);
        }
        finally
        {
            PlaySong?.Invoke(0);
            Interface = before;
            mem.InterfaceState = state;
        }
    }

    /// <summary>
    /// The command line's V (0x1710F / 0x17327 → 0x21AED): the View page and menu on the current member, as the camp's,
    /// with DS:49F3 = 3 or 4 (Items offers Use, no Sell / Id); its exit (0x21D4F) draws START 0x3379 for the state.
    /// </summary>
    public void View()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return;
        CharacterView.Run(t, Venue.Explore);
        DrawInterface();
    }

    /// <summary>
    /// The command line's C (0x170F8 / 0x1731C → 0x17CD2): the camp Magic menu's Cast outside the camp (<see
    /// cref="CampScreens.CastMenu"/>); its redraws (0x17D96 after a list, the target pick's 0x27AFA) are START 0x3379 for
    /// the interface state (<see cref="GoldBox.Core.Explore.AreaLoop.DrawInterface"/>), not the camp's.
    /// </summary>
    public void Cast()
    {
        if (Trade(Game()?.Encounter.State) is not { } t)
            return;
        CampScreens.CastMenu(CampContextFor(t, DrawInterface));
    }

    /// <summary>The camp's rules on this game: spell table, scroll test, the effect engine out of combat, the generator.</summary>
    private CampContext CampContextFor(TradeContext t, Action? interfaceRedraw = null)
    {
        var mem = memory();
        var game = Game();
        Func<ushort, ushort> random = n => game is null ? (ushort)0 : game.Rng.Next(n);
        var table = Resources.Spells ?? new SpellTable(new Dictionary<int, SpellInfo>());
        var spells = new CampSpells(table, it => Resources.ItemSlot(it) is > 10 and < 14);
        var effects = CampEffects(t.Party);
        var state = CampState;
        var rest = new CampRest(mem, state, spells, random, (c, e) => effects.RemoveEffect(c, e[0], e),
            c => effects.RestoreHitPoints(c, 1, false));
        return new CampContext
        {
            Trade = t,
            State = state,
            Spells = spells,
            Rest = rest,
            Memory = mem,
            Cast = Resources.Spells is null ? null : new CampCast(new FightSpells(table, effects, random), t.Party),
            Resources = Resources,
            Save = SaveGame,
            Quit = Quit,
            // On the overland the editor's end reloads icon slots 11 and 12 (0x1D84A), one of them a marker slot.
            EditIcon = IconEditor is { } edit ? c => { edit(c); game?.Loop.IconEditorDone(); } : null,
            Effects = effects,
            Cue = cue => Sound.Cue(cue),
            InterfaceRedraw = interfaceRedraw,
        };
    }

    /// <summary>
    /// The camp's Save (0x1FD3E): the wall sets into $4AFA–$4AFF (the picture flags $49FF are already the cell here),
    /// then the save as the program builds <c>SAVGAMx.DAT</c> (interface state 2, camp) with the party, written as an
    /// engine save <c>x.json</c> into <see cref="SaveFolder"/> (docs/module-format.md §15; <c>goldbox-import
    /// save-export</c> writes the original files from it). The save takes the location type from DS:49FA
    /// (<see cref="EclMemory.ViewMode"/>) and writes the game speed DS:49E2 (<see cref="EclMemory.GameSpeed"/>) into
    /// $49FC, as the program does.
    /// </summary>
    public string? SaveGame(char letter) => SaveGame(letter, 2);

    /// <summary>The save with the interface state DS:49F3 the program writes (2 camp, 0 the training hall).</summary>
    public string? SaveGame(char letter, byte interfaceState)
    {
        if (SaveFolder is null || memory().Party is not { } party || Game() is null)
            return "NO SAVE FOLDER.";
        var mem = memory();
        for (int k = 0; k < 3; k++)
            if (_wallSetKnown[k])
            {
                mem.Write((ushort)(0x4AFA + k), (ushort)(_wallSets[k] is { } w ? w & 0xFFFF : 0xFFFF));
                mem.Write((ushort)(0x4AFD + k), (ushort)(k + 1));
            }
        mem.MenuAnimates = mem.Read(GoldBox.Core.Explore.AreaLoop.MenuAnimateCell) != 0; // the cells written (0x1FE70)
        try
        {
            var bytes = GoldBox.Data.Por.PorSaveGame.Build(mem, party.Members.Count, letter, interfaceState);
            var content = Game()!.Content;
            var save = GoldBox.Data.Module.Por.PorEngineSave.FromOriginal(bytes, party,
                GoldBox.Data.Module.Por.PorEngineSave.Identity(content), content.LoadBlock, content.VmProfile);
            Directory.CreateDirectory(SaveFolder);
            var path = Path.Combine(SaveFolder, GoldBox.Data.Module.Por.PorEngineSave.FileName(letter));
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, save);
            Log.Add($"saved slot {letter} to {path}");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "SAVE FAILED: " + e.Message;
        }
    }

    /// <summary>
    /// The party menu's Save before Begin (0x19D9C → 0x1FD3E): the unstarted game as the program saves it
    /// (<see cref="GoldBox.Data.Por.PorGame.UnstartedSave"/>, interface state 0), written as an engine save
    /// <c>x.json</c> into <see cref="SaveFolder"/>; the menu's Load takes it back into the menu. After such a Load
    /// (<paramref name="loaded"/>) the loaded game's memory is saved instead
    /// (<see cref="GoldBox.Data.Por.PorGame.SaveAtPartyMenu"/>). Returns null when written, else what went wrong.
    /// </summary>
    public string? SaveAtPartyMenu(char letter, EclParty party, GoldBox.Data.Por.IPorContent content,
        GoldBox.Data.Por.PorGame? loaded = null)
    {
        if (SaveFolder is null)
            return "NO SAVE FOLDER.";
        try
        {
            var save = loaded is not null ? loaded.SaveAtPartyMenu(party, letter)
                : GoldBox.Data.Por.PorGame.UnstartedSave(content, party, letter);
            Directory.CreateDirectory(SaveFolder);
            var path = Path.Combine(SaveFolder, GoldBox.Data.Module.Por.PorEngineSave.FileName(letter));
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, save);
            Log.Add($"saved slot {letter} (party menu) to {path}");
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "SAVE FAILED: " + e.Message;
        }
    }

    /// <summary>The camp menu is up (the position line says CAMPING, DS:49F3 = 2).</summary>
    public bool Camping => Interface == Venue.Camp;
}
