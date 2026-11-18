using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>The introduction screen (GAME.OVR 0x6FB) over the party menu: pages, keys, lightning, credits.</summary>
public class IntroScreenTests
{
    private sealed class Host : IScreenHost
    {
        public int Pops;
        public void Push(IScreen screen) { }
        public void Pop() => Pops++;
        public void Quit() { }
        public void SaveBindings() { }
    }

    private static PorArtImage Picture(string variant, Func<int, byte> px) =>
        new(variant, [DaxPicture.Create(320, 200, 0, 0, [[.. Enumerable.Range(0, 64000).Select(px)]])], IndexedPng.EgaPalette);

    // A font whose every glyph is solid, so text shows as filled cells.
    private static TextFont SolidFont() => new([.. Enumerable.Repeat((byte)0xFF, 177 * 8)]);

    private static string? Text(int i) => i < Intro.CreditTexts ? "X" + i : null;

    private static int Max(int n) => n - 1;

    private static void Press(ActionInput input, Keys k)
    {
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
        input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
    }

    [Fact]
    public void Pages_draw_in_order_with_the_lightning_and_the_credits()
    {
        var now = TimeSpan.Zero;
        var host = new Host();
        var logo = Picture("dos", i => 4);
        var title = Picture("dos", i => (byte)(i % 2));         // every other pixel index 1
        var screen = new IntroScreen(logo, 0, title, 0, SolidFont(), Text, host, Max, () => now);
        var input = new ActionInput(InputBindings.Defaults());
        var f = new IndexedFrame(320, 200);
        Assert.False(screen.Empty);

        screen.Draw(f, input);
        Assert.Equal(4, f[100, 100]);
        now = TimeSpan.FromSeconds(5);
        screen.Update(input);
        Assert.Equal(IntroStep.Title, screen.Step);
        screen.Draw(f, input);
        Assert.Equal(0, f[0, 0]);
        Assert.Equal(1, f[1, 0]);

        // The first burst (416.675 ms into the title at 40 passes a millisecond, 3 a pass): register 1 shows 9; index 0 stays.
        now = TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(420);
        screen.Update(input);
        screen.Draw(f, input);
        Assert.Equal(0, f[0, 0]);
        Assert.Equal(9, f[1, 0]);

        // The second burst (3033.35 ms to 5233.35 ms) holds the title past its 5 seconds; then the credits.
        now = TimeSpan.FromSeconds(10);
        screen.Update(input);
        Assert.Equal(IntroStep.Title, screen.Step);
        now = TimeSpan.FromSeconds(10.3);
        screen.Update(input);
        Assert.Equal(IntroStep.Credits, screen.Step);
        screen.Draw(f, input);
        // The first line, colour 10 at column 5 of row 1; the frame's corner at (0, 4) with no art is the stand-in.
        Assert.Equal(10, f[5 * 8, 1 * 8]);
        Assert.Equal(Chrome.Yellow, f[0, 4 * 8]);
        Assert.Equal(0, f[3 * 8, 1 * 8]);

        // The 10 seconds run out: the screen leaves once.
        now += TimeSpan.FromSeconds(10);
        screen.Update(input);
        Assert.Equal(1, host.Pops);
        screen.Update(input);
        Assert.Equal(1, host.Pops);
    }

    [Fact]
    public void A_key_ends_the_whole_intro()
    {
        // START 0x8A94, the key flush after each wait, does nothing: the key stays, every later wait ends at once
        // (DOSBox-X s22: one key on the logo, then the title and credits only while they draw).
        var now = TimeSpan.Zero;
        var host = new Host();
        var screen = new IntroScreen(Picture("dos", i => 4), 0, Picture("dos", i => 2), 0, SolidFont(), Text, host, Max,
            () => now);
        var input = new ActionInput(InputBindings.Defaults());
        screen.Update(input);
        Press(input, Keys.Space);
        screen.Update(input);
        Assert.Equal(IntroStep.Done, screen.Step);
        Assert.Equal(1, host.Pops);
    }

    [Fact]
    public void Clicks_and_pad_buttons_end_it_too_and_the_amiga_painting_has_no_lightning()
    {
        var now = TimeSpan.Zero;
        var host = new Host();
        var painting = Picture("amiga", i => 1);
        var screen = new IntroScreen(null, 0, painting, 48, SolidFont(), Text, host, Max, () => now);
        var input = new ActionInput(InputBindings.Defaults());
        var f = new IndexedFrame(320, 200);
        Assert.Equal(IntroStep.Title, screen.Step);
        now = TimeSpan.FromMilliseconds(2800);
        screen.Update(input);
        screen.Draw(f, input);
        Assert.Equal(49, f[10, 10]);
        Assert.Null(screen.MapClick(new ScreenClick(5, 5, false)));
        screen.Update(input);
        Assert.Equal(IntroStep.Done, screen.Step);

        var pad = new IntroScreen(Picture("dos", i => 4), 0, Picture("dos", i => 2), 0, SolidFont(), Text, new Host(), Max,
            () => TimeSpan.Zero);
        var input2 = new ActionInput(InputBindings.Defaults());
        var a = new GamePadState(default, default, new GamePadButtons(Buttons.A), default);
        input2.Update(default, default, default, TimeSpan.Zero, _ => null);
        input2.Update(default, a, default, TimeSpan.Zero, _ => null);
        pad.Update(input2);
        Assert.Equal(IntroStep.Done, pad.Step);
    }

    [Fact]
    public void Nothing_to_show_leaves_the_menu_alone()
    {
        var screen = new IntroScreen(null, 0, null, 0, null, _ => null, new Host(), Max, () => TimeSpan.Zero);
        Assert.True(screen.Empty);
        // Credits need every text.
        var partial = new IntroScreen(null, 0, null, 0, SolidFont(), i => i == 3 ? null : "X", new Host(), Max, () => TimeSpan.Zero);
        Assert.True(partial.Empty);
    }

    // ---- the player's files (nothing checked without GOLDBOX_POR_DIR) ----

    [Fact]
    public void The_module_holds_the_intro_pictures_and_texts()
    {
        var dir = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (dir is null || !Directory.Exists(dir))
            return;
        var module = PorModule.Open(PorModuleCache.Ensure(dir, Path.Combine(Path.GetTempPath(), "goldbox-tests", "client-module-cache")));
        var art = new ClientArt(module, null, new ScreenPalette(), PorArtChoice.Best);
        var logo = art.IntroLogo();
        var title = art.IntroTitle();
        Assert.NotNull(logo);
        Assert.NotNull(title);
        Assert.Equal("dos", title!.Variant);                    // no Amiga overlay in this module
        Assert.Equal((320, 200), (logo!.Frames[0].Width, logo.Frames[0].Height));
        Assert.Equal((320, 200), (title.Frames[0].Width, title.Frames[0].Height));
        // The title has pixels of index 1 for the lightning to recolour.
        Assert.Contains((byte)1, title.Frames[0].Items[0]);

        var program = module.Program()!;
        var folder = PorProgramData.Open(new PorGameDirectory(dir))!;
        for (int i = 0; i < Intro.CreditTexts; i++)
        {
            Assert.False(string.IsNullOrEmpty(program.Text($"Intro.{i}")), $"Intro.{i}");
            Assert.Equal(folder.Text($"Intro.{i}"), program.Text($"Intro.{i}"));
        }
        var font = PorViewArt.Load(module).Font;
        var f = new IndexedFrame(320, 200);
        IntroScreen.DrawCredits(f, font, i => program.Text($"Intro.{i}"));
        // Some of the first line's cells are drawn in its colour.
        Assert.Contains(Enumerable.Range(0, 160).SelectMany(x => Enumerable.Range(8, 8).Select(y => f[40 + x, y])), v => v == 10);
    }
}
