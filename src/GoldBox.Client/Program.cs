using GoldBox.Client;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Data.Module.Por;
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
if (options.GameDirectory is not null || options.Stretch43 is not null || options.AmigaDirectory is not null
    || options.Art is not null)
{
    if (options.GameDirectory is not null)
        settings.GameDirectory = Path.GetFullPath(options.GameDirectory);
    if (options.AmigaDirectory is not null)
        settings.AmigaDirectory = Path.GetFullPath(options.AmigaDirectory);
    if (options.Art is { } choice)
        settings.ArtChoice = choice;
    settings.Stretch43 = options.Stretch43 ?? settings.Stretch43;
    settings.Save();
}
var gameDir = settings.GameDirectory ?? Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
var amigaDir = settings.AmigaDirectory ?? Environment.GetEnvironmentVariable("GOLDBOX_POR_AMIGA_DIR");
if (string.IsNullOrWhiteSpace(amigaDir) || !Directory.Exists(amigaDir))
{
    if (!string.IsNullOrWhiteSpace(amigaDir))
        Console.Error.WriteLine($"Amiga folder {amigaDir} not found: DOS art only");
    amigaDir = null;
}
else
{
    // The overlay is optional: a folder that is not the unpacked Amiga disks (no program or pic.dax) leaves DOS art.
    try
    {
        PorAmigaArt.Locate(amigaDir);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"{e.Message}: DOS art only");
        amigaDir = null;
    }
}
var palette = new ScreenPalette();
ClientArt? clientArt = null;

// Load the player's files before the window opens; any problem is shown in the window title and on stderr.
// The game logic runs on its own thread (ScriptRunner); the area entry (vector 4) already runs through it.
var runner = new ScriptRunner();
PorGame? game = null;
ClientPlayer? player = null;
string? problem = null;
if (options.ModuleDirectory is null && (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)))
    problem = "Pool of Radiance folder not set: start with --game DIR or --module DIR";
else
{
    try
    {
        // --module: an imported module (no game folder needed); otherwise the player's folder, imported into the cache
        // first, so every run plays through a module.
        string ImportGame()
        {
            if (amigaDir is null)
                return PorModuleCache.Ensure(gameDir!, ClientSettings.ModuleCacheFolder);
            try
            {
                return PorModuleCache.Ensure(gameDir!, ClientSettings.ModuleCacheFolder, amigaDir);
            }
            catch (Exception e) when (e is GoldBox.Data.Dax.DaxFormatException or FormatException or IOException)
            {
                // A broken or other Amiga version: play with the DOS art rather than not at all.
                Console.Error.WriteLine($"Amiga art not imported ({e.Message}): DOS art only");
                return PorModuleCache.Ensure(gameDir!, ClientSettings.ModuleCacheFolder);
            }
        }
        var moduleDir = options.ModuleDirectory ?? ImportGame();
        IPorContent content = PorModule.Open(moduleDir);
        Console.Error.WriteLine($"content: {content.Origin}");
        var art = PorViewArt.Load(content);
        // The art setting (Original DOS / Best / Amiga) applies before the area entry loads its wall sets.
        clientArt = new ClientArt(content, art, palette, settings.ArtChoice);
        clientArt.Changed = c =>
        {
            settings.ArtChoice = c;
            settings.Save();
        };
        Console.Error.WriteLine($"art: {ClientArt.Label(settings.ArtChoice)}");
        player = new ClientPlayer(runner, () => game!.Memory)
        {
            Resources = ClientResources.Load(content),
            Game = () => game,
        };
        player.InstallPaging();
        player.SaveFolder = ClientSettings.SavesFolder;
        var program = content.Program();
        player.Chooser.Text = new CombatText(program);
        if (program?.SpellNames is { Count: > 0 } names)
        {
            var spells = new SpellNames(names);
            player.Chooser.SpellName = id => spells[id];
        }
        var who = new PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true);
        // The game seeds its generator from the DOS clock at start-up.
        uint seed = (uint)Environment.TickCount64;
        string? savePath = null;
        if (options.Area is null)
        {
            // The player's own saves (written by the camp) come first, then the module's shipped saves; engine saves
            // before original files, which are converted on load.
            savePath = ClientOptions.ResolveSave(options.Save ?? "A", ClientSettings.SavesFolder, content.SaveFolder)
                ?? throw new FileNotFoundException($"save {options.Save ?? "A"} not found in {ClientSettings.SavesFolder} or {content.Origin}");
            Console.Error.WriteLine($"save: {savePath}");
        }
        // The entry runs on the logic thread, so its menus or text (if any) wait for the window.
        runner.Start(() =>
        {
            game = options.Area is { } script && options.At is var (x, y, f)
                ? PorGame.Enter(content, art, script, x, y, f, who, seed)
                : PorGame.FromSave(content, art, savePath!, who, seed);
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
                                   or GoldBox.Core.Ecl.EclVmFault or System.Text.Json.JsonException
                                   or GoldBox.Core.Ecl.Profiles.UnknownEclProfileException
                                   or GoldBox.Data.Module.Por.ModuleHasNoVmException)
    {
        problem = "could not load the game files: " + e.Message;
    }
}
if (problem is not null)
    Console.Error.WriteLine(problem);

using var window = new GoldBoxGame(
    host =>
    {
        if (game is null || player is null)
            return null;
        char letter = options.Area is null ? ClientOptions.SlotOf(options.Save ?? "A") : 'A';
        var explore = new ExploreScreen(game, player, runner, host, letter, clientArt);
        // Best / Amiga: the title painting first (any key, button or click goes on to the game).
        if (clientArt?.Title() is not { } title)
            return explore;
        host.Push(explore);
        return new TitleScreen(title, palette.Base(title.PaletteRgb), host);
    },
    ClientSettings.LoadBindings(), options.Scale)
{
    Problem = problem,
    Stretch43 = settings.Stretch43,
    Palette = palette,
};
if (player is not null)
    player.Quit = window.Exit;
window.Run();
runner.Dispose();
return problem is null ? 0 : 1;
