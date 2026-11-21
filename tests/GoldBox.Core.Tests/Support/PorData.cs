using GoldBox.Data.Module.Por;
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

    /// <summary>
    /// A copy of <see cref="Dir"/> holding only the files of the v1.3 release (<see cref="IsReleaseFile"/>), made once
    /// per test run under <c>&lt;temp&gt;/goldbox-tests/</c>. Tests that pin totals of the whole folder (the importer's
    /// pictures, saves and engine saves; the round trip's per-file checks) read this, so a played folder, or one holding
    /// the test saves B–I of <c>tools/scripts/por_test_saves.py</c> (and the <c>FINAL2.DAX</c> it adds), gives the same
    /// counts. Not deleted by the tests (no deletes of computed paths); the temp folder's own cleanup removes it.
    /// </summary>
    public static string ReleaseDir => Release.Value.Dir;

    /// <summary>The files of <see cref="Dir"/> left out of <see cref="ReleaseDir"/>.</summary>
    public static IReadOnlyList<string> NotRelease => Release.Value.Left;

    public static PorGameDirectory OpenRelease() => new(ReleaseDir);

    /// <summary>
    /// The files of the v1.3 release (the WizardWorks 3.5" image the tests' totals come from), any case: the DAX
    /// files, the program, <c>ITEMS</c>, <c>POOL.CFG</c> and the shipped saves A and J with their party files.
    /// </summary>
    public static bool IsReleaseFile(string name) => ReleaseFiles.Contains(name);

    private static readonly HashSet<string> ReleaseFiles = ReleaseNames();

    private static HashSet<string> ReleaseNames()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BACPAC.DAX", "CBODY.DAX", "CHEAD.DAX", "COMSPR.DAX", "DUNGCOM.DAX", "FINAL5.DAX", "ICON.DAX", "RANDCOM.DAX",
            "SQRPACI.DAX", "TITLE.DAX", "WILDCOM.DAX", "GAME.OVR", "START.EXE", "ITEMS", "POOL.CFG",
            "SAVGAMA.DAT", "SAVGAMJ.DAT",
        };
        for (int d = 1; d <= 8; d++)
        {
            foreach (var family in new[] { "8X8D", "BODY", "CPIC", "ECL", "GEO", "HEAD", "ITEM", "PIC", "SPRIT", "WALLDEF" })
                s.Add($"{family}{d}.DAX");
            s.Add($"MON{d}CHA.DAX");
            s.Add($"MON{d}ITM.DAX");
            if (d is not (1 or 3))
                s.Add($"MON{d}SPC.DAX");
        }
        for (int i = 1; i <= 6; i++)
        {
            s.Add($"CHRDATA{i}.SAV");
            s.Add($"CHRDATA{i}.ITM");
            if (i is 1 or 2 or 3 or 6)
                s.Add($"CHRDATA{i}.SPC");
            foreach (var ext in new[] { "SAV", "ITM", "SPC" })
                s.Add($"CHRDATJ{i}.{ext}");
        }
        return s;
    }

    private static readonly Lazy<(string Dir, List<string> Left)> Release = new(() =>
    {
        var from = Dir ?? throw new InvalidOperationException(SkipReason);
        // One copy per content of the folder (reused by later runs, touched on use; TestImportCache.Prune removes
        // copies unused for a day): written to a temporary folder and renamed into place when complete.
        var root = Path.Combine(Path.GetTempPath(), "goldbox-tests");
        var to = Path.Combine(root, $"por-release-{GoldBox.Data.Module.Por.PorModuleCache.Key(from)}");
        var left = new List<string>();
        var release = new List<string>();
        foreach (var path in Directory.EnumerateFiles(from).Order(StringComparer.Ordinal))
            (IsReleaseFile(Path.GetFileName(path)) ? release : left).Add(path);
        if (Directory.Exists(to))
            TestImportCache.Touch(to);
        else
        {
            var temp = Path.Combine(root, $".release-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temp);
            foreach (var path in release)
                File.Copy(path, Path.Combine(temp, Path.GetFileName(path)));
            try
            {
                Directory.Move(temp, to);
            }
            catch (IOException) when (Directory.Exists(to))
            {
                // Another test process made the same copy meanwhile: its folder is used.
            }
        }
        return (to, left.Select(p => Path.GetFileName(p)).ToList());
    });

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

/// <summary>
/// The player's Amiga Pool of Radiance v1.0 disks, unpacked, from <c>GOLDBOX_POR_AMIGA_DIR</c>: <c>program</c> and
/// <c>intro</c> from disk A, the <c>.dax</c> files from disk B, found the way the importer finds them
/// (<see cref="PorAmigaArt.Locate"/>, <see cref="PorAmigaArt.LocateIntro"/>: any case, up to three folders deep). The
/// Amiga overlay tests also need <see cref="PorData"/>. A folder that lacks a file skips the tests that need it with a
/// reason naming the file and its disk (never a silent pass).
/// </summary>
public static class PorAmigaData
{
    public const string EnvVar = "GOLDBOX_POR_AMIGA_DIR";

    public static string? Dir { get; } =
        Environment.GetEnvironmentVariable(EnvVar) is { } d && !string.IsNullOrWhiteSpace(d) && Directory.Exists(d) ? d : null;

    /// <summary>Why <see cref="Dir"/> cannot be imported (no <c>program</c> or no <c>pic.dax</c>), or null.</summary>
    public static string? Missing { get; } = FindMissing();

    /// <summary>Disk A's <c>intro</c> in <see cref="Dir"/>, or null (the importer treats it as optional).</summary>
    public static string? Intro { get; } = Dir is null ? null : PorAmigaArt.LocateIntro(Dir);

    /// <summary>
    /// Disk A's rune font (<c>fonts/protect.font</c> and <c>fonts/protect/8</c>) in <see cref="Dir"/>, as the program
    /// names it, or null (the importer treats it as optional).
    /// </summary>
    public static string? RuneFont { get; } = Dir is null || Missing is not null ? null
        : PorAmigaArt.LocateRuneFont(Dir, File.ReadAllBytes(PorAmigaArt.Locate(Dir).Program))?.Path;

    /// <summary>The overlay can be imported: <see cref="Dir"/> holds <c>program</c> and the <c>.dax</c> files.</summary>
    public static bool Usable => Dir is not null && Missing is null;

    public static bool Present => Usable && PorData.Present;

    public static string SkipReason => Dir is not null && Missing is not null
        ? Missing
        : "needs the player's Pool of Radiance DOS v1.3 and Amiga v1.0 files: set " + PorData.EnvVar + " and " + EnvVar;

    public static string IntroSkipReason => Present
        ? $"needs the Amiga intro: {EnvVar} ({Dir}) has no 'intro' file; unpack it from disk A of Pool of Radiance " +
          "next to 'program' (the overlay imports without it, so only the intro tests skip)"
        : SkipReason;

    public static string RuneFontSkipReason => Present
        ? $"needs the Amiga rune font: {EnvVar} ({Dir}) has no 'fonts/protect.font' with 'fonts/protect/8'; copy disk A's " +
          "'fonts' folder of Pool of Radiance next to 'program' (the overlay imports without it, so only the rune font tests skip)"
        : SkipReason;

    private static string? FindMissing()
    {
        if (Dir is null)
            return null;
        try
        {
            PorAmigaArt.Locate(Dir);
            return null;
        }
        catch (FileNotFoundException e)
        {
            return $"{EnvVar} is set but incomplete: {e.Message}";
        }
    }
}

/// <summary>A fact that needs the DOS and Amiga PoR files; skipped without them.</summary>
public sealed class PorAmigaFactAttribute : FactAttribute
{
    public PorAmigaFactAttribute()
    {
        if (!PorAmigaData.Present)
            Skip = PorAmigaData.SkipReason;
    }
}

/// <summary>A fact that also needs disk A's rune font (<see cref="PorAmigaData.RuneFont"/>); skipped naming the files without it.</summary>
public sealed class PorAmigaRuneFontFactAttribute : FactAttribute
{
    public PorAmigaRuneFontFactAttribute()
    {
        if (!PorAmigaData.Present || PorAmigaData.RuneFont is null)
            Skip = PorAmigaData.RuneFontSkipReason;
    }
}

/// <summary>
/// A fact that also needs disk A's <c>intro</c> in the Amiga folder (<see cref="PorAmigaData.Intro"/>); skipped with a
/// reason naming the file without it.
/// </summary>
public sealed class PorAmigaIntroFactAttribute : FactAttribute
{
    public PorAmigaIntroFactAttribute()
    {
        if (!PorAmigaData.Present || PorAmigaData.Intro is null)
            Skip = PorAmigaData.IntroSkipReason;
    }
}
