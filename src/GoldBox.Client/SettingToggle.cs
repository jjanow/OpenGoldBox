namespace GoldBox.Client;

/// <summary>
/// A setting shown as a row of the controls screen (<see cref="Screens.ControlsScreen"/>): the label and the word for
/// each value (the first, or the one the setting names, = the original's behaviour), read and written through the
/// settings it stands for. Confirm or a click step to the next value, left / right step back and on (with two values
/// each flips it); the change applies at once. <see cref="Help"/>, when given, is shown under the rows while the row
/// is selected.
/// </summary>
public sealed class SettingToggle
{
    private readonly string[] _words;
    private readonly Func<int> _get;
    private readonly Action<int> _set;

    /// <summary>An on/off setting: <paramref name="offWord"/> for false, <paramref name="onWord"/> for true.</summary>
    public SettingToggle(string label, string offWord, string onWord, Func<bool> get, Action<bool> set)
        : this(label, [offWord, onWord], () => get() ? 1 : 0, i => set(i != 0))
    {
    }

    /// <summary>A setting with several values: <paramref name="get"/> / <paramref name="set"/> by index in <paramref name="words"/>.</summary>
    public SettingToggle(string label, IReadOnlyList<string> words, Func<int> get, Action<int> set, IReadOnlyList<string>? help = null)
    {
        Label = label;
        _words = [.. words];
        _get = get;
        _set = set;
        Help = help ?? [];
    }

    public string Label { get; }

    /// <summary>The value's index (0 or 1 for an on/off setting).</summary>
    public int Index => Math.Clamp(_get(), 0, _words.Length - 1);

    /// <summary>On (an on/off setting: the second word).</summary>
    public bool Value => Index != 0;

    /// <summary>Lines (up to two, 37 characters) explaining the row, shown while it is selected.</summary>
    public IReadOnlyList<string> Help { get; }

    /// <summary>The row as the screen shows it, e.g. <c>AFTER DEFEAT: QUIT</c>.</summary>
    public string Row => $"{Label}: {_words[Index]}";

    /// <summary>The next value (the last wraps to the first); with two values, a flip.</summary>
    public void Flip() => Step(true);

    /// <summary>The next (<paramref name="forward"/>) or previous value, wrapping.</summary>
    public void Step(bool forward) => _set((Index + (forward ? 1 : _words.Length - 1)) % _words.Length);

    /// <summary>
    /// QoL <see cref="ClientSettings.AfterDefeatMenu"/>: QUIT (off, the original quits after the party is destroyed,
    /// docs/re/start-menu.md §8) or PARTY MENU. Each change is saved.
    /// </summary>
    public static SettingToggle AfterDefeat(ClientSettings settings, Action? saved = null) =>
        new("AFTER DEFEAT", "QUIT", "PARTY MENU", () => settings.AfterDefeatMenu, v =>
        {
            settings.AfterDefeatMenu = v;
            (saved ?? settings.Save)();
        });

    /// <summary>
    /// QoL <see cref="ClientSettings.AnimationSpeed"/>: SLOWEST, SLOWER, ORIGINAL (off, the program's timings), FASTER,
    /// FASTEST, INSTANT. Each change is saved and handed to <paramref name="apply"/> (the running game's
    /// <see cref="Play.ScriptRunner.AnimationSpeed"/> and audio).
    /// </summary>
    public static SettingToggle Animation(ClientSettings settings, Action<Play.AnimationSpeed>? apply = null, Action? saved = null) =>
        new("ANIMATION", Enum.GetValues<Play.AnimationSpeed>().Select(Play.AnimationTiming.Label).ToArray(),
            () => (int)settings.AnimationSpeedChoice, i =>
            {
                settings.AnimationSpeedChoice = (Play.AnimationSpeed)i;
                apply?.Invoke(settings.AnimationSpeedChoice);
                (saved ?? settings.Save)();
            },
            ["FASTEST: INSTANT PLUS GAME SPEED 0", "(CAMP ALTER>SPEED, FIGHT DONE>SPEED)"]);
}
