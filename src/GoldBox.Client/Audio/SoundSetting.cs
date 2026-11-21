namespace GoldBox.Client.Audio;

public enum ClientSoundDevice
{
    Speaker,
    /// <summary>The Tandy 1000 EX / HX / TX's NCR 8496 (setting word <c>"tandy"</c>).</summary>
    Tandy,
    /// <summary>
    /// The early Tandy 1000 / PCjr's SN76496 (setting word <c>"tandy-early"</c>, user s42): its noise differs, so misses
    /// become a 237 Hz buzz and steps are near silent (docs/re/sound.md §3 "SN76496").
    /// </summary>
    TandyEarly,
    Amiga,
}

/// <summary>The configured sound device and the session-only mute used by Ctrl-S.</summary>
public sealed class SoundSetting(ClientSettings settings, Action<ClientSoundDevice?> apply, Action? save = null,
    Func<bool>? hasAmiga = null)
{
    private readonly object _lock = new();
    private ClientSoundDevice? _configured = ClientAudio.Parse(settings.Sound);
    private bool _muted;
    private bool AmigaAvailable => hasAmiga?.Invoke() == true;

    public string Row
    {
        get
        {
            lock (_lock)
                return "SOUND: " + (_muted ? "OFF" : _configured switch
                {
                    ClientSoundDevice.Speaker => "PC SPEAKER",
                    ClientSoundDevice.Tandy => "TANDY",
                    ClientSoundDevice.TandyEarly => "TANDY (EARLY)",
                    ClientSoundDevice.Amiga => AmigaAvailable ? "AMIGA" : "PC SPEAKER (AMIGA UNAVAILABLE)",
                    _ => "OFF",
                });
        }
    }

    /// <summary>Changes and saves the configured device, or turns sound off.</summary>
    public void Step(bool forward)
    {
        lock (_lock)
        {
            ClientSoundDevice? current = _muted ? null : _configured;
            _configured = (current, forward) switch
            {
                (null, true) or (ClientSoundDevice.Tandy, false) => ClientSoundDevice.Speaker,
                (ClientSoundDevice.Speaker, true) or (ClientSoundDevice.TandyEarly, false) => ClientSoundDevice.Tandy,
                (ClientSoundDevice.Tandy, true) or (ClientSoundDevice.Amiga, false) => ClientSoundDevice.TandyEarly,
                (ClientSoundDevice.TandyEarly, true) or (null, false) when AmigaAvailable => ClientSoundDevice.Amiga,
                (null, false) => ClientSoundDevice.TandyEarly,
                _ => null,
            };
            _muted = false;
            settings.Sound = ClientAudio.Word(_configured);
            apply(_configured);
            (save ?? settings.Save)();
        }
    }

    /// <summary>Mutes or restores the configured device without changing the settings file.</summary>
    public void ToggleMute()
    {
        lock (_lock)
        {
            if (_configured is null)
                return;
            _muted = !_muted;
            apply(_muted ? null : _configured);
        }
    }
}
