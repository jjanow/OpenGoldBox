using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// The shop and temple screen's view box (START 0x3379 case 1, 0x33BA–0x33D0; Amiga hunk 13 +0x5B5C): the head and body
/// last given to the head + body loader (GAME.OVR 0x79E4 → DS:49FD / 49FE) are drawn at (3, 3), each part the current
/// disk's files have (view3d.md "The shop and temple box"; FS-UAE s45: a fresh session's overland shop shows head 0).
/// Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class VenuePortraitTests
{
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    /// <summary>Save A (slums, disk 2), vector 0 (a step) pointed at <paramref name="code"/> at $9920.</summary>
    private static Rig Start(PorGameDirectory dir, byte[] code)
    {
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var bytes = File.ReadAllBytes(savePath);
        var area = new List<byte>([1, 1, 0x20, 0x99]);
        for (int v = 1; v < 5; v++)
            area.AddRange([1, 1, 0x1F, 0x99]);
        area.AddRange(new byte[0x1F - 0x14]);
        area.Add(0);
        area.AddRange(code);
        area.Add(0);
        Array.Clear(bytes, PorSaveGame.OffScript, 7680);
        area.CopyTo(bytes, PorSaveGame.OffScript);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), bytes, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner,
            new ActionInput(InputBindings.Defaults()), player);
    }

    private static IndexedFrame Draw(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    /// <summary>Pixels in the 88 × 88 box at (24, 24) that are not black.</summary>
    private static int Lit(IndexedFrame f)
    {
        int n = 0;
        for (int y = 24; y < 24 + 88; y++)
            for (int x = 24; x < 24 + 88; x++)
                if (f[x, y] != 0)
                    n++;
        return n;
    }

    private static readonly byte[] Shop = [0x1C, 0x09, 0, 1, 1, 0x6C, 0x6E, 0x24]; // CLRMON, SAVE 1 → $6E6C, COMBAT

    [Fact]
    public void A_fresh_sessions_shop_box_is_empty_on_a_disk_without_head_0()
    {
        if (TestRig.PorDir() is not { } d)
            return;
        var r = Start(new PorGameDirectory(d), Shop);
        Assert.Equal(((byte)0, (byte)0), r.Player.VenuePortrait);
        r.Runner.Start(() => r.Game.Loop.Execute(AreaCommand.Forward));
        Assert.Equal(Venue.Shop, r.Player.Interface);
        Assert.Equal(0, Lit(Draw(r)));                        // HEAD2 / BODY2 have no block 0 (DOSBox-X s22: empty)
        r.Runner.Dispose();
    }

    [Fact]
    public void The_shop_box_shows_the_last_head_and_body_given_to_the_loader()
    {
        // SAVE 5 → $6DE1, PICTURE 2: head 5 + body 2 through 0x79E4 (both on disk 2); then the shop.
        if (TestRig.PorDir() is not { } d)
            return;
        byte[] code = [0x09, 0, 5, 1, 0xE1, 0x6D, 0x0E, 0, 2, .. Shop];
        var r = Start(new PorGameDirectory(d), code);
        r.Runner.Start(() => r.Game.Loop.Execute(AreaCommand.Forward));
        Assert.Equal(((byte)5, (byte)2), r.Player.VenuePortrait);
        Assert.Equal(Venue.Shop, r.Player.Interface);
        Assert.True(Lit(Draw(r)) > 500);
        r.Runner.Dispose();
    }
}
