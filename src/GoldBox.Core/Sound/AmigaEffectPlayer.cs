using System.Buffers.Binary;

namespace GoldBox.Core.Sound;

/// <summary>Pool of Radiance Amiga v1.0's sampled effects on channel 0 (docs/re/sound.md §5.4).</summary>
public sealed class AmigaEffectPlayer
{
    public const int Base = 0x0200_0000;
    private const int PeriodTable = 0x574, Definitions = 0x662, SamplePointers = 0x6DA, SampleLengths = 0x716;
    private const int EffectCount = 15, PeriodCount = 60, DefinitionSize = 8;

    private readonly record struct Effect(int Sample, int Note, int Parameter, int Volume, int Flags, int Duration,
        int Decay, sbyte Repeats);
    private readonly record struct Sample(int Address, int Length, int RepeatOffset, int RepeatLength, bool Loops);

    private readonly IPaulaRegisters _output;
    private readonly Effect[] _effects;
    private readonly Sample[] _samples;
    private readonly int[] _periods;
    private readonly byte[] _sampleBytes;
    private int _request = -1;
    private Effect _effect;
    private Sample _sample;
    private int _remaining, _period, _volume, _repeats;
    private int _repeatClock = 20;
    private bool _active, _alternate;

    private AmigaEffectPlayer(IPaulaRegisters output, Effect[] effects, Sample[] samples, int[] periods, byte[] sampleBytes)
    {
        _output = output;
        _effects = effects;
        _samples = samples;
        _periods = periods;
        _sampleBytes = sampleBytes;
    }

    /// <summary>Creates a player when the imported definitions and sample metadata fit in the audio data.</summary>
    public static AmigaEffectPlayer? Create(AmigaSoundImage image, IPaulaRegisters output)
    {
        bool Fits(int at, int size) => at >= image.EffectsOrigin && at - image.EffectsOrigin <= image.Effects.Length - size;
        if (image.EffectsOrigin < 0 || !Fits(PeriodTable, 2 * PeriodCount)
            || !Fits(Definitions, EffectCount * DefinitionSize) || !Fits(SamplePointers, EffectCount * 4))
            return null;

        ReadOnlySpan<byte> At(int at) => image.Effects.AsSpan(at - image.EffectsOrigin);
        int Word(int at) => BinaryPrimitives.ReadUInt16BigEndian(At(at));

        var effects = new Effect[EffectCount];
        var samples = new Sample[EffectCount];
        var periods = new int[PeriodCount];
        for (int i = 0; i < PeriodCount; i++)
            periods[i] = Word(PeriodTable + 2 * i);
        for (int i = 0; i < EffectCount; i++)
        {
            var d = At(Definitions + i * DefinitionSize);
            int number = d[0], pointer = SamplePointers + 4 * number, lengths = SampleLengths + 8 * number;
            if (number >= EffectCount || d[1] >= PeriodCount || !Fits(lengths, 8) || !image.EffectRelocs.Contains(pointer))
                return null;
            if ((d[4] & 4) != 0 && d[2] >= PeriodCount)
                return null;
            int addr = BinaryPrimitives.ReadInt32BigEndian(At(pointer));
            int length = Word(lengths), repeat = Word(lengths + 2), repeatLength = Word(lengths + 4);
            bool loops = (Word(lengths + 6) & 255) != 0;
            if (addr < 0 || addr > image.EffectSamples.Length - length
                || loops && addr > image.EffectSamples.Length - repeat - repeatLength)
                return null;
            effects[i] = new Effect(number, d[1], d[2], d[3], d[4], d[5], d[6], (sbyte)d[7]);
            samples[number] = new Sample(addr + Base, length, repeat, repeatLength, loops);
        }
        return new AmigaEffectPlayer(output, effects, samples, periods, image.EffectSamples);
    }

    public sbyte SampleAt(int address)
    {
        int offset = address - Base;
        return (uint)offset < (uint)_sampleBytes.Length ? (sbyte)_sampleBytes[offset] : (sbyte)0;
    }

    /// <summary>Queues an effect for the next vertical blank. Only the channel 0 request has a writer.</summary>
    public bool Play(int effect)
    {
        if ((uint)effect >= EffectCount)
            return false;
        _request = effect;
        return true;
    }

    public void Tick()
    {
        bool dmaOff = !_active;
        if (_request >= 0)
        {
            Start();
            dmaOff = true; // the first tick writes the sample setup with the channel stopped
        }
        else if (_active)
        {
            Step();
            dmaOff = !_active;
        }

        _output.Word(0xA6, _period);
        for (int register = 0xB6; register <= 0xD6; register += 0x10)
            _output.Word(register, 0);
        int dma = 0x8200 | (dmaOff ? 0 : 1);
        _output.Word(0x96, dma);
        _output.Word(0x96, dma ^ 0xFFFF);
        _output.Word(0xA8, _volume);
        for (int register = 0xB8; register <= 0xD8; register += 0x10)
            _output.Word(register, 0);

        if (--_repeatClock != 0)
            return;
        if (_sample.Loops)
        {
            _output.Word(0xA4, _sample.RepeatLength / 2);
            _output.Long(0xA0, _sample.Address + _sample.RepeatOffset);
        }
        _repeatClock = 20;
    }

    private void Start()
    {
        _effect = _effects[_request];
        _sample = _samples[_effect.Sample];
        _request = -1;
        _active = true;
        _remaining = _effect.Duration;
        _period = _periods[_effect.Note];
        _volume = _effect.Volume;
        _repeats = _effect.Repeats;
        _output.Long(0xA0, _sample.Address);
        _output.Word(0xA4, _sample.Length / 2);
        _output.Word(0x96, 1);
    }

    private void Step()
    {
        if (_effect.Repeats != -1 && _remaining == 0)
        {
            if (_repeats >= 0)
            {
                _repeats--;
                _remaining = _effect.Duration;
                return;
            }
            if (_effect.Decay == 0 || (_volume -= _effect.Decay) < 0)
            {
                _active = false;
                _volume = 0;
            }
            return;
        }

        if (_effect.Repeats != -1)
            _remaining--;
        if ((_effect.Flags & 4) != 0)
        {
            if (!_alternate)
                _period = _periods[_effect.Parameter];
            _alternate = !_alternate;
        }
        else if ((_effect.Flags & 2) != 0 && _period != 3424)
            _period = (_period + _effect.Parameter) & 0xFFF;
        else if ((_effect.Flags & 2) == 0 && (_effect.Flags & 1) != 0 && _period != 113)
            _period = (_period - _effect.Parameter) & 0xFFF;
    }
}
