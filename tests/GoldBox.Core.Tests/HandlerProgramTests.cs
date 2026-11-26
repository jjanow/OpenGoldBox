using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// Effect handlers 40, 84, 88, 90 and 97 against the program itself: the private fixture <c>program-v1.3/handlers.json</c>
/// (made by <c>tools/scripts/por_handler_oracle.py</c>, outside a fight as <see cref="FightSpellsProgramTests"/>):
/// Stinking Cloud casts and the removal of the first cloud (handler 40) with party bodies on the map, the charm gaze
/// (handler 84) with its uninitialised range word set per case, the lightning breath (handler 88) with its target pick
/// scripted, and the saving throw 0x2BC4D on carriers of 90 / 97.
/// </summary>
public class HandlerProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "handlers.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/handlers.json";
        }
    }

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
    public void Handlers_40_84_88_90_97_match_the_program_under_emulation()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var table = PorSpellData.Spells(program)!;
        var names = PorSpellData.Names(program);
        var tables = PorCombatTables.Load(program);
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, residue = 0;
        var kinds = new Dictionary<string, int>();
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string kind = k.GetProperty("kind").GetString()!;
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            var list = k.GetProperty("creatures").EnumerateArray().Select(Load).ToList();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
            memory.Party.Members.AddRange(list);
            var fx = new EffectHandlers(rng.Next, names)
            {
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
                if (k.TryGetProperty("bodies", out var bodies))
                    foreach (var b in bodies.EnumerateArray())
                        map.Bodies.Add(b[0].ValueKind == JsonValueKind.Null ? null
                            : new CombatMap.Body(list[b[0].GetInt32()], b[1].GetInt32(), b[2].GetInt32(), (byte)b[3].GetInt32()));
            }
            var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(),
                map is null ? new GridGeometry() : map, fx);
            var spells = new FightSpells(table, fx, rng.Next);
            fight.Spells = spells;
            var current = list[k.GetProperty("current").GetInt32()];
            spells.Range(fight, current, 1); // binds the engine to the fight
            fx.InCombat = false;             // the oracle runs outside a fight (DS:49F3 = 0)
            fight.State.Current = current;
            fight.State.RoundCounter = (byte)(k.TryGetProperty("round", out var rd) ? rd.GetInt32() : 0);
            var res = k.GetProperty("result");
            string what = $"case {n} {kind}";
            switch (kind)
            {
                case "cloud":
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
                    break;
                case "breath":
                {
                    var pick = k.GetProperty("pick");
                    spells.TargetPick = (_, _, _) =>
                    {
                        spells.Aim = (pick[0].GetInt32(), pick[1].GetInt32());
                        return true;
                    };
                    var c = list[k.GetProperty("creature").GetInt32()];
                    fx.CallHandler(88, c, EffectHandlers.FindEffect(c, 88), false);
                    var aim = res.GetProperty("aim");
                    if (((sbyte)(byte)spells.Aim.X, (sbyte)(byte)spells.Aim.Y) != ((sbyte)aim[0].GetInt32(), (sbyte)aim[1].GetInt32()))
                        fails.Add($"{what}: aim {spells.Aim} vs ({aim[0]}, {aim[1]})");
                    break;
                }
                case "gaze":
                {
                    var c = list[k.GetProperty("creature").GetInt32()];
                    fight.Record(c).Other = list[k.GetProperty("other").GetInt32()];
                    fx.CharmGazeRange = k.GetProperty("range").GetInt32();
                    fx.CallHandler(84, c, EffectHandlers.FindEffect(c, 84), false);
                    if (fx.Cells.Spell != res.GetProperty("spellCell").GetInt32())
                        fails.Add($"{what}: DS:681B {fx.Cells.Spell} vs {res.GetProperty("spellCell")}");
                    break;
                }
                case "place":
                {
                    // s52: 0x317E1(creature, x, y, moving) in a fight (CombatMap.TryPlace) or outside one (true)
                    var c = list[k.GetProperty("creature").GetInt32()];
                    fx.InCombat = k.GetProperty("fight").GetBoolean();
                    fight.Record(c)[CombatRecord.NotParty] = (byte)k.GetProperty("notParty").GetInt32();
                    var to = k.GetProperty("to");
                    bool moving = k.GetProperty("moving").GetInt32() != 0;
                    bool put = fx.InCombat ? map!.TryPlace(c, to[0].GetInt32(), to[1].GetInt32(), moving, fight.State) : true;
                    if (put != (res.GetProperty("put").GetInt32() != 0))
                        fails.Add($"{what}: put {put} vs {res.GetProperty("put")}");
                    int sl = 0;
                    foreach (var w in res.GetProperty("slots").EnumerateArray())
                    {
                        sl++;
                        var (sx, sy) = map!.SlotPosition(sl);
                        if (((byte)sx, (byte)sy, map.SlotSize(sl)) != ((byte)w[0].GetInt32(), (byte)w[1].GetInt32(), w[3].GetInt32()))
                            fails.Add($"{what}: slot {sl} ({sx}, {sy}, {map.SlotSize(sl)}) vs ({w[0]}, {w[1]}, {w[3]})");
                    }
                    var wantBodies = res.GetProperty("bodies").EnumerateArray().Select(b => b[0].ValueKind == JsonValueKind.Null
                        ? "-" : $"{b[0]}@{b[1]},{b[2]}:{b[3]}").ToList();
                    var gotBodies = map!.Bodies.Select(b => b is null ? "-" : $"{list.IndexOf(b.Creature)}@{b.X},{b.Y}:{b.Terrain}").ToList();
                    if (!gotBodies.SequenceEqual(wantBodies))
                        fails.Add($"{what}: bodies {string.Join(" ", gotBodies)} vs {string.Join(" ", wantBodies)}");
                    break;
                }
                case "save":
                {
                    var c = list[0];
                    int type = k.GetProperty("type").GetInt32();
                    bool saved = fx.SavingThrow(c, type, k.GetProperty("mod").GetInt32());
                    byte roll = (byte)res.GetProperty("roll").GetInt32();
                    byte con = c[0x14];
                    bool bonus = con is < 4 or > 20 && c.Effects.Any(e =>
                        e[0] == 90 && type == 0 || e[0] == 97 && type is 2 or 4);
                    if (bonus && roll != fx.Cells.SaveRoll)
                    {
                        // Constitution outside 4..20: the handler adds its uninitialised byte, the oracle's stack filler
                        // 0xEE (a natural 1 or 20 returns before the handlers run, so those rolls are equal); Core adds
                        // the s50 fix instead: 0 below 4, 5 above 20.
                        int fix = con > 20 ? 5 : 0;
                        if ((byte)(roll - fx.Cells.SaveRoll) != (byte)(0xEE - fix))
                            fails.Add($"{what}: Con {con}: roll {fx.Cells.SaveRoll} vs {roll} (residue not 0xEE)");
                        residue++;
                    }
                    else
                    {
                        if (roll != fx.Cells.SaveRoll)
                            fails.Add($"{what}: Con {con} type {type}: roll {fx.Cells.SaveRoll} vs {roll}");
                        if (saved != (res.GetProperty("saved").GetInt32() != 0))
                            fails.Add($"{what}: saved {saved}");
                    }
                    break;
                }
            }
            if (rng.Seed != res.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed {TurboPascalRandom.StepsBetween(k.GetProperty("seed").GetUInt32(), res.GetProperty("seedAfter").GetUInt32())} draws in the program, {rng.Draws} here");
            if (map is not null && Convert.ToHexString(map.Arena.Bytes, CombatArena.HeaderSize, 1250) !=
                res.GetProperty("cells").GetString()!.ToUpperInvariant())
                fails.Add($"{what}: arena cells differ");
            int ci = 0;
            foreach (var a in res.GetProperty("after").EnumerateArray())
            {
                var c = list[ci];
                var want = Load(a);
                for (int b = 0; b < EclCharacter.RecordSize; b++)
                    if (!PointerByte(b) && want[b] != c[b])
                        fails.Add($"{what} creature {ci} +{b:X2}: {c[b]} vs {want[b]}");
                string Effects(EclCharacter x) => string.Join(" ", x.Effects.Select(e => Convert.ToHexString(e, 0, 5)));
                if (Effects(want) != Effects(c))
                    fails.Add($"{what} creature {ci} effects: {Effects(c)} vs {Effects(want)}");
                ci++;
            }
            n++;
        }
        Assert.True(kinds.GetValueOrDefault("cloud") >= 30 && kinds.GetValueOrDefault("breath") >= 50 &&
                    kinds.GetValueOrDefault("gaze") >= 50 &&
                    kinds.GetValueOrDefault("save") >= 100 && kinds.GetValueOrDefault("place") >= 50, string.Join(", ", kinds));
        Assert.True(residue >= 10, $"{residue} residue cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }
}
