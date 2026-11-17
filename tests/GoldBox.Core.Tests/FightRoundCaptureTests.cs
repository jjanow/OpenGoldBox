using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The start of round (GAME.OVR 0x127FB) and the first pick (0x9938) against DOSBox-X states taken at the first
/// player menu of rounds 0–4 of a goblin fight (fixtures <c>fight_rounds/</c>, private; README "fight_rounds"). The
/// seed before the round is the captured RandSeed stepped back by one 1d6 per creature in play and one 1d100 per
/// list member; replaying the round start and the pick forward from it must give every captured initiative and
/// movement, the creature whose menu was shown, and the captured RandSeed.
/// </summary>
public class FightRoundCaptureTests
{
    private static readonly string? Dir = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_rounds") : null;

    public sealed class RoundFactAttribute : TheoryAttribute
    {
        public RoundFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir))
                Skip = "needs the private DOSBox captures (tests/fixtures-private/por/dosbox-v1.3/fight_rounds)";
        }
    }

    /// <summary>Inverse of the RandSeed step (seed · 0x08088405 + 1).</summary>
    private static uint Back(uint seed)
    {
        uint inv = TurboPascalRandom.Multiplier; // Newton: inv = inv · (2 − m · inv), 5 steps for 32 bits
        for (int i = 0; i < 5; i++)
            inv = unchecked(inv * (2 - TurboPascalRandom.Multiplier * inv));
        return unchecked((seed - 1) * inv);
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s);

    [RoundFact]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Round_start_and_first_pick_match_DOSBox(int round)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, $"round_{round}.json")));
        var root = doc.RootElement;
        var creatures = root.GetProperty("creatures").EnumerateArray()
            .Select(c => (Record: Hex(c.GetProperty("record").GetString()!), Combat: Hex(c.GetProperty("combat").GetString()!)))
            .ToList();
        uint captured = root.GetProperty("randseed").GetUInt32();

        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        foreach (var (rec, _) in creatures)
            memory.Party.Members.Add(new EclCharacter(rec));
        var list = memory.Party.Members;
        memory.Write(FightRules.SurpriseCell, root.GetProperty("surprise_6dcb").GetUInt16());

        uint seed = captured;
        int draws = list.Count + list.Count(c => c[FightRules.InPlay] != 0);
        for (int i = 0; i < draws; i++)
            seed = Back(seed);
        var rng = new TurboPascalRandom(seed);

        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), rng.Next, new EndActionsController(), new GridGeometry());
        fight.State.RoundCounter = root.GetProperty("round_counter").GetByte();
        foreach (var c in list)
            FightRules.BeginRound(c, fight.Record(c), fight.State, fight.Items, memory, rng.Next, NoFightEffects.Instance);
        var picked = fight.NextCreature();

        Assert.Equal(captured, rng.Seed);
        Assert.Same(list[root.GetProperty("current_index").GetInt32()], picked);
        for (int i = 0; i < list.Count; i++)
        {
            var cap = creatures[i].Combat;
            Assert.Equal((sbyte)cap[CombatRecord.Initiative], fight.Record(list[i]).InitiativeValue);
            Assert.Equal(cap[CombatRecord.Movement], fight.Record(list[i])[CombatRecord.Movement]);
            Assert.Equal(creatures[i].Record[0x114], list[i][0x114]);
        }
    }
}
