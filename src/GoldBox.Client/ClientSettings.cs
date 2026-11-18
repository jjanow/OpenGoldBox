using System.Text.Json;
using GoldBox.Client.Input;

namespace GoldBox.Client;

/// <summary>
/// The player's settings: where their Pool of Radiance folder is, and the input bindings. Stored as JSON in
/// <c>$XDG_CONFIG_HOME/goldbox/</c> (default <c>~/.config/goldbox/</c>); nothing from the game is ever copied there.
/// </summary>
public sealed class ClientSettings
{
    public string? GameDirectory { get; set; }

    /// <summary>QoL, off = original: show the 320×200 screen stretched to 4:3 (pixels 1.2 times taller, as on a CRT).</summary>
    public bool Stretch43 { get; set; }

    public static string Folder
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return Path.Combine(root, "goldbox");
        }
    }

    /// <summary>
    /// Where the camp's Save writes and <c>--save X</c> looks first: <c>$XDG_DATA_HOME/goldbox/por-saves/</c> (default
    /// <c>~/.local/share/goldbox/por-saves/</c>). The files are the original's (<c>SAVGAMx.DAT</c>, <c>CHRDATxn.*</c>),
    /// so they can be copied into the DOS game's folder; the game folder itself is never written.
    /// </summary>
    public static string SavesFolder
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg;
            return Path.Combine(root, "goldbox", "por-saves");
        }
    }

    /// <summary>
    /// Where <c>--game DIR</c> is imported before play (<see cref="Data.Module.Por.PorModuleCache"/>):
    /// <c>$XDG_CACHE_HOME/goldbox/modules/</c> (default <c>~/.cache/goldbox/modules/</c>). The modules there hold the
    /// player's game data, for the player's own use; they are rebuilt from the game folder when it or the importer changes.
    /// </summary>
    public static string ModuleCacheFolder
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
                : xdg;
            return Path.Combine(root, "goldbox", "modules");
        }
    }

    private static string SettingsPath => Path.Combine(Folder, "client.json");
    private static string BindingsPath => Path.Combine(Folder, "bindings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath)) ?? new ClientSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"settings not read ({e.Message}); using defaults");
        }
        return new ClientSettings();
    }

    public void Save() => Write(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static InputBindings LoadBindings()
    {
        try
        {
            return InputBindings.FromJson(File.Exists(BindingsPath) ? File.ReadAllText(BindingsPath) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return InputBindings.Defaults();
        }
    }

    public static void SaveBindings(InputBindings b) => Write(BindingsPath, b.ToJson());

    private static void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"could not save {path}: {e.Message}");
        }
    }
}
