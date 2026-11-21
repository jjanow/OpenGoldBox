using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>
/// A character screen a fight opens over the whole frame: View (GAME.OVR 0x21AED), Items (0x21F32, also the menu's
/// Use) and the money drop (0x234E1). Fixed texts, an optional list of rows, the menu on row 24
/// (<see cref="PageRequest"/>). The logic thread fills it; the combat screen draws it while it is set.
/// </summary>
public sealed class CombatPage
{
    /// <summary>Fixed texts: column, row, text, EGA colour.</summary>
    public List<(int Col, int Row, string Text, byte Colour)> Texts { get; } = [];
    /// <summary>The braid's horizontal lines (rows); columns 0 and 39 always. DOSBox-X: Items 0, 2, 23; View 0, 23.</summary>
    public IReadOnlyList<int> FrameRows { get; init; } = [0, 23];
    /// <summary>
    /// The combat palette (EGA 0 and 8 swapped) stays on: the Items screen opened by the menu's Use is dark grey; View
    /// restores the normal palette on entry (0x21AFB → 0x3083F) and swaps again on exit (0x21D4A → 0x3081D), so View
    /// and its Items are black.
    /// </summary>
    public bool Grey { get; init; }
    /// <summary>The rows to pick from (none on the View page).</summary>
    public IReadOnlyList<string> Rows { get; init; } = [];
    public int RowCol { get; init; } = 1;
    public int FirstRow { get; init; } = 5;
    public int LastRow { get; init; } = 22;
    public int Selected { get; set; }
    /// <summary>The selected row is drawn white; false while a question is up (DOSBox-X: every row green then).</summary>
    public bool ShowSelection { get; set; } = true;
    /// <summary>The row for the Client's pad / mouse hint and number buttons (not the program's): a row the page leaves empty.</summary>
    public int HintRow { get; init; } = 22;
    /// <summary>Lines drawn in yellow from row 21 (the drop warning, 0x22401).</summary>
    public List<string> Warning { get; } = [];
}

/// <summary>The Items page of 0x21F32 (a fight's Use and every View's Items: the party menu, hall, camp, shop, temple,
/// loot) and its words.</summary>
public static class ItemsPages
{
    /// <summary>The Items menu's words (0x21F32's strings, unit 0x2103A).</summary>
    public static string Word(CombatText text, ItemsEntry e) => e switch
    {
        ItemsEntry.Ready => text[CombatMsg.ItemsReady].Trim(),
        ItemsEntry.Use => text[CombatMsg.ItemsUse].Trim(),
        ItemsEntry.Trade => text[CombatMsg.ItemsTrade].Trim(),
        ItemsEntry.Drop => text[CombatMsg.ItemsDrop].Trim(),
        ItemsEntry.Halve => text[CombatMsg.ItemsHalve].Trim(),
        ItemsEntry.Join => text[CombatMsg.ItemsJoin].Trim(),
        ItemsEntry.Sell => text[CombatMsg.ItemsSell].Trim(),
        ItemsEntry.Id => text[CombatMsg.ItemsId].Trim(),
        // The list routine 0x2D669 appends " Exit" (CS:0D94 of its unit, GAME.OVR 0x2D663) when its flag [bp+0x12] is
        // set, as the Items call (0x222B7) and the coin list (0x2369D) set it; the same word as View's.
        _ => text[CombatMsg.ViewExit].Trim(),
    };

    /// <summary>
    /// The Items page (0x221DE–0x2229B), measured on DOSBox-X (s15, Darkstar's darts; s39 the camp's): braid on rows 0,
    /// 2 and 23 and columns 0 and 39; row 1 the name with "'S" in its colour (START 0x23B6) and the title (CS:0EB2)
    /// green at column (name length + 4) (0x2220B); row 3 the heading (CS:0EB8) white; the list window (1,5)–(38,22)
    /// (0x2229B): per item the readied word at column 2 and the item's name at column 7, the selected row white, the
    /// others green. [?] The readied column's words are taken from the yes/no menu's; the "'S" comes from the name
    /// routine START 0x1816 (not read).
    /// </summary>
    public static CombatPage Build(CombatText text, EclCharacter c, IReadOnlyList<byte[]> items, Func<byte[], string> itemName,
        int selected, bool grey)
    {
        var yn = text.Words(CombatMsg.YesNo);
        string yes = yn.ElementAtOrDefault(0) ?? "YES", no = yn.ElementAtOrDefault(1) ?? "NO";
        var rows = items.Select(i => " " + (i[CombatRules.ItemEquipped] != 0 ? yes : no).PadRight(5) + itemName(i)).ToList();
        var page = new CombatPage
        {
            FrameRows = [0, 2, 23],
            Grey = grey,
            Rows = rows,
            RowCol = 1,
            FirstRow = 5,
            LastRow = 22,
            Selected = Math.Clamp(selected, 0, Math.Max(0, rows.Count - 1)),
        };
        string name = c.Name.ToUpperInvariant();
        page.Texts.Add((1, 1, name + "'S", CombatSheet.NameColour(c)));
        page.Texts.Add((name.Length + 4, 1, text[CombatMsg.ItemsTitle].Trim(), 10));
        page.Texts.Add((1, 3, text[CombatMsg.ItemsHeading].Trim(), 15));
        return page;
    }
}

/// <summary>
/// The menu of a <see cref="CombatPage"/> on row 24: the prompt (colour 13, may be null) and the entries, the program's
/// menu routine style (highlighted entry white, the others with a white hotkey). The answer is a
/// <see cref="ListAnswer"/>: the entry and the selected row; <paramref name="CancelEntry"/> on Escape / B / right click.
/// </summary>
public sealed record PageRequest(IReadOnlyList<string> Entries, string? Prompt, int CancelEntry) : PlayerRequest
{
    /// <summary>The answer's action when a row itself was picked (<see cref="PickRows"/>).</summary>
    public const int RowPicked = -10;

    /// <summary>Return / A / a click on the selected row picks the row (the coin list), not the highlighted entry.</summary>
    public bool PickRows { get; init; }

    /// <summary>
    /// The menu is the program's menu routine 0x2CC8C called directly (View's): it opens on the shared cell
    /// (<see cref="MenuHighlight"/>). Otherwise the page is the item list routine 0x2D669 (the Items page, the coin
    /// list), which stores 1 in the cell on entry (0x2D6B9), so it opens on its first entry. Both leave the cell where
    /// their menu ends (0x2D669 runs its menu through 0x2CC8C).
    /// </summary>
    public bool SharedHighlight { get; init; }
}

public sealed partial class CombatChooser
{
    /// <summary>The program's texts and item words (the Client's resources; our wordings until loaded).</summary>
    public Func<ClientResources> Resources { get; set; } = () => new ClientResources();

    /// <summary>The menu whose View / Use is open (the aim of an item's spell goes through it).</summary>
    private FightMenu? _menu;
    /// <summary>Items was opened from View (black page), not from the menu's Use (grey).</summary>
    private bool _fromView;
    /// <summary>The Items list's row (DOSBox-X: it stays where it was between choices).</summary>
    private int _itemRow;
    /// <summary>The cast already announced by the spell list (skipped once by <see cref="Announce"/>).</summary>
    private (EclCharacter Caster, int Spell)? _announced;

    private bool Detect => View.Fight?.List.Any(m => m[FightRules.Hostile] == 0 && m.HasEffect(5)) ?? false;

    private string ItemName(byte[] item) =>
        ItemNames.Compose(item, Resources().ItemWord, Detect).Trim().ToUpperInvariant();

    private string SpellLabel(int spell) =>
        (SpellName(spell) ?? "SPELL " + spell.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant();

    /// <summary>
    /// The memorised list (0x239D6 kind 0, as the camp's: camp.md "Spell lists"): each slot 0x17–0x2B with a byte
    /// 1..0x7F in slot order, every one (camp-only spells too: 0x14C0F refuses those after the pick), a level heading
    /// (a label) before a spell whose level differs from the one before. Ids: 0 on a heading.
    /// </summary>
    private (List<string> Rows, List<int> Ids, HashSet<int> Labels) MemorisedList(EclCharacter c, Fight? fight)
    {
        var table = (fight?.Spells as FightSpells)?.Table;
        var rows = new List<string>();
        var ids = new List<int>();
        var labels = new HashSet<int>();
        int last = 0;
        for (int i = 0; i < FightItemScreens.SpellSlots; i++)
        {
            int s = c[FightItemScreens.Spells + i];
            if (s is 0 or >= 0x80)
                continue;
            int level = table is { } t && s <= 67 ? t[s].Level : 0;
            if (level != last)
            {
                labels.Add(rows.Count);
                rows.Add((Resources().LevelName(level) ?? "LEVEL " + level.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant());
                ids.Add(0);
                last = level;
            }
            rows.Add("  " + SpellLabel(s));
            ids.Add(s);
        }
        return (rows, ids, labels);
    }

    /// <summary>The spell list's paging (0x270A2: rows 5–22) for a menu line of <paramref name="actions"/> ending in Exit.</summary>
    private ListPaging SpellPaging(int actions) => SpellLists.Paging(Text, memorize: false, wordsAt: actions - 1);

    // ---- Messages of the fight (display hooks) ----------------------------------------------------------------------

    /// <summary>
    /// The cast message (0x27D48 → 0x2A332, read s18, s19): in a fight, START 0x16EC writes the caster and the "casts a
    /// spell" words into the panel from row 10 with its pause (pause flag 1 at 0x2A36B) and clears them; then row 23 is
    /// cleared and "Spell:" with the spell's name is written there in green (it stays while the spell's results are
    /// shown; DOSBox-X s18: the AI's Hold Person and Magic Missile).
    /// </summary>
    private void SayCast(EclCharacter caster, int spell)
    {
        View.StatusLine = null;
        Say(caster, Text[CombatMsg.CastsASpell]);
        View.StatusLine = Text[CombatMsg.SpellLabel].Trim() + SpellLabel(spell);
    }

    /// <summary><see cref="FightSpells.Announce"/>: every cast not from an item (the AI's included).</summary>
    private void Announce(EclCharacter caster, int spell)
    {
        if (_announced is { } a && ReferenceEquals(a.Caster, caster) && a.Spell == spell)
        {
            _announced = null;
            return;
        }
        SayCast(caster, spell);
    }

    /// <summary>
    /// <see cref="FightSpells.ItemUsed"/> (0x22B58–0x22BF2 in a fight): the panel message (START 0x16EC from row 10:
    /// the user and the uses-an-item words), row 23 "Item:" in green with the item's name at column 5, then the pause.
    /// [?] How START 0x16EC breaks the line is not read: name, then the words.
    /// </summary>
    private void ItemUsed(EclCharacter user, byte[] item)
    {
        View.StatusLine = Text[CombatMsg.ItemLabel] + ItemName(item);
        Say(user, Text[CombatMsg.UsesAnItem]);
        View.StatusLine = null;
    }

    /// <summary>The AI's cast begins, or a spell routine shows a direct or animated creature message.</summary>
    private void SpellEvent(EclCharacter who, int spell, EffectEvent e)
    {
        if (e == EffectEvent.BeginsCasting)
        {
            Say(who, Text[CombatMsg.BeginsCasting]);
            return;
        }
        if (e == EffectEvent.Healed && spell == 3)
        {
            Say(who, HealedWords(who)); // Cure Light Wounds 0x28083 → START 0x2EEA (0x280B7): no list in a fight
            return;
        }
        if (CombatText.SpellMessage(e) is not { } message)
            return;
        string text = Text[message.Text];
        if (message.Animated)
            ShowHit(who, text, effect: true);
        else if (e == EffectEvent.SpellCloudFormed && View.Fight?.Spells is FightSpells spells)
        {
            View.Page = null;
            View.Mode = CombatMode.Watch;
            PrintCreatureMessage(who, text, CombatView.MessageTop, false);
            View.Scroll(spells.Aim.X, spells.Aim.Y, 255);
            Pause(MessageMs);
            ClearMessageWindow();
        }
        else
            Say(who, text);
    }

    /// <summary>
    /// The monster AI's messages (0xABC0–0xBC8C). The key check's (0xBB81): Alt-M's (0xBBD7 / 0xBBED) and Alt-X's
    /// (0x1638E) go through START 0x1693 on row 24. The others are START 0x16EC(creature, text, row 10, pause 1): the
    /// name, the text typed from row 11, the game-speed pause, rows 10–21 cleared: the panic message (0xACC1) and the
    /// forced flee (0xBCF0). The surrender goes through the status change 0x2BDF0, which first scrolls the view to the
    /// creature with delta 3 and no cursor box (0x319D2(creature, 3, 0) at 0x2BE31), then shows the same message.
    /// </summary>
    internal void AiSays(EclCharacter who, AiMessage m)
    {
        if (m is AiMessage.QuickMagicOn or AiMessage.QuickMagicOff or AiMessage.GodsIntervene)
        {
            PageSay(Text[m switch
            {
                AiMessage.QuickMagicOn => CombatMsg.AiMagicOn,
                AiMessage.QuickMagicOff => CombatMsg.AiMagicOff,
                _ => CombatMsg.GodsIntervene,
            }]);
            return;
        }
        var word = m switch
        {
            AiMessage.FleesInPanic => CombatMsg.FleesInPanic,
            AiMessage.GodsIntervene => CombatMsg.GodsIntervene,
            AiMessage.ForcedToFlee => CombatMsg.ForcedToFlee,
            AiMessage.Surrenders => CombatMsg.Surrenders,
            AiMessage.QuickMagicOn => CombatMsg.AiMagicOn,
            _ => CombatMsg.AiMagicOff,
        };
        if (m == AiMessage.Surrenders)
            StatusChangeScroll(who);
        Say(who, Text[word]);
    }

    /// <summary>0x2BDF0's first step (0x2BE31): 0x319D2(creature, 3, 0), ScrollView on its square, delta 3, cursor off.</summary>
    private void StatusChangeScroll(EclCharacter who)
    {
        if (Square(who) is { } p)
            View.Scroll(p.X, p.Y, 3);
    }

    // ---- IItemScreenChooser: View (0x21AED) -------------------------------------------------------------------------

    /// <summary>
    /// The View page (0x21084, <see cref="CombatSheet"/>): braid on rows 0 and 23, black, the program's layout, and the
    /// menu "View:" with the program's entries on row 24.
    /// </summary>
    private CombatPage ViewPage(EclCharacter c)
    {
        // The sheet leaves rows 13–14, 16 and 19 empty: the Client's hint goes on 19.
        var page = new CombatPage { FrameRows = [0, 23], HintRow = 19 };
        byte[]? weapon = null, armour = null;
        if (View.Fight is { } fight)
        {
            var eq = FightRules.Equipment(c, fight.Items);
            weapon = eq[FightRules.SlotWeapon];
            armour = eq[FightRules.SlotBodyArmour];
        }
        page.Texts.AddRange(CombatSheet.Build(Resources(), c, weapon, armour, ItemName));
        return page;
    }

    private string ViewWord(ViewEntry e) => Text[e switch
    {
        ViewEntry.Items => CombatMsg.ViewItems,
        ViewEntry.Spells => CombatMsg.ViewSpells,
        ViewEntry.Trade => CombatMsg.ViewTrade,
        ViewEntry.Drop => CombatMsg.ViewDrop,
        _ => CombatMsg.ViewExit,
    }].Trim();

    /// <summary>
    /// The View page drawn last and the choice made on it: after Drop the View loop does not draw the page again, it
    /// clears and rewrites the money lines only (0x21547: the box (12, 7)–(26, 14)), so the other texts keep the values
    /// they had (FightItemScreens.RedrawsViewPage).
    /// </summary>
    private (EclCharacter C, CombatPage Page, ViewEntry Pick)? _lastView;

    /// <summary>The money box 0x21547 clears before it writes the money lines (columns 12–26, rows 7–14).</summary>
    private static bool InMoneyBox(int col, int row) => col is >= 12 and <= 26 && row is >= 7 and <= 14;

    /// <summary>The View page after a choice that does not redraw it: the old texts, the money lines new.</summary>
    private CombatPage MoneyRedrawn(EclCharacter c, CombatPage old)
    {
        var page = new CombatPage { FrameRows = old.FrameRows, HintRow = old.HintRow };
        page.Texts.AddRange(old.Texts.Where(t => !InMoneyBox(t.Col, t.Row)));
        page.Texts.AddRange(ViewPage(c).Texts.Where(t => InMoneyBox(t.Col, t.Row)));
        return page;
    }

    public ViewEntry ChooseView(EclCharacter c, IReadOnlyList<ViewEntry> entries)
    {
        _fromView = true;
        _itemRow = 0;
        bool redraw = !(_lastView is { } last && ReferenceEquals(last.C, c) && !FightItemScreens.RedrawsViewPage(last.Pick));
        while (true)
        {
            View.Page = redraw || _lastView is not { } prev ? ViewPage(c) : prev.Pick == ViewEntry.Drop ? MoneyRedrawn(c, prev.Page) : prev.Page;
            redraw = true;
            View.Mode = CombatMode.Page;
            int exit = entries.Count - 1;
            var a = Ask(new PageRequest([.. entries.Select(ViewWord)], Text[CombatMsg.ViewPrompt].Trim(), exit) { SharedHighlight = true }) as ListAnswer;
            var pick = a is { Action: >= 0 } && a.Action < entries.Count ? entries[a.Action] : ViewEntry.Exit;
            _lastView = pick == ViewEntry.Exit ? null : (c, View.Page!, pick);
            if (pick != ViewEntry.Spells)
                return pick;
            // Spells (0x239D6 in show mode): the memorised list; nothing changes, the View menu comes back. S is a
            // redraw key: the page is drawn again, with its recalculation (0x21642 → START 0x1758).
            ShowMemorised(c);
            View.Fight?.RecalculateStats(c);
        }
    }

    /// <summary>
    /// The memorised spells (0x239D6 kind 0, action 0: no prompt, no action word) as a list page: the memorised list
    /// in the list routine's window with Next / Prev and Exit (P, s30), on the list's own screen (no prompt: the menu
    /// line starts with " Next" / " Exit" at column 1; D s33).
    /// </summary>
    private void ShowMemorised(EclCharacter c)
    {
        var (rows, _, labels) = MemorisedList(c, View.Fight);
        View.Page = null;
        View.Mode = CombatMode.Spell;
        Ask(new ListRequest([new ListLine(Name(c), NameColour(c))], rows, [ViewWord(ViewEntry.Exit)], 0, FirstRow: SpellLists.FirstRow)
        {
            Labels = labels,
            Paging = SpellPaging(1),
            Spell = SpellLists.Layout(c, Resources().Texts, Msg.InMemory, prompt: false),
        });
    }

    // ---- Items (0x21F32) --------------------------------------------------------------------------------------------

    private string ItemsWord(ItemsEntry e) => ItemsPages.Word(Text, e);

    private CombatPage ItemsPage(EclCharacter c, IReadOnlyList<byte[]> items) =>
        ItemsPages.Build(Text, c, items, ItemName, _itemRow, grey: !_fromView);

    public ItemChoice ChooseItem(EclCharacter c, IReadOnlyList<ItemsEntry> entries, IReadOnlyList<byte[]> items)
    {
        var page = ItemsPage(c, items);
        View.Page = page;
        View.Mode = CombatMode.Page;
        int exit = entries.Count - 1;
        var a = Ask(new PageRequest([.. entries.Select(ItemsWord)], null, exit)) as ListAnswer;
        if (a is null || a.Action < 0 || a.Action >= entries.Count)
            return new ItemChoice(ItemsEntry.Exit, null);
        _itemRow = a.Row;
        var item = a.Row >= 0 && a.Row < items.Count ? items[a.Row] : null;
        return new ItemChoice(entries[a.Action], item);
    }

    /// <summary>
    /// "Drop it?" (0x223C6–0x22419): the warning naming the item (lost for good) in yellow on row 21, the question on
    /// row 24 (DOSBox-X: No highlighted, every list row green while it is up).
    /// </summary>
    public bool ConfirmDrop(EclCharacter c, byte[] item)
    {
        var page = View.Page;
        if (page is not null)
        {
            page.Warning.Clear();
            page.Warning.AddRange(WrapWarning(Text[CombatMsg.Your] + ItemName(item) + " " + Text[CombatMsg.GoneForever].Trim()));
            page.ShowSelection = false;
        }
        bool yes = AskYesNo(Text[CombatMsg.DropIt], 1);
        if (page is not null)
        {
            page.Warning.Clear();
            page.ShowSelection = true;
        }
        return yes;
    }

    /// <summary>
    /// 0x21E70: the scroll was to be scribed from. [?] Where the two lines go on the fight's Items page is not seen:
    /// as the drop warning (yellow from row 21), the question on row 24.
    /// </summary>
    public bool ConfirmLoseScroll(EclCharacter c, byte[] scroll)
    {
        var page = View.Page;
        if (page is not null)
        {
            page.Warning.Clear();
            page.Warning.AddRange(WrapWarning(Name(c) + Text[CombatMsg.ScribeWarning].TrimEnd()));
            page.ShowSelection = false;
        }
        bool yes = AskYesNo(Text[CombatMsg.OkayToLose], 1);
        if (page is not null)
        {
            page.Warning.Clear();
            page.ShowSelection = true;
        }
        return yes;
    }

    /// <summary>
    /// The warning window (1,21)–(38,22) of 0x709:0x768: the text in up to two lines of 38 columns. [?] The routine's
    /// break rule is not read: at the last space that fits.
    /// </summary>
    public static List<string> WrapWarning(string text)
    {
        const int width = 38;
        var lines = new List<string>();
        string rest = text.Trim();
        while (rest.Length > 0 && lines.Count < 2)
        {
            if (rest.Length <= width || lines.Count == 1)
            {
                lines.Add(rest.Length <= width ? rest : rest[..width]);
                break;
            }
            int cut = rest.LastIndexOf(' ', width);
            if (cut <= 0)
                cut = width;
            lines.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        return lines;
    }

    /// <summary>The yes/no menu (0x2D979 words) on row 24 with <paramref name="prompt"/>, the screen staying as it is.</summary>
    private bool AskYesNo(string prompt, int highlighted)
    {
        var words = Text.Words(CombatMsg.YesNo);
        if (words.Count < 2)
            words = ["YES", "NO"];
        var mode = View.Mode;
        var r = Ask(new ChoiceRequest(null, words, false, 1) { MenuPrompt = prompt, Selected = highlighted });
        View.Mode = mode;
        return r is 0;
    }

    /// <summary>
    /// A message of the item screens: the program's message routine (START 0x1693: row 24 cleared, the text in green
    /// at column 0, the pause 0x709:0xDBE, row 24 cleared). [?] That Ready's and Halve's refusals use the same
    /// routine is not read; Trade, Sell and Id are never offered in a fight.
    /// </summary>
    public void Notice(ItemNotice notice, EclCharacter c, byte[]? item)
    {
        string text = notice switch
        {
            ItemNotice.MustBeReadied => Text[CombatMsg.MustBeReadied],
            ItemNotice.MustBeUnreadied => Text[CombatMsg.MustBeUnreadied],
            ItemNotice.CannotHalve => Text[CombatMsg.CannotHalve],
            ItemNotice.Cursed => Text[CombatMsg.Cursed],
            ItemNotice.WrongClass => Text[CombatMsg.WrongClass],
            ItemNotice.SlotTaken => Text[CombatMsg.AlreadyUsing] + (item is null ? "" : ItemName(item)), // [?] what follows the words
            ItemNotice.HandsFull => Text[CombatMsg.HandsFull],
            _ => "NOT IN THIS BUILD YET",
        };
        PageSay(text.Trim());
    }

    /// <summary>START 0x1693 (0x2233): row 24 cleared, the text in green at column 0, the game-speed pause, row 24 cleared.</summary>
    private void PageSay(string text)
    {
        View.MovePrompt = null; // row 24 is cleared, so an AI move prompt there goes
        View.PageMessage = text;
        if (Pauses)
            Ask(new DelayRequest(TimeSpan.FromMilliseconds(MessageMs)));
        View.PageMessage = null;
    }

    // ---- Drop money (0x234E1) ---------------------------------------------------------------------------------------

    /// <summary>
    /// The coin pick (a coin-type prompt in colour 13, then Exit; DOSBox-X: the sheet's money lines are the list, the
    /// picked one white, the others green) and the amount (an amount question naming the coin, in green on row 24,
    /// digits typed after it). The list rows are the page's money lines (<see cref="CombatSheet.MoneyLine"/>: kinds 6
    /// down to 0; names DS:0FFA + 11·kind). [?] The input routine 0x24F1D is not read: an empty or unreadable answer
    /// drops nothing and the coin list comes back.
    /// </summary>
    public (int Kind, ushort Amount)? ChooseCoins(EclCharacter c, IReadOnlyList<int> kinds)
    {
        var r = Resources();
        var page = ViewPage(c);
        var order = CombatSheet.MoneyKinds(c).Where(kinds.Contains).ToList();
        int width = order.Count == 0 ? 0 : order.Max(k => CombatSheet.CoinName(r, k).Length);
        var moneyRows = CombatSheet.MoneyKinds(c).Count;
        var coinPage = new CombatPage
        {
            FrameRows = [0, 23],
            HintRow = 19,
            // Each row: the name right-aligned to end at column 19, a space, the amount from column 21.
            Rows = [.. order.Select(k => CombatSheet.CoinName(r, k).PadLeft(width) + " " +
                                         FightItemScreens.Coins(c, k).ToString(CultureInfo.InvariantCulture))],
            RowCol = CombatSheet.MoneyNameEnd + 1 - width,
            FirstRow = CombatSheet.MoneyFirstRow,
            LastRow = Math.Max(CombatSheet.MoneyFirstRow, CombatSheet.MoneyFirstRow + order.Count - 1),
        };
        // The sheet without its money lines (the list draws them).
        coinPage.Texts.AddRange(page.Texts.Where(t => !(t.Row >= CombatSheet.MoneyFirstRow &&
                                                        t.Row < CombatSheet.MoneyFirstRow + moneyRows && t.Col >= 12)));
        View.Page = coinPage;
        View.Mode = CombatMode.Page;
        var a = Ask(new PageRequest([Text[CombatMsg.ViewExit].Trim()], Text[CombatMsg.SelectCoin].Trim() + " ", 0) { PickRows = true }) as ListAnswer;
        if (a is null || a.Action != PageRequest.RowPicked || a.Row < 0 || a.Row >= order.Count)
            return null;
        int kind = order[a.Row];
        coinPage.Selected = a.Row;
        string prompt = GameText.Cat(GameText.Cat(Text[CombatMsg.HowMuch], CombatSheet.CoinName(r, kind)), Text[CombatMsg.WillYouDrop]).TrimEnd();
        var typed = Ask(new TextRequest(prompt, true, 5)) as string;
        ushort amount = ushort.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (ushort)0;
        return (kind, amount);
    }

    // ---- Use: scrolls and the aim -----------------------------------------------------------------------------------

    /// <summary>
    /// A scroll's spells: 0x22AC4 → 0x239D6 (kind 2, action 1 Cast; P s30, s38): the list of
    /// <see cref="CampSpells.ScrollList"/> (the scroll identifies itself first; a hidden one lists nothing and the list
    /// returns at once), level headings, a byte above 0x7F marked pending, on the list's own screen as the fight's Cast
    /// (title "Spells on Scroll", the choose-spell prompt, Cast and Exit on row 24, the combat palette; D s38).
    /// </summary>
    public int ChooseScrollSpell(EclCharacter c, byte[] scroll)
    {
        if (((View.Fight?.Spells as FightSpells)?.Table ?? Resources().Spells) is not { } table)
            return 0;
        var list = CampSpells.ScrollList(table, c, scroll, Resources().ItemSlot(scroll));
        if (list.Count == 0)
            return 0;
        var rows = new List<string>();
        var ids = new List<int>();
        var labels = new HashSet<int>();
        foreach (var r in list)
        {
            if (r.HeadingLevel is { } l)
            {
                labels.Add(rows.Count);
                rows.Add((Resources().LevelName(l) ?? "LEVEL " + l.ToString(CultureInfo.InvariantCulture)).ToUpperInvariant());
                ids.Add(0);
            }
            rows.Add((r.Pending ? " *" : "  ") + SpellLabel(r.Spell));
            ids.Add(r.Spell);
        }
        var page = View.Page;
        View.Page = null;
        View.Mode = CombatMode.Spell;
        string cast = Word(FightCommand.Cast), exit = Text.Words(CombatMsg.AimCenterExit).ElementAtOrDefault(1) ?? "EXIT";
        var a = Ask(new ListRequest([new ListLine(Name(c), NameColour(c))], rows, [cast, exit], 1, FirstRow: SpellLists.FirstRow)
        {
            RowActions = new HashSet<int> { 0 },
            Labels = labels,
            Paging = SpellPaging(2),
            Spell = SpellLists.Layout(c, Resources().Texts, Msg.OnScroll, prompt: true),
            Grey = true,
        }) as ListAnswer;
        View.Page = page;
        View.Mode = CombatMode.Page;
        return a is { Action: 0 } && a.Row >= 0 && a.Row < ids.Count ? ids[a.Row] : 0;
    }

    /// <summary>
    /// The player's aim for an item's spell (0x14611 with ai = 0 → Aim 0x15D53, attack mode 0): the menu's own spell
    /// aim (<see cref="FightMenu.AimSpell"/>) on the combat map. Null when nothing was picked.
    /// </summary>
    public (IReadOnlyList<EclCharacter> Targets, (int X, int Y)? Square)? AimItem(Fight fight, EclCharacter user, int spell)
    {
        if (_menu is not { } menu || !ReferenceEquals(menu.Creature, user))
            return null;
        View.Page = null;
        View.Mode = CombatMode.Menu;
        var (picked, targets, square) = menu.AimSpell(spell);
        return picked ? (targets, square) : null;
    }
}
