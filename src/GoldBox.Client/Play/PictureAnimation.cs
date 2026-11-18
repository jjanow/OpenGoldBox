namespace GoldBox.Client.Play;

/// <summary>
/// The animation of the picture window (read s19, GAME.OVR; docs/re/por-art-formats.md §3 "Animation"). The loader
/// 0x2E170 fills the frame table at DS:6ABE (count; current frame DS:6ABF = 1; per frame a delay dword and a pointer),
/// each later frame of a PIC or FINAL block XORed with the first, so every frame is a whole picture. Only the menu
/// routine 0x2CC8C plays it: while it waits for a key, when its animate flag ([bp+0xA]) is set (or VM $4CFE ≠ 0 [?]
/// not modelled), it draws the current frame at cell (3, 3) on every pass (0x2CD7C) and moves to the next one (the last
/// wraps to the first) once the time since the menu opened, or since the last change, reaches the frame's delay × 10
/// centiseconds (GetTime, 0x709:0xC3F; 0x2CDB1). The current frame stays between menus; a new menu restarts the clock.
/// DOSBox-X (s19, the camp's PIC 29, delays 2 and 2): the two frames alternate about every 0.22 s (the BIOS clock moves
/// in 55 ms ticks). [?] A delay of 0 changes the frame on the next pass; the Client changes it on its next frame.
/// </summary>
public sealed class PictureAnimation
{
    private object? _picture;
    private TimeSpan _since;

    /// <summary>The frame shown (0-based; the program's DS:6ABF − 1).</summary>
    public int Frame { get; private set; }

    /// <summary>A picture is loaded: a different one starts on its first frame (the loader skips one already loaded).</summary>
    public void Show(object? picture)
    {
        if (Equals(picture, _picture))
            return;
        _picture = picture;
        Frame = 0;
    }

    /// <summary>A menu with the animate flag opened at <paramref name="now"/>: the clock restarts.</summary>
    public void MenuOpened(TimeSpan now) => _since = now;

    /// <summary>
    /// One pass of the waiting menu at <paramref name="now"/> over frames with these delays (units of 100 ms): the
    /// frame moves on when its delay has run out.
    /// </summary>
    public void Tick(TimeSpan now, IReadOnlyList<uint> delays)
    {
        if (delays.Count < 2)
        {
            Frame = 0;
            return;
        }
        if (Frame >= delays.Count)
            Frame = 0;
        if (now - _since >= TimeSpan.FromMilliseconds(100.0 * delays[Frame]))
        {
            Frame = (Frame + 1) % delays.Count;
            _since = now;
        }
    }
}
