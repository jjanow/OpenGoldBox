using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GoldBox.Data.Module;

/// <summary>
/// The content hash an engine save records (docs/module-format.md §15): SHA-256 over the files that decide play,
/// the folders <c>scripts/</c>, <c>maps/</c>, <c>areas/</c>, <c>events/</c>, <c>records/</c> and <c>rules/</c> of every
/// module in the resolution chain, in chain order, each file as its relative path (lower case, <c>/</c>) and its bytes,
/// paths in ordinal order. Art, text, audio, saves and the manifest are left out, so an art overlay or a
/// re-import with the same game data keeps the hash. Computed once per module folder per process.
/// </summary>
public static class ModuleContentHash
{
    public static readonly string[] Folders = ["scripts", "maps", "areas", "events", "records", "rules"];

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Of(ModuleResolver modules)
    {
        if (modules.Chain.Count == 1)
            return Of(modules.Main.Root);
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var m in modules.Chain)
            h.AppendData(System.Text.Encoding.UTF8.GetBytes(Of(m.Root) + "\n"));
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }

    /// <summary>The hash of one module folder.</summary>
    public static string Of(string moduleRoot) =>
        Cache.GetOrAdd(Path.GetFullPath(moduleRoot), Compute);

    private static string Compute(string root)
    {
        var files = new List<(string Rel, string Path)>();
        foreach (var folder in Folders)
        {
            var d = Path.Combine(root, folder);
            if (Directory.Exists(d))
                foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                    files.Add((Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/').ToLowerInvariant(), f));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (rel, path) in files)
        {
            h.AppendData(System.Text.Encoding.UTF8.GetBytes(rel + "\n"));
            h.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexStringLower(h.GetHashAndReset());
    }
}
