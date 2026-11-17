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
    /// since the AI cast aim (0x14611 / 0x148BE, s13). Later in the same capture, from q19 on, the Mirror Image effect's
    /// minutes run out in DOSBox but not in Core (no per-round effect clock in a fight yet); after that the replay drifts
    /// (not yet matched): Magic Missile from the same magic-user takes Phineas from 6 HP to
    /// dying (q24 → q25, its first Magic Missile slot cleared), a second Charm Person lands on Darkstar (q27 → q28),
    /// and the party falls in round 7 (q34, RandSeed d8fe9216).
    /// </summary>
    [CastsFact]
    public void The_magic_user_charms_Brother_Sean()
    {
        var r = Replay();
        var (_, _, diffs) = r.States.Single(x => x.State == "q13_rhiannon_menu");
        Assert.True(diffs.Count == 0, string.Join("; ", diffs));
        Assert.Contains("menu of #2 at draw 129", r.Trace);
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
