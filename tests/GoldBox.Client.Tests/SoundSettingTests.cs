using GoldBox.Client.Audio;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Sound;
using GoldBox.Client.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>The sound setting (<c>--sound</c>, settings key <c>Sound</c>) and the Client's own sound calls.</summary>
public class SoundSettingTests
{
    [Fact]
    public void The_sound_option_parses_and_refuses_unknown_words()
    {
        Assert.Equal("tandy", ClientOptions.Parse(["--sound", "Tandy"]).Sound);
        Assert.Equal("amiga", ClientOptions.Parse(["--sound", "Amiga"]).Sound);
        Assert.Equal("off", ClientOptions.Parse(["--sound", "off"]).Sound);
        Assert.Null(ClientOptions.Parse([]).Sound);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--sound", "adlib"]));
    }

    [Fact]
    public void The_setting_names_a_device_or_off()
    {
        Assert.Equal(ClientSoundDevice.Speaker, ClientAudio.Parse("speaker"));
        Assert.Equal(ClientSoundDevice.Tandy, ClientAudio.Parse("tandy"));
        Assert.Equal(ClientSoundDevice.Amiga, ClientAudio.Parse("amiga"));
        Assert.Null(ClientAudio.Parse("off"));
        Assert.Null(ClientAudio.Parse(null));
        Assert.Equal("speaker", new ClientSettings().Sound);
    }

    [Fact]
    public void Sound_control_saves_the_device_and_Ctrl_S_restores_it_without_saving()
    {
        var settings = new ClientSettings();
        var modes = new List<ClientSoundDevice?>();
        int saves = 0;
        var sound = new SoundSetting(settings, modes.Add, () => saves++);
        Assert.Equal("SOUND: PC SPEAKER", sound.Row);

        sound.Step(forward: true);
        Assert.Equal("tandy", settings.Sound);
        Assert.Equal("SOUND: TANDY", sound.Row);
        sound.ToggleMute();
        Assert.Equal("SOUND: OFF", sound.Row);
        sound.ToggleMute();
        Assert.Equal("SOUND: TANDY", sound.Row);
        Assert.Equal("tandy", settings.Sound);
        Assert.Equal(1, saves);

        sound.Step(forward: true);
        Assert.Equal("off", settings.Sound);
        sound.ToggleMute();
        Assert.Equal("SOUND: OFF", sound.Row);
        sound.Step(forward: false);
        Assert.Equal("tandy", settings.Sound);
        Assert.Equal(3, saves);
        Assert.Equal([ClientSoundDevice.Tandy, null, ClientSoundDevice.Tandy, null, ClientSoundDevice.Tandy], modes);
    }

    [Fact]
    public void Ctrl_S_toggles_at_the_first_key_read_of_the_buffer()
    {
        var input = new ActionInput(InputBindings.FromJson("{}"));
        int toggles = 0;
        input.ToggleSound = () => toggles++;

        void Frame(bool read, params Keys[] keys)
        {
            input.Update(new KeyboardState(keys), default, default, TimeSpan.Zero, (Point _) => null);
            input.ReadingKeys(read);
        }

        Frame(false, Keys.LeftControl, Keys.S);
        Assert.Equal(0, toggles);
        Frame(false);
        Frame(true, Keys.Enter);
        Assert.Equal(1, toggles);
        Assert.True(input.Pressed(GameAction.Confirm));

        Frame(false, Keys.A);
        Frame(false);
        Frame(true, Keys.LeftControl, Keys.S);
        Assert.Equal(1, toggles); // later queued keys are read directly by the DOS drain loop
        Frame(true);
        Frame(true, Keys.LeftControl, Keys.S);
        Assert.Equal(2, toggles);
        Assert.False(input.Pressed(GameAction.Search));
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
