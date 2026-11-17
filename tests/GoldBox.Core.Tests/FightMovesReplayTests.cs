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
/// The player-driven goblin fight (fixtures <c>fight_moves/</c>, README "fight_moves") replayed through the wired fight
/// (<see cref="FightKit.Create"/>) with the player's menu (<see cref="MenuController"/>, <see cref="FightMenu"/>)
/// answered by a script of the keys pressed in DOSBox-X: Darkstar moves S, S, N and undoes it with Escape, moves S, S
/// and keeps it, delays; Thrender aims, Next, Target (melee); Bakshi and Phineas quit; Rhiannon casts Sleep at the
/// goblin next to her (Next, Target); Sean turns undead with none on the map; Darkstar's delayed turn aims and Next.
/// The fight starts from <c>fight_quick/r0_start</c> (the same DOSBox-X state <c>m00_start</c> was reloaded from), the
/// arena seed found as <see cref="FightQuickReplayTests"/> does. A state captured at a prompt is compared with the
/// replay at that prompt; the others (taken during a message delay) with the snapshots between their draw and the next
/// one (README draw counts), as <see cref="FightQuickReplayTests"/> does.
/// </summary>
public class FightMovesReplayTests(ITestOutputHelper output)
{
    private static readonly string? Root = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory);
    private static readonly string? Dir = Root is { } d ? Path.Combine(d, "fight_moves") : null;
    private static readonly string? QuickDir = Root is { } q ? Path.Combine(q, "fight_quick") : null;

    public sealed class MovesReplayFactAttribute : FactAttribute
    {
        public MovesReplayFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir) || QuickDir is null || !Directory.Exists(QuickDir) || !PorData.Present)
                Skip = "needs the private DOSBox captures (fight_moves, fight_quick) and " + PorData.EnvVar;
        }
    }

    /// <summary>States taken during a message delay: draws from r0_start (README), compared in that window.</summary>
    public static readonly (string Name, int Draws)[] Windowed =
        [("s03_sleep_cast_1", 110), ("t01_turn_pressed", 180)];

    private enum Ask { Menu, Done, Move, Aim, Spell }

    /// <summary>One key: who is asked (list index), which prompt, the answer, and the state captured at that prompt.</summary>
    private sealed record Key(int Who, Ask Ask, object Answer, string? State = null);

    private const int Thrender = 0, Bakshi = 1, Rhiannon = 2, Sean = 3, Darkstar = 4, Phineas = 5;
    private const int Sleep = 21;
    private const int S = 4, N = 0;

    private static readonly Key[] Script =
    [
        new(Darkstar, Ask.Menu, FightCommand.Move, "m00_start"),
        new(Darkstar, Ask.Move, new MoveInput(S), "m01_move_mode"),
        new(Darkstar, Ask.Move, new MoveInput(S), "m02_step1_down"),
        new(Darkstar, Ask.Move, new MoveInput(N), "m03_step2_down"),
        new(Darkstar, Ask.Move, MoveInput.Escape, "m04_step3_up"),
        new(Darkstar, Ask.Menu, FightCommand.Move, "m05_after_escape"),
        new(Darkstar, Ask.Move, new MoveInput(S)),
        new(Darkstar, Ask.Move, new MoveInput(S)),
        new(Darkstar, Ask.Move, MoveInput.Return, "m06_move2_return"),
        new(Darkstar, Ask.Menu, FightCommand.Done),
        new(Darkstar, Ask.Done, DoneCommand.Delay, "m07_done_menu"),
        new(Thrender, Ask.Menu, FightCommand.Aim, "m08_after_delay"),
        new(Thrender, Ask.Aim, AimKey.Next, "m09_thrender_aim"),
        new(Thrender, Ask.Aim, AimKey.Target, "m10_thrender_next"),
        new(Bakshi, Ask.Menu, FightCommand.Done, "m11_after_melee_bakshi_menu"),
        new(Bakshi, Ask.Done, DoneCommand.Quit, "m12_bakshi_done_menu"),
        new(Phineas, Ask.Menu, FightCommand.Done, "m13_after_quit"),
        new(Phineas, Ask.Done, DoneCommand.Quit),
        new(Rhiannon, Ask.Menu, FightCommand.Cast, "m14_after_phineas_quit"),
        new(Rhiannon, Ask.Spell, Sleep, "s01_sleep_chosen"),
        new(Rhiannon, Ask.Aim, AimKey.Next, "s02_sleep_target"),
        new(Rhiannon, Ask.Aim, AimKey.Target),
        new(Sean, Ask.Menu, FightCommand.Turn, "s04_sleep_cast_2"),
        new(Darkstar, Ask.Menu, FightCommand.Aim, "t02_after_turn"),
        new(Darkstar, Ask.Aim, AimKey.Next),
        new(Darkstar, Ask.Aim, AimKey.Exit, "r01_darkstar_aim_next"),
    ];

    private static JsonElement Load(string dir, string state) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, state, "fight.json"))).RootElement;

    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    private sealed class StopReplay : Exception;

    /// <summary>The script: each prompt checks who is asked and what, records the state named there, answers.</summary>
    private sealed class ScriptChooser(Func<EclCharacter, int> index, Action<string?> atPrompt, List<string> log) : IFightMenuChooser
    {
        private int _next;
        public int Used => _next;

        private object Answer(FightMenu menu, Ask ask, string what)
        {
            if (_next >= Script.Length)
                throw new StopReplay();
            var k = Script[_next];
            int who = index(menu.Creature);
            log.Add($"#{_next} {ask} for #{who} ({what})");
            if (k.Who != who || k.Ask != ask)
                throw new InvalidOperationException($"key {_next}: expected {k.Ask} for #{k.Who}, the fight asks {ask} for #{who} ({what})");
            atPrompt(k.State);
            _next++;
            return k.Answer;
        }

        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) =>
            (FightCommand)Answer(menu, Ask.Menu, string.Join(" ", entries));
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) =>
            (DoneCommand)Answer(menu, Ask.Done, string.Join(" ", entries));
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) =>
            (MoveInput)Answer(menu, Ask.Move, $"{movementLeft} left");
        public AimKey ChooseAim(FightMenu menu, AimView view) =>
            (AimKey)Answer(menu, Ask.Aim, $"#{index(view.Candidate)} at {view.Distance} range {view.Range} target {view.TargetOffered}");
        public bool ConfirmFlee(FightMenu menu) => throw new InvalidOperationException("flee asked");
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => throw new InvalidOperationException("attack ally asked");
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) =>
            (int)Answer(menu, Ask.Spell, string.Join(" ", castable));
    }

    /// <summary>Moves that take a snapshot after every step (steps draw nothing unless attacked).</summary>
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

    public sealed record Result(List<(string State, List<string> Diffs)> States, int KeysUsed, int Draws, List<string> Log, List<string> Diagnostics);

    public static Result Replay(List<string>? trace = null)
    {
        var game = PorData.Open();
        var kit = PorFight.Kit(game)!;
        var pre = Load(QuickDir!, "pre_fight");
        var r0 = Load(QuickDir!, "r0_start");
        var v = r0.GetProperty("vars");
        int px = v.GetProperty("party_x").GetInt32(), py = v.GetProperty("party_y").GetInt32();
        int facing = v.GetProperty("party_facing").GetInt32();
        int block = v.GetProperty("current_ecl_block").GetInt32();
        var geo = PorGeo.Load(game, 2, 20);
        DoorMap.Open(geo.Planes, 10, 2, Facing.North);

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
        memory.Write(MonsterAi.MoraleThresholdCell, 60);

        var list = memory.Party.Members;
        int creatures = list.Count;
        int drawn = 0, origin = -1;
        Fight? fight = null;
        var results = new List<(string, List<string>)>();
        var windows = new Queue<(string Name, int Draws)>(Windowed);
        var between = new Dictionary<int, List<Dictionary<string, string>>>();
        var log = new List<string>();

        void CheckWindow(string name, int n)
        {
            var seen = between.GetValueOrDefault(n) ?? [];
            seen.Add(Fields(fight!, rng!.Seed));
            results.Add((name, Compare(Fields(Load(Dir!, name)), seen)));
        }
        void Snap()
        {
            int n = drawn - origin;
            if (origin >= 0 && fight is not null && windows.Any(k => k.Draws == n))
                (between.TryGetValue(n, out var l) ? l : between[n] = []).Add(Fields(fight, rng!.Seed));
        }
        void AtPrompt(string? name)
        {
            Snap();
            if (name is not null)
                results.Add((name, Compare(Fields(Load(Dir!, name)), [Fields(fight!, rng!.Seed)])));
        }
        ushort Random(ushort range)
        {
            if (origin >= 0)
            {
                int n = drawn - origin;
                while (windows.Count > 0 && windows.Peek().Draws == n)
                    CheckWindow(windows.Dequeue().Name, n);
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

        var chooser = new ScriptChooser(c => list.IndexOf(c), AtPrompt, trace ?? log);
        var area = new FightStartArea { ViewMode = 1, Map = geo, PartyX = px, PartyY = py, PartyFacing = facing, EclBlock = block };
        fight = kit.Create(memory, state, Random, area, chooser);
        fight.NoContinueQuestion = false;
        fight.Moves = new SnappingMoves(fight.Moves, Snap);
        origin = drawn + 2 * creatures;
        try
        {
            fight.Run(1);
        }
        catch (StopReplay)
        {
        }
        return new Result(results, chooser.Used, drawn - origin, log, [.. fight.Diagnostics]);
    }

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

    /// <summary>The compared fields: RandSeed, round, current, squares, record bytes, combat records, effects,
    /// memorised spells.</summary>
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
            f[$"#{i} spells"] = Convert.ToHexString(c.Record.AsSpan(FightMenu.SpellsFirst, FightMenu.SpellsLast - FightMenu.SpellsFirst + 1));
            f[$"#{i} effects"] = string.Join(",", c.Effects.Select(e => Convert.ToHexString(e.AsSpan(0, 5))));
            var ours = fight.Record(c).Bytes;
            for (int b = 0; b < ours.Length; b++)
                if (b is < 0x0A or > 0x0D)
                    f[$"#{i} +{b:X2}"] = ours[b].ToString();
            f[$"#{i} other"] = (fight.Record(c).Other is { } o ? list.IndexOf(o) : -1).ToString();
        }
        return f;
    }

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
                f[$"#{i} {name}"] = rec[off].ToString();
            f[$"#{i} spells"] = Convert.ToHexString(rec.AsSpan(FightMenu.SpellsFirst, FightMenu.SpellsLast - FightMenu.SpellsFirst + 1));
            f[$"#{i} effects"] = string.Join(",", cc[i].GetProperty("effects").EnumerateArray()
                .Select(e => Convert.ToHexString(Hex(e.GetProperty("record")).AsSpan(0, 5))));
            var comb = Hex(cc[i].GetProperty("combat"));
            for (int b = 0; b < comb.Length; b++)
                if (b is < 0x0A or > 0x0D)
                    f[$"#{i} +{b:X2}"] = comb[b].ToString();
            long p = comb[0x0C] * 16L + comb[0x0D] * 4096L + comb[0x0A] + comb[0x0B] * 256L;
            f[$"#{i} other"] = (p == 0 ? -1 : linear.IndexOf(p)).ToString();
        }
        return f;
    }

    /// <summary>Every key of the script is asked for in order, by the creature it was pressed for, and every captured
    /// state matches.</summary>
    [MovesReplayFact]
    public void Replays_the_player_driven_fight_like_DOSBox()
    {
        var r = Replay();
        foreach (var l in r.Log)
            output.WriteLine(l);
        foreach (var (s, d) in r.States)
            output.WriteLine($"{s}: {(d.Count == 0 ? "ok" : string.Join("; ", d))}");
        foreach (var d in r.Diagnostics)
            output.WriteLine(d);
        Assert.Equal(Script.Length, r.KeysUsed);
        Assert.Equal(Script.Count(k => k.State is not null) + Windowed.Length, r.States.Count);
        foreach (var (state, diffs) in r.States)
            Assert.True(diffs.Count == 0, $"{state}: {string.Join("; ", diffs)}");
        Assert.Equal(240, r.Draws);
    }

    /// <summary>Set GOLDBOX_FIGHT_TRACE to print every draw and prompt.</summary>
    [MovesReplayFact]
    public void Dump()
    {
        if (Environment.GetEnvironmentVariable("GOLDBOX_FIGHT_TRACE") is null)
            return;
        var trace = new List<string>();
        Result r;
        try
        {
            r = Replay(trace);
        }
        finally
        {
            foreach (var t in trace)
                output.WriteLine(t);
        }
        foreach (var (s, d) in r.States)
            output.WriteLine($"{s}: {(d.Count == 0 ? "ok" : string.Join("; ", d))}");
        output.WriteLine($"keys {r.KeysUsed}/{Script.Length}, draws {r.Draws}");
        foreach (var d in r.Diagnostics)
            output.WriteLine(d);
    }
}
