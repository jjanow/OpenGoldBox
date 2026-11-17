using GoldBox.Core.Doors;

namespace GoldBox.Core.Explore;

public enum StepOutcome
{
    /// <summary>The party moved one square (wrapping at the map edge).</summary>
    Moved,
    /// <summary>The own face is impassable (barrier 0): no move, the program plays its bump sound.</summary>
    Blocked,
    /// <summary>The own face is a door (barrier 2 or 3): the program opens the door menu instead of moving.</summary>
    Door,
}

/// <summary>Result of a forward step.</summary>
/// <param name="LeftMap">
/// The step crossed the map edge: the program sets VM $6DD5 = 1 before the area script's step vector runs, so the
/// script can take the party to another area; if it does not, the party wraps to the other side (geo-format.md
/// "Edges").
/// </param>
public readonly record struct StepResult(StepOutcome Outcome, DoorBarrier Barrier, bool LeftMap);

/// <summary>
/// The party's square and facing on a 3D map, and the exploring moves of the program's command loop
/// (docs/re/view3d.md "Movement"): Left/Right turn by 90°, Down turns about, Up steps forward. Turning never
/// fails and costs no time; a step reads only the face of the party's own square (one-way walls, geo-format.md).
/// </summary>
public sealed class PartyPosition(int x, int y, int facing)
{
    public int X { get; private set; } = Check(x, nameof(x));
    public int Y { get; private set; } = Check(y, nameof(y));
    public int Facing { get; private set; } = Explore.Facing.IsCardinal(facing)
        ? facing
        : throw new ArgumentOutOfRangeException(nameof(facing), facing, "cardinal facings only (0, 2, 4, 6)");

    private static int Check(int v, string name) =>
        v is >= 0 and < AreaMap.Size ? v : throw new ArgumentOutOfRangeException(name, v, "squares are 0..15");

    public void TurnLeft() => Facing = Explore.Facing.Left(Facing);
    public void TurnRight() => Facing = Explore.Facing.Right(Facing);
    public void TurnAround() => Facing = Explore.Facing.Back(Facing);

    /// <summary>The barrier the party faces (what a step would meet).</summary>
    public DoorBarrier Ahead(AreaMap map) => map.Passage(X, Y, Facing);

    /// <summary>
    /// The forward step without an area script (TryWalkOffMap, then MoveParty and StepForward3D): passable → move and
    /// wrap; impassable → no move; a door → no move here (the door menu is the caller's: <c>GoldBox.Core.Doors</c>).
    /// </summary>
    public StepResult StepForward(AreaMap map)
    {
        var barrier = map.Passage(X, Y, Facing);
        int nx = X + Explore.Facing.DeltaX(Facing), ny = Y + Explore.Facing.DeltaY(Facing);
        bool leftMap = barrier != DoorBarrier.Impassable && !AreaMap.InMap(nx, ny);
        switch (barrier)
        {
            case DoorBarrier.Passable:
                X = Wrap(nx);
                Y = Wrap(ny);
                return new StepResult(StepOutcome.Moved, barrier, leftMap);
            case DoorBarrier.Impassable:
                return new StepResult(StepOutcome.Blocked, barrier, false);
            default:
                return new StepResult(StepOutcome.Door, barrier, leftMap);
        }
    }

    /// <summary>Moves the party through a door it has just opened (the program's step after a successful door action).</summary>
    public void StepThrough()
    {
        X = Wrap(X + Explore.Facing.DeltaX(Facing));
        Y = Wrap(Y + Explore.Facing.DeltaY(Facing));
    }

    private static int Wrap(int v) => v > 15 ? 0 : v < 0 ? 15 : v;

    public override string ToString() => $"{X},{Y} {Explore.Facing.Letter(Facing)}";
}
