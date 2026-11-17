using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// The <c>party_opcodes</c> capture (fixtures README, s11): save A patched with test code in vector 4 (fixture
/// <c>saves/SAVGAMH.DAT</c>), loaded from the main menu: LOADCHAR #0, money 10/5, ROB #0, #10, #0, SPELL #40 into
/// $00B8/$00B9, CHKPARTY of movement, DAMAGE 1 to the current character, LOADCHAR #1, SAVE #0 to $6B00/$6C00,
/// LOADCHAR #0x81, $6E3E to $9706, EXIT. The party list of the "after" state is in <c>after/vars.json</c>.
/// </summary>
public class PartyOpcodesCaptureTests
{
    [CaptureFact]
    public void Party_opcodes_replay_matches_the_dosbox_state()
    {
        var dir = Path.Combine(Captures.Dir!, "party_opcodes");
        var game = PorData.Open();
        var before = new DosboxVmState(Path.Combine(dir, "before"));
        var after = new DosboxVmState(Path.Combine(dir, "after"));
        var save = PorSaveGame.Open(Path.Combine(Captures.Dir!, "saves", "SAVGAMH.DAT"));
        var party = PorParty.Load(save, game.Root);
        var memory = new EclMemory { Party = party };
        var rng = new TurboPascalRandom(before.RandSeed);
        var host = new ScriptedEclHost { Rng = rng };
        var vm = new EclVm(memory, host);
        // SPELL's uninitialised "found" byte was non-zero here (results 1 and 0): the policy default.

        var r = save.LoadGame(vm);

        Assert.Equal(EclStopReason.Exit, r.Reason);
        Assert.Equal(after.RandSeed, rng.Seed); // 4 draws: two item rolls (ROB), 1d1 and the target 1d6 (DAMAGE)
        // Every cell of the four areas, except those the program owns (clock, wall-set slot), as ecl-compare does.
        var diffs = new List<string>();
        void Area(ushort[] dosbox, ushort[] vm, int baseAddr)
        {
            for (int i = 0; i < dosbox.Length; i++)
            {
                int a = baseAddr + i;
                if (a is >= 0x49C6 and <= 0x49CC or 0x4AFF)
                    continue;
                if (dosbox[i] != vm[i])
                    diffs.Add($"${a:X4}: DOSBox {dosbox[i]}, VM {vm[i]}");
            }
        }
        Area(after.Area4900, memory.Cells4900.ToArray(), 0x4900);
        Area(after.Area6B00, memory.Cells6B00.ToArray(), 0x6B00);
        Area(after.Area9700, memory.Cells9700.ToArray(), 0x9700);
        for (int i = 0; i < after.Script.Length; i++)
            if (after.Script[i] != memory.ScriptArea[i])
                diffs.Add($"script ${0x9900 + i:X4}");
        Assert.Empty(diffs);
        for (int i = 0; i < 8; i++)
            Assert.Equal(after.Area9700[i], memory.Read((ushort)(0x9700 + i)));
        Assert.Equal(after.Area6B00[0x6E3E - 0x6B00], memory.Read(0x6E3E));
        Assert.Equal(after.Var("ds_84ce_b8"), memory.SpecialWriteValues[0x00B8]);
        Assert.Equal(after.Var("ds_84d0_b9"), memory.SpecialWriteValues[0x00B9]);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "after", "vars.json")));
        var list = doc.RootElement.GetProperty("party").EnumerateArray().ToList();
        Assert.Equal(list.Count, party.Members.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var m = party.Members[i];
            Assert.Equal(list[i].GetProperty("name").GetString(), m.Name);
            Assert.Equal(list[i].GetProperty("hp").GetInt32(), m[EclPartyRules.Hp]);
            Assert.Equal(list[i].GetProperty("items").GetInt32(), m.Items.Count);
            var money = list[i].GetProperty("money").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Assert.Equal(money, Enumerable.Range(0, 7).Select(k => (int)m.Word(EclPartyRules.Money + 2 * k)));
        }
        // EXIT restored DS:4436, which after a Load still points to no party record: no current character.
        Assert.DoesNotContain(list, p => p.GetProperty("current").GetBoolean());
        Assert.Null(party.Current);
    }
}
