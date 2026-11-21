using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>The shop, temple, loot and camp screens driven with scripted answers (no window, no game files).</summary>
public class VenueTests
{
    /// <summary>Answers requests in order and keeps them for checks.</summary>
    private sealed class Script(params object?[] answers)
    {
        private readonly Queue<object?> _answers = new(answers);
        public List<PlayerRequest> Asked { get; } = [];
        public List<IReadOnlyList<WindowLine>> Windows { get; } = [];

        public object? Ask(PlayerRequest r)
        {
            Asked.Add(r);
            if (_answers.Count == 0)
                throw new InvalidOperationException($"no answer left for {r}");
            return _answers.Dequeue();
        }

        public int Left => _answers.Count;
    }

    private static EclCharacter Member(string name, ushort pp = 0, ushort gp = 0, byte str = 12)
    {
        var c = new EclCharacter { Name = name };
        c[0x10] = str;
        c.SetWord(Money.Platinum, pp);
        c.SetWord(Money.Gold, gp);
        c[0x10C] = 0;
        c[0x10D] = 1;
        Carrying.RecalculateWeight(c);
        return c;
    }

    private static byte[] Item(ushort value, ushort weight, byte type = 1)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2E] = type;
        r[0x31] = 7;
        r[0x37] = (byte)weight; r[0x38] = (byte)(weight >> 8);
        r[0x3A] = (byte)value; r[0x3B] = (byte)(value >> 8);
        return r;
    }

    private static (TradeContext T, Script S) Context(Script s, EclParty party, List<byte[]>? treasure = null, uint[]? pool = null,
        Action? rosterDrawn = null, Func<EclCharacter, IReadOnlyList<ViewEntry>, int>? viewPage = null) =>
        (new TradeContext
        {
            Dialog = new Dialog(s.Ask, new TextWindow()),
            Text = new GameText(),
            Party = party,
            Pool = pool ?? new uint[7],
            Treasure = treasure ?? [],
            Random = n => n - 1,
            Word = w => w == 7 ? "SWORD" : null,
            ShowWindow = w => { if (w is not null) s.Windows.Add(w); },
            RosterDrawn = rosterDrawn ?? (() => { }),
            ViewPage = viewPage,
        }, s);

    private static EclParty Party(params EclCharacter[] members)
    {
        var p = new EclParty();
        p.Members.AddRange(members);
        p.CurrentIndex = 0;
        return p;
    }

    [Fact]
    public void Buy_pays_after_the_copy_and_keeps_the_weight_of_the_old_coins()
    {
        var c = Member("ANNA", pp: 600);                 // 3000 gold
        var stock = new List<byte[]> { Item(2000, 10) };
        var (t, s) = Context(new Script(
            0,                          // BUY
            new ListAnswer(0, 0),       // buy the first item
            new ListAnswer(1, 0),       // leave the list
            4), Party(c), stock);       // EXIT (no money in the pool: BUY VIEW POOL APPRAISE EXIT)
        VenueScreens.Shop(t);
        Assert.Single(c.Items);
        Assert.Equal(200, c.Word(Money.Platinum));       // 1000 left = 200 pp
        Assert.Equal(610, c.Word(Money.Weight));         // recalculated in the copy, before the payment (DOSBox s11)
        Assert.Single(stock);                            // the stock keeps its item
        Assert.Equal(0, s.Left);
        var list = Assert.IsType<ListRequest>(s.Asked[1]);
        Assert.Equal("SWORD                     2000", list.Rows[0]);
    }

    [Fact]
    public void A_free_stock_item_costs_one_gold()
    {
        var stock = new List<byte[]> { Item(0, 1) };
        ShopRules.PriceStock(stock);
        Assert.Equal(1, stock[0][0x3A] | stock[0][0x3B] << 8);
    }

    [Fact]
    public void Leaving_with_money_in_the_pool_asks_to_go_back()
    {
        var c = Member("ANNA", pp: 600);
        var (t, s) = Context(new Script(
            2,                          // POOL
            6,                          // EXIT (money: BUY VIEW TAKE POOL SHARE APPRAISE EXIT)
            0,                          // go back? YES
            6,                          // EXIT
            1), Party(c));              // NO: leave
        VenueScreens.Shop(t);
        Assert.Equal(600u, t.Pool[Money.PoolPlatinum]);
        Assert.Equal(0, c.Word(Money.Platinum));
        Assert.Equal(0, s.Left);
        Assert.Equal(["BUY", "VIEW", "TAKE", "POOL", "SHARE", "APPRAISE", "EXIT"], Assert.IsType<ChoiceRequest>(s.Asked[1]).Items);
    }

    [Fact]
    public void Share_with_no_member_to_take_a_share_halts_the_program()
    {
        // The loot's pool with money and only an NPC in the party (0x84 = 0x81: not counted, 0x84 = 0 or 0xB3).
        var npc = Member("SKULLCRUSHER");
        npc[0x84] = 0x81;
        ProgramHaltException? seen = null;
        var s = new Script();
        var t = new TradeContext
        {
            Dialog = new Dialog(s.Ask, new TextWindow()),
            Text = new GameText(),
            Party = Party(npc),
            Pool = [0, 0, 0, 40, 0, 0, 0],
            Treasure = [],
            Random = n => n - 1,
            Halt = e => seen = e,
        };
        // Run-time error 200 (pool div 0): the original quits to DOS, so the menu never resumes.
        var e = Assert.Throws<ProgramHaltException>(() => MoneyScreens.Share(t));
        Assert.Same(e, seen);
        Assert.Equal(200, e.Code);
        Assert.Equal(40u, t.Pool[3]);                    // nothing has changed by then
        Assert.Equal(0, npc.Word(Money.Gold));
    }

    [Fact]
    public void The_client_shows_a_halt_then_quits()
    {
        using var runner = new ScriptRunner();
        var memory = new EclMemory();
        bool quit = false;
        var player = new ClientPlayer(runner, () => memory) { Quit = () => quit = true };
        var halt = new ProgramHaltException(200, "test");
        runner.Start(() => player.Halt(halt));
        Assert.IsType<KeyRequest>(runner.Pending);       // the message waits for a key
        Assert.Same(halt, player.Halted);
        Assert.False(quit);
        runner.Answer(null);
        Assert.True(quit);
        Assert.False(runner.Busy);
        Assert.Null(runner.Fault);
    }

    [Fact]
    public void The_shop_starts_with_an_empty_pool_and_switches_members()
    {
        var a = Member("ANNA");
        var b = Member("BORIS");
        var pool = new uint[] { 0, 0, 0, 0, 99, 0, 0 };
        var (t, s) = Context(new Script(ChoiceRequest.NextMember, ChoiceRequest.NextMember, TradeContext.MemberPick - 1, 4),
            Party(a, b), pool: pool);
        VenueScreens.Shop(t);
        Assert.Equal(new uint[7], pool);                 // 0x7212 zeroes the pool on entry
        Assert.Same(b, t.Current);                       // next, next (wraps to the head), then a click on BORIS
        var menu = Assert.IsType<ChoiceRequest>(s.Asked[0]);
        Assert.True(menu.PartySelect);
        Assert.Equal("GO", menu.MemberLetters);
    }

    [Fact]
    public void Temple_heals_after_payment()
    {
        var c = Member("ANNA", gp: 3, pp: 20);           // 103 gold
        c[0x32] = 10;
        c[0x11B] = 1;
        var (t, s) = Context(new Script(
            0,              // HEAL
            2,              // cure light wounds
            0,              // pay: YES
            null,           // "is cured" key
            9,              // the 10th entry leaves
            4), Party(c));  // EXIT
        VenueScreens.Temple(t);
        Assert.Equal(9, c[0x11B]);                       // 1 + 1d8 (8 with Random(8) = 7)
        Assert.Equal(0, c.Word(Money.Platinum));         // 3 gold left: 0 pp, 3 gp
        Assert.Equal(3, c.Word(Money.Gold));
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Shop_and_temple_draw_the_party_list_on_entry_and_after_every_pass()
    {
        // P s39: 0x722B / 0x5298 on entry, 0x745A / 0x5497 at the end of every pass of the menu loop (before the exit
        // test): a held light red name shows its colour again there.
        int drawn = 0;
        var (t, s) = Context(new Script(-2, 4), Party(Member("ANNA"), Member("BEN")), rosterDrawn: () => drawn++); // Home, EXIT
        VenueScreens.Shop(t);
        Assert.Equal(3, drawn);                                        // entry, the pick's pass, the exit pass
        drawn = 0;
        (t, s) = Context(new Script(4), Party(Member("ANNA")), rosterDrawn: () => drawn++); // EXIT at once
        VenueScreens.Temple(t);
        Assert.Equal(2, drawn);                                        // entry, then the exit pass
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Temple_asks_before_a_cure_that_is_not_needed()
    {
        var c = Member("ANNA", pp: 1000);
        var (t, s) = Context(new Script(0, 0, 1, 9, 4), Party(c)); // HEAL, cure blindness, anyway? NO, leave, EXIT
        VenueScreens.Temple(t);
        Assert.Equal(1000, c.Word(Money.Platinum));
        Assert.Equal(0, s.Left);
        Assert.Contains(s.Asked, r => r is ChoiceRequest { Items: ["YES", "NO"] });
    }

    [Fact]
    public void Loot_shows_the_summary_and_takes_an_item()
    {
        var c = Member("ANNA", str: 18);
        var loot = new List<byte[]> { Item(5, 20) };
        var outcome = new AfterCombatOutcome(false, false, true, true, 90, 9);
        var (t, s) = Context(new Script(
            null,                       // summary key
            1,                          // TAKE (VIEW TAKE POOL EXIT)
            new ListAnswer(0, 0),       // take the item
            2), Party(c), loot);        // EXIT (VIEW POOL EXIT)
        VenueScreens.Loot(t, outcome, duel: false, []);
        Assert.Empty(loot);
        Assert.Single(c.Items);
        Assert.Equal(20, c.Word(Money.Weight));
        Assert.Equal(["VIEW", "TAKE", "POOL", "EXIT"], Assert.IsType<ChoiceRequest>(s.Asked[1]).Items);
        Assert.Contains("90", Assert.Single(s.Windows)[1].Text);
    }

    [Fact]
    public void Npc_share_is_a_full_screen_window_with_a_white_press_enter_menu()
    {
        // 0x695E (DOSBox-X s22, a gnoll that joined by ADDNPC): column 5, rows 5, 7, …, wrapped at column 34; then the
        // press-enter text through the menu routine (white, cell back to the first), before the summary's window.
        var (t, s) = Context(new Script(null, null, 2), Party(Member("ANNA")));
        var outcome = new AfterCombatOutcome(false, false, false, false, 0, 0);
        VenueScreens.Loot(t, outcome, duel: false, [Member("GNOLL"), Member("A VERY LONG NAMED HIRELING")]);
        var share = s.Windows[0];
        Assert.Equal(5, share[0].Column);
        Assert.Equal(5, share[0].Row);
        Assert.Equal(7, share.First(l => l.Text.StartsWith("A VERY", StringComparison.Ordinal)).Row);
        Assert.All(share, l => Assert.True(l.Column + l.Text.Length - 1 <= 34));
        var key = Assert.IsType<KeyRequest>(s.Asked[0]);
        Assert.True(key.AsMenu);
        Assert.Equal(2, s.Windows.Count); // the share window, then the summary's
    }

    [Theory]
    [InlineData(true, false, false, Msg.PartyWon)]
    [InlineData(false, false, false, Msg.FoundTreasure)]   // COMBAT with no monsters: treasure only
    [InlineData(false, true, false, Msg.PartyFled)]
    [InlineData(false, false, true, Msg.LostDuel)]
    public void Summary_line_follows_the_programs_flags(bool hostile, bool fled, bool duel, Msg expected)
    {
        var (t, s) = Context(new Script((object?)null), Party(Member("ANNA")));
        VenueScreens.Summary(t, new AfterCombatOutcome(false, fled, false, hostile, 0, 0), duel);
        // 0x5E94: a full-screen window, the result at column 1 row 3, the experience lines at rows 5 and 7.
        var w = Assert.Single(s.Windows);
        Assert.Equal(new WindowLine(1, 3, t.Text[expected]), w[0]);
        Assert.Equal([5, 7], w.Skip(1).Select(l => l.Row));
        var key = Assert.IsType<KeyRequest>(s.Asked[0]);
        Assert.True(key.AsMenu);
        Assert.Equal(0, key.MenuEntries); // a lower-case text has no entries: the cell goes back to the first
    }

    [Theory]
    [InlineData(1, true)]       // KEEP
    [InlineData(0, false)]      // SELL
    public void Appraise_keeps_or_sells_a_gem(int answer, bool kept)
    {
        var c = Member("ANNA", str: 18);
        c.SetWord(Money.Gems, 1);
        Carrying.RecalculateWeight(c);
        var (t, s) = Context(new Script(0, answer, null), Party(c));   // GEMS, keep/sell, then the "no gems" key
        MoneyScreens.Appraise(t);                                       // Random(n) = n - 1: 1d100 = 100, 5000
        Assert.Equal(0, c.Word(Money.Gems));
        if (kept)
        {
            var gem = Assert.Single(c.Items);
            Assert.Equal(5000, gem[0x3A] | gem[0x3B] << 8);
            Assert.Equal(70, gem[0x2E]);
        }
        else
        {
            Assert.Empty(c.Items);
            Assert.Equal(1000, c.Word(Money.Platinum));                 // 5000 div 5
        }
        Assert.Equal(0, s.Left);
        Assert.Equal(0, Assert.IsType<ChoiceRequest>(s.Asked[1]).CancelIndex);
    }

    [Fact]
    public void Loot_exit_with_treasure_left_asks_and_detect_is_offered_with_the_spell()
    {
        var c = Member("ANNA");
        c[0x17] = 5;                                     // detect magic memorised
        var loot = new List<byte[]> { Item(5, 20) };
        var (t, s) = Context(new Script(4, 1), Party(c), loot);
        VenueScreens.LootMenu(t);
        Assert.Equal(["VIEW", "TAKE", "POOL", "DETECT", "EXIT"], Assert.IsType<ChoiceRequest>(s.Asked[0]).Items);
        Assert.True(((ChoiceRequest)s.Asked[0]).Animates);       // 0x659D: animate flag 1 (the loot picture moves)
        Assert.Single(loot);                             // left behind after "no"
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Take_moves_coins_from_the_pool()
    {
        var c = Member("ANNA");
        var pool = new uint[] { 0, 0, 0, 7, 0, 0, 0 };
        var (t, s) = Context(new Script(0, "5"), Party(c), pool: pool);  // gold, 5
        MoneyScreens.Take(t);
        Assert.Equal(5, c.Word(Money.Gold));
        Assert.Equal(2u, pool[3]);
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Selling_in_the_view_pays_half()
    {
        var c = Member("ANNA");
        c.Items.Add(Item(30, 5));
        var (t, s) = Context(new Script(
            new ListAnswer(0, 0),       // ITEMS
            new ListAnswer(6, 0),       // SELL (READY USE TRADE DROP HALVE JOIN SELL ID EXIT)
            0,                          // a deal: YES
            null,                       // "sold" key
            new ListAnswer(8, 0),       // EXIT the items
            new ListAnswer(2, 0)), Party(c));  // EXIT the view (TRADE DROP EXIT: the item is gone, money came)
        CharacterView.Run(t, Venue.Shop);
        Assert.Empty(c.Items);
        Assert.Equal(3, c.Word(Money.Platinum));         // 15 gold = 3 pp
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void The_view_menu_offers_only_the_entries_of_0x21AED()
    {
        // DOSBox-X s24 (shop, Thrender: items and money, no spells): ITEMS TRADE DROP EXIT, so its Exit leaves the shared
        // highlight on the shop menu's 4th entry (Appraise), as the program does.
        var c = Member("ANNA", pp: 2);
        c.Items.Add(Item(30, 5));
        var (t, s) = Context(new Script(new ListAnswer(3, 0)), Party(c));
        CharacterView.Run(t, Venue.Shop);
        Assert.Equal(["ITEMS", "TRADE", "DROP", "EXIT"], s.Asked.OfType<ListRequest>().Single().Actions);
        c[0x17] = 5;                                     // a memorised spell adds SPELLS
        (t, s) = Context(new Script(new ListAnswer(4, 0)), Party(c));
        CharacterView.Run(t, Venue.Shop);
        Assert.Equal(["ITEMS", "SPELLS", "TRADE", "DROP", "EXIT"], s.Asked.OfType<ListRequest>().Single().Actions);
    }

    /// <summary>
    /// The camp (0x197B3), shop (0x730D), temple (0x5341) and loot menu (0x65B4) all call 0x21AED: with the program's
    /// page at hand their View asks it (DOSBox-X s39: the same page and words as the party menu's, no member switch
    /// inside View), Items still opens the items page, and a command not built yet is said on the page's bottom line
    /// (no text window over the page).
    /// </summary>
    [Fact]
    public void Camp_shop_and_loot_view_use_the_programs_page()
    {
        var c = Member("ANNA", pp: 2);
        c.Items.Add(Item(30, 5));
        c[0x17] = 5;
        var pages = new List<IReadOnlyList<ViewEntry>>();
        var answers = new Queue<int>([1, 0, 4]);          // SPELLS (not built), ITEMS, EXIT
        var (t, s) = Context(new Script(
            null,                       // the SPELLS notice's key
            new ListAnswer(8, 0)),      // EXIT the items page (shop: … SELL ID EXIT)
            Party(c), viewPage: (who, entries) =>
            {
                Assert.Same(c, who);
                pages.Add(entries);
                return answers.Dequeue();
            });
        CharacterView.Run(t, Venue.Shop);
        Assert.Equal(3, pages.Count);
        Assert.Equal([ViewEntry.Items, ViewEntry.Spells, ViewEntry.Trade, ViewEntry.Drop, ViewEntry.Exit], pages[0]);
        var note = Assert.IsType<KeyRequest>(s.Asked[0]);
        Assert.Equal("SPELLS: NOT IN THIS BUILD YET.", note.Prompt);
        Assert.IsType<ListRequest>(s.Asked[1]);
        Assert.Equal(0, s.Left);

        // The loot menu's View and the camp's V reach the same page.
        int asked = 0;
        (t, s) = Context(new Script(), Party(c), viewPage: (_, e) => { asked++; return e.Count - 1; });
        CharacterView.Run(t, Venue.Loot);
        Assert.Equal(1, asked);
        (t, s) = Context(new Script(1, 5), Party(c), viewPage: (_, e) => { asked++; return e.Count - 1; });  // VIEW, EXIT
        Assert.False(VenueScreens.Camp(CampScreenTests.Camp(t), null));
        Assert.Equal(2, asked);
        Assert.Equal(0, s.Left);
    }

    [Fact]
    public void Camp_view_has_no_sell()
    {
        var c = Member("ANNA");
        c.Items.Add(Item(30, 5));
        var (t, s) = Context(new Script(
            1,                          // VIEW
            new ListAnswer(0, 0),       // ITEMS
            new ListAnswer(6, 0),       // EXIT (no SELL / ID in camp)
            new ListAnswer(1, 0),       // EXIT the view (ITEMS EXIT)
            5), Party(c));              // EXIT
        Assert.False(VenueScreens.Camp(CampScreenTests.Camp(t), null));
        Assert.Equal(0, s.Left);
        var items = s.Asked.OfType<ListRequest>().ElementAt(1);
        Assert.DoesNotContain("SELL", items.Actions);
        Assert.Equal("CAMP:", Assert.IsType<ChoiceRequest>(s.Asked[0]).MenuPrompt);
    }
}

/// <summary>The program texts and tables the screens read (skipped without GOLDBOX_POR_DIR).</summary>
public class ClientResourcesTests
{
    [Fact]
    public void Every_text_and_table_is_found_in_v13()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return;
        var dir = new PorGameDirectory(d);
        var r = ClientResources.Load(dir);
        Assert.Equal(Enum.GetValues<Msg>().Length, r.Texts.FromGame);
        Assert.Equal(10, r.TempleNames?.Count);
        Assert.NotNull(r.CampMenu);
        Assert.Equal(6, r.CampMenu!.Split(' ').Length);
        Assert.NotNull(r.PagePrompt);
        // The camp's DS strings (v1.3 offsets, trusted because DS:0508 holds the camp line).
        Assert.Equal(6, r.MagicMenu?.Split(' ').Length);
        Assert.Equal(6, r.AlterMenu?.Split(' ').Length);
        Assert.Equal(2, r.OrderMenus?.Count);
        Assert.All(Enumerable.Range(1, 56), id => Assert.NotNull(r.SpellName(id)));
        Assert.NotNull(r.LevelName(1));
        Assert.NotNull(r.Spells);
        Assert.All(CampSpells.LabelledEffects, id => Assert.NotNull(r.Texts.EffectLabel(id)));
        Assert.Equal(7, CampScreens.ParseMenu(r.Texts.Original(Msg.RestMenu)).Items.Count);
        Assert.NotNull(r.ItemWord(1));
    }
}
