using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// The camp Cast's effect message (0x2C540 → START 0x2B8B outside a fight → START 0x16EC's out-of-fight branch, read
/// s27): the name at (1, 18), the text from (1, 19) in green, the game-speed pause, then rows 18–22 cleared. Our
/// wordings (no game files).
/// </summary>
public class CampEffectMessageTests
{
    /// <summary>Scripted answers; the text window's rows are kept at every delay.</summary>
    private sealed class Script(TextWindow w, params object?[] answers)
    {
        private readonly Queue<object?> _answers = new(answers);
        public List<(TimeSpan Wait, IReadOnlyList<string> Rows, byte NameColour)> Delays { get; } = [];

        public object? Ask(PlayerRequest r)
        {
            if (r is DelayRequest d)
            {
                Delays.Add((d.Duration, w.Lines, w.Cell(18, 1).Colour));
                return null;
            }
            if (_answers.Count == 0)
                throw new InvalidOperationException($"no answer left for {r}");
            return _answers.Dequeue();
        }

        public int Left => _answers.Count;
    }

    private static CampContext Camp(Script s, TextWindow w, EclCharacter member, byte speed, Action<ListRequest?>? pageUnder = null)
    {
        var p = new EclParty();
        p.Members.Add(member);
        p.CurrentIndex = 0;
        var t = new TradeContext
        {
            Dialog = new Dialog(s.Ask, w),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = n => 0,
            ShowPageUnder = pageUnder ?? (_ => { }),
        };
        var m = new EclMemory { Party = p, GameSpeed = speed };
        m.Write(EclPartyRules.PartySizeCell, 1);
        // Spell 5: cleric 1, target the caster (mode 1), effect 0x0E for 1 + 0 rounds (the routine 0x28101 passes
        // its text to the resolution).
        var table = new SpellTable(new Dictionary<int, SpellInfo>
        {
            [5] = new(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0x0E, 1, 0, 0, 0, 0),
        });
        var spells = new CampSpells(table, _ => false);
        var state = new CampState();
        var fx = new EffectHandlers(_ => 0) { List = () => p.Members, CurrentCharacter = () => p.Current };
        return new CampContext
        {
            Trade = t,
            State = state,
            Spells = spells,
            Rest = new CampRest(m, state, spells, _ => 0, (c, e) => c.Effects.Remove(e), c => fx.RestoreHitPoints(c, 1, false)),
            Memory = m,
            Cast = new CampCast(new FightSpells(table, fx, _ => 0), p),
        };
    }

    private static EclCharacter Member()
    {
        var c = new EclCharacter { Name = "Anna" };
        c[0x10C] = 0;
        c[0x10D] = 1;
        c[0x17] = 5; // spell 5 memorised
        return c;
    }

    [Fact]
    public void The_effect_text_shows_under_the_name_with_the_game_speed_pause()
    {
        var w = new TextWindow();
        var a = Member();
        var s = new Script(w,
            2, 0,                 // MAGIC, CAST
            new ListAnswer(0, 1), // spell 5 (row 0 is the heading)
            5, 5);                // (nothing memorised now: START 0x16EC with the pause, 0x17D82: a delay, no key)
        var c = Camp(s, w, a, speed: 2);
        VenueScreens.Camp(c, null);
        Assert.Equal(0, s.Left);
        // The cast message (DOSBox-X s29): name and the casts word on row 19, the spell on row 20, the game-speed
        // pause and no key; then the effect message.
        Assert.Equal(3, s.Delays.Count);                     // the cast, the effect, "no spells memorised"
        var cast = s.Delays[0];
        Assert.Equal(TimeSpan.FromMilliseconds(450), cast.Wait);
        Assert.StartsWith("ANNA ", cast.Rows[2].Trim());
        Assert.NotEqual("", cast.Rows[3].Trim());
        Assert.Equal("", cast.Rows[1].Trim());
        var d = s.Delays[1];
        Assert.Equal(TimeSpan.FromMilliseconds(450), d.Wait); // PauseOnly: DS:49E2 × 225 ms
        Assert.Equal("", d.Rows[0].Trim());                  // row 17 (index 0) cleared
        Assert.Equal("ANNA", d.Rows[1].Trim());              // the name at (1, 18)
        Assert.Equal(11, d.NameColour);                      // in play, not hostile: cyan
        Assert.Equal(c.Resources.CombatTexts.Effect(EffectTexts.ForSpell(5)!), d.Rows[2].Trim());
        Assert.NotNull(EffectHandlers.FindEffect(a, 0x0E));
    }

    [Fact]
    public void Speed_0_does_not_wait()
    {
        var w = new TextWindow();
        var s = new Script(w, 2, 0, new ListAnswer(0, 1), 5, 5);
        VenueScreens.Camp(Camp(s, w, Member(), speed: 0), null);
        Assert.Equal(0, s.Left);
        Assert.Empty(s.Delays);
    }

    [Fact]
    public void The_spell_list_page_stays_under_the_cast_and_comes_back_afresh()
    {
        // 0x17D15–0x17D52 (P, s30; DOSBox-X s29): the text box's frame over the spell list, the cast's messages in it,
        // then the position word −1, so the next list is drawn anew (first row, top 0).
        var w = new TextWindow();
        ListRequest? under = null;
        var seenUnder = new List<ListRequest?>();
        var lists = new List<ListRequest>();
        var s = new Script(w, 2, 0, new ListAnswer(0, 1), 5, 5);
        var c = Camp(s, w, Member(), speed: 2, pageUnder: p => under = p);
        var t = c.Trade;
        var dialog = new Dialog(r =>
        {
            if (r is DelayRequest)
                seenUnder.Add(under);
            if (r is ListRequest l)
            {
                Assert.Null(under); // a new list is never drawn over a kept page
                lists.Add(l);
            }
            return s.Ask(r);
        }, w);
        var camp = new CampContext
        {
            Trade = new TradeContext
            {
                Dialog = dialog, Text = t.Text, Party = t.Party, Pool = t.Pool, Treasure = t.Treasure, Random = t.Random,
                ShowPageUnder = t.ShowPageUnder,
            },
            State = c.State, Spells = c.Spells, Rest = c.Rest, Memory = c.Memory, Cast = c.Cast,
        };
        VenueScreens.Camp(camp, null);
        Assert.Equal(0, s.Left);
        Assert.Equal(3, seenUnder.Count);
        Assert.All(seenUnder.Take(2), p => Assert.Same(lists[0], p)); // the cast message and the effect message over the page
        Assert.Null(under);
        Assert.NotNull(lists[0].Paging);
        Assert.Equal(SpellLists.FirstRow, lists[0].FirstRow);
    }

    [Fact]
    public void Drops_messages_are_the_panel_layout_and_its_pause_is_the_game_speed_delay()
    {
        // START 0x16EC from Drop (0x19195 pause 0, 0x19258 pause 1; D s39): the name on row 18, the text from row 19,
        // the relief message cleared after DS:49E2 × 225 ms with no key.
        var w = new TextWindow();
        var a = Member();
        var s = new Script(w,
            4, 1,                 // ALTER, DROP
            1,                    // drop from party? NO
            5, 5);                // EXIT, EXIT
        var c = Camp(s, w, a, speed: 4);
        var b = Member();
        b.Name = "Ben";
        c.Party.Members.Add(b);
        VenueScreens.Camp(c, null);
        Assert.Equal(0, s.Left);
        var d = Assert.Single(s.Delays);
        Assert.Equal(TimeSpan.FromMilliseconds(900), d.Wait);
        Assert.Equal("", d.Rows[0].Trim());
        Assert.Equal("ANNA", d.Rows[1].Trim());
        Assert.Equal(11, d.NameColour);
        Assert.NotEqual("", d.Rows[2].Trim());
        Assert.Equal(2, c.Party.Members.Count);
    }

    [Fact]
    public void The_target_picks_escape_clears_the_pick_and_the_menu_goes_on()
    {
        // START 0x37E8 (P s41, D s41): Escape answers 0, which clears the pick (00BA:2DD6) but is not in the exit set
        // 00BA:2C1F, so the menu comes again with nobody lit; End from no pick lights nobody, Home the last member.
        var w = new TextWindow();
        var a = Member();
        var s = new Script(w,
            CampScreens.Escaped,          // Escape (the request's cancel answer)
            ChoiceRequest.NextMember,     // End: still nobody
            ChoiceRequest.PreviousMember, // Home: the last member
            0);                           // Select
        var c = Camp(s, w, a, speed: 2);
        var b = Member();
        b.Name = "Ben";
        c.Party.Members.Add(b);
        Assert.Same(b, CampScreens.PickTarget(c, a));
        Assert.Equal(0, s.Left);

        var s2 = new Script(w, CampScreens.Escaped, 0); // Escape, then Select with nobody picked: no target
        var c2 = Camp(s2, w, a, speed: 2);
        Assert.Null(CampScreens.PickTarget(c2, a));
        Assert.Equal(0, s2.Left);
    }
}
