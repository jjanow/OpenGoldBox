using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;

namespace GoldBox.Core.Game;

/// <summary>Where a fight starts: the inputs of the fight start (0xE402) that are not in the creature records.</summary>
public sealed class FightStartArea
{
    /// <summary>DS:49FA: 1 = a 3D area (the dungeon arena), else the wilderness (3 central map, 4 east map, other
    /// values the west map).</summary>
    public int ViewMode { get; init; } = 1;
    /// <summary>DS:6AAD / 6AAE / 6AAF: the party's square and facing (0, 2, 4, 6) on the 3D map.</summary>
    public int PartyX { get; init; }
    public int PartyY { get; init; }
    public int PartyFacing { get; init; }
    /// <summary>The 3D map (walls and barriers) for the dungeon arena; null in the wilderness.</summary>
    public AreaMap? Map { get; init; }
    /// <summary>DS:84DC, the current ECL block (block 10 has no rare features of the 1-in-100 kind).</summary>
    public int EclBlock { get; init; } = -1;
    /// <summary>DS:84D4: a duel (no bodies at placement).</summary>
    public bool Duel { get; init; }
    /// <summary>Wilderness only: the party's square on the wilderness map (low bytes of VM $49C3 / $49C4).</summary>
    public int WildX { get; init; }
    public int WildY { get; init; }
    /// <summary>Wilderness only: VM $4AB3 = 255 (the class of swamp terrain changes).</summary>
    public bool YarashDone { get; init; }
}

/// <summary>
/// The fight start's arena and placement (GAME.OVR 0xE402 with 0xD815, 0xD8B2 and 0xDFC6; docs/re/combat-map.md
/// "Fight start", "Placement"). The VM-cell bookkeeping of 0xE402 is done by <see cref="EncounterHost"/>; this does the
/// rest, and draws only from <see cref="Fight.Random"/> before the first round (the arena's dice; placement draws
/// none).
/// </summary>
public static class FightPlacement
{
    /// <summary>VM cell of the monsters' distance in 3D squares ($6DC1, word at zone 1 + 0x582).</summary>
    public const ushort DistanceCell = 0x6DC1;
    /// <summary>VM cells the wilderness arena reads (zone 0 + 0x186 / + 0x188 / + 0x366).</summary>
    public const ushort WildXCell = 0x49C3, WildYCell = 0x49C4, YarashCell = 0x4AB3;
    /// <summary>Effect trigger the fight start runs for every creature (0x2B1E4 with 8).</summary>
    public const int TriggerFightStart = 8;

    /// <summary>
    /// 0xE402 from the arena on: the arena (0xD815: header cursor 0 / size 1 / line mode 0, then the dungeon or the
    /// wilderness builder), the combat records (the map part of 0xD8B2: +0x13 for list positions past $6E3E, facing =
    /// DS:02F2[party facing ÷ 2], + 4 mod 8 for hostile creatures), placement (0xDFC6), the view's upper-left cell =
    /// the first creature's cell − 3, then for every creature in list order the trigger-8 effects, and the enemy HP
    /// percentage (0x15074). Also DS:6E30 (quick magic) = 0, DS:6DE3 (round counter) = 0 and DS:6822 (attack roll) = 0 (0xE457–0xE461),
    /// the bodies (DS:6715 = 0) and the stinking-cloud list DS:6DBB = nil (<see cref="EffectHandlers.Clouds"/> emptied).
    /// Sets <see cref="Fight.Geometry"/>'s map: pass the same <paramref name="map"/> as the fight's geometry.
    /// </summary>
    public static void Start(Fight fight, CombatMap map, FightStartArea area)
    {
        fight.State.RoundCounter = 0;
        fight.State.AttackRoll = 0;
        fight.State.QuickMagic = false;
        map.State = fight.State;
        map.Bodies.Clear();
        if (fight.Effects is EffectHandlers eh)
            eh.Clouds.Clear();
        map.Arena.ResetHeader();
        if (area.ViewMode == 1)
            ArenaBuilder.BuildDungeon(map.Arena, map.Tables, area.Map ?? throw new ArgumentException("a 3D fight needs the map", nameof(area)),
                area.PartyX, area.PartyY, area.EclBlock, fight.Random);
        else
            ArenaBuilder.BuildWilderness(map.Arena, map.Tables, area.WildX, area.WildY, area.ViewMode, area.YarashDone,
                area.EclBlock, fight.Random);
        InitRecords(fight, map.Tables, area.PartyFacing);
        PlaceAll(fight, map, area);
        if (fight.List.Count > 0)
        {
            var (x, y) = map.Position(fight.List[0]);
            map.Arena.ViewX = (sbyte)(x - 3);
            map.Arena.ViewY = (sbyte)(y - 3);
        }
        foreach (var c in fight.List.ToList())
            fight.Effects.Trigger(TriggerFightStart, c, fight.State);
        FightRules.UpdateHostileHpPercent(fight.List, fight.State);
    }

    /// <summary>The map part of 0xD8B2 (records are zeroed on creation): +0x13 = 1 for 1-based list positions past
    /// $6E3E; facing from the party's.</summary>
    public static void InitRecords(Fight fight, CombatTables t, int partyFacing)
    {
        int partySize = fight.Memory.Read(0x6E3E);
        int n = 0;
        foreach (var c in fight.List)
        {
            fight.RecalculateStats(c); // 0xD8DA
            var rec = fight.Record(c);
            n++;
            if (n > partySize)
                rec[CombatRecord.NotParty] = 1;
            rec[CombatRecord.Facing] = t.QuadDir[(partyFacing & 0xFF) >> 1 & 3];
            if (c[FightRules.Hostile] == 1)
                rec[CombatRecord.Facing] = (byte)((rec[CombatRecord.Facing] + 4) % 8);
        }
    }

    // ---- Placement (0xDFC6, 0xDB48, 0xDA05) ------------------------------------------------------------------------

    /// <summary>The placement state of one team (DS:4650.. and the free-square grids DS:4440).</summary>
    private sealed class TeamGrid
    {
        public int PlaceX, PlaceY, Dir, Rows;
        /// <summary>[ring 0–3][row 0–5][column 0–10]: 1 while the square is free.</summary>
        public readonly byte[,,] Free = new byte[4, 6, 11];
    }

    /// <summary>
    /// 0xDFC6: places the list. Team counts (START 0x2F7B); party team 0 at (0, 0) with direction facing ÷ 2; the other
    /// team at the facing's step × $6DC1 (low byte of the product) with direction ((facing + 4) mod 8) ÷ 2; rows per team
    /// = (team count + 1) ÷ 2. Each team has four 11 × 6 grids (one per ring) whose free squares are the row bands of
    /// its direction (ring 1: band 4). Then each creature in list order gets the next slot (team = its hostile byte,
    /// size = 0x6C AND 7) and <see cref="FindPlacement"/>:
    /// <list type="bullet">
    /// <item>placed: out of play → size 0, and outside a duel a party member (+0x13 = 0) leaves a body (its cell's
    /// terrain saved, the cell 0x1F); the grid is rebuilt;</item>
    /// <item>not placed: size 0; a creature with +0x13 = 1 is taken out of the combat list (0x1CC10 with "keep $6E3E")
    /// and its slot is reused; a party member keeps its slot, off the map, at the last square tried.</item>
    /// </list>
    /// No random draws.
    /// </summary>
    public static void PlaceAll(Fight fight, CombatMap map, FightStartArea area)
    {
        var t = map.Tables;
        FightRules.CountTeams(fight.List, fight.State);
        for (int k = 1; k <= CombatMap.MaxSlots; k++)
            map.SetSlotSize(k, 0);
        map.Rebuild();
        int facing = area.PartyFacing & 0xFF;
        int dist = fight.Memory.Read(DistanceCell);
        var teams = new TeamGrid[2] { new(), new() };
        teams[0].PlaceX = 0;
        teams[0].PlaceY = 0;
        teams[0].Dir = facing >> 1;
        teams[1].PlaceX = (sbyte)(byte)(CombatTables.DirDx[facing & 7] * dist + teams[0].PlaceX);
        teams[1].PlaceY = (sbyte)(byte)(CombatTables.DirDy[facing & 7] * dist + teams[0].PlaceY);
        teams[1].Dir = (facing + 4) % 8 >> 1;
        for (int team = 0; team <= 1; team++)
            teams[team].Rows = (fight.State.TeamCount[team] + 1) >> 1;
        for (int team = 0; team <= 1; team++)
            for (int g = 0; g <= 3; g++)
            {
                int d = g == 1 ? 4 : teams[team].Dir;
                for (int y = 0; y <= 5; y++)
                    for (int x = 0; x <= 10; x++)
                        teams[team].Free[g, y, x] = (byte)(t.RowBandLo(d, y) > x || t.RowBandHi(d, y) < x ? 0 : 1);
            }
        int slot = 1;
        map.SlotCount = 1;
        var list = fight.List;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            map.SetSlotCreature(slot, c);
            int team = c[FightRules.Hostile];
            map.SetSlotSize(slot, c[FightRules.Size] & 7);
            var rec = fight.Record(c);
            if (FindPlacement(map, slot, team, teams, area))
            {
                if (c[FightRules.InPlay] == 0)
                {
                    map.SetSlotSize(slot, 0);
                    if (!area.Duel && rec[CombatRecord.NotParty] == 0)
                    {
                        var (x, y) = map.SlotPosition(slot);
                        map.Bodies.Add(new CombatMap.Body(c, x, y, map.Arena[x, y]));
                        map.Arena[x, y] = ArenaBuilder.Body;
                    }
                }
                map.Rebuild();
                slot++;
                map.SlotCount++;
            }
            else
            {
                map.SetSlotSize(slot, 0);
                if (rec[CombatRecord.NotParty] == 1)
                {
                    map.SetSlotCreature(slot, null);
                    list.RemoveAt(i);
                    fight.Encounter.Monsters.Remove(c);
                    fight.Encounter.NotParty.Remove(c);
                    i--;
                }
                else
                {
                    slot++;
                    map.SlotCount++;
                }
            }
        }
    }

    /// <summary>
    /// 0xDB48: the search for a square for slot <paramref name="slot"/> in its team's grid. From the ring's start
    /// corner (DS:02E2 ÷ 2) the search runs in legs: state 1 sets the leg's start (corner start square of
    /// DS:02F6/02FE, moved back legShift steps along corner + 2), states 2 and 3 alternate legLen steps either side
    /// (corner + 1, corner + 3), legLen growing after state 3. A leg ends at a square partly off the grid, or after
    /// the team's rows of tries on the first leg, or more than 11 tries later; the next leg starts one step further
    /// back (two for the party facing an odd direction... see docs/re/combat-map.md). A square fully off the grid
    /// moves to the next ring whose 3D direction (DS:02D2) is not walled off (any ring outdoors), whose grid origin
    /// is one 3D square that way; none left → not placed.
    /// </summary>
    private static bool FindPlacement(CombatMap map, int slot, int team, TeamGrid[] teams, FightStartArea area)
    {
        var t = map.Tables;
        var tg = teams[team & 1];
        bool firstPass = true, giveUp = false, placed = false;
        int state = 1, legShift = 0, ring = 0;
        int baseX = tg.PlaceX, baseY = tg.PlaceY;
        int legX = 0, legY = 0, legLen = 0, tries = 0, qx = 0, qy = 0;
        bool Walled(int dir)
        {
            int wx = (sbyte)(byte)(tg.PlaceX + area.PartyX), wy = (sbyte)(byte)(tg.PlaceY + area.PartyY);
            return area.ViewMode <= 1 && ArenaBuilder.WallBetween(area.Map!, area.PartyY, wx, wy, dir) == 1;
        }
        int guard = 0;
        do
        {
            if (++guard > 100_000)
                throw new InvalidOperationException("placement search did not end");
            int quad = t.RingStartCorner(tg.Dir, ring) >> 1;
            switch (state)
            {
                case 1:
                {
                    int d = t.QuadDir[(quad + 2) % 4];
                    legX = (sbyte)(byte)(t.StartQuadXAt(ring > 0, quad) + CombatTables.DirDx[d] * legShift);
                    legY = (sbyte)(byte)(t.StartQuadYAt(ring > 0, quad) + CombatTables.DirDy[d] * legShift);
                    qx = legX;
                    qy = legY;
                    legLen = 1;
                    state = 2;
                    tries = 1;
                    break;
                }
                case 2:
                {
                    int d = t.QuadDir[(quad + 1) % 4];
                    qx = (sbyte)(byte)(legX + CombatTables.DirDx[d] * legLen);
                    qy = (sbyte)(byte)(legY + CombatTables.DirDy[d] * legLen);
                    state = 3;
                    tries++;
                    break;
                }
                case 3:
                {
                    int d = t.QuadDir[(quad + 3) % 4];
                    qx = (sbyte)(byte)(legX + CombatTables.DirDx[d] * legLen);
                    qy = (sbyte)(byte)(legY + CombatTables.DirDy[d] * legLen);
                    state = 2;
                    legLen++;
                    tries++;
                    break;
                }
            }
            bool offGrid = qx < 0 || qy < 0 || qx > 10 || qy > 5;
            if (state > 1)
            {
                if ((offGrid && !FullyOff(qx, qy)) || (firstPass && tries >= tg.Rows) || (!firstPass && (byte)tries > 11))
                {
                    legShift++;
                    if (team == 0 && (teams[0].Dir & 1) != 0 && ring == 0 && legShift == 1)
                    {
                        bool open = false;
                        for (int k = 1; k <= 3; k++)
                            if (!Walled(t.RingDirection(tg.Dir, k)))
                                open = true;
                        if (open)
                            legShift++;
                    }
                    state = 1;
                    firstPass = false;
                }
            }
            if (offGrid && FullyOff(qx, qy))
            {
                placed = false;
                state = 0;
                while (ring < 3 && state != 1)
                {
                    ring++;
                    int dir = t.RingDirection(tg.Dir, ring);
                    if (!Walled(dir))
                    {
                        baseX = (sbyte)(byte)(tg.PlaceX + CombatTables.DirDx[dir]);
                        baseY = (sbyte)(byte)(tg.PlaceY + CombatTables.DirDy[dir]);
                        legShift = 0;
                        state = 1;
                    }
                }
                if (state != 1)
                    giveUp = true;
            }
            if (!offGrid)
                placed = TryPlaceAt(map, slot, qx, qy, baseX, baseY, ring, tg);
        } while (!placed && !giveUp);
        return !giveUp;
    }

    /// <summary>0xD9D4: true only when the square is off the grid on both axes.</summary>
    private static bool FullyOff(int qx, int qy) => !(qx is >= 0 and < 11 || qy is >= 0 and < 6);

    /// <summary>
    /// 0xDA05: square (qx, qy) of the grid based at 3D offset (bqx, bqy): free in this ring's grid → the slot's cell
    /// becomes (qx + 6·bqx + 5·bqy + 22, qy + 5·bqy + 10) (stored before the test, so a failed try leaves it there);
    /// placed when no other creature is on the footprint, the terrain is not 0 and its cost is below 255
    /// (<see cref="CombatMap.Probe"/> with the centre). A placed square is no longer free.
    /// </summary>
    private static bool TryPlaceAt(CombatMap map, int slot, int qx, int qy, int bqx, int bqy, int ring, TeamGrid tg)
    {
        if (qx < 0 || qy < 0 || qx > 10 || qy > 5 || tg.Free[ring, qy, qx] == 0)
            return false;
        map.SetSlotPosition(slot, (sbyte)(byte)(qx + bqx * 6 + bqy * 5 + 22), (sbyte)(byte)(qy + bqy * 5 + 10));
        var (cr, terr) = map.Probe(map.SlotCreature(slot)!, CombatTables.Center);
        if (cr == 0 && terr > 0 && map.Tables.Cost(terr) < 255)
        {
            tg.Free[ring, qy, qx] = 0;
            return true;
        }
        return false;
    }
}
