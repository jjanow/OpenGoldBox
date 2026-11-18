using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The training hall's Train Character (GAME.OVR 0x1C32D) against the program itself: the private fixture
/// <c>program-v1.3/training.json</c> (<c>tools/scripts/por_train_oracle.py</c>) holds 900 made-up characters with the
/// hall mask, the cheat flag, the answer to the question, RandSeed before and after, the refusal reached, the spell
/// picked from the Learn list and the record afterwards, run under emulation. <see cref="TrainingHall"/> with the
/// tables read from the player's program must give the same bytes, the same refusal and the same draws.
/// </summary>
public class TrainingProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "training.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/training.json";
        }
    }

    /// <summary>The refusal messages' code-segment offsets in 0x1C32D's unit (the strings at GAME.OVR 0x1C26D–0x1C2C3).</summary>
    private static TrainingRefusal Refusal(JsonElement e) => e.ValueKind == JsonValueKind.Null
        ? TrainingRefusal.None
        : e.GetInt32() switch
        {
            0x28EE => TrainingRefusal.NotConscious,
            0x290D => TrainingRefusal.NoMoney,
            0x2925 => TrainingRefusal.WrongClass,
            0x2944 => TrainingRefusal.NotEnoughExperience,
            var x => throw new InvalidDataException($"refusal {x:X}"),
        };

    [OracleFact]
    public void Matches_0x1C32D_under_emulation()
    {
        var t = PorTrainingTables.From(PorProgramData.Open(PorData.Open()));
        Assert.NotNull(t);
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, trained = 0, halts = 0, empty = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("before").GetString()!));
            ushort mask = (ushort)k.GetProperty("mask").GetInt32();
            bool cheat = k.GetProperty("cheat").GetInt32() != 0;
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var offer = TrainingHall.Check(c, mask, cheat, t!);
            var want = Refusal(k.GetProperty("refusal"));
            if (offer.Refusal != want)
                fails.Add($"case {n}: refusal {offer.Refusal} vs {want}");
            bool halted = false;
            if (!offer.Refused && k.GetProperty("answer").GetString() == "Y")
            {
                int pick = k.GetProperty("pick").GetInt32();
                var lists = k.GetProperty("lists").EnumerateArray()
                    .Select(l => l.EnumerateArray().Select(x => x.GetInt32()).ToList()).ToList();
                try
                {
                    TrainingHall.Train(c, offer.Train, cheat, t!, rng.Next, list =>
                    {
                        if (lists.Count == 0 || !list.SequenceEqual(lists[^1]))
                            fails.Add($"case {n}: learn list {string.Join(",", list)}");
                        return pick;
                    });
                    trained++;
                    if (lists.Count > 0 && lists[^1].Count == 0)
                        empty++;
                }
                catch (ProgramHaltException)
                {
                    halted = true;
                    halts++;
                }
            }
            if (halted != k.GetProperty("halt").GetBoolean())
                fails.Add($"case {n}: halt {halted}");
            if (!halted && rng.Seed != k.GetProperty("seedAfter").GetUInt32())
                fails.Add($"case {n}: RandSeed {rng.Seed:X8} vs {k.GetProperty("seedAfter").GetUInt32():X8}");
            var after = Convert.FromHexString(k.GetProperty("after").GetString()!);
            for (int i = 0; i < after.Length; i++)
                if (c.Record[i] != after[i])
                    fails.Add($"case {n} +{i:X2}: {c.Record[i]:X2} vs {after[i]:X2}");
            n++;
        }
        Assert.Equal(900, n);
        Assert.True(fails.Count == 0, string.Join("; ", fails.Take(30)));
        Assert.True(trained > 150, $"only {trained} level-ups");
        Assert.True(halts > 0 && empty > 0, $"halts {halts}, empty Learn lists {empty}");
    }
}
