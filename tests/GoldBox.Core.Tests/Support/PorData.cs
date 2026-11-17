using GoldBox.Data.Por;

namespace GoldBox.Core.Tests.Support;

/// <summary>
/// The player's Pool of Radiance (DOS v1.3) folder, from the environment variable <c>GOLDBOX_POR_DIR</c>. Tests that
/// read it carry <see cref="PorFactAttribute"/> and are skipped without it, so the public repo's tests pass with no
/// game files.
/// </summary>
public static class PorData
{
    public const string EnvVar = "GOLDBOX_POR_DIR";

    public static string? Dir { get; } = Resolve();

    public static bool Present => Dir is not null;

    public const string SkipReason = "needs the player's Pool of Radiance v1.3 files: set " + EnvVar;

    public static PorGameDirectory Open() => new(Dir ?? throw new InvalidOperationException(SkipReason));

    private static string? Resolve()
    {
        var d = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return null;
        return Directory.EnumerateFiles(d).Any(f => Path.GetFileName(f).Equals("ECL1.DAX", StringComparison.OrdinalIgnoreCase))
            ? d
            : null;
    }
}

/// <summary>A fact that needs the PoR files (<see cref="PorData"/>); skipped without them.</summary>
public sealed class PorFactAttribute : FactAttribute
{
    public PorFactAttribute()
    {
        if (!PorData.Present)
            Skip = PorData.SkipReason;
    }
}

/// <summary>A theory that needs the PoR files (<see cref="PorData"/>); skipped without them.</summary>
public sealed class PorTheoryAttribute : TheoryAttribute
{
    public PorTheoryAttribute()
    {
        if (!PorData.Present)
            Skip = PorData.SkipReason;
    }
}
