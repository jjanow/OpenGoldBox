using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>The combat screen's device-free parts.</summary>
public class CombatScreenUnitTests
{
    private static ActionInput Input() => new(InputBindings.Defaults());

    private static int? DirectionFor(KeyboardState k, GamePadState pad = default)
    {
        var i = Input();
        i.Update(k, pad, default, TimeSpan.Zero, _ => null);
        return CombatScreen.Direction(i);
    }

    [Fact]
    public void Keys_give_the_programs_eight_directions()
    {
        Assert.Equal(0, DirectionFor(new KeyboardState(Keys.Up)));
        Assert.Equal(2, DirectionFor(new KeyboardState(Keys.Right)));
        Assert.Equal(4, DirectionFor(new KeyboardState(Keys.NumPad2)));
        Assert.Equal(6, DirectionFor(new KeyboardState(Keys.Left)));
        // The keypad's diagonals (0xA1E6: PgUp 1, PgDn 3, End 5, Home 7) and the digits.
        Assert.Equal(1, DirectionFor(new KeyboardState(Keys.PageUp)));
        Assert.Equal(3, DirectionFor(new KeyboardState(Keys.NumPad3)));
        Assert.Equal(5, DirectionFor(new KeyboardState(Keys.End)));
        Assert.Equal(7, DirectionFor(new KeyboardState(Keys.Home)));
        Assert.Null(DirectionFor(new KeyboardState(Keys.Enter)));
    }

    [Fact]
    public void Two_held_directions_make_a_diagonal_on_the_pad()
    {
        var pad = new GamePadState(new GamePadThumbSticks(new Vector2(-1, 1), Vector2.Zero), default, default, default);
        Assert.True(pad.IsConnected);
        var i = Input();
        i.Update(default, pad, default, TimeSpan.Zero, _ => null);
        Assert.Equal(7, CombatScreen.Direction(i));
        // The right stick turned 45°: up = up-right.
        var rs = new GamePadState(new GamePadThumbSticks(Vector2.Zero, new Vector2(0, 1)), default, default, default);
        var j = Input();
        j.Update(default, rs, default, TimeSpan.Zero, _ => null);
        Assert.Equal(1, CombatScreen.Direction(j));
    }

    [Fact]
    public void Direction_to_a_neighbour_square()
    {
        Assert.Equal(0, CombatScreen.DirectionTo((5, 5), (5, 4)));
        Assert.Equal(3, CombatScreen.DirectionTo((5, 5), (6, 6)));
        Assert.Equal(7, CombatScreen.DirectionTo((5, 5), (4, 4)));
        Assert.Null(CombatScreen.DirectionTo((5, 5), (7, 5)));
        Assert.Null(CombatScreen.DirectionTo((5, 5), (5, 5)));
    }

    [Fact]
    public void The_view_scrolls_as_the_program_does()
    {
        // 0x30F0E: a band of delta squares around the centre (view + 3); outside it the centre goes to the point.
        var v = new CombatView { ViewX = 24, ViewY = 10 };
        Assert.False(v.Rebuild(29, 12, 2));     // view column 5, row 2: inside the band of 2 (s18: DOSBox-X kept columns 3 and 4)
        Assert.Equal((24, 10), (v.ViewX, v.ViewY));
        Assert.True(v.Rebuild(30, 12, 2));      // column 6: the centre moves onto the point's x, y untouched
        Assert.Equal((27, 10), (v.ViewX, v.ViewY));
        Assert.False(v.Rebuild(33, 16, 3));     // a band of 3: anywhere in the view
        Assert.True(v.Rebuild(30, 13, 255));    // 255: always redrawn, centred on the point (band 0)
        Assert.Equal((27, 10), (v.ViewX, v.ViewY));
        Assert.True(v.Rebuild(49, 24, 2));      // the arena's corner: centre kept at 46, 21
        Assert.Equal((CombatArena.Width - 7, CombatArena.Height - 7), (v.ViewX, v.ViewY));
        v.Center(0, 0);
        Assert.Equal((0, 0), (v.ViewX, v.ViewY));

        // 0x310D0: the point plus one step in the direction; the cursor box goes with a redraw and comes back when on.
        v = new CombatView { ViewX = 10, ViewY = 10 };
        v.Scroll(13, 13, 2, 8, cursorSize: 1);
        Assert.Equal((13, 13), v.Cursor);
        v.Scroll(16, 13, 2, 2);                 // 16 + 1 east = 17: out of the band, the view moves and the box is gone
        Assert.Equal((14, 10), (v.ViewX, v.ViewY));
        Assert.Null(v.Cursor);
        v.Scroll(17, 13, 3, 8, cursorSize: 4);
        Assert.Equal((17, 13), v.Cursor);
        Assert.Equal(4, v.CursorSize);
        v.Scroll(17, 13, 3);                    // the cell itself is redrawn: its box goes
        Assert.Null(v.Cursor);
    }

    [Fact]
    public void The_combat_palette_swaps_colours_0_and_8()
    {
        var f = new IndexedFrame(3, 1);
        f[0, 0] = 0;
        f[1, 0] = 8;
        f[2, 0] = 7;
        CombatScreen.SwapGrey(f);
        Assert.Equal(new byte[] { 8, 0, 7 }, f.Pixels);
    }

    [Fact]
    public void Monster_icons_turn_light_magenta_into_dark_grey_on_load()
    {
        // 0x31C34: a CPIC picture is remapped through DS:0CD2 → DS:0CE2 (13 → 8).
        Assert.Equal(new byte[] { 0, 8, 7, 8, 15 }, CombatArt.MonsterColours([0, 13, 7, 8, 15]));
    }

    [Fact]
    public void Default_icon_colours_keep_the_art()
    {
        var c = new EclCharacter();
        byte[] def = [0x91, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7];
        for (int k = 0; k < 6; k++)
            c[CombatArt.IconColours + k] = def[k];
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), CombatArt.ColourMap(c));
        c[CombatArt.IconColours] = 0x5D;        // pair (1, 9) → 13, 5
        var m = CombatArt.ColourMap(c);
        Assert.Equal((13, 5), (m[1], m[9]));
    }

    [Fact]
    public void The_item_screens_ask_through_pages()
    {
        var asked = new List<PlayerRequest>();
        object? answer = null;
        var view = new CombatView();
        var chooser = new CombatChooser(q => { asked.Add(q); return answer; }, view) { Pauses = false };
        var c = new EclCharacter { Name = "Darkstar" };
        byte[] darts = new byte[0x3F], ring = new byte[0x3F];
        darts[FightSpells.ItemCount] = 11;
        darts[CombatRules.ItemEquipped] = 1;
        ring[FightSpells.ItemCount] = 1;
        ItemsEntry[] entries = [ItemsEntry.Ready, ItemsEntry.Use, ItemsEntry.Drop, ItemsEntry.Halve, ItemsEntry.Join, ItemsEntry.Exit];

        // The Items page (from the menu's Use: grey); the answer is the entry and the selected row.
        answer = new ListAnswer(3, 1);
        var pick = chooser.ChooseItem(c, entries, [darts, ring]);
        Assert.Equal(ItemsEntry.Halve, pick.Entry);
        Assert.Same(ring, pick.Item);
        var req = Assert.IsType<PageRequest>(asked[^1]);
        Assert.Equal(["READY", "USE", "DROP", "HALVE", "JOIN", "EXIT"], req.Entries);
        Assert.Equal(5, req.CancelEntry);
        var page = view.Page!;
        Assert.True(page.Grey);
        Assert.Equal([0, 2, 23], page.FrameRows);
        Assert.Equal((1, 1, "DARKSTAR'S"), (page.Texts[0].Col, page.Texts[0].Row, page.Texts[0].Text));
        Assert.Equal(12, page.Texts[1].Col);              // the title at name length + 4
        Assert.StartsWith(" YES  ", page.Rows[0]);
        Assert.StartsWith(" NO   ", page.Rows[1]);
        Assert.Equal(CombatMode.Page, view.Mode);

        // Escape: Exit with no item.
        answer = new ListAnswer(5, 0);
        Assert.Equal(ItemsEntry.Exit, chooser.ChooseItem(c, entries, [darts, ring]).Entry);

        // Drop it?: the warning on the page, No highlighted, the selection hidden while asked.
        answer = 0;
        Assert.True(chooser.ConfirmDrop(c, ring));
        var q = Assert.IsType<ChoiceRequest>(asked[^1]);
        Assert.Equal(1, q.Selected);
        Assert.Equal(chooser.Text[CombatMsg.DropIt], q.MenuPrompt);
        Assert.Empty(view.Page!.Warning);

        // View: the program's prompt, then Items from it is black.
        answer = new ListAnswer(0, 0);
        Assert.Equal(ViewEntry.Items, chooser.ChooseView(c, [ViewEntry.Items, ViewEntry.Exit]));
        Assert.Equal("VIEW:", Assert.IsType<PageRequest>(asked[^1]).Prompt);
        answer = new ListAnswer(0, 0);
        chooser.ChooseItem(c, entries, [darts]);
        Assert.False(view.Page!.Grey);
    }

    [Fact]
    public void The_drop_warning_wraps_into_the_two_row_window()
    {
        var two = CombatChooser.WrapWarning("YOUR 2 POTIONS OF EXTRA HEALING WILL BE GONE FOREVER");
        Assert.Equal(["YOUR 2 POTIONS OF EXTRA HEALING WILL", "BE GONE FOREVER"], two);
        Assert.All(two, l => Assert.True(l.Length <= 38));
        Assert.Equal(["YOUR 5 DARTS WILL BE GONE FOREVER"], CombatChooser.WrapWarning("YOUR 5 DARTS WILL BE GONE FOREVER"));
    }

    [Fact]
    public void Combat_texts_fall_back_to_our_words()
    {
        var t = new CombatText();
        Assert.Equal(0, t.FromGame);
        foreach (var id in Enum.GetValues<CombatMsg>())
            Assert.NotEmpty(t[id]);
        Assert.Equal(2, t.Words(CombatMsg.MenuViewAim).Count);
        Assert.Equal(2, t.Words(CombatMsg.MenuQuickDone).Count);
    }
}

/// <summary>A fight on the combat screen, headless, on save A of the player's files (skipped without GOLDBOX_POR_DIR).</summary>
public class CombatScreenGameTests
{
    private static string? Dir()
    {
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        return string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) ? null : d;
    }

    private sealed class NoHost : IScreenHost
    {
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() { }
        public void SaveBindings() { }
    }

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static Rig Start(uint seed)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = new ClientPlayer(runner, () => game!.Memory);
        player.Chooser.Text = new CombatText(dir.Program());
        player.Chooser.Pauses = false;
        player.TypesText = false;
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!,
            new PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true), seed);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new NoHost()), game, runner, input, player);
    }

    private static void Press(Rig r, GameAction a)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    private static void Click(Rig r, int x, int y)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (r.Screen.MapClick(new ScreenClick(x, y, false)) is { } a)
            r.Input.Inject(a);
        r.Screen.Update(r.Input);
    }

    private static bool AtMainMenu(Rig r) =>
        r.Player.FightView.Active && r.Runner.Pending is ChoiceRequest && r.Player.FightView.Mode == CombatMode.Menu;

    /// <summary>
    /// Save A to the Slums door at 10,2 N, bashed until it opens (the goblin fight), through the text to the first
    /// combat menu. Random encounters on the way are fought on Quick.
    /// </summary>
    private static Rig ToFight()
    {
        for (uint seed = 1; seed < 40; seed++)
        {
            var r = Start(seed);
            GameAction[] route =
            [
                GameAction.Forward, GameAction.TurnLeft, GameAction.Forward, GameAction.Forward, GameAction.Forward,
                GameAction.TurnRight, GameAction.Forward, GameAction.TurnRight, GameAction.Forward, GameAction.Forward,
                GameAction.Forward, GameAction.Forward, GameAction.Forward, GameAction.TurnLeft, GameAction.Forward,
                GameAction.Forward, GameAction.Forward, GameAction.TurnRight, GameAction.Forward,
            ];
            bool clean = true;
            foreach (var a in route)
            {
                Press(r, a);
                if (r.Runner.Pending is not null && a != route[^1])
                    clean = false; // an encounter on the way: try another seed
            }
            if (!clean || r.Runner.Pending is not ChoiceRequest door || door.Items[0] != "BASH")
            {
                r.Runner.Dispose();
                continue;
            }
            Press(r, GameAction.Confirm);                 // Bash
            for (int g = 0; g < 50 && r.Runner.Pending is { } p && !r.Player.FightView.Active; g++)
                Press(r, p is ChoiceRequest { Items: ["BASH", ..] } ? GameAction.Confirm : GameAction.Confirm);
            if (AtMainMenu(r))
                return r;
            r.Runner.Dispose();
        }
        throw new InvalidOperationException("no seed reached the goblin fight");
    }

    [Fact]
    public void The_fight_shows_the_programs_menu_and_plays_to_the_end_on_the_screen()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        // The program's words in its order (0x9E6D): Move first, Quick and Done last.
        Assert.Equal("MOVE", menu.Items[0]);
        Assert.Equal(["QUICK", "DONE"], menu.Items.TakeLast(2));
        Assert.NotNull(v.Current);
        Assert.Same(v.Current, v.Shown);

        // Drawn as the program's combat screen: the menu row's background shows colour 8 (palette swap), the map
        // window has ground tiles, the panel names the creature.
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(8, f[319, 199]);
        Assert.Contains(f.Pixels.Skip(8 * 320).Take(168 * 320), p => p == 7);

        // Move with the keypad, then Escape: back on the starting square (0xA1E6 undoes the whole move).
        var map = (CombatMap)r.Player.FightView.Fight!.Map!;
        var me = v.Current!;
        var start = map.Position(me);
        Key(r, Keys.M);
        Assert.IsType<CombatMoveRequest>(r.Runner.Pending);
        for (int d = 0; d < 8 && map.Position(me) == start; d++)
            if (map.At(start.X + FightMenu.Steps[d].Dx, start.Y + FightMenu.Steps[d].Dy) is null)
                Key(r, d switch { 0 => Keys.Up, 1 => Keys.PageUp, 2 => Keys.Right, 3 => Keys.PageDown, 4 => Keys.Down, 5 => Keys.End, 6 => Keys.Left, _ => Keys.Home });
        Assert.NotEqual(start, map.Position(me));
        Key(r, Keys.Escape);
        Assert.Equal(start, map.Position(me));
        Assert.True(AtMainMenu(r));

        // Aim: the candidates through Next, Exit back to the menu.
        Key(r, Keys.A);
        var aim = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.NotNull(aim.MenuPrompt);
        Assert.StartsWith("RANGE", v.StatusLine);
        var first = v.Shown;
        Key(r, Keys.N);
        Assert.NotSame(first, v.Shown);
        // Manual (Core's 0x155F5, one pass per key): the cursor starts on the shown candidate's square; the panel shows
        // the creature under it, nothing on an empty square; Exit returns to Aim.
        var shown = v.Shown!;
        var hp0 = v.Fight!.List.Select(x => x[FightRules.Hp]).ToArray();
        Key(r, Keys.M);
        var cur = Assert.IsType<CombatCursorRequest>(r.Runner.Pending);
        Assert.Equal(map.Position(shown), (cur.View.X, cur.View.Y));
        Assert.Equal(v.Cursor, (cur.View.X, cur.View.Y));
        var cs = r.Screen.Combat;
        Assert.Same(shown, cs.PanelCreature());
        for (int k = 0; k < 6 && map.At(v.Cursor!.Value.X, v.Cursor.Value.Y) is not null; k++)
            Key(r, Keys.Down);
        cur = Assert.IsType<CombatCursorRequest>(r.Runner.Pending);
        Assert.Null(map.At(cur.View.X, cur.View.Y));
        Assert.Null(cs.PanelCreature());
        Assert.False(cur.View.TargetOffered);          // a bare square: only a spell's square mode takes one
        Press(r, GameAction.Cancel);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);

        // Return on a square where Target is not offered still ends Manual (the program sets CS:0x2DA7): back to Aim,
        // no attack. A click on another square walks the cursor there one pass at a time.
        Key(r, Keys.M);
        var at = Assert.IsType<CombatCursorRequest>(r.Runner.Pending).View;
        var goal = (X: Math.Clamp(at.X + 2, 0, CombatArena.Width - 1), Y: at.Y);
        Click(r, CombatScreen.MapX + (goal.X - v.ViewX) * 24 + 12, CombatScreen.MapY + (goal.Y - v.ViewY) * 24 + 12);
        var walked = Assert.IsType<CombatCursorRequest>(r.Runner.Pending).View;
        Assert.Equal(goal, (walked.X, walked.Y));
        if (!walked.TargetOffered)
        {
            Press(r, GameAction.Confirm);
            Assert.Equal(CombatMode.Aim, v.Mode);
            Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        }
        else
            Press(r, GameAction.Cancel);
        Assert.Equal(hp0, v.Fight.List.Select(x => x[FightRules.Hp]).ToArray());

        // A click on another creature shows it (no attack); a second click on it targets it and leaves Aim.
        var hp = v.Fight!.List.Select(x => x[FightRules.Hp]).ToArray();
        var other = v.Fight.List.First(x => map.OnMap(x) && !ReferenceEquals(x, v.Shown) && !ReferenceEquals(x, me)
            && map.Position(x) is var (ox, oy) && ox - v.ViewX is >= 0 and < 7 && oy - v.ViewY is >= 0 and < 7);
        (int, int) Pixel(EclCharacter x) =>
            (CombatScreen.MapX + (map.Position(x).X - v.ViewX) * 24 + 12, CombatScreen.MapY + (map.Position(x).Y - v.ViewY) * 24 + 12);
        var (px, py) = Pixel(other);
        Click(r, px, py);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.Same(other, v.Shown);
        Assert.Equal(hp, v.Fight.List.Select(x => x[FightRules.Hp]).ToArray());
        if (r.Runner.Pending is ChoiceRequest { Items: var items } && items.Contains("TARGET"))
        {
            (px, py) = Pixel(other);
            Click(r, px, py);
            Assert.NotEqual(CombatMode.Aim, v.Mode);
            // An ally on the player's side brings the attack-ally question: answer no.
            if (v.Mode == CombatMode.Question)
                Press(r, GameAction.Cancel);
        }
        else
            Press(r, GameAction.Cancel);

        // Everyone on Quick to the end; refuse to go on fighting.
        for (int g = 0; g < 400 && v.Active; g++)
        {
            if (AtMainMenu(r))
                Key(r, Keys.Q);
            else if (r.Runner.Pending is ChoiceRequest { CancelIndex: not null } && v.Mode is CombatMode.Question or CombatMode.Aim)
                Press(r, GameAction.Cancel);              // Continue battle: No; leave an aim menu
            else
                Press(r, GameAction.Confirm);
        }
        Assert.False(v.Active);
        Assert.False(r.Game.Loop.Defeated);
        r.Runner.Dispose();
    }

    [Fact]
    public void View_and_Use_open_the_programs_item_screens_in_a_fight()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var fight = v.Fight!;
        Assert.IsType<FightItemScreens>(Assert.IsType<MenuController>(fight.Controller).Screens);
        var me = v.Current!;
        Assert.NotEmpty(me.Items);

        // Use (0x21F32): the Items page on the combat palette, the program's entries, Exit back to the menu.
        Key(r, Keys.U);
        var use = Assert.IsType<PageRequest>(r.Runner.Pending);
        Assert.Equal(CombatMode.Page, v.Mode);
        Assert.Equal(["READY", "USE"], use.Entries.Take(2));
        Assert.Equal("EXIT", use.Entries[^1]);
        Assert.Null(use.Prompt);
        var page = v.Page!;
        Assert.True(page.Grey);
        Assert.Equal(me.Items.Count, page.Rows.Count);
        Assert.Contains(page.Texts, t => t.Row == 3 && t.Text == "READY ITEM");
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(8, f[100, 100]);                    // grey background (0 ↔ 8)
        int count = me.Items.Count;
        // Halve on a stack, then Join: the list grows by one, then shrinks back.
        int stack = me.Items.FindIndex(i => i[FightSpells.ItemCount] >= 2);
        if (stack >= 0)
        {
            page.Selected = stack;
            Key(r, Keys.H);
            Assert.Equal(count + 1, me.Items.Count);
            Key(r, Keys.J);
            Assert.Equal(count, me.Items.Count);
        }
        // Drop of a readied item: refused ("must be unreadied"), nothing asked.
        int readied = me.Items.FindIndex(i => i[CombatRules.ItemEquipped] != 0);
        if (readied >= 0)
        {
            v.Page!.Selected = readied;
            Key(r, Keys.D);
            Assert.IsType<PageRequest>(r.Runner.Pending);
            Assert.Equal(count, me.Items.Count);
        }
        Key(r, Keys.E);
        Assert.True(AtMainMenu(r));
        Assert.Null(v.Page);

        // View (0x21AED): "VIEW:" and its entries; Items from it is drawn black; Escape twice back to the menu.
        Key(r, Keys.V);
        var view = Assert.IsType<PageRequest>(r.Runner.Pending);
        Assert.Equal("VIEW:", view.Prompt);
        Assert.Equal("ITEMS", view.Entries[0]);
        Key(r, Keys.I);
        Assert.IsType<PageRequest>(r.Runner.Pending);
        Assert.False(v.Page!.Grey);
        Press(r, GameAction.Cancel);
        Assert.Equal("VIEW:", Assert.IsType<PageRequest>(r.Runner.Pending).Prompt);
        Press(r, GameAction.Cancel);
        Assert.True(AtMainMenu(r));
        Assert.Same(me, v.Current);

        // Drop money (0x234E1) from View: the coin list (Return picks the row), the amount typed, one coin less.
        if (FightItemScreens.ViewEntries(me, FightItemScreens.InFight).Contains(ViewEntry.Drop))
        {
            // The coin list is the sheet's money lines: the highest coin kind held first (0x21547).
            int kind = CombatSheet.MoneyKinds(me)[0];
            ushort before = FightItemScreens.Coins(me, kind);
            Key(r, Keys.V);
            Key(r, Keys.D);
            var coins = Assert.IsType<PageRequest>(r.Runner.Pending);
            Assert.True(coins.PickRows);
            Assert.Equal(0, v.Page!.Selected);
            Press(r, GameAction.Confirm);
            Assert.IsType<TextRequest>(r.Runner.Pending);
            Key(r, Keys.D1);
            Press(r, GameAction.Confirm);
            Assert.Equal(before - 1, FightItemScreens.Coins(me, kind));
            for (int g = 0; g < 4 && !AtMainMenu(r); g++)
                Press(r, GameAction.Cancel);
            Assert.True(AtMainMenu(r));
        }
        r.Runner.Dispose();
    }

    [Fact]
    public void The_game_files_give_the_programs_fight_texts_and_spell_names()
    {
        if (Dir() is null)
            return;
        var program = new PorGameDirectory(Dir()!).Program()!;
        var t = new CombatText(program);
        Assert.Equal(Enum.GetValues<CombatMsg>().Length, t.FromGame);
        Assert.Equal(["MOVE", "VIEW", "AIM", "USE", "CAST", "TURN", "QUICK", "DONE"],
            [.. t.Words(CombatMsg.MenuMove), .. t.Words(CombatMsg.MenuViewAim), .. t.Words(CombatMsg.MenuUse),
             .. t.Words(CombatMsg.MenuCast), .. t.Words(CombatMsg.MenuTurn), .. t.Words(CombatMsg.MenuQuickDone)]);
        var spells = new SpellNames(program.SpellNames);
        Assert.True(spells.Count >= PorSpellNames.MinRun);
        Assert.All(Enumerable.Range(1, spells.Count), i => Assert.False(string.IsNullOrWhiteSpace(spells[i])));
    }

    [Fact]
    public void Party_icons_are_composed_from_the_art()
    {
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var art = new CombatArt(dir);
        Assert.Equal(25, art.Dungeon.Count);
        Assert.NotNull(art.Ground(22, true));
        Assert.All(art.Ground(22, true)!.Pixels, p => Assert.Equal(0, p)); // open ground: colour 0, shown grey
        var party = PorParty.Load(Enumerable.Range(1, 6).Select(i => $"CHRDATA{i}"), dir.Find);
        Assert.Equal(6, party.Members.Count);
        foreach (var m in party.Members)
        {
            var icon = art.Party(m, false);
            Assert.NotNull(icon);
            Assert.Equal((24, 24), (icon!.Width, icon.Height));
            Assert.Contains(icon.Pixels, p => p != 0);
        }
        Assert.NotNull(art.Monster(2, false));
        Assert.NotNull(art.Monster(2, true));
        Assert.NotNull(art.Sprite(CombatArt.HitMarker));
    }

    [Theory]
    [InlineData(26, "bcb363a456232bd31955989bc3162a10f742d644772fc2c07e12c3d934b9e5fd")]
    [InlineData(27, "7faf5e1bbb76a5e0fb0af1191b9fd27165c8a51336c1d23fbbdb6e7c4b8b0167")]
    [InlineData(31, "de5da7e892b12fe3114f00117a1f268bb537dedb4300392d2d004a9f140e16df")]
    public void Rare_terrain_in_a_dungeon_fight_matches_dosbox(int terrain, string sha256)
    {
        // DOSBox-X s24 (save K's Slums fight, fight_casts' setup, several boots): 24 × 24 cells of terrain 26 and 27
        // (rare features, scrolled into view with Manual aim) and 31 (two bodies) reduced to EGA indices as shown,
        // i.e. with the combat palette's 0/8 swap; every sample of a terrain gave the same cell.
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var tables = PorCombatTables.Open(dir)!;
        Assert.Equal(CombatArt.RareFirstTile + terrain - 26, tables.Tile(terrain));
        var tile = new CombatArt(dir).Ground(tables.Tile(terrain), dungeon: true)!;
        var shown = tile.Pixels.Select(p => p switch { 0 => (byte)8, 8 => (byte)0, var v => p }).ToArray();
        Assert.Equal(sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(shown)));
    }

    [Fact]
    public void Every_rare_terrain_draws_a_randcom_tile()
    {
        // Terrains 0x1A–0x1F (features 26–29, the area-spell cloud 30, a body 31) carry tiles 0x22–0x27 in a dungeon too.
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var tables = PorCombatTables.Open(dir)!;
        var art = new CombatArt(dir);
        for (int t = 0x1A; t <= 0x1F; t++)
            Assert.Same(art.Rare[t - 0x1A], art.Ground(tables.Tile(t), dungeon: true));
    }

    [Theory]
    [InlineData(23, "fbdf93999e5fdc8d5d8b73b72739f562b4a9f63d75691de03fb57c8545fe98ad")]
    [InlineData(33, "6369cb22ae8f8b31caebb503ab6aa3d1c849c3a800457fd4cb17781718a1da5b")]
    [InlineData(37, "a52865a7d20add2fe871da439d2325d3ff95ac78389f105223232b26f9877414")]
    [InlineData(42, "fc238a145476c5d28a34fe400e26c22d9b1455aa4a70d9ca830bdb02dfd0de8f")]
    [InlineData(44, "9c003805384cedb6ccf4cddd99f2466132f1a2de9e4f2f6ad3fa2f62a6466519")]
    [InlineData(46, "ea732a7dacb62ace4306234738680708cad0ebcfad2b6b46d8da332266d315a7")]
    [InlineData(48, "ce5195c104d674cddbe539848f27e785d71cfec975b07cf3af0a5342217047aa")]
    [InlineData(57, "d0b99609c594f49a9ad2b93ff0a48bc441009ca326c54fe3e31fcd5585e73f5b")]
    public void Wilderness_ground_in_an_outdoor_fight_matches_dosbox(int terrain, string sha256)
    {
        // DOSBox-X s26 (MACHINE=ega): save A put on script 26's overland at 12,13 N (goldbox-tools save-overland), its
        // vector 0 = CLRMON, LOADMON 1 ×2, COMBAT, one step north: an outdoor fight (DS:49FA = 3). The first screen's
        // 7 × 7 cells with no creature on them, reduced to EGA indices as shown (the combat palette's 0/8 swap), against
        // the arena's terrain from the state at that screen: every cell of a terrain gave the same picture.
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var tables = PorCombatTables.Open(dir)!;
        var tile = new CombatArt(dir).Ground(tables.Tile(terrain), dungeon: false)!;
        var shown = tile.Pixels.Select(p => p switch { 0 => (byte)8, 8 => (byte)0, var v => p }).ToArray();
        Assert.Equal(sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(shown)));
    }

    [Fact]
    public void Every_wilderness_terrain_draws_a_wildcom_tile()
    {
        // combat-menu.md "Ground, program read" (s26): outside a 3D area 0xD815 loads WILDCOM block 1 at tile 0 (the
        // loader copies the block's own 34 items, up to RANDCOM's first tile 0x22) and the ground draws index the sheet
        // by the terrain's tile byte with no special case. Every terrain the wilderness builder writes, over the whole
        // overland map and many seeds, has a WILDCOM or RANDCOM tile: 23 → 22, 26–31 → RANDCOM 0–5, 32–65 → 0–33.
        if (Dir() is null)
            return;
        var dir = new PorGameDirectory(Dir()!);
        var tables = PorCombatTables.Open(dir)!;
        var art = new CombatArt(dir);
        Assert.Equal(CombatArt.RareFirstTile, art.Wilderness.Count);
        Assert.Equal(6, art.Rare.Count);
        var seen = new SortedSet<int>();
        for (uint seed = 1; seed <= 12; seed++)
        {
            var rng = new TurboPascalRandom(seed * 7919);
            for (int y = 0; y < PorCombatTables.WildRows; y++)
                for (int x = 0; x < CombatTables.WildMapStride; x++)
                {
                    var a = new CombatArena();
                    ArenaBuilder.BuildWilderness(a, tables, x, y, 2, seed % 2 == 0, 20, rng.Next);
                    foreach (byte t in a.Cells())
                        seen.Add(t);
                }
        }
        Assert.Contains(23, seen);
        Assert.Contains(50, seen);
        foreach (int t in seen)
        {
            int tile = tables.Tile(t);
            var expected = t switch
            {
                23 => art.Wilderness[22],
                >= 0x1A and <= 0x1F => art.Rare[t - 0x1A],
                >= 32 and <= 65 => art.Wilderness[t - 32],
                _ => throw new Xunit.Sdk.XunitException($"terrain {t} outdoors"),
            };
            Assert.Same(expected, art.Ground(tile, dungeon: false));
        }
        // A dungeon's terrains (1–25 → DUNGCOM 0–24) never reach the sheet's tiles 0x19–0x21.
        for (int t = 1; t <= 25; t++)
            Assert.Same(art.Dungeon[tables.Tile(t)], art.Ground(tables.Tile(t), dungeon: true));
    }
}

/// <summary>The controls screen with more actions than rows: the mouse reaches everything.</summary>
public class ControlsScreenTests
{
    private sealed class Host : IScreenHost
    {
        public bool Quitted;
        public void Push(IScreen screen) { }
        public void Pop() { }
        public void Quit() => Quitted = true;
        public void SaveBindings() { }
    }

    [Fact]
    public void Hovering_keeps_the_rows_still_and_the_extras_are_always_clickable()
    {
        var host = new Host();
        var font = new TextFont(new byte[177 * 8]);
        var screen = new ControlsScreen(font, host);
        var input = new ActionInput(InputBindings.Defaults());
        var before = new IndexedFrame(320, 200);
        screen.Draw(before, input);
        for (int pass = 0; pass < 3; pass++)
            for (int row = 3; row <= 21; row++)
                screen.Hover(20, row * 8 + 4);
        var after = new IndexedFrame(320, 200);
        screen.Draw(after, input);
        // The action labels did not move (compare the label columns of rows 3–17, not the ">" marker).
        for (int y = 3 * 8; y < 18 * 8; y++)
            for (int x = 16; x < 320; x++)
                Assert.Equal(before[x, y], after[x, y]);
        // QUIT: the last extra, row 21.
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (screen.MapClick(new ScreenClick(20, 21 * 8 + 4, false)) is { } a)
            input.Inject(a);
        screen.Update(input);
        Assert.True(host.Quitted);
    }
    private static void Frame(ControlsScreen s, ActionInput i, Keys k = Keys.None, GameAction? a = null)
    {
        i.Update(k == Keys.None ? default : new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        if (a is { } act)
            i.Inject(act);
        s.Update(i);
        i.Update(default, default, default, TimeSpan.Zero, _ => null);
        s.Update(i);
    }

    /// <summary>The start word row (the hall's J word): keyboard typing, the pad's grid and the mouse each set it.</summary>
    [Fact]
    public void The_start_word_is_set_with_keys_pad_or_mouse()
    {
        string? stored = null;
        var word = new WordSetting(() => stored, w => stored = w);
        var font = new TextFont(new byte[177 * 8]);
        var input = new ActionInput(InputBindings.Defaults());
        // Rows: the actions (with TAKE CONTROL and HALL J), then the word row (no art row), reset, back, quit.
        var screen = new ControlsScreen(font, new Host(), null, word);
        bool there = false;
        void ToWordRow()
        {
            if (!there)
                for (int i = 0; i < 4; i++)
                    Frame(screen, input, a: GameAction.MenuUp);  // up from the first action wraps: quit, back, reset, word
            there = true;                                        // the row stays selected after an edit
            Frame(screen, input, a: GameAction.Confirm);
        }
        // Keyboard: type A, B, Backspace, C, Enter.
        ToWordRow();
        Frame(screen, input, Keys.A);
        Frame(screen, input, Keys.B);
        Frame(screen, input, Keys.Back);
        Frame(screen, input, Keys.C);
        Frame(screen, input, Keys.Enter);
        Assert.Equal("AC", stored);
        // Pad: Cancel deletes, the grid starts on A; right and Confirm types B; down three times reaches OK.
        ToWordRow();
        Frame(screen, input, a: GameAction.Cancel);      // deletes C
        Frame(screen, input, a: GameAction.Cancel);      // deletes A
        Frame(screen, input, a: GameAction.MenuRight);
        Frame(screen, input, a: GameAction.Confirm);     // B
        for (int i = 0; i < 3; i++)
            Frame(screen, input, a: GameAction.MenuDown);
        Frame(screen, input, a: GameAction.Confirm);     // OK
        Assert.Equal("B", stored);
        // Mouse: click Z then OK (row 6 + 1 for N–Z; OK on the last row at column 8).
        ToWordRow();
        Assert.Null(screen.MapClick(new ScreenClick((2 + 2 * 12) * 8 + 2, 7 * 8 + 4, false)));
        Assert.Null(screen.MapClick(new ScreenClick(8 * 8 + 4, 9 * 8 + 4, false)));
        Assert.Equal("BZ", stored);
        // Cancel with nothing typed gives up and leaves it as it was.
        ToWordRow();
        Frame(screen, input, a: GameAction.Cancel);
        Frame(screen, input, a: GameAction.Cancel);
        Frame(screen, input, a: GameAction.Cancel);      // nothing left: gives up
        Assert.Equal("BZ", stored);
    }
}
