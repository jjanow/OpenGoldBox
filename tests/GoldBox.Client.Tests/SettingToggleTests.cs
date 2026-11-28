using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The controls screen's on/off settings: AFTER DEFEAT (<see cref="ClientSettings.AfterDefeatMenu"/>, off = QUIT as the
/// original) flips with keys, pad and mouse, is saved on each change, and leaves the rows under it reachable.
/// </summary>
public class SettingToggleTests
{
    private sealed class Host : IScreenHost
    {
        public int Pops, Quits;
        public void Push(IScreen screen) { }
        public void Pop() => Pops++;
        public void Quit() => Quits++;
        public void SaveBindings() { }
    }

    private static void Press(IScreen s, ActionInput input, GameAction a)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        s.Update(input);
    }

    private static void Click(IScreen s, ActionInput input, int row)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (s.MapClick(new ScreenClick(20, row * 8 + 4, false)) is { } a)
            input.Inject(a);
        s.Update(input);
    }

    [Fact]
    public void After_defeat_is_off_by_default_and_reads_quit()
    {
        var settings = new ClientSettings();
        Assert.False(settings.AfterDefeatMenu);
        var t = SettingToggle.AfterDefeat(settings, () => { });
        Assert.Equal("AFTER DEFEAT: QUIT", t.Row);
        t.Flip();
        Assert.True(settings.AfterDefeatMenu);
        Assert.Equal("AFTER DEFEAT: PARTY MENU", t.Row);
        // The settings file keeps it.
        Assert.True(ClientSettings.FromJson(settings.ToJson()).AfterDefeatMenu);
    }

    [Fact]
    public void The_controls_screen_flips_after_defeat_with_keys_pad_and_mouse()
    {
        var settings = new ClientSettings();
        int saves = 0;
        var toggle = SettingToggle.AfterDefeat(settings, () => saves++);
        var art = new ClientArt(null, null, new ScreenPalette(), PorArtChoice.Best);
        var host = new Host();
        var screen = new ControlsScreen(new TextFont(new byte[177 * 8]), host, art, toggles: [toggle]);
        var input = new ActionInput(InputBindings.Defaults());

        // Up from the first action wraps to QUIT, then BACK, RESET, AFTER DEFEAT.
        for (int i = 0; i < 4; i++)
            Press(screen, input, GameAction.MenuUp);
        Press(screen, input, GameAction.Confirm);                // keyboard Enter / pad A
        Assert.True(settings.AfterDefeatMenu);
        Press(screen, input, GameAction.MenuRight);              // arrow / d-pad
        Assert.False(settings.AfterDefeatMenu);
        Press(screen, input, GameAction.MenuLeft);
        Assert.True(settings.AfterDefeatMenu);
        Assert.Equal(PorArtChoice.Best, art.Choice);             // the art row above is untouched
        // The rows: ART 17, AFTER DEFEAT 18, RESET 19, BACK 20, QUIT 21. The mouse flips it back.
        Click(screen, input, 18);
        Assert.False(settings.AfterDefeatMenu);
        Assert.Equal(4, saves);
        Click(screen, input, 17);
        Assert.Equal(PorArtChoice.Amiga, art.Choice);
        Assert.False(settings.AfterDefeatMenu);
        Assert.Equal(0, host.Pops);
        Click(screen, input, 20);                                // BACK
        Assert.Equal(1, host.Pops);
        Click(screen, input, 21);                                // QUIT
        Assert.Equal(1, host.Quits);
    }

    [Fact]
    public void Without_toggles_the_rows_stay_where_they_were()
    {
        var host = new Host();
        var screen = new ControlsScreen(new TextFont(new byte[177 * 8]), host);
        var input = new ActionInput(InputBindings.Defaults());
        Click(screen, input, 20);                                // BACK (rows 19–21 without art and toggles)
        Assert.Equal(1, host.Pops);
    }

    /// <summary>The two Amiga QoL options of s48: off by default (the original), saved, and on the command line.</summary>
    [Fact]
    public void The_amiga_qol_options_are_off_by_default_and_remembered()
    {
        var settings = new ClientSettings();
        Assert.False(settings.AmigaAltKeys);
        Assert.False(settings.AmigaGridOffText);
        int saves = 0;
        bool? alt = null, grid = null;
        var a = SettingToggle.AmigaAltKeys(settings, v => alt = v, () => saves++);
        var g = SettingToggle.AmigaGridOffText(settings, v => grid = v, () => saves++);
        Assert.Equal("AMIGA ALT-M/Q: OFF", a.Row);
        Assert.Equal("AMIGA INPUT GRID: OVER TEXT", g.Row);
        a.Flip();
        g.Flip();
        Assert.Equal((true, true, 2), (alt, grid, saves));
        Assert.Equal("AMIGA INPUT GRID: OFF TEXT", g.Row);
        Assert.All(a.Help.Concat(g.Help), h => Assert.True(h.Length <= 37, h));
        var back = ClientSettings.FromJson(settings.ToJson());
        Assert.True(back.AmigaAltKeys && back.AmigaGridOffText);
        Assert.True(ClientOptions.Parse(["--amiga-alt-keys"]).AmigaAltKeys);
        Assert.False(ClientOptions.Parse(["--no-amiga-grid-off-text"]).AmigaGridOffText);
        Assert.Null(ClientOptions.Parse([]).AmigaAltKeys);
        Assert.Contains("--amiga-grid-off-text", ClientOptions.Usage);
    }

    /// <summary>The roster-delete option of s61: off by default (the engine's roster keeps everyone), saved, and on the
    /// command line.</summary>
    [Fact]
    public void The_roster_delete_option_is_off_by_default_and_remembered()
    {
        var settings = new ClientSettings();
        Assert.False(settings.RosterDeletes);
        int saves = 0;
        var t = SettingToggle.RosterDeletes(settings, () => saves++);
        Assert.Equal("ROSTER ON SAVE: KEEP", t.Row);
        t.Flip();
        Assert.Equal(("ROSTER ON SAVE: DELETE", 1), (t.Row, saves));
        Assert.All(t.Help, h => Assert.True(h.Length <= 37, h));
        Assert.True(ClientSettings.FromJson(settings.ToJson()).RosterDeletes);
        Assert.True(ClientOptions.Parse(["--roster-deletes"]).RosterDeletes);
        Assert.False(ClientOptions.Parse(["--no-roster-deletes"]).RosterDeletes);
        Assert.Null(ClientOptions.Parse([]).RosterDeletes);
        Assert.Contains("--roster-deletes", ClientOptions.Usage);
    }
}
