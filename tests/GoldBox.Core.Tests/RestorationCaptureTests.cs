using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Restoration from a scroll on a monster (fixtures <c>fight_restoration/</c>, README "fight_restoration", s18): save A's
/// party with one drained level each, Brother Sean carrying a clerical scroll of two Restorations, against three goblin
/// guards. Sean moved next to a goblin, used the scroll and aimed at it (<c>aim_goblin</c>, taken before Target),
/// <c>after</c> right after the cast. A goblin guard has no class level and drain bytes 255/255, so no class qualifies
/// and the routine raises the record byte at its uninitialised index: DOSBox gave index 8 (gender 0x9E 0 → 1).
/// </summary>
public class RestorationCaptureTests(ITestOutputHelper output)
{
    private static readonly string? Dir = FightCapture.Folder("fight_restoration");

    public sealed class RestorationFactAttribute : FactAttribute
    {
        public RestorationFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Dir is null)
                Skip = "needs the private DOSBox captures (fight_restoration)";
        }
    }

    private static bool PointerByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0xCC or >= 0x104 and < 0x10C;

    [RestorationFact]
    public void Restoration_on_a_goblin_matches_the_dosbox_record()
    {
        var s = FightCapture.Load(Path.Combine(Dir!, "aim_goblin"), new ScriptedChooser());
        var after = FightCapture.Load(Path.Combine(Dir!, "after"), new ScriptedChooser());
        var sean = s.List[3];
        var goblin = s.List[7];
        Assert.Equal(255, goblin[0x74]);
        Assert.Equal(0, goblin[0x9E]);
        var spells = (FightSpells)s.Fight.Spells;
        spells.Targets.Clear();
        spells.Targets.Add(goblin);
        spells.Routine(56, sean);

        Assert.Equal(0, s.Rng.Draws);
        // `after` was taken a few seconds on: by then the goblin had been recalculated (START 0x1758: THAC0 0x110 from
        // the new base THAC0 0x2D), consistent with its turn start (0x9A8A); the trigger was not pinned. The routine
        // itself does not recalculate.
        s.Fight.Recalculate!(goblin);
        var want = after.List[7];
        var diffs = new List<string>();
        for (int b = 0; b < EclCharacter.RecordSize; b++)
            if (!PointerByte(b) && goblin[b] != want[b])
                diffs.Add($"+{b:X2}: {goblin[b]} vs DOSBox {want[b]}");
        foreach (var d in diffs)
            output.WriteLine(d);
        Assert.Empty(diffs);
        Assert.Equal(1, goblin[0x96 + FightSpells.RestorationResidueIndex]); // gender, from the residue index 8
        Assert.Equal(10_000_000, goblin[0xAC] | (goblin[0xAD] << 8) | (goblin[0xAE] << 16) | (goblin[0xAF] << 24));
    }
}
