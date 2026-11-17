using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Pool of Radiance's fight: the <see cref="FightKit"/> read from the player's game (START.EXE / GAME.OVR tables, the
/// <c>ITEMS</c> file) and the fight built from it for an encounter (<see cref="FightKit.Create"/>).
/// </summary>
public static class PorFight
{
    /// <summary>The kit from the game directory; null when a table is not found.</summary>
    public static FightKit? Kit(PorGameDirectory game)
    {
        if (PorProgram.Open(game) is not { } program || game.Find("ITEMS") is not { } items)
            return null;
        return Kit(program, BaseItemTable.FromItemsFile(File.ReadAllBytes(items)));
    }

    /// <summary>The kit from the program and the base item table; null when a table is not found.</summary>
    public static FightKit? Kit(PorProgram program, BaseItemTable items)
    {
        if (PorSpellData.Spells(program) is not { } spells || program.AiStepOffsets() is not { } steps)
            return null;
        return new FightKit
        {
            Tables = PorCombatTables.Load(program),
            Spells = spells,
            Items = items,
            AiSteps = steps,
            Names = PorSpellData.Names(program),
            TurnUndead = program.TurnUndeadTable(),
        };
    }

    /// <summary>
    /// The fight start's inputs from VM memory: the view mode (DS:49FA), the party's square and facing, the current ECL
    /// block, and for a 3D area the map (a copy of <paramref name="planes"/>, with doors as they are now); in the
    /// wilderness the party's square on the wilderness map ($49C3 / $49C4) and $4AB3.
    /// </summary>
    public static FightStartArea Area(EclMemory memory, byte[]? planes, int eclBlock, bool duel = false)
    {
        var p = memory.Position;
        return new FightStartArea
        {
            ViewMode = memory.ViewMode,
            PartyX = p.X,
            PartyY = p.Y,
            PartyFacing = p.Facing,
            Map = planes is { Length: >= AreaMap.PlanesLength }
                ? new AreaMap(planes.AsSpan(0, AreaMap.PlanesLength)) { Bounded = eclBlock is 0 or 10 } : null,
            EclBlock = eclBlock,
            Duel = duel,
            WildX = memory.Read(FightPlacement.WildXCell) & 0xFF,
            WildY = memory.Read(FightPlacement.WildYCell) & 0xFF,
            YarashDone = (memory.Read(FightPlacement.YarashCell) & 0xFF) == 0xFF,
        };
    }
}
