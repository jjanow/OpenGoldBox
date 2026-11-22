namespace GoldBox.Client.Play;

/// <summary>What the game logic waits for from the player (or from the clock, for a delay).</summary>
public abstract record PlayerRequest;

/// <summary>
/// Pick one of <paramref name="Items"/>: a horizontal menu on the bottom line, or a vertical list in the text window
/// under <paramref name="Prompt"/>. The answer is the index. <paramref name="CancelIndex"/> is answered on Cancel
/// (null: Cancel does nothing, as in the original menus that have no way out).
/// </summary>
public sealed record ChoiceRequest(string? Prompt, IReadOnlyList<string> Items, bool Vertical, int? CancelIndex = null) : PlayerRequest
{
    /// <summary>Answers for the previous / next party member (the program's party selection 0x23819).</summary>
    public const int PreviousMember = -2, NextMember = -3;

    /// <summary>
    /// A prompt drawn on the menu line before the items, in colour 13, as the program's menu routine (0x2CC8C) does
    /// ("Camp:"); null = none.
    /// </summary>
    public string? MenuPrompt { get; init; }

    /// <summary>The items are an area script's text (MENU / HMENU / vertical menus), not the program's own menu words:
    /// the Amiga pages keep them as they are (<see cref="AmigaView.MenuCase(string)"/> is not applied).</summary>
    public bool ScriptText { get; init; }

    /// <summary>The Amiga menu call's own pens (words, capitals) when they are not the usual ones (HMENU's), or null.</summary>
    public (int Words, int Capitals)? AmigaPens { get; init; }

    /// <summary>The prompt's colour when the caller passes another than 13 (the yes/no routine 0x3C5:0x39 takes it as an
    /// argument: 13 or 14, e.g. 14 for the camp's keep questions 0x1818B / 0x182BA and the quit question 0x19791). Under the Amiga
    /// pages an interface pen (16–30) the Amiga caller passes (24 for DOS's 14, AmigaView.YesNoPrompt).</summary>
    public byte? MenuPromptColour { get; init; }

    /// <summary>
    /// The menu routine's word colours when the caller passes others than 10 / 15 (DOS): the words in
    /// <c>Words</c>, the lit word and every hotkey letter in <c>Lit</c>. The yes/no routine 0x3C5:0x39 takes them as
    /// arguments; three of its 26 calls in GAME.OVR pass 13, 15, 10 (the temple's pay question 0x46CE, the camp cast's
    /// two questions 0x27CCA / 0x27D30): the words white, the lit one and the hotkeys green (DOSBox-X s46, the pay
    /// question: "Y" green, "ES" white, "NO" green). Null: 10 / 15.
    /// </summary>
    public (byte Words, byte Lit)? MenuColours { get; init; }

    /// <summary>
    /// Each item's column from the menu's start, when the program's menu line has more than one space between items:
    /// the menu routine draws the line as it is (D s33: the rest-time menu "Rest   daYs Hours Mins   Inc Dec   Exit").
    /// Null: one space between items.
    /// </summary>
    public IReadOnlyList<int>? Columns { get; init; }

    /// <summary>
    /// The menu also takes the party selection keys (the original's Home / End, and G / O in the shop, temple and
    /// loot menus): answered <see cref="PreviousMember"/> / <see cref="NextMember"/>.
    /// </summary>
    public bool PartySelect { get; init; }

    /// <summary>Extra hotkeys that answer as <see cref="PreviousMember"/> / <see cref="NextMember"/> (G and O where the program tests them).</summary>
    public string? MemberLetters { get; init; }

    /// <summary>
    /// The hotkey of each item (one character per item), when it is not the item's first letter: the program's menu
    /// routine takes every capital letter of its line as a hotkey ("daYs" → Y). Null = first letters.
    /// </summary>
    public string? Hotkeys { get; init; }

    /// <summary>The item highlighted first (the program's yes/no for a drop starts on No; the combat screen).</summary>
    public int Selected { get; init; }

    /// <summary>
    /// A vertical list drawn as the program's VMENU draws it (GAME.OVR 0x1C4E → 0x8C5B → list routine 0x2D669): the
    /// prompt is already in the text window (<see cref="ClientPlayer.Text"/>), the items go at column 1 from this row
    /// to row 22, the highlighted one in colour 15, the others in 10, no cursor glyph; row 24 holds only the list's
    /// Next / Prev words (<see cref="Paging"/>). Null: the Client's own layout (the prompt on row 17, a cursor glyph).
    /// </summary>
    public int? ListRow { get; init; }

    /// <summary>With <see cref="ListRow"/>: the list routine's Next / Prev words (its window is <see cref="ListRow"/> to
    /// row 22); null: no paging words.</summary>
    public ListPaging? Paging { get; init; }

    /// <summary>
    /// The menu routine's animate flag ([bp+0xA] of 0x2CC8C): while this menu waits, the picture window's animation
    /// plays (<see cref="PictureAnimation"/>). Set by the camp's menus and, while a close-up is shown, by HMENU and
    /// ENCMENU (read s19).
    /// </summary>
    public bool Animates { get; init; }

    /// <summary>
    /// Opened while VM $4AFF was not 0: the menu routine advances the frames on every pass without waiting for their
    /// delays (0x2CDD6; user s42: copied, each frame lasting one Client update as delay-0 frames do, s27).
    /// </summary>
    public bool EveryPass { get; init; }

    /// <summary>Items answered by the Up / Down actions of a horizontal menu (the rest-time menu's Inc / Dec); null = none.</summary>
    public int? UpAnswer { get; init; }
    public int? DownAnswer { get; init; }

    /// <summary>
    /// Answers for Left / Right and Confirm of a horizontal menu that takes them itself instead of moving the highlight
    /// (the rest-time menu 0x244ED: Left / Right move the time field, Enter rests; D s38: row 24 keeps its lit word);
    /// null = the usual highlight move and pick.
    /// </summary>
    public int? LeftAnswer { get; init; }
    public int? RightAnswer { get; init; }
    public int? ConfirmAnswer { get; init; }

    /// <summary>The hotkey of item <paramref name="i"/>.</summary>
    public char HotkeyOf(int i) => Hotkeys is { } h && i < h.Length ? h[i] : Items[i].Length > 0 ? char.ToUpperInvariant(Items[i][0]) : '\0';
}

/// <summary>
/// Wait for any key (the program's "press a key" stops): <paramref name="Prompt"/> on the bottom line in colour 13
/// for the keyboard (the device prompt for pad and mouse); null = the text window's page prompt.
/// </summary>
public sealed record KeyRequest(string? Prompt = null) : PlayerRequest
{
    /// <summary>
    /// The prompt is a menu of the menu routine 0x2CC8C with every colour 15 (the "press enter" text after the fight
    /// summary, 0x6071): drawn white, and the shared highlight goes back to the first entry when it is past the menu's
    /// last (0x2CCE7). See <see cref="MenuEntries"/>.
    /// </summary>
    public bool AsMenu { get; init; }

    /// <summary>
    /// The menu's entries as the routine counts them (0x2C99A, read s21): an entry starts at each digit or capital letter
    /// of the text in its original case (the set at the unit's CS:00AB). The program's press-enter text is in lower
    /// case: no entries, so the cell always goes back to the first (DOSBox-X s21: after the summary the loot menu opens
    /// on View).
    /// </summary>
    public int MenuEntries { get; init; }

    /// <summary>
    /// With <see cref="AsMenu"/>: opened while VM $4AFF was not 0 with a picture in the frame table, so the menu routine
    /// draws the frames at cell (3, 3) over the page on every pass (0x2CD5D–0x2CDD6; DOSBox-X s44: PICTURE 2 over the
    /// summary window after a COMBAT with no monsters from a start-up Load's vector 4). <see cref="ClientPlayer.MenuAsked"/>.
    /// </summary>
    public bool EveryPass { get; init; }

    /// <summary>The prompt's text in the program's own case (the Amiga draws it so: <see cref="AmigaView"/>).</summary>
    public string? Original { get; init; }

    /// <summary>The text window's page prompt (START 0x7A12's wait; the Amiga's own text and pen, <see cref="AmigaView.KeyPen"/>).</summary>
    public bool PagePrompt { get; init; }

    /// <summary>The Amiga caller's pen, by its name in the imported call-site pens (<see cref="GoldBox.Data.Module.Por.PorAmigaText.Calls"/>),
    /// where DOS passes another colour; null: the DOS colour's pen.</summary>
    public string? AmigaPen { get; init; }

    /// <summary>The prompt's colour when it is not 13 (START 0x7C85 takes it as an argument: DAMAGE's line is 15).</summary>
    public byte? Colour { get; init; }

    /// <summary>Entries of a menu text by the menu routine's rule (<see cref="MenuEntries"/>).</summary>
    public static int CountEntries(string original) => original.Count(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));
}

/// <summary>A cell of a full-screen window (START 03F1:02E8 with 1, 1, 38, 22): text at a column and row, in a colour.</summary>
public sealed record WindowLine(int Column, int Row, string Text, byte Colour = 10)
{
    /// <summary>Printed text (the text routine, <see cref="TextWindow.WindowLines"/>): the Amiga draws it glyph by glyph
    /// (hunk 25 +0x10EC → +0xA4E), not as one string; false for a string drawn whole (the string routine).</summary>
    public bool Glyphs { get; init; }
}

/// <summary>A row of a full-screen list: its text and colour (selectable rows are drawn by the screen in its colours).</summary>
public sealed record ListLine(string Text, byte Colour = 10, int Column = 1);

/// <summary>
/// A full-screen page (the program's item lists and character view): fixed <paramref name="Lines"/> at the top
/// (title, header, character data), then <paramref name="Rows"/> to pick from (may be empty), and the actions on the
/// bottom line. The answer is a <see cref="ListAnswer"/>. <paramref name="CancelAction"/> is answered on Cancel.
/// </summary>
public sealed record ListRequest(IReadOnlyList<ListLine> Lines, IReadOnlyList<string> Rows, IReadOnlyList<string> Actions,
    int? CancelAction, int FirstRow = 3, int VisibleRows = 18) : PlayerRequest
{
    /// <summary>The row picked first.</summary>
    public int Selected { get; init; }

    /// <summary>The action highlighted first (the one used last, when the page comes back).</summary>
    public int SelectedAction { get; init; }

    /// <summary>Actions that need a row (greyed out when the list is empty).</summary>
    public IReadOnlySet<int> RowActions { get; init; } = new HashSet<int>();

    /// <summary>The menu also takes the party selection keys (see <see cref="ChoiceRequest.PartySelect"/>).</summary>
    public bool PartySelect { get; init; }

    /// <summary>Rows that are labels (the spell lists' level headings, the Display list's names): shown, never picked.</summary>
    public IReadOnlySet<int> Labels { get; init; } = new HashSet<int>();

    /// <summary>
    /// The page's menu is the menu routine's own (the View page 0x21AED): it opens on the shared highlight
    /// (<see cref="MenuHighlight"/>) instead of <see cref="SelectedAction"/>. Otherwise the page is an item list
    /// (0x2D669), whose menu starts on <see cref="SelectedAction"/> and leaves the shared highlight where it ends.
    /// </summary>
    public bool SharedHighlight { get; init; }

    /// <summary>The list routine 0x2D669's window and its Next / Prev words (null: the Client's scrolling list).</summary>
    public ListPaging? Paging { get; init; }

    /// <summary>With <see cref="Paging"/>: the window's first row on entry (DS:6B29, kept by a caller that keeps its row).
    /// The screens use the shared word instead (<see cref="MenuHighlight.ListTop"/>, <see cref="GoldBox.Client.Screens.ListPage.Shared"/>),
    /// which is the program's: this value serves a page without it.</summary>
    public int Top { get; init; }

    /// <summary>
    /// With <see cref="Paging"/>: the caller takes the highlighted row on any key the list routine returns but Escape, E
    /// and 0 (Take's coin kinds, 0x25B14 → 0x25B25), so Return (the pad's A, a click on the lit row) answers
    /// <see cref="RowPicked"/> with the row whatever word is lit; the words still answer their own index.
    /// </summary>
    public bool EnterPicks { get; init; }

    /// <summary>The answer's action when <see cref="EnterPicks"/> took the row.</summary>
    public const int RowPicked = -20;

    /// <summary>With <see cref="Paging"/>: the spell list's own screen (0x239D6 / 0x270A2), drawn instead of <see cref="Lines"/>.</summary>
    public SpellPageLayout? Spell { get; init; }

    /// <summary>
    /// In a fight: the combat palette stays on (EGA 0 and 8 swapped, <see cref="CombatPage.Grey"/>), as for the menu's
    /// Cast list (0x14C0F → 0x239D6; D s33: the list on dark grey); View's pages restore the normal palette.
    /// </summary>
    public bool Grey { get; init; }

    /// <summary>
    /// The keyboard's Return runs the first action word on the highlighted row whatever word is lit: the list routine
    /// returns 13 with the row and the caller acts on 13 (the heal list 0x50F2: DOSBox-X s46, Exit lit, Return asked
    /// for the lit row's cure). The pad's confirm and a click take the lit word.
    /// </summary>
    public bool ReturnRunsFirstAction { get; init; }
}

/// <summary>
/// The spell list's screen (P + D, s33; combat-menu.md "Spell lists' Next / Prev"). 0x239D6, when its position word is
/// below 0, draws the window (1, 1)–(38, 1) (START 03F1:02E8: inside cleared, frame one cell outside), the name with
/// "'s" at (1, 1) in the name colour (START 0x1816 with its flag: CS:1813) and "Spells " + the list's words at
/// (name length + 4, 1) in green; 0x270A2 draws the window (1, 3)–(38, y1) (y1 22, Memorize's 15). The list routine's
/// rows: labels in colour 13, the others green, the highlighted one white, no cursor. Row 24: <see cref="Prompt"/> in
/// colour 13 (the choose-spell words, actions 1–4), then the words; with no prompt the menu line starts with the space
/// of " Next" / " Exit" (column 1). <see cref="Box"/>: Memorize's slot lines (0x17DD5) in the text box (1, 17)–(38, 22),
/// whose frame START 0x16EC's camp branch draws again.
/// </summary>
public sealed record SpellPageLayout(string Name, byte NameColour, string Title)
{
    /// <summary>The prompt before the menu words on row 24 (its trailing space included), or null.</summary>
    public string? Prompt { get; init; }

    /// <summary>Memorize's text box lines (rows 18–22), or null for no text box.</summary>
    public IReadOnlyList<WindowLine>? Box { get; init; }

    /// <summary>
    /// Camp Display (0x187ED, P s34): one full-screen window (START 03F1:02E8 with 1, 1, 38, 22, colour 15) with
    /// <see cref="Title"/> centred on row 1 in white and no name; the list window is the request's rows. The venues'
    /// lists draw the same window (P + D s46): the shop's Buy (0x6E1A, "Shop") and the loot's item take (0x609C,
    /// "Loot"), Take's coin kinds (0x2597B, no title); Add's roster clears the inside only (0x1C9FF), under the party
    /// menu's frame.
    /// </summary>
    public bool CentredTitle { get; init; }

    /// <summary>The list routine's label colour (0x2D669's first colour: 13 for the spell lists, 11 for Display).</summary>
    public byte LabelColour { get; init; } = 13;

    /// <summary>
    /// The temple's heal list (0x4F4E, P + D s46): <see cref="Title"/> is the whole title line, printed at (1, 1) in white
    /// (0x709:0x626 with colour 15) with no name before it; the list window's frame and the text box's are drawn as for
    /// Memorize (<see cref="Box"/> empty: the box blank).
    /// </summary>
    public bool WholeTitle { get; init; }

    /// <summary>
    /// The title's Amiga pen under the Amiga font (the string routine, hunk 25 +0xC3C), or null for the DOS colour's
    /// usual pen. The window routine's centred title (hunk 25 +0x19F4: Buy's "Shop", the take's "Loot") passes the raw
    /// colour 15, which the glyph routine's rule makes pen 30 (FS-UAE s47: orange, where DOS 15's usual pen 16 is grey);
    /// the heal list's name line passes pen 28 (hunk 25 +0x417E).
    /// </summary>
    public int? AmigaTitlePen { get; init; }

    /// <summary>The list window's first column (0x2D669's x0: 1 for the spell lists, 2 for the heal list).</summary>
    public int RowColumn { get; init; } = 1;

    /// <summary>The column the menu words start at on row 24 when set (the heal list's "Heal Exit" from column 0).</summary>
    public int? MenuColumn { get; init; }

    /// <summary>The column the menu words start at on row 24.</summary>
    public int MenuStart => MenuColumn ?? (Prompt is { Length: > 0 } p ? p.Length : 1);
}

/// <summary>
/// The item list routine 0x2D669's window (P, s30; combat-menu.md "Spell lists' Next / Prev"): <paramref name="Rows"/>
/// list rows (its y1 − y0 + 1) shown from the window's top (DS:6B29). The menu line is the caller's word, then
/// <paramref name="Next"/> when rows remain below the window (count − rows &gt; top), <paramref name="Prev"/> when the top
/// is past the leading labels, then Exit: the words go in at <paramref name="WordsAt"/> of the request's actions. Next /
/// Prev move the window by the rows (clamped to count − rows and 0), the highlight keeping its place in the window;
/// Up / Down move the highlight inside the window, wrapping there (0x2D594), and skip labels (0x2D3D2).
/// </summary>
public sealed record ListPaging(string Next, string Prev, int Rows, int WordsAt);

/// <summary>
/// The action picked on a <see cref="ListRequest"/> (or a member switch) and the row it applies to; with paging, the
/// window's top as it was left (<see cref="ListRequest.Top"/>).
/// </summary>
public sealed record ListAnswer(int Action, int Row, int Top = 0);

/// <summary>A line typed by the player: digits only when <paramref name="Numeric"/>. The answer is the string.</summary>
public sealed record TextRequest(string Prompt, bool Numeric, int MaxLength) : PlayerRequest
{
    /// <summary>
    /// An area script's line input (INPUTSTR / INPUTNUM, <see cref="ClientPlayer.InputString"/> /
    /// <see cref="ClientPlayer.InputNumber"/>): the one whose letter grid the Amiga QoL option
    /// <see cref="AmigaView.GridOffText"/> places off the text window. Other inputs keep the grid where it was.
    /// </summary>
    public bool ScriptInput { get; init; }

    /// <summary>
    /// A numeric input's largest value (the money input 0x24F1D: a number typed above it becomes it, Escape answers an
    /// empty line); null: none.
    /// </summary>
    public ushort? Maximum { get; init; }

    /// <summary>
    /// The keyboard types letters as an Amiga keymap does (lower case unless Shift or Caps Lock), not DOS's capitals:
    /// the Amiga's input requester (hunk 25 +0xEC6) is a plain Intuition string gadget (no ALTKEYMAP), and the name
    /// prompts keep what it gives (creation's name, Rename). The pad / mouse letter grid stays upper case.
    /// </summary>
    public bool KeepsCase { get; init; }
}

/// <summary>A pause (DELAY): answered by the screen when the time has passed.</summary>
public sealed record DelayRequest(TimeSpan Duration) : PlayerRequest
{
    /// <summary>
    /// Answer <c>true</c> as soon as any key, button or click arrives, <c>false</c> when the time has passed (the rest
    /// loop's key check 0x24B1D, polled by the camp); off = the plain DELAY (answered null either way).
    /// </summary>
    public bool ReportInput { get; init; }

    /// <summary>A bottom line left on the screen during the pause (text, colour), or null.</summary>
    public (string Text, byte Colour)? BottomLine { get; init; }

    /// <summary>Keep the exploration commands if they were visible when the pause began.</summary>
    public bool KeepCommandLine { get; init; }

    /// <summary>
    /// The pause runs on from the end of the pause just before it (a loop that times each frame from the last change,
    /// as the end game's 0x206A3), so the screen's frame of delay in noticing each end does not add up.
    /// </summary>
    public bool Chained { get; init; }

    /// <summary>
    /// A fixed program delay (or the Client's one-frame display point) that the game speed does not scale: the QoL
    /// <see cref="AnimationSpeed"/> scales it (<see cref="AnimationTiming.Apply"/>, in <see cref="ScriptRunner.Ask"/>).
    /// </summary>
    public bool Animation { get; init; }
}

/// <summary>Thrown inside the worker when the runner is shut down while it waits.</summary>
public sealed class RunnerCancelledException() : Exception("the game logic was stopped");

/// <summary>
/// Runs game logic (the area loop with its ECL runs, which call the host synchronously) on a worker thread with a
/// strict hand-off: the UI thread starts or resumes the worker and waits until the worker either finishes or parks on
/// a <see cref="PlayerRequest"/>. So the two threads never run at the same time, the UI draws the game state only
/// while the worker is parked, and no locks are needed.
/// </summary>
public sealed class ScriptRunner : IDisposable
{
    private readonly SemaphoreSlim _toWorker = new(0), _toUi = new(0);
    private Thread? _thread;
    private object? _answer;
    private volatile bool _cancel;

    /// <summary>UI services that need to advance during a synchronous command. Must not read game state.</summary>
    internal Action? ServiceWhileWaiting { get; set; }

    private volatile AnimationSpeed _animationSpeed = AnimationSpeed.Original;

    /// <summary>
    /// QoL animation speed (<see cref="ClientSettings.AnimationSpeed"/>; Original = the program's timings): applied here
    /// to every <see cref="DelayRequest.Animation"/> pause the worker asks for. Set from the UI thread.
    /// </summary>
    public AnimationSpeed AnimationSpeed
    {
        get => _animationSpeed;
        set => _animationSpeed = value;
    }

    /// <summary>
    /// Worker thread: called with each request once the UI has answered it, before <see cref="Ask"/> returns (the
    /// player's <see cref="ClientPlayer.MenuEnded"/>: every menu of the program's menu routine clears VM $4AFF at its end).
    /// </summary>
    public Action<PlayerRequest>? Answered { get; set; }

    /// <summary>
    /// Worker thread: called with each request before it is posted, and may return it changed (the player's <see
    /// cref="ClientPlayer.MenuAsked"/>: a program menu opened while VM $4AFF is set draws the loaded picture on every pass).
    /// </summary>
    public Func<PlayerRequest, PlayerRequest>? Asking { get; set; }

    /// <summary>A command is in progress (running or parked on a request).</summary>
    public bool Busy { get; private set; }

    /// <summary>What the parked worker waits for (null while idle).</summary>
    public PlayerRequest? Pending { get; private set; }

    /// <summary>The exception that ended the last command, if any (shown by the screen).</summary>
    public Exception? Fault { get; private set; }

    /// <summary>UI thread: runs <paramref name="work"/> until it finishes or waits for the player.</summary>
    public void Start(Action work)
    {
        if (Busy)
            throw new InvalidOperationException("a command is already running");
        Busy = true;
        Fault = null;
        _thread = new Thread(() =>
        {
            _toWorker.Wait();
            try
            {
                if (!_cancel)
                    work();
            }
            catch (RunnerCancelledException)
            {
            }
            catch (Exception e)
            {
                Fault = e;
            }
            finally
            {
                Pending = null;
                Busy = false;
                _toUi.Release();
            }
        })
        {
            IsBackground = true,
            Name = "game logic",
        };
        _thread.Start();
        _toWorker.Release();
        WaitForWorker();
    }

    /// <summary>UI thread: answers the pending request and runs the worker until it parks again or finishes.</summary>
    public void Answer(object? value)
    {
        if (Pending is null)
            throw new InvalidOperationException("nothing is waiting for an answer");
        _answer = value;
        Pending = null;
        _toWorker.Release();
        WaitForWorker();
    }

    private void WaitForWorker()
    {
        if (ServiceWhileWaiting is not { } service)
        {
            _toUi.Wait();
            return;
        }

        while (!_toUi.Wait(4))
            service();
    }

    /// <summary>Worker thread: parks on <paramref name="request"/> and returns the UI's answer.</summary>
    public object? Ask(PlayerRequest request)
    {
        if (_cancel)
            throw new RunnerCancelledException();
        if (AnimationTiming.Apply(request, AnimationSpeed) is not { } asked)
            return null; // Instant: a skipped animation pause, answered as a pause is
        request = Asking?.Invoke(asked) ?? asked;
        Pending = request;
        _toUi.Release();
        _toWorker.Wait();
        if (_cancel)
            throw new RunnerCancelledException();
        var answer = _answer;
        Answered?.Invoke(request);
        return answer;
    }

    /// <summary>Stops a parked worker (window closing): it throws <see cref="RunnerCancelledException"/> and ends.</summary>
    public void Dispose()
    {
        _cancel = true;
        if (Busy && Pending is not null)
        {
            Pending = null;
            _toWorker.Release();
            _toUi.Wait(TimeSpan.FromSeconds(2));
        }
        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
