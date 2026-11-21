using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Tools;

/// <summary>
/// goldbox-tools: developer commands over the player's Pool of Radiance (DOS v1.3) folder.
/// <code>
///   ecl-run   GAMEDIR SCRIPT [--vector N|all] [--save SAVGAMx.DAT] [--steps N] [--follow]
///             [--menu 0,1,..] [--random 3,5,..] [--seed N] [--randseed HEX] [--out FILE.jsonl]
///   ecl-check GAMEDIR [--vm PROFILE]
///   ecl-compare GAMEDIR [FIXTURES] [--capture NAME] [--trace-out DIR]
///   view-compare GAMEDIR SAVGAMx.DAT REF.bin [--map] [--dump] [--keys ULRDSK]
///   save-area    GAMEDIR SAVGAMx.DAT OUT.DAT SCRIPT X,Y,F [--flag0 N] [--walls A,B,C]
/// </code>
/// ecl-run loads script SCRIPT (a block id of ECL1..8.DAX; its disk is found by id), optionally on top of a save
/// game's VM state, runs the vector(s) headless with the scripted host and writes the JSONL trace (stdout by default).
/// ecl-check decodes every ECL block of GAMEDIR with the VM profile PROFILE (default ecl-por-1.3; ecl-sotsb-1.3 reads a
/// Secret of the Silver Blades folder).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "ecl-run" => EclRun(args[1..]),
                "ecl-check" => EclCheck(args[1..]),
                "ecl-compare" => EclCompare(args[1..]),
                "view-compare" => ViewCommands.Compare(args[1..]),
                "save-area" => ViewCommands.SaveArea(args[1..]),
                "save-overland" => OverlandCommands.Save(args[1..]),
                "overland-compare" => OverlandCommands.Compare(args[1..]),
                "amiga-sound" => SoundCommands.AmigaRender(args[1..]),
                _ => Usage(),
            };
        }
        catch (Exception e) when (e is ArgumentException or IOException or FormatException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            usage:
              goldbox-tools ecl-run   GAMEDIR SCRIPT [--vector N|all] [--save SAVGAMx.DAT] [--steps N] [--follow]
                                      [--menu 0,1,..] [--random 3,5,..] [--seed N] [--randseed HEX] [--out FILE.jsonl]
              goldbox-tools ecl-check GAMEDIR [--vm PROFILE]   (ecl-por-1.3, ecl-sotsb-1.3)
              goldbox-tools ecl-compare GAMEDIR [FIXTURES] [--capture NAME] [--trace-out DIR]
              goldbox-tools view-compare GAMEDIR SAVGAMx.DAT REF.bin [--map] [--dump] [--keys ULRDSK]
              goldbox-tools save-area    GAMEDIR SAVGAMx.DAT OUT.DAT SCRIPT X,Y,F [--flag0 N] [--walls A,B,C]
              goldbox-tools save-overland GAMEDIR SAVGAMx.DAT OUT.DAT SCRIPT X,Y,F [CELL=VALUE …]
              goldbox-tools overland-compare GAMEDIR SAVGAMx.DAT REF.bin [--keys NESWK] [--counter N] [--dump]
              goldbox-tools amiga-sound PROGRAM OUT.wav [--song N] [--effect CUE@SECONDS …] [--seconds S] [--rate HZ] [--no-filter] [--mute MASK]
            """);
        return 2;
    }

    private static int EclRun(string[] a)
    {
        if (a.Length < 2)
            return Usage();
        var dir = new PorGameDirectory(a[0]);
        int script = int.Parse(a[1], CultureInfo.InvariantCulture);
        string vectorArg = "init";
        string? save = null, outPath = null;
        int steps = 10_000;
        bool follow = false;
        var host = new ScriptedEclHost();
        for (int i = 2; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--vector": vectorArg = a[++i]; break;
                case "--save": save = a[++i]; break;
                case "--steps": steps = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--follow": follow = true; break;
                case "--out": outPath = a[++i]; break;
                case "--seed": host.RandomFallback = new Random(int.Parse(a[++i], CultureInfo.InvariantCulture)); break;
                case "--randseed":
                    host.Rng = new TurboPascalRandom(uint.Parse(a[++i], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    break;
                case "--menu":
                    foreach (var v in a[++i].Split(','))
                        host.MenuChoices.Enqueue(ushort.Parse(v, CultureInfo.InvariantCulture));
                    break;
                case "--random":
                    foreach (var v in a[++i].Split(','))
                        host.RandomValues.Enqueue(ushort.Parse(v, CultureInfo.InvariantCulture));
                    break;
                default: throw new ArgumentException("unknown option " + a[i]);
            }
        }

        int disk = dir.DiskOf(script) ?? throw new ArgumentException($"no ECL block {script} in {dir.Root}");
        var memory = new EclMemory();
        var vm = new EclVm(memory, host) { Scripts = dir };
        GoldBox.Core.Game.FightKit.WireLevels(vm.Effects, dir.Program()?.LevelTables());  // ADDNPC's level stats, as in PorGame
        using var writer = outPath is null ? Console.Out : new StreamWriter(outPath);
        vm.Trace = new JsonlTraceWriter(writer);

        if (save is not null)
        {
            var sg = PorSaveGame.Open(dir.Find(save) ?? save);
            memory.Party = PorParty.Load(sg, Path.GetDirectoryName(Path.GetFullPath(dir.Find(save) ?? save))!, dir.Root);
            sg.ApplyTo(vm);
            if (sg.ScriptId != script || sg.Disk != disk)
                vm.LoadScript(script, disk);
            else
                vm.Trace.Event("restore", new Dictionary<string, string> { ["script"] = Inv(script), ["disk"] = Inv(disk) });
        }
        else
        {
            memory.Disk = (byte)disk;
            vm.LoadScript(script, disk);
        }

        int[] vectors = vectorArg switch
        {
            "all" => [4, 0, 1, 2, 3],
            "init" => [EclLayout.VectorInit],
            _ => [int.Parse(vectorArg, CultureInfo.InvariantCulture)],
        };
        int rc = 0;
        foreach (var v in vectors)
        {
            var r = vm.RunVector(v, steps);
            if (follow && r.Reason == EclStopReason.NewScript)
                r = vm.FollowScriptChanges(steps);
            Console.Error.WriteLine($"vector {v}: {r.Reason} after {r.Steps} steps{(r.Message is null ? "" : ": " + r.Message)}");
            if (r.Reason is EclStopReason.Fault or EclStopReason.UnhandledOpcode)
                rc = 1;
            if (r.Reason == EclStopReason.NewScript && !follow)
                break;
        }
        Console.Error.WriteLine($"diagnostics: {vm.DiagnosticCount}; host calls: {host.Calls.Count}");
        return rc;
    }

    private static int EclCheck(string[] a)
    {
        if (a.Length < 1)
            return Usage();
        var dir = new PorGameDirectory(a[0]);
        var profile = EclProfiles.Default;
        for (int i = 1; i < a.Length; i++)
        {
            if (a[i] == "--vm" && i + 1 < a.Length)
                profile = EclProfiles.TryGet(a[++i], out var p)
                    ? p
                    : throw new ArgumentException($"unknown VM profile {a[i]} (have: {string.Join(", ", EclProfiles.Names)})");
            else
                throw new ArgumentException("unknown option " + a[i]);
        }
        int blocks = 0, insns = 0, hardErr = 0, specErr = 0, overlaps = 0, unhandled = 0, hazards = 0;
        int bytes = 0, hardBytes = 0, specBytes = 0;
        foreach (var (disk, file) in dir.EclFiles())
        {
            foreach (var e in file.Entries)
            {
                var an = new EclAnalysis(file.Decode(e), profile);
                blocks++;
                insns += an.Instructions.Count;
                hardErr += an.HardErrors.Count();
                specErr += an.Errors.Count - an.HardErrors.Count();
                overlaps += an.Overlaps.Count;
                unhandled += an.Unhandled.Count;
                hazards += an.Hazards.Count;
                var cov = an.ByteCoverage();
                bytes += cov.Hard + cov.Speculative + cov.Unreached;
                hardBytes += cov.Hard;
                specBytes += cov.Speculative;
                Console.WriteLine($"ECL{disk} block {e.Id,2}: {an.Instructions.Count,5} instructions, bytes hard {cov.Hard,5} " +
                                  $"spec {cov.Speculative,5} unreached {cov.Unreached,4}, {an.HardErrors.Count()} hard errors, {an.Overlaps.Count} overlaps");
            }
        }
        Console.WriteLine($"blocks {blocks}, instructions {insns}, hard errors {hardErr}, speculative errors {specErr}, " +
                          $"overlaps {overlaps}, unhandled {unhandled}, IF-skip hazards {hazards}");
        Console.WriteLine($"script bytes {bytes}: hard {hardBytes}, speculative only {specBytes}, not reached {bytes - hardBytes - specBytes}");
        return hardErr == 0 && overlaps == 0 && unhandled == 0 ? 0 : 1;
    }

    /// <summary>
    /// Replays the golden DOSBox-X captures (tests/fixtures-private/por/dosbox-v1.3, found above the current folder
    /// unless given) in the VM and prints, per capture, match or the first divergence. Exit code 1 if any diverged.
    /// </summary>
    private static int EclCompare(string[] a)
    {
        if (a.Length < 1)
            return Usage();
        var dir = new PorGameDirectory(a[0]);
        string? fixtures = null, only = null, traceOut = null;
        for (int i = 1; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--capture": only = a[++i]; break;
                case "--trace-out": traceOut = a[++i]; break;
                default:
                    if (a[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("unknown option " + a[i]);
                    fixtures = a[i];
                    break;
            }
        }
        fixtures ??= PorGoldenCaptures.FindFixtures(Directory.GetCurrentDirectory())
            ?? throw new ArgumentException("fixtures folder not found; pass it after GAMEDIR");
        int diverged = 0;
        foreach (var s in PorGoldenCaptures.All.Where(s => only is null || s.Name == only))
        {
            var cmp = new EclCaptureComparer(fixtures, dir);
            StreamWriter? tw = null;
            if (traceOut is not null)
            {
                Directory.CreateDirectory(traceOut);
                tw = new StreamWriter(Path.Combine(traceOut, s.Name + ".jsonl"));
                cmp.TraceOut = tw;
            }
            var r = cmp.Compare(s);
            tw?.Dispose();
            Console.Write(r.ToString());
            if (r.Outcome == CaptureOutcome.Diverged)
                diverged++;
        }
        Console.WriteLine($"diverged: {diverged}");
        return diverged == 0 ? 0 : 1;
    }

    private static string Inv(int v) => v.ToString(CultureInfo.InvariantCulture);
}
