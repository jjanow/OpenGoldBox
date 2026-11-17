using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>The private golden DOSBox-X captures, found above the test binaries; null when absent (public tree).</summary>
public static class Captures
{
    public static string? Dir { get; } = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory);

    public static bool Present => Dir is not null && PorData.Present;

    public const string SkipReason =
        "needs the private DOSBox captures (tests/fixtures-private/por/dosbox-v1.3) and the PoR v1.3 files (" + PorData.EnvVar + ")";

    public static CaptureReport Compare(CaptureScenario s) => new EclCaptureComparer(Dir!, PorData.Open()).Compare(s);
}

public sealed class CaptureFactAttribute : FactAttribute
{
    public CaptureFactAttribute()
    {
        if (!Captures.Present)
            Skip = Captures.SkipReason;
    }
}

public sealed class CaptureTheoryAttribute : TheoryAttribute
{
    public CaptureTheoryAttribute()
    {
        if (!Captures.Present)
            Skip = Captures.SkipReason;
    }
}

/// <summary>
/// The VM replays every golden capture (docs/re/dosbox-trace-method.md) and must reproduce it: state captures
/// cell for cell (apart from the listed host-owned cells) including RandSeed, trace captures instruction for
/// instruction. Skipped without the private fixtures and the game files.
/// </summary>
public class EclCaptureCompareTests
{
    [CaptureTheory]
    [InlineData("load_saveA", CaptureOutcome.Match, null)]
    [InlineData("step_west", CaptureOutcome.Match, null)]
    [InlineData("turn_left", CaptureOutcome.NoVmSteps, null)]
    [InlineData("encamp", CaptureOutcome.Match, null)]
    [InlineData("rest_interrupted", CaptureOutcome.Match, 2)]   // two 1d100 interval rolls before vector 3
    [InlineData("door_pick_fail", CaptureOutcome.Match, 6)]
    [InlineData("door_bash", CaptureOutcome.Match, 3)]
    [InlineData("combat_win", CaptureOutcome.NoVmSteps, null)]  // both captures inside COMBAT
    [InlineData("load_saveB", CaptureOutcome.Match, null)]
    [InlineData("edge_wrap_patched", CaptureOutcome.Match, null)]
    public void State_capture(string name, CaptureOutcome expected, int? hostDraws)
    {
        var r = Captures.Compare(PorGoldenCaptures.Find(name)!);
        Assert.True(r.Outcome == expected, r.ToString());
        Assert.Equal(hostDraws, r.HostDraws);
        Assert.Empty(r.Divergences);
    }

    [CaptureTheory]
    [InlineData("trace_load_step")]
    [InlineData("trace_camp_rest")]
    public void Trace_capture(string name)
    {
        var r = Captures.Compare(PorGoldenCaptures.Find(name)!);
        Assert.True(r.Outcome == CaptureOutcome.Match, r.ToString());
        Assert.Equal(r.TraceTotal, r.TraceMatched);
        Assert.True(r.TraceTotal > 50);
    }

    [CaptureFact]
    public void Rolls_from_the_captured_seed_reach_the_captured_seed()
    {
        // step_west: one RANDOM from the before-state RandSeed; the comparison checks the after RandSeed too.
        var r = Captures.Compare(PorGoldenCaptures.Find("step_west")!);
        Assert.Single(r.Rolls);
        Assert.Equal(CaptureOutcome.Match, r.Outcome);
    }

    [CaptureFact]
    public void A_wrong_replay_is_reported_as_a_divergence()
    {
        // Without the step, vector 1 runs on the old square: the comparison must notice.
        var s = PorGoldenCaptures.Find("step_west")!;
        var broken = s with { Actions = s.Actions.Where(a => a is not StepAction).ToList() };
        var r = Captures.Compare(broken);
        Assert.Equal(CaptureOutcome.Diverged, r.Outcome);
        Assert.NotNull(r.FirstDivergence);

        // Without vector 0 the instruction stream skips the move vector. (Dropping the step would not show in this
        // trace: vector 1 takes the same path on both squares.)
        var t = PorGoldenCaptures.Find("trace_load_step")!;
        var brokenTrace = t with { Actions = t.Actions.Where(a => a is not RunVectorAction { Vector: 0 }).ToList() };
        var rt = Captures.Compare(brokenTrace);
        Assert.Equal(CaptureOutcome.Diverged, rt.Outcome);
        Assert.True(rt.TraceMatched < rt.TraceTotal);
        Assert.Contains("context", rt.FirstDivergence);
    }
}
