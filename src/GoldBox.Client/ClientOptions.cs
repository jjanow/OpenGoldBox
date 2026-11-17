using System.Globalization;
using GoldBox.Core.Explore;

namespace GoldBox.Client;

/// <summary>
/// Command line of the Client:
/// <code>
///   GoldBox.Client [--game DIR] [--save A|FILE] [--area N --at X,Y,F] [--scale K]
/// </code>
/// <c>--game</c> is the player's Pool of Radiance (DOS) folder; it is remembered in the settings, so it is needed once
/// (the environment variable <c>GOLDBOX_POR_DIR</c> also works). Without <c>--save</c> or <c>--area</c> the Client
/// loads save A (<c>SAVGAMA.DAT</c>). <c>--area N --at X,Y,F</c> enters area script N at square X,Y facing F (N, E, S,
/// W) with no save. <c>--scale</c> sets the window size (default 3 × 320×200).
/// </summary>
public sealed record ClientOptions
{
    public string? GameDirectory { get; init; }
    public string? Save { get; init; }
    public int? Area { get; init; }
    public (int X, int Y, int Facing)? At { get; init; }
    public int Scale { get; init; } = 3;

    public const string Usage = "usage: GoldBox.Client [--game DIR] [--save A|FILE] [--area N --at X,Y,F] [--scale K]";

    public static ClientOptions Parse(IReadOnlyList<string> args)
    {
        var o = new ClientOptions();
        for (int i = 0; i < args.Count; i++)
        {
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            o = args[i] switch
            {
                "--game" => o with { GameDirectory = Next() },
                "--save" => o with { Save = Next() },
                "--area" => o with { Area = int.Parse(Next(), CultureInfo.InvariantCulture) },
                "--at" => o with { At = ParseAt(Next()) },
                "--scale" => o with { Scale = int.Parse(Next(), CultureInfo.InvariantCulture) },
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
}
