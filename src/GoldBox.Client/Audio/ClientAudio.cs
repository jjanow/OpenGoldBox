using GoldBox.Core.Sound;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace GoldBox.Client.Audio;

/// <summary>
/// The Client's sound (docs/re/sound.md): the program's sound calls (<see cref="ISoundSink"/>, from the game-logic
/// thread) go to a <see cref="PcSoundPlayer"/> on the device the setting picks, rendered at 44.1 kHz mono into a
/// MonoGame <see cref="DynamicSoundEffectInstance"/> on the UI thread through its buffer callback.
/// <list type="bullet">
/// <item>Auto-stop cues (step, impact, miss, bump): game logic waits for the driver to finish while the UI thread
/// continues to service the sound output.</item>
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
    private readonly float[] _samples = new float[Chunk];
    private readonly byte[] _bytes = new byte[Chunk * 2];
    private readonly int _uiThread;
    private bool _resident, _closing;

    private ClientAudio(PcSoundPlayer player, AmigaMusic? music, DynamicSoundEffectInstance output, int uiThread)
    {
        _player = player;
        _music = music;
        _out = output;
        _uiThread = uiThread;
        _out.BufferNeeded += FillBuffers;
        FillBuffers(_out, EventArgs.Empty);
        _out.Play();
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
        DynamicSoundEffectInstance? output = null;
        try
        {
            var player = sound.Player(d, SampleRate);
            var music = amiga is null ? null : new AmigaMusic(amiga, SampleRate);
            output = new DynamicSoundEffectInstance(SampleRate, AudioChannels.Mono);
            return new ClientAudio(player, music, output, Environment.CurrentManagedThreadId);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or TypeInitializationException
                                       or DllNotFoundException)
        {
            output?.Dispose();
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
            var started = Clock.Elapsed;
            while (Clock.Elapsed - started < TimeSpan.FromMilliseconds(500))
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

    /// <summary>Service the audio backend while the UI thread waits for a synchronous game command.</summary>
    internal void Update() => FrameworkDispatcher.Update();

    private void FillBuffers(object? sender, EventArgs args)
    {
        // The dispatcher removes played buffers and calls this handler. Both operations must stay on the UI thread.
        while (_out.PendingBufferCount < 3)
        {
            lock (_lock)
            {
                _player.Render(_samples);
                _music?.Render(_samples);
                if (_resident && _closing && !_player.Driver.Busy(_player.Device))
                    CloseBracket();
            }
            for (int i = 0; i < Chunk; i++)
            {
                int v = (int)Math.Round(Math.Clamp(_samples[i], -1f, 1f) * 32767);
                _bytes[2 * i] = (byte)v;
                _bytes[2 * i + 1] = (byte)(v >> 8);
            }
            _out.SubmitBuffer(_bytes);
        }
    }

    public void Dispose()
    {
        _out.BufferNeeded -= FillBuffers;
        _out.Stop();
        _out.Dispose();
    }
}
