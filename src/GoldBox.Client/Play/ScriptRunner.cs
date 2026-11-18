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
public sealed record KeyRequest(string? Prompt = null) : PlayerRequest;

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
}

/// <summary>The action picked on a <see cref="ListRequest"/> (or a member switch) and the row it applies to.</summary>
public sealed record ListAnswer(int Action, int Row);

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
        _toUi.Wait();
    }

    /// <summary>UI thread: answers the pending request and runs the worker until it parks again or finishes.</summary>
    public void Answer(object? value)
    {
        if (Pending is null)
            throw new InvalidOperationException("nothing is waiting for an answer");
        _answer = value;
        Pending = null;
        _toWorker.Release();
        _toUi.Wait();
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
