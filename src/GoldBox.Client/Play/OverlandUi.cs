using GoldBox.Client.Input;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The explore screen's overland parts (DOSBox-X, PoR v1.3 EGA, s19): the 120 × 120 view at pixel (8, 8) (window
/// cells 1–15, no inner box), the overland command line (DS:04DF: the 3D line without its first word), the arrows'
/// four directions, and mouse clicks on the view. The rules are Core's (<see cref="Overland"/>).
/// </summary>
public static class OverlandUi
{
    /// <summary>The view's top-left pixel (measured: 0 differing pixels against DOSBox-X at (8, 8)).</summary>
    public const int ViewX = 8, ViewY = 8;

    /// <summary>The screen shows the overland (DS:49FA 2–4 and $49E6 = 0, the redraw's test).</summary>
    public static bool Shown(PorGame game) => Overland.Shown(game.Memory);

    /// <summary>The overland command line takes the commands (DS:49FA 2–4, the command routine's test).</summary>
    public static bool Commands(PorGame game) => Overland.Commands(game.Memory);

    /// <summary>
    /// The overland command line from the 3D one: the program's DS:04DF is the DS:04B6 string without its first word
    /// (<see cref="PorStartExe.FindCommandLine"/> finds the pair by that structure). It uses the shared highlight cell
    /// like every menu (<see cref="MenuHighlight"/>): DOSBox-X loads of slots B–I (cells 1–8 by the load menu)
    /// highlight VIEW, ENCAMP, SEARCH, LOOK, then CAST (past the line's five entries).
    /// </summary>
    public static string[] CommandLine(string[] threeD) => threeD.Length > 1 ? threeD[1..] : threeD;

    /// <summary>
    /// On the overland, a picture (PIC, portrait) or the stand-in text are shown as in the 3D view: the window (cells
    /// 1–15) black, the 3D view's box at cells 2–14 and the picture at pixel (24, 24) (DOSBox-X, s19: ECL7 block 26's
    /// north edge of the city, CLEARBOX / CALL $0806 / PICTURE 40 / PRINTCLR / HMENU). PICTURE draws the box: with $49E6
    /// = 0 and v ≠ 255 it draws the window (3, 3)–(13, 13) first (0x15B5–0x15E8, START 0x41F8); in the 3D view ($49E6 ≠
    /// 0) it does not. CLEARBOX alone blanks cells 1–15 with no box (P + D s41: vector 1 = CLEARBOX after Look on the
    /// overland). A sprite (SPRIT, an encounter's monsters) is drawn over the overland tiles with no box (DOSBox-X: giant
    /// snakes at 0,0 of block 25; s39: SETUPMON and APPROACH at 10,20 of block 26). The stand-in text
    /// (<see cref="ClientPlayer.ViewOverlay"/>, kept beside every encounter picture for missing art) boxes only with no
    /// picture. [?] CALL $0806 is not read.
    /// </summary>
    public static bool Boxed(ClientPlayer player) =>
        player.ViewPicture is { Kind: not ViewPictureKind.Sprite }
        || (player.ViewPicture is null && player.ViewOverlay is not null);

    /// <summary>Blanks the overland window (cells 1–15).</summary>
    public static void Blank(IndexedFrame f) => f.FillRect(ViewX, ViewY, Overland.ViewPixels, Overland.ViewPixels, 0);

    /// <summary>The overland command for an arrow action (Up / Right / Down / Left = N / E / S / W), or null.</summary>
    public static AreaCommand? Travel(ActionInput input) =>
        input.Pressed(GameAction.Forward) ? AreaCommand.TravelNorth
        : input.Pressed(GameAction.TurnRight) ? AreaCommand.TravelEast
        : input.Pressed(GameAction.TurnAround) ? AreaCommand.TravelSouth
        : input.Pressed(GameAction.TurnLeft) ? AreaCommand.TravelWest
        : null;

    /// <summary>The column of the view mode's window (DS:3C76[DS:49FA]; the area's <c>overland.column</c>), or null.</summary>
    public static int? Column(PorGame game) =>
        game.Content.OverlandColumn(game.Memory.ViewMode - Overland.FirstMode + PorOverland.FirstScript);

    /// <summary>The action of a click on the view: towards the clicked square from the party's marker (the longer axis).</summary>
    public static GameAction? Click(PorGame game, int x, int y)
    {
        int vx = x - ViewX, vy = y - ViewY;
        if (vx < 0 || vy < 0 || vx >= Overland.ViewPixels || vy >= Overland.ViewPixels)
            return null;
        var (px, py) = Overland.Position(game.Memory);
        int column = Column(game) ?? 0;
        var (mc, mr) = Overland.MarkerCell(column, px, py);
        int dx = vx - (mc * Overland.TilePixels + Overland.TilePixels / 2);
        int dy = vy - (mr * Overland.TilePixels + Overland.TilePixels / 2);
        if (Math.Abs(dx) < Overland.TilePixels / 2 && Math.Abs(dy) < Overland.TilePixels / 2)
            return null; // on the party itself
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? GameAction.TurnRight : GameAction.TurnLeft;
        return dy > 0 ? GameAction.TurnAround : GameAction.Forward;
    }

    /// <summary>
    /// Draws the view as the area loop left it (<see cref="PorOverlandArt.Draw(IndexedFrame, int, int, AreaLoop, OverlandMap, int)"/>);
    /// false when the content has no overland or no column for the script.
    /// </summary>
    public static bool Draw(IndexedFrame f, PorGame game, OverlandArt art)
    {
        if (game.Content.Overland() is not { } map || Column(game) is not { } column)
            return false;
        art.Art.Draw(f, ViewX, ViewY, game.Loop, map, column);
        return true;
    }
}
