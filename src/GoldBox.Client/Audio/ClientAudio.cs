using GoldBox.Core.Sound;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace GoldBox.Client.Audio;

/// <summary>
/// The Client's sound (docs/re/sound.md): the program's sound calls (<see cref="ISoundSink"/>, from the game-logic
/// thread) go to the DOS driver or Amiga effects on the device the setting picks, rendered at 44.1 kHz stereo (the
/// DOS devices on both sides, the Amiga's channels 0 and 3 left, 1 and 2 right) into a MonoGame
/// <see cref="DynamicSoundEffectInstance"/> on the UI thread through its buffer callback.
/// <list type="bullet">
/// <item>Auto-stop cues (step, impact, miss, bump): game logic waits for the driver to finish while the UI thread
/// continues to service the sound output.</item>
/// <item>Resident brackets: the program's bracket lasts as long as its animation (a missile's or spell's flight, the
/// downed picture and its pause), and set auto-stop(10) at its end uninstalls the timer, which silences the tune
/// (DOSBox-X s39, Tandy: the body tune cut at the picture's pause end, 0.92 s, where it would run 1.37 s). A bracket
/// the Client drew an animation in (open for a while) closes at once; one opened and closed with nothing drawn
/// between (a drop the Client does not draw) stays open until its tune ends or the next sound call.</item>
/// </list>
/// </summary>
public sealed class ClientAudio : ISoundSink, IDisposable
{
    public const int SampleRate = 44100;
    private const int Chunk = SampleRate / 60;
    private readonly object _lock = new();
    private readonly PorSound? _sound;
    private PcSoundPlayer? _player;
    private readonly AmigaMusic? _music;
    private ClientSoundDevice? _device;
    private readonly DynamicSoundEffectInstance _out;
    private readonly float[] _samples = new float[Chunk];
    private readonly float[] _right = new float[Chunk];
    private readonly byte[] _bytes = new byte[Chunk * 4];
    private readonly int _uiThread;
    private bool _resident, _closing;
    private TimeSpan _openedAt;

    /// <summary>A bracket open at least this long had an animation drawn inside it (frames are 16.7 ms).</summary>
    private static readonly TimeSpan DrawnBracket = TimeSpan.FromMilliseconds(15);

    private ClientAudio(PorSound? sound, ClientSoundDevice? device, AmigaMusic? music, DynamicSoundEffectInstance output,
        int uiThread)
    {
        _sound = sound;
        _music = music;
        SetDevice(device);
        _out = output;
        _uiThread = uiThread;
        _out.BufferNeeded += FillBuffers;
        FillBuffers(_out, EventArgs.Empty);
        _out.Play();
    }

    /// <summary>The sound device setting (<see cref="ClientSettings.Sound"/>) as a device; null = off.</summary>
    public static ClientSoundDevice? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "speaker" or "pc" or "p" => ClientSoundDevice.Speaker,
        "tandy" or "t" => ClientSoundDevice.Tandy,
        "amiga" => ClientSoundDevice.Amiga,
        _ => null,
    };

    /// <summary>
    /// Opens the audio output for the imported DOS or Amiga players, or null when neither is present or audio is unavailable.
    /// </summary>
    public static ClientAudio? Open(PorSound? sound, ClientSoundDevice? device, AmigaSoundImage? amiga = null)
    {
        if (sound is null && amiga is null)
            return null;
        DynamicSoundEffectInstance? output = null;
        try
        {
            var music = amiga is null ? null : new AmigaMusic(amiga, SampleRate);
            output = new DynamicSoundEffectInstance(SampleRate, AudioChannels.Stereo);
            return new ClientAudio(sound, device, music, output, Environment.CurrentManagedThreadId);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or TypeInitializationException
                                       or DllNotFoundException)
        {
            output?.Dispose();
            Console.Error.WriteLine($"sound off: no audio output ({e.Message})");
            return null;
        }
    }

    public ClientSoundDevice? Device
    {
        get
        {
            lock (_lock)
                return _device;
        }
    }

    /// <summary>Applies a change of device or a mute made at a key read, on either game thread.</summary>
    public void SetDevice(ClientSoundDevice? device)
    {
        lock (_lock)
        {
            if (device == ClientSoundDevice.Amiga && !HasAmigaEffects)
                device = ClientSoundDevice.Speaker;
            if (_sound is null && device != ClientSoundDevice.Amiga)
                device = null;
            if (_device == device)
                return;
            if (_device == ClientSoundDevice.Amiga || device is null)
                _music?.Play(0);
            _device = device;
            _player = device switch
            {
                ClientSoundDevice.Speaker => _sound!.Player(PcSoundDevice.Speaker, SampleRate),
                ClientSoundDevice.Tandy => _sound!.Player(PcSoundDevice.Tandy, SampleRate),
                _ => null,
            };
            _resident = false;
            _closing = false;
        }
    }

    /// <summary>The module has the data needed to play sampled effects.</summary>
    public bool HasAmigaEffects => _music?.HasEffects == true;

    /// <summary>
    /// The Amiga's PlaySong(n) (hunk 27 +0x6E54): 0 stops, 1–6 start song n (docs/re/sound.md §5.3: 1 the intro and the
    /// camp, 2 a battle, 3 a character or icon accepted, 5 the party killed). Nothing without the Amiga music.
    /// </summary>
    public void PlaySong(int song)
    {
        if (_music is null)
            return;
        lock (_lock)
        {
            if (_device is not null)
            {
                Log($"song {song}");
                _music.Play(song);
            }
        }
    }

    /// <summary>GOLDBOX_SOUND_LOG=1: each sound call on stderr with the time (for comparing with captures).</summary>
    private static readonly bool LogCalls = Environment.GetEnvironmentVariable("GOLDBOX_SOUND_LOG") == "1";
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private static void Log(string what)
    {
        if (LogCalls)
            Console.Error.WriteLine($"sound {Clock.Elapsed.TotalSeconds:F3} {what}");
    }

    private volatile bool _instant;

    /// <summary>
    /// QoL animation speed Instant (<see cref="Play.AnimationSpeed.Instant"/>): the logic does not wait out an auto-stop
    /// sound (a step, hit, miss: the program's busy-wait, docs/re/sound.md §2), and a cue that comes while one still
    /// sounds is skipped rather than queued, so the sounds never pile up behind the fight (also outside fights: the 3D
    /// view's step and bump sounds no longer pace walking). Resident brackets (flights,
    /// the body) are unchanged: their end cuts the tune. Off = the program's waits.
    /// </summary>
    public bool Instant
    {
        get => _instant;
        set => _instant = value;
    }

    public void Cue(SoundCue cue)
    {
        PcSoundPlayer player;
        bool wait;
        lock (_lock)
        {
            if (_device == ClientSoundDevice.Amiga)
            {
                Log($"amiga {cue}");
                if (cue == SoundCue.Stop)
                    _music!.Play(0);
                else
                    _music!.PlayEffect(cue);
                return;
            }
            if (_player is not { } active)
                return;
            player = active;
            if (_instant && !_resident && player.Busy)
            {
                Log($"{cue} skipped (instant)");
                return; // QoL Instant: an auto-stop sound still running is not queued behind (no pile-up)
            }
            Log(cue.ToString());
            if (_resident && _closing)
                CloseBracket();
            _held = false; // a cue right after a waited one starts where that one ended (FillBuffers)
            player.Cue(cue);
            wait = !_resident && !_instant;
            _awaited = wait;
        }
        // The program busy-waits in the sound command until an auto-stop sound is cut; the logic thread does too.
        if (wait && Environment.CurrentManagedThreadId != _uiThread)
        {
            var started = Clock.Elapsed;
            while (Clock.Elapsed - started < TimeSpan.FromMilliseconds(500))
            {
                lock (_lock)
                    if (_player != player || !player.Busy)
                        break;
                Thread.Sleep(2);
            }
        }
    }

    public void BeginResident()
    {
        Log("(");
        lock (_lock)
        {
            _held = false;
            _closing = false;
            if (_player is null || _resident)
                return; // a new bracket right after a deferred one: the timer stays
            _resident = true;
            _openedAt = Clock.Elapsed;
            _player.BeginResident();
        }
    }

    public void EndResident()
    {
        Log(")");
        lock (_lock)
            if (_resident)
            {
                if (Clock.Elapsed - _openedAt >= DrawnBracket)
                    CloseBracket(); // the timer uninstall silences the tune (CS:1391)
                else
                    _closing = true;
            }
    }

    private void CloseBracket()
    {
        _player?.EndResident();
        _resident = false;
        _closing = false;
    }

    /// <summary>Service the audio backend while the UI thread waits for a synchronous game command.</summary>
    internal void Update() => FrameworkDispatcher.Update();

    private void FillBuffers(object? sender, EventArgs args)
    {
        // The dispatcher removes played buffers and calls this handler. Both operations must stay on the UI thread.
        while (_out.PendingBufferCount < 3)
        {
            int n = Chunk;
            lock (_lock)
            {
                if (_held)
                {
                    // Only while buffers are still queued: the next BufferNeeded comes when one of them is played, so
                    // an empty queue would never call back (a stalled UI thread drains it).
                    if (Clock.Elapsed - _heldSince < HoldFor && _out.PendingBufferCount >= 2)
                        return;
                    _held = false;
                }
                if (_player is { } player)
                {
                    // A cue the logic thread waits on: the buffer ends where it stops, and nothing more is rendered
                    // until the next sound call (or HoldFor), so a cue that follows at once starts on that sample as
                    // in the program (DOS: the second of two back-to-back cues 109.85 ms after the first's rise, the
                    // step's 26 driver ticks; the Client was 116.67 ms, the 60 Hz buffer after the stop).
                    n = player.Render(_samples, _awaited);
                    if (n < Chunk)
                    {
                        Array.Clear(_samples, n, Chunk - n);
                        _awaited = false;
                        _held = true;
                        _heldSince = Clock.Elapsed;
                    }
                    if (_resident && _closing && !player.Driver.Busy(player.Device))
                        CloseBracket();
                }
                else
                    Array.Clear(_samples);
                Array.Copy(_samples, _right, Chunk); // the DOS devices are mono: the same on both sides
                if (_device is not null)
                    _music?.Render(_samples.AsSpan(0, n), _right.AsSpan(0, n));
            }
            for (int i = 0; i < n; i++)
            {
                int l = (int)Math.Round(Math.Clamp(_samples[i], -1f, 1f) * 32767);
                int r = (int)Math.Round(Math.Clamp(_right[i], -1f, 1f) * 32767);
                _bytes[4 * i] = (byte)l;
                _bytes[4 * i + 1] = (byte)(l >> 8);
                _bytes[4 * i + 2] = (byte)r;
                _bytes[4 * i + 3] = (byte)(r >> 8);
            }
            if (n > 0)
                _out.SubmitBuffer(_bytes, 0, 4 * n); // 16-bit stereo
        }
    }

    /// <summary>The longest the output holds at a waited cue's end for the next call (one 60 Hz buffer).</summary>
    private static readonly TimeSpan HoldFor = TimeSpan.FromMilliseconds(16);
    private bool _awaited, _held;
    private TimeSpan _heldSince;

    public void Dispose()
    {
        _out.BufferNeeded -= FillBuffers;
        _out.Stop();
        _out.Dispose();
    }
}
