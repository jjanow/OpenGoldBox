using System.Text.Json;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// The whole all-Quick goblin fight (fixtures <c>fight_quick/</c>, README "fight_quick") replayed through the wired
/// fight (<see cref="FightKit.Create"/>: arena, placement, map, moves, spells, effects, AI, menu): from the seed the
/// arena builder starts from (as <see cref="FightStartCaptureTests"/>), with a menu chooser that answers Quick at every
/// player menu (as the capture's Q). At each captured state (ordered by draw count from <c>r0_start</c>) the replay is
/// stopped right before the next draw and compared: round counter, current creature, squares, HP, status, in play,
/// combat records.
/// </summary>
public class FightQuickReplayTests(ITestOutputHelper output)
{
    private static readonly string? Dir = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_quick") : null;

    public sealed class QuickFactAttribute : FactAttribute
    {
        public QuickFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir) || !PorData.Present)
                Skip = "needs the private DOSBox captures (fight_quick) and " + PorData.EnvVar;
        }
    }

    /// <summary>Draws from r0_start to each state (README).</summary>
    public static readonly (string Name, int Draws)[] States =
    [
        ("r0_start", 0), ("r0_menu2", 24), ("r0_menu3", 47), ("r0_menu4", 91), ("r0_menu5", 134), ("r0_menu6", 225),
        ("auto_01", 233), ("run3_01", 277), ("auto_02", 327), ("run2_01", 369), ("auto_03", 391), ("run2_02", 433),
        ("auto_04", 435), ("run3_03", 475), ("auto_05", 494), ("auto_06", 497), ("run2_04", 519), ("run3_04", 563),
        ("auto_07", 588), ("run2_05", 604), ("auto_08", 680), ("run3_05", 700), ("end_continue_question", 715),
    ];

    private static JsonElement Load(string state) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, state, "fight.json"))).RootElement;

    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    /// <summary>Answers Quick at every menu, no to every question.</summary>
    private sealed class QuickChooser : IFightMenuChooser
    {
        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) => FightCommand.Quick;
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => DoneCommand.Exit;
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => MoveInput.Escape;
        public AimKey ChooseAim(FightMenu menu, AimView view) => AimKey.Exit;
        public bool ConfirmFlee(FightMenu menu) => false;
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => false;
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => null;
    }

    private sealed class StopReplay : Exception;

    /// <summary>Moves that take a snapshot after every step (moves draw nothing, so captures fall between steps).</summary>
    private sealed class SnappingMoves(IFightMoves inner, Action snap) : IFightMoves
    {
        public Game.StepResult Step(Fight fight, EclCharacter c, int direction)
        {
            var r = inner.Step(fight, c, direction);
            snap();
            return r;
        }

        public bool Flee(Fight fight, EclCharacter c)
        {
            bool r = inner.Flee(fight, c);
            snap();
            return r;
        }
    }

    /// <summary>The replay and its comparison: one line per difference, per state, in draw order.</summary>
    public sealed record Result(List<(string State, List<string> Diffs)> States, int DrawsAtEnd, int Rounds, List<string> Diagnostics);

    public static Result Replay(int stopAfterDraws = int.MaxValue, List<string>? trace = null)
    {
        var game = PorData.Open();
        var kit = PorFight.Kit(game)!;
        var pre = Load("pre_fight");
        var r0 = Load("r0_start");
        var v = r0.GetProperty("vars");
        int px = v.GetProperty("party_x").GetInt32(), py = v.GetProperty("party_y").GetInt32();
        int facing = v.GetProperty("party_facing").GetInt32();
        int block = v.GetProperty("current_ecl_block").GetInt32();
        var geo = PorGeo.Load(game, 2, 20);
        DoorMap.Open(geo.Planes, 10, 2, Facing.North);

        // The arena builder's starting seed: the first k after pre_fight that builds the captured arena.
        var captured = Hex(r0.GetProperty("arena")).AsSpan(CombatArena.HeaderSize).ToArray();
        uint seed = pre.GetProperty("vars").GetProperty("randseed").GetUInt32();
        TurboPascalRandom? rng = null;
        for (int k = 0; k < 2000 && rng is null; k++)
        {
            var r = new TurboPascalRandom(seed);
            r.Skip(k);
            var a = new CombatArena();
            var probe = new TurboPascalRandom(r.Seed);
            ArenaBuilder.BuildDungeon(a, kit.Tables, geo, px, py, block, probe.Next);
            if (a.Cells().AsSpan().SequenceEqual(captured))
                rng = r;
        }
        Assert.NotNull(rng);

        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var state = new EncounterState();
        foreach (var c in r0.GetProperty("creatures").EnumerateArray())
        {
            var ch = new EclCharacter(Hex(c.GetProperty("record")));
            foreach (var it in c.GetProperty("items").EnumerateArray())
                ch.Items.Add(Hex(it.GetProperty("record")));
            foreach (var e in c.GetProperty("effects").EnumerateArray())
                ch.Effects.Add(Hex(e.GetProperty("record")));
            memory.Party.Members.Add(ch);
            if (c.GetProperty("index").GetInt32() >= 6)
            {
                state.NotParty.Add(ch);
                state.Monsters.Add(ch);
            }
        }
        memory.Write(0x6E3E, 6);
        memory.Write(FightPlacement.DistanceCell, 0);
        // VM cells of r0_start.sav (dosbox_state.py dump --areas): $6DC6 = 60; $6DCB, $6E70–$6E72, $49E5 = 0.
        memory.Write(MonsterAi.MoraleThresholdCell, 60);

        // Draw counter: draws before r0_start are the arena's (counted from the builder's seed) and the round start
        // and pick of round 0 (2 per creature); r0_start = 0.
        int creatures = memory.Party.Members.Count;
        int drawn = 0, origin = -1;
        var checkpoints = new Queue<(string Name, int Draws)>(States);
        var results = new List<(string, List<string>)>();
        Fight? fight = null;
        var list = memory.Party.Members;
        // Snapshots between a checkpoint's draw and the next one: at the draw, after each step on the map, before the next.
        var between = new Dictionary<int, List<Dictionary<string, string>>>();
        void Check(string name, int n)
        {
            var seen = between.GetValueOrDefault(n) ?? [];
            seen.Add(Fields(fight!, rng!.Seed));
            results.Add((name, Compare(Fields(Load(name)), seen)));
        }
        void Snap()
        {
            int n = drawn - origin;
            if (origin >= 0 && fight is not null && checkpoints.Any(k => k.Draws == n))
                (between.TryGetValue(n, out var l) ? l : between[n] = []).Add(Fields(fight, rng!.Seed));
        }
        ushort Random(ushort range)
        {
            if (origin >= 0)
            {
                int n = drawn - origin;
                while (checkpoints.Count > 0 && checkpoints.Peek().Draws == n)
                    Check(checkpoints.Dequeue().Name, n);
                if (n >= stopAfterDraws)
                    throw new StopReplay();
            }
            drawn++;
            ushort value = rng!.Next(range);
            Snap();
            if (trace is not null && fight is not null)
                trace.Add($"{drawn - origin} d{range}={value + 1} #{(fight.State.Current is { } cc0 ? list.IndexOf(cc0) : -1)} r{fight.State.RoundCounter} " +
                          string.Join("<", new System.Diagnostics.StackTrace().GetFrames().Skip(1).Select(f => f.GetMethod()?.Name)
                              .Where(n => n is not null && !n.Contains("Random") && !n.StartsWith("<") && n != "Dice").Take(4)) + " " +
                          string.Join(" ", list.Select(c => $"{c[FightRules.Hp]}/{c[FightRules.Status]}")));
            return value;
        }

        var area = new FightStartArea { ViewMode = 1, Map = geo, PartyX = px, PartyY = py, PartyFacing = facing, EclBlock = block };
        fight = kit.Create(memory, state, Random, area, new QuickChooser());
        fight.NoContinueQuestion = false;
        fight.Moves = new SnappingMoves(fight.Moves, Snap);
        origin = drawn + 2 * creatures;
        int rounds = 0;
        try
        {
            rounds = fight.Run(50);
            int n = drawn - origin;
            while (checkpoints.Count > 0 && checkpoints.Peek().Draws == n)
                Check(checkpoints.Dequeue().Name, n);
        }
        catch (StopReplay)
        {
        }
        var diags = new List<string>(fight.Diagnostics);
        return new Result(results, drawn - origin, rounds, diags);
    }

    /// <summary>
    /// Differences between a captured state and the replay. A capture is taken somewhere between its draw and the
    /// next one (often in a message delay inside an attack or between two steps), so a field matches when the captured
    /// value is the replay's at one of the snapshots <paramref name="seen"/> taken in that window (at the draw, after
    /// each step, right before the next draw).
    /// </summary>
    private static List<string> Compare(Dictionary<string, string> cap, List<Dictionary<string, string>> seen)
    {
        var diffs = new List<string>();
        foreach (var (k, v) in cap)
        {
            var ours = seen.Select(d => d.GetValueOrDefault(k, "?")).Distinct().ToList();
            if (!ours.Contains(v))
                diffs.Add($"{k} {string.Join("/", ours)} vs {v}");
        }
        return diffs;
    }

    private static readonly (int Off, string Name)[] RecordFields =
        [(FightRules.Hp, "hp"), (FightRules.Status, "status"), (FightRules.InPlay, "inplay"), (0x10F, "auto")];

    /// <summary>The compared fields of the replay: RandSeed, round, current, squares, record bytes, combat records.</summary>
    private static Dictionary<string, string> Fields(Fight fight, uint seed)
    {
        var f = new Dictionary<string, string>();
        var list = fight.List;
        f["randseed"] = seed.ToString("x8");
        f["round"] = fight.State.RoundCounter.ToString();
        f["current"] = (fight.State.Current is { } oc ? list.IndexOf(oc) : -1).ToString();
        var map = (CombatMap)fight.Geometry;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            int size = map.IconSize(c);
            f[$"#{i} at"] = size > 0 ? $"{map.Position(c)} size {size}" : "off";
            foreach (var (off, name) in RecordFields)
                f[$"#{i} {name}"] = c[off].ToString();
            var ours = fight.Record(c).Bytes;
            for (int b = 0; b < ours.Length; b++)
                if (b is < 0x0A or > 0x0D)
                    f[$"#{i} +{b:X2}"] = ours[b].ToString();
            f[$"#{i} other"] = (fight.Record(c).Other is { } o ? list.IndexOf(o) : -1).ToString();
        }
        return f;
    }

    /// <summary>The same fields of a captured state.</summary>
    private static Dictionary<string, string> Fields(JsonElement cap)
    {
        var f = new Dictionary<string, string>();
        f["randseed"] = cap.GetProperty("vars").GetProperty("randseed").GetUInt32().ToString("x8");
        f["round"] = cap.GetProperty("ds_bytes").GetProperty("6DE3").GetByte().ToString();
        long cur = cap.GetProperty("pointers").GetProperty("current_5d92").GetProperty("linear").GetInt64();
        var cc = cap.GetProperty("creatures").EnumerateArray().ToList();
        int capCur = cc.FindIndex(c => c.GetProperty("linear").GetInt64() == cur);
        f["current"] = capCur.ToString();
        foreach (var l in cap.GetProperty("locations_lee").EnumerateArray())
        {
            if (l.GetProperty("list_index").ValueKind != JsonValueKind.Number)
                continue;
            int size = l.GetProperty("size").GetInt32();
            f[$"#{l.GetProperty("list_index").GetInt32()} at"] = size > 0
                ? $"{(l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32())} size {size}" : "off";
        }
        var linear = cc.Select(c => c.GetProperty("linear").GetInt64()).ToList();
        for (int i = 0; i < cc.Count; i++)
        {
            var rec = Hex(cc[i].GetProperty("record"));
            foreach (var (off, name) in RecordFields)
                if (!(off == 0x10F && i == capCur && rec[off] == 0)) // the menu shown before Q
                    f[$"#{i} {name}"] = rec[off].ToString();
            var comb = Hex(cc[i].GetProperty("combat"));
            for (int b = 0; b < comb.Length; b++)
                if (b is < 0x0A or > 0x0D)
                    f[$"#{i} +{b:X2}"] = comb[b].ToString();
            // +0x0A: the remembered target, a far pointer (offset, segment).
            long p = comb[0x0C] * 16L + comb[0x0D] * 4096L + comb[0x0A] + comb[0x0B] * 256L;
            f[$"#{i} other"] = (p == 0 ? -1 : linear.IndexOf(p)).ToString();
        }
        return f;
    }

    /// <summary>
    /// Every one of the 23 states matches (RandSeed, round, current creature, squares, HP, status, in play, Quick,
    /// combat records and remembered targets), and the fight ends after 3 rounds with the 715th draw, the monsters gone.
    /// </summary>
    [QuickFact]
    public void Replays_the_whole_fight_like_DOSBox()
    {
        var r = Replay();
        Assert.Equal(States.Length, r.States.Count);
        foreach (var (state, diffs) in r.States)
            Assert.True(diffs.Count == 0, $"{state}: {string.Join("; ", diffs)}");
        Assert.Equal(715, r.DrawsAtEnd);
        Assert.Equal(3, r.Rounds);
    }

    /// <summary>Set GOLDBOX_FIGHT_TRACE to print every draw (who, where from) and each state's differences.</summary>
    [QuickFact]
    public void Dump()
    {
        if (Environment.GetEnvironmentVariable("GOLDBOX_FIGHT_TRACE") is null)
            return;
        var trace = new List<string>();
        var r = Replay(trace: trace);
        foreach (var t in trace)
            output.WriteLine(t);
        foreach (var (s, d) in r.States)
            output.WriteLine($"{s}: {(d.Count == 0 ? "ok" : string.Join("; ", d))}");
        output.WriteLine($"draws at end {r.DrawsAtEnd}, rounds {r.Rounds}");
        foreach (var d in r.Diagnostics)
            output.WriteLine(d);
    }
}
