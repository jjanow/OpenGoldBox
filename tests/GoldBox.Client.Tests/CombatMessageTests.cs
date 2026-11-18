using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The combat panel's messages laid out as the program writes them (START 0x16EC / 0x77F8 word wrap in columns 23–38;
/// the attack message 0x12AF4, non-weapon damage 0x2C22E, read s19), with the program's own words from the player's
/// GAME.OVR. Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class CombatMessageTests
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

    /// <summary>A chooser that records the panel's rows (1–21) and the row-24 prompt at every pause.</summary>
    private sealed class Rig
    {
        public readonly CombatView View = new() { Speed = 1 };
        public readonly List<(TimeSpan Wait, IReadOnlyList<string> Rows, byte[] Colours)> Pauses = [];
        public readonly CombatChooser Chooser;
        public readonly Fight Fight;

        public Rig(string dir)
        {
            Chooser = new CombatChooser(q =>
            {
                if (q is DelayRequest d)
                    Pauses.Add((d.Duration, View.Panel.Lines,
                        Enumerable.Range(1, 21).Select(row => View.Panel.Cell(row, 23).Colour).ToArray()));
                return null;
            }, View) { Text = new CombatText(new PorGameDirectory(dir).Program()) };
            Fight = new Fight(new EclMemory(), new EncounterState(),
                new BaseItemTable(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]), _ => 0, new Idle(), new GridGeometry());
            View.Fight = Fight;
        }

        /// <summary>A text of the program (from the player's GAME.OVR), trimmed.</summary>
        public string T(CombatMsg m) => Chooser.Text[m].Trim();

        /// <summary>Rows joined with single spaces (a wrapped text read back as one line).</summary>
        public static string Join(IReadOnlyList<string> rows, int from, int to) =>
            string.Join(" ", Enumerable.Range(from, to - from + 1).Select(k => Row(rows, k)).Where(x => x.Length > 0));

        /// <summary>A text with its runs of spaces made single, as the wrap leaves it when joined.</summary>
        public static string Flat(string t) => string.Join(" ", t.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Panel row r (1-based screen row).</summary>
        public static string Row(IReadOnlyList<string> rows, int r) => rows[r - 1].Trim();
    }

    private static EclCharacter Creature(string name, byte hostile, bool inPlay = true) =>
        new() { Name = name, [FightRules.Hostile] = hostile, [FightRules.InPlay] = (byte)(inPlay ? 1 : 0) };

    [Fact]
    public void A_hit_that_drops_the_defender_shows_both_messages_with_a_pause_each()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var thrender = Creature("Thrender", 0);
        var goblin = Creature("Goblin", 1, inPlay: false);
        goblin[FightRules.Status] = 5;
        r.Chooser.AttackMessage(new AttackMessage(thrender, goblin, 0, 5, true, DefenderWasInPlay: true, SpellLost: false));

        Assert.Equal(2, r.Pauses.Count);
        Assert.All(r.Pauses, p => Assert.Equal(TimeSpan.FromMilliseconds(225), p.Wait)); // PauseOnly at speed 1
        var first = r.Pauses[0].Rows;
        Assert.Equal("THRENDER", Rig.Row(first, 10));
        Assert.Equal(r.T(CombatMsg.Attacks), Rig.Row(first, 11));
        Assert.Equal("GOBLIN", Rig.Row(first, 12));
        // The hit words wrapped at 16 columns (v1.3's words: two rows); the last word ends on column 38, so the cursor
        // goes to the next row and the down message starts one row further (the blank row DOSBox-X showed, s18).
        Assert.Equal((r.T(CombatMsg.HittingFor) + " 5"), Rig.Row(first, 13));
        Assert.Equal(Rig.Flat(r.T(CombatMsg.Points) + " " + r.T(CombatMsg.OfDamage)), Rig.Row(first, 14));
        var second = r.Pauses[1].Rows;
        Assert.Equal("", Rig.Row(second, 15));
        Assert.Equal("GOBLIN", Rig.Row(second, 16));
        Assert.Equal(r.T(CombatMsg.GoesDown), Rig.Row(second, 17));
        Assert.Equal(r.T(CombatMsg.AndIsDying), Rig.Row(second, 18));
        // Colours (START 0x23B6): the attacker cyan, the defender yellow as it was before the damage, light red once down.
        Assert.Equal((11, 14), (r.Pauses[0].Colours[10 - 1], r.Pauses[0].Colours[12 - 1]));
        Assert.Equal((12, 10, 10), (r.Pauses[1].Colours[16 - 1], r.Pauses[1].Colours[17 - 1], r.Pauses[1].Colours[18 - 1]));
        // Then the message area is cleared (START 0x2383).
        Assert.All(r.View.Panel.Lines.Skip(9), l => Assert.Equal("", l));
    }

    [Fact]
    public void A_killed_defender_gets_the_killed_message_two_rows_below_the_down_words()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var goblin = Creature("Goblin", 1);
        var darkstar = Creature("Darkstar", 0, inPlay: false);
        darkstar[FightRules.Status] = 6;
        r.Chooser.AttackMessage(new AttackMessage(goblin, darkstar, 1, 1, true, DefenderWasInPlay: true, SpellLost: false));
        var first = r.Pauses[0].Rows;
        Assert.Equal(r.T(CombatMsg.FromBehind), Rig.Row(first, 13));
        Assert.Equal(r.T(CombatMsg.HittingFor) + " 1", Rig.Row(first, 14));
        Assert.Equal(Rig.Flat(r.T(CombatMsg.Point) + " " + r.T(CombatMsg.OfDamage)), Rig.Row(first, 15)); // no extra row
        var second = r.Pauses[1].Rows;
        Assert.Equal("DARKSTAR", Rig.Row(second, 16));
        Assert.Equal(r.T(CombatMsg.GoesDown), Rig.Row(second, 17));
        Assert.Equal("DARKSTAR", Rig.Row(second, 18));
        Assert.Equal(r.T(CombatMsg.IsKilled), Rig.Row(second, 19));
    }

    [Fact]
    public void A_miss_and_a_slain_helpless_defender_use_their_own_words()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var a = Creature("Thrender", 0);
        var d = Creature("Goblin", 1);
        r.Chooser.AttackMessage(new AttackMessage(a, d, 0, 0, false, true, false));
        Assert.Single(r.Pauses);
        Assert.Equal(r.T(CombatMsg.AndMisses), Rig.Row(r.Pauses[0].Rows, 13));
        var dead = Creature("Goblin", 1, inPlay: false);
        dead[FightRules.Status] = 6;
        r.Chooser.AttackMessage(new AttackMessage(a, dead, 3, 1, true, true, false));
        Assert.Equal(r.T(CombatMsg.SlaysHelpless), Rig.Row(r.Pauses[1].Rows, 11));
        Assert.Equal(r.T(CombatMsg.CruelBlow), Rig.Join(r.Pauses[1].Rows, 13, 14));
        Assert.NotEqual("", Rig.Row(r.Pauses[1].Rows, 14)); // v1.3's words take two rows
    }

    [Fact]
    public void Spell_damage_names_its_element_and_the_down_words_follow_at_the_cursor()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var goblin = Creature("Goblin", 1);
        var s = r.Fight.State;

        // Magic Missile (mask 8): the damage words and the magic words; then the hit picture's 4 × 70 ms and the pause.
        s.PendingDamage = 12;
        s.DamageFlags = 8;
        r.Chooser.EffectMessage(goblin, EffectEvent.NonWeaponDamage);
        Assert.Equal([70, 70, 70, 70, 225], r.Pauses.Select(p => (int)p.Wait.TotalMilliseconds));
        var rows = r.Pauses[0].Rows;
        Assert.Equal("GOBLIN", Rig.Row(rows, 10));
        var taken = Rig.Flat(r.T(CombatMsg.Takes) + " 12 " + r.T(CombatMsg.PointsOfDamage) + " " + r.T(CombatMsg.FromMagic));
        Assert.Equal(taken, Rig.Join(rows, 11, 13));
        Assert.NotEqual("", Rig.Row(rows, 13));          // v1.3's words take three rows

        // It went down dying: the dying words join the down words, START 0x16EC at the cursor row + 1.
        goblin[FightRules.InPlay] = 0;
        goblin[FightRules.Status] = 5;
        r.Chooser.EffectMessage(goblin, EffectEvent.GoesDown);
        rows = r.Pauses[^1].Rows;
        Assert.Equal(taken, Rig.Join(rows, 11, 13));     // the damage text is still up
        Assert.Equal("GOBLIN", Rig.Row(rows, 14));
        Assert.Equal(Rig.Flat(r.T(CombatMsg.SpellGoesDown) + r.Chooser.Text[CombatMsg.SpellAndIsDying]), Rig.Join(rows, 15, 16));
        Assert.NotEqual("", Rig.Row(rows, 16));

        // One point of electricity + magic (0x0C): its own one-point string and no magic words.
        r.Pauses.Clear();
        s.PendingDamage = 1;
        s.DamageFlags = 0x0C;
        r.Chooser.EffectMessage(Creature("Orc", 1), EffectEvent.NonWeaponDamage);
        Assert.Equal(Rig.Flat(r.T(CombatMsg.TakesOnePoint) + " " + r.T(CombatMsg.FromElectricity)),
            Rig.Join(r.Pauses[0].Rows, 11, 13));
    }

    [Fact]
    public void An_ai_turn_shows_the_creature_and_waits_one_frame()
    {
        if (Dir() is not { } dir)
            return;
        var r = new Rig(dir);
        var orc = Creature("Orc", 1);
        orc[CombatRules.AutoCombat] = 1;
        r.Chooser.TurnStarted(orc);
        Assert.Same(orc, r.View.Shown);
        Assert.Single(r.Pauses);                          // no wait of the program's own: one frame
        Assert.Equal(TimeSpan.Zero, r.Pauses[0].Wait);
    }

    [Theory]
    [InlineData(FightNotice.GodsIntervene)]
    [InlineData(FightNotice.DoesNotWork)]
    [InlineData(FightNotice.MagicOn)]
    public void Alt_key_messages_go_to_row_24_with_the_game_speed_pause(FightNotice notice)
    {
        // START 0x1693 (0x2233), called by Alt-X (0x1637A in 0x1638E), its refusal (0x9DC0) and Alt-M (0x9CD1): row 24
        // in green, Delay(speed × 225), row 24 cleared; the panel keeps the current creature (DOSBox-X s24: about
        // 0.9 s at the save's speed 4, then the map drawn again without the cursor box after Alt-X).
        var seen = new List<(TimeSpan Wait, string? Line, string Panel)>();
        var view = new CombatView { Speed = 4, Cursor = (3, 3), MovePrompt = "MOVE" };
        var chooser = new CombatChooser(q =>
        {
            if (q is DelayRequest d)
                seen.Add((d.Duration, view.PageMessage, string.Concat(view.Panel.Lines)));
            return null;
        }, view);
        chooser.Notice(notice, null);
        var (wait, line, panel) = Assert.Single(seen);
        Assert.Equal(TimeSpan.FromMilliseconds(900), wait);
        Assert.Equal(chooser.Text[notice switch
        {
            FightNotice.GodsIntervene => CombatMsg.GodsIntervene,
            FightNotice.DoesNotWork => CombatMsg.DoesNotWork,
            _ => CombatMsg.MagicOn,
        }], line);
        Assert.Equal("", panel.Trim());                   // nothing in the panel
        Assert.Null(view.PageMessage);                    // row 24 cleared after the pause
        Assert.Null(view.MovePrompt);
        Assert.Equal(notice == FightNotice.GodsIntervene ? null : (3, 3), view.Cursor);
    }
}
