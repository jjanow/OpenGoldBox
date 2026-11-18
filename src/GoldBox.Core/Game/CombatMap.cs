using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The combat map (docs/re/combat-map.md): the arena (DS:6716), the creatures registered on it and the program's map
/// and line routines (unit 0x3E0 at GAME.OVR 0x3073A, the line unit 0x3DB at 0x2FB52, START helpers). A creature's
/// place is not in its record: it has a slot k (1-based, the order the fight start registered the list in;
/// DS:65B9 + 4k holds its pointer) and slot k's entry at DS:5F27 + 4k holds x, y, k and the icon size (record 0x6C
/// AND 7; 0 = not on the map). DS:5F2A (entry 0's size byte) is the slot count, one more than the creatures
/// registered. The cell → slot grid DS:60DB (25 × 50) is rebuilt from the entries by 0x30ADA.
/// </summary>
public sealed class CombatMap : IFightMap, IFightAuraRange, IFightTerrain, IFightTargetList
{
    public const int MaxSlots = 255;

    public CombatMap(CombatTables tables, CombatArena? arena = null)
    {
        Tables = tables;
        Arena = arena ?? new CombatArena();
    }

    public CombatTables Tables { get; }
    public CombatArena Arena { get; }

    // ---- Slots (DS:65B9, DS:5F27, DS:5F2A) ------------------------------------------------------------------------

    private readonly EclCharacter?[] _creature = new EclCharacter?[MaxSlots + 1];
    private readonly byte[] _x = new byte[MaxSlots + 1], _y = new byte[MaxSlots + 1], _size = new byte[MaxSlots + 1];
    private readonly byte[] _cells = new byte[CombatArena.Width * CombatArena.Height];

    /// <summary>DS:5F2A: the slot count (the fight start leaves it at the number of creatures registered + 1).</summary>
    public int SlotCount { get; set; }

    /// <summary>The creature in slot <paramref name="k"/> (DS:65B9 + 4k), or null.</summary>
    public EclCharacter? SlotCreature(int k) => _creature[k];
    public void SetSlotCreature(int k, EclCharacter? c) => _creature[k] = c;
    public (int X, int Y) SlotPosition(int k) => ((sbyte)_x[k], (sbyte)_y[k]);
    public void SetSlotPosition(int k, int x, int y) { _x[k] = (byte)x; _y[k] = (byte)y; }
    /// <summary>Icon size of slot k (0 = not on the map).</summary>
    public int SlotSize(int k) => _size[k];
    public void SetSlotSize(int k, int size) => _size[k] = (byte)size;

    /// <summary>
    /// 0x31399: the slot of a creature, 1..<see cref="SlotCount"/>, or 0. [?] With a slot count of 0 the program's loop
    /// runs past 255 and wraps.
    /// </summary>
    public int Slot(EclCharacter c)
    {
        for (int i = 1; i <= Math.Max(SlotCount, 1) && i <= MaxSlots; i++)
            if (ReferenceEquals(_creature[i], c))
                return i;
        return 0;
    }

    /// <summary>0x31321 / 0x31349: the creature's arena cell (slot 0's entry when it has no slot).</summary>
    public (int X, int Y) Position(EclCharacter c) => SlotPosition(Slot(c));
    /// <summary>0x31371: its icon size (0 once off the map).</summary>
    public int IconSize(EclCharacter c) => _size[Slot(c)];
    public bool OnMap(EclCharacter c) => Slot(c) is > 0 and var k && _size[k] > 0;

    /// <summary>
    /// 0x30ADA: the cell grid cleared, then for slots 1..count with a size, every footprint cell gets the slot (no bounds
    /// check in the program: a cell past the right edge lands on the next row; cells outside the grid are dropped here).
    /// The view coordinates it also computes (DS:604A / DS:6092) are not kept.
    /// </summary>
    public void Rebuild()
    {
        Array.Clear(_cells);
        for (int i = 1; i <= SlotCount && i <= MaxSlots; i++)
        {
            if (_size[i] == 0)
                continue;
            for (int j = 0; j <= 3; j++)
                if (Tables.IconCell(_size[i], j, out int dx, out int dy))
                {
                    int idx = ((sbyte)_y[i] + dy) * CombatArena.Width + (sbyte)_x[i] + dx;
                    if (idx >= 0 && idx < _cells.Length)
                        _cells[idx] = (byte)i;
                }
        }
    }

    /// <summary>The slot standing on a cell (0 = none), from the grid.</summary>
    public int SlotAt(int x, int y) => CombatArena.InArena(x, y) ? _cells[y * CombatArena.Width + x] : 0;

    public EclCharacter? At(int x, int y) => SlotAt(x, y) is > 0 and var k ? _creature[k] : null;

    /// <summary>0x30BF8: slot and terrain of a cell; both 0 outside the arena.</summary>
    public (int Slot, int Terrain) Contents(int x, int y)
    {
        x = (sbyte)(byte)x;
        y = (sbyte)(byte)y;
        return CombatArena.InArena(x, y) ? (_cells[y * CombatArena.Width + x], Arena[x, y]) : (0, 0);
    }

    /// <summary>
    /// 0x313F3, what lies one step in direction <paramref name="dir"/> (8 = the creature's own cells) for each
    /// footprint cell: the slot of another creature there (the last one found), and one terrain for the step,
    /// starting from 23: any cell off the arena gives 0 (and stays 0); else any cloud (0x1E) gives 0x1E; else the
    /// terrain whose move cost is the highest so far (ties: the later cell; costs start at 1, so with every cost 0 the
    /// result stays 23).
    /// </summary>
    public (int Slot, int Terrain) Probe(EclCharacter c, int dir)
    {
        int crIdx = 0, terr = 23, best = 1;
        int slot = Slot(c);
        int x = (sbyte)_x[slot], y = (sbyte)_y[slot];
        for (int i = 0; i <= 3; i++)
        {
            if (!Tables.IconCell(_size[slot], i, out int dx, out int dy))
                continue;
            var (s, t) = Contents(x + dx + CombatTables.DirDx[dir], y + dy + CombatTables.DirDy[dir]);
            if (s == slot)
                s = 0;
            if (s > 0)
                crIdx = s;
            if (t == 0 || terr == 0)
                terr = 0;
            else if (t == ArenaBuilder.Cloud || terr == ArenaBuilder.Cloud)
                terr = ArenaBuilder.Cloud;
            else if (Tables.Cost(t) >= best)
            {
                best = Tables.Cost(t);
                terr = t;
            }
        }
        return (crIdx, terr);
    }

    // ---- Bodies (DS:66D6, count DS:6715) --------------------------------------------------------------------------

    /// <summary>A party member's body on the map: the creature, its cell and the terrain the body covers.</summary>
    public sealed record Body(EclCharacter Creature, int X, int Y, byte Terrain);

    /// <summary>
    /// DS:66DD + 7k (k = 1..DS:6715): 7-byte entries, 8 cleared at the fight start. [?] A ninth body would write over
    /// DS:6715 and the arena pointer; here the list just grows. Entries emptied by a move back (0x317E1) stay as null.
    /// </summary>
    public List<Body?> Bodies { get; } = [];

    /// <summary>
    /// 0x3154B in a fight, the parts that change the map (the rest is drawing and sound): a party member (combat record
    /// +0x13 = 0) leaves a body: its cell's terrain is saved and becomes 0x1F unless it is a cloud (0x1E); then its icon
    /// leaves the map (size 0) and the grid is rebuilt; combat record initiative, movement, spell and guard = 0. [?]
    /// The routine first tests body entry DS:70C7 (a drawing loop index) instead of each entry, and leaves when that one
    /// holds this creature; not modelled.
    /// </summary>
    public void Downed(EclCharacter c, FightState fight)
    {
        // Sound (docs/re/sound.md §2.4): resident bracket 0x31554 … 0x317D6 with the body cue at 0x31744 for every
        // creature; the program's drawing and message pause inside the bracket (its length) are the front end's.
        fight.Sound.BeginResident();
        fight.Sound.Cue(Sound.SoundCue.Body);
        fight.Sound.EndResident();
        int slot = Slot(c);
        var rec = fight.Record(c);
        if (rec[CombatRecord.NotParty] == 0)
        {
            var (x, y) = SlotPosition(slot);
            byte under = CombatArena.InArena(x, y) ? Arena[x, y] : (byte)0;
            Bodies.Add(new Body(c, x, y, under));
            if (CombatArena.InArena(x, y) && under != ArenaBuilder.Cloud)
                Arena[x, y] = ArenaBuilder.Body;
        }
        _size[slot] = 0;
        Rebuild();
        rec.InitiativeValue = 0;
        rec[CombatRecord.Movement] = 0;
        rec[CombatRecord.SpellCast] = 0;
        rec[CombatRecord.Guarding] = 0;
    }

    /// <summary>
    /// 0x317E1 (in a fight): puts the creature at (x, y) with icon size record 0x6C AND 0x7F and checks the cells
    /// (<see cref="Probe"/> with the centre): another creature, terrain 0 or cost 255 → size 0 and false. Else true,
    /// and when <paramref name="moving"/> and the creature is a party member: its own body entries are emptied (the
    /// last one's terrain, unless it was 0x1F, is the terrain to put back) and the cell gets that terrain back unless
    /// another body lies there; the grid is rebuilt. [?] The terrain put back defaults to the probe's terrain.
    /// </summary>
    public bool TryPlace(EclCharacter c, int x, int y, bool moving, FightState fight)
    {
        int slot = Slot(c);
        _size[slot] = (byte)(c[FightRules.Size] & 0x7F);
        _x[slot] = (byte)x;
        _y[slot] = (byte)y;
        var (cr, t) = Probe(c, CombatTables.Center);
        if (cr != 0 || t == 0 || Tables.Cost(t) == 255)
        {
            _size[slot] = 0;
            return false;
        }
        if (moving && fight.Record(c)[CombatRecord.NotParty] == 0)
        {
            for (int i = 0; i < Bodies.Count; i++)
                if (Bodies[i] is { } b && ReferenceEquals(b.Creature, c))
                {
                    if (b.Terrain != ArenaBuilder.Body)
                        t = b.Terrain;
                    Bodies[i] = null;
                }
            bool found = Bodies.Any(b => b is not null && b.X == (byte)x && b.Y == (byte)y);
            if (!found && CombatArena.InArena(x, y))
                Arena[x, y] = (byte)t;
        }
        Rebuild();
        return true;
    }

    /// <summary>The map part of 0x2BDF0 (a creature put out of play): its icon leaves the map and the grid is rebuilt.</summary>
    public void TakeOff(EclCharacter c)
    {
        _size[Slot(c)] = 0;
        Rebuild();
    }

    // ---- Lines, directions, distances (unit 0x3DB) -------------------------------------------------------------------

    /// <summary>
    /// 0x300CD: is (tx, ty) in the 90° cone of direction <paramref name="dir"/> seen from (x, y)? False when either cell
    /// is outside the arena. 255 counts as 8 (the centre: always true). True when the cells are equal or (tx, ty) is
    /// the next cell in that direction. Else, with (nx, ny) that next cell, the cone of each direction is two half-planes
    /// (docs/re/combat-map.md). [?] Directions 9–254 match no arm (stack residue in the program; false here).
    /// </summary>
    public static bool IsDirToward(int x, int y, int tx, int ty, int dir)
    {
        x = (sbyte)(byte)x; y = (sbyte)(byte)y; tx = (sbyte)(byte)tx; ty = (sbyte)(byte)ty;
        if (!CombatArena.InArena(x, y) || !CombatArena.InArena(tx, ty))
            return false;
        dir &= 0xFF;
        if (dir == 255)
            dir = 8;
        if (dir > 8)
            return false;
        int nx = (sbyte)(byte)(x + CombatTables.DirDx[dir]), ny = (sbyte)(byte)(y + CombatTables.DirDy[dir]);
        if ((x == tx && y == ty) || (nx == tx && ny == ty))
            return true;
        return dir switch
        {
            0 => (tx >= nx && ty <= nx - tx + ny) || (tx <= nx && ty <= tx - nx + ny),
            1 => (tx >= nx && ty <= nx - tx + ny) || (tx >= nx + ny - ty && ty <= ny),
            2 => (tx >= nx + ny - ty && ty <= ny) || (tx >= nx + ty - ny && ty >= ny),
            3 => (tx >= nx + ty - ny && ty >= ny) || (tx >= nx && ty >= tx - nx + ny),
            4 => (tx >= nx && ty >= tx - nx + ny) || (tx <= nx && ty >= nx - tx + ny),
            5 => (tx <= nx && ty >= nx - tx + ny) || (tx <= nx + ny - ty && ty >= ny),
            6 => (tx <= nx + ny - ty && ty >= ny) || (tx <= nx + ty - ny && ty <= ny),
            7 => (tx <= nx + ty - ny && ty <= ny) || (tx <= nx && ty <= tx - nx + ny),
            _ => true,
        };
    }

    /// <summary>The line state of 0x2FCF2 / 0x2FE1E (the request record, field offsets in the comments); also the
    /// bolt's line (Lightning Bolt 0x29973).</summary>
    public struct LineWalk
    {
        public int Sx, Sy, Ex, Ey, Err, Adx, Ady, IncD, IncS, StepX, StepY, Cx, Cy; // +0 … +0x16 (words; steps bytes)
        public byte Dist; // +0x18, a byte: 2 per straight step, 3 per diagonal

        /// <summary>0x2FCF2.</summary>
        public static LineWalk Start(int sx, int sy, int ex, int ey)
        {
            var l = new LineWalk { Sx = sx, Sy = sy, Ex = ex, Ey = ey, Dist = 0 };
            l.Adx = Math.Abs(ex - sx);
            l.Ady = Math.Abs(ey - sy);
            l.StepX = Math.Sign(ex - sx);
            l.StepY = Math.Sign(ey - sy);
            l.Cx = sx;
            l.Cy = sy;
            if (l.Adx > l.Ady)
            {
                l.Err = l.Ady * 2 - l.Adx;
                l.IncD = (l.Ady - l.Adx) * 2;
                l.IncS = l.Ady * 2;
            }
            else
            {
                l.Err = l.Adx * 2 - l.Ady;
                l.IncD = (l.Adx - l.Ady) * 2;
                l.IncS = l.Adx * 2;
            }
            return l;
        }

        /// <summary>0x2FE1E: one step along the major axis; false at the end.</summary>
        public bool Next()
        {
            if (Adx > Ady)
            {
                if (Cx == Ex)
                    return false;
                if (Err >= 0)
                {
                    Cy = (short)(Cy + StepY);
                    Err = (short)(Err + IncD);
                    Dist += 3;
                }
                else
                {
                    Err = (short)(Err + IncS);
                    Dist += 2;
                }
                Cx = (short)(Cx + StepX);
                return true;
            }
            if (Cy == Ey)
                return false;
            if (Err >= 0)
            {
                Cx = (short)(Cx + StepX);
                Err = (short)(Err + IncD);
                Dist += 3;
            }
            else
            {
                Err = (short)(Err + IncS);
                Dist += 2;
            }
            Cy = (short)(Cy + StepY);
            return true;
        }
    }

    /// <summary>
    /// 0x2FF74: walks the line from (x, y) to (tx, ty). The viewer's sight height is that of the terrain at (x, y). On
    /// each cell from the start to the end: when the line mode is 0 and the cell's blocking height is above the sight
    /// height, or the walked distance (half squares: 2 straight, 3 diagonal, a byte) exceeds 2 × <paramref name="range"/>
    /// + 1, the walk stops there: (tx, ty) and the distance become that cell's and the result is false. Else true with
    /// the full distance. [?] Cells off the arena would read the heap past the block; the callers stay inside.
    /// </summary>
    public bool WalkLine(int x, int y, ref int tx, ref int ty, ref int dist)
    {
        int maxDist = (ushort)(dist * 2 + 1);
        var walk = LineWalk.Start((sbyte)(byte)x, (sbyte)(byte)y, (sbyte)(byte)tx, (sbyte)(byte)ty);
        int sight = Tables.SightHeight(CellOrZero(x, y));
        while (true)
        {
            if ((Arena.LineMode == 0 && Tables.Blocking(CellOrZero(walk.Cx, walk.Cy)) > sight) || walk.Dist > maxDist)
            {
                tx = walk.Cx;
                ty = walk.Cy;
                dist = walk.Dist;
                return false;
            }
            if (!walk.Next())
                break;
        }
        dist = walk.Dist;
        return true;
    }

    private byte CellOrZero(int x, int y) => CombatArena.InArena(x, y) ? Arena[x, y] : (byte)0;

    /// <summary>One entry of the target list DS:671B (3 bytes): the slot, the walked distance (half squares), a direction.</summary>
    public readonly record struct Target(int Slot, int Dist, int Dir);

    /// <summary>Entries of the target list, DS:6718 + 3i for i = 1..255 (index 0 unused).</summary>
    private readonly Target[] _targets = new Target[MaxSlots + 1];

    /// <summary>DS:671A: the number of valid entries in the target list.</summary>
    public int TargetCount { get; set; }

    /// <summary>Target list entry <paramref name="i"/> (1-based; entries past the count keep older values).</summary>
    public Target TargetAt(int i) => _targets[i];

    /// <summary>The entries 1..<see cref="TargetCount"/> of the target list.</summary>
    public IReadOnlyList<Target> Targets => _targets.AsSpan(1, TargetCount).ToArray();

    /// <summary>
    /// 0x3045B: every slot with a size, seen from a creature of icon size <paramref name="size"/> at (x, y): over each
    /// pair (their cell j outer, our cell k inner) in the cone <paramref name="filter"/> (<see cref="IsDirToward"/>),
    /// a line (<see cref="WalkLine"/>) within <paramref name="range"/>; a slot with any clear line is listed with its
    /// shortest distance (the first pair reaching it) and a direction: <paramref name="filter"/> when below 8, else
    /// the first direction 0, 1, … whose cone holds that pair. The list includes the viewer's own slot (distance 0).
    /// Then sorted (0x2FB80).
    /// </summary>
    public void BuildTargetList(int x, int y, int range, int filter, int size)
    {
        var my = Cells(x, y, size);
        TargetCount = 0;
        for (int i = 1; i <= SlotCount && i <= MaxSlots; i++)
        {
            if (_size[i] == 0)
                continue;
            var tg = Cells((sbyte)_x[i], (sbyte)_y[i], _size[i]);
            bool found = false;
            int bestDist = 255, bestK = 0, bestJ = 0;
            for (int j = 0; j <= 3; j++)
            {
                if (tg[j] is not var (gx, gy))
                    continue;
                for (int k = 0; k <= 3; k++)
                {
                    if (my[k] is not var (mx, myy))
                        continue;
                    if (!IsDirToward(mx, myy, gx, gy, filter))
                        continue;
                    int tx = gx, ty = gy, dist = (ushort)range;
                    if (!WalkLine(mx, myy, ref tx, ref ty, ref dist))
                        continue;
                    found = true;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestK = k;
                        bestJ = j;
                    }
                }
            }
            if (!found)
                continue;
            int dir = 0;
            if ((byte)filter < 8)
                dir = (byte)filter;
            else
            {
                var (mx, myy) = my[bestK]!.Value;
                var (gx, gy) = tg[bestJ]!.Value;
                while (dir < 9 && !IsDirToward(mx, myy, gx, gy, dir))
                    dir++;
            }
            _targets[++TargetCount] = new Target(i, (byte)bestDist, dir);
        }
        SortTargets();
    }

    private (int X, int Y)?[] Cells(int x, int y, int size)
    {
        var r = new (int, int)?[4];
        for (int k = 0; k <= 3; k++)
            r[k] = Tables.IconCell(size, k, out int dx, out int dy) ? ((sbyte)(byte)(x + dx), (sbyte)(byte)(y + dy)) : null;
        return r;
    }

    /// <summary>
    /// 0x2FB80: an exchange sort, i = 1..n−1, j = i+1..n: entry j moves before entry i when it is nearer, or as near
    /// with a lower direction that is not diagonal while i's is straight.
    /// </summary>
    private void SortTargets()
    {
        for (int i = 1; i <= TargetCount - 1; i++)
            for (int j = i + 1; j <= TargetCount; j++)
            {
                var a = _targets[i];
                var b = _targets[j];
                if (b.Dist < a.Dist || (b.Dist == a.Dist && b.Dir < a.Dir && b.Dir % 2 <= a.Dir % 2))
                {
                    _targets[i] = b;
                    _targets[j] = a;
                }
            }
    }

    /// <summary>
    /// START 0x2FD3: <see cref="BuildTargetList"/> from the creature's cell with <paramref name="range"/> and no cone,
    /// kept to the creatures of the other side (hostile byte = 1 when the creature's is 0, else 0; START 0x2F57); the
    /// list keeps them in order, and their slots go to DS:6DE4 + i (<see cref="PotentialTargets"/>). Returns the count.
    /// Range 1 = the adjacent enemies (distance 2 or 3) with a clear line; 255 = every enemy in sight.
    /// </summary>
    public int CountTargets(EclCharacter c, int range)
    {
        var (x, y) = Position(c);
        BuildTargetList(x, y, range, 255, IconSize(c));
        byte enemy = c[FightRules.Hostile] == 0 ? (byte)1 : (byte)0;
        int n = TargetCount;
        if (n > 0)
        {
            int cnt = 0;
            for (int i = 1; i <= n; i++)
                if (_creature[_targets[i].Slot]?[FightRules.Hostile] == enemy)
                    _targets[++cnt] = _targets[i];
            n = cnt;
            TargetCount = cnt;
        }
        PotentialTargets.Clear();
        for (int i = 1; i <= n; i++)
            PotentialTargets.Add(_targets[i].Slot);
        return n;
    }

    /// <summary>DS:6DE4 + i: the slots <see cref="CountTargets"/> found.</summary>
    public List<int> PotentialTargets { get; } = [];

    /// <summary>
    /// 0x14E38: the direction from one creature to another, the first d = 0, 1, … with the other's cell in the cone of d
    /// (<see cref="IsDirToward"/>, from cell to cell); the same cell gives 0. [?] With a cell off the arena no cone
    /// holds and the program's loop runs into the undefined directions; 8 here.
    /// </summary>
    public int DirectionToward(EclCharacter from, EclCharacter to)
    {
        var (x1, y1) = Position(from);
        var (x2, y2) = Position(to);
        return DirectionToward(x1, y1, x2, y2);
    }

    public int DirectionToward(int fromX, int fromY, int toX, int toY)
    {
        int d = 0;
        while (d < 8 && !IsDirToward(fromX, fromY, toX, toY, d))
            d++;
        return d;
    }

    /// <summary>
    /// START 0x30FB: the distance in squares from <paramref name="a"/> to <paramref name="b"/>: the target list from
    /// a's cell with the line mode set to 1 (terrain ignored), range 255 and no cone, then b's entry's distance shr 1
    /// (so a diagonal step counts 1.5 squares, rounded down over the line). The target entries are put back afterwards
    /// but not their count (DS:671A keeps the new one). When b is not listed (off the map) the search stops at the last
    /// entry and that one's distance is used [?] (and an empty list reads the old first entry).
    /// </summary>
    public int Distance(EclCharacter a, EclCharacter b)
    {
        var saved = _targets.AsSpan(1, 72).ToArray(); // 0xD8 bytes from DS:671B
        Arena.LineMode = 1;
        var (x, y) = Position(a);
        BuildTargetList(x, y, 0xFF, 0xFF, IconSize(a));
        Arena.LineMode = 0;
        int i = 1;
        while (!ReferenceEquals(_creature[_targets[i].Slot], b) && i < TargetCount)
            i++;
        int dist = _targets[i].Dist;
        saved.CopyTo(_targets.AsSpan(1));
        return dist >> 1;
    }

    /// <summary>Distance between two cells by the same walk (terrain ignored), in squares.</summary>
    public int Distance(int ax, int ay, int bx, int by)
    {
        byte mode = Arena.LineMode;
        Arena.LineMode = 1;
        int tx = bx, ty = by, dist = 0xFF;
        WalkLine(ax, ay, ref tx, ref ty, ref dist);
        Arena.LineMode = mode;
        return dist >> 1;
    }

    // ---- Seams for the menu, the effects and the spells ------------------------------------------------------------

    /// <summary>The fight's state, for the combat-record parts of <see cref="Remove"/> and <see cref="Place"/>
    /// (set by <see cref="FightPlacement.Start"/>).</summary>
    public FightState? State { get; set; }

    /// <summary>The creatures of a target list, in its order (slots past the creature table read as null are skipped).</summary>
    private IReadOnlyList<EclCharacter> ListedCreatures()
    {
        var r = new List<EclCharacter>();
        for (int i = 1; i <= TargetCount; i++)
            if (_creature[_targets[i].Slot] is { } c)
                r.Add(c);
        return r;
    }

    /// <summary>The area search 0x3045B from the creature's cell with its icon size, no cone and
    /// <paramref name="range"/> (in squares; a line of up to 2 × range + 1 half squares, stopped by blocking terrain):
    /// the creatures with a size, nearest first (the searcher itself first, at distance 0).</summary>
    public IReadOnlyList<EclCharacter> Search(Fight fight, EclCharacter from, int range)
    {
        var (x, y) = Position(from);
        BuildTargetList(x, y, range, 255, IconSize(from));
        return ListedCreatures();
    }

    /// <summary>0x313F3's terrain 0 for a cell: outside the arena.</summary>
    public bool IsOffMap(int x, int y) => Contents(x, y).Terrain == 0;

    /// <summary>0x317E1 with "not moving" (the menu's undo): see <see cref="TryPlace"/>.</summary>
    public bool Place(EclCharacter c, int x, int y) => TryPlace(c, x, y, false, State ?? new FightState());

    /// <summary>0x3154B: <see cref="Downed"/> (needs <see cref="State"/> for the body and the record; without it only
    /// the icon leaves the map).</summary>
    public void Remove(EclCharacter c)
    {
        if (State is { } s)
            Downed(c, s);
        else
            TakeOff(c);
    }

    /// <summary>The aura test of 0x2B04A: the target list around the carrier (its cell and icon size, range
    /// <paramref name="range"/>, no cone) holds the creature's slot.</summary>
    public bool InAuraRange(EclCharacter carrier, EclCharacter creature, int range)
    {
        var (x, y) = Position(carrier);
        BuildTargetList(x, y, range, 255, IconSize(carrier));
        int slot = Slot(creature);
        for (int i = 1; i <= TargetCount; i++)
            if (_targets[i].Slot == slot)
                return true;
        return false;
    }

    /// <summary>0x313F3 with direction 8: the terrain under the creature.</summary>
    public int TerrainUnder(EclCharacter c) => Probe(c, CombatTables.Center).Terrain;

    /// <summary>The target list 0x3045B around cell (x, y) with icon size 1 and no cone.</summary>
    public IReadOnlyList<EclCharacter> Around(int x, int y, int radius)
    {
        BuildTargetList(x, y, radius, 255, 1);
        return ListedCreatures();
    }

    /// <summary>The target list 0x3045B around a creature's cell with its own icon size (0x31371).</summary>
    public IReadOnlyList<EclCharacter> AroundCreature(EclCharacter c, int radius)
    {
        var (x, y) = Position(c);
        BuildTargetList(x, y, radius, 255, IconSize(c));
        return ListedCreatures();
    }

    /// <summary>0x2FF74 (<see cref="WalkLine"/>) for the AI: true when the walk reached the target within 2 × range + 1
    /// half squares with no blocking terrain; <paramref name="length"/> = the walked distance (where it stopped).</summary>
    public bool Line(Fight fight, int fromX, int fromY, int toX, int toY, int range, out int length)
    {
        int tx = toX, ty = toY, dist = (ushort)range;
        bool ok = WalkLine(fromX, fromY, ref tx, ref ty, ref dist);
        length = dist;
        return ok;
    }

    /// <summary>0x313F3 (<see cref="Probe"/>): the terrain one step away and the creature there.</summary>
    public (byte Ground, EclCharacter? Occupant) Look(EclCharacter c, int direction)
    {
        var (slot, terr) = Probe(c, direction);
        return ((byte)terr, slot > 0 ? _creature[slot] : null);
    }

    /// <summary>DS:2744 byte 0: the move cost of a terrain.</summary>
    public byte MoveCost(byte ground) => Tables.Cost(ground);
}
