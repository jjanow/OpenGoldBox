using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// The combat map's tables (<see cref="CombatTables"/>, docs/re/combat-map.md "Tables") read from the player's
/// <c>START.EXE</c> data segment at the DS offsets the v1.3 code uses. Nothing from the program is copied into our
/// code. The direction table at DS:2736 is checked first, so another program version is refused rather than misread.
/// </summary>
public static class PorCombatTables
{
    /// <summary>DS offsets (v1.3).</summary>
    public const int DirDx = 0x2736, Terrain = 0x2744, IconDelta = 0x2844, RingDir = 0x02D2, RingStart = 0x02E2,
        QuadDir = 0x02F2, StartQuadX = 0x02F6, StartQuadY = 0x02FE, RowBand = 0x0306, WildMap = 0x3648;

    /// <summary>Terrain indices read: 0–65 (64 and 65 are the first bytes of the icon table, as the program reads them).</summary>
    public const int TerrainEntries = 66;
    /// <summary>Rows of the wilderness map (the data after row 35 is another table).</summary>
    public const int WildRows = 36;

    public static CombatTables Load(PorProgram program) => Load(program.Ds);

    /// <summary>The tables from the data segment (DS:0000 onwards).</summary>
    public static CombatTables Load(ReadOnlySpan<byte> data)
    {
        var img = data.ToArray();
        byte[] Bytes(int off, int n)
        {
            if (off + n > img.Length)
                throw new FormatException($"START.EXE: DS:{off:X4} + {n} is past the image");
            return img.AsSpan(off, n).ToArray();
        }
        var dirs = Bytes(DirDx, 18);
        for (int i = 0; i < 9; i++)
            if ((sbyte)dirs[i] != CombatTables.DirDx[i] || (sbyte)dirs[9 + i] != CombatTables.DirDy[i])
                throw new FormatException("START.EXE: no direction table at DS:2736 (not Pool of Radiance DOS v1.3?)");
        return new CombatTables(
            Bytes(Terrain, TerrainEntries * CombatTables.TerrainEntrySize),
            Bytes(IconDelta, 64),
            Bytes(RingDir, 16),
            Bytes(RingStart, 16),
            Bytes(QuadDir, 4),
            Bytes(StartQuadX, 8),
            Bytes(StartQuadY, 8),
            Bytes(RowBand, 60),
            Bytes(WildMap, WildRows * CombatTables.WildMapStride));
    }

    public static CombatTables? Open(IPorContent game) => game.Program()?.CombatTables();
}
