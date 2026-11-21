using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The attacker's pose (read s38, docs/re/combat-menu.md "Attack pose"): MakeAttack draws frame 1 once (0x31232) and
/// nothing keeps it; a rebuilt view (ScrollView with the point outside its band, every flight's ScrollView(…, 255))
/// draws every creature with frame 0, the cell redraw 0x30A0F draws the creature on its cell with frame 0, and
/// CreatureDowned scrolls only when the downed creature is not wholly in view, then redraws the view centre's cell.
/// </summary>
public class CombatPoseTests
{
    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 1 };
        public readonly CombatChooser Chooser;
        public readonly CombatMap Map;
        public readonly EclCharacter Attacker = Creature("ATTACKER", 1);
        public readonly EclCharacter Defender = Creature("DEFENDER", 0);
        /// <summary>At each wait: the attacker posed, the downed picture's square, the view's corner.</summary>
        public readonly List<(bool Posed, (int X, int Y)? Marker, int ViewX, bool Flying)> Waits = [];

        public Rig((int X, int Y) attacker, (int X, int Y) defender)
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest)
                    Waits.Add((View.Attacking.Contains(Attacker), View.HitMarker, View.ViewX, View.Flying is not null));
                return null;
            }, View);
            var icon = new byte[64];
            Array.Fill(icon, (byte)0xFF);
            icon[8] = 0; icon[9] = 0; // size 1: one cell
            Map = new CombatMap(new CombatTables(new byte[64 * 4], icon, new byte[16], new byte[16], [7, 2, 3, 6],
                new byte[8], new byte[8], new byte[60]));
            View.Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), Map);
            Put(1, Attacker, attacker);
            Put(2, Defender, defender);
            Map.SlotCount = 3;
            Map.Rebuild();
        }

        private void Put(int slot, EclCharacter c, (int X, int Y) at)
        {
            Map.SetSlotCreature(slot, c);
            Map.SetSlotPosition(slot, at.X, at.Y);
            Map.SetSlotSize(slot, 1);
        }

        private static EclCharacter Creature(string name, byte hostile)
        {
            var c = new EclCharacter { Name = name, [FightRules.Hostile] = hostile, [FightRules.InPlay] = 1 };
            c[FightRules.Hp] = 10;
            c[FightRules.HpMax] = 10;
            return c;
        }

        public void Down()
        {
            Defender[FightRules.InPlay] = 0;
            Defender[FightRules.Status] = 6;
            Chooser.AttackEnd(new AttackMessage(Attacker, Defender, 0, 10, true, true, false), down: true);
        }
    }

    [Fact]
    public void A_missiles_flight_draws_the_attacker_standing()
    {
        // MakeAttack: frame 1 at 0x14113, Delay(100), then the missile 0x14EAB → START 0x25C9, whose first
        // ScrollView(…, 255) rebuilds the view with frame 0 for everyone (DOSBox-X s33: Darkstar's and Phineas's darts
        // under Quick, the icon differed in ~30 frames per throw while the Client kept the pose).
        var r = new Rig((2, 2), (5, 2));
        r.View.Attacking.Add(r.Attacker);
        r.Chooser.Flight(FlightAnimation.Missile(2, 2, 5, 2, 0x49, 2));
        Assert.NotEmpty(r.Waits);
        Assert.All(r.Waits, w => Assert.False(w.Posed));
        Assert.Empty(r.View.Attacking);
    }

    [Fact]
    public void A_scroll_that_keeps_the_view_redraws_only_its_own_cell()
    {
        var r = new Rig((2, 2), (5, 5));
        r.View.Attacking.Add(r.Attacker);
        r.View.Scroll(5, 5, 3); // inside the band: no rebuild; 0x30A0F redraws (5, 5) only
        Assert.Contains(r.Attacker, r.View.Attacking);
        r.View.Scroll(2, 2, 3); // the attacker's own cell: drawn with frame 0
        Assert.DoesNotContain(r.Attacker, r.View.Attacking);
        r.View.Attacking.Add(r.Attacker);
        r.View.Scroll(20, 2, 3); // outside the band: the view is rebuilt
        Assert.Empty(r.View.Attacking);
    }

    [Fact]
    public void The_downed_picture_keeps_the_pose_when_the_creature_is_in_view()
    {
        // CreatureDowned 0x3154B: wholly in view (0x30E54 with 1) → no ScrollView, nothing redraws the attacker.
        var r = new Rig((2, 2), (2, 3));
        r.View.Attacking.Add(r.Attacker);
        r.Down();
        var w = Assert.Single(r.Waits);
        Assert.Equal(((int X, int Y)?)(2, 3), w.Marker);
        Assert.True(w.Posed);
        Assert.Equal(0, w.ViewX);
        // Off the view's centre (3, 3): the closing ScrollView(centre, 3) leaves it posed until the attack's end.
        Assert.Contains(r.Attacker, r.View.Attacking);
    }

    [Fact]
    public void An_attacker_on_the_view_centre_stands_when_the_downed_picture_goes()
    {
        // CreatureDowned's end 0x3177B–0x3179C: ScrollView(view centre, 3, none) redraws the centre cell (0x30A0F,
        // frame 0). DOSBox-X s39 (vampire MON4 23, seed 12345): the vampire on the centre square (28, 13) posed through
        // Rhiannon's downed picture (29, 14) and stood in the frame where it went; at (26, 12) or (28, 12) it stayed posed.
        var r = new Rig((3, 3), (4, 4));
        r.View.Attacking.Add(r.Attacker);
        r.Down();
        var w = Assert.Single(r.Waits);
        Assert.Equal(((int X, int Y)?)(4, 4), w.Marker);
        Assert.True(w.Posed);
        Assert.Equal(0, w.ViewX);
        Assert.DoesNotContain(r.Attacker, r.View.Attacking);
    }

    [Fact]
    public void The_downed_picture_of_a_creature_out_of_view_scrolls_and_the_attacker_stands()
    {
        // Not wholly in view → ScrollView(its square, 3) at 0x315ED: the view is rebuilt (frame 0 for everyone), then
        // the downed picture on the creature's square and the pause.
        var r = new Rig((5, 2), (8, 2));
        r.View.Attacking.Add(r.Attacker);
        r.Down();
        var w = Assert.Single(r.Waits);
        Assert.Equal(((int X, int Y)?)(8, 2), w.Marker);
        Assert.False(w.Posed);
        Assert.True(r.View.InView(8, 2));
    }

    private sealed class TimedSound(List<(bool Posed, (int X, int Y)? Marker, int ViewX, bool Flying)> waits) : GoldBox.Core.Sound.ISoundSink
    {
        public readonly List<(string Call, int Waits)> Calls = [];
        public void Cue(GoldBox.Core.Sound.SoundCue cue) => Calls.Add((cue.ToString(), waits.Count));
        public void BeginResident() => Calls.Add(("(", waits.Count));
        public void EndResident() => Calls.Add((")", waits.Count));
    }

    [Fact]
    public void The_body_sound_starts_with_the_downed_picture_and_its_bracket_spans_the_pause()
    {
        // CreatureDowned 0x3154B: set auto-stop(0) at 0x31554, the body cue at 0x31744 once the picture is drawn, the
        // pause, set auto-stop(10) at 0x317D6. DOSBox-X s39 (PC speaker, a goblin guard downed by Thrender): the body
        // tune began 0.17 s after the picture appeared; the Client played it after the pause (the map removal's cue).
        var r = new Rig((2, 2), (2, 3));
        var sound = new TimedSound(r.Waits);
        r.View.Fight!.State.Sound = sound;
        r.View.Fight.State.FrontEndPlaysDowned = true;
        r.Down();
        Assert.Equal([("(", 0), ("Body", 0), (")", 1)], sound.Calls);
        // The map removal then makes no sound of its own.
        r.Map.Downed(r.Defender, r.View.Fight.State, sound: !r.View.Fight.State.FrontEndPlaysDowned);
        Assert.Equal(3, sound.Calls.Count);
    }
}
