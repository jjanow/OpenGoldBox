namespace GoldBox.Core.Sound;

/// <summary>
/// The Tandy 1000's SN76489 (docs/re/sound.md §3): three square-wave tone channels and a noise channel, clocked at
/// 3,579,545 Hz (each counter steps at clock / 16; tone = clock / 32 / period), 2 dB per attenuation step, 15 =
/// off. Port C0h bytes as the chip takes them: a latch byte (bit 7 set: channel in bits 5–6, bit 4 = attenuation,
/// low 4 bits of data) or a data byte (the period's high 6 bits for the latched tone channel). Writing the noise
/// control resets its shift register. `[?]` The shift register is 15 bits with taps 0 and 1 (the TI part's); the Tandy
/// 1000's exact chip variant and its period-0 rule are not checked (period 0 counts as 1024 here).
/// </summary>
public sealed class Sn76489Synth
{
    public const double ClockHz = 3_579_545.0;
    private readonly double _stepsPerSample;
    private readonly int[] _period = new int[3];
    private readonly int[] _counter = new int[3];
    private readonly bool[] _out = new bool[3];
    private readonly int[] _att = [15, 15, 15, 15];
    private int _latched, _noiseCtl, _noiseCounter, _lfsr = 0x4000;
    private bool _noiseOut, _noiseClockPrev;
    private double _frac;
    private static readonly double[] Volume = Enumerable.Range(0, 16).Select(i => i == 15 ? 0 : Math.Pow(10, -2.0 * i / 20)).ToArray();

    public Sn76489Synth(int sampleRate) => _stepsPerSample = ClockHz / 16 / sampleRate;

    public double Gain { get; set; } = 0.2;

    public void Out(byte value)
    {
        if ((value & 0x80) != 0)
        {
            _latched = (value >> 5) & 3;
            if ((value & 0x10) != 0)
                _att[_latched] = value & 0x0F;
            else if (_latched == 3)
            {
                _noiseCtl = value & 7;
                _lfsr = 0x4000;
            }
            else
                _period[_latched] = (_period[_latched] & 0x3F0) | (value & 0x0F);
        }
        else if (_latched < 3)
            _period[_latched] = (_period[_latched] & 0x0F) | (value & 0x3F) << 4;
    }

    private int NoisePeriod => (_noiseCtl & 3) switch { 0 => 0x10, 1 => 0x20, 2 => 0x40, _ => P(2) };
    private int P(int ch) => _period[ch] == 0 ? 0x400 : _period[ch];

    /// <summary>Renders <paramref name="n"/> samples, added to <paramref name="buffer"/>.</summary>
    public void Render(Span<float> buffer, int n)
    {
        for (int i = 0; i < n; i++)
        {
            _frac += _stepsPerSample;
            int steps = (int)_frac;
            _frac -= steps;
            double acc = 0;
            for (int s = 0; s < steps; s++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    if (--_counter[ch] <= 0)
                    {
                        _counter[ch] = P(ch);
                        _out[ch] = !_out[ch];
                    }
                }
                if (--_noiseCounter <= 0)
                {
                    _noiseCounter = NoisePeriod;
                    _noiseClockPrev = !_noiseClockPrev;
                    if (_noiseClockPrev)
                    {
                        int fb = (_noiseCtl & 4) != 0 ? ((_lfsr ^ (_lfsr >> 1)) & 1) : (_lfsr & 1);
                        _noiseOut = (_lfsr & 1) != 0;
                        _lfsr = (_lfsr >> 1) | fb << 14;
                    }
                }
                double v = 0;
                for (int ch = 0; ch < 3; ch++)
                    v += (_period[ch] <= 1 ? 1 : _out[ch] ? 1 : -1) * Volume[_att[ch]];
                v += (_noiseOut ? 1 : -1) * Volume[_att[3]];
                acc += v;
            }
            if (steps > 0)
                buffer[i] += (float)(acc / steps * Gain);
        }
    }
}
