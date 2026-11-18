using GoldBox.Data.Por;

namespace GoldBox.Core.Tests.Support;

/// <summary>
/// The player's Secret of the Silver Blades (DOS v1.00–v1.30) folder, installed or unpacked from <c>SECRET1.EXE</c> and
/// <c>SECRET2.EXE</c>, from the environment variable <c>GOLDBOX_SOTSB_DIR</c>. Tests that read it carry
/// <see cref="SotsbFactAttribute"/> and are skipped without it.
/// </summary>
public static class SotsbData
{
    public const string EnvVar = "GOLDBOX_SOTSB_DIR";

    public static string? Dir { get; } = Resolve();

    public static bool Present => Dir is not null;

    public const string SkipReason = "needs the player's Secret of the Silver Blades files: set " + EnvVar;

    /// <summary>The folder; its <c>ECL1–3.DAX</c> are found the same way as PoR's.</summary>
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

/// <summary>A fact that needs the SotSB files (<see cref="SotsbData"/>); skipped without them.</summary>
public sealed class SotsbFactAttribute : FactAttribute
{
    public SotsbFactAttribute()
    {
        if (!SotsbData.Present)
            Skip = SotsbData.SkipReason;
    }
}
