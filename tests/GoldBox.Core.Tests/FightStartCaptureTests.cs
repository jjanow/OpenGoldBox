using System.Text.Json;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The fight start against DOSBox-X (capture <c>fight_quick</c>, private): the Slums goblin fight entered from 10,1
/// facing N right after bashing the door north of 10,2. From the RandSeed before the fight (<c>pre_fight</c>) some
/// number of draws (the monster loader's) leads to the seed the arena builder starts from; with it the dungeon arena
/// must equal the captured arena byte for byte, placement must give the captured slots (DS:5F2B, DS:65BD) and facings,
/// and exactly one round start (one 1d6 per creature) and one pick (one 1d100 per creature) later the seed must be the
/// captured one of <c>r0_start</c>.
/// </summary>
public class FightStartCaptureTests
{
    private static readonly string? Dir = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_quick") : null;

    public sealed class CaptureFactAttribute : FactAttribute
    {
        public CaptureFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir))
                Skip = "needs the private DOSBox captures (tests/fixtures-private/por/dosbox-v1.3/fight_quick)";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static JsonElement Load(string state) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, state, "fight.json"))).RootElement;

    [CaptureFact]
    public void Arena_placement_and_draws_match_the_goblin_fight()
    {
        var game = PorData.Open();
        var tables = PorCombatTables.Open(game)!;
        var pre = Load("pre_fight");
        var r0 = Load("r0_start");
        var v = r0.GetProperty("vars");
        int px = v.GetProperty("party_x").GetInt32(), py = v.GetProperty("party_y").GetInt32();
        int facing = v.GetProperty("party_facing").GetInt32();
        int block = v.GetProperty("current_ecl_block").GetInt32();
        Assert.Equal((10, 1, 0, 20, 1), (px, py, facing, block, v.GetProperty("ds_49fa").GetInt32()));

        // The Slums map (GEO2 block 20) with the bashed door north of 10,2 opened, as in memory.
        var map = PorGeo.Load(game, 2, 20);
        DoorMap.Open(map.Planes, 10, 2, Facing.North);

        var captured = Convert.FromHexString(r0.GetProperty("arena").GetString()!);
        uint seed = pre.GetProperty("vars").GetProperty("randseed").GetUInt32();
        int found = -1;
        CombatArena? arena = null;
        TurboPascalRandom? rng = null;
        for (int k = 0; k < 2000 && found < 0; k++)
        {
            var r = new TurboPascalRandom(seed);
            r.Skip(k);
            var a = new CombatArena();
            ArenaBuilder.BuildDungeon(a, tables, map, px, py, block, r.Next);
            if (a.Cells().AsSpan().SequenceEqual(captured.AsSpan(CombatArena.HeaderSize)))
            {
                found = k;
                arena = a;
                rng = r;
            }
        }
        Assert.True(found >= 0, "no seed after pre_fight builds the captured arena");

        // Placement on that arena: the captured list (party of 6, 8 guards and the leader), distance 0.
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var state = new EncounterState();
        var creatures = r0.GetProperty("creatures").EnumerateArray().ToList();
        foreach (var c in creatures)
        {
            var ch = new EclCharacter(Convert.FromHexString(c.GetProperty("record").GetString()!));
            memory.Party.Members.Add(ch);
            if (c.GetProperty("index").GetInt32() >= 6)
            {
                state.NotParty.Add(ch);
                state.Monsters.Add(ch);
            }
        }
        memory.Write(0x6E3E, 6);
        memory.Write(FightPlacement.DistanceCell, 0);
        var cmap = new CombatMap(tables, arena);
        var fight = new Fight(memory, state, new BaseItemTable(new byte[2048]), rng!.Next, new EndActionsController(), cmap);
        var area = new FightStartArea { ViewMode = 1, Map = map, PartyX = px, PartyY = py, PartyFacing = facing, EclBlock = block };
        FightPlacement.InitRecords(fight, tables, facing);
        FightPlacement.PlaceAll(fight, cmap, area);

        var lee = r0.GetProperty("locations_lee").EnumerateArray().ToList();
        for (int k = 1; k < cmap.SlotCount; k++)
        {
            var l = lee[k - 1];
            Assert.Same(memory.Party.Members[l.GetProperty("list_index").GetInt32()], cmap.SlotCreature(k));
            Assert.Equal((l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32()), cmap.SlotPosition(k));
            Assert.Equal(l.GetProperty("size").GetInt32(), cmap.SlotSize(k));
        }
        Assert.Equal(lee.Count(l => l.GetProperty("size").GetInt32() > 0) + 1, cmap.SlotCount);
        for (int i = 0; i < creatures.Count; i++)
            Assert.Equal(Convert.FromHexString(creatures[i].GetProperty("combat").GetString()!)[CombatRecord.Facing],
                fight.Record(memory.Party.Members[i])[CombatRecord.Facing]);
        Assert.Equal(captured.AsSpan(CombatArena.HeaderSize).ToArray(), cmap.Arena.Cells());

        // Placement draws nothing: one round start (1d6 each) and one pick (1d100 each) reach r0_start's seed.
        rng.Skip(2 * creatures.Count);
        Assert.Equal(v.GetProperty("randseed").GetUInt32(), rng.Seed);

        // fight_moves m01..m04: Darkstar (list 4, 29,14, movement 0x18) steps S, S, N with nobody adjacent: each
        // step costs 2 and sets the facing; no draws.
        var moves = new FightMoves(cmap);
        fight.Moves = moves;
        var darkstar = memory.Party.Members[4];
        var rec = fight.Record(darkstar);
        foreach (var (i, c) in creatures.Select((c, i) => (i, c)))
            Convert.FromHexString(c.GetProperty("combat").GetString()!).CopyTo(fight.Record(memory.Party.Members[i]).Bytes, 0);
        uint before = rng.Seed;
        foreach (var (dir, state2) in new[] { (Facing.South, "m02_step1_down"), (Facing.South, "m03_step2_down"), (Facing.North, "m04_step3_up") })
        {
            Assert.Equal(Game.StepResult.Moved, moves.Step(fight, darkstar, dir));
            var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, "..", "fight_moves", state2, "fight.json"))).RootElement;
            var l = m.GetProperty("locations_lee")[4];
            Assert.Equal((l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32()), cmap.Position(darkstar));
            Assert.Equal(Convert.FromHexString(m.GetProperty("creatures")[4].GetProperty("combat").GetString()!), rec.Bytes);
        }
        Assert.Equal(before, rng.Seed);
    }
}
