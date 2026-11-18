using GoldBox.Client.Play;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>The picture window's animation (menu routine 0x2CC8C, loader 0x2E170; docs/re/por-art-formats.md §3).</summary>
public class PictureAnimationTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Frames_change_after_their_delay_in_tenths_of_a_second_and_wrap()
    {
        var a = new PictureAnimation();
        a.Show("pic");
        a.MenuOpened(Ms(0));
        uint[] delays = [2, 0, 3];
        a.Tick(Ms(219), delays);                 // 3 ticks: 16 cs
        Assert.Equal(0, a.Frame);
        a.Tick(Ms(220), delays);                 // 4 ticks: 21 cs
        Assert.Equal(1, a.Frame);
        a.Tick(Ms(236), delays);                 // delay 0: the next pass
        Assert.Equal(2, a.Frame);
        a.Tick(Ms(549), delays);                 // 9 ticks: 49 cs, 28 since the change
        Assert.Equal(2, a.Frame);
        a.Tick(Ms(550), delays);                 // 10 ticks: 54 cs
        Assert.Equal(0, a.Frame);                // the last wraps to the first
    }

    [Fact]
    public void Frames_hold_whole_bios_ticks_as_in_dosbox()
    {
        // DOSBox-X s24: HMENU over the slave pen's PIC 42 (ECL6 block 1), delays 4, 3, 1, 2, 3, 2, held
        // 0.44, 0.33, 0.11, 0.22, 0.33, 0.22 s (8, 6, 2, 4, 6, 4 ticks of 54.9 ms), the first from the menu's opening.
        uint[] delays = [4, 3, 1, 2, 3, 2];
        int[] ticks = [8, 6, 2, 4, 6, 4];
        foreach (double open in new[] { 0.0, 20.0, 54.0 })      // the menu opens anywhere within a tick
        {
            var a = new PictureAnimation();
            a.Show("pic");
            a.MenuOpened(TimeSpan.FromMilliseconds(open));
            double at = Math.Floor(open / PictureAnimation.TickMs) * PictureAnimation.TickMs;
            for (int k = 0; k < 12; k++)
            {
                int f = k % delays.Length;
                Assert.Equal(f, a.Frame);
                double next = at + ticks[f] * PictureAnimation.TickMs;
                a.Tick(TimeSpan.FromMilliseconds(next - 1), delays);
                Assert.Equal(f, a.Frame);
                a.Tick(TimeSpan.FromMilliseconds(next + 1), delays);
                Assert.Equal((f + 1) % delays.Length, a.Frame);
                at = next;
            }
        }
    }

    [Fact]
    public void A_new_menu_restarts_the_clock_and_keeps_the_frame()
    {
        var a = new PictureAnimation();
        a.Show("pic");
        a.MenuOpened(Ms(0));
        uint[] delays = [1, 5];
        a.Tick(Ms(110), delays);                 // 2 ticks: 10 cs
        Assert.Equal(1, a.Frame);
        a.MenuOpened(Ms(10_000));
        a.Tick(Ms(10_400), delays);
        Assert.Equal(1, a.Frame);
        a.Show("pic");                           // the same picture again: the loader skips it
        Assert.Equal(1, a.Frame);
        a.Show("other");
        Assert.Equal(0, a.Frame);
    }

    [Fact]
    public void A_still_picture_stays_on_its_frame()
    {
        var a = new PictureAnimation();
        a.Show("pic");
        a.MenuOpened(Ms(0));
        a.Tick(Ms(5000), [7]);
        Assert.Equal(0, a.Frame);
    }

    [Fact]
    public void Later_frames_are_xor_deltas_against_the_first()
    {
        var first = DaxPicture.Create(8, 1, 0, 0, [new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }]);
        var delta = DaxPicture.Create(8, 1, 0, 0, [new byte[] { 0, 0, 3, 0, 15, 0, 0, 8 }], delay: 4);
        var f = ViewPictures.Composed(first, delta);
        Assert.Equal(new byte[] { 1, 2, 0, 4, 10, 6, 7, 0 }, f.Items[0]);
        Assert.Equal(4u, f.Delay);
    }

    [Fact]
    public void The_camp_picture_frames_match_dosbox()
    {
        // PIC 29 (the camp, every disk): DOSBox-X s19 showed exactly the first frame and the first XOR the second
        // (0 differing pixels in the 88 × 88 window), alternating; delays 2 and 2.
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return;
        var content = new PorGameDirectory(d);
        var pictures = new ViewPictures(content);
        var v = new ViewPicture(ViewPictureKind.Picture, 29, null, 0, 1);
        Assert.Equal([2u, 2u], pictures.Delays(v));
        var frames = pictures.Find("PIC", 29, 1)!.Frames;
        var composed = ViewPictures.Composed(frames[0], frames[1]);
        Assert.Equal(88 * 88, composed.Items[0].Length);
        int changed = Enumerable.Range(0, 88 * 88).Count(i => composed.Items[0][i] != frames[0].Items[0][i]);
        Assert.Equal(676, changed);   // the flames: DOSBox-X's two frames differ in 676 pixels
    }
    [Fact]
    public void A_picture_on_another_disk_only_is_not_drawn()
    {
        // PICTURE (0x159C) loads from PIC<disk> of the current disk only; a block that is not there draws nothing, the
        // window keeps the view (DOSBox-X s22). The Client knows the art exists, so it shows no stand-in either.
        var d = Environment.GetEnvironmentVariable("GOLDBOX_POR_DIR");
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d))
            return;
        var content = new PorGameDirectory(d);
        var (id, on, off) = (from i in Enumerable.Range(0, 256)
                             from a in Enumerable.Range(1, 8)
                             from b in Enumerable.Range(1, 8)
                             where content.Picture($"PIC{a}", i) is not null && content.Picture($"PIC{b}", i) is null
                             select (i, a, b)).First();
        var pictures = new ViewPictures(content);
        Assert.NotNull(pictures.Find("PIC", id, on));
        Assert.Null(pictures.Find("PIC", id, off));
        Assert.NotNull(pictures.Find("PIC", id, off, anyDisk: true));
        var v = new ViewPicture(ViewPictureKind.Picture, id, null, 0, off);
        var f = new Core.Explore.IndexedFrame(320, 200);
        Assert.False(pictures.Draw(f, 0, 0, v));
        Assert.True(pictures.Exists(v));
        Assert.False(pictures.Exists(new ViewPicture(ViewPictureKind.Picture, 999, null, 0, off)));
    }
}
