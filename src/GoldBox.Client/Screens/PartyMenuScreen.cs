using System.Globalization;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The party menu and character creation on screen (<see cref="PartyMenuFlow"/> runs on the runner thread and parks on
/// its requests; this screen draws and answers them). Layouts as the program draws them (DOSBox-X, s19): the braided
/// window (0x3F1:02E8 with 1,1,38,22), the party list (NAME / AC / HP at row 2, members from row 4), the entries from
/// row 12 (hotkey letter white at column 2, the rest green), the prompt on the bottom line in colour 13; the creation
/// lists from row 2; the character page as View draws it (<see cref="CombatSheet"/>) with the portrait at the top right.
/// Keyboard: the hotkeys, arrows, Return, Esc, Home / End. Pad: D-pad, A, B, LB / RB. Mouse: point, click, right click
/// backs out. When Begin or Load starts a game, the explore screen takes over (made by <paramref name="explore"/>).
/// </summary>
public sealed class PartyMenuScreen(PartyMenuFlow flow, ScriptRunner runner, IScreenHost host, TextFont font,
    ClientResources resources, ClientArt art, Func<PorGame, IScreen> explore) : IScreen
{
    private const int MenuRow = 24, ItemsRow = 12, RosterRow = 4;
    private const byte PromptColour = 13;

    /// <summary>HEAD / BODY tables of the portrait pick (DS:286F / DS:287D, 1-based) and the disk the party menu reads them from (DS:5376 = 3).</summary>
    public const int HeadTable = 0x286F, BodyTable = 0x287D, PortraitDisk = 3;

    private PlayerRequest? _shown;
    private PlayerRequest? _background;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _delayEnd;
    /// <summary>Train's Learn list (a full-screen list page).</summary>
    private readonly ListPage _list = new() { Shared = flow.Highlight }; // the list routine's global top (MenuHighlight.ListTop)
    private int _selected, _word;
    private string _typed = "";
    private bool _switched;
    private object? _clickAnswer;
    private bool _hasClick;

    /// <summary>The program data the portrait tables are read from (null: the head and body ids as picked).</summary>
    public PorProgramData? Program { get; init; }

    /// <summary>
    /// Over the training hall or the camp (the explore screen pushes it when the hall's Create or Add asks for a creation
    /// page, or the camp's Alter Icon for the icon editor): it answers the pages of <see cref="PartyMenuFlow"/> and closes
    /// itself when the hall's menu (or anything else) asks.
    /// </summary>
    public bool Overlay { get; init; }

    /// <summary>The on/off settings the controls screen shows (<see cref="SettingToggle"/>).</summary>
    public IReadOnlyList<SettingToggle>? Toggles { get; init; }
    public Audio.SoundSetting? Sound { get; init; }

    /// <summary>The requests the overlay answers: the creation and roster pages, and the questions asked over them.</summary>
    public static bool IsPage(PlayerRequest? r) =>
        r is PickRequest or SheetRequest or IconRequest or ModifyRequest or CharacterPageRequest;

    /// <summary>The start word the controls screen edits (the hall's J, <see cref="ClientSettings.ProgramWord"/>), or null.</summary>
    public WordSetting? StartWord { get; init; }

    /// <summary>A message for the bottom line (a fault of the game logic).</summary>
    public string? Problem { get; private set; }

    // ---------------------------------------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------------------------------------

    public void Update(ActionInput input)
    {
        if (input.Pressed(GameAction.Controls))
        {
            host.Push(new ControlsScreen(font, host, art, StartWord, Toggles, Sound));
            return;
        }
        if (HandOver())
            return;
        Sync();
        if (runner.Pending is not { } request)
            return;
        // A key kept from a pause is read here (ActionInput.ReadingKeys).
        input.ReadingKeys(request is not DelayRequest { ReportInput: false },
            request is not (KeyRequest { AsMenu: false } or TextRequest));
        if (Answer(request, input) is { } reply)
        {
            // Sell's and Id's yes/no over the Items page: its line stays on row 24 with the answer lit until row 24 is
            // written again (the menu routine draws the chosen entry once more, 0x2CF48; DOSBox-X s42: the scroll
            // warning's question under Sell's offer, the deal question under Id's result).
            if (request is ChoiceRequest { Vertical: false } tc && flow.TextOverPage is not null)
                _talkChoice = reply.Value is int ti and >= 0 && ti < tc.Items.Count ? (tc, ti) : null;
            else if (request is not DelayRequest || request is DelayRequest { BottomLine: not null })
                _talkChoice = null; // START 0x2233 writes row 24 and clears it after its pause
            runner.Answer(reply.Value);
            if (runner.Fault is { } e)
            {
                Console.Error.WriteLine("party menu: " + e);
                Problem = "ERROR: " + e.Message.ToUpperInvariant();
            }
            HandOver();
        }
    }

    /// <summary>A started game takes over; a finished menu (Exit) closes the window.</summary>
    private bool HandOver()
    {
        if (_switched)
            return true;
        if (Overlay)
        {
            var r = runner.Pending;
            // The icon editor asks only icon pages: after it (the camp's Alter Icon) the next question is the caller's.
            // View's page (the camp's, shop's, temple's, loot menu's) has only our notices over it: after it the next
            // question is the venue's.
            // Over View's pages only our notices (a key with a bottom-line text) and the drop amount stay, and while Sell
            // or Id talks over the Items page (flow.TextOverPage) their yes/no, pauses and row-24 messages.
            bool viewPages = _background is SheetRequest { IsView: true } or CharacterPageRequest;
            if (IsPage(r)
                || (viewPages && r is KeyRequest { Prompt.Length: > 0 } or TextRequest)
                // the money Trade's refusal (0x250D5: START 0x1693 on row 24) over its coin list
                || (_background is CharacterPageRequest { PickRows: true } && r is DelayRequest { BottomLine: not null })
                || (_background is CharacterPageRequest && flow.TextOverPage is not null
                    && r is ChoiceRequest { Vertical: false } or DelayRequest or KeyRequest)
                || (!viewPages && r is TextRequest or ChoiceRequest or KeyRequest && _background is not null and not IconRequest))
                return false;
            _switched = true;
            host.Pop();
            return true;
        }
        if (flow.Started is { } game)
        {
            _switched = true;
            var next = explore(game);
            if (next is ExploreScreen es)
                es.PartyMenuPage = _menuPage; // a Load's vector 4 draws over the page (ClientPlayer.PartyMenuUnder)
            host.Replace(next);
            return true;
        }
        if (!runner.Busy && Problem is null)
        {
            _switched = true;
            host.Quit();
            return true;
        }
        return false;
    }

    /// <summary>A list routine page: a <see cref="ListRequest"/>, or a pick shown as one (<see cref="PickRequest.List"/>: Add).</summary>
    private static ListRequest? AsList(PlayerRequest? r) => r as ListRequest ?? (r as PickRequest)?.List;

    private void Sync()
    {
        var r = runner.Pending;
        if (ReferenceEquals(r, _shown))
            return;
        _shown = r;
        _selected = 0;
        _word = 0;
        _typed = "";
        _hasClick = false;
        if (r is PartyMenuRequest or PickRequest or SheetRequest or IconRequest or ModifyRequest or CharacterPageRequest)
        {
            _background = r;
            flow.PickScreen = false; // a page draws over Trade's pick screen
        }
        if (r is CharacterPageRequest cp)
            _selected = cp.Page.Selected;
        if (r is DelayRequest d)
            _delayEnd = _clock.Elapsed + d.Duration;
        if (AsList(r) is { } l)
        {
            _list.Sync(l);
            if (l.SharedHighlight)
                _list.SetAction(l, flow.Highlight.Start(l.Actions.Count));
            else
                flow.Highlight.Index = _list.SelectedAction;
        }
        _word = r switch
        {
            // The party menu's own call of the menu routine (0x19C77) has an empty item line, so the shared cell is past
            // its last entry and goes back to 1 (0x2CCE7): View opens on its first word each time (DOSBox-X s39: Escape
            // with Drop lit, V again: Items lit).
            PartyMenuRequest => flow.Highlight.Start(0),
            SheetRequest s => flow.Highlight.Start(s.Actions.Count),
            // The list routine 0x2D669 stores 1 in the cell on entry (0x2D6B9; the flow did): its first word.
            CharacterPageRequest => flow.Highlight.Start(0),
            IconRequest ic => flow.Highlight.Start(ic.Actions.Count),
            ModifyRequest md => flow.Highlight.Start(md.Actions.Count),
            // The yes/no routine 0x2D9A0 first sets the cell to No (0x2D9B9), as ExploreScreen does.
            ChoiceRequest { Selected: > 0 } c => flow.Highlight.Index = c.Selected,
            ChoiceRequest c => flow.Highlight.Start(c.Items.Count),
            _ => 0,
        };
    }

    private readonly record struct Reply(object? Value);

    private static bool Key(ActionInput input, out char key)
    {
        key = '\0';
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } k))
        {
            key = k[0];
            return true;
        }
        return false;
    }

    private Reply? Answer(PlayerRequest request, ActionInput input)
    {
        if (_hasClick)
        {
            _hasClick = false;
            return new Reply(_clickAnswer);
        }
        switch (AsList(request) ?? request)
        {
            case PartyMenuRequest m:
            {
                int n = m.Items.Count;
                if (input.Pressed(GameAction.MenuUp)) _selected = (_selected + n - 1) % n;
                if (input.Pressed(GameAction.MenuDown)) _selected = (_selected + 1) % n;
                if (input.Pressed(GameAction.PreviousMember)) return new Reply(ChoiceRequest.PreviousMember);
                if (input.Pressed(GameAction.NextMember)) return new Reply(ChoiceRequest.NextMember);
                if (input.Pressed(GameAction.Confirm)) return new Reply(_selected);
                // The hidden J (0x19E2C), from its binding (the pad's left stick) as from the J key.
                if (input.Pressed(GameAction.HallJ)) return new Reply(PartyMenuFlow.PasswordAnswer);
                if (Key(input, out var k))
                    for (int i = 0; i < n; i++)
                        if (char.ToUpperInvariant(m.Items[i].Name[0]) == k)
                            return new Reply(i);
                return null;
            }
            case DelayRequest { ReportInput: true }:
                if (input.NewInput is not null || input.Pressed(GameAction.Confirm) || input.Pressed(GameAction.Cancel))
                    return new Reply(true);
                return _clock.Elapsed >= _delayEnd ? new Reply(false) : null;
            case DelayRequest:
                return _clock.Elapsed >= _delayEnd || input.SkipsPause ? new Reply(null) : null;
            case ListRequest l:
            {
                var a = _list.Update(l, input);
                if (a is null)
                    return null;
                flow.Highlight.Index = a.Action >= 0 && a.Action < l.Actions.Count ? a.Action : _list.SelectedAction;
                return new Reply(a);
            }
            case PickRequest p:
            {
                int n = p.Rows.Count, w = p.Actions.Count;
                if (n > 0 && input.Pressed(GameAction.MenuUp)) _selected = (_selected + n - 1) % n;
                if (n > 0 && input.Pressed(GameAction.MenuDown)) _selected = (_selected + 1) % n;
                if (w > 0 && input.Pressed(GameAction.MenuLeft)) _word = (_word + w - 1) % w;
                if (w > 0 && input.Pressed(GameAction.MenuRight)) _word = (_word + 1) % w;
                if (input.Pressed(GameAction.Cancel)) return new Reply(new ListAnswer(p.CancelAction, _selected));
                if (input.Pressed(GameAction.Confirm))
                    return new Reply(new ListAnswer(n > 0 ? PickRequest.RowChosen : p.CancelAction, _selected));
                if (Key(input, out var k))
                    for (int i = 0; i < w; i++)
                        if (p.Actions[i].Length > 0 && p.Actions[i][0] == k)
                            return new Reply(new ListAnswer(i, _selected));
                return null;
            }
            case SheetRequest s:
                if (s.PartySelect && input.Pressed(GameAction.PreviousMember)) return new Reply(ChoiceRequest.PreviousMember);
                if (s.PartySelect && input.Pressed(GameAction.NextMember)) return new Reply(ChoiceRequest.NextMember);
                // View: the program's Return gives 13, which its loop ignores (DOSBox-X s39); the pad's A takes the word.
                if (s.IsView && input.Pressed(GameAction.Confirm) && input.NewInput is (InputDevice.Keyboard, _))
                    return null;
                return Words(s.Actions, s.CancelAction, input);
            case CharacterPageRequest cp:
                return PageKeys(cp, input);
            case IconRequest ic:
                return Words(ic.Actions, ic.CancelAction, input, ic.Hotkeys);
            case ModifyRequest md:
                return ModifyKeys(md, input);
            case ChoiceRequest c:
                // Trade's member pick (START 0x37E8): Home / End move the lit member, Return selects whatever word is lit.
                if (c.PartySelect && input.Pressed(GameAction.PreviousMember)) return new Reply(ChoiceRequest.PreviousMember);
                if (c.PartySelect && input.Pressed(GameAction.NextMember)) return new Reply(ChoiceRequest.NextMember);
                if (c.ConfirmAnswer is { } confirm && input.Pressed(GameAction.Confirm)) return new Reply(confirm);
                return Words(c.Items, c.CancelIndex, input, c.Hotkeys);
            case TextRequest t:
                return TypeOrPick(t, input);
            case KeyRequest:
                return input.NewInput is not null || input.Pressed(GameAction.Confirm) ? new Reply(null) : null;
            default:
                return new Reply(null);
        }
    }

    /// <summary>
    /// A line of words on the bottom row (the menu routine 0x2CC8C): Left / Right, Return, the first letters (each moves
    /// the shared highlight), Esc (leaves it). With <paramref name="hotkeys"/> a word's key is its hotkey (digits too).
    /// </summary>
    private Reply? Words(IReadOnlyList<string> words, int? cancel, ActionInput input, string? hotkeys = null)
    {
        int w = words.Count;
        if (w == 0)
            return null;
        // ',' / '.' move the highlight and the shared cell with it (0x2CE93 / 0x2CEB7, wrapping); Left / Right do the
        // same (the Client's, as on every menu line). Escape then leaves the cell where it was moved.
        if (input.Pressed(GameAction.MenuLeft) || input.NewInput is (InputDevice.Keyboard, "OemComma"))
            flow.Highlight.Index = _word = (_word + w - 1) % w;
        if (input.Pressed(GameAction.MenuRight) || input.NewInput is (InputDevice.Keyboard, "OemPeriod"))
            flow.Highlight.Index = _word = (_word + 1) % w;
        if (input.Pressed(GameAction.Confirm)) return Chosen(_word);
        if (input.Pressed(GameAction.Cancel) && cancel is { } c) return new Reply(c);
        if (hotkeys is not null)
        {
            if (input.NewInput is (InputDevice.Keyboard, var name) && KeyChar(name) is { } hk)
                for (int i = 0; i < w && i < hotkeys.Length; i++)
                    if (hotkeys[i] == hk)
                        return Chosen(i);
            return null;
        }
        if (Key(input, out var k))
            for (int i = 0; i < w; i++)
                if (words[i].Length > 0 && words[i][0] == k)
                    return Chosen(i);
        return null;
    }

    /// <summary>
    /// The item list routine 0x2D669's keys on View's Items page and the coin list (DOSBox-X s39, the camp's Items page):
    /// Home / End and the keypad's 7 / 1 move the row (Up / Down too, the Client's: the program reads Down's scan code
    /// as 'P' and only redraws); ',' / '.' (and Left / Right) the lit word; a hotkey runs its word on the row; Escape
    /// the cancel word. A keyboard Return gives 13, which 0x21F32 does not act on, but the coin list takes the row
    /// whatever the key (0x234E1); the pad's A takes the lit word (the row on the coin list).
    /// </summary>
    private Reply? PageKeys(CharacterPageRequest cp, ActionInput input)
    {
        int n = cp.Page.Rows.Count, w = cp.Entries.Count;
        bool keys = input.LastDevice == InputDevice.Keyboard;
        if (n > 0 && (input.Pressed(GameAction.MenuUp) || input.Pressed(GameAction.PreviousMember) || (keys && input.Pressed(GameAction.StepUpLeft))))
            _selected = (_selected + n - 1) % n;
        if (n > 0 && (input.Pressed(GameAction.MenuDown) || input.Pressed(GameAction.NextMember) || (keys && input.Pressed(GameAction.StepDownLeft))))
            _selected = (_selected + 1) % n;
        cp.Page.Selected = _selected;
        if (w > 0 && (input.Pressed(GameAction.MenuLeft) || input.NewInput is (InputDevice.Keyboard, "OemComma")))
            flow.Highlight.Index = _word = (_word + w - 1) % w;
        if (w > 0 && (input.Pressed(GameAction.MenuRight) || input.NewInput is (InputDevice.Keyboard, "OemPeriod")))
            flow.Highlight.Index = _word = (_word + 1) % w;
        if (input.Pressed(GameAction.Cancel))
            return new Reply(new ListAnswer(cp.CancelEntry, _selected));
        if (input.Pressed(GameAction.Confirm))
        {
            if (cp.PickRows && n > 0)
                return new Reply(new ListAnswer(PageRequest.RowPicked, _selected));
            if (input.NewInput is (InputDevice.Keyboard, _))
                return null;
            flow.Highlight.Chosen(_word);
            return new Reply(new ListAnswer(_word, _selected));
        }
        if (Key(input, out var k))
            for (int i = 0; i < w; i++)
                if (cp.Entries[i].Length > 0 && cp.Entries[i][0] == k)
                {
                    flow.Highlight.Chosen(i);
                    _word = i;
                    return new Reply(new ListAnswer(i, _selected));
                }
        return null;
    }

    /// <summary>
    /// Modify's keys (0x1BA1E with the menu routine's keypad flag): Up / Down move the field, Left / Right step it, K / E
    /// the words, Escape Exit; Home / End (LB / RB) move the word highlight (the program's ',' / '.'). The program
    /// ignores Return here; the pad's A (and a mouse-made Confirm) take the highlighted word, so the pad can keep.
    /// </summary>
    private Reply? ModifyKeys(ModifyRequest md, ActionInput input)
    {
        int w = md.Actions.Count;
        if (input.Pressed(GameAction.MenuUp)) return new Reply(ModifyKey.Up);
        if (input.Pressed(GameAction.MenuDown)) return new Reply(ModifyKey.Down);
        if (input.Pressed(GameAction.MenuLeft)) return new Reply(ModifyKey.Left);
        if (input.Pressed(GameAction.MenuRight)) return new Reply(ModifyKey.Right);
        // The keypad flag: the digits 2, 8, 4, 6 (top row or keypad) are the arrows.
        if (input.NewInput is (InputDevice.Keyboard, var name) && name is "D2" or "D8" or "D4" or "D6" or "NumPad2"
                or "NumPad8" or "NumPad4" or "NumPad6")
            return new Reply(name[^1] switch
            {
                '2' => ModifyKey.Down,
                '8' => ModifyKey.Up,
                '4' => ModifyKey.Left,
                _ => ModifyKey.Right,
            });
        if (w > 0 && input.Pressed(GameAction.PreviousMember)) flow.Highlight.Index = _word = (_word + w - 1) % w;
        if (w > 0 && input.Pressed(GameAction.NextMember)) flow.Highlight.Index = _word = (_word + 1) % w;
        if (input.Pressed(GameAction.Cancel)) return new Reply(ModifyKey.Exit);
        if (input.Pressed(GameAction.Confirm))
            return input.NewInput is (InputDevice.Keyboard, _) || w == 0 ? new Reply(ModifyKey.Other) : ModifyWord(_word);
        if (Key(input, out var k))
            for (int i = 0; i < w; i++)
                if (md.Actions[i].Length > 0 && md.Actions[i][0] == k)
                    return ModifyWord(i);
        return null;
    }

    /// <summary>Modify's words in the program's order: Keep, then Exit.</summary>
    private Reply ModifyWord(int word)
    {
        flow.Highlight.Chosen(word);
        return new Reply(word == 0 ? ModifyKey.Keep : ModifyKey.Exit);
    }

    /// <summary>Modify's field under a cell: an ability's row (7–12) or the hit points' (18), label and value columns.</summary>
    private static int? ModifyFieldAt(int col, int row) => row switch
    {
        >= 7 and <= 12 when col is >= 1 and <= 10 => row - 7,
        18 when col is >= 1 and <= 7 => CharacterModify.HpField,
        _ => null,
    };

    private Reply Chosen(int word)
    {
        flow.Highlight.Chosen(word);
        return new Reply(word);
    }

    // Name entry: typing on the keyboard, or a character grid for pad and mouse (as the explore screen's).
    private const string Letters = CharGrid.Letters;
    private const int GridCols = CharGrid.Columns, GridRow = 14;

    private static (int Col, int Row, string Label) Cell(int k) => CharGrid.Cell(Letters, GridRow, k);

    private static readonly int Cells = CharGrid.CellCount(Letters);

    private static int? CellAt(int col, int row) => CharGrid.CellAt(Letters, GridRow, col, row);

    private static char? KeyChar(string name) => CharGrid.KeyChar(name);

    private void Type(TextRequest t, char ch)
    {
        if (t.Maximum is { } max)
        {
            // The money input 0x24F1D takes digits only; a number above the maximum becomes the maximum at once.
            if (ch is < '0' or > '9')
                return;
            // A full line stays as it is (DOS: the string[6] keeps its 6 characters; only a leading zero gets there).
            string next = _typed + ch;
            if (next.Length > t.MaxLength)
                return;
            _typed = int.Parse(next, System.Globalization.CultureInfo.InvariantCulture) > max
                ? max.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : next;
            return;
        }
        if (_typed.Length < t.MaxLength && t.Takes(ch))
            _typed += ch;
    }

    private Reply? TypeOrPick(TextRequest t, ActionInput input)
    {
        if (input.NewInput is (InputDevice.Keyboard, var name))
        {
            if (name == "Enter")
                return new Reply(_typed);
            if (name == "Escape" && t.Maximum is not null)
                return new Reply(""); // 0x24F1D answers 0 to Escape
            if (name == "Escape" && !AmigaView.On)
                return new Reply(_typed); // creation's name (0x1B7BE → START 0x7A73): Escape ends the line as Return does
            if (name == "Back")
            {
                if (_typed.Length > 0)
                    _typed = _typed[..^1];
                return null;
            }
            if (CharGrid.KeyChar(name, input, t.KeepsCase) is { } ch)
            {
                Type(t, ch);
                return null;
            }
            if (name is not ("Up" or "Down" or "Left" or "Right" or "Escape"))
                return null;
        }
        if (input.Pressed(GameAction.MenuLeft)) _selected = (_selected + Cells - 1) % Cells;
        if (input.Pressed(GameAction.MenuRight)) _selected = (_selected + 1) % Cells;
        if (input.Pressed(GameAction.MenuUp)) _selected = Math.Max(0, _selected - GridCols);
        if (input.Pressed(GameAction.MenuDown)) _selected = Math.Min(Cells - 1, _selected + GridCols);
        if (input.Pressed(GameAction.Cancel) && _typed.Length == 0 && t.Maximum is not null)
            return new Reply(""); // the money input: B with nothing typed is Escape (0)
        if (input.Pressed(GameAction.Cancel) && _typed.Length > 0)
            _typed = _typed[..^1];
        if (input.Pressed(GameAction.Confirm))
        {
            if (_selected < Letters.Length)
                Type(t, Letters[_selected]);
            else if (_selected == Letters.Length)
            {
                if (_typed.Length > 0)
                    _typed = _typed[..^1];
            }
            else
                return new Reply(_typed);
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Mouse
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The word under a column of a line of words starting at <paramref name="start"/>.</summary>
    private static int? WordAt(IReadOnlyList<string> words, int col, int start)
    {
        int c = start;
        for (int i = 0; i < words.Count; i++)
        {
            if (col >= c && col < c + words[i].Length)
                return i;
            c += words[i].Length + 1;
        }
        return null;
    }

    /// <summary>
    /// First column of the words: right after the prompt (it ends with its own space); with no prompt, column 1 for the
    /// vertical menu's words (DOSBox-X: the creation lists' EXIT), column 0 for the others.
    /// </summary>
    private static int WordsStart(string? prompt, bool vertical = false) =>
        prompt is { Length: > 0 } p ? p.Length : vertical ? 1 : 0;

    /// <summary>The last yes/no answered while Sell or Id talks over the Items page, and its answer: row 24 keeps it.</summary>
    private (ChoiceRequest Choice, int Lit)? _talkChoice;

    /// <summary>A bottom-line menu's words: <see cref="Dialog"/> trims its prompts, so one space after a trimmed one
    /// (DOSBox-X s41: Id's deal question over the Items page).</summary>
    private static int ChoiceStart(string? prompt) =>
        prompt is { Length: > 0 } p && !p.EndsWith(' ') ? p.Length + 1 : WordsStart(prompt);

    public void Hover(int x, int y)
    {
        Sync();
        int col = x / 8, row = y / 8;
        switch (AsList(runner.Pending) ?? runner.Pending)
        {
            case ListRequest l:
                _list.Hover(l, col, row);
                break;
            case PartyMenuRequest m when row >= ItemsRow && row < ItemsRow + m.Items.Count:
                _selected = row - ItemsRow;
                break;
            case PickRequest p when row >= p.FirstRow && row - p.FirstRow < p.Rows.Count && row < 23:
                _selected = row - p.FirstRow;
                break;
            case PickRequest p when row == MenuRow && WordAt(p.Actions, col, WordsStart(p.Prompt, true)) is { } w:
                _word = w;
                break;
            case SheetRequest s when row == MenuRow && WordAt(s.Actions, col, WordsStart(s.Prompt)) is { } w:
                _word = w;
                break;
            case IconRequest ic when row == MenuRow && WordAt(ic.Actions, col, WordsStart(ic.Prompt)) is { } w:
                _word = w;
                break;
            case ModifyRequest md when row == MenuRow && WordAt(md.Actions, col, WordsStart(md.Prompt)) is { } w:
                _word = w;
                break;
            case ChoiceRequest c when row == MenuRow && WordAt(c.Items, col, ChoiceStart(c.MenuPrompt)) is { } w:
                _word = w;
                break;
            case TextRequest when CellAt(col, row) is { } k:
                _selected = k;
                break;
        }
    }

    public GameAction? MapClick(ScreenClick click)
    {
        Sync();
        int col = click.X / 8, row = click.Y / 8;
        void Give(object? v)
        {
            _clickAnswer = v;
            _hasClick = true;
        }
        switch (AsList(runner.Pending) ?? runner.Pending)
        {
            case ListRequest l:
                return _list.Click(l, click);
            case PartyMenuRequest m:
                if (click.Right)
                    return null;
                if (row >= ItemsRow && row < ItemsRow + m.Items.Count)
                    Give(row - ItemsRow);
                // The prompt on the bottom line: the hidden J for the mouse (the original shows no entry for it).
                else if (row == MenuRow && m.Notice is null && col < m.Prompt.Length)
                    Give(PartyMenuFlow.PasswordAnswer);
                else if (row >= RosterRow && row - RosterRow < flow.Party.Members.Count && row < ItemsRow)
                {
                    // A click on a member makes it current: step to it.
                    int target = row - RosterRow, cur = Math.Max(0, flow.Party.CurrentIndex);
                    if (target != cur)
                        Give(target > cur ? ChoiceRequest.NextMember : ChoiceRequest.PreviousMember);
                }
                return null;
            case PickRequest p:
                if (click.Right)
                    return GameAction.Cancel;
                if (row >= p.FirstRow && row - p.FirstRow < p.Rows.Count && row < 23)
                    Give(new ListAnswer(PickRequest.RowChosen, row - p.FirstRow));
                else if (row == MenuRow && WordAt(p.Actions, col, WordsStart(p.Prompt, true)) is { } w)
                    Give(new ListAnswer(w, _selected));
                return null;
            case CharacterPageRequest cp:
            {
                if (click.Right)
                    return GameAction.Cancel;
                if (row == MenuRow && WordAt(cp.Entries, col, WordsStart(cp.Prompt)) is { } pw)
                {
                    flow.Highlight.Chosen(pw);
                    Give(new ListAnswer(pw, _selected));
                }
                else if (PageChrome.RowAt(cp.Page, row) is { } pr)
                {
                    // A click on the selected row: the coin list takes it; the Items page runs the lit word on it.
                    if (pr == _selected)
                        Give(new ListAnswer(cp.PickRows ? PageRequest.RowPicked : _word, pr));
                    cp.Page.Selected = _selected = pr;
                }
                return null;
            }
            case SheetRequest s:
                if (click.Right)
                    return s.CancelAction is not null ? GameAction.Cancel : null;
                if (row == MenuRow && WordAt(s.Actions, col, WordsStart(s.Prompt)) is { } sw)
                {
                    flow.Highlight.Chosen(sw);
                    Give(sw);
                }
                return null;
            case IconRequest ic:
                if (click.Right)
                    return ic.CancelAction is not null ? GameAction.Cancel : null;
                if (row == MenuRow && WordAt(ic.Actions, col, WordsStart(ic.Prompt)) is { } iw)
                {
                    flow.Highlight.Chosen(iw);
                    Give(iw);
                }
                return null;
            case ModifyRequest md:
            {
                // A click on a field moves the cursor there; on the cursor's field, left raises and right lowers it.
                // A right click off the fields is Exit.
                var field = ModifyFieldAt(col, row);
                if (click.Right)
                {
                    if (field is not { } rf)
                        return GameAction.Cancel;
                    Give(rf == md.Field ? ModifyKey.Left : rf);
                }
                else if (field is { } fl)
                    Give(fl == md.Field ? ModifyKey.Right : fl);
                else if (row == MenuRow && WordAt(md.Actions, col, WordsStart(md.Prompt)) is { } mw)
                {
                    flow.Highlight.Chosen(mw);
                    Give(mw == 0 ? ModifyKey.Keep : ModifyKey.Exit);
                }
                return null;
            }
            case ChoiceRequest c:
                if (click.Right)
                    return c.CancelIndex is not null ? GameAction.Cancel : null;
                if (row == MenuRow && WordAt(c.Items, col, ChoiceStart(c.MenuPrompt)) is { } cw)
                {
                    flow.Highlight.Chosen(cw);
                    Give(cw);
                }
                else if (c.PartySelect && flow.PickScreen && row >= 4 && row - 4 < flow.Party.Members.Count && row < 12)
                    Give(TradeContext.MemberPick - (row - 4)); // a click on a name of Trade's pick (ours)
                return null;
            case TextRequest:
                if (click.Right)
                    return GameAction.Cancel;
                if (CellAt(col, row) is { } k)
                {
                    _selected = k;
                    return GameAction.Confirm;
                }
                return null;
            case KeyRequest:
                return GameAction.Confirm;
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The menu page's pixels above row 24 as last drawn (null before the first draw).</summary>
    private byte[]? _menuPage;

    public void Draw(IndexedFrame f, ActionInput input)
    {
        Sync(); // a request answered this frame is drawn with its own highlight at once (no frame with the old one)
        var pending = runner.Pending;
        if (AsList(pending) is { } list)
        {
            _list.Draw(f, font, list, input);
            return;
        }
        f.Clear(0);
        var back = pending is ChoiceRequest or TextRequest or KeyRequest or DelayRequest ? _background : pending;
        if (flow.Hall.Page is { } page)
        {
            // Train's page (0x1C32D) over the menu window: "NAME will become:" and the levels.
            Chrome.Box(f, 0, 0, 39, 23);
            foreach (var (col, row, text, colour) in page)
                Chrome.Text(f, font, col, row, Fit(text, 39 - col), colour);
            back = null;
        }
        if (flow.PickScreen && pending is ChoiceRequest or DelayRequest or KeyRequest)
        {
            // Trade's pick in the party menu or the hall (START 0x3379 case 0, then 00BA:2CA1): the frame, nothing inside
            // but the party list with the pick lit (nobody when there is no pick).
            Chrome.Box(f, 0, 0, 39, 23);
            var party = flow.Party;
            Chrome.PartyList(f, font, party, 1, flow.PickLit is { } lit ? party.Members.IndexOf(lit) : -1);
            back = null;
        }
        switch (back)
        {
            case PartyMenuRequest m:
                DrawMenu(f, m, input, pending == m);
                // The page above row 24 as it stands, for a Load's vector 4 to draw over.
                _menuPage ??= new byte[f.Width * 8 * 24]; // rows 0–23
                if (_menuPage.Length <= f.Pixels.Length)
                    Array.Copy(f.Pixels, _menuPage, _menuPage.Length);
                break;
            case PickRequest p:
                DrawPick(f, p, input);
                break;
            case SheetRequest s:
                // The Amiga's portrait menu (hunk 10 +0x374A) draws only the portrait window (hunk 19 +0x14C4) over the
                // sheet the keep question left, drawn before the name was asked: no name row (FS-UAE s46).
                DrawSheet(f, s.Character, hideName: s.IsPortraitMenu && AmigaView.On);
                break;
            case CharacterPageRequest cp:
                // The list routine draws its rows again without the highlight when it returns (DOSBox-X s39: the coin
                // rows all green while the amount is asked).
                DrawCharacterPage(f, cp, lit: pending == cp);
                break;
            case IconRequest ic:
                DrawIcons(f, ic);
                break;
            case ModifyRequest md:
                DrawSheet(f, md.Character, md.Field);
                break;
            case null when flow.Hall.Page is not null:
                break;
            default:
                Chrome.Box(f, 0, 0, 39, 23);
                break;
        }
        switch (pending)
        {
            case PartyMenuRequest m:
                Line(f, m.Notice ?? m.Prompt, m.Notice is null ? PromptColour : Chrome.Yellow, [], -1);
                break;
            case PickRequest p:
                Line(f, p.Prompt, PromptColour, p.Actions, _word, WordsStart(p.Prompt, true));
                break;
            case SheetRequest s:
                Line(f, s.Prompt, PromptColour, s.Actions, _word, amigaPens: s.IsPortraitMenu ? AmigaView.PortraitMenuPens : null);
                break;
            case CharacterPageRequest cp:
                Line(f, cp.Prompt, PromptColour, cp.Entries, _word);
                break;
            case IconRequest ic:
                Line(f, ic.Prompt, PromptColour, ic.Actions, _word, hotkeys: ic.Hotkeys);
                break;
            case ModifyRequest md:
                Line(f, md.Prompt, PromptColour, md.Actions, _word);
                break;
            case ChoiceRequest c:
                Line(f, c.MenuPrompt, c.MenuPromptColour ?? PromptColour, c.Items, _word, ChoiceStart(c.MenuPrompt));
                break;
            case TextRequest t when back is CharacterPageRequest { PickRows: true } && input.LastDevice == InputDevice.Keyboard && !AmigaView.On:
                // Drop's amount (input 0x24F1D; DOSBox-X s39): the question in green, a space, the digits typed in white,
                // no cursor.
                Chrome.Clear(f, 0, MenuRow, 40);
                string question = Fit(AmigaView.Case(t.Prompt) + " ", 40);
                Chrome.Text(f, font, 0, MenuRow, question, Chrome.Green);
                Chrome.Text(f, font, question.Length, MenuRow, Fit(_typed, 40 - question.Length), Chrome.White);
                break;
            case TextRequest t:
                DrawTextEntry(f, t, input);
                break;
            case KeyRequest { Prompt: { Length: > 0 } kp } k:
                // Our notices over a page (View's commands not built yet): the bottom line, then a key.
                Line(f, kp, k.Colour ?? PromptColour, [], -1);
                break;
        }
        if (back is CharacterPageRequest && flow.TextOverPage is { } box && pending is not CharacterPageRequest)
        {
            // Sell's and Id's text (0x22D28, 0x22F8C, 0x21DAA: START 0x77F8 in the box (1, 21)–(38, 22), colour 14)
            // straight over the page, no frame; their row-24 messages (START 0x2233) during the pause.
            if (flow.TextOverPageFramed)
            {
                // An item handler's text from Ready (0x2250A → 0x2AEEA, e.g. 131): START 0x16EC outside a fight draws
                // the text window's frame again, blanks (1, 17)–(38, 22), the name on row 18 and the text from row 19
                // (DOSBox-X s51: the gauntlets over the camp's Items page). Its end (START 0x2383) clears the box.
                Chrome.Box(f, TextWindow.Left - 1, TextWindow.Top - 1, TextWindow.Right + 1, TextWindow.Bottom + 1);
                for (int row = TextWindow.Top; row <= TextWindow.Bottom; row++)
                    Chrome.Clear(f, TextWindow.Left, row, TextWindow.Right - TextWindow.Left + 1);
                Chrome.TextRuns(f, font, box.Runs(TextWindow.Top, TextWindow.Bottom));
            }
            for (int row = 21; row <= 22; row++)
                Chrome.Clear(f, 1, row, 38);
            Chrome.TextRuns(f, font, box.Runs(21, 22));
            if (pending is DelayRequest { BottomLine: { } bl })
            {
                Chrome.Clear(f, 0, MenuRow, 40);
                Chrome.Text(f, font, 0, MenuRow, Fit(bl.Text, 40), bl.Colour);
            }
            else if (pending is DelayRequest && _talkChoice is var (kc, lit))
                Line(f, kc.MenuPrompt, kc.MenuPromptColour ?? PromptColour, kc.Items, lit, ChoiceStart(kc.MenuPrompt));
            else if (pending is DelayRequest && back is CharacterPageRequest itemsPage)
                // Row 24 keeps the page's menu with the chosen entry lit while the text types and pauses (DOSBox-X
                // s41: the page menu with Id lit under Id's offer).
                Line(f, itemsPage.Prompt, PromptColour, itemsPage.Entries, flow.Highlight.Index);
        }
        if (pending is DelayRequest && flow.DeveloperRow is var (devLines, devPen))
        {
            // Creation's developer lines under the Amiga (hunk 10 +0x2148–0x2330): each through the string routine at
            // (0, 24) in the program's pen, over the one before it: its JAM2 pass blanks its own cells only, then the
            // two passes (a longer line's tail and the pixel its last glyph's second pass put past it stay; FS-UAE s45).
            Chrome.Clear(f, 0, MenuRow, 40);
            foreach (var line in devLines)
            {
                string t = Fit(line, 39);
                Chrome.Clear(f, 0, MenuRow, t.Length);
                font.DrawAmiga(f, 0, MenuRow * 8, t, devPen);
            }
        }
        if (back is IconRequest)
            CombatScreen.SwapGrey(f); // the icon editor's palette, row 24 included (D s39)
        // J's message and Train's (START 0x7DF8 / 0x2233: bottom line, during the pause).
        if (flow.Hall.Message is { } msg)
        {
            Chrome.Clear(f, 0, MenuRow, 40);
            Chrome.Text(f, font, 0, MenuRow, Fit(msg.Text, 40), msg.Colour);
        }
        if (Problem is { } p2)
            Chrome.Text(f, font, 0, MenuRow, Fit(p2, 40), Chrome.Yellow);
    }

    /// <summary>The bottom line: a prompt in colour 13, then words drawn as the menu routine 0x2CC8C draws them
    /// (<see cref="ExploreScreen.DrawLine"/>: the hotkey is the first letter, or the word's character in
    /// <paramref name="hotkeys"/>; the lit entry runs from its hotkey to two cells before the next one).</summary>
    private void Line(IndexedFrame f, string? prompt, byte promptColour, IReadOnlyList<string> words, int selected,
        int? start = null, string? hotkeys = null, ExploreScreen.AmigaMenuPens? amigaPens = null)
    {
        Chrome.Clear(f, 0, MenuRow, 40);
        if (prompt is { Length: > 0 })
            Chrome.MenuPrompt(f, font, 0, MenuRow, Fit(AmigaView.Case(prompt), 40), promptColour);
        // D s39: the icon editor's "Parts color-1 color-2 Size Exit" (hotkeys P 1 2 S E): Parts lit shows PARTS COLOR
        // white, the '-' green and the 1 white; color-1 lit shows its 1 and the next word's COLOR white.
        ExploreScreen.DrawLine(f, font, words, selected, start ?? WordsStart(prompt),
            hotkeys is null ? null : i => i < hotkeys.Length ? hotkeys[i] : words[i].Length > 0 ? words[i][0] : '\0', amigaPens: amigaPens);
    }

    private void DrawMenu(IndexedFrame f, PartyMenuRequest m, ActionInput input, bool active)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        var party = flow.Party;
        if (party.Members.Count > 0)
            Chrome.PartyList(f, font, party, 1);
        for (int i = 0; i < m.Items.Count; i++)
        {
            string s = AmigaView.Case(m.Items[i].Name);   // the Amiga keeps the entries' case (FS-UAE s43)
            Chrome.Text(f, font, 2, ItemsRow + i, s[..1], Chrome.White);
            Chrome.Whole(f, font, 3, ItemsRow + i, s[1..], Chrome.Green);   // hunk 10 +0x7EE: the rest a whole string
        }
        // The original marks no entry; for the pad and the mouse the picked one gets a pointer.
        if (active && input.LastDevice != InputDevice.Keyboard && m.Items.Count > 0)
            Chrome.Text(f, font, 1, ItemsRow + Math.Clamp(_selected, 0, m.Items.Count - 1), ">", Chrome.Yellow);
    }

    private void DrawPick(IndexedFrame f, PickRequest p, ActionInput input)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        // Under the Amiga the rows keep the program's case (FS-UAE s43: the race, gender, class and alignment lists) and the lit row
        // is the list routine's bar (Chrome.Lit).
        if (p.Title is { } title)
            Chrome.Text(f, font, 1, 2, Fit(AmigaView.Case(title), 38), PromptColour);
        for (int i = 0; i < p.Rows.Count && p.FirstRow + i < 23; i++)
        {
            string row = Fit(AmigaView.Case(p.Rows[i]), 38 - p.Column);
            if (i == _selected)
                Chrome.Lit(f, font, p.Column, p.FirstRow + i, row, Chrome.White, listRow: true);
            else
                Chrome.Text(f, font, p.Column, p.FirstRow + i, row, Chrome.Green);
        }
    }

    /// <summary>
    /// The character page (View 0x21084) with the portrait window at columns 27–39, rows 0–12. Modify's field cursor
    /// (<paramref name="field"/>) draws that value in colour 13: an ability (0x2193B, with Strength's exceptional part)
    /// or the hit points (START 0x973).
    /// </summary>
    private void DrawSheet(IndexedFrame f, EclCharacter c, int? field = null, bool hideName = false)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        byte[]? weapon = null, armour = null;
        if (c.Items.Count > 0 && Items is { } table)
        {
            var eq = FightRules.Equipment(c, table);
            weapon = eq[FightRules.SlotWeapon];
            armour = eq[FightRules.SlotBodyArmour];
        }
        // Under the Amiga art the Amiga's page (AmigaView): the name on row 2, the hit points in their Amiga colours.
        var amiga = new List<(int Col, int Row, string Text, int AmigaColour)>();
        int nameRow = AmigaView.On ? AmigaView.NameRow : 1;
        foreach (var (col, row, text, colour) in AmigaView.Sheet(CombatSheet.Build(resources, c, weapon, armour,
                     it => AmigaView.Case(ItemNames.Compose(it, resources.ItemWord).Trim())), c, amiga))
            if ((c.Name.Length > 0 && !hideName) || row != nameRow)
                Chrome.Text(f, font, col, row, Fit(text, 40 - col),
                    field is { } h && (h < 6 ? row == 7 + h && col is >= 5 and <= 7 : row == 18 && col == 4)
                        ? PromptColour : colour);
        foreach (var (col, row, text, colour) in amiga)
            if (field is 6 || !font.DrawAmiga(f, col * 8, row * 8, text, colour))
                Chrome.Text(f, font, col, row, text, field is 6 ? PromptColour : colour == 8 ? Chrome.Yellow : Chrome.Green);
        // The portrait window only with portraits on and the disk byte 3 (0x21530; DOSBox-X s25); the Amiga has no disk
        // test (AmigaView).
        if (!flow.PortraitShown)
            return;
        Chrome.Box(f, AmigaView.PortraitCol0, AmigaView.PortraitRow0, AmigaView.PortraitCol1, AmigaView.PortraitRow1);
        int head = c[CharacterCreation.PortraitHead], body = c[CharacterCreation.PortraitBody];
        if (head > 0 && body > 0)
        {
            int headId = Program is { } p ? p.Ds.ElementAtOrDefault(HeadTable + head) : head;
            int bodyId = AmigaView.BodyId(body, Program is { } q ? q.Ds.ElementAtOrDefault(BodyTable + body) : body);
            art.Pictures.Draw(f, 28 * 8, 8, new ViewPicture(ViewPictureKind.Portrait, bodyId, headId, 0, PortraitDisk));
        }
    }

    /// <summary>
    /// View's Items page (0x21F32, <see cref="ItemsPages"/>) on its own frame, or the coin list of drop money (0x234E1)
    /// over the View page: the money lines' box (0x21547: columns 12–26, rows 7–14) holds the coin rows, the selected
    /// one lit.
    /// </summary>
    private void DrawCharacterPage(IndexedFrame f, CharacterPageRequest cp, bool lit)
    {
        var p = cp.Page;
        if (lit)
            p.Selected = _selected;
        if (cp.Sheet is not { } c)
        {
            PageChrome.DrawBody(f, font, p, lit);
            return;
        }
        DrawSheet(f, c);
        for (int r = 7; r <= 14; r++)
            Chrome.Clear(f, 12, r, 15);
        for (int i = 0; i < p.Rows.Count && p.FirstRow + i <= 14; i++)
        {
            if (lit && i == p.Selected)
                Chrome.Lit(f, font, p.RowCol, p.FirstRow + i, p.Rows[i], Chrome.White, listRow: true);
            else
                Chrome.Text(f, font, p.RowCol, p.FirstRow + i, p.Rows[i], Chrome.Green);
        }
    }

    /// <summary>The item types (for the readied weapon and armour on the page), when known.</summary>
    public BaseItemTable? Items { get; init; }

    /// <summary>The icon editor's page: the icon before the editor (old) and now (new), ready and action frames.</summary>
    private void DrawIcons(IndexedFrame f, IconRequest ic)
    {
        // The ground is colour 0 under the combat palette (0x1D1D9 → GAME.OVR 0x3081D: EGA registers 0 and 8 swapped,
        // restored at 0x1D8A7 after the IconOk question), so it shows dark grey, row 24 and the frame's gaps too; Draw
        // swaps the whole frame (CombatScreen.SwapGrey). The boxes below give the colours as seen, swapped back here.
        Chrome.Box(f, 0, 0, 39, 23);
        var old = new EclCharacter(ic.OldRecord);
        // Each frame over a 24 × 24 box (DOSBox-X s19: ready on light grey with a white / black bevel, action on dark
        // grey with a light grey / black bevel; [?] the program draws icon slot 25's frames there, not decoded).
        static byte Seen(byte c) => c switch { 0 => 8, 8 => 0, _ => c };
        void Box(int x0, int y0, byte fill, byte light)
        {
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                    f[x0 + x, y0 + y] = Seen(x < 2 || y < 2 ? light : x >= 22 || y >= 22 ? (byte)0 : fill);
        }
        void Pair(EclCharacter c, int y, string label)
        {
            Chrome.Text(f, font, 8, y / 8 - 1, label, Chrome.White);
            Box(32, y, 7, 15);
            Box(104, y, 8, 7);
            for (int k = 0; k < 2; k++)
                if (art.Combat.Party(c, k == 1) is { } pic)
                    CombatArt.Draw(f, pic, 32 + 72 * k, y, false, (0, 0, 320, 200));
            Chrome.Text(f, font, 3, y / 8 + 3, AmigaView.Case(Fit(Texts("IconFrames") ?? "READY   ACTION", 30)), Chrome.White);
        }
        Pair(old, 56, AmigaView.Case((Texts("IconOld") ?? "OLD")));
        Pair(ic.Character, 104, AmigaView.Case((Texts("IconNew") ?? "NEW")));
    }

    private string? Texts(string key) => Program?.Text("PartyMsg." + key);

    private void DrawTextEntry(IndexedFrame f, TextRequest t, ActionInput input)
    {
        string prompt = AmigaView.Case(t.Prompt);   // the requester draws its prompt as it is (FS-UAE s43: creation's name prompt)
        Chrome.Clear(f, 0, MenuRow, 40);
        if (AmigaView.On && Chrome.AmigaGadget is { } g)
            g.Requester(f, font, prompt, _typed, t.MaxLength + 1);   // the Amiga's input requester (hunk 25 +0xEC6)
        else
        {
            Chrome.Text(f, font, 0, MenuRow, Fit(prompt, 40), PromptColour);
            Chrome.Text(f, font, prompt.Length, MenuRow, Fit(_typed + "_", Math.Max(0, 40 - prompt.Length)), Chrome.White);
        }
        if (input.LastDevice == InputDevice.Keyboard)
            return;
        // The pick grid for the pad and the mouse, over the page's middle rows.
        for (int r = GridRow - 1; r <= GridRow + 4; r++)
            Chrome.Clear(f, 1, r, 38);
        for (int k = 0; k < Cells; k++)
        {
            var (c, r, label) = Cell(k);
            Chrome.Text(f, font, c, r, label, k == _selected ? Chrome.White : Chrome.Green);
            if (k == _selected)
                Chrome.Text(f, font, c - 1, r, ">", Chrome.Yellow);
        }
    }

    private static string Fit(string s, int n) => n <= 0 ? "" : s.Length > n ? s[..n] : s;
}
