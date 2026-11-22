using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// The party menu through its screen and the action layer (keyboard keys, then the pad's actions alone): create a
/// character, add it, begin; skipped without GOLDBOX_POR_DIR.
/// </summary>
public class PartyMenuScreenTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class Host : IScreenHost
    {
        public List<IScreen> Pushed { get; } = [];
        public bool Quitted { get; private set; }
        public void Push(IScreen screen) => Pushed.Add(screen);
        public void Pop() { }
        public void Quit() => Quitted = true;
        public void SaveBindings() { }
    }

    private sealed class Rig : IDisposable
    {
        public required PartyMenuScreen Screen { get; init; }
        public required PartyMenuFlow Flow { get; init; }
        public required ScriptRunner Runner { get; init; }
        public required Host Host { get; init; }
        public required DirectoryInfo Roster { get; init; }
        public required CreationTables Tables { get; init; }
        public required BaseItemTable Items { get; init; }
        public ActionInput Input { get; } = new(InputBindings.Defaults());
        public PorGame? Game { get; set; }

        public void Key(Keys k)
        {
            Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
            Screen.Update(Input);
            Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        }

        public void Act(GameAction a)
        {
            Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            Input.Inject(a);
            Screen.Update(Input);
        }

        public void Dispose()
        {
            Runner.Dispose();
            Roster.Delete(true);
        }
    }

    private static Rig Start(uint seed, Action<PartyMenuFlow, CreationTables, BaseItemTable>? setup = null, string? saves = null,
        string? word = null, Action<PartyMenuFlow, ScriptRunner>? script = null)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        Rig? rig = null;
        var player = new ClientPlayer(runner, () => rig!.Game!.Memory) { Resources = ClientResources.Load(dir), Game = () => rig!.Game };
        var who = new PorPlayer(player, player, player, player, QuickFight: true);
        var program = dir.Program()!;
        var tables = program.CreationTables()!;
        var items = PorFight.Kit(dir)!.Items;
        var roster = Directory.CreateTempSubdirectory("goldbox-party-");
        player.SaveFolder = saves;
        string Slot(char l) => Path.Combine(saves!, GoldBox.Data.Module.Por.PorEngineSave.FileName(l));
        var flow = new PartyMenuFlow(runner.Ask, new PartyMenuTexts(program, player.Resources), tables, items,
            new PorRoster(roster.FullName), seed)
        {
            NewGame = (party, s) => rig!.Game = PorGame.NewGame(dir, art, party, who, s),
            SaveGame = saves is null ? null : (l, party, loaded) => player.SaveAtPartyMenu(l, party, dir, loaded),
            SaveSlots = saves is null ? null : () => [.. "ABCDEFGHIJ".Where(l => File.Exists(Slot(l)))],
            LoadGame = saves is null ? null : (l, s, started) => rig!.Game = PorGame.LoadAtPartyMenu(dir, art, Slot(l), who, s, started: g =>
            {
                rig!.Game = g;
                started(g);
            }),
            Resources = player.Resources,
            ProgramWord = () => word,
        };
        var host = new Host();
        var screen = new PartyMenuScreen(flow, runner, host, art.Font, player.Resources, new ClientArt(dir, art, new ScreenPalette(), PorArtChoice.Dos),
            g => new ExploreScreen(g, player, runner, host)) { Program = program, Items = items };
        rig = new Rig { Screen = screen, Flow = flow, Runner = runner, Host = host, Roster = roster, Tables = tables, Items = items };
        setup?.Invoke(flow, tables, items);
        runner.Start(script is null ? flow.Run : () => script(flow, runner));
        return rig;
    }

    /// <summary>
    /// The page's portrait window needs the portraits flag ($49FF bit 0) and the disk byte 3 (0x21530; DOSBox-X s25:
    /// Modify after a state-0 load of a disk-2 save, or with portraits off, shows neither portrait nor box).
    /// </summary>
    [Fact]
    public void The_portrait_window_needs_portraits_on_and_disk_3()
    {
        if (Dir() is null)
            return;
        using var r = Start(1);
        Assert.True(r.Flow.PortraitShown); // start-up: disk 3, portraits on
        var m = new GoldBox.Core.Ecl.EclMemory { Disk = 3 };
        m.Write(CampSettings.PicturesCell, 3);
        r.Flow.GameMemory = m;
        Assert.True(r.Flow.PortraitShown);
        m.Disk = 2;
        Assert.False(r.Flow.PortraitShown);
        m.Disk = 3;
        m.Write(CampSettings.PicturesCell, 2);
        Assert.False(r.Flow.PortraitShown);
    }

    [Fact]
    public void Keyboard_creates_a_human_adds_it_and_begins()
    {
        if (Dir() is null)
            return;
        using var r = Start(4242);
        var menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal([PartyMenuItem.Create, PartyMenuItem.Add, PartyMenuItem.Load, PartyMenuItem.Exit], menu.Items.Select(i => i.Item));

        r.Key(Keys.C);
        var races = Assert.IsType<PickRequest>(r.Runner.Pending);
        Assert.Equal(6, races.Rows.Count);
        for (int i = 0; i < 5; i++)
            r.Key(Keys.Down);
        r.Key(Keys.Enter);                       // human
        r.Key(Keys.Down);
        r.Key(Keys.Enter);                       // female
        r.Key(Keys.Enter);                       // the first class
        r.Key(Keys.Enter);                       // the first alignment
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Key(Keys.N);                           // roll again
        r.Key(Keys.Y);
        Assert.IsType<TextRequest>(r.Runner.Pending);
        foreach (var k in new[] { Keys.H, Keys.E, Keys.R, Keys.O })
            r.Key(k);
        r.Key(Keys.Enter);
        var portrait = Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Key(Keys.H);                           // head 2
        r.Key(Keys.K);
        Assert.IsType<IconRequest>(r.Runner.Pending);
        // D s39: the line "Parts color-1 color-2 Size Exit" lit as the menu routine does: Parts lit also lights the
        // COLOR of color-1 (its entry runs to two cells before the hotkey 1); the '-' stays green, the 1 is white.
        for (int i = r.Flow.Highlight.Index; i % 5 != 0; i++)
            r.Act(GameAction.MenuRight);
        var icon = Drawn(r);
        // The combat palette (0x3081D, colours 0 and 8 swapped): row 24's ground and the frame's gaps are dark grey.
        Assert.Equal([8], Cell(icon, 5, 24));
        Assert.Equal([8], Cell(icon, 20, 12));
        Assert.Contains((byte)8, Cell(icon, 10, 0));
        Assert.Equal([15, 15, 15, 15, 10, 15, 10], new[] { 0, 4, 6, 10, 11, 12, 15 }.Select(c => Cell(icon, c, 24).Max()));
        r.Act(GameAction.MenuRight);             // color-1 lit: P white, COLOR- green, 1 white, the next COLOR white
        icon = Drawn(r);
        Assert.Equal([15, 10, 10, 15, 15, 15, 10, 15], new[] { 0, 1, 6, 12, 14, 18, 19, 20 }.Select(c => Cell(icon, c, 24).Max()));
        r.Key(Keys.E);                           // Exit
        r.Key(Keys.Y);                           // the icon is ok
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);

        // The record equals Core's for the same seed and picks (2 rolls), with the portrait head changed.
        var roster = new PorRoster(r.Roster.FullName);
        Assert.Equal(["HERO"], roster.Entries(null));
        var saved = roster.Load("HERO")!;
        var rng = new TurboPascalRandom(4242);
        var cc = new CharacterCreation(r.Tables, rng.Next);
        cc.PickRace(7);
        cc.PickGender(1);
        cc.PickClass(cc.ClassChoices()[0]);
        cc.PickAlignment(cc.AlignmentChoices()[0]);
        cc.Roll(r.Items);
        cc.Roll(r.Items);
        cc.SetName("HERO");
        cc.Character[CharacterCreation.PortraitHead] = 2;
        Assert.Equal(cc.Character.Record, saved.Record);
        Assert.Equal(rng.Seed, r.Flow.Random.Seed);

        r.Key(Keys.A);
        Assert.IsType<PickRequest>(r.Runner.Pending);
        r.Key(Keys.Enter);                       // HERO joins
        r.Key(Keys.Escape);
        menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Contains(menu.Items, i => i.Item == PartyMenuItem.Begin);
        Assert.Equal("HERO", r.Flow.Party.Current!.Name);
        Assert.Empty(roster.Entries(null));      // Add's exit writes the list without the members

        r.Key(Keys.B);
        Assert.NotNull(r.Game);
        Assert.Single(r.Host.Pushed);
        Assert.IsType<ExploreScreen>(r.Host.Pushed[0]);
        Assert.Equal((15, 1), (r.Game!.Memory.Position.X, r.Game.Memory.Position.Y));
    }

    [Fact]
    public void A_creation_list_shows_its_label_only_when_the_list_top_is_0()
    {
        // DOSBox-X s44: a fresh start (the list routine's top DS:6B29 = 1) shows the races from row 2 with no label;
        // after camp Display (position 0, top 0) the label on row 2 and the races from row 3, the gender list
        // likewise. The Amiga starts with the top 0 (hunk 25 +0x1540): its labels show from the start.
        if (Dir() is null)
            return;
        using (var r = Start(77))
        {
            Assert.Equal(MenuHighlight.DosListTop, r.Flow.Highlight.ListTop);
            r.Act(GameAction.Confirm);               // Create
            var race = Assert.IsType<PickRequest>(r.Runner.Pending);
            Assert.Null(race.Title);
            Assert.Equal(2, race.FirstRow);
            Assert.Equal(1, r.Flow.Highlight.ListTop);
        }
        using (var r = Start(77))
        {
            r.Flow.Highlight.ListTop = 0;            // after a list at position 0 (camp Display), or the Amiga's start
            r.Act(GameAction.Confirm);
            var race = Assert.IsType<PickRequest>(r.Runner.Pending);
            Assert.False(string.IsNullOrEmpty(race.Title));
            Assert.Equal(3, race.FirstRow);
            r.Act(GameAction.Confirm);               // dwarf
            var gender = Assert.IsType<PickRequest>(r.Runner.Pending);
            Assert.False(string.IsNullOrEmpty(gender.Title));
            Assert.NotEqual(race.Title, gender.Title);
        }
        using (var r = Start(77))
        {
            AddHero(r);                              // Add's roster list: position 0 (0x1CA19) takes the top to 0
            Assert.Equal(0, r.Flow.Highlight.ListTop);
            r.Key(Keys.C);
            Assert.False(string.IsNullOrEmpty(Assert.IsType<PickRequest>(r.Runner.Pending).Title));
        }
    }

    [Fact]
    public void The_list_routines_entry_moves_the_global_top()
    {
        Assert.Equal(1, MenuHighlight.EnterList(1, 1, 7));    // creation on a fresh DOS start: the label stays hidden
        Assert.Equal(0, MenuHighlight.EnterList(1, 0, 20));   // camp Display (position 0)
        Assert.Equal(0, MenuHighlight.EnterList(0, 1, 7));    // then creation shows its label
        Assert.Equal(1, MenuHighlight.EnterList(5, 1, 7));    // a top below the position goes up to it
        Assert.Equal(0, MenuHighlight.EnterList(9, 12, 7));   // past the entries: 0
        var h = new MenuHighlight();
        Assert.Equal(MenuHighlight.DosListTop, h.ListTop);
        Assert.Equal(0, h.ListEntered(0, 3));
        Assert.Equal(0, h.ListTop);
    }

    [Fact]
    public void The_pad_alone_creates_and_adds_and_drops()
    {
        if (Dir() is null)
            return;
        using var r = Start(77);
        r.Act(GameAction.Confirm);               // the first entry: Create
        Assert.IsType<PickRequest>(r.Runner.Pending);
        for (int k = 0; k < 4; k++)
            r.Act(GameAction.Confirm);           // dwarf, male, first class, first alignment
        Assert.Equal(1, r.Flow.Highlight.Index); // the yes/no opens on NO (0x2D9A0 sets DS:6B2B to 2)
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.Confirm);               // YES: keep
        Assert.IsType<TextRequest>(r.Runner.Pending);
        r.Act(GameAction.Confirm);               // A from the grid
        r.Act(GameAction.MenuRight);
        r.Act(GameAction.Confirm);               // B
        for (int k = 0; k < 39; k++)
            r.Act(GameAction.MenuRight);         // from B (cell 1) to OK (cell 40: 39 letters, DEL, OK)
        r.Act(GameAction.Confirm);
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Act(GameAction.MenuLeft);              // KEEP is the last word
        r.Act(GameAction.Confirm);
        Assert.IsType<IconRequest>(r.Runner.Pending);
        r.Act(GameAction.Cancel);                // Exit
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.Confirm);               // YES
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(["AB"], new PorRoster(r.Roster.FullName).Entries(null));

        r.Act(GameAction.MenuDown);              // Add
        r.Act(GameAction.Confirm);
        r.Act(GameAction.Confirm);               // AB joins
        r.Act(GameAction.Cancel);
        Assert.Single(r.Flow.Party.Members);

        // Drop: the menu now is Create, Drop, Modify, View, Add, Remove, Save, Begin, Exit.
        r.Act(GameAction.MenuDown);
        r.Act(GameAction.Confirm);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.Confirm);               // YES
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.Confirm);               // YES, sure
        var after = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Empty(r.Flow.Party.Members);
        Assert.NotNull(after.Notice);
    }

    [Fact]
    public void The_mouse_picks_entries_rows_and_words()
    {
        if (Dir() is null)
            return;
        using var r = Start(9);
        void Click(int col, int row, bool right = false)
        {
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            if (r.Screen.MapClick(new ScreenClick(col * 8 + 3, row * 8 + 3, right)) is { } a)
                r.Input.Inject(a);
            r.Screen.Update(r.Input);
        }
        Click(5, 12);                            // CREATE NEW CHARACTER (row 12)
        Assert.IsType<PickRequest>(r.Runner.Pending);
        Click(4, 2 + 5);                         // HUMAN
        Click(4, 2);                             // MALE
        Click(4, 2);                             // the first class
        Click(4, 2);                             // the first alignment
        var sheet = Assert.IsType<SheetRequest>(r.Runner.Pending);
        int yes = sheet.Prompt!.Length;          // YES right after the prompt
        Click(yes, 24);
        Assert.IsType<TextRequest>(r.Runner.Pending);
        Click(2, 14);                            // A on the grid (row 14, column 2)
        Click(8, 17);                            // OK
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        Click(10, 24);                           // KEEP (HEAD BODY KEEP from column 0)
        Assert.IsType<IconRequest>(r.Runner.Pending);
        Click(5, 10, right: true);               // right click: Exit
        var ok = Assert.IsType<IconRequest>(r.Runner.Pending);
        Click(ok.Prompt!.Length, 24);            // YES
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(["A"], new PorRoster(r.Roster.FullName).Entries(null));
        Click(5, 13);                            // ADD CHARACTER TO PARTY
        Click(3, 2);                             // A joins
        Click(5, 5, right: true);                // back out
        Assert.Single(r.Flow.Party.Members);
    }

    // ---- Modify (0x1BA1E) --------------------------------------------------------------------------------------------

    /// <summary>A human fighter made by Core's creation (seed 31), joined to the party before the menu runs.</summary>
    private static Rig WithFighter(uint xp = 0) => Start(5, (flow, t, items) =>
    {
        var cc = new CharacterCreation(t, new TurboPascalRandom(31).Next);
        cc.PickRace(7);
        cc.PickGender(0);
        cc.PickClass(2);
        cc.PickAlignment(cc.AlignmentChoices()[0]);
        cc.Roll(items);
        cc.SetName("MOD");
        cc.Character.SetWord(CharacterModify.Experience, (ushort)xp);
        PartyMenu.Join(flow.Party, cc.Character, c => LevelStats.Apply(c, t.Levels));
    });

    /// <summary>Core's Modify on a copy of the record (with the member's effects), the keys given in order.</summary>
    private static byte[] Expected(Rig r, byte[] before, params ModifyKey[] keys)
    {
        var c = new EclCharacter((byte[])before.Clone());
        foreach (var e in r.Flow.Party.Current!.Effects)
            c.Effects.Add((byte[])e.Clone());
        var m = new CharacterModify(r.Tables, r.Items, c);
        foreach (var k in keys)
            if (m.Press(k))
                break;
        return c.Record;
    }

    [Fact]
    public void Keyboard_modifies_the_current_character()
    {
        if (Dir() is null)
            return;
        using var r = WithFighter();
        var c = r.Flow.Party.Current!;
        var before = (byte[])c.Record.Clone();
        r.Key(Keys.M);
        var md = Assert.IsType<ModifyRequest>(r.Runner.Pending);
        Assert.Equal(0, md.Field);
        Assert.Equal(["KEEP", "EXIT"], md.Actions);
        r.Key(Keys.Right);
        r.Key(Keys.Right);
        r.Key(Keys.Down);
        r.Key(Keys.Left);
        r.Key(Keys.Enter);                       // ignored, as in the program
        r.Key(Keys.D8);                          // the keypad flag: 8 is Up
        r.Key(Keys.Up);                          // wraps to the hit points
        Assert.Equal(CharacterModify.HpField, Assert.IsType<ModifyRequest>(r.Runner.Pending).Field);
        r.Key(Keys.Left);
        r.Key(Keys.Right);
        r.Key(Keys.Right);
        r.Key(Keys.K);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        var want = Expected(r, before, ModifyKey.Right, ModifyKey.Right, ModifyKey.Down, ModifyKey.Left, ModifyKey.Other,
            ModifyKey.Up, ModifyKey.Up, ModifyKey.Left, ModifyKey.Right, ModifyKey.Right, ModifyKey.Keep);
        Assert.Equal(want, c.Record);
        Assert.NotEqual(before, c.Record);
        Assert.Equal(1, c[CharacterModify.TreasureShare]);

        // E puts the values back.
        var kept = (byte[])c.Record.Clone();
        r.Key(Keys.M);
        r.Key(Keys.Left);
        r.Key(Keys.Down);
        r.Key(Keys.Left);
        r.Key(Keys.E);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(Expected(r, kept, ModifyKey.Left, ModifyKey.Down, ModifyKey.Left, ModifyKey.Exit), c.Record);
    }

    [Fact]
    public void A_character_with_experience_is_refused()
    {
        if (Dir() is null)
            return;
        using var r = WithFighter(xp: 1);
        var before = (byte[])r.Flow.Party.Current!.Record.Clone();
        r.Key(Keys.M);
        // START 0x7DF8: the refusal in yellow on the bottom line for game speed × 225 ms, then the menu again with its
        // prompt and no key (DOSBox-X s25).
        var pause = Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal(TimeSpan.FromMilliseconds(225), pause.Duration);
        Assert.Equal(14, r.Flow.Hall.Message!.Value.Colour);
        Assert.StartsWith("MOD", r.Flow.Hall.Message!.Value.Text);
        r.Runner.Answer(null);
        var menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Null(menu.Notice);
        Assert.Null(r.Flow.Hall.Message);
        Assert.Equal(before, r.Flow.Party.Current!.Record);
    }

    [Fact]
    public void The_pad_modifies_exits_and_keeps()
    {
        if (Dir() is null)
            return;
        using var r = WithFighter();
        var c = r.Flow.Party.Current!;
        var before = (byte[])c.Record.Clone();
        r.Act(GameAction.MenuDown);
        r.Act(GameAction.MenuDown);              // Modify is the third entry (Create, Drop, Modify)
        r.Act(GameAction.Confirm);
        Assert.IsType<ModifyRequest>(r.Runner.Pending);
        for (int k = 0; k < 4; k++)
            r.Act(GameAction.MenuDown);          // Constitution
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.MenuLeft);
        r.Act(GameAction.Cancel);                // B: Exit, all back
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(Expected(r, before, ModifyKey.Down, ModifyKey.Down, ModifyKey.Down, ModifyKey.Down, ModifyKey.Left,
            ModifyKey.Left, ModifyKey.Exit), c.Record);

        var now = (byte[])c.Record.Clone();
        r.Act(GameAction.MenuDown);
        r.Act(GameAction.MenuDown);
        r.Act(GameAction.Confirm);               // Modify again
        Assert.IsType<ModifyRequest>(r.Runner.Pending);
        r.Act(GameAction.MenuRight);
        r.Act(GameAction.NextMember);            // RB: the highlight to EXIT
        r.Act(GameAction.PreviousMember);        // LB: back to KEEP
        r.Act(GameAction.Confirm);               // A: Keep
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(Expected(r, now, ModifyKey.Right, ModifyKey.Keep), c.Record);
        Assert.Equal(1, c[CharacterModify.TreasureShare]);
    }

    [Fact]
    public void The_mouse_picks_fields_steps_them_and_keeps()
    {
        if (Dir() is null)
            return;
        using var r = WithFighter();
        var c = r.Flow.Party.Current!;
        var before = (byte[])c.Record.Clone();
        void Click(int col, int row, bool right = false)
        {
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            if (r.Screen.MapClick(new ScreenClick(col * 8 + 3, row * 8 + 3, right)) is { } a)
                r.Input.Inject(a);
            r.Screen.Update(r.Input);
        }
        Click(5, 14);                            // MODIFY (row 12 + 2)
        Assert.IsType<ModifyRequest>(r.Runner.Pending);
        Click(2, 9, right: true);                // a right click on another field moves the cursor, no Exit
        Assert.Equal(2, Assert.IsType<ModifyRequest>(r.Runner.Pending).Field);
        Click(5, 9);                             // raise
        Click(5, 9, right: true);                // lower
        Click(5, 9, right: true);
        Click(2, 18);                            // hit points
        Click(4, 18);                            // raise
        var md = Assert.IsType<ModifyRequest>(r.Runner.Pending);
        Assert.Equal(CharacterModify.HpField, md.Field);
        Click(md.Prompt!.Length, 24);            // KEEP
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(Expected(r, before, ModifyKey.Down, ModifyKey.Down, ModifyKey.Right, ModifyKey.Left, ModifyKey.Left,
            ModifyKey.Down, ModifyKey.Down, ModifyKey.Down, ModifyKey.Down, ModifyKey.Right, ModifyKey.Keep), c.Record);
    }

    /// <summary>Create up to the icon editor with the keyboard (human, female, first class and alignment, "HERO").</summary>
    private static void CreateToIcon(Rig r)
    {
        r.Key(Keys.C);
        for (int i = 0; i < 5; i++)
            r.Key(Keys.Down);
        r.Key(Keys.Enter);
        r.Key(Keys.Down);
        r.Key(Keys.Enter);
        r.Key(Keys.Enter);
        r.Key(Keys.Enter);
        r.Key(Keys.Y);
        foreach (var k in new[] { Keys.H, Keys.E, Keys.R, Keys.O })
            r.Key(k);
        r.Key(Keys.Enter);
        r.Key(Keys.K);
        Assert.IsType<IconRequest>(r.Runner.Pending);
    }

    [Fact]
    public void The_icon_editor_has_the_programs_menus_and_colours()
    {
        if (Dir() is null)
            return;
        using var r = Start(31);
        CreateToIcon(r);
        var main = Assert.IsType<IconRequest>(r.Runner.Pending);
        Assert.Equal("P12SE", main.Hotkeys);     // Parts color-1 color-2 Size Exit (DS:07E8)
        r.Key(Keys.D1);                           // color-1: the low nibbles
        var colours = Assert.IsType<IconRequest>(r.Runner.Pending);
        Assert.Equal("WBHSALE", colours.Hotkeys); // the third word is the hair word (0x1D16B)
        r.Key(Keys.B);                            // body, byte 0xC1
        Assert.Equal("NPKE", Assert.IsType<IconRequest>(r.Runner.Pending).Hotkeys);
        var c = Assert.IsType<IconRequest>(r.Runner.Pending).Character;
        byte before = c[IconEditor.Colours];
        r.Key(Keys.N);
        Assert.Equal((before & 0xF0) | ((before + 1) & 0x0F), c[IconEditor.Colours]);
        r.Key(Keys.K);                            // keep, back to the colour menu
        r.Key(Keys.E);
        r.Key(Keys.S);                            // the size menu offers the other size first
        Assert.Equal(c[IconEditor.Size] == 2 ? 'S' : 'L', Assert.IsType<IconRequest>(r.Runner.Pending).Hotkeys![0]);
        r.Key(Keys.E);
        r.Key(Keys.P);
        r.Key(Keys.H);
        byte head = c[IconEditor.Head];
        r.Key(Keys.N);
        r.Key(Keys.E);                            // Exit puts the head back
        Assert.Equal(head, c[IconEditor.Head]);
        r.Key(Keys.E);
        r.Key(Keys.E);                            // leave the editor
        r.Key(Keys.Y);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        var saved = new PorRoster(r.Roster.FullName).Load("HERO")!;
        Assert.Equal((before & 0xF0) | ((before + 1) & 0x0F), saved[IconEditor.Colours]);
    }

    /// <summary>
    /// Add's roster (0x1CA7A, P + D s46): the list routine's window (1, 2)–(38, 22) of 21 rows with "Add Exit" and then
    /// Next / Prev, paging through the shared top. DOSBox-X with 30 names: ROSTER01–21 with "ADD EXIT NEXT"; N shows
    /// ROSTER10–30 with ROSTER10 lit and "ADD EXIT PREV" (top min(0 + 21, 30 − 21) = 9).
    /// </summary>
    [Fact]
    public void Add_pages_its_roster_in_the_programs_window_through_the_shared_top()
    {
        if (Dir() is null)
            return;
        using var r = Start(77);
        for (int i = 1; i <= 30; i++)
        {
            var cc = new CharacterCreation(r.Tables, new TurboPascalRandom((uint)i).Next);
            cc.PickRace(7);
            cc.PickGender(0);
            cc.PickClass(cc.ClassChoices()[0]);
            cc.PickAlignment(cc.AlignmentChoices()[0]);
            cc.Roll(r.Items);
            cc.SetName($"ROSTER{i:00}");
            new PorRoster(r.Roster.FullName).Save(cc.Character, null);
        }
        r.Key(Keys.A);
        var pick = Assert.IsType<PickRequest>(r.Runner.Pending);
        var list = Assert.IsType<ListRequest>(pick.List);
        Assert.Equal((2, 21, 2), (list.FirstRow, list.Paging!.Rows, list.Paging.WordsAt));
        Assert.Equal(["ADD", "EXIT"], list.Actions);
        Assert.Equal(0, r.Flow.Highlight.ListTop);  // position 0 (0x1CA19): the entry takes the start-up's 1 to 0
        r.Key(Keys.N);                               // Next: 0x2D502
        Assert.Equal(9, r.Flow.Highlight.ListTop);
        r.Key(Keys.Enter);                           // the lit row, at the window's top: ROSTER10 joins
        Assert.Equal("ROSTER10", r.Flow.Party.Current!.Name);
        Assert.Equal(9, Assert.IsType<PickRequest>(r.Runner.Pending).List!.Selected); // the position word kept
        r.Key(Keys.Escape);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    private static void AddHero(Rig r, int classChoice = 0)
    {
        var cc = new CharacterCreation(r.Tables, new TurboPascalRandom(5).Next);
        cc.PickRace(7);
        cc.PickGender(0);
        cc.PickClass(cc.ClassChoices()[classChoice]);
        cc.PickAlignment(cc.AlignmentChoices()[0]);
        cc.Roll(r.Items);
        cc.SetName("HERO");
        new PorRoster(r.Roster.FullName).Save(cc.Character, null);
        r.Key(Keys.A);
        r.Key(Keys.Enter);
        r.Key(Keys.Escape);
        Assert.Single(r.Flow.Party.Members);
    }

    [Fact]
    public void Save_before_begin_writes_the_unstarted_game_and_load_takes_it_back_to_the_menu()
    {
        if (Dir() is null)
            return;
        var saves = Directory.CreateTempSubdirectory("goldbox-saves-");
        try
        {
            using (var r = Start(11, saves: saves.FullName))
            {
                AddHero(r);
                r.Key(Keys.S);
                Assert.IsType<ChoiceRequest>(r.Runner.Pending);
                r.Key(Keys.C);                    // slot C
                Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
                var doc = GoldBox.Data.Module.Por.PorEngineSave.Read(Path.Combine(saves.FullName, "c.json"));
                var sg = GoldBox.Data.Module.Por.PorEngineSave.ToSaveGame(doc);
                Assert.Equal((3, 0, 1, 1), (sg.Disk, sg.InterfaceState, sg.LocationType, sg.PartySize));
                var m = new EclMemory();
                sg.ApplyTo(m);
                Assert.Equal((15, 1, 6), (m.Position.X, m.Position.Y, m.Position.Facing));
                Assert.Equal(3, m.Read(0x49FF));  // portraits and monster pictures on (DS:49F5 / 49F6 = 1)
                Assert.Equal(1, m.Read(0x49FC));  // game speed 1
                Assert.Equal(3, m.Read(0x6E12));
                Assert.Equal([0, 0xFFFF, 0xFFFF, 1, 0xFFFF, 0xFFFF], Enumerable.Range(0x4AFA, 6).Select(a => (int)m.Read((ushort)a)));
                Assert.Equal(1, m.Read(0x6E3E));
                // Saved just now: Exit quits without asking for a slot again (DS:557D).
                r.Key(Keys.E);
                r.Key(Keys.Y);
                Assert.Null(r.Runner.Pending);
                Assert.Null(r.Flow.Started);
            }
            using (var r = Start(12, saves: saves.FullName))
            {
                r.Key(Keys.L);
                r.Key(Keys.C);
                // Interface state 0: the menu stays up with the save's party (0x19D8A), Load gone, Begin offered.
                var menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
                Assert.Null(r.Flow.Started);
                Assert.Equal("HERO", r.Flow.Party.Current!.Name);
                Assert.DoesNotContain(menu.Items, i => i.Item == PartyMenuItem.Load);
                r.Key(Keys.B);
                Assert.Same(r.Game, r.Flow.Started);
                Assert.Same(r.Flow.Party, r.Game!.Memory.Party);
                Assert.Equal((15, 1), (r.Game.Memory.Position.X, r.Game.Memory.Position.Y));
                Assert.Equal(0, r.Game.Vm.CurrentScript);
            }
        }
        finally
        {
            saves.Delete(true);
        }
    }

    /// <summary>
    /// Load at the start-up menu of a save whose vector 4 opens the training hall: the game is started (and the screen
    /// hands over to its explore screen) before the entry runs, so the hall reads the loaded game; Begin ends the entry
    /// and the menu's run with it.
    /// </summary>
    [Fact]
    public void Load_of_a_save_whose_entry_opens_the_hall_hands_over_first()
    {
        if (Dir() is null)
            return;
        var saves = Directory.CreateTempSubdirectory("goldbox-saves-");
        try
        {
            HallPlayTests.HallEntrySave(new PorGameDirectory(Dir()!), saves.FullName);
            using var r = Start(14, saves: saves.FullName);
            r.Key(Keys.L);
            r.Key(Keys.B);                        // slot B
            Assert.Null(r.Runner.Fault);
            Assert.IsType<HallMenuRequest>(r.Runner.Pending);
            Assert.NotNull(r.Game);
            Assert.Same(r.Game, r.Flow.Started);
            r.Act(GameAction.Confirm);            // any update hands over
            var explore = Assert.IsType<ExploreScreen>(Assert.Single(r.Host.Pushed));
            r.Input.Update(new KeyboardState(Keys.B), default, default, TimeSpan.Zero, _ => null);
            explore.Update(r.Input);              // Begin: the hall ends, vector 4 and the menu's run with it
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            for (int i = 0; i < 200 && r.Runner.Busy; i++)
            {
                Thread.Sleep(5);
                explore.Update(r.Input);
            }
            Assert.Null(r.Runner.Fault);
            Assert.False(r.Runner.Busy);
            Assert.Same(r.Game, r.Flow.Started);
            Assert.Equal(6, r.Game!.Memory.Party!.Members.Count);
        }
        finally
        {
            saves.Delete(true);
        }
    }

    [Fact]
    public void Exit_with_a_party_asks_for_a_slot_until_a_save_is_written()
    {
        if (Dir() is null)
            return;
        var saves = Directory.CreateTempSubdirectory("goldbox-saves-");
        try
        {
            using var r = Start(13, saves: saves.FullName);
            AddHero(r);
            r.Key(Keys.E);
            r.Key(Keys.Y);                        // quit: no save since the last key, so the slot menu
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            r.Key(Keys.Escape);                   // no slot: asked again (0x19E23 jumps back to 0x19E0E)
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            r.Key(Keys.D);
            Assert.Null(r.Runner.Pending);
            Assert.True(File.Exists(Path.Combine(saves.FullName, "d.json")));
        }
        finally
        {
            saves.Delete(true);
        }
    }

    // ---- View (0x19D13 → 0x21AED, the camp's View) -------------------------------------------------------------------

    /// <summary>Two human fighters made by Core's creation (they get gold, no items); the first also carries an item.</summary>
    private static Rig WithTwoFighters() => Start(5, (flow, t, items) =>
    {
        foreach (var (seed, name) in new[] { (31u, "ONE"), (32u, "TWO") })
        {
            var cc = new CharacterCreation(t, new TurboPascalRandom(seed).Next);
            cc.PickRace(7);
            cc.PickGender(0);
            cc.PickClass(2);
            cc.PickAlignment(cc.AlignmentChoices()[0]);
            cc.Roll(items);
            cc.SetName(name);
            PartyMenu.Join(flow.Party, cc.Character, c => LevelStats.Apply(c, t.Levels));
        }
        var item = new byte[EclCharacter.ItemRecordSize];
        item[0x2E] = 1;
        flow.Party.Members[0].Items.Add(item);
        flow.Party.CurrentIndex = 0;
    });

    private static IndexedFrame Drawn(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    /// <summary>The colours drawn in a text cell (8 × 8).</summary>
    private static HashSet<byte> Cell(IndexedFrame f, int col, int row)
    {
        var s = new HashSet<byte>();
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                s.Add(f[col * 8 + x, row * 8 + y]);
        return s;
    }

    /// <summary>
    /// Sell / Id over the Items page (shop: TextOverPage on): after a yes/no is answered, row 24 keeps its line with the
    /// answer lit while the next text types (DOSBox-X s42: the scroll warning's question under Sell's offer, the deal
    /// question under Id's result), until a row-24 message (START 0x2233) writes it; the page's menu comes back with
    /// the page.
    /// </summary>
    [Fact]
    public void Sell_and_ids_yes_no_line_stays_on_row_24_while_the_next_text_types()
    {
        if (Dir() is null)
            return;
        using var r = Start(5, (flow, t, items) =>
        {
            var c = new EclCharacter { Name = "ONE" };
            c[0x10D] = 1;
            c.Items.Add(new byte[EclCharacter.ItemRecordSize]);
            flow.Party.Members.Add(c);
            flow.Party.CurrentIndex = 0;
        }, script: (flow, runner) =>
        {
            ItemsEntry[] entries = [ItemsEntry.Ready, ItemsEntry.Sell, ItemsEntry.Id, ItemsEntry.Exit];
            var c = flow.Party.Members[0];
            while (flow.ItemsPage(c, entries, 0).Action != 3)
            {
                var w = new TextWindow();
                flow.TextOverPage = w;
                var d = new Dialog(runner.Ask, w);
                d.YesNo("OKAY?");
                runner.Ask(new DelayRequest(TimeSpan.Zero));     // the next text types, one pause per character
                runner.Ask(new DelayRequest(TimeSpan.Zero));
                runner.Ask(new DelayRequest(TimeSpan.Zero) { BottomLine = ("SOLD!", TextWindow.ScriptColour) });
                runner.Ask(new DelayRequest(TimeSpan.Zero));
                flow.TextOverPage = null;
            }
        });
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        r.Key(Keys.S);                                         // Sell: the page menu with Sell lit, then the question
        Assert.Equal("OKAY?", Assert.IsType<ChoiceRequest>(r.Runner.Pending).MenuPrompt);
        r.Key(Keys.Y);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        var f = Drawn(r);
        Assert.Contains((byte)13, Cell(f, 0, 24));             // the question's prompt, not the page's READY
        Assert.Contains(Chrome.White, Cell(f, 7, 24));         // YES lit (0x2CF48 draws the chosen entry)
        Assert.DoesNotContain(Chrome.Green, Cell(f, 7, 24));
        r.Key(Keys.None);                                      // still kept after the first character's pause
        Assert.Contains((byte)13, Cell(Drawn(r), 0, 24));
        r.Key(Keys.None);
        Assert.Equal("SOLD!", Assert.IsType<DelayRequest>(r.Runner.Pending).BottomLine?.Text);
        r.Key(Keys.None);
        var g = Drawn(r);                                      // after the row-24 message: the page's menu again
        Assert.DoesNotContain((byte)13, Cell(g, 0, 24));
        r.Key(Keys.None);
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        r.Key(Keys.E);
    }

    /// <summary>
    /// V runs 0x21AED, the camp's View: the program's page with the View menu after "View:" (DOSBox-X s25: a fighter
    /// with an item and gold gets Items, Trade, Drop, Exit); Home / End do nothing in View (0x21AED's loop has no member
    /// selection); Items opens the items menu without Use (DS:49F3 = 0) and comes back to the View menu; Escape and Exit
    /// go back to the party menu; a member without items has no Items.
    /// </summary>
    [Fact]
    public void View_is_the_camp_view_menu_on_the_program_page()
    {
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        var party = r.Flow.Party;
        r.Key(Keys.V);
        var page = Assert.IsType<SheetRequest>(r.Runner.Pending);
        Assert.Same(party.Members[0], page.Character);
        Assert.Equal("VIEW:", page.Prompt);
        Assert.Equal(["ITEMS", "TRADE", "DROP", "EXIT"], page.Actions);
        Assert.False(page.PartySelect);
        Assert.Equal(3, page.CancelAction);

        // The page as before (portrait window, View's layout) with the words right after the prompt on row 24.
        var f = Drawn(r);
        Assert.Contains((byte)13, Cell(f, 0, 24));             // V of the prompt
        Assert.Contains(Chrome.White, Cell(f, 5, 24));         // I of ITEMS (the hotkey)
        Assert.Contains(Chrome.Green, Cell(f, 12, 24));        // R of TRADE
        Assert.True(Cell(f, 33, 6).Count > 1);                 // the portrait

        r.Key(Keys.End);                                       // no member switch inside View
        Assert.Same(page, r.Runner.Pending);
        Assert.Same(party.Members[0], party.Current);

        r.Key(Keys.I);                                         // Items: 0x21F32's page, no Use (DS:49F3 = 0)
        var items = Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        Assert.Equal(["READY", "TRADE", "DROP", "HALVE", "JOIN", "EXIT"], items.Entries);
        Assert.Single(items.Page.Rows);
        Assert.Equal([0, 2, 23], items.Page.FrameRows);
        Assert.Contains(items.Page.Texts, t => t.Row == 3 && t.Text == "READY ITEM");
        r.Key(Keys.None);
        var fi = Drawn(r);
        Assert.Contains(Chrome.White, Cell(fi, 2, 24));        // READY lit (0x2D669 opens on its first word)
        Assert.DoesNotContain(Chrome.Green, Cell(fi, 2, 24));
        r.Key(Keys.Enter);                                     // Return: 13, which 0x21F32 does not act on
        Assert.Same(items, r.Runner.Pending);
        r.Key(Keys.R);                                         // Ready: not built yet, on the bottom line
        Assert.Equal("READY: NOT IN THIS BUILD YET.", Assert.IsType<KeyRequest>(r.Runner.Pending).Prompt);
        r.Key(Keys.Enter);
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        r.Key(Keys.Escape);
        Assert.IsType<SheetRequest>(r.Runner.Pending);         // back on the View menu

        r.Key(Keys.T);                                         // the money Trade: START 0x3379 case 0, then the pick
        var pick = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.True(pick.PartySelect);
        var fp = Drawn(r);
        Assert.Contains(Chrome.White, Cell(fp, 1, 4));         // the viewer lit in the party menu's list
        Assert.Single(Cell(fp, 33, 6));                         // no page, no portrait: blank inside the frame
        r.Key(Keys.E);                                         // Exit: nobody, back to View
        Assert.IsType<SheetRequest>(r.Runner.Pending);

        r.Key(Keys.Escape);                                    // Escape (the menu routine's 0) ends View
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        r.Key(Keys.V);
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Key(Keys.E);                                         // and Exit
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        // Exit moved the shared highlight to the 4th word, but the party menu's own menu call (an empty item line) puts
        // the cell back on the first entry (0x2CCE7) when it comes up.
        Assert.Equal(3, r.Flow.Highlight.Index);
        r.Key(Keys.None);
        Assert.Equal(0, r.Flow.Highlight.Index);

        r.Key(Keys.End);                                       // the next member, picked on the party menu
        r.Key(Keys.V);
        page = Assert.IsType<SheetRequest>(r.Runner.Pending);
        Assert.Same(party.Members[1], page.Character);
        Assert.Equal(["TRADE", "DROP", "EXIT"], page.Actions); // no items: no Items
        r.Key(Keys.E);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    [Fact]
    public void Views_items_and_coin_lists_take_the_list_top_to_0_so_create_shows_its_labels()
    {
        // Read s45: View's Items (0x21F32: position 0 on entry, 0x21F5F) and the coin list of Drop (0x2365B) call the
        // list routine 0x2D669, whose entry takes the global top DS:6B29 from the start-up's 1 to 0 (0x2D766); a later
        // Create then shows "Pick Race" (por-art-formats.md "Creation's list labels").
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        Assert.Equal(MenuHighlight.DosListTop, r.Flow.Highlight.ListTop);
        r.Key(Keys.V);
        Assert.Equal(1, r.Flow.Highlight.ListTop);         // View's page is the menu routine's: no list
        r.Key(Keys.I);
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        Assert.Equal(0, r.Flow.Highlight.ListTop);
        r.Key(Keys.Escape);
        r.Flow.Highlight.ListTop = MenuHighlight.DosListTop;
        r.Key(Keys.D);
        Assert.True(Assert.IsType<CharacterPageRequest>(r.Runner.Pending).PickRows);
        Assert.Equal(0, r.Flow.Highlight.ListTop);
        r.Key(Keys.Escape);
        r.Key(Keys.E);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        r.Key(Keys.C);
        var race = Assert.IsType<PickRequest>(r.Runner.Pending);
        Assert.False(string.IsNullOrEmpty(race.Title));
        Assert.Equal(3, race.FirstRow);
    }

    /// <summary>
    /// View's Drop (0x234E1, DOSBox-X s39 in the camp): the coin list over the page (prompt, Exit lit; Return takes the
    /// row), the amount typed, the coin list again until Escape; View then opens on Items (0x2D669 stored 1 in the
    /// cell), and the page keeps the old encumbrance (only the money lines are redrawn, 0x21547).
    /// </summary>
    [Fact]
    public void View_drop_goes_through_the_coin_list()
    {
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        var me = r.Flow.Party.Members[0];
        int kind = CombatSheet.MoneyKinds(me)[0];
        ushort before = FightItemScreens.Coins(me, kind), weight = me.Word(0x102);
        r.Key(Keys.V);
        r.Key(Keys.D);
        var coins = Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        Assert.True(coins.PickRows);
        Assert.NotNull(coins.Sheet);
        Assert.Equal(["EXIT"], coins.Entries);
        r.Key(Keys.Enter);                                     // the first (highest) coin kind
        Assert.IsType<TextRequest>(r.Runner.Pending);
        r.Key(Keys.D1);
        r.Key(Keys.Enter);
        Assert.Equal(before - 1, FightItemScreens.Coins(me, kind));
        Assert.Equal(weight - 1, me.Word(0x102));
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending); // the coin list again
        r.Key(Keys.Escape);
        var page = Assert.IsType<SheetRequest>(r.Runner.Pending);
        Assert.Equal(0, r.Flow.Highlight.Index);               // View back on Items
        Assert.NotSame(me, page.Character);                    // the page as shown: old weight, new money
        Assert.Equal(weight, page.Character.Word(0x102));
        Assert.Equal(before - 1, FightItemScreens.Coins(page.Character, kind));
        r.Key(Keys.E);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    /// <summary>
    /// View's Trade on the party menu (DOSBox-X s42, the hall of slot C): the item Trade by keys (T, End to the next
    /// member, Return), the money Trade by the pad (the pick, the coin row, the amount clamped to what is held as it is
    /// typed) and the mouse (a name, then Select); the pick screen is the frame and the party list with the pick lit.
    /// </summary>
    [Fact]
    public void Trade_by_keys_pad_and_mouse()
    {
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        var (one, two) = (r.Flow.Party.Members[0], r.Flow.Party.Members[1]);
        var item = one.Items[0];
        one.SetWord(Money.Coins + 2, 5);                       // 5 silver besides the gold
        r.Key(Keys.V);
        r.Key(Keys.I);
        r.Key(Keys.T);                                         // the item Trade (an unreadied item)
        var pick = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Contains(Chrome.White, Cell(Drawn(r), 1, 4));   // the pick starts on the viewer (DS:471A)
        r.Key(Keys.End);
        Assert.Contains(Chrome.White, Cell(Drawn(r), 1, 5));   // TWO lit
        r.Key(Keys.Enter);                                     // Return selects (whatever word is lit)
        Assert.Empty(one.Items);
        Assert.Single(two.Items);
        Assert.NotSame(item, two.Items[0]);
        var view = Assert.IsType<SheetRequest>(r.Runner.Pending); // no items left: the Items page ends, View again
        Assert.Equal(["TRADE", "DROP", "EXIT"], view.Actions);

        int kind = CombatSheet.MoneyKinds(one)[0];
        ushort held = FightItemScreens.Coins(one, kind), had = FightItemScreens.Coins(two, kind);
        Assert.True(held is > 0 and < 10000);
        // The Items page's T put the shared cell on its 2nd word; the pick (Exit lit) and View keep it: DROP lit.
        Assert.Equal(1, r.Flow.Highlight.Index);
        r.Act(GameAction.MenuLeft);                            // the pad: to TRADE, A
        r.Act(GameAction.Confirm);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Contains(Chrome.White, Cell(Drawn(r), 1, 5));   // the pick starts on the last one traded with
        r.Act(GameAction.Confirm);
        var coins = Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        Assert.True(coins.PickRows);
        Assert.Equal(one.Name, coins.Sheet!.Name);             // the viewer's page under the coin list
        r.Act(GameAction.Confirm);                             // the first coin row
        var amount = Assert.IsType<TextRequest>(r.Runner.Pending);
        Assert.Equal(held, amount.Maximum);
        foreach (var k in new[] { Keys.D9, Keys.D9, Keys.D9, Keys.D9, Keys.D9 })
            r.Key(k);                                          // 0x24F1D: the number becomes the maximum as it is typed
        r.Key(Keys.Enter);
        Assert.Equal(0, FightItemScreens.Coins(one, kind));
        Assert.Equal(held + had, FightItemScreens.Coins(two, kind));
        Assert.True(TradeRules.HasMoney(one));                 // the silver
        {
            Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
            r.Act(GameAction.Cancel);                          // leave the coin list: the pick again
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            r.Act(GameAction.Cancel);                          // Escape clears the pick
            Assert.DoesNotContain(Chrome.White, Cell(Drawn(r), 1, 5));
            void Click(int col, int row)
            {
                r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
                if (r.Screen.MapClick(new ScreenClick(col * 8 + 3, row * 8 + 3, false)) is { } a)
                    r.Input.Inject(a);
                r.Screen.Update(r.Input);
            }
            Click(2, 5);                                       // the mouse: TWO's name
            Assert.Contains(Chrome.White, Cell(Drawn(r), 1, 5));
            var again = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
            int exitCol = again.MenuPrompt!.Length + 1 + again.Items[0].Length + 1;
            Click(exitCol + 1, 24);                            // EXIT: nobody, View again
        }
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Key(Keys.E);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    /// <summary>The pad and the mouse on View's menu: a click on a word picks it, B backs out, A takes the highlighted
    /// word.</summary>
    [Fact]
    public void Pad_and_mouse_work_the_view_menu()
    {
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        r.Key(Keys.V);
        r.Act(GameAction.NextMember);                          // nothing in View
        Assert.Same(r.Flow.Party.Members[0], Assert.IsType<SheetRequest>(r.Runner.Pending).Character);
        r.Act(GameAction.Cancel);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);

        r.Key(Keys.V);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (r.Screen.MapClick(new ScreenClick(6 * 8 + 3, 24 * 8 + 3, false)) is { } a)  // ITEMS (column 5–9)
            r.Input.Inject(a);
        r.Screen.Update(r.Input);
        Assert.IsType<CharacterPageRequest>(r.Runner.Pending);
        r.Act(GameAction.Cancel);
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        r.Act(GameAction.MenuLeft);                            // the highlight wraps to EXIT
        r.Act(GameAction.Confirm);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    /// <summary>
    /// View's keys as DOSBox-X took them (s39, a created dwarf fighter: Items Trade Drop Exit): ',' / '.' move the
    /// highlight (wrapping), Return, Home, End and space do nothing (0x2CC8C gives 13 or ' ', which 0x21AED's loop
    /// ignores; keypad flag 0, so extended keys are swallowed); Escape leaves View, and V again opens on Items (the
    /// party menu's menu call resets the cell). The pad's A still takes the highlighted word.
    /// </summary>
    [Fact]
    public void View_keys_are_the_programs()
    {
        if (Dir() is null)
            return;
        using var r = WithTwoFighters();
        r.Key(Keys.V);
        var page = Assert.IsType<SheetRequest>(r.Runner.Pending);
        Assert.True(page.IsView);
        // The lit word is white from its first to its last letter; the others green but for the white hotkey.
        bool Lit(int col0, int len)
        {
            var f = Drawn(r);
            for (int c = col0 + 1; c < col0 + len; c++)
                if (Cell(f, c, 24).Contains(Chrome.Green))
                    return false;
            return true;
        }
        Assert.True(Lit(5, 5));                                // ITEMS
        r.Key(Keys.OemPeriod);
        Assert.Equal(1, r.Flow.Highlight.Index);
        Assert.True(Lit(11, 5));                               // TRADE
        Assert.False(Lit(5, 5));
        r.Key(Keys.OemComma);
        r.Key(Keys.OemComma);                                  // wraps to EXIT
        Assert.Equal(3, r.Flow.Highlight.Index);
        Assert.True(Lit(22, 4));
        r.Key(Keys.OemPeriod);
        foreach (var k in new[] { Keys.Enter, Keys.Home, Keys.End, Keys.Space })
        {
            r.Key(k);
            Assert.Same(page, r.Runner.Pending);
        }
        Assert.True(Lit(5, 5));
        r.Key(Keys.OemPeriod);
        r.Key(Keys.OemPeriod);                                 // DROP
        r.Key(Keys.Escape);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, r.Flow.Highlight.Index);               // Escape leaves the cell where '.' put it
        r.Key(Keys.None);
        Assert.Equal(0, r.Flow.Highlight.Index);               // the party menu's call puts it back on the first
        r.Key(Keys.V);
        Assert.IsType<SheetRequest>(r.Runner.Pending);
        Assert.True(Lit(5, 5));                                // on Items again
        r.Act(GameAction.MenuRight);
        r.Act(GameAction.MenuRight);
        r.Act(GameAction.MenuRight);
        r.Act(GameAction.Confirm);                             // the pad's A on EXIT
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
    }

    // ---- J and Train at the start-up menu (s24) ------------------------------------------------------------------

    private static string Word() => new PorGameDirectory(Dir()!).Program()!.Text("Hall.Password")!;

    /// <summary>Updates with no new input until the pending request is no longer a pause (the screen times it).</summary>
    private static void WaitPause(Rig r)
    {
        for (int i = 0; i < 100 && r.Runner.Pending is DelayRequest; i++)
        {
            Thread.Sleep(20);
            r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
            r.Screen.Update(r.Input);
        }
    }

    /// <summary>
    /// DOSBox-X s24 (START with the program's word): J at the start-up menu shows the word's message on the bottom line
    /// for game speed 1 × 225 ms and turns Train on; Train then raises a level-1 character with no gold or experience
    /// checks (the page, the question, "Congratulations", the hit points), from the menu's generator.
    /// </summary>
    [Fact]
    public void J_with_the_word_offers_train_and_train_raises_the_menus_character()
    {
        if (Dir() is null)
            return;
        using var r = Start(21, word: Word() + " MORE");
        AddHero(r, classChoice: 1);              // a human fighter, 0 experience, a few gold
        var hero = r.Flow.Party.Current!;
        var menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.DoesNotContain(menu.Items, i => i.Item == PartyMenuItem.Train);

        r.Key(Keys.J);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        Assert.Equal(TimeSpan.FromMilliseconds(225), ((DelayRequest)r.Runner.Pending!).Duration);
        Assert.Equal((r.Flow.Hall.Message!.Value.Text, (byte)10), r.Flow.Hall.Message);
        Assert.StartsWith("I UNDERSTAND", r.Flow.Hall.Message!.Value.Text);
        WaitPause(r);
        Assert.Null(r.Flow.Hall.Message);
        menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Contains(menu.Items, i => i.Item == PartyMenuItem.Train);
        Assert.True(r.Flow.DebugFlag);

        int goldBefore = Money.CharacterGold(hero);
        int maxHp = hero[TrainingHall.MaxHp];
        r.Key(Keys.T);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        var page = r.Flow.Hall.Page!;
        Assert.Equal("HERO", page[0].Text);
        Assert.Contains(page, l => l.Row == 5 && l.Text.Contains('2'));
        r.Key(Keys.Y);
        Assert.IsType<DelayRequest>(r.Runner.Pending);   // "Congratulations..."
        WaitPause(r);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, hero[TrainingHall.Levels + 2]);  // fighter level 2
        Assert.True(hero[TrainingHall.MaxHp] > maxHp);
        Assert.Equal(goldBefore, Money.CharacterGold(hero)); // no payment with the debug flag
        Assert.Null(r.Flow.Hall.Page);

        // The flag stays while the menu runs: Remove, then Add, and Train is offered again.
        r.Key(Keys.R);
        r.Key(Keys.A);
        r.Key(Keys.Enter);
        r.Key(Keys.Escape);
        menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Contains(menu.Items, i => i.Item == PartyMenuItem.Train);
    }

    [Fact]
    public void J_with_another_word_or_none_does_nothing()
    {
        if (Dir() is null)
            return;
        foreach (var word in new string?[] { null, "OTHER", Word().ToLowerInvariant() })
        {
            using var r = Start(22, word: word);
            AddHero(r);
            r.Key(Keys.J);
            var menu = Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
            Assert.DoesNotContain(menu.Items, i => i.Item == PartyMenuItem.Train);
            Assert.False(r.Flow.DebugFlag);
        }
    }

    /// <summary>
    /// DOSBox-X s24: X then Right typed during J's message opened Modify (Right's scan code 0x4D read as 'M'); the pad's
    /// left-stick press is J.
    /// </summary>
    [Fact]
    public void Keys_typed_during_js_message_are_read_as_the_program_reads_them()
    {
        if (Dir() is null)
            return;
        using var r = Start(23, word: Word());
        AddHero(r);
        r.Input.Update(default, new GamePadState(default, default, new GamePadButtons(Buttons.LeftStick), default), default,
            TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        r.Key(Keys.X);
        r.Key(Keys.Right);
        Assert.IsType<DelayRequest>(r.Runner.Pending);
        WaitPause(r);
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);                // the menu reads the waiting keys
        Assert.IsType<ModifyRequest>(r.Runner.Pending);
    }

    /// <summary>A magic-user trained at the start-up menu picks a spell on the Learn list (the hall's 0x239D6 mode 4).</summary>
    [Fact]
    public void Train_at_the_start_up_menu_shows_the_learn_list()
    {
        if (Dir() is null)
            return;
        using var r = Start(24, word: Word());
        AddHero(r, classChoice: 2);
        var hero = r.Flow.Party.Current!;
        Assert.Equal(1, hero[TrainingHall.MageLevel]);
        r.Key(Keys.J);
        WaitPause(r);
        r.Key(Keys.T);
        r.Flow.Highlight.ListTop = MenuHighlight.DosListTop;
        r.Key(Keys.Y);
        WaitPause(r);
        Assert.IsType<ListRequest>(r.Runner.Pending);
        r.Key(Keys.None);
        Assert.Equal(0, r.Flow.Highlight.ListTop); // Learn's list (position −1 → 0) moved the shared top (s45)
        r.Key(Keys.Enter);                       // the highlighted row: the last spell
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, hero[TrainingHall.MageLevel]);
    }
}
