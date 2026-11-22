using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// View's Items page (0x21F32) against the program itself: the fixture <c>program-v1.3/items_page.json</c> (private,
/// from <c>tools/scripts/por_items_oracle.py</c>) holds the loop run whole under emulation for every DS:49F3 (0–6) with
/// scripted keys: the entries it offers each pass, Ready (0x2250A), Drop (0x21DAA then 0x22398), Halve (0x22827) and
/// Join (0x228D7), their messages (START 0x1693), questions, the item handler calls (0x2AEEA) and the recalculation
/// START 0x1758 after every answer. Core's <see cref="ItemCommands"/>, <see cref="FightItemScreens"/> and
/// <see cref="StatRecalc"/> must do the same.
/// </summary>
public class ItemsPageProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "items_page.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/items_page.json";
        }
    }

    // The program's strings (GAME.OVR unit 0x2103A).
    private const string Cursed = "It's Cursed", WrongClass = "Wrong Class", AlreadyUsing = "already using ",
        HandsFull = "Your hands are full!", CannotHalve = "Can't halve that", MustBeUnreadied = "Must be unreadied",
        DropIt = "Drop It? ", OkayToLose = "is it Okay to lose it? ";

    private static string Word(ItemsEntry e) => e.ToString();

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    /// <summary>The made-up name the oracle gives each item ("ITEMn "), as START 0xFE2 would have cached it.</summary>
    private static string Name(byte[] item) => System.Text.Encoding.Latin1.GetString(item, 1, item[0]);

    [OracleFact]
    public void Items_page_commands_match_0x21F32_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var table = new BaseItemTable(Convert.FromHexString(doc.RootElement.GetProperty("items_5582").GetString()!));
        var fails = new List<string>();
        var seen = new Dictionary<string, int>();
        void Count(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        int n = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string at = $"case {n}";
            byte context = (byte)k.GetProperty("context").GetInt32();
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("record").GetString()!));
            foreach (var it in k.GetProperty("items").EnumerateArray())
                c.Items.Add(Convert.FromHexString(it.GetProperty("bytes").GetString()!));
            var ids = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
            for (int j = 0; j < c.Items.Count; j++)
                ids[c.Items[j]] = j;
            var memory = new EclMemory();
            memory.Write(FightItemScreens.NoMagicCell, (ushort)k.GetProperty("noMagic").GetInt32());
            CombatRecord? record = null;
            if (context == FightItemScreens.InFight)
            {
                record = new CombatRecord();
                record[CombatRecord.ItemUseAllowed] = (byte)k.GetProperty("itemUse").GetInt32();
            }
            var log = k.GetProperty("log").EnumerateArray().ToList();
            int p = 0;
            JsonElement? Next(string kind)
            {
                if (p < log.Count && log[p].TryGetProperty(kind, out _))
                    return log[p++];
                fails.Add($"{at}: wanted {kind} at log {p}, have {(p < log.Count ? log[p].ToString() : "the end")}");
                p = log.Count;
                return null;
            }
            void Message(string text)
            {
                if (Next("message") is { } m && m.GetProperty("message").GetString() != text)
                    fails.Add($"{at}: message '{m.GetProperty("message").GetString()}' vs '{text}'");
                Count("message " + text.Split(' ')[0]);
            }
            bool Answer(string question)
            {
                if (Next("question") is not { } q)
                    return false;
                if (q.GetProperty("question").GetString() != question)
                    fails.Add($"{at}: question '{q.GetProperty("question").GetString()}' vs '{question}'");
                return q.GetProperty("answer").GetString() == "Y";
            }
            void Handler(EclCharacter who, byte[] item, bool removed)
            {
                Count("handler");
                if (Next("handler") is not { } h)
                    return;
                if (h.GetProperty("handler").GetInt32() != item[ItemCommands.Kind] || h.GetProperty("removed").GetInt32() != (removed ? 1 : 0) ||
                    h.GetProperty("item").GetInt32() != ids.GetValueOrDefault(item, -1) || !h.GetProperty("current").GetBoolean())
                    fails.Add($"{at}: handler {h} vs {item[ItemCommands.Kind]} item {ids.GetValueOrDefault(item, -1)} removed {removed}");
            }
            while (p < log.Count)
            {
                if (c[FightItemScreens.ItemCount] == 0)
                {
                    fails.Add($"{at}: the program went on with no items");
                    break;
                }
                if (Next("entries") is not { } e)
                    break;
                string want = e.GetProperty("entries").GetString()!;
                string have = string.Join(" ", FightItemScreens.ItemsEntries(c, context, record, memory)
                    .Where(x => x != ItemsEntry.Exit).Select(Word));
                if (want != have)
                    fails.Add($"{at}: entries '{want}' vs '{have}'");
                if (Next("key") is not { } a)
                    break;
                char key = a.GetProperty("key").GetString()![0];
                int row = a.GetProperty("row").GetInt32();
                var item = row >= 0 && row < c.Items.Count ? c.Items[row] : null;
                Count("key " + key);
                if (item is not null)
                {
                    switch (key)
                    {
                        case 'R':
                        {
                            var r = ItemCommands.Ready(table, c, item, context, Handler);
                            Count("ready " + r.Refusal + (r.Message || r.Refusal == ReadyRefusal.None ? "" : " quiet"));
                            if (r.Message)
                                Message(r.Refusal switch
                                {
                                    ReadyRefusal.Cursed => Cursed,
                                    ReadyRefusal.WrongClass => WrongClass,
                                    ReadyRefusal.HandsFull => HandsFull,
                                    _ => AlreadyUsing + (r.InTheWay is { } w ? Name(w) : "?"),
                                });
                            break;
                        }
                        case 'D':
                        {
                            bool letGo;
                            if (item[ItemCommands.Readied] != 0)
                            {
                                Message(MustBeUnreadied);
                                letGo = false;
                            }
                            else if (FightSpells.IsScroll(table, item) && (item[0x3C] > 0x7F || item[0x3D] > 0x7F || item[0x3E] > 0x7F))
                                letGo = Answer(OkayToLose);
                            else
                                letGo = true;
                            if (letGo && ItemCommands.Drop(c, item, Answer(DropIt)))
                                Count("dropped");
                            break;
                        }
                        case 'H':
                            if (!FightItemScreens.Halve(c, item))
                                Message(CannotHalve);
                            else
                                Count("halved");
                            break;
                        case 'J':
                            FightItemScreens.Join(c, item);
                            break;
                    }
                }
                StatRecalc.Recalculate(c, table); // 0x224AF: after every answer of the list
            }
            if (p != log.Count)
                fails.Add($"{at}: {log.Count - p} log entries left");
            var after = Convert.FromHexString(k.GetProperty("after").GetString()!);
            for (int b = 0; b < after.Length; b++)
                if (after[b] != c[b] && b is not (>= 0xC8 and < 0x108) and not (>= 0x7F and < 0x83))
                    fails.Add($"{at} +{b:X2}: {c[b]} vs {after[b]}");
            var wantItems = k.GetProperty("itemsAfter").EnumerateArray().ToList();
            if (wantItems.Count != c.Items.Count)
                fails.Add($"{at}: {c.Items.Count} items vs {wantItems.Count}");
            else
                for (int j = 0; j < wantItems.Count; j++)
                {
                    var b = (byte[])c.Items[j].Clone();
                    Array.Clear(b, 0x2A, 4);
                    if (Hex(b)[(0x2A * 2)..] != wantItems[j].GetProperty("bytes").GetString()![(0x2A * 2)..] ||
                        ids.GetValueOrDefault(c.Items[j], -1) != wantItems[j].GetProperty("id").GetInt32())
                        fails.Add($"{at}: item {j} differs");
                }
            n++;
        }
        string counts = string.Join(", ", seen.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"));
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(40)) + "\n" + counts);
        Assert.True(n >= 400, counts);
        foreach (var must in new[] { "ready None", "ready Cursed", "ready WrongClass", "ready SlotTaken", "ready HandsFull",
                     "ready HandsFull quiet", "dropped", "halved", "handler", "message Can't", "message Must" })
            Assert.True(seen.GetValueOrDefault(must) > 0, $"no '{must}': {counts}");
    }
}
