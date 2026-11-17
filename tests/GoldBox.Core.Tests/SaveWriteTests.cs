using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The save writer (camp Save, GAME.OVR 0x1FD3E): a save read into VM memory and written back gives the same bytes,
/// except the leftover memory after each stem and in unused name slots; the member files come back byte for byte.
/// SAVGAMA ships with the game; SAVGAMB was written by the program in DOSBox-X (fixtures <c>saves/</c>).
/// </summary>
public class SaveWriteTests
{
    private static void RoundTrip(string savePath, char letter, string charFolder)
    {
        var original = File.ReadAllBytes(savePath);
        var save = new PorSaveGame(original);
        var memory = new EclMemory();
        save.ApplyTo(memory);
        var party = PorParty.Load(save, charFolder);
        var written = PorSaveGame.Build(memory, party.Members.Count, letter, save.LocationType, save.InterfaceState);
        Assert.Equal(PorSaveGame.Size, written.Length);
        for (int i = 0; i < PorSaveGame.Size; i++)
        {
            if (i >= PorSaveGame.OffNames)
            {
                int slot = (i - PorSaveGame.OffNames) / 41, pos = (i - PorSaveGame.OffNames) % 41;
                if (slot >= save.PartySize || pos > original[PorSaveGame.OffNames + 41 * slot])
                    continue;                                   // leftover memory in the program's file
            }
            Assert.True(original[i] == written[i], $"byte {i}: {original[i]} vs {written[i]}");
        }

        var tmp = Directory.CreateTempSubdirectory("goldbox-save-");
        try
        {
            PorParty.Save(party, tmp.FullName, letter);
            for (int n = 1; n <= party.Members.Count; n++)
                foreach (var ext in new[] { ".SAV", ".ITM", ".SPC" })
                {
                    var name = PorSaveGame.Stem(letter, n) + ext;
                    var src = Directory.EnumerateFiles(charFolder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                    var dst = Path.Combine(tmp.FullName, name);
                    Assert.Equal(src is not null, File.Exists(dst));
                    if (src is not null)
                        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dst));
                }
            var back = PorParty.Load(new PorSaveGame(written), tmp.FullName);
            Assert.Equal(party.Members.Count, back.Members.Count);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [PorFact]
    public void Shipped_save_a_round_trips()
    {
        var dir = PorData.Open();
        RoundTrip(dir.Find("SAVGAMA.DAT")!, 'A', dir.Root);
    }

    [CaptureFact]
    public void Program_written_save_b_round_trips()
    {
        var saves = Path.Combine(Captures.Dir!, "saves");
        RoundTrip(Path.Combine(saves, "SAVGAMB.DAT"), 'B', saves);
    }

    [Fact]
    public void Stems_and_the_disk_cell()
    {
        var m = new EclMemory { Disk = 3 };
        var d = PorSaveGame.Build(m, 2, 'c', 1, 2);
        Assert.Equal(3, d[0]);
        Assert.Equal(3, m.Read(0x6E12));
        Assert.Equal(2, d[PorSaveGame.OffPartySize]);
        Assert.Equal("CHRDATC2", new PorSaveGame(d).PartyFileStems[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PorSaveGame.Build(m, 1, 'K', 1, 2));
    }
}
