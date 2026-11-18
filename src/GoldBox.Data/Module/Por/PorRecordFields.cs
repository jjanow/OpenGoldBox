using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace GoldBox.Data.Module.Por;

/// <summary>How a field's bytes read.</summary>
public enum FieldType
{
    U8,
    S8,
    U16,
    U32,
    /// <summary>Stored as 60 − value in a byte (THAC0, AC; character-format.md "Conventions").</summary>
    Sixty,
    /// <summary>Pascal string: length byte + characters, field size = capacity + 1.</summary>
    PString,
    /// <summary>A byte array (field size = length).</summary>
    Bytes,
}

/// <summary>One named field of a fixed-size record. The name is a dotted JSON path.</summary>
public sealed record FieldSpec(string Name, int Offset, FieldType Type, int Size = 1);

/// <summary>
/// A table-driven decoder of a fixed-size record into a nested JSON object (dotted names become nested objects), and
/// the encoder back over a copy of the raw bytes. Fields not in the table (stale pointers, unknown bytes) are kept
/// only in the raw bytes. Encode(Decode(raw), raw) == raw for every PoR record (tested).
/// </summary>
public sealed class RecordFieldCodec(int recordSize, IReadOnlyList<FieldSpec> fields)
{
    public int RecordSize { get; } = recordSize;
    public IReadOnlyList<FieldSpec> Fields { get; } = fields;

    public JsonObject Decode(ReadOnlySpan<byte> r)
    {
        if (r.Length != RecordSize)
            throw new ArgumentException($"record is {r.Length} bytes, expected {RecordSize}");
        var root = new JsonObject();
        foreach (var f in Fields)
        {
            var span = r.Slice(f.Offset, Size(f));
            JsonNode node = f.Type switch
            {
                FieldType.U8 => JsonValue.Create((int)span[0]),
                FieldType.S8 => JsonValue.Create((int)(sbyte)span[0]),
                FieldType.U16 => JsonValue.Create((int)BinaryPrimitives.ReadUInt16LittleEndian(span)),
                FieldType.U32 => JsonValue.Create((long)BinaryPrimitives.ReadUInt32LittleEndian(span)),
                FieldType.Sixty => JsonValue.Create(60 - span[0]),
                FieldType.PString => JsonValue.Create(Latin1(span[1..(1 + Math.Min(span[0], span.Length - 1))])),
                FieldType.Bytes => new JsonArray([.. span.ToArray().Select(b => (JsonNode)JsonValue.Create((int)b))]),
                _ => throw new InvalidOperationException(),
            };
            Put(root, f.Name, node);
        }
        return root;
    }

    /// <summary>Writes the fields present in <paramref name="fields"/> over a copy of <paramref name="raw"/>.</summary>
    public byte[] Encode(JsonObject fields, ReadOnlySpan<byte> raw)
    {
        var r = raw.ToArray();
        foreach (var f in Fields)
        {
            if (Get(fields, f.Name) is not { } node)
                continue;
            var span = r.AsSpan(f.Offset, Size(f));
            switch (f.Type)
            {
                case FieldType.U8:
                case FieldType.S8:
                    span[0] = (byte)node.GetValue<int>();
                    break;
                case FieldType.U16:
                    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)node.GetValue<int>());
                    break;
                case FieldType.U32:
                    BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)node.GetValue<long>());
                    break;
                case FieldType.Sixty:
                    span[0] = (byte)(60 - node.GetValue<int>());
                    break;
                case FieldType.PString:
                {
                    var s = Encoding.Latin1.GetBytes(node.GetValue<string>());
                    int n = Math.Min(s.Length, span.Length - 1);
                    // Only rewrite when the text changed: the padding after the text is kept from raw.
                    if (span[0] != n || !span.Slice(1, n).SequenceEqual(s.AsSpan(0, n)))
                    {
                        span.Clear();
                        span[0] = (byte)n;
                        s.AsSpan(0, n).CopyTo(span[1..]);
                    }
                    break;
                }
                case FieldType.Bytes:
                {
                    var a = node.AsArray();
                    for (int i = 0; i < span.Length && i < a.Count; i++)
                        span[i] = (byte)a[i]!.GetValue<int>();
                    break;
                }
            }
        }
        return r;
    }

    private static int Size(FieldSpec f) => f.Type switch
    {
        FieldType.U16 => 2,
        FieldType.U32 => 4,
        FieldType.PString or FieldType.Bytes => f.Size,
        _ => 1,
    };

    private static string Latin1(ReadOnlySpan<byte> b) => Encoding.Latin1.GetString(b);

    private static void Put(JsonObject root, string path, JsonNode value)
    {
        var parts = path.Split('.');
        var o = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (o[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                o[parts[i]] = child;
            }
            o = child;
        }
        o[parts[^1]] = value;
    }

    private static JsonNode? Get(JsonObject root, string path)
    {
        JsonNode? n = root;
        foreach (var p in path.Split('.'))
        {
            if (n is not JsonObject o || !o.TryGetPropertyValue(p, out n))
                return null;
        }
        return n;
    }
}

/// <summary>The PoR record layouts as field tables (docs/re/character-format.md, docs/re/item-format.md).</summary>
public static class PorRecordFields
{
    private static FieldSpec U8(string n, int o) => new(n, o, FieldType.U8);
    private static FieldSpec S8(string n, int o) => new(n, o, FieldType.S8);

    /// <summary>
    /// The 285-byte creature record (character-format.md "Layout"), keys as <c>gbchar.py</c> writes them. Left out
    /// (raw only): the stale far pointers 0x7F, 0xC8, 0xCC–0xFF, 0x104, 0x108 and the reserved 0x86–0x87.
    /// </summary>
    public static RecordFieldCodec Creature { get; } = new(0x11D,
    [
        new("name", 0x00, FieldType.PString, 16),
        U8("abilities.str", 0x10), U8("abilities.int", 0x11), U8("abilities.wis", 0x12), U8("abilities.dex", 0x13),
        U8("abilities.con", 0x14), U8("abilities.cha", 0x15), U8("abilities.str_exc", 0x16),
        new("spells.memorized", 0x17, FieldType.Bytes, 21),
        U8("spells.camp_hours_code", 0x2C),
        new("thac0_base", 0x2D, FieldType.Sixty),
        U8("race", 0x2E), U8("class", 0x2F),
        new("age", 0x30, FieldType.U16),
        U8("hp.max", 0x32),
        new("spells.known", 0x33, FieldType.Bytes, 56),
        U8("derived.sweep_level", 0x6B),
        U8("size", 0x6C),
        U8("saves.paralyzation_poison_death", 0x6D), U8("saves.petrification_polymorph", 0x6E),
        U8("saves.rod_staff_wand", 0x6F), U8("saves.breath_weapon", 0x70), U8("saves.spell", 0x71),
        U8("movement_base", 0x72),
        U8("level_highest", 0x73),
        U8("drain.levels", 0x74), U8("drain.hp", 0x75),
        U8("monster.undead_turn_index", 0x76),
        U8("thief_skills.pick_pockets", 0x77), U8("thief_skills.open_locks", 0x78),
        U8("thief_skills.find_remove_traps", 0x79), U8("thief_skills.move_silently", 0x7A),
        U8("thief_skills.hide_in_shadows", 0x7B), U8("thief_skills.hear_noise", 0x7C),
        U8("thief_skills.climb_walls", 0x7D), U8("thief_skills.read_languages", 0x7E),
        U8("reserved.0x83", 0x83),
        U8("npc.raw", 0x84),
        U8("npc.share_raw", 0x85),
        new("money.copper", 0x88, FieldType.U16), new("money.silver", 0x8A, FieldType.U16),
        new("money.electrum", 0x8C, FieldType.U16), new("money.gold", 0x8E, FieldType.U16),
        new("money.platinum", 0x90, FieldType.U16), new("money.gems", 0x92, FieldType.U16),
        new("money.jewelry", 0x94, FieldType.U16),
        S8("levels.cleric", 0x96), S8("levels.druid", 0x97), S8("levels.fighter", 0x98), S8("levels.paladin", 0x99),
        S8("levels.ranger", 0x9A), S8("levels.magic-user", 0x9B), S8("levels.thief", 0x9C), S8("levels.monk", 0x9D),
        U8("gender", 0x9E),
        U8("monster.creature_type", 0x9F),
        U8("alignment", 0xA0),
        U8("attacks.primary.attacks_x2", 0xA1), U8("attacks.secondary.attacks_x2", 0xA2),
        U8("attacks.primary.dice", 0xA3), U8("attacks.secondary.dice", 0xA4),
        U8("attacks.primary.sides", 0xA5), U8("attacks.secondary.sides", 0xA6),
        S8("attacks.primary.bonus", 0xA7), S8("attacks.secondary.bonus", 0xA8),
        new("ac_base", 0xA9, FieldType.Sixty),
        U8("attacks.str_bonus_allowed", 0xAA),
        U8("byte_0xAB", 0xAB),
        new("xp", 0xAC, FieldType.U32),
        U8("item_usability_mask", 0xB0),
        U8("hp.rolled", 0xB1),
        new("spells.slots", 0xB2, FieldType.Bytes, 6),
        new("monster.xp_base", 0xB8, FieldType.U16),
        U8("monster.xp_per_rolled_hp", 0xBA),
        U8("portrait.head", 0xBB), U8("portrait.body", 0xBC),
        U8("icon.head", 0xBD), U8("icon.body", 0xBE), U8("icon.order", 0xBF), U8("icon.size", 0xC0),
        new("icon.colors", 0xC1, FieldType.Bytes, 6),
        U8("item_count", 0xC7),
        U8("derived.hands_used", 0x100), S8("derived.save_bonus", 0x101),
        new("derived.encumbrance", 0x102, FieldType.U16),
        U8("combat.status", 0x10C), U8("combat.in_play", 0x10D), U8("combat.hostile", 0x10E),
        U8("combat.autocombat", 0x10F),
        new("derived.thac0", 0x110, FieldType.Sixty), new("derived.ac", 0x111, FieldType.Sixty),
        new("derived.ac_rear", 0x112, FieldType.Sixty),
        U8("derived.attacks.primary.attacks_x2", 0x113), U8("derived.attacks.secondary.attacks_x2", 0x114),
        U8("derived.attacks.primary.dice", 0x115), U8("derived.attacks.secondary.dice", 0x116),
        U8("derived.attacks.primary.sides", 0x117), U8("derived.attacks.secondary.sides", 0x118),
        S8("derived.attacks.primary.bonus", 0x119), S8("derived.attacks.secondary.bonus", 0x11A),
        U8("hp.current", 0x11B),
        U8("derived.movement", 0x11C),
    ]);

    /// <summary>The 63-byte item record (item-format.md "Item record"); the stale <c>next</c> pointer is raw only.</summary>
    public static RecordFieldCodec Item { get; } = new(63,
    [
        new("name", 0x00, FieldType.PString, 41),
        U8("byte29", 0x29),
        U8("type", 0x2E),
        U8("words.2f", 0x2F), U8("words.30", 0x30), U8("words.31", 0x31),
        S8("plus", 0x32), S8("save_bonus", 0x33),
        U8("equipped", 0x34), U8("hidden_mask", 0x35), U8("cursed", 0x36),
        new("weight", 0x37, FieldType.U16),
        U8("count", 0x39),
        new("value", 0x3A, FieldType.U16),
        new("props", 0x3C, FieldType.Bytes, 3),
    ]);

    /// <summary>The 9-byte effect record (item-format.md "Effect record"); the stale <c>next</c> pointer is raw only.</summary>
    public static RecordFieldCodec Effect { get; } = new(9,
    [
        U8("id", 0),
        new("minutes", 1, FieldType.U16),
        U8("param", 3),
        U8("remove_calls_handler", 4),
    ]);

    /// <summary>One 16-byte entry of the base item table <c>ITEMS</c> (item-format.md "Base item table").</summary>
    public static RecordFieldCodec ItemType { get; } = new(16,
    [
        U8("slot", 0), U8("hands", 1),
        U8("damage_large.dice", 2), U8("damage_large.sides", 3), S8("damage_large.bonus", 4),
        U8("rate_of_fire_x2", 5), U8("ac_code", 6), U8("weapon_kind", 7), U8("byte8", 8),
        U8("damage_small_medium.dice", 9), U8("damage_small_medium.sides", 10), S8("damage_small_medium.bonus", 11),
        U8("range", 12), U8("usable_mask", 13), U8("weapon_flags", 14), U8("byte15", 15),
    ]);
}
