using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;

namespace GoldBox.Core.Tests.Support;

/// <summary>
/// Imports reused across test runs, so runs do not each leave a new module folder under <c>&lt;temp&gt;/goldbox-tests/</c>
/// (s29: ~1,200 left-behind imports filled the tmpfs's inodes). A folder is named by
/// <see cref="PorModuleCache.Key"/> (importer name, game files, Amiga files) plus a hash of the built
/// <c>GoldBox.Data</c> assembly, so any change to the importer's code imports again. The import report is kept next to
/// the folder as <c>&lt;folder&gt;.report.json</c>. A reused folder is touched, and <see cref="Prune"/> (run once when
/// the test assembly loads) removes what no run has used for <see cref="KeepFor"/> (user decision s29; three hours since s42).
/// </summary>
public static class TestImportCache
{
    public static string Root => Path.Combine(Path.GetTempPath(), "goldbox-tests");

    private static readonly Lazy<string> AssemblyKey = new(() =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(PorImporter).Assembly.Location)))[..12]);

    /// <summary>A key for this build of the importer: test caches use it in their folder names.</summary>
    public static string BuildKey => AssemblyKey.Value;

    private static readonly JsonSerializerOptions Json = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        WriteIndented = true,
    };

    /// <summary>The module of <paramref name="gameDir"/> (with the Amiga overlay when given), imported once per build.</summary>
    public static (string Dir, PorImportReport Report) Import(string name, string gameDir, string? amigaDir = null)
    {
        var dir = Path.Combine(Root, $"{name}-{PorModuleCache.Key(gameDir, amigaDir)}-{BuildKey}");
        var reportFile = dir + ".report.json";
        if (File.Exists(Path.Combine(dir, ModuleManifest.FileName)) && File.Exists(reportFile)
            && JsonSerializer.Deserialize<PorImportReport>(File.ReadAllText(reportFile), Json) is { } cached)
        {
            Touch(dir);
            Touch(reportFile);
            return (dir, cached);
        }

        Directory.CreateDirectory(Root);
        var temp = Path.Combine(Root, $".import-{Guid.NewGuid():N}");
        var report = PorImporter.Import(gameDir, temp, amigaDir);
        try
        {
            Directory.Move(temp, dir);
        }
        catch (IOException) when (File.Exists(Path.Combine(dir, ModuleManifest.FileName)))
        {
            // Another test process imported the same build meanwhile: its folder is used (ours stays behind).
        }
        var tempReport = $"{reportFile}.{Guid.NewGuid():N}";
        File.WriteAllText(tempReport, JsonSerializer.Serialize(report, Json));
        File.Move(tempReport, reportFile, overwrite: true);
        return (dir, report);
    }

    /// <summary>Marks a reused folder or file as used now, so <see cref="Prune"/> keeps it.</summary>
    public static void Touch(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            else if (File.Exists(path))
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// How long an unused folder is kept. Three hours since s42 (was a day): every rebuild of <c>GoldBox.Data</c> imports
    /// again (~4,300 inodes for an Amiga import), and with several lanes rebuilding all day the old builds filled 70 % of
    /// the tmpfs's inodes. A run touches what it uses, so a lane's current build stays.
    /// </summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(3);

    /// <summary>
    /// Removes the test folders under <see cref="Root"/> that no run has used for <see cref="KeepFor"/>: entries made by
    /// these tests only (the name patterns below), directly in <see cref="Root"/> or in the Client tests' two cache
    /// folders; never a link. Reused folders are touched on use, so a run of another build or worktree in progress keeps
    /// its own. Errors are ignored (another process may be pruning too).
    /// </summary>
    public static void Prune()
    {
        if (!Directory.Exists(Root))
            return;
        var cutoff = DateTime.UtcNow - KeepFor;
        PruneIn(Root, IsTestEntry, cutoff);
        foreach (var cache in new[] { "client-module-cache", "client-amiga-cache" })
        {
            var dir = Path.Combine(Root, cache);
            if (Directory.Exists(dir) && !new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                PruneIn(dir, n => n.StartsWith("por-", StringComparison.Ordinal) || n.StartsWith(".import-", StringComparison.Ordinal), cutoff);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex TestEntry = new(
        @"^(\.import-|\.release-|test-|rules-|por-module-|por-module-again-|por-amiga-module-|por-release-)[0-9a-f-]+(\.report\.json(\.[0-9a-f]+)?)?$");

    private static bool IsTestEntry(string name) => TestEntry.IsMatch(name);

    private static void PruneIn(string parent, Func<string, bool> ours, DateTime cutoff)
    {
        foreach (var info in new DirectoryInfo(parent).EnumerateFileSystemInfos())
        {
            if (!ours(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LastWriteTimeUtc >= cutoff)
                continue;
            try
            {
                if (info is DirectoryInfo d)
                    d.Delete(recursive: true);
                else
                    info.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>Prunes the old test folders once, when the test assembly loads (<see cref="TestImportCache.Prune"/>).</summary>
internal static class TestFolderPruning
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Run() => TestImportCache.Prune();
}
