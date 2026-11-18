namespace GoldBox.Client.Play;

/// <summary>
/// The animation of the picture window (read s19, GAME.OVR; docs/re/por-art-formats.md §3 "Animation"). The loader
/// 0x2E170 fills the frame table at DS:6ABE (count; current frame DS:6ABF = 1; per frame a delay dword and a pointer),
/// each later frame of a PIC or FINAL block XORed with the first, so every frame is a whole picture. Only the menu
/// routine 0x2CC8C plays it: while it waits for a key, when its animate flag ([bp+0xA]) is set (or VM $4CFE ≠ 0 [?]
/// not modelled), it draws the current frame at cell (3, 3) on every pass (0x2CD7C) and moves to the next one (the last
/// wraps to the first) once the time since the menu opened, or since the last change, reaches the frame's delay × 10
/// centiseconds (GetTime, 0x709:0xC3F; 0x2CDB1). The current frame stays between menus; a new menu restarts the clock.
/// GetTime reads the DOS clock, which moves once per BIOS timer tick (65536 / 1193182 s, about 55 ms) in steps of 5 or
/// 6 centiseconds (DOSBox-X's INT 21h 2Ch: ticks × 100 × 65536 / 1193182, rounded down; [?] real DOS's conversion
/// not read: only a delay whose tick count sits on a boundary, 5 in the PoR data, can differ by one tick, by the
/// clock's phase either way), so a frame holds for the first whole tick count whose centiseconds reach
/// delay × 10: delay 1 → 2 ticks (0.11 s), 2 → 4 (0.22 s), 3 → 6 (0.33 s), 4 → 8 (0.44 s). DOSBox-X (s24, HMENU with
/// the slave pen's PIC 42 in ECL6 block 1, delays 4, 3, 1, 2, 3, 2): 0.44, 0.33, 0.11, 0.22, 0.33, 0.22 s, the first
/// hold counted from the menu's opening; s19's camp PIC 29 (delays 2, 2) alternated every 0.22 s. The Client reads the
/// same clock from its own time. [?] A delay of 0 changes the frame on the next pass (CPU-speed dependent); the Client
/// changes it on its next frame.
/// </summary>
public sealed class PictureAnimation
{
    /// <summary>One BIOS timer tick in milliseconds (the 8253 at 1193182 Hz, divisor 65536).</summary>
    public const double TickMs = 65536 * 1000.0 / 1193182;

    private object? _picture;
    private long _since;

    /// <summary>The DOS clock at <paramref name="now"/> in centiseconds: whole ticks, converted as DOSBox-X's INT 21h 2Ch does.</summary>
    public static long Centiseconds(TimeSpan now) => (long)(Math.Floor(now.TotalMilliseconds / TickMs) * 6553600.0 / 1193182);

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
    public void MenuOpened(TimeSpan now) => _since = Centiseconds(now);

    /// <summary>
    /// One pass of the waiting menu at <paramref name="now"/> over frames with these delays (units of 100 ms): the
    /// frame moves on when the DOS clock has advanced by its delay × 10 centiseconds.
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
        long cs = Centiseconds(now);
        if (cs - _since >= 10L * delays[Frame])
        {
            Frame = (Frame + 1) % delays.Count;
            _since = cs;
        }
    }
}
