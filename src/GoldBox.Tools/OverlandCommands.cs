using System.Globalization;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Tools;

/// <summary>
/// save-overland / overland-compare: the overland view (docs/re/geo-format.md "Wilderness (overland)") against
/// DOSBox-X. save-overland writes a save with the party on a wilderness script's overland; overland-compare loads a
/// save in our code, runs overland commands, renders the 120 × 120 view and compares it with a DOSBox-X screenshot
/// reduced to EGA indices (320 × 200 bytes, the view at pixel 8,8).
/// </summary>
internal static class OverlandCommands
{
    public const int ViewX = 8, ViewY = 8;

    public static int Save(string[] a)
    {
        if (a.Length < 5)
        {
            Console.Error.WriteLine("usage: goldbox-tools save-overland GAMEDIR SAVGAMx.DAT OUT.DAT SCRIPT X,Y,F [CELL=VALUE …]");
            return 2;
        }
        var dir = new PorGameDirectory(a[0]);
        var source = File.ReadAllBytes(dir.Find(a[1]) ?? a[1]);
        int script = int.Parse(a[3], CultureInfo.InvariantCulture);
        var p = a[4].Split(',');
        int f = FacingOf(p[2]);
        var save = PorSaveGame.PlaceOnOverland(source, dir, script, int.Parse(p[0], CultureInfo.InvariantCulture),
            int.Parse(p[1], CultureInfo.InvariantCulture), f);
        foreach (var kv in a.Skip(5))
        {
            // CELL=VALUE: a $4900-area cell (hex address) set before the load, e.g. 4AB3=254.
            var parts = kv.Split('=');
            int cell = int.Parse(parts[0].TrimStart('$'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int v = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int o = PorSaveGame.Off4900 + 2 * (cell - 0x4900);
            save[o] = (byte)v;
            save[o + 1] = (byte)(v >> 8);
        }
        File.WriteAllBytes(a[2], save);
        Console.WriteLine($"{a[2]}: script {script} mode {PorOverland.ViewMode(script)} at {p[0]},{p[1]} {p[2]}");
        return 0;
    }

    private static int FacingOf(string s) => s.ToUpperInvariant() switch
    {
        "N" => 0, "E" => 2, "S" => 4, "W" => 6, _ => throw new ArgumentException("facing N/E/S/W"),
    };

    public static int Compare(string[] a)
    {
        if (a.Length < 3)
        {
            Console.Error.WriteLine("usage: goldbox-tools overland-compare GAMEDIR SAVGAMx.DAT REF.bin [--keys NESWK] [--counter N] [--dump]");
            return 2;
        }
        var dir = new PorGameDirectory(a[0]);
        string? Opt(string name) => Array.IndexOf(a, name) is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        var host = new Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true };
        var ui = new Ui();
        var player = new PorPlayer(host, new Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true },
            new Core.Game.RecordingCombatRunner(), ui);
        var game = PorGame.FromSave(dir, PorViewArt.Load(dir), dir.Find(a[1]) ?? a[1], player, 0);
        foreach (char c in Opt("--keys") ?? "")
            game.Loop.Execute(c switch
            {
                'N' => AreaCommand.TravelNorth,
                'E' => AreaCommand.TravelEast,
                'S' => AreaCommand.TravelSouth,
                'W' => AreaCommand.TravelWest,
                'K' => AreaCommand.Look,
                _ => throw new ArgumentException($"key {c}: use N E S W K"),
            });
        var m = game.Memory;
        if (!Overland.Shown(m) || game.Content.Overland() is not { } map
            || game.Content.OverlandColumn(m.ViewMode - Overland.FirstMode + PorOverland.FirstScript) is not { } column)
        {
            Console.WriteLine($"not on the overland: script {game.Vm.CurrentScript} mode {m.ViewMode} $49E6 {m.Read(0x49E6)}");
            return 1;
        }
        // --counter N, or N+M…: marker frames drawn one over the other (a marker-only redraw between tile draws).
        // Without it: the frames the area loop's counter gives (AreaLoop.MarkerCounter).
        var art = PorOverlandArt.Load(dir);
        if (m.Read(OverlandRecovery.RiverCell) != 0 && map.Recovery is { } tables) // Load's still recovery (0x1FC89, a fresh session)
            OverlandRecovery.ApplyFinal(tables, t => art.Tile(t, outdoor: false)?.Pixels);
        IndexedFrame frame;
        string counter;
        if (Opt("--counter") is { } co)
        {
            var counters = co.Split('+').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            frame = art.Render(m, map, column, counters[0]);
            foreach (int c in counters.Skip(1))
                art.DrawMarker(frame, 0, 0, Overland.MarkerCell(column, Overland.Position(m).X, Overland.Position(m).Y), c,
                    m.Position.Facing);
            counter = co;
        }
        else
        {
            frame = art.Render(game.Loop, map, column);
            counter = $"loop {game.Loop.MarkerCounter}{(game.Loop.OverlandInterfacePending ? " interface" : "")}";
        }
        var reference = File.ReadAllBytes(a[2]);
        if (reference.Length != 320 * 200)
            throw new ArgumentException("reference must be 320×200 EGA indices");
        var (px, py) = Overland.Position(m);
        var (mc, mr) = Overland.MarkerCell(column, px, py);
        int bad = 0, badMarker = 0;
        var rows = new List<string>();
        for (int y = 0; y < Overland.ViewPixels; y++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < Overland.ViewPixels; x++)
            {
                byte ours = frame[x, y], theirs = reference[(ViewY + y) * 320 + ViewX + x];
                bool inMarker = x / Overland.TilePixels == mc && y / Overland.TilePixels == mr;
                if (ours != theirs)
                {
                    if (inMarker)
                        badMarker++;
                    else
                        bad++;
                }
                sb.Append(ours == theirs ? '.' : ours.ToString("x", CultureInfo.InvariantCulture)[0]);
            }
            rows.Add(sb.ToString());
        }
        var (sx, sy) = Overland.StatusPosition(m);
        Console.WriteLine($"{Path.GetFileName(a[1])} script {game.Vm.CurrentScript} mode {m.ViewMode} at {sx},{sy} " +
            $"{Facing.Letter(m.Position.Facing)} {GameClock.Format(m)} after '{Opt("--keys")}'{(ui.River ? " (river)" : "")}: " +
            $"{bad} pixels differ outside the marker, {badMarker} in the marker square (counter {counter}), sha256 " +
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(frame.Pixels)));
        if (a.Contains("--dump") && bad + badMarker > 0)
            foreach (var r in rows)
                Console.WriteLine(r);
        return bad + badMarker == 0 ? 0 : 1;
    }

    private sealed class Ui : IAreaUi
    {
        public bool River { get; private set; }
        public Core.Doors.DoorAction? ChooseDoorAction(Core.Doors.DoorBarrier barrier,
            IReadOnlyList<Core.Doors.DoorAction> choices) => null;
        public void Bump() { }
        public void Message(AreaMessage message) => River |= message == AreaMessage.RiverBlocks;
        public bool Camp() => false;
    }
}
