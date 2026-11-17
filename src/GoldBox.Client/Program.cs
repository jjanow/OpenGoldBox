using GoldBox.Client;
using GoldBox.Client.Screens;
using GoldBox.Data.Por;

ClientOptions options;
try
{
    options = ClientOptions.Parse(args);
}
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(ClientOptions.Usage);
    return 2;
}

var settings = ClientSettings.Load();
if (options.GameDirectory is not null)
{
    settings.GameDirectory = Path.GetFullPath(options.GameDirectory);
    settings.Save();
}
var gameDir = settings.GameDirectory ?? Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");

// Load the player's files before the window opens; any problem is shown in the window title and on stderr.
PorArea? area = null;
PorViewArt? art = null;
string? problem = null;
if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
    problem = "Pool of Radiance folder not set: start with --game DIR";
else
{
    try
    {
        var dir = new PorGameDirectory(gameDir);
        art = PorViewArt.Load(dir);
        if (options.Area is { } script && options.At is var (x, y, f))
            area = PorArea.Enter(dir, art, script, x, y, f);
        else
        {
            var name = ClientOptions.SaveFileName(options.Save ?? "A");
            var path = dir.Find(name) ?? (File.Exists(name) ? name : null)
                ?? throw new FileNotFoundException($"save {name} not found in {gameDir}");
            area = PorArea.FromSave(dir, art, path);
        }
        Console.Error.WriteLine($"area: script {area.Script} map {area.Map.Id} at {area.Party}; entry {area.EntryRun.Reason}");
    }
    catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException
                                   or UnauthorizedAccessException or GoldBox.Data.Dax.DaxFormatException)
    {
        problem = "could not load the game files: " + e.Message;
    }
}
if (problem is not null)
    Console.Error.WriteLine(problem);

using var game = new GoldBoxGame(
    host => area is not null && art is not null ? new ExploreScreen(area, art, host) : null,
    ClientSettings.LoadBindings(), options.Scale)
{
    Problem = problem,
};
game.Run();
return problem is null ? 0 : 1;
