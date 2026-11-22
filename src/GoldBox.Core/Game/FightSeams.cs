using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// Shared seams between the parts of the fight that are read in parallel (s12): the combat map and moves (fight start
// 0xE402, stub 0x68 move routines 0x12E22/0x12F0D/0x130AD/0x13465, direction/distance 0x14E38 / START 0x30FB), the
// monster AI (0xABCF), the player's menu (0x9B21) and spells (0x14C0F, 0x27C38). Each part implements or consumes
// these; members may be added (keep existing names and meanings), and every rule behind them cites its address.

/// <summary>The combat map as the fight sees it: squares, who stands where. Implemented by the combat-map code.</summary>
public interface IFightMap : IFightGeometry
{
    /// <summary>The creature's square on the combat map. [?] coordinates as the program stores them.</summary>
    (int X, int Y) Position(EclCharacter c);
    /// <summary>The creature standing on a square, or null.</summary>
    EclCharacter? At(int x, int y);
    /// <summary>False once a creature has left the map (fled) or was never placed.</summary>
    bool OnMap(EclCharacter c);
    /// <summary>Direction 0–7 (0 N, clockwise) between squares, by the program's rule (0x14E38).</summary>
    int DirectionToward(int fromX, int fromY, int toX, int toY);
    /// <summary>Distance between squares, by the program's rule (START 0x30FB).</summary>
    int Distance(int ax, int ay, int bx, int by);

    // Added for the menu, sweep and turn undead (s12, docs/re/combat-menu.md). Default bodies so implementations
    // compile unchanged; each default is a stand-in, not the program's rule.

    /// <summary>
    /// The area search GAME.OVR 0x3045B from <paramref name="from"/>'s square: the creatures on the map within
    /// <paramref name="range"/> squares, both sides, in the search's order (START 0x2FD3 filters it to enemies for
    /// sweeps and turn undead; Aim 0x15AE1 walks it with range 0x7F). [?] Default: <paramref name="from"/> first, then
    /// the other creatures on the map that are in play, by <see cref="IFightGeometry.Distance(EclCharacter, EclCharacter)"/>,
    /// ties in list order — not the program's order.
    /// </summary>
    IReadOnlyList<EclCharacter> Search(Fight fight, EclCharacter from, int range) =>
        FightSearch.DefaultSearch(fight, from, range, OnMap);

    /// <summary>0x313F3's ground byte 0: the square is off the map (the menu's move asks the flee question).
    /// [?] Default: never.</summary>
    bool IsOffMap(int x, int y) => false;

    /// <summary>0x317E1: put a creature on a square (the menu's move undo on Escape). False when it can't stand
    /// there. [?] Default: not supported (false).</summary>
    bool Place(EclCharacter c, int x, int y) => false;

    // Added for the monster AI (docs/re/combat-ai.md); the defaults are stand-ins [?] until the map code overrides them.

    /// <summary>
    /// 0x2FF74: a line from one square towards another, at most 2 × range + 1 long (program units; the AI compares
    /// length / 2 with a range in squares). True when it reached the target; <paramref name="length"/> = its length.
    /// Default [?]: 2 × distance, no obstacles.
    /// </summary>
    bool Line(Fight fight, int fromX, int fromY, int toX, int toY, int range, out int length)
    {
        int d = Distance(fromX, fromY, toX, toY);
        length = 2 * d;
        return length <= 2 * range + 1;
    }

    /// <summary>
    /// 0x313F3: the square one step from the creature in <paramref name="direction"/>: its ground type (0 off the
    /// map; the highest-cost type over the footprint, 0x1E wins) and another creature there, or null. Default [?]:
    /// ground 1, occupant from <see cref="At"/>.
    /// </summary>
    (byte Ground, EclCharacter? Occupant) Look(EclCharacter c, int direction)
    {
        var (x, y) = Position(c);
        int[] dx = [0, 1, 1, 1, 0, -1, -1, -1], dy = [-1, -1, 0, 1, 1, 1, 0, -1];
        var o = At(x + dx[direction & 7], y + dy[direction & 7]);
        return (1, ReferenceEquals(o, c) ? null : o);
    }

    /// <summary>DS:2744 + 4 × ground, byte 0: the movement cost of a ground type (the step needs 2 or, diagonally,
    /// 3 × cost on combat record +6). Default 1 [?].</summary>
    byte MoveCost(byte ground) => 1;

    /// <summary>0x3154B: take a creature off the map (destroyed by turn undead, dropped). [?] Default: nothing.</summary>
    void Remove(EclCharacter c) { }

    /// <summary>
    /// The icon size byte of the creature's slot := 0 and nothing else (DS:5F2A + 4 × slot, as 0x1638E writes it): no
    /// grid rebuild, no body, no sound. Default: <see cref="Remove"/> [?] (a map without slots).
    /// </summary>
    void ClearIconSize(EclCharacter c) => Remove(c);
}

/// <summary>What a single step on the combat map ended in.</summary>
public enum StepResult
{
    /// <summary>The creature moved one square.</summary>
    Moved,
    /// <summary>The square can't be entered (wall, creature, edge with no flee); nothing spent.</summary>
    Blocked,
    /// <summary>Not enough movement left (combat record +6).</summary>
    NoMovement,
    /// <summary>The creature left the map (fled).</summary>
    Fled,
    /// <summary>The creature dropped during the step (attacks of opportunity).</summary>
    Dropped,
}

/// <summary>
/// Moving on the combat map: a step, with the movement it costs, the attacks of opportunity it draws and fleeing off
/// the edge. Implemented by the combat-map code; used by the AI and the menu.
/// </summary>
public interface IFightMoves
{
    /// <summary>One step towards direction 0–7 (0 N, clockwise).</summary>
    StepResult Step(Fight fight, EclCharacter c, int direction);

    // Added for the monster AI (docs/re/combat-ai.md). The AI's step (0xB673–0xB6E2) is the facing (0x31232), then
    // the attacks of opportunity 0x130AD and, while the creature is still in play, the move 0x12F0D and the terrain
    // effects 0x2B9D1: Step covers those three. The sweep (0x1367D) is SweepAttack.Try.

    /// <summary>0x13465: try to flee off the map edge (the AI calls it when a fleeing creature's step meets the edge).
    /// True = the move is over. Default false.</summary>
    bool Flee(Fight fight, EclCharacter c) => false;
}

/// <summary>
/// Casting in a fight: the spell list a creature can cast now and the cast itself (target pick, effects, saves).
/// Implemented by the spell code; used by the AI and the menu.
/// </summary>
public interface IFightSpells
{
    /// <summary>The combat Cast's list (0x275B4 kind 0): the memorised ids the spell list offers. Empty for none.</summary>
    IReadOnlyList<int> Castable(Fight fight, EclCharacter caster);
    /// <summary>Casts <paramref name="spell"/> at a creature target or a square (one of them, or neither for spells
    /// with no target). Returns false when the cast did not happen.</summary>
    bool Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square);
    /// <summary>Casts at several creatures: spells whose area mode (table byte 6 AND 0x0F) is 1–7 take up to
    /// (area AND 3) + 1 picks (target pick 0x148BE). Default: the first one through <see cref="Cast"/>.</summary>
    bool CastOn(Fight fight, EclCharacter caster, int spell, IReadOnlyList<EclCharacter> targets) =>
        Cast(fight, caster, spell, targets.Count > 0 ? targets[0] : null, null);
    /// <summary>The turn of a creature that began casting earlier (combat record +0 &gt; 0, set by a cast with a casting
    /// time): the menu (0x9B21) and the AI cast <paramref name="spell"/> now (0x27C38) on the targets picked now, and
    /// the actions end. The menu clears +0 before the cast (0x9B59), the AI only after it (0xACEE, START 0x31D7).
    /// False when it was casting nothing (spell 0).</summary>
    bool ContinueCasting(Fight fight, EclCharacter caster, int spell, IReadOnlyList<EclCharacter> targets, (int X, int Y)? square) => false;

    // Added for the menu's Cast (0x14C0F, s12, docs/re/combat-menu.md "Cast"); default bodies so implementations
    // compile unchanged. The menu aims for the player first (0x14611 with ai = 0, FightMenu.CastNow) and then calls
    // Cast / CastOn with what was aimed at, or ContinueCasting for a spell begun earlier.

    /// <summary>Spell table byte 5 (DS:3200 + 16 × spell + 5) ≠ 0: the spell can be cast in a fight (else the menu
    /// shows a "camp only" message). [?] Default: true.</summary>
    bool UsableInCombat(int spell) => true;

    /// <summary>Casting time in initiative steps: spell table byte 6 (DS:3206 + 16 × spell) div 3. 0 = cast at once.
    /// [?] Default: 0.</summary>
    int CastingTime(int spell) => 0;

    // ---- Added for the monster AI (docs/re/combat-ai.md) ----

    /// <summary>The spell table entry (DS:31FA + 16 × spell) bytes the AI reads: 13 priority, 14 needs targets
    /// (0 = cast with no target check), 15 area. Default (0, 0, 0): the AI never picks the spell.</summary>
    (byte Priority, byte Targeted, byte Area) AiInfo(int spell) => (0, 0, 0);
    /// <summary>0x27741: the spell's range in squares for the current caster.</summary>
    int Range(Fight fight, EclCharacter caster, int spell) => 0;
    /// <summary>0x143F3: a cure target for spell 3 exists (the AI's cure test). Default false.</summary>
    bool FindCureTarget(Fight fight, EclCharacter caster) => false;
    /// <summary>0x14C0F with ai = 1: the cast with the program's own target pick. Returns the routine's "cast" flag.</summary>
    bool CastAuto(Fight fight, EclCharacter caster, int spell) => Cast(fight, caster, spell, null, null);
    /// <summary>0x22AC4: use a magic item (the AI's item pick 0xAFA4).</summary>
    void UseItem(Fight fight, EclCharacter user, byte[] item) { }
    /// <summary>0xACEE: the AI's turn of a creature that began casting: 0x27C38(spell, ai = 1, 1), the program's own
    /// target pick, then the actions end. Default: <see cref="ContinueCasting"/> with no targets.</summary>
    bool ContinueCastingAuto(Fight fight, EclCharacter caster) =>
        ContinueCasting(fight, caster, fight.Record(caster)[CombatRecord.SpellCast], [], null);
}

/// <summary>No moves: every step is blocked (a fight with no combat map).</summary>
public sealed class NoFightMoves : IFightMoves
{
    public static readonly NoFightMoves Instance = new();
    public StepResult Step(Fight fight, EclCharacter c, int direction) => StepResult.Blocked;
}

/// <summary>No spells: nothing castable.</summary>
public sealed class NoFightSpells : IFightSpells
{
    public static readonly NoFightSpells Instance = new();
    public IReadOnlyList<int> Castable(Fight fight, EclCharacter caster) => [];
    public bool Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square) => false;
}
