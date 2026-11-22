using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The text window's page prompt stays on row 24 after its key (START 0x7A12, Amiga hunk 25 +0x1200: the wait does not
/// blank the row): the text typed after it, and the party-killed window's message, run with the prompt still there
/// until a menu, another key line or the command line draws over it (FS-UAE s45: DAMAGE's six death lines, the window
/// typed over "PRESS ANY KEY"; DOSBox-X s22: the same row through the wipe's pause). Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class PagePromptRowTests
{
    private sealed record Rig(ExploreScreen Screen, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    /// <summary>Save A with its script all EXIT, loaded as a --save start.</summary>
    private static Rig Start(PorGameDirectory dir)
    {
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var bytes = File.ReadAllBytes(savePath);
        var area = new List<byte>();
        for (int v = 0; v < 5; v++)
            area.AddRange([1, 1, 0x1F, 0x99]);
        area.AddRange(new byte[0x1F - 0x14]);
        area.Add(0);
        Array.Clear(bytes, PorSaveGame.OffScript, 7680);
        area.CopyTo(bytes, PorSaveGame.OffScript);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        player.InstallPaging();
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), bytes, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), runner, input, player);
    }

    private static HashSet<byte> Row24(Rig r)
    {
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        var set = new HashSet<byte>();
        for (int y = 24 * 8; y < 200; y++)
            for (int x = 0; x < 13 * 8; x++)
                set.Add(f[x, y]);
        return set;
    }

    [Fact]
    public void The_page_prompt_stays_under_the_text_typed_after_it_until_another_line_is_drawn()
    {
        if (TestRig.PorDir() is not { } d)
            return;
        var r = Start(new PorGameDirectory(d));
        using (r.Runner)
        {
            var typing = new DelayRequest(TimeSpan.FromHours(1)) { Chained = true, KeepCommandLine = true };
            r.Runner.Start(() =>
            {
                for (int i = 0; i < 6; i++)
                    r.Player.DamageReport(new EclDamageReport(new EclCharacter { Name = "MEMBER " + i }, 200, true));
                r.Runner.Ask(typing);                                   // a character of the sixth line, typed
                r.Runner.Ask(new KeyRequest("A LINE OF ITS OWN"));      // another key line over row 24
                r.Runner.Ask(typing with { });                          // typing again: the prompt is gone
            });
            var page = Assert.IsType<KeyRequest>(r.Runner.Pending);
            Assert.True(page.PagePrompt);
            Assert.Contains((byte)13, Row24(r));                         // the page prompt, colour 13
            r.Runner.Answer(null);
            Assert.Same(typing, r.Runner.Pending);
            Assert.True(r.Player.PagePromptStands);
            Assert.Contains((byte)13, Row24(r));                         // still there while the text goes on
            r.Runner.Answer(null);
            Assert.IsType<KeyRequest>(r.Runner.Pending);
            Row24(r);
            Assert.False(r.Player.PagePromptStands);
            r.Runner.Answer(null);
            Assert.DoesNotContain((byte)13, Row24(r));
            r.Runner.Answer(null);
        }
    }
}
