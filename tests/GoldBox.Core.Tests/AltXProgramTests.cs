using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The fight's Alt-X debugging switch 0x1638E against the program itself: fixtures made by running the v1.3 routine
/// under emulation (<c>tools/scripts/por_altx_oracle.py</c>, private fixture <c>program-v1.3/altx.json</c>) on made-up
/// creature lists, combat records and slots, with ParamStr(1) the program's word or a variant of it. Core runs the menu's
/// Alt-X (<see cref="FightMenu.Perform"/> with <see cref="FightMenu.CheatEnabled"/> as the word test) and the result,
/// records, combat records, slot sizes, slot count and the cell grid are compared. Where a hostile creature has no slot
/// the program writes the slot count to 0 (an original bug); the engine keeps it (fixed, user s51), so those cases
/// expect the count as it was and check that the program's was 0.
/// </summary>
public class AltXProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "altx.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/altx.json";
            else if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static readonly Lazy<CombatTables> Tables = new(() => PorCombatTables.Open(PorData.Open())!);

    /// <summary>Record bytes the program keeps pointers in (effects, items, list link, combat record).</summary>
    private static bool PointerByte(int b) => b is >= 0x7F and < 0x83 or >= 0xC8 and < 0x10C;

    /// <summary>The menu's chooser: only the notices are reached (the message and the refusal).</summary>
    private sealed class Chooser : IFightMenuChooser
    {
        public List<FightNotice> Notices { get; } = [];
        public FightCommand Choose(FightMenu menu, IReadOnlyList<FightCommand> entries) => throw new InvalidOperationException();
        public DoneCommand ChooseDone(FightMenu menu, IReadOnlyList<DoneCommand> entries) => throw new InvalidOperationException();
        public MoveInput ChooseMove(FightMenu menu, int movementLeft) => throw new InvalidOperationException();
        public AimKey ChooseAim(FightMenu menu, AimView view) => throw new InvalidOperationException();
        public bool ConfirmFlee(FightMenu menu) => throw new InvalidOperationException();
        public bool ConfirmAttackAlly(FightMenu menu, EclCharacter ally) => throw new InvalidOperationException();
        public int? ChooseSpell(FightMenu menu, IReadOnlyList<int> castable) => throw new InvalidOperationException();
        public void Notice(FightNotice notice, EclCharacter? who) => Notices.Add(notice);
    }

    [OracleFact]
    public void Alt_X_matches_the_program()
    {
        var root = JsonDocument.Parse(File.ReadAllText(File_!)).RootElement;
        var fails = new List<string>();
        int n = 0, accepted = 0, fixedCases = 0;
        foreach (var k in root.GetProperty("cases").EnumerateArray())
        {
            var list = k.GetProperty("creatures").EnumerateArray()
                .Select(c => new EclCharacter(Convert.FromHexString(c.GetString()!))).ToList();
            var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
            memory.Party.Members.AddRange(list);
            var map = new CombatMap(Tables.Value);
            int slot = 0, i = 0;
            foreach (var pl in k.GetProperty("places").EnumerateArray())
            {
                if (pl.ValueKind != JsonValueKind.Null)
                {
                    slot++;
                    map.SetSlotCreature(slot, list[i]);
                    map.SetSlotPosition(slot, pl[0].GetInt32(), pl[1].GetInt32());
                    map.SetSlotSize(slot, pl[2].GetInt32());
                }
                i++;
            }
            map.SlotCount = slot + 1;
            map.Rebuild();
            int slotCount = map.SlotCount;
            // A hostile creature (0x10E = 1) with no slot: the program writes the slot count to 0 there, the engine
            // skips it (original bug fixed, user s51; combat-menu.md "0x1638E, every branch").
            bool slotlessHostile = k.GetProperty("places").EnumerateArray()
                .Select((pl, x) => pl.ValueKind == JsonValueKind.Null && list[x][FightRules.Hostile] == 1).Any(b => b);
            var rng = new TurboPascalRandom(1);
            var fight = new Fight(memory, new EncounterState(), new BaseItemTable(new byte[16 * 128]), rng.Next,
                new EndActionsController(), map, new EffectHandlers(rng.Next));
            i = 0;
            foreach (var cb in k.GetProperty("combat").EnumerateArray())
                Convert.FromHexString(cb.GetString()!).CopyTo(fight.Record(list[i++]).Bytes, 0);

            var o = k.GetProperty("out");
            bool word = k.GetProperty("param").GetString() == "word";
            var chooser = new Chooser();
            bool done = new FightMenu(fight, list[0], chooser) { CheatEnabled = word }.Perform(FightCommand.GodsIntervene);
            string what = $"case {n} ({k.GetProperty("param").GetString()})";
            // the program's message (START 0x2233) once when it works; the menu's refusal (0x9B0F) otherwise
            bool said = o.GetProperty("messages").GetInt32() == 1;
            if (chooser.Notices.Count != 1 || said != (chooser.Notices[0] == FightNotice.GodsIntervene))
                fails.Add($"{what}: notices {string.Join(",", chooser.Notices)}, program messages {o.GetProperty("messages").GetInt32()}");
            if ((o.GetProperty("result").GetInt32() == 1) != done)
                fails.Add($"{what}: done {done} vs result {o.GetProperty("result").GetInt32()}");
            if (done)
                accepted++;
            i = 0;
            foreach (var a in o.GetProperty("after").EnumerateArray())
            {
                var want = Convert.FromHexString(a.GetString()!);
                for (int b = 0; b < EclCharacter.RecordSize; b++)
                    if (!PointerByte(b) && want[b] != list[i][b])
                        fails.Add($"{what} creature {i} +{b:X2}: {list[i][b]} vs {want[b]}");
                i++;
            }
            i = 0;
            foreach (var a in o.GetProperty("combat").EnumerateArray())
            {
                var want = Convert.FromHexString(a.GetString()!);
                var got = fight.Record(list[i]).Bytes;
                for (int b = 0; b < CombatRecord.Size; b++)
                    if (b is < 0x0A or > 0x0D && want[b] != got[b]) // +0x0A: a far pointer (made-up bytes, not used here)
                        fails.Add($"{what} combat {i} +{b:X2}: {got[b]} vs {want[b]}");
                i++;
            }
            int j = 1;
            foreach (var s in o.GetProperty("sizes").EnumerateArray())
            {
                if (map.SlotSize(j) != s.GetInt32())
                    fails.Add($"{what}: slot {j} size {map.SlotSize(j)} vs {s.GetInt32()}");
                j++;
            }
            int programCount = o.GetProperty("slotCount").GetInt32();
            if (done && slotlessHostile)
            {
                fixedCases++;
                if (programCount != 0)
                    fails.Add($"{what}: the program's slot count {programCount}, expected its 0 for a slotless hostile");
                if (map.SlotCount != slotCount)
                    fails.Add($"{what}: slot count {map.SlotCount} vs kept {slotCount} (fixed)");
            }
            else if (map.SlotCount != programCount)
                fails.Add($"{what}: slot count {map.SlotCount} vs {programCount}");
            var grid = Convert.FromHexString(o.GetProperty("cells").GetString()!);
            for (int y = 0; y < 25; y++)
                for (int x = 0; x < 50; x++)
                    if (map.SlotAt(x, y) != grid[y * 50 + x])
                        fails.Add($"{what}: grid {x},{y} {map.SlotAt(x, y)} vs {grid[y * 50 + x]}");
            n++;
        }
        Assert.True(n >= 40, $"{n} cases");
        Assert.True(accepted >= 20, $"{accepted} accepted");
        Assert.True(fixedCases >= 3, $"{fixedCases} cases with a slotless hostile");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(40)));
    }
}
