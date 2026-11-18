namespace GoldBox.Core.Sound;

/// <summary>Where the driver's port writes go: the PC speaker (PIT channel 2 at 42h, gate bits at 61h) and the Tandy
/// SN76489 at C0h.</summary>
public interface ISoundPorts
{
    void Out(int port, byte value);
}

/// <summary>Which device the driver's tunes play on (POOL.CFG's sound letter, START 0x8AE:0x00DC).</summary>
public enum PcSoundDevice
{
    /// <summary>'T': the Tandy SN76489 (sound mode 0).</summary>
    Tandy = 0,
    /// <summary>'P': the PC speaker (sound mode 1).</summary>
    Speaker = 1,
}

/// <summary>
/// PoR's PC-speaker / Tandy sequencer (START.EXE segment 0x08AE in v1.3; docs/re/sound.md §2), our own
/// implementation of what the v1.3 code does, over the driver's data from the player's program
/// (<see cref="PcSoundDriverImage"/>). It works on a 64 KB copy of the driver's segment, so stream, envelope and LFO
/// pointers are used as the program uses them. <see cref="Tick"/> is one timer interrupt (CS:13EA): both devices' four
/// voices are stepped and written to the ports, and every <see cref="PcSoundDriverImage.BiosChainTicks"/>th tick the
/// BIOS clock advances (<see cref="Tick"/> returns true). Checked port write for port write against the program run
/// under emulation (tools/scripts/por_sound_oracle.py, <c>SoundDriverProgramTests</c>).
/// </summary>
public sealed class PcSoundDriver
{
    // Voice block fields (word offsets, docs/re/sound.md §2.2).
    private const int Duration = 0x00, Stream = 0x02, Period = 0x04, PeriodAdd = 0x06, OutPeriod = 0x08, Level = 0x0A,
        LevelAdd = 0x0C, Tempo = 0x0E, Transpose = 0x12, GateLen = 0x14, EnvBase = 0x16, EnvPos = 0x18,
        EnvWait = 0x1A, LfoTable = 0x1C, LfoPhase = 0x1E, LfoRate = 0x20, LfoDepth = 0x22, LfoWrap = 0x24;
    // SRESET clears these fields; duration, stream, tempo, gate length and the two loop counters survive.
    private static readonly int[] ResetFields =
        [Period, PeriodAdd, OutPeriod, Level, LevelAdd, 0x10, Transpose, EnvBase, EnvPos, EnvWait, LfoTable, LfoPhase,
         LfoRate, LfoDepth, LfoWrap];

    private readonly byte[] _m = new byte[0x10000];
    private readonly PcSoundDriverImage _d;
    private readonly ISoundPorts _ports;
    private int _curTune, _spkrVoice, _voiceLeft, _seqVoice, _voiceBase, _freqBase, _seqRet;
    private int _si;
    private byte _port61;

    public PcSoundDriver(PcSoundDriverImage image, ISoundPorts ports)
    {
        image.Validate();
        _d = image;
        _ports = ports;
        image.Bytes.CopyTo(_m, image.Origin);
        DivCount = image.Bytes[0];
    }

    public PcSoundDriverImage Image => _d;

    /// <summary>Ticks left before the next BIOS clock tick (CS:01D7; the data's first byte, 13 at start-up).</summary>
    public int DivCount { get; private set; }

    /// <summary>The tune last programmed (CS:01FF); 0 = silent.</summary>
    public int CurrentTune => _curTune;

    /// <summary>Output gate (CS:020D, 1 at start-up): 0 mutes both devices while the voices keep running.</summary>
    public bool OutputOn { get; set; } = true;

    /// <summary>Whether a voice of <paramref name="device"/> still has a stream to run.</summary>
    public bool Busy(PcSoundDevice device)
    {
        int b = device == PcSoundDevice.Speaker ? _d.SpeakerVoices : _d.TandyVoices;
        for (int v = 0; v < 4; v++)
            if (W(b + v * _d.VoiceSize + Stream) != 0 || W(b + v * _d.VoiceSize + Duration) != 0)
                return true;
        return false;
    }

    private int W(int a) => _m[a & 0xFFFF] | _m[(a + 1) & 0xFFFF] << 8;
    private void SetW(int a, int v)
    {
        _m[a & 0xFFFF] = (byte)v;
        _m[(a + 1) & 0xFFFF] = (byte)(v >> 8);
    }
    private int B(int a) => _m[a & 0xFFFF];

    /// <summary>
    /// Starts tune <paramref name="tune"/> on <paramref name="device"/> (CS:1742 speaker, CS:17AE Tandy): every voice
    /// the tune's row gives a stream is cleared, pointed at it and armed for the next tick; a voice whose entry is 0
    /// keeps running.
    /// </summary>
    public void Program(PcSoundDevice device, int tune)
    {
        _curTune = tune & 0xFFFF;
        int row = (device == PcSoundDevice.Speaker ? _d.SpeakerSongs : _d.TandySongs) + (_curTune << 3);
        int di = device == PcSoundDevice.Speaker ? _d.SpeakerVoices : _d.TandyVoices;
        for (int v = 0; v < 4; v++, di += _d.VoiceSize, row += 2)
        {
            int stream = W(row);
            if (stream == 0)
                continue;
            for (int k = _d.VoiceSize - 2; k >= 0; k -= 2)
                SetW(di + k, 0);
            SetW(di + Stream, stream);
            SetW(di + Duration, 1);
        }
    }

    /// <summary>
    /// One timer interrupt: the speaker's voices, then the Tandy's, then the BIOS-chain count. Returns true when this
    /// tick chains the BIOS handler (the DOS clock advances).
    /// </summary>
    public bool Tick()
    {
        TickSpeaker();
        TickTandy();
        if (--DivCount != 0)
            return false;
        DivCount = _d.BiosChainTicks;
        return true;
    }

    private void TickSpeaker()
    {
        int bl = 0;
        if (_curTune != 0)
        {
            _spkrVoice = 0;
            _voiceLeft = 4;
            _freqBase = _d.SpeakerNotes;
            _voiceBase = _d.SpeakerVoices;
            for (_si = _d.SpeakerVoices; ; )
            {
                if (W(_si + Duration) != 0)
                {
                    VoiceStep();
                    if (_spkrVoice == 0 && W(_si + Level) != 0 && W(_si + Duration) != 0)
                        _spkrVoice = _si;
                }
                _si += _d.VoiceSize;
                if (--_voiceLeft == 0)
                    break;
            }
            if (OutputOn && _spkrVoice != 0)
            {
                int p = W(_spkrVoice + OutPeriod);
                _ports.Out(0x42, (byte)p);
                _ports.Out(0x42, (byte)(p >> 8));
                bl = W(_spkrVoice + Level) & 3;
            }
        }
        _port61 = (byte)(_port61 & 0xFC | bl);
        _ports.Out(0x61, _port61);
    }

    private void TickTandy()
    {
        if (_curTune == 0 || !OutputOn)
        {
            if (_curTune != 0)
                StepTandyVoices();
            TandyOff();
            return;
        }
        StepTandyVoices();
        int si = _d.TandyVoices;
        for (int dl = 0x80, n = 0; n < 4; n++, dl += 0x20, si += _d.VoiceSize)
        {
            int ax = W(si + OutPeriod) >> 2;
            int al = (ax & 0xFF) >> 4, ah = ax >> 8;
            _ports.Out(0xC0, (byte)(al ^ dl));
            if (dl != 0xE0)
                _ports.Out(0xC0, (byte)ah);
            int att = ((0xFFFF - W(si + Level)) & 0xFFFF) >> 12;
            _ports.Out(0xC0, (byte)((att ^ dl) + 0x10));
        }
    }

    private void StepTandyVoices()
    {
        _voiceLeft = 4;
        _freqBase = _d.TandyNotes;
        _voiceBase = _d.TandyVoices;
        for (_si = _d.TandyVoices; ; )
        {
            if (W(_si + Duration) != 0)
                VoiceStep();
            _si += _d.VoiceSize;
            if (--_voiceLeft == 0)
                break;
        }
    }

    private void TandyOff()
    {
        _ports.Out(0xC0, 0x9F);
        _ports.Out(0xC0, 0xBF);
        _ports.Out(0xC0, 0xDF);
        _ports.Out(0xC0, 0xFF);
    }

    /// <summary>Silences both devices the way the timer uninstall does (CS:1391): speaker gate bits off, Tandy full
    /// attenuation. The voices keep their state.</summary>
    public void Uninstall()
    {
        _port61 &= 0xFC;
        _ports.Out(0x61, _port61);
        TandyOff();
    }

    /// <summary>One tick of the voice at <see cref="_si"/> (CS:157A).</summary>
    private void VoiceStep()
    {
        int si = _si;
        SetW(si + Level, W(si + Level) + W(si + LevelAdd));
        SetW(si + Period, W(si + Period) + W(si + PeriodAdd));
        int dx = 0;
        int ax = (W(si + LfoPhase) + W(si + LfoRate)) & 0xFFFF;
        if (ax != 0)
        {
            if (ax >= W(si + LfoWrap))
                ax = (ax - W(si + LfoWrap)) & 0xFFFF;
            SetW(si + LfoPhase, ax);
            int idx = ((short)ax >> 4) + W(si + LfoTable);
            int sample = (sbyte)B(idx);
            int prod = (short)(sample << 8) * (short)W(si + LfoDepth);
            dx = (prod >> 16) & 0xFFFF;
        }
        SetW(si + OutPeriod, dx + W(si + Period));
        if (W(si + GateLen) != 0)
        {
            SetW(si + GateLen, W(si + GateLen) - 1);
            if (W(si + GateLen) == 0)
            {
                SetW(si + EnvPos, 0x10);
                SetW(si + EnvWait, 1);
            }
        }
        SetW(si + Duration, W(si + Duration) - 1);
        if (W(si + Duration) == 0)
            SeqProc();
        si = _si;
        if (W(si + EnvWait) == 0)
            return;
        SetW(si + EnvWait, W(si + EnvWait) - 1);
        if (W(si + EnvWait) != 0)
            return;
        while (true)
        {
            int bx = W(si + EnvBase) + W(si + EnvPos);
            if (W(bx + 2) == 0xFFFF)
            {
                int lv = W(bx);
                SetW(si + Level, lv);
                if (lv == 0)
                    SetW(si + LevelAdd, 0);
                SetW(si + EnvPos, W(si + EnvPos) + 4);
                continue;
            }
            SetW(si + LevelAdd, W(bx));
            SetW(si + EnvWait, W(bx + 2));
            SetW(si + EnvPos, W(si + EnvPos) + 4);
            return;
        }
    }

    /// <summary>Runs the voice's stream until it yields (CS:160B).</summary>
    private void SeqProc()
    {
        int di = W(_si + Stream);
        if (di == 0)
            return;
        _seqVoice = _si;
        while (true)
        {
            int bl = B(di++);
            if (bl < 0xFA)
            {
                if (Note(bl, ref di))
                    break;
                continue;
            }
            switch (bl)
            {
                case 0xFE: // SLOOP field, target: jump unless the counter decrements to zero
                {
                    int f = B(di++);
                    int target = W(di);
                    di += 2;
                    int a = _si + f;
                    if (W(a) != 0)
                    {
                        SetW(a, W(a) - 1);
                        if (W(a) == 0)
                            continue;
                    }
                    di = target;
                    continue;
                }
                case 0xFD: // SCLEAR voice: continue on another voice block of the device, cleared
                    _si = (W(di) + _voiceBase) & 0xFFFF;
                    di += 2;
                    Reset();
                    continue;
                case 0xFA: // SRESET
                    Reset();
                    continue;
                case 0xFF: // SSET field, value; field 0 (duration) yields
                {
                    int f = B(di++);
                    SetW(_si + f, W(di));
                    di += 2;
                    if (f != 0)
                        continue;
                    SeqEnd(di);
                    return;
                }
                case 0xFB: // SRET
                    di = _seqRet;
                    continue;
                default: // 0xFC SCALL
                    _seqRet = (di + 2) & 0xFFFF;
                    di = W(di);
                    continue;
            }
        }
        SeqEnd(di);
    }

    private void Reset()
    {
        foreach (int f in ResetFields)
            SetW(_si + f, 0);
    }

    private void SeqEnd(int di)
    {
        _si = _seqVoice;
        if (W(_si + Duration) == 0)
            di = 0;
        SetW(_si + Stream, di & 0xFFFF);
    }

    /// <summary>
    /// A note record (CS:16C1): voice index in bits 5–7, then the last flag and pitch, then the duration. Returns true
    /// when the record carried the last flag (the stream yields).
    /// </summary>
    private bool Note(int first, ref int di)
    {
        while (true)
        {
            int bx = di; // the record's second byte
            int cl = first >> 5;
            int target = (cl * _d.VoiceSize + _voiceBase) & 0xFFFF;
            int tempo = W(_si + Tempo);
            int dur = (tempo & 0xFF) > 1 ? (tempo & 0xFF) * B(bx + 1) : B(bx + 1);
            SetW(_si + Duration, dur);
            int pitch = B(bx) & 0x7F;
            if (pitch != 0x7F && cl == 0)
            {
                SetW(target + Duration, dur);
                SetW(target + GateLen, dur);
                int ax = (pitch + W(target + Transpose)) & 0xFFFF;
                SetW(target + EnvPos, 0);
                SetW(target + EnvWait, 1);
                // Octave = how many times 12 comes off before a borrow (CL counts from 0FFh, 8 bits); the shift count
                // is masked to 5 bits as on a 286 or later.
                int oct = 0xFF;
                while (true)
                {
                    oct = (oct + 1) & 0xFF;
                    bool borrow = ax < 12;
                    ax = (ax - 12) & 0xFFFF;
                    if (borrow)
                        break;
                }
                ax = (ax + 12) & 0xFFFF;
                int sh = oct & 0x1F;
                int p = sh >= 16 ? 0 : W(_freqBase + ax * 2) >> sh;
                SetW(target + Period, p);
                SetW(target + OutPeriod, p);
            }
            int last = B(bx) & 0x80;
            di = bx + 2;
            if (last != 0)
                return true;
            first = B(di++);
        }
    }
}
