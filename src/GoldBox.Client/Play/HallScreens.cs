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
public sealed record HallMenuRequest(IReadOnlyList<HallEntry> Entries, string Prompt) : PlayerRequest;

/// <summary>
/// What the hall screen shows while it is open (<see cref="ClientPlayer.Hall"/>): the party list and the menu, or a page
/// of lines (Train's "will become" list), and a message on the bottom line during a pause (START 0x7DF8 / 0x2233).
/// </summary>
public sealed class HallState
{
    public List<(int Col, int Row, string Text, byte Colour)>? Page { get; set; }
    public (string Text, byte Colour)? Message { get; set; }
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

    /// <summary>A menu entry's text (DS:0619 + 42·i), or our words.</summary>
    public string Entry(TrainingHall.Entry e) => PorTrainingTables.MenuEntry(Program, e) ?? OurEntries[(int)e];
}

/// <summary>
/// The training hall, ECL PROGRAM 0 (GAME.OVR 0x19AE0), and Train Character (0x1C32D) on the logic thread
/// (docs/re/training.md). Rules are Core's <see cref="TrainingHall"/>; the other entries go to the screens that exist
/// (View, Save) or to <see cref="HallHooks"/> (character creation and the roster: not part of this screen).
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
        while (true)
        {
            var party = camp.Party;
            bool hasCurrent = party.Current is not null;
            var flags = TrainingHall.MenuFlags(hasCurrent, h.Memory.Read(TrainingHall.HallMaskCell), cheat, h.Defaults);
            var entries = new List<HallEntry>();
            for (int i = 0; i < flags.Length; i++)
                if (flags[i])
                {
                    string text = h.Text.Entry((TrainingHall.Entry)i).ToUpperInvariant();
                    entries.Add(new HallEntry(text, text.Length > 0 ? text[0] : '?'));
                }
            hall.Page = null;
            var answer = camp.Dialog.Ask(new HallMenuRequest(entries, h.Text["Hall.ChooseFunction"].TrimEnd().ToUpperInvariant()));
            if (answer is int member)
            {
                camp.Trade.SelectMember(member);
                continue;
            }
            if (answer is not char key)
                continue;
            var e = EntryOf(key);
            if (e is not { } entry || !flags[(int)entry])
                continue;
            switch (entry)
            {
                case TrainingHall.Entry.Train:
                    Train(h, cheat);
                    break;
                case TrainingHall.Entry.View:
                    CharacterView.Run(camp.Trade, Venue.Camp);
                    break;
                case TrainingHall.Entry.Save:
                    CampScreens.Save(camp);
                    break;
                case TrainingHall.Entry.Begin:
                    return;
                case TrainingHall.Entry.Exit:
                    if (camp.Dialog.YesNo(h.Text["Hall.QuitToDos"].ToUpperInvariant()))
                        camp.Quit?.Invoke();
                    break;
                default:
                    if (h.Hooks.For(entry) is { } run)
                        run();
                    else
                        Message(h, $"{h.Text.Entry(entry).ToUpperInvariant()}: NOT IN THIS BUILD YET.", 14);
                    break;
            }
        }
    }

    private static TrainingHall.Entry? EntryOf(char key) => char.ToUpperInvariant(key) switch
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
    public static void Message(HallContext h, string text, byte colour)
    {
        h.State.Message = (text.ToUpperInvariant(), colour);
        try
        {
            h.Camp.Dialog.Ask(new DelayRequest(TimeSpan.FromMilliseconds(h.Memory.GameSpeed * 225)));
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
    public static void Train(HallContext h, bool cheat)
    {
        var camp = h.Camp;
        if (camp.Party.Current is not { } c || h.Tables is not { } t)
        {
            Message(h, "TRAINING NEEDS THE GAME'S TABLES.", 14);
            return;
        }
        var offer = TrainingHall.Check(c, h.Memory.Read(TrainingHall.HallMaskCell), cheat, t);
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
        string name = c.Name.ToUpperInvariant();
        var page = new List<(int, int, string, byte)>
        {
            (4, 4, name, Chrome11),
            (name.Length + 4, 4, h.Text["Hall.WillBecome"].ToUpperInvariant(), 10),
        };
        int row = 4;
        foreach (var (cls, level) in TrainingHall.Raised(c, offer.Train, t))
        {
            row++;
            string words = (row == 5 ? h.Text["Hall.ALevel"] : h.Text["Hall.AndALevel"]).ToUpperInvariant();
            string cname = (camp.Resources.ClassName(cls) ?? $"CLASS {cls}").ToUpperInvariant();
            page.Add((6, row, words + level.ToString(CultureInfo.InvariantCulture) + " " + cname, 10));
        }
        h.State.Page = page;
        try
        {
            if (!camp.Dialog.YesNo(h.Text["Hall.WishToTrain"].ToUpperInvariant()))
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
            camp.Trade.Halt?.Invoke(e);
            throw;
        }
    }

    private const byte Chrome11 = 11;

    /// <summary>
    /// The Learn list (0x239D6 mode 4): "NAME'S SPELLS TO CHOOSE FROM", level headings, the spells; the only action is
    /// Learn and the list comes back until a spell is chosen (Escape does not leave). [?] The first highlighted row
    /// (DOSBox-X showed the last one).
    /// </summary>
    private static int Learn(HallContext h, EclCharacter who, IReadOnlyList<int> spells)
    {
        var camp = h.Camp;
        var rows = new List<string>();
        var ids = new List<int>();
        var labels = new HashSet<int>();
        int last = 0;
        foreach (int s in spells)
        {
            int level = camp.Resources.Spells?[s].Level ?? 0;
            if (level != last)
            {
                labels.Add(rows.Count);
                rows.Add((camp.Resources.LevelName(level) ?? $"LEVEL {level}").ToUpperInvariant());
                ids.Add(0);
                last = level;
            }
            rows.Add("  " + CampScreens.SpellName(camp, s));
            ids.Add(s);
        }
        string title = who.Name.ToUpperInvariant() + "'S " + camp.Text[Msg.SpellsWord] + h.Text["Hall.ToChooseFrom"].ToUpperInvariant();
        while (true)
        {
            var a = camp.Dialog.Page(new ListRequest([new ListLine(title, 11, 1)], rows,
                [h.Text["Hall.Learn"].ToUpperInvariant()], null, FirstRow: 3)
            {
                Labels = labels,
                RowActions = new HashSet<int> { 0 },
                Selected = rows.Count - 1,
            });
            if (a.Action == 0 && a.Row >= 0 && a.Row < ids.Count && ids[a.Row] != 0)
                return ids[a.Row];
        }
    }

    // ---- End game (0x20941) ----------------------------------------------------------------------------------------

    /// <summary>
    /// The end game (<see cref="EndGame.Steps"/>): DS:49F3 := 7 while it runs (restored after), the lines in the text
    /// window, the FINAL pictures over the view and animations on the clock (pauses the player cannot skip in the
    /// program; Enter skips a frame here as for every Client delay). DOS art only: the Amiga has blocks 2, 7 and 8 of
    /// another painting `[?]` (its program's sequence is not read).
    /// </summary>
    public static void EndGameSequence(ClientPlayer player, TextWindow text, Func<PlayerRequest, object?> ask, EclMemory memory)
    {
        var scene = new EndScene(memory.Disk);
        player.Scene = scene;
        int speed = memory.GameSpeed;
        try
        {
            foreach (var step in EndGame.Steps(memory.Read(0x6E3E), speed))
            {
                switch (step)
                {
                    case EndGameClearText:
                        text.Clear();
                        break;
                    case EndGameLine l:
                        text.Put(l.Row, 1, player.Resources.HallTexts.End(l.Text).ToUpperInvariant(), 10);
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
                }
            }
        }
        finally
        {
            player.Scene = null;
            text.Clear();
        }
    }

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
                ask(new DelayRequest(EndGame.FrameTime(speed, delays[f])));
            }
    }
}

/// <summary>A FINAL block on the end-game screen: its frame, its top-left cell, whether it is the running animation.</summary>
public sealed record EndLayer(int Block, int Column, int Row, int Frame, bool Animation);

/// <summary>The end-game pictures the screen draws over the view (cells of 8 pixels, clipped to the screen).</summary>
public sealed class EndScene(int disk)
{
    public int Disk { get; } = disk;
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
}

/// <summary>
/// The entries this screen does not run itself, by the program's routine: Create 0x19EEB, Drop 0x1B8B6, Modify
/// 0x1BA1E, Add 0x1C9EC, Remove 0x1E684 + 0x1CC10, Load 0x1F5E8. Null = a notice.
/// </summary>
public sealed class HallHooks
{
    public Action? Create { get; set; }
    public Action? Drop { get; set; }
    public Action? Modify { get; set; }
    public Action? Add { get; set; }
    public Action? Remove { get; set; }
    public Action? Load { get; set; }

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
