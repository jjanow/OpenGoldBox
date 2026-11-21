using GoldBox.Client.Play;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Sound;
using GoldBox.Data.Por;

namespace GoldBox.Client.Tests;

/// <summary>
/// ECL sound in a loaded game's vector 4 (sound.md "D (s39): ECL sound in a load's vector 4"): the party menu's Load and
/// a load at the start (<c>--save</c>) both run vector 4 with the Client's sound attached, so both cue the step tune and
/// tune 10 (DOSBox-X s39 at the party menu; the Client's null sink s42: tune 9, then tune 10 110 ms later, on a party
/// menu Load). Skipped without GOLDBOX_POR_DIR.
/// </summary>
public class LoadSoundTests
{
    /// <summary>Save A with vectors 0–3 → EXIT and vector 4 = SAVE 8 → $03DE, CALL $BA03, SAVE 10 → $03DE, CALL $BA03.</summary>
    private static string WriteSave(PorGameDirectory dir, DirectoryInfo folder)
    {
        var bytes = File.ReadAllBytes(dir.Find("SAVGAMA.DAT")!);
        byte[] code = Convert.FromHexString("09000801DE032D0203BA09000A01DE032D0203BA00");
        var area = new List<byte>();
        foreach (ushort v in new ushort[] { 0x991F, 0x991F, 0x991F, 0x991F, 0x9920 })
            area.AddRange([1, 1, (byte)v, (byte)(v >> 8)]);
        area.AddRange(new byte[0x1F - 0x14]);
        area.Add(0);                                     // $991F: EXIT
        area.AddRange(code);                             // $9920
        Array.Clear(bytes, PorSaveGame.OffScript, 7680);
        area.CopyTo(bytes, PorSaveGame.OffScript);
        foreach (var f in Directory.GetFiles(dir.Root, "CHRDATA*"))
            File.Copy(f, Path.Combine(folder.FullName, Path.GetFileName(f)));
        var path = Path.Combine(folder.FullName, "SAVGAMB.DAT");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_loaded_games_vector_4_sound_plays_on_both_loads(bool partyMenu)
    {
        if (TestRig.PorDir() is not { } d)
            return;
        var dir = new PorGameDirectory(d);
        var folder = Directory.CreateTempSubdirectory("load-sound-");
        try
        {
            var path = WriteSave(dir, folder);
            var runner = new ScriptRunner();
            PorGame? game = null;
            var log = new SoundLog();
            var player = TestRig.Player(runner, () => game!.Memory);
            player.Sound = log;
            player.Game = () => game;
            var who = new PorPlayer(player, player, player, player);
            void Started(PorGame g)
            {
                game = g;
                g.Loop.Sound = player.Sound;                 // Program.cs's WithSound
            }
            runner.Start(() =>
            {
                game = partyMenu
                    ? PorGame.LoadAtPartyMenu(dir, PorViewArt.Load(dir), path, who, 1, started: Started)
                    : PorGame.FromSave(dir, PorViewArt.Load(dir), path, who, 1, started: Started);
            });
            for (int guard = 0; runner.Pending is not null && guard < 50; guard++)
                runner.Answer(runner.Pending is ChoiceRequest ? 0 : null);
            Assert.NotEqual(0, game!.LoadedInterfaceState);
            Assert.Equal(["Step", "Fireball"], log.Calls);
            runner.Dispose();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
