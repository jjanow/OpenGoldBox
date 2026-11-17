using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The combat menu against DOSBox-X states of the Slums goblin fight (fixtures <c>fight_moves/</c>, private; README
/// "fight_moves"): the Done menu's entries, Delay and Quit (no draws: the next pick follows from the captured
/// RandSeed and gives the creature whose menu came next), and a cleric's Turn with no undead (two draws, then the pick).
/// </summary>
public class FightMenuCaptureTests
{
    private static readonly string? Dir = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_moves") : null;

    public sealed class MovesFactAttribute : FactAttribute
    {
        public MovesFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir))
                Skip = "needs the private DOSBox captures (tests/fixtures-private/por/dosbox-v1.3/fight_moves)";
        }
    }

    private sealed record State(Fight Fight, TurboPascalRandom Rng, int Current, uint Seed);

    /// <summary>A capture as a fight: creatures with their items and effects, combat records, the base item table
    /// DS:5582, DS:6DE3, the RandSeed; the current creature DS:5D92.</summary>
    private static State Load(string name, IFightController? controller = null)
    {
        var dir = Path.Combine(Dir!, name);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "fight.json")));
        var root = doc.RootElement;
        var ds = File.ReadAllBytes(Path.Combine(dir, "ds.bin"));
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var combat = new List<byte[]>();
        long cur = root.GetProperty("pointers").GetProperty("current_5d92").GetProperty("linear").GetInt64();
        int current = -1;
        foreach (var c in root.GetProperty("creatures").EnumerateArray())
        {
            var ch = new EclCharacter(Convert.FromHexString(c.GetProperty("record").GetString()!));
            foreach (var it in c.GetProperty("items").EnumerateArray())
                ch.Items.Add(Convert.FromHexString(it.GetProperty("record").GetString()!));
            foreach (var e in c.GetProperty("effects").EnumerateArray())
                ch.Effects.Add(Convert.FromHexString(e.GetProperty("record").GetString()!));
            if (c.GetProperty("linear").GetInt64() == cur)
                current = memory.Party.Members.Count;
            memory.Party.Members.Add(ch);
            combat.Add(Convert.FromHexString(c.GetProperty("combat").GetString()!));
        }
        uint seed = root.GetProperty("vars").GetProperty("randseed").GetUInt32();
        var rng = new TurboPascalRandom(seed);
        var items = new BaseItemTable(ds.AsSpan(0x5582, BaseItemTable.EntrySize * BaseItemTable.Count).ToArray());
        var fight = new Fight(memory, new EncounterState(), items, rng.Next, controller ?? new EndActionsController(), new GridGeometry());
        for (int i = 0; i < combat.Count; i++)
            combat[i].CopyTo(fight.Record(memory.Party.Members[i]).Bytes, 0);
        fight.State.RoundCounter = ds[0x6DE3];
        FightRules.CountTeams(fight.List, fight.State);
        return new State(fight, rng, current, seed);
    }

    private static uint Seed(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, name, "fight.json")));
        return doc.RootElement.GetProperty("vars").GetProperty("randseed").GetUInt32();
    }

    private static int CurrentIndex(string name) => Load(name).Current;

    private sealed class DoneThen(DoneCommand cmd) : IFightMenuChooser
    {
        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) => throw new InvalidOperationException();
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => cmd;
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => MoveInput.Return;
        public AimKey ChooseAim(FightMenu menu, AimView view) => AimKey.Exit;
        public bool ConfirmFlee(FightMenu menu) => false;
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => false;
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => null;
    }

    private sealed class Pick(FightCommand cmd) : IFightMenuChooser
    {
        public List<IReadOnlyList<FightCommand>> Shown { get; } = [];
        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries)
        {
            Shown.Add(entries);
            return cmd;
        }
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => DoneCommand.Quit;
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => MoveInput.Return;
        public AimKey ChooseAim(FightMenu menu, AimView view) => AimKey.Exit;
        public bool ConfirmFlee(FightMenu menu) => false;
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => false;
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => null;
    }

    [MovesFact]
    public void Guard_is_hidden_for_darts_and_shown_for_a_two_handed_sword()
    {
        var darts = Load("m07_done_menu");
        var thief = darts.Fight.List[darts.Current];
        Assert.Equal([DoneCommand.Delay, DoneCommand.Quit, DoneCommand.Speed, DoneCommand.Exit],
            new FightMenu(darts.Fight, thief, new DoneThen(DoneCommand.Exit)).DoneEntries());
        var sword = Load("m12_bakshi_done_menu");
        Assert.Equal([DoneCommand.Guard, DoneCommand.Delay, DoneCommand.Quit, DoneCommand.Speed, DoneCommand.Exit],
            new FightMenu(sword.Fight, sword.Fight.List[sword.Current], new DoneThen(DoneCommand.Exit)).DoneEntries());
    }

    [MovesFact]
    public void Delay_and_Quit_draw_nothing_and_the_next_pick_matches()
    {
        foreach (var (from, to, cmd) in new[]
                 {
                     ("m07_done_menu", "m08_after_delay", DoneCommand.Delay),
                     ("m12_bakshi_done_menu", "m13_after_quit", DoneCommand.Quit),
                 })
        {
            var s = Load(from);
            var c = s.Fight.List[s.Current];
            Assert.True(new FightMenu(s.Fight, c, new DoneThen(cmd)).DoneMenu());
            Assert.Equal(s.Seed, s.Rng.Seed);
            Assert.Equal(cmd == DoneCommand.Delay ? 1 : 0, s.Fight.Record(c).InitiativeValue);
            var next = s.Fight.NextCreature();
            Assert.Same(s.Fight.List[CurrentIndex(to)], next);
            Assert.Equal(Seed(to), s.Rng.Seed);
        }
    }

    [MovesFact]
    public void A_clerics_Turn_with_no_undead_draws_twice_before_the_next_pick()
    {
        var s = Load("s04_sleep_cast_2");
        var cleric = s.Fight.List[s.Current];
        var chooser = new Pick(FightCommand.Turn);
        new FightMenu(s.Fight, cleric, chooser).Run();
        Assert.Contains(FightCommand.Turn, chooser.Shown[0]);
        Assert.Equal(2, s.Rng.Draws);
        Assert.Equal(1, s.Fight.Record(cleric)[CombatRecord.TriedTurning]);
        Assert.Equal(0, s.Fight.Record(cleric).InitiativeValue);
        // The next pick is the sleeping goblin guard (list 12, initiative 4), which ended its turn before the guard
        // (13, also 4) that is current in t01; with no draws for the turn the pick would be 13.
        Assert.Same(s.Fight.List[12], s.Fight.NextCreature());
        var after = Load("t01_turn_pressed");
        Assert.Equal(13, after.Current);
        Assert.Equal(1, after.Fight.Record(after.Fight.List[s.Current])[CombatRecord.TriedTurning]);
    }

    [MovesFact]
    public void The_main_menu_shows_the_entries_of_the_capture()
    {
        var s = Load("m14_after_phineas_quit"); // Rhiannon (magic-user): Move View Aim Use Cast Quick Done
        var chooser = new Pick(FightCommand.Done);
        new FightMenu(s.Fight, s.Fight.List[s.Current], chooser).Run();
        Assert.Equal([FightCommand.Move, FightCommand.View, FightCommand.Aim, FightCommand.Use, FightCommand.Cast,
            FightCommand.Quick, FightCommand.Done], chooser.Shown[0]);
    }

    [PorFact]
    public void The_turn_undead_table_is_found_in_START_EXE()
    {
        var program = PorProgram.Open(PorData.Open())!;
        Assert.Equal(0x0447, program.TurnUndeadBase());
        var t = program.TurnUndeadTable()!;
        // Shape only (the values stay in the player's file): every value is −1 … 99, and the lowest row is easier to
        // turn than the highest at level 1.
        for (int row = 1; row <= 10; row++)
            for (int col = 1; col <= 10; col++)
                Assert.InRange(t.Value(row, col), -1, 99);
        Assert.True(t.Value(1, 1) < t.Value(10, 1));
    }
}
