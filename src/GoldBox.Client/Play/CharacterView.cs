using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// View (GAME.OVR 0x21AED): the character page with its menu (Items, Spells, Trade, Drop, Exit), and the items page
/// whose menu adds Sell and Id in a shop or temple (DS:49F3 = 1; 0x22104–0x2217E). Only Sell, Id and the member
/// selection do real work; the other item commands say they are not built yet (TODO: Ready 0x2207B, Use 0x2D669,
/// Trade 0x23154, Drop 0x234B2, Halve, Join, Spells — not read). [?] The page layout is ours (same data; the
/// program's character page is not decoded yet).
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
        while (t.Current is { } c)
        {
            // The menu has only the entries 0x21AED offers (FightItemScreens.ViewEntries): DOSBox-X s24, a fighter with
            // no memorised spells gets Items, Trade, Drop, Exit, so its Exit (4th) leaves the shared highlight on the
            // shop menu's 4th entry (Appraise).
            var entries = FightItemScreens.ViewEntries(c, venue == Venue.Shop ? FightItemScreens.InShop : (byte)0);
            int action = page is not null
                ? page(c, entries)
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
                case ViewEntry.Spells or ViewEntry.Trade or ViewEntry.Drop:
                    NotBuilt(t, Word(entry));
                    break;
                default:
                    return;
            }
        }
    }

    private static string Word(ViewEntry e) => e.ToString().ToUpperInvariant();

    /// <summary>Our notice for a command not built yet: in the text window, or on the bottom line where the screen has
    /// no text window (<see cref="TradeContext.MessagesOnBottomLine"/>: the party menu).</summary>
    private static void NotBuilt(TradeContext t, string what)
    {
        string text = what + ": NOT IN THIS BUILD YET.";
        if (t.MessagesOnBottomLine)
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
        var lines = new List<ListLine> { new(c.Name.ToUpperInvariant(), 15, 1) };
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
        int selected = 0, lastAction = 0;
        while (t.Current is { } c)
        {
            Carrying.RecalculateWeight(c); // the items screen redraw recalculates (DOSBox s11: weight fresh again)
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
                new(c.Name.ToUpperInvariant(), 15, 1),
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
                d.Show(c.Name.ToUpperInvariant() + t.Text[Msg.ScribeWarning]);
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
