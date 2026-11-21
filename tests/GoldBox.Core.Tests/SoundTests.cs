using GoldBox.Core.Sound;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Module;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The sound path (docs/re/sound.md): the cue table and driver read from the program, the module round trip, and the
/// player's auto-stop / resident rules with the real driver data.
/// </summary>
public class SoundTests
{
    private static readonly Lazy<PorSound> Program = new(() => PorData.Open().Sound() ?? throw new InvalidOperationException("no sound"));

    [PorFact]
    public void The_cue_table_is_found_in_the_data_segment()
    {
        var ds = PorData.Open().Program()!.Ds;
        Assert.Equal(0x2612, PorSoundDriver.CueTable(ds));
        Assert.Equal([255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], Program.Value.CueWords);
        Assert.Equal(10, Program.Value.CueWords[(int)SoundCue.Step]);
        Assert.Equal(7, Program.Value.CueWords[(int)SoundCue.Impact]);
    }

    [PorFact]
    public void The_module_holds_the_same_driver_and_cues()
    {
        var m = PorModuleData.Open().Sound();
        Assert.True(PorModuleData.Report.Sound);
        Assert.NotNull(m);
        var a = Program.Value.Driver;
        var b = m!.Driver;
        Assert.Equal(a.Bytes, b.Bytes);
        Assert.Equal(a with { Bytes = b.Bytes }, b);
        Assert.Equal(Program.Value.CueWords, m.CueWords);
        Assert.True(File.Exists(Path.Combine(PorModuleData.Dir, PcSoundDocument.RelativePath)));
    }

    [Fact]
    public void A_document_with_an_unknown_driver_is_refused()
    {
        var d = new PcSoundDocument { Driver = "x", Data = new byte[64] };
        Assert.Throws<FormatException>(() => d.ToImage());
    }

    [PorFact]
    public void An_auto_stop_sound_lasts_until_the_second_BIOS_tick()
    {
        var p = Program.Value.Player(PcSoundDevice.Speaker);
        Assert.False(p.Installed);
        p.Cue(SoundCue.Step);
        Assert.True(p.Installed);
        p.Advance(1000);
        // The chain count starts at 13: 13 ticks to the first BIOS tick, 13 more to the second.
        Assert.Equal(26, p.Ticks);
        Assert.False(p.Installed);
        Assert.Equal(13, p.Driver.DivCount);
        p.Cue(SoundCue.Miss);
        p.Advance(1000);
        Assert.Equal(52, p.Ticks);
        Assert.Equal([10, 9], p.Commands);
    }

    [PorFact]
    public void A_cue_during_an_auto_stop_sound_waits_its_turn()
    {
        var p = Program.Value.Player(PcSoundDevice.Speaker);
        p.Cue(SoundCue.Impact);
        p.Advance(5);
        p.Cue(SoundCue.Miss);
        Assert.Equal([7], p.Commands);
        p.Advance(21);
        Assert.Equal([7, 9], p.Commands);
        Assert.True(p.Installed);
        p.Advance(1000);
        Assert.Equal(52, p.Ticks);
    }

    [PorFact]
    public void A_resident_bracket_runs_the_tune_until_it_closes()
    {
        var p = Program.Value.Player(PcSoundDevice.Speaker);
        p.BeginResident();
        p.Cue(SoundCue.LightningBolt); // speaker tune 7 never ends by itself
        p.Advance(500);
        Assert.Equal(500, p.Ticks);
        Assert.True(p.Busy);
        p.EndResident();
        Assert.False(p.Installed);
        p.Advance(10);
        Assert.Equal(500, p.Ticks);
    }

    [PorTheory]
    [InlineData(PcSoundDevice.Speaker)]
    [InlineData(PcSoundDevice.Tandy)]
    public void A_cue_renders_sound_and_silence_renders_none(PcSoundDevice device)
    {
        var p = Program.Value.Player(device);
        var buf = new float[6000];
        p.Render(buf);
        Assert.All(buf, s => Assert.Equal(0f, s));
        p.Cue(SoundCue.Impact);
        p.Render(buf);
        Assert.True(buf.Max(MathF.Abs) > 0.05f, $"peak {buf.Max(MathF.Abs)}");
        Assert.False(p.Installed); // 26 ticks ≈ 4,844 samples, inside the 6,000
    }
}

/// <summary>The rendered speaker against DOSBox-X's PC speaker (s19 capture, docs/re/sound.md §2.4).</summary>
public class SoundRenderTests
{
    private static (double Start, double End) Span(float[] b, int rate)
    {
        int s = Array.FindIndex(b, x => MathF.Abs(x) > 0.05f), e = Array.FindLastIndex(b, x => MathF.Abs(x) > 0.05f);
        return (s * 1000.0 / rate, e * 1000.0 / rate);
    }

    /// <summary>
    /// DOSBox-X (speaker on, captured from its null sink): a step is one 27.4 ms pulse (the first tick's divisor 0 =
    /// a half period of 18.2 Hz, the later divisors wait for the half period's end); the bump (impact, auto-stop) sounds
    /// 106 ms, i.e. 25 tick intervals from the first tick to the uninstall.
    /// </summary>
    [PorTheory]
    [InlineData(SoundCue.Step, 27.0, 28.0)]
    [InlineData(SoundCue.Impact, 104.0, 108.0)]
    public void A_cue_sounds_as_long_as_in_DOSBox(SoundCue cue, double min, double max)
    {
        var p = PorData.Open().Sound()!.Player(PcSoundDevice.Speaker);
        p.Cue(cue);
        var b = new float[44100 / 2];
        p.Render(b);
        var (s, e) = Span(b, 44100);
        Assert.InRange(e - s, min, max);
    }
}

/// <summary>
/// The Tandy noise channel as DOSBox-X plays it (its NCR 8496 model): the PoR driver writes the noise control on every
/// tick (4.22 ms), and the shift register is reset only when the white/periodic bit changes (DOSBox-X s39: the misses'
/// noise; with a reset on every write the Client played a 237 Hz buzz).
/// </summary>
public class TandyNoiseTests
{
    private const int Rate = 44100, Tick = Rate * 5041 / 1193182; // samples per driver tick

    private static float[] Noise(Func<int, byte> controlAt)
    {
        var chip = new Sn76489Synth(Rate);
        chip.Out(0xF0); // noise attenuation 0
        var b = new float[Tick * 8];
        for (int t = 0; t < 8; t++)
        {
            chip.Out(controlAt(t));
            chip.Render(b.AsSpan(t * Tick), Tick);
        }
        return b;
    }

    /// <summary>Correlation of the output with itself one driver tick later (1 = repeats every tick).</summary>
    private static double TickCorrelation(float[] b)
    {
        double sxy = 0, sxx = 0, syy = 0;
        for (int k = 0; k + Tick < b.Length; k++)
        {
            sxy += b[k] * b[k + Tick];
            sxx += b[k] * b[k];
            syy += b[k + Tick] * b[k + Tick];
        }
        return sxy / Math.Sqrt(sxx * syy);
    }

    [Fact]
    public void Rewriting_the_same_noise_control_every_tick_does_not_restart_the_noise()
    {
        Assert.InRange(TickCorrelation(Noise(_ => 0xE4)), -0.3, 0.3); // white noise, N/512, written every tick
    }

    [Fact]
    public void Changing_the_white_noise_bit_every_tick_restarts_it_every_tick()
    {
        // White on even ticks, periodic on odd ones: every white tick starts from the reset register, so the white
        // ticks repeat each other (correlated two ticks apart).
        var b = Noise(t => t % 2 == 0 ? (byte)0xE4 : (byte)0xE0);
        double sxy = 0, sxx = 0, syy = 0;
        for (int k = 0; k < Tick - 2; k++)
        {
            sxy += b[k] * b[k + 2 * Tick];
            sxx += b[k] * b[k];
            syy += b[k + 2 * Tick] * b[k + 2 * Tick];
        }
        Assert.True(sxy / Math.Sqrt(sxx * syy) > 0.5);
    }
}

