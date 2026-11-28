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

    /// <summary>
    /// Every heal-list entry (0x47C0, 0x4864, 0x4978 with 1–3, 0x4CC8, 0x4A87, 0x4DB1, 0x4E9F) on made-up characters:
    /// the questions asked, the record, effect list and items afterwards, DS:681F and RandSeed, and the messages the
    /// effect engine raises against the program's START 0x16EC / 0x2B8B calls (heal: the revive of handler 0x4E;
    /// poison: the death of handler 22 and the "goes down" of handler 15's damage). The poison cure is an original bug
    /// fixed (user s51, <see cref="TempleRules.CurePoison"/>): where the program's cure leaves a record of 55, 22 or 15,
    /// changes the record (handler 15's hit point, handler 22's death) or raises a message, the test expects the fixed
    /// result instead, worked out here from the input: every 55, 22 and 15 gone, the record, RandSeed and messages
    /// unchanged, the lock 0. Raise dead's max HP is also an original bug fixed (user s61,
    /// <see cref="TempleRules.RaiseDead"/>): the test expects the hall's Con bonus difference per level instead of the
    /// program's byte 0x32; every other byte as the program.
    /// </summary>
    [OracleDataFact]
    public void Temple_entries_match_the_program_under_emulation()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var names = PorSpellData.Names(program);
        var tables = PorTrainingTables.From(PorProgramData.Open(PorData.Open()))!;
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0, poisonFixed = 0, raiseFixed = 0;
        var seen = new HashSet<int>();
        foreach (var k in doc.RootElement.GetProperty("templeEntries").EnumerateArray())
        {
            n++;
            var cure = (TempleRules.Cure)k.GetProperty("entry").GetInt32();
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("record").GetString()!));
            foreach (var e in k.GetProperty("effects").EnumerateArray())
            {
                var r = new byte[EclCharacter.EffectRecordSize];
                Convert.FromHexString(e.GetString()!).CopyTo(r, 0);
                c.Effects.Add(r);
            }
            foreach (var it in k.GetProperty("items").EnumerateArray())
                c.Items.Add(Convert.FromHexString(it.GetString()!));
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
            int messages = 0, effectMessages = 0;
            fx.Event += (_, e) =>
            {
                if (e is EffectEvent.Revived or EffectEvent.PoisonDeath or EffectEvent.GoesDown)
                    messages++;
            };
            var answers = k.GetProperty("answers").EnumerateArray().Select(a => a.GetInt32()).ToList();
            // Cures 2–4 have no condition and no "cure anyway"; the others ask it when the condition is absent.
            bool conditional = cure is not (TempleRules.Cure.CureLight or TempleRules.Cure.CureSerious or TempleRules.Cure.CureCritical);
            bool applies = TempleRules.Applies(cure, c);
            int asked = conditional && !applies ? 1 : 0, a = 0;
            bool go = asked == 0 || answers[a++] == 'Y';
            int paid = go ? 1 : 0;
            bool cured = go && answers[a] == 'Y';
            if (cured)
            {
                seen.Add((int)cure);
                TempleRules.Apply(cure, c, x => rng.Next((ushort)x), fx, (_, e) =>
                {
                    // Cure 7's spell routine 0x29558: 0x2BF6B's START 0x16EC, then its own START 0x2B8B texts.
                    if (e == EffectEvent.SpellEffectRemoved)
                        messages++;
                    else
                        effectMessages++;
                }, tables);
            }
            string what = $"case {n} ({cure})";
            var counts = k.GetProperty("counts");
            int Count(string name) => counts.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            if (asked != Count("cureAnyway") || paid != Count("pay"))
                fails.Add($"{what}: asked {asked} paid {paid} vs {counts}");
            string Effects(IEnumerable<byte[]> x) => string.Join(" ", x.Select(e => Convert.ToHexString(e, 0, 5)));
            var after = k.GetProperty("after");
            if (cured && cure == TempleRules.Cure.Poison)
            {
                // The fixed cure, from the input alone.
                string inRecord = k.GetProperty("record").GetString()!;
                var inEffects = k.GetProperty("effects").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToList();
                string fixedEffects = Effects(inEffects.Where(e => !TempleRules.PoisonEffects.Contains(e[0])));
                bool programHarms = !Convert.FromHexString(after.GetProperty("record").GetString()!)
                        .Where((b, i) => !PointerByte(i)).SequenceEqual(Convert.FromHexString(inRecord).Where((b, i) => !PointerByte(i)))
                    || Count("message") != 0
                    || after.GetProperty("effects").EnumerateArray().Any(e => TempleRules.PoisonEffects.Contains(Convert.FromHexString(e.GetString()!)[0]));
                if (programHarms)
                {
                    poisonFixed++;
                    SameRecord(fails, what + " (fixed)", c, inRecord);
                    if (Effects(c.Effects) != fixedEffects)
                        fails.Add($"{what} effects (fixed): {Effects(c.Effects)} vs {fixedEffects}");
                    if (messages != 0 || fx.Cells.Lock != 0 || rng.Seed != k.GetProperty("seed").GetUInt32())
                        fails.Add($"{what} (fixed): {messages} messages, lock {fx.Cells.Lock}, RandSeed moved");
                    continue;
                }
            }
            if (messages != Count("message") || effectMessages != Count("effectMessage"))
                fails.Add($"{what}: {messages} / {effectMessages} messages vs {counts}");
            if (fx.Cells.Lock != k.GetProperty("lock").GetInt32())
                fails.Add($"{what}: lock {fx.Cells.Lock}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed differs");
            string wantRecord = after.GetProperty("record").GetString()!;
            if (cured && applies && cure == TempleRules.Cure.RaiseDead)
            {
                // Raise dead's max HP, an original bug fixed (user s61): the program's byte 0x32 is replaced by the
                // input's max HP less, per level, the hall's bonus (DS:408E, +1 above 16 and 17 for class byte 2) for
                // the old Con minus that for the new, divided by the classes with a level; never below 1.
                var input = Convert.FromHexString(k.GetProperty("record").GetString()!);
                int Bonus(int con) => (sbyte)tables.At(0x408E + con) + (input[0x2F] == 2 && con > 16 ? 1 : 0)
                    + (input[0x2F] == 2 && con > 17 ? 1 : 0);
                int oldCon = input[0x14], diff = Bonus(oldCon) - Bonus(Math.Max(0, oldCon - 1)), sum = 0, classes = 0;
                for (int j = 0; j < 8; j++)
                    if ((sbyte)input[0x96 + j] > 0)
                    {
                        classes++;
                        sum += (sbyte)input[0x96 + j] * diff;
                    }
                int loss = classes == 0 ? 0 : sum / classes;
                var want = Convert.FromHexString(wantRecord);
                byte fixedMax = loss > 0 ? (byte)Math.Max(1, input[0x32] - loss) : input[0x32];
                if (want[0x32] != fixedMax)
                    raiseFixed++;
                want[0x32] = fixedMax;
                wantRecord = Convert.ToHexString(want);
            }
            SameRecord(fails, what, c, wantRecord);
            string wantEffects = string.Join(" ", after.GetProperty("effects").EnumerateArray().Select(e => e.GetString()!.ToUpperInvariant()));
            if (Effects(c.Effects) != wantEffects)
                fails.Add($"{what} effects: {Effects(c.Effects)} vs {wantEffects}");
            var wantItems = after.GetProperty("items").EnumerateArray().Select(i => Convert.FromHexString(i.GetString()!)).ToList();
            if (wantItems.Count != c.Items.Count)
                fails.Add($"{what}: {c.Items.Count} items vs {wantItems.Count}");
            else
                for (int i = 0; i < wantItems.Count; i++)
                    for (int b = 0; b < wantItems[i].Length; b++)
                        if (b is not (>= 0x2A and < 0x2E) && wantItems[i][b] != c.Items[i][b])
                            fails.Add($"{what} item {i} +{b:X2}: {c.Items[i][b]} vs {wantItems[i][b]}");
        }
        Assert.True(n >= 1000, $"{n} cases");
        Assert.True(seen.Count == 9, $"entries cured: {string.Join(",", seen)}");
        Assert.True(poisonFixed >= 5, $"{poisonFixed} poison cures where the program harms");
        Assert.True(raiseFixed >= 5, $"{raiseFixed} raise deads where the fixed max HP differs from the program's");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// The loot menu 0x644B run with its menu routine answered by the oracle: every pass's item line, the D cast's
    /// arguments (the first 5 or 11 among the current character's memorised bytes, ai 0, announce 0) and the cast run
    /// for real (0x27C38 → 0x27AA9 → 0x28101), Home / End through 0x23819; afterwards the party's records, effect
    /// lists, the current member and RandSeed equal Core's (<see cref="LootRules"/>, <see cref="CampCast"/>).
    /// </summary>
    [OracleDataFact]
    public void Loot_menu_and_its_detect_cast_match_0x644B_under_emulation()
    {
        var program = PorProgram.Open(PorData.Open())!;
        var names = PorSpellData.Names(program);
        var table = PorSpellData.Spells(program)!;
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0, casts = 0;
        foreach (var k in doc.RootElement.GetProperty("loot").EnumerateArray())
        {
            n++;
            var party = new EclParty();
            foreach (var m in k.GetProperty("members").EnumerateArray())
            {
                var c = new EclCharacter(Convert.FromHexString(m.GetProperty("record").GetString()!));
                foreach (var e in m.GetProperty("effects").EnumerateArray())
                {
                    var r = new byte[EclCharacter.EffectRecordSize];
                    Convert.FromHexString(e.GetString()!).CopyTo(r, 0);
                    c.Effects.Add(r);
                }
                party.Members.Add(c);
            }
            party.CurrentIndex = k.GetProperty("current").GetInt32();
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var fx = new EffectHandlers(rng.Next, names)
            {
                LevelStats = _ => { },
                RaceLevelLimit = (_, _) => false,
                CanRevive = _ => true,
                Recalculate = _ => { },
            };
            var cast = new CampCast(new FightSpells(table, fx, rng.Next), party);
            bool money = k.GetProperty("pool").EnumerateArray().Any(p => p.GetUInt32() != 0);
            bool items = k.GetProperty("items").GetInt32() != 0;
            string what = $"case {n}";
            var log = k.GetProperty("log").EnumerateArray().ToList();
            for (int i = 0; i < log.Count; i++)
            {
                var l = log[i];
                if (!l.TryGetProperty("line", out var line))
                {
                    fails.Add($"{what}: unexpected {l}");
                    break;
                }
                int spell = LootRules.DetectSpell(party.Current, items);
                string want = string.Join(" ", LootRules.MenuWords(money, items, spell != 0));
                if (want != line.GetString())
                    fails.Add($"{what} pass {i}: '{want}' vs '{line.GetString()}'");
                int key = l.GetProperty("key").GetInt32();
                bool ext = l.GetProperty("ext").GetInt32() != 0;
                if (key == 'D' && !ext)
                {
                    if (i + 1 >= log.Count || !log[i + 1].TryGetProperty("cast", out var args) ||
                        args[0].GetInt32() != spell || args[1].GetInt32() != 0 || args[2].GetInt32() != 0)
                        fails.Add($"{what} pass {i}: D with spell {spell} vs {(i + 1 < log.Count ? log[i + 1] : "nothing")}");
                    else
                    {
                        i++;
                        casts++;
                        if (LootRules.Detect(cast, spell) != CampCast.Outcome.Cast)
                            fails.Add($"{what} pass {i}: the cast of {spell} did not happen");
                    }
                }
                else if (key is PartySelection.Home or PartySelection.End && ext)
                    PartySelection.Select(party, (byte)key);
                else if (key is 'V' or 'T' or 'S' or 'P')
                {
                    // View, Take, Share and (non-extended) Pool are trapped by the oracle: one call each.
                    if (i + 1 < log.Count && log[i + 1].TryGetProperty("call", out _))
                        i++;
                    else
                        fails.Add($"{what} pass {i}: {(char)key} called nothing");
                }
            }
            if (party.CurrentIndex != k.GetProperty("currentAfter").GetInt32())
                fails.Add($"{what}: current {party.CurrentIndex} vs {k.GetProperty("currentAfter")}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"{what}: RandSeed differs");
            var after = k.GetProperty("after").EnumerateArray().ToList();
            for (int m = 0; m < after.Count; m++)
            {
                var c = party.Members[m];
                SameRecord(fails, $"{what} member {m}", c, after[m].GetProperty("record").GetString()!);
                string Effects(IEnumerable<byte[]> x) => string.Join(" ", x.Select(e => Convert.ToHexString(e, 0, 5)));
                string wantEffects = string.Join(" ", after[m].GetProperty("effects").EnumerateArray().Select(e => e.GetString()!.ToUpperInvariant()));
                if (Effects(c.Effects) != wantEffects)
                    fails.Add($"{what} member {m} effects: {Effects(c.Effects)} vs {wantEffects}");
            }
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(casts >= 100, $"{casts} casts");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// Take 0x62B6 and the item take 0x6184 run for real by the oracle on made-up pools and treasure lists, the Take:
    /// menu, the coin take, the list routine on a non-empty list and the copy step answered by its script: Core's <see
    /// cref="LootRules.Take"/> / <see cref="LootRules.TakeItems{T}"/> fed the same answers make the same calls in the
    /// same order and leave the same list and pool. The empty list (Shift-F1 with nothing left): the window, the list
    /// routine's immediate return and the redraw, nothing else (DS:6B2B untouched).
    /// </summary>
    [OracleFact]
    public void Loot_take_matches_0x62B6_and_0x6184_under_emulation()
    {
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0, empty = 0, menus = 0, copies = 0;
        foreach (var k in doc.RootElement.GetProperty("lootTake").EnumerateArray())
        {
            n++;
            string what = $"case {n}";
            var log = k.GetProperty("log").EnumerateArray().Where(l => !(l.TryGetProperty("call", out var c) &&
                c.GetString() == "window")).ToList();
            var pool = k.GetProperty("pool").EnumerateArray().Select(p => p.GetUInt32()).ToArray();
            var treasure = Enumerable.Range(0, k.GetProperty("items").GetInt32()).ToList();
            if (pool.All(p => p == 0) && treasure.Count == 0)
            {
                empty++;
                if (log.Count != 2 || log[0].GetProperty("page").GetString() != "empty" ||
                    log[1].GetProperty("call").GetString() != "redraw" ||
                    k.GetProperty("b6b2bAfter").GetInt32() != k.GetProperty("b6b2b").GetInt32())
                    fails.Add($"{what}: empty take {k.GetProperty("log")}");
            }
            int i = 0;
            JsonElement? Next(string kind)
            {
                // The empty list's page runs in the program and returns at once; Core does not call the page there.
                while (i < log.Count && log[i].TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String)
                    i++;
                if (i < log.Count && log[i].TryGetProperty(kind, out _))
                    return log[i++];
                fails.Add($"{what}: {kind} vs {(i < log.Count ? log[i].ToString() : "the end")}");
                return null;
            }
            void Call(string name)
            {
                if (Next("call") is { } c && c.GetProperty("call").GetString() != name)
                    fails.Add($"{what}: {name} vs {c}");
            }
            int before = fails.Count;
            LootRules.Take(new LootRules.TakeSteps
            {
                Flags = () => (pool.Any(p => p != 0), treasure.Count > 0),
                Menu = () =>
                {
                    menus++;
                    if (Next("menu") is not { } m)
                        return (byte)'E';
                    if (m.GetProperty("menu").GetString() != "Money Items Exit")
                        fails.Add($"{what}: menu line {m}");
                    return (byte)m.GetProperty("key").GetInt32();
                },
                Coins = () =>
                {
                    if (Next("coins") is { } c && c.GetProperty("coins").GetInt32() != 0)
                        Array.Clear(pool);
                },
                Items = () => LootRules.TakeItems(treasure, _ =>
                    {
                        if (Next("page") is not { } p)
                            return (0, -1);
                        var rows = p.GetProperty("page").EnumerateArray().Select(r => r.GetInt32()).ToList();
                        if (!rows.SequenceEqual(treasure))
                            fails.Add($"{what}: page {string.Join(",", treasure)} vs {p}");
                        return ((byte)p.GetProperty("key").GetInt32(), p.GetProperty("row").GetInt32());
                    },
                    item =>
                    {
                        copies++;
                        if (Next("copy") is not { } c)
                            return false;
                        if (c.GetProperty("copy").GetInt32() != item)
                            fails.Add($"{what}: copy {item} vs {c}");
                        if (c.GetProperty("refused").GetInt32() != 0)
                            return false;
                        if (Next("free") is { } f && f.GetProperty("free").GetInt32() != item)
                            fails.Add($"{what}: free {item} vs {f}");
                        return true;
                    },
                    () => Call("redraw")),
                Select = key =>
                {
                    if (Next("select") is { } s && s.GetProperty("select").GetInt32() != key)
                        fails.Add($"{what}: select {key} vs {s}");
                },
                Redraw = () => Call("redraw"),
                PartyList = () => Call("party"),
            });
            if (fails.Count == before && i != log.Count)
                fails.Add($"{what}: Core stopped at {i} of {log.Count}");
            string left = string.Join(",", k.GetProperty("left").EnumerateArray().Select(x => x.GetInt32()));
            if (string.Join(",", treasure) != left)
                fails.Add($"{what}: left {string.Join(",", treasure)} vs {left}");
            if (!pool.SequenceEqual(k.GetProperty("poolAfter").EnumerateArray().Select(p => p.GetUInt32())))
                fails.Add($"{what}: pool differs");
        }
        Assert.True(n >= 300, $"{n} cases");
        Assert.True(empty >= 20, $"{empty} empty takes");
        Assert.True(menus >= 100 && copies >= 100, $"{menus} menus, {copies} copies");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    /// <summary>
    /// The after-combat award 0x58D3 run under emulation over lists of made-up records (class, prime scores, in-play,
    /// status and experience varied) with longint shares, negative and large ones included: <see
    /// cref="CombatRules.AwardExperience"/> leaves the same records.
    /// </summary>
    [OracleFact]
    public void Experience_award_matches_0x58D3_under_emulation()
    {
        using var doc = Doc();
        var fails = new List<string>();
        int n = 0, changed = 0;
        foreach (var k in doc.RootElement.GetProperty("award").EnumerateArray())
        {
            n++;
            var list = k.GetProperty("records").EnumerateArray()
                .Select(r => new EclCharacter(Convert.FromHexString(r.GetString()!))).ToList();
            var before = list.Select(c => c[0xAC]).ToList();
            CombatRules.AwardExperience(list, unchecked((int)k.GetProperty("share").GetUInt32()));
            var after = k.GetProperty("after").EnumerateArray().ToList();
            for (int m = 0; m < list.Count; m++)
            {
                SameRecord(fails, $"case {n} member {m}", list[m], after[m].GetString()!);
                changed += list[m][0xAC] != before[m] ? 1 : 0;
            }
        }
        Assert.True(n >= 300 && changed > 300, $"{n} cases, {changed} changed");
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
