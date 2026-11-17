using System.Globalization;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Tools;

/// <summary>
/// view-compare: renders the 3D view (or the area map) of a save in our code and compares it pixel by pixel with a
/// DOSBox-X screenshot reduced to EGA indices (320×200 bytes, the view at pixel 24,24).
/// </summary>
internal static class ViewCommands
{
    public const int ViewX = 24, ViewY = 24;

    public static int Compare(string[] a)
    {
        if (a.Length < 3)
        {
            Console.Error.WriteLine("usage: goldbox-tools view-compare GAMEDIR SAVGAMx.DAT REF.bin [--map] [--dump]");
            return 2;
        }
        var dir = new PorGameDirectory(a[0]);
        bool map = a.Contains("--map"), dump = a.Contains("--dump");
        var art = PorViewArt.Load(dir);
        var area = PorArea.FromSave(dir, art, dir.Find(a[1]) ?? a[1]);
        var frame = area.RenderView(art, map);
        var reference = File.ReadAllBytes(a[2]);
        if (reference.Length != 320 * 200)
            throw new ArgumentException("reference must be 320×200 EGA indices");
        int bad = 0;
        var rows = new List<string>();
        for (int y = 0; y < ViewRaster.ViewPixels; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < ViewRaster.ViewPixels; x++)
            {
                byte ours = frame[x, y], theirs = reference[(ViewY + y) * 320 + ViewX + x];
                if (ours != theirs)
                    bad++;
                sb.Append(ours == theirs ? '.' : ours.ToString("x", CultureInfo.InvariantCulture)[0]);
            }
            rows.Add(sb.ToString());
        }
        Console.WriteLine($"{Path.GetFileName(a[1])} script {area.Script} map {area.Map.Id} at {area.Party} " +
                          $"sky {area.SkyIndex}/{area.SkyAltIndex}: {bad} of {88 * 88} pixels differ{(map ? " (area map)" : "")}");
        if (dump && bad > 0)
            foreach (var r in rows)
                Console.WriteLine(r);
        return bad == 0 ? 0 : 1;
    }
}
