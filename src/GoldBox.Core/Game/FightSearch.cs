using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// Who stands near whom, as the menu, the sweep and turn undead ask it (docs/re/combat-menu.md): the area search
/// (GAME.OVR 0x3045B, behind <see cref="IFightMap.Search"/>) and its enemy filter START 0x2FD3.
/// </summary>
public static class FightSearch
{
    /// <summary>
    /// The creatures the area search finds from <paramref name="from"/> within <paramref name="range"/> squares, in its
    /// order: <see cref="IFightMap.Search"/> when the fight has a combat map, else <see cref="DefaultSearch"/>.
    /// </summary>
    public static IReadOnlyList<EclCharacter> Nearby(Fight fight, EclCharacter from, int range) =>
        fight.Map is { } map ? map.Search(fight, from, range) : DefaultSearch(fight, from, range, _ => true);

    /// <summary>
    /// [?] A stand-in for the area search 0x3045B (its order is not read): <paramref name="from"/> first, then the
    /// other creatures in play on the map within <paramref name="range"/>, nearest first, ties in list order.
    /// </summary>
    public static IReadOnlyList<EclCharacter> DefaultSearch(Fight fight, EclCharacter from, int range,
        Func<EclCharacter, bool> onMap)
    {
        var found = new List<EclCharacter>();
        if (onMap(from))
            found.Add(from);
        found.AddRange(fight.List
            .Select((c, i) => (C: c, I: i))
            .Where(t => !ReferenceEquals(t.C, from) && t.C[FightRules.InPlay] != 0 && onMap(t.C))
            .Select(t => (t.C, t.I, D: fight.Geometry.Distance(from, t.C)))
            .Where(t => t.D <= range)
            .OrderBy(t => t.D).ThenBy(t => t.I)
            .Select(t => t.C));
        return found;
    }

    /// <summary>START 0x2F57: the side a creature fights against (hostile byte 0x10E 0 → 1, anything else → 0).</summary>
    public static byte EnemySide(EclCharacter c) => (byte)(c[FightRules.Hostile] == 0 ? 1 : 0);

    /// <summary>START 0x2FD3: the area search's creatures whose hostile byte is <see cref="EnemySide"/> of
    /// <paramref name="c"/>, in the search's order (the program copies them to DS:6DE4).</summary>
    public static List<EclCharacter> EnemiesNear(Fight fight, EclCharacter c, int range)
    {
        byte side = EnemySide(c);
        return Nearby(fight, c, range).Where(x => x[FightRules.Hostile] == side).ToList();
    }
}
