using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

const string usage = """
    usage: goldbox-import por GAMEDIR OUTDIR [--amiga AMIGADIR] [--no-verify]
           goldbox-import schemas DIR   (writes the module JSON schemas, src/GoldBox.Data/Module/Schemas/)
           goldbox-import save-export SAVE.json OUTDIR [SLOT]   (engine save → SAVGAMx.DAT + CHRDATxn.*, slot A..J)
           goldbox-import save-import SAVGAMx.DAT MODULEDIR OUT.json   (original save + its CHRDAT files → engine save)
      por    Pool of Radiance (DOS): GAMEDIR is the player's game folder (ECL1.DAX …); OUTDIR must not exist or be
             empty. Writes the module (docs/module-format.md), then checks that every imported block rebuilds
             byte for byte from the module (skipped with --no-verify). AMIGADIR holds the unpacked Amiga v1.0
             disks ('program' from disk A, the .dax files from disk B): their art is added as the Amiga overlay.
    """;

if (args is ["save-export", var saveJson, var exportDir, ..] && args.Length <= 4)
{
    // Engine save → the original SAVGAMx.DAT and CHRDATxn.* files (for the DOS game). Existing files are not overwritten.
    char? letter = args.Length == 4 && args[3].Length == 1 ? char.ToUpperInvariant(args[3][0]) : null;
    if (args.Length == 4 && letter is not (>= 'A' and <= 'J'))
    {
        Console.Error.WriteLine(usage);
        return 2;
    }
    try
    {
        var files = PorEngineSave.ToOriginalFiles(PorEngineSave.Read(saveJson), letter);
        Directory.CreateDirectory(exportDir);
        foreach (var (name, _) in files)
            if (File.Exists(Path.Combine(exportDir, name)))
                throw new IOException($"{Path.Combine(exportDir, name)} exists; export into an empty folder");
        foreach (var (name, bytes) in files)
            File.WriteAllBytes(Path.Combine(exportDir, name), bytes);
        Console.WriteLine($"{files.Count} files written to {Path.GetFullPath(exportDir)}: {string.Join(", ", files.Select(f => f.Name))}");
        return 0;
    }
    catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"export failed: {e.Message}");
        return 1;
    }
}
if (args is ["save-import", var savgam, var moduleDir, var outJson])
{
    // Original SAVGAMx.DAT (+ the CHRDAT files next to it) → engine save for a module.
    try
    {
        var target = PorModule.Open(moduleDir);
        var save = PorEngineSave.FromFolders(File.ReadAllBytes(savgam), savgam, PorEngineSave.Identity(target.Modules),
            target.LoadBlock, Path.GetDirectoryName(Path.GetFullPath(savgam))!);
        if (File.Exists(outJson))
            throw new IOException($"{outJson} exists");
        PorEngineSave.Write(outJson, save);
        Console.WriteLine($"engine save written to {Path.GetFullPath(outJson)} ({save.Party?.Count ?? 0} members; " +
                          $"script {save.Script.Key.Id} {(save.Script.MatchesModule == false ? "differs from" : "matches")} the module's)");
        return 0;
    }
    catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"import failed: {e.Message}");
        return 1;
    }
}
if (args is ["schemas", var schemaDir])
{
    GoldBox.Data.Module.ModuleSchemas.WriteAll(schemaDir);
    Console.WriteLine($"schemas written to {Path.GetFullPath(schemaDir)}");
    return 0;
}
string? amigaDir = null;
bool verify = true, badArgs = args.Length < 3 || args[0] != "por";
for (int i = 3; i < args.Length && !badArgs; i++)
{
    if (args[i] == "--no-verify")
        verify = false;
    else if (args[i] == "--amiga" && i + 1 < args.Length)
        amigaDir = args[++i];
    else
        badArgs = true;
}
if (badArgs)
{
    Console.Error.WriteLine(usage);
    return 2;
}
string gameDir = args[1], outDir = args[2];

PorImportReport report;
try
{
    report = PorImporter.Import(gameDir, outDir, amigaDir);
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
Console.WriteLine($"  art: {report.Pictures} pictures, {report.ArtData} data blocks; program texts {report.Texts}; rules tables {report.RulesTables}; saves {report.Saves} files read, {report.EngineSaves} engine saves" +
                  (report.Overland ? "; overland" : ""));
if (report.Amiga is { } amiga)
    Console.WriteLine($"  Amiga overlay: {amiga.Pictures} pictures{(amiga.Sound ? ", music and effects" : "")}{(amiga.RuneFont ? ", rune font" : "")}{(amiga.TextFont ? ", text font" : "")}{(amiga.Frame ? ", window frame" : "")}; placeholders left out: {string.Join(", ", amiga.Skipped)}" +
                      (amiga.NotPictures.Count > 0 ? $"; not pictures: {string.Join(", ", amiga.NotPictures)}" : ""));
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
var module = PorModule.Open(outDir);
var rt = PorRoundTrip.Check(new PorGameDirectory(gameDir), module);
if (amigaDir is not null)
    rt.Add(PorAmigaArt.Check(amigaDir, module));
Console.WriteLine($"round trip: {rt.Total} blocks compared ({string.Join(", ", rt.Checked.Select(kv => $"{kv.Key} {kv.Value}"))})");
foreach (var m in rt.Mismatches)
    Console.WriteLine($"  MISMATCH {m}");
Console.WriteLine(rt.Mismatches.Count == 0 ? "round trip: all identical" : $"round trip: {rt.Mismatches.Count} mismatches");
return rt.Mismatches.Count == 0 ? 0 : 1;
