namespace GoldBox.Client.Audio;

public enum ClientSoundDevice
{
    Speaker,
    Tandy,
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
                (ClientSoundDevice.Speaker, true) or (ClientSoundDevice.Amiga, false) => ClientSoundDevice.Tandy,
                (ClientSoundDevice.Tandy, true) or (null, false) when AmigaAvailable => ClientSoundDevice.Amiga,
                (null, false) => ClientSoundDevice.Tandy,
                _ => null,
            };
            _muted = false;
            settings.Sound = _configured?.ToString().ToLowerInvariant() ?? "off";
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
