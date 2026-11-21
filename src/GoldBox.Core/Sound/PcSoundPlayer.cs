namespace GoldBox.Core.Sound;

/// <summary>
/// The program's sound cues: index into the cue table (DS:2612 in v1.3, 16 command words; docs/re/sound.md §2.4).
/// A cue's command word is read from the player's program; command − 1 is the tune.
/// </summary>
public enum SoundCue
{
    /// <summary>Command 255: silence (leaving the game, Ctrl-S off).</summary>
    Stop = 0,
    /// <summary>Command 0: silence and close the output gate (no caller in PoR).</summary>
    GateOff = 1,
    /// <summary>Command 1: open the output gate (no caller in PoR).</summary>
    GateOn = 2,
    /// <summary>A spell's flight to its target in a fight (ids other than 47 and 51); Hold Person's further targets.</summary>
    Cast = 3,
    /// <summary>A creature's name shown with damage in a fight.</summary>
    Damage = 4,
    /// <summary>A creature's name shown with an effect in a fight.</summary>
    Effect = 5,
    /// <summary>The body routine (OVR 0x3154B).</summary>
    Body = 6,
    /// <summary>A missile leaves.</summary>
    MissileOut = 7,
    /// <summary>A hit; an attack on a helpless defender; the bump of a refused step or failed door.</summary>
    Impact = 8,
    /// <summary>Lightning Bolt (spell 51) cast in a fight.</summary>
    LightningBolt = 9,
    /// <summary>An attack with no hit.</summary>
    Miss = 10,
    /// <summary>A step, a turn left or right, a creature's step in a fight, ECL CALL $BA03 with $03DE = 8.</summary>
    Step = 11,
    /// <summary>Fireball (spell 47) cast in a fight; ECL CALL $BA03 with $03DE = 10.</summary>
    Fireball = 12,
    /// <summary>A missile arrives.</summary>
    MissileIn = 13,
}

/// <summary>
/// Plays the program's sound commands the way PoR's sound unit does (docs/re/sound.md §2.1, §2.4) and renders them:
/// the driver (<see cref="PcSoundDriver"/>) ticks at its timer rate while its timer is installed, and the speaker
/// (<see cref="PcSpeakerSynth"/>) or Tandy (<see cref="Sn76489Synth"/>) turns the port writes into samples.
/// <para>Auto-stop (the default): a command installs the timer, starts the tune and stops it at the second BIOS clock
/// tick (the driver's sound delay(10)), then uninstalls the timer. The program waits that long (busy-wait), so cues
/// that come in meanwhile wait their turn here. Inside <see cref="BeginResident"/> … <see cref="EndResident"/> (set
/// auto-stop 0 … 10, the fight animations) the timer stays installed and tunes run until they end or the bracket
/// closes.</para>
/// Not thread-safe: call it from one thread (the Client's game loop).
/// </summary>
public sealed class PcSoundPlayer : ISoundPorts
{
    private const int BeginOp = -1, EndOp = -2;
    private readonly PcSoundDriver _driver;
    private readonly IReadOnlyList<int> _cueWords;
    private readonly PcSpeakerSynth _speaker;
    private readonly Sn76489Synth _tandy;
    private readonly Queue<int> _ops = new();
    private readonly double _samplesPerTick;
    private double _sampleDebt;
    private bool _installed, _resident;
    private int _chainsLeft; // auto-stop: BIOS clock ticks until the stop; 0 = none running

    /// <param name="cueWords">The cue table's command words (DS:2612, 16 words in v1.3).</param>
    /// <param name="chip">The Tandy chip model (Tandy device only; default the NCR 8496 DOSBox-X plays).</param>
    public PcSoundPlayer(PcSoundDriverImage image, IReadOnlyList<int> cueWords, PcSoundDevice device, int sampleRate = 44100,
        TandyChip chip = TandyChip.Ncr8496)
    {
        _driver = new PcSoundDriver(image, this);
        _cueWords = cueWords;
        Device = device;
        SampleRate = sampleRate;
        _speaker = new PcSpeakerSynth(sampleRate);
        _tandy = new Sn76489Synth(sampleRate, chip);
        _samplesPerTick = sampleRate / image.TickHz;
    }

    public int SampleRate { get; }
    public PcSoundDevice Device { get; }
    public PcSoundDriver Driver => _driver;

    /// <summary>The commands run so far, in order (for tests and logs).</summary>
    public List<int> Commands { get; } = [];

    /// <summary>Driver ticks run so far.</summary>
    public long Ticks { get; private set; }

    /// <summary>Whether the driver's timer is installed (the voices advance).</summary>
    public bool Installed => _installed;

    /// <summary>True while a sound is running or waiting to.</summary>
    public bool Busy => _chainsLeft > 0 || _ops.Count > 0 || _installed && _driver.Busy(Device);

    void ISoundPorts.Out(int port, byte value)
    {
        if (port == 0xC0)
            _tandy.Out(value);
        else
            _speaker.Out(port, value);
    }

    /// <summary>Plays cue <paramref name="cue"/> (the program's sound command with the cue table's word).</summary>
    public void Cue(SoundCue cue)
    {
        int i = (int)cue;
        if (i >= 0 && i < _cueWords.Count)
            Command(_cueWords[i]);
    }

    /// <summary>
    /// The sound command (START CS:00DC): 0 gate off, 1 gate on, 255 silence, else tune command − 1. While an
    /// auto-stop sound runs, the program is still waiting in it, so the command waits its turn.
    /// </summary>
    public void Command(int command)
    {
        _ops.Enqueue(command & 0xFFFF);
        Pump();
    }

    /// <summary>Set auto-stop(0): the timer is installed and stays (a fight animation starts).</summary>
    public void BeginResident()
    {
        _ops.Enqueue(BeginOp);
        Pump();
    }

    /// <summary>Set auto-stop(10): the timer is uninstalled, which silences whatever still sounds.</summary>
    public void EndResident()
    {
        _ops.Enqueue(EndOp);
        Pump();
    }

    private void Pump()
    {
        while (_ops.Count > 0 && !(_chainsLeft > 0 && !_resident))
        {
            int op = _ops.Dequeue();
            if (op == BeginOp)
            {
                if (!_resident)
                {
                    _resident = true;
                    Install();
                }
            }
            else if (op == EndOp)
            {
                if (_resident)
                {
                    _resident = false;
                    Uninstall();
                }
            }
            else
                Run(op);
        }
    }

    private void Run(int command)
    {
        Commands.Add(command);
        if (!_resident)
            Install();
        switch (command)
        {
            case 0:
                Silence();
                _driver.OutputOn = false;
                break;
            case 1:
                _driver.OutputOn = true;
                break;
            case 255:
                Silence();
                break;
            default:
                _driver.Program(Device, command - 1);
                break;
        }
        if (!_resident)
            _chainsLeft = 2; // sound delay(10): the DOS clock moves 5 or 6 cs per BIOS tick, so two ticks
    }

    private void Silence()
    {
        _driver.Program(PcSoundDevice.Tandy, 0);
        _driver.Program(PcSoundDevice.Speaker, 0);
    }

    private void Install()
    {
        _installed = true;
        _speaker.Control();
    }

    private void Uninstall()
    {
        _installed = false;
        _driver.Uninstall();
    }

    /// <summary>Renders <paramref name="buffer"/>.Length samples of mono audio, advancing the driver in time.</summary>
    public void Render(Span<float> buffer) => Render(buffer, false);

    /// <summary>
    /// Renders up to <paramref name="buffer"/>.Length samples; with <paramref name="untilIdle"/> and a sound running,
    /// stops at the sample where the last one ends (the auto-stop's uninstall, at a driver tick) and returns how many
    /// samples were rendered. The program's next sound command follows that end with no gap (it busy-waits in the
    /// command), so a caller that waits for it can start the next cue there.
    /// </summary>
    public int Render(Span<float> buffer, bool untilIdle)
    {
        buffer.Clear();
        bool watch = untilIdle && Busy;
        int done = 0;
        while (done < buffer.Length)
        {
            if (_installed && _sampleDebt <= 0)
            {
                Advance(1);
                _sampleDebt += _samplesPerTick;
            }
            if (watch && !Busy)
                return done;
            int n = buffer.Length - done;
            if (_installed)
                n = Math.Min(n, Math.Max(1, (int)Math.Ceiling(_sampleDebt)));
            var slice = buffer.Slice(done, n);
            _speaker.Render(slice, n);
            if (Device == PcSoundDevice.Tandy)
                _tandy.Render(slice, n);
            done += n;
            _sampleDebt = _installed ? _sampleDebt - n : 0;
        }
        return done;
    }

    /// <summary>Runs up to <paramref name="ticks"/> driver ticks without rendering (tests); stops when the timer is
    /// not installed.</summary>
    public void Advance(int ticks)
    {
        for (int t = 0; t < ticks && _installed; t++)
        {
            Ticks++;
            bool chain = _driver.Tick();
            if (chain && _chainsLeft > 0 && --_chainsLeft == 0 && !_resident)
            {
                Silence();
                Uninstall();
                Pump();
            }
        }
    }
}
