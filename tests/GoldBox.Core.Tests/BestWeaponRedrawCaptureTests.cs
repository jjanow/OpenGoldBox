using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;

namespace GoldBox.Core.Tests;

/// <summary>
/// 0xBF8A's panel redraw (START 0x1583 at 0xC4D7 when [bp−0x1D] is set) on the s39 boar fight's party
/// (<c>fight_boar/c17_boar_revived</c>). DOSBox-X s39 after Quick: Darkstar (darts, no off-hand: the shield branch, 0x100
/// &lt; 2 with an empty off-hand slot, 0xC46C–0xC4BC), Phineas (dagger unreadied → readied, 0xC336–0xC3C3) and Thrender
/// (flail, no shield: the shield branch) are drawn again; Bakshi (two-handed sword: chosen = readied, 0x100 rebuilt to 2
/// by START 0x1758 at 0xC3CD, so neither the &gt; 2 nor the &lt; 2 branch), Rhiannon and Brother Sean (weapon and the best
/// shield: [bp−0x1C] = 0) are not; their typing after Quick is MakeAttack's panel (0x140FE).
/// </summary>
public class BestWeaponRedrawCaptureTests
{
    private static readonly string? Dir = FightCapture.Folder("fight_boar");

    public sealed class BoarFactAttribute : FactAttribute
    {
        public BoarFactAttribute()
        {
            if (Dir is null || !PorData.Present)
                Skip = "needs the private DOSBox captures (fight_boar) and " + PorData.EnvVar;
        }
    }

    [BoarFact]
    public void Only_a_change_or_an_empty_off_hand_below_two_hands_redraws_the_panel()
    {
        var s = FightCapture.Load(Path.Combine(Dir!, "c17_boar_revived"), new ScriptedChooser());
        var redrawn = new List<string>();
        var ai = new MonsterAi(FightCapture.Kit.AiSteps) { PanelRedrawn = c => redrawn.Add(c.Name) };
        foreach (var c in s.Fight.List.Where(c => c[FightRules.Hostile] == 0))
            ai.EquipWeapon(s.Fight, c);
        Assert.Equal(["THRENDER GRONE", "DARKSTAR", "PHINEAS"], redrawn);
        var bakshi = s.Fight.List.Single(c => c.Name == "BAKSHI");
        Assert.Equal(2, bakshi[MonsterAi.HandsUsed]);
        Assert.Contains(bakshi.Items, i => i[0x2E] == 38 && i[CombatRules.ItemEquipped] != 0); // the sword stays readied
    }
}
