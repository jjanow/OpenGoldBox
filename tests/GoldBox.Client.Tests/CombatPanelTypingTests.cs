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
        public readonly CombatMap Map;

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
            Map = map;
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
    public void The_ais_weapon_readying_draws_the_panel_again_and_types_the_weapon()
    {
        // 0xBF8A's redraw (START 0x1583 at 0xC4D7, when its readying changed anything): DOSBox-X s39, after the menu's
        // Quick the panel is cleared and "11 DARTS" types again from an empty line (the Client had kept the line).
        var r = new Rig(typing: true);
        var ai = new MonsterAi(new byte[40]);
        var fight = new Fight(new EclMemory(), new EncounterState(),
            new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0,
            new MenuController(r.Chooser, ai), r.Map);
        r.View.Fight = fight;
        r.Chooser.Attach(fight);
        Assert.NotNull(ai.PanelRedrawn);
        r.Me[CombatRules.AutoCombat] = 1;
        Assert.Null(r.View.ShownLines);
        ai.PanelRedrawn!(r.Me);
        var typed = r.Delays.Where(d => d.Typed is not null).ToList();
        Assert.Equal(Enumerable.Range(1, r.Cells), typed.Select(d => d.Typed!.Value));
        Assert.Same(r.Me, r.View.ShownLines!.Value.Creature);
        r.Chooser.Detach(fight);
        Assert.Null(ai.PanelRedrawn);
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

    [Fact]
    public void A_spells_aim_leaves_row_23_with_the_spell_label()
    {
        // 0x1529B draws "Range = n" only in attack mode (0x152CD); Manual writes row 23 only in attack mode (0x15722,
        // 0x15768). DOSBox-X s30 (Darkstar's Sleep): the cast's spell label stays on row 23 through the aim.
        var r = new Rig(typing: false);
        var menu = new FightMenu(r.Fight, r.Me, r.Chooser);
        r.View.StatusLine = "SPELL:SLEEP";
        r.Chooser.ChooseAim(menu, new AimView(r.Me, 0, 1, true) { Attack = false });
        Assert.Equal("SPELL:SLEEP", r.View.StatusLine);
        r.Chooser.ChooseManual(menu, new ManualView(0, 0, null, 3, 1, false, false) { LineReached = false });
        Assert.Equal("SPELL:SLEEP", r.View.StatusLine);

        // Attack mode: the range line, cleared by Manual when the line does not get through.
        r.Chooser.ChooseAim(menu, new AimView(r.Me, 2, 1, true));
        Assert.EndsWith("2", r.View.StatusLine);
        r.Chooser.ChooseManual(menu, new ManualView(0, 0, null, 3, 1, false, true) { LineReached = false });
        Assert.Null(r.View.StatusLine);
    }

    [Fact]
    public void Moving_neither_retypes_the_panel_nor_redraws_the_box()
    {
        // DOSBox-X s37 (seeded flee run): after M the menu's box stays under the creature; each step changes the map
        // and row 24 only (no panel typing, no box at the new square).
        var r = new Rig(typing: true);
        r.Map.SetSlotCreature(1, r.Me);
        r.Map.SetSlotPosition(1, 10, 10);
        r.Map.SetSlotSize(1, 1);
        r.View.Cursor = (10, 10);
        var menu = new FightMenu(r.Fight, r.Me, r.Chooser);
        r.Chooser.ChooseMove(menu, 12);
        Assert.Empty(r.Delays);
        Assert.Equal((10, 10), r.View.Cursor!.Value);
        Assert.Equal(CombatMode.Move, r.View.Mode);
        // The prompt stays on row 24 (through the menu's panel typing) until a menu or question is written there.
        Assert.EndsWith("12", r.View.MovePrompt);
        r.Chooser.ConfirmFlee(menu);
        Assert.Null(r.View.MovePrompt);
    }

    [Fact]
    public void The_panel_keeps_the_name_colour_and_hp_it_was_drawn_with()
    {
        // DOSBox-X s37: a vampire at HP 1 killed by Magic Missile kept its yellow name and "1" on the panel through the
        // damage and death texts; only the next START 0x1583 draws the lines again.
        var r = new Rig(typing: false);
        r.Me[FightRules.Hostile] = 1;
        r.Me[FightRules.Hp] = 1;
        r.Me[CombatRules.AutoCombat] = 1;
        r.Chooser.TurnStarted(r.Me);
        r.Me[FightRules.Hp] = 0;
        r.Me[FightRules.InPlay] = 0;
        Assert.Equal((r.Me, (byte)14, 1), r.View.ShownLines!.Value);
        r.Chooser.TurnStarted(r.Me);
        Assert.Equal((r.Me, (byte)12, 0), r.View.ShownLines!.Value);
    }

    [Fact]
    public void The_turns_end_takes_the_cursor_box_off_the_creatures_square()
    {
        // 0x99FD redraws the cell at the creature's square after its turn (0x9AD9–0x9AF1): DOSBox-X s30, no box under
        // the last member while the continue-battle question is up.
        var r = new Rig(typing: false);
        r.Map.SetSlotCreature(1, r.Me);
        r.Map.SetSlotPosition(1, 10, 10);
        r.Map.SetSlotSize(1, 1);
        Assert.True(r.Map.OnMap(r.Me));
        r.Chooser.TurnStarted(r.Me);
        Assert.Equal(r.Map.Position(r.Me), r.View.Cursor!.Value);
        r.Chooser.TurnEnded(r.Me);
        Assert.Null(r.View.Cursor);
    }
}
