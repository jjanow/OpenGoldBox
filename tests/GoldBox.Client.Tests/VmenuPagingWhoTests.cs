using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// VMENU's paging and WHO's pick on the explore screen (ecl-format.md "VMENU's screen" and "WHO"; read s44, DOSBox-X
/// s44 with scratch saves: nine entries under a one-row prompt, seven under a two-row one, then WHO). Skipped without
/// GOLDBOX_POR_DIR.
/// </summary>
public class VmenuPagingWhoTests
{
    private sealed record Rig(ExploreScreen Screen, PorGame Game, ScriptRunner Runner, ActionInput Input, ClientPlayer Player);

    private static readonly string[] Nine = ["ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL", "INDIA"];

    /// <summary>Save A, its script all EXIT; loaded as a --save start.</summary>
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
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), bytes, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
        var input = new ActionInput(InputBindings.Defaults());
        return new Rig(new ExploreScreen(game, player, runner, new TestRig.NoHost(), 'A', art), game, runner, input, player);
    }

    private static void Key(Rig r, Keys k)
    {
        r.Input.Update(new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
        r.Input.Update(default, default, default, TimeSpan.Zero, _ => null);
        r.Screen.Update(r.Input);
    }

    private static IndexedFrame Draw(Rig r)
    {
        var f = new IndexedFrame(320, 200);
        r.Screen.Draw(f, r.Input);
        return f;
    }

    private static HashSet<byte> Colours(IndexedFrame f, int col, int row)
    {
        var set = new HashSet<byte>();
        for (int y = row * 8; y < row * 8 + 8; y++)
            for (int x = col * 8; x < col * 8 + 8; x++)
                set.Add(f[x, y]);
        return set;
    }

    /// <summary>The list row (from the window's first row) drawn in white: the highlight.</summary>
    private static int LitRow(IndexedFrame f, int first, int rows)
    {
        for (int r = 0; r < rows; r++)
            if (Colours(f, 1, first + r).Contains(15))
                return r;
        return -1;
    }

    private static PorGameDirectory? Dir() => TestRig.PorDir() is { } d ? new PorGameDirectory(d) : null;

    [Fact]
    public void A_vmenu_longer_than_its_window_pages_with_next_and_prev_and_escape_takes_the_lit_entry()
    {
        // 0x2D669 under VMENU (DOSBox-X s44, nine entries under "PICK ONE:"): rows 18-22 show the first five, row 24
        // "NEXT" from column 1; End wraps inside the window; N moves the window by five, at most to the last full one
        // (top 4), the highlight keeping its place; other letters (A, E) do nothing; PgUp goes back; Escape stores the
        // highlighted entry (1).
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        int pick = -1;
        r.Runner.Start(() => pick = r.Player.VerticalMenu("PICK ONE:", Nine));
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(18, menu.ListRow);
        var f = Draw(r);
        Assert.Equal(0, LitRow(f, 18, 5));
        Assert.Equal([0], Colours(f, 0, 24));
        Assert.Contains((byte)15, Colours(f, 1, 24));      // NEXT, lit, from column 1
        for (int k = 0; k < 4; k++)
            Key(r, Keys.End);
        Assert.Equal(4, r.Screen.Selected);                // ECHO, the window's last row
        Key(r, Keys.End);
        Assert.Equal(0, r.Screen.Selected);                // wrapped to the window's top
        Key(r, Keys.End);
        Key(r, Keys.N);
        Assert.Equal(5, r.Screen.Selected);                // FOXTROT: top 4, second row
        f = Draw(r);
        Assert.Equal(1, LitRow(f, 18, 5));
        Assert.Contains((byte)15, Colours(f, 1, 24));      // PREV alone, lit
        Key(r, Keys.A);
        Key(r, Keys.E);
        Key(r, Keys.N);                                    // no Next shown: nothing
        Assert.NotNull(r.Runner.Pending);
        Assert.Equal(5, r.Screen.Selected);
        Key(r, Keys.PageUp);
        Assert.Equal(1, r.Screen.Selected);                // BRAVO: top 0, second row
        Key(r, Keys.Escape);
        Assert.Null(r.Runner.Pending);
        Assert.Equal(1, pick);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_two_row_prompt_leaves_four_rows_and_the_last_window_overlaps()
    {
        // DOSBox-X s44: seven entries under a prompt of two rows: rows 19-22; PgDn from the second row → DELTA..GOLF
        // (top 3 = 7 − 4), ECHO lit; Return stores 4.
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        int pick = -1;
        r.Runner.Start(() => pick = r.Player.VerticalMenu("A LONGER QUESTION THAT RUNS ONTO A SECOND LINE OF THE BOX:", Nine[..7]));
        var menu = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal(19, menu.ListRow);
        Assert.Equal(4, menu.Paging!.Rows);
        Key(r, Keys.End);
        Key(r, Keys.PageDown);
        Assert.Equal(4, r.Screen.Selected);
        var f = Draw(r);
        Assert.Equal(1, LitRow(f, 19, 4));
        Key(r, Keys.Enter);
        Assert.Equal(4, pick);
        r.Runner.Dispose();
    }

    [Fact]
    public void A_vmenu_that_fits_shows_no_words_and_takes_no_page_keys()
    {
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        int pick = -1;
        r.Runner.Start(() => pick = r.Player.VerticalMenu("PICK:", Nine[..3]));
        var f = Draw(r);
        for (int c = 0; c < 40; c++)
            Assert.Equal([0], Colours(f, c, 24));
        Key(r, Keys.PageDown);
        Key(r, Keys.N);
        Assert.Equal(0, r.Screen.Selected);
        Key(r, Keys.End);
        Key(r, Keys.End);
        Key(r, Keys.End);
        Assert.Equal(0, r.Screen.Selected);                // wraps past the list's last entry
        Key(r, Keys.Escape);
        Assert.Equal(0, pick);
        r.Runner.Dispose();
    }

    [Fact]
    public void Who_is_the_member_pick_with_select_alone_and_escape_then_select_leaves_nobody()
    {
        // GAME.OVR 0x3879: the box blanked, then START's member pick 0x37E8 with the Exit flag 0 on DS:5D92: row 24 the
        // prompt and Select; End moves the lit member (DOSBox-X s44: End twice → the third member, kept after EXIT).
        if (Dir() is not { } dir)
            return;
        var r = Start(dir);
        var party = r.Game.Memory.Party!;
        int? pick = null;
        r.Runner.Start(() =>
        {
            r.Player.Print("SOME TEXT.", true);
            pick = r.Player.PickCharacter("WHO GOES:");
        });
        var c = Assert.IsType<ChoiceRequest>(r.Runner.Pending);
        Assert.Equal("WHO GOES:", c.MenuPrompt);
        Assert.Equal(["SELECT"], c.Items);
        Assert.True(c.PartySelect);
        Assert.Equal('\0', r.Player.Text.Cell(17, 1).Item1); // the box blanked
        Key(r, Keys.E);                                     // no Exit: nothing
        Assert.NotNull(r.Runner.Pending);
        Key(r, Keys.End);
        Key(r, Keys.End);
        Key(r, Keys.Enter);
        Assert.Null(r.Runner.Pending);
        Assert.Equal(party.CurrentIndex + 2, pick);

        r.Runner.Start(() => pick = r.Player.PickCharacter("WHO GOES:"));
        Key(r, Keys.Escape);
        Assert.NotNull(r.Runner.Pending);                   // the menu's 0 clears the pick; the loop goes on
        Key(r, Keys.S);
        Assert.Equal(IEclHost.NoCharacter, pick);
        r.Runner.Dispose();
    }
}
