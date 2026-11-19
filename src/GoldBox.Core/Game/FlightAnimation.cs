namespace GoldBox.Core.Game;

/// <summary>
/// One frame of the moving icon's sprite (DS:6E36), as START 0x2432 (0xBA:0x1892) copies it in: the picture of icon cell
/// 13 + <paramref name="Block"/> (the fight start 0xEADC loads COMSPR i into cell i + 13), its first or second picture
/// (DS:5E5A + cell·8 + sub·4: the Client's sprite + 128), mirrored left to right or not.
/// </summary>
public readonly record struct FlightFrame(int Block, bool Second, bool Mirror);

/// <summary>
/// The moving icon START 0x25C9 (0xBA:0x1A29, 12 bytes of arguments; read s25, docs/re/combat-menu.md "Moving icon"):
/// the sprite flies from square (<see cref="FromX"/>, <see cref="FromY"/>) to (<see cref="ToX"/>, <see cref="ToY"/>),
/// one third of a square per step, cycling through <see cref="Frames"/>, Delay(<see cref="DelayMs"/>) per drawn
/// position (a fixed delay, not the game speed). The program calls it inside a resident sound bracket, so the cue played
/// before it sounds for the flight's length. A display hook only: no game state changes.
/// </summary>
public sealed record FlightAnimation(int FromX, int FromY, int ToX, int ToY, IReadOnlyList<FlightFrame> Frames, int DelayMs)
{
    /// <summary>
    /// START 0x257E (0xBA:0x19DE): the four frames of one icon cell: first picture, first mirrored, second mirrored,
    /// second.
    /// </summary>
    public static IReadOnlyList<FlightFrame> Burst(int block) =>
    [
        new(block, false, false), new(block, false, true), new(block, true, true), new(block, true, false),
    ];

    /// <summary>COMSPR block of icon cell 0x12 (the spell flight, 0x27DA9 / 0x28706) and 0x13 (the bolt, 0x2997C).</summary>
    public const int SpellBlock = 5, BoltBlock = 6;

    /// <summary>The cast's flight to the aimed square (0x27E37–0x27E4D) and Hold Person's to a further target (0x28745).</summary>
    public static FlightAnimation Spell(int fromX, int fromY, int toX, int toY) =>
        new(fromX, fromY, toX, toY, Burst(SpellBlock), 30);

    /// <summary>One flight of the Lightning Bolt routine 0x29973 (0x29AEC–0x29B02): from the line's start to the cell
    /// about to be hit.</summary>
    public static FlightAnimation Bolt(int fromX, int fromY, int toX, int toY) =>
        new(fromX, fromY, toX, toY, Burst(BoltBlock), 50);

    /// <summary>
    /// A missile's flight (0x14EAB, before 0x15057), by the missile item's type (+0x2E) and the direction 0–7 from
    /// attacker to defender (0x14E38): types 9, 0x15, 0x1C, 0x1F, 0x49 one frame for 10 ms (straight: COMSPR 0 for
    /// north / south, 2 for east / west, the second picture for south and west; diagonal: COMSPR 1, the second picture
    /// for south-east and south-west, mirrored for south-west and north-west); 2, 7, 0x14 the four frames of COMSPR 3
    /// for 50 ms; 0x55, 0x56 those of COMSPR 4; 0x2F COMSPR 8 and any other type COMSPR 7, two frames (first picture,
    /// second mirrored) for 20 ms.
    /// </summary>
    public static FlightAnimation Missile(int fromX, int fromY, int toX, int toY, byte itemType, int direction)
    {
        int d = direction & 7;
        switch (itemType)
        {
            case 9 or 0x15 or 0x1C or 0x1F or 0x49:
                FlightFrame f = (d & 1) != 0
                    ? new(1, d is 3 or 5, d is 5 or 7)
                    : new(d % 4, d / 4 == 1, false);
                return new(fromX, fromY, toX, toY, [f], 10);
            case 2 or 7 or 0x14:
                return new(fromX, fromY, toX, toY, Burst(3), 50);
            case 0x55 or 0x56:
                return new(fromX, fromY, toX, toY, Burst(4), 50);
            default:
                int b = itemType == 0x2F ? 8 : 7;
                return new(fromX, fromY, toX, toY, [new(b, false, false), new(b, true, true)], 20);
        }
    }
}
