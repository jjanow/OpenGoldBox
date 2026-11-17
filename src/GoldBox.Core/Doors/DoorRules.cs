using GoldBox.Core.Ecl;

namespace GoldBox.Core.Doors;

/// <summary>Barrier value of a face as the engine reads it (plane 3, docs/re/geo-format.md "Barriers").</summary>
public enum DoorBarrier : byte
{
    Impassable = 0,
    Passable = 1,
    /// <summary>Locked door: Bash / Pick / Knock.</summary>
    Locked = 2,
    /// <summary>Harder door: Pick never opens it, Bash needs 18/91+, Knock needs a magic-user level.</summary>
    Barred = 3,
}

/// <summary>Door-menu choice.</summary>
public enum DoorAction
{
    Bash,
    Pick,
    Knock,
}

/// <summary>
/// The fields of a party member the door routines read (creature record offsets, docs/re/character-format.md).
/// </summary>
/// <param name="Strength">0x10.</param>
/// <param name="StrengthExceptional">0x16 (1..100 with Strength 18; 100 = 18/00).</param>
/// <param name="OpenLocks">0x78, thief skill in percent.</param>
/// <param name="Status">0x10C; Pick needs 0 (okay). Bash does not read it.</param>
/// <param name="MagicUserLevel">0x9B, signed.</param>
/// <param name="ThiefLevel">0x9C, signed.</param>
/// <param name="Memorised">The 21 spell slots at 0x17 (0 = empty, bit 7 = not yet memorised).</param>
public sealed record DoorMember(
    byte Strength,
    byte StrengthExceptional,
    byte OpenLocks,
    byte Status,
    sbyte MagicUserLevel,
    sbyte ThiefLevel,
    byte[] Memorised)
{
    public const int StrengthOffset = 0x10;
    public const int StrengthExceptionalOffset = 0x16;
    public const int SpellSlotsOffset = 0x17;
    public const int SpellSlots = 21;
    public const int OpenLocksOffset = 0x78;
    public const int MagicUserLevelOffset = 0x9B;
    public const int ThiefLevelOffset = 0x9C;
    public const int StatusOffset = 0x10C;

    /// <summary>Reads the fields from a 285-byte creature record.</summary>
    public static DoorMember FromRecord(ReadOnlySpan<byte> r) => new(
        r[StrengthOffset], r[StrengthExceptionalOffset], r[OpenLocksOffset], r[StatusOffset],
        (sbyte)r[MagicUserLevelOffset], (sbyte)r[ThiefLevelOffset],
        r.Slice(SpellSlotsOffset, SpellSlots).ToArray());

    public static DoorMember FromCharacter(EclCharacter c) => FromRecord(c.Record);

    /// <summary>
    /// First slot holding exactly spell <paramref name="spell"/> (GAME.OVR 0x16C76: slots 0..20 compared for
    /// equality, so a pending slot with bit 7 set does not count), or -1.
    /// </summary>
    public int SlotOf(byte spell) => Array.IndexOf(Memorised, spell);
}

/// <summary>The three door-menu flags DS:6DDE (Bash), DS:6DDF (Pick), DS:6DE0 (Knock).</summary>
public sealed class DoorMenuFlags
{
    public bool Bash { get; set; } = true;
    public bool Pick { get; set; } = true;
    public bool Knock { get; set; } = true;

    /// <summary>All three set to 1: after every completed step (0x16E74) and at start-up (0xE915).</summary>
    public void Reset() => Bash = Pick = Knock = true;
}

/// <summary>Result of one Bash / Pick / Knock choice.</summary>
/// <param name="Opened">The door opened: both faces become passable and the party steps through.</param>
/// <param name="Member">0-based party index of the member who opened it, or -1.</param>
/// <param name="Draws">Random draws made (each roll is one Random call).</param>
/// <param name="KnockSlot">Spell slot cleared by Knock, or -1.</param>
public readonly record struct DoorAttempt(bool Opened, int Member, int Draws, int KnockSlot = -1);

/// <summary>
/// The door menu's Bash / Pick / Knock routines of PoR DOS v1.3 (GAME.OVR 0x1695F, 0x16BAA, 0x16D13; menu
/// 0x17443–0x1771B), as docs/re/geo-format.md "Door menu" describes them. Pure functions of the party, the barrier
/// and the generator, so they replay a DOSBox capture from its RandSeed.
/// </summary>
public static class DoorRules
{
    /// <summary>Spell id of Knock (GAME.OVR 0x16D13 / 0x174DF pass 0x1F).</summary>
    public const byte KnockSpell = 31;

    /// <summary>The game's dice routine (0x2BCCD) with count 1: Random(sides) + 1, one draw.</summary>
    private static int Roll(TurboPascalRandom rng, ushort sides) => rng.Next(sides) + 1;

    /// <summary>Which choices the menu offers (Exit is always there).</summary>
    public static IReadOnlyList<DoorAction> MenuChoices(DoorBarrier barrier, IReadOnlyList<DoorMember> party, DoorMenuFlags flags)
    {
        var list = new List<DoorAction>(3);
        if (flags.Bash)
            list.Add(DoorAction.Bash);
        if (flags.Pick && party.Any(m => m.ThiefLevel > 0))
            list.Add(DoorAction.Pick);
        if (flags.Knock && (barrier == DoorBarrier.Barred
                ? party.Any(m => m.MagicUserLevel > 0)
                : party.Any(m => m.SlotOf(KnockSpell) >= 0)))
            list.Add(DoorAction.Knock);
        return list;
    }

    /// <summary>
    /// Bash (0x1695F): members in party order until one succeeds. Only members whose Strength is in the table roll;
    /// status is not checked. Barrier 3: a member below 18/91 clears <see cref="DoorMenuFlags.Bash"/>, and the loop
    /// still goes on to the next member. Any barrier other than 3 uses the barrier-2 table.
    /// </summary>
    public static DoorAttempt Bash(DoorBarrier barrier, IReadOnlyList<DoorMember> party, TurboPascalRandom rng, DoorMenuFlags flags)
    {
        long start = rng.Draws;
        for (int i = 0; i < party.Count; i++)
        {
            bool ok = barrier == DoorBarrier.Barred
                ? BashBarred(party[i], rng, flags)
                : BashLocked(party[i], rng);
            if (ok)
                return new DoorAttempt(true, i, (int)(rng.Draws - start));
        }
        return new DoorAttempt(false, -1, (int)(rng.Draws - start));
    }

    // Barrier 2 (0x16A78–0x16B2F). Strength outside 3..18 (and 18 with exceptional > 100): no roll, no success.
    private static bool BashLocked(DoorMember m, TurboPascalRandom rng)
    {
        int s = m.Strength;
        if (s is >= 3 and <= 7)
            return Roll(rng, 6) == 1;
        if (s is >= 8 and <= 15)
            return Roll(rng, 6) <= 2;
        if (s is 16 or 17)
            return Roll(rng, 6) <= 3;
        if (s != 18)
            return false;
        int e = m.StrengthExceptional;
        if (e <= 50)
            return Roll(rng, 6) <= 3;
        if (e <= 99)
            return Roll(rng, 6) <= 4;
        if (e == 100)
            return Roll(rng, 6) <= 5;
        return false;
    }

    // Barrier 3 (0x169AB–0x16A70).
    private static bool BashBarred(DoorMember m, TurboPascalRandom rng, DoorMenuFlags flags)
    {
        switch (m.Strength)
        {
            case 18:
                if (m.StrengthExceptional is >= 91 and <= 99)
                    return Roll(rng, 6) == 1;
                if (m.StrengthExceptional == 100)
                    return Roll(rng, 6) <= 2;
                flags.Bash = false;
                return false;
            case 19 or 20:
                return Roll(rng, 6) <= 3;
            case 21 or 22:
                return Roll(rng, 6) <= 4;
            case 23:
                return Roll(rng, 6) <= 5;
            case 24:
                return Roll(rng, 8) <= 7;
            case 25:
                return true;
            default:
                flags.Bash = false;
                return false;
        }
    }

    /// <summary>
    /// Pick. Barrier 2 (0x16BAA): every member in order rolls 1d100 until one succeeds (roll ≤ open locks and
    /// status 0), whatever the class; then <see cref="DoorMenuFlags.Pick"/> is cleared. Barrier 3 (0x176E0): no
    /// roll, the flag is cleared, the door stays shut.
    /// </summary>
    public static DoorAttempt Pick(DoorBarrier barrier, IReadOnlyList<DoorMember> party, TurboPascalRandom rng, DoorMenuFlags flags)
    {
        flags.Pick = false;
        if (barrier == DoorBarrier.Barred)
            return new DoorAttempt(false, -1, 0);
        long start = rng.Draws;
        for (int i = 0; i < party.Count; i++)
        {
            int r = Roll(rng, 100);
            if (r <= party[i].OpenLocks && party[i].Status == 0)
                return new DoorAttempt(true, i, (int)(rng.Draws - start));
        }
        return new DoorAttempt(false, -1, (int)(rng.Draws - start));
    }

    /// <summary>
    /// Knock (0x16D13, both barriers): the first member with spell 31 memorised loses that slot (set to 0) and the
    /// door opens; without one nothing happens. No draw. The caller clears the slot.
    /// </summary>
    public static DoorAttempt Knock(IReadOnlyList<DoorMember> party)
    {
        for (int i = 0; i < party.Count; i++)
        {
            int slot = party[i].SlotOf(KnockSpell);
            if (slot >= 0)
                return new DoorAttempt(true, i, 0, slot);
        }
        return new DoorAttempt(false, -1, 0);
    }
}
