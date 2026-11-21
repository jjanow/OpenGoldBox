using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>What the camp screens work on: the trade context (dialog, texts, party), Core's camp rules and state, the
/// game's memory, and the Client's save and quit.</summary>
public sealed class CampContext
{
    public required TradeContext Trade { get; init; }
    public required CampState State { get; init; }
    public required CampSpells Spells { get; init; }
    public required CampRest Rest { get; init; }
    public required EclMemory Memory { get; init; }
    public CampCast? Cast { get; init; }
    public ClientResources Resources { get; init; } = new();
    /// <summary>Writes save slot A..J; returns null when written, else what went wrong.</summary>
    public Func<char, string?>? Save { get; init; }
    /// <summary>Quit to DOS (closes the Client).</summary>
    public Action? Quit { get; init; }
    /// <summary>The icon editor 0x1D1AC on a character (Alter Icon), or null when the Client has none.</summary>
    public Action<EclCharacter>? EditIcon { get; init; }
    /// <summary>The camp's effect engine (the rest's effect timeout and healing, Cast), whose handler messages the camp
    /// shows (<see cref="OutsideEffectMessages"/>); null in tests that need none.</summary>
    public EffectHandlers? Effects { get; init; }
    /// <summary>The sound cues (CreatureDowned's body cue outside a fight), or null for none.</summary>
    public Action<GoldBox.Core.Sound.SoundCue>? Cue { get; init; }
    /// <summary>
    /// START 0x3379 for the interface state when Cast runs outside the camp (the command line's C, DS:49F3 = 3 or 4):
    /// its redraws (0x17D96, the target pick's 0x27AFA) draw the exploring screen, not the camp's. Null in the camp.
    /// </summary>
    public Action? InterfaceRedraw { get; init; }

    /// <summary>The handler messages outside a fight on the camp's text window; <paramref name="resting"/> is DS:6DDA.</summary>
    public OutsideEffectMessages OutsideMessages(bool resting) =>
        new(Dialog.Text, Dialog.Ask, () => Memory.GameSpeed, Resources.CombatTexts, () => resting, Cue, () => Trade.FrameTextBox(true),
            Trade.HoldRosterName, Trade.RosterDrawn);

    public Dialog Dialog => Trade.Dialog;
    public GameText Text => Trade.Text;
    public EclParty Party => Trade.Party;

    /// <summary>The spell list page last asked (null when the last list was empty).</summary>
    internal ListRequest? LastPage { get; set; }
}

/// <summary>Where a spell list was left: the highlighted row and the window's top (the caller's position word, DS:6B29).</summary>
public sealed class ListPlace
{
    public int Row { get; set; }
    public int Top { get; set; }
}

/// <summary>The spell list 0x239D6 → 0x270A2 → the list routine 0x2D669 (P, s30; combat-menu.md "Spell lists' Next / Prev").</summary>
public static class SpellLists
{
    /// <summary>The window's first row (0x270A2 passes y0 = 5).</summary>
    public const int FirstRow = 5;

    /// <summary>
    /// The window: rows 5–22 (y1 = 0x16), Memorize's rows 5–15 (y1 = 0x0F, 0x2713C); the Next / Prev words before the
    /// Exit (or, in Learn, which has no Exit, after its word). The list routine's own words (GAME.OVR 0x2D657 / 0x2D65D)
    /// read the same as the aim menu's first two, which the Client already has from the program (<see cref="CombatMsg.AimNextPrevManual"/>).
    /// </summary>
    public static ListPaging Paging(CombatText text, bool memorize, int wordsAt)
    {
        var w = text.Words(CombatMsg.AimNextPrevManual);
        return new ListPaging(w.ElementAtOrDefault(0) ?? "NEXT", w.ElementAtOrDefault(1) ?? "PREV", memorize ? 11 : 18, wordsAt);
    }

    /// <summary>
    /// The list's screen for <paramref name="who"/> (<see cref="SpellPageLayout"/>): the name in its colour (START
    /// 0x23B6), "Spells " + <paramref name="list"/>'s words (0x239CE + 0x23973–0x239BF) as the title, the choose-spell
    /// prompt (0x27053) when <paramref name="prompt"/> (actions 1–4), Memorize's slot lines in <paramref name="box"/>.
    /// </summary>
    public static SpellPageLayout Layout(EclCharacter who, GameText text, Msg list, bool prompt, IReadOnlyList<WindowLine>? box = null) =>
        new(who.Name.ToUpperInvariant(), CombatChooser.NameColour(who), (text[Msg.SpellsWord] + text[list]).ToUpperInvariant())
        {
            Prompt = prompt ? text[Msg.ChooseSpell].TrimEnd().ToUpperInvariant() + " " : null,
            Box = box,
        };

    /// <summary>
    /// Memorize's slot lines (0x17DD5, read s33; D s33): per class (cleric, magic-user) and level 1–3 the free slots
    /// (0x17A8D, a byte) through Str, at most two characters, blank where the slot byte is 0; a class with any slot byte
    /// counts. With one, START 0x16EC(member, "can memorize:", 10, 0) in the camp's text box: the box's frame drawn
    /// again, rows 17–22 cleared, the name on row 18, the words from row 19 (green); then each counting class on its own
    /// row from 20 (CS:057E / CS:0591 at column 1, green) with its numbers at columns 20, 23, 26.
    /// </summary>
    public static IReadOnlyList<WindowLine> SlotBox(EclCharacter who, GameText text, Func<int, int, int> slotsLeft)
    {
        var lines = new List<WindowLine>
        {
            new(1, 18, who.Name.ToUpperInvariant(), CombatChooser.NameColour(who)),
            new(1, 19, text[Msg.CanMemorise].Trim().ToUpperInvariant()),
        };
        int row = 20;
        for (int cls = 0; cls <= 1; cls++)
        {
            if (!Enumerable.Range(1, 3).Any(l => CampSpells.SlotsFor(who, cls, l) != 0))
                continue;
            lines.Add(new(1, row, text[cls == 0 ? Msg.ClericSpells : Msg.MageSpells].TrimEnd().ToUpperInvariant().PadLeft(18)));
            for (int l = 1; l <= 3; l++)
            {
                if (CampSpells.SlotsFor(who, cls, l) == 0)
                    continue;
                string n = ((byte)slotsLeft(l, cls)).ToString(CultureInfo.InvariantCulture);
                lines.Add(new(20 + 3 * (l - 1), row, n.Length > 2 ? n[..2] : n));
            }
            row++;
        }
        return lines;
    }
}

/// <summary>
/// The camp (GAME.OVR 0x196A0) and its menus as the program runs them (docs/re/camp.md): Save 0x1FD3E, View, Magic
/// 0x18C22 (Cast 0x17CD2, Memorize 0x1813B, Scribe 0x1838C, Display 0x187ED, Rest 0x17FAB), Rest 0x17FAB → 0x24A66,
/// Alter 0x1948A (Order 0x18FB1, Drop 0x19116, Speed 0x192C8, Icon 0x1D1AC, Pics). The rules are Core's (<see cref="CampRest"/>,
/// <see cref="CampSpells"/>, <see cref="CampParty"/>, <see cref="CampCast"/>); texts are the program's
/// (<see cref="GameText"/>). [?] The screen layout keeps the Client's explore frame (text window, bottom-line menus,
/// full-screen lists); cell positions of the program's messages are followed where read (rest time row 17, messages
/// rows 19–20).
/// </summary>
public static class CampScreens
{
    /// <summary>The target pick's answer to Escape (<see cref="PickTarget"/>; the program's menu answers 0).</summary>
    internal const int Escaped = -10;

    // ---- Menu lines ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A menu line as the program's menu routine (0x2CC8C) reads it: every capital letter or digit starts an item and
    /// is its hotkey; words with none belong to the item before. Items upper case.
    /// </summary>
    /// <summary>
    /// Each item's column in <paramref name="line"/> as <see cref="ParseMenu"/> splits it (the menu routine draws the line
    /// with its own spaces), or null when every item is one space after the one before.
    /// </summary>
    public static IReadOnlyList<int>? MenuColumns(string line)
    {
        var cols = new List<int>();
        bool spaced = false;
        int at = 0, end = -1;
        foreach (var w in line.Split(' '))
        {
            if (w.Length > 0)
            {
                bool starts = w.IndexOfAny("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()) >= 0 || cols.Count == 0;
                if (starts)
                {
                    if (cols.Count > 0 && at - end > 1)
                        spaced = true;
                    cols.Add(at);
                }
                end = at + w.Length;
            }
            at += w.Length + 1;
        }
        return spaced ? cols : null;
    }

    public static (List<string> Items, string Hotkeys) ParseMenu(string line)
    {
        var items = new List<string>();
        var keys = new System.Text.StringBuilder();
        foreach (var w in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int k = w.IndexOfAny("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray());
            if (k < 0 && items.Count > 0)
                items[^1] += " " + w.ToUpperInvariant();
            else
            {
                items.Add(w.ToUpperInvariant());
                keys.Append(k < 0 ? char.ToUpperInvariant(w[0]) : w[k]);
            }
        }
        return (items, keys.ToString());
    }

    /// <summary>A bottom-line menu from a program line; the answer is the hotkey of the chosen item, '\0' on cancel.</summary>
    private static char Menu(CampContext c, string line, string? prompt, bool partySelect = true,
        int? up = null, int? down = null, Action? selectMember = null)
    {
        var (items, keys) = ParseMenu(line);
        while (true)
        {
            int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1)
            {
                MenuPrompt = prompt?.TrimEnd(),
                PartySelect = partySelect,
                Hotkeys = keys,
                UpAnswer = up,
                DownAnswer = down,
                Animates = true, // the camp unit's menus pass 1 (0x18C73, 0x194D0, 0x19750, …)
            }) is int a ? a : items.Count - 1;
            if (i is ChoiceRequest.PreviousMember or ChoiceRequest.NextMember || i <= TradeContext.MemberPick)
            {
                if (selectMember is null)
                    c.Trade.SelectMember(i);
                else
                    selectMember();
                // the extended key answered: the caller's loop asks again (0x23819, then the party list redrawn)
                return i == ChoiceRequest.PreviousMember ? '<' : i == ChoiceRequest.NextMember ? '>' : '=';
            }
            if (i >= 0 && i < items.Count)
                return keys[i];
            return '\0';
        }
    }

    /// <summary>A character's message (the program's PrintCreatureMessage): name and text in the text window, a key when asked.</summary>
    /// <summary>
    /// The camp's member messages are START 0x16EC(member, text, 10, pause) (the refusals 0x17CAC, Order 0x19095, Drop
    /// 0x19195 / 0x191E7 / 0x1920B, the scroll and memorise ones): the same layout as <see cref="EffectMessage"/> (name on
    /// row 18, the text typed from row 19), and the pause flag is PauseOnly, the game-speed delay, not a key (D s39: Drop's
    /// relief message cleared 0.9 s after it was typed at speed 4).
    /// </summary>
    private static void Say(CampContext c, EclCharacter who, string text, bool pause = true) =>
        PanelMessage(c, who, text.ToUpperInvariant(), pause);

    // ---- Camp (0x196A0) ------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x196A0: DS:6DD8 and the rest time cleared, the camp message in the text window, pending memorising and scribing
    /// cleared for the party; then the Camp menu until Exit, Escape or an interrupted rest; Home / End pick a member.
    /// Save, then always the quit question; View; Magic; Rest (0x17FAB); Alter. On the way out the pending activities and
    /// the remembered target (DS:6DB5) are cleared. Returns whether a rest was interrupted.
    /// </summary>
    public static bool Camp(CampContext c, string? menuLine)
    {
        c.State.HealTicks = 0;
        Array.Clear(c.State.RestTime);
        c.State.Camping = true;
        c.Dialog.Text.Clear();
        c.Trade.FrameTextBox(false); // START 0x27D9 on entry (0x196C7)
        c.Trade.RosterDrawn(); // its case 2 draws the party list (00BA:286A; D s39: a held name shows cyan again)
        c.Dialog.Text.Put(18, 1, c.Text[Msg.MakesCamp], 10);   // column 1, row 18 (0x196A0)
        c.Spells.ResetParty(c.Party.Members);
        bool interrupted = false;
        try
        {
            string line = menuLine ?? "Save View Magic Rest Alter Exit";
            while (!interrupted)
            {
                char k = Menu(c, line, c.Text[Msg.CampPrompt]);
                if (k is '<' or '>' or '=')
                    continue;
                switch (k)
                {
                    case 'S':
                        Save(c);
                        if (c.Dialog.YesNo(c.Text[Msg.QuitToDos], 14)) // 0x19791: prompt colour 14
                            c.Quit?.Invoke();
                        break;
                    case 'V':
                        CharacterView.Run(c.Trade, Venue.Camp);
                        break;
                    case 'M':
                        interrupted = Magic(c);
                        break;
                    case 'R':
                        interrupted = Rest(c);
                        break;
                    case 'A':
                        Alter(c);
                        break;
                    case 'E' or '\0':
                        return false;
                }
            }
            return interrupted;
        }
        finally
        {
            // An interrupted rest: the area's encamp path redraws the screen (0x3B3B, START 0x27D9 with the area's state),
            // the party list with it; a plain Exit redraws only the view (0x3B48), so a held name stays (D s39).
            if (interrupted)
                c.Trade.RosterDrawn();
            c.Spells.ResetParty(c.Party.Members);
            if (c.Cast is { } cast)
                cast.Remembered = null;
            c.State.Camping = false;
            c.Dialog.Text.Clear();
            c.Trade.FrameTextBox(false);
        }
    }

    // ---- Save (0x1FD3E) --------------------------------------------------------------------------------------------

    /// <summary>The slot menu A..J (Escape gives up); then the save is written (0x1FD3E).</summary>
    public static void Save(CampContext c)
    {
        var (items, keys) = ParseMenu(c.Text.Original(Msg.SaveSlots));
        int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, -1)
        {
            MenuPrompt = c.Text[Msg.SaveWhich].TrimEnd(),
            Hotkeys = keys,
            Animates = c.State.Camping, // 0x1FD7B: DS:49F3 = 2
        }) is int a ? a : -1;
        if (i < 0 || i >= items.Count || keys[i] is < 'A' or > 'J')
            return;
        c.Dialog.Show(c.Text[Msg.Saving]);
        if (c.Save?.Invoke(keys[i]) is { } problem)
            c.Dialog.Say(problem.ToUpperInvariant(), colour: 14);
        c.Dialog.Text.Clear();
    }

    // ---- Rest (0x17FAB, 0x24A66) -----------------------------------------------------------------------------------

    /// <summary>The camp's (and Magic's) Rest: <see cref="CampRest.RestForPreparation"/> with the screens below.</summary>
    public static bool Rest(CampContext c)
    {
        // The rest's effect timeout (handlers 22, 57, … through 0x2AF10) and healing (0x2C647 → handler 0x4E → 0x2C753)
        // show their handler messages while the loop runs: DS:6DDA = 1, so START 0x16EC uses rows 18–22 (s30).
        var shown = c.OutsideMessages(resting: true);
        void OnEffect(EclCharacter t, EffectEvent e) => shown.Show(t, e, c.Effects);
        if (c.Effects is { } fx)
            fx.Event += OnEffect;
        c.Trade.HoldPositionLine(true);
        try
        {
            bool interrupted = c.Rest.RestForPreparation(new RestUi(c));
            c.Dialog.Text.Clear();
            return interrupted;
        }
        finally
        {
            c.Trade.HoldPositionLine(false); // 0x1804A: the position line drawn again after the rest
            if (c.Effects is { } fx2)
                fx2.Event -= OnEffect;
        }
    }

    /// <summary>The rest loop's screens: the rest-time line and menu, the key poll, the messages.</summary>
    public sealed class RestUi(CampContext c) : ICampRestUi
    {
        /// <summary>How long a key poll waits (the Client polls at each rest-time redraw, every 5 ticks).</summary>
        public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(30);

        private int _sincePoll;

        /// <summary>0x243BC: the label at column 1, row 17, then days, hours and minutes, the highlighted one white.</summary>
        public void Draw(int highlight)
        {
            var t = c.Dialog.Text;
            t.ClearRow(17);
            var r = c.State.RestTime;
            t.Put(17, 1, c.Text[Msg.RestTime], 10);
            int x = 11;
            for (int i = CampTime.Days; i >= CampTime.Hours; i--)
            {
                t.Put(17, x + 1, Two(r[i]), (byte)(highlight == i ? 15 : 10));
                t.Put(17, x + 3, ":", 10);
                x += 3;
            }
            t.Put(17, x + 1, Two(10 * r[CampTime.TenMinutes] + r[CampTime.Minutes]), (byte)(highlight == CampTime.TenMinutes ? 15 : 10));
        }

        private static string Two(int v)
        {
            var s = v.ToString(CultureInfo.InvariantCulture);
            return (v < 10 ? "0" + s : s)[..2];
        }

        private const int FieldLeft = -30, FieldRight = -31;

        public bool ChooseRestTime(RestTimeMenu menu)
        {
            c.Dialog.Text.Clear();
            var (items, keys) = ParseMenu(c.Text.Original(Msg.RestMenu));
            var columns = MenuColumns(c.Text.Original(Msg.RestMenu));
            int inc = keys.IndexOf('I'), dec = keys.IndexOf('D');
            while (true)
            {
                Draw(menu.Selected);
                int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1)
                {
                    Hotkeys = keys,
                    UpAnswer = inc >= 0 ? inc : null,
                    DownAnswer = dec >= 0 ? dec : null,
                    // 0x244ED: Left / Right move the field (row 24 keeps its lit word, D s38), Enter = Rest
                    LeftAnswer = FieldLeft,
                    RightAnswer = FieldRight,
                    ConfirmAnswer = keys.IndexOf('R') is >= 0 and var restAt ? restAt : null,
                    Columns = columns,
                }) is int a ? a : items.Count - 1;
                char k = i == FieldLeft ? '<' : i == FieldRight ? '>' : i >= 0 && i < keys.Length ? keys[i] : 'E';
                RestTimeMenu.Key key = k switch
                {
                    '<' => RestTimeMenu.Key.Left,
                    '>' => RestTimeMenu.Key.Right,
                    'R' => RestTimeMenu.Key.Rest,
                    'Y' => RestTimeMenu.Key.Days,
                    'H' => RestTimeMenu.Key.Hours,
                    'M' => RestTimeMenu.Key.Minutes,
                    'I' => RestTimeMenu.Key.Inc,
                    'D' => RestTimeMenu.Key.Dec,
                    _ => RestTimeMenu.Key.Exit,
                };
                if (menu.Press(key) is { } rest)
                {
                    // The line stays as the menu left it, its field lit, until the rest shows it again every 5th tick
                    // (D s33: the minutes still white under a handler message on the first tick).
                    Draw(menu.Selected);
                    return rest;
                }
            }
        }

        public bool StopAsked()
        {
            // The program checks the keyboard every tick; the Client asks the screen at each redraw (every 5 ticks).
            if (++_sincePoll < 5)
                return false;
            _sincePoll = 0;
            if (c.Dialog.Ask(new DelayRequest(Poll) { ReportInput = true }) is not true)
                return false;
            Draw(0);
            return c.Dialog.YesNo(c.Text[Msg.StopResting]);
        }

        public void ShowRestTime() => Draw(0);

        public void PartyHealed(bool anyone)
        {
            Draw(0);
            if (anyone)
                c.Trade.RosterDrawn(); // 0x2463D redraws the party list when anyone healed
            c.Dialog.Text.Put(19, 1, c.Text[Msg.PartyHealed], 10);
            c.Dialog.WaitKey();
            ClearMessages();
        }

        public void Memorised(EclCharacter who, int spell) => SpellMessage(who, Msg.HasMemorised, spell);
        public void Scribed(EclCharacter who, int spell) => SpellMessage(who, Msg.HasScribed, spell);

        /// <summary>The out-of-combat spell message (PrintCastSpellMsg): name and text on row 19, the spell on row 20, a key.</summary>
        private void SpellMessage(EclCharacter who, Msg text, int spell)
        {
            Draw(0);
            ClearMessages();
            var name = who.Name.ToUpperInvariant();
            c.Dialog.Text.Put(19, 1, name, 11);
            c.Dialog.Text.Put(19, name.Length + 2, c.Text[text], 10);
            c.Dialog.Text.Put(20, 1, SpellName(c, spell), 10);
            c.Dialog.WaitKey();
            ClearMessages();
        }

        public void Interrupted()
        {
            ClearMessages();
            Draw(0);
            c.Dialog.Text.Put(19, 1, c.Text[Msg.Interrupted], 15);
            c.Dialog.WaitKey();
        }

        private void ClearMessages()
        {
            for (int r = 18; r <= 22; r++)
                c.Dialog.Text.ClearRow(r);
        }
    }

    // ---- Magic (0x18C22) -------------------------------------------------------------------------------------------

    /// <summary>0x18C22: Cast, Memorize, Scribe, Display, Rest, Exit until Exit, Escape or an interrupted rest.</summary>
    public static bool Magic(CampContext c)
    {
        string line = c.Resources.MagicMenu ?? "Cast Memorize Scribe Display Rest Exit";
        while (true)
        {
            char k = Menu(c, line, null);
            switch (k)
            {
                case '<' or '>' or '=':
                    continue;
                case 'C':
                    CastMenu(c);
                    break;
                case 'M':
                    Memorise(c);
                    break;
                case 'S':
                    Scribe(c);
                    break;
                case 'D':
                    Display(c);
                    break;
                case 'R':
                    if (Rest(c))
                        return true;
                    break;
                default:
                    return false;
            }
        }
    }

    public static string SpellName(CampContext c, int spell) =>
        (c.Resources.SpellName(spell) ?? $"SPELL {spell.ToString(CultureInfo.InvariantCulture)}").ToUpperInvariant();

    private static string LevelName(CampContext c, int level) =>
        (c.Resources.LevelName(level) ?? $"LEVEL {level.ToString(CultureInfo.InvariantCulture)}").ToUpperInvariant();

    /// <summary>The refusal of 0x17B9D as a message; true when the character may go on.</summary>
    private static bool Allowed(CampContext c, CampActivity a)
    {
        if (c.Party.Current is not { } who)
            return false;
        switch (CampSpells.Check(a, who, c.Memory))
        {
            case CampRefusal.NoMagicHere:
                Say(c, who, c.Text[Msg.NoMagicHere]);
                return false;
            case CampRefusal.NoCondition:
                var what = a switch { CampActivity.Cast => Msg.CondCast, CampActivity.Memorise => Msg.CondMemorise, _ => Msg.CondScribe };
                Say(c, who, c.Text[Msg.NoCondition] + c.Text[what]);
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// The spell list page (the list builder and its menu): the name and the list's words on the first line, level headings as
    /// labels, a pending spell marked with an asterisk. <paramref name="action"/> null = display only (Exit). Returns the spell
    /// picked, 0 for none, and whether the list had rows. The rows are the list routine 0x2D669's window (P, s30;
    /// 0x270A2): rows 5–22, Memorize's 5–15 (its slot lines go below), with Next / Prev on the menu line
    /// (<see cref="ListPaging"/>). <paramref name="place"/> keeps the highlighted row and the window's top between calls,
    /// as Memorize and Scribe keep their position word (0x181B3, 0x18404: −1 once before their loop); Cast sets it to −1
    /// before each list (0x17CDC, 0x17D52), so its list starts afresh.
    /// </summary>
    private static (int Spell, bool NotEmpty) SpellPage(CampContext c, CampSpells.ListKind kind, Msg? action,
        IReadOnlyList<ListLine>? extra = null, ListPlace? place = null, IReadOnlyList<WindowLine>? box = null)
    {
        c.LastPage = null;
        if (c.Party.Current is not { } who)
            return (0, false);
        var list = c.Spells.List(who, kind, it => c.Resources.ItemSlot(it));
        if (list.Count == 0)
            return (0, false);
        var rows = new List<string>();
        var labels = new HashSet<int>();
        var ids = new List<int>();
        foreach (var r in list)
        {
            if (r.HeadingLevel is { } l)
            {
                labels.Add(rows.Count);
                rows.Add(LevelName(c, l));
                ids.Add(0);
            }
            rows.Add((r.Pending ? " *" : "  ") + SpellName(c, r.Spell));
            ids.Add(r.Spell);
        }
        Msg suffix = kind switch
        {
            CampSpells.ListKind.InMemory => Msg.InMemory,
            CampSpells.ListKind.SpellBook => Msg.InSpellBook,
            CampSpells.ListKind.OnScrolls => Msg.OnScrolls,
            CampSpells.ListKind.ToMemorise => Msg.ToBeMemorised,
            _ => Msg.ToBeScribed,
        };
        var lines = new List<ListLine> { new(who.Name.ToUpperInvariant() + " " + c.Text[Msg.SpellsWord] + c.Text[suffix], 11, 1) };
        if (action is not null)
            lines.Add(new(c.Text[Msg.ChooseSpell], 13, 1));
        if (extra is not null)
            lines.AddRange(extra);
        string[] actions = action is { } m ? [c.Text[m], "EXIT"] : ["EXIT"];
        var request = new ListRequest(lines, rows, actions, actions.Length - 1, FirstRow: SpellLists.FirstRow)
        {
            Labels = labels,
            RowActions = new HashSet<int> { 0 },
            Paging = SpellLists.Paging(c.Resources.CombatTexts, memorize: action == Msg.MemoriseAction, wordsAt: actions.Length - 1),
            Selected = place?.Row ?? 0,
            Top = place?.Top ?? 0,
            Spell = SpellLists.Layout(who, c.Text, suffix, action is not null, box),
        };
        c.LastPage = request;
        var a = c.Dialog.Page(request);
        if (place is not null)
            (place.Row, place.Top) = (Math.Max(0, a.Row), a.Top);
        if (action is null || a.Action != 0 || a.Row < 0 || a.Row >= ids.Count || labels.Contains(a.Row))
            return (0, true);
        return (ids[a.Row], true);
    }

    /// <summary>
    /// Cast (0x17CD2): the remembered target cleared; refused per 0x17B9D; else the memorised list (Cast) until none
    /// is chosen: each choice is cast (<see cref="CampCast"/>); an empty list gives the no-spells message.
    /// </summary>
    public static void CastMenu(CampContext c)
    {
        if (c.Cast is { } cc)
            cc.Remembered = null;
        if (!Allowed(c, CampActivity.Cast))
            return;
        bool drawn = false; // [bp−4]: a list was drawn or a spell cast → START 0x27D9 at the end (0x17D90)
        while (c.Party.Current is { } who)
        {
            var (spell, notEmpty) = SpellPage(c, CampSpells.ListKind.InMemory, Msg.CastAction);
            drawn |= notEmpty;
            if (spell == 0)
            {
                if (!notEmpty)
                    Say(c, who, c.Text[Msg.NoSpellsMemorised]);
                if (drawn)
                    Redrawn(c);
                return;
            }
            // 0x17D15–0x17D52 (P, s30): the text box's frame (03F1:0000 around (1, 17)–(38, 22)) is drawn over the
            // spell list and its inside blanked (03F1:0137), the spell is cast (0x27C38) with its messages in the box,
            // then the position word goes back to −1, so the next list draws its page afresh (0x239D6 / 0x270A2 draw
            // the window and title only for a position below 0). Row 24 keeps the list's menu line (DOSBox-X s29).
            c.Trade.ShowPageUnder(c.LastPage);
            try
            {
                CastOne(c, who, spell);
            }
            finally
            {
                c.Trade.ShowPageUnder(null);
            }
        }
    }

    /// <summary>
    /// A one-member target outside a fight (0x27AA9, target type 2): START 0x27D9 redraws the camp screen (0x27AFA: the
    /// kept spell page goes, the party list is drawn), then START's member pick 0x37E8 (00BA:2C48) on DS:6DB5, the
    /// remembered target (the caster the first time, 0x27AB8). Each pass draws the party list with the pick lit
    /// (00BA:2CA1) and runs the menu "Cast Spell on whom" with Select / Exit (00BA:2C41, 2C19; keypad flag in the camp):
    /// End the next member, Home the one before (both wrap, 00BA:2D29 / 2D57), E (or Escape: 0) clears the pick
    /// (00BA:2DD6); Return, E and S end the loop (set 00BA:2C1F holds 0x0D, 0x1B, 'E', 'S'; the menu answers Escape with
    /// 0, not 0x1B, so Escape clears the pick and the loop goes on with nobody lit). DS:5D92 (the caster) is not changed.
    /// With no pick, Home walks to the member whose next pointer is nil, the last (00BA:2CBE); End reads the next
    /// pointer of a nil record (0000:0104, the INT 41h vector): no member, so nobody is lit, and a Select then would cast
    /// on that address `[?]` (the Client keeps no pick, so Select ends there like Exit). D s39: Brother Sean's Cure Light
    /// Wounds, End twice from himself to Phineas, S. D s41: Escape clears the lit name and stays in the menu; End after it
    /// lights nobody; Escape, Home lights Phineas (the last).
    /// </summary>
    internal static EclCharacter? PickTarget(CampContext c, EclCharacter? remembered)
    {
        c.Trade.ShowPageUnder(null);
        if (c.InterfaceRedraw is { } redraw)
            redraw(); // 0x27AFA outside the camp: START 0x3379 for DS:49F3 = 3 / 4
        c.Trade.FrameTextBox(false);
        var party = c.Party;
        var (items, keys) = ParseMenu(c.Resources.TargetMenu ?? "Select Exit");
        var pick = remembered;
        {
            while (true)
            {
                c.Trade.RosterDrawnLit(pick);
                int a = c.Dialog.Ask(new ChoiceRequest(null, items, false, Escaped)
                {
                    MenuPrompt = c.Text[Msg.CastOnWhom].TrimEnd(),
                    PartySelect = true,
                    Hotkeys = keys,
                }) is int x ? x : items.Count - 1;
                int n = party.Members.Count;
                if (a == Escaped)
                    pick = null; // 00BA:2DD6, and the loop goes on
                else if (a == ChoiceRequest.NextMember)
                    pick = n == 0 || pick is null ? null : party.Members[(party.Members.IndexOf(pick) + 1) % n];
                else if (a == ChoiceRequest.PreviousMember)
                    pick = n == 0 ? null : party.Members[pick is null ? n - 1 : (party.Members.IndexOf(pick) + n - 1) % n];
                else if (a <= TradeContext.MemberPick && TradeContext.MemberPick - a < n)
                    pick = party.Members[TradeContext.MemberPick - a]; // a click on a name (ours)
                else
                    return a == 0 ? pick : null;
            }
        }
    }

    private static void CastOne(CampContext c, EclCharacter who, int spell)
    {
        c.Dialog.Text.Clear();
        if (c.Cast is not { } cast)
        {
            c.Dialog.Say("CASTING NEEDS THE SPELL TABLE.", colour: 14);
            return;
        }
        if (cast.TargetMode(spell) == 0)
        {
            c.Dialog.Text.Put(19, 1, SpellName(c, spell), 10);
            c.Dialog.Text.Put(20, 1, c.Text[Msg.CombatOnly], 10);
            if (c.Dialog.YesNo(c.Text[Msg.LoseIt]))
                cast.Lose(spell);
            c.Dialog.Text.Clear();
            return;
        }
        if (!FightSpells.Modelled.Contains(spell))
        {
            // [?] Core has no routine for this spell yet: nothing is cast, the spell stays memorised.
            c.Dialog.Say(SpellName(c, spell) + ": NOT IN THIS BUILD YET.", colour: 14);
            return;
        }
        // "casts" and the spell name (PrintCastSpellMsg), then the target and the routine. DOSBox-X s29 (Brother Sean's
        // Bless in camp, speed 4): the name and the casts word on row 19, the spell name on row 20 (one row below the
        // effect messages' name row), drawn at once (not typed), then the game-speed pause (no key) and the rows
        // cleared; the effect messages follow. [?] The routine is not read (0x27D48 outside a fight).
        var name = who.Name.ToUpperInvariant();
        var w = c.Dialog.Text;
        w.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        w.Put(TextWindow.Top + 2, TextWindow.Left, name, 11);
        w.Put(TextWindow.Top + 2, TextWindow.Left + name.Length + 1, c.Text[Msg.Casts].Trim(), 10);
        w.Put(TextWindow.Top + 3, TextWindow.Left, SpellName(c, spell), 10);
        if (c.Memory.GameSpeed > 0)
            c.Dialog.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * c.Memory.GameSpeed)));
        w.ClearRect(TextWindow.Left, TextWindow.Top + 1, TextWindow.Right, TextWindow.Bottom);
        var heard = new List<string>();
        var shown = c.OutsideMessages(resting: false);
        void OnSpell(EclCharacter t, int s, EffectEvent e)
        {
            if (CombatText.SpellMessage(e) is { } message)
                shown.Message(t, c.Resources.CombatTexts[message.Text], true);
            else if (e == EffectEvent.Healed && s == 3)
                shown.Healed(t); // Cure Light Wounds 0x28083 → START 0x2EEA (0x280B7)
            else
                heard.Add(t.Name.ToUpperInvariant() + ": " + e.ToString().ToUpperInvariant());
        }
        void OnEffect(EclCharacter t, EffectEvent e)
        {
            // 0x2C540's text (START 0x2B8B) and its unaffected words (0x2C532) are shown where they happen.
            if (cast.Spells.Effects.EffectText is { } key)
                EffectMessage(c, t, Cut40(c.Resources.CombatTexts.Effect(key)));
            else if (e == EffectEvent.Unaffected)
                EffectMessage(c, t, c.Resources.CombatTexts[CombatMsg.Unaffected]);
            else if (shown.Show(t, e, cast.Spells.Effects))
            {
                // The handlers' own messages (0xECB3's callers and end, the revive 0x2C753, …): START 0x16EC's camp branch.
            }
            else if (e is EffectEvent.GoesDownDone or EffectEvent.DamageDone)
            {
                // The end of 0x2C44A (pause and clear) carries no text of its own.
            }
            else
                heard.Add(t.Name.ToUpperInvariant() + ": " + e.ToString().ToUpperInvariant());
        }
        cast.Spells.Event += OnSpell;
        cast.Spells.Effects.Event += OnEffect;
        try
        {
            cast.Cast(spell, remembered => PickTarget(c, remembered));
        }
        finally
        {
            cast.Spells.Event -= OnSpell;
            cast.Spells.Effects.Event -= OnEffect;
        }
        // [?] The other events' messages outside a fight (damage, healing) are not matched: they are
        // listed in our words.
        if (heard.Count > 0)
        {
            c.Dialog.Text.Clear();
            foreach (var h in heard.Take(6))
            {
                c.Dialog.Show(h, clear: false);
                c.Dialog.Text.NewLine();
            }
            c.Dialog.WaitKey();
            c.Dialog.Text.Clear();
        }
    }

    /// <summary>
    /// An effect message outside a fight (read s27): 0x2C540 calls START 0x2B8B, whose out-of-fight branch (DS:49F3 ≠ 5)
    /// is START 0x16EC(creature, text, row 10, pause 1) (the unaffected words 0x2C532 go to START 0x16EC with the same
    /// arguments). Outside a fight START 0x16EC ignores the row: with DS:6DDA = 0 (no rest running) the text box's frame
    /// is drawn again (03F1:0000 around 1, 17–38, 22; the Client's frame is always there), (1, 17)–(38, 22) cleared, the
    /// name at (1, 18) (START 0x1816, the fight panel's name colours), the text word-wrapped into (1, 19)–(38, 22) in
    /// green (START 0x709:0x0768), then PauseOnly (DS:49E2 × 225 ms) and START 0x2383, which outside a fight clears
    /// (1, 18)–(38, 22). The text types out while DS:4A00 is set (after the start-up or a fight, no PRINT or ENCMENU
    /// since): the camp's text window is <see cref="ClientPlayer.Text"/>, which types by <see cref="GoldBox.Core.Ecl.EclMemory.TypesText"/>.
    /// </summary>
    /// <summary>START 0x2B8B copies its text into a string[40] before it looks at the fight flag (0x2B9B).</summary>
    private static string Cut40(string s) => s.Length > 40 ? s[..40] : s;

    internal static void EffectMessage(CampContext c, EclCharacter who, string text) => PanelMessage(c, who, text, true);

    /// <summary>START 0x16EC outside a fight with DS:6DDA = 0 (00BA:1765–00BA:17DA); see <see cref="EffectMessage"/>.</summary>
    private static void PanelMessage(CampContext c, EclCharacter who, string text, bool pause)
    {
        c.Trade.FrameTextBox(true);
        var w = c.Dialog.Text;
        w.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
        w.Put(TextWindow.Top + 1, TextWindow.Left, who.Name.ToUpperInvariant(), CombatChooser.NameColour(who));
        w.PrintIn(TextWindow.Left, TextWindow.Top + 2, TextWindow.Right, TextWindow.Bottom, text, true, TextWindow.ScriptColour);
        if (!pause)
            return;
        int speed = c.Memory.GameSpeed;
        if (speed > 0)
            c.Dialog.Ask(new DelayRequest(TimeSpan.FromMilliseconds(225 * speed)));
        w.ClearRect(TextWindow.Left, TextWindow.Top + 1, TextWindow.Right, TextWindow.Bottom);
    }

    /// <summary>
    /// Memorize (0x1813B): refused per 0x17B9D. The pending list first: Yes keeps it and ends, No clears it. Then while
    /// not done: no slots at all → the no-slots message; else the spell book (Memorize) with the free slots
    /// shown; a pick goes pending when a slot is left; none ends. When the book was shown, the pending list and the
    /// question again.
    /// </summary>
    public static void Memorise(CampContext c)
    {
        if (!Allowed(c, CampActivity.Memorise) || c.Party.Current is not { } who)
            return;
        bool done = false;
        var (_, pending) = SpellPage(c, CampSpells.ListKind.ToMemorise, null);
        bool drawn = pending; // [bp−3] (0x18170 / 0x181AF / 0x18212): a list page was drawn
        if (pending)
        {
            if (!PageQuestion(c, c.Text[Msg.MemoriseThese]))
                CampSpells.ResetMemorise(who);
            else
                done = true;
        }
        bool shown = false;
        var place = new ListPlace(); // 0x181B3: the position word −1 once, kept through the loop
        while (!done)
        {
            if (!CampSpells.HasSlots(who))
            {
                Say(c, who, c.Text[Msg.CannotMemorise]);
                break;
            }
            var box = SpellLists.SlotBox(who, c.Text, (l, cls) => c.Spells.SlotsLeft(who, l, cls));
            SlotMessage(c, box, shown ? c.LastPage : null);
            shown = drawn = true;
            var (spell, _) = SpellPage(c, CampSpells.ListKind.SpellBook, Msg.MemoriseAction, SlotLines(c, who), place, box);
            if (spell == 0)
                break;
            c.Spells.MarkMemorise(who, spell);
        }
        if (shown && SpellPage(c, CampSpells.ListKind.ToMemorise, null).NotEmpty && !PageQuestion(c, c.Text[Msg.MemoriseTheseAgain]))
            CampSpells.ResetMemorise(who);
        if (drawn)
            Redrawn(c);
    }

    /// <summary>
    /// 0x17DD5's message before each pass of the book (D s33): START 0x16EC's camp branch clears rows 17–22, puts the
    /// name on row 18 and types "can memorize:" from row 19 (while DS:4A00 is set), then the class lines go on at once
    /// (0x709:0x626). The first time over the camp screen (row 24 keeps the Magic menu); later over the book page, whose
    /// text box frame the routine draws again (<see cref="TradeContext.ShowPageUnder"/>).
    /// </summary>
    private static void SlotMessage(CampContext c, IReadOnlyList<WindowLine> box, ListRequest? page)
    {
        var w = c.Dialog.Text;
        if (page is not null)
            c.Trade.ShowPageUnder(page);
        c.Trade.FrameTextBox(true);
        try
        {
            w.ClearRect(TextWindow.Left, TextWindow.Top, TextWindow.Right, TextWindow.Bottom);
            for (int i = 0; i < box.Count; i++)
            {
                var l = box[i];
                if (i == 1) // the words through 0x77F8 (word-wrapped, typed)
                    w.PrintIn(TextWindow.Left, l.Row, TextWindow.Right, TextWindow.Bottom, l.Text, true, l.Colour);
                else
                    w.Put(l.Row, l.Column, l.Text, l.Colour);
            }
        }
        finally
        {
            if (page is not null)
                c.Trade.ShowPageUnder(null);
        }
    }

    /// <summary>
    /// A keep question asked over the list page it follows (0x3C5:0x39 after 0x239D6; D s33: the page stays, the
    /// question on row 24).
    /// </summary>
    private static bool PageQuestion(CampContext c, string question)
    {
        c.Trade.ShowPageAlone(c.LastPage);
        try
        {
            return c.Dialog.YesNo(question, 14); // 0x3C5:0x39 with 14, 10, 15 (0x18189, 0x182B8, Scribe's alike)
        }
        finally
        {
            c.Trade.ShowPageAlone(null);
        }
    }

    /// <summary>
    /// START 0x27D9 (the redraw for the interface state, here the camp's 2) at the end of Cast, Memorize and Scribe when
    /// a list page was drawn (0x17D96, 0x182E1, Scribe's end; D s33): the frames, the party list and the position
    /// line again, the text window empty (the picture comes back with the menu's animation).
    /// </summary>
    private static void Redrawn(CampContext c)
    {
        if (c.InterfaceRedraw is { } redraw)
        {
            redraw(); // outside the camp: START 0x3379 for DS:49F3 = 3 / 4
            return;
        }
        c.Dialog.Text.Clear();
        c.Trade.FrameTextBox(false);
        c.Trade.RosterDrawn(); // START 0x27D9's case 2 draws the party list (00BA:286A)
    }

    /// <summary>0x17DD5: its heading and, per class with slots, its free slots of levels 1–3 (blank without a slot).</summary>
    private static List<ListLine> SlotLines(CampContext c, EclCharacter who)
    {
        var lines = new List<ListLine> { new(who.Name.ToUpperInvariant() + " " + c.Text[Msg.CanMemorise], 10, 1) };
        for (int cls = 0; cls <= 1; cls++)
        {
            var s = new System.Text.StringBuilder(c.Text[cls == 0 ? Msg.ClericSpells : Msg.MageSpells].PadLeft(18));
            bool any = false;
            for (int l = 1; l <= 3; l++)
            {
                bool has = CampSpells.SlotsFor(who, cls, l) != 0;
                any |= has;
                s.Append(' ').Append(has ? c.Spells.SlotsLeft(who, l, cls).ToString(CultureInfo.InvariantCulture).PadLeft(2) : "  ");
            }
            if (any)
                lines.Add(new(s.ToString(), 10, 1));
        }
        return lines;
    }

    /// <summary>
    /// Scribe (0x1838C): refused per 0x17B9D. The pending scroll spells first (Yes keeps them and ends, No clears them);
    /// then the scroll spells (Scribe) until none is chosen: known / already pending / no slot → a message on the
    /// bottom line; else marked. An empty list gives the no-scrolls message. When shown, the pending list again.
    /// </summary>
    public static void Scribe(CampContext c)
    {
        if (!Allowed(c, CampActivity.Scribe) || c.Party.Current is not { } who)
            return;
        bool done = false;
        var (_, pending) = SpellPage(c, CampSpells.ListKind.ToScribe, null);
        bool drawn = pending;
        if (pending)
        {
            if (!PageQuestion(c, c.Text[Msg.ScribeThese]))
                c.Spells.ResetScribe(who);
            else
                done = true;
        }
        bool shown = false;
        var place = new ListPlace(); // 0x18404: the position word −1 once, kept through the loop
        while (!done)
        {
            var (spell, notEmpty) = SpellPage(c, CampSpells.ListKind.OnScrolls, Msg.ScribeAction, place: place);
            drawn |= notEmpty;
            if (spell == 0)
            {
                if (!notEmpty)
                    Say(c, who, c.Text[Msg.NoCopyableScrolls]);
                else
                    shown = true;
                break;
            }
            shown = true;
            switch (c.Spells.MarkScribe(who, spell))
            {
                case CampSpells.ScribeResult.AlreadyKnown:
                    c.Dialog.WaitKey(c.Text[Msg.AlreadyKnow]);
                    break;
                case CampSpells.ScribeResult.AlreadyScribing:
                    c.Dialog.WaitKey(c.Text[Msg.AlreadyScribing]);
                    break;
                case CampSpells.ScribeResult.NoSlot:
                    c.Dialog.WaitKey(c.Text[Msg.CannotScribe]);
                    break;
            }
        }
        if (shown && SpellPage(c, CampSpells.ListKind.ToScribe, null).NotEmpty && !PageQuestion(c, c.Text[Msg.ScribeTheseAgain]))
            c.Spells.ResetScribe(who);
        if (drawn)
            Redrawn(c);
    }

    /// <summary>
    /// Display (0x187ED, P s34): the list starts with an empty entry, then per member the name (a label), its effects
    /// (<see cref="CampSpells.Display"/>, no indent) or the none text, and an empty entry. One full-screen window
    /// (START 03F1:02E8 with 1, 1, 38, 22, its title centred in white), then 0x2D669 with the window (1, 4)–(38, 22),
    /// colours 11 / 10 / 15, no prompt, no action word and Exit: the empty entry on row 4, the first name on row 5,
    /// " Exit" from column 1 (D: s33). Display only: any key that ends the list leaves.
    /// </summary>
    public static void Display(CampContext c)
    {
        var rows = new List<string> { "" };
        var labels = new HashSet<int>();
        foreach (var m in c.Party.Members)
        {
            labels.Add(rows.Count);
            rows.Add(m.Name.ToUpperInvariant());
            var lines = c.Spells.Display(m);
            foreach (var l in lines)
                rows.Add((l.Spell > 0 ? SpellName(c, l.Spell)
                    : l.Funky ? c.Text[Msg.Funky] + l.EffectId.ToString(CultureInfo.InvariantCulture)
                    : c.Text.EffectLabel(l.EffectId) ?? $"EFFECT {l.EffectId.ToString(CultureInfo.InvariantCulture)}").ToUpperInvariant());
            if (lines.Count == 0)
                rows.Add(c.Text[Msg.NoSpellEffects].ToUpperInvariant());
            rows.Add("");
        }
        var paging = SpellLists.Paging(c.Resources.CombatTexts, memorize: false, wordsAt: 0) with { Rows = DisplayRows };
        c.Dialog.Page(new ListRequest([], rows, ["EXIT"], 0, FirstRow: DisplayFirstRow)
        {
            Labels = labels,
            Paging = paging,
            Spell = new SpellPageLayout("", 15, c.Text[Msg.DisplayTitle].ToUpperInvariant()) { CentredTitle = true, LabelColour = 11 },
        });
    }

    /// <summary>Display's list window (1, 4)–(38, 22): its first row and its rows.</summary>
    public const int DisplayFirstRow = 4, DisplayRows = 19;

    // ---- Alter (0x1948A) -----------------------------------------------------------------------------------------

    /// <summary>0x1948A: Order, Drop, Speed, Icon, Pics, Exit; Home / End pick a member.</summary>
    public static void Alter(CampContext c)
    {
        string line = c.Resources.AlterMenu ?? "Order Drop Speed Icon Pics Exit";
        while (true)
        {
            char k = Menu(c, line, c.Text[Msg.AlterPrompt]);
            switch (k)
            {
                case '<' or '>' or '=':
                    continue;
                case 'O':
                    Order(c);
                    break;
                case 'D':
                    Drop(c);
                    break;
                case 'S':
                    Speed(c);
                    break;
                case 'I':
                    // 0x1951F: the icon editor 0x1D1AC (0078:002F) on the current character (DS:5D92), then the screen
                    // redrawn for the interface state (START 0x3379): the camp's own frame comes back with the menu.
                    if (c.EditIcon is { } edit && c.Party.Current is { } who)
                        edit(who);
                    else
                        c.Dialog.Say("ICON: NOT IN THIS BUILD YET.", colour: 14);
                    c.Trade.RosterDrawn(); // 0x19524: START 0x27D9 (case 2) draws the party list
                    break;
                case 'P':
                    Pictures(c);
                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Party order (0x18FB1): its prompt with Select / Exit, or Place / Exit. In Select mode Home / End pick the member;
    /// in Place mode Home moves it up (0x18CDA), End down (0x18E32). Enter, P or S switch the mode: into Place
    /// the selected message, back to Select the message is cleared.
    /// </summary>
    public static void Order(CampContext c)
    {
        var lines = c.Resources.OrderMenus ?? ["Select Exit", "Place Exit"];
        int mode = 0;
        while (true)
        {
            var (items, keys) = ParseMenu(lines[mode]);
            int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1)
            {
                MenuPrompt = c.Text[Msg.PartyOrder].TrimEnd(),
                PartySelect = true,
                Hotkeys = keys,
                Animates = true, // 0x19006
            }) is int a ? a : items.Count - 1;
            if (i is ChoiceRequest.PreviousMember or ChoiceRequest.NextMember || i <= TradeContext.MemberPick)
            {
                if (mode == 0)
                    c.Trade.SelectMember(i);
                else if (i == ChoiceRequest.PreviousMember)
                    CampParty.MoveUp(c.Party);
                else if (i == ChoiceRequest.NextMember)
                    CampParty.MoveDown(c.Party);
                else if (TradeContext.MemberPick - i is var m && m != c.Party.CurrentIndex)
                {
                    // The mouse (ours): a click on a name above the current member moves it up one, below down one.
                    if (m < c.Party.CurrentIndex)
                        CampParty.MoveUp(c.Party);
                    else
                        CampParty.MoveDown(c.Party);
                }
                c.Trade.RosterDrawn(); // 0x1902B / 0x1904F: the party list after Home / End in either mode
                continue;
            }
            char k = i >= 0 && i < keys.Length ? keys[i] : 'E';
            if (k is 'P' or 'S')
            {
                mode = mode == 0 ? 1 : 0;
                if (mode == 1 && c.Party.Current is { } who)
                    Say(c, who, c.Text[Msg.HasBeenSelected], pause: false);
                else
                    c.Dialog.Text.Clear();
                continue;
            }
            c.Dialog.Text.Clear();
            return;
        }
    }

    /// <summary>
    /// Drop (0x19116): the only member → the quit question (Yes quits). Else the leaving message and the drop question: Yes →
    /// the farewell (enabled, 0x10D ≠ 0) or the other message, and the member leaves (<see cref="CampParty.Drop"/>);
    /// No → the relief message.
    /// </summary>
    public static void Drop(CampContext c)
    {
        if (c.Party.Current is not { } who)
            return;
        if (c.Party.Members.Count == 1)
        {
            if (c.Dialog.YesNo(c.Text[Msg.QuitToDosDrop], 14))
                c.Quit?.Invoke();
            return;
        }
        Say(c, who, c.Text[Msg.WillBeGone], pause: false);
        if (c.Dialog.YesNo(c.Text[Msg.DropFromParty], 14)) // 0x1914E / 0x191A9: 14
        {
            Say(c, who, c.Text[CampParty.LeavesWillingly(who) ? Msg.BidsFarewell : Msg.DumpedInDitch]);
            CampParty.Drop(c.Party, c.Memory);
            c.Trade.RosterDrawn(); // 0x19234: the party list after the member left
        }
        else
            Say(c, who, c.Text[Msg.SighOfRelief]);
        c.Dialog.Text.Clear();
    }

    /// <summary>
    /// Speed (0x192C8): the speed line on row 18; the menu offers Faster while above 0,
    /// Slower while below 9, and Exit; Up slows, Down speeds up (as the arrows). Until Exit or Escape.
    /// </summary>
    public static void Speed(CampContext c)
    {
        while (true)
        {
            int speed = CampSettings.Speed(c.Memory);
            c.Dialog.Text.Clear();
            c.Dialog.Text.Put(18, 1, c.Text[Msg.GameSpeedIs] + speed.ToString(CultureInfo.InvariantCulture) + c.Text[Msg.SpeedRange], 10);
            string line = (speed > 0 ? c.Text.Original(Msg.Faster) : "") + (speed < 9 ? c.Text.Original(Msg.Slower) : "") + c.Text.Original(Msg.SpeedExit);
            var (items, keys) = ParseMenu(line);
            int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1)
            {
                MenuPrompt = c.Text[Msg.GameSpeedPrompt].TrimEnd(),
                Hotkeys = keys,
                UpAnswer = -10,
                DownAnswer = -11,
                Animates = true, // 0x193BD
            }) is int a ? a : items.Count - 1;
            switch (i)
            {
                case -10:
                    CampSettings.Slower(c.Memory);
                    continue;
                case -11:
                    CampSettings.Faster(c.Memory);
                    continue;
            }
            char k = i >= 0 && i < keys.Length ? keys[i] : 'E';
            if (k == 'F')
                CampSettings.Faster(c.Memory);
            else if (k == 'S')
                CampSettings.Slower(c.Memory);
            else
            {
                c.Dialog.Text.Clear();
                return;
            }
        }
    }

    /// <summary>Pics (0x1948A): monsters on/off, portraits on/off, Exit; M and P toggle, until Exit or Escape.</summary>
    public static void Pictures(CampContext c)
    {
        while (true)
        {
            string line = c.Text.Original(CampSettings.MonsterPictures(c.Memory) ? Msg.MonstersOn : Msg.MonstersOff)
                + c.Text.Original(CampSettings.Portraits(c.Memory) ? Msg.PortraitsOn : Msg.PortraitsOff)
                + c.Text.Original(Msg.PicsExit);
            var (items, keys) = ParseMenu(line);
            int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1) { Hotkeys = keys, Animates = true }) is int a ? a : items.Count - 1;
            char k = i >= 0 && i < keys.Length ? keys[i] : 'E';
            if (k == 'M')
                CampSettings.ToggleMonsters(c.Memory);
            else if (k == 'P')
                CampSettings.TogglePortraits(c.Memory);
            else
                return;
        }
    }
}
