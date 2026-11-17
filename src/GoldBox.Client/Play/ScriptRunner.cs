namespace GoldBox.Client.Play;

/// <summary>What the game logic waits for from the player (or from the clock, for a delay).</summary>
public abstract record PlayerRequest;

/// <summary>
/// Pick one of <paramref name="Items"/>: a horizontal menu on the bottom line, or a vertical list in the text window
/// under <paramref name="Prompt"/>. The answer is the index. <paramref name="CancelIndex"/> is answered on Cancel
/// (null: Cancel does nothing, as in the original menus that have no way out).
/// </summary>
public sealed record ChoiceRequest(string? Prompt, IReadOnlyList<string> Items, bool Vertical, int? CancelIndex = null) : PlayerRequest;

/// <summary>A line typed by the player: digits only when <paramref name="Numeric"/>. The answer is the string.</summary>
public sealed record TextRequest(string Prompt, bool Numeric, int MaxLength) : PlayerRequest;

/// <summary>A pause (DELAY): answered by the screen when the time has passed.</summary>
public sealed record DelayRequest(TimeSpan Duration) : PlayerRequest;

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
