using System.Globalization;
using GoldBox.Core.Explore;

namespace GoldBox.Client;

/// <summary>
/// Command line of the Client:
/// <code>
///   GoldBox.Client [--game DIR] [--amiga DIR] [--module DIR] [--art dos|best|amiga] [--save A|FILE] [--area N --at X,Y,F] [--scale K]
/// </code>
/// <c>--game</c> is the player's Pool of Radiance (DOS) folder; it is remembered in the settings, so it is needed once
/// (the environment variable <c>GOLDBOX_POR_DIR</c> also works). The folder is imported into a module in the cache
/// folder first (<see cref="ClientSettings.ModuleCacheFolder"/>, again only when the folder changes) and the game runs
/// from that module. <c>--module DIR</c> plays a module made by <c>goldbox-import por</c> (docs/module-format.md)
/// instead, with no game folder. Without <c>--save</c> or <c>--area</c> the Client
/// starts at the party menu, the original's first screen (create, add and load characters, load a saved game, begin a
/// new game; docs/re/start-menu.md). <c>--save A</c> loads save A (<see cref="ResolveSave"/>: the player's
/// <c>a.json</c>, else the module's shipped save A). <c>--area N --at X,Y,F</c> enters area script N at square X,Y facing F (N, E, S,
/// W) with no save. <c>--scale</c> sets the window size (default 3 × 320×200). <c>--stretch</c> switches on the QoL 4:3
/// display (remembered; <c>--no-stretch</c> switches it off). <c>--amiga DIR</c> names the unpacked Amiga disks (remembered;
/// the environment variable <c>GOLDBOX_POR_AMIGA_DIR</c> also works): their art is imported with the game folder as the
/// module's Amiga overlay (the cache key covers the Amiga files). <c>--art dos|best|amiga</c> sets the art (remembered;
/// also on the controls screen): Original DOS, Best (default: the module's mix, PoR: Amiga with DOS filling the gaps) or
/// Amiga wherever the module has it.
/// </summary>
public sealed record ClientOptions
{
    public string? GameDirectory { get; init; }
    /// <summary>--module DIR: an imported module (not remembered).</summary>
    public string? ModuleDirectory { get; init; }
    public string? Save { get; init; }
    public int? Area { get; init; }
    public (int X, int Y, int Facing)? At { get; init; }
    public int Scale { get; init; } = 3;
    /// <summary>--stretch / --no-stretch: 4:3 display (remembered).</summary>
    public bool? Stretch43 { get; init; }
    /// <summary>--amiga DIR: the unpacked Amiga disks for the overlay (remembered).</summary>
    public string? AmigaDirectory { get; init; }
    /// <summary>--art dos|best|amiga (remembered).</summary>
    public Data.Por.PorArtChoice? Art { get; init; }
    /// <summary>--sound speaker|tandy|off (remembered).</summary>
    public string? Sound { get; init; }

    public const string Usage = "usage: GoldBox.Client [--game DIR] [--amiga DIR] [--module DIR] [--art dos|best|amiga] [--save A|FILE] [--area N --at X,Y,F] [--scale K] [--stretch|--no-stretch] [--sound speaker|tandy|off]";

    public static ClientOptions Parse(IReadOnlyList<string> args)
    {
        var o = new ClientOptions();
        for (int i = 0; i < args.Count; i++)
        {
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            o = args[i] switch
            {
                "--game" => o with { GameDirectory = Next() },
                "--module" => o with { ModuleDirectory = Next() },
                "--save" => o with { Save = Next() },
                "--area" => o with { Area = int.Parse(Next(), CultureInfo.InvariantCulture) },
                "--at" => o with { At = ParseAt(Next()) },
                "--scale" => o with { Scale = int.Parse(Next(), CultureInfo.InvariantCulture) },
                "--stretch" => o with { Stretch43 = true },
                "--no-stretch" => o with { Stretch43 = false },
                "--amiga" => o with { AmigaDirectory = Next() },
                "--sound" => Next() is var sw && (Audio.ClientAudio.Parse(sw) is not null || sw.Equals("off", StringComparison.OrdinalIgnoreCase))
                    ? o with { Sound = sw.ToLowerInvariant() }
                    : throw new ArgumentException($"--sound {sw}: use speaker, tandy or off"),
                "--art" => Next() is var w && Play.ClientArt.Parse(w) is { } art
                    ? o with { Art = art }
                    : throw new ArgumentException($"--art {w}: use dos, best or amiga"),
                _ => throw new ArgumentException($"unknown argument {args[i]}"),
            };
        }
        if (o.Area is not null && o.At is null)
            throw new ArgumentException("--area needs --at X,Y,F");
        if (o.Area is not null && o.Save is not null)
            throw new ArgumentException("--area and --save exclude each other");
        return o;
    }

    private static (int, int, int) ParseAt(string s)
    {
        var p = s.Split(',');
        if (p.Length != 3)
            throw new ArgumentException("--at wants X,Y,F (e.g. 15,4,W)");
        int x = int.Parse(p[0], CultureInfo.InvariantCulture), y = int.Parse(p[1], CultureInfo.InvariantCulture);
        int f = p[2].ToUpperInvariant() switch
        {
            "N" => Facing.North,
            "E" => Facing.East,
            "S" => Facing.South,
            "W" => Facing.West,
            _ => throw new ArgumentException($"facing {p[2]}: use N, E, S or W"),
        };
        if (x is < 0 or > 15 || y is < 0 or > 15)
            throw new ArgumentException("squares are 0..15");
        return (x, y, f);
    }

    /// <summary>The save file a --save value names: a single letter is SAVGAM&lt;letter&gt;.DAT in the game folder.</summary>
    public static string SaveFileName(string save) =>
        save.Length == 1 && char.IsLetter(save[0]) ? $"SAVGAM{char.ToUpperInvariant(save[0])}.DAT" : save;

    /// <summary>
    /// The file a <c>--save</c> value loads. A slot letter X is looked up in this order: the player's engine save
    /// <c>x.json</c> in <paramref name="ownFolder"/>, an original <c>SAVGAMX.DAT</c> there (written by earlier Clients;
    /// converted on load), the module's engine save <c>saves/x.json</c>, the module's original <c>saves/SAVGAMX.DAT</c>.
    /// Any other value is a file name looked up in the same two folders, then a path. A <c>.json</c> file is an engine
    /// save, anything else an original <c>SAVGAMx.DAT</c>. Null when nothing is found.
    /// </summary>
    public static string? ResolveSave(string save, string ownFolder, string? moduleSaves)
    {
        static string? In(string? folder, string name) =>
            folder is not null && Directory.Exists(folder) && new Data.Por.PorGameDirectory(folder).Find(name) is { } p ? p : null;
        if (save.Length == 1 && char.IsLetter(save[0]))
        {
            string json = Data.Module.Por.PorEngineSave.FileName(save[0]), dat = SaveFileName(save);
            return In(ownFolder, json) ?? In(ownFolder, dat) ?? In(moduleSaves, json) ?? In(moduleSaves, dat);
        }
        if (Path.GetFileName(save) == save && (In(ownFolder, save) ?? In(moduleSaves, save)) is { } named)
            return named;
        return File.Exists(save) ? save : null;
    }

    /// <summary>The camp's default slot for a --save value: the letter, or the letter in a <c>SAVGAMx.DAT</c> / <c>x.json</c> name; else A.</summary>
    public static char SlotOf(string save)
    {
        var name = Path.GetFileName(save).ToUpperInvariant();
        if (name.Length == 1 && char.IsLetter(name[0]))
            return name[0];
        if (name.Length == 11 && name.StartsWith("SAVGAM", StringComparison.Ordinal) && name.EndsWith(".DAT", StringComparison.Ordinal))
            return name[6];
        if (name.Length == 6 && name.EndsWith(".JSON", StringComparison.Ordinal) && name[0] is >= 'A' and <= 'J')
            return name[0];
        return 'A';
    }
}
