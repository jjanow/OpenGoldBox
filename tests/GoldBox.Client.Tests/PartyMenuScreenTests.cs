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

    private static Rig Start(uint seed)
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
        var flow = new PartyMenuFlow(runner.Ask, new PartyMenuTexts(program, player.Resources), tables, items,
            new PorRoster(roster.FullName), seed)
        {
            NewGame = (party, s) => rig!.Game = PorGame.NewGame(dir, art, party, who, s),
        };
        var host = new Host();
        var screen = new PartyMenuScreen(flow, runner, host, art.Font, player.Resources, new ClientArt(dir, art, new ScreenPalette(), PorArtChoice.Dos),
            g => new ExploreScreen(g, player, runner, host)) { Program = program, Items = items };
        rig = new Rig { Screen = screen, Flow = flow, Runner = runner, Host = host, Roster = roster, Tables = tables, Items = items };
        runner.Start(flow.Run);
        return rig;
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
}
