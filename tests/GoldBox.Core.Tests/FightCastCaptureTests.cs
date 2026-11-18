using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Casts in a DOSBox-X fight (fixtures <c>fight_casts/</c>, README "fight_casts", s13): save K (= test save B: the
/// party of save A in the Slums, Darkstar with Magic Missile, Rhiannon with Mirror Image, against two 5th-level
/// magic-users and three goblin guards). Each test loads a state where the game waited at a party member's menu, plays
/// the captured keys through the menu (<see cref="ScriptedChooser"/>) and goes on to the next menu the capture shows.
/// </summary>
public class FightCastCaptureTests(ITestOutputHelper output)
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

    private static string S(string state) => Path.Combine(Dir!, state);

    private void Report(List<string> diffs, string state)
    {
        foreach (var d in diffs)
            output.WriteLine($"{state}: {d}");
    }

    /// <summary>
    /// Darkstar (magic-user level 1) casts Magic Missile at the 5th-level magic-user on 27,13 (list 7, 20 HP). DOSBox:
    /// no damage and **no draw** (damage (level ÷ 2)d4 + level ÷ 2 = 0d4 + 0), the memorised copy (slot 0x2A) gone,
    /// the actions over; the next 11 draws are the pick of Thrender (state b04).
    /// </summary>
    [CastsFact]
    public void Level_1_Magic_Missile_does_no_damage_and_draws_nothing()
    {
        var chooser = new ScriptedChooser(FightCommand.Cast) { Spell = 15 };
        var s = FightCapture.Load(S("b02_darkstar_menu"), chooser);
        var darkstar = s.List[4];
        var mu = s.List[7];
        chooser.AimAt = mu;
        Assert.Same(darkstar, s.Current);
        Assert.Equal(15, darkstar[0x2A]);
        s.RunMenu();
        Assert.Equal(0, s.Rng.Draws);
        Assert.Equal(20, mu[FightRules.Hp]);
        Assert.Equal(0, darkstar[0x2A]);
        Assert.Equal(21, darkstar[0x2B]);
        Assert.Equal(0, s.Fight.Record(darkstar).InitiativeValue);
        var next = s.ContinueToNextMenu();
        Assert.Same(s.List[0], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("b04_thrender_menu")));
        Report(diffs, "b04_thrender_menu");
        Assert.Empty(diffs);
    }

    /// <summary>
    /// The same cast: after the cast the caster faces its target (combat record +9: 6 →
    /// 5, from 29,12 toward 27,13) and +0x0A points at it (list 7), as after a melee attack (spells-effects.md "Cast":
    /// "facing and animation"; FightSpells.CastNow, 0x27D9C).
    /// </summary>
    [CastsFact]
    public void Cast_faces_the_target_and_remembers_it()
    {
        var chooser = new ScriptedChooser(FightCommand.Cast) { Spell = 15 };
        var s = FightCapture.Load(S("b02_darkstar_menu"), chooser);
        chooser.AimAt = s.List[7];
        s.RunMenu();
        Assert.Equal(5, s.Fight.Record(s.List[4])[CombatRecord.Facing]);
        Assert.Same(s.List[7], s.Fight.Record(s.List[4]).Other);
    }


    /// <summary>
    /// Rhiannon (elf fighter/magic-user 1/1) casts Mirror Image (spell 32) on herself. DOSBox: 1d4 = 4 images (effect
    /// 0x1C, 2 minutes, param 4), the memorised copy (slot 0x29) gone, then her elf resistance (handler 107, 1d100)
    /// runs on the new effect; the play goes on to Bakshi's menu (state b07, 31 draws after b05). Only her facing
    /// after the cast's facing (a cast on herself leaves +9 = 0) is also matched.
    /// </summary>
    [CastsFact]
    public void Mirror_Image_cast_rolls_1d4_images()
    {
        var chooser = new ScriptedChooser(FightCommand.Cast) { Spell = 32 };
        var s = FightCapture.Load(S("b05_rhiannon_menu"), chooser);
        var rhiannon = s.List[2];
        Assert.Same(rhiannon, s.Current);
        s.RunMenu();
        Assert.Equal(2, s.Rng.Draws);
        Assert.Equal("1C02000400", Convert.ToHexString(rhiannon.Effects[^1], 0, 5));
        Assert.Equal(0, rhiannon[0x29]);
        var next = s.ContinueToNextMenu();
        Assert.Equal(31, s.Rng.Draws);
        Assert.Equal(FightCapture.Draws(S("b05_rhiannon_menu"), S("b07_bakshi_menu")), s.Rng.Draws);
        Assert.Same(s.List[1], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("b07_bakshi_menu")));
        Report(diffs, "b07_bakshi_menu");
        Assert.Empty(diffs);
    }

    /// <summary>
    /// Brother Sean attacks Rhiannon (an ally, AC 0, with 4 mirror images): Aim, Next, Next, Target, Yes. DOSBox: the
    /// attack misses, no image is lost, $6E33 (allies attacked) = 1; the round ends and round 1 starts at Thrender's
    /// menu (state q01, 73 draws after b08). Everything matches, the image's minutes included
    /// (<see cref="Effect_minutes_count_down_at_the_end_of_a_round"/>).
    /// </summary>
    [CastsFact]
    public void Attack_on_an_ally_with_mirror_images()
    {
        var chooser = new ScriptedChooser(FightCommand.Aim);
        var s = FightCapture.Load(S("b08_sean_menu"), chooser);
        chooser.AimAt = s.List[2];
        s.RunMenu();
        Assert.Equal(1, s.Fight.Memory.Read(FightMenu.AttackedAlliesCell));
        Assert.Equal(7, s.List[2][FightRules.Hp]);
        Assert.Equal("1C02000400", Convert.ToHexString(s.List[2].Effects[^1], 0, 5));
        var next = s.ContinueToNextMenu();
        Assert.Equal(FightCapture.Draws(S("b08_sean_menu"), S("q01_thrender_menu_r1")), s.Rng.Draws);
        Assert.Same(s.List[0], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S("q01_thrender_menu_r1")));
        Report(diffs, "q01_thrender_menu_r1");
        Assert.Empty(diffs);
    }

    /// <summary>
    /// The same play: at the end of round 0 the Mirror Image effect's minutes (byte +1) go 2 → 1 (one round = one
    /// minute: the end of round's clock 0x241A1 runs the effect timeout 0x23DCC, <see cref="FightKit.WireClock"/>).
    /// </summary>
    [CastsFact]
    public void Effect_minutes_count_down_at_the_end_of_a_round()
    {
        var chooser = new ScriptedChooser(FightCommand.Aim);
        var s = FightCapture.Load(S("b08_sean_menu"), chooser);
        chooser.AimAt = s.List[2];
        s.RunMenu();
        s.ContinueToNextMenu();
        Assert.Equal("1C01000400", Convert.ToHexString(s.List[2].Effects[^1], 0, 5));
    }
}
