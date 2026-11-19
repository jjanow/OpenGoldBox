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
/// The fight start against DOSBox-X (captures <c>fight_quick</c> and <c>fight_west</c>, private). <c>fight_quick</c>:
/// the Slums goblin fight entered from 10,1 facing N right after bashing the door north of 10,2. <c>fight_west</c>
/// (s26): test save D (<c>por_test_saves.py</c>: save A's party with 1 HP; vector 0 = CLRMON, LOADMON 2 ×8, COMBAT) at
/// 15,4 facing W, the fight of the s24 Alt-X check. From the RandSeed before the fight (<c>pre_fight</c>) some number
/// of draws (the monster loader's) leads to the seed the arena builder starts from; with it the dungeon arena must
/// equal the captured arena byte for byte, placement must give the captured slots (DS:5F2B, DS:65BD) and facings, and
/// exactly one round start (one 1d6 per creature) and one pick (one 1d100 per creature) later the seed must be the
/// captured one of <c>r0_start</c>.
/// </summary>
public class FightStartCaptureTests
{
    private static readonly string? Root = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory);

    public sealed class CaptureFactAttribute : FactAttribute
    {
        public CaptureFactAttribute()
        {
            if (Root is null || !Directory.Exists(Path.Combine(Root, "fight_quick")) || !Directory.Exists(Path.Combine(Root, "fight_west")))
                Skip = "needs the private DOSBox captures (tests/fixtures-private/por/dosbox-v1.3/fight_quick, fight_west)";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static JsonElement Load(string capture, string state) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root!, capture, state, "fight.json"))).RootElement;

    [CaptureFact]
    public void Arena_placement_and_draws_match_the_goblin_fight()
    {
        var game = PorData.Open();
        // The Slums map (GEO2 block 20) with the bashed door north of 10,2 opened, as in memory.
        var map = PorGeo.Load(game, 2, 20);
        DoorMap.Open(map.Planes, 10, 2, Facing.North);
        var (fight, cmap, memory, rng, creatures) = StartMatches(game, "fight_quick", map, (10, 1, Facing.North, 20));

        // fight_moves m01..m04: Darkstar (list 4, 29,14, movement 0x18) steps S, S, N with nobody adjacent: each
        // step costs 2 and sets the facing; no draws.
        var moves = new FightMoves(cmap);
        fight.Moves = moves;
        var darkstar = memory.Party!.Members[4];
        var rec = fight.Record(darkstar);
        foreach (var (i, c) in creatures.Select((c, i) => (i, c)))
            Convert.FromHexString(c.GetProperty("combat").GetString()!).CopyTo(fight.Record(memory.Party.Members[i]).Bytes, 0);
        uint before = rng.Seed;
        foreach (var (dir, state2) in new[] { (Facing.South, "m02_step1_down"), (Facing.South, "m03_step2_down"), (Facing.North, "m04_step3_up") })
        {
            Assert.Equal(Game.StepResult.Moved, moves.Step(fight, darkstar, dir));
            var l = Load("fight_moves", state2).GetProperty("locations_lee")[4];
            Assert.Equal((l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32()), cmap.Position(darkstar));
            Assert.Equal(Convert.FromHexString(Load("fight_moves", state2).GetProperty("creatures")[4].GetProperty("combat").GetString()!), rec.Bytes);
        }
        Assert.Equal(before, rng.Seed);
    }

    [CaptureFact]
    public void Arena_placement_and_draws_match_the_save_D_fight_facing_west()
    {
        // fight_west (s26, DOSBox-X boot seeded by the clock): save D's party and eight goblin guards in the Slums
        // corridor at 15,4 W (the party's team direction 3, the goblins' 1; fight_quick faces N). The s24 note "the
        // Client placed combatants differently" was the seed: placement draws nothing and gives these slots for every
        // seed tried, but the round start's 1d6 per creature picks who comes first and the view is centred on that
        // creature (Client seed 1: Darkstar first; this boot: Phineas). The Client started with --seed = pre_fight's
        // RandSeed (decimal) showed DOSBox-X's first screen.
        var game = PorData.Open();
        var (fight, cmap, _, _, creatures) = StartMatches(game, "fight_west", PorGeo.Load(game, 2, 20), (15, 4, Facing.West, 20));
        Assert.Equal(14, creatures.Count);
        Assert.Equal(15, cmap.SlotCount);
        Assert.Equal(6, fight.Record(fight.List[0])[CombatRecord.Facing]);
        Assert.Equal(2, fight.Record(fight.List[13])[CombatRecord.Facing]);
    }

    /// <summary>
    /// The arena from <paramref name="capture"/>'s pre_fight RandSeed (searched over the monster loader's draws), the
    /// placement of r0_start's list on it (slots, positions, sizes, facings, arena unchanged) and one 1d6 + one 1d100
    /// per creature to r0_start's RandSeed. Returns the generator at r0_start's seed.
    /// </summary>
    private static (Fight Fight, CombatMap Map, EclMemory Memory, TurboPascalRandom Rng, List<JsonElement> Creatures)
        StartMatches(IPorContent game, string capture, AreaMap map, (int X, int Y, int Facing, int Block) where)
    {
        var tables = PorCombatTables.Open(game)!;
        var pre = Load(capture, "pre_fight");
        var r0 = Load(capture, "r0_start");
        var v = r0.GetProperty("vars");
        int px = v.GetProperty("party_x").GetInt32(), py = v.GetProperty("party_y").GetInt32();
        int facing = v.GetProperty("party_facing").GetInt32();
        int block = v.GetProperty("current_ecl_block").GetInt32();
        Assert.Equal((where.X, where.Y, where.Facing, where.Block, 1), (px, py, facing, block, v.GetProperty("ds_49fa").GetInt32()));

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

        // Placement on that arena: the captured list (party of 6, then the monsters), distance 0.
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
        return (fight, cmap, memory, rng, creatures);
    }
}
