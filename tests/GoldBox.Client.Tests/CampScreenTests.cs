using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>The camp screens (CampScreens, docs/re/camp.md) driven with scripted answers (no window, no game files).</summary>
public class CampScreenTests
{
    private sealed class Script(params object?[] answers)
    {
        private readonly Queue<object?> _answers = new(answers);
        public List<PlayerRequest> Asked { get; } = [];

        public object? Ask(PlayerRequest r)
        {
            Asked.Add(r);
            if (_answers.Count == 0)
                throw new InvalidOperationException($"no answer left for {r}");
            return _answers.Dequeue();
        }

        public int Left => _answers.Count;
    }

    private static EclCharacter Member(string name)
    {
        var c = new EclCharacter { Name = name };
        c[0x10C] = 0;
        c[0x10D] = 1;
        return c;
    }

    private static (TradeContext T, Script S) Context(Script s, params EclCharacter[] members)
    {
        var p = new EclParty();
        p.Members.AddRange(members);
        p.CurrentIndex = 0;
        return (new TradeContext
        {
            Dialog = new Dialog(s.Ask, new TextWindow()),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = n => 0,
        }, s);
    }

    /// <summary>A camp on a hand-made party: spells 3 (cleric 1, one member), 5 (cleric 1, caster), 15 (magic-user 1,
    /// combat only); every generator draw 0.</summary>
    internal static CampContext Camp(TradeContext t, EclMemory? memory = null, List<char>? saved = null)
    {
        var m = memory ?? new EclMemory();
        m.Party = t.Party;
        m.Write(EclPartyRules.PartySizeCell, (ushort)t.Party.Members.Count);
        var d = new Dictionary<int, SpellInfo>
        {
            [3] = new(0, 1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 0),
            [5] = new(0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0),
            [15] = new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        };
        var table = new SpellTable(d);
        var spells = new CampSpells(table, it => it[0x2E] == 99);
        var state = new CampState();
        var fx = new EffectHandlers(_ => 0) { List = () => t.Party.Members, CurrentCharacter = () => t.Party.Current };
        return new CampContext
        {
            Trade = t,
            State = state,
            Spells = spells,
            Rest = new CampRest(m, state, spells, _ => 0, (c, e) => c.Effects.Remove(e), c => fx.RestoreHitPoints(c, 1, false)),
            Memory = m,
            Cast = new CampCast(new FightSpells(table, fx, _ => 0) { Globals = m.SpellGlobals }, t.Party), // as ClientPlayer
            Save = k =>
            {
                saved?.Add(k);
                return null;
            },
        };
    }

    [Fact]
    public void Menu_lines_take_every_capital_as_a_hotkey()
    {
        var (items, keys) = CampScreens.ParseMenu("Go   fiNd Here Mark   Exit");
        Assert.Equal(["GO", "FIND", "HERE", "MARK", "EXIT"], items);
        Assert.Equal("GNHME", keys);
        (items, keys) = CampScreens.ParseMenu("Lamps on  Bells off  Exit");
        Assert.Equal(["LAMPS ON", "BELLS OFF", "EXIT"], items);
        Assert.Equal("LBE", keys);
    }

    [Fact]
    public void Rest_eight_hours_by_the_rest_menu()
    {
        var answers = new List<object?> { 3, 2 };            // REST, then HOURS
        answers.AddRange(Enumerable.Repeat<object?>(4, 8));  // INC ×8 (Up answers INC as well)
        answers.Add(0);                                       // REST
        answers.AddRange(Enumerable.Repeat<object?>(false, 96)); // the key polls, one a tick: nothing pressed
        answers.Add(5);                                       // EXIT the camp
        var (t, s) = Context(new Script([.. answers]), Member("ANNA"));
        var camp = Camp(t);
        Assert.False(VenueScreens.Camp(camp, null));
        Assert.Equal("08:00", GameClock.Format(camp.Memory));
        var restMenu = s.Asked.OfType<ChoiceRequest>().ElementAt(1);
        Assert.Equal("RYHMIDE", restMenu.Hotkeys);
        Assert.Equal(4, restMenu.UpAnswer);
        Assert.Equal(5, restMenu.DownAnswer);
        Assert.All(s.Asked.OfType<DelayRequest>(), d => Assert.True(d.ReportInput));
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void A_key_during_the_rest_asks_to_stop()
    {
        var (t, s) = Context(new Script(
            3, 2, 4, 0,                 // REST, HOURS, INC, REST
            true, 0,                    // input at the first poll; Stop resting? YES
            5), Member("ANNA"));
        var camp = Camp(t);
        VenueScreens.Camp(camp, null);
        Assert.Equal("00:00", GameClock.Format(camp.Memory));   // the poll comes before the first tick
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Memorise_from_the_book_then_rest_learns_it()
    {
        var c = Member("ANNA");
        c[0xB2] = 1;                    // one cleric level-1 slot
        c[0x32 + 3] = 1;                // knows spell 3
        var answers = new List<object?>
        {
            2, 1,                       // MAGIC, MEMORIZE (nothing pending: no first list)
            new ListAnswer(0, 1),       // the book: row 0 is the level heading, row 1 spell 3
            new ListAnswer(1, 0),       // EXIT the book
            new ListAnswer(0, 0),       // the pending list (display only: EXIT)
            0,                          // Memorize these spells? YES
            4, 0,                       // REST (4 h 15 min = 51 ticks), REST in the rest menu
        };
        answers.AddRange(Enumerable.Repeat<object?>(false, 51));      // a poll before each of the 51 ticks
        answers.Add(null);              // "has memorized" at tick 51
        answers.Add(5);                 // EXIT magic
        answers.Add(5);                 // EXIT camp
        var (t, s) = Context(new Script([.. answers]), c);
        var camp = Camp(t);
        VenueScreens.Camp(camp, null);
        Assert.Equal(3, c[0x2B]);
        Assert.Equal("04:15", GameClock.Format(camp.Memory));
        Assert.Equal(0, s.Left);
        var book = s.Asked.OfType<ListRequest>().First();
        Assert.Contains(0, book.Labels);
    }

    [Fact]
    public void Alter_order_place_moves_the_current_member_and_drop_removes_it()
    {
        var a = Member("ANNA");
        var b = Member("BORN");
        var (t, s) = Context(new Script(
            4, 0,                       // ALTER, ORDER
            0,                          // SELECT → place mode ("has been selected")
            ChoiceRequest.NextMember,   // End: ANNA one place down
            1,                          // EXIT order
            1, 0,                       // DROP, Drop from party? YES
            null,                       // "bids you farewell" key
            5, 5), a, b);               // EXIT alter, EXIT camp
        var camp = Camp(t);
        VenueScreens.Camp(camp, null);
        Assert.Same(b, Assert.Single(t.Party.Members));
        Assert.Equal(1, camp.Memory.Read(EclPartyRules.PartySizeCell));
        Assert.Equal(0, s.Left);
    }

    /// <summary>
    /// The s50 rule (user; shop-format.md "Party selection"): the camp's menus pass every extended key to 0x23819, so a
    /// key the Client leaves free (an F-key, PgUp, Alt-letter, keypad digit) makes the first member current. The camp,
    /// Magic and Alter menus and Order's Select carry the flag; the screen answers <see cref="ChoiceRequest.FirstMember"/>.
    /// </summary>
    [Fact]
    public void Camp_extended_keys_make_the_first_member_current()
    {
        var a = Member("ANNA");
        var b = Member("BORN");
        var (t, s) = Context(new Script(
            4,                          // ALTER
            ChoiceRequest.NextMember,   // End: BORN current
            ChoiceRequest.FirstMember,  // F5 (say): ANNA current
            3,                          // ICON
            5, 5), a, b);               // EXIT alter, EXIT camp
        var edited = new List<EclCharacter>();
        var c = Camp(t);
        c = new CampContext
        {
            Trade = c.Trade, State = c.State, Spells = c.Spells, Rest = c.Rest, Memory = c.Memory,
            EditIcon = edited.Add,
        };
        VenueScreens.Camp(c, null);
        Assert.Same(a, Assert.Single(edited));
        Assert.All(s.Asked.OfType<ChoiceRequest>().Where(r => r.PartySelect), r => Assert.True(r.ExtendedKeysPickFirst));
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Alter_icon_runs_the_icon_editor_on_the_current_member()
    {
        var a = Member("ANNA");
        var b = Member("BORN");
        var (t, s) = Context(new Script(
            4,                          // ALTER
            ChoiceRequest.NextMember,   // End: BORN current
            3,                          // ICON
            5, 5), a, b);               // EXIT alter, EXIT camp
        var edited = new List<EclCharacter>();
        var c = Camp(t);
        c = new CampContext
        {
            Trade = c.Trade, State = c.State, Spells = c.Spells, Rest = c.Rest, Memory = c.Memory,
            EditIcon = edited.Add,
        };
        VenueScreens.Camp(c, null);
        Assert.Same(b, Assert.Single(edited));
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Speed_and_pictures_change_the_option_cells()
    {
        var m = new EclMemory { GameSpeed = 0 };
        var (t, s) = Context(new Script(
            4, 2,                       // ALTER, SPEED (at 0: SLOWER EXIT)
            -10,                        // Up: slower
            0,                          // FASTER (FASTER SLOWER EXIT at 1)
            1,                          // EXIT (SLOWER EXIT at 0)
            4,                          // PICS
            0, 1, 2,                    // MONSTERS, PORTRAITS, EXIT
            5, 5), Member("ANNA"));
        VenueScreens.Camp(Camp(t, m), null);
        Assert.Equal(0, CampSettings.Speed(m));
        Assert.True(CampSettings.MonsterPictures(m));
        Assert.True(CampSettings.Portraits(m));
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Save_picks_a_slot_then_asks_to_quit()
    {
        var saved = new List<char>();
        var (t, s) = Context(new Script(
            0, 1,                       // SAVE, slot B
            1,                          // Quit to DOS? NO
            5), Member("ANNA"));
        VenueScreens.Camp(Camp(t, saved: saved), null);
        Assert.Equal(['B'], saved);
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Cast_cure_light_wounds_on_a_picked_member_uses_the_spell()
    {
        var a = Member("ANNA");
        var b = Member("BORN");
        a[0x17] = 3;                    // CLW memorised
        b[0x32] = 10;                   // HP max 10
        b[0x11B] = 2;                   // HP 2
        var (t, s) = Context(new Script(
            2, 0,                       // MAGIC, CAST
            new ListAnswer(0, 1),       // spell 3 (row 0 is the heading)
            null,                       // "casts" pause
            ChoiceRequest.NextMember,   // START's member pick (0x37E8) from the caster: End to BORN
            0,                          // Select
            null,                       // START 0x2EEA's healed words, the pause
            null,                       // nothing memorised now: "has no spells memorized" pause
            5, 5), a, b);
        VenueScreens.Camp(Camp(t), null);
        Assert.Equal(0, a[0x17]);
        Assert.Equal(3, b[0x11B]);      // 1d8 with every draw 0 → 1
        Assert.Equal(0, s.Left);
        var pick = s.Asked.OfType<ChoiceRequest>().First(r => r.Items.Contains("SELECT"));
        Assert.Equal(["SELECT", "EXIT"], pick.Items);
        Assert.True(pick.PartySelect);
        Assert.Equal(0, t.Party.CurrentIndex);      // the caster stays current (DS:5D92 is not changed)
    }

    [Fact]
    public void A_combat_only_spell_offers_to_lose_it()
    {
        var a = Member("ANNA");
        a[0x17] = 15;
        var (t, s) = Context(new Script(
            2, 0, new ListAnswer(0, 1), // MAGIC, CAST, spell 15
            0,                          // Lose it? YES
            null,                       // nothing memorised now: message key
            5, 5), a);
        VenueScreens.Camp(Camp(t), null);
        Assert.Equal(0, a[0x17]);
        Assert.Equal(0, s.Left);
    }

    /// <summary>The Items page's Use outside a fight (0x222F0 → 0x22AC4 → 0x27C38 with DS:6DBF = 1, s49): a readied
    /// wand of the combat-only spell 15 asks "Use it?"; Yes spends a charge with no effect, No keeps it; an item not
    /// readied only gets the message.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void A_combat_only_item_asks_use_it_and_yes_spends_a_charge(int answer, int chargesLeft)
    {
        var a = Member("ANNA");
        var types = new byte[BaseItemTable.EntrySize * BaseItemTable.Count];
        types[7 * BaseItemTable.EntrySize + BaseItemTable.Slot] = 2;
        var wand = new byte[63];
        (wand[0x2E], wand[0x34], wand[0x3D], wand[0x3C], wand[0x39]) = (7, 1, 15, 2, 1);
        a.Items.Add(wand);
        var s = new Script(null, answer); // the item message's pause, Use it?
        var p = new EclParty();
        p.Members.Add(a);
        p.CurrentIndex = 0;
        var t = new TradeContext
        {
            Dialog = new Dialog(s.Ask, new TextWindow()),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = n => 0,
            ItemTypes = new BaseItemTable(types),
        };
        var camp = Camp(t);
        CampScreens.UseItem(camp, a, wand);
        Assert.Equal(chargesLeft, wand[0x3C]);
        Assert.Equal(0, s.Left);
        var yesNo = s.Asked.OfType<ChoiceRequest>().Single();
        Assert.Contains(t.Text[Msg.UseIt].Trim(), yesNo.ToString() ?? "");

        wand[0x34] = 0; // not readied: the message only
        CampScreens.UseItem(camp, a, wand);
        Assert.Equal(chargesLeft, wand[0x3C]);
        Assert.Equal(0, s.Left);
    }
    /// <summary>
    /// DS:6DB5, the remembered target, is a program global (read s51: written only by the start-up 0x2A42B, the Cast menu
    /// 0x17CE3, the camp's end 0x197E9, 0x27AA9 and the member pick it hands it to). The command line's View builds a
    /// new camp context for each V, so the target lives on the game's spell globals: a wand's second Use, from a new
    /// context on the same game memory, starts its pick on the member the first Use chose; the Cast menu clears it.
    /// </summary>
    [Fact]
    public void The_remembered_target_carries_over_to_the_next_use_and_the_cast_menu_clears_it()
    {
        var a = Member("ANNA");
        var b = Member("BO");
        var types = new byte[BaseItemTable.EntrySize * BaseItemTable.Count];
        types[7 * BaseItemTable.EntrySize + BaseItemTable.Slot] = 2;
        var wand = new byte[63];
        (wand[0x2E], wand[0x34], wand[0x3D], wand[0x3C], wand[0x39]) = (7, 1, 3, 5, 1); // spell 3: one member
        a.Items.Add(wand);
        var memory = new EclMemory();
        var lit = new List<EclCharacter?>();
        bool talking = false; // the Items page's text over the page (CharacterView sets it around Use)

        CampContext Use(params object?[] answers)
        {
            var s = new Script(answers);
            var p = new EclParty();
            p.Members.AddRange([a, b]);
            p.CurrentIndex = 0;
            var t = new TradeContext
            {
                Dialog = new Dialog(s.Ask, new TextWindow()),
                Text = new GameText(),
                Party = p,
                Pool = new uint[7],
                Treasure = [],
                Random = n => 0,
                ItemTypes = new BaseItemTable(types),
                // The pick redraws the venue's screen (0x27AFA; DOSBox-X s51): the page's text no longer talks over it.
                RosterDrawnLit = who => { Assert.False(talking); lit.Add(who); },
                TalkOverItemsPage = on => talking = on,
            };
            var camp = Camp(t, memory);
            talking = true;
            CampScreens.UseItem(camp, a, wand);
            Assert.Equal(0, s.Left);
            return camp;
        }

        Use(null, ChoiceRequest.NextMember, 0, null); // the item message's pause; End → BO; Select; the heal message
        Assert.Equal([a, b], lit);                  // the first pick starts on the caster (0x27ABF)
        Assert.Same(b, memory.SpellGlobals.Ds6DB5);
        Assert.Equal(4, wand[0x3C]);

        lit.Clear();
        Use(null, 0, null);                         // a new context (the next V): Select at once
        Assert.Equal([b], lit);                     // the pick starts on BO
        Assert.Same(b, memory.SpellGlobals.Ds6DB5);
        Assert.Equal(3, wand[0x3C]);

        // The command line's C (Cast 0x17CD2) clears it at once (0x17CE3): nothing memorised, the message and out.
        var (t2, s2) = Context(new Script(new object?[] { null }), a, b);
        CampScreens.CastMenu(Camp(t2, memory));
        Assert.Null(memory.SpellGlobals.Ds6DB5);
        Assert.Equal(0, s2.Left);

        // The camp keeps it while it runs and clears it at its end (0x197E9).
        memory.SpellGlobals.Ds6DB5 = b;
        var (t3, s3) = Context(new Script(5), a, b); // EXIT
        Assert.False(VenueScreens.Camp(Camp(t3, memory), null));
        Assert.Null(memory.SpellGlobals.Ds6DB5);
        Assert.Equal(0, s3.Left);
    }
}
