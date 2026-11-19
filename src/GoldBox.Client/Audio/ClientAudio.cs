using GoldBox.Core.Sound;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Audio;

namespace GoldBox.Client.Audio;

/// <summary>
/// The Client's sound (docs/re/sound.md): the program's sound calls (<see cref="ISoundSink"/>, from the game-logic
/// thread) go to a <see cref="PcSoundPlayer"/> on the device the setting picks, rendered at 44.1 kHz mono into a
/// MonoGame <see cref="DynamicSoundEffectInstance"/> by a pump thread of its own.
/// <list type="bullet">
/// <item>Auto-stop cues (step, impact, miss, bump): the program waits in the sound command until the sound is cut
/// (sound delay 10, 59–110 ms), so the logic thread waits here too (not the window's thread).</item>
/// <item>Resident brackets: the program's bracket lasts as long as its animation (a missile's or spell's flight, the
/// body drawing, the message pause), which the Client does not draw yet. `[?]` Until it does, a bracket stays open
/// until its tune ends by itself or the next sound call outside a bracket.</item>
/// </list>
/// </summary>
public sealed class ClientAudio : ISoundSink, IDisposable
{
    public const int SampleRate = 44100;
    private const int Chunk = SampleRate / 60;
    private readonly object _lock = new();
    private readonly PcSoundPlayer _player;
    private readonly AmigaMusic? _music;
    private readonly DynamicSoundEffectInstance _out;
    private readonly Thread _pump;
    private readonly int _uiThread;
    private volatile bool _stop;
    private bool _resident, _closing;

    private ClientAudio(PcSoundPlayer player, AmigaMusic? music, DynamicSoundEffectInstance output, int uiThread)
    {
        _player = player;
        _music = music;
        _out = output;
        _uiThread = uiThread;
        _pump = new Thread(Pump) { IsBackground = true, Name = "GoldBox audio" };
        _out.Play();
        _pump.Start();
    }

    /// <summary>The sound device setting (<see cref="ClientSettings.Sound"/>) as a device; null = off.</summary>
    public static PcSoundDevice? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "speaker" or "pc" or "p" => PcSoundDevice.Speaker,
        "tandy" or "t" => PcSoundDevice.Tandy,
        _ => null,
    };

    /// <summary>
    /// Opens the audio output for <paramref name="sound"/>, or null (sound off, no driver data, no audio device). With
    /// <paramref name="amiga"/> (the module's Amiga players' data) the Amiga music can be played too (<see cref="PlaySong"/>).
    /// </summary>
    public static ClientAudio? Open(PorSound? sound, PcSoundDevice? device, AmigaSoundImage? amiga = null)
    {
        if (sound is null || device is not { } d)
            return null;
        try
        {
            var output = new DynamicSoundEffectInstance(SampleRate, AudioChannels.Mono);
            return new ClientAudio(sound.Player(d, SampleRate), amiga is null ? null : new AmigaMusic(amiga, SampleRate),
                output, Environment.CurrentManagedThreadId);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or TypeInitializationException
                                       or DllNotFoundException)
        {
            Console.Error.WriteLine($"sound off: no audio output ({e.Message})");
            return null;
        }
    }

    public PcSoundDevice Device => _player.Device;

    /// <summary>The module has the Amiga music (docs/re/sound.md §5).</summary>
    public bool HasAmigaMusic => _music is not null;

    /// <summary>
    /// The Amiga's PlaySong(n) (hunk 27 +0x6E54): 0 stops, 1–6 start song n (docs/re/sound.md §5.3: 1 the intro and the
    /// camp, 2 a battle, 3 a character or icon accepted, 5 the party killed). Nothing without the Amiga music.
    /// </summary>
    public void PlaySong(int song)
    {
        if (_music is null)
            return;
        Log($"song {song}");
        lock (_lock)
            _music.Play(song);
    }

    /// <summary>GOLDBOX_SOUND_LOG=1: each sound call on stderr with the time (for comparing with captures).</summary>
    private static readonly bool LogCalls = Environment.GetEnvironmentVariable("GOLDBOX_SOUND_LOG") == "1";
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private static void Log(string what)
    {
        if (LogCalls)
            Console.Error.WriteLine($"sound {Clock.Elapsed.TotalSeconds:F3} {what}");
    }

    public void Cue(SoundCue cue)
    {
        Log(cue.ToString());
        bool wait;
        lock (_lock)
        {
            if (_resident && _closing)
                CloseBracket();
            _player.Cue(cue);
            wait = !_resident;
        }
        // The program busy-waits in the sound command until an auto-stop sound is cut; the logic thread does too.
        if (wait && Environment.CurrentManagedThreadId != _uiThread)
        {
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until)
            {
                lock (_lock)
                    if (!_player.Busy)
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
            _closing = false;
            if (_resident)
                return; // a new bracket right after a deferred one: the timer stays
            _resident = true;
            _player.BeginResident();
        }
    }

    public void EndResident()
    {
        Log(")");
        lock (_lock)
            if (_resident)
                _closing = true;
    }

    private void CloseBracket()
    {
        _player.EndResident();
        _resident = false;
        _closing = false;
    }

    private void Pump()
    {
        var samples = new float[Chunk];
        var bytes = new byte[Chunk * 2];
        while (!_stop)
        {
            try
            {
                if (_out.PendingBufferCount >= 3)
                {
                    Thread.Sleep(4);
                    continue;
                }
                lock (_lock)
                {
                    _player.Render(samples);
                    _music?.Render(samples);
                    if (_resident && _closing && !_player.Driver.Busy(_player.Device))
                        CloseBracket();
                }
                for (int i = 0; i < Chunk; i++)
                {
                    int v = (int)Math.Round(Math.Clamp(samples[i], -1f, 1f) * 32767);
                    bytes[2 * i] = (byte)v;
                    bytes[2 * i + 1] = (byte)(v >> 8);
                }
                _out.SubmitBuffer(bytes);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _pump.Join(500);
        _out.Stop();
        _out.Dispose();
    }
}
