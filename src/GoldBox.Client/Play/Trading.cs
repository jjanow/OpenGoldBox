using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>Where the money and item screens run (the program's interface state DS:49F3).</summary>
public enum Venue
{
    /// <summary>Camp (DS:49F3 = 2): View without Sell / Id.</summary>
    Camp,
    /// <summary>Shop or temple (DS:49F3 = 1): the items menu adds Sell and Id.</summary>
    Shop,
    /// <summary>The loot step after a fight (DS:49F3 = 6).</summary>
    Loot,
    /// <summary>The party menu and the training hall (0x19AE0 sets DS:49F3 = 0): the items menu has no Use.</summary>
    PartyMenu,
    /// <summary>The command line's View (DS:49F3 = 4 in the 3D view, 3 on the overland): Use, no Sell / Id.</summary>
    Explore,
}

/// <summary>
/// What the shop, temple, loot and camp screens work on: the party, the money pool (DS:67F4) and the treasure list
/// (DS:6810: the shop's stock or the loot), the game's random generator, and the program's texts. The rules are
/// Core's (<see cref="ShopRules"/>, <see cref="Money"/>, <see cref="TempleRules"/>, <see cref="Carrying"/>); the
/// menus and their order follow the program (docs/re/shop-format.md, combat-format.md "After combat").
/// </summary>
public sealed class TradeContext
{
    public required Dialog Dialog { get; init; }

    /// <summary>Shows a full-screen window over the screen (null closes it).</summary>
    public Action<IReadOnlyList<WindowLine>?> ShowWindow { get; init; } = _ => { };

    /// <summary>
    /// A full-screen window's text goes through the text routine START 0x77F8 (the NPC share window, 0x69F0): the
    /// window is shown as it grows and types while DS:4A00 is set (<see cref="ClientPlayer"/>'s typing). Unset: at once.
    /// </summary>
    public Action<TextWindow> TypesInto { get; init; } = _ => { };

    /// <summary>
    /// A list page stays on the screen under the text box until the next page (the camp's Cast: 0x17D28 draws the box's
    /// frame over the spell list and blanks its inside; row 24 keeps the list's menu line). Null takes it away.
    /// </summary>
    public Action<ListRequest?> ShowPageUnder { get; init; } = _ => { };

    /// <summary>A list page stays on the screen with nothing over it but row 24 (a keep question after a spell list). Null takes it away.</summary>
    public Action<ListRequest?> ShowPageAlone { get; init; } = _ => { };

    /// <summary>
    /// START 0x16EC's camp branch drew the text box's frame again (03F1:0000 around (1, 17)–(38, 22)): its top row runs
    /// through the joint at (16, 16) until the screen is redrawn (START 0x27D9). False: the redraw.
    /// </summary>
    public Action<bool> FrameTextBox { get; init; } = _ => { };

    /// <summary>
    /// A member's name stays as the party list last drew it (out of play, light red) after a revive outside a fight:
    /// 0x2C753 draws no list and the rest loop never calls START 0x0767 (D s38); the next full redraw shows it again.
    /// </summary>
    public Action<EclCharacter> HoldRosterName { get; init; } = _ => { };

    /// <summary>The party list drawn again (START 0x0767): held names show their colour again.</summary>
    public Action RosterDrawn { get; init; } = () => { };

    /// <summary>
    /// A shop, temple or loot loop drew its screen again (START 0x3379, case 1 or 6): the shop after Buy and Take
    /// (0x743C), after Appraise with gems or jewellery to appraise (0x744D), the temple likewise (0x5479, 0x548A), View's
    /// exit and the heal list's end (0x51A0); the loot menu on entry (0x644B, the chest loaded). A picture a venue menu
    /// drew over the view box (<see cref="ClientPlayer.VenuePictureOver"/>) is gone.
    /// </summary>
    public Action VenueRedrawn { get; init; } = () => { };

    /// <summary>The party list drawn with another lit member (null: none), as START's member pick draws it (00BA:2CA1).</summary>
    public Action<EclCharacter?> RosterDrawnLit { get; init; } = _ => { };

    /// <summary>
    /// The position line keeps what it showed (true) until released (false): the rest loop 0x24A66 never draws it, the
    /// camp's Rest draws it after (0x1804A; D s33: 10:50 under a handler message on the first tick, the clock at 10:55).
    /// </summary>
    public Action<bool> HoldPositionLine { get; init; } = _ => { };
    public required GameText Text { get; init; }
    /// <summary>The fight's texts (the Items page's Ready / Drop / Halve messages are the same strings, GAME.OVR unit
    /// 0x2103A, catalogued as <see cref="CombatMsg"/>).</summary>
    public CombatText CombatTexts { get; init; } = new();

    /// <summary>The list routine's page words Next and Prev (GAME.OVR 0x2D657 / 0x2D65D, read as the aim menu's first
    /// two, <see cref="CombatMsg.AimNextPrevManual"/>).</summary>
    public (string Next, string Prev) ListWords { get; init; } = ("NEXT", "PREV");

    public required EclParty Party { get; init; }
    /// <summary>The pool: seven dwords (copper … jewellery).</summary>
    public required uint[] Pool { get; init; }
    /// <summary>The treasure list (shop stock or loot), head first.</summary>
    public required List<byte[]> Treasure { get; init; }
    /// <summary>Random(n): 0..n−1 from the game's generator.</summary>
    public required Func<int, int> Random { get; init; }
    /// <summary>Item name word w from the program's table, or null.</summary>
    public Func<int, string?> Word { get; init; } = _ => null;
    /// <summary>Whether an item type is a scroll (base slot 11..13; GAME.OVR 0x2A255), from the ITEMS table.</summary>
    public Func<byte, bool> IsScroll { get; init; } = _ => false;

    /// <summary>The ITEMS table for the stat recalculation START 0x1758 (<see cref="Recalculate"/>), or null.</summary>
    public BaseItemTable? ItemTypes { get; init; }

    /// <summary>
    /// The stat recalculation START 0x1758 on a member (<see cref="StatRecalc.Recalculate"/>: item count, hands,
    /// weight, AC, THAC0, attack dice, movement…). Without the ITEMS table only the weight and item count.
    /// </summary>
    public void Recalculate(EclCharacter c)
    {
        if (ItemTypes is { } items)
        {
            StatRecalc.Recalculate(c, items);
            return;
        }
        Carrying.RecalculateWeight(c);
        c[StatRecalc.ItemCount] = (byte)Math.Min(c.Items.Count, 255);
    }
    /// <summary>The temple's heal list names (program table), or null.</summary>
    public IReadOnlyList<string>? TempleNames { get; init; }
    /// <summary>The game's effect engine outside a fight (<c>PorGame.OutsideEffects</c>): the temple's cure 1 removes the
    /// disease effects through it (0x4864, <see cref="TempleRules.CureDisease"/>). Null: the records are only dropped.</summary>
    public EffectHandlers? Effects { get; init; }

    /// <summary>
    /// A fresh effect engine for the Items page's item handlers outside a fight (the game's rules: race names,
    /// experience, level seams), with no other listener on its messages; null: none (the handlers do not run).
    /// </summary>
    public Func<EffectHandlers>? ItemEffects { get; init; }

    /// <summary>
    /// The item handler 0x2AEEA (kind 0x3E &gt; 0x7F) run by the Items page's Ready outside a fight (0x2250A): the
    /// handler on an <see cref="ItemEffects"/> engine with its messages shown as START 0x16EC / 0x1693 show them outside
    /// a fight (<see cref="OutsideEffectMessages"/> framed over the page). Its own engine, so the game's engine's display
    /// (the area window's, under the page) does not show the text a second time. Null when there is no engine.
    /// </summary>
    public Action<EclCharacter, byte[], bool>? ItemHandler => ItemEffects is { } make ? (c, item, removed) =>
    {
        var fx = make();
        var shown = new OutsideEffectMessages(Dialog.Text, Dialog.Ask, () => Memory?.GameSpeed ?? 0, CombatTexts, () => false,
            null, () => FrameOverItemsPage(true), HoldRosterName, RosterDrawn);
        fx.Event += (who, e) => shown.Show(who, e, fx);
        try
        {
            fx.CallHandler(item[ItemCommands.Kind], c, item, removed);
        }
        finally
        {
            FrameOverItemsPage(false); // 0x221CE: DS:6821 set by the handler, the page is drawn again whole
        }
    } : null;

    /// <summary>View's Spells outside a fight (0x21AED → 0x239D6 in show mode): the current member's memorised list, Exit
    /// back to View. Null: not built here (our notice).</summary>
    public Action? ShowMemorised { get; init; }
    /// <summary>
    /// Where the original program stops (a run-time error back to DOS): shows it and ends the game. Called on the logic
    /// thread; the exception is thrown on afterwards, so the screen that hit it never goes on.
    /// </summary>
    public Action<ProgramHaltException>? Halt { get; init; }

    /// <summary>The screen has no text window (the party menu): our notices go on the bottom line as a key wait.</summary>
    public bool MessagesOnBottomLine { get; init; }

    /// <summary>
    /// View's page and menu as the program draws them (<see cref="PartyMenuFlow.ViewPage"/>: 0x21AED, which the camp,
    /// shop, temple and loot menus call); null: <see cref="CharacterView"/>'s own list page.
    /// </summary>
    public Func<EclCharacter, IReadOnlyList<ViewEntry>, int>? ViewPage { get; init; }

    /// <summary>View's Items page as the program draws it (<see cref="PartyMenuFlow.ItemsPage"/>, 0x21F32): the entry and
    /// the row; null: our list page.</summary>
    public Func<EclCharacter, IReadOnlyList<ItemsEntry>, int, Func<byte[], string>, ListAnswer>? ItemsPage { get; init; }

    /// <summary>
    /// On while an Items command talks over <see cref="ItemsPage"/> (Sell 0x22D28, Id 0x22F8C and their check 0x21DAA:
    /// the text box (1, 21)–(38, 22) of <see cref="Dialog"/>'s window, the yes/no and row-24 messages over the page).
    /// </summary>
    public Action<bool> TalkOverItemsPage { get; init; } = _ => { };

    /// <summary>On while an item handler's text shows over <see cref="ItemsPage"/> (START 0x16EC outside a fight: the
    /// text window's frame again, (1, 17)–(38, 22) blanked, the name and the text).</summary>
    public Action<bool> FrameOverItemsPage { get; init; } = _ => { };

    /// <summary>The Items page's Use outside a fight (0x222F0 → 0x22AC4; <see cref="CampScreens.UseItem"/>), set by
    /// the camp and the command line's View; null: not offered here.</summary>
    public Action<EclCharacter, byte[]>? UseItem { get; set; }

    /// <summary>The loot menu's D (0x6605 → 0x27C38(spell, 0, 0, var done); <see cref="CampScreens.LootDetect"/>), set
    /// by the after-combat loot; null: the cast is not built here.</summary>
    public Action<int>? LootDetect { get; set; }

    /// <summary>
    /// View's coin list and amount (<see cref="PartyMenuFlow.Coins"/>): Drop's (0x234E1, texts null) and the money
    /// Trade's (0x23190, its own copies of the texts); null: not built.
    /// </summary>
    public Func<EclCharacter, IReadOnlyList<int>, CoinTexts?, (int Kind, ushort Amount)?>? Coins { get; init; }

    /// <summary>
    /// START 0x3379 for the venue's interface state DS:49F3 (the item and money Trade draw it before their member pick:
    /// 0x22781, 0x231A4): exploring, the area's layout (<see cref="ClientPlayer.DrawInterface"/>); null: the text box
    /// cleared and the party list drawn.
    /// </summary>
    public Action? InterfaceRedraw { get; init; }

    /// <summary>The member pick's item line (START 0x37E1: Select, Exit), or null for ours.</summary>
    public string? PickMenu { get; init; }

    /// <summary>
    /// DS:471A, the member Trade's pick starts on: View sets it to the viewed member (0x21B0B), each Trade to its pick
    /// (0x227B0, 0x231E4), so a later Trade in the same View starts on the last member traded with.
    /// </summary>
    public EclCharacter? TradeTarget { get; set; }

    /// <summary>The game's memory (VM $49E5 for the Items menu's Use), when there is a game.</summary>
    public EclMemory? Memory { get; init; }

    public EclCharacter? Current => Party.Current;

    /// <summary>Detect magic (effect 5) on any party member: the naming routine marks magical items with "*".</summary>
    public bool Detect => Party.Members.Any(m => m.HasEffect(5));

    public string ItemName(byte[] item) => AmigaView.Case(ItemNames.Compose(item, Word, Detect).TrimEnd());

    /// <summary>The name as the naming routine leaves it (START 0x0FE2: ending in a space), which the shop's stock rows
    /// copy whole (0x6D04).</summary>
    public string StoredItemName(byte[] item) => AmigaView.Case(ItemNames.Compose(item, Word, Detect));

    /// <summary>The pool holds money (0x25C07, first answer).</summary>
    public bool PoolHasMoney => Pool.Any(v => v != 0);

    /// <summary>Apply a member answer of a menu (previous / next: the program's 0x23819 with Home / End,
    /// <see cref="PartySelection.Select"/>; or a clicked name). Returns false for other answers.</summary>
    public bool SelectMember(int answer)
    {
        int n = Party.Members.Count;
        if (n == 0)
            return false;
        switch (answer)
        {
            case ChoiceRequest.PreviousMember:
                PartySelection.Select(Party, PartySelection.Home); // the head wraps to the last member
                return true;
            case ChoiceRequest.NextMember:
                PartySelection.Select(Party, PartySelection.End);  // the last wraps to the head
                return true;
            case ChoiceRequest.FirstMember:
                PartySelection.Select(Party, 0); // any other extended key: the head (user s50)
                return true;
            case <= MemberPick when MemberPick - answer < n:
                Party.CurrentIndex = MemberPick - answer;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Answers at or below this pick a member directly (a click on the party list): <c>MemberPick − index</c>.</summary>
    public const int MemberPick = -100;
}

/// <summary>The coin list's title and the amount question's two halves (Drop's or the money Trade's copies).</summary>
public sealed record CoinTexts(string Title, string HowMuch, string WillYou);

/// <summary>The money routines the shop, temple and loot menus share (GAME.OVR 0x25191, 0x252C3, 0x25974, 0x2663B).</summary>
public static class MoneyScreens
{
    /// <summary>Pool (0x25191).</summary>
    public static void Pool(TradeContext t) => ShopRules.PoolMoney(t.Party.Members, t.Pool);

    /// <summary>
    /// Share (0x252C3). With money in the pool and no member that counts (0x84 = 0 or 0xB3) the program divides by
    /// zero and quits to DOS with run-time error 200 (<see cref="ShopRules.Share"/>): the halt goes to
    /// <see cref="TradeContext.Halt"/> (the Client ends the game there), then on up so the menu never resumes.
    /// </summary>
    public static void Share(TradeContext t)
    {
        try
        {
            ShopRules.Share(t.Party.Members, t.Pool, Carrying.StrengthAllowance);
        }
        catch (ProgramHaltException e)
        {
            t.Halt?.Invoke(e);
            throw;
        }
    }

    /// <summary>
    /// Take (0x25974 → 0x25711), P + D s46: the window (1, 1)–(38, 22) with no title (START 03F1:02E8), then until the
    /// pool is empty or the player leaves: the kinds the pool holds as a list (built 6 down to 0 by putting each in
    /// front, so copper comes first; "Copper 100"), the list routine 0x2D669 with the window (2, 2)–(15, 8), colours
    /// 13 / 10 / 15, the prompt "Select type of coin", an empty word and Exit (flag 1), the position word 0 once
    /// (0x259A4) and then as the routine leaves it; any key but Escape / E / 0 takes the row (Return too, with Exit lit).
    /// Then the amount (input 0x24F1D, colour 10: "How much " + the coin + " will you take?", at most the pool's low
    /// word) on row 24 over the list drawn unlit; overloaded → a message and nothing moves. The Amiga passes the same
    /// window and position (hunk 19 +0x3A60–0x3B84).
    /// </summary>
    public static void Take(TradeContext t)
    {
        int position = 0;
        while (t.Current is { } c)
        {
            // 0x259CF / 0x25BD9: a kind is listed, and the loop goes on, while its pool longint is above 0 (signed).
            var kinds = Enumerable.Range(0, 7).Where(k => (int)t.Pool[k] > 0).ToList();
            if (kinds.Count == 0)
                return;
            var rows = kinds.Select(k => $"{t.Text.Coin(k)} {t.Pool[k].ToString(CultureInfo.InvariantCulture)}").ToList();
            var page = new ListRequest([], rows, ["EXIT"], 0, FirstRow: CoinKindsFirstRow)
            {
                Selected = position,
                EnterPicks = true,
                Paging = new ListPaging(t.ListWords.Next, t.ListWords.Prev, CoinKindsRows, 0),
                Spell = new SpellPageLayout("", 10, "")
                {
                    CentredTitle = true,
                    RowColumn = CoinKindsColumn,
                    Prompt = AmigaView.Case(t.Text[Msg.SelectCoin].TrimEnd()) + " ",
                },
            };
            var a = t.Dialog.Page(page);
            if (a.Action != ListRequest.RowPicked || a.Row < 0 || a.Row >= kinds.Count)
                return;
            position = a.Row;
            int kind = kinds[a.Row];
            string prompt = AmigaView.AmountPrompt(GameText.Cat(GameText.Cat(t.Text[Msg.HowMuch], t.Text.Coin(kind)), t.Text[Msg.WillYouTake]));
            t.ShowPageAlone(page);
            // The program caps the input at the pool's low word (0x25B89) and lets the coin word and weight wrap:
            // original bugs, fixed (user, s48, s50): the pool up to 65535, less what would wrap the character's coin
            // word or weight (shop-format.md "Take").
            var typed = AmigaView.Number(t.Dialog.Ask, new TextRequest(prompt, true, 5) { Maximum = TakeMaximum(c, t.Pool[kind], kind) });
            t.ShowPageAlone(null);
            // 0x24F1D answers 0 to Escape and to Return with nothing typed (Val of an empty string), and 0x25974 calls
            // 0x25711 with it all the same (0x25BAF): its carry test then refuses a member already over the limit (the
            // message), else every write adds 0 (P + D s47, shop-format.md "Take").
            if (!ushort.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                amount = 0;
            if (!ShopRules.Take(c, t.Pool, kind, amount, Carrying.StrengthAllowance(c)))
                VenueScreens.RowMessageOver(t, page, t.Text[Msg.Overloaded]); // 0x2573C: START 0x2233
        }
    }

    /// <summary>
    /// Take's amount maximum (<see cref="ShopRules.TakeMaximum"/>): the pool, at most 65535 (s48 fix; the program's
    /// <c>pool &amp; 0xFFFF</c> made a pool of 65536 untakeable), less what would wrap the coin word or weight (s50 fix).
    /// </summary>
    public static ushort TakeMaximum(EclCharacter c, uint pool, int kind) => ShopRules.TakeMaximum(c, pool, kind);

    /// <summary>Take's coin-kind window (2, 2)–(15, 8): its first row, rows and column.</summary>
    public const int CoinKindsFirstRow = 2, CoinKindsRows = 7, CoinKindsColumn = 2;

    /// <summary>
    /// Appraise (0x2663B): needs gems or jewellery on the current character; Gems / Jewelry / Exit until Exit or
    /// nothing is left; each appraisal rolls a value, then Sell, or Sell / Keep when the character can carry one more
    /// item (<see cref="ShopRules.Appraise"/>). Each pass of the menu, Exit included, ends with START 0x1758 on the
    /// character (0x26EC2); with none left at the start the message and no recalculation (0x26661), with none left
    /// after an appraisal the routine just ends (0x26698 → 0x26ECF).
    /// </summary>
    public static void Appraise(TradeContext t)
    {
        if (t.Current is not { } c)
            return;
        var d = t.Dialog;
        for (bool first = true; ; first = false)
        {
            int gems = c.Word(Money.Gems), jewels = c.Word(Money.Jewellery);
            if (gems == 0 && jewels == 0)
            {
                if (first)
                    d.Say(t.Text[Msg.NoGems]);
                return;
            }
            d.Show(t.Text[Msg.FineCollection]);
            if (gems > 0)
            {
                d.Text.NewLine();
                d.Show(gems + t.Text[gems == 1 ? Msg.OneGem : Msg.Gems], clear: false);
            }
            if (jewels > 0)
            {
                d.Text.NewLine();
                d.Show(jewels + t.Text[jewels == 1 ? Msg.OneJewel : Msg.Jewels], clear: false);
            }
            var items = new List<string>();
            if (gems > 0) items.Add(t.Text[Msg.GemsItem].Trim());
            if (jewels > 0) items.Add(t.Text[Msg.JewelryItem].Trim());
            items.Add("EXIT");
            // The first menu runs with the keypad flag and its loop compares the byte with G / J / E (s53 rule): Home
            // and keypad 7 (0x47 = G) appraise a gem; End and the other extended keys do nothing.
            int pick = d.Menu(items, t.Text[Msg.AppraisePrompt], cancel: items.Count - 1,
                extendedKeys: scan => scan == 0x47 && gems > 0 ? 0 : null);
            if (pick < 0 || pick >= items.Count - 1)
            {
                t.Recalculate(c); // Exit (E or 0) also passes 0x26EC2
                return;
            }
            bool jewellery = gems == 0 || pick == 1;
            ShopRules.Appraise(c, t.Pool, jewellery, t.Random, (value, canKeep) =>
            {
                d.Show(GameText.Cat(t.Text[jewellery ? Msg.JewelValued : Msg.GemValued], value));
                string[] choices = canKeep ? ["SELL", "KEEP"] : ["SELL"];
                // K only when offered; any other key sells (0x2663B), so backing out sells too.
                return d.Menu(choices, t.Text[Msg.YouCan], cancel: 0) == 1;
            }, Carrying.StrengthAllowance(c));
            t.Recalculate(c); // START 0x1758 after each appraisal (0x26EC2)
        }
    }

    /// <summary>
    /// The exit of the shop (0x7350–0x740D) and temple (0x5384–0x544A): with money in the pool (0x25C07's first answer)
    /// the two texts and "Yes No" through the menu helper (<see cref="StayAtExit"/>); returns true to stay. Pool items
    /// alone do not ask. The shop prints its question in colour 15 running on after the message; the temple passes
    /// colour 10 (<paramref name="goBackColour"/>). The temple's question also passes the clear flag 1 (0x53E8), so in
    /// the program the priest's message is typed and at once replaced (DOSBox-X s47): an original bug, fixed (user,
    /// s48): its question runs on after the message as the shop's does.
    /// </summary>
    public static bool StayForMoney(TradeContext t, Msg leaving, Msg goBack, byte? goBackColour = null) =>
        t.PoolHasMoney && StayAtExit(t, leaving, goBack, goBackColour);

    /// <summary>
    /// A venue's exit question (shop 0x736F, temple 0x53A3, loot menu 0x6636): the first text through the text routine in
    /// the box (1, 17)–(38, 22), colour 10, cleared first; the second (0709:0768) running on after it in colour 15 or
    /// the caller's (<paramref name="goBackColour"/>: the temple's 10, the Amiga loot's pen 24); then "~Yes ~No" through
    /// the menu helper 0x8B57 (<see cref="Dialog.MenuHelper"/>: the shared highlight, Escape ignored, Return answers
    /// the lit word (s48), Home / End pick a member and draw the party list). Answer 1 (No) leaves with the text left in the box (COMBAT's last call,
    /// START 0x3379, clears it); any other answer blanks the box (0709:0DD3 with 1, 17, 38, 22) and stays. DOSBox-X s47
    /// (test saves J and K): the shop's and the loot's question run on after the message, both open on Yes after Exit,
    /// End during the question lit the next member and kept the question, Yes left the box empty.
    /// </summary>
    public static bool StayAtExit(TradeContext t, Msg leaving, Msg goBack, byte? goBackColour = null)
    {
        t.Dialog.Show(t.Text[leaving]);
        t.Dialog.Show(t.Text[goBack], clear: false, colour: goBackColour ?? 15);
        int answer;
        while (t.SelectMember(answer = t.Dialog.MenuHelper(["YES", "NO"])))
            t.RosterDrawn(); // 0x8BD3: START 0x0767 after the selection
        if (answer == 1)
            return false;
        t.Dialog.Text.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        return true;
    }

    /// <summary>The money of a character as "N KIND" parts, highest kind first (for the view and lists).</summary>
    public static string MoneyLine(TradeContext t, EclCharacter c) => MoneyLine(t.Text, c);

    /// <summary>As <see cref="MoneyLine(TradeContext, EclCharacter)"/> from the texts alone.</summary>
    public static string MoneyLine(GameText text, EclCharacter c)
    {
        var parts = new List<string>();
        for (int k = 6; k >= 0; k--)
            if (c.Word(Money.Coins + 2 * k) is var v and > 0)
                parts.Add($"{v} {text.Coin(k)}");
        return string.Join(" ", parts);
    }
}
