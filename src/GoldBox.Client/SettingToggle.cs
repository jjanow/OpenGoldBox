namespace GoldBox.Client;

/// <summary>
/// An on/off setting shown as a row of the controls screen (<see cref="Screens.ControlsScreen"/>): the label and the
/// word for each state (off = the original's behaviour), read and written through the settings it stands for.
/// Confirm, a click or left / right flip it; the change applies at once.
/// </summary>
public sealed class SettingToggle(string label, string offWord, string onWord, Func<bool> get, Action<bool> set)
{
    public string Label { get; } = label;
    public bool Value => get();

    /// <summary>The row as the screen shows it, e.g. <c>AFTER DEFEAT: QUIT</c>.</summary>
    public string Row => $"{Label}: {(Value ? onWord : offWord)}";

    public void Flip() => set(!get());

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
}
