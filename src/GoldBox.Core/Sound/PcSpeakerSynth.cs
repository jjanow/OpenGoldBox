namespace GoldBox.Core.Sound;

/// <summary>
/// The PC speaker as PoR drives it (docs/re/sound.md §3): PIT channel 2 in mode 3 (square wave of
/// 1,193,182 / count Hz; count 0 = 65,536) behind port 61h's gate (bit 0) and speaker-data (bit 1) bits. Bit 0 and 1
/// set: the wave reaches the speaker; bit 1 alone: the speaker is held high; else low. A count written while the
/// wave runs takes effect at the end of the current half cycle (8254 mode 3). Rendered by integrating the output over
/// each sample (box filter), unfiltered and unipolar (0 = speaker low), as DOSBox-X outputs it (a held 27 ms pulse for a period-0 tick). `[?]` A real speaker's analogue response is not modelled.
/// </summary>
public sealed class PcSpeakerSynth
{
    private readonly double _pitPerSample;
    private int _count = 0x10000, _pending = -1;
    private bool _lowByte = true;
    private int _latch;
    private byte _port61;
    private double _phase;      // PIT clocks into the current half cycle
    private bool _high = true;  // the timer's output

    public PcSpeakerSynth(int sampleRate)
    {
        _pitPerSample = PcSoundDriverImage.PitHz / sampleRate;
    }

    /// <summary>Output gain of a sample at full deflection.</summary>
    public double Gain { get; set; } = 0.3;

    /// <summary>Port 43h control byte 0B6h: channel 2, low then high byte, mode 3 (the byte order restarts).</summary>
    public void Control() => _lowByte = true;

    public void Out(int port, byte value)
    {
        if (port == 0x42)
        {
            if (_lowByte)
                _latch = value;
            else
            {
                int c = _latch | value << 8;
                _pending = c == 0 ? 0x10000 : c;
            }
            _lowByte = !_lowByte;
        }
        else if (port == 0x61)
        {
            bool gateWas = (_port61 & 1) != 0, gate = (value & 1) != 0;
            _port61 = value;
            if (gate && !gateWas)
            {
                // Gate rising: the counter reloads and the wave starts high.
                if (_pending >= 0)
                {
                    _count = _pending;
                    _pending = -1;
                }
                _phase = 0;
                _high = true;
            }
        }
    }

    private double Half(bool high) => (_count & 1) == 0 ? _count / 2.0 : high ? (_count + 1) / 2.0 : (_count - 1) / 2.0;

    /// <summary>Renders <paramref name="n"/> samples, added to <paramref name="buffer"/>.</summary>
    public void Render(Span<float> buffer, int n)
    {
        for (int i = 0; i < n; i++)
        {
            bool gate = (_port61 & 1) != 0, data = (_port61 & 2) != 0;
            double level;
            if (!gate)
            {
                level = data ? 1 : 0; // mode 3 with the gate low: OUT high
                if (_pending >= 0)
                {
                    _count = _pending;
                    _pending = -1;
                }
            }
            else
            {
                double left = _pitPerSample, highTime = 0;
                while (left > 0)
                {
                    double rest = Half(_high) - _phase;
                    if (rest > left)
                    {
                        if (_high)
                            highTime += left;
                        _phase += left;
                        left = 0;
                    }
                    else
                    {
                        if (_high)
                            highTime += rest;
                        left -= rest;
                        _phase = 0;
                        _high = !_high;
                        if (_pending >= 0)
                        {
                            _count = _pending;
                            _pending = -1;
                        }
                    }
                }
                level = data ? highTime / _pitPerSample : 0;
            }
            buffer[i] += (float)(level * Gain);
        }
    }
}
