using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The combat cast 0x14C0F, its memorised list 0x275B4(0) and the rest-time digits 0x24331 against the program itself:
/// fixtures made by running the v1.3 routines under emulation (<c>tools/scripts/por_cast_oracle.py</c>, private fixture
/// <c>program-v1.3/combat_cast.json</c>).
/// </summary>
public class CombatCastProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "combat_cast.json");
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
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/combat_cast.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);
    private static readonly Lazy<SpellTable> Table = new(() => PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!);

    private static FightSpells NewSpells()
    {
        var rng = new TurboPascalRandom(1);
        return new FightSpells(Table.Value, new EffectHandlers(rng.Next), rng.Next);
    }

    [OracleFact]
    public void Rest_time_digits_match_0x24331()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("two").EnumerateArray())
        {
            byte v = (byte)c.GetProperty("v").GetInt32();
            Assert.Equal(c.GetProperty("text").GetString(), CampTime.TwoDigits(v));
            n++;
        }
        Assert.Equal(256, n);
    }

    [OracleFact]
    public void Memorised_list_matches_0x275B4_kind_0()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("list").EnumerateArray())
        {
            var slots = Convert.FromHexString(c.GetProperty("slots").GetString()!);
            var ch = new EclCharacter();
            for (int i = 0; i < slots.Length; i++)
                ch[FightSpells.Memorised + i] = slots[i];
            int count = c.GetProperty("entries").EnumerateArray().Count(e => e.GetProperty("heading").GetInt32() == 0);
            var want = c.GetProperty("ids").EnumerateArray().Take(count).Select(e => e.GetInt32()).ToList();
            var got = FightSpells.MemorisedList(ch);
            if (!got.SequenceEqual(want) || (got.Count > 0) != (c.GetProperty("any").GetInt32() != 0))
                fails.Add($"case {n}: [{string.Join(",", got)}] vs [{string.Join(",", want)}]");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    /// <summary>
    /// 0x14C0F for every spell id 0..67, ai 0 and 1, initiatives around the delay: the calls it makes (list, camp-only
    /// message, redraws for ai = 0, cast 0x27C38(spell, ai, 1), begins-casting message), the done flag and combat record
    /// bytes 0..7. Ids above 67 read past the table in the program and are left out ([?] in spells-effects.md).
    /// </summary>
    [OracleFact]
    public void Combat_cast_matches_0x14C0F()
    {
        var spells = NewSpells();
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("cast").EnumerateArray())
        {
            int arg = c.GetProperty("spell").GetInt32(), ai = c.GetProperty("ai").GetInt32();
            if (arg > SpellTable.Count)
                continue;
            int spell = arg == 0 ? c.GetProperty("pick").GetInt32() : arg;
            var rec = new CombatRecord();
            Convert.FromHexString(c.GetProperty("before").GetString()!).CopyTo(rec.Bytes, 0);
            var outcome = FightSpells.CombatCastStep(spells, rec, spell, out sbyte initiative);
            var calls = new List<string>();
            if (arg == 0)
                calls.Add("list");
            if (outcome == CombatCastOutcome.CampOnly)
                calls.Add("text");
            if (ai == 0)
                calls.AddRange(["clear", "map", "summary"]);
            int done = 0;
            switch (outcome)
            {
                case CombatCastOutcome.CastNow:
                    calls.Add("cast");
                    FightRules.EndActions(rec);
                    done = 1;
                    break;
                case CombatCastOutcome.Begun:
                    calls.Add("named");
                    rec.InitiativeValue = initiative;
                    done = 1;
                    break;
            }
            var got = c.GetProperty("calls").EnumerateArray().ToList();
            var ops = got.Select(x => x.GetProperty("op").GetString()!).ToList();
            string after = Convert.ToHexString(rec.Bytes, 0, 8).ToLowerInvariant();
            bool castArgs = got.Where(x => x.GetProperty("op").GetString() == "cast").All(x =>
                x.GetProperty("spell").GetInt32() == spell && x.GetProperty("ai").GetInt32() == ai && x.GetProperty("b").GetInt32() == 1);
            bool listArgs = got.Where(x => x.GetProperty("op").GetString() == "list").All(x =>
                x.GetProperty("kind").GetInt32() == 0 && x.GetProperty("action").GetInt32() == 1 && x.GetProperty("position").GetInt32() == -1);
            if (!ops.SequenceEqual(calls) || after != c.GetProperty("after").GetString() || done != c.GetProperty("done").GetInt32()
                || !castArgs || !listArgs)
                fails.Add($"case {n} (spell {arg}/{spell}, ai {ai}): [{string.Join(",", calls)}] {after} {done} vs " +
                          $"[{string.Join(",", ops)}] {c.GetProperty("after").GetString()} {c.GetProperty("done").GetInt32()}");
            n++;
        }
        Assert.True(n >= 700);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }
}
