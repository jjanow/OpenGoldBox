using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Manual aim and an item use in a DOSBox-X fight (fixtures <c>fight_manual_use/</c>, README "fight_manual_use", s18):
/// save K as test save B (the fight of <c>fight_casts</c>: the party of save A against two 5th-level magic-users and
/// three goblin guards), with Phineas carrying a readied Wand (ITEM1.DAX block 9 item 3: 20 charges, power 88 = spell
/// 65, 2d4 + 2) as his first item (<c>saves/CHRDATP6.*</c>). Round 0, own boot: Phineas uses the wand on a
/// magic-user (p01 → p03); Thrender attacks through Manual (p03 → p04); Rhiannon Done, Quit (p04 → p05); Darkstar
/// throws darts through Manual (p05 → p06); Bakshi Done, Quit (p06 → p07). Every state was taken at a party member's
/// menu, so each test runs the captured keys and goes on to the next menu, where every compared field must match.
/// </summary>
public class FightManualUseCaptureTests(ITestOutputHelper output)
{
    private static readonly string? Dir = FightCapture.Folder("fight_manual_use");

    public sealed class CaptureFactAttribute : FactAttribute
    {
        public CaptureFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (Dir is null)
                Skip = "needs the private DOSBox captures (fight_manual_use)";
        }
    }

    private const int Thrender = 0, Bakshi = 1, Rhiannon = 2, Sean = 3, Darkstar = 4, Phineas = 5, Mu1 = 6, Mu2 = 7;
    private static readonly ManualInput North = new(0), South = new(4), West = new(6);

    private static string S(string state) => Path.Combine(Dir!, state);

    /// <summary>Goes on from the menu just played to the next menu and compares it with <paramref name="state"/>.</summary>
    private void ContinueTo(FightCapture s, string from, string state, int creature)
    {
        var next = s.ContinueToNextMenu();
        Assert.Equal(FightCapture.Draws(S(from), S(state)), s.Rng.Draws);
        Assert.Same(s.List[creature], next);
        var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S(state)));
        foreach (var d in diffs)
            output.WriteLine($"{state}: {d}");
        Assert.Empty(diffs);
    }

    /// <summary>
    /// Phineas: U (Items), U (Use) on the wand, the aim's candidates Next × 5 to the second magic-user (list 7), Target.
    /// DOSBox: 20 → 13 HP (2d4 + 2 = 7), the wand 20 → 19 charges, Thrender's menu next (p03). The state p02, taken at
    /// the item's aim (no draw yet), equals p01.
    /// </summary>
    [CaptureFact]
    public void Wand_use_through_the_menu()
    {
        Assert.Equal(0, FightCapture.Draws(S("p01_phineas_menu"), S("p02_wand_aim")));
        var chooser = new ScriptedChooser(FightCommand.Use);
        var s = FightCapture.Load(S("p01_phineas_menu"), chooser);
        var phineas = s.List[Phineas];
        Assert.Same(phineas, s.Current);
        var wand = phineas.Items[0];
        Assert.Equal((79, 20, 88), (wand[0x2E], wand[0x3C], wand[0x3D]));
        chooser.UseItem = wand;
        chooser.AimAt = s.List[Mu2];
        s.RunMenu();
        Assert.Equal(13, s.List[Mu2][FightRules.Hp]);
        Assert.Equal(19, wand[0x3C]);
        Assert.Same(s.List[Mu2], s.Fight.Record(phineas).Other);
        ContinueTo(s, "p01_phineas_menu", "p03_thrender_menu", Thrender);
    }

    /// <summary>
    /// Thrender (flail): A, M, then Up, Left, Down, T. DOSBox: the cursor starts on him ("Range = 0", no Target), Up is
    /// a bare square (range 1, no Target: square mode 0), Left is Bakshi (an ally, range 1, Target offered), Down is the
    /// wounded magic-user (list 7, range 1, Target offered); Target makes the melee attack: 13 → 6 HP. Rhiannon's menu
    /// next (p04).
    /// </summary>
    [CaptureFact]
    public void Manual_melee_attack()
    {
        var chooser = new ScriptedChooser(FightCommand.Aim) { Manual = new([North, West, South, ManualInput.Target]) };
        var s = FightCapture.Load(S("p03_thrender_menu"), chooser);
        Assert.Same(s.List[Thrender], s.Current);
        s.RunMenu();
        var seen = chooser.ManualSeen.Select(v => (v.Creature is { } c ? s.List.IndexOf(c) : -1, v.Distance, v.TargetOffered)).ToList();
        Assert.Equal([(Thrender, 0, false), (-1, 1, false), (Bakshi, 1, true), (Mu2, 1, true)], seen);
        Assert.Equal(6, s.List[Mu2][FightRules.Hp]);
        ContinueTo(s, "p03_thrender_menu", "p04_rhiannon_menu", Rhiannon);
    }

    /// <summary>Rhiannon: D, Q (no dice). Darkstar's menu next (p05).</summary>
    [CaptureFact]
    public void Done_quit_to_Darkstar()
    {
        var s = FightCapture.Load(S("p04_rhiannon_menu"), new ScriptedChooser(FightCommand.Done));
        s.RunMenu();
        ContinueTo(s, "p04_rhiannon_menu", "p05_darkstar_menu", Darkstar);
    }

    /// <summary>
    /// Darkstar (11 darts, range 5): A, M, then Left × 3, T. DOSBox: "Range = 1" on a bare square (no Target), 2 on
    /// Bakshi (Target), 3 on the other magic-user (list 6, Target); three darts are thrown (11 → 8), the magic-user
    /// 20 → 19 HP. The monsters' turns follow (Bakshi 7 → 2 HP) up to Bakshi's menu (p06).
    /// </summary>
    [CaptureFact]
    public void Manual_dart_throw()
    {
        var chooser = new ScriptedChooser(FightCommand.Aim) { Manual = new([West, West, West, ManualInput.Target]) };
        var s = FightCapture.Load(S("p05_darkstar_menu"), chooser);
        Assert.Same(s.List[Darkstar], s.Current);
        s.RunMenu();
        var seen = chooser.ManualSeen.Select(v => (v.Creature is { } c ? s.List.IndexOf(c) : -1, v.Distance, v.TargetOffered)).ToList();
        Assert.Equal([(Darkstar, 0, false), (-1, 1, false), (Bakshi, 2, true), (Mu1, 3, true)], seen);
        Assert.All(chooser.ManualSeen, v => Assert.Equal(5, v.Range));
        Assert.Equal(8, s.List[Darkstar].Items[0][0x39]);
        ContinueTo(s, "p05_darkstar_menu", "p06_bakshi_menu", Bakshi);
    }

    /// <summary>
    /// The whole capture as one replay from p01, as <c>fight_moves</c> is replayed: the wand, Manual with the flail,
    /// Done / Quit, Manual with the darts, Done / Quit, one fight state carried through (remembered targets, aim and
    /// item state included); every next menu (p03–p07) matches.
    /// </summary>
    [CaptureFact]
    public void The_capture_replays_from_p01()
    {
        var chooser = new ScriptedChooser(FightCommand.Use, FightCommand.Aim, FightCommand.Done, FightCommand.Aim, FightCommand.Done);
        var s = FightCapture.Load(S("p01_phineas_menu"), chooser);
        chooser.UseItem = s.List[Phineas].Items[0];
        chooser.AimAt = s.List[Mu2];
        s.RunMenu();
        var steps = new (string State, int Creature, Action? Before)[]
        {
            ("p03_thrender_menu", Thrender, () => { chooser.AimAt = null; chooser.Manual = new([North, West, South, ManualInput.Target]); }),
            ("p04_rhiannon_menu", Rhiannon, null),
            ("p05_darkstar_menu", Darkstar, () => chooser.Manual = new([West, West, West, ManualInput.Target])),
            ("p06_bakshi_menu", Bakshi, null),
            ("p07_sean_menu", Sean, null),
        };
        foreach (var (state, creature, before) in steps)
        {
            var next = s.ContinueToNextMenu();
            Assert.Equal(FightCapture.Draws(S("p01_phineas_menu"), S(state)), s.Rng.Draws);
            Assert.Same(s.List[creature], next);
            var diffs = FightCapture.Diff(s.Fields(), FightCapture.Fields(S(state)));
            foreach (var d in diffs)
                output.WriteLine($"{state}: {d}");
            Assert.Empty(diffs);
            if (state == "p07_sean_menu")
                break;
            before?.Invoke();
            s.RunMenu(next);
        }
        Assert.Equal(8, chooser.ManualSeen.Count);
    }

    /// <summary>Bakshi: D, Q. The goblins' turns up to Brother Sean's menu (p07).</summary>
    [CaptureFact]
    public void Done_quit_to_Sean()
    {
        var s = FightCapture.Load(S("p06_bakshi_menu"), new ScriptedChooser(FightCommand.Done));
        s.RunMenu();
        ContinueTo(s, "p06_bakshi_menu", "p07_sean_menu", Sean);
    }
}
