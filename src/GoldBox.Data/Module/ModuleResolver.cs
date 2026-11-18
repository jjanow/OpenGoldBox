using System.Globalization;

namespace GoldBox.Data.Module;

/// <summary>One module on disk: a folder with <c>module.json</c>. [?] the zip form (§3) is not read yet.</summary>
public sealed class ModuleDirectory
{
    public string Root { get; }
    public ModuleManifest Manifest { get; }

    public ModuleDirectory(string root)
    {
        Root = Path.GetFullPath(root);
        Manifest = ModuleManifest.Load(Root);
    }

    /// <summary>Full path of a file by its module-relative path (forward slashes), or null when absent.</summary>
    public string? File(string relativePath)
    {
        var p = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return System.IO.File.Exists(p) ? p : null;
    }

    public string? Find(ResourceKey key, string ext) => File(key.RelativePath(ext));

    public override string ToString() => $"{Manifest.Id} ({Root})";
}

/// <summary>
/// A module plus its dependencies (docs/module-format.md §1.4, §4): a key is looked up in the module itself first,
/// then in each <c>dependsOn</c> entry in list order (each dependency with its own dependencies after it, depth
/// first; a module met twice is searched once). Dependencies are found by id in the search folders: a folder whose
/// <c>module.json</c> has that id (by default the module's parent folder, so sibling modules depend on each other).
/// </summary>
public sealed class ModuleResolver
{
    /// <summary>The modules in lookup order; [0] is the module that was opened.</summary>
    public IReadOnlyList<ModuleDirectory> Chain { get; }

    public ModuleDirectory Main => Chain[0];

    private ModuleResolver(IReadOnlyList<ModuleDirectory> chain) => Chain = chain;

    public static ModuleResolver Open(string moduleRoot, IEnumerable<string>? searchFolders = null)
    {
        var main = new ModuleDirectory(moduleRoot);
        var folders = (searchFolders ?? [Path.GetDirectoryName(main.Root)!]).ToList();
        var chain = new List<ModuleDirectory>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Add(main, folders, chain, seen, []);
        return new ModuleResolver(chain);
    }

    private static void Add(ModuleDirectory m, List<string> folders, List<ModuleDirectory> chain, HashSet<string> seen,
        HashSet<string> path)
    {
        if (!seen.Add(m.Manifest.Id))
            return;
        chain.Add(m);
        path.Add(m.Manifest.Id);
        foreach (var dep in m.Manifest.DependsOn)
        {
            if (path.Contains(dep.Id))
                throw new FormatException($"module {m.Manifest.Id}: dependency cycle through {dep.Id}");
            var d = FindModule(dep, folders)
                ?? throw new FileNotFoundException($"module {m.Manifest.Id} depends on {dep.Id} {dep.Version}, which is not in {string.Join(", ", folders)}");
            Add(d, folders, chain, seen, path);
        }
        path.Remove(m.Manifest.Id);
    }

    private static ModuleDirectory? FindModule(ModuleDependency dep, List<string> folders)
    {
        foreach (var folder in folders.Where(Directory.Exists))
            foreach (var sub in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
            {
                if (!System.IO.File.Exists(Path.Combine(sub, ModuleManifest.FileName)))
                    continue;
                ModuleDirectory m;
                try
                {
                    m = new ModuleDirectory(sub);
                }
                catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or IOException)
                {
                    continue;
                }
                if (m.Manifest.Id == dep.Id && VersionMatches(m.Manifest.Version, dep.Version))
                    return m;
            }
        return null;
    }

    /// <summary>"x.y.z" exact, "&gt;=x.y" at least, null or "*" any. Missing parts count as 0.</summary>
    public static bool VersionMatches(string? have, string? want)
    {
        if (string.IsNullOrWhiteSpace(want) || want == "*")
            return true;
        if (have is null)
            return false;
        if (want.StartsWith(">=", StringComparison.Ordinal))
            return Compare(have, want[2..].Trim()) >= 0;
        return Compare(have, want.Trim()) == 0;
    }

    private static int Compare(string a, string b)
    {
        static int[] Parts(string v) => [.. v.Split('.').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)];
        var x = Parts(a);
        var y = Parts(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0)
                return c;
        }
        return 0;
    }

    /// <summary>The first module in <see cref="Chain"/> that has the resource file, as (module, full path); or null.</summary>
    public (ModuleDirectory Module, string Path)? Find(ResourceKey key, string ext)
    {
        foreach (var m in Chain)
            if (m.Find(key, ext) is { } p)
                return (m, p);
        return null;
    }

    /// <summary>A file by module-relative path (e.g. <c>records/itemTypes.json</c>), first module that has it.</summary>
    public string? File(string relativePath)
    {
        foreach (var m in Chain)
            if (m.File(relativePath) is { } p)
                return p;
        return null;
    }
}
