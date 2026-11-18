using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoldBox.Data.Module;

/// <summary>JSON settings shared by every module file: camelCase names, indented, nulls left out.</summary>
public static class ModuleJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new FormatException($"{path}: empty JSON");

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n");
    }
}

/// <summary>A module this one layers on (§4 <c>dependsOn</c>).</summary>
public sealed record ModuleDependency
{
    public required string Id { get; init; }
    /// <summary>"1.2.0" (exact) or ">=1.2" (at least); null = any.</summary>
    public string? Version { get; init; }
}

public sealed record ModuleStart
{
    public int Area { get; init; }
    public int Map { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Facing { get; init; }
}

public sealed record ModuleArt
{
    public List<string> Variants { get; init; } = [];
    public Dictionary<string, string> Defaults { get; init; } = [];
}

public sealed record ModuleInput
{
    public required string File { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record ModuleSource
{
    public string? Importer { get; init; }
    public string? Game { get; init; }
    public string? Platform { get; init; }
    public string? Release { get; init; }
    public List<ModuleInput> Inputs { get; init; } = [];
}

/// <summary><c>module.json</c> (docs/module-format.md §4).</summary>
public sealed record ModuleManifest
{
    public const string FileName = "module.json";
    /// <summary>The format version this code writes; it reads any 0.x.</summary>
    public const string CurrentFormatVersion = "0.1";

    public required string FormatVersion { get; init; }
    public required string Id { get; init; }
    public string? Title { get; init; }
    public string? Version { get; init; }
    /// <summary><c>game</c>, <c>base</c>, <c>design</c> or <c>addon</c>.</summary>
    public string Kind { get; init; } = "game";
    public string? Rules { get; init; }
    public string? Vm { get; init; }
    /// <summary>[?] what the engine passes to the start-up script; not written by the PoR importer yet (§18).</summary>
    public ModuleStart? Start { get; init; }
    public List<ModuleDependency> DependsOn { get; init; } = [];
    public ModuleArt? Art { get; init; }
    public ModuleSource? Source { get; init; }
    public string? Credits { get; init; }
    public string? License { get; init; }

    public static ModuleManifest Load(string moduleRoot)
    {
        var path = Path.Combine(moduleRoot, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{moduleRoot} is not a module (no {FileName})", path);
        var m = ModuleJson.Read<ModuleManifest>(path);
        var major = MajorOf(m.FormatVersion);
        if (major > MajorOf(CurrentFormatVersion))
            throw new FormatException($"{path}: format {m.FormatVersion} is newer than this engine reads ({CurrentFormatVersion})");
        return m;
    }

    private static int MajorOf(string v) =>
        int.TryParse(v.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n : throw new FormatException($"bad formatVersion '{v}'");

    public void Save(string moduleRoot) => ModuleJson.Write(Path.Combine(moduleRoot, FileName), this);
}
