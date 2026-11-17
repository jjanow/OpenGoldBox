using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The turn-undead table (START.EXE DS:0447 in v1.3, read from the player's file by <c>GoldBox.Data.Por.PorProgram</c>;
/// none of it is in our code): the signed byte at base + 10 × row + column, row = creature byte 0x76 (1–10 in PoR),
/// column 1–10 from the cleric's level (<see cref="TurnUndead.Column"/>). A value v lets a roll of at least |v| work;
/// v &gt; 0 turns, v ≤ 0 destroys.
/// </summary>
public sealed class TurnUndeadTable
{
    /// <summary>Bytes needed from the base: up to row 10, column 10.</summary>
    public const int Length = 111;

    private readonly byte[] _bytes;

    /// <param name="fromBase">The data-segment bytes from the table base (DS:0447) on, at least <see cref="Length"/>.</param>
    public TurnUndeadTable(byte[] fromBase)
    {
        if (fromBase.Length < Length)
            throw new ArgumentException($"turn-undead table needs {Length} bytes from its base", nameof(fromBase));
        _bytes = fromBase;
    }

    /// <summary>The signed byte at base + 10 × <paramref name="row"/> + <paramref name="column"/> (0x13A21–0x13A38).</summary>
    public sbyte Value(int row, int column)
    {
        int i = row * 10 + column;
        if (i < 0 || i >= _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(row), $"[?] row {row} column {column} lies outside the bytes read");
        return (sbyte)_bytes[i];
    }
}

/// <summary>What one turn-undead attempt did: the 1d12 count and 1d20 roll drawn, and the creatures turned / destroyed.</summary>
public readonly record struct TurnUndeadResult(byte Count, byte Roll, bool Affected, IReadOnlyList<EclCharacter> Turned,
    IReadOnlyList<EclCharacter> Destroyed);

/// <summary>
/// Turn undead (GAME.OVR 0x13965, docs/re/combat-menu.md "Turn undead"), from the menu's Turn and the AI (0xAE01).
/// </summary>
public static class TurnUndead
{
    /// <summary>Creature bytes: turn-undead row 0x76 (signed, 0 = can't be turned), cleric level 0x96 (signed).</summary>
    public const int Row = 0x76, ClericLevel = 0x96;

    /// <summary>Highest row + 1 the target search accepts (it starts with 13 and needs a lower row).</summary>
    public const int RowLimit = 13;

    /// <summary>0x139CA–0x139F5: cleric level 1–8 → that level; 9–13 → 9; anything else → 10.</summary>
    public static int Column(sbyte level) => level switch
    {
        >= 1 and <= 8 => level,
        >= 9 and <= 13 => 9,
        _ => 10,
    };

    /// <summary>
    /// 0x13B4C: among the cleric's enemies at any range (START 0x2FD3 range 0xFF), in the search's order, the first with
    /// combat record +0x10 (turned) = 0 and row 0x76 &gt; 0 with the lowest row (strictly lower than the best so far,
    /// starting at 13). Null when none.
    /// </summary>
    public static EclCharacter? NextTarget(Fight fight, EclCharacter cleric)
    {
        EclCharacter? found = null;
        sbyte best = RowLimit;
        foreach (var e in FightSearch.EnemiesNear(fight, cleric, 0xFF))
        {
            sbyte row = (sbyte)e[Row];
            if (fight.Record(e)[CombatRecord.Turned] == 0 && row > 0 && row < best)
            {
                best = row;
                found = e;
            }
        }
        return found;
    }

    /// <summary>
    /// 0x13965: message; +0x11 (tried) = 1; left = 6; count = 1d12, then roll = 1d20 (always both, first); then while a
    /// target is found, count &gt; 0 and nothing failed: v = table(row, column); roll &lt; |v| fails and stops; else
    /// v &gt; 0 turns it (+0x10 = 1), v ≤ 0 destroys it (off the map, status 8, in play 0); left − 1 when &gt; 0; count − 1;
    /// count back to 1 when it reached 0 with left &gt; 0 and v &lt; 0. Nothing affected: "nothing happens". Teams counted
    /// (START 0x2F7B), actions end (START 0x31D7). <paramref name="table"/> is needed only once a target is found.
    /// </summary>
    public static TurnUndeadResult Run(Fight fight, EclCharacter cleric, TurnUndeadTable? table,
        Action<FightNotice, EclCharacter?>? notice = null)
    {
        notice?.Invoke(FightNotice.TurnsUndead, cleric);
        bool affected = false, stop = false;
        fight.Record(cleric)[CombatRecord.TriedTurning] = 1;
        byte left = 6;
        byte count = FightRules.Dice(fight.Random, 1, 12);
        byte roll = FightRules.Dice(fight.Random, 1, 20);
        byte dice = count;
        int column = Column((sbyte)cleric[ClericLevel]);
        var turned = new List<EclCharacter>();
        var destroyed = new List<EclCharacter>();
        while (NextTarget(fight, cleric) is { } t && count > 0 && !stop)
        {
            if (table is null)
                throw new InvalidOperationException("turn undead found an undead target but has no turn-undead table (START.EXE)");
            sbyte v = table.Value((sbyte)t[Row], column);
            if (roll < Math.Abs((int)v))
            {
                stop = true;
                continue;
            }
            affected = true;
            if (v > 0)
            {
                fight.Record(t)[CombatRecord.Turned] = 1;
                turned.Add(t);
                notice?.Invoke(FightNotice.Turned, t);
            }
            else
            {
                notice?.Invoke(FightNotice.Destroyed, t);
                fight.Map?.Remove(t);
                t[FightRules.Status] = (byte)CreatureStatus.Gone;
                t[FightRules.InPlay] = 0;
                destroyed.Add(t);
            }
            if (left > 0)
                left--;
            count--;
            if (count == 0 && left > 0 && v < 0)
                count++;
        }
        if (!affected)
            notice?.Invoke(FightNotice.NothingHappens, null);
        FightRules.CountTeams(fight.List, fight.State);
        fight.EndActions(cleric);
        return new TurnUndeadResult(dice, roll, affected, turned, destroyed);
    }
}
