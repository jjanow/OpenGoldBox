namespace GoldBox.Core.Sound;

/// <summary>
/// The Amiga's four sampled audio channels (Paula) and the A500's audio output, as far as PoR's players use them
/// (docs/re/sound.md §5.2, §5.6): AUDxLC / LEN / PER / VOL and DMACON. Hardware model, not read in the program
/// (compared with FS-UAE, docs §5.6): switching a channel's DMA on loads its sample pointer and length from LC and LEN;
/// when that length has played the channel reloads from LC and LEN as they are then (so values written after the
/// start are the repeat part); switching it off stops the fetches at once and the channel's output holds its last
/// sample (times its volume). The output sample is held between fetches (no interpolation); periods are taken as
/// written (below 124 too); the clock is PAL's 3,546,895 Hz. Channels 0 and 3 go to the left output, 1 and 2 to the
/// right; the A500's output stage follows on each side (<see cref="Filter"/>).
/// </summary>
public sealed class PaulaSynth : IPaulaRegisters
{
    public const double PalClock = 3_546_895.0;

    private readonly Func<int, sbyte> _memory;
    private readonly double _sampleRate;
    private readonly Channel[] _ch = [new(), new(), new(), new()];
    private readonly OutputStage _left, _right;
    private float[] _l = [], _r = [];

    private sealed class Channel
    {
        public int Lc, Len, Per, Vol;
        public bool On;
        public int Ptr;
        public int Bytes;   // bytes left in the current pass
        public double Phase; // fraction of a byte played
        public sbyte Sample;
    }

    /// <param name="memory">The sample byte at an address (the player's memory; 0 outside it).</param>
    public PaulaSynth(Func<int, sbyte> memory, int sampleRate)
    {
        _memory = memory;
        _sampleRate = sampleRate;
        _left = new OutputStage(sampleRate);
        _right = new OutputStage(sampleRate);
    }

    /// <summary>
    /// The A500's output stage (on by default): a first-order high-pass at 5.2 Hz (the output capacitor), a
    /// first-order low-pass at 4.42 kHz (the fixed RC filter) and the second-order "LED" low-pass at 3.09 kHz, Q 0.66,
    /// which works while the power LED is bright (Kickstart's state at start-up; PoR never writes CIA-A's port A).
    /// Off: the channels unfiltered.
    /// </summary>
    public bool Filter { get; set; } = true;

    /// <summary>Channels left out of the output (bit c = channel c; for comparisons with captures).</summary>
    public int Mute { get; set; }

    public void Word(int register, int value)
    {
        if (register == 0x96)
        {
            bool set = (value & 0x8000) != 0;
            for (int c = 0; c < 4; c++)
            {
                if ((value & (1 << c)) == 0)
                    continue;
                var ch = _ch[c];
                if (set && !ch.On)
                {
                    ch.On = true;
                    Load(ch);
                    ch.Phase = 0;
                    ch.Sample = _memory(ch.Ptr);
                }
                else if (!set)
                    ch.On = false;
            }
            return;
        }
        int n = (register - 0xA0) >> 4;
        if ((uint)n >= 4)
            return;
        var x = _ch[n];
        switch ((register - 0xA0) & 0x0F)
        {
            case 4: x.Len = value & 0xFFFF; break;
            case 6: x.Per = value & 0xFFFF; break;
            case 8: x.Vol = Math.Min(value & 0x7F, 64); break;
        }
    }

    public void Long(int register, int address)
    {
        int n = (register - 0xA0) >> 4;
        if ((uint)n < 4 && ((register - 0xA0) & 0x0F) == 0)
            _ch[n].Lc = address;
    }

    private static void Load(Channel ch)
    {
        ch.Ptr = ch.Lc;
        ch.Bytes = 2 * (ch.Len == 0 ? 0x10000 : ch.Len);
    }

    /// <summary>Adds the left (channels 0, 3) and right (1, 2) outputs to the buffers (range about ±1 each).</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("the two sides differ in length");
        int n = left.Length;
        if (_l.Length < n)
        {
            _l = new float[n];
            _r = new float[n];
        }
        var l = _l.AsSpan(0, n);
        var r = _r.AsSpan(0, n);
        l.Clear();
        r.Clear();
        for (int c = 0; c < 4; c++)
            if ((Mute & 1 << c) == 0)
                RenderChannel(_ch[c], c is 0 or 3 ? l : r);
        if (Filter)
        {
            _left.Run(l);
            _right.Run(r);
        }
        for (int i = 0; i < n; i++)
        {
            left[i] += l[i];
            right[i] += r[i];
        }
    }

    private void RenderChannel(Channel ch, Span<float> side)
    {
        float gain = ch.Vol / 64f / 128f / 2f;
        if (!ch.On || ch.Per == 0)
        {
            // Fetches stopped: the output holds the last sample at the channel's volume.
            float hold = ch.Sample * gain;
            if (hold != 0)
                for (int i = 0; i < side.Length; i++)
                    side[i] += hold;
            return;
        }
        double step = PalClock / ch.Per / _sampleRate;
        for (int i = 0; i < side.Length; i++)
        {
            side[i] += ch.Sample * gain;
            ch.Phase += step;
            while (ch.Phase >= 1)
            {
                ch.Phase -= 1;
                ch.Ptr++;
                if (--ch.Bytes <= 0)
                    Load(ch);
                ch.Sample = _memory(ch.Ptr);
            }
        }
    }

    /// <summary>One side of the A500's output: high-pass 5.2 Hz, low-pass 4.42 kHz, LED low-pass 3.09 kHz.</summary>
    private sealed class OutputStage
    {
        private readonly double _hp, _lp, _b0, _b1, _b2, _a1, _a2;
        private double _hpIn, _hpOut, _lpOut, _x1, _x2, _y1, _y2;

        public OutputStage(double rate)
        {
            // First-order high-pass R = 1390 ohm, C = 22 uF; first-order low-pass R = 360 ohm, C = 0.1 uF.
            double dt = 1 / rate, rcHp = 1390 * 22e-6, rcLp = 360 * 0.1e-6;
            _hp = rcHp / (rcHp + dt);
            _lp = 1 - Math.Exp(-dt / rcLp);
            // LED filter: Sallen-Key low-pass, R1 = R2 = 10 kohm, C1 = 6800 pF, C2 = 3900 pF (bilinear, pre-warped).
            double r1 = 10_000, r2 = 10_000, c1 = 6800e-12, c2 = 3900e-12, rc = Math.Sqrt(r1 * r2 * c1 * c2);
            double fc = 1 / (2 * Math.PI * rc), q = rc / (c2 * (r1 + r2));
            double k = Math.Tan(Math.PI * Math.Min(fc, rate * 0.45) / rate), norm = 1 / (1 + k / q + k * k);
            _b0 = k * k * norm;
            _b1 = 2 * _b0;
            _b2 = _b0;
            _a1 = 2 * (k * k - 1) * norm;
            _a2 = (1 - k / q + k * k) * norm;
        }

        public void Run(Span<float> x)
        {
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i];
                _hpOut = _hp * (_hpOut + v - _hpIn);
                _hpIn = v;
                _lpOut += _lp * (_hpOut - _lpOut);
                double y = _b0 * _lpOut + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1;
                _x1 = _lpOut;
                _y2 = _y1;
                _y1 = y;
                x[i] = (float)y;
            }
        }
    }
}

/// <summary>
/// The Amiga audio: the active music or effect player ticks at the vertical blank (a PAL A500's non-interlaced frame,
/// docs/re/sound.md §5.1, §5.6) into a shared <see cref="PaulaSynth"/>, rendered on demand in stereo. Not thread-safe.
/// </summary>
public sealed class AmigaMusic
{
    /// <summary>PAL frame rate: 313 lines of 227 colour clocks, 49.92 Hz (FS-UAE: pitch and tempo in this ratio).</summary>
    public const double TickHz = PaulaSynth.PalClock / (227 * 313);
    private readonly AmigaMusicPlayer _player;
    private readonly AmigaEffectPlayer? _effects;
    private readonly int[] _cues;
    private readonly PaulaSynth _paula;
    private readonly double _samplesPerTick;
    private double _untilTick;
    private bool _playingEffects;

    public AmigaMusic(AmigaSoundImage image, int sampleRate)
    {
        _paula = new PaulaSynth(SampleAt, sampleRate);
        _player = new AmigaMusicPlayer(image, _paula);
        _effects = AmigaEffectPlayer.Create(image, _paula);
        _cues = image.Cues;
        _samplesPerTick = sampleRate / TickHz;
    }

    public AmigaMusicPlayer Player => _player;
    public bool HasEffects => _effects is not null;

    /// <summary>The A500 output filter (<see cref="PaulaSynth.Filter"/>).</summary>
    public bool Filter
    {
        get => _paula.Filter;
        set => _paula.Filter = value;
    }

    /// <summary>Channels left out of the output (<see cref="PaulaSynth.Mute"/>).</summary>
    public int Mute
    {
        get => _paula.Mute;
        set => _paula.Mute = value;
    }

    private sbyte SampleAt(int address) =>
        address >= AmigaEffectPlayer.Base ? _effects?.SampleAt(address) ?? 0 : _player.SampleAt(address);

    /// <summary>PlaySong(n): 0 stops, 1–7 start song n (taken at the next tick).</summary>
    public void Play(int song)
    {
        _playingEffects = false;
        _player.PlaySong(song);
    }

    /// <summary>Queues the corresponding sampled cue, taking over the music player's vertical blank.</summary>
    public void PlayEffect(SoundCue cue)
    {
        // The Amiga has no caller for MissileOut. Its combat sound plays on arrival.
        int index = (int)cue - (int)SoundCue.Cast;
        if (cue != SoundCue.MissileOut && (uint)index <= (int)SoundCue.MissileIn - (int)SoundCue.Cast
            && index < _cues.Length && _effects?.Play(_cues[index]) == true)
            _playingEffects = true;
    }

    /// <summary>Adds the left and right outputs to the buffers, ticking the selected player.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        int i = 0;
        while (i < left.Length)
        {
            if (_untilTick <= 0)
            {
                if (_playingEffects)
                    _effects!.Tick();
                else
                    _player.Tick();
                _untilTick += _samplesPerTick;
            }
            int n = Math.Min(left.Length - i, Math.Max(1, (int)Math.Ceiling(_untilTick)));
            _paula.Render(left.Slice(i, n), right.Slice(i, n));
            _untilTick -= n;
            i += n;
        }
    }
}
