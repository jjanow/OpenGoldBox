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
        a.Tick(Ms(199), delays);
        Assert.Equal(0, a.Frame);
        a.Tick(Ms(200), delays);
        Assert.Equal(1, a.Frame);
        a.Tick(Ms(216), delays);                 // delay 0: the next pass
        Assert.Equal(2, a.Frame);
        a.Tick(Ms(515), delays);
        Assert.Equal(2, a.Frame);
        a.Tick(Ms(516), delays);
        Assert.Equal(0, a.Frame);                // the last wraps to the first
    }

    [Fact]
    public void A_new_menu_restarts_the_clock_and_keeps_the_frame()
    {
        var a = new PictureAnimation();
        a.Show("pic");
        a.MenuOpened(Ms(0));
        uint[] delays = [1, 5];
        a.Tick(Ms(100), delays);
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
}
