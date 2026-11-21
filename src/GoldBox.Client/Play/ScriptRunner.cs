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

    /// <summary>The prompt's colour when the caller passes another than 13 (the yes/no routine 0x3C5:0x39 takes it as an
    /// argument: 13 or 14, e.g. 14 for the camp's keep questions 0x1818B / 0x182BA and the quit question 0x19791).</summary>
    public byte? MenuPromptColour { get; init; }

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
    /// The menu routine's animate flag ([bp+0xA] of 0x2CC8C): while this menu waits, the picture window's animation
    /// plays (<see cref="PictureAnimation"/>). Set by the camp's menus and, while a close-up is shown, by HMENU and
    /// ENCMENU (read s19).
    /// </summary>
    public bool Animates { get; init; }

    /// <summary>Items answered by the Up / Down actions of a horizontal menu (the rest-time menu's Inc / Dec); null = none.</summary>
    public int? UpAnswer { get; init; }
    public int? DownAnswer { get; init; }

    /// <summary>The hotkey of item <paramref name="i"/>.</summary>
    public char HotkeyOf(int i) => Hotkeys is { } h && i < h.Length ? h[i] : Items[i].Length > 0 ? Items[i][0] : '\0';
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

    /// <summary>The prompt's colour when it is not 13 (START 0x7C85 takes it as an argument: DAMAGE's line is 15).</summary>
    public byte? Colour { get; init; }

    /// <summary>Entries of a menu text by the menu routine's rule (<see cref="MenuEntries"/>).</summary>
    public static int CountEntries(string original) => original.Count(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));
}

/// <summary>A cell of a full-screen window (START 03F1:02E8 with 1, 1, 38, 22): text at a column and row, in a colour.</summary>
public sealed record WindowLine(int Column, int Row, string Text, byte Colour = 10);

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

    /// <summary>With <see cref="Paging"/>: the window's first row on entry (DS:6B29, kept by a caller that keeps its row).</summary>
    public int Top { get; init; }

    /// <summary>With <see cref="Paging"/>: the spell list's own screen (0x239D6 / 0x270A2), drawn instead of <see cref="Lines"/>.</summary>
    public SpellPageLayout? Spell { get; init; }

    /// <summary>
    /// In a fight: the combat palette stays on (EGA 0 and 8 swapped, <see cref="CombatPage.Grey"/>), as for the menu's
    /// Cast list (0x14C0F → 0x239D6; D s33: the list on dark grey); View's pages restore the normal palette.
    /// </summary>
    public bool Grey { get; init; }
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
    /// <see cref="Title"/> centred on row 1 in white and no name; the list window is the request's rows.
    /// </summary>
    public bool CentredTitle { get; init; }

    /// <summary>The list routine's label colour (0x2D669's first colour: 13 for the spell lists, 11 for Display).</summary>
    public byte LabelColour { get; init; } = 13;

    /// <summary>The column the menu words start at on row 24.</summary>
    public int MenuStart => Prompt is { Length: > 0 } p ? p.Length : 1;
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
public sealed record TextRequest(string Prompt, bool Numeric, int MaxLength) : PlayerRequest;

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
        Pending = request;
        _toUi.Release();
        _toWorker.Wait();
        if (_cancel)
            throw new RunnerCancelledException();
        return _answer;
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
