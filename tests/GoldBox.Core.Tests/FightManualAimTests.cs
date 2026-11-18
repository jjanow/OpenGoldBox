using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// Manual aim (0x155F5, docs/re/combat-menu.md "Aim"): the cursor on squares, on a real combat map (the DOSBox state
/// <c>fight_casts/b02_darkstar_menu</c>: Darkstar on 29,12, a 5th-level magic-user on 27,13). Program read; the DOSBox
/// captures of Manual and of an item use are in <see cref="FightManualUseCaptureTests"/>. Also the item use through
/// the menu's aim and the revival square test (0x317E1) on the same state.
/// </summary>
public class FightManualAimTests(ITestOutputHelper output)
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

    /// <summary>
    /// Attack mode (the menu's Aim, square mode 0): the cursor starts on the shown candidate (Darkstar himself) and moves
    /// W, W, S onto the magic-user (27,13). On the way: his own square is not offered (attack mode), the bare square
    /// 28,12 is not (square mode 0), Bakshi on 27,12 is (an ally: the attack question would come), the magic-user is
    /// (distance 2 within the darts' range 5). Target there makes the same attack as Next / Target on the candidate list:
    /// same draws, same state.
    /// </summary>
    [CastsFact]
    public void Manual_attack_on_a_creature_matches_the_candidate_aim()
    {
        var manual = new ScriptedChooser(FightCommand.Aim)
        {
            Manual = new([new ManualInput(6), new ManualInput(6), new ManualInput(4), ManualInput.Target]),
        };
        var a = FightCapture.Load(S("b02_darkstar_menu"), manual);
        a.RunMenu();
        var seen = manual.ManualSeen.Select(v => (v.X, v.Y, v.Creature is { } c ? a.List.IndexOf(c) : -1, v.Distance, v.TargetOffered)).ToList();
        Assert.Equal([(29, 12, 4, 0, false), (28, 12, -1, 1, false), (27, 12, 1, 2, true), (27, 13, 7, 2, true)], seen);
        Assert.All(manual.ManualSeen, v => Assert.Equal(5, v.Range));

        var listed = new ScriptedChooser(FightCommand.Aim);
        var b = FightCapture.Load(S("b02_darkstar_menu"), listed);
        listed.AimAt = b.List[7];
        b.RunMenu();
        Assert.True(a.Rng.Draws > 0);
        Assert.Equal(b.Rng.Draws, a.Rng.Draws);
        Assert.Empty(FightCapture.Diff(a.Fields(), b.Fields()));
    }

    /// <summary>
    /// A spell with area mode 8–0x0E aims in square mode 1 (0x148BE → 0x14611 → 0x15D53): Manual may take a bare square.
    /// Darkstar casts Sleep (21, mode 9: the list around the square, radius 1) at the empty square 28,12: picked, the
    /// aimed square DS:6DB9/6DBA = 28,12, his +0x0A = none (the result has no creature), the memorised copy gone, and he
    /// faces the square (west, 6).
    /// </summary>
    [CastsFact]
    public void Sleep_aimed_at_a_bare_square()
    {
        var chooser = new ScriptedChooser(FightCommand.Cast)
        {
            Spell = 21,
            Manual = new([new ManualInput(6), ManualInput.Target]),
        };
        var s = FightCapture.Load(S("b02_darkstar_menu"), chooser);
        var darkstar = s.List[4];
        darkstar[0x2A] = 21;
        s.RunMenu();
        Assert.Equal((28, 12, (EclCharacter?)null, true), (chooser.ManualSeen[^1].X, chooser.ManualSeen[^1].Y,
            chooser.ManualSeen[^1].Creature, chooser.ManualSeen[^1].TargetOffered));
        var spells = (FightSpells)s.Fight.Spells;
        Assert.Equal((28, 12), spells.Aim);
        Assert.Null(s.Fight.Record(darkstar).Other);
        Assert.Equal(0, darkstar[0x2A]);
        Assert.Equal(6, s.Fight.Record(darkstar)[CombatRecord.Facing]);
    }

    /// <summary>
    /// The same bare square for Magic Missile (15, area mode 4: picks in square mode 0) is not offered; Target there
    /// clears the result and Manual ends, the aim goes back to the candidate list.
    /// </summary>
    [CastsFact]
    public void A_bare_square_is_not_offered_in_square_mode_0()
    {
        var chooser = new ScriptedChooser(FightCommand.Cast)
        {
            Spell = 15,
            Manual = new([new ManualInput(6), ManualInput.Target]),
        };
        var s = FightCapture.Load(S("b02_darkstar_menu"), chooser);
        chooser.AimAt = s.List[7];
        s.RunMenu();
        Assert.False(chooser.ManualSeen[^1].TargetOffered);
        Assert.Null(chooser.ManualSeen[^1].Creature);
        Assert.Contains(s.List[7], chooser.AimSeen);                 // back on the list, then the candidate's Target
        Assert.Equal(0, s.List[4][0x2A]);
        Assert.Same(s.List[7], s.Fight.Record(s.List[4]).Other);
    }

    /// <summary>
    /// The menu's Use (0x21F32 → 0x22AC4 → 0x27C38 with ai = 0) aims an item's spell through the menu's own aim
    /// (<see cref="FightMenu.PickForItem"/>, the candidate list here): Darkstar uses a readied item (a made-up copy of
    /// his first item) to hold Magic
    /// Missile (power 15, 2 charges) at the magic-user. The aim walks the candidates to it, the item loses a charge, his
    /// memorised Magic Missile stays (an item use removes no copy), his +0x0A points at the target and the missile does
    /// damage (item level 6: 3d4 + 3).
    /// </summary>
    [CastsFact]
    public void Item_use_aims_through_the_menus_aim()
    {
        var chooser = new ScriptedChooser(FightCommand.Use);
        var s = FightCapture.Load(S("b02_darkstar_menu"), chooser);
        var darkstar = s.List[4];
        var mu = s.List[7];
        var item = (byte[])darkstar.Items[0].Clone(); // a copy of his first item, readied, one of it
        item[CombatRules.ItemEquipped] = 1;
        item[0x39] = 1;
        darkstar.Items.Add(item);
        darkstar[FightItemScreens.ItemCount]++;
        item[0x3C] = 2;
        item[0x3D] = 15;
        item[0x3E] = 0;
        chooser.UseItem = item;
        chooser.AimAt = mu;
        int hp = mu[FightRules.Hp];
        s.RunMenu();
        Assert.Contains(mu, chooser.AimSeen);
        Assert.Equal(1, item[0x3C]);
        Assert.Equal(15, darkstar[0x2A]);
        Assert.Same(mu, s.Fight.Record(darkstar).Other);
        Assert.InRange(hp - mu[FightRules.Hp], 1, 15);
    }

    /// <summary>
    /// 0x2C753's test 0x317E1 in a fight (<see cref="EffectHandlers.PutBackOnSquare"/>): Bakshi (27,12) is downed and
    /// leaves a body; revived, he stands on his square again and his body entry is gone. Downed again with Thrender
    /// moved onto that square, the revival fails and he stays down.
    /// </summary>
    [CastsFact]
    public void Revive_puts_the_creature_back_on_its_square_when_free()
    {
        var s = FightCapture.Load(S("b02_darkstar_menu"), new ScriptedChooser());
        var effects = (EffectHandlers)s.Fight.Effects;
        Assert.True(effects.InCombat);
        var bakshi = s.List[1];
        s.Map.Downed(bakshi, s.Fight.State);
        Assert.False(s.Map.OnMap(bakshi));
        Assert.Contains(s.Map.Bodies, b => b is not null && ReferenceEquals(b.Creature, bakshi));
        Assert.True(effects.Revive(bakshi, 5));
        Assert.True(s.Map.OnMap(bakshi));
        Assert.Equal((27, 12), s.Map.Position(bakshi));
        Assert.DoesNotContain(s.Map.Bodies, b => b is not null && ReferenceEquals(b.Creature, bakshi));
        Assert.Equal(5, bakshi[FightRules.Hp]);

        s.Map.Downed(bakshi, s.Fight.State);
        bakshi[FightRules.Status] = 6;
        Assert.True(s.Map.Place(s.List[0], 27, 12));
        Assert.False(effects.Revive(bakshi, 5));
        Assert.False(s.Map.OnMap(bakshi));
        Assert.Equal(6, bakshi[FightRules.Status]);
    }
}
