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
    /// <summary>
    /// The kit from the program's tables and the item types of <paramref name="content"/> (the folder's START.EXE,
    /// GAME.OVR and <c>ITEMS</c>, or a module's <c>rules/program.json</c> and <c>records/itemTypes.json</c>); null when
    /// a table is not found.
    /// </summary>
    public static FightKit? Kit(IPorContent content) =>
        content.Program() is { } program && content.ItemTypes() is { } items ? program.FightKit(items) : null;

    /// <summary>The kit from the program and the base item table; null when a table is not found.</summary>
    public static FightKit? Kit(PorProgram program, BaseItemTable items) =>
        PorProgramData.FromProgram(program, null).FightKit(items);

    /// <summary>
    /// The fight start's inputs from VM memory: the view mode (DS:49FA), the party's square and facing, the current ECL
    /// block, and for a 3D area the map (a copy of <paramref name="planes"/>, with doors as they are now); in the
    /// wilderness the party's square on the wilderness map ($49C3 / $49C4) and $4AB3.
    /// </summary>
    public static FightStartArea Area(EclMemory memory, byte[]? planes, int eclBlock, bool duel = false, Func<int, bool>? noWrapOffMap = null)
    {
        var p = memory.Position;
        return new FightStartArea
        {
            ViewMode = memory.ViewMode,
            PartyX = p.X,
            PartyY = p.Y,
            PartyFacing = p.Facing,
            Map = planes is { Length: >= AreaMap.PlanesLength }
                ? new AreaMap(planes.AsSpan(0, AreaMap.PlanesLength)) { Bounded = (noWrapOffMap ?? (s => s is 0 or 10))(eclBlock) } : null,
            EclBlock = eclBlock,
            Duel = duel,
            WildX = memory.Read(FightPlacement.WildXCell) & 0xFF,
            WildY = memory.Read(FightPlacement.WildYCell) & 0xFF,
            YarashDone = (memory.Read(FightPlacement.YarashCell) & 0xFF) == 0xFF,
        };
    }
}
