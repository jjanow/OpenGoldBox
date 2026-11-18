using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework;

namespace GoldBox.Client.Tests;

/// <summary>
/// The art setting (Original DOS / Best / Amiga, PLAN §4.3): parsing and remembering it, the screen palette runs, which
/// variant each drawn picture comes from and how its indices are moved, and the controls screen's ART row.
/// </summary>
public class ArtSettingTests
{
    // ---- a content with hand-made DOS and "Amiga" art ----

    private static readonly byte[] AmigaPalette = [.. Enumerable.Range(0, 48).Select(i => (byte)(i * 5))];

    private sealed class FakeContent : IPorContent
    {
        public readonly Dictionary<(string, int), IReadOnlyList<DaxPicture>> Dos = [];
        public readonly Dictionary<(string, int), IReadOnlyList<DaxPicture>> Amiga = [];
        public byte[]? Pairs;
        public byte[]? AmigaIconPairs() => Pairs;

        public string Origin => "fake";
        public int? ScriptDisk(int script) => null;
        public int? MapDisk(int id) => null;
        public MonsterData? Monster(int disk, int id) => null;
        public IReadOnlyList<byte[]>? TreasureList(int disk, int id) => null;
        public BaseItemTable? ItemTypes() => null;
        public PorProgramData? Program() => null;
        public byte[]? ArtData(string file, int id) => null;
        public bool NoWrapOffMap(int script) => false;
        public string? SaveFolder => null;
        public byte[]? LoadBlock(int disk, int script) => null;
        public AreaMap? LoadMap(int disk, int id) => null;

        public IReadOnlyList<DaxPicture>? Picture(string file, int id) => Dos.GetValueOrDefault((file, id));

        /// <summary>As PorModule: Best and Amiga take the overlay (the fake's "defaults" are Amiga), DOS otherwise.</summary>
        public PorArtImage? Art(string file, int id, PorArtChoice choice)
        {
            var (container, _) = Data.Module.ArtDocument.SplitName(file);
            if (choice != PorArtChoice.Dos && Amiga.TryGetValue((container, id), out var a))
                return new PorArtImage("amiga", a, AmigaPalette);
            return Picture(file, id) is { } f ? new PorArtImage("dos", f, Data.Module.IndexedPng.EgaPalette) : null;
        }
    }

    private static DaxPicture Pic(int w, int h, params byte[][] items) => DaxPicture.Create(w, h, 0, 0, items);
    private static DaxPicture PicAt(int w, int h, int x, int y, params byte[][] items) => DaxPicture.Create(w, h, x, y, items);
    private static byte[] Fill(int n, Func<int, byte> f) => [.. Enumerable.Range(0, n).Select(f)];

    // ---- setting words, options, settings file ----

    [Fact]
    public void The_setting_words_cycle_and_parse()
    {
        Assert.Equal(PorArtChoice.Best, ClientArt.Next(PorArtChoice.Dos));
        Assert.Equal(PorArtChoice.Amiga, ClientArt.Next(PorArtChoice.Best));
        Assert.Equal(PorArtChoice.Dos, ClientArt.Next(PorArtChoice.Amiga));
        Assert.Equal(PorArtChoice.Amiga, ClientArt.Previous(PorArtChoice.Dos));
        Assert.Equal(PorArtChoice.Dos, ClientArt.Parse("DOS"));
        Assert.Equal(PorArtChoice.Dos, ClientArt.Parse("original"));
        Assert.Equal(PorArtChoice.Best, ClientArt.Parse(" best "));
        Assert.Equal(PorArtChoice.Amiga, ClientArt.Parse("Amiga"));
        Assert.Null(ClientArt.Parse("c64"));
        Assert.Equal("ORIGINAL DOS", ClientArt.Label(PorArtChoice.Dos));
    }

    [Fact]
    public void Options_take_the_art_and_the_amiga_folder()
    {
        var o = ClientOptions.Parse(["--game", "/g", "--amiga", "/a", "--art", "amiga"]);
        Assert.Equal("/a", o.AmigaDirectory);
        Assert.Equal(PorArtChoice.Amiga, o.Art);
        Assert.Null(ClientOptions.Parse([]).Art);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--art", "vga"]));
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--amiga"]));
        Assert.Contains("--art dos|best|amiga", ClientOptions.Usage);
        Assert.Contains("--amiga DIR", ClientOptions.Usage);
    }

    [Fact]
    public void The_settings_file_keeps_the_art_and_defaults_to_best()
    {
        Assert.Equal(PorArtChoice.Best, new ClientSettings().ArtChoice);
        Assert.Equal(PorArtChoice.Best, ClientSettings.FromJson("""{ "GameDirectory": "/g" }""").ArtChoice); // older file
        Assert.Equal(PorArtChoice.Best, ClientSettings.FromJson("""{ "Art": "nonsense" }""").ArtChoice);
        var s = new ClientSettings { ArtChoice = PorArtChoice.Dos, AmigaDirectory = "/a" };
        Assert.Contains("\"Art\": \"dos\"", s.ToJson());
        var back = ClientSettings.FromJson(s.ToJson());
        Assert.Equal(PorArtChoice.Dos, back.ArtChoice);
        Assert.Equal("/a", back.AmigaDirectory);
    }

    // ---- the screen palette ----

    [Fact]
    public void Each_palette_gets_its_own_run_of_sixteen()
    {
        var p = new ScreenPalette();
        Assert.Equal(0, p.Base(Data.Module.IndexedPng.EgaPalette));
        Assert.Equal(16, p.Base(AmigaPalette));
        Assert.Equal(16, p.Base([.. AmigaPalette]));             // same colours, same run
        var other = Fill(48, i => (byte)(255 - i));
        Assert.Equal(32, p.Base(other));
        Assert.Equal(new Color(0, 5, 10), p.Colours[16]);
        Assert.Equal(new Color(255, 254, 253), p.Colours[32]);
        Assert.Equal(FrameTexture.Ega[9], p.Colours[9]);
        for (int k = 3; k < 16; k++)
            p.Base(Fill(48, i => (byte)(k * 7 + i)));
        Assert.Throws<InvalidOperationException>(() => p.Base(Fill(48, i => (byte)(i + 200))));
    }

    // ---- the 3D view's tiles ----

    [Fact]
    public void Amiga_wall_tiles_keep_the_mask_and_move_into_their_run()
    {
        var c = new FakeContent();
        c.Dos[("8X8D1", 202)] = [Pic(8, 8, Fill(64, i => 13), Fill(64, i => 7))];
        // The Amiga has one tile more; 0 is its see-through colour.
        c.Amiga[("8X8D", 202)] = [Pic(8, 8, Fill(64, i => (byte)(i % 2 == 0 ? 0 : 14)), Fill(64, i => 5), Fill(64, i => 0))];
        int Base(byte[] _) => 32;

        var dos = PorViewArt.Tiles(c, "8X8D1", 202, PorArtChoice.Dos, Base);
        Assert.Equal(c.Dos[("8X8D1", 202)][0].AsTiles(), dos);

        var amiga = PorViewArt.Tiles(c, "8X8D1", 202, PorArtChoice.Amiga, Base);
        Assert.Equal(128, amiga.Length);                         // DOS's tile count
        Assert.Equal(IndexedFrame.MaskColour, amiga[0]);         // Amiga 0 → the DOS mask colour
        Assert.Equal(32 + 14, amiga[1]);
        Assert.All(amiga[64..], v => Assert.Equal(32 + 5, v));
        Assert.Equal(amiga, PorViewArt.Tiles(c, "8X8D1", 202, PorArtChoice.Best, Base));

        // A variant with fewer tiles than DOS: DOS.
        c.Amiga[("8X8D", 202)] = [Pic(8, 8, Fill(64, i => 1))];
        Assert.Equal(dos, PorViewArt.Tiles(c, "8X8D1", 202, PorArtChoice.Amiga, Base));
        // No palette runs (no screen palette): DOS.
        Assert.Equal(dos, PorViewArt.Tiles(c, "8X8D1", 202, PorArtChoice.Amiga, null));
    }

    // ---- combat pictures ----

    [Fact]
    public void Combat_pictures_follow_the_setting_and_party_icons_stay_dos()
    {
        var c = new FakeContent();
        c.Dos[("DUNGCOM", 1)] = [Pic(24, 24, Fill(576, _ => 2), Fill(576, _ => 3))];
        c.Amiga[("DUNGCOM", 1)] = [Pic(24, 24, Fill(576, _ => 0), Fill(576, _ => 4), Fill(576, _ => 9))];
        c.Dos[("WILDCOM", 1)] = [Pic(24, 24, Fill(576, _ => 2))];
        c.Dos[("RANDCOM", 1)] = [Pic(24, 24, Fill(576, _ => 6))];
        c.Amiga[("RANDCOM", 1)] = [Pic(24, 24, Fill(576, _ => 1), Fill(576, _ => 1))];
        c.Dos[("CPIC1", 5)] = [Pic(24, 24, Fill(576, i => (byte)(i % 3)))];
        c.Amiga[("CPIC", 5)] = [Pic(24, 24, Fill(576, i => (byte)(i % 3 == 0 ? 0 : 7)))];
        c.Dos[("CBODY", 1)] = [Pic(24, 24, Fill(576, _ => 1))];
        c.Amiga[("CBODY", 1)] = [Pic(24, 24, Fill(576, _ => 4))];
        var palette = new ScreenPalette();

        var art = new CombatArt(c, PorArtChoice.Dos, palette.Base);
        Assert.Equal(2, art.Dungeon.Count);
        Assert.Equal(2, art.Dungeon[0].Pixels[0]);
        Assert.Equal(1, art.Monster(5, false)!.Pixels[1]);

        art.UseArt(PorArtChoice.Amiga, palette.Base);
        int b = palette.Base(AmigaPalette);
        Assert.Equal(2, art.Dungeon.Count);                      // cut to DOS's count
        Assert.Equal(b + 0, art.Dungeon[0].Pixels[0]);           // ground: every index moves (opaque)
        Assert.Equal(b + 4, art.Dungeon[1].Pixels[0]);
        Assert.Equal(2, art.Wilderness[0].Pixels[0]);            // no Amiga set: DOS
        Assert.Equal(b + 1, art.Rare[0].Pixels[0]);              // RANDCOM: the Amiga set as it is (s19)
        Assert.Same(art.Rare[0], art.Ground(CombatArt.RareFirstTile, dungeon: true)); // tile 0x22 in both sets
        Assert.Same(art.Rare[0], art.Ground(CombatArt.RareFirstTile, dungeon: false));
        Assert.Null(art.Ground(2, dungeon: true));               // past DUNGCOM's tiles, before the rare set
        var m = art.Monster(5, false)!;
        Assert.Equal(0, m.Pixels[0]);                            // icon: 0 stays see-through
        Assert.Equal(b + 7, m.Pixels[1]);
        var member = new Core.Ecl.EclCharacter();
        member[CombatArt.IconBody] = 1;
        byte[] defaults = [0x91, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7];  // the creation's colour bytes: the art's own colours
        for (int k = 0; k < 6; k++)
            member[CombatArt.IconColours + k] = defaults[k];
        Assert.Equal(1, art.Party(member, false)!.Pixels[300]);  // no colour pairs in the module: party icons stay DOS

        art.UseArt(PorArtChoice.Dos, palette.Base);
        Assert.Equal(1, art.Monster(5, false)!.Pixels[1]);       // the cache follows the change
    }

    /// <summary>Pair bytes as the Amiga program's (the pens of each DOS pair; the light one in the high nibble here).</summary>
    private static byte[] AgreeingPairs() =>
        [.. PorAmigaArt.DosIconPairs.Select(c => (byte)(PorAmigaArt.IconPen(c + 8)!.Value << 4 | PorAmigaArt.IconPen(c)!.Value))];

    [Fact]
    public void Amiga_party_icons_or_the_head_into_the_body_and_recolour_through_the_pairs()
    {
        var c = new FakeContent { Pairs = AgreeingPairs() };
        c.Dos[("CBODY", 1)] = [Pic(24, 24, Fill(576, _ => 1))];
        c.Dos[("CHEAD", 0)] = [Pic(24, 10, Fill(240, _ => 0))];
        // Amiga body: index 1 at the top left, the pen of DOS 4 (6) at pixel 300; the head (10 rows) has 8 at the top left.
        c.Amiga[("CBODY", 1)] = [Pic(24, 24, Fill(576, i => i == 0 ? (byte)1 : i == 300 ? (byte)6 : (byte)0))];
        c.Amiga[("CBODY", 1 + 0x80)] = [Pic(24, 24, Fill(576, _ => 9))];
        c.Amiga[("CHEAD", 0)] = [Pic(24, 10, Fill(240, i => i == 0 ? (byte)8 : (byte)0))];
        c.Amiga[("CBODY", 1 + 0x40)] = [Pic(24, 24, Fill(576, _ => 3))];
        c.Amiga[("CHEAD", 0 + 0x40)] = [Pic(24, 8, Fill(192, _ => 0))];
        var palette = new ScreenPalette();
        var art = new CombatArt(c, PorArtChoice.Amiga, palette.Base);
        int b = palette.Base(AmigaPalette);
        var member = new Core.Ecl.EclCharacter();
        member[CombatArt.IconBody] = 1;
        member[CombatArt.IconSize] = 1;
        byte[] defaults = [0x91, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7];  // the creation's colour bytes: the art's own colours
        for (int k = 0; k < 6; k++)
            member[CombatArt.IconColours + k] = defaults[k];

        var icon = art.Party(member, false)!;
        Assert.Equal(b + (1 | 8), icon.Pixels[0]);               // the head's 8 ORed into the body's 1 (hunk 16 +0x210)
        Assert.Equal(b + 6, icon.Pixels[300]);                   // the defaults leave the art's colours
        Assert.Equal(0, icon.Pixels[1]);                         // 0 stays see-through
        Assert.Equal(b + 9, art.Party(member, true)!.Pixels[5]); // the action picture: CBODY id + 0x80, same head
        member[CombatArt.IconColours + 3] = 0xC2;                // pair 4 / 12: the dark part EGA green
        art.UseArt(PorArtChoice.Amiga, palette.Base);
        Assert.Equal(b + PorAmigaArt.IconPen(2)!.Value, art.Party(member, false)!.Pixels[300]);
        member[CombatArt.IconSize] = 2;                          // size 2: CBODY / CHEAD id + 0x40 (the letter T)
        Assert.Equal(b + 3, art.Party(member, false)!.Pixels[0]);

        member[CombatArt.IconSize] = 1;
        member[CombatArt.IconColours + 3] = 0xC5;                // EGA 5 has no pen [?]: the DOS icon
        Assert.Equal(1, art.Party(member, false)!.Pixels[300]);
        member[CombatArt.IconColours + 3] = 0xC4;
        art.UseArt(PorArtChoice.Dos, palette.Base);              // Original DOS: the DOS icon
        Assert.Equal(1, art.Party(member, false)!.Pixels[300]);
        c.Amiga.Remove(("CHEAD", 0));                            // no Amiga head: the DOS icon
        art.UseArt(PorArtChoice.Best, palette.Base);
        Assert.Equal(1, art.Party(member, false)!.Pixels[300]);
    }

    [Fact]
    public void The_amiga_backdrop_pens_are_the_amiga_programs()
    {
        // The DOS rule's colours moved to the Amiga pens (PorAmigaArt.BackdropPen): horizon 0/15, 8/7, ground 8/1/6
        // as the Amiga rule (hunk 7 +0x38A2..) gives them.
        Assert.Equal([(byte)4, (byte)15, (byte)12, (byte)14, (byte)12, (byte)1, (byte)5],
            new byte[] { 0, 15, 8, 7, 8, 1, 6 }.Select(c => PorAmigaArt.BackdropPen(c)!.Value));
        Assert.Null(PorAmigaArt.BackdropPen(2));
        // With both programs: the DOS sky list moved to pens is the Amiga sky list (hunk 8 +0x35F, file offset 0x1604B).
        var dos = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        var amiga = Environment.GetEnvironmentVariable("GOLDBOX_POR_AMIGA_DIR");
        if (string.IsNullOrWhiteSpace(dos) || !Directory.Exists(dos) || string.IsNullOrWhiteSpace(amiga)
            || !File.Exists(Path.Combine(amiga, "program")))
            return;
        var sky = PorViewArt.Load(new PorGameDirectory(dos)).Program.SkyColours;
        var program = File.ReadAllBytes(Path.Combine(amiga, "program"));
        Assert.Equal(program.AsSpan(0x1604B, 16).ToArray(), sky.Select(c => PorAmigaArt.BackdropPen(c)!.Value).ToArray());
    }

    [Fact]
    public void Rebasing_keeps_dos_art_as_it_is()
    {
        byte[] px = [0, 5, 15];
        Assert.Same(px, CombatArt.Rebase(px, 0, opaque: false));
        Assert.Equal(new byte[] { 0, 21, 31 }, CombatArt.Rebase(px, 16, opaque: false));
        Assert.Equal(new byte[] { 16, 21, 31 }, CombatArt.Rebase(px, 16, opaque: true));
        var dos = new List<CombatPicture> { new(1, 1, [1]) };
        Assert.Same(dos, CombatArt.SameOrDos([], dos));
    }

    // ---- encounter pictures ----

    [Fact]
    public void Encounter_pictures_portraits_and_sprites_are_drawn_in_the_chosen_art()
    {
        var c = new FakeContent();
        c.Dos[("PIC3", 7)] = [Pic(88, 88, Fill(88 * 88, _ => 4)), Pic(88, 88, Fill(88 * 88, _ => 9))];
        c.Amiga[("PIC", 7)] = [Pic(88, 88, Fill(88 * 88, _ => 2))];
        c.Dos[("HEAD3", 2)] = [Pic(88, 40, Fill(88 * 40, _ => 6))];
        c.Dos[("BODY3", 5)] = [Pic(88, 48, Fill(88 * 48, _ => 12))];
        c.Amiga[("HEAD", 2)] = [Pic(56, 40, Fill(56 * 40, _ => 3))];
        c.Amiga[("BODY", 5)] = [Pic(88, 48, Fill(88 * 48, _ => 8))];
        c.Dos[("HEAD3", 1)] = [Pic(88, 40, Fill(88 * 40, _ => 6))];  // DOS-only head (the Amiga lacks HEAD 1 and 3)
        c.Dos[("SPRIT3", 9)] = [PicAt(40, 80, 3, 1, Fill(40 * 80, i => (byte)(i < 40 ? 0 : 10))),
            PicAt(24, 65, 4, 1, Fill(24 * 65, _ => 11))];
        var palette = new ScreenPalette();
        var pics = new ViewPictures(c, PorArtChoice.Dos, palette.Base);
        var f = new IndexedFrame(320, 200);

        // DOS close-up: frame 0 only [?], at the window's corner.
        Assert.True(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Picture, 7, null, 0, 3)));
        Assert.Equal(4, f[24, 24]);
        Assert.Equal(4, f[24 + 87, 24 + 87]);
        Assert.Equal(0, f[24 + 88, 24]);

        pics.UseArt(PorArtChoice.Amiga, palette.Base);
        int b = palette.Base(AmigaPalette);
        Assert.True(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Picture, 7, null, 0, 3)));
        Assert.Equal(b + 2, f[30, 30]);

        // Amiga portrait: the 56-pixel head at x = 16 [?], the body under row 40, both in the Amiga run.
        f.Clear();
        Assert.True(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Portrait, 5, 2, 0, 3)));
        Assert.Equal(b + 0, f[24 + 15, 24]);
        Assert.Equal(b + 3, f[24 + ViewPictures.AmigaHeadX, 24]);
        Assert.Equal(b + 3, f[24 + 71, 24 + 39]);
        Assert.Equal(b + 0, f[24 + 72, 24]);
        Assert.Equal(b + 8, f[24, 24 + 40]);

        // A head the Amiga lacks: head and body both DOS (one palette per portrait).
        f.Clear();
        Assert.True(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Portrait, 5, 1, 0, 3)));
        Assert.Equal(6, f[24, 24]);
        Assert.Equal(12, f[24, 24 + 40]);

        // Sprite at distance 1: the nearest frame at its block place (x 3, y 1 in 8-pixel units), 0 see-through.
        f.Clear(1);
        Assert.True(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Sprite, 9, null, 1, 3)));
        Assert.Equal(1, f[24 + 24, 24 + 8]);                     // index 0 left the view as it was
        Assert.Equal(10, f[24 + 24, 24 + 9]);
        Assert.Equal(1, f[24 + 23, 24 + 9]);
        Assert.Equal(1, ViewPictures.SpriteFrame(2, 3));
        Assert.Equal(2, ViewPictures.SpriteFrame(9, 3));
        Assert.Equal(0, ViewPictures.SpriteFrame(0, 3));

        Assert.False(pics.Draw(f, 24, 24, new ViewPicture(ViewPictureKind.Picture, 99, null, 0, 3)));
    }

    // ---- the controls screen's ART row and the title ----

    private sealed class Host : IScreenHost
    {
        public int Pops;
        public void Push(IScreen screen) { }
        public void Pop() => Pops++;
        public void Quit() { }
        public void SaveBindings() { }
    }

    private static void Press(IScreen s, ActionInput input, GameAction a)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Inject(a);
        s.Update(input);
    }

    [Fact]
    public void The_controls_screen_changes_the_art_with_keys_pad_and_mouse()
    {
        var art = new ClientArt(null, null, new ScreenPalette(), PorArtChoice.Best);
        var saved = new List<PorArtChoice>();
        art.Changed = saved.Add;
        var host = new Host();
        var screen = new ControlsScreen(new TextFont(new byte[177 * 8]), host, art);
        var input = new ActionInput(InputBindings.Defaults());

        // Up from the first action wraps to QUIT, then BACK, RESET, ART.
        for (int i = 0; i < 4; i++)
            Press(screen, input, GameAction.MenuUp);
        Press(screen, input, GameAction.Confirm);                // keyboard Enter / pad A
        Assert.Equal(PorArtChoice.Amiga, art.Choice);
        Press(screen, input, GameAction.MenuRight);              // d-pad / arrow right
        Assert.Equal(PorArtChoice.Dos, art.Choice);
        Press(screen, input, GameAction.MenuLeft);
        Assert.Equal(PorArtChoice.Amiga, art.Choice);
        // The mouse: a click on the ART row (row 18).
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (screen.MapClick(new ScreenClick(20, 18 * 8 + 4, false)) is { } a)
            input.Inject(a);
        screen.Update(input);
        Assert.Equal(PorArtChoice.Dos, art.Choice);
        Assert.Equal([PorArtChoice.Amiga, PorArtChoice.Dos, PorArtChoice.Amiga, PorArtChoice.Dos], saved);
        Assert.Equal(0, host.Pops);
        // QUIT is still the last row (21) and BACK above it.
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        if (screen.MapClick(new ScreenClick(20, 20 * 8 + 4, false)) is { } back)
            input.Inject(back);
        screen.Update(input);
        Assert.Equal(1, host.Pops);
    }

    [Fact]
    public void Original_dos_shows_no_title_and_the_title_goes_on_with_any_input()
    {
        Assert.Null(new ClientArt().Title());
        var title = new PorArtImage("amiga", [Pic(320, 200, Fill(64000, i => (byte)(i % 16)))], AmigaPalette);
        var host = new Host();
        var screen = new TitleScreen(title, 48, host);
        var f = new IndexedFrame(320, 200);
        var input = new ActionInput(InputBindings.Defaults());
        screen.Draw(f, input);
        Assert.Equal(48 + 5, f[5, 0]);
        Assert.Equal(48 + 15, f[319, 199]);
        Assert.Null(screen.MapClick(new ScreenClick(10, 10, false)));
        Assert.Equal(1, host.Pops);
        screen.Update(input);
        Assert.Equal(1, host.Pops);                              // once only
    }

    // ---- the player's files (DOS + Amiga; nothing checked without GOLDBOX_POR_DIR and GOLDBOX_POR_AMIGA_DIR) ----

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { } d && Directory.Exists(d) ? d : null;

    [Fact]
    public void The_cache_key_covers_the_amiga_files_and_the_art_follows_the_setting()
    {
        if (Env("GOLDBOX_POR_DIR") is not { } dos || Env("GOLDBOX_POR_AMIGA_DIR") is not { } amiga)
            return;
        Assert.NotEqual(PorModuleCache.Key(dos), PorModuleCache.Key(dos, amiga));
        Assert.Equal(PorModuleCache.Key(dos, amiga), PorModuleCache.Key(dos, amiga));
        var root = Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-amiga-cache");
        var module = PorModule.Open(PorModuleCache.Ensure(dos, root, amiga));
        var plain = PorModule.Open(PorModuleCache.Ensure(dos, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-module-cache")));

        // Walls: Original DOS gives exactly the DOS tiles; Best and Amiga move them into the Amiga table's run.
        var palette = new ScreenPalette();
        var view = PorViewArt.Load(module);
        var dosCommon = PorViewArt.Load(plain).Symbols.Tile(1).ToArray();
        var art = new ClientArt(module, view, palette, PorArtChoice.Dos);
        Assert.Equal(dosCommon, view.Symbols.Tile(1).ToArray());
        view.LoadWallSet(1, 1, 1);
        var dosWall = view.Symbols.Tile(view.Symbols.Bases[1]).ToArray();
        Assert.All(dosWall, v => Assert.InRange(v, 0, 15));
        art.Set(PorArtChoice.Best);
        var amigaWall = view.Symbols.Tile(view.Symbols.Bases[1]).ToArray();
        Assert.NotEqual(dosWall, amigaWall);
        Assert.All(amigaWall, v => Assert.True(v == IndexedFrame.MaskColour || v >= 16));
        // The mask stays where DOS has it (Amiga 0 = DOS 13 on every wall tile).
        Assert.Equal(dosWall.Select(v => v == IndexedFrame.MaskColour), amigaWall.Select(v => v == IndexedFrame.MaskColour));

        // Combat: Amiga monster icons and ground, DOS counts.
        var dosCombat = new CombatArt(plain);
        Assert.Equal(dosCombat.Dungeon.Count, art.Combat.Dungeon.Count);
        Assert.Contains(art.Combat.Monster(2, false)!.Pixels, v => v >= 16);
        art.Set(PorArtChoice.Dos);
        Assert.Equal(dosCombat.Monster(2, false)!.Pixels, art.Combat.Monster(2, false)!.Pixels);
        Assert.Equal(dosCombat.Dungeon[3].Pixels, art.Combat.Dungeon[3].Pixels);

        // The title needs disk A's intro, which the Amiga folder may not hold.
        art.Set(PorArtChoice.Amiga);
        bool intro = PorAmigaArt.LocateIntro(amiga) is not null;
        Assert.Equal(intro, art.Title() is { Variant: "amiga" });
        // Without the overlay every setting draws DOS.
        var dosOnly = new ClientArt(plain, null, new ScreenPalette(), PorArtChoice.Amiga);
        Assert.Equal(dosCombat.Monster(2, false)!.Pixels, dosOnly.Combat.Monster(2, false)!.Pixels);
        Assert.Null(dosOnly.Title());
    }
}
