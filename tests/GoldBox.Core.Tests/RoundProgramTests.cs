using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The start of round (0x127FB with 0x1351E, 0x1364D, 0x1291E and the trigger-0x12 effects), the picks (0x9938), one
/// square (0x12F0D with the guards 0x12E22) and the terrain effects (0x2B9D1) against the program itself: fixtures made
/// by running the v1.3 routines under emulation (<c>tools/scripts/por_round_oracle.py</c>, private fixture
/// <c>program-v1.3/round.json</c>). Records, combat records, effect lists, picks, slots and the cell grid, RandSeed.
/// </summary>
public class RoundProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "round.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/round.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(File_!)).RootElement);
    private static readonly Lazy<BaseItemTable> Items =
        new(() => new BaseItemTable(Convert.FromHexString(Root.Value.GetProperty("items_5582").GetString()!)));
    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);

    /// <summary>Record bytes the program keeps pointers in (effects, items, slots, list link, combat record).</summary>
    private static bool PointerByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0x10C;

    private static EclCharacter Load(JsonElement c)
    {
        var x = new EclCharacter(Convert.FromHexString(c.GetProperty("record").GetString()!));
        foreach (var e in c.GetProperty("effects").EnumerateArray())
        {
            var r = new byte[EclCharacter.EffectRecordSize];
            Convert.FromHexString(e.GetString()!).CopyTo(r, 0);
            x.Effects.Add(r);
        }
        foreach (var it in c.GetProperty("items").EnumerateArray())
            x.Items.Add(Convert.FromHexString(it.GetString()!));
        return x;
    }

    private static string EffectsOf(EclCharacter x) => string.Join(" ", x.Effects.Select(e => Convert.ToHexString(e, 0, 5)));

    private sealed class Setup
    {
        public required List<EclCharacter> List;
        public required Fight Fight;
        public required EffectHandlers Fx;
        public required TurboPascalRandom Rng;
        public required EclMemory Memory;
        public CombatMap? Map;
    }

    private static Setup Build(JsonElement k, bool withMap)
    {
        var list = k.GetProperty("creatures").EnumerateArray().Select(Load).ToList();
        var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange(list);
        var fx = new EffectHandlers(rng.Next) { Recalculate = _ => { } }; // START 0x1758 is trapped in the oracle
        CombatMap? map = null;
        if (withMap)
        {
            map = new CombatMap(Tables.Value);
            Convert.FromHexString(k.GetProperty("cells").GetString()!).CopyTo(map.Arena.Bytes, CombatArena.HeaderSize);
            int slot = 0;
            foreach (var pl in k.GetProperty("places").EnumerateArray())
            {
                slot++;
                map.SetSlotCreature(slot, list[slot - 1]);
                map.SetSlotPosition(slot, pl[0].GetInt32(), pl[1].GetInt32());
                map.SetSlotSize(slot, pl[2].GetInt32());
            }
            map.SlotCount = slot + 1;
            map.Rebuild();
        }
        var fight = new Fight(memory, new EncounterState(), Items.Value, rng.Next, new EndActionsController(),
            map is null ? new GridGeometry() : map, fx);
        fx.Attach(fight);
        fx.InCombat = withMap; // DS:49F3: 0 for the round cases, 5 on a map
        if (k.TryGetProperty("combat", out var combats))
        {
            int i = 0;
            foreach (var cb in combats.EnumerateArray())
                Convert.FromHexString(cb.GetString()!).CopyTo(fight.Record(list[i++]).Bytes, 0);
        }
        return new Setup { List = list, Fight = fight, Fx = fx, Rng = rng, Memory = memory, Map = map };
    }

    private static void CompareCreatures(string what, Setup s, JsonElement after, List<string> fails)
    {
        int ci = 0;
        foreach (var a in after.EnumerateArray())
        {
            var c = s.List[ci];
            var want = Load(a);
            for (int b = 0; b < EclCharacter.RecordSize; b++)
                if (!PointerByte(b) && want[b] != c[b])
                    fails.Add($"{what} creature {ci} +{b:X2}: {c[b]} vs {want[b]}");
            if (EffectsOf(want) != EffectsOf(c))
                fails.Add($"{what} creature {ci} effects: {EffectsOf(c)} vs {EffectsOf(want)}");
            ci++;
        }
    }

    private static string Draws(uint from, uint to, TurboPascalRandom rng) =>
        $"{TurboPascalRandom.StepsBetween(from, to)} draws in the program, {rng.Draws} here";

    [OracleFact]
    public void Round_start_and_picks_match_0x127FB_and_0x9938()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("round").EnumerateArray())
        {
            var s = Build(k, false);
            s.Memory.Write(FightRules.SurpriseCell, (ushort)k.GetProperty("surprise").GetInt32());
            s.Memory.Write(FightRules.PartyMoveCell, (ushort)k.GetProperty("partyMove").GetInt32());
            s.Fight.State.RoundCounter = (byte)k.GetProperty("roundCounter").GetInt32();
            uint seed = k.GetProperty("seed").GetUInt32();
            foreach (var c in s.List)
                FightRules.BeginRound(c, s.Fight.Record(c), s.Fight.State, Items.Value, s.Memory, s.Rng.Next, s.Fx);
            string what = $"round {n}";
            CompareCreatures(what, s, k.GetProperty("started"), fails);
            int ci = 0;
            foreach (var cb in k.GetProperty("startedCombat").EnumerateArray())
            {
                var want = Convert.FromHexString(cb.GetString()!);
                var got = s.Fight.Record(s.List[ci]).Bytes;
                if (!want.AsSpan().SequenceEqual(got))
                    fails.Add($"{what} creature {ci} combat: {Convert.ToHexString(got)} vs {Convert.ToHexString(want)}");
                ci++;
            }
            uint started = k.GetProperty("seedStarted").GetUInt32();
            if (s.Rng.Seed != started)
                fails.Add($"{what}: round start {Draws(seed, started, s.Rng)}");
            s.Memory.Write(FightRules.SurpriseCell, 0);
            var picks = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                var c = s.Fight.NextCreature();
                picks.Add(c is null ? -1 : s.List.IndexOf(c));
                if (c is null)
                    break;
                s.Fight.Record(c).InitiativeValue = 0;
            }
            var wantPicks = k.GetProperty("picks").EnumerateArray().Select(p => p.GetInt32()).ToList();
            if (!picks.SequenceEqual(wantPicks))
                fails.Add($"{what}: picks {string.Join(",", picks)} vs {string.Join(",", wantPicks)}");
            if (s.Rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: after the picks {Draws(seed, k.GetProperty("seedAfter").GetUInt32(), s.Rng)}");
            // s52: 0x15103 for each creature (both ports), then 0x15074 into DS:6E2F (0xEE before)
            int fi = 0;
            foreach (var f in k.GetProperty("fastest").EnumerateArray())
            {
                var c = s.List[fi++];
                int a = FightMoves.HighestEnemyMovement(s.Fight, c), b = MonsterAi.HighestEnemyMovement(s.Fight, c);
                if (a != f.GetInt32() || b != f.GetInt32())
                    fails.Add($"{what}: 0x15103 creature {fi - 1}: {a} / {b} vs {f.GetInt32()}");
            }
            s.Fight.State.HostileHpPercent = 0xEE;
            FightRules.UpdateHostileHpPercent(s.List, s.Fight.State);
            if (s.Fight.State.HostileHpPercent != k.GetProperty("hpPercent").GetInt32())
                fails.Add($"{what}: 0x15074 {s.Fight.State.HostileHpPercent} vs {k.GetProperty("hpPercent").GetInt32()}");
            if (s.Rng.Seed != k.GetProperty("seedEnd").GetUInt32())
                fails.Add($"{what}: 0x15103 {Draws(k.GetProperty("seedAfter").GetUInt32(), k.GetProperty("seedEnd").GetUInt32(), s.Rng)}");
            n++;
        }
        Assert.True(n >= 100, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    [OracleFact]
    public void Picks_match_0x9938_on_every_initiative_byte()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("picks").EnumerateArray())
        {
            var inits = k.GetProperty("initiatives").EnumerateArray().Select(v => (byte)v.GetInt32()).ToList();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var cur = inits.ToList();
            var picks = new List<int>();
            var want = k.GetProperty("picks").EnumerateArray().Select(p => p.GetInt32()).ToList();
            var zeroed = k.GetProperty("zeroed").EnumerateArray().Select(z => z.GetBoolean()).ToList();
            for (int i = 0; i < want.Count; i++)
            {
                var r = CombatRules.NextCreature(cur.Select(b => (sbyte)b).ToList(), () => FightRules.Dice(rng.Next, 1, 100));
                picks.Add(r ?? -1);
                if (r is { } j && zeroed[i])
                    cur[j] = 0; // the oracle ended that creature's turn
            }
            if (!picks.SequenceEqual(want))
                fails.Add($"picks {n}: {string.Join(",", picks)} vs {string.Join(",", want)}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"picks {n}: RandSeed");
            n++;
        }
        Assert.True(n >= 100, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    [OracleFact]
    public void One_square_matches_0x12F0D_with_the_guards()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("move").EnumerateArray())
        {
            var s = Build(k, true);
            var map = s.Map!;
            var moves = new FightMoves(map);
            s.Fight.Moves = moves;
            var mover = s.List[0];
            var attacks = new List<string>();
            int? knockAt = null;
            string? knock = null;
            if (k.GetProperty("knock") is { ValueKind: JsonValueKind.Array } kn)
            {
                knockAt = kn[0].GetInt32();
                knock = kn[1].GetString();
            }
            s.Fight.AttackStarting = (a, d) =>
            {
                attacks.Add($"{s.List.IndexOf(a)}>{s.List.IndexOf(d)}");
                if (attacks.Count == knockAt)
                {
                    if (knock == "out")
                        d[FightRules.InPlay] = 0;
                    else
                        d.Effects.Insert(0, [0x35, 0, 0, 0xFF, 0, 0, 0, 0, 0]);
                }
            };
            // MakeAttack is trapped in the oracle, so the mover keeps its facing between the guards' attacks (each
            // 0x13FF1 measures from it); here MakeAttack turns it, so it is put back after each attack.
            byte facing = s.Fight.Record(mover)[CombatRecord.Facing];
            s.Fight.AttackMade = (_, d, _, _) => s.Fight.Record(d)[CombatRecord.Facing] = facing;
            int dir = k.GetProperty("dir").GetInt32();
            moves.MoveOne(s.Fight, mover, dir);
            string what = $"move {n} dir {dir}";
            var wantAttacks = k.GetProperty("attacks").EnumerateArray().Select(a => $"{a[0]}>{a[1]}").ToList();
            if (!attacks.SequenceEqual(wantAttacks))
                fails.Add($"{what}: guard attacks {string.Join(" ", attacks)} vs {string.Join(" ", wantAttacks)}");
            CompareCreatures(what, s, k.GetProperty("after"), fails);
            int ci = 0;
            foreach (var cb in k.GetProperty("afterCombat").EnumerateArray())
            {
                var want = Convert.FromHexString(cb.GetString()!);
                var got = s.Fight.Record(s.List[ci]).Bytes;
                // MakeAttack is trapped in the oracle: here it turns the creatures (+9), notes the target (+0x0A) and
                // ends an attacker's actions; only the bytes 0x12F0D / 0x12E22 / 0x13FF1 write are compared.
                int[] bytes = ci == 0 ? [CombatRecord.Movement, CombatRecord.Guarding, CombatRecord.FlankCount, CombatRecord.FlankSpread]
                    : [CombatRecord.Guarding, CombatRecord.FlankCount, CombatRecord.FlankSpread];
                foreach (int b in bytes)
                    if (want[b] != got[b])
                        fails.Add($"{what} creature {ci} combat +{b:X2}: {got[b]} vs {want[b]}");
                ci++;
            }
            int slot = 0;
            foreach (var sl in k.GetProperty("slots").EnumerateArray())
            {
                slot++;
                var (x, y) = map.SlotPosition(slot);
                if ((byte)x != sl[0].GetInt32() || (byte)y != sl[1].GetInt32())
                    fails.Add($"{what} slot {slot}: {x},{y} vs {sl[0]},{sl[1]}");
            }
            var grid = Convert.FromHexString(k.GetProperty("grid").GetString()!);
            for (int y = 0; y < 25; y++)
                for (int x = 0; x < 50; x++)
                    if (map.SlotAt(x, y) != grid[y * 50 + x])
                    {
                        fails.Add($"{what}: grid {x},{y} {map.SlotAt(x, y)} vs {grid[y * 50 + x]}");
                        y = 25;
                        break;
                    }
            if (s.Rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed");
            n++;
        }
        Assert.True(n >= 100, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// s52: the free attacks 0x130AD before a step. The oracle traps MakeAttack and logs each call with the attacker's
    /// phase (+4) and attacks 0x113 / 0x114 as 0x130AD left them; here MakeAttack runs whole (the mover has 255 HP, so
    /// it stays in play), so only the calls, those bytes and the attackers' +0x0A put back are compared.
    /// </summary>
    [OracleFact]
    public void Free_attacks_match_0x130AD()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("free").EnumerateArray())
        {
            var s = Build(k, true);
            var moves = new FightMoves(s.Map!);
            s.Fight.Moves = moves;
            var mover = s.List[0];
            var got = new List<string>();
            s.Fight.AttackStarting = (a, d) =>
                got.Add($"{s.List.IndexOf(a)}>{s.List.IndexOf(d)} phase {s.Fight.Record(a)[CombatRecord.AttackPhase]} " +
                        $"attacks {a[0x113]},{a[0x114]}");
            moves.AttacksOfOpportunity(s.Fight, mover, k.GetProperty("dir").GetInt32());
            var want = k.GetProperty("attacks").EnumerateArray()
                .Select(a => $"{a[0]}>{a[1]} phase {a[4]} attacks {a[5]},{a[6]}").ToList();
            string what = $"free {n}";
            if (!got.SequenceEqual(want))
                fails.Add($"{what}: {string.Join(" | ", got)} vs {string.Join(" | ", want)}");
            foreach (var a in k.GetProperty("attacks").EnumerateArray())
                if (a[2].GetInt32() != 1 || a[3].GetBoolean())
                    fails.Add($"{what}: the program's call was not from behind without ammunition");
            int ci = 0;
            foreach (var cb in k.GetProperty("afterCombat").EnumerateArray())
            {
                var wantCb = Convert.FromHexString(cb.GetString()!);
                var rec = s.Fight.Record(s.List[ci]);
                bool otherNil = wantCb.AsSpan(0x0A, 4).IndexOfAnyExcept((byte)0) < 0;
                if (otherNil != (rec.Other is null))
                    fails.Add($"{what} creature {ci}: +0x0A {(rec.Other is null ? "nil" : "set")} vs {(otherNil ? "nil" : "set")}");
                ci++;
            }
            n++;
        }
        Assert.True(n >= 100, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>s52: the flee 0x13465 whole (with 0x1291E, 0x15103 and 0x2BDF0): records, slots, combat records,
    /// RandSeed; the program returns 1 (START 0x31D7) whether the creature got away or not.</summary>
    [OracleFact]
    public void Flee_matches_0x13465()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("flee").EnumerateArray())
        {
            var s = Build(k, true);
            var moves = new FightMoves(s.Map!);
            s.Fight.Moves = moves;
            s.Memory.Write(FightRules.PartyMoveCell,
                BitConverter.ToUInt16(Convert.FromHexString(k.GetProperty("partyMove").GetString()!)));
            uint seed = k.GetProperty("seed").GetUInt32();
            moves.Flee(s.Fight, s.List[0]);
            string what = $"flee {n}";
            if (k.GetProperty("result").GetInt32() != 1)
                fails.Add($"{what}: the program returned {k.GetProperty("result").GetInt32()}");
            CompareCreatures(what, s, k.GetProperty("after"), fails);
            int ci = 0;
            foreach (var cb in k.GetProperty("afterCombat").EnumerateArray())
            {
                var want = Convert.FromHexString(cb.GetString()!);
                var got = s.Fight.Record(s.List[ci]).Bytes;
                foreach (int b in new[] { 0, 3, 6, 7 })
                    if (want[b] != got[b])
                        fails.Add($"{what} creature {ci} combat +{b:X2}: {got[b]} vs {want[b]}");
                ci++;
            }
            int slot = 0;
            foreach (var sl in k.GetProperty("slots").EnumerateArray())
            {
                slot++;
                if (s.Map!.SlotSize(slot) != sl[3].GetInt32())
                    fails.Add($"{what}: slot {slot} size {s.Map.SlotSize(slot)} vs {sl[3].GetInt32()}");
            }
            if (s.Rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: {Draws(seed, k.GetProperty("seedAfter").GetUInt32(), s.Rng)}");
            n++;
        }
        Assert.True(n >= 100, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    [OracleFact]
    public void Terrain_effects_match_0x2B9D1()
    {
        var fails = new List<string>();
        int n = 0;
        foreach (var k in Root.Value.GetProperty("terrain").EnumerateArray())
        {
            var s = Build(k, true);
            uint seed = k.GetProperty("seed").GetUInt32();
            foreach (var c in s.List)
                s.Fx.TerrainCheck(c);
            string what = $"terrain {n}";
            CompareCreatures(what, s, k.GetProperty("after"), fails);
            int ci = 0;
            foreach (var cb in k.GetProperty("afterCombat").EnumerateArray())
            {
                var want = Convert.FromHexString(cb.GetString()!);
                var got = s.Fight.Record(s.List[ci]).Bytes;
                if (!want.AsSpan().SequenceEqual(got))
                    fails.Add($"{what} creature {ci} combat: {Convert.ToHexString(got)} vs {Convert.ToHexString(want)}");
                ci++;
            }
            if (s.Rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: {Draws(seed, k.GetProperty("seedAfter").GetUInt32(), s.Rng)}");
            foreach (var h in k.GetProperty("handlers").EnumerateArray())
                if (h[2].GetBoolean())
                    fails.Add($"{what}: handler {h[0]} got a record from the program");
            n++;
        }
        Assert.True(n >= 50, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }
}
