using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

public sealed partial class CombatChooser
{
    /// <summary>The path array of START 0x25C9 ([bp−0x94], 0x94 bytes, filled with 8 = no step).</summary>
    private const int FlightPathMax = 0x94;
    /// <summary>The last position (in thirds of a square) a 3 × 3 icon fits in the 7 × 7 view.</summary>
    private const int FlightCellMax = 0x12;

    /// <summary>
    /// The moving icon START 0x25C9 (read s25; docs/re/combat-menu.md "Moving icon"), as the program runs it:
    /// <list type="bullet">
    /// <item>The path is the line 0x2FCF2 / 0x2FE1E from (from × 3) to (to × 3) in thirds of a square; each step stores
    /// its direction (DS:25C0 by the step, +0x19 of the line record), and the failing last step stores the last
    /// direction again; fewer than 2 entries: nothing. The walk reads entry 1 first, so the first step's direction is
    /// skipped and the last one repeated (a quirk of the program).</item>
    /// <item>The view: both ends in view → redrawn where it is; else when both distances are at most 6 it is centred on
    /// the midpoint; else it stays and follows the icon (ScrollView(…, 255) each time).</item>
    /// <item>Each position: when the delay is above 0 (or, with 0, the position is on a whole square on either axis) the
    /// frame is drawn, Delay(delay), erased; the frame advances only when drawn. Then one step along the path; a
    /// position past 0..18 in either axis leaves the view: in follow mode the view centre goes 3 squares past the icon on
    /// the axis it moved along and the walk goes on, else the flight ends there.</item>
    /// <item>At the end: the view brought to the target (ScrollView(target, 3)) when it is out of it, the frame drawn
    /// on the target, Delay, erased. With delay 0 (only the aim's cursor box, 0x15D02) it stays drawn until its cell is
    /// redrawn; the Client leaves it to the aim's own cursor box (`CombatView.Cursor`), which ChooseAim puts on the
    /// new candidate next.</item>
    /// </list>
    /// </summary>
    internal void Flight(FlightAnimation a)
    {
        if (a.Frames.Count == 0)
            return;
        int x0 = a.FromX, y0 = a.FromY, x1 = a.ToX, y1 = a.ToY;
        // 0x25DE–0x2691: the path.
        var path = new List<int>();
        var line = CombatMap.LineWalk.Start(x0 * 3, y0 * 3, x1 * 3, y1 * 3);
        int dir = 0;
        while (path.Count < FlightPathMax)
        {
            int px = line.Cx, py = line.Cy;
            bool more = line.Next();
            if (more)
                dir = Direction(line.Cx - px, line.Cy - py);
            path.Add(dir);
            if (!more)
                break;
        }
        int total = path.Count;
        if (total < 2)
            return;

        // 0x2692–0x27BB: where the view goes first.
        bool fixedView;
        int cx, cy;
        bool startIn = View.InView(x0, y0), endIn = View.InView(x1, y1);
        if (startIn && endIn)
            (fixedView, cx, cy) = (true, View.ViewX + 3, View.ViewY + 3);
        else if (Math.Abs(x1 - x0) <= 6 && Math.Abs(y1 - y0) <= 6)
            (fixedView, cx, cy) = (true, x0 + (x1 - x0) / 2, y0 + (y1 - y0) / 2);
        else
            (fixedView, cx, cy) = (false, View.ViewX + 3, View.ViewY + 3);
        View.Scroll(cx, cy, 255);

        int step = 0, subX = 0, subY = 0, frame = 0;
        while (true)
        {
            // 0x27CF: the position from the view, the start square of this pass kept.
            int c1 = (x0 - View.ViewX) * 3 + subX, c2 = (y0 - View.ViewY) * 3 + subY;
            int passX = x0, passY = y0;
            bool off = false;
            do
            {
                if (a.DelayMs > 0 || c1 % 3 == 0 || c2 % 3 == 0)
                {
                    DrawFlying(c1, c2, a.Frames[frame], a.DelayMs);
                    if (++frame >= a.Frames.Count)
                        frame = 0;
                }
                step++;
                int d = step < path.Count ? path[step] : CombatTables.Center;
                int dx = CombatTables.DirDx[d], dy = CombatTables.DirDy[d];
                c1 += dx;
                c2 += dy;
                if (c1 < 0 || c1 > FlightCellMax || c2 < 0 || c2 > FlightCellMax)
                    off = true;
                if (!off && step < total)
                {
                    subX += dx;
                    subY += dy;
                    if (Math.Abs(subX) == 3)
                    {
                        x0 += Math.Sign(subX);
                        cx += Math.Sign(subX);
                        subX = 0;
                    }
                    if (Math.Abs(subY) == 3)
                    {
                        y0 += Math.Sign(subY);
                        cy += Math.Sign(subY);
                        subY = 0;
                    }
                }
            }
            while (step < total && !off);
            if (step >= total)
                break;
            // 0x29CB–0x2A92: the icon left the view.
            if (Math.Sign(passX - x0) != 0)
            {
                if (x0 >= View.ViewX + 6)
                    cx = x0 + 3;
                else if (x0 <= View.ViewX)
                    cx = x0 - 3;
            }
            if (Math.Sign(passY - y0) != 0)
            {
                if (y0 >= View.ViewY + 6)
                    cy = y0 + 3;
                else if (y0 <= View.ViewY)
                    cy = y0 - 3;
            }
            View.Scroll(cx, cy, 255);
            if (fixedView)
                return;
        }
        // 0x2A95–0x2B80: on the target.
        if (!View.InView(x1, y1))
            View.Scroll(x1, y1, 3);
        if (a.DelayMs > 0)
            DrawFlying((x1 - View.ViewX) * 3, (y1 - View.ViewY) * 3, a.Frames[frame], a.DelayMs);
    }

    /// <summary>DS:25C0: the direction 0–7 of a step (dx, dy), 8 for none.</summary>
    private static int Direction(int dx, int dy)
    {
        for (int d = 0; d < 8; d++)
            if (CombatTables.DirDx[d] == Math.Sign(dx) && CombatTables.DirDy[d] == Math.Sign(dy))
                return d;
        return CombatTables.Center;
    }

    /// <summary>
    /// START 0x464:0xD71 (the frame at the position), 0x1B52 (shown), Delay, then the saved background back. The
    /// position is in 8-pixel units from the view's corner, as the map's own cell draws (0x31679, 0x32097: cell × 3)
    /// into the same buffer DS:4A20, so the Client places it as the squares. With delay 0 nothing waits (the program's
    /// Delay(0)); the positions go by within one frame.
    /// </summary>
    private void DrawFlying(int c1, int c2, FlightFrame f, int ms)
    {
        View.Flying = (c1, c2, f.Block + (f.Second ? 128 : 0), f.Mirror);
        if (Pauses && ms > 0)
            ask(new DelayRequest(TimeSpan.FromMilliseconds(ms)) { Chained = true }); // positions follow back to back
        View.Flying = null;
    }
}
