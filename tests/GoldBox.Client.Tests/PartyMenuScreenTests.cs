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
        string? word = null)
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
            LoadGame = saves is null ? null : (l, s) => rig!.Game = PorGame.LoadAtPartyMenu(dir, art, Slot(l), who, s),
            Resources = player.Resources,
            ProgramWord = () => word,
        };
        var host = new Host();
        var screen = new PartyMenuScreen(flow, runner, host, art.Font, player.Resources, new ClientArt(dir, art, new ScreenPalette(), PorArtChoice.Dos),
            g => new ExploreScreen(g, player, runner, host)) { Program = program, Items = items };
        rig = new Rig { Screen = screen, Flow = flow, Runner = runner, Host = host, Roster = roster, Tables = tables, Items = items };
        setup?.Invoke(flow, tables, items);
        runner.Start(flow.Run);
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
        r.Key(Keys.Y);
        WaitPause(r);
        Assert.IsType<ListRequest>(r.Runner.Pending);
        r.Key(Keys.Enter);                       // the highlighted row: the last spell
        Assert.IsType<PartyMenuRequest>(r.Runner.Pending);
        Assert.Equal(2, hero[TrainingHall.MageLevel]);
    }
}
