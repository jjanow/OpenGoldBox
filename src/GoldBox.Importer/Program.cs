using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;
using GoldBox.Importer;

const string usage = """
    usage: goldbox-import por GAMEDIR OUTDIR [--no-verify]
      por    Pool of Radiance (DOS): GAMEDIR is the player's game folder (ECL1.DAX …); OUTDIR must not exist or be
             empty. Writes the module (docs/module-format.md), then checks that every imported block rebuilds
             byte for byte from the module (skipped with --no-verify).
    """;

if (args.Length < 3 || args[0] != "por" || args.Skip(3).Any(a => a != "--no-verify"))
{
    Console.Error.WriteLine(usage);
    return 2;
}
string gameDir = args[1], outDir = args[2];
bool verify = !args.Contains("--no-verify");

PorImportReport report;
try
{
    report = PorImporter.Import(gameDir, outDir);
}
catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException or GoldBox.Data.Dax.DaxFormatException)
{
    Console.Error.WriteLine($"import failed: {e.Message}");
    return 1;
}

Console.WriteLine($"module written to {Path.GetFullPath(outDir)}");
Console.WriteLine($"  scripts {report.Scripts}, areas {report.Areas}, maps {report.Maps}, monsters {report.Monsters}, " +
                  $"item lists {report.ItemLists}, effect lists {report.EffectLists}, treasure lists {report.TreasureLists}, " +
                  $"item types {report.ItemTypes}");
Console.WriteLine($"  inputs: {report.Inputs.Count} files");
if (report.ItemIdCollisions.Count > 0)
{
    int n = report.ItemIdCollisions.Values.Sum(l => l.Count);
    Console.WriteLine($"  ITEM<d> and MON<d>ITM share {n} block ids ({report.CollisionsWithDifferentContent} with different bytes); " +
                      "placed treasure is kind treasureList:");
    foreach (var (disk, ids) in report.ItemIdCollisions)
        Console.WriteLine($"    disk {disk}: {string.Join(", ", ids)}");
}
foreach (var note in report.Notes)
    Console.WriteLine($"  note: {note}");

if (!verify)
    return 0;
var rt = PorRoundTrip.Check(new PorGameDirectory(gameDir), PorModule.Open(outDir));
Console.WriteLine($"round trip: {rt.Total} blocks compared ({string.Join(", ", rt.Checked.Select(kv => $"{kv.Key} {kv.Value}"))})");
foreach (var m in rt.Mismatches)
    Console.WriteLine($"  MISMATCH {m}");
Console.WriteLine(rt.Mismatches.Count == 0 ? "round trip: all identical" : $"round trip: {rt.Mismatches.Count} mismatches");
return rt.Mismatches.Count == 0 ? 0 : 1;
