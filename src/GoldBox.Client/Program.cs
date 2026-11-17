using GoldBox.Client;
using GoldBox.Client.Play;
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
if (options.GameDirectory is not null || options.Stretch43 is not null)
{
    if (options.GameDirectory is not null)
        settings.GameDirectory = Path.GetFullPath(options.GameDirectory);
    settings.Stretch43 = options.Stretch43 ?? settings.Stretch43;
    settings.Save();
}
var gameDir = settings.GameDirectory ?? Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");

// Load the player's files before the window opens; any problem is shown in the window title and on stderr.
// The game logic runs on its own thread (ScriptRunner); the area entry (vector 4) already runs through it.
var runner = new ScriptRunner();
PorGame? game = null;
ClientPlayer? player = null;
string? problem = null;
if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
    problem = "Pool of Radiance folder not set: start with --game DIR";
else
{
    try
    {
        var dir = new PorGameDirectory(gameDir);
        var art = PorViewArt.Load(dir);
        player = new ClientPlayer(runner, () => game!.Memory)
        {
            Resources = ClientResources.Load(dir),
            Game = () => game,
        };
        player.InstallPaging();
        player.SaveFolder = ClientSettings.SavesFolder;
        var program = PorProgram.Open(dir);
        player.Chooser.Text = new CombatText(program);
        if (program is not null && new SpellNames(program.Image) is { Count: > 0 } spells)
            player.Chooser.SpellName = id => spells[id];
        var who = new PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true);
        // The game seeds its generator from the DOS clock at start-up.
        uint seed = (uint)Environment.TickCount64;
        string? savePath = null;
        if (options.Area is null)
        {
            var name = ClientOptions.SaveFileName(options.Save ?? "A");
            // The player's own saves (written by the camp) come first, then the game folder's.
            var own = Path.Combine(ClientSettings.SavesFolder, name);
            savePath = (File.Exists(own) ? own : null) ?? dir.Find(name) ?? (File.Exists(name) ? name : null)
                ?? throw new FileNotFoundException($"save {name} not found in {gameDir}");
        }
        // The entry runs on the logic thread, so its menus or text (if any) wait for the window.
        runner.Start(() =>
        {
            game = options.Area is { } script && options.At is var (x, y, f)
                ? PorGame.Enter(dir, art, script, x, y, f, who, seed)
                : PorGame.FromSave(dir, art, savePath!, who, seed);
        });
        if (runner.Fault is { } fault)
            throw fault;
        if (game is null && runner.Pending is null)
            throw new InvalidOperationException("the area entry did not finish");
        if (game is not null)
            Console.Error.WriteLine($"area: script {game.Vm.CurrentScript} map {game.Loop.Map?.Id} at {game.Memory.Position.X},{game.Memory.Position.Y}; entry {game.EntryRun.Reason}");
    }
    catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException
                                   or UnauthorizedAccessException or GoldBox.Data.Dax.DaxFormatException
                                   or GoldBox.Core.Ecl.EclVmFault)
    {
        problem = "could not load the game files: " + e.Message;
    }
}
if (problem is not null)
    Console.Error.WriteLine(problem);

using var window = new GoldBoxGame(
    host => game is not null && player is not null ? new ExploreScreen(game, player, runner, host, options.Area is null ? ClientOptions.SaveFileName(options.Save ?? "A") is var n && n.StartsWith("SAVGAM", StringComparison.Ordinal) ? n[6] : 'A' : 'A') : null,
    ClientSettings.LoadBindings(), options.Scale)
{
    Problem = problem,
    Stretch43 = settings.Stretch43,
};
if (player is not null)
    player.Quit = window.Exit;
window.Run();
runner.Dispose();
return problem is null ? 0 : 1;
