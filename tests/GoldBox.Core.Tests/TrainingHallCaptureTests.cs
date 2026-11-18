using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The thieves' training hall in DOSBox-X (private fixture <c>dosbox-v1.3/training_hall/training.json</c>, s19): the
/// party of save A in civilised Phlan, the hall's mask 116 ($6DA8). Train on the dwarf fighter is refused (wrong class),
/// yet his experience 5000 is cut to 4000 (the fighter's level-3 threshold − 1) before the refusal, with no draw. Train
/// on the halfling thief (3000 XP, 4 of 6 HP, a mixed purse) pays 1000 gold from the small coins up with change, raises
/// the thief to level 2, caps the experience at 2500 and rolls 1d6: record and RandSeed as the program left them. In
/// the magic-users' hall (mask 113) a level-1 magic-user rises to 2, pays 200 platinum and learns the spell picked from
/// the Learn list (20).
/// </summary>
public class TrainingHallCaptureTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "training_hall", "training.json") : null;

    public sealed class CaptureFactAttribute : FactAttribute
    {
        public CaptureFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture dosbox-v1.3/training_hall";
        }
    }

    [CaptureFact]
    public void Train_matches_DOSBox()
    {
        var t = PorTrainingTables.From(PorProgramData.Open(PorData.Open()))!;
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = k.GetProperty("name").GetString()!;
            var c = new EclCharacter(Convert.FromHexString(k.GetProperty("before").GetString()!));
            var rng = new TurboPascalRandom(k.GetProperty("seed").GetUInt32());
            var offer = TrainingHall.Check(c, (ushort)k.GetProperty("mask").GetInt32(), false, t);
            var refusal = k.GetProperty("refusal");
            if (refusal.ValueKind == JsonValueKind.String)
                Assert.Equal(Enum.Parse<TrainingRefusal>(refusal.GetString()!), offer.Refusal);
            else
            {
                Assert.False(offer.Refused, name);
                int pick = k.TryGetProperty("pick", out var p) ? p.GetInt32() : 0;
                TrainingHall.Train(c, offer.Train, false, t, rng.Next, list =>
                {
                    Assert.Contains(pick, list);
                    return pick;
                });
            }
            Assert.Equal(k.GetProperty("seedAfter").GetUInt32(), rng.Seed);
            Assert.Equal(k.GetProperty("after").GetString(), Convert.ToHexString(c.Record).ToLowerInvariant());
        }
    }
}
