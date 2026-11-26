using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The game clock, the effect timeout and the camp's rest against the program itself: fixtures made by running the v1.3
/// routines of GAME.OVR unit 0x8C under emulation (<c>tools/scripts/por_clock_oracle.py</c>, private fixture
/// <c>program-v1.3/clock.json</c>): the clock 0x241A1 with its carry 0x240BE and the effect timeout 0x23DCC
/// (<see cref="GameClock.Advance"/>, <see cref="CampTime.TimeOutEffects(EclMemory, byte[], bool, int, int, Action{EclCharacter, byte[]})"/>),
/// the rest-time carry 0x2416F and borrow 0x24246 (<see cref="CampTime.Validate"/>, <see cref="CampTime.Decrement"/>),
/// the rest-time menu 0x244ED (<see cref="RestTimeMenu"/>) and the rest loop 0x24A66 with the heal count 0x2463D and
/// the preparation 0x248D6 / 0x2497E / 0x246F1 / 0x247B3 (<see cref="CampRest.Run"/>).
/// </summary>
public class ClockProgramTests
{
    private static readonly string? FixturePath = Find();

    private static string? Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "program-v1.3", "clock.json");
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
                Skip = "needs the private fixture tests/fixtures-private/por/program-v1.3/clock.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<JsonElement> Root = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath!)).RootElement);
    private static readonly Lazy<SpellTable> Spells = new(() => PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!);
    private static readonly Lazy<BaseItemTable> Items =
        new(() => BaseItemTable.FromItemsFile(File.ReadAllBytes(PorData.Open().Find("ITEMS")!)));

    private static int[] Ints(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();
    private static string Join(IEnumerable<int> v) => string.Join(",", v);

    private static EclMemory Memory(JsonElement c, out List<EclCharacter> party)
    {
        var memory = new EclMemory { Party = new EclParty() };
        party = memory.Party.Members;
        foreach (var s in c.GetProperty("party").EnumerateArray())
        {
            var ch = new EclCharacter();
            if (s.TryGetProperty("bytes", out var b))
                foreach (var p in b.EnumerateObject())
                    ch[int.Parse(p.Name)] = (byte)p.Value.GetInt32();
            if (s.TryGetProperty("age", out var age))
                ch.SetWord(0x30, (ushort)age.GetInt32());
            foreach (var e in s.GetProperty("effects").EnumerateArray())
            {
                int mins = e[1].GetInt32();
                ch.Effects.Add([(byte)e[0].GetInt32(), (byte)mins, (byte)(mins >> 8), 0, 0, 0, 0, 0, 0]);
            }
            if (s.TryGetProperty("items", out var items))
                foreach (var it in items.EnumerateArray())
                    ch.Items.Add(Convert.FromHexString(it.GetString()!));
            party.Add(ch);
        }
        var clock = Ints(c.GetProperty("clock"));
        for (int i = 0; i < GameClock.Cells; i++)
            memory.Write((ushort)(GameClock.FirstCell + i), (ushort)clock[i]);
        return memory;
    }

    private static string Clock(EclMemory m) =>
        Join(Enumerable.Range(0, GameClock.Cells).Select(i => (int)m.Read((ushort)(GameClock.FirstCell + i))));

    private static string Effects(List<EclCharacter> party) =>
        string.Join(";", party.Select(c => string.Join(" ", c.Effects.Select(e => $"{e[0]}:{e[1] | e[2] << 8}"))));

    private static string WantEffects(JsonElement c) =>
        string.Join(";", c.GetProperty("effectsAfter").EnumerateArray()
            .Select(l => string.Join(" ", l.EnumerateArray().Select(e => $"{e[0].GetInt32()}:{e[1].GetInt32()}"))));

    private static string WantLog(JsonElement c) =>
        string.Join(" ", c.GetProperty("log").EnumerateArray()
            .Where(e => e[0].GetString() != "dropped")
            .Select(e => string.Join(":", e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetInt32().ToString()))));

    private static Action<EclCharacter, byte[]> Remover(List<EclCharacter> party, List<string> log) => (ch, e) =>
    {
        log.Add($"remove:{party.IndexOf(ch)}:{ch.Effects.IndexOf(e)}:{e[0]}");
        ch.Effects.Remove(e);
    };

    [OracleFact]
    public void Clock_and_effect_timeout_match_0x241A1()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("advance").EnumerateArray())
        {
            var memory = Memory(c, out var party);
            memory.Write(EclPartyRules.PartySizeCell, (ushort)c.GetProperty("partySize").GetInt32());
            var flags = Ints(c.GetProperty("flags")).Select(x => (byte)x).ToArray();
            var log = new List<string>();
            int unit = c.GetProperty("unit").GetInt32(), count = c.GetProperty("count").GetInt32();
            GameClock.Advance(memory, unit, count);
            CampTime.TimeOutEffects(memory, flags, c.GetProperty("camping").GetInt32() != 0, count, unit, Remover(party, log));
            var got = (Clock(memory), Join(party.Select(p => (int)p.Word(0x30))), Effects(party), Join(flags.Select(x => (int)x)),
                string.Join(" ", log));
            var want = (Join(Ints(c.GetProperty("clockAfter"))), Join(Ints(c.GetProperty("ages"))), WantEffects(c),
                Join(Ints(c.GetProperty("flagsAfter"))), WantLog(c));
            if (got != want)
                fails.Add($"case {n} (unit {unit}, count {count}): {got} vs {want}");
            n++;
        }
        Assert.True(n >= 300);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(10)));
    }

    [OracleFact]
    public void Rest_time_carry_and_borrow_match_0x2416F_and_0x24246()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("resttime").EnumerateArray())
        {
            var party = new EclParty();
            var ch = new EclCharacter();
            ch.SetWord(0x30, (ushort)c.GetProperty("ageBefore").GetInt32());
            party.Members.Add(ch);
            var s = new CampState();
            Ints(c.GetProperty("time")).Select(x => (ushort)x).ToArray().CopyTo(s.RestTime, 0);
            if (c.GetProperty("op").GetString() == "carry")
                CampTime.Validate(s, party);
            else
                CampTime.Decrement(s, c.GetProperty("count").GetInt32(), c.GetProperty("unit").GetInt32(), party);
            var got = (Join(s.RestTime.Select(x => (int)x)), (int)ch.Word(0x30));
            var want = (Join(Ints(c.GetProperty("after"))), c.GetProperty("age").GetInt32());
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 300);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(10)));
    }

    /// <summary>The menu routine's (key, extended) pairs as the Client's rest-time menu receives them (null: no meaning).</summary>
    private static RestTimeMenu.Key? MenuKey(int key, int ext) => ext != 0
        ? key switch
        {
            0x48 => RestTimeMenu.Key.Up, 0x50 => RestTimeMenu.Key.Down, 0x4B => RestTimeMenu.Key.Left,
            0x4D => RestTimeMenu.Key.Right, _ => null,
        }
        : key switch
        {
            'R' or 13 => RestTimeMenu.Key.Rest, 'E' or 0 => RestTimeMenu.Key.Exit, 'Y' => RestTimeMenu.Key.Days,
            'H' => RestTimeMenu.Key.Hours, 'M' => RestTimeMenu.Key.Minutes, 'I' => RestTimeMenu.Key.Inc,
            'D' => RestTimeMenu.Key.Dec, _ => null,
        };

    [OracleFact]
    public void Rest_time_menu_matches_0x244ED()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("menu").EnumerateArray())
        {
            var party = new EclParty();
            party.Members.Add(new EclCharacter());
            party.Members[0].SetWord(0x30, 5);
            var s = new CampState();
            Ints(c.GetProperty("time")).Select(x => (ushort)x).ToArray().CopyTo(s.RestTime, 0);
            var menu = new RestTimeMenu(s, party);
            var keys = c.GetProperty("keys").EnumerateArray().Select(k => (k[0].GetInt32(), k[1].GetInt32())).ToList();
            bool? result = null;
            int used = 0;
            foreach (var (key, ext) in keys)
            {
                used++;
                if (MenuKey(key, ext) is { } k && menu.Press(k) is { } r)
                {
                    result = r;
                    break;
                }
            }
            var got = (result == true ? 1 : 0, Join(s.RestTime.Select(x => (int)x)), keys.Count - used);
            var want = (c.GetProperty("result").GetInt32(), Join(Ints(c.GetProperty("after"))), c.GetProperty("left").GetInt32());
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 150);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(10)));
    }

    private sealed class RestUi(List<string> log, List<EclCharacter> party, bool rest, Queue<int> keys, Queue<int> answers) : ICampRestUi
    {
        public bool ChooseRestTime(RestTimeMenu menu) => rest;
        public bool StopAsked() => keys.Dequeue() != 0 && answers.Dequeue() == 'Y';
        public void ShowRestTime() { }
        public void PartyHealed(bool anyone) { }
        public void Memorised(EclCharacter c, int spell) => log.Add($"memorised:{party.IndexOf(c)}:{spell}");
        public void Scribed(EclCharacter c, int spell) => log.Add($"scribed:{party.IndexOf(c)}:{spell}");
        public void Interrupted() { }
    }

    /// <summary>Record bytes compared: all but the far pointers the emulator wrote (effects 0x7F, items 0xC8, next 0x104).</summary>
    private static string Bytes(byte[] r) =>
        Convert.ToHexString(r.Select((b, i) => i is >= 0x7F and < 0x83 or >= 0xC8 and < 0xCC or >= 0x104 and < 0x108 ? (byte)0 : b).ToArray());

    [OracleFact]
    public void Rest_loop_matches_0x24A66()
    {
        int n = 0;
        var fails = new List<string>();
        var items = Items.Value;
        foreach (var c in Root.Value.GetProperty("rest").EnumerateArray())
        {
            var memory = Memory(c, out var party);
            memory.Write(EclPartyRules.PartySizeCell, (ushort)party.Count);
            memory.Write(CampRest.SafeTicksCell, (ushort)c.GetProperty("safe").GetInt32());
            memory.Write(CampRest.InterruptPercentCell, (ushort)c.GetProperty("percent").GetInt32());
            var state = new CampState
            {
                Camping = true,
                HealTicks = (ushort)c.GetProperty("healTicks").GetInt32(),
                IntervalTicks = (ushort)c.GetProperty("interval").GetInt32(),
            };
            Ints(c.GetProperty("restTime")).Select(x => (ushort)x).ToArray().CopyTo(state.RestTime, 0);
            var rng = new TurboPascalRandom(c.GetProperty("seed").GetUInt32());
            var log = new List<string>();
            var spells = new CampSpells(Spells.Value, it => items[it[0x2E], BaseItemTable.Slot] is > 10 and < 14);
            var rest = new CampRest(memory, state, spells, rng.Next, Remover(party, log), ch =>
            {
                bool ok = ch[FightRules.Hp] < ch[FightRules.HpMax];
                if (ok)
                    ch[FightRules.Hp]++;
                log.Add($"heal:{party.IndexOf(ch)}:{(ok ? 1 : 0)}");
                return ok;
            });
            var ui = new RestUi(log, party, c.GetProperty("rest").GetInt32() != 0,
                new Queue<int>(Ints(c.GetProperty("keys")).Append(0).Concat(Enumerable.Repeat(0, 1000))),
                new Queue<int>(Ints(c.GetProperty("answers"))));
            bool interrupted = rest.Run(ui);
            var got = (interrupted ? 1 : 0, Clock(memory), Join(state.RestTime.Select(x => (int)x)), (int)state.HealTicks,
                (int)state.IntervalTicks, Join(state.PrepTimers.Take(party.Count).Select(x => (int)x)), rng.Seed,
                string.Join(";", party.Select(p => Bytes(p.Record))), Effects(party),
                string.Join(";", party.Select(p => string.Join(" ", p.Items.Select(it => Convert.ToHexString(it, 0x2E, it.Length - 0x2E))))),
                string.Join(" ", log));
            var want = (c.GetProperty("result").GetInt32(), Join(Ints(c.GetProperty("clockAfter"))),
                Join(Ints(c.GetProperty("restTimeAfter"))), c.GetProperty("healTicksAfter").GetInt32(),
                c.GetProperty("intervalAfter").GetInt32(), Join(Ints(c.GetProperty("prepTimers"))), c.GetProperty("seedAfter").GetUInt32(),
                string.Join(";", c.GetProperty("records").EnumerateArray().Select(r => Bytes(Convert.FromHexString(r.GetString()!)))),
                WantEffects(c),
                string.Join(";", c.GetProperty("itemsAfter").EnumerateArray().Select(l => string.Join(" ", l.EnumerateArray().Select(x => x.GetString()!.ToUpperInvariant())))),
                WantLog(c));
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(5)));
    }
}
