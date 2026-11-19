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

    /// <summary>
    /// The unpacked Amiga Pool of Radiance disks (optional; <c>--amiga DIR</c>, remembered): their art is imported with
    /// the game folder as the module's Amiga overlay (docs/module-format.md §10).
    /// </summary>
    public string? AmigaDirectory { get; set; }

    /// <summary>
    /// The art setting (PLAN §4.3), key <c>"Art"</c>: <c>"dos"</c> (Original DOS), <c>"best"</c> (default: the module's
    /// per-category mix, PoR: Amiga with DOS filling the gaps) or <c>"amiga"</c>. Changed with <c>--art</c> or the
    /// controls screen's ART row; an unknown word reads as Best.
    /// </summary>
    public string Art { get; set; } = "best";

    /// <summary>The art setting as a choice (Best for an unknown word).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Data.Por.PorArtChoice ArtChoice
    {
        get => Play.ClientArt.Parse(Art) ?? Data.Por.PorArtChoice.Best;
        set => Art = value switch
        {
            Data.Por.PorArtChoice.Dos => "dos",
            Data.Por.PorArtChoice.Amiga => "amiga",
            _ => "best",
        };
    }

    /// <summary>
    /// The sound (docs/re/sound.md), key <c>"Sound"</c>: <c>"speaker"</c> (default: the PC speaker, the installer's
    /// usual choice), <c>"tandy"</c> (the Tandy 1000's SN76489), <c>"amiga"</c> (with its imported samples), or <c>"off"</c>. Changed
    /// with <c>--sound</c> or the controls screen's SOUND row. Ctrl-S mutes or restores it for the session.
    /// </summary>
    public string Sound { get; set; } = "speaker";

    /// <summary>QoL, off = original: show the 320×200 screen stretched to 4:3 (pixels 1.2 times taller, as on a CRT).</summary>
    public bool Stretch43 { get; set; }

    /// <summary>
    /// QoL, off = original: after the party is destroyed, open the party menu again (a new menu, empty party, as at
    /// start-up). The original's game loop then returns and the program quits to DOS (docs/re/start-menu.md §8).
    /// Changed on the controls screen's AFTER DEFEAT row (QUIT / PARTY MENU, <see cref="SettingToggle.AfterDefeat"/>).
    /// </summary>
    public bool AfterDefeatMenu { get; set; }

    /// <summary>
    /// QoL, off = original: Confirm (or a click) ends a pause early, and keys pressed during a pause are dropped. The
    /// original's pauses cannot be cut short and a key pressed meanwhile waits for the next key read, which keeps only
    /// the last one (docs/re/view3d.md "Keys while the program is busy").
    /// </summary>
    public bool SkipPauses { get; set; }

    /// <summary>
    /// The word after <c>START</c> on the DOS command line (the program's ParamStr(1)), key <c>"ProgramWord"</c>; null =
    /// none, as the game is normally started. The training hall's J compares it with a word in the player's files
    /// (docs/re/training.md). Changed with <c>--param WORD</c> / <c>--no-param</c>.
    /// </summary>
    public string? ProgramWord { get; set; }

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
    /// <c>~/.local/share/goldbox/por-saves/</c>). The camp writes engine saves (<c>a.json</c> … <c>j.json</c>,
    /// docs/module-format.md §15); <c>goldbox-import save-export</c> turns one into the original files for the DOS game.
    /// Original <c>SAVGAMx.DAT</c> files there (from earlier Clients) still load. The game folder is never written.
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
                return FromJson(File.ReadAllText(SettingsPath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"settings not read ({e.Message}); using defaults");
        }
        return new ClientSettings();
    }

    public void Save() => Write(SettingsPath, ToJson());

    /// <summary>The settings file's text (indented JSON).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Settings from the file's text (keys missing in an older file keep their defaults).</summary>
    public static ClientSettings FromJson(string json) =>
        JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();

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
