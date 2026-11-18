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
    private int _selected, _word;
    private string _typed = "";
    private bool _switched;
    private object? _clickAnswer;
    private bool _hasClick;

    /// <summary>The program data the portrait tables are read from (null: the head and body ids as picked).</summary>
    public PorProgramData? Program { get; init; }

    /// <summary>
    /// Over the training hall (the explore screen pushes it when the hall's Create or Add asks for a creation page): it
    /// answers the pages of <see cref="PartyMenuFlow"/> and closes itself when the hall's menu (or anything else) asks.
    /// </summary>
    public bool Overlay { get; init; }

    /// <summary>The requests the overlay answers: the creation and roster pages, and the questions asked over them.</summary>
    public static bool IsPage(PlayerRequest? r) => r is PickRequest or SheetRequest or IconRequest or ModifyRequest;

    /// <summary>A message for the bottom line (a fault of the game logic).</summary>
    public string? Problem { get; private set; }

    // ---------------------------------------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------------------------------------

    public void Update(ActionInput input)
    {
        if (input.Pressed(GameAction.Controls))
        {
            host.Push(new ControlsScreen(font, host, art));
            return;
        }
        if (HandOver())
            return;
        Sync();
        if (runner.Pending is not { } request)
            return;
        if (Answer(request, input) is { } reply)
        {
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
            if (IsPage(r) || (r is TextRequest or ChoiceRequest or KeyRequest && _background is not null))
                return false;
            _switched = true;
            host.Pop();
            return true;
        }
        if (flow.Started is { } game)
        {
            _switched = true;
            host.Replace(explore(game));
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
        if (r is PartyMenuRequest or PickRequest or SheetRequest or IconRequest or ModifyRequest)
            _background = r;
        _word = r switch
        {
            SheetRequest s => flow.Highlight.Start(s.Actions.Count),
            IconRequest ic => flow.Highlight.Start(ic.Actions.Count),
            ModifyRequest md => flow.Highlight.Start(md.Actions.Count),
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
        switch (request)
        {
            case PartyMenuRequest m:
            {
                int n = m.Items.Count;
                if (input.Pressed(GameAction.MenuUp)) _selected = (_selected + n - 1) % n;
                if (input.Pressed(GameAction.MenuDown)) _selected = (_selected + 1) % n;
                if (input.Pressed(GameAction.PreviousMember)) return new Reply(ChoiceRequest.PreviousMember);
                if (input.Pressed(GameAction.NextMember)) return new Reply(ChoiceRequest.NextMember);
                if (input.Pressed(GameAction.Confirm)) return new Reply(_selected);
                if (Key(input, out var k))
                    for (int i = 0; i < n; i++)
                        if (char.ToUpperInvariant(m.Items[i].Name[0]) == k)
                            return new Reply(i);
                return null;
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
                return Words(s.Actions, s.CancelAction, input);
            case IconRequest ic:
                return Words(ic.Actions, ic.CancelAction, input, ic.Hotkeys);
            case ModifyRequest md:
                return ModifyKeys(md, input);
            case ChoiceRequest c:
                return Words(c.Items, c.CancelIndex, input);
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
        if (input.Pressed(GameAction.MenuLeft)) _word = (_word + w - 1) % w;
        if (input.Pressed(GameAction.MenuRight)) _word = (_word + 1) % w;
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
    private const string Letters = "ABCDEFGHIJKLM" + "NOPQRSTUVWXYZ" + "0123456789 -.";
    private const int GridCols = 13, GridRow = 14;

    private static (int Col, int Row, string Label) Cell(int k)
    {
        if (k < Letters.Length)
            return (2 + 2 * (k % GridCols), GridRow + k / GridCols, Letters[k] == ' ' ? "_" : Letters[k].ToString());
        int last = GridRow + (Letters.Length + GridCols - 1) / GridCols;
        return k == Letters.Length ? (2, last, "DEL") : (8, last, "OK");
    }

    private const int Cells = 39 + 2;

    private static int? CellAt(int col, int row)
    {
        for (int k = 0; k < Cells; k++)
        {
            var (c, r, label) = Cell(k);
            if (row == r && col >= c && col < c + label.Length)
                return k;
        }
        return null;
    }

    private static char? KeyChar(string name) => name switch
    {
        { Length: 1 } when char.IsAsciiLetterUpper(name[0]) => name[0],
        { Length: 2 } when name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1],
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 => name[6],
        "Space" => ' ',
        "OemMinus" or "Subtract" => '-',
        "OemPeriod" or "Decimal" => '.',
        _ => null,
    };

    private void Type(TextRequest t, char ch)
    {
        if (_typed.Length < t.MaxLength)
            _typed += ch;
    }

    private Reply? TypeOrPick(TextRequest t, ActionInput input)
    {
        if (input.NewInput is (InputDevice.Keyboard, var name))
        {
            if (name == "Enter")
                return new Reply(_typed);
            if (name == "Back")
            {
                if (_typed.Length > 0)
                    _typed = _typed[..^1];
                return null;
            }
            if (KeyChar(name) is { } ch)
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

    public void Hover(int x, int y)
    {
        Sync();
        int col = x / 8, row = y / 8;
        switch (runner.Pending)
        {
            case PartyMenuRequest m when row >= ItemsRow && row < ItemsRow + m.Items.Count:
                _selected = row - ItemsRow;
                break;
            case PickRequest p when row >= 2 && row - 2 < p.Rows.Count && row < 23:
                _selected = row - 2;
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
            case ChoiceRequest c when row == MenuRow && WordAt(c.Items, col, WordsStart(c.MenuPrompt)) is { } w:
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
        switch (runner.Pending)
        {
            case PartyMenuRequest m:
                if (click.Right)
                    return null;
                if (row >= ItemsRow && row < ItemsRow + m.Items.Count)
                    Give(row - ItemsRow);
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
                if (row >= 2 && row - 2 < p.Rows.Count && row < 23)
                    Give(new ListAnswer(PickRequest.RowChosen, row - 2));
                else if (row == MenuRow && WordAt(p.Actions, col, WordsStart(p.Prompt, true)) is { } w)
                    Give(new ListAnswer(w, _selected));
                return null;
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
                if (row == MenuRow && WordAt(c.Items, col, WordsStart(c.MenuPrompt)) is { } cw)
                {
                    flow.Highlight.Chosen(cw);
                    Give(cw);
                }
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

    public void Draw(IndexedFrame f, ActionInput input)
    {
        var pending = runner.Pending;
        f.Clear(0);
        var back = pending is ChoiceRequest or TextRequest or KeyRequest ? _background : pending;
        switch (back)
        {
            case PartyMenuRequest m:
                DrawMenu(f, m, input, pending == m);
                break;
            case PickRequest p:
                DrawPick(f, p, input);
                break;
            case SheetRequest s:
                DrawSheet(f, s.Character);
                break;
            case IconRequest ic:
                DrawIcons(f, ic);
                break;
            case ModifyRequest md:
                DrawSheet(f, md.Character, md.Field);
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
                Line(f, s.Prompt, PromptColour, s.Actions, _word);
                break;
            case IconRequest ic:
                Line(f, ic.Prompt, PromptColour, ic.Actions, _word, hotkeys: ic.Hotkeys);
                break;
            case ModifyRequest md:
                Line(f, md.Prompt, PromptColour, md.Actions, _word);
                break;
            case ChoiceRequest c:
                Line(f, c.MenuPrompt, PromptColour, c.Items, _word);
                break;
            case TextRequest t:
                DrawTextEntry(f, t, input);
                break;
        }
        if (Problem is { } p2)
            Chrome.Text(f, font, 0, MenuRow, Fit(p2, 40), Chrome.Yellow);
    }

    /// <summary>The bottom line: a prompt in colour 13, then words (the highlighted one white, others with a white
    /// hotkey: the first letter, or the word's character in <paramref name="hotkeys"/>).</summary>
    private void Line(IndexedFrame f, string? prompt, byte promptColour, IReadOnlyList<string> words, int selected,
        int? start = null, string? hotkeys = null)
    {
        Chrome.Clear(f, 0, MenuRow, 40);
        if (prompt is { Length: > 0 })
            Chrome.Text(f, font, 0, MenuRow, Fit(prompt.ToUpperInvariant(), 40), promptColour);
        int col = start ?? WordsStart(prompt);
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (col + w.Length > 40)
                break;
            Chrome.Text(f, font, col, MenuRow, w, i == selected ? Chrome.White : Chrome.Green);
            int k = hotkeys is not null && i < hotkeys.Length ? Math.Max(0, w.IndexOf(hotkeys[i])) : 0;
            Chrome.Text(f, font, col + k, MenuRow, w[k..(k + 1)], Chrome.White);
            col += w.Length + 1;
        }
    }

    private void DrawMenu(IndexedFrame f, PartyMenuRequest m, ActionInput input, bool active)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        var party = flow.Party;
        if (party.Members.Count > 0)
        {
            Chrome.Text(f, font, 1, 2, "NAME", Chrome.White);
            Chrome.TextRight(f, font, 34, 2, "AC", Chrome.White);
            Chrome.TextRight(f, font, 38, 2, "HP", Chrome.White);
            for (int i = 0; i < party.Members.Count && i < 8; i++)
            {
                var c = party.Members[i];
                string name = c.Name.ToUpperInvariant();
                Chrome.Text(f, font, 1, RosterRow + i, name, i == party.CurrentIndex ? Chrome.White : Chrome.Cyan);
                Chrome.TextRight(f, font, 34, RosterRow + i, (60 - c[0x111]).ToString(CultureInfo.InvariantCulture), Chrome.Green);
                Chrome.TextRight(f, font, 38, RosterRow + i, c[0x11B].ToString(CultureInfo.InvariantCulture), Chrome.Green);
            }
        }
        for (int i = 0; i < m.Items.Count; i++)
        {
            string s = m.Items[i].Name.ToUpperInvariant();
            Chrome.Text(f, font, 2, ItemsRow + i, s[..1], Chrome.White);
            Chrome.Text(f, font, 3, ItemsRow + i, s[1..], Chrome.Green);
        }
        // The original marks no entry; for the pad and the mouse the picked one gets a pointer.
        if (active && input.LastDevice != InputDevice.Keyboard && m.Items.Count > 0)
            Chrome.Text(f, font, 1, ItemsRow + Math.Clamp(_selected, 0, m.Items.Count - 1), ">", Chrome.Yellow);
    }

    private void DrawPick(IndexedFrame f, PickRequest p, ActionInput input)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        for (int i = 0; i < p.Rows.Count && 2 + i < 23; i++)
            Chrome.Text(f, font, p.Column, 2 + i, Fit(p.Rows[i].ToUpperInvariant(), 38 - p.Column),
                i == _selected ? Chrome.White : Chrome.Green);
    }

    /// <summary>
    /// The character page (View 0x21084) with the portrait window at columns 27–39, rows 0–12. Modify's field cursor
    /// (<paramref name="field"/>) draws that value in colour 13: an ability (0x2193B, with Strength's exceptional part)
    /// or the hit points (START 0x973).
    /// </summary>
    private void DrawSheet(IndexedFrame f, EclCharacter c, int? field = null)
    {
        Chrome.Box(f, 0, 0, 39, 23);
        byte[]? weapon = null, armour = null;
        if (c.Items.Count > 0 && Items is { } table)
        {
            var eq = FightRules.Equipment(c, table);
            weapon = eq[FightRules.SlotWeapon];
            armour = eq[FightRules.SlotBodyArmour];
        }
        foreach (var (col, row, text, colour) in CombatSheet.Build(resources, c, weapon, armour,
                     it => ItemNames.Compose(it, resources.ItemWord).Trim().ToUpperInvariant()))
            if (c.Name.Length > 0 || row != 1)
                Chrome.Text(f, font, col, row, Fit(text, 40 - col),
                    field is { } h && (h < 6 ? row == 7 + h && col is >= 5 and <= 7 : row == 18 && col == 4)
                        ? PromptColour : colour);
        Chrome.Box(f, 27, 0, 39, 12);
        int head = c[CharacterCreation.PortraitHead], body = c[CharacterCreation.PortraitBody];
        if (head > 0 && body > 0)
        {
            int headId = Program is { } p ? p.Ds.ElementAtOrDefault(HeadTable + head) : head;
            int bodyId = Program is { } q ? q.Ds.ElementAtOrDefault(BodyTable + body) : body;
            art.Pictures.Draw(f, 28 * 8, 8, new ViewPicture(ViewPictureKind.Portrait, bodyId, headId, 0, PortraitDisk));
        }
    }

    /// <summary>The item types (for the readied weapon and armour on the page), when known.</summary>
    public BaseItemTable? Items { get; init; }

    /// <summary>The icon editor's page: the icon before the editor (old) and now (new), ready and action frames.</summary>
    private void DrawIcons(IndexedFrame f, IconRequest ic)
    {
        // [?] The program switches to the combat palette (grey ground); the ground here is EGA dark grey.
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 320; x++)
                f[x, y] = 8;
        Chrome.Box(f, 0, 0, 39, 23);
        var old = new EclCharacter(ic.OldRecord);
        // Each frame over a 24 × 24 box (DOSBox-X s19: ready on light grey with a white / black bevel, action on dark
        // grey with a light grey / black bevel; [?] the program draws icon slot 25's frames there, not decoded).
        void Box(int x0, int y0, byte fill, byte light)
        {
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                    f[x0 + x, y0 + y] = x < 2 || y < 2 ? light : x >= 22 || y >= 22 ? (byte)0 : fill;
        }
        void Pair(EclCharacter c, int y, string label)
        {
            Chrome.Text(f, font, 8, y / 8 - 1, label, Chrome.White);
            Box(32, y, 7, 15);
            Box(104, y, 8, 7);
            for (int k = 0; k < 2; k++)
                if (art.Combat.Party(c, k == 1) is { } pic)
                    CombatArt.Draw(f, pic, 32 + 72 * k, y, false, (0, 0, 320, 200));
            Chrome.Text(f, font, 3, y / 8 + 3, Fit(Texts("IconFrames") ?? "READY   ACTION", 30).ToUpperInvariant(), Chrome.White);
        }
        Pair(old, 56, (Texts("IconOld") ?? "OLD").ToUpperInvariant());
        Pair(ic.Character, 104, (Texts("IconNew") ?? "NEW").ToUpperInvariant());
    }

    private string? Texts(string key) => Program?.Text("PartyMsg." + key);

    private void DrawTextEntry(IndexedFrame f, TextRequest t, ActionInput input)
    {
        string prompt = t.Prompt.ToUpperInvariant();
        Chrome.Clear(f, 0, MenuRow, 40);
        Chrome.Text(f, font, 0, MenuRow, Fit(prompt, 40), PromptColour);
        Chrome.Text(f, font, prompt.Length, MenuRow, Fit(_typed + "_", Math.Max(0, 40 - prompt.Length)), Chrome.White);
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
