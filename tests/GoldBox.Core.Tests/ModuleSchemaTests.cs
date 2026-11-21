using System.Text.Json;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;

namespace GoldBox.Core.Tests;

/// <summary>The module JSON schemas (docs/module-format.md §17.5) match the document types and the importer's output.</summary>
public class ModuleSchemaTests
{
    private static string SchemaFolder()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "GoldBox.Data", "Module", "Schemas");
            if (Directory.Exists(p))
                return p;
        }
        throw new DirectoryNotFoundException("src/GoldBox.Data/Module/Schemas not found above the test folder");
    }

    [Fact]
    public void The_schema_files_match_the_document_types()
    {
        var folder = SchemaFolder();
        foreach (var (file, type) in ModuleSchemas.Documents)
            Assert.True(ModuleSchemas.Generate(type) == File.ReadAllText(Path.Combine(folder, file)),
                $"{file} is out of date: run goldbox-import schemas {folder}");
        Assert.Equal(ModuleSchemas.Documents.Select(d => d.File).Order(),
            Directory.EnumerateFiles(folder, "*.schema.json").Select(Path.GetFileName).Order());
    }

    /// <summary>The schema type a module file follows, by its path.</summary>
    private static Type? TypeOf(string rel) => rel switch
    {
        ModuleManifest.FileName => typeof(ModuleManifest),
        ProgramRulesDocument.RelativePath => typeof(ProgramRulesDocument),
        TextDocument.RelativePath => typeof(TextDocument),
        OverlandDocument.RelativePath => typeof(OverlandDocument),
        PcSoundDocument.RelativePath => typeof(PcSoundDocument),
        AmigaSoundDocument.RelativePath => typeof(AmigaSoundDocument),
        AmigaIconColoursDocument.RelativePath => typeof(AmigaIconColoursDocument),
        AmigaTextPensDocument.RelativePath => typeof(AmigaTextPensDocument),
        ItemTypesDocument.RelativePath => typeof(ItemTypesDocument),
        _ when GoldBox.Data.Module.Por.PorRulesTables.Files.Any(f => f.Path == rel) =>
            GoldBox.Data.Module.Por.PorRulesTables.Files.First(f => f.Path == rel).Type,
        _ when rel.StartsWith("areas/", StringComparison.Ordinal) => typeof(AreaDocument),
        _ when rel.StartsWith("maps/", StringComparison.Ordinal) => typeof(MapDocument),
        _ when rel.StartsWith("scripts/", StringComparison.Ordinal) => typeof(ScriptSidecar),
        _ when rel.StartsWith("records/monsters/", StringComparison.Ordinal) => typeof(MonsterDocument),
        _ when rel.StartsWith("records/", StringComparison.Ordinal) => typeof(RecordListDocument),
        _ when rel.StartsWith("art/", StringComparison.Ordinal) => typeof(ArtDocument),
        _ when rel.StartsWith("saves/", StringComparison.Ordinal) => typeof(EngineSaveDocument),
        _ => null,
    };

    [PorFact]
    public void Every_imported_json_file_reads_strictly_into_its_document_type()
    {
        var root = PorModuleData.Dir;
        int n = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var type = TypeOf(rel);
            Assert.True(type is not null, $"{rel}: no document type");
            Assert.NotNull(JsonSerializer.Deserialize(File.ReadAllText(path), type!, ModuleSchemas.Strict));
            n++;
        }
        Assert.True(n > 1000, $"{n} JSON files");
    }
}
