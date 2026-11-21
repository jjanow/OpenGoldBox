using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Sound;

namespace GoldBox.Client.Tests;

/// <summary>
/// The handler messages outside a fight (s30, docs/re/spells-effects.md "Status changes"): START 0x16EC's out-of-fight
/// branch (rows 17–22, or 18–22 while the rest loop runs, DS:6DDA), 0xECB3's end outside a fight (the body cue and two
/// pauses, rows 18–22 cleared), the revive 0x2C753's text by the hostile byte, START 0x1693 on row 24. Our wordings.
/// </summary>
public class OutsideEffectMessagesTests
{
    private sealed class Rig
    {
        public readonly TextWindow Window = new();
        public readonly List<string> Log = [];
        public readonly CombatText Texts = new();
        public bool Resting;
        public readonly OutsideEffectMessages Shown;

        public Rig()
        {
            Shown = new OutsideEffectMessages(Window, q =>
            {
                if (q is DelayRequest d)
                    Log.Add(d.BottomLine is { } b ? $"row24 {b.Text} {(int)d.Duration.TotalMilliseconds}" : $"pause {(int)d.Duration.TotalMilliseconds} [{Row(18)}|{Row(19)}|{Row(20)}]");
                return null;
            }, () => 2, Texts, () => Resting, cue => Log.Add("cue " + cue));
        }

        public string Row(int r) =>
            new string(Enumerable.Range(TextWindow.Left, TextWindow.Right - TextWindow.Left + 1).Select(c => Window.Cell(r, c).Char is var ch && ch == (char)0 ? ' ' : Window.Cell(r, c).Char).ToArray()).Trim();
    }

    private static EclCharacter Creature(byte hostile = 0, bool inPlay = true) => new()
    {
        Name = "Tess",
        [FightRules.Hostile] = hostile,
        [FightRules.InPlay] = (byte)(inPlay ? 1 : 0),
    };

    [Fact]
    public void A_status_change_outside_a_fight_shows_its_text_then_the_body_cue_two_pauses_and_the_clear()
    {
        var r = new Rig();
        var c = Creature();
        r.Window.Put(17, 1, "OLD LINE");
        Assert.True(r.Shown.Show(c, EffectEvent.PoisonDeath));
        Assert.Equal("", r.Row(17)); // the box from row 17 cleared (DS:6DDA = 0)
        Assert.Equal("TESS", r.Row(18));
        Assert.Equal(r.Texts[CombatMsg.StatusPoisonDeath].Trim(), r.Row(19));
        Assert.Empty(r.Log); // no pause flag (0xECDD)

        c[FightRules.InPlay] = 0;
        Assert.True(r.Shown.Show(c, EffectEvent.StatusDone));
        string shown = $"[TESS|{r.Texts[CombatMsg.StatusPoisonDeath].Trim()}|]";
        Assert.Equal(["cue " + SoundCue.Body, "pause 450 " + shown, "pause 450 " + shown], r.Log);
        Assert.Equal("", r.Row(18));
        Assert.Equal("", r.Row(19));
    }

    [Fact]
    public void While_resting_the_message_goes_one_row_lower_and_leaves_the_rest_time_line()
    {
        var r = new Rig { Resting = true };
        r.Window.Put(17, 1, "REST TIME");
        Assert.True(r.Shown.Show(Creature(), EffectEvent.Rots));
        // Name on row 19, text on row 20, the pause, then (1, 18)–(38, 22) cleared; row 17 kept.
        Assert.Equal([$"pause 450 [|TESS|{r.Texts[CombatMsg.Rots].Trim()}]"], r.Log);
        Assert.Equal("REST TIME", r.Row(17));
        Assert.Equal("", r.Row(19));
    }

    [Fact]
    public void A_revive_names_the_side_by_the_hostile_byte_and_row_24_messages_use_the_bottom_line()
    {
        var r = new Rig();
        r.Shown.Show(Creature(hostile: 1), EffectEvent.Revived);
        r.Shown.Show(Creature(hostile: 0), EffectEvent.Revived);
        r.Shown.Show(Creature(), EffectEvent.AirClears);
        Assert.Equal([
            $"pause 450 [TESS|{r.Texts[CombatMsg.StatusRevivedEnemy].Trim()}|]",
            $"pause 450 [TESS|{r.Texts[CombatMsg.StatusRevived].Trim()}|]",
            $"row24 {r.Texts[CombatMsg.HandlerAirClears]} 450",
        ], r.Log);
    }

    [Fact]
    public void Healed_shows_START_0x2EEA_words_by_full_hit_points_and_draws_the_list()
    {
        // START 0x2EEA (00BA:234A): "fully" when HP 0x11B = 0x32, else "partially"; START 0x16EC with the pause, then
        // the party list outside a fight (00BA:23AE). Handlers 15 / 44 draw the list after their damage (0xF299 / 0xFE19).
        int drawn = 0;
        var log = new List<string>();
        var w = new TextWindow();
        var texts = new CombatText();
        var shown = new OutsideEffectMessages(w, q => { if (q is DelayRequest) log.Add(w.Lines[2].Trim()); return null; },
            () => 2, texts, () => false, listDrawn: () => drawn++);
        var c = Creature();
        c[0x32] = 10;
        c[0x11B] = 10;
        Assert.True(shown.Show(c, EffectEvent.Healed));
        c[0x11B] = 9;
        Assert.True(shown.Show(c, EffectEvent.Healed));
        Assert.Equal([texts[CombatMsg.FullyHealed].Trim(), texts[CombatMsg.PartiallyHealed].Trim()], log);
        Assert.Equal(2, drawn);
        Assert.True(shown.Show(c, EffectEvent.HandlerDamageListed));
        Assert.Equal(3, drawn);
        Assert.Equal(2, log.Count);                                   // no text of its own
    }

    [Fact]
    public void A_revive_holds_the_name_and_statusdone_draws_the_list_again()
    {
        // D s38: 0x2C753 outside a fight draws no party list, so the revived name stays light red; 0xECB3's end calls
        // START 0x0767.
        var held = new List<string>();
        int drawn = 0;
        var w = new TextWindow();
        var shown = new OutsideEffectMessages(w, _ => null, () => 0, new CombatText(), () => true,
            revived: c => held.Add(c.Name), listDrawn: () => drawn++);
        var c = Creature();
        Assert.True(shown.Show(c, EffectEvent.Revived));
        Assert.Equal(["Tess"], held);
        Assert.Equal(0, drawn);
        Assert.True(shown.Show(c, EffectEvent.StatusDone));
        Assert.Equal(1, drawn);
    }

    [Fact]
    public void A_held_name_is_drawn_light_red()
    {
        var font = new GoldBox.Core.Explore.TextFont(Enumerable.Repeat((byte)0xFF, 177 * 8).ToArray());
        var party = new EclParty();
        party.Members.Add(Creature());
        var c = new EclCharacter { Name = "Ada", [FightRules.InPlay] = 1 };
        party.Members.Add(c);
        party.CurrentIndex = 0;
        byte Colour(Func<EclCharacter, bool>? held)
        {
            var f = new GoldBox.Core.Explore.IndexedFrame(320, 200);
            GoldBox.Client.Screens.Chrome.PartyList(f, font, party, 17, null, null, held);
            return f[17 * 8, 5 * 8];
        }
        Assert.Equal(GoldBox.Client.Screens.Chrome.Cyan, Colour(null));
        Assert.Equal(GoldBox.Client.Screens.Chrome.LightRed, Colour(m => ReferenceEquals(m, c)));
    }
}
