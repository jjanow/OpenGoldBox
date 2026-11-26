using System.Text.Json;
using GoldBox.Core.Doors;
using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The door menu's Bash 0x1695F, Pick 0x16BAA, Knock 0x16D13 and the menu tests 0x16901 / 0x16CB9 against the program
/// itself: the private fixture <c>program-v1.3/doors.json</c> (<c>tools/scripts/por_door_oracle.py</c>) holds made-up
/// parties, squares, maps, menu flags and RandSeed run under emulation (the face set 0x167D4 and the dice run as they
/// are; the barrier accessor answers the case's barrier). <see cref="DoorRules"/> and <see cref="DoorMap.Open"/> must
/// give the same result, records, barrier plane, flags and RandSeed. The program's 0x16D13 only clears the slot and
/// its caller 0x17438 sets no face (the door stays locked); Core's caller <c>AreaLoop.DoorMenu</c> sets both faces as
/// Bash and Pick do (original bug, fixed: user s53, AreaLoopTests).
/// </summary>
public class DoorProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "doors.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/doors.json";
        }
    }

    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    [OracleFact]
    public void Door_routines_match_the_program()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, opened = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string kind = k.GetProperty("kind").GetString()!;
            var records = k.GetProperty("records").EnumerateArray().Select(Hex).ToList();
            var party = records.Select(r => DoorMember.FromRecord(r)).ToList();
            var planes = new byte[DoorMap.PlaneSize * 4];
            Hex(k.GetProperty("planes")).CopyTo(planes, DoorMap.BarrierPlane);
            int x = k.GetProperty("x").GetInt32(), y = k.GetProperty("y").GetInt32(), facing = k.GetProperty("facing").GetInt32();
            var barrier = (DoorBarrier)k.GetProperty("barrier").GetInt32();
            var f = k.GetProperty("flags").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var flags = new DoorMenuFlags { Bash = f[0] != 0, Pick = f[1] != 0, Knock = f[2] != 0 };
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            int result = k.GetProperty("result").GetInt32();
            int got;
            switch (kind)
            {
                case "bash":
                case "pick":
                {
                    // 0x16BAA has no barrier test of its own (the menu's 0x176E0 refuses barrier 3 before it)
                    var a = kind == "bash" ? DoorRules.Bash(barrier, party, rng, flags) : DoorRules.Pick(DoorBarrier.Locked, party, rng, flags);
                    if (a.Opened)
                        DoorMap.Open(planes, x, y, facing);
                    got = a.Opened ? 1 : 0;
                    break;
                }
                case "knock":
                {
                    var a = DoorRules.Knock(party);
                    if (a.Opened)
                        records[a.Member][DoorMember.SpellSlotsOffset + a.KnockSlot] = 0;
                    got = a.Opened ? 1 : 0;
                    break;
                }
                case "class":
                {
                    int cls = k.GetProperty("arg").GetInt32();
                    got = records.Any(r => (sbyte)r[0x96 + cls] > 0) ? 1 : 0;
                    break;
                }
                default:
                {
                    byte spell = (byte)k.GetProperty("arg").GetInt32();
                    int i = party.FindIndex(m => m.SlotOf(spell) >= 0);
                    got = i < 0 ? 0xFF : i;
                    break;
                }
            }
            if (got != result)
                fails.Add($"case {n} {kind}: result {got} vs {result}");
            opened += result == 1 && kind is "bash" or "pick" or "knock" ? 1 : 0;
            var after = k.GetProperty("recordsAfter");
            for (int i = 0; i < records.Count; i++)
            {
                byte[] want = after.TryGetProperty(i.ToString(), out var w) ? Hex(w) : Hex(k.GetProperty("records")[i]);
                if (!records[i].AsSpan().SequenceEqual(want))
                    fails.Add($"case {n} {kind}: record {i}");
            }
            if (!planes.AsSpan(DoorMap.BarrierPlane).SequenceEqual(Hex(k.GetProperty("planesAfter"))))
                fails.Add($"case {n} {kind}: barrier plane");
            var fa = k.GetProperty("flagsAfter").EnumerateArray().Select(v => v.GetInt32() != 0).ToArray();
            if (kind is "bash" or "pick" && (flags.Bash != fa[0] || flags.Pick != fa[1] || flags.Knock != fa[2]))
                fails.Add($"case {n} {kind}: flags {flags.Bash}/{flags.Pick}/{flags.Knock}");
            if (rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"case {n} {kind}: RandSeed");
            n++;
        }
        Assert.True(n >= 900, $"{n} cases");
        Assert.True(opened > 300, $"{opened} opened");
        Assert.True(fails.Count == 0, $"{fails.Count} of {n} differ:\n" + string.Join("\n", fails.Take(30)));
    }
}
