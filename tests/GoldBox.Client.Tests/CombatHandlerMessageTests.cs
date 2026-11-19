using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The monster specials' messages in the combat panel (class 0x0E handlers 83, 84, 88, 121 and the stoning 0xECB3,
/// docs/re/spells-effects.md "Handler messages", read s27): START 0x16EC with the row and pause flag each handler
/// passes, in the program's own words from the player's GAME.OVR. Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class CombatHandlerMessageTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    /// <summary>A chooser that records the panel's rows at every pause.</summary>
    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 1 };
        public readonly List<(TimeSpan Wait, IReadOnlyList<string> Rows)> Pauses = [];
        public readonly CombatChooser Chooser;

        public Rig(string dir)
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest d)
                    Pauses.Add((d.Duration, View.Panel.Lines));
                return null;
            }, View) { Text = new CombatText(new PorGameDirectory(dir).Program()) };
            View.Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), new GridGeometry());
        }

        public string T(CombatMsg m) => Chooser.Text[m].Trim();

        /// <summary>Panel row r (1-based screen row), trimmed.</summary>
        public static string Row(IReadOnlyList<string> rows, int r) => rows[r - 1].Trim();
    }

    private static EclCharacter Creature(string name, byte hostile) =>
        new() { Name = name, [FightRules.Hostile] = hostile, [FightRules.InPlay] = 1 };

    [Fact]
    public void Every_handler_message_comes_from_the_game_files()
    {
        if (Dir() is not { } dir)
            return;
        var t = new CombatText(new PorGameDirectory(dir).Program());
        CombatMsg[] ids = [CombatMsg.HandlerGazes, CombatMsg.HandlerGazeReflected, CombatMsg.HandlerStoned,
            CombatMsg.HandlerCharmGazes, CombatMsg.HandlerBreathes, CombatMsg.HandlerSpitsAcid];
        var ours = new CombatText();
        Assert.All(ids, m => Assert.False(string.IsNullOrWhiteSpace(t[m])));
        Assert.All(ids, m => Assert.NotEqual(ours[m], t[m])); // the program's words, not our fallback
    }

    [Fact]
    public void A_gaze_and_its_reflection_stay_in_the_panel_without_a_pause()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var basilisk = Creature("Basilisk", 1);
        var hero = Creature("Hero", 0);
        r.Chooser.EffectMessage(basilisk, EffectEvent.Gazes);
        Assert.Empty(r.Pauses);
        var rows = r.View.Panel.Lines;
        Assert.Equal("BASILISK", Rig.Row(rows, 10));
        Assert.Equal(r.T(CombatMsg.HandlerGazes), Rig.Row(rows, 11));

        // The reflection: START 0x16EC from row 12 clears rows 12–21 only; the gaze's two rows stay.
        r.Chooser.EffectMessage(hero, EffectEvent.GazeReflected);
        Assert.Empty(r.Pauses);
        rows = r.View.Panel.Lines;
        Assert.Equal("BASILISK", Rig.Row(rows, 10));
        Assert.Equal(r.T(CombatMsg.HandlerGazes), Rig.Row(rows, 11));
        Assert.Equal("HERO", Rig.Row(rows, 12));
        Assert.Equal(r.T(CombatMsg.HandlerGazeReflected), Rig.Row(rows, 13));

        // The stoning (0xECB3): from row 10 again, no pause.
        r.Chooser.EffectMessage(basilisk, EffectEvent.Stoned);
        Assert.Empty(r.Pauses);
        rows = r.View.Panel.Lines;
        Assert.Equal("BASILISK", Rig.Row(rows, 10));
        Assert.Equal(r.T(CombatMsg.HandlerStoned), Rig.Row(rows, 11));
        Assert.All(Enumerable.Range(12, 10), k => Assert.Equal("", Rig.Row(rows, k)));
    }

    [Fact]
    public void A_charm_gaze_shows_from_row_10_without_a_pause()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        r.Chooser.EffectMessage(Creature("Vampire", 1), EffectEvent.CharmGazes);
        Assert.Empty(r.Pauses);
        Assert.Equal("VAMPIRE", Rig.Row(r.View.Panel.Lines, 10));
        Assert.Equal(r.T(CombatMsg.HandlerCharmGazes), Rig.Row(r.View.Panel.Lines, 11));
    }

    [Theory]
    [InlineData(EffectEvent.Breathes, CombatMsg.HandlerBreathes)]
    [InlineData(EffectEvent.SpitsAcid, CombatMsg.HandlerSpitsAcid)]
    public void A_breath_shows_from_row_10_with_the_pause_and_clears_the_window(EffectEvent e, CombatMsg text)
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        r.Chooser.EffectMessage(Creature("Dragon", 1), e);
        var p = Assert.Single(r.Pauses);
        Assert.Equal(TimeSpan.FromMilliseconds(225), p.Wait); // PauseOnly at speed 1
        Assert.Equal("DRAGON", Rig.Row(p.Rows, 10));
        Assert.Equal(r.T(text), Rig.Row(p.Rows, 11));
        Assert.All(r.View.Panel.Lines.Skip(9).Take(12), l => Assert.Equal("", l.Trim())); // START 0x2383
    }
}
