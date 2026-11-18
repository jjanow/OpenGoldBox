using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace GoldBox.Data.Module;

/// <summary>
/// JSON schemas of the module documents (docs/module-format.md §17.5), generated from the document types with the
/// options the importer writes with. They are kept in <c>src/GoldBox.Data/Module/Schemas/</c>; a test checks that the
/// files match the types and that the importer's output reads strictly (no unknown members) into them.
/// <c>goldbox-import schemas DIR</c> writes them again after a change.
/// </summary>
public static class ModuleSchemas
{
    /// <summary>Schema file name → document type.</summary>
    public static readonly IReadOnlyList<(string File, Type Type)> Documents =
    [
        ("module.schema.json", typeof(ModuleManifest)),
        ("area.schema.json", typeof(AreaDocument)),
        ("map.schema.json", typeof(MapDocument)),
        ("script-sidecar.schema.json", typeof(ScriptSidecar)),
        ("monster.schema.json", typeof(MonsterDocument)),
        ("record-list.schema.json", typeof(RecordListDocument)),
        ("item-types.schema.json", typeof(ItemTypesDocument)),
        ("art.schema.json", typeof(ArtDocument)),
        ("program-rules.schema.json", typeof(ProgramRulesDocument)),
        ("text.schema.json", typeof(TextDocument)),
        ("save.schema.json", typeof(EngineSaveDocument)),
        ("rules-spells.schema.json", typeof(SpellsDocument)),
        ("rules-experience.schema.json", typeof(ExperienceDocument)),
        ("rules-level-stats.schema.json", typeof(LevelStatsDocument)),
        ("rules-turn-undead.schema.json", typeof(TurnUndeadDocument)),
        ("rules-ai-steps.schema.json", typeof(AiStepsDocument)),
        ("rules-random-items.schema.json", typeof(RandomItemsDocument)),
        ("rules-temple.schema.json", typeof(TempleDocument)),
        ("rules-combat-map.schema.json", typeof(CombatMapDocument)),
        ("overland.schema.json", typeof(OverlandDocument)),
    ];

    /// <summary>The options of <see cref="ModuleJson"/> that reject members the types do not have.</summary>
    public static JsonSerializerOptions Strict { get; } = new(ModuleJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Generate(Type type)
    {
        var options = new JsonSerializerOptions(ModuleJson.Options) { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        var node = options.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        if (node is JsonObject o)
        {
            var schema = new JsonObject { ["$schema"] = "https://json-schema.org/draft/2020-12/schema", ["title"] = type.Name };
            foreach (var (k, v) in o.ToList())
            {
                o.Remove(k);
                schema[k] = v;
            }
            node = schema;
        }
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static void WriteAll(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (file, type) in Documents)
            File.WriteAllText(Path.Combine(folder, file), Generate(type));
    }
}
