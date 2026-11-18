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

    public Dialog Dialog => Trade.Dialog;
    public GameText Text => Trade.Text;
    public EclParty Party => Trade.Party;
}

/// <summary>
/// The camp (GAME.OVR 0x196A0) and its menus as the program runs them (docs/re/camp.md): Save 0x1FD3E, View, Magic
/// 0x18C22 (Cast 0x17CD2, Memorize 0x1813B, Scribe 0x1838C, Display 0x187ED, Rest 0x17FAB), Rest 0x17FAB → 0x24A66,
/// Alter 0x1948A (Order 0x18FB1, Drop 0x19116, Speed 0x192C8, Icon, Pics). The rules are Core's (<see cref="CampRest"/>,
/// <see cref="CampSpells"/>, <see cref="CampParty"/>, <see cref="CampCast"/>); texts are the program's
/// (<see cref="GameText"/>). [?] The screen layout keeps the Client's explore frame (text window, bottom-line menus,
/// full-screen lists); cell positions of the program's messages are followed where read (rest time row 17, messages
/// rows 19–20).
/// </summary>
public static class CampScreens
{
    // ---- Menu lines ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A menu line as the program's menu routine (0x2CC8C) reads it: every capital letter or digit starts an item and
    /// is its hotkey; words with none belong to the item before. Items upper case.
    /// </summary>
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
    private static void Say(CampContext c, EclCharacter who, string text, bool pause = true)
    {
        c.Dialog.Show(GameText.Cat(who.Name.ToUpperInvariant(), text.ToUpperInvariant()));
        if (pause)
            c.Dialog.WaitKey();
    }

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
                        if (c.Dialog.YesNo(c.Text[Msg.QuitToDos]))
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
            c.Spells.ResetParty(c.Party.Members);
            if (c.Cast is { } cast)
                cast.Remembered = null;
            c.State.Camping = false;
            c.Dialog.Text.Clear();
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
        bool interrupted = c.Rest.RestForPreparation(new RestUi(c));
        c.Dialog.Text.Clear();
        return interrupted;
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

        public bool ChooseRestTime(RestTimeMenu menu)
        {
            c.Dialog.Text.Clear();
            var (items, keys) = ParseMenu(c.Text.Original(Msg.RestMenu));
            int inc = keys.IndexOf('I'), dec = keys.IndexOf('D');
            while (true)
            {
                Draw(menu.Selected);
                int i = c.Dialog.Ask(new ChoiceRequest(null, items, false, items.Count - 1)
                {
                    Hotkeys = keys,
                    UpAnswer = inc >= 0 ? inc : null,
                    DownAnswer = dec >= 0 ? dec : null,
                }) is int a ? a : items.Count - 1;
                char k = i >= 0 && i < keys.Length ? keys[i] : 'E';
                RestTimeMenu.Key key = k switch
                {
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
                    Draw(0);
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
    /// picked, 0 for none, and whether the list had rows.
    /// </summary>
    private static (int Spell, bool NotEmpty) SpellPage(CampContext c, CampSpells.ListKind kind, Msg? action,
        IReadOnlyList<ListLine>? extra = null)
    {
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
        var a = c.Dialog.Page(new ListRequest(lines, rows, actions, actions.Length - 1, FirstRow: lines.Count + 1)
        {
            Labels = labels,
            RowActions = new HashSet<int> { 0 },
        });
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
        while (c.Party.Current is { } who)
        {
            var (spell, notEmpty) = SpellPage(c, CampSpells.ListKind.InMemory, Msg.CastAction);
            if (spell == 0)
            {
                if (!notEmpty)
                    Say(c, who, c.Text[Msg.NoSpellsMemorised]);
                return;
            }
            CastOne(c, who, spell);
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
        // "casts" and the spell name (PrintCastSpellMsg), then the target and the routine.
        var name = who.Name.ToUpperInvariant();
        c.Dialog.Text.Put(19, 1, name, 11);
        c.Dialog.Text.Put(19, name.Length + 2, c.Text[Msg.Casts], 10);
        c.Dialog.Text.Put(20, 1, SpellName(c, spell), 10);
        c.Dialog.WaitKey();
        c.Dialog.Text.Clear();
        var heard = new List<string>();
        void OnSpell(EclCharacter t, int s, EffectEvent e) => heard.Add(t.Name.ToUpperInvariant() + ": " + e.ToString().ToUpperInvariant());
        void OnEffect(EclCharacter t, EffectEvent e) => heard.Add(t.Name.ToUpperInvariant() + ": " + e.ToString().ToUpperInvariant());
        cast.Spells.Event += OnSpell;
        cast.Spells.Effects.Event += OnEffect;
        try
        {
            cast.Cast(spell, remembered =>
            {
                // "Cast Spell on whom" (START's member pick; [?] its first highlighted member is not matched).
                var names = c.Party.Members.Select(m => m.Name.ToUpperInvariant()).Append("EXIT").ToList();
                int i = c.Dialog.Ask(new ChoiceRequest(c.Text[Msg.CastOnWhom], names, true, names.Count - 1)) is int a ? a : names.Count - 1;
                return i >= 0 && i < c.Party.Members.Count ? c.Party.Members[i] : null;
            });
        }
        finally
        {
            cast.Spells.Event -= OnSpell;
            cast.Spells.Effects.Event -= OnEffect;
        }
        // [?] The effect messages' wording and rows are not read: the events are listed in our words.
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
        if (pending)
        {
            if (!c.Dialog.YesNo(c.Text[Msg.MemoriseThese]))
                CampSpells.ResetMemorise(who);
            else
                done = true;
        }
        bool shown = false;
        while (!done)
        {
            if (!CampSpells.HasSlots(who))
            {
                Say(c, who, c.Text[Msg.CannotMemorise]);
                break;
            }
            shown = true;
            var (spell, _) = SpellPage(c, CampSpells.ListKind.SpellBook, Msg.MemoriseAction, SlotLines(c, who));
            if (spell == 0)
                break;
            c.Spells.MarkMemorise(who, spell);
        }
        if (shown && SpellPage(c, CampSpells.ListKind.ToMemorise, null).NotEmpty && !c.Dialog.YesNo(c.Text[Msg.MemoriseTheseAgain]))
            CampSpells.ResetMemorise(who);
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
        if (pending)
        {
            if (!c.Dialog.YesNo(c.Text[Msg.ScribeThese]))
                c.Spells.ResetScribe(who);
            else
                done = true;
        }
        bool shown = false;
        while (!done)
        {
            var (spell, notEmpty) = SpellPage(c, CampSpells.ListKind.OnScrolls, Msg.ScribeAction);
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
        if (shown && SpellPage(c, CampSpells.ListKind.ToScribe, null).NotEmpty && !c.Dialog.YesNo(c.Text[Msg.ScribeTheseAgain]))
            c.Spells.ResetScribe(who);
    }

    /// <summary>
    /// Display (0x187ED): a page with each member's name (a label) and its effects (<see cref="CampSpells.Display"/>),
    /// the none text when none, a blank line after each member.
    /// </summary>
    public static void Display(CampContext c)
    {
        var rows = new List<string>();
        var labels = new HashSet<int>();
        foreach (var m in c.Party.Members)
        {
            labels.Add(rows.Count);
            rows.Add(m.Name.ToUpperInvariant());
            var lines = c.Spells.Display(m);
            foreach (var l in lines)
                rows.Add(" " + (l.Spell > 0 ? SpellName(c, l.Spell)
                    : l.Funky ? c.Text[Msg.Funky] + l.EffectId.ToString(CultureInfo.InvariantCulture)
                    : c.Text.EffectLabel(l.EffectId) ?? $"EFFECT {l.EffectId.ToString(CultureInfo.InvariantCulture)}"));
            if (lines.Count == 0)
                rows.Add(c.Text[Msg.NoSpellEffects]);
            labels.Add(rows.Count);
            rows.Add("");
        }
        c.Dialog.Page(new ListRequest([new(c.Text[Msg.DisplayTitle], 15, 1)], rows, ["EXIT"], 0, FirstRow: 2) { Labels = labels });
    }

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
                    // [?] The icon editor (CustomizeIcon) belongs to character creation, not built.
                    c.Dialog.Say("ICON: NOT IN THIS BUILD YET.", colour: 14);
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
            if (c.Dialog.YesNo(c.Text[Msg.QuitToDosDrop]))
                c.Quit?.Invoke();
            return;
        }
        Say(c, who, c.Text[Msg.WillBeGone], pause: false);
        if (c.Dialog.YesNo(c.Text[Msg.DropFromParty]))
        {
            Say(c, who, c.Text[CampParty.LeavesWillingly(who) ? Msg.BidsFarewell : Msg.DumpedInDitch]);
            CampParty.Drop(c.Party, c.Memory);
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
