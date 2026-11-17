using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>Wall types of the current map (docs/re/geo-format.md, planes 0 and 1).</summary>
public interface IWallMap
{
    /// <summary>4-bit wall type of face <paramref name="face"/> (0 N, 2 E, 4 S, 6 W) of square (x, y), 0 ≤ x, y ≤ 15.</summary>
    int WallType(int x, int y, int face);
}

/// <summary>The map planes as the game holds them in memory: four 256-byte planes (walls N/E, walls S/W, event, barrier).</summary>
public sealed class MapPlanes : IWallMap
{
    public const int Size = 1024;
    public byte[] Planes { get; }

    /// <param name="planes">At least the two wall planes (512 bytes); a GEO block without its 2-byte header, or a capture's <c>geo_planes.bin</c>.</param>
    public MapPlanes(byte[] planes)
    {
        if (planes.Length < 512)
            throw new ArgumentException("need the two wall planes (512 bytes)", nameof(planes));
        Planes = planes;
    }

    public int WallType(int x, int y, int face)
    {
        int i = y * 16 + x;
        return face switch
        {
            0 => Planes[i] >> 4,
            2 => Planes[i] & 0x0F,
            4 => Planes[256 + i] >> 4,
            6 => Planes[256 + i] & 0x0F,
            _ => 0,
        };
    }
}

/// <summary>What the party chose in the encounter menu, after the program's index mapping (GAME.OVR 0x2F28).</summary>
public enum EncounterChoice { Combat = 0, Wait = 1, Flee = 2, Advance = 3, Parley = 4 }

/// <summary>Value ENCMENU writes to its destination (Lee 12.3.3 names; the values are P, GAME.OVR 0x2F4A–0x32C0).</summary>
public enum EncounterResult : ushort { MonstersFlee = 0, Combat = 1, PartyFlees = 2, Parley = 3 }

/// <summary>Which bottom-line menu ENCMENU shows (two fixed strings in the program, GAME.OVR 0x2ED9/0x2EEF).</summary>
public enum EncounterMenuKind
{
    /// <summary>Indoors at distance &gt; 0: combat, wait, flee, advance.</summary>
    CombatWaitFleeAdvance,
    /// <summary>Distance 0 or outdoors: combat, wait, flee, parley (the 4th item is choice 4).</summary>
    CombatWaitFleeParley,
}

/// <summary>The two fixed one-line messages ENCMENU prints (GAME.OVR unit offsets 0x1ED4 / 0x1EE5).</summary>
public enum EncounterMessage { BothSidesWait, MonstersFlee }

/// <summary>One pass of the ENCMENU loop: what to write, whether the monsters close in and whether the menu repeats.</summary>
public readonly record struct EncounterStep(EncounterResult? Result, bool Repeat, bool Approach, EncounterMessage? Message);

/// <summary>
/// The encounter rules read from the program (docs/re/ecl-format.md "Encounters"). Pure functions; the host applies
/// them to VM memory.
/// </summary>
public static class EncounterRules
{
    /// <summary>Effect ids the speed rule tests (GAME.OVR 0x9484): 0x27 doubles movement, 0x2A halves it.</summary>
    public const byte EffectDoubleMove = 0x27, EffectHalveMove = 0x2A;

    /// <summary>Record byte of the current movement (character-format.md 0x11C).</summary>
    public const int MovementOffset = 0x11C;

    /// <summary>
    /// How many squares the monsters can be from the party, 0–2 (GAME.OVR 0x7955). Outdoors ($49E6 = 0) always 2.
    /// Indoors: walk from the party's square in the facing direction while the face's wall type is 0, at most two
    /// squares. Any drawn wall (door, open door, passable wall) stops the walk; the barrier plane is not read. Off
    /// the map the coordinate wraps by one step (> 15 → 0, &lt; 0 → 15), except that while the current ECL block is
    /// 0 or 10 a square off the map has no wall (0x2ED72 / 0x2EC16). A null map counts as no walls.
    /// </summary>
    public static int ViewDistance(IWallMap? map, bool outdoors, int x, int y, int facing, int block)
    {
        if (outdoors)
            return 2;
        int n = 0;
        while (n < 2)
        {
            if (WallAt(map, x, y, facing, block) != 0)
                break;
            n++;
            switch (facing)
            {
                case 0: y--; break;
                case 2: x++; break;
                case 4: y++; break;
                case 6: x--; break;
            }
        }
        return n;
    }

    /// <summary>The wall accessor 0x2ED72 with its edge rules. x and y are signed bytes in the program.</summary>
    public static int WallAt(IWallMap? map, int x, int y, int face, int block)
    {
        x = (sbyte)(byte)x;
        y = (sbyte)(byte)y;
        bool onMap = x is >= 0 and <= 15 && y is >= 0 and <= 15;
        if (!onMap && (block == 0 || block == 10))
            return 0;
        if (x > 15) x = 0;
        if (x < 0) x = 15;
        if (y > 15) y = 0;
        if (y < 0) y = 15;
        if (face is not (0 or 2 or 4 or 6))
            return 0;
        return map?.WallType(x, y, face) ?? 0;
    }

    /// <summary>
    /// Fastest and slowest movement over the whole combat list (party, plus any monsters LOADMON appended),
    /// GAME.OVR 0x9484: movement = record byte 0x11C, doubled with effect 0x27, else halved with effect 0x2A. Quirk
    /// kept: both start from the first entry's raw byte, before its effects are applied. Empty list: (0, 0).
    /// </summary>
    public static (byte Fastest, byte Slowest) PartySpeed(IReadOnlyList<EclCharacter> list)
    {
        if (list.Count == 0)
            return (0, 0);
        byte max = list[0][MovementOffset], min = max;
        foreach (var c in list)
        {
            byte m = c[MovementOffset];
            if (c.HasEffect(EffectDoubleMove))
                m = (byte)(m << 1);
            else if (c.HasEffect(EffectHalveMove))
                m = (byte)(m >> 1);
            if (m > max) max = m;
            if (m < min) min = m;
        }
        return (max, min);
    }

    /// <summary>
    /// Text shown at distance <paramref name="distance"/> (GAME.OVR 0x2DB9): the first non-empty of slot d+1, d+2, …,
    /// wrapping to slot 1 (so a missing text falls back to the next farther one; Lee says the nearer one). Null when
    /// all three are empty. [?] Distances above 2 leave the program's buffer unset; null here.
    /// </summary>
    public static string? PickText(IReadOnlyList<string> texts, int distance)
    {
        if (distance is < 0 or > 2 || texts.Count < 3)
            return null;
        for (int k = 0; k < 3; k++)
        {
            var t = texts[(distance + k) % 3];
            if (t.Length > 0)
                return t;
        }
        return null;
    }

    /// <summary>The menu shown at this distance (GAME.OVR 0x2EC1).</summary>
    public static EncounterMenuKind MenuKind(int distance, bool outdoors) =>
        distance != 0 && !outdoors ? EncounterMenuKind.CombatWaitFleeAdvance : EncounterMenuKind.CombatWaitFleeParley;

    /// <summary>Menu index (0–3) to choice: the 4th item is Parley in the parley menu (GAME.OVR 0x2F40).</summary>
    public static EncounterChoice Choice(int menuIndex, EncounterMenuKind kind) =>
        menuIndex == 3 && kind == EncounterMenuKind.CombatWaitFleeParley ? EncounterChoice.Parley : (EncounterChoice)menuIndex;

    /// <summary>
    /// One menu pass (GAME.OVR 0x2F4A–0x32C5): the monsters' reaction to the choice. <paramref name="distance"/> is
    /// $6DC1 before the pass; <paramref name="move1"/> is the slowest speed the party needs to flee, and
    /// <paramref name="move2"/> the speed the monsters need to escape an attack.
    /// </summary>
    public static EncounterStep Resolve(int reaction, EncounterChoice choice, int distance, byte fastest, byte slowest,
        byte move1, byte move2)
    {
        var combat = new EncounterStep(EncounterResult.Combat, false, false, null);
        var partyFlees = new EncounterStep(EncounterResult.PartyFlees, false, false, null);
        var parley = new EncounterStep(EncounterResult.Parley, false, false, null);
        var close = new EncounterStep(null, true, true, null);
        var bothWait = new EncounterStep(null, true, false, EncounterMessage.BothSidesWait);
        var monstersFlee = new EncounterStep(EncounterResult.MonstersFlee, false, false, EncounterMessage.MonstersFlee);
        EncounterStep CloseOrWait() => distance != 0 ? close : bothWait;
        EncounterStep CloseOrParley() => distance != 0 ? close : parley;

        switch (reaction)
        {
            case 0: // fight, unless the party flees and its slowest member is fast enough
                return choice == EncounterChoice.Flee && slowest >= move1 ? partyFlees : combat;
            case 1: // do what the party chose
                return choice switch
                {
                    EncounterChoice.Combat => combat,
                    EncounterChoice.Wait => bothWait,
                    EncounterChoice.Flee => partyFlees,
                    EncounterChoice.Advance => CloseOrWait(),
                    _ => CloseOrParley(),
                };
            case 2: // the monsters flee; an attack catches them unless they are faster than the fastest member
                return choice == EncounterChoice.Combat && move2 <= fastest ? combat : monstersFlee;
            case 3: // the monsters advance
                return choice switch
                {
                    EncounterChoice.Combat => combat,
                    EncounterChoice.Flee => partyFlees,
                    EncounterChoice.Parley => CloseOrParley(),
                    _ => CloseOrWait(),
                };
            case 4: // the monsters want to talk
                return choice switch
                {
                    EncounterChoice.Combat => combat,
                    EncounterChoice.Flee => partyFlees,
                    _ => CloseOrParley(),
                };
            default: // 5 and up: the menu ends and nothing is written
                return new EncounterStep(null, false, false, null);
        }
    }

    /// <summary>
    /// Icon colour byte from one 1d6 roll r (monster loader GAME.OVR 0x1EFC2): low nibble r, high nibble r + 8.
    /// </summary>
    public static byte IconColour(int r) => (byte)((r + 8) * 16 + r);

    /// <summary>Item type for one random treasure item (TREASURE, GAME.OVR 0x28A9–0x2A13), from the program's dice.</summary>
    /// <param name="roll">Answers Random(n) (0..n−1); the program's dice are 1 + Random(n).</param>
    /// <param name="previous">The type of the item before (the program leaves its variable unchanged for a second roll of 48 or 49).</param>
    public static byte RandomItemType(Func<int, int> roll, byte previous)
    {
        int D(int sides) => roll(sides) + 1;
        int r1 = D(100);
        if (r1 <= 60)
        {
            int r2 = D(100);
            if (r2 is >= 1 and <= 47 or >= 50 and <= 59)
                return (byte)(r2 == 45 ? 59 : r2);
            if (r2 is >= 60 and <= 90)
            {
                int r3 = D(10);
                return r3 switch { <= 4 => 36, <= 7 => 35, 8 => 34, 9 => 37, _ => 38 };
            }
            if (r2 is >= 91 and <= 94) return 73;
            if (r2 is >= 95 and <= 97) return 93;
            if (r2 >= 98) return 77;
            return previous; // 48, 49: [?] unset in the program (previous item's type, or stack residue for the first)
        }
        if (r1 <= 85) return 61;
        if (r1 <= 92) return 62;
        if (r1 <= 98)
        {
            int r4 = D(16);
            return r4 switch { <= 7 => 71, <= 9 => 70, 10 => 84, <= 12 => 78, <= 14 => 79, 15 => 92, _ => 67 };
        }
        return 69;
    }
}
