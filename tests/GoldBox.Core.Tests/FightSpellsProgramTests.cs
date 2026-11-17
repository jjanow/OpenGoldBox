using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// Spell routines and effect handlers against the program itself: the private fixture <c>program-v1.3/spells.json</c>
/// (made by <c>tools/scripts/por_spell_oracle.py</c>) holds generated creatures before and after the v1.3 routine ran
/// under emulation outside a fight, with RandSeed before and after. <see cref="FightSpells.Routine"/> and
/// <see cref="EffectHandlers.CallHandler"/> must leave the same record bytes, effect lists and items, with the same
/// draws. The oracle skips the level stats (0x2AA87), the stat recalculation (START 0x1758) and the race limit
/// (START 0x3AF7, no limit), so the seams here do nothing.
/// </summary>
public class FightSpellsProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "spells.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/spells.json";
        }
    }

    /// <summary>Record bytes the program keeps pointers in (effects, items, list link, combat record).</summary>
    private static bool PointerByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0xCC or >= 0x104 and < 0x10C;

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

    [OracleFact]
    public void Routines_and_handlers_match_the_program_under_emulation()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var table = PorSpellData.Spells(program)!;
        var names = PorSpellData.Names(program);
        var xp = PorSpellData.ExperienceTable(program);
        var tables = PorCombatTables.Load(program);
        Assert.NotNull(xp);
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string kind = k.GetProperty("kind").GetString()!;
            int id = k.GetProperty("id").GetInt32();
            var list = k.GetProperty("creatures").EnumerateArray().Select(Load).ToList();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
            memory.Party.Members.AddRange(list);
            var fx = new EffectHandlers(rng.Next, names)
            {
                ExperienceTable = xp,
                LevelStats = _ => { },
                RaceLevelLimit = (_, _) => false,
                CanRevive = _ => true,
                Recalculate = _ => { },
            };
            CombatMap? map = null;
            if (k.TryGetProperty("cells", out var cells))
            {
                map = new CombatMap(tables);
                Convert.FromHexString(cells.GetString()!).CopyTo(map.Arena.Bytes, CombatArena.HeaderSize);
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
                memory.Write(FightSpells.FireballRangeCell, (ushort)(k.TryGetProperty("cell49e6", out var r) ? r.GetInt32() : 0));
            }
            var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(),
                map is null ? new GridGeometry() : map, fx);
            var spells = new FightSpells(table, fx, rng.Next);
            fight.Spells = spells;
            var current = list[k.GetProperty("current").GetInt32()];
            spells.Range(fight, current, 1); // binds the engine to the fight
            fx.InCombat = false;             // the oracle runs outside a fight (DS:49F3 = 0)
            fight.State.Current = current;
            string what = $"case {n} {kind} {id}";
            if (k.TryGetProperty("aim", out var aim))
                spells.Aim = (aim[0].GetInt32(), aim[1].GetInt32());
            if (kind == "cloud")
            {
                foreach (var cast in k.GetProperty("casts").EnumerateArray())
                {
                    var who = list[cast[0].GetInt32()];
                    fight.State.Current = who;
                    spells.Targets.Clear();
                    spells.Aim = (cast[1][0].GetInt32(), cast[1][1].GetInt32());
                    fx.Cells.Spell = 34;
                    spells.Routine(34, who);
                    fx.Cells.Spell = 0;
                }
                if (k.TryGetProperty("removeFrom", out var rf))
                    fx.RemoveEffect(list[rf.GetInt32()], 0x28);
            }
            else if (kind == "spell")
            {
                foreach (var t in k.GetProperty("targets").EnumerateArray())
                    spells.Targets.Add(list[t.GetInt32()]);
                fx.Cells.Spell = (byte)id;
                spells.Routine(id, current);
                fx.Cells.Spell = 0;
            }
            else
            {
                var c = list[k.GetProperty("creature").GetInt32()];
                if (k.TryGetProperty("other", out var o))
                    fight.Record(c).Other = list[o.GetInt32()];
                fx.CallHandler((byte)id, c, EffectHandlers.FindEffect(c, (byte)id), false);
            }
            var res = k.GetProperty("result");
            if (rng.Seed != res.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed {TurboPascalRandom.StepsBetween(k.GetProperty("seed").GetUInt32(), res.GetProperty("seedAfter").GetUInt32())} draws in the program, {rng.Draws} here");
            if (map is not null && Convert.ToHexString(map.Arena.Bytes, CombatArena.HeaderSize, 1250) !=
                res.GetProperty("cells").GetString()!.ToUpperInvariant())
                fails.Add($"{what}: arena cells differ");
            if (res.TryGetProperty("aim", out var aimAfter) && kind == "spell" &&
                spells.Aim != (aimAfter[0].GetInt32(), aimAfter[1].GetInt32()))
                fails.Add($"{what}: aim {spells.Aim} vs ({aimAfter[0]}, {aimAfter[1]})");
            int ci = 0;
            foreach (var a in res.GetProperty("after").EnumerateArray())
            {
                var c = list[ci];
                var want = Load(a);
                for (int b = 0; b < EclCharacter.RecordSize; b++)
                    if (!PointerByte(b) && want[b] != c[b] &&
                        !(kind == "spell" && id == 56 && b == 0x96 - 0x12)) // the uninitialised class index is
                                                                                      // the emulator's stack filler 0xEE
                        fails.Add($"{what} creature {ci} +{b:X2}: {c[b]} vs {want[b]}");
                string Effects(EclCharacter x) => string.Join(" ", x.Effects.Select(e => Convert.ToHexString(e, 0, 5)));
                if (Effects(want) != Effects(c))
                    fails.Add($"{what} creature {ci} effects: {Effects(c)} vs {Effects(want)}");
                string Items(EclCharacter x) => string.Join(" ", x.Items.Select(i =>
                    Convert.ToHexString(i, 0, 0x2A) + Convert.ToHexString(i, 0x2E, i.Length - 0x2E)));
                if (Items(want) != Items(c))
                    fails.Add($"{what} creature {ci} items differ");
                ci++;
            }
            n++;
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }
}
