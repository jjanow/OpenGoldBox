using System.IO.Compression;
using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The effect handlers of the unit at GAME.OVR 0xEC5B (and its helpers 0xEC5B, 0xEC7A, 0xECB3, 0xFC07, 0xFC78, 0x11570)
/// against the program itself: the private fixture <c>program-v1.3/handler_calls.json.gz</c>, made by
/// <c>tools/scripts/por_handler_oracle.py</c> ('call' cases, s52; docs/re/spells-effects.md "Every handler under
/// emulation"). Handlers 40, 84, 88, 90, 97 are <see cref="HandlerProgramTests"/>', 81–86 FightSpellsProgramTests'.
/// </summary>
public class HandlerCallProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "handler_calls.json.gz") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/handler_calls.json.gz";
        }
    }

    /// <summary>Record bytes the program keeps pointers in: effects, items, list link, combat record, equipment slots.</summary>
    private static bool PointerOrSlotByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0x100 or >= 0x104 and < 0x10C;

    private static EclCharacter Load(JsonElement c)
    {
        var x = new EclCharacter(Convert.FromHexString(c.GetProperty("record").GetString()!));
        foreach (var e in c.GetProperty("effects").EnumerateArray())
        {
            var r = new byte[EclCharacter.EffectRecordSize];
            Convert.FromHexString(e.GetString()!).CopyTo(r, 0);
            x.Effects.Add(r);
        }
        foreach (var it in c.GetProperty("items").EnumerateArray())
            x.Items.Add(Convert.FromHexString(it.GetString()!));
        return x;
    }

    /// <summary>
    /// Every handler of the unit at 0xEC5B the other oracles do not run, called through 0x2AEEA
    /// with the DS cells (6816–682A, round counter, team counts, item use), the combat records and their +0x0A target,
    /// the current character and the record (the creature's first, an aura carrier's or an item) set per case. Records,
    /// effect lists, items, combat records, the cells and RandSeed afterwards must equal the program's.
    /// </summary>
    [OracleFact]
    public void Every_handler_called_through_0x2AEEA_matches_the_program()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var table = PorSpellData.Spells(program)!;
        var names = PorSpellData.Names(program);
        var tables = PorCombatTables.Load(program);
        using var gz = new GZipStream(File.OpenRead(File_!), CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        var items = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        var ids = new Dictionary<int, int>();
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            int id = k.GetProperty("id").GetInt32();
            int n = ids[id] = ids.GetValueOrDefault(id) + 1;
            string what = $"handler {id} case {n}";
            var list = k.GetProperty("creatures").EnumerateArray().Select(Load).ToList();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
            memory.Party.Members.AddRange(list);
            var fx = new EffectHandlers(rng.Next, names)
            {
                LevelStats = _ => { },
                RaceLevelLimit = (_, _) => false,
                Recalculate = _ => { },
            };
            CombatMap? map = null;
            if (k.TryGetProperty("cells", out var cells))
            {
                map = new CombatMap(tables);
                Array.Fill(map.Arena.Bytes, (byte)23, CombatArena.HeaderSize, 1250); // "floor": every cell 23
                int slot = 0;
                foreach (var pl in k.GetProperty("places").EnumerateArray())
                {
                    slot++;
                    map.SetSlotCreature(slot, list[slot - 1]);
                    map.SetSlotPosition(slot, pl[0].GetInt32(), pl[1].GetInt32());
                    map.SetSlotSize(slot, pl[2].GetInt32());
                }
                map.SlotCount = slot + 1;
                map.Rebuild();
            }
            else
                fx.CanRevive = _ => true; // the oracle answers 0x317E1 "free" without a map
            var fight = new Fight(memory, new EncounterState(), items, rng.Next, new EndActionsController(),
                map is null ? new GridGeometry() : map, fx);
            var spells = new FightSpells(table, fx, rng.Next);
            fight.Spells = spells;
            var current = list[k.GetProperty("current").GetInt32()];
            spells.Range(fight, current, 1); // binds the engine to the fight
            var s = fight.State;
            s.Current = current;
            var others = k.GetProperty("others").EnumerateArray().ToList();
            var combat = k.GetProperty("combat").EnumerateArray().ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var rec = fight.Record(list[i]);
                Convert.FromHexString(combat[i].GetString()!).CopyTo(rec.Bytes, 0);
                rec.Other = others[i].ValueKind == JsonValueKind.Null ? null : list[others[i].GetInt32()];
            }
            var ci = k.GetProperty("cellsIn");
            byte Cell(string a) => (byte)ci.GetProperty(a).GetInt32();
            fx.Cells.SaveRoll = Cell("6816");
            fx.Cells.PendingEffect = Cell("6817");
            s.PendingDamage = Cell("6818");
            s.DamageFlags = Cell("6819");
            s.RateX2 = Cell("681A");
            fx.Cells.Spell = Cell("681B");
            s.DamageDice = Cell("681C");
            s.CalculatingMovement = Cell("681D") != 0;
            s.TargetBlocked = Cell("681E");
            fx.Cells.Lock = Cell("681F");
            fx.Cells.MultiTarget = Cell("6820");
            fx.ItemFlag = Cell("6821");
            s.AttackRoll = (sbyte)Cell("6822");
            s.HitsMade[1] = Cell("6823");
            s.Morale = Cell("6825");
            fx.Cells.SaveType = Cell("682A");
            s.RoundCounter = Cell("6DE3");
            s.TeamCount[0] = Cell("6814");
            s.TeamCount[1] = Cell("6815");
            spells.UsingItem = Cell("6DBF") != 0;
            var o6826 = k.GetProperty("ds6826");
            fx.Cells.Other = o6826.ValueKind == JsonValueKind.Null ? null : list[o6826.GetInt32()];
            fx.InCombat = k.TryGetProperty("inFight", out var inf) && inf.GetBoolean();
            bool nil = k.TryGetProperty("ivt3", out var ivt3); // the cloaks: handler 89 on a nil record (0000:0003)
            if (nil)
                fx.NilRecord[3] = (byte)ivt3.GetInt32();
            var how = k.GetProperty("record");
            byte[]? node = how.ValueKind == JsonValueKind.String ? null
                : how[0].GetString() == "item" ? list[0].Items[how[1].GetInt32()]
                : EffectHandlers.FindEffect(list[how[1].GetInt32()], (byte)id);
            fx.CallHandler((byte)id, list[k.GetProperty("creature").GetInt32()], node, k.GetProperty("removed").GetInt32() != 0);

            var res = k.GetProperty("result");
            if (rng.Seed != res.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed {TurboPascalRandom.StepsBetween(k.GetProperty("seed").GetUInt32(), res.GetProperty("seedAfter").GetUInt32())} draws in the program, {rng.Draws} here");
            var co = res.GetProperty("cellsOut");
            void Check(string a, int got)
            {
                int want = co.GetProperty(a).GetInt32();
                if ((byte)got != want)
                    fails.Add($"{what}: DS:{a} {(byte)got:X2} vs {want:X2}");
            }
            Check("6816", fx.Cells.SaveRoll);
            Check("6817", fx.Cells.PendingEffect);
            Check("6818", s.PendingDamage);
            Check("6819", s.DamageFlags);
            Check("681A", s.RateX2);
            Check("681B", fx.Cells.Spell);
            Check("681C", s.DamageDice);
            Check("681D", s.CalculatingMovement ? 1 : 0);
            Check("681E", s.TargetBlocked);
            Check("681F", fx.Cells.Lock);
            Check("6820", fx.Cells.MultiTarget);
            Check("6821", fx.ItemFlag);
            Check("6822", s.AttackRoll);
            Check("6823", s.HitsMade[1]);
            Check("6825", s.Morale);
            Check("682A", fx.Cells.SaveType);
            int Index(EclCharacter? c) => c is null ? -1 : list.FindIndex(x => ReferenceEquals(x, c)) is var i and >= 0 ? i : -2;
            if (nil && fx.NilRecord[3] != res.GetProperty("ivt3").GetInt32())
                fails.Add($"{what}: 0000:0003 {fx.NilRecord[3]:X2} vs {res.GetProperty("ivt3").GetInt32():X2}");
            if (Index(fx.Cells.Other) != res.GetProperty("ds6826").GetInt32())
                fails.Add($"{what}: DS:6826 creature {Index(fx.Cells.Other)} vs {res.GetProperty("ds6826").GetInt32()}");
            var cOut = res.GetProperty("combat").EnumerateArray().ToList();
            var oOut = res.GetProperty("othersOut").EnumerateArray().ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var rec = fight.Record(list[i]);
                var want = Convert.FromHexString(cOut[i].GetString()!);
                for (int b = 0; b < CombatRecord.Size; b++)
                    if (b is < 0x0A or > 0x0D && rec[b] != want[b])
                        fails.Add($"{what} creature {i} combat +{b:X2}: {rec[b]:X2} vs {want[b]:X2}");
                if (Index(rec.Other) != oOut[i].GetInt32())
                    fails.Add($"{what} creature {i} combat +0A: creature {Index(rec.Other)} vs {oOut[i].GetInt32()}");
            }
            int cn = 0;
            foreach (var a in res.GetProperty("after").EnumerateArray())
            {
                var c = list[cn];
                var want = Load(a);
                for (int b = 0; b < EclCharacter.RecordSize; b++)
                    if (!PointerOrSlotByte(b) && want[b] != c[b])
                        fails.Add($"{what} creature {cn} +{b:X2}: {c[b]:X2} vs {want[b]:X2}");
                string Effects(EclCharacter x) => string.Join(" ", x.Effects.Select(e => Convert.ToHexString(e, 0, 5)));
                if (Effects(want) != Effects(c))
                    fails.Add($"{what} creature {cn} effects: {Effects(c)} vs {Effects(want)}");
                string Items(EclCharacter x) => string.Join(" ", x.Items.Select(e =>
                    Convert.ToHexString(e, 0, 0x2A) + Convert.ToHexString(e, 0x2E, e.Length - 0x2E)));
                if (Items(want) != Items(c))
                    fails.Add($"{what} creature {cn} items: {Items(c)} vs {Items(want)}");
                cn++;
            }
        }
        Assert.True(ids.Count >= 100 && ids.Values.All(v => v >= 20), $"{ids.Count} handlers");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(60)));
    }
}
