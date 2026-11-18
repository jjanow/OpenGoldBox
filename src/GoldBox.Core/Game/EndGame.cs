namespace GoldBox.Core.Game;

/// <summary>One step of the end game (ECL PROGRAM 8, GAME.OVR 0x20941).</summary>
public abstract record EndGameStep;

/// <summary>The text window (columns 1–38, rows 17–22) cleared (START 0x7E63).</summary>
public sealed record EndGameClearText : EndGameStep;

/// <summary>A line printed at column 1 of <paramref name="Row"/> in green (10): the program's text <paramref name="Text"/>
/// (catalogue key <c>End.n</c>, n = <paramref name="Text"/>).</summary>
public sealed record EndGameLine(int Row, int Text) : EndGameStep;

/// <summary>
/// Block <paramref name="Block"/> of <c>FINAL&lt;disk&gt;</c> drawn (START 0x53B1) with its top-left at screen cell
/// (<paramref name="Column"/>, <paramref name="Row"/>), cells of 8 pixels; the block's own x / y are 0 in v1.3.
/// </summary>
public sealed record EndGamePicture(int Block, int Column, int Row) : EndGameStep;

/// <summary>
/// The animation 0x206A3: block <paramref name="Block"/> (frames with delays) at screen cell (<paramref name="Column"/>,
/// <paramref name="Row"/>), played <paramref name="Loops"/> times. Frame k shows until (game speed + 3) × its delay has
/// passed (START 0x7CCF: hundredths of a second from the DOS clock); the loop ends on the last frame.
/// </summary>
public sealed record EndGameAnimation(int Block, int Column, int Row, int Loops) : EndGameStep;

/// <summary>The program's Delay (START 0x7E4E): game speed × 225 milliseconds.</summary>
public sealed record EndGamePause : EndGameStep;

/// <summary>
/// Pool of Radiance DOS v1.3: the end game, ECL PROGRAM 8 (GAME.OVR 0x20941), run after Tyranthraxus' fight
/// (ECL5 block 7 $A82A). Program read s19 and seen in DOSBox-X (docs/re/training.md "End game"). No key is waited for:
/// the lines print, the pictures and animations of <c>FINAL&lt;disk&gt;.DAX</c> play on the clock, then three delays;
/// the interface state DS:49F3 is 7 meanwhile and restored after, and the screen is redrawn. The script then goes on
/// (the epilogue text and NEWECL 0 to civilised Phlan): there is no return to the menu.
/// </summary>
public static class EndGame
{
    /// <summary>DS:49F3 while the end game runs (talonius "game won").</summary>
    public const byte InterfaceState = 7;

    /// <summary>The file the blocks come from: "FINAL" (GAME.OVR 0x20812) and the current disk DS:5376.</summary>
    public static string FileName(int disk) => "FINAL" + disk.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The animation's loop count for the dragon and the spirit: ((10 − speed) × 5) shr 1 (words).</summary>
    public static int Loops(int gameSpeed) => (ushort)((10 - gameSpeed) * 5) >> 1;

    /// <summary>
    /// The steps of 0x20941 for a party of <paramref name="partySize"/> ($6E3E) at game speed DS:49E2: lines 0–2 at
    /// rows 17–19; picture 1 at (1, 1); the dragon (block 2) at (3, 5); picture 3 at (1, 1), the party figures 4 at
    /// (1, 12) when more than one member, 5 at (14, 10) when more than two, 6 at (1, 10) when more than three; lines 3–8
    /// at rows 17–22; the spirit (block 7) at (3, 1); lines 9–12 at rows 17–20; block 8 at (3, 1) once; three pauses.
    /// </summary>
    public static IReadOnlyList<EndGameStep> Steps(int partySize, int gameSpeed)
    {
        int loops = Loops(gameSpeed);
        var s = new List<EndGameStep> { new EndGameClearText() };
        for (int i = 0; i < 3; i++)
            s.Add(new EndGameLine(17 + i, i));
        s.Add(new EndGamePicture(1, 1, 1));
        s.Add(new EndGameAnimation(2, 3, 5, loops));
        s.Add(new EndGamePicture(3, 1, 1));
        if (partySize > 1)
            s.Add(new EndGamePicture(4, 1, 12));
        if (partySize > 2)
            s.Add(new EndGamePicture(5, 14, 10));
        if (partySize > 3)
            s.Add(new EndGamePicture(6, 1, 10));
        s.Add(new EndGameClearText());
        for (int i = 0; i < 6; i++)
            s.Add(new EndGameLine(17 + i, 3 + i));
        s.Add(new EndGameAnimation(7, 3, 1, loops));
        s.Add(new EndGameClearText());
        for (int i = 0; i < 4; i++)
            s.Add(new EndGameLine(17 + i, 9 + i));
        s.Add(new EndGameAnimation(8, 3, 1, 1));
        s.Add(new EndGamePause());
        s.Add(new EndGamePause());
        s.Add(new EndGamePause());
        return s;
    }

    /// <summary>How long frame <paramref name="delay"/> stays up: (speed + 3) × delay hundredths of a second.</summary>
    public static TimeSpan FrameTime(int gameSpeed, uint delay) => TimeSpan.FromMilliseconds(10.0 * (gameSpeed + 3) * delay);

    /// <summary>The Delay of START 0x7E4E: speed × 225 ms.</summary>
    public static TimeSpan Pause(int gameSpeed) => TimeSpan.FromMilliseconds(225 * gameSpeed);
}
