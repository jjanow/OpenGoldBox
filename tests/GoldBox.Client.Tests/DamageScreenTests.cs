using GoldBox.Client.Play;
using GoldBox.Core.Ecl;

namespace GoldBox.Client.Tests;

/// <summary>
/// ECL DAMAGE's lines and its end (GAME.OVR 0x959F, 0x375A–0x37E4), and the after-combat "party destroyed" window
/// (0x6B37), as seen in DOSBox-X in s22.
/// </summary>
public class DamageScreenTests
{
    private static (ClientPlayer Player, ScriptRunner Runner) Rig()
    {
        var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => throw new InvalidOperationException("no memory in this test")) { TypesText = false };
        player.InstallPaging();
        return (player, runner);
    }

    private static EclCharacter Member(string name) => new() { Name = name };

    [Fact]
    public void Every_damage_ends_with_the_white_press_enter_line()
    {
        var (player, runner) = Rig();
        using (runner)
        {
            runner.Start(() =>
            {
                var anna = Member("ANNA");
                player.DamageReport(new EclDamageReport(anna, 1, false));
                player.DamageApplied(anna); // the hit, then the blank and the party list (0x96BF–0x96DD)
                player.DamageEnd(false);
            });
            var key = Assert.IsType<KeyRequest>(runner.Pending);
            Assert.Equal((byte)15, key.Colour);
            Assert.Null(player.Window);
            // The line: a new row (the column was set to 38), white, the position line blanked.
            Assert.Equal(18, player.Text.Row);
            Assert.Equal('A', player.Text.Cell(18, 1).Char); // the wrap skips the two leading spaces
            Assert.Equal((byte)15, player.Text.Cell(18, 1).Colour);
            Assert.True(player.PositionLineErased);
            runner.Answer(null);
            Assert.Null(runner.Pending);
        }
    }

    [Fact]
    public void A_wipe_shows_the_killed_window_pauses_three_seconds_then_waits()
    {
        var (player, runner) = Rig();
        using (runner)
        {
            runner.Start(() => player.DamageEnd(true));
            var pause = Assert.IsType<DelayRequest>(runner.Pending);
            Assert.Equal(TimeSpan.FromSeconds(3), pause.Duration);
            var w = Assert.IsAssignableFrom<IReadOnlyList<WindowLine>>(player.Window);
            // The title is erased by the message's clear: the message alone at column 1, row 1, colour 10.
            var line = Assert.Single(w);
            Assert.Equal((1, 1, (byte)10), (line.Column, line.Row, line.Colour));
            runner.Answer(null);
            Assert.Equal((byte)15, Assert.IsType<KeyRequest>(runner.Pending).Colour);
            runner.Answer(null);
            Assert.Null(player.Window);
        }
    }

    [Fact]
    public void The_damage_page_prompt_stays_on_the_bottom_line_during_the_pause()
    {
        var (player, runner) = Rig();
        using (runner)
        {
            runner.Start(() =>
            {
                for (int i = 0; i < 6; i++)
                    player.DamageReport(new EclDamageReport(Member("MEMBER " + i), 200, true));
                player.DamageEnd(true);
            });
            // Five lines fill rows 18–22; the sixth needs a new page: the text routine's page prompt.
            Assert.IsType<KeyRequest>(runner.Pending);
            Assert.Null(((KeyRequest)runner.Pending!).Colour);
            runner.Answer(null);
            var pause = Assert.IsType<DelayRequest>(runner.Pending);
            Assert.Equal((byte)13, pause.BottomLine!.Value.Colour);
            runner.Answer(null);
            runner.Answer(null);
            Assert.Null(runner.Pending);
        }
    }

    [Fact]
    public void Party_destroyed_after_a_fight_is_a_window_with_title_message_and_magenta_key_line()
    {
        var (player, runner) = Rig();
        using (runner)
        {
            runner.Start(() => player.PartyDestroyed(null!));
            var key = Assert.IsType<KeyRequest>(runner.Pending);
            Assert.Null(key.Colour);      // colour 13
            Assert.False(key.AsMenu);
            var w = player.Window!;
            Assert.Equal((byte)15, w[0].Colour);
            Assert.Equal(1, w[0].Row);
            Assert.All(w.Skip(1), l => Assert.True(l.Column == 2 && l.Row >= 5 && l.Column + l.Text.Length - 1 <= 37));
            runner.Answer(null);
            Assert.Null(player.Window);
        }
    }

    [Fact]
    public void The_party_list_is_drawn_after_the_hit_not_before()
    {
        // 0x959F: the line (0x96B0), the hit (START 0x2DC8), the blank and the party list (0x96DD). DOSBox-X s60: at
        // the sixth line's page prompt the fifth member already shows 0 HP in red; the Client took the list one line late.
        var party = new EclParty();
        var anna = new EclCharacter { Name = "ANNA", [EclPartyRules.Hp] = 9, [EclPartyRules.InPlay] = 1 };
        party.Members.Add(anna);
        party.CurrentIndex = 0;
        var memory = new EclMemory { Party = party };
        var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => memory) { TypesText = false };
        player.InstallPaging();
        using (runner)
        {
            var rules = new EclPartyRules(memory, _ => 0);
            rules.Damaged += player.DamageReport;
            rules.DamageApplied += player.DamageApplied;
            player.ReleaseRosterNames();
            runner.Start(() => rules.Apply(anna, 4));
            Assert.Null(runner.Pending);
            Assert.Equal(5, player.RosterAsDrawn(anna)[EclPartyRules.Hp]);
            Assert.True(player.PositionLineErased);
        }
    }
}
