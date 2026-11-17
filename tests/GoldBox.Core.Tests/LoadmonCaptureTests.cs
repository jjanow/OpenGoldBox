using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The <c>loadmon_combat</c> capture (fixtures README): from camp, the interrupted rest's encounter menu, Advance,
/// Combat; the script runs CLRMON, one LOADMON and COMBAT, and the "after" state is the fight screen. The combat
/// list (DS:5D96) of the "after" state is in <c>after/combat_list.json</c>. The replay stops at the fight and compares
/// the monster records LOADMON made (icon slot, size, the five 1d6 colour bytes), the counters and the order.
/// </summary>
public class LoadmonCaptureTests
{
    private sealed class StopAtFight : ICombatRunner
    {
        public void Fight(EncounterState state) => throw new EclAwaitingInput("fight");
        public void Loot(EncounterState state, AfterCombatOutcome outcome) { }
        public void PartyDestroyed(EncounterState state) { }
        public void Shop(EncounterState state) { }
        public void Temple(EncounterState state) { }
    }

    private sealed class SeedUi(TurboPascalRandom rng, Queue<ushort> choices) : IEncounterUi
    {
        public List<uint> SeedAtMenu { get; } = [];
        public void ShowEncounter(byte sprite, byte picture, int distance) { }
        public void ShowPicture(byte picture, byte? head) { }
        public void RestoreView() { }
        public int ChooseEncounterAction(EncounterMenuKind kind, string? text, int distance)
        {
            SeedAtMenu.Add(rng.Seed);
            return choices.TryDequeue(out var c) ? c : throw new EclAwaitingInput("menu");
        }
        public void ShowEncounterMessage(EncounterMessage message) { }
        public int ChooseParleyAttitude() => 0;
        public void LoadCombatIcon(byte picture, int slot) { }
    }

    private static JsonElement List(string folder) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "combat_list.json"))).RootElement;

    [CaptureFact]
    public void Loadmon_records_match_the_fight_state()
    {
        var dir = Path.Combine(Captures.Dir!, "loadmon_combat");
        var game = PorData.Open();
        var before = new DosboxVmState(Path.Combine(dir, "before"));
        var menu = new DosboxVmState(Path.Combine(dir, "menu"));
        var after = List(Path.Combine(dir, "after"));

        // The rest loop's program draws before vector 3 are not modelled: pick the count whose run reaches the
        // second menu (after Advance) with the captured "menu" RandSeed.
        for (int draws = 0; draws <= 5; draws++)
        {
            var memory = new EclMemory { Party = PorParty.Load(PorSaveGame.Open(game.Find("SAVGAMA.DAT")!), game.Root) };
            var rng = new TurboPascalRandom(before.RandSeed);
            rng.Skip(draws);
            var inner = new ScriptedEclHost { Rng = rng, StopWhenInputMissing = true };
            var ui = new SeedUi(rng, new Queue<ushort>([3, 0])); // advance, then combat
            var host = new EncounterHost(memory, inner, ui, new StopAtFight(), new PorEncounterData(game))
            {
                Map = new MapPlanes(before.GeoPlanes!),
            };
            var vm = new EclVm(memory, host) { Scripts = game };
            host.CurrentBlock = () => vm.CurrentScript;
            before.ApplyTo(vm);
            var r = vm.RunVector(3);
            if (ui.SeedAtMenu.Count < 2 || ui.SeedAtMenu[1] != menu.RandSeed)
                continue;

            Assert.Equal(EclStopReason.AwaitingInput, r.Reason); // stopped in the fight, like the capture
            var s = host.State;
            Assert.Equal(after.GetProperty("ds4435").GetInt32(), s.MonsterCount);
            Assert.Equal(after.GetProperty("ds6f83").GetInt32(), s.IconSlot);
            Assert.Equal(after.GetProperty("ds84e7").GetInt32() != 0, s.MonstersLoaded);
            var records = after.GetProperty("records").EnumerateArray().ToList();
            Assert.Equal(records.Count, memory.Party!.Members.Count); // party, then the monsters
            for (int i = 0; i < records.Count; i++)
            {
                var want = records[i];
                var got = memory.Party.Members[i];
                if (i < 6)
                    continue; // party records: not touched by LOADMON
                Assert.Equal(want.GetProperty("bf").GetInt32(), got[0xBF]);
                Assert.Equal(want.GetProperty("c0").GetInt32(), got[0xC0]);
                Assert.Equal(want.GetProperty("c1_c6").EnumerateArray().Select(e => e.GetInt32()),
                    Enumerable.Range(0xC1, 6).Select(o => (int)got[o]));
            }
            return;
        }
        Assert.Fail("no rest draw count reaches the captured menu RandSeed");
    }
}
