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
    || options.Art is not null || options.Sound is not null)
{
    if (options.Sound is not null)
        settings.Sound = options.Sound;
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
GoldBox.Client.Audio.ClientAudio? audio = null;
// No --save and no --area: the party menu, the original's first screen (create, add, load, begin).
PartyMenuFlow? partyMenu = null;
// The program's one menu highlight cell DS:6B2B, shared by the party menu, the hall and the game's menus.
var sharedHighlight = new MenuHighlight();
bool startAtMenu = options.Area is null && options.Save is null;
PorViewArt? menuContent = null;
PorProgramData? menuProgram = null;
GoldBox.Core.Game.BaseItemTable? menuItems = null;
string? problem = null;
Func<PartyMenuFlow>? makeFlow = null;
PartyMenuFlow? hallFlow = null;
Func<char, PorGame>? loadSlot = null;
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
        menuContent = art;
        Chrome.FrameSymbols = art.Symbols;
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
        // Sound (docs/re/sound.md): the program's driver data from the content, on the device the setting names.
        audio = GoldBox.Client.Audio.ClientAudio.Open(content.Sound(), GoldBox.Client.Audio.ClientAudio.Parse(settings.Sound));
        Console.Error.WriteLine($"sound: {(audio is null ? "off" : audio.Device.ToString().ToLowerInvariant())}");
        if (audio is not null)
            player.Sound = audio;
        player.SaveFolder = ClientSettings.SavesFolder;
        var program = content.Program();
        menuProgram = program;
        player.Chooser.Text = new CombatText(program);
        if (program?.SpellNames is { Count: > 0 } names)
        {
            var spells = new SpellNames(names);
            player.Chooser.SpellName = id => spells[id];
        }
        var who = new PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true);
        // The game seeds its generator from the DOS clock at start-up.
        uint seed = options.Seed ?? (uint)Environment.TickCount64;
        string? savePath = null;
        // The party menu's pieces (the menu at start-up and after a destroyed party; the training hall's roster entries).
        var tables = program?.CreationTables();
        var items = PorFight.Kit(content)?.Items;
        if (startAtMenu && tables is null)
            throw new InvalidOperationException("the party menu's tables were not found (not Pool of Radiance DOS v1.3?)");
        if (startAtMenu && items is null)
            throw new InvalidOperationException("no item types");
        PorGame WithSound(PorGame g) { g.Loop.Sound = player.Sound; return g; }
        var roster = new PorRoster(Path.Combine(ClientSettings.SavesFolder, "characters"));
        IReadOnlyList<char> Slots() => [.. "ABCDEFGHIJ".Where(l => ClientOptions.ResolveSave(l.ToString(), ClientSettings.SavesFolder, content.SaveFolder) is not null)];
        string SlotPath(char slot) => ClientOptions.ResolveSave(slot.ToString(), ClientSettings.SavesFolder, content.SaveFolder)
            ?? throw new FileNotFoundException($"save {slot} not found");
        menuItems = items;
        if (tables is not null && items is not null)
        {
            var texts = new PartyMenuTexts(program, player.Resources);
            makeFlow = () => new PartyMenuFlow(runner.Ask, texts, tables, items, roster, seed)
            {
                NewGame = (party, s) => game = WithSound(PorGame.NewGame(content, art, party, who, s)),
                SaveSlots = Slots,
                LoadGame = (slot, s) => game = WithSound(PorGame.LoadAtPartyMenu(content, art, SlotPath(slot), who, s)),
                SaveGame = (slot, party, loaded) => player.SaveAtPartyMenu(slot, party, content, loaded),
                Highlight = sharedHighlight,
            };
            // The training hall (PROGRAM 0) runs the same menu routine 0x19AE0 on the game's party: its Create, Drop,
            // Add, Remove and Load are the party menu's, drawing from the game's generator.
            hallFlow = makeFlow();
            var hf = hallFlow;
            void OnGame(Action a)
            {
                if (game is { } g)
                {
                    hf.Party = g.Memory.Party ??= new GoldBox.Core.Ecl.EclParty();
                    hf.Random = g.Rng;
                }
                a();
            }
            player.HallHooks.Create = () => OnGame(hf.Create);
            player.HallHooks.Drop = () => OnGame(hf.Drop);
            player.HallHooks.Modify = () => OnGame(hf.Modify);
            player.HallHooks.Add = () => OnGame(hf.Add);
            player.HallHooks.Remove = () => OnGame(hf.Remove);
            player.HallHooks.Load = () =>
            {
                if (hf.PickSlot() is { } slot)
                    throw new HallLoadException(slot);
            };
            player.HallHooks.Notice = hf.TakeNotice;
            loadSlot = slot => game = WithSound(PorGame.FromSave(content, art, SlotPath(slot), who, seed));
        }
        if (startAtMenu)
        {
            partyMenu = makeFlow!();
            runner.Start(partyMenu.Run);
            if (runner.Fault is { } menuFault)
                throw menuFault;
        }
        else if (options.Area is null)
        {
            // The player's own saves (written by the camp) come first, then the module's shipped saves; engine saves
            // before original files, which are converted on load.
            savePath = ClientOptions.ResolveSave(options.Save ?? "A", ClientSettings.SavesFolder, content.SaveFolder)
                ?? throw new FileNotFoundException($"save {options.Save ?? "A"} not found in {ClientSettings.SavesFolder} or {content.Origin}");
            Console.Error.WriteLine($"save: {savePath}");
        }
        // The entry runs on the logic thread, so its menus or text (if any) wait for the window.
        if (!startAtMenu)
        runner.Start(() =>
        {
            game = options.Area is { } script && options.At is var (x, y, f)
                ? PorGame.Enter(content, art, script, x, y, f, who, seed)
                : PorGame.FromSave(content, art, savePath!, who, seed);
            game.Loop.Sound = player.Sound;
        });
        if (runner.Fault is { } fault)
            throw fault;
        if (game is null && runner.Pending is null && !startAtMenu)
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

// The screens a game hands over to: the explore screen (with the hall's creation pages, a hall Load and the party menu
// after a destroyed party), and the party menu (a new menu on an empty party, as after the program's game loop ends).
IScreen MenuScreen(PartyMenuFlow flow, IScreenHost host) =>
    new PartyMenuScreen(flow, runner, host, menuContent!.Font, player!.Resources, clientArt!, g => ExploreFor(g, 'A', host))
    {
        Program = menuProgram,
        Items = menuItems,
    };
IScreen? NewMenu(IScreenHost host)
{
    if (makeFlow is null || menuContent is null || player is null)
        return null;
    var flow = makeFlow();
    runner.Start(flow.Run);
    if (runner.Fault is { } f)
    {
        Console.Error.WriteLine("party menu: " + f);
        return null;
    }
    return MenuScreen(flow, host);
}
IScreen? LoadInto(char slot, IScreenHost host)
{
    if (loadSlot is null)
        return null;
    PorGame? loaded = null;
    runner.Start(() => loaded = loadSlot(slot));
    if (runner.Fault is { } f)
    {
        Console.Error.WriteLine("load: " + f);
        return null;
    }
    // [?] An entry vector that waits for the player during a load is not handed over (as at start-up with --save).
    return loaded is null ? null : ExploreFor(loaded, slot, host);
}
ExploreScreen ExploreFor(PorGame g, char letter, IScreenHost host) => new(g, player!, runner, host, letter, clientArt,
    partyMenu is not null ? sharedHighlight : null)
{
    PartyPages = hallFlow is { } hf && menuContent is not null
        ? () => new PartyMenuScreen(hf, runner, host, menuContent.Font, player!.Resources, clientArt!, _ => null!)
        {
            Program = menuProgram,
            Items = menuItems,
            Overlay = true,
        }
        : null,
    LoadSave = slot => LoadInto(slot, host),
    PartyMenu = settings.AfterDefeatMenu ? () => NewMenu(host) : null,
};

using var window = new GoldBoxGame(
    host =>
    {
        if (partyMenu is not null && player is not null && menuContent is not null)
        {
            var menu = MenuScreen(partyMenu, host);
            if (clientArt?.Title() is not { } menuTitle)
                return menu;
            host.Push(menu);
            return new TitleScreen(menuTitle, palette.Base(menuTitle.PaletteRgb), host);
        }
        if (game is null || player is null)
            return null;
        char letter = options.Area is null ? ClientOptions.SlotOf(options.Save ?? "A") : 'A';
        var explore = ExploreFor(game, letter, host);
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
audio?.Dispose();
return problem is null ? 0 : 1;
