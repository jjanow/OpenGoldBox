using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The cast routine 0x27C38 and the scroll spell lists 0x275B4 → 0x27466 / 0x27534 against the program itself
/// (<c>tools/scripts/por_spellcast_oracle.py</c>, private fixture <c>program-v1.3/cast.json</c>; s49).
/// </summary>
public class CastProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "cast.json");
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
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/cast.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);
    private static readonly Lazy<SpellTable> Spells = new(() => PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!);

    /// <summary>
    /// Out of a fight (DS:49F3 = 2–4): Core's <see cref="CampCast.Run"/> with the oracle's scripted pick and answers
    /// gives the same done byte, memorised bytes and order of questions, cast message, pick and routine call; the
    /// routine's way out (START 0x2383) every time.
    /// </summary>
    [OracleFact]
    public void Outside_a_fight_the_cast_matches_the_program()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("cast").EnumerateArray())
        {
            if (c.GetProperty("fight").GetInt32() != 0)
                continue;
            n++;
            int spell = c.GetProperty("spell").GetInt32();
            var p = new EclParty();
            var caster = new EclCharacter { Name = "A" };
            Convert.FromHexString(c.GetProperty("memorised").GetString()!).CopyTo(caster.Record, FightSpells.Memorised);
            p.Members.Add(caster);
            p.SetCurrent(caster);
            var cast = new CampCast(new FightSpells(Spells.Value, new EffectHandlers(_ => 0), _ => 0), p)
            {
                Spells = { UsingItem = c.GetProperty("item").GetInt32() != 0 },
            };
            var picks = new Queue<int>(c.GetProperty("picks").EnumerateArray().Select(x => x.GetInt32()));
            var answers = new Queue<char>(c.GetProperty("answers").GetString()!);
            var log = new List<string>();
            bool done = c.GetProperty("done0").GetInt32() != 0;
            cast.Run(spell, c.GetProperty("announce").GetInt32() != 0, new CampCast.Hooks
            {
                CombatOnly = (_, item) =>
                {
                    char a = answers.Dequeue();
                    log.Add("print");
                    log.Add("print");
                    log.Add("yesno " + (item ? 8 : 9) + " " + a); // the question's length: Use it? / Lose it?
                    return a == 'Y';
                },
                Announce = (_, s) => log.Add("castmsg " + s),
                Pick = s => { log.Add("pick " + s); return picks.Dequeue() != 0; },
                Routine = s => { log.Add("invis"); log.Add("routine " + s); },
                End = () => log.Add("end"),
            }, ref done);
            var expected = new List<string>();
            foreach (var e in c.GetProperty("log").EnumerateArray())
            {
                var name = e[0].GetString()!;
                expected.Add(name switch
                {
                    "castmsg" => "castmsg " + e[1].GetInt32(),
                    "pick" => "pick " + e[1].GetInt32(),
                    "routine" => "routine " + e[1].GetInt32(),
                    "yesno" => "yesno " + e[1].GetInt32() + " " + e[5].GetString(),
                    _ => name,
                });
            }
            string where = $"case {n}: spell {spell}";
            Assert.True(expected.SequenceEqual(log), where + ": " + string.Join(",", expected) + " vs " + string.Join(",", log));
            Assert.Equal(c.GetProperty("done").GetInt32() != 0, done);
            Assert.Equal(c.GetProperty("after").GetString(), Convert.ToHexString(caster.Record, FightSpells.Memorised, 21).ToLowerInvariant());
        }
        Assert.True(n > 100, $"{n} cases");
    }

    /// <summary>
    /// In a fight the routine's facts Core's fight cast follows (<see cref="FightSpells.CastNow"/>, the menu's
    /// <c>FightMenu.CastNow</c> loop): the cast message once, before the first pick, only with announce and no item; no
    /// combat-only question; a computer caster not picked: the aborted message, no question; a player not picked: the
    /// abort question (colours 14, 10, 15 in push order), No → pick again; given up: the memorised copy goes unless an item casts;
    /// picked: the flight, then the routine; DS:681B and DS:6820 = 0 after a routine; START 0x2383 and the row-23 clear
    /// on every way out.
    /// </summary>
    [OracleFact]
    public void In_a_fight_the_cast_follows_the_menu_and_ai_rules()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("cast").EnumerateArray())
        {
            if (c.GetProperty("fight").GetInt32() == 0)
                continue;
            n++;
            bool item = c.GetProperty("item").GetInt32() != 0, ai = c.GetProperty("ai").GetInt32() != 0;
            bool announce = c.GetProperty("announce").GetInt32() != 0;
            int spell = c.GetProperty("spell").GetInt32();
            var log = c.GetProperty("log").EnumerateArray().Select(e => e[0].GetString()!).ToList();
            Assert.Equal(announce && !item ? 1 : 0, log.Count(x => x == "castmsg"));
            if (log.Contains("castmsg"))
                Assert.Equal(0, log.IndexOf("castmsg"));
            Assert.DoesNotContain("print", log);
            Assert.Equal(["end", "clear"], log.TakeLast(2));
            var picks = c.GetProperty("picks").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var answers = c.GetProperty("answers").GetString()!;
            // model of the loop
            var model = new List<string>();
            if (announce && !item)
                model.Add("castmsg");
            int k = 0, q = 0;
            bool cast = false;
            while (true)
            {
                model.Add("pick");
                if ((k < picks.Count ? picks[k++] : 0) != 0)
                {
                    cast = true;
                    break;
                }
                if (!ai && (q < answers.Length ? answers[q++] : 'Y') != 'Y')
                {
                    model.Add("yesno");
                    continue;
                }
                if (!ai)
                    model.Add("yesno");
                model.Add("rowmsg");
                break;
            }
            Assert.Equal(model, log.Where(x => x is "castmsg" or "pick" or "yesno" or "rowmsg").ToList());
            Assert.Equal(cast, log.Contains("routine"));
            var before = Convert.FromHexString(c.GetProperty("memorised").GetString()!);
            var after = Convert.FromHexString(c.GetProperty("after").GetString()!);
            int first = Array.IndexOf(before, (byte)spell);
            if (item || first < 0)
                Assert.Equal(before, after);
            else
            {
                before[first] = 0; // START 0x202B: the first slot holding the spell (bit 7 clear)
                Assert.Equal(before, after);
            }
            if (cast)
                Assert.Equal((0, 0), (c.GetProperty("ds681b").GetInt32(), c.GetProperty("ds6820").GetInt32()));
            foreach (var y in c.GetProperty("log").EnumerateArray().Where(e => e[0].GetString() == "yesno"))
                Assert.Equal((14, 10, 15), (y[2].GetInt32(), y[3].GetInt32(), y[4].GetInt32()));
        }
        Assert.True(n > 100, $"{n} cases");
    }

    /// <summary>
    /// 0x275B4 modes 2, 3 and 6: Core's <see cref="CampSpells.ScrollList"/> / <see cref="CampSpells.List"/> give the
    /// program's rows (headings and spells, the spell AND 0x7F as DS:476A holds it: bit 7 of a scroll pick is not
    /// kept), the row → scroll table DS:477F (modes 3 / 6), the identification (0x35) and the found flag. DS:4843: reset
    /// by 0x27534 (modes 3 / 6) only, a mode-2 read adds its rows to the count left before.
    /// </summary>
    [OracleFact]
    public void Scroll_lists_match_the_program()
    {
        int n = 0;
        foreach (var c in Root.Value.GetProperty("list").EnumerateArray())
        {
            n++;
            int mode = c.GetProperty("mode").GetInt32();
            var items = c.GetProperty("items").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToList();
            var slots = c.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var reader = new EclCharacter();
            reader[CampSpells.ClericLevel] = (byte)c.GetProperty("cleric").GetInt32();
            foreach (var id in c.GetProperty("effects").EnumerateArray())
                reader.Effects.Add([(byte)id.GetInt32(), 0, 0, 0xFF, 0]);
            foreach (var it in items)
                reader.Items.Add(it);
            int Slot(byte[] it) => slots[items.IndexOf(it)];
            var camp = new CampSpells(Spells.Value, it => Slot(it) is > 10 and < 14);
            List<CampSpellRow> rows = mode switch
            {
                2 => CampSpells.ScrollList(Spells.Value, reader, items[c.GetProperty("which").GetInt32()],
                    slots[c.GetProperty("which").GetInt32()]),
                3 => camp.List(reader, CampSpells.ListKind.OnScrolls, Slot),
                _ => camp.List(reader, CampSpells.ListKind.ToScribe, Slot),
            };
            var heads = rows.SelectMany(r => r.HeadingLevel is null ? new[] { 0 } : [1, 0]).ToList();
            string where = $"list case {n} mode {mode}";
            Assert.True(heads.SequenceEqual(c.GetProperty("headings").EnumerateArray().Select(x => x.GetInt32())), where);
            Assert.Equal(rows.Count > 0 ? 1 : 0, c.GetProperty("found").GetInt32());
            var spells = c.GetProperty("spells").EnumerateArray().Select(x => x.GetInt32()).ToList();
            Assert.Equal(spells, rows.Take(21).Select(r => r.Spell).ToList());
            if (mode != 2)
            {
                Assert.Equal(rows.Count, c.GetProperty("count").GetInt32());
                Assert.Equal(c.GetProperty("table").EnumerateArray().Select(x => x.GetInt32()).ToList(),
                    rows.Select(r => items.IndexOf(r.Scroll!)).ToList());
            }
            else
                Assert.Equal(c.GetProperty("count0").GetInt32() + rows.Count, c.GetProperty("count").GetInt32());
            Assert.Equal(c.GetProperty("hidden").EnumerateArray().Select(x => x.GetInt32()).ToList(),
                items.Select(it => (int)it[0x35]).ToList());
        }
        Assert.True(n > 100, $"{n} cases");
    }
}
