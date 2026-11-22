using GoldBox.Client;
using GoldBox.Client.Audio;
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
    || options.Art is not null || options.Sound is not null || options.ProgramWord is not null || options.AnimationSpeed is not null)
{
    if (options.AnimationSpeed is { } anim)
        settings.AnimationSpeedChoice = anim;
    if (options.ProgramWord is not null)
        settings.ProgramWord = options.ProgramWord.Length > 0 ? options.ProgramWord : null;
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
var runner = new ScriptRunner { AnimationSpeed = settings.AnimationSpeedChoice };
PorGame? game = null;
ClientPlayer? player = null;
ClientAudio? audio = null;
var soundSetting = new SoundSetting(settings, device => audio?.SetDevice(device), hasAmiga: () => audio?.HasAmigaEffects == true);
void PlayAmigaSong(int song)
{
    if (clientArt is { Choice: not PorArtChoice.Dos } || audio?.Device == ClientSoundDevice.Amiga)
        audio?.PlaySong(song);
}
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
// A load in the game: the slot and the hook called with the loaded game before its entry runs.
Func<char, Action<PorGame>, PorGame>? loadSlot = null;
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
        Chrome.AmigaFrame = clientArt.Frame;
        Chrome.AmigaGadget = clientArt.Gadget;
        AmigaView.On = clientArt.Frame is not null;
        // The list routine's window top after start-up (DOS 1, Amiga 0): a change of art later keeps the running value.
        sharedHighlight.ListTop = AmigaView.On ? MenuHighlight.AmigaListTop : MenuHighlight.DosListTop;
        clientArt.Changed = c =>
        {
            Chrome.AmigaFrame = clientArt.Frame;
            Chrome.AmigaGadget = clientArt.Gadget;
            AmigaView.On = clientArt.Frame is not null;
            if (c == PorArtChoice.Dos && audio?.Device != ClientSoundDevice.Amiga)
                audio?.PlaySong(0);
            settings.ArtChoice = c;
            settings.Save();
        };
        Console.Error.WriteLine($"art: {ClientArt.Label(settings.ArtChoice)}");
        player = new ClientPlayer(runner, () => game!.Memory)
        {
            Resources = ClientResources.Load(content),
            Game = () => game,
            PlaySong = PlayAmigaSong,
        };
        // The Amiga pages' wordings and pens (s43): the program's menu spelling, its own strings and call-site pens.
        AmigaView.LearnMenuLines(player.Resources.MenuLines);
        AmigaView.CallPens = content.AmigaCallPens() ?? new Dictionary<string, int>();
        AmigaView.Strings = content.AmigaStrings() ?? new Dictionary<string, string>();
        var endArt = clientArt;
        player.EndArt = (disk, block) => endArt.Pictures.Find("FINAL", block, disk, anyDisk: true);
        player.InstallPaging();
        runner.Answered = player.MenuEnded; // every program menu clears VM $4AFF at its end (0x2CFAD)
        runner.Asking = player.MenuAsked;   // and draws the loaded picture on every pass while it is set (0x2CDD6)
        // Sound (docs/re/sound.md): the program's driver data from the content, on the device the setting names.
        audio = ClientAudio.Open(content.Sound(), ClientAudio.Parse(settings.Sound), content.AmigaSound());
        Console.Error.WriteLine($"sound: {audio?.Device?.ToString().ToLowerInvariant() ?? "off"}");
        if (audio is not null)
        {
            player.Sound = audio;
            audio.Instant = settings.AnimationSpeedChoice == AnimationSpeed.Instant;
            runner.ServiceWhileWaiting = audio.Update;
        }
        player.SaveFolder = ClientSettings.SavesFolder;
        player.ProgramWord = settings.ProgramWord;
        player.ProgramWordChanged = w =>
        {
            settings.ProgramWord = w;
            settings.Save();
        };
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
        PorGame WithSound(PorGame g) { g.Loop.Sound = player.Sound; player.ShowOutsideEffects(g); return g; }
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
                // DS:6DDC (the overland marker's counter) is not saved: a load goes on from the session's. The player's
                // hosts read `game`, so it is the loaded one before vector 4 runs (the entry may open the hall).
                LoadGame = (slot, s, started) =>
                {
                    try
                    {
                        return game = WithSound(PorGame.LoadAtPartyMenu(content, art, SlotPath(slot), who, s,
                            game?.Memory.MarkerCounter ?? 0, g =>
                            {
                                game = WithSound(g);
                                // vector 4 draws over the party menu's page until START 0x3379 runs (DOSBox-X s42)
                                player.PartyMenuUnder = g.LoadedInterfaceState != 0;
                                started(g);
                            }, game?.Loop.Map)); // the map buffer keeps the session's map when the save reads none
                    }
                    finally
                    {
                        player.PartyMenuUnder = false; // the entry ended (DS:49F3 = 3: the first command draws the interface)
                    }
                },
                SaveGame = (slot, party, loaded) => player.SaveAtPartyMenu(slot, party, content, loaded),
                Highlight = sharedHighlight,
                // J and Train at the start-up menu (the hall's, on the menu's party).
                Resources = player.Resources,
                ProgramWord = () => player.ProgramWord,
                PlaySong = PlayAmigaSong,
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
                    hf.GameMemory = g.Memory;
                }
                a();
            }
            player.HallHooks.Create = () => OnGame(hf.Create);
            player.HallHooks.Drop = () => OnGame(hf.Drop);
            player.HallHooks.Modify = () => OnGame(hf.Modify);
            player.HallHooks.Add = () => OnGame(hf.Add);
            player.HallHooks.Remove = () => OnGame(hf.Remove);
            player.HallHooks.View = () => OnGame(hf.View);
            player.HallHooks.Load = () =>
            {
                if (hf.PickSlot() is not { } slot)
                    return;
                // Load copies the save over the running game (PorGame.LoadInHall). 0x19D8A: a save with interface state 0
                // keeps the hall's menu up, and the script goes on at its PC after Begin; any other leaves the menu at
                // once and the running script goes on at its PC in the loaded script area (DOSBox-X s39).
                if (game is { } g && g.Memory.Party is { } party)
                {
                    try
                    {
                        // A save of another area enters it afresh (user decision s39), as HallLoadException does below.
                        switch (g.LoadInHall(SlotPath(slot), party))
                        {
                            case GoldBox.Data.Por.HallLoad.StayInMenu:
                                return;
                            case GoldBox.Data.Por.HallLoad.CarryOn:
                                if (player.Hall is { } hallState)
                                    hallState.LeaveAfterLoad = true;
                                return;
                        }
                    }
                    catch (Exception e) when (e is FormatException or IOException or System.Text.Json.JsonException)
                    {
                        Console.Error.WriteLine($"load {slot}: {e.Message}");
                        hf.CouldNotLoad(slot);
                        return;
                    }
                }
                throw new HallLoadException(slot);
            };
            player.HallHooks.Notice = hf.TakeNotice;
            // The camp's Alter Icon runs the same icon editor 0x1D1AC on the current character.
            player.IconEditor = hf.EditIcon;
            // The camp's, shop's, temple's and loot menu's View call the same 0x21AED: the party menu's page (drawn by
            // the explore screen's PartyPages overlay, so only when that exists), with the game's memory for the
            // portrait rule (disk byte and portraits flag).
            if (menuContent is not null)
                player.ViewPage = (c, entries) =>
                {
                    if (game is { } g)
                        hf.GameMemory = g.Memory;
                    return hf.ViewPage(c, entries);
                };
            if (menuContent is not null)
            {
                // Its Items page (0x21F32) and Drop's coin list (0x234E1), on the same overlay.
                player.ItemsPage = (c, entries, row, name) => hf.ItemsPage(c, entries, row, name);
                player.ItemsTalk = w => hf.TextOverPage = w;
                player.Coins = (c, kinds, words) => hf.Coins(c, kinds, words);
            }
            loadSlot = (slot, started) =>
            {
                // DS:84E1 (the picture animation's arm) is not in the save and Load (0x1F5E8) does not write it: the
                // loaded game goes on with the running one's. So does the typing flag DS:4A00 (only the start-up, PRINT,
                // ENCMENU and the after-combat end write it).
                bool armed = game?.Memory.PictureArmed ?? false;
                bool typing = game?.Memory.TypesText ?? true;
                return game = WithSound(PorGame.FromSave(content, art, SlotPath(slot), who, seed,
                    game?.Memory.MarkerCounter ?? 0, g =>
                    {
                        g.Memory.PictureArmed = armed;
                        g.Memory.TypesText = typing;
                        game = WithSound(g);
                        started(g);
                    }, game?.Loop.Map));
            };
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
        // The entry runs on the logic thread, so its menus or text (if any) wait for the window; the game is handed over
        // before it runs (the player's hosts read it: an entry may open the hall).
        if (!startAtMenu)
        runner.Start(() =>
        {
            game = options.Area is { } script && options.At is var (x, y, f)
                ? PorGame.Enter(content, art, script, x, y, f, who, seed, g => game = WithSound(g))
                : PorGame.FromSave(content, art, savePath!, who, seed, started: g => game = WithSound(g));
        });
        if (runner.Fault is { } fault)
            throw fault;
        if (game is null && runner.Pending is null && !startAtMenu)
            throw new InvalidOperationException("the area entry did not finish");
        if (game is not null)
            Console.Error.WriteLine($"area: script {game.Vm.CurrentScript} map {game.Loop.Map?.Id} at {game.Memory.Position.X},{game.Memory.Position.Y}; entry {(runner.Pending is null ? game.EntryRun.Reason.ToString() : "waiting for the player")}");
    }
    catch (Exception e) when (e is IOException or FormatException or ArgumentException or InvalidOperationException
                                   or UnauthorizedAccessException or GoldBox.Data.Dax.DaxFormatException
                                   or GoldBox.Core.Ecl.EclVmFault or System.Text.Json.JsonException
                                   or GoldBox.Core.Ecl.Profiles.UnknownEclProfileException
                                   or GoldBox.Core.Ecl.Profiles.EclProfileNotRunnableException
                                   or GoldBox.Data.Module.Por.ModuleHasNoVmException)
    {
        problem = "could not load the game files: " + e.Message;
    }
}
if (problem is not null)
    Console.Error.WriteLine(problem);

// The screens a game hands over to: the explore screen (with the hall's creation pages, a hall Load and the party menu
// after a destroyed party), and the party menu (a new menu on an empty party, as after the program's game loop ends).
// The controls screen's on/off settings (QoL, off = original), saved on each change.
IReadOnlyList<SettingToggle> toggles =
[
    SettingToggle.AfterDefeat(settings),
    SettingToggle.Animation(settings, s =>
    {
        runner.AnimationSpeed = s;
        if (audio is not null)
            audio.Instant = s == AnimationSpeed.Instant;
    }),
];
IScreen MenuScreen(PartyMenuFlow flow, IScreenHost host) =>
    new PartyMenuScreen(flow, runner, host, menuContent!.Font, player!.Resources, clientArt!, g => ExploreFor(g, 'A', host))
    {
        Program = menuProgram,
        Items = menuItems,
        Toggles = toggles,
        Sound = soundSetting,
        StartWord = player.ProgramWordSetting,
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
    // The loaded game is handed over before its entry runs, so an entry that waits for the player (the hall, a menu)
    // goes on under the new screen; a load that fails leaves the running game as it was.
    PorGame? previous = game, loaded = null;
    runner.Start(() => loadSlot(slot, g => loaded = g));
    if (runner.Fault is { } f)
    {
        Console.Error.WriteLine("load: " + f);
        game = previous;
        return null;
    }
    return loaded is null ? null : ExploreFor(loaded, slot, host);
}
// One highlight cell (DS:6B2B) for every screen, the party pages over the explore screen too (View's Exit leaves the
// shop menu on that entry): with --save it starts at the slot's place among the saves present, as the explore
// screen's own did.
MenuHighlight SharedHighlightFor(PorGame g, char letter)
{
    if (partyMenu is null)
        sharedHighlight.Index = ExploreScreen.LoadMenuIndex(g.Content.SaveFolder, char.ToUpperInvariant(letter));
    return sharedHighlight;
}
ExploreScreen ExploreFor(PorGame g, char letter, IScreenHost host) => new(g, player!, runner, host, letter, clientArt,
    // One cell DS:6B2B for every menu, the camp's icon editor (a party-menu page) too (D s39: Alter's I lights Icon,
    // the 4th, and the editor's line opens on its 4th, Size). A --save start seeds it as the load menu would.
    SharedHighlightFor(g, letter))
{
    PartyPages = hallFlow is { } hf && menuContent is not null
        ? () => new PartyMenuScreen(hf, runner, host, menuContent.Font, player!.Resources, clientArt!, _ => null!)
        {
            Program = menuProgram,
            Items = menuItems,
            Overlay = true,
            Toggles = toggles,
            Sound = soundSetting,
            StartWord = player!.ProgramWordSetting,
        }
        : null,
    LoadSave = slot => LoadInto(slot, host),
    // Read when the party is destroyed, so a change on the controls screen applies at once (null: quit, the original).
    PartyMenu = () => settings.AfterDefeatMenu ? NewMenu(host) : null,
    Toggles = toggles,
    Sound = soundSetting,
};

using var window = new GoldBoxGame(
    host =>
    {
        if (partyMenu is not null && player is not null && menuContent is not null)
        {
            var menu = MenuScreen(partyMenu, host);
            // The introduction first (GAME.OVR 0x6FB: logo, title, credits), as the program runs it before the code
            // wheel and the party menu. A start from a save or an area skips it, as the program's debug start does, and
            // so does the start word when it is the program's debugging word (START 0x116: ParamStr(1) equal skips the
            // introduction and the code wheel; DOSBox-X s24, START STING: straight to the party menu).
            if (clientArt is null || GoldBox.Client.Play.HallScreens.IsProgramWord(player.Resources.HallTexts, player.ProgramWord))
                return menu;
            // Best / Amiga with the Amiga intro's pages: the Amiga intro (its credits panels and the painting, faded).
            if (clientArt.AmigaIntroPages() is { } pages)
            {
                host.Push(menu);
                // Song 1 plays while the intro runs and stops after it (hunk 10 +0x580 / +0x59A, docs/re/sound.md §5.3).
                audio?.PlaySong(1);
                return new AmigaIntroScreen(pages, palette, host) { Done = () => audio?.PlaySong(0) };
            }
            var logo = clientArt.IntroLogo();
            var titlePage = clientArt.IntroTitle();
            var intro = new IntroScreen(logo, logo is null ? 0 : palette.Base(logo.PaletteRgb),
                titlePage, titlePage is null ? 0 : palette.Base(titlePage.PaletteRgb), menuContent.Font,
                i => menuProgram?.Texts.GetValueOrDefault("Intro." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), host);
            if (intro.Empty)
                return menu;
            host.Push(menu);
            return intro;
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
window.Input.SkipPauses = settings.SkipPauses;
window.Input.ToggleSound = soundSetting.ToggleMute;
if (player is not null)
    player.Quit = window.Exit;
window.Run();
runner.Dispose();
audio?.Dispose();
return problem is null ? 0 : 1;
