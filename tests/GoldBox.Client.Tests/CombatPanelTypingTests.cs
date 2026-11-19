using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The status panel's weapon name (START 0x1583 → 0x77F8) types while DS:4A00 is set, and Aim's range line comes before
/// it (DOSBox-X s29: a fight entered by a step's COMBAT after a load, Phineas's turn and his Aim with Next / Prev; every
/// Client frame equal to one of DOSBox-X's).
/// </summary>
public class CombatPanelTypingTests
{
    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 3 };
        public readonly List<(DelayRequest D, int? Typed, string? Status, CombatMode Mode)> Delays = [];
        public readonly CombatChooser Chooser;
        public readonly Fight Fight;
        public readonly EclCharacter Me;

        public Rig(bool typing)
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest d)
                    Delays.Add((d, View.WeaponTyped, View.StatusLine, View.Mode));
                return null;
            }, View) { Typing = () => typing, Resources = () => new ClientResources { ItemWord = Word } };
            var map = new CombatMap(new CombatTables(new byte[64 * 4], new byte[64], new byte[16], new byte[16],
                [7, 2, 3, 6], new byte[8], new byte[8], new byte[60]));
            Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), map);
            View.Fight = Fight;
            Me = new EclCharacter { Name = "PHINEAS", [FightRules.InPlay] = 1 };
            var dagger = new byte[63];
            var name = "Dagger "u8.ToArray();
            dagger[0] = (byte)name.Length;
            name.CopyTo(dagger, 1);
            dagger[0x31] = 1;    // name word 1
            dagger[0x2E] = 8;    // base type 8; the zeroed table puts every type in the weapon slot
            dagger[0x34] = 1;    // readied
            Me.Items.Add(dagger);
        }

        private static string? Word(int id) => id == 1 ? "DAGGER" : null;

        public int Cells => CombatChooser.WeaponCells(ItemNames.Compose(Me.Items[0], Word));
    }

    [Fact]
    public void The_weapon_name_types_one_cell_per_delay_while_the_flag_is_set()
    {
        // An AI creature's turn start types its panel (a menu-driven one types once, at the menu loop's redraw).
        var r = new Rig(typing: true);
        r.Me[CombatRules.AutoCombat] = 1;
        r.Chooser.TurnStarted(r.Me);
        var typed = r.Delays.Where(d => d.Typed is not null).ToList();
        Assert.True(r.Cells > 0);
        Assert.Equal(r.Cells, typed.Count);
        Assert.Equal(Enumerable.Range(1, r.Cells), typed.Select(d => d.Typed!.Value));
        Assert.All(typed, d => Assert.Equal(TimeSpan.FromMilliseconds(30), d.D.Duration)); // DS:49E2 × 10 ms
        Assert.All(typed, d => Assert.True(d.D.Chained));
        Assert.Null(r.View.WeaponTyped);              // all shown afterwards
    }

    [Fact]
    public void A_menu_driven_creatures_turn_start_does_not_type_before_its_menu()
    {
        var r = new Rig(typing: true);
        r.Chooser.TurnStarted(r.Me);
        Assert.DoesNotContain(r.Delays, d => d.Typed is not null);
    }

    [Fact]
    public void Without_the_flag_or_at_speed_0_the_weapon_shows_at_once()
    {
        var r = new Rig(typing: false);
        r.Me[CombatRules.AutoCombat] = 1;
        r.Chooser.TurnStarted(r.Me);
        Assert.DoesNotContain(r.Delays, d => d.Typed is not null);

        var s = new Rig(typing: true);
        s.View.Speed = 0;
        s.Me[CombatRules.AutoCombat] = 1;
        s.Chooser.TurnStarted(s.Me);
        Assert.DoesNotContain(s.Delays, d => d.Typed is not null);
    }
}
