namespace GoldBox.Core.Sound;

/// <summary>
/// The Amiga's four sampled audio channels (Paula) as far as PoR's players use them (docs/re/sound.md §5.2): AUDxLC /
/// LEN / PER / VOL and DMACON. `[?]` Hardware model, not read in the program: switching a channel's DMA on loads its
/// sample pointer and length from LC and LEN; when that length has played the channel reloads from LC and LEN as they
/// are then (so values written after the start are the repeat part); switching it off stops it at once. The output
/// sample is held between fetches (no interpolation, no filter); periods are taken as written (below 124 too); the
/// clock is PAL's 3,546,895 Hz. The four channels are mixed to mono.
/// </summary>
public sealed class PaulaSynth : IPaulaRegisters
{
    public const double PalClock = 3_546_895.0;

    private readonly Func<int, sbyte> _memory;
    private readonly double _sampleRate;
    private readonly Channel[] _ch = [new(), new(), new(), new()];

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
    }

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

    /// <summary>Adds the four channels' output to <paramref name="mono"/> (range about ±1).</summary>
    public void Render(Span<float> mono)
    {
        foreach (var ch in _ch)
        {
            if (!ch.On || ch.Per == 0)
                continue;
            double step = PalClock / ch.Per / _sampleRate;
            float gain = ch.Vol / 64f / 128f / 2f;
            for (int i = 0; i < mono.Length; i++)
            {
                mono[i] += ch.Sample * gain;
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
    }
}

/// <summary>
/// The Amiga audio: the active music or effect player ticks at the vertical blank (50 Hz PAL `[?]`,
/// docs/re/sound.md §5.1) into a shared <see cref="PaulaSynth"/>, rendered on demand. Not thread-safe.
/// </summary>
public sealed class AmigaMusic
{
    public const double TickHz = 50.0;
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

    /// <summary>Adds the audio to <paramref name="mono"/>, ticking the selected player.</summary>
    public void Render(Span<float> mono)
    {
        int i = 0;
        while (i < mono.Length)
        {
            if (_untilTick <= 0)
            {
                if (_playingEffects)
                    _effects!.Tick();
                else
                    _player.Tick();
                _untilTick += _samplesPerTick;
            }
            int n = Math.Min(mono.Length - i, Math.Max(1, (int)Math.Ceiling(_untilTick)));
            _paula.Render(mono.Slice(i, n));
            _untilTick -= n;
            i += n;
        }
    }
}
