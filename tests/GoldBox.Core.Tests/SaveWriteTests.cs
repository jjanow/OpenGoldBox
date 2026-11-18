using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
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
        // Location type and $49FC come back from DS:49FA / DS:49E2, which ApplyTo restored (the program's own load/save).
        Assert.Equal(save.LocationType, memory.ViewMode);
        var written = PorSaveGame.Build(memory, party.Members.Count, letter, save.InterfaceState);
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

    /// <summary>
    /// The party menu's Save before Begin (0x19D9C → 0x1FD3E): <c>saves/unstarted/SAVGAMC.DAT</c> was written by the
    /// program in DOSBox-X with one roster character added; the engine's unstarted save of that party gives the same
    /// bytes (but the leftover memory after the stem), and loading it at the party menu leaves the menu up.
    /// </summary>
    [CaptureFact]
    public void Party_menu_save_before_begin_matches_the_program()
    {
        var dir = Path.Combine(Captures.Dir!, "saves", "unstarted");
        var original = File.ReadAllBytes(Path.Combine(dir, "SAVGAMC.DAT"));
        var save = new PorSaveGame(original);
        var party = PorParty.Load(save, dir);
        Assert.Single(party.Members);
        var content = PorData.Open();
        var doc = PorGame.UnstartedSave(content, party, 'C');
        var written = GoldBox.Data.Module.Por.PorEngineSave.ToSavgam(doc, 'C');
        for (int i = 0; i < PorSaveGame.Size; i++)
        {
            if (i >= PorSaveGame.OffNames)
            {
                int slot = (i - PorSaveGame.OffNames) / 41, pos = (i - PorSaveGame.OffNames) % 41;
                if (slot >= save.PartySize || pos > original[PorSaveGame.OffNames + 41 * slot])
                    continue;
            }
            Assert.True(original[i] == written[i], $"byte {i}: {original[i]} vs {written[i]}");
        }
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "CHRDATC1.SAV")),
            GoldBox.Data.Module.Por.PorEngineSave.Character(doc.Party![0]).Record);

        var tmp = Directory.CreateTempSubdirectory("goldbox-save-");
        try
        {
            var path = Path.Combine(tmp.FullName, "c.json");
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, doc);
            var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
                new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), new ScriptedAreaUi());
            var g = PorGame.LoadAtPartyMenu(content, PorViewArt.Load(content), path, player, 1);
            Assert.Equal(0, g.LoadedInterfaceState);
            Assert.Equal(0, g.Vm.CurrentScript);
            g.RunNewGameEntry();
            Assert.Equal((15, 1, 6), (g.Memory.Position.X, g.Memory.Position.Y, g.Memory.Position.Facing));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A save with interface state 0 that is not a new game (e.g. the training hall's): loaded at the party menu it stays
    /// there, and the menu's Save writes the loaded memory back (DOSBox-X s20: equal but for the stem), not a new game.
    /// Save A with its interface byte set to 0 stands in for one.
    /// </summary>
    [PorFact]
    public void Party_menu_save_after_a_menu_load_keeps_the_loaded_game()
    {
        var content = PorData.Open();
        var bytes = File.ReadAllBytes(content.Find("SAVGAMA.DAT")!);
        bytes[PorSaveGame.OffInterface] = 0;
        var doc = GoldBox.Data.Module.Por.PorEngineSave.FromFolders(bytes, "SAVGAMA.DAT",
            GoldBox.Data.Module.Por.PorEngineSave.Identity(content), content.LoadBlock, [content.Root]);
        var tmp = Directory.CreateTempSubdirectory("goldbox-save-");
        try
        {
            var path = Path.Combine(tmp.FullName, "a.json");
            GoldBox.Data.Module.Por.PorEngineSave.Write(path, doc);
            var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
                new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), new ScriptedAreaUi());
            var g = PorGame.LoadAtPartyMenu(content, PorViewArt.Load(content), path, player, 1);
            Assert.Equal(0, g.LoadedInterfaceState);
            var again = GoldBox.Data.Module.Por.PorEngineSave.ToSavgam(g.SaveAtPartyMenu(g.Memory.Party!, 'B'), 'B');
            var before = new PorSaveGame(bytes);
            for (int i = 0; i < PorSaveGame.OffNames; i++)
                Assert.True(bytes[i] == again[i], $"byte {i}: {bytes[i]} vs {again[i]}");
            Assert.Equal(before.PartySize, again[PorSaveGame.OffPartySize]);
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

    [Fact]
    public void Location_type_is_the_view_mode_and_the_speed_goes_to_49fc()
    {
        var m = new EclMemory { ViewMode = 3, GameSpeed = 7 };
        m.Write(0x49FC, 2); // stale until a save, as in the program
        var d = PorSaveGame.Build(m, 1, 'A', 2);
        Assert.Equal(3, d[PorSaveGame.OffLocationType]);
        Assert.Equal(7, m.Read(0x49FC));
        var back = new EclMemory();
        new PorSaveGame(d).ApplyTo(back);
        Assert.Equal(3, back.ViewMode);
        Assert.Equal(7, back.GameSpeed);
    }
}
