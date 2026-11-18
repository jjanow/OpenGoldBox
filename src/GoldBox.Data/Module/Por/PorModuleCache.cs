using System.Security.Cryptography;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// <c>--game DIR</c> plays through a module too (docs/module-format.md §17.4): the folder is imported once into
/// <c>&lt;cache&gt;/por-&lt;key&gt;</c>, where the key hashes the importer's name and every file of the folder (name and
/// bytes), so a changed game folder or a newer importer imports again. The import is written to a temporary folder
/// and renamed into place when complete. Old cache folders are left as they are (nothing is deleted).
/// </summary>
public static class PorModuleCache
{
    /// <summary>The cached module for <paramref name="gameDir"/>, importing it first when needed; returns its folder.</summary>
    public static string Ensure(string gameDir, string cacheRoot)
    {
        var target = Path.Combine(cacheRoot, "por-" + Key(gameDir));
        if (File.Exists(Path.Combine(target, ModuleManifest.FileName)))
            return target;
        Directory.CreateDirectory(cacheRoot);
        var temp = Path.Combine(cacheRoot, $".import-{Guid.NewGuid():N}");
        PorImporter.Import(gameDir, temp);
        try
        {
            Directory.Move(temp, target);
        }
        catch (IOException) when (File.Exists(Path.Combine(target, ModuleManifest.FileName)))
        {
            // Another run imported the same folder meanwhile; its copy is used (the temporary one stays).
        }
        return target;
    }

    /// <summary>The cache key of a game folder (16 hex digits).</summary>
    public static string Key(string gameDir)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(System.Text.Encoding.UTF8.GetBytes(PorImporter.ImporterName + "\n"));
        foreach (var f in Directory.EnumerateFiles(gameDir).Order(StringComparer.Ordinal))
        {
            h.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(f).ToUpperInvariant() + "\n"));
            h.AppendData(File.ReadAllBytes(f));
        }
        return Convert.ToHexStringLower(h.GetHashAndReset())[..16];
    }
}
