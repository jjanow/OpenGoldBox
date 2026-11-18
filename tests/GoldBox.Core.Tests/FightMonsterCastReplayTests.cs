using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Monsters that cast (fixtures <c>fight_casts/</c> q01–q34, README "fight_casts", s13): from Thrender's menu in round 1
/// (q01) every party member was put on Quick (Q at each of the six menus: q01, q04, q07, q10, q13, q16) and the fight
/// ran to its end (the party destroyed in round 7). In it the 5th-level magic-user (list 6) casts Charm Person on
/// Brother Sean (q12 → q13: effect 0x0B), Magic Missile on Phineas (q24 → q25: 6 HP → dying) and Charm Person on
/// Darkstar (q27 → q28). The replay runs the wired fight from q01 with Quick at every menu; every captured state is
/// compared when the replay has made as many draws as the capture (RandSeed), right before the next draw.
/// </summary>
public class FightMonsterCastReplayTests(ITestOutputHelper output)
{
    private static readonly string? Dir = FightCapture.Folder("fight_casts");

    public sealed class CastsFactAttribute : FactAttribute
    {
        public CastsFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Dir is null)
                Skip = "needs the private DOSBox captures (fight_casts)";
        }
    }

    private sealed class TooLong : Exception;

    public sealed record Result(List<(string State, long Draws, List<string> Diffs)> States, long DrawsAtEnd, List<string> Trace);

    /// <summary>The q states after q01, with their draw counts from q01.</summary>
    public static List<(string Name, long Draws)> Checkpoints()
    {
        var from = Path.Combine(Dir!, "q01_thrender_menu_r1");
        return Directory.GetDirectories(Dir!, "q*").Select(Path.GetFileName).Where(n => n != "q01_thrender_menu_r1")
            .Select(n => (n!, FightCapture.Draws(from, Path.Combine(Dir!, n!)) ?? -1)).OrderBy(c => c.Item2).ToList();
    }

    public static Result Replay()
    {
        var s = FightCapture.Load(Path.Combine(Dir!, "q01_thrender_menu_r1"), new ScriptedChooser());
        var queue = new Queue<(string Name, long Draws)>(Checkpoints());
        var results = new List<(string, long, List<string>)>();
        void Check(long n)
        {
            while (queue.Count > 0 && queue.Peek().Draws <= n)
            {
                var (name, draws) = queue.Dequeue();
                results.Add((name, draws, draws == n
                    ? FightCapture.Diff(s.Fields(), FightCapture.Fields(Path.Combine(Dir!, name)))
                    : ["not reached at this draw count"]));
            }
        }
        long limit = queue.Max(c => c.Draws) + 200;
        s.Hooks.BeforeDraw = n =>
        {
            Check(n);
            if (n > limit)
                throw new TooLong();
        };
        var notes = new List<string>();
        try
        {
            s.RunMenu();
            for (int menus = 0; menus < 40 && s.ContinueToNextMenu() is { } c; menus++)
            {
                notes.Add($"menu of #{s.List.IndexOf(c)} at draw {s.Rng.Draws}");
                Check(s.Rng.Draws);
                s.RunMenu(c);
            }
        }
        catch (TooLong)
        {
            notes.Add($"stopped: still fighting {limit} draws after q01");
        }
        Check(s.Rng.Draws);
        return new Result(results, s.Rng.Draws, [.. notes, .. s.Trace]);
    }

    /// <summary>
    /// Round 1 up to the magic-user's Charm Person: the party menus come at the captured draw counts (Sean 18,
    /// Darkstar 36, Phineas 72 after q01) and q03, q04, q07, q10 and q12 match field for field. q06 and q09 were taken
    /// in the middle of a turn (Sean one step short of his square, Darkstar between two attacks): the replay compares
    /// right before the next draw, so their differences are of that window only and are not asserted.
    /// </summary>
    [CastsFact]
    public void Round_1_matches_DOSBox_up_to_the_first_monster_cast()
    {
        var r = Replay();
        foreach (var name in new[] { "q03", "q04_sean_menu", "q07_darkstar_menu", "q10_phineas_menu", "q12" })
        {
            var (_, _, diffs) = r.States.Single(x => x.State == name);
            Assert.True(diffs.Count == 0, $"{name}: {string.Join("; ", diffs)}");
        }
        Assert.Contains("menu of #3 at draw 18", r.Trace);
        Assert.Contains("menu of #4 at draw 36", r.Trace);
        Assert.Contains("menu of #5 at draw 72", r.Trace);
    }

    /// <summary>
    /// DOSBox, q12 → q13 (76 → 129 draws after q01): the 5th-level magic-user (list 6) casts Charm Person (spell 10) at
    /// Brother Sean: its first memorised copy (slot 0x18) goes, Sean gets effect 0x0B (record 0B 00 00 A5 01) and the
    /// magic-user's +0x0A points at him; Bakshi keeps 7 HP; Rhiannon's menu comes 129 draws after q01. Core matches
    /// since the AI cast aim (0x14611 / 0x148BE, s13). The rest of the capture: <see cref="The_fight_runs_to_the_party_fall"/>.
    /// </summary>
    [CastsFact]
    public void The_magic_user_charms_Brother_Sean()
    {
        var r = Replay();
        var (_, _, diffs) = r.States.Single(x => x.State == "q13_rhiannon_menu");
        Assert.True(diffs.Count == 0, string.Join("; ", diffs));
        Assert.Contains("menu of #2 at draw 129", r.Trace);
    }

    /// <summary>
    /// The whole capture after q13: Rhiannon's Mirror Image (1 minute left at q16) runs out at the end of round 2 (the
    /// end of round's clock 0x241A1 → 0x23DCC, <see cref="FightKit.WireClock"/>); Magic Missile takes Phineas to dying
    /// (q24 → q25); Charm Person lands on Darkstar (q27 → q28); the charmed Brother Sean (on the enemy side, NPC byte
    /// above 0x7F) begins Bless (casting time 3) and is picked again in the round to cast it (q29 → q30: his slot 0x29
    /// gone, his 10 HP kept); Hold Person holds Thrender, who is then struck down (q31); Magic Missile takes Rhiannon
    /// down and the party falls in round 7. Every state taken between turns matches field for field, and the fight ends
    /// after 1060 draws. The states taken in the middle of a turn (q19, q20, q22–q27, q29, q31, q32) differ only in that
    /// window (the acting creature's turn bytes, or a dropped creature's square and effects before the drop removes
    /// them): the replay compares right before the next draw. [?] q34 (the end screen, list freed) comes 3 draws later
    /// (1063): those draws after the fight are not identified and the replay does not run the after-combat step.
    /// </summary>
    [CastsFact]
    public void The_fight_runs_to_the_party_fall()
    {
        var r = Replay();
        foreach (var name in new[] { "q16_bakshi_menu", "q21", "q28", "q30", "q33" })
        {
            var (_, _, diffs) = r.States.Single(x => x.State == name);
            Assert.True(diffs.Count == 0, $"{name}: {string.Join("; ", diffs)}");
        }
        Assert.DoesNotContain(r.Trace, t => t.StartsWith("stopped", StringComparison.Ordinal));
        Assert.Equal(1060, r.DrawsAtEnd);
        Assert.Equal(1063, Checkpoints().Single(x => x.Name == "q34_the_end").Draws);
    }

    /// <summary>Set GOLDBOX_FIGHT_TRACE to print every draw, the menus reached and each captured state's differences.</summary>
    [CastsFact]
    public void Dump()
    {
        if (!FightCapture.TraceDraws)
            return;
        var r = Replay();
        foreach (var t in r.Trace)
            output.WriteLine(t);
        foreach (var (state, draws, diffs) in r.States)
            output.WriteLine($"{state} ({draws}): {(diffs.Count == 0 ? "ok" : string.Join("; ", diffs))}");
        output.WriteLine($"draws at end {r.DrawsAtEnd}");
    }
}
