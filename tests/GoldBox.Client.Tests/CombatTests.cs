using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
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
    public void A_scrolls_spells_come_on_the_spell_lists_screen()
    {
        // 0x22AC4 → 0x239D6 kind 2, action 1 (D s38): headings, the list's own screen titled "Spells on Scroll", the
        // choose-spell prompt with Cast and Exit, the combat palette; the pick is the spell id.
        var asked = new List<PlayerRequest>();
        var view = new CombatView();
        var table = new SpellTable(new Dictionary<int, SpellInfo>
        {
            [10] = new(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
            [35] = new(1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0),
        });
        var chooser = new CombatChooser(q => { asked.Add(q); return new ListAnswer(0, 3); }, view)
        {
            Pauses = false,
            Resources = () => new ClientResources { Spells = table },
        };
        var c = new EclCharacter { Name = "Darkstar" };
        var scroll = new byte[0x3F];
        (scroll[0x3C], scroll[0x3D]) = (10, 35);
        Assert.Equal(35, chooser.ChooseScrollSpell(c, scroll));
        var req = Assert.IsType<ListRequest>(asked[^1]);
        Assert.Equal(4, req.Rows.Count);                   // 1st level, the spell, 2nd level, the spell
        Assert.Equal(new HashSet<int> { 0, 2 }, req.Labels);
        Assert.True(req.Grey);
        Assert.Equal(SpellLists.FirstRow, req.FirstRow);
        Assert.Equal(2, req.Actions.Count);
        Assert.EndsWith("ON SCROLL", req.Spell!.Title);
        Assert.NotNull(req.Spell.Prompt);

        scroll[0x35] = 1;                                  // a hidden scroll lists nothing
        int before = asked.Count;
        Assert.Equal(0, chooser.ChooseScrollSpell(c, scroll));
        Assert.Equal(before, asked.Count);
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
    private static string? Dir() => TestRig.PorDir();

    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static Rig Start(uint seed)
    {
        var dir = new PorGameDirectory(Dir()!);
        var art = PorViewArt.Load(dir);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Chooser.Text = new CombatText(dir.Program());
        player.Chooser.Pauses = false;
        game = PorGame.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!,
            new PorPlayer(player, player, player, player, FightMenu: player.Chooser, QuickFight: true), seed);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost()), game, runner, input, player);
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
    public void A_charmed_member_keeps_the_party_icon()
    {
        // DOSBox-X s29 (a vampire's charm gaze 84 on Brother Sean): the charm handler 0xF05B sets the NPC byte to 0xB3,
        // and the member is still drawn with his own icon (cast box, moves, attacks), not left blank.
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var me = v.Current!;
        var (x, y) = ((CombatMap)v.Fight!.Map!).Position(me);
        v.Cursor = null;
        byte[] Cell()
        {
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            int x0 = CombatScreen.MapX + (x - v.ViewX) * 24, y0 = CombatScreen.MapY + (y - v.ViewY) * 24;
            var cell = new byte[24 * 24];
            for (int j = 0; j < 24; j++)
                for (int i = 0; i < 24; i++)
                    cell[j * 24 + i] = f[x0 + i, y0 + j];
            return cell;
        }
        var own = Cell();
        byte npc = me[CombatRules.Npc];
        me[CombatRules.Npc] = EffectHandlers.AnimatedMember;
        Assert.Equal(own, Cell());
        me[CombatRules.Npc] = npc;
    }

    [Fact]
    public void A_joined_npc_is_drawn_with_the_composed_party_icon()
    {
        // ADDNPC (0x1F248): the NPC byte is 0x80 OR morale / 2, and the party join 0x1F42C builds its slot's picture
        // with 0x1E468 from the record (CHEAD 0xBD, CBODY 0xBE, colours 0xC1–0xC6) as for a player character; no CPIC
        // is loaded for it. DOSBox-X s39 (ADDNPC 94, the level 3 magic-user): drawn with that icon, the Client left the
        // square empty. A party-side creature (combat record +0x13 = 0) with an NPC byte draws the composed icon.
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var me = v.Current!;
        var (x, y) = ((CombatMap)v.Fight!.Map!).Position(me);
        v.Cursor = null;
        byte[] Cell()
        {
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            int x0 = CombatScreen.MapX + (x - v.ViewX) * 24, y0 = CombatScreen.MapY + (y - v.ViewY) * 24;
            var cell = new byte[24 * 24];
            for (int j = 0; j < 24; j++)
                for (int i = 0; i < 24; i++)
                    cell[j * 24 + i] = f[x0 + i, y0 + j];
            return cell;
        }
        var own = Cell();
        byte npc = me[CombatRules.Npc], slot = me[0xBF];
        me[CombatRules.Npc] = 0x80 | 50;
        me[0xBF] = 6; // a party slot with no CPIC loaded
        Assert.False(v.IconPictures.ContainsKey(6));
        Assert.Equal(0, v.Fight.Record(me)[CombatRecord.NotParty]);
        Assert.Equal(own, Cell());
        me[CombatRules.Npc] = npc;
        me[0xBF] = slot;
    }

    [Fact]
    public void The_downed_picture_is_drawn_on_the_ground_not_over_the_icon()
    {
        // DOSBox-X s30 (a scorpion's poisoned hit killing Rhiannon): COMSPR 11's see-through pixels show the ground,
        // not the creature's icon under it (s29's "3 skull pixels" black in the Client).
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var me = v.Current!;
        var map = (CombatMap)v.Fight!.Map!;
        var (x, y) = map.Position(me);
        v.Cursor = null;
        byte[] Cell()
        {
            var f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            int x0 = CombatScreen.MapX + (x - v.ViewX) * 24, y0 = CombatScreen.MapY + (y - v.ViewY) * 24;
            var cell = new byte[24 * 24];
            for (int j = 0; j < 24; j++)
                for (int i = 0; i < 24; i++)
                    cell[j * 24 + i] = f[x0 + i, y0 + j];
            return cell;
        }
        v.HitMarker = (x, y);
        var withIcon = Cell();
        map.SetSlotSize(map.Slot(me), 0); // the same square with no creature
        var bare = Cell();
        Assert.Equal(bare, withIcon);
        // COMSPR 11's colour-0 pixels are drawn (grey on screen, colour 8 after the combat palette swap), not see-through.
        var skull = new CombatArt(new PorGameDirectory(Dir()!)).Sprite(CombatArt.HitMarker)!;
        for (int j = 0; j < 24; j++)
            for (int i = 0; i < 24; i++)
                if (skull.Pixels[j * skull.Width + i] == 0)
                    Assert.Equal(8, withIcon[j * 24 + i]);
        v.HitMarker = null;
    }

    [Fact]
    public void The_downed_picture_covers_every_cell_of_the_footprint()
    {
        // CreatureDowned 0x315FE–0x31682 draws COMSPR 11 on each cell 0x3073A gives for the slot's icon size (i = 0..3):
        // DOSBox-X s38/s39, the wild boar (2 × 1) shows it on both cells.
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var me = v.Current!;
        var map = (CombatMap)v.Fight!.Map!;
        v.Cursor = null;
        // A square with its right and lower neighbours in view.
        int x = v.ViewX + 2, y = v.ViewY + 2;
        var skull = new CombatArt(new PorGameDirectory(Dir()!)).Sprite(CombatArt.HitMarker)!;
        Assert.Equal(24, skull.Width);
        var f = new IndexedFrame(320, 200);
        bool IsSkull(int cx, int cy)
        {
            int x0 = CombatScreen.MapX + (cx - v.ViewX) * 24, y0 = CombatScreen.MapY + (cy - v.ViewY) * 24;
            for (int j = 0; j < 24; j++)
                for (int i = 0; i < 24; i++)
                    if (f[x0 + i, y0 + j] != CombatPalette(skull.Pixels[j * 24 + i]))
                        return false;
            return true;
        }
        foreach (var (size, cells) in new[] { (1, 1), (2, 2), (3, 2), (4, 4) })
        {
            v.HitMarker = (x, y);
            v.HitMarkerSize = size;
            f = new IndexedFrame(320, 200);
            r.Screen.Draw(f, r.Input);
            var expected = new HashSet<(int, int)>();
            for (int i = 0; i < 4; i++)
                if (map.Tables.IconCell(size, i, out int dx, out int dy))
                    expected.Add((x + dx, y + dy));
            Assert.Equal(cells, expected.Count);
            for (int cy = y - 1; cy <= y + 2; cy++)
                for (int cx = x - 1; cx <= x + 2; cx++)
                    Assert.Equal(expected.Contains((cx, cy)), IsSkull(cx, cy));
        }
        v.HitMarker = null;
        v.HitMarkerSize = 1;
    }

    /// <summary>The fight's palette swap (0 and 8: dark grey; see the colour-0 check above).</summary>
    private static byte CombatPalette(byte c) => c switch { 0 => 8, 8 => 0, _ => c };

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
        // View's keys (0x21AED, keypad flag 0): a keyboard Return gives 13, which View ignores; '.' lights the next word.
        Key(r, Keys.Enter);
        Assert.Same(view, r.Runner.Pending);
        Key(r, Keys.OemPeriod);
        Key(r, Keys.OemComma);
        Assert.Same(view, r.Runner.Pending);
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

    /// <summary>An empty frame: the screen syncs to the request now pending (as the next drawn frame would).</summary>
    private static void Idle(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
    }

    /// <summary>
    /// A readied wand put first in the current creature's list: the capture's wand (fight_manual_use, ITEM1.DAX block 9
    /// item 3: type 79, 20 charges, power 88 = spell 65), its other bytes those of the creature's first item.
    /// </summary>
    private static byte[] GiveWand(EclCharacter me)
    {
        var wand = (byte[])me.Items[0].Clone();
        wand[0x2E] = 79;
        wand[CombatRules.ItemEquipped] = 1;
        wand[FightSpells.ItemCount] = 0;
        wand[0x3C] = 20;
        wand[0x3D] = 88;
        wand[0x3E] = 0;
        me.Items.Insert(0, wand);
        me[FightItemScreens.ItemCount] = (byte)me.Items.Count;
        return wand;
    }

    /// <summary>The pixel at the middle of a creature's square on the combat map.</summary>
    private static (int X, int Y) Pixel(CombatView v, EclCharacter c)
    {
        var (x, y) = ((CombatMap)v.Fight!.Map!).Position(c);
        return (CombatScreen.MapX + (x - v.ViewX) * 24 + 12, CombatScreen.MapY + (y - v.ViewY) * 24 + 12);
    }

    [Fact]
    public void Manuals_return_takes_target_with_the_lit_entry_kept()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var fight = v.Fight!;
        var me = v.Current!;
        var wand = GiveWand(me);
        Key(r, Keys.U);
        Key(r, Keys.U);
        for (int g = 0; g < 10 && !(r.Runner.Pending is ChoiceRequest && v.Mode == CombatMode.Aim); g++)
            Press(r, GameAction.Confirm);
        for (int g = 0; g < 20 && v.Shown is { } s && s[FightRules.Hostile] == me[FightRules.Hostile]; g++)
            Key(r, Keys.N);
        var target = v.Shown!;
        Assert.NotEqual(me[FightRules.Hostile], target[FightRules.Hostile]);

        // Manual on the enemy: Target, Center, Exit. ',' / '.' light another entry inside the menu routine (0x2CE93 /
        // 0x2CEB7: the cell moves, no pass of the cursor).
        Key(r, Keys.M);
        var cur = Assert.IsType<CombatCursorRequest>(r.Runner.Pending);
        Assert.True(cur.View.TargetOffered);
        int lit = r.Screen.MenuHighlight;
        Key(r, Keys.OemPeriod);
        Assert.Same(cur, r.Runner.Pending);
        Assert.Equal((lit + 1) % 3, r.Screen.MenuHighlight);
        Key(r, Keys.OemComma);
        Key(r, Keys.OemComma);
        Assert.Equal((lit + 2) % 3, r.Screen.MenuHighlight);
        if (r.Screen.MenuHighlight == 0)
            Key(r, Keys.OemPeriod);
        lit = r.Screen.MenuHighlight;
        Assert.NotEqual(0, lit);

        // Return gives 13 (0x2CE7D), which Manual takes as Target (0x15979); the cell is not written, so the line keeps
        // Center or Exit lit (T would move it to Target, 0x2CF48).
        Key(r, Keys.Enter);
        Assert.Equal(19, wand[0x3C]);
        Assert.Same(target, fight.Record(me).Other);
        Assert.Equal(lit, r.Screen.MenuHighlight);
        r.Runner.Dispose();
    }

    [Fact]
    public void An_items_spell_is_aimed_on_the_map_by_keys_pad_and_mouse()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var fight = v.Fight!;
        var me = v.Current!;
        var wand = GiveWand(me);

        // Use (0x21F32): the Items page; U on the wand (row 0) runs 0x22AC4: the item message, then the aim 0x15D53.
        Key(r, Keys.U);
        Assert.IsType<PageRequest>(r.Runner.Pending);
        Assert.Equal(0, v.Page!.Selected);
        Key(r, Keys.U);
        for (int g = 0; g < 10 && !(r.Runner.Pending is ChoiceRequest && v.Mode == CombatMode.Aim); g++)
            Press(r, GameAction.Confirm);              // the item message's pause
        var aim = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.NotNull(aim.MenuPrompt);
        // The aim is drawn on the combat map, not on the Items page.
        Assert.Null(v.Page);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        Assert.Equal(8, f[319, 199]);
        Assert.Same(v.Shown, r.Screen.Combat.PanelCreature());
        // The Items list left the shared cell on Use (its 2nd entry, the hotkey 0x2CF48), so the aim menu opens on
        // its 2nd entry, Prev (P: read, not seen in DOSBox-X).
        Assert.Equal(1, r.Screen.MenuHighlight);

        // Next by key, Prev by the pad's shoulder: back on the first candidate.
        var first = v.Shown;
        Key(r, Keys.N);
        var second = v.Shown;
        Press(r, GameAction.PreviousMember);
        Assert.Same(first, v.Shown);
        Assert.NotSame(first, second);

        // Manual: the cursor on the shown candidate's square; Escape back to the aim.
        Key(r, Keys.M);
        var cur = Assert.IsType<CombatCursorRequest>(r.Runner.Pending);
        Assert.Null(v.Page);
        Assert.Equal(((CombatMap)fight.Map!).Position(first!), (cur.View.X, cur.View.Y));
        Assert.False(cur.View.Attack);                 // a spell's aim (attack mode 0)
        Key(r, Keys.Escape);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);

        // Exit: nothing picked, the abort question (0x27EF2, the yes/no 0x2D9A0 opens on No); No aims again, and the
        // aim menu opens on the cell the answer left (No, the 2nd entry).
        Key(r, Keys.E);
        Assert.Equal(CombatMode.Question, v.Mode);
        Idle(r);
        Assert.Equal(1, r.Screen.MenuHighlight);
        Press(r, GameAction.Confirm);                  // Return on the highlight: No
        Assert.Equal(CombatMode.Aim, v.Mode);
        Idle(r);
        Assert.Equal(1, r.Screen.MenuHighlight);
        Assert.Equal(20, wand[0x3C]);

        // Step to an enemy with Next, then a click on it takes it (Target), the wand's spell hits it.
        for (int g = 0; g < 20 && v.Shown is { } s && s[FightRules.Hostile] == me[FightRules.Hostile]; g++)
            Key(r, Keys.N);
        var target = v.Shown!;
        Assert.NotEqual(me[FightRules.Hostile], target[FightRules.Hostile]);
        Assert.Contains("TARGET", Assert.IsType<ChoiceRequest>(r.Runner.Pending).Items);
        int hp = target[FightRules.Hp];
        var (px, py) = Pixel(v, target);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        Assert.Null(r.Screen.MapClick(new ScreenClick(px, py, false)));
        r.Screen.Update(r.Input);
        Assert.Equal(19, wand[0x3C]);
        Assert.Same(target, fight.Record(me).Other);
        Assert.True(target[FightRules.Hp] < hp || target[FightRules.InPlay] == 0 || target[FightRules.Status] != 0);
        // The wand's spell ends the creature's turn: the next menu is someone else's.
        for (int g = 0; g < 40 && !(AtMainMenu(r) || !v.Active); g++)
            Press(r, GameAction.Confirm);
        Assert.NotSame(me, v.Current);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_scrolls_spell_is_aimed_on_the_map_not_under_the_items_page()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var fight = v.Fight!;
        var me = v.Current!;
        // A readied scroll (base item slot 11–13, 0x2A255) holding the wand's spell 65.
        int type = Enumerable.Range(1, 254).First(t => fight.Items[t, BaseItemTable.Slot] is >= 11 and <= 13);
        var scroll = (byte[])me.Items[0].Clone();
        scroll[0x2E] = (byte)type;
        scroll[CombatRules.ItemEquipped] = 1;
        scroll[0x3C] = 65;
        scroll[0x3D] = 0;
        scroll[0x3E] = 0;
        me.Items.Insert(0, scroll);
        me[FightItemScreens.ItemCount] = (byte)me.Items.Count;

        // Use, U on the scroll: its spell list (0x239D6 → 0x2D669: the cell to its first entry, Cast), Return casts.
        Key(r, Keys.U);
        Key(r, Keys.U);
        Assert.IsType<ListRequest>(r.Runner.Pending);
        Idle(r);
        Assert.Equal(0, r.Screen.MenuHighlight);
        Press(r, GameAction.Confirm);
        // The aim (no item message for a scroll): on the combat map, the Items page gone; the menu opens on the cell
        // the spell list left (Cast, the 1st entry): Next.
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(CombatMode.Aim, v.Mode);
        Assert.Null(v.Page);
        Idle(r);
        Assert.Equal(0, r.Screen.MenuHighlight);
        // Exit, then Yes to the abort question: the aim ends.
        Key(r, Keys.E);
        Assert.Equal(CombatMode.Question, v.Mode);
        Key(r, Keys.Y);
        Assert.NotEqual(CombatMode.Aim, v.Mode);
        r.Runner.Dispose();
    }

    [Fact]
    public void Combat_item_lists_coins_and_yes_no_follow_the_shared_highlight()
    {
        if (Dir() is null)
            return;
        var r = ToFight();
        var v = r.Player.FightView;
        var me = v.Current!;
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        // An unreadied item and some copper, so the drop question and the coin list are always reached.
        var loose = (byte[])me.Items[0].Clone();
        loose[CombatRules.ItemEquipped] = 0;
        me.Items.Add(loose);
        me[FightItemScreens.ItemCount] = (byte)me.Items.Count;
        if (FightItemScreens.Coins(me, 0) == 0)
            me[FightItemScreens.Money] = 5;

        // The Items list (0x2D669) stores 1 in the cell on entry (0x2D6B9): it opens on Ready whatever the cell was.
        Key(r, Keys.U);
        var items = Assert.IsType<PageRequest>(r.Runner.Pending);
        Idle(r);
        Assert.Equal(0, r.Screen.MenuHighlight);
        // ',' / '.' (Left / Right) move the cell itself (0x2CEA2–0x2CECB); Escape leaves it there: the main menu
        // opens on the 3rd entry.
        Press(r, GameAction.MenuRight);
        Press(r, GameAction.MenuRight);
        Assert.Equal(2, r.Screen.MenuHighlight);
        Press(r, GameAction.Cancel);
        Assert.True(AtMainMenu(r));
        Idle(r);
        Assert.Equal(2, r.Screen.MenuHighlight);

        // E (Exit, the list's last entry) leaves the cell on Exit: the main menu opens on that entry.
        Key(r, Keys.U);
        Idle(r);
        Assert.Equal(0, r.Screen.MenuHighlight);
        Key(r, Keys.E);
        Assert.True(AtMainMenu(r));
        Idle(r);
        int exit = items.Entries.Count - 1;
        Assert.Equal(exit < menu.Items.Count ? exit : 0, r.Screen.MenuHighlight);

        // Drop of an unreadied item: "Drop it?" opens on No (0x2D9B9 stores 2); No (Escape is our No), and the Items
        // list again on its first entry.
        int count = me.Items.Count;
        Key(r, Keys.U);
        v.Page!.Selected = me.Items.IndexOf(loose);
        Key(r, Keys.D);
        Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Idle(r);
        Assert.Equal(1, r.Screen.MenuHighlight);
        Press(r, GameAction.Cancel);
        Assert.Equal(count, me.Items.Count);
        Assert.IsType<PageRequest>(r.Runner.Pending);
        Idle(r);
        Assert.Equal(0, r.Screen.MenuHighlight);
        Key(r, Keys.E);
        Assert.True(AtMainMenu(r));

        // Drop money from View: the coin list is the item list routine too (0x236AF), its menu only Exit (the
        // caller's text CS:248C is empty): the cell is 1 when it closes, so the View menu comes back on its first
        // entry, not on Drop.
        {
            Key(r, Keys.V);
            var view = Assert.IsType<PageRequest>(r.Runner.Pending);
            int drop = view.Entries.ToList().IndexOf("DROP");
            Assert.True(drop > 0);
            Key(r, Keys.D);
            Assert.True(Assert.IsType<PageRequest>(r.Runner.Pending).PickRows);
            Idle(r);
            Assert.Equal(0, r.Screen.MenuHighlight);
            Press(r, GameAction.Cancel);
            Assert.Equal("VIEW:", Assert.IsType<PageRequest>(r.Runner.Pending).Prompt);
            Idle(r);
            Assert.Equal(0, r.Screen.MenuHighlight);
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
    // s28, the same fight: rare features 26–29 (RANDCOM 0–3) and Stinking Cloud's 30 (RANDCOM 4), and more WILDCOM tiles.
    [InlineData(26, "bcb363a456232bd31955989bc3162a10f742d644772fc2c07e12c3d934b9e5fd")]
    [InlineData(27, "7faf5e1bbb76a5e0fb0af1191b9fd27165c8a51336c1d23fbbdb6e7c4b8b0167")]
    [InlineData(28, "bfaf2d8b8ec9d5f2f9f6724a3f64f11b4f74d83574e26818833f63c18b5bf4b3")]
    [InlineData(29, "e8d61fe4a919ed2e6ec2f171b0320ebd76d6009c5c5171d51dc01906a90fe7a9")]
    [InlineData(30, "5ea8293ef4293717a21e6c6ad1c76326742e69807f0fa788e553bf03785b697c")]
    [InlineData(32, "5e31f3caf44358572faa3761bb5c75097d7dd1768404d6f8e3bfda3c503ec548")]
    [InlineData(34, "aa66c635ea716ddea691b532c6b9a4fe832881add7110fcb50ccc1bc14ebca4f")]
    [InlineData(36, "41cc50fa92ea78df0ab907bf4d224dce3c162ed13e9eeee0055bafd77d471340")]
    [InlineData(38, "2a831192c284a0a70a173c31d1eec0c01f92772de460a6b1c7356862936903d8")]
    [InlineData(40, "ede55154b41ee59a56a369db63340b793fa8dc944e5111d67a0a424e880296df")]
    [InlineData(45, "5dc0d2fe6719f66c869e7b8a4918f59377fa919998ac7d104cea7e7c53955bc7")]
    [InlineData(47, "f6eca3db30f629c6488925686c262cce6c08882edd3e38f1edfcd94e032c0bab")]
    [InlineData(56, "a109bb5fb0e994e3dfaef19400f60c870840a8a5045c5bb34e90f4ec9d3f9a03")]
    public void Wilderness_ground_in_an_outdoor_fight_matches_dosbox(int terrain, string sha256)
    {
        // DOSBox-X s26 (MACHINE=ega): save A put on script 26's overland at 12,13 N (goldbox-tools save-overland), its
        // vector 0 = CLRMON, LOADMON 1 ×2, COMBAT, one step north: an outdoor fight (DS:49FA = 3). The first screen's
        // 7 × 7 cells with no creature on them, reduced to EGA indices as shown (the combat palette's 0/8 swap), against
        // the arena's terrain from the state at that screen: every cell of a terrain gave the same picture.
        // s28: RandSeed DS:4428 patched in the state before the step (0x3A6D3172: terrain 28 at 31,14, shown by moving
        // the Manual aim cursor past it; 0x7A17D593: 29 at 31,13 on the first screen; the arena builder starts 5 draws
        // after that seed, as ArenaBuilder.BuildWilderness predicted), and Darkstar's Stinking Cloud (a 2 × 2 of 30).
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
