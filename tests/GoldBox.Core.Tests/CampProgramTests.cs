using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The camp unit's spell and party-order routines against the program itself: the private fixture
/// <c>program-v1.3/camp.json</c> (<c>tools/scripts/por_camp_oracle.py</c>) runs, on made-up characters with spell slots,
/// a spell book, memorised / pending slots and scroll items: the preparation time 0x17831, the resets 0x1798A / 0x179CD /
/// 0x17A45, the sort 0x18055, the slots left 0x17A8D, the check 0x17B9D, Alter's moves 0x18CDA / 0x18E32, and Memorize
/// 0x1813B and Scribe 0x1838C whole with their spell lists and questions scripted. <see cref="CampSpells"/> and
/// <see cref="CampParty"/> must give the same records, items, list order, results and messages. For Memorize and Scribe
/// the test replays the program's own sequence of lists and answers through Core's marks and resets (the Client's loop,
/// <c>CampScreens.Memorise</c> / <c>Scribe</c>, follows the same sequence, read s52).
/// </summary>
public class CampProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "camp.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/camp.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<SpellTable> Table = new(() => PorSpellData.Spells(PorProgram.Open(PorData.Open())!)!);

    // Code-segment offsets (in the camp unit) of the texts the routines copy before a message or question.
    private const int NoMagicHere = 0x302, NoCondition = 0x322, CannotMemorise = 0x570, NoScrolls = 0xACF,
        AlreadyKnown = 0xAE7, AlreadyScribing = 0xB03, NoSlot = 0xB26;

    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    /// <summary>Record bytes the program keeps pointers in (item list, party link).</summary>
    private static bool PointerByte(int b) => b is >= 0xC8 and < 0xCC or >= 0x104 and < 0x108;

    private static EclCharacter Member(JsonElement m)
    {
        var c = new EclCharacter(Hex(m.GetProperty("record")));
        foreach (var it in m.GetProperty("items").EnumerateArray())
            c.Items.Add(Hex(it));
        return c;
    }

    [OracleFact]
    public void Camp_routines_match_the_program()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var baseItems = new BaseItemTable(Hex(doc.RootElement.GetProperty("items_5582")));
        var spells = new CampSpells(Table.Value, it => baseItems[it[0x2E], BaseItemTable.Slot] is > 10 and < 14);
        var fails = new List<string>();
        var seen = new Dictionary<string, int>();
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string kind = k.GetProperty("kind").GetString()!;
            string at = $"case {n} {kind}";
            var party = new EclParty();
            foreach (var m in k.GetProperty("members").EnumerateArray())
                party.Members.Add(Member(m));
            var members = party.Members.ToList();
            party.CurrentIndex = k.GetProperty("current").GetInt32();
            var c = party.Current!;
            var memory = new EclMemory();
            memory.Write(CampSpells.AntiMagicCell, (ushort)k.GetProperty("noMagic").GetInt32());
            var args = k.GetProperty("args").EnumerateArray().Select(a => a.GetInt32()).ToArray();
            int? ax = k.GetProperty("ax").ValueKind == JsonValueKind.Null ? null : k.GetProperty("ax").GetInt32();
            var log = k.GetProperty("log").EnumerateArray().ToList();
            int p = 0;
            JsonElement? Next(string what)
            {
                if (p < log.Count && log[p].TryGetProperty(what, out _))
                    return log[p++];
                fails.Add($"{at}: wanted {what} at log {p}, have {(p < log.Count ? log[p].ToString() : "the end")}");
                p = log.Count;
                return null;
            }
            void Expect(string what, int offset)
            {
                if (Next(what) is { } e && e.GetProperty(what).GetInt32() != offset)
                    fails.Add($"{at}: {what} {e.GetProperty(what).GetInt32():X} vs {offset:X}");
            }
            bool Checked(CampActivity a)
            {
                switch (CampSpells.Check(a, c, memory))
                {
                    case CampRefusal.NoMagicHere: Expect("message", NoMagicHere); return false;
                    case CampRefusal.NoCondition: Expect("message", NoCondition); return false;
                    default: return true;
                }
            }
            (int Kind, int Action, int Pick)? List()
            {
                if (Next("list") is not { } e)
                    return null;
                var l = e.GetProperty("list").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                return (l[0], l[1], e.GetProperty("pick").GetInt32());
            }
            // whether the scroll list would have rows (the oracle answers the list as the program builds it; Core's
            // List is not called here since it also identifies the scrolls, which the trapped list does not)
            bool OnScrolls(EclCharacter who, bool pendingOnly) => who.Items.Any(it => spells.Readable(it) && it[CampSpells.Hidden] == 0
                && Enumerable.Range(0, 3).Any(j => pendingOnly ? it[CampSpells.ScrollBytes + j] > 0x80 : it[CampSpells.ScrollBytes + j] > 0));
            bool No() => Next("question") is { } q && q.GetProperty("answer").GetString() == "N";

            switch (kind)
            {
                case "prep":
                    if (spells.PrepMinutes(c) != ax)
                        fails.Add($"{at}: minutes {spells.PrepMinutes(c)} vs {ax}");
                    break;
                case "resetMem": CampSpells.ResetMemorise(c); break;
                case "resetScribe": spells.ResetScribe(c); break;
                case "sort": CampSpells.Sort(c); break;
                case "resetParty": spells.ResetParty(party.Members); break;
                case "moveUp": CampParty.MoveUp(party); break;
                case "moveDown": CampParty.MoveDown(party); break;
                case "slots":
                    if (spells.SlotsLeft(c, args[0], args[1]) != ax)
                        fails.Add($"{at}: slots {spells.SlotsLeft(c, args[0], args[1])} vs {ax}");
                    break;
                case "check":
                    if ((Checked((CampActivity)args[0]) ? 1 : 0) != ax)
                        fails.Add($"{at}: check vs {ax}");
                    break;
                case "memorize":
                    if (!Checked(CampActivity.Memorise))
                        break;
                    while (p < log.Count)
                    {
                        if (log[p].TryGetProperty("message", out var msg))
                        {
                            // 0x17DD5's slot message before each pass of the book; with no slot it is the refusal
                            p++;
                            if (msg.GetInt32() != CannotMemorise)
                                fails.Add($"{at}: message {msg.GetInt32():X}");
                            if (!CampSpells.HasSlots(c) && p < log.Count)
                                fails.Add($"{at}: went on without slots");
                            continue;
                        }
                        if (List() is not { } l)
                            break;
                        if (l.Kind == 5)
                        {
                            bool pending = Enumerable.Range(0, CampSpells.Slots).Any(i => c[CampSpells.Memorised + i] > 0x7F);
                            if (pending && No())
                                CampSpells.ResetMemorise(c);
                        }
                        else if (l.Kind == 1 && l.Pick != 0)
                            spells.MarkMemorise(c, l.Pick);
                        else if (l.Kind != 1)
                            fails.Add($"{at}: list {l.Kind}");
                    }
                    break;
                case "scribe":
                    if (!Checked(CampActivity.Scribe))
                        break;
                    while (p < log.Count)
                    {
                        if (List() is not { } l)
                            break;
                        if (l.Kind == 6)
                        {
                            if (OnScrolls(c, true) && No())
                                spells.ResetScribe(c);
                        }
                        else if (l.Kind == 3 && l.Pick == 0)
                        {
                            if (!OnScrolls(c, false))
                                Expect("message", NoScrolls);
                        }
                        else if (l.Kind == 3)
                        {
                            var r = spells.MarkScribe(c, l.Pick);
                            seen[r.ToString()] = seen.GetValueOrDefault(r.ToString()) + 1;
                            switch (r)
                            {
                                case CampSpells.ScribeResult.AlreadyKnown: Expect("row", AlreadyKnown); break;
                                case CampSpells.ScribeResult.NoSlot: Expect("row", NoSlot); break;
                                case CampSpells.ScribeResult.AlreadyScribing:
                                    // one message per matching pending byte on the item that holds it (0x184F6)
                                    Expect("row", AlreadyScribing);
                                    while (p < log.Count && log[p].TryGetProperty("row", out var x) && x.GetInt32() == AlreadyScribing)
                                        p++;
                                    break;
                            }
                        }
                        else
                            fails.Add($"{at}: list {l.Kind}");
                    }
                    break;
            }
            if (p < log.Count)
                fails.Add($"{at}: log left from {p}: {log[p]}");
            var after = k.GetProperty("after").EnumerateArray().ToList();
            for (int i = 0; i < members.Count; i++)
            {
                var want = Member(after[i]);
                var have = members[i];
                for (int b = 0; b < EclCharacter.RecordSize; b++)
                    if (!PointerByte(b) && have[b] != want[b])
                    {
                        fails.Add($"{at}: member {i} byte {b:X}: {have[b]} vs {want[b]}");
                        break;
                    }
                if (have.Items.Count != want.Items.Count)
                    fails.Add($"{at}: member {i} items {have.Items.Count} vs {want.Items.Count}");
                else
                    for (int j = 0; j < have.Items.Count; j++)
                        if (!have.Items[j].AsSpan(0, 0x2A).SequenceEqual(want.Items[j].AsSpan(0, 0x2A))
                            || !have.Items[j].AsSpan(0x2E).SequenceEqual(want.Items[j].AsSpan(0x2E)))
                            fails.Add($"{at}: member {i} item {j}");
            }
            var order = k.GetProperty("order").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var haveOrder = party.Members.Select(m => members.IndexOf(m)).ToList();
            if (!order.SequenceEqual(haveOrder))
                fails.Add($"{at}: order {string.Join(",", haveOrder)} vs {string.Join(",", order)}");
            if (members.IndexOf(party.Current!) != k.GetProperty("currentAfter").GetInt32())
                fails.Add($"{at}: current");
            seen[kind] = seen.GetValueOrDefault(kind) + 1;
            n++;
        }
        Assert.True(n >= 600, $"{n} cases");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
        Assert.True(seen.GetValueOrDefault(nameof(CampSpells.ScribeResult.Marked)) > 5 && seen.GetValueOrDefault(nameof(CampSpells.ScribeResult.AlreadyScribing)) > 0,
            string.Join(", ", seen.Select(s => $"{s.Key} {s.Value}")));
    }
}
