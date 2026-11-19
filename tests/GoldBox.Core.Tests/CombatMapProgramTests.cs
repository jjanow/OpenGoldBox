using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The combat map against the program itself: fixtures made by running the v1.3 routines under emulation
/// (<c>tools/scripts/por_combat_oracle.py</c>, private fixture <c>program-v1.3/combat_map.json</c>): the dungeon and
/// wilderness arenas with their dice, placement, target lists, distances, directions, the step probe and the cone test.
/// </summary>
public class CombatMapProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "combat_map.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (FixturePath is null)
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/combat_map.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);
    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);

    private static byte[] Hex(JsonElement e, string name) => Convert.FromHexString(e.GetProperty(name).GetString()!);

    private static Func<ushort, ushort> Rng(uint seed, out TurboPascalRandom r)
    {
        var rr = new TurboPascalRandom(seed);
        r = rr;
        return rr.Next;
    }

    [OracleFact]
    public void Dungeon_arenas_match_the_program()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("dungeon").EnumerateArray())
        {
            var map = new AreaMap(Hex(c, "planes"));
            var arena = new CombatArena();
            var random = Rng(c.GetProperty("seed").GetUInt32(), out var r);
            ArenaBuilder.BuildDungeon(arena, Tables.Value, map, c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32(),
                c.GetProperty("block").GetInt32(), random);
            Assert.Equal(Hex(c, "cells"), arena.Cells());
            Assert.Equal(c.GetProperty("seedAfter").GetUInt32(), r.Seed);
            n++;
        }
        Assert.True(n >= 10);
    }

    [OracleFact]
    public void Terrain_classes_match_the_program()
    {
        var t = Root.Value.GetProperty("terrainClass");
        for (int y = 0; y <= 1; y++)
        {
            var row = t[y].EnumerateArray().Select(e => e.GetInt32()).ToArray();
            for (int code = 0; code < 256; code++)
            {
                // 0xEE (0x6F after the swamp rule) is the stack the oracle filled: no arm names the code [?].
                if (row[code] == (y == 0 ? 0xEE : 0x6F))
                    Assert.Equal(0, ArenaBuilder.TerrainClass((byte)code, y == 1));
                else
                    Assert.Equal(row[code], ArenaBuilder.TerrainClass((byte)code, y == 1));
            }
        }
    }

    [OracleFact]
    public void Wilderness_arenas_match_the_program()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("wild").EnumerateArray())
        {
            var arena = new CombatArena();
            var random = Rng(c.GetProperty("seed").GetUInt32(), out var r);
            ArenaBuilder.BuildWilderness(arena, Tables.Value, c.GetProperty("wx").GetInt32(), c.GetProperty("wy").GetInt32(),
                c.GetProperty("mode").GetInt32(), c.GetProperty("yarash").GetBoolean(), c.GetProperty("block").GetInt32(), random);
            Assert.Equal(Hex(c, "cells"), arena.Cells());
            Assert.Equal(c.GetProperty("seedAfter").GetUInt32(), r.Seed);
            n++;
        }
        Assert.True(n >= 8);
    }

    [PorFact]
    public void Rare_features_28_and_29_match_dosbox_seeds()
    {
        // DOSBox-X s28: save A on script 26's overland at 12,13 N (DS:49FA = 3), vector 0 = CLRMON, LOADMON 1 ×2,
        // COMBAT; RandSeed DS:4428 before the step patched in the state. The offset of 5 draws (the monster loader's)
        // was found by rebuilding the boot's own arena (seed 0xED17FD58) byte for byte in a one-off probe (not pinned
        // here). This test pins what the two patched seeds were chosen for: the arena's one rare feature and its cell,
        // which the states at the first menu showed.
        foreach (var (seed, x, y, terrain) in new[] { (0x3A6D3172u, 31, 14, 28), (0x7A17D593u, 31, 13, 29) })
        {
            var arena = new CombatArena();
            var r = new TurboPascalRandom(seed);
            r.Skip(5);
            ArenaBuilder.BuildWilderness(arena, Tables.Value, 12, 13, 3, false, 26, r.Next);
            var rare = Enumerable.Range(0, CombatArena.Width * CombatArena.Height)
                .Where(i => arena.Cells()[i] is 28 or 29).Select(i => (i % CombatArena.Width, i / CombatArena.Width, arena.Cells()[i]));
            Assert.Equal([(x, y, (byte)terrain)], rare);
        }
    }

    private static readonly BaseItemTable NoItems =new(new byte[BaseItemTable.EntrySize * BaseItemTable.Count]);

    [OracleFact]
    public void Placement_matches_the_program()
    {
        foreach (var c in Root.Value.GetProperty("place").EnumerateArray())
        {
            bool outdoors = c.GetProperty("outdoors").GetBoolean();
            int facing = c.GetProperty("facing").GetInt32();
            var specs = c.GetProperty("creatures").EnumerateArray().ToList();
            var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
            var state = new EncounterState();
            int party = specs.Count(s => s.GetProperty("notparty").GetInt32() == 0);
            var all = new List<EclCharacter>();
            foreach (var s in specs)
            {
                var ch = new EclCharacter();
                ch[FightRules.Size] = (byte)s.GetProperty("size").GetInt32();
                ch[FightRules.InPlay] = (byte)s.GetProperty("inplay").GetInt32();
                ch[FightRules.Status] = (byte)(ch[FightRules.InPlay] != 0 ? 0 : 4);
                ch[FightRules.Hostile] = (byte)s.GetProperty("hostile").GetInt32();
                memory.Party.Members.Add(ch);
                all.Add(ch);
                if (s.GetProperty("notparty").GetInt32() == 1)
                {
                    state.NotParty.Add(ch);
                    state.Monsters.Add(ch);
                }
            }
            memory.Write(0x6E3E, (ushort)party);
            memory.Write(FightPlacement.DistanceCell, (ushort)c.GetProperty("distance").GetInt32());
            var random = Rng(c.GetProperty("seed").GetUInt32(), out _);
            var map = new CombatMap(Tables.Value);
            var fight = new Fight(memory, state, NoItems, random, new EndActionsController(), map);
            FightStartArea area;
            if (outdoors)
            {
                area = new FightStartArea
                {
                    ViewMode = c.GetProperty("mode").GetInt32(), PartyFacing = facing, EclBlock = 20,
                    WildX = c.GetProperty("wx").GetInt32(), WildY = c.GetProperty("wy").GetInt32(),
                    Duel = c.GetProperty("duel").GetBoolean(),
                };
                ArenaBuilder.BuildWilderness(map.Arena, map.Tables, area.WildX, area.WildY, area.ViewMode, false, 20, random);
            }
            else
            {
                area = new FightStartArea
                {
                    ViewMode = 1, Map = new AreaMap(Hex(c, "planes")), PartyX = c.GetProperty("x").GetInt32(),
                    PartyY = c.GetProperty("y").GetInt32(), PartyFacing = facing, EclBlock = 20,
                    Duel = c.GetProperty("duel").GetBoolean(),
                };
                ArenaBuilder.BuildDungeon(map.Arena, map.Tables, area.Map, area.PartyX, area.PartyY, 20, random);
            }
            FightPlacement.InitRecords(fight, map.Tables, facing);
            FightPlacement.PlaceAll(fight, map, area);

            Assert.Equal(c.GetProperty("slotCount").GetInt32(), map.SlotCount);
            var slots = c.GetProperty("slots").EnumerateArray().ToList();
            for (int k = 1; k < map.SlotCount; k++)
            {
                var s = slots[k - 1];
                int who = s.GetProperty("creature").GetInt32();
                Assert.Same(who < 0 ? null : all[who], map.SlotCreature(k));
                Assert.Equal(s.GetProperty("size").GetInt32(), map.SlotSize(k));
                if (who >= 0)
                    Assert.Equal(((sbyte)s.GetProperty("x").GetInt32(), (sbyte)s.GetProperty("y").GetInt32()), map.SlotPosition(k));
            }
            var after = c.GetProperty("listAfter").EnumerateArray().Select(e => all[e.GetInt32()]).ToList();
            Assert.Equal(after, fight.List);
            var bodies = c.GetProperty("bodies").EnumerateArray()
                .Select(b => (all[b.GetProperty("creature").GetInt32()], b.GetProperty("x").GetInt32(), b.GetProperty("y").GetInt32(),
                    b.GetProperty("terrain").GetInt32()))
                .ToList();
            Assert.Equal(bodies, map.Bodies.Select(b => (b!.Creature, b.X, b.Y, (int)b.Terrain)).ToList());
            Assert.Equal(Hex(c, "cells"), map.Arena.Cells());
            var teams = c.GetProperty("teamCounts").EnumerateArray().Select(e => e.GetByte()).ToArray();
            Assert.Equal(teams, fight.State.TeamCount);
        }
    }

    private static (CombatMap Map, List<EclCharacter> Creatures) LineCase(JsonElement c)
    {
        var map = new CombatMap(Tables.Value);
        Hex(c, "cells").CopyTo(map.Arena.Bytes, CombatArena.HeaderSize);
        var list = new List<EclCharacter>();
        int k = 0;
        foreach (var s in c.GetProperty("creatures").EnumerateArray())
        {
            var ch = new EclCharacter();
            ch[FightRules.Hostile] = (byte)s.GetProperty("hostile").GetInt32();
            list.Add(ch);
            k++;
            map.SetSlotCreature(k, ch);
            map.SetSlotPosition(k, s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32());
            map.SetSlotSize(k, s.GetProperty("size").GetInt32());
        }
        map.SlotCount = k + 1;
        map.Rebuild();
        return (map, list);
    }

    private static List<(int, int, int)> Entries(JsonElement list) =>
        list.EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())).ToList();

    private static List<(int, int, int)> Entries(CombatMap map) =>
        map.Targets.Select(t => (t.Slot, t.Dist, t.Dir)).ToList();

    [OracleFact]
    public void Target_lists_distances_directions_and_probes_match_the_program()
    {
        foreach (var c in Root.Value.GetProperty("lines").EnumerateArray())
        {
            var (map, list) = LineCase(c);
            var places = c.GetProperty("creatures").EnumerateArray()
                .Select(s => (X: s.GetProperty("x").GetInt32(), Y: s.GetProperty("y").GetInt32(), Size: s.GetProperty("size").GetInt32()))
                .ToList();
            foreach (var t in c.GetProperty("targetLists").EnumerateArray())
            {
                var p = places[t.GetProperty("from").GetInt32()];
                map.BuildTargetList(p.X, p.Y, t.GetProperty("range").GetInt32(), t.GetProperty("filter").GetInt32(), p.Size);
                Assert.Equal(Entries(t.GetProperty("list")), Entries(map));
            }
            foreach (var t in c.GetProperty("countTargets").EnumerateArray())
            {
                int n = map.CountTargets(list[t.GetProperty("from").GetInt32()], t.GetProperty("range").GetInt32());
                Assert.Equal(t.GetProperty("count").GetInt32(), n);
                Assert.Equal(Entries(t.GetProperty("list")), Entries(map));
                Assert.Equal(t.GetProperty("potential").EnumerateArray().Select(e => e.GetInt32()), map.PotentialTargets);
            }
            map.BuildTargetList(places[0].X, places[0].Y, 1, 255, places[0].Size);
            foreach (var d in c.GetProperty("distance").EnumerateArray())
            {
                var before = Entries(map);
                int dist = map.Distance(list[d.GetProperty("a").GetInt32()], list[d.GetProperty("b").GetInt32()]);
                Assert.Equal(d.GetProperty("d").GetInt32(), dist);
                Assert.Equal(d.GetProperty("countAfter").GetInt32(), map.TargetCount);
                if (map.TargetCount <= before.Count)
                    Assert.Equal(before.Take(map.TargetCount), Entries(map));
            }
            foreach (var d in c.GetProperty("direction").EnumerateArray())
                Assert.Equal(d.GetProperty("dir").GetInt32(),
                    map.DirectionToward(list[d.GetProperty("a").GetInt32()], list[d.GetProperty("b").GetInt32()]));
            foreach (var pr in c.GetProperty("probes").EnumerateArray())
            {
                var (slot, terr) = map.Probe(list[pr.GetProperty("a").GetInt32()], pr.GetProperty("dir").GetInt32());
                Assert.Equal((pr.GetProperty("slot").GetInt32(), pr.GetProperty("terrain").GetInt32()), (slot, terr));
            }
        }
    }

    [OracleFact]
    public void Cone_test_matches_the_program()
    {
        foreach (var e in Root.Value.GetProperty("cone").EnumerateArray())
        {
            var v = e.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Assert.Equal(v[5] != 0, CombatMap.IsDirToward(v[0], v[1], v[2], v[3], v[4]));
        }
    }
}
