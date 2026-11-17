namespace GoldBox.Core.Ecl;

/// <summary>
/// The game's random number generator as docs/re/ecl-format.md ("RANDOM") describes it: a 32-bit seed (RandSeed,
/// DS:4428 low word / DS:442A high word), advanced as seed = seed · 0x08088405 + 1, and Random(n) = (high word of
/// the new seed) mod n. Seeded from the DOS clock at start-up, so a run that must match a capture takes the seed
/// from the capture. Dice (routine 0x2BCCD) = count × (Random(sides) + 1).
/// </summary>
public sealed class TurboPascalRandom(uint seed)
{
    public const uint Multiplier = 0x08088405;

    /// <summary>RandSeed after the last draw.</summary>
    public uint Seed { get; set; } = seed;

    /// <summary>Number of draws since construction (each Random(n) is one draw).</summary>
    public long Draws { get; private set; }

    /// <summary>Advances the seed once and returns the new seed.</summary>
    public uint NextSeed()
    {
        Seed = unchecked(Seed * Multiplier + 1);
        Draws++;
        return Seed;
    }

    /// <summary>Random(n): 0..n−1. [?] n = 0 is never passed by the VM (RANDOM uses n ≥ 1); returns 0 without a draw.</summary>
    public ushort Next(ushort n)
    {
        if (n == 0)
            return 0;
        return (ushort)((NextSeed() >> 16) % n);
    }

    /// <summary>Advances the seed <paramref name="count"/> times (draws made by code outside the VM).</summary>
    public void Skip(int count)
    {
        for (int i = 0; i < count; i++)
            NextSeed();
    }

    /// <summary>Number of seed steps from <paramref name="from"/> to <paramref name="to"/>, or null if not within <paramref name="limit"/>.</summary>
    public static long? StepsBetween(uint from, uint to, long limit = 1_000_000)
    {
        uint s = from;
        for (long k = 0; k <= limit; k++)
        {
            if (s == to)
                return k;
            s = unchecked(s * Multiplier + 1);
        }
        return null;
    }
}
