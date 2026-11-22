using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// View (GAME.OVR 0x21AED): the character page with its menu (Items, Spells, Trade, Drop, Exit), and the items page
/// whose menu adds Sell and Id in a shop or temple (DS:49F3 = 1; 0x22104–0x2217E). Only Sell, Id and the member
/// selection do real work, and Trade (the item's 0x2276E, the money's 0x23190; <see cref="TradeRules"/>); the other item
/// commands say they are not built yet (TODO: Ready 0x2207B, Use 0x2D669, Drop 0x234B2, Halve, Join, Spells). The character page is the program's (0x21084,
/// <see cref="CombatSheet"/>) through <see cref="TradeContext.ViewPage"/>; our list page (<see cref="Sheet(TradeContext,
/// EclCharacter)"/>) only when the Client has no party pages. [?] The items page is still ours, not 0x21F32's.
/// </summary>
public static class CharacterView
{
    // Our short labels (the program's class and status names are not read yet).
    private static readonly string[] Classes =
        ["CLERIC", "DRUID", "FIGHTER", "PALADIN", "RANGER", "MAGIC-USER", "THIEF", "MONK"];

    private static readonly string[] Statuses =
        ["OKAY", "ANIMATED", "TEMP GONE", "RUNNING", "UNCONSCIOUS", "DYING", "DEAD", "STONED", "GONE"];

    /// <summary>
    /// The character page until Exit (or Escape). <paramref name="page"/> asks for the page with the entries shown and
    /// answers the picked entry's index, or a member answer (<see cref="TradeContext.SelectMember"/>); null: our page
    /// (<see cref="Sheet(TradeContext, EclCharacter)"/> as a list page). The party menu passes the program's page
    /// (<see cref="PartyMenuFlow"/>: its V calls this routine, 0x19D23 → 0x21AED, as the camp's does, 0x197B3).
    /// </summary>
    public static void Run(TradeContext t, Venue venue, Func<EclCharacter, IReadOnlyList<ViewEntry>, int>? page = null)
    {
        // The camp (0x197B3), shop (0x730D), temple (0x5341) and loot menu (0x65B4) all call 0x21AED: the program's
        // page whenever the Client has it (TradeContext.ViewPage).
        page ??= t.ViewPage;
        // After Drop the loop redraws only the money lines (0x21547): the page keeps the other values it showed.
        EclCharacter? shown = null;
        t.TradeTarget = t.Current; // DS:471A := DS:5D92 (0x21B0B): Trade's pick starts on the viewed member
        while (t.Current is { } c)
        {
            // The menu has only the entries 0x21AED offers (FightItemScreens.ViewEntries): DOSBox-X s24, a fighter with
            // no memorised spells gets Items, Trade, Drop, Exit, so its Exit (4th) leaves the shared highlight on the
            // shop menu's 4th entry (Appraise).
            var entries = AmigaView.Entries(FightItemScreens.ViewEntries(c, venue == Venue.Shop ? FightItemScreens.InShop : (byte)0), c);
            // Each full draw of the page recalculates the member (0x21084 → 0x21642 → START 0x1758): on entry and after
            // I, S and T (the redraw set 0x21ACD), so an edited save's weight word or a traded weapon's dice show fresh.
            // Drop and the Amiga's Rename redraw nothing but the money lines (shown): no recalculation there.
            if (shown is null)
                t.Recalculate(c);
            var onPage = shown ?? c;
            shown = null;
            int action = page is not null
                ? page(onPage, entries)
                : t.Dialog.Page(new ListRequest(Sheet(t, c), [], [.. entries.Select(Word)], entries.Count - 1)
                {
                    PartySelect = true,
                    SharedHighlight = true,
                }).Action;
            if (t.SelectMember(action))
                continue;
            var entry = action >= 0 && action < entries.Count ? entries[action] : ViewEntry.Exit;
            switch (entry)
            {
                case ViewEntry.Items:
                    Items(t, venue);
                    break;
                case ViewEntry.Drop when page is not null && t.Coins is { } coins:
                    shown = DropMoney(t, c, venue, coins, onPage);
                    break;
                case ViewEntry.Trade when page is not null && t.Coins is { } coins:
                    TradeMoney(t, c, coins); // T is in the redraw set (0x21ACD): the page is drawn again after it
                    break;
                case ViewEntry.Rename:
                    shown = Rename(t, c, onPage);
                    break;
                case ViewEntry.Spells or ViewEntry.Trade or ViewEntry.Drop:
                    // Over the program's page there is no text window: the notice goes on its bottom line.
                    NotBuilt(t, Word(entry), page is not null);
                    break;
                default:
                    // View's end draws the party list (0x21D4F; view3d.md "Where the party list is drawn"; the Amiga's
                    // too: FS-UAE s41, a renamed member shows the new name in the camp's list after Exit).
                    t.RosterDrawn();
                    return;
            }
        }
    }

    private static string Word(ViewEntry e) => e.ToString().ToUpperInvariant();

    /// <summary>DS:49F3 for a venue: 0 party menu / hall, 1 shop and temple, 2 camp, 4 (or 3) exploring, 6 loot.</summary>
    public static byte Context(Venue venue) => venue switch
    {
        Venue.Shop => FightItemScreens.InShop,
        Venue.Camp => 2,
        Venue.Loot => 6,
        Venue.Explore => 4, // 3 on the overland: the same entries (Use at 2, 3 and 4)
        _ => 0,
    };

    /// <summary>
    /// View's Drop (0x234E1, then the money lines 0x21547): until no money is left or the player leaves the coin list, a
    /// coin kind and an amount (at most what is held; 0x2568A: coins and weight down, the pool up in a shop or the
    /// loot step). Returns the character as the page now shows it: <paramref name="shownBefore"/>'s bytes with the new
    /// money words, since the loop redraws only the money lines (encumbrance and the rest keep their old values).
    /// </summary>
    private static EclCharacter DropMoney(TradeContext t, EclCharacter c, Venue venue,
        Func<EclCharacter, IReadOnlyList<int>, CoinTexts?, (int Kind, ushort Amount)?> coins, EclCharacter shownBefore)
    {
        var shown = new EclCharacter(shownBefore.Record);
        shown.Items.AddRange(shownBefore.Items);
        void Money()
        {
            for (int i = 0; i < 2 * FightItemScreens.CoinKinds; i++)
                shown[FightItemScreens.Money + i] = c[FightItemScreens.Money + i];
        }
        while (true)
        {
            Money();
            var kinds = Enumerable.Range(0, FightItemScreens.CoinKinds).Where(k => FightItemScreens.Coins(c, k) != 0).ToList();
            if (kinds.Count == 0 || coins(shown, kinds, null) is not { } pick || !kinds.Contains(pick.Kind))
                break;
            ushort amount = Math.Min(pick.Amount, FightItemScreens.Coins(c, pick.Kind));
            FightItemScreens.DropCoins(c, pick.Kind, amount, Context(venue), t.Pool);
        }
        Money();
        return shown;
    }

    /// <summary>
    /// The Amiga's Rename (hunk 10 +0x5500–0x5536, <see cref="AmigaView"/>): "NEW NAME FOR " and the name in the input
    /// requester, its answer (an empty one too) becomes the name. The loop does not draw the page again after R, so it
    /// keeps showing <paramref name="shownBefore"/> (the old name; FS-UAE s41).
    /// </summary>
    private static EclCharacter Rename(TradeContext t, EclCharacter c, EclCharacter shownBefore)
    {
        var shown = new EclCharacter(shownBefore.Record);
        shown.Items.AddRange(shownBefore.Items);
        if (t.Dialog.Ask(new TextRequest(AmigaView.RenamePrompt(c), false, AmigaView.RenameLength) { KeepsCase = true }) is string name)
            c.Name = name;
        return shown;
    }

    /// <summary>Our notice for a command not built yet: in the text window, or on the bottom line where the screen has
    /// no text window (<see cref="TradeContext.MessagesOnBottomLine"/>: the party menu).</summary>
    private static void NotBuilt(TradeContext t, string what, bool bottomLine = false)
    {
        string text = what + ": NOT IN THIS BUILD YET.";
        if (bottomLine || t.MessagesOnBottomLine)
            t.Dialog.Ask(new KeyRequest(text) { Colour = 14 });
        else
            t.Dialog.Say(text, colour: 14);
    }

    /// <summary>The character page's lines: name, abilities, levels, hit points, money, weight.</summary>
    public static List<ListLine> Sheet(TradeContext t, EclCharacter c) => Sheet(t.Text, c);

    /// <summary>The character page's lines from the program's texts alone (the fight's View has no trade context).</summary>
    public static List<ListLine> Sheet(GameText x, EclCharacter c)
    {
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        string L(Msg m) => x[m].Trim(); // the program pads its labels for its own layout
        var lines = new List<ListLine> { new(AmigaView.Case(c.Name), 15, 1) };
        string str = c[0x10] == 18 && c[0x16] > 0 ? $"18/{(c[0x16] == 100 ? "00" : I(c[0x16]).PadLeft(2, '0'))}" : I(c[0x10]);
        lines.Add(new($"{L(Msg.Age)} {I(c.Word(0x30))}", 10, 1));
        (Msg, string)[] abilities =
        [
            (Msg.Str, str), (Msg.Int, I(c[0x11])), (Msg.Wis, I(c[0x12])), (Msg.Dex, I(c[0x13])), (Msg.Con, I(c[0x14])),
            (Msg.Cha, I(c[0x15])),
        ];
        foreach (var (m, v) in abilities)
            lines.Add(new($"{L(m)} {v}", 10, 1));
        var levels = new List<string>();
        for (int k = 0; k < 8; k++)
            if ((sbyte)c[0x96 + k] > 0)
                levels.Add($"{Classes[k]} {I((sbyte)c[0x96 + k])}");
        lines.Add(new($"{L(Msg.Level)} {string.Join("/", levels)}", 10, 1));
        uint xp = (uint)(c[0xAC] | c[0xAD] << 8 | c[0xAE] << 16 | c[0xAF] << 24);
        lines.Add(new($"{L(Msg.Exp)} {xp.ToString(CultureInfo.InvariantCulture)}", 10, 1));
        // [?] AC as 60 − 0x111 and HP as 0x11B / 0x32, as the explore screen's party list.
        lines.Add(new($"HP {I(c[0x11B])}/{I(c[0x32])}  AC {I(60 - c[0x111])}", 10, 1));
        byte st = c[0x10C];
        lines.Add(new($"{L(Msg.Status)} {(st < Statuses.Length ? Statuses[st] : I(st))}", 10, 1));
        lines.Add(new(MoneyScreens.MoneyLine(x, c), 14, 1));
        lines.Add(new($"{L(Msg.Encumbrance)} {I(c.Word(Money.Weight))}  {L(Msg.Movement)} {I(c[0x11C])}", 10, 1));
        // One row each (the screen places the lines top down from row 1).
        return lines;
    }

    /// <summary>
    /// The items page: the item list (readied column, name) with Ready, Use, Trade, Drop, Halve, Join, (Sell, Id)
    /// and Exit. Sell is added for a member with 0x84 &lt; 0x80, and for others only when in play = 0 or status = 1
    /// (0x22104–0x2217E, [?] intent).
    /// </summary>
    public static void Items(TradeContext t, Venue venue)
    {
        if (t.ItemsPage is { } itemsPage)
        {
            ProgramItems(t, venue, itemsPage);
            return;
        }
        int selected = 0, lastAction = 0;
        while (t.Current is { } c)
        {
            t.Recalculate(c); // START 0x1758 before each draw (the program: after each command, 0x224AF; DOSBox s11)
            // Use needs DS:49F3 at 2–4 (or a fight; 0x21F32): never at the party menu or in the training hall (0).
            var actions = new List<string> { "READY" };
            if (venue != Venue.PartyMenu)
                actions.Add("USE");
            actions.AddRange(["TRADE", "DROP", "HALVE", "JOIN"]);
            int sell = -1, id = -1;
            if (venue == Venue.Shop)
            {
                if (c[0x84] < 0x80 || c[0x10D] == 0 || c[0x10C] == 1)
                {
                    sell = actions.Count;
                    actions.Add("SELL");
                }
                id = actions.Count;
                actions.Add("ID");
            }
            actions.Add("EXIT");
            int exit = actions.Count - 1;
            var rows = c.Items.Select(i => (i[0x34] != 0 ? "YES " : "NO  ") + t.ItemName(i)).ToList();
            var lines = new List<ListLine>
            {
                new(AmigaView.Case(c.Name), 15, 1),
                new(MoneyScreens.MoneyLine(t, c), 14, 1),
            };
            var a = t.Dialog.Page(new ListRequest(lines, rows, actions, exit)
            {
                Selected = Math.Clamp(selected, 0, Math.Max(0, rows.Count - 1)),
                SelectedAction = Math.Min(lastAction, exit),
                RowActions = Enumerable.Range(0, exit).ToHashSet(),
                PartySelect = true,
            });
            if (t.SelectMember(a.Action))
                continue;
            if (a.Action < 0 || a.Action == exit)
                return;
            selected = a.Row;
            lastAction = a.Action;
            if (a.Row < 0 || a.Row >= c.Items.Count)
                continue;
            var item = c.Items[a.Row];
            if (a.Action == sell)
                Sell(t, c, item);
            else if (a.Action == id)
                Identify(t, c, item);
            else
                NotBuilt(t, actions[a.Action]);
        }
    }

    /// <summary>
    /// 0x21F32 on the program's page (<see cref="TradeContext.ItemsPage"/>): while the character has items, the entries
    /// of <see cref="FightItemScreens.ItemsEntries"/> for the venue's DS:49F3 (Use only in the camp, 2; Sell and Id in a
    /// shop or temple, 1) and the list, the row kept between choices; Exit or Escape back to View. Sell and Id run; the
    /// other commands are not built yet (our notice on the bottom line). After each choice the member (DS:5D92, the
    /// giver of a Trade) is recalculated (START 0x1758 at 0x224AF, on [bp−0x3F] := DS:5D92 from 0x21F39).
    /// </summary>
    private static void ProgramItems(TradeContext t, Venue venue,
        Func<EclCharacter, IReadOnlyList<ItemsEntry>, int, Func<byte[], string>, ListAnswer> itemsPage)
    {
        int row = 0;
        while (t.Current is { } c && c.Items.Count > 0)
        {
            var entries = FightItemScreens.ItemsEntries(c, Context(venue), null, t.Memory ?? new EclMemory());
            var a = itemsPage(c, entries, row, t.ItemName);
            if (a.Action < 0 || a.Action >= entries.Count || entries[a.Action] == ItemsEntry.Exit)
                return;
            row = a.Row;
            if (a.Row < 0 || a.Row >= c.Items.Count)
            {
                t.Recalculate(c);
                continue;
            }
            var item = c.Items[a.Row];
            switch (entries[a.Action])
            {
                case ItemsEntry.Sell:
                    t.TalkOverItemsPage(true);
                    try
                    {
                        SellOnPage(t, c, item);
                    }
                    finally
                    {
                        t.TalkOverItemsPage(false);
                    }
                    break;
                case ItemsEntry.Id:
                    t.TalkOverItemsPage(true);
                    try
                    {
                        IdentifyOnPage(t, c, item);
                    }
                    finally
                    {
                        t.TalkOverItemsPage(false);
                    }
                    break;
                case ItemsEntry.Trade:
                    bool letGo;
                    t.TalkOverItemsPage(true); // 0x21DAA's refusal and question talk over the page
                    try
                    {
                        letGo = LetGoOnPage(t, c, item);
                    }
                    finally
                    {
                        t.TalkOverItemsPage(false);
                    }
                    if (letGo)
                        TradeItem(t, c, item);
                    break;
                case var e:
                    NotBuilt(t, e.ToString().ToUpperInvariant(), bottomLine: true);
                    break;
            }
            t.Recalculate(c); // 0x224AF: START 0x1758 on DS:5D92 after every command
        }
    }

    // The Items page's text box (START 0x77F8 with (1, 21)–(38, 22), colour 14: 0x21E53, 0x22DF7, 0x22FE2, 0x230CC,
    // 0x23126), blanked at each command's end (03F1:0137 at 0x21E8F, 0x22EF7, 0x23149).
    private const int BoxLeft = 1, BoxTop = 21, BoxRight = 38, BoxBottom = 22;
    private const byte BoxColour = 14;

    private static void BoxText(TradeContext t, string text, bool clear = true) =>
        t.Dialog.Text.PrintIn(BoxLeft, BoxTop, BoxRight, BoxBottom, text, clear, BoxColour);

    private static void BoxBlank(TradeContext t) => t.Dialog.Text.ClearRect(BoxLeft, BoxTop, BoxRight, BoxBottom);

    /// <summary>START 0x2233 (0x1693): the text on row 24 in green, the game-speed pause (PauseOnly), row 24 cleared. The
    /// Amiga's copy (hunk 13 +0x4928) draws the whole string (no per-word wait) and waits speed · 100 ms
    /// (<see cref="AmigaView.MessagePause"/>).</summary>
    internal static void RowMessage(TradeContext t, string text)
    {
        if (AmigaView.MessagePause(t.Memory?.GameSpeed ?? 0) is { Ticks: > 0 } pause)
            t.Dialog.Ask(new DelayRequest(pause) { BottomLine = (text, TextWindow.ScriptColour) });
    }

    /// <summary>PauseOnly (START 0x7E4E): DS:49E2 × 225 ms, no key (the Amiga's Sell / Id: hunk 10 +0x6096 / +0x64A0,
    /// speed · 100 ms).</summary>
    private static void PauseOnly(TradeContext t)
    {
        if (AmigaView.MessagePause(t.Memory?.GameSpeed ?? 0) is { Ticks: > 0 } pause)
            t.Dialog.Ask(new DelayRequest(pause));
    }

    /// <summary>
    /// Sell on the program's Items page (0x22472 → 0x21DAA, 0x22D28; read s41): a readied item gets the unready message
    /// on row 24; a scroll with spells its owner's name at (1, 21), the warning after it (column name length + 2) in the
    /// box and the yes/no; then the offer in the box (cleared first), the deal question (0x2D9A0); Yes: the sold message on row 24,
    /// the item goes and the money with the overloaded message on row 24 when it went to the pool (0x24CDD). The box is
    /// blanked at the end of each routine.
    /// </summary>
    private static void SellOnPage(TradeContext t, EclCharacter c, byte[] item)
    {
        var d = t.Dialog;
        if (!LetGoOnPage(t, c, item))
            return;
        ushort price = ShopRules.SellPrice(item);
        BoxText(t, GameText.Cat(GameText.Cat(t.Text[Msg.IllGiveYou], price) + t.Text[Msg.GoldPiecesForYour], t.ItemName(item)));
        if (d.YesNo(t.Text[Msg.IsItADeal]))
        {
            RowMessage(t, t.Text[Msg.Sold]);
            if (ShopRules.Sell(c, t.Pool, item, Carrying.StrengthAllowance(c)))
                RowMessage(t, t.Text[Msg.SoldOverloaded]);
        }
        BoxBlank(t);
    }

    /// <summary>
    /// 0x21DAA on the program's Items page (Trade, Drop and Sell ask it first): a readied item gets the unready message
    /// on row 24; a scroll with a spell to scribe its owner's name at (1, 21) (START 0x23B6), the warning from column
    /// name length + 2 in the box and the yes/no. The box is blanked at its end (0x21E8F). True: the item may go.
    /// </summary>
    private static bool LetGoOnPage(TradeContext t, EclCharacter c, byte[] item)
    {
        var d = t.Dialog;
        switch (ShopRules.SellCheck(item, t.IsScroll(item[0x2E])))
        {
            case null:
                RowMessage(t, t.Text[Msg.MustBeUnreadied]);
                BoxBlank(t);
                return false;
            case true:
                var name = AmigaView.Case(c.Name);
                d.Text.ClearRect(BoxLeft, BoxTop, BoxRight, BoxBottom);
                d.Text.Put(BoxTop, BoxLeft, name, CombatChooser.NameColour(c)); // START 0x23B6 at (1, 21)
                d.Text.MoveTo(BoxLeft + name.Length + 1, BoxTop);               // DS:5E24 = name length + 2
                BoxText(t, t.Text[Msg.ScribeWarning], clear: false);
                bool lose = d.YesNo(t.Text[Msg.OkayToLose]);
                BoxBlank(t);
                return lose;
            default:
                return true;
        }
    }

    /// <summary>START 0x3379 for the venue (<see cref="TradeContext.InterfaceRedraw"/>): the item and money Trade draw
    /// it before their member pick.</summary>
    private static void InterfaceRedraw(TradeContext t)
    {
        if (t.InterfaceRedraw is { } redraw)
        {
            redraw();
            return;
        }
        t.Dialog.Text.Clear();
        t.FrameTextBox(false);
        t.RosterDrawn();
    }

    /// <summary>
    /// The item Trade (0x2276E, after 0x21DAA let the item go): START 0x3379 redraws the venue's screen (the exploring
    /// layout from the command line; on the overland the marker steps on), START's member pick (<see cref="MemberPick"/>)
    /// with the ask-whom prompt starts on DS:471A (<see cref="TradeContext.TradeTarget"/>); nobody picked ends. Else
    /// DS:471A := the pick and <see cref="TradeRules.GiveItem"/>: the carry test refuses with the overloaded message on
    /// row 24 (START 0x1693, the game-speed pause), or the item moves. The Items loop then draws its page again
    /// (0x2238A sets its redraw flag).
    /// </summary>
    private static void TradeItem(TradeContext t, EclCharacter c, byte[] item)
    {
        InterfaceRedraw(t);
        if (MemberPick.Run(t, t.Text[Msg.TradeWithWhom], t.TradeTarget, t.PickMenu) is not { } pick)
            return;
        t.TradeTarget = pick;
        if (!TradeRules.GiveItem(c, pick, item, Carrying.StrengthAllowance, t.Recalculate))
            RowMessage(t, t.Text[Msg.TradeOverloaded]);
    }

    /// <summary>
    /// The money Trade (0x23190): START 0x3379 redraws the venue's screen and the member pick asks with the trade-to
    /// prompt from DS:471A; nobody picked ends. Else View's page of the viewed member (0x21084, its recalculation) and,
    /// with DS:471A := the pick, until the coin list is left: the money lines (0x21547), the coin list of the viewed
    /// member and the amount (the input 0x24F1D, at most what it holds) with Trade's own texts, then the transfer
    /// 0x250D5 (<see cref="TradeRules.GiveCoins"/>; refused: the overloaded message on row 24). With no money left Trade
    /// ends; leaving the coin list after a transfer goes back to the redraw and the pick (0x23468 → 0x2315D: the flag
    /// [bp−0x131] the last transfer cleared). Leaving the first coin list before any transfer reads that flag
    /// unwritten `[?]` (stack contents): the Client ends Trade there.
    /// </summary>
    private static void TradeMoney(TradeContext t, EclCharacter c,
        Func<EclCharacter, IReadOnlyList<int>, CoinTexts?, (int Kind, ushort Amount)?> coins)
    {
        var texts = new CoinTexts(t.Text[Msg.TradeSelectCoin], t.Text[Msg.TradeHowMuch], t.Text[Msg.WillYouTrade]);
        while (true)
        {
            InterfaceRedraw(t);
            if (MemberPick.Run(t, t.Text[Msg.TradeTo], t.TradeTarget, t.PickMenu) is not { } pick)
                return;
            t.Recalculate(c); // the page 0x21084 recalculates DS:5D92 (0x21642 → START 0x1758)
            var shown = new EclCharacter(c.Record);
            shown.Items.AddRange(c.Items);
            t.TradeTarget = pick;
            bool traded = false;
            while (true)
            {
                for (int i = 0; i < 2 * FightItemScreens.CoinKinds; i++)
                    shown[FightItemScreens.Money + i] = c[FightItemScreens.Money + i];
                var kinds = Enumerable.Range(0, FightItemScreens.CoinKinds).Where(k => FightItemScreens.Coins(c, k) != 0).ToList();
                if (coins(shown, kinds, texts) is not { } p || !kinds.Contains(p.Kind))
                {
                    if (!traded)
                        return;
                    break;
                }
                ushort amount = Math.Min(p.Amount, FightItemScreens.Coins(c, p.Kind));
                if (!TradeRules.GiveCoins(c, pick, p.Kind, amount, Carrying.StrengthAllowance(pick)))
                    RowMessage(t, t.Text[Msg.Overloaded]);
                traded = true;
                if (!TradeRules.HasMoney(c))
                    return;
            }
        }
    }

    /// <summary>
    /// Id on the program's Items page (0x22F8C, read s41): the 200-gold offer in the box, the yes/no; Yes: paid from the
    /// character or else the pool (<see cref="ShopRules.Identify"/>), the no-money message on row 24 when neither can;
    /// paid: the nothing-new words and the item when it was identified already, else the item identified (0x35 := 0) and
    /// the looks-like words and its new name, both in the box, then PauseOnly. The box is blanked at the end.
    /// </summary>
    private static void IdentifyOnPage(TradeContext t, EclCharacter c, byte[] item)
    {
        BoxText(t, GameText.Cat(t.Text[Msg.IdentifyOffer], t.ItemName(item)));
        if (t.Dialog.YesNo(t.Text[Msg.IdentifyDeal]))
        {
            switch (ShopRules.Identify(c, t.Pool, item))
            {
                case ShopRules.IdentifyResult.NotEnoughMoney:
                    RowMessage(t, t.Text[Msg.IdentifyNoMoney]);
                    break;
                case ShopRules.IdentifyResult.AlreadyIdentified:
                    BoxText(t, GameText.Cat(t.Text[Msg.NothingNew], t.ItemName(item)));
                    PauseOnly(t);
                    break;
                default:
                    BoxText(t, GameText.Cat(t.Text[Msg.LooksLike], t.ItemName(item)));
                    PauseOnly(t);
                    break;
            }
        }
        BoxBlank(t);
    }

    /// <summary>Sell (0x22472): the readied check, the scroll question, the offer, then <see cref="ShopRules.Sell"/>.</summary>
    public static void Sell(TradeContext t, EclCharacter c, byte[] item)
    {
        var d = t.Dialog;
        switch (ShopRules.SellCheck(item, t.IsScroll(item[0x2E])))
        {
            case null:
                d.Say(t.Text[Msg.MustBeUnreadied]);
                return;
            case true:
                d.Show(AmigaView.Case(c.Name) + t.Text[Msg.ScribeWarning]);
                if (!d.YesNo(t.Text[Msg.OkayToLose]))
                    return;
                break;
        }
        ushort price = ShopRules.SellPrice(item);
        d.Show(GameText.Cat(GameText.Cat(t.Text[Msg.IllGiveYou], price) + t.Text[Msg.GoldPiecesForYour], t.ItemName(item)));
        if (!d.YesNo(t.Text[Msg.IsItADeal]))
            return;
        bool toPool = ShopRules.Sell(c, t.Pool, item, Carrying.StrengthAllowance(c));
        d.Say(t.Text[toPool ? Msg.SoldOverloaded : Msg.Sold]);
    }

    /// <summary>Id (0x22F8C): the 200-gold offer, then <see cref="ShopRules.Identify"/>.</summary>
    public static void Identify(TradeContext t, EclCharacter c, byte[] item)
    {
        var d = t.Dialog;
        d.Show(GameText.Cat(t.Text[Msg.IdentifyOffer], t.ItemName(item)));
        if (!d.YesNo(t.Text[Msg.IdentifyDeal]))
            return;
        switch (ShopRules.Identify(c, t.Pool, item))
        {
            case ShopRules.IdentifyResult.NotEnoughMoney:
                d.Say(t.Text[Msg.IdentifyNoMoney]);
                break;
            case ShopRules.IdentifyResult.AlreadyIdentified:
                d.Say(GameText.Cat(t.Text[Msg.NothingNew], t.ItemName(item)));
                break;
            default:
                d.Say(t.ItemName(item)); // the item is renamed (item-format.md "Identify"); [?] the program's wording here
                break;
        }
    }
}
