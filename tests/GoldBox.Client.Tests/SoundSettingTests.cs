using GoldBox.Client.Audio;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Sound;

namespace GoldBox.Client.Tests;

/// <summary>The sound setting (<c>--sound</c>, settings key <c>Sound</c>) and the Client's own sound calls.</summary>
public class SoundSettingTests
{
    [Fact]
    public void The_sound_option_parses_and_refuses_unknown_words()
    {
        Assert.Equal("tandy", ClientOptions.Parse(["--sound", "Tandy"]).Sound);
        Assert.Equal("off", ClientOptions.Parse(["--sound", "off"]).Sound);
        Assert.Null(ClientOptions.Parse([]).Sound);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--sound", "adlib"]));
    }

    [Fact]
    public void The_setting_names_a_device_or_off()
    {
        Assert.Equal(PcSoundDevice.Speaker, ClientAudio.Parse("speaker"));
        Assert.Equal(PcSoundDevice.Tandy, ClientAudio.Parse("tandy"));
        Assert.Null(ClientAudio.Parse("off"));
        Assert.Null(ClientAudio.Parse(null));
        Assert.Equal("speaker", new ClientSettings().Sound);
    }

    [Fact]
    public void The_bump_and_ECL_CALL_BA03_play_their_cues()
    {
        var memory = new EclMemory();
        using var runner = new ScriptRunner();
        var player = new ClientPlayer(runner, () => memory);
        var log = new SoundLog();
        player.Sound = log;
        player.Bump();
        player.Call(0xBA03);                         // $03DE never written: no sound
        memory.SpecialWriteValues[0x03DE] = 8;
        player.Call(0xBA03);
        memory.SpecialWriteValues[0x03DE] = 10;
        player.Call(0xBA03);
        memory.SpecialWriteValues[0x03DE] = 3;
        player.Call(0xBA03);
        player.Call(0x2C90);
        Assert.Equal(["Impact", "Step", "Fireball"], log.Calls);
    }
}
