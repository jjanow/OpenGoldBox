using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The AI's casting and the item use against the program itself: fixtures made by running the v1.3 routines under
/// emulation (<c>tools/scripts/por_ai_oracle.py</c>, private fixture <c>program-v1.3/ai_cast.json</c>): the cure-target
/// test 0x143F3, the AI's target pick 0x148BE / aim 0x14611 / target search 0x15FDF with their draws, the should-cast
/// test 0xAEAB, and the item use 0x22AC4 (cast trapped).
/// </summary>
public class AiCastProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "ai_cast.json");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (FixturePath is null)
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/ai_cast.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);
    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);
    private static readonly Lazy<SpellTable> Spells = new(() => PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!);
    private static readonly Lazy<BaseItemTable> Items =
        new(() => BaseItemTable.FromItemsFile(File.ReadAllBytes(PorData.Open().Find("ITEMS")!)));

    private sealed class Case
    {
        public required Fight Fight;
        public required CombatMap Map;
        public required List<EclCharacter> All;
        public required FightSpells Spells;
        public required TurboPascalRandom Rng;
    }

    private static int Int(JsonElement e, string name, int dflt = 0) => e.TryGetProperty(name, out var v) ? v.GetInt32() : dflt;

    /// <summary>The fight the oracle made: the creature list (party first), the slots of <c>places</c>, the cells, bodies.</summary>
    private static Case Build(JsonElement c, uint seed)
    {
        var map = new CombatMap(Tables.Value);
        Convert.FromHexString(c.GetProperty("cells").GetString()!).CopyTo(map.Arena.Bytes, CombatArena.HeaderSize);
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var all = new List<EclCharacter>();
        foreach (var s in c.GetProperty("creatures").EnumerateArray())
        {
            var ch = new EclCharacter();
            ch[FightRules.Size] = (byte)Int(s, "size");
            ch[FightRules.InPlay] = (byte)Int(s, "inplay");
            ch[FightRules.Status] = (byte)Int(s, "status", ch[FightRules.InPlay] != 0 ? 0 : 4);
            ch[FightRules.Hostile] = (byte)Int(s, "hostile");
            ch[FightRules.Hp] = (byte)Int(s, "hp");
            ch[FightSpells.MaxHp] = (byte)Int(s, "maxhp");
            ch[FightSpells.ClericLevel] = (byte)Int(s, "cleric");
            ch[FightSpells.MageLevel] = (byte)Int(s, "mage");
            ch[CombatRules.AutoCombat] = (byte)Int(s, "auto", 1);
            if (s.TryGetProperty("effects", out var effs))
                foreach (var id in effs.EnumerateArray())
                    ch.Effects.Add([(byte)id.GetInt32(), 0, 0, 0xFF, 0, 0, 0, 0, 0]);
            memory.Party.Members.Add(ch);
            all.Add(ch);
        }
        int k = 0;
        foreach (var p in c.GetProperty("places").EnumerateArray())
        {
            k++;
            map.SetSlotCreature(k, all[k - 1]);
            map.SetSlotPosition(k, p[0].GetInt32(), p[1].GetInt32());
            map.SetSlotSize(k, p[2].GetInt32());
        }
        map.SlotCount = k + 1;
        map.Rebuild();
        if (c.TryGetProperty("bodies", out var bodies))
            foreach (var b in bodies.EnumerateArray())
            {
                int who = b.GetProperty("creature").GetInt32();
                map.Bodies.Add(who < 0 ? null
                    : new CombatMap.Body(all[who], b.GetProperty("x").GetInt32(), b.GetProperty("y").GetInt32(), ArenaBuilder.Floor));
            }
        var rng = new TurboPascalRandom(seed);
        var fight = new Fight(memory, new EncounterState(), Items.Value, rng.Next, new EndActionsController(), map);
        var spells = new FightSpells(Spells.Value, new EffectHandlers(rng.Next), rng.Next);
        fight.Spells = spells;
        fight.State.Current = all[0];
        return new Case { Fight = fight, Map = map, All = all, Spells = spells, Rng = rng };
    }

    private static int IndexOf(Case k, EclCharacter? c) => c is null ? -1 : k.All.IndexOf(c);

    [OracleFact]
    public void Cure_target_matches_0x143F3()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("cure").EnumerateArray())
        {
            var k = Build(c, 1);
            var t = k.Spells.CureTarget(k.Fight, k.All[0]);
            int want = c.GetProperty("target").GetInt32();
            if (IndexOf(k, t) != want || (t is not null) != (c.GetProperty("result").GetInt32() != 0))
                fails.Add($"case {n}: {IndexOf(k, t)} vs {want}");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Ai_pick_and_aim_match_0x148BE_with_their_draws()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("pick").EnumerateArray())
        {
            uint seed = c.GetProperty("seed").GetUInt32();
            int spell = c.GetProperty("spell").GetInt32(), other = c.GetProperty("other").GetInt32();
            var k = Build(c, seed);
            var caster = k.All[0];
            k.Fight.Record(caster).Other = other < 0 ? null : k.All[other];
            k.Spells.UsingItem = c.GetProperty("item").GetInt32() != 0;
            bool picked = k.Spells.PickAuto(k.Fight, caster, spell);
            var got = (picked ? 1 : 0, string.Join(",", k.Spells.Targets.Select(t => IndexOf(k, t))),
                (int)k.Spells.Effects.Cells.MultiTarget, (byte)k.Spells.AimedSquare.X, (byte)k.Spells.AimedSquare.Y,
                IndexOf(k, k.Fight.Record(caster).Other), k.Rng.Seed);
            var aimed = c.GetProperty("aimed");
            var want = (c.GetProperty("picked").GetInt32(), string.Join(",", c.GetProperty("targets").EnumerateArray().Select(e => e.GetInt32())),
                c.GetProperty("multi").GetInt32(), aimed[0].GetByte(), aimed[1].GetByte(),
                c.GetProperty("otherAfter").GetInt32(), c.GetProperty("seedAfter").GetUInt32());
            if (got != want)
                fails.Add($"case {n} spell {spell}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 200);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Should_cast_matches_0xAEAB()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("pick").EnumerateArray())
        {
            uint seed = c.GetProperty("seed").GetUInt32();
            int spell = c.GetProperty("spell").GetInt32(), other = c.GetProperty("other").GetInt32();
            var k = Build(c, seed);
            k.Fight.Record(k.All[0]).Other = other < 0 ? null : k.All[other];
            bool should = MonsterAi.DecideCast(k.Fight, k.All[0], spell, c.GetProperty("priority").GetInt32());
            if ((should ? 1 : 0) != c.GetProperty("should").GetInt32() || k.Rng.Seed != c.GetProperty("shouldSeed").GetUInt32())
                fails.Add($"case {n} spell {spell}: {should}");
            n++;
        }
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Item_use_matches_0x22AC4()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("use").EnumerateArray())
        {
            var k = Build(JsonDocument.Parse("""{"cells":"","creatures":[{"size":1,"inplay":1,"hostile":0}],"places":[]}""").RootElement, 1);
            var user = k.All[0];
            user[CombatRules.AutoCombat] = (byte)c.GetProperty("auto").GetInt32();
            var items = c.GetProperty("items").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToList();
            user.Items.AddRange(items);
            var rec = k.Fight.Record(user);
            new byte[] { 7, 1, 1, 12, 0, 0, 9, 1 }.CopyTo(rec.Bytes, 0);
            int scripted = c.GetProperty("scripted").GetInt32();
            (int Spell, int Ai, int Flag)? seen = null;
            bool done = k.Spells.UseItemNow(k.Fight, user, items[c.GetProperty("which").GetInt32()], c.GetProperty("scroll").GetInt32(),
                cast: (spell, ai) =>
                {
                    seen = (spell, ai ? 1 : 0, k.Spells.UsingItem ? 1 : 0);
                    return scripted != 0;
                });
            var wantCast = c.GetProperty("cast");
            (int, int, int)? wantSeen = wantCast.ValueKind == JsonValueKind.Null ? null
                : (wantCast.GetProperty("spell").GetInt32(), wantCast.GetProperty("ai").GetInt32(), wantCast.GetProperty("flag").GetInt32());
            string after = string.Join(";", user.Items.Select(it => $"{items.IndexOf(it)}:{Convert.ToHexString(it.AsSpan(0x2E)).ToLowerInvariant()}"));
            string wantAfter = string.Join(";", c.GetProperty("after").EnumerateArray()
                .Select(a => $"{a.GetProperty("index").GetInt32()}:{a.GetProperty("bytes").GetString()}"));
            var got = (seen, done ? 1 : 0, Convert.ToHexString(rec.Bytes.AsSpan(0, 8)).ToLowerInvariant(), after);
            var want = (wantSeen, c.GetProperty("done").GetInt32(), c.GetProperty("record").GetString(), wantAfter);
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Halve_and_join_match_0x22827_and_0x228D7()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("stack").EnumerateArray())
        {
            var user = new EclCharacter();
            var items = c.GetProperty("items").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToList();
            user.Items.AddRange(items);
            var item = items[c.GetProperty("which").GetInt32()];
            if (c.GetProperty("op").GetString() == "halve")
                FightItemScreens.Halve(user, item);
            else
                FightItemScreens.Join(user, item);
            string after = string.Join(";", user.Items.Select(it => $"{items.IndexOf(it)}:{Convert.ToHexString(it.AsSpan(0x2E)).ToLowerInvariant()}"));
            string want = string.Join(";", c.GetProperty("after").EnumerateArray()
                .Select(a => $"{a.GetProperty("index").GetInt32()}:{a.GetProperty("bytes").GetString()}"));
            if (after != want)
                fails.Add($"case {n} {c.GetProperty("op").GetString()}: {after} vs {want}");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }
}
