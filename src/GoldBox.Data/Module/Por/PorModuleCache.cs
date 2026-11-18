using System.Security.Cryptography;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// <c>--game DIR</c> plays through a module too (docs/module-format.md §17.4): the folder is imported once into
/// <c>&lt;cache&gt;/por-&lt;key&gt;</c>, where the key hashes the importer's name and every file of the folder (name and
/// bytes), so a changed game folder or a newer importer imports again. With an Amiga folder (<c>--amiga DIR</c>) the
/// overlay is imported too and the key also hashes the Amiga files the overlay reads (<see cref="PorAmigaArt.InputFiles"/>),
/// so a cache without the overlay (or with another one) is not reused. The import is written to a temporary folder and
/// renamed into place when complete. Old cache folders are left as they are (nothing is deleted).
/// </summary>
public static class PorModuleCache
{
    /// <summary>The cached module for <paramref name="gameDir"/> (plus the Amiga overlay from <paramref name="amigaDir"/>), importing it first when needed; returns its folder.</summary>
    public static string Ensure(string gameDir, string cacheRoot, string? amigaDir = null)
    {
        var target = Path.Combine(cacheRoot, "por-" + Key(gameDir, amigaDir));
        if (File.Exists(Path.Combine(target, ModuleManifest.FileName)))
            return target;
        Directory.CreateDirectory(cacheRoot);
        var temp = Path.Combine(cacheRoot, $".import-{Guid.NewGuid():N}");
        PorImporter.Import(gameDir, temp, amigaDir);
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

    /// <summary>The cache key of a game folder, and of the Amiga files when an Amiga folder is given (16 hex digits).</summary>
    public static string Key(string gameDir, string? amigaDir = null)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(System.Text.Encoding.UTF8.GetBytes(PorImporter.ImporterName + "\n"));
        foreach (var f in Directory.EnumerateFiles(gameDir).Order(StringComparer.Ordinal))
        {
            h.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(f).ToUpperInvariant() + "\n"));
            h.AppendData(File.ReadAllBytes(f));
        }
        if (amigaDir is not null)
        {
            h.AppendData("--amiga\n"u8);
            foreach (var f in PorAmigaArt.InputFiles(amigaDir))
            {
                h.AppendData(System.Text.Encoding.UTF8.GetBytes("amiga/" + Path.GetFileName(f).ToLowerInvariant() + "\n"));
                h.AppendData(File.ReadAllBytes(f));
            }
        }
        return Convert.ToHexStringLower(h.GetHashAndReset())[..16];
    }
}
