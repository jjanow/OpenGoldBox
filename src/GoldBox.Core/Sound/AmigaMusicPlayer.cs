using System.Buffers.Binary;

namespace GoldBox.Core.Sound;

/// <summary>Writes to the Amiga's custom-chip registers (offsets from 0xDFF000): what a sound player drives.</summary>
public interface IPaulaRegisters
{
    /// <summary>A word write: DMACON (0x96), AUDxLEN (0xA4 + 0x10x), AUDxPER (0xA6 + …), AUDxVOL (0xA8 + …).</summary>
    void Word(int register, int value);

    /// <summary>A long write: AUDxLC (0xA0 + 0x10x), <paramref name="address"/> an address in the player's memory.</summary>
    void Long(int register, int address);
}

/// <summary>
/// Pool of Radiance Amiga v1.0's music player (hunk 27 +0x78A0 … +0x831E, docs/re/sound.md §5.2), run on a copy of its
/// data hunk (<see cref="AmigaSoundImage.Music"/>) placed at <see cref="Base"/> with its relocations applied, so the
/// pointers it copies, adds to and indexes behave as in the program (including the one-shot repeat pointer read past
/// its table for voices 1–3). Each routine below names its program address; voice fields sit at
/// 0x1750 + field + voice as in the program. <see cref="Tick"/> is the vertical-blank call (+0x78B2) and writes the
/// same registers in the same order as the program (checked against the program under emulation, AmigaSoundProgramTests).
/// The arpeggio, portamento and slide paths are not reached by any song of the game (docs §5.2) and are unchecked.
/// </summary>
public sealed class AmigaMusicPlayer
{
    /// <summary>The address the data hunk is placed at (any value works; pointers below it are chip addresses).</summary>
    public const int Base = 0x0100_0000;

    // Data hunk offsets (hunk 28).
    private const int Periods = 0x16E8, OutPeriod = 0x1748, Vs = 0x1750, VoiceNo = 0x17D4, Playing = 0x17D5,
        Speed = 0x17D6, SpeedCount = 0x17D7, VibDepth = 0x17D8, VibSpeed = 0x17D9, ArpMask = 0x17DA, RowPos = 0x17DB,
        RegOff = 0x17DC, SlideDir = 0x17DE, DmaOff = 0x17E0, BasePeriod = 0x17E2, TargetPeriod = 0x17EA,
        SlideStep = 0x17F2, SampleStart = 0x17FC, RepLen = 0x180C, RepOff = 0x1814, PatPtr = 0x181C, Song = 0x182C,
        TrackTmp = 0x183C, PatTmp = 0x1840, VibStep = 0x1844, VibWork = 0x1848, V4 = 0x184C, V2 = 0x1850,
        ArpBuf = 0x1854, SamplePtrs = 0x187C, Silence = 0x18B8, Instruments = 0x18C4, ArpIntervals = 0x1924,
        Envelopes = 0x192C, Songs = 0x197C, Patterns = 0x19DC, Volumes = 0x17B0, LoopFlags = 0x17C8;

    private readonly byte[] _m;
    private readonly IPaulaRegisters _out;
    private byte _command;               // hunk 27 +0x78C0 (0 in the program)
    private byte _portaTarget, _portaLen; // the immediates the player patches (+0x80F5, +0x7E93 / +0x817F)
    private int _v;                       // d1: the voice being run
    private int _d4;                      // d4 in the event reader: bytes consumed

    public AmigaMusicPlayer(AmigaSoundImage image, IPaulaRegisters output)
    {
        _m = (byte[])image.Music.Clone();
        foreach (int o in image.MusicRelocs)
            L(o, L(o) + Base);
        _out = output;
    }

    /// <summary>The data hunk as the player has it now (its samples are what AUDxLC points at).</summary>
    public ReadOnlySpan<byte> Memory => _m;

    /// <summary>The sample byte at <paramref name="address"/>: the data hunk's, or 0 for chip memory outside it `[?]`.</summary>
    public sbyte SampleAt(int address)
    {
        int o = address - Base;
        return (uint)o < (uint)_m.Length ? (sbyte)_m[o] : (sbyte)0;
    }

    /// <summary>True while a song runs (hunk 28 +0x17D5 with the command consumed).</summary>
    public bool IsPlaying => _m[Playing] != 0 || _command is >= 1 and <= 7;

    /// <summary>PlaySong(n) (hunk 27 +0x78A0): n is taken at the next tick; 0 stops, 1–7 start song n.</summary>
    public void PlaySong(int n)
    {
        _command = (byte)n;
        Reset();
    }

    /// <summary>The vertical-blank call (+0x78B2 → +0x78C2).</summary>
    public void Tick() => Entry();

    // ---- memory ----------------------------------------------------------------------------------------------------

    private int W(int o) => BinaryPrimitives.ReadUInt16BigEndian(_m.AsSpan(o));
    private void W(int o, int v) => BinaryPrimitives.WriteUInt16BigEndian(_m.AsSpan(o), (ushort)v);
    private int L(int o) => BinaryPrimitives.ReadInt32BigEndian(_m.AsSpan(o));
    private void L(int o, int v) => BinaryPrimitives.WriteInt32BigEndian(_m.AsSpan(o), v);

    /// <summary>A byte through a pointer (track lists, patterns).</summary>
    private byte At(int address)
    {
        int o = address - Base;
        return (uint)o < (uint)_m.Length ? _m[o] : (byte)0;
    }

    /// <summary>Voice field <paramref name="f"/> of the running voice (a0 + f).</summary>
    private ref byte F(int f) => ref _m[Vs + f + _v];

    // ---- commands --------------------------------------------------------------------------------------------------

    private void Entry() // +0x78C2
    {
        byte d0 = _command;
        if (d0 == 0)
        {
            _m[Playing] = 0;
            SilenceAll();
            _command = 0xFF;
            return;
        }
        if (d0 > 7)
        {
            Running();
            return;
        }
        Buffer.BlockCopy(_m, Songs + 16 * (d0 - 1), _m, Song, 16);
        for (_v = 3; _v >= 0; _v--)
        {
            F(0x14) = 0;
            F(0x18) = 0;
            F(0x0C) = 0;
            NextPattern();
        }
        Reset();
        _m[Playing] = 1;
        _m[DmaOff] = 0x0F;
        _command = 0xFF;
    }

    private void Reset() // +0x807E
    {
        SilenceAll();
        _m[SpeedCount] = 0;
        _m[Playing] = 0;
    }

    private void SilenceAll() // +0x8052
    {
        for (int c = 0; c < 4; c++)
            _out.Word(0xA8 + 0x10 * c, 0);
        for (int c = 0; c < 4; c++)
            _m[Volumes + c] = 0;
    }

    /// <summary>+0x793C: the voice's next track-list entry: transposes, the restart, then a pattern.</summary>
    private void NextPattern()
    {
        int d2 = _v * 4;
        L(TrackTmp, L(Song + d2));
        while (true)
        {
            int list = L(TrackTmp);
            int pos = F(0x0C);
            F(0x0C)++;
            byte b = At(list + pos);
            if (b <= 0xC0)
            {
                int p = L(Patterns + 4 * b);
                L(PatPtr + d2, p);
                L(PatTmp, p);
                return;
            }
            if (b == 0xFF)
                F(0x0C) = 0;
            else
                F(0x20) = (byte)(b + 0x20);
        }
    }

    // ---- the running tick ------------------------------------------------------------------------------------------

    private void Running() // +0x79AC
    {
        if (_m[Playing] == 0)
        {
            _command = 0xFF; // +0x78DE: past the silence call
            return;
        }
        _m[VoiceNo] = 3;
        while (true)
        {
            _v = _m[VoiceNo];
            L(V4, _v * 4);
            L(V2, _v * 2);
            _m[RegOff] = F(0);
            if (!Voice())
                return; // 0xFE / 0xFC: the start code ran instead
            _m[VoiceNo]--;
            if ((sbyte)_m[VoiceNo] < 0)
                break;
        }
        Output();
        for (int ch = 3; ch >= 0; ch--) // +0x7FB6: the repeat part
        {
            _out.Word(0xA4 + 0x10 * ch, W(RepLen + 2 * ch) >> 1);
            int ptr = W(RepOff + 2 * ch) + L((_m[LoopFlags + ch] == 0 ? Silence : SampleStart) + 4 * ch);
            _out.Long(0xA0 + 0x10 * ch, ptr);
        }
        _m[SpeedCount]--;
        if ((sbyte)_m[SpeedCount] < 0)
            _m[SpeedCount] = _m[Speed];
        _command = 0xFF;
    }

    /// <summary>One voice's part of the tick (+0x79BE … +0x7F9C); false when the song was stopped or chained.</summary>
    private bool Voice()
    {
        if (_m[SpeedCount] != 0)
        {
            if (F(0x34) != 0) // +0x7A42 (never set)
            {
                F(0x34) = 0;
                F(0x08) -= 2;
                SetPeriod();
                Envelope();
                return true;
            }
            Effects();
            return true;
        }
        F(0x14)--;
        if (F(0x70) == 0 && F(0x14) == 0)
            _m[DmaOff] |= (byte)(1 << _v);
        else if ((sbyte)F(0x14) < 0)
            return ReadEvent();
        if (F(0x2C) != 0)
        {
            F(0x08)--;
            SetPeriod();
            Envelope();
        }
        else if (F(0x30) != 0)
        {
            F(0x08)++;
            SetPeriod();
            Envelope();
        }
        else
            Effects();
        return true;
    }

    /// <summary>+0x7A58: the voice's next event (prefixes, effect, note).</summary>
    private bool ReadEvent()
    {
        if (F(0x18) != 2)
        {
            F(0x18) = 0;
            F(0x1C) = 0;
        }
        _m[RowPos] = 0;
        F(0x24) = 0;
        F(0x2C) = 0;
        F(0x30) = 0;
        F(0x6C) = 0;
        L(PatTmp, L(PatPtr + L(V4)));
        int a3;
    restart:
        _d4 = _m[RowPos];
        _m[RowPos]++;
        a3 = L(PatTmp);
        byte Next()
        {
            _d4++;
            return At(a3++);
        }
        byte b = Next();
        if (b == 0xFF)
            goto newPattern;
        if (b == 0xFE)
        {
            _command = 0;
            Entry();
            return false;
        }
        if (b == 0xFC)
        {
            _command = At(a3);
            Entry();
            return false;
        }
        if (b > 0xEF)
        {
            _m[Speed] = (byte)(b & 0x0F);
            b = Next();
        }
        if (b > 0xDF)
        {
            F(0x4C) = (byte)((b & 0x0F) << 3);
            b = Next();
            if (b == 0xFF)
                goto newPattern;
        }
        if (b > 0xBF)
        {
            F(0x50) = (byte)((b & 0x1F) << 3);
            b = Next();
            if (b == 0xFF)
                goto newPattern;
        }
        if (b > 0x7F)
        {
            F(0x10) = (byte)(b & 0x3F);
            b = Next();
        }
        if (b > 0x5F)
        {
            switch (b)
            {
                case 0x7F:
                    F(0x2C) = 1;
                    F(0x30) = 0;
                    F(0x18) = 1;
                    b = Next();
                    break;
                case 0x7E:
                    F(0x30) = 1;
                    F(0x2C) = 0;
                    F(0x18) = 1;
                    b = Next();
                    break;
                case 0x7D or 0x7C:
                    F(0x18) = (byte)(b == 0x7D ? 1 : 2);
                    b = Next();
                    F(0x1C) = b;
                    _m[ArpMask] = b;
                    BuildArpeggio();
                    b = Next();
                    break;
                case 0x7B:
                    F(0x1C) = 0;
                    F(0x18) = 1;
                    F(0x24) = (byte)(At(a3++) + F(0x20));
                    F(0x28) = At(a3++);
                    _d4 += 2;
                    b = Next();
                    break;
                case 0x7A:
                    F(0x44) = At(a3++);
                    b = At(a3);
                    _d4 += 2;
                    break;
            }
        }
        Note(b);
        return true;
    newPattern:
        NextPattern();
        _m[RowPos] = 0;
        goto restart;
    }

    /// <summary>+0x7BF8: a new note on the voice.</summary>
    private void Note(byte d0)
    {
        _m[DmaOff] &= (byte)~(1 << _v);
        _out.Word(0x96, F(0x64));
        _m[RowPos] = (byte)_d4;
        SetInstrument();
        SetEnvelope();
        d0 += F(0x20);
        F(0x08) = d0;
        if (d0 != 0 && F(0x1C) == 0 && F(0x34) != 0)
            F(0x08) += 2;
        SetPeriod();
        int pp = PatPtr + L(V4);
        L(pp, L(pp) + _m[RowPos]);
        F(0x14) = F(0x10);
        F(0x48) = F(0x44);
        F(0x38) = F(0x04);
        F(0x38)++;
        Envelope();
    }

    /// <summary>+0x8204: the instrument's sample start, first length and repeat part.</summary>
    private void SetInstrument()
    {
        int a1 = Instruments + F(0x4C);
        int sample = _m[a1];
        F(0x78) = _m[a1 + 1];
        int len = F(0x78) == 0 ? W(a1 + 2) : (W(a1 + 4) + W(a1 + 6)) & 0xFFFF;
        W(RepLen + L(V2), W(a1 + 6));
        W(RepOff + L(V2), W(a1 + 4));
        int reg = _m[RegOff];
        _out.Word(0xA4 + reg, len >> 1);
        int ptr = L(SamplePtrs + 4 * sample);
        _out.Long(0xA0 + reg, ptr);
        L(SampleStart + L(V4), ptr);
    }

    /// <summary>+0x8198: the envelope's volume, attack, release, sustain, vibrato and arpeggio.</summary>
    private void SetEnvelope()
    {
        int a2 = Envelopes + F(0x50);
        F(0x60) = _m[a2];
        F(0x54) = _m[a2 + 1];
        F(0x58) = _m[a2 + 2];
        F(0x5C) = _m[a2 + 3];
        F(0x70) = _m[a2 + 4];
        F(0x68) = _m[a2 + 5];
        if (_m[a2 + 6] != 0)
        {
            F(0x1C) = _m[a2 + 6];
            F(0x18) = 0x7C;
            _m[ArpMask] = _m[a2 + 6];
            BuildArpeggio();
        }
        if ((_m[a2 + 7] & 8) != 0)
            F(0x27) = 1; // the program writes $27(a0): voice v + 3's portamento target
    }

    /// <summary>+0x808E: the arpeggio buffer from the mask (a leading 0, the set bits' intervals, 0xFF).</summary>
    private void BuildArpeggio()
    {
        int d4 = (byte)(F(0x04) + 1);
        for (int d5 = 0; d5 < 8; d5++)
        {
            byte mask = _m[ArpMask];
            if ((mask & 1) != 0)
                _m[ArpBuf + d4++] = _m[ArpIntervals + d5];
            _m[ArpMask] = (byte)(mask >> 1);
        }
        _m[ArpBuf + d4] = 0xFF;
        F(0x24) = 0;
    }

    /// <summary>+0x8290: the note's period as base and output.</summary>
    private void SetPeriod()
    {
        int p = W(Periods + (byte)(F(0x08) << 1));
        W(BasePeriod + L(V2), p);
        W(OutPeriod + L(V2), p);
    }

    /// <summary>+0x7CB8: vibrato, arpeggio, portamento; then the envelope.</summary>
    private void Effects()
    {
        if (F(0x08) == 0)
        {
            Envelope();
            return;
        }
        if (F(0x18) == 0)
        {
            if (F(0x68) != 0)
            {
                Vibrato();
                Envelope();
                return;
            }
            F(0x80) = F(0x4E); // the program reads $4E(a0): another voice's byte
        }
        if (F(0x1C) != 0)
            Arpeggio();
        else if (F(0x24) != 0)
        {
            Portamento();
            if (F(0x80) != 0)
                Vibrato();
        }
        Envelope();
    }

    /// <summary>+0x7CD8.</summary>
    private void Vibrato()
    {
        byte x = F(0x68);
        _m[VibSpeed] = (byte)(x & 0x0F);
        _m[VibDepth] = (byte)(x >> 4);
        int i = (byte)(F(0x08) << 1);
        W(VibStep, W(Periods + i) - W(Periods + i + 2));
        while (true)
        {
            _m[VibSpeed]--;
            if ((sbyte)_m[VibSpeed] < 0)
                break;
            W(VibStep, (W(VibStep) >> 1) | 1);
        }
        bool up = true;
        if ((sbyte)F(0x3C) < 0)
        {
            F(0x40)--;
            if (F(0x40) != 0)
                up = false;
            else
            {
                F(0x3C)++;
                if ((sbyte)F(0x3C) >= 0)
                    up = false;
            }
        }
        if (up)
        {
            F(0x40)++;
            if (F(0x40) > _m[VibDepth])
                F(0x3C)--;
        }
        if (F(0x48) != 0)
        {
            F(0x48)--;
            return;
        }
        W(VibWork, W(BasePeriod + L(V2)));
        for (int k = (_m[VibDepth] >> 1) - 1; k >= 0; k--)
            W(VibWork, W(VibWork) + W(VibStep));
        int d4 = F(0x40);
        while (true)
        {
            d4--;
            if ((sbyte)(d4 & 0xFF) < 0)
                break;
            W(VibWork, W(VibWork) - W(VibStep));
        }
        W(OutPeriod + L(V2), W(VibWork));
    }

    /// <summary>+0x7E02.</summary>
    private void Arpeggio()
    {
        while (true)
        {
            byte x = _m[ArpBuf + F(0x38)];
            if (x == 0xFF)
            {
                F(0x38) = F(0x04);
                continue;
            }
            int i = (byte)((byte)(x + F(0x08)) << 1);
            W(OutPeriod + L(V2), W(Periods + i));
            F(0x38)++;
            return;
        }
    }

    /// <summary>+0x7E5A (with +0x80F2): glide from the note to the target between the delay and delay + length rows.</summary>
    private void Portamento()
    {
        _portaTarget = F(0x24);
        _portaLen = (byte)(F(0x28) & 0x0F);
        byte d0 = (byte)((F(0x28) >> 4) + F(0x14));
        if ((sbyte)d0 > (sbyte)F(0x10))
            return;
        d0 += _portaLen;
        if (d0 <= F(0x10))
        {
            W(BasePeriod + L(V2), W(TargetPeriod + L(V2)));
            return;
        }
        byte note = F(0x08);
        int target = W(Periods + (byte)(_portaTarget << 1));
        W(TargetPeriod + L(V2), target);
        int from = W(Periods + (byte)(note << 1));
        if (note < _portaTarget)
        {
            _m[SlideDir] = 1;
            W(SlideStep, from - target);
        }
        else
        {
            _m[SlideDir] = 0;
            W(SlideStep, target - from);
        }
        byte div = 0;
        for (int k = _m[Speed]; k >= 0; k--)
            div += _portaLen;
        if (div != 0)
            W(SlideStep, W(SlideStep) / div); // `[?]` 0 would trap on the 68000; unreachable (length 0 snaps)
        int b = W(BasePeriod + L(V2));
        b = _m[SlideDir] != 0 ? b - W(SlideStep) : b + W(SlideStep);
        W(BasePeriod + L(V2), b);
        W(OutPeriod + L(V2), W(BasePeriod + L(V2)));
    }

    /// <summary>+0x7F0E: attack, sustain, release.</summary>
    private void Envelope()
    {
        if (F(0x14) == 0)
            return;
        if (F(0x6C) == 0)
        {
            if (F(0x70) == 0)
            {
                F(0x6C) = 1;
                return;
            }
            if (F(0x70) == 0xFF)
                return;
            byte q = (byte)(F(0x10) / F(0x70));
            if ((byte)(F(0x14) - q) != 0)
            {
                if (F(0x60) != F(0x54))
                    F(0x60) += F(0x58);
                return;
            }
        }
        F(0x6C) = 1;
        byte v = (byte)(F(0x60) - F(0x5C));
        if (v == 0 || (sbyte)v < 0)
        {
            F(0x60) = 0;
            _m[DmaOff] |= (byte)(1 << _v);
        }
        else
            F(0x60) = v;
    }

    /// <summary>+0x82BE: periods, DMA, volumes.</summary>
    private void Output()
    {
        for (int c = 0; c < 4; c++)
            _out.Word(0xA6 + 0x10 * c, W(OutPeriod + 2 * c));
        int d1 = (~_m[DmaOff] & 0x0F) | 0x8200;
        _out.Word(0x96, d1);
        _out.Word(0x96, d1 ^ 0x820F);
        for (int c = 0; c < 4; c++)
            _out.Word(0xA8 + 0x10 * c, _m[Volumes + c]);
    }
}
