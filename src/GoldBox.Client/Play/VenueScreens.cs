using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// The shop (GAME.OVR 0x7212), the temple (0x5280), the loot step after a fight (0x5E94, 0x644B) and the camp menu
/// (0x196A0), run on the logic thread through a <see cref="TradeContext"/>. Menus, keys and their conditions are the
/// program's (docs/re/shop-format.md, combat-format.md "After combat"); the screens keep the explore layout (party
/// list on the right, text window, menu on the bottom line), lists and the character view are full-screen pages.
/// </summary>
public static class VenueScreens
{
    /// <summary>The shop and loot menus compare the key with G / O for the party selection (0x7415, 0x66E4).</summary>
    private const string MemberLetters = "GO";

    // ---- Shop (0x7212) ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The shop: the pool and DS:6DC0 are cleared, the stock is the treasure list. Menu: Buy, View, Take, Pool, Share,
    /// Appraise, Exit while the pool holds money (0x25C07's first answer), else Buy, View, Pool, Appraise, Exit.
    /// </summary>
    public static void Shop(TradeContext t)
    {
        Array.Clear(t.Pool);
        while (true)
        {
            t.RosterDrawn(); // 0x722B / 0x745A: the party list on entry and after every pass of the menu (a pick too)
            bool money = t.PoolHasMoney;
            string[] menu = money
                ? ["BUY", "VIEW", "TAKE", "POOL", "SHARE", "APPRAISE", "EXIT"]
                : ["BUY", "VIEW", "POOL", "APPRAISE", "EXIT"];
            int i = t.Dialog.Menu(menu, cancel: menu.Length - 1, partySelect: true, memberLetters: MemberLetters);
            if (t.SelectMember(i))
                continue;
            switch (menu[Math.Clamp(i, 0, menu.Length - 1)])
            {
                case "BUY": Buy(t); t.VenueRedrawn(); break; // 0x743C
                case "VIEW": CharacterView.Run(t, Venue.Shop); t.VenueRedrawn(); break; // View's exit
                case "TAKE": MoneyScreens.Take(t); t.VenueRedrawn(); break; // 0x743C
                case "POOL": MoneyScreens.Pool(t); break;
                case "SHARE": MoneyScreens.Share(t); break;
                case "APPRAISE": Appraise(t); break;
                default:
                    if (!MoneyScreens.StayForMoney(t, Msg.ShopMoneyLeft, Msg.ShopGoBack))
                    {
                        t.RosterDrawn(); // the pass's list comes before the exit test
                        return;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Buy (0x7035): the stock list (0x6D04: a value 0 becomes 1; the name over the value right-aligned at column 30) on
    /// the item page (<see cref="ItemPage"/>); B or Enter buys the highlighted item for the current character
    /// (<see cref="ShopRules.Buy"/>), anything else leaves. The position word starts at 1 (0x7050) and keeps the row
    /// the list routine leaves in it (P + D s46: after start-up's top 1 the window starts at the second item with Prev
    /// shown; after a list that took the top to 0, the first window with the second row lit).
    /// </summary>
    public static void Buy(TradeContext t)
    {
        ShopRules.PriceStock(t.Treasure);
        int position = 1;
        while (t.Current is { } c && t.Treasure.Count > 0)
        {
            var rows = t.Treasure.Select(it => PriceRow(t.StoredItemName(it), it)).ToList();
            var page = ItemPage(t, t.Text[Msg.ShopTitle], "BUY", rows, position);
            var a = t.Dialog.Page(page);
            if (a.Action != 0 || a.Row < 0 || a.Row >= t.Treasure.Count)
                return;
            position = a.Row;
            var item = t.Treasure[a.Row];
            // The copy step 0x6F0D runs START 0x1758 before the carry test and after the copy, then the price is paid
            // with no recalculation: the weight keeps the old coins (DOSBox s11: until the items screen is drawn again).
            switch (ShopRules.Buy(c, t.Pool, item, Carrying.StrengthAllowance(c), t.Recalculate))
            {
                case ShopRules.BuyResult.NotEnoughMoney:
                    RowMessageOver(t, page, t.Text[Msg.ShopNoMoney]); // 0x7136
                    break;
                case ShopRules.BuyResult.CannotCarry:
                    RowMessageOver(t, page, t.Text[Msg.ShopOverloaded]); // the copy step's refusal, 0x6F39
                    break;
            }
        }
    }

    /// <summary>
    /// The item page of the shop's Buy (0x6D04) and the loot's item take (0x6092), P + D s46: the window (1, 1)–(38, 22)
    /// with the title centred on row 1 in white (START 03F1:02E8), then the list routine 0x2D669 with the window
    /// (1, 4)–(38, 22) (19 rows, paged by Next / Prev through the shared top <see cref="MenuHighlight.ListTop"/>),
    /// colours 13 / 10 / 15, the prompt "Items: " and the caller's word, then Next / Prev and Exit (flag 1), from the
    /// position word <paramref name="position"/> as the caller keeps it. No member switch: a key other than the word's
    /// letter or Return leaves the list (0x706D, 0x61BC). The Amiga passes the same window and positions (hunk 22
    /// +0x5418–0x545E, hunk 19 +0x7F0–0x86C).
    /// </summary>
    public static ListRequest ItemPage(TradeContext t, string title, string word, IReadOnlyList<string> rows, int position) =>
        new([], rows, [word, "EXIT"], 1, FirstRow: ItemPageFirstRow)
        {
            Selected = position,
            RowActions = new HashSet<int> { 0 },
            Paging = new ListPaging(t.ListWords.Next, t.ListWords.Prev, ItemPageRows, 1),
            Spell = new SpellPageLayout("", 15, AmigaView.Case(title.Trim()))
            {
                CentredTitle = true,
                Prompt = AmigaView.Case(t.Text[Msg.ItemsHeader].TrimEnd()) + " ",
            },
        };

    /// <summary>
    /// A refusal on a list page (START 0x2233 through 00BA:1693): the text on row 24 in green over the page, its rows
    /// unlit as the list routine left them, for the game-speed pause, then the page again (DOSBox-X s46: Buy of a cloak
    /// without the money, "NOT ENOUGH MONEY." under the shop's rows).
    /// </summary>
    public static void RowMessageOver(TradeContext t, ListRequest page, string text)
    {
        t.ShowPageAlone(page);
        CharacterView.RowMessage(t, text);
        t.ShowPageAlone(null);
    }

    /// <summary>The item page's list window (1, 4)–(38, 22): its first row and its rows.</summary>
    public const int ItemPageFirstRow = 4, ItemPageRows = 19;

    /// <summary>
    /// A stock row (0x6D04): 30 spaces, the value right-aligned in them, then the name copied over from the first
    /// character and the length set to 30, so a name of 30 characters or more hides the value (DOSBox-X s46: the
    /// cleric scroll's 29 letters and the naming routine's closing space cover it, the row shows the name alone).
    /// </summary>
    public static string PriceRow(string name, byte[] item)
    {
        string v = ((ushort)(item[0x3A] | item[0x3B] << 8)).ToString(CultureInfo.InvariantCulture);
        var row = v.PadLeft(PriceField).ToCharArray();
        for (int i = 0; i < name.Length && i < PriceField; i++)
            row[i] = name[i];
        return new string(row);
    }

    private const int PriceField = 30;

    // ---- Temple (0x5280) -------------------------------------------------------------------------------------------

    /// <summary>
    /// The temple: the shop's frame (pool cleared), menu Heal, View, Take, Pool, Share, Appraise, Exit (Heal, View,
    /// Pool, Appraise, Exit with no money in the pool); Heal opens the heal list (0x4F4E).
    /// </summary>
    public static void Temple(TradeContext t)
    {
        Array.Clear(t.Pool);
        while (true)
        {
            t.RosterDrawn(); // 0x5298 / 0x5497: the party list on entry and after every pass of the menu (a pick too)
            bool money = t.PoolHasMoney;
            string[] menu = money
                ? ["HEAL", "VIEW", "TAKE", "POOL", "SHARE", "APPRAISE", "EXIT"]
                : ["HEAL", "VIEW", "POOL", "APPRAISE", "EXIT"];
            int i = t.Dialog.Menu(menu, cancel: menu.Length - 1, partySelect: true, memberLetters: MemberLetters);
            if (t.SelectMember(i))
                continue;
            switch (menu[Math.Clamp(i, 0, menu.Length - 1)])
            {
                case "HEAL": HealList(t); t.VenueRedrawn(); break; // the heal list's end (0x51A0)
                case "VIEW": CharacterView.Run(t, Venue.Shop); t.VenueRedrawn(); break; // View's exit
                case "TAKE": MoneyScreens.Take(t); t.VenueRedrawn(); break; // 0x5479
                case "POOL": MoneyScreens.Pool(t); break;
                case "SHARE": MoneyScreens.Share(t); break;
                case "APPRAISE": Appraise(t); break;
                default:
                    if (!MoneyScreens.StayForMoney(t, Msg.TempleMoneyLeft, Msg.TempleGoBack))
                    {
                        t.RosterDrawn(); // the pass's list comes before the exit test
                        return;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Appraise from the shop or temple menu: 0x2663B sets its flag to 0 only when the current character has no gems and
    /// no jewellery at the start (the message alone); with the flag set the loop draws its screen again (0x744D, 0x548A).
    /// </summary>
    private static void Appraise(TradeContext t)
    {
        bool any = t.Current is { } c && (c.Word(Money.Gems) != 0 || c.Word(Money.Jewellery) != 0);
        MoneyScreens.Appraise(t);
        if (any)
            t.VenueRedrawn();
    }

    /// <summary>Our names for the heal list when the program's table is not found.</summary>
    private static readonly string[] OurCures =
        ["BLINDNESS", "SICKNESS", "LIGHT WOUNDS", "SERIOUS WOUNDS", "CRITICAL WOUNDS", "POISON", "RAISE", "CURSE",
         "STONE", "LEAVE"];

    private static Msg? Absent(TempleRules.Cure cure) => cure switch
    {
        TempleRules.Cure.Blindness => Msg.NotBlind,
        TempleRules.Cure.Disease => Msg.NotDiseased,
        TempleRules.Cure.Poison => Msg.NotPoisoned,
        TempleRules.Cure.RaiseDead => Msg.NotDead,
        TempleRules.Cure.RemoveCurse => Msg.NotCursed,
        TempleRules.Cure.StoneToFlesh => Msg.NotStoned,
        _ => null,
    };

    /// <summary>
    /// The heal list (0x4F4E): ten names, the 10th leaves; each entry works on the current character. Absent
    /// condition → "cure anyway?" (0x458A); then the price question (0x4636) and payment (character, else pool, else
    /// "not enough money"); after payment the "cured" text and the effect (<see cref="TempleRules.Apply"/>).
    /// The screen (P + D s46): row 24 blanked, the window (1, 1)–(38, 22) and the title window (1, 1)–(38, 1) once
    /// (START 03F1:02E8, frames one cell outside); each pass the title (the name and the question) at (1, 1) in white,
    /// the text box (1, 17)–(38, 22) blanked, and the list routine 0x2D669 (0x50E7) with the window (2, 4)–(38, 15),
    /// colours 13 / 10 / 15, the word "Heal" and Exit: the menu line "HEAL EXIT" from column 0, the highlighted row
    /// white, the others green, no cursor. H or Return runs the highlighted row whatever word is lit (Return is 13: with
    /// Exit lit it still asked for the row's cure), the 10th row leaves, and so do Escape and E; the entry's texts and
    /// questions come in the text box with the list left above it, every row unlit; at the end the screen is drawn
    /// again (START 0x27D9: the text box empty) (DOSBox-X s46). `[?]` The Amiga's heal list layout and its pay
    /// question's pens are not seen in FS-UAE: the Client draws the DOS layout under either art.
    /// The position word [bp−0xC] is not stored before the first call: it is the stack word that the temple menu's call
    /// of the menu routine left there (its 0 argument, 0x5314, pushed as AX whose AH is 0 from `mov ax, 0x28` at 0x52D6 /
    /// 0x52EB through the two string calls), so 0 on every entry (DOSBox-X s46, residue probe: 0 after a fresh start,
    /// after leaving the list on row 3 and after View). The list therefore starts lit on its first row and takes the
    /// shared window top to 0 (<see cref="MenuHighlight.ListEntered"/>, done by the page's host); later passes keep the
    /// row the list returned (DOSBox-X: Cure Light Wounds still lit after its price question was refused).
    /// </summary>
    public static void HealList(TradeContext t)
    {
        var names = (t.TempleNames ?? OurCures).Select(n => AmigaView.Case(n)).ToList();
        int position = 0; // [bp−0xC]: 0 on entry (see above), then the row the list returns
        while (t.Current is { } c)
        {
            string who = AmigaView.Case(c.Name);
            var page = HealPage(t, names, who, position);
            var a = t.Dialog.Page(page);
            if (a.Action != 0 || a.Row < 0 || a.Row >= 9)
            {
                t.Dialog.Text.Clear(); // 0x51A0: START 0x27D9 draws the screen again, the text box empty
                return;
            }
            int i = position = a.Row;
            t.ShowPageUnder(page); // the list stays above the text box while the entry runs (rows unlit)
            try
            {
                Heal(t, c, names, who, i);
            }
            finally
            {
                t.ShowPageUnder(null);
            }
        }
    }

    /// <summary>The heal list's window (2, 4)–(38, 15): its first row and its rows.</summary>
    public const int HealFirstRow = 4, HealRows = 12;

    /// <summary>The pay question's word colours (0x4636 → 0x3C5:0x39 at 0x46CE with 13, 15, 10: words white, lit and hotkeys green).</summary>
    public static readonly (byte Words, byte Lit) PayColours = (15, 10);

    /// <summary>One pass of the heal list's page (<see cref="HealList"/>).</summary>
    public static ListRequest HealPage(TradeContext t, IReadOnlyList<string> names, string who, int position) =>
        new([], names, ["HEAL", "EXIT"], 1, FirstRow: HealFirstRow)
        {
            Selected = position,
            RowActions = new HashSet<int> { 0 },
            ReturnRunsFirstAction = true, // 0x50F2: 'H' or 13 runs the row
            // Ten rows in a twelve-row window whose top is 0: Next / Prev never show (0x2D7E6 / 0x2D854).
            Paging = new ListPaging("NEXT", "PREV", HealRows, 1),
            Spell = new SpellPageLayout("", 15, who + t.Text[Msg.HowCanWeHelp])
            {
                WholeTitle = true,
                RowColumn = 2,
                MenuColumn = 0,
                Box = [],
            },
        };

    private static void Heal(TradeContext t, EclCharacter c, List<string> names, string who, int i)
    {
        var cure = (TempleRules.Cure)i;
        var d = t.Dialog;
        if (!TempleRules.Applies(cure, c) && Absent(cure) is { } absent)
        {
            // 0x458A: START 0x16EC(member, text, 0, no pause) outside a fight: (1, 17)–(38, 22) cleared, the name at
            // (1, 18) in its colour, the text word-wrapped from (1, 19) in green (DOSBox-X s46: "THRENDER GRONE" cyan on
            // row 18, "IS NOT BLIND." on row 19), then the question (13, 10, 15).
            var w = d.Text;
            w.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
            w.Put(TextWindow.Top + 1, TextWindow.Left, who, CombatChooser.NameColour(c));
            w.PrintIn(TextWindow.Left, TextWindow.Top + 2, TextWindow.Right, TextWindow.Bottom, AmigaView.Case(t.Text[absent]),
                true, TextWindow.ScriptColour);
            if (!d.YesNo(t.Text[Msg.CureAnyway]))
                return;
        }
        ushort price = TempleRules.Price(cure);
        d.Show(GameText.Cat(names[i] + t.Text[Msg.WillOnlyCost], price) + t.Text[Msg.GoldPieces]);
        if (!d.YesNo(t.Text[Msg.PayForCure], colours: PayColours))
            return;
        if (Money.Pay(c, t.Pool, price, wideCharacterTotal: false) == Money.Payer.None)
        {
            d.Say(t.Text[Msg.TempleNoMoney]);
            return;
        }
        d.Say(who + t.Text[Msg.IsCured]);
        TempleRules.Apply(cure, c, t.Random);
    }

    // ---- After combat (0x6866, 0x5E94, 0x644B) ---------------------------------------------------------------------

    /// <summary>
    /// The loot part of the after-combat step: the NPCs' share window (when any took a share), the summary (0x5E94),
    /// then the loot menu (0x644B).
    /// </summary>
    public static void Loot(TradeContext t, AfterCombatOutcome outcome, bool duel, IReadOnlyList<EclCharacter> npcsTakingShare)
    {
        var d = t.Dialog;
        if (npcsTakingShare.Count > 0)
        {
            // 0x695E (DOSBox-X s22): a full-screen window (START 03F1:02E8 with 1, 1, 38, 22, empty title); each NPC
            // through the text routine in the box (5, 5 + 2k)–(34, 22), colour 10, cleared first; then the press-enter
            // text as a white menu of the menu routine (as after the summary). The text routine (START 0x77F8 at
            // 0x69F0, P s30) types while DS:4A00 is set: the value the fight began with, as 0x6BAF sets it only after
            // the loot step.
            var box = new TextWindow(1, 1, 38, 22);
            t.TypesInto(box);
            int k = 0;
            foreach (var npc in npcsTakingShare)
            {
                box.PrintIn(5, 5 + k, 34, 22, AmigaView.Case(npc.Name) + t.Text[Msg.TakesShare], clear: true, colour: 10);
                k += 2;
            }
            t.ShowWindow(box.WindowLines());
            d.MenuKey(t.Text[Msg.ShareContinue], t.Text.Original(Msg.ShareContinue));
            t.ShowWindow(null);
        }
        Summary(t, outcome, duel);
        LootMenu(t);
    }

    /// <summary>
    /// The summary screen (0x5E94, program read s12, rows s21): a full-screen window (START 03F1:02E8 with 1, 1, 38, 22),
    /// then in colour 10 at column 1: the result on row 3, the experience line (the text and the share) on row 5, the
    /// "experience points" text on row 7; then the press-enter words as a white menu (0x6071). The result: when a
    /// hostile creature that did not flee was in the list (DS:443A) or in a duel (DS:84D4): the duel lost (no winner,
    /// DS:84DA = 0) or won, else the party won; otherwise the party fled (DS:443B) or found treasure (no monsters).
    /// The experience line names the duelist in a duel, else each character. DOSBox-X s21: the slums' kobolds.
    /// Every line is a fixed-cell string (START 0709:0626 = 0x76B6, glyph by glyph with no DS:4A00 test) and the key
    /// is the menu routine's (P s30), so the summary never types.
    /// </summary>
    public static void Summary(TradeContext t, AfterCombatOutcome outcome, bool duel)
    {
        var d = t.Dialog;
        d.Text.Clear();
        Msg result = outcome.HostileFound || duel
            ? duel ? (outcome.Won ? Msg.WonDuel : Msg.LostDuel) : Msg.PartyWon
            : outcome.PartyFled ? Msg.PartyFled : Msg.FoundTreasure;
        t.ShowWindow(
        [
            new WindowLine(1, 3, t.Text[result]),
            new WindowLine(1, 5, GameText.Cat(t.Text[duel ? Msg.DuelistReceives : Msg.EachReceives], outcome.ExperienceShare)),
            new WindowLine(1, 7, t.Text[Msg.ExperiencePoints]),
        ]);
        d.MenuKey(t.Text[Msg.PressEnter], t.Text.Original(Msg.PressEnter));
        t.ShowWindow(null);
        // The loot menu then redraws the screen for DS:49F3 = 6 (START 0x3379): the text window starts empty.
        d.Text.Clear();
    }

    /// <summary>
    /// The loot menu (0x644B), DS:49F3 = 6. With money in the pool: View, Take, Pool, Share; with items only: View,
    /// Take, Pool; then " Detect Exit" when there are items and the current character has spell 5 or 11 memorised
    /// (bytes 0x17–0x2B), else " Exit"; nothing left: View, Pool, Exit. Exit with treasure left asks to go back.
    /// </summary>
    public static void LootMenu(TradeContext t)
    {
        t.VenueRedrawn(); // 0x644B: START 0x3379 case 6 loads the chest (PIC block 1) into the frame table
        while (true)
        {
            bool money = t.PoolHasMoney, items = t.Treasure.Count > 0;
            bool detect = items && t.Current is { } c && Enumerable.Range(0, 21).Any(k => c[0x17 + k] is 5 or 11);
            var menu = new List<string>();
            if (money) menu.AddRange(["VIEW", "TAKE", "POOL", "SHARE"]);
            else if (items) menu.AddRange(["VIEW", "TAKE", "POOL"]);
            else menu.AddRange(["VIEW", "POOL"]);
            if (detect) menu.Add("DETECT");
            menu.Add("EXIT");
            // 0x659D passes the animate flag 1: the loot picture (PIC 1) animates while the menu waits.
            int i = t.Dialog.Menu(menu, cancel: menu.Count - 1, partySelect: true, memberLetters: MemberLetters, animates: true);
            if (t.SelectMember(i))
                continue;
            switch (menu[Math.Clamp(i, 0, menu.Count - 1)])
            {
                case "VIEW": CharacterView.Run(t, Venue.Loot); break;
                case "TAKE": LootTake(t); break;
                case "POOL": MoneyScreens.Pool(t); break;
                case "SHARE": MoneyScreens.Share(t); break;
                case "DETECT":
                    // TODO: the detect magic cast (0x27C38) belongs to the spells, not built yet.
                    t.Dialog.Say("DETECT: NOT IN THIS BUILD YET.", colour: 14);
                    break;
                default:
                    if (money || items)
                    {
                        // 0x664A–0x66B3 (s21): the first text in colour 10 with a clear, the second in 15 running on
                        // after it (the text routine 0x709:0x768, no line break), then "Yes No" through the menu helper
                        // 0x8B57, which opens on the shared highlight (not the yes/no routine; DOSBox-X: on Yes after
                        // Exit). Answer 1 (Yes) goes back.
                        t.Dialog.Show(t.Text[Msg.TreasureLeft]);
                        t.Dialog.Show(t.Text[Msg.ClaimTreasure], clear: false, colour: 15);
                        if (t.Dialog.Menu(["YES", "NO"], cancel: 1) == 0)
                            break;
                    }
                    return;
            }
        }
    }

    /// <summary>
    /// Take (0x62B6): with money and items a "Take:" menu (Money, Items, Exit) until Exit or nothing is left; money
    /// only → the coin take; items only → the item take.
    /// </summary>
    public static void LootTake(TradeContext t)
    {
        while (true)
        {
            bool money = t.PoolHasMoney, items = t.Treasure.Count > 0;
            if (money && items)
            {
                int i = t.Dialog.Menu(["MONEY", "ITEMS", "EXIT"], t.Text[Msg.TakePrompt], cancel: 2, animates: true); // 0x630A: flag 1
                if (i == 0) MoneyScreens.Take(t);
                else if (i == 1) TakeItems(t);
                else return;
            }
            else
            {
                if (money) MoneyScreens.Take(t);
                else if (items) TakeItems(t);
                return;
            }
        }
    }

    /// <summary>
    /// The item take (0x6184): the treasure list on the item page (<see cref="ItemPage"/>, title "Loot"); T or Enter
    /// gives the highlighted item to the current character through the copy step (refused: overloaded) and frees it
    /// from the list; any other key leaves. The position word starts at 0 (0x619F) and keeps the row the list routine
    /// leaves (D s46: after a take on the second page the window stays at its top, one row shorter).
    /// </summary>
    public static void TakeItems(TradeContext t)
    {
        int position = 0;
        while (t.Current is { } c && t.Treasure.Count > 0)
        {
            var rows = t.Treasure.Select(t.ItemName).ToList();
            var page = ItemPage(t, t.Text[Msg.LootTitle], "TAKE", rows, position);
            var a = t.Dialog.Page(page);
            if (a.Action != 0 || a.Row < 0 || a.Row >= t.Treasure.Count)
                return;
            position = a.Row;
            var item = t.Treasure[a.Row];
            // 0x6184 -> the copy step 0x6F0D: START 0x1758 before the carry test and after the copy.
            if (!ShopRules.GiveCopy(c, item, Carrying.StrengthAllowance(c), t.Recalculate))
            {
                RowMessageOver(t, page, t.Text[Msg.ShopOverloaded]); // the copy step's refusal, 0x6F39
                continue;
            }
            t.Treasure.RemoveAt(a.Row);
        }
    }

    // ---- Camp (0x196A0) --------------------------------------------------------------------------------------------

    /// <summary>
    /// The camp menu (DS:49F3 = 2): Save, View, Magic, Rest, Alter, Exit from the program's line (<see cref="CampScreens"/>).
    /// Returns whether a rest was interrupted (the area loop then runs vector 3).
    /// </summary>
    public static bool Camp(CampContext c, string? menuLine) => CampScreens.Camp(c, menuLine);
}
