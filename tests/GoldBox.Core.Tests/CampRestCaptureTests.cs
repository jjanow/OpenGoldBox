using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The <c>rest_interrupted</c> capture (fixtures README): save A's party in camp at 10:50 ($6DD2 = $6DD3 = 24 after
/// vector 2), then R, H, I×8, R. DOSBox: the rest broke after 4 hours (clock 14:50) and vector 3 ran. Replayed with
/// <see cref="CampRest"/> (the rest loop 0x24A66) on the game's generator, then vector 3 in the VM.
/// </summary>
public class CampRestCaptureTests
{
    /// <summary>Answers the rest-time menu with H, I×8, R; never stops; counts the messages.</summary>
    private sealed class Ui : ICampRestUi
    {
        public int Shown, Healed, Interruptions;
        public bool ChooseRestTime(RestTimeMenu menu)
        {
            menu.Press(RestTimeMenu.Key.Hours);
            for (int i = 0; i < 8; i++)
                menu.Press(RestTimeMenu.Key.Inc);
            Assert.Equal((0, 8, 0), menu.Shown);
            return menu.Press(RestTimeMenu.Key.Rest) == true;
        }
        public bool StopAsked() => false;
        public void ShowRestTime() => Shown++;
        public void PartyHealed(bool anyone) => Healed++;
        public void Memorised(EclCharacter c, int spell) { }
        public void Scribed(EclCharacter c, int spell) { }
        public void Interrupted() => Interruptions++;
    }

    [CaptureFact]
    public void Eight_hour_rest_is_interrupted_after_four_hours_like_dosbox()
    {
        var dir = PorData.Open();
        var before = new DosboxVmState(Path.Combine(Captures.Dir!, "rest_interrupted", "before"));
        var after = new DosboxVmState(Path.Combine(Captures.Dir!, "rest_interrupted", "after"));
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var player = new PorPlayer(host, new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), new ScriptedAreaUi());
        var party = PorParty.Load(PorSaveGame.Open(dir.Find("SAVGAMA.DAT")!), dir.Root);
        var g = PorGame.Restore(dir, PorViewArt.Load(dir), player, before.RandSeed, vm => before.ApplyTo(vm),
            new AreaMap(before.GeoPlanes!, before.Area4900[0x49C5 - 0x4900]), party);
        Assert.Equal(24, g.Memory.Read(CampRest.SafeTicksCell));

        var table = PorSpellData.Spells(PorProgram.Open(dir)!)!;
        var items = BaseItemTable.FromItemsFile(File.ReadAllBytes(dir.Find("ITEMS")!));
        var effects = new EffectHandlers(n => g.Rng.Next(n)) { List = () => party.Members };
        var spells = new CampSpells(table, it => items[it[0x2E], BaseItemTable.Slot] is > 10 and < 14);
        var state = new CampState { Camping = true };
        var rest = new CampRest(g.Memory, state, spells, g.Rng.Next, (c, e) => effects.RemoveEffect(c, e[0], e),
            c => effects.RestoreHitPoints(c, 1, false));
        var ui = new Ui();

        Assert.True(rest.RestForPreparation(ui));
        Assert.Equal(1, ui.Interruptions);
        Assert.Equal(0, ui.Healed);
        Assert.Equal(48 / 5, ui.Shown);
        Assert.Equal("14:50", GameClock.Format(g.Memory));
        Assert.Equal(0, state.IntervalTicks);
        Assert.All(state.RestTime, w => Assert.Equal(0, w));                 // cleared after the rest (0x17FAB)

        g.Vm.RunVector(3);                                                    // the camp routine's vector 3
        Assert.Equal(after.RandSeed, g.Rng.Seed);
        var bad = new List<string>();
        for (int i = 0; i < 1024; i++)
            if (g.Memory.Cells4900[i] != after.Area4900[i] && i != 0x4AFF - 0x4900)
                bad.Add($"${0x4900 + i:X4} {g.Memory.Cells4900[i]} vs {after.Area4900[i]}");
        for (int i = 0; i < 1024; i++)
            if (g.Memory.Cells6B00[i] != after.Area6B00[i])
                bad.Add($"${0x6B00 + i:X4} {g.Memory.Cells6B00[i]} vs {after.Area6B00[i]}");
        Assert.Empty(bad);
    }
}
