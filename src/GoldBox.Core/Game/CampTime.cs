using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;

namespace GoldBox.Core.Game;

// The camp's time keeping in Pool of Radiance DOS v1.3 (docs/re/camp.md "Rest"): the rest time DS:6DC2, the clock
// advance with the effect timeout (GAME.OVR 0x241A1 → 0x23DCC), the rest-time carry 0x2416F and borrow 0x24246.
// These are data-segment variables of the program, not VM cells: DS:6DC2–6DCF, DS:49FB and DS:6DD8 overlap VM cells
// that mean other things ($6DCA Search, $49FB no area map), so they live here and never in EclMemory.

/// <summary>
/// The program's camp and rest variables (data segment, not VM cells). One per game session: DS:49FB survives from
/// one camp to the next (nothing but the rest loop writes it), the others are cleared where the program clears them.
/// </summary>
public sealed class CampState
{
    /// <summary>
    /// DS:6DC2: the rest time, seven words shaped like the game clock (index 1 minutes, 2 ten minutes, 3 hours, 4 days,
    /// 5 months, 6 years). Cleared on camp entry (0x196A0) and after each rest (0x17FAB).
    /// </summary>
    public ushort[] RestTime { get; } = new ushort[GameClock.Cells];

    /// <summary>DS:6DD8: rest ticks since the last whole-party heal (0x2463D); cleared on camp entry.</summary>
    public ushort HealTicks { get; set; }

    /// <summary>DS:49FB: rest ticks since the last interruption roll (0x24BA7); never cleared on camp entry.</summary>
    public ushort IntervalTicks { get; set; }

    /// <summary>
    /// DS:471E: one byte per creature-list position 1..72, has timed effects left (0x23DCC). Index 0 here is
    /// position 1.
    /// </summary>
    public byte[] EffectFlags { get; } = new byte[72];

    /// <summary>DS:6DD0: the spell preparation timer of party position 1..8 (0x248D6); index 0 = position 1.</summary>
    public byte[] PrepTimers { get; } = new byte[8];

    /// <summary>DS:49F3 = 2 (camp): the effect timeout ages only flagged creatures.</summary>
    public bool Camping { get; set; }

    /// <summary>True when the rest time is zero in the days, hours, ten-minute and minute words (0x24AFE).</summary>
    public bool RestTimeLeft => RestTime[CampTime.Days] > 0 || RestTime[CampTime.Hours] > 0 ||
                                RestTime[CampTime.TenMinutes] > 0 || RestTime[CampTime.Minutes] > 0;
}

/// <summary>The rest-time arithmetic and the clock advance with effect timeouts (GAME.OVR 0x23DCC–0x24331).</summary>
public static class CampTime
{
    public const int Minutes = GameClock.Minutes, TenMinutes = GameClock.TenMinutes, Hours = GameClock.Hours,
        Days = GameClock.Days, Months = GameClock.Months;

    /// <summary>
    /// 0x2416F: the rest time carried (0x240BE, the game clock's carry; a year overflow ages the party, as for the
    /// clock); then any months fold into days, 30 each (DS:363A[4]), months := 0, days capped at 99.
    /// </summary>
    public static void Validate(CampState s, EclParty? party)
    {
        var t = s.RestTime;
        GameClock.Normalise(t, party);
        if (t[Months] > 0)
        {
            t[Days] = (ushort)(t[Days] + GameClock.Limits[Days] * t[Months]);
            t[Months] = 0;
            if (t[Days] > 99)
                t[Days] = 99;
        }
    }

    /// <summary>
    /// 0x24246: takes <paramref name="count"/> units of <paramref name="unit"/> off the rest time. Nothing when days,
    /// hours, ten minutes and minutes are all zero. While count exceeds the unit's word, the next non-zero word above
    /// it (up to days) lends one, carried down word by word with each word below gaining its limit; when none can lend
    /// (the search reaches months), the whole rest time is cleared and count becomes 0. Then the unit's word loses count
    /// and the carry (<see cref="Validate"/>) runs.
    /// </summary>
    public static void Decrement(CampState s, int count, int unit, EclParty? party)
    {
        var t = s.RestTime;
        if (!s.RestTimeLeft)
            return;
        count &= 0xFF;
        while (count > t[unit])
        {
            int j = unit + 1;
            while (t[j] == 0 && j < Months)
                j++;
            if (j == Months)
            {
                Array.Clear(t);
                count = 0;
            }
            else
            {
                for (int k = j; k >= unit + 1; k--)
                {
                    t[k]--;
                    t[k - 1] = (ushort)(t[k - 1] + GameClock.Limits[k - 1]);
                }
            }
        }
        t[unit] = (ushort)(t[unit] - count);
        Validate(s, party);
    }

    /// <summary>
    /// 0x241A1: the game clock advanced (<see cref="GameClock.Advance"/>), then the party's timed effects aged by the
    /// same duration (<see cref="TimeOutEffects"/>).
    /// </summary>
    public static void Advance(EclMemory memory, CampState s, int unit, int count, Action<EclCharacter, byte[]> removeEffect)
    {
        GameClock.Advance(memory, unit, count);
        TimeOutEffects(memory, s, count, unit, removeEffect);
    }

    /// <summary>
    /// 0x23DCC: ages the party's timed effects (record word +1, minutes) by <paramref name="count"/> units of
    /// <paramref name="unit"/>. Outside camp (DS:49F3 ≠ 2) every flag DS:471E is set first; in camp nothing happens
    /// unless a flag of positions 1..$6E3E is set. The duration in minutes (count × the clock limits below the unit,
    /// 16-bit) goes in steps of at most 10. Per step, each flagged creature (flag cleared first) walks its effect list
    /// up to the record that was last when the step began: a record with 0 minutes is skipped; one with more minutes
    /// than the step loses the step and sets the flag; any other is removed (0x2AF10 through
    /// <paramref name="removeEffect"/>). After a removal the walk goes on at the next record, except when the record
    /// before the removed one was the head: then it starts again at the head, which ages a second time in that step.
    /// The records after the walk set the flag when they have minutes left.
    /// </summary>
    public static void TimeOutEffects(EclMemory memory, CampState s, int count, int unit, Action<EclCharacter, byte[]> removeEffect)
    {
        var party = memory.Party?.Members ?? [];
        var flags = s.EffectFlags;
        if (!s.Camping)
            Array.Fill(flags, (byte)1);
        else
        {
            bool found = false;
            int size = memory.Read(EclPartyRules.PartySizeCell);
            int i = 1;
            do
            {
                if (i <= flags.Length && flags[i - 1] != 0)
                    found = true;
                i++;
            } while (!found && i <= size);
            if (!found)
                return;
        }
        ushort total = (ushort)(count & 0xFF);
        for (int u = unit; u > 1; u--)
            total = unchecked((ushort)(GameClock.Limits[u - 1] * total));
        while (total > 0)
        {
            ushort step = total > 10 ? (ushort)10 : total;
            for (int pos = 1; pos <= party.Count && pos <= flags.Length; pos++)
            {
                if (flags[pos - 1] == 0)
                    continue;
                flags[pos - 1] = 0;
                AgeOne(party[pos - 1], step, () => flags[pos - 1] = 1, removeEffect);
            }
            total = total > 10 ? (ushort)(total - 10) : (ushort)0;
        }
    }

    private static ushort MinutesLeft(byte[] e) => (ushort)(e[1] | e[2] << 8);

    private static void AgeOne(EclCharacter c, ushort step, Action flag, Action<EclCharacter, byte[]> removeEffect)
    {
        var list = c.Effects;
        if (list.Count == 0)
            return;
        byte[]? Next(byte[] e)
        {
            int i = list.FindIndex(x => ReferenceEquals(x, e));
            return i >= 0 && i + 1 < list.Count ? list[i + 1] : null;
        }
        byte[]? Head() => list.Count > 0 ? list[0] : null;

        byte[]? e = Head();
        byte[] tail = list[^1];
        byte[]? prev = Head();
        bool done = false;
        while (e is not null && !done)
        {
            if (ReferenceEquals(e, tail))
                done = true;
            ushort m = MinutesLeft(e);
            if (m == 0)
                e = Next(e);
            else if (step < m)
            {
                m -= step;
                e[1] = (byte)m;
                e[2] = (byte)(m >> 8);
                flag();
                e = Next(e);
            }
            else
            {
                var nxt = Next(e);
                removeEffect(c, e);
                e = ReferenceEquals(prev, Head()) ? Head() : nxt;
            }
            // The record before e (or e itself when it is the head, or the head when e is its successor).
            prev = Head();
            while (prev is not null && !ReferenceEquals(prev, e) && !ReferenceEquals(Next(prev), e))
                prev = Next(prev);
        }
        while (e is not null)
        {
            if (MinutesLeft(e) > 0)
                flag();
            e = Next(e);
        }
    }
}
