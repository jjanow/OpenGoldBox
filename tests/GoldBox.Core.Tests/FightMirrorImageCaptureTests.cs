using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// A melee hit on a Mirror Image caster (fixtures <c>fight_mirror/</c>, README "fight_mirror", s13): save L (= test
/// save C: save K's fight, Darkstar, AC 7, also with Mirror Image). Darkstar cast it first (4 images, effect 0x1C, 2
/// minutes, param 4); Phineas quit; the 5th-level magic-user (list 6) charmed Brother Sean, and Sean, now fighting
/// for the monsters, hit Darkstar with his mace: 5 HP → 0 (dying), and the images stayed at 4.
/// </summary>
public class FightMirrorImageCaptureTests(ITestOutputHelper output)
{
    private static readonly string? Dir = FightCapture.Folder("fight_mirror");

    public sealed class MirrorFactAttribute : FactAttribute
    {
        public MirrorFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Dir is null)
                Skip = "needs the private DOSBox captures (fight_mirror)";
        }
    }

    private static string S(string state) => Path.Combine(Dir!, state);

    /// <summary>
    /// c03 was taken in Sean's (charmed, AI) turn right before his attack, c04 three draws later: 1d20 = 19 (a hit on
    /// AC 7), 1d6 = 5 + 1 = 6 damage (5 HP → 0, dying, out of play), then the Mirror Image handler's 1d(param + 1) =
    /// 1d5 = 5. The images stay at 4 (effect still 1C 02 00 04): the roll is made on a weapon hit, but only a spell
    /// being cast (DS:681B) lets an image take the hit (spells-effects.md handler 28). Core's melee attack makes the
    /// same three draws with the same result (c03 already holds the attack's count on Darkstar, +0x0F = 1, so the
    /// replay makes the attack itself, MakeAttack 0x1407F). The other differences at c04 are of the capture's moment
    /// (taken during the hit's messages: Darkstar's icon still on 29,12, Sean's actions not yet ended).
    /// </summary>
    [MirrorFact]
    public void A_weapon_hit_on_a_Mirror_Image_caster_rolls_for_the_images_but_is_not_absorbed()
    {
        var s = FightCapture.Load(S("c03_sean_charmed_turn"), new ScriptedChooser());
        var sean = s.List[3];
        var darkstar = s.List[4];
        Assert.Same(sean, s.Current);
        Assert.Equal("1C02000400", Convert.ToHexString(darkstar.Effects[^1], 0, 5));
        s.Fight.MakeAttack(sean, darkstar);
        foreach (var t in s.Trace)
            output.WriteLine(t);
        Assert.Equal(3, s.Rng.Draws);
        Assert.Equal(FightCapture.Seed(S("c04_after_sean_hit")), s.Rng.Seed);
        Assert.Equal(0, darkstar[FightRules.Hp]);
        Assert.Equal((byte)CreatureStatus.Dying, darkstar[FightRules.Status]);
        Assert.Equal(0, darkstar[FightRules.InPlay]);
        Assert.Equal("1C02000400", Convert.ToHexString(darkstar.Effects[^1], 0, 5));
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("c04_after_sean_hit")));
        foreach (var d in diffs)
            output.WriteLine(d);
        Assert.Equal(["#4 at off vs (29, 12) size 1", "#3 +03 0 vs 4", "#3 +05 0 vs 1", "#3 +06 0 vs 12"], diffs);
    }

    /// <summary>
    /// The whole stretch from Phineas's menu (c02: Done, Quit) to Thrender's (c05, 46 draws), all through Core's wired
    /// fight: both magic-users cast Charm Person (one on Sean, which takes: effect 0B 00 00 A5 01; one on Bakshi, which
    /// does not), the charmed Sean hits Darkstar through his images, then the pick of Thrender. Matches field for field.
    /// </summary>
    [MirrorFact]
    public void Phineas_quits_and_the_monsters_charm_Sean_who_hits_Darkstar()
    {
        var s = FightCapture.Load(S("c02_phineas_menu"), new ScriptedChooser(FightCommand.Done));
        s.RunMenu();
        var next = s.ContinueToNextMenu();
        Assert.Equal(FightCapture.Draws(S("c02_phineas_menu"), S("c05_thrender_menu")), s.Rng.Draws);
        Assert.Equal("0B0000A501", Convert.ToHexString(s.List[3].Effects[^1], 0, 5));
        Assert.Equal("1C02000400", Convert.ToHexString(s.List[4].Effects[^1], 0, 5));
        Assert.Equal((byte)CreatureStatus.Dying, s.List[4][FightRules.Status]);
        Assert.Same(s.List[0], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("c05_thrender_menu")));
        Assert.True(diffs.Count == 0, string.Join("; ", diffs));
    }
}
