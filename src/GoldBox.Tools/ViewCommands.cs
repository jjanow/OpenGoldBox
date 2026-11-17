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
            Console.Error.WriteLine("usage: goldbox-tools view-compare GAMEDIR SAVGAMx.DAT REF.bin [--map] [--dump] [--keys ULRDS]");
            return 2;
        }
        var dir = new PorGameDirectory(a[0]);
        bool map = a.Contains("--map"), dump = a.Contains("--dump");
        var art = PorViewArt.Load(dir);
        var savePath = dir.Find(a[1]) ?? a[1];
        IndexedFrame frame;
        string what;
        if (Array.IndexOf(a, "--keys") is var ki and >= 0 && ki + 1 < a.Length)
        {
            // Commands after the load, through the area loop with headless hosts (menus would stop the run):
            // U step, L/R/D turn, S search, K look.
            var host = new GoldBox.Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true };
            var player = new PorPlayer(host, new GoldBox.Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true },
                new GoldBox.Core.Game.RecordingCombatRunner(), new HeadlessAreaUi());
            var game = PorGame.FromSave(dir, art, savePath, player, 0);
            foreach (char c in a[ki + 1])
                game.Loop.Execute(c switch
                {
                    'U' => AreaCommand.Forward,
                    'L' => AreaCommand.TurnLeft,
                    'R' => AreaCommand.TurnRight,
                    'D' => AreaCommand.TurnAround,
                    'S' => AreaCommand.Search,
                    'K' => AreaCommand.Look,
                    _ => throw new ArgumentException($"key {c}: use U L R D S K"),
                });
            frame = game.RenderView(map && game.Loop.ShowAreaMap);
            var p = game.Memory.Position;
            what = $"script {game.Vm.CurrentScript} map {game.Loop.Map?.Id} at {p.X},{p.Y} after {a[ki + 1]} sky {game.Memory.Read(0x49FD)}/{game.Memory.Read(0x49FE)}";
        }
        else
        {
            var area = PorArea.FromSave(dir, art, savePath);
            frame = area.RenderView(art, map && area.AreaMapAllowed);
            what = $"script {area.Script} map {area.Map.Id} at {area.Party} sky {area.SkyIndex}/{area.SkyAltIndex}";
        }
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
        Console.WriteLine($"{Path.GetFileName(a[1])} {what}: {bad} of {88 * 88} pixels differ{(map ? " (area map key)" : "")}");
        if (dump && bad > 0)
            foreach (var r in rows)
                Console.WriteLine(r);
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// save-area: a copy of a save with the party put in another area, for side-by-side checks in DOSBox-X: the
    /// script area holds ECL block SCRIPT (from its disk), $49C5 = SCRIPT, the disk byte and cell $6E12, the position, and the
    /// script's flags $4A00–$4A1F (kept on a load) set to the given value (default 0). Main menu → Load then
    /// re-reads map $49C5 and runs the script's vector 4, in the game and in <c>PorArea.FromSave</c>.
    /// </summary>
    public static int SaveArea(string[] a)
    {
        if (a.Length < 5)
        {
            Console.Error.WriteLine("usage: goldbox-tools save-area GAMEDIR SAVGAMx.DAT OUT.DAT SCRIPT X,Y,F [--flag0 N] [--walls A,B,C]");
            return 2;
        }
        var dir = new PorGameDirectory(a[0]);
        var source = File.ReadAllBytes(dir.Find(a[1]) ?? a[1]);
        int script = int.Parse(a[3], CultureInfo.InvariantCulture);
        var p = a[4].Split(',');
        int f = p[2].ToUpperInvariant() switch { "N" => 0, "E" => 2, "S" => 4, "W" => 6, _ => throw new ArgumentException("facing N/E/S/W") };
        string? Opt(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        int flag0 = Opt("--flag0") is { } fl ? int.Parse(fl, CultureInfo.InvariantCulture) : 0;
        int[]? walls = Opt("--walls")?.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var save = PorSaveGame.PlaceInArea(source, dir, script, int.Parse(p[0], CultureInfo.InvariantCulture),
            int.Parse(p[1], CultureInfo.InvariantCulture), f, flag0, walls);
        int disk = save[PorSaveGame.OffDisk];
        File.WriteAllBytes(a[2], save);
        var check = PorArea.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(Path.GetFullPath(dir.Find(a[1]) ?? a[1]))!);
        Console.WriteLine($"{a[2]}: script {script} disk {disk}; our load: map {check.Map.Id} at {check.Party} sky {check.SkyIndex}/{check.SkyAltIndex}, entry {check.EntryRun.Reason}");
        return 0;
    }

    /// <summary>Door menus answer Exit, the camp runs no rest.</summary>
    private sealed class HeadlessAreaUi : IAreaUi
    {
        public GoldBox.Core.Doors.DoorAction? ChooseDoorAction(GoldBox.Core.Doors.DoorBarrier barrier,
            IReadOnlyList<GoldBox.Core.Doors.DoorAction> choices) => null;
        public void Bump() { }
        public void Message(AreaMessage message) { }
        public bool Camp() => false;
    }
}
