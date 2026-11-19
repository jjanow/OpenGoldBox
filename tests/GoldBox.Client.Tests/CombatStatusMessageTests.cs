using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The status changes in the combat panel (0xECB3 and its callers, docs/re/spells-effects.md "Status changes", read
/// s29): each caller's text by START 0x16EC from row 10 without the pause flag; when the status is set, CreatureDowned's
/// picture and pause for a creature still out of play, PauseOnly and START 0x17E3; the messages the handlers show
/// before it (the poisoned hit, the rot, the level drain). The program's words from the player's GAME.OVR. Skipped
/// without GOLDBOX_POR_DIR.
/// </summary>
public class CombatStatusMessageTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class Idle : IFightController
    {
        public void Act(Fight fight, EclCharacter creature, bool auto) { }
    }

    /// <summary>A chooser that records the panel's rows and the downed picture's square at every pause (typing excluded).</summary>
    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 1 };
        public readonly List<(int Ms, IReadOnlyList<string> Rows, (int X, int Y)? Marker)> Pauses = [];
        public readonly CombatChooser Chooser;
        public readonly PorProgramData Program;

        public Rig(string dir)
        {
            Program = new PorGameDirectory(dir).Program()!;
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest { Chained: false } d)
                    Pauses.Add(((int)d.Duration.TotalMilliseconds, View.Panel.Lines, View.HitMarker));
                return null;
            }, View) { Text = new CombatText(Program) };
            View.Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), new GridGeometry());
        }

        public string T(CombatMsg m) => Chooser.Text[m].Trim();

        /// <summary>Panel row r (1-based screen row), trimmed.</summary>
        public static string Row(IReadOnlyList<string> rows, int r) => rows[r - 1].Trim();

        /// <summary>Rows joined with single spaces (a wrapped text read back as one line).</summary>
        public static string Join(IReadOnlyList<string> rows, int from, int to) =>
            string.Join(" ", Enumerable.Range(from, to - from + 1).Select(k => Row(rows, k)).Where(x => x.Length > 0));

        public static string Flat(string t) => string.Join(" ", t.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        public bool MessageAreaClear => View.Panel.Lines.Skip(9).Take(12).All(l => l.Trim() == "");
    }

    private static EclCharacter Creature(string name, byte hostile, bool inPlay = true)
    {
        var c = new EclCharacter { Name = name, [FightRules.Hostile] = hostile, [FightRules.InPlay] = (byte)(inPlay ? 1 : 0) };
        c[FightRules.Hp] = 20;
        c[FightRules.HpMax] = 20;
        c[FightRules.Size] = 1;
        return c;
    }

    private static readonly CombatMsg[] Ids =
    [
        CombatMsg.StatusPoisonDeath, CombatMsg.StatusCollapses, CombatMsg.StatusRotsAway, CombatMsg.StatusPoisonKilled,
        CombatMsg.StatusDeadForever, CombatMsg.StatusDrainKilled, CombatMsg.StatusFallsDead, CombatMsg.Poisoned,
        CombatMsg.Rots, CombatMsg.Drained, CombatMsg.DrainedOneLevel, CombatMsg.DrainedLevels,
    ];

    [Fact]
    public void Every_status_message_comes_from_the_game_files()
    {
        if (Dir() is not { } dir)
            return;
        var t = new CombatText(new PorGameDirectory(dir).Program());
        var ours = new CombatText();
        Assert.All(Ids, m => Assert.False(string.IsNullOrWhiteSpace(t[m])));
        Assert.All(Ids, m => Assert.NotEqual(ours[m], t[m]));
        // The drain's status-6 text and the poisoned hit's are separate copies (CS:2118, CS:153B) with the same words;
        // handler 22's (CS:0764) has its own.
        Assert.Equal(t[CombatMsg.StatusDrainKilled], t[CombatMsg.StatusPoisonKilled]);
        Assert.NotEqual(t[CombatMsg.StatusPoisonDeath], t[CombatMsg.StatusPoisonKilled]);
    }

    [Fact]
    public void A_creature_turned_to_stone_waits_twice_then_the_panel_clears()
    {
        // 0xECB3 on a creature in play: the text from row 10 (no pause flag), then CreatureDowned's pause (still out of
        // play), PauseOnly (0xED44) and START 0x17E3.
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var hero = Creature("Hero", 0);
        r.Chooser.EffectMessage(hero, EffectEvent.Stoned);
        Assert.Empty(r.Pauses);
        hero[FightRules.InPlay] = 0;
        hero[FightRules.Status] = 7;
        r.Chooser.EffectMessage(hero, EffectEvent.StatusDone);
        Assert.Equal([225, 225], r.Pauses.Select(p => p.Ms));
        Assert.All(r.Pauses, p =>
        {
            Assert.Equal("HERO", Rig.Row(p.Rows, 10));
            Assert.Equal(r.T(CombatMsg.HandlerStoned), Rig.Row(p.Rows, 11));
        });
        Assert.True(r.MessageAreaClear);
    }

    [Fact]
    public void A_creature_back_in_play_after_trigger_0x0D_gets_only_PauseOnly()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var orc = Creature("Orc", 1);
        r.Chooser.EffectMessage(orc, EffectEvent.FallsDead);
        r.Chooser.EffectMessage(orc, EffectEvent.StatusDone); // still in play: no CreatureDowned
        Assert.Equal([225], r.Pauses.Select(p => p.Ms));
        Assert.Equal(r.T(CombatMsg.StatusFallsDead), Rig.Row(r.Pauses[0].Rows, 11));
        Assert.True(r.MessageAreaClear);
    }

    [Theory]
    [InlineData(EffectEvent.PoisonDeath, CombatMsg.StatusPoisonDeath)]
    [InlineData(EffectEvent.Collapses, CombatMsg.StatusCollapses)]
    [InlineData(EffectEvent.RotsAway, CombatMsg.StatusRotsAway)]
    [InlineData(EffectEvent.PoisonKilled, CombatMsg.StatusPoisonKilled)]
    [InlineData(EffectEvent.DeadForever, CombatMsg.StatusDeadForever)]
    [InlineData(EffectEvent.DrainKilled, CombatMsg.StatusDrainKilled)]
    [InlineData(EffectEvent.FallsDead, CombatMsg.StatusFallsDead)]
    public void A_status_text_shows_from_row_10_and_stays_without_a_pause(EffectEvent e, CombatMsg text)
    {
        // A creature already dead, stone or gone: 0xECB3 returns after its text, which stays until the next one.
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        r.Chooser.EffectMessage(Creature("Zombie", 0, inPlay: false), e);
        Assert.Empty(r.Pauses);
        var rows = r.View.Panel.Lines;
        Assert.Equal("ZOMBIE", Rig.Row(rows, 10));
        Assert.Equal(Rig.Flat(r.T(text)), Rig.Join(rows, 11, 21));
    }

    [Fact]
    public void A_poisoned_hit_pauses_without_clearing_and_the_death_text_replaces_it()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var hero = Creature("Hero", 0);
        r.Chooser.EffectMessage(hero, EffectEvent.Poisoned);
        var p = Assert.Single(r.Pauses);
        Assert.Equal(225, p.Ms);
        Assert.Equal("HERO", Rig.Row(p.Rows, 10));
        Assert.Equal(r.T(CombatMsg.Poisoned), Rig.Row(p.Rows, 11));
        Assert.Equal(r.T(CombatMsg.Poisoned), Rig.Row(r.View.Panel.Lines, 11)); // 0x101F7 is PauseOnly alone
        r.Chooser.EffectMessage(hero, EffectEvent.PoisonKilled);
        Assert.Equal(r.T(CombatMsg.StatusPoisonKilled), Rig.Row(r.View.Panel.Lines, 11));
    }

    [Fact]
    public void The_rot_shows_with_the_pause_and_clears()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        r.Chooser.EffectMessage(Creature("Hero", 0), EffectEvent.Rots);
        var p = Assert.Single(r.Pauses);
        Assert.Equal(r.T(CombatMsg.Rots), Rig.Row(p.Rows, 11));
        Assert.True(r.MessageAreaClear);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_level_drain_names_the_levels_lost_then_the_target_is_gone(int count)
    {
        // 0x10D7D: CS:20EF + (count > 1: Str(count) + CS:20DF; else CS:20E7), START 0x16EC row 10 with the pause flag;
        // an animated target then goes (0xECB3 status 8, 0x10E15) with its end.
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var fx = new EffectHandlers(_ => 0);
        var fight = r.View.Fight!;
        fx.Attach(fight);
        fight.Spells = new FightSpells(r.Program.Spells()!, fx, _ => 0);
        fx.Event += r.Chooser.EffectMessage;
        var zombie = Creature("Zombie", 1);
        zombie[FightRules.Status] = 1;
        fx.LevelDrain(zombie, count);
        Assert.Equal([225, 225, 225], r.Pauses.Select(p => p.Ms)); // the drain's, CreatureDowned's, PauseOnly
        var t = r.Chooser.Text;
        string drained = t[CombatMsg.Drained] + (count > 1 ? count + t[CombatMsg.DrainedLevels] : t[CombatMsg.DrainedOneLevel]);
        Assert.Equal("ZOMBIE", Rig.Row(r.Pauses[0].Rows, 10));
        Assert.Equal(Rig.Flat(drained), Rig.Join(r.Pauses[0].Rows, 11, 21));
        Assert.Equal(r.T(CombatMsg.StatusDeadForever), Rig.Join(r.Pauses[1].Rows, 11, 21));
        Assert.True(r.MessageAreaClear);
    }

    [Fact]
    public void An_animated_creature_downed_by_spell_damage_shows_its_collapse_under_the_downed_picture()
    {
        // A real fight on the combat map: 0x2C22E's damage words and pause, the down words (0x2C4E0, no wait), then
        // 0x2BEF4 removes Animate Dead's 0x20 and handler 32 prints its text through 0xECB3 over them (row 10, no
        // pause; dead already, so nothing more), then CreatureDowned's picture on its square and pause, START 0x17E3.
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var map = new CombatMap(r.Program.CombatTables());
        for (int y = 0; y < CombatArena.Height; y++)
            for (int x = 0; x < CombatArena.Width; x++)
                map.Arena[x, y] = 23;
        var cleric = Creature("Cleric", 1);
        var zombie = Creature("Zombie", 0);
        zombie[FightRules.Status] = 1;
        map.SetSlotCreature(1, cleric);
        map.SetSlotPosition(1, 10, 10);
        map.SetSlotSize(1, 1);
        map.SetSlotCreature(2, zombie);
        map.SetSlotPosition(2, 11, 10);
        map.SetSlotSize(2, 1);
        map.SlotCount = 3;
        map.Rebuild();
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.Add(zombie);
        memory.Party.Members.Add(cleric);
        var fx = new EffectHandlers(_ => 0);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), _ => 0, new EndActionsController(), map, fx);
        fx.Attach(fight);
        fx.LeaveMap = map.Remove;
        fight.Spells = new FightSpells(r.Program.Spells()!, fx, _ => 0);
        r.Chooser.Attach(fight);
        r.View.Speed = 1;
        EffectHandlers.AddEffect(zombie, 0x20, 0, 0x00, true);

        fx.NonWeaponDamage(zombie, 20, 0, false);

        Assert.Equal([70, 70, 70, 70, 225, 225], r.Pauses.Select(p => p.Ms));
        var downed = r.Pauses[^1];
        Assert.Equal((11, 10), downed.Marker);            // CreatureDowned's picture where it stood
        Assert.Equal("ZOMBIE", Rig.Row(downed.Rows, 10));
        Assert.Equal(Rig.Flat(r.T(CombatMsg.StatusCollapses)), Rig.Join(downed.Rows, 11, 21));
        Assert.False(map.OnMap(zombie));
        Assert.True(r.MessageAreaClear);
        r.Chooser.Detach(fight);
    }
}
