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

    /// <summary>
    /// A bracket closed before the driver's first tick (a cast aimed at the caster's own square, docs/re/sound.md §2.4
    /// s43): the uninstall mutes the ports and stops the ticks but leaves the voice blocks, so the started tune runs on in
    /// the next bracket until that bracket's cue (the damage line's tune 2, which restarts all four voices).
    /// </summary>
    [PorFact]
    public void An_empty_bracket_leaves_its_tune_for_the_next_bracket()
    {
        var p = Program.Value.Player(PcSoundDevice.Tandy);
        p.BeginResident();
        p.Cue(SoundCue.Fireball);
        p.EndResident();
        Assert.Equal(0, p.Ticks);
        var buf = new float[2000];
        p.Render(buf);
        Assert.All(buf, s => Assert.Equal(0f, s));
        p.BeginResident(); // the name animation's 0x2BB1
        p.Render(buf);
        Assert.True(buf.Max(MathF.Abs) > 0.05f, $"peak {buf.Max(MathF.Abs)}");
        Assert.True(p.Busy);
        p.EndResident();
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

    [PorFact]
    public void Rendering_until_idle_stops_where_the_auto_stop_ends_and_the_next_cue_starts_there()
    {
        // DOSBox-X s39 (sound.md "ECL sound in a load's vector 4"): two back-to-back cues, the second 109.85 ms after
        // the first's rise: the step's 26 driver ticks. The Client renders up to that sample and starts the next cue on
        // it (ClientAudio; it was 116.67 ms, the 60 Hz buffer after the stop).
        var p = Program.Value.Player(PcSoundDevice.Speaker);
        var buf = new float[44100];
        p.Cue(SoundCue.Step);
        int n = p.Render(buf, true);
        Assert.False(p.Busy);
        Assert.Equal(26, p.Ticks);
        Assert.InRange(n * 1000.0 / 44100, 105.0, 106.0); // 25 tick periods from the first tick to the 26th
        Assert.Equal(n, p.Render(buf.AsSpan(0, n), true)); // idle: a full render
        p.Cue(SoundCue.Fireball);
        Assert.Equal(buf.Length, p.Render(buf, false));
        // Its rise comes one driver tick (4.22 ms) in, so rise to rise is 26 ticks, 109.85 ms (the Client measured
        // 109.86 ms on its null sink, s41).
        Assert.InRange(Array.FindIndex(buf, x => MathF.Abs(x) > 0.05f), 0, 200);
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

    private static float[] Noise(Func<int, byte> controlAt, TandyChip model = TandyChip.Ncr8496)
    {
        var chip = new Sn76489Synth(Rate, model);
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

    [Fact]
    public void The_SN76496_restarts_the_noise_on_every_write_so_the_driver_makes_it_repeat_every_tick()
    {
        // docs/re/sound.md §3 "SN76496": the early Tandy 1000's chip (MAME's SN76496) resets the register on every
        // write of the noise control; with the driver's write each tick the noise repeats at the tick rate (a buzz).
        Assert.True(TickCorrelation(Noise(_ => 0xE4, TandyChip.Sn76496)) > 0.8);
    }

    [Fact]
    public void The_SN76496_at_the_slowest_noise_rate_never_shifts_its_start_bit_out_within_a_tick()
    {
        // N/2048: about 7.4 shifts per 4.22 ms tick, while the reset value (bit 16) needs 16 to reach the output bit.
        var b = Noise(_ => 0xE6, TandyChip.Sn76496);
        Assert.All(b, v => Assert.Equal(b[0], v));
        Assert.NotEqual(b[0], Noise(_ => 0xE6).Skip(Tick * 4).Max()); // the NCR 8496 plays noise there
    }
}


/// <summary>
/// Paula and the A500 output as `PaulaSynth` models them (docs/re/sound.md §5.2 "Paula", §5.6; FS-UAE s41): stereo
/// sides, the held sample of a stopped channel, the output stage's low-pass, the PAL frame rate.
/// </summary>
public class PaulaSynthTests
{
    private const int Rate = 48000;

    // A 64-byte square wave at address 0x1000: 32 bytes of +100, 32 of -100.
    private static sbyte Square(int address) =>
        address is >= 0x1000 and < 0x1040 ? (sbyte)(address < 0x1020 ? 100 : -100) : (sbyte)0;

    private static PaulaSynth Start(int channel, int period, bool filter)
    {
        var p = new PaulaSynth(Square, Rate) { Filter = filter };
        int b = 0xA0 + 0x10 * channel;
        p.Long(b, 0x1000);
        p.Word(b + 4, 32);
        p.Word(b + 6, period);
        p.Word(b + 8, 64);
        p.Word(0x96, 0x8200 | 1 << channel);
        return p;
    }

    [Fact]
    public void Channels_0_and_3_play_left_1_and_2_right()
    {
        foreach (int c in new[] { 0, 1, 2, 3 })
        {
            var p = Start(c, 400, filter: false);
            var l = new float[4800];
            var r = new float[4800];
            p.Render(l, r);
            bool left = c is 0 or 3;
            Assert.True((left ? l : r).Max(Math.Abs) > 0.3f);
            Assert.All(left ? r : l, x => Assert.Equal(0f, x));
        }
    }

    [Fact]
    public void A_stopped_channel_holds_its_last_sample_at_its_volume()
    {
        var p = Start(0, 400, filter: false);
        var l = new float[1000];
        var r = new float[1000];
        p.Render(l, r);
        float last = l[^1];
        p.Word(0x96, 1); // DMA off
        Array.Clear(l);
        p.Render(l, r);
        Assert.All(l, x => Assert.Equal(last, x));
        p.Word(0xA8, 32); // the held level follows the volume
        Array.Clear(l);
        p.Render(l, r);
        Assert.All(l, x => Assert.Equal(last / 2, x, 5));
    }

    [Fact]
    public void The_output_stage_cuts_high_frequencies_and_blocks_a_held_level()
    {
        static double Rms(float[] x) => Math.Sqrt(x.Average(v => (double)v * v));
        // Period 28 with 64 bytes per cycle: 3,546,895 / 28 / 64 = 1.98 kHz fundamental, harmonics to 20 kHz.
        var raw = new float[Rate];
        var filtered = new float[Rate];
        Start(0, 28, filter: false).Render(raw, new float[Rate]);
        Start(0, 28, filter: true).Render(filtered, new float[Rate]);
        Assert.InRange(Rms(filtered) / Rms(raw), 0.5, 0.95);
        // A held level decays through the 5.2 Hz high-pass (time constant 31 ms): gone after a second.
        var p = Start(0, 400, filter: true);
        var l = new float[960];
        p.Render(l, new float[960]);
        p.Word(0x96, 1);
        var tail = new float[Rate];
        p.Render(tail, new float[Rate]);
        Assert.True(Math.Abs(tail[^1]) < 1e-3f);
    }

    [Fact]
    public void The_players_tick_at_the_PAL_frame_rate()
    {
        Assert.Equal(49.92, AmigaMusic.TickHz, 2);
    }
}
