using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The AI (GAME.OVR 0xABCF) against the DOSBox-X all-Quick goblin fight (fixtures <c>fight_quick/</c>, README
/// "fight_quick"). <c>r0_menu6</c> is Brother Sean's menu in round 0; Q put him on Quick and <c>auto_01</c> was taken
/// 8 draws later, still in his turn: he had moved one square NE (28,14 → 29,13) and left goblin guard 8 (28,12)
/// dying with 1 point over. Replayed with the program's step table (START.EXE DS:02AE), items (ITEMS) and the real
/// combat map (the captured arena and slots, <see cref="CombatMap"/>, <see cref="FightMoves"/>). The search draw takes
/// goblin guard 7 at 26,12 (the target list 0x3045B is ordered by line distance, then direction); after the step it
/// is 3 squares away, so the line walk (0x2FF74) with range 1 stops past 2 × 1 + 1 half squares and the remembered
/// target is not in reach: the 8th draw is the 1d1 pick from the adjacent list (goblin guard 8), which is attacked.
/// </summary>
public class MonsterAiCaptureTests
{
    private static readonly string? Dir = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "fight_quick") : null;

    public sealed class QuickFactAttribute : FactAttribute
    {
        public QuickFactAttribute()
        {
            if (Dir is null || !Directory.Exists(Dir) || !PorData.Present)
                Skip = "needs the private DOSBox captures (fight_quick) and " + PorData.EnvVar;
        }
    }

    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);

    [QuickFact]
    public void Brother_Sean_on_Quick_steps_and_attacks_like_DOSBox()
    {
        var game = PorData.Open();
        var program = PorProgram.Open(game)!;
        var steps = program.AiStepOffsets()!;
        var items = BaseItemTable.FromItemsFile(File.ReadAllBytes(game.Find("ITEMS")!));

        using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, "r0_menu6", "fight.json")));
        using var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir!, "auto_01", "fight.json")));
        var root = before.RootElement;
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        var map = new CombatMap(PorCombatTables.Open(game)!);
        Hex(root.GetProperty("arena")).CopyTo(map.Arena.Bytes, 0);
        var rng = new TurboPascalRandom(root.GetProperty("vars").GetProperty("randseed").GetUInt32());
        var fight = new Fight(memory, new EncounterState(), items, rng.Next, new EndActionsController(), map) { Moves = new FightMoves(map) };
        map.State = fight.State;
        foreach (var e in root.GetProperty("creatures").EnumerateArray())
        {
            var c = new EclCharacter(Hex(e.GetProperty("record")));
            foreach (var it in e.GetProperty("items").EnumerateArray())
                c.Items.Add(Hex(it.GetProperty("record")));
            foreach (var ef in e.GetProperty("effects").EnumerateArray())
                c.Effects.Add(Hex(ef.GetProperty("record")));
            memory.Party.Members.Add(c);
            Hex(e.GetProperty("combat")).CopyTo(fight.Record(c).Bytes, 0);
        }
        var list = memory.Party.Members;
        int slots = 0;
        foreach (var l in root.GetProperty("locations_lee").EnumerateArray())
        {
            if (l.GetProperty("list_index").ValueKind != JsonValueKind.Number)
                continue;
            int k = l.GetProperty("slot").GetInt32();
            map.SetSlotCreature(k, list[l.GetProperty("list_index").GetInt32()]);
            map.SetSlotPosition(k, l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32());
            map.SetSlotSize(k, l.GetProperty("size").GetInt32());
            slots = k;
        }
        map.SlotCount = slots + 1;
        map.Rebuild();
        FightRules.CountTeams(list, fight.State);
        fight.State.HostileHpPercent = root.GetProperty("ds_bytes").GetProperty("6E2F").GetByte();

        var sean = list[3];
        var goblin = list[8];
        sean[CombatRules.AutoCombat] = 1; // Q (0xAA32)
        fight.State.Current = sean;
        new MonsterAi(steps).Turn(fight, sean);

        var cap = after.RootElement.GetProperty("creatures");
        Assert.Equal(after.RootElement.GetProperty("vars").GetProperty("randseed").GetUInt32(), rng.Seed);
        Assert.Equal((29, 13), map.Position(sean));
        var seanRec = Hex(cap[3].GetProperty("combat"));
        Assert.Equal(seanRec[MonsterAi.StepRow], fight.Record(sean)[MonsterAi.StepRow]);
        // auto_01 was taken inside the attack (Sean still current, initiative 4, +6 = 9 after the diagonal step):
        // the actions end (START 0x31D7) comes after it, so +6 and +3 are not compared.
        Assert.Equal(seanRec[CombatRecord.Facing], fight.Record(sean)[CombatRecord.Facing]);
        Assert.Same(goblin, fight.Record(sean).Other);
        var gobRec = Hex(cap[8].GetProperty("record"));
        var gobCombat = Hex(cap[8].GetProperty("combat"));
        Assert.Equal(gobRec[FightRules.Status], goblin[FightRules.Status]);
        Assert.Equal(gobRec[FightRules.Hp], goblin[FightRules.Hp]);
        Assert.Equal(gobCombat[CombatRecord.Dying], fight.Record(goblin)[CombatRecord.Dying]);
        // The weapon pick left Sean's equipment as it was.
        var seanItems = cap[3].GetProperty("items").EnumerateArray().Select(i => Hex(i.GetProperty("record"))).ToList();
        for (int i = 0; i < seanItems.Count; i++)
            Assert.Equal(seanItems[i][CombatRules.ItemEquipped], sean.Items[i][CombatRules.ItemEquipped]);
    }

    [PorFact]
    public void Step_table_is_found_in_START_EXE()
    {
        var program = PorProgram.Open(PorData.Open())!;
        Assert.Equal(0x02AE, program.AiStepOffsetsBase());
        var t = program.AiStepOffsets()!;
        for (int row = 1; row <= 6; row++)
            for (int a = 1; a <= 5; a++)
                Assert.InRange(t[5 * row + a], 1, 8);
    }
}
