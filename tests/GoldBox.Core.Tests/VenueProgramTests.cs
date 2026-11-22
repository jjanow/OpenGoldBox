using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The member selection 0x23819, the temple's cure 1 (0x4864) and the shop's Id (0x22F8C) against the program itself:
/// the private fixture <c>program-v1.3/venue.json</c> (made by <c>tools/scripts/por_venue_oracle.py</c>) holds generated
/// parties before and after the v1.3 routine ran under emulation, its questions answered by the script.
/// </summary>
public class VenueProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "venue.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/venue.json";
        }
    }

    public sealed class OracleDataFactAttribute : FactAttribute
    {
        public OracleDataFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/venue.json";
        }
    }

    private static JsonDocument Doc() => JsonDocument.Parse(File.ReadAllText(File_!));

    /// <summary>Record bytes the program keeps pointers in (effects, items, list link, combat record).</summary>
    private static bool PointerByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0xCC or >= 0x104 and < 0x10C;

    private static void SameRecord(List<string> fails, string what, EclCharacter c, string wantHex)
    {
        var want = Convert.FromHexString(wantHex);
        for (int b = 0; b < want.Length; b++)
            if (!PointerByte(b) && want[b] != c[b])
                fails.Add($"{what} +{b:X2}: {c[b]} vs {want[b]}");
    }

    [OracleFact]
    public void Member_selection_matches_0x23819_under_emulation()
    {
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("members").EnumerateArray())
        {
            int size = k.GetProperty("size").GetInt32(), cur = k.GetProperty("current").GetInt32();
            int want = k.GetProperty("after").GetInt32();
            byte key = (byte)k.GetProperty("key").GetInt32();
            n++;
            if (cur == -2)
            {
                // A current record outside the list: Home walks to nil; End takes its link (nil here: the head);
                // other keys the head. The engine's index cannot hold such a record, so only the reading is checked.
                int expect = key == PartySelection.Home ? -1 : size == 0 ? -1 : 0;
                if (want != expect)
                    fails.Add($"case {n}: outside record, key {key:X2}: {want} vs reading {expect}");
                continue;
            }
            var party = new EclParty { CurrentIndex = cur };
            for (int i = 0; i < size; i++)
                party.Members.Add(new EclCharacter(new byte[EclCharacter.RecordSize]));
            PartySelection.Select(party, key);
            if (party.CurrentIndex != want)
                fails.Add($"case {n}: size {size}, current {cur}, key {key:X2}: {party.CurrentIndex} vs {want}");
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    [OracleDataFact]
    public void Temple_cure_1_matches_0x4864_under_emulation()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var names = PorSpellData.Names(program);
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("temple").EnumerateArray())
        {
            n++;
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("record").GetString()!));
            foreach (var e in k.GetProperty("effects").EnumerateArray())
            {
                var r = new byte[EclCharacter.EffectRecordSize];
                Convert.FromHexString(e.GetString()!).CopyTo(r, 0);
                c.Effects.Add(r);
            }
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var fx = new EffectHandlers(rng.Next, names)
            {
                List = () => [c],
                CurrentCharacter = () => c,
                LevelStats = _ => { },
                RaceLevelLimit = (_, _) => false,
                CanRevive = _ => true,
                Recalculate = _ => { },
            };
            var answers = k.GetProperty("answers").EnumerateArray().Select(a => a.GetInt32()).ToList();
            // 0x4864: the condition (any of DS:0114) or "cure anyway" (0x458A), then the pay question (0x4636).
            bool applies = TempleRules.Applies(TempleRules.Cure.Disease, c);
            int asked = applies ? 0 : 1, a = 0;
            bool go = applies || answers[a++] == 'Y';
            int paid = go ? 1 : 0;
            if (go && answers[a] == 'Y')
                TempleRules.Apply(TempleRules.Cure.Disease, c, x => rng.Next((ushort)x), fx);
            string what = $"case {n}";
            if (asked != k.GetProperty("asked").GetInt32() || paid != k.GetProperty("paid").GetInt32())
                fails.Add($"{what}: asked {asked} paid {paid} vs {k.GetProperty("asked")} {k.GetProperty("paid")}");
            if (fx.Cells.Lock != k.GetProperty("lock").GetInt32())
                fails.Add($"{what}: lock {fx.Cells.Lock}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed differs");
            var after = k.GetProperty("after");
            SameRecord(fails, what, c, after.GetProperty("record").GetString()!);
            string Effects(IEnumerable<byte[]> x) => string.Join(" ", x.Select(e => Convert.ToHexString(e, 0, 5)));
            string wantEffects = string.Join(" ", after.GetProperty("effects").EnumerateArray().Select(e => e.GetString()!.ToUpperInvariant()));
            if (Effects(c.Effects) != wantEffects)
                fails.Add($"{what} effects: {Effects(c.Effects)} vs {wantEffects}");
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    [OracleFact]
    public void Shop_identify_matches_0x22F8C_under_emulation()
    {
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("identify").EnumerateArray())
        {
            n++;
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("record").GetString()!));
            var item = Convert.FromHexString(k.GetProperty("item").GetString()!);
            c.Items.Add(item);
            var pool = k.GetProperty("pool").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            bool yes = k.GetProperty("answer").GetInt32() == 'Y';
            var counts = k.GetProperty("counts");
            int Count(string name) => counts.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            string what = $"case {n}";
            // The offer (name, text) and the yes/no always; after a yes the payment, then one of the three outcomes.
            int texts = 1, rows = 0, pauses = 0, redraw = 0;
            if (yes)
            {
                switch (ShopRules.Identify(c, pool, item))
                {
                    case ShopRules.IdentifyResult.NotEnoughMoney:
                        rows = 1;
                        break;
                    case ShopRules.IdentifyResult.AlreadyIdentified:
                        texts = 2;
                        pauses = 1;
                        break;
                    default:
                        texts = 2;
                        pauses = 1;
                        redraw = 1; // the item list is rebuilt (0x2312E: the caller's [bp−0x2D])
                        break;
                }
            }
            if (Count("text") != texts || Count("rowMessage") != rows || Count("pause") != pauses ||
                Count("yesNo") != 1 || Count("clearBox") != 1)
                fails.Add($"{what}: texts {texts} rows {rows} pauses {pauses} vs {counts}");
            if (k.GetProperty("redraw").GetInt32() != redraw)
                fails.Add($"{what}: redraw {redraw} vs {k.GetProperty("redraw")}");
            SameRecord(fails, what, c, k.GetProperty("after").GetString()!);
            var wantItem = Convert.FromHexString(k.GetProperty("itemAfter").GetString()!);
            for (int b = 0; b < wantItem.Length; b++)
                if (b is not (>= 0x2A and < 0x2E) && wantItem[b] != item[b])
                    fails.Add($"{what} item +{b:X2}: {item[b]} vs {wantItem[b]}");
            var wantPool = k.GetProperty("poolAfter").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            if (!wantPool.SequenceEqual(pool))
                fails.Add($"{what}: pool {string.Join(",", pool)} vs {string.Join(",", wantPool)}");
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }
}
