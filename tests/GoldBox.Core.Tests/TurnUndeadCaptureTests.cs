using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Turn undead against real undead (fixtures <c>fight_undead/</c>, README "fight_undead", s13): save M (= test save D:
/// the party of save A put on disk 4, Valhingen Graveyard map 10 at 11,13 N; one step north loads 4 skeletons (MON4
/// 34) and 3 zombies (35)). Darkstar, Rhiannon and Phineas quit; at Brother Sean's menu (cleric level 1) Turn.
/// </summary>
public class TurnUndeadCaptureTests(ITestOutputHelper output)
{
    private static readonly string? Dir = FightCapture.Folder("fight_undead");

    public sealed class UndeadFactAttribute : FactAttribute
    {
        public UndeadFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Dir is null)
                Skip = "needs the private DOSBox captures (fight_undead)";
        }
    }

    private static string S(string state) => Path.Combine(Dir!, state);

    /// <summary>
    /// DOSBox: two draws, count 1d12 = 11 and roll 1d20 = 15 (d05 has that RandSeed; it was taken during the messages,
    /// with two skeletons marked so far); the 15 beats the level-1 values for skeleton (row 1) and zombie (row 2), and
    /// 11 covers all seven, so every skeleton and zombie is turned (+0x10 = 1), none destroyed, Sean's +0x11 set. The
    /// next pick (13 creatures) gives Bakshi's menu (d06, 15 draws after d04), which matches field for field.
    /// </summary>
    [UndeadFact]
    public void A_level_1_cleric_turns_four_skeletons_and_three_zombies()
    {
        var chooser = new ScriptedChooser(FightCommand.Turn);
        var s = FightCapture.Load(S("d04_sean_menu"), chooser);
        var sean = s.List[3];
        Assert.Same(sean, s.Current);
        s.RunMenu();
        foreach (var t in s.Trace)
            output.WriteLine(t);
        Assert.Equal(2, s.Rng.Draws);
        Assert.Equal(FightCapture.Seed(S("d05_turn_rolled")), s.Rng.Seed);
        Assert.Contains(FightNotice.Turned, chooser.Notices);
        Assert.DoesNotContain(FightNotice.Destroyed, chooser.Notices);
        for (int i = 6; i <= 12; i++)
        {
            Assert.Equal(1, s.Fight.Record(s.List[i])[CombatRecord.Turned]);
            Assert.Equal(0, s.List[i][FightRules.Status]);
        }
        Assert.Equal(1, s.Fight.Record(sean)[CombatRecord.TriedTurning]);
        var next = s.ContinueToNextMenu();
        Assert.Equal(FightCapture.Draws(S("d04_sean_menu"), S("d06_bakshi_menu")), s.Rng.Draws);
        Assert.Same(s.List[1], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("d06_bakshi_menu")));
        foreach (var d in diffs)
            output.WriteLine("d06: " + d);
        Assert.Empty(diffs);
    }
}
