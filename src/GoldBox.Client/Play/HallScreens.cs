using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>An entry of the hall's menu: its text and hotkey (the first letter).</summary>
public sealed record HallEntry(string Text, char Key);

/// <summary>
/// The training hall's menu (GAME.OVR 0x19AE0): the shown entries as a vertical list (rows 12 on, column 2, the hotkey
/// letter white), the prompt on the bottom line. The answer is the chosen entry's hotkey (a <see cref="char"/>), or a
/// party selection (<see cref="ChoiceRequest.PreviousMember"/> / <see cref="ChoiceRequest.NextMember"/> /
/// <see cref="TradeContext.MemberPick"/> − index). The program has no highlight here (keys only); the Client's
/// highlight is for the pad and the mouse.
/// </summary>
public sealed record HallMenuRequest(IReadOnlyList<HallEntry> Entries, string Prompt) : PlayerRequest
{
    /// <summary>
    /// Opened while VM $4AFF was not 0 (the first hall menu after a Save or a Load) with a picture in the frame table:
    /// the menu routine draws the frames over the hall's page at cell (3, 3), one on every pass (0x2CD5D–0x2CDD6;
    /// <see cref="ClientPlayer.MenuAsked"/>, <see cref="HallState.PictureOver"/>).
    /// </summary>
    public bool EveryPass { get; init; }
}

/// <summary>What is left of a picture the hall's menu drew over the page (<see cref="HallState.PictureOver"/>).</summary>
public enum HallPicture
{
    /// <summary>None: the loop drew its page (the menu routine draws no picture with $4AFF = 0 and its flag 0).</summary>
    None,
    /// <summary>The whole picture at cell (3, 3), its last frame once the menu has ended.</summary>
    Whole,
    /// <summary>After a member pick: the party list drawn again over it (START 0x1307 blanks rows 4 to 4 + n).</summary>
    UnderList,
}

/// <summary>
/// What the hall screen shows while it is open (<see cref="ClientPlayer.Hall"/>): the party list and the menu, or a page
/// of lines (Train's "will become" list), and a message on the bottom line during a pause (START 0x7DF8 / 0x2233).
/// </summary>
public sealed class HallState
{
    public List<(int Col, int Row, string Text, byte Colour)>? Page { get; set; }
    public (string Text, byte Colour)? Message { get; set; }
    /// <summary>Load brought a save of interface state ≠ 0: the menu ends at once (0x19D91 → 0x19E6C), as Begin does.</summary>
    public bool LeaveAfterLoad { get; set; }

    /// <summary>
    /// A picture the hall's menu drew over its page (DOSBox-X s43: after a Save, PICTURE 2's frames at cell (3, 3)):
    /// the menu routine draws the frame table whenever $4AFF ≠ 0 and a picture is loaded, whatever page is up
    /// (0x2CD5D–0x2CD9B). It stays until the loop draws its page again (its redraw flag [bp−0xF], set after every
    /// entry at 0x19E65 and tested at 0x19AFE); a member pick (0x19E69) draws only the party list over it.
    /// </summary>
    public HallPicture PictureOver { get; set; }
}

/// <summary>The hall's and the end game's own texts (catalogue keys <c>Hall.*</c> / <c>End.*</c>), with our wording when
/// the game files do not hold them.</summary>
public sealed class HallText(PorProgramData? program)
{
    private static readonly Dictionary<string, string> Ours = new()
    {
        ["Hall.ChooseFunction"] = "PICK ONE ",
        ["Hall.QuitToDos"] = "LEAVE THE GAME ",
        ["Hall.Understood"] = "AS YOU WISH.",
        ["Hall.NotConscious"] = "NOT AWAKE ENOUGH TO TRAIN",
        ["Hall.Costs"] = "A LEVEL COSTS 1000 GOLD.",
        ["Hall.WrongClass"] = "NO TEACHER HERE FOR THAT CLASS",
        ["Hall.NotEnoughXp"] = "TOO FEW EXPERIENCE POINTS",
        ["Hall.WillBecome"] = " RISES TO:",
        ["Hall.ALevel"] = "    LEVEL ",
        ["Hall.AndALevel"] = "AND LEVEL ",
        ["Hall.WishToTrain"] = "TRAIN NOW? ",
        ["Hall.Congratulations"] = "WELL DONE.",
        ["Hall.Learn"] = "LEARN",
        ["Hall.ToChooseFrom"] = "TO PICK FROM",
    };

    /// <summary>The menu entries' fallback words, in the program's order (Create … Exit).</summary>
    private static readonly string[] OurEntries =
        ["CREATE A CHARACTER", "DROP A CHARACTER", "MODIFY A CHARACTER", "TRAIN A CHARACTER", "VIEW A CHARACTER",
         "ADD TO THE PARTY", "REMOVE FROM THE PARTY", "LOAD A SAVED GAME", "SAVE THE GAME", "BEGIN THE ADVENTURE",
         "EXIT TO DOS"];

    public PorProgramData? Program { get; } = program;

    /// <summary>The text, original case (the program prints it as it is; the font has no lower case).</summary>
    public string this[string key] => Program?.Text(key) ?? (Ours.TryGetValue(key, out var s) ? s : key);

    /// <summary>An end-game line (End.0 … End.12), or our marker when the game files lack it.</summary>
    public string End(int n) => Program?.Text("End." + n.ToString(CultureInfo.InvariantCulture)) ?? $"(END TEXT {n})";

    /// <summary>
    /// A menu entry's text (DS:0619 + 42·i), or our words. Under the Amiga art the Amiga program's own table (hunk 11
    /// +0xD2, the same stride, copied by the one menu routine hunk 10 +0x64E that is both the start-up party menu and the
    /// hall of PROGRAM 0): its last entry names the Workbench, not DOS (FS-UAE s47).
    /// </summary>
    public string Entry(TrainingHall.Entry e) =>
        AmigaView.On && AmigaView.Strings.TryGetValue(GoldBox.Data.Module.Por.PorAmigaText.PartyMenuKey + (int)e, out var amiga) ? amiga
        : PorTrainingTables.MenuEntry(Program, e) ?? OurEntries[(int)e];
}

/// <summary>
/// The training hall, ECL PROGRAM 0 (GAME.OVR 0x19AE0), and Train Character (0x1C32D) on the logic thread
/// (docs/re/training.md). Rules are Core's <see cref="TrainingHall"/>; the other entries go to the screens that exist
/// (Save; View without a hook) or to <see cref="HallHooks"/> (character creation, the roster and View: the party menu's).
/// </summary>
public static class HallScreens
{
    /// <summary>
    /// The menu loop: DS:5376 (disk) := 3, the cheat flag DS:470C := 0, DS:49F3 := 0; then until Begin: the entries
    /// shown by <see cref="TrainingHall.MenuFlags"/>; Home / End (and a click on a name) pick the member. A key whose
    /// entry is hidden does nothing. Begin returns (DS:49F3 := 4 in the program); Exit asks the quit question.
    /// </summary>
    public static void Run(HallContext h)
    {
        var camp = h.Camp;
        h.Memory.Disk = 3;
        bool cheat = false;
        var hall = h.State;
        bool redraw = true; // [bp−0xF] (0x19AF0): the page is drawn before the next menu
        while (true)
        {
            if (redraw)
                hall.PictureOver = HallPicture.None; // 0x19B07–0x19C4B: the window blanked, the list and the entries drawn
            redraw = true; // every entry, a hidden one too, ends at 0x19E65; only a member pick skips it (0x19E69)
            var party = camp.Party;
            bool hasCurrent = party.Current is not null;
            var flags = TrainingHall.MenuFlags(hasCurrent, h.Memory.Read(TrainingHall.HallMaskCell), cheat, h.Defaults);
            var entries = new List<HallEntry>();
            for (int i = 0; i < flags.Length; i++)
                if (flags[i])
                {
                    string text = AmigaView.Case(h.Text.Entry((TrainingHall.Entry)i));   // the Amiga keeps the case
                    entries.Add(new HallEntry(text, text.Length > 0 ? text[0] : '?'));
                }
            hall.Page = null;
            var answer = camp.Dialog.Ask(new HallMenuRequest(entries, AmigaView.Case(h.Text["Hall.ChooseFunction"].TrimEnd())));
            if (answer is int member)
            {
                camp.Trade.SelectMember(member);
                if (hall.PictureOver != HallPicture.None)
                    hall.PictureOver = HallPicture.UnderList; // 0x19CAB: START 0x1307 over the picture (DOSBox-X s43)
                redraw = false;
                continue;
            }
            if (answer is not char key)
                continue;
            if (char.ToUpperInvariant(key) == PasswordKey)
            {
                if (Password(h.Trainer()))
                    cheat = true;
                continue;
            }
            var e = EntryOf(key);
            if (e is not { } entry || !flags[(int)entry])
                continue;
            switch (entry)
            {
                case TrainingHall.Entry.Train:
                    Train(h.Trainer(), cheat);
                    break;
                case TrainingHall.Entry.View:
                    // 0x21AED, the camp's View, with DS:49F3 = 0 (no Use on the items menu).
                    if (h.Hooks.View is { } view)
                        view();
                    else
                        CharacterView.Run(camp.Trade, Venue.PartyMenu);
                    break;
                case TrainingHall.Entry.Save:
                    CampScreens.Save(camp);
                    break;
                case TrainingHall.Entry.Begin:
                    return;
                case TrainingHall.Entry.Exit:
                    if (camp.Dialog.YesNo(AmigaView.HallQuit(AmigaView.Case(h.Text["Hall.QuitToDos"])), 14)) // 0x19DF7: 14 (Amiga hunk 10 +0xA38: 24); Workbench under Amiga (s48 fix)
                        camp.Quit?.Invoke();
                    break;
                default:
                    if (h.Hooks.For(entry) is { } run)
                    {
                        // The roster entries move the party size $6E3E with the list (the join 0x1F42C adds one,
                        // 0x1CC10 takes one off). Load brings the save's own $6E3E with its memory.
                        int before = party.Members.Count;
                        run();
                        if (entry == TrainingHall.Entry.Load && hall.LeaveAfterLoad)
                            return;
                        int delta = camp.Party.Members.Count - before;
                        if (delta != 0 && entry != TrainingHall.Entry.Load)
                            h.Memory.Write(EclPartyRules.PartySizeCell,
                                (ushort)(h.Memory.Read(EclPartyRules.PartySizeCell) + delta));
                        if (h.Hooks.Notice?.Invoke() is { } note)
                            Message(h.Trainer(), note, 14);
                    }
                    else
                        Message(h.Trainer(), $"{h.Text.Entry(entry).ToUpperInvariant()}: NOT IN THIS BUILD YET.", 14);
                    break;
            }
        }
    }

    /// <summary>The hidden key (0x19E2C): not an entry, never shown.</summary>
    public const char PasswordKey = 'J';

    /// <summary>
    /// J (0x19E2C): the program's first command-line word (ParamStr(1), Turbo Pascal System 0AF8:16BC) compared, exactly
    /// and with case, with the word at 0x19AC2 (0AF8:0C10). Equal: the cheat flag DS:470C := 1 and the message at
    /// 0x19AC8 on the bottom line in green with its pause (START 0x2233). Not equal: nothing. No line is read. The word
    /// comes from the player's files only; without them J does nothing.
    /// </summary>
    public static bool Password(HallTrainer h)
    {
        if (!IsProgramWord(h.Text, h.ProgramWord()))
            return false;
        Message(h, h.Text["Hall.Understood"], 10);
        return true;
    }

    /// <summary>Whether ParamStr(1) of <paramref name="tail"/> is the program's debugging word (from the player's files only).</summary>
    public static bool IsProgramWord(HallText text, string? tail) =>
        text.Program?.Text("Hall.Password") is { Length: > 0 } word && ParamStr1(tail) == word;

    /// <summary>ParamStr(1) of a DOS command tail: its first word (words end at a space or a control character).</summary>
    public static string ParamStr1(string? tail)
    {
        if (tail is null)
            return "";
        int i = 0;
        while (i < tail.Length && tail[i] <= ' ')
            i++;
        int start = i;
        while (i < tail.Length && tail[i] > ' ')
            i++;
        return tail[start..i];
    }

    internal static TrainingHall.Entry? EntryOf(char key) => char.ToUpperInvariant(key) switch
    {
        'C' => TrainingHall.Entry.Create,
        'D' => TrainingHall.Entry.Drop,
        'M' => TrainingHall.Entry.Modify,
        'T' => TrainingHall.Entry.Train,
        'V' => TrainingHall.Entry.View,
        'A' => TrainingHall.Entry.Add,
        'R' => TrainingHall.Entry.Remove,
        'L' => TrainingHall.Entry.Load,
        'S' => TrainingHall.Entry.Save,
        'B' => TrainingHall.Entry.Begin,
        'E' => TrainingHall.Entry.Exit,
        _ => null,
    };

    /// <summary>A message on the bottom line for the program's Delay (START 0x7DF8: game speed × 225 ms).</summary>
    public static void Message(HallTrainer h, string text, byte colour)
    {
        h.State.Message = (AmigaView.Case(text), colour);
        try
        {
            // The Amiga's timed message (hunk 25 +0xE10, the hall's refusals hunk 10 +0x24FC …): speed · 100 ms.
            h.Ask(new DelayRequest(AmigaView.MessagePause(h.GameSpeed)));
        }
        finally
        {
            h.State.Message = null;
        }
    }

    /// <summary>
    /// Train Character (0x1C32D): the checks (<see cref="TrainingHall.Check"/>; a refusal is a bottom-line message in
    /// yellow); the page "NAME will become:" with one line per class raised (column 6, rows 5 on: "    a level N
    /// class", then "and a level …"), the question; on Yes "Congratulations..." (green) and the level-up
    /// (<see cref="TrainingHall.Train"/>), the Learn list for a magic-user level.
    /// </summary>
    public static void Train(HallTrainer h, bool cheat)
    {
        if (h.Party.Current is not { } c || h.Tables is not { } t)
        {
            Message(h, "TRAINING NEEDS THE GAME'S TABLES.", 14);
            return;
        }
        var offer = TrainingHall.Check(c, h.HallMask, cheat, t);
        if (offer.Refused)
        {
            string key = offer.Refusal switch
            {
                TrainingRefusal.NotConscious => "Hall.NotConscious",
                TrainingRefusal.NoMoney => "Hall.Costs",
                TrainingRefusal.WrongClass => "Hall.WrongClass",
                _ => "Hall.NotEnoughXp",
            };
            Message(h, h.Text[key], 14);
            return;
        }
        string name = AmigaView.Case(c.Name);
        var page = new List<(int, int, string, byte)>
        {
            (4, 4, name, Chrome11),
            (name.Length + 4, 4, AmigaView.Case(h.Text["Hall.WillBecome"]), 10),
        };
        int row = 4;
        foreach (var (cls, level) in TrainingHall.Raised(c, offer.Train, t))
        {
            row++;
            string words = AmigaView.Case((row == 5 ? h.Text["Hall.ALevel"] : h.Text["Hall.AndALevel"]));
            string cname = AmigaView.Case((h.Resources.ClassName(cls) ?? $"CLASS {cls}"));
            page.Add((6, row, words + level.ToString(CultureInfo.InvariantCulture) + " " + cname, 10));
        }
        h.State.Page = page;
        try
        {
            if (!h.YesNo(AmigaView.Case(h.Text["Hall.WishToTrain"])))
                return;
            Message(h, h.Text["Hall.Congratulations"], 10);
        }
        finally
        {
            h.State.Page = null;
        }
        try
        {
            TrainingHall.Train(c, offer.Train, cheat, t, h.Random, list => Learn(h, c, list));
        }
        catch (ProgramHaltException e)
        {
            h.Halt?.Invoke(e);
            throw;
        }
    }

    private const byte Chrome11 = 11;

    /// <summary>
    /// The Learn list (0x239D6 mode 4): "NAME'S SPELLS TO CHOOSE FROM", level headings, the spells; the only action is
    /// Learn and the list comes back until a spell is chosen (Escape does not leave). The first highlighted row
    /// is the list routine's entry rule (row 0 a heading → the window's last row; DOSBox-X showed the last one).
    /// </summary>
    private static int Learn(HallTrainer h, EclCharacter who, IReadOnlyList<int> spells)
    {
        var rows = new List<string>();
        var ids = new List<int>();
        var labels = new HashSet<int>();
        int last = 0;
        foreach (int s in spells)
        {
            int level = h.Resources.Spells?[s].Level ?? 0;
            if (level != last)
            {
                labels.Add(rows.Count);
                rows.Add(AmigaView.Case((h.Resources.LevelName(level) ?? $"LEVEL {level}")));
                ids.Add(0);
                last = level;
            }
            rows.Add("  " + AmigaView.Case((h.Resources.SpellName(s) ?? $"SPELL {s.ToString(CultureInfo.InvariantCulture)}")));
            ids.Add(s);
        }
        string title = AmigaView.Case(who.Name) + "'S " + h.Resources.Texts[Msg.SpellsWord] + AmigaView.Case(h.Text["Hall.ToChooseFrom"]);
        int row = 0, top = 0; // 0x1C8EB: the position word −1 once; the loop (back to 0x1C8F0) keeps it
        while (true)
        {
            // 0x270A2 action 4 gives no Exit, so Next / Prev follow the Learn word. The first highlight comes from the
            // list routine's entry (P, s30): row 0 is a heading, so the highlight steps up past it and wraps to the
            // window's last row (or the list's), which is what DOSBox-X showed.
            var a = h.Ask(new ListRequest([new ListLine(title, 11, 1)], rows,
                [AmigaView.Case(h.Text["Hall.Learn"])], null, FirstRow: SpellLists.FirstRow)
            {
                Labels = labels,
                RowActions = new HashSet<int> { 0 },
                Paging = SpellLists.Paging(h.Resources.CombatTexts, memorize: false, wordsAt: 1),
                Selected = row,
                Top = top,
                // the list's screen: "Spells " + the list's words for kind 4 (0x239A0), the choose-spell prompt (action 4)
                Spell = new SpellPageLayout(AmigaView.Case(who.Name), CombatChooser.NameColour(who),
                    AmigaView.Case((h.Resources.Texts[Msg.SpellsWord] + h.Text["Hall.ToChooseFrom"])))
                {
                    Prompt = AmigaView.Case(h.Resources.Texts[Msg.ChooseSpell].TrimEnd()) + " ",
                },
            }) as ListAnswer ?? new ListAnswer(-1, 0);
            (row, top) = (Math.Max(0, a.Row), a.Top);
            if (a.Action == 0 && a.Row >= 0 && a.Row < ids.Count && ids[a.Row] != 0)
                return ids[a.Row];
        }
    }

    // ---- End game (0x20941) ----------------------------------------------------------------------------------------

    /// <summary>
    /// The end game (<see cref="EndGame.Steps"/>): DS:49F3 := 7 while it runs (restored after), the lines in the text
    /// window, the FINAL pictures over the view and animations on the clock (pauses the player cannot skip in the
    /// program; Enter skips a frame here as for every Client delay). Under Best / Amiga, when the chosen art has the
    /// Amiga's blocks 2, 7 and 8 (<see cref="ClientPlayer.EndArt"/>), the Amiga program's own sequence plays instead
    /// (<see cref="EndGame.AmigaSteps"/>: those three blocks in the picture window, no arena), all from the Amiga art.
    /// </summary>
    public static void EndGameSequence(ClientPlayer player, TextWindow text, Func<PlayerRequest, object?> ask, EclMemory memory)
    {
        bool amiga = AmigaEndGame(player, memory.Disk);
        var scene = new EndScene(memory.Disk) { Amiga = amiga };
        player.Scene = scene;
        int speed = memory.GameSpeed;
        try
        {
            foreach (var step in amiga ? EndGame.AmigaSteps() : EndGame.Steps(memory.Read(0x6E3E), speed))
            {
                switch (step)
                {
                    case EndGameClearText:
                        text.Clear();
                        break;
                    case EndGameLine l:
                        text.Put(l.Row, 1, AmigaView.Case(player.Resources.HallTexts.End(l.Text)), 10);
                        break;
                    case EndGamePicture p:
                        scene.Layers.RemoveAll(x => x.Animation);
                        scene.Layers.Add(new EndLayer(p.Block, p.Column, p.Row, 0, false));
                        break;
                    case EndGameAnimation a:
                        Animate(scene, a, speed, ask, player);
                        break;
                    case EndGamePause:
                        ask(new DelayRequest(EndGame.Pause(speed)));
                        break;
                    case EndGameFrames fr:
                        int count = player.EndArt?.Invoke(scene.Disk, fr.Block)?.Frames.Count ?? 0;
                        foreach (int f in EndGame.FrameOrder(fr, count))
                        {
                            // Each frame is drawn whole over the window (FINAL pictures get no mask: h25 +0x5C4).
                            var layer = new EndLayer(fr.Block, fr.Column, fr.Row, f, true);
                            if (scene.Layers.Count == 1)
                                scene.Layers[0] = layer;
                            else
                                scene.Layers.Add(layer);
                            ask(new DelayRequest(EndGame.AmigaFrameTime) { Animation = true });
                        }
                        break;
                }
            }
        }
        finally
        {
            player.Scene = null;
            text.Clear();
        }
    }

    /// <summary>
    /// True when the Amiga end game plays: <see cref="ClientPlayer.AmigaEndGame"/> is on and the chosen art gives the
    /// Amiga version of every block it draws (never a mix of DOS and Amiga blocks in one run).
    /// </summary>
    public static bool AmigaEndGame(ClientPlayer player, int disk) =>
        player.AmigaEndGame && player.EndArt is { } art
        && EndGame.AmigaBlocks.All(b => art(disk, b) is { Variant: not "dos", Frames.Count: > 0 });

    private static void Animate(EndScene scene, EndGameAnimation a, int speed, Func<PlayerRequest, object?> ask, ClientPlayer player)
    {
        var delays = player.EndArtDelays?.Invoke(scene.Disk, a.Block) ?? [];
        if (delays.Count == 0)
            return;
        var layer = new EndLayer(a.Block, a.Column, a.Row, 0, true);
        scene.Layers.RemoveAll(x => x.Animation);
        scene.Layers.Add(layer);
        for (int loop = 0; loop < a.Loops; loop++)
            for (int f = 0; f < delays.Count; f++)
            {
                scene.Layers[^1] = layer with { Frame = f };
                ask(new DelayRequest(EndGame.FrameTime(speed, delays[f])) { Chained = true });
            }
    }
}

/// <summary>A FINAL block on the end-game screen: its frame, its top-left cell, whether it is the running animation.</summary>
public sealed record EndLayer(int Block, int Column, int Row, int Frame, bool Animation);

/// <summary>The end-game pictures the screen draws over the view (cells of 8 pixels, clipped to the screen).</summary>
public sealed class EndScene(int disk)
{
    public int Disk { get; } = disk;
    /// <summary>The Amiga sequence: the layers are drawn from the chosen art with their own palette, in the picture window.</summary>
    public bool Amiga { get; init; }
    public List<EndLayer> Layers { get; } = [];
}

/// <summary>What the hall screens work on.</summary>
public sealed class HallContext
{
    public required CampContext Camp { get; init; }
    public required HallState State { get; init; }
    public required HallText Text { get; init; }
    public required EclMemory Memory { get; init; }
    public TrainingTables? Tables { get; init; }
    public required Func<ushort, ushort> Random { get; init; }
    /// <summary>The DS flags of the Create, Add and Exit entries (null: all shown).</summary>
    public bool[]? Defaults { get; init; }
    public HallHooks Hooks { get; init; } = new();
    /// <summary>The word after START on the DOS command line (ParamStr(1)), or null (the hall's J).</summary>
    public Func<string?> ProgramWord { get; init; } = () => null;

    /// <summary>What J, Train and the hall's messages work on: the game's party, its hall mask $6DA8 and speed.</summary>
    public HallTrainer Trainer() => new()
    {
        Ask = Camp.Dialog.Ask,
        State = State,
        Text = Text,
        Resources = Camp.Resources,
        Party = Camp.Party,
        Tables = Tables,
        HallMask = Memory.Read(TrainingHall.HallMaskCell),
        GameSpeed = Memory.GameSpeed,
        Random = Random,
        Halt = Camp.Trade.Halt,
        ProgramWord = ProgramWord,
        YesNo = q => Camp.Dialog.YesNo(q),
    };
}

/// <summary>
/// What the menu routine's J (0x19E2C), Train Character (0x1C32D) and the bottom-line message (START 0x7DF8) need: the
/// training hall's (<see cref="HallContext.Trainer"/>) or the start-up party menu's (<see cref="PartyMenuFlow"/>: the
/// same routine 0x19AE0 on the menu's party).
/// </summary>
public sealed class HallTrainer
{
    public required Func<PlayerRequest, object?> Ask { get; init; }
    public required HallState State { get; init; }
    public required HallText Text { get; init; }
    public required ClientResources Resources { get; init; }
    public required EclParty Party { get; init; }
    public TrainingTables? Tables { get; init; }
    /// <summary>VM $6DA8 (the hall's classes).</summary>
    public ushort HallMask { get; init; }
    /// <summary>DS:49E2 (the message's pause is speed × 225 ms).</summary>
    public byte GameSpeed { get; init; } = 1;
    public required Func<ushort, ushort> Random { get; init; }
    public Action<ProgramHaltException>? Halt { get; init; }
    public Func<string?> ProgramWord { get; init; } = () => null;
    /// <summary>The program's yes/no (0x2D9A0: the highlight on No first) with this prompt: true on Yes.</summary>
    public required Func<string, bool> YesNo { get; init; }
}

/// <summary>
/// The entries this screen does not run itself, by the program's routine: Create 0x19EEB, Drop 0x1B8B6, Modify
/// 0x1BA1E, Add 0x1C9EC, Remove 0x1E684 + 0x1CC10, Load 0x1F5E8. Null = a notice. Load copies the save over the
/// running game (<see cref="Data.Por.PorGame.LoadInHall"/>): with interface state 0 the hall stays up with the save's
/// party; with any other it ends (<see cref="HallState.LeaveAfterLoad"/>, 0x19D91) and the running script goes on at
/// its PC in the loaded script area. <see cref="HallLoadException"/> is left for a hall without a game.
/// </summary>
public sealed class HallHooks
{
    public Action? Create { get; set; }
    public Action? Drop { get; set; }
    public Action? Modify { get; set; }
    public Action? Add { get; set; }
    public Action? Remove { get; set; }
    public Action? Load { get; set; }
    /// <summary>View (0x21AED on the program's page: <see cref="PartyMenuFlow.View"/>); null: the camp's View as it is
    /// drawn in the camp.</summary>
    public Action? View { get; set; }
    /// <summary>A message an entry left for the bottom line (Drop's farewell; [?] its colour), taken after each entry.</summary>
    public Func<string?>? Notice { get; set; }

    public Action? For(TrainingHall.Entry e) => e switch
    {
        TrainingHall.Entry.Create => Create,
        TrainingHall.Entry.Drop => Drop,
        TrainingHall.Entry.Modify => Modify,
        TrainingHall.Entry.Add => Add,
        TrainingHall.Entry.Remove => Remove,
        TrainingHall.Entry.Load => Load,
        _ => null,
    };
}

/// <summary>
/// Load in the training hall picked a save: the running game ends here (the program leaves the menu after a successful
/// load, 0x19D91, and its game loop 0x403E starts on the loaded state); the screen loads <see cref="Slot"/>.
/// </summary>
public sealed class HallLoadException(char slot) : Exception($"load save {slot}")
{
    public char Slot { get; } = slot;
}
