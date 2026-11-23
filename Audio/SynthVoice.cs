using FLLiteStudio.Core;

namespace FLLiteStudio.Audio;

public sealed class SynthVoice
{
    public bool Active;
    public int MixerTrack;
    public InstrumentType Type;
    public DrumKind DrumKind;
    public KeysMode KeysMode;
    public PluckStyle PluckStyle;
    public WindStyle WindStyle;
    public PadStyle PadStyle;
    public MalletStyle MalletStyle;
    public WaveKind Wave1, Wave2;

    private float _sr = WinMmAudioOut.SampleRate;
    private int _ageSamples;
    private int _noteSamples;
    private int _releaseSamples;
    private bool _inRelease;

    private float _baseFreq;
    private float _velocity;
    private float _gainL, _gainR;

    // Synth params snapshot at trigger time
    private float _attack, _decay, _sustain, _release;
    private float _cutoff, _res, _filterEnv;
    private float _drive, _punch, _tone, _detune;
    private float _fmRatio, _fmIndex, _fmDecay;
    private float _vibDepth, _vibRate;
    private int _coarse2;
    private float _osc2Mix;

    // Oscillator & envelope state
    private float _env;
    private float _p1, _p2, _p3, _p4, _p5, _pSub, _pMod, _pLfo;
    private readonly StereoBiquad _filter = new();
    private readonly StereoBiquad _hpFilter = new();
    private readonly StereoBiquad _formant2 = new();
    private FastRng _rng = new(0x5A17E123);

    // Karplus-Strong physical string buffer (8 KB pre-allocated -> ZERO GC!)
    private readonly float[] _ksBuf = new float[2048];
    private int _ksLen = 100;
    private int _ksIdx;
    private float _ksPrev;
    private float _ksDamp = 0.994f;

    // Sampler / Audio clip state
    private LoadedSample? _sample;
    private double _samplePos;
    private double _sampleStep;
    private int _sampleStartIdx, _sampleEndIdx;
    private bool _sampleReverse;
    private bool _isAudioClip;
    private bool _isMetronome;
    private bool _metroAccent;

    public int AgeSamples => _ageSamples;

    public void TriggerInstrument(
        ChannelModel ch,
        int midi,
        float velocity,
        double durationSec,
        float panOffset,
        LoadedSample? sample,
        bool ecoMode)
    {
        Active = true;
        _isAudioClip = false;
        _isMetronome = false;
        MixerTrack = Math.Clamp(ch.MixerTrack, 0, 12);
        Type = ch.Type;
        DrumKind = ch.DrumKind;
        KeysMode = ch.KeysMode;
        PluckStyle = ch.PluckStyle;
        WindStyle = ch.WindStyle;
        PadStyle = ch.PadStyle;
        MalletStyle = ch.MalletStyle;
        Wave1 = ch.Wave1;
        Wave2 = ch.Wave2;

        _ageSamples = 0;
        _inRelease = false;
        _env = 0f;
        _p1 = _p2 = _p3 = _p4 = _p5 = _pSub = _pMod = _pLfo = 0f;
        _filter.Reset();
        _hpFilter.Reset();
        _formant2.Reset();

        _baseFreq = MusicTheory.MidiToFreq(midi + ch.PitchSemitones);
        _velocity = Math.Clamp(velocity * ch.Volume, 0.01f, 1.5f);

        float pan = Math.Clamp(ch.Pan + panOffset, -1f, 1f);
        float angle = (pan + 1f) * 0.25f * MathF.PI;
        _gainL = MathF.Cos(angle) * 1.15f;
        _gainR = MathF.Sin(angle) * 1.15f;

        _attack = Math.Clamp(ch.Attack, 0.002f, 1.8f);
        _decay = Math.Clamp(ch.Decay, 0.03f, 3.5f);
        _sustain = Math.Clamp(ch.Sustain, 0.02f, 1.0f);
        _release = Math.Clamp(ch.Release, 0.02f, 2.5f);
        _cutoff = Math.Clamp(ch.CutoffHz, 80f, 18500f);
        _res = Math.Clamp(ch.Resonance, 0.5f, 12f);
        _filterEnv = Math.Clamp(ch.FilterEnv, 0f, 10000f);
        _drive = Math.Clamp(ch.Drive, 0f, 1f);
        _punch = Math.Clamp(ch.Punch, 0f, 1f);
        _tone = Math.Clamp(ch.Tone, 0f, 1f);
        _detune = Math.Clamp(ch.Detune, 0f, 55f);
        _fmRatio = Math.Clamp(ch.FmRatio, 0.25f, 12f);
        _fmIndex = Math.Clamp(ch.FmIndex, 0f, 10f);
        _fmDecay = Math.Clamp(ch.FmDecay, 0.02f, 2.5f);
        _vibDepth = Math.Clamp(ch.VibratoDepth, 0f, 1f);
        _vibRate = Math.Clamp(ch.VibratoRateHz, 0.5f, 12f);
        _coarse2 = ch.Coarse2;
        _osc2Mix = Math.Clamp(ch.Osc2Mix, 0f, 1f);

        _noteSamples = Math.Max(64, (int)(durationSec * _sr));
        _releaseSamples = Math.Max(64, (int)(_release * _sr));

        if (Type == InstrumentType.DrumSynth)
        {
            _noteSamples = (int)(Math.Clamp(_decay, 0.04f, 1.8f) * _sr);
            if (DrumKind == DrumKind.Crash) _noteSamples = Math.Max(_noteSamples, (int)(0.85f * _sr));
            if (DrumKind == DrumKind.ClosedHat) _noteSamples = Math.Min(_noteSamples, (int)(0.11f * _sr));
            if (DrumKind == DrumKind.Shaker) _noteSamples = Math.Min(_noteSamples, (int)(0.14f * _sr));
        }
        else if (Type == InstrumentType.PluckedString)
        {
            InitKarplusStrong();
        }
        else if (Type == InstrumentType.WavSampler && sample != null && sample.Left.Length > 0)
        {
            _sample = sample;
            _sampleReverse = ch.Reverse;
            _sampleStartIdx = Math.Clamp((int)(ch.TrimStart * sample.Left.Length), 0, sample.Left.Length - 1);
            _sampleEndIdx = Math.Clamp((int)(ch.TrimEnd * sample.Left.Length), _sampleStartIdx + 1, sample.Left.Length);
            _samplePos = _sampleReverse ? _sampleEndIdx - 1 : _sampleStartIdx;
            double pitchRatio = Math.Pow(2.0, (midi - 60 + ch.PitchSemitones) / 12.0);
            _sampleStep = pitchRatio * (sample.SampleRate / (double)_sr);
        }
        else if (Type == InstrumentType.WavSampler)
        {
            Active = false;
        }
    }

    private void InitKarplusStrong()
    {
        float freq = Math.Clamp(_baseFreq, 28f, 3500f);
        _ksLen = Math.Clamp((int)MathF.Round(_sr / freq), 16, _ksBuf.Length - 1);
        _ksIdx = 0;
        _ksPrev = 0f;

        // Excitation burst tailored by string instrument style
        float lp = 0f;
        float smooth = PluckStyle switch
        {
            PluckStyle.AcousticNylon => 0.45f,
            PluckStyle.ElectricBass => 0.25f,
            PluckStyle.Harp => 0.40f,
            PluckStyle.Pizzicato => 0.50f,
            PluckStyle.Koto => 0.90f,
            _ => 0.72f
        };
        float mean = 0f;
        for (int i = 0; i < _ksLen; i++)
        {
            float n = _rng.NextFloat();
            // Mix triangle body with pick noise for warm acoustic fundamental
            float tri = DspMath.Triangle(i / (float)_ksLen) * 0.35f;
            lp += smooth * (n - lp);
            float val = lp * 0.75f + tri;
            _ksBuf[i] = val;
            mean += val;
        }
        // Remove DC offset from excitation
        mean /= _ksLen;
        for (int i = 0; i < _ksLen; i++) _ksBuf[i] -= mean;

        float baseDamp = PluckStyle switch
        {
            PluckStyle.Pizzicato => 0.976f,
            PluckStyle.Koto => 0.986f,
            PluckStyle.AcousticNylon => 0.9925f,
            PluckStyle.Harp => 0.9965f,
            PluckStyle.ElectricRock => 0.9960f,
            _ => 0.9945f
        };
        _ksDamp = Math.Clamp(baseDamp + (_decay - 1.2f) * 0.004f, 0.955f, 0.9985f);
        _noteSamples = Math.Max(_noteSamples, (int)(Math.Clamp(_decay, 0.25f, 3.0f) * _sr));
    }

    public void TriggerAudioClip(LoadedSample sample, float volume, int mixerTrack, double maxDurationSec)
    {
        if (sample.Left.Length == 0) return;
        Active = true;
        _isAudioClip = true;
        _isMetronome = false;
        MixerTrack = Math.Clamp(mixerTrack, 0, 12);
        _sample = sample;
        _sampleReverse = false;
        _sampleStartIdx = 0;
        _sampleEndIdx = sample.Left.Length;
        _samplePos = 0;
        _sampleStep = sample.SampleRate / (double)_sr;
        _velocity = Math.Clamp(volume, 0f, 1.5f);
        _gainL = _gainR = 1f;
        _ageSamples = 0;
        _noteSamples = (int)(maxDurationSec * _sr);
    }

    public void TriggerMetronome(bool accent)
    {
        Active = true;
        _isMetronome = true;
        _isAudioClip = false;
        MixerTrack = 0;
        _metroAccent = accent;
        _p1 = 0f;
        _ageSamples = 0;
        _noteSamples = (int)(0.035f * _sr);
    }

    public void RenderInto(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        if (!Active) return;

        if (_isMetronome)
        {
            float freq = _metroAccent ? 1500f : 980f;
            float dt = freq / _sr;
            for (int i = 0; i < frames; i++)
            {
                if (_ageSamples >= _noteSamples) { Active = false; return; }
                float env = 1f - (_ageSamples / (float)_noteSamples);
                float s = DspMath.Triangle(_p1) * env * 0.24f;
                _p1 = (_p1 + dt) % 1f;
                busL[i] += s;
                busR[i] += s;
                _ageSamples++;
            }
            return;
        }

        if (_isAudioClip && _sample != null)
        {
            var sL = _sample.Left;
            var sR = _sample.Right;
            for (int i = 0; i < frames; i++)
            {
                int idx = (int)_samplePos;
                if (idx >= _sampleEndIdx - 1 || _ageSamples >= _noteSamples) { Active = false; return; }
                float frac = (float)(_samplePos - idx);
                float l = (sL[idx] + (sL[idx + 1] - sL[idx]) * frac) * _velocity;
                float r = (sR[idx] + (sR[idx + 1] - sR[idx]) * frac) * _velocity;
                busL[i] += l;
                busR[i] += r;
                _samplePos += _sampleStep;
                _ageSamples++;
            }
            return;
        }

        switch (Type)
        {
            case InstrumentType.DrumSynth: RenderDrum(busL, busR, frames); break;
            case InstrumentType.Bass808: RenderBass808(busL, busR, frames); break;
            case InstrumentType.Osc3x: RenderOsc3x(busL, busR, frames, ecoMode); break;
            case InstrumentType.SytrusFM: RenderSytrus(busL, busR, frames); break;
            case InstrumentType.SuperSaw: RenderSuperSaw(busL, busR, frames, ecoMode); break;
            case InstrumentType.KeysPiano: RenderKeys(busL, busR, frames, ecoMode); break;
            case InstrumentType.PluckedString: RenderPluckedString(busL, busR, frames); break;
            case InstrumentType.BrassWind: RenderBrassWind(busL, busR, frames, ecoMode); break;
            case InstrumentType.StringsPad: RenderStringsPad(busL, busR, frames, ecoMode); break;
            case InstrumentType.WorldMallet: RenderWorldMallet(busL, busR, frames, ecoMode); break;
            case InstrumentType.WavSampler: RenderSampler(busL, busR, frames); break;
        }
    }

    private float NextAdsr(int totalNoteSamples)
    {
        if (!_inRelease && _ageSamples >= totalNoteSamples) _inRelease = true;

        if (!_inRelease)
        {
            float t = _ageSamples / _sr;
            if (t < _attack)
            {
                _env = t / MathF.Max(0.001f, _attack);
            }
            else
            {
                float dt = (t - _attack) / MathF.Max(0.01f, _decay);
                _env = _sustain + (1f - _sustain) * MathF.Exp(-dt * 3.5f);
            }
        }
        else
        {
            float relRate = 1f / MathF.Max(16f, _releaseSamples);
            _env -= _env * relRate * 4.5f;
            if (_env < 0.0008f)
            {
                _env = 0f;
                Active = false;
            }
        }
        return _env;
    }

    // ========================================================================
    // 1. EXPANDED DRUM SYNTH (Trap, 808, Boom-Bap, Reggaetón, Phonk Cowbell)
    // ========================================================================
    private void RenderDrum(float[] busL, float[] busR, int frames)
    {
        float pitchMult = _baseFreq / 261.6256f;
        for (int i = 0; i < frames; i++)
        {
            if (_ageSamples >= _noteSamples) { Active = false; return; }
            float t = _ageSamples / _sr;
            float norm = _ageSamples / (float)_noteSamples;
            float s = 0f;

            switch (DrumKind)
            {
                case DrumKind.Kick:
                case DrumKind.Kick808:
                case DrumKind.KickBoomBap:
                {
                    float baseF = (DrumKind == DrumKind.Kick808 ? 44f : DrumKind == DrumKind.KickBoomBap ? 58f : 52f) * pitchMult;
                    float sweep = MathF.Exp(-t * (28f + _punch * 36f));
                    float freq = baseF + (115f + _punch * 135f) * sweep * pitchMult;
                    _p1 = (_p1 + freq / _sr) % 1f;
                    float env = MathF.Exp(-t / MathF.Max(0.04f, _decay * 0.42f));
                    float click = t < 0.014f ? MathF.Sin(t * 2800f) * (1f - t / 0.014f) * _tone * 0.45f : 0f;
                    float bodyWave = DrumKind == DrumKind.KickBoomBap
                        ? MathF.Sin(_p1 * MathF.Tau) * 0.85f + DspMath.Triangle(_p1) * 0.25f
                        : MathF.Sin(_p1 * MathF.Tau);
                    s = (bodyWave + click) * env * 1.15f;
                    if (_drive > 0.05f) s = DspMath.FastTanh(s * (1f + _drive * 4.2f));
                    break;
                }
                case DrumKind.Snare:
                case DrumKind.SnareTrap:
                case DrumKind.SnareReggaeton:
                {
                    float startB = DrumKind == DrumKind.SnareTrap ? 290f : DrumKind == DrumKind.SnareReggaeton ? 265f : 240f;
                    float endB = DrumKind == DrumKind.SnareTrap ? 175f : DrumKind == DrumKind.SnareReggaeton ? 165f : 140f;
                    float bodyF = (endB + (startB - endB) * MathF.Exp(-t * 48f)) * pitchMult;
                    _p1 = (_p1 + bodyF / _sr) % 1f;
                    float bodyGain = DrumKind == DrumKind.SnareReggaeton ? 0.88f : 0.65f;
                    float body = MathF.Sin(_p1 * MathF.Tau) * MathF.Exp(-t * 26f) * bodyGain;
                    _hpFilter.Set(BiquadMode.HighPass, (DrumKind == DrumKind.SnareTrap ? 1350f : 850f) + _tone * 1500f, 0.9f);
                    float n = _hpFilter.ProcessMono(_rng.NextFloat()) * MathF.Exp(-t / MathF.Max(0.025f, _decay * 0.34f)) * 0.85f;
                    s = body + n;
                    break;
                }
                case DrumKind.Clap:
                {
                    _filter.Set(BiquadMode.BandPass, (1050f + _tone * 1200f) * pitchMult, 1.35f);
                    float burst = (t < 0.010f) ? 0.9f
                        : (t < 0.020f) ? 0.85f
                        : MathF.Exp(-(t - 0.020f) / MathF.Max(0.025f, _decay * 0.32f));
                    s = _filter.ProcessMono(_rng.NextFloat()) * burst * 1.35f;
                    break;
                }
                case DrumKind.Cowbell:
                {
                    // Classic TR-808 Phonk Cowbell: 540Hz + 800Hz dual square through resonant bandpass
                    float f1 = 540f * pitchMult;
                    float f2 = 800f * pitchMult;
                    _p1 = (_p1 + f1 / _sr) % 1f;
                    _p2 = (_p2 + f2 / _sr) % 1f;
                    float sq = (DspMath.SquareBlep(_p1, f1 / _sr) + DspMath.SquareBlep(_p2, f2 / _sr)) * 0.5f;
                    _filter.Set(BiquadMode.BandPass, 860f * pitchMult, 2.2f);
                    float env = MathF.Exp(-t / MathF.Max(0.04f, _decay * 0.38f));
                    s = _filter.ProcessMono(sq) * env * 1.65f;
                    if (_drive > 0.05f) s = DspMath.FastTanh(s * (1f + _drive * 4.5f));
                    break;
                }
                case DrumKind.ClosedHat:
                case DrumKind.OpenHat:
                case DrumKind.Crash:
                case DrumKind.Shaker:
                {
                    float hpFreq = DrumKind == DrumKind.Crash ? 4200f
                        : DrumKind == DrumKind.Shaker ? 5200f
                        : 6500f + _tone * 2800f;
                    _hpFilter.Set(BiquadMode.HighPass, hpFreq, 0.9f);
                    float env;
                    if (DrumKind == DrumKind.Shaker)
                    {
                        float atk = MathF.Min(1f, t / 0.022f);
                        env = atk * MathF.Exp(-t / 0.036f);
                    }
                    else
                    {
                        float tau = DrumKind == DrumKind.Crash ? 0.38f : DrumKind == DrumKind.OpenHat ? _decay * 0.5f : 0.024f;
                        env = MathF.Exp(-t / MathF.Max(0.012f, tau));
                    }
                    s = _hpFilter.ProcessMono(_rng.NextFloat()) * env * (DrumKind == DrumKind.Crash ? 0.62f : 0.74f);
                    break;
                }
                case DrumKind.Tom:
                {
                    float f = (165f * MathF.Exp(-t * 14f) + 75f) * pitchMult;
                    _p1 = (_p1 + f / _sr) % 1f;
                    float env = MathF.Exp(-t / MathF.Max(0.05f, _decay * 0.45f));
                    s = (MathF.Sin(_p1 * MathF.Tau) * 0.85f + DspMath.Triangle(_p1) * 0.2f) * env * 0.95f;
                    break;
                }
                default: // Rimshot / Perc
                {
                    float f0 = (DrumKind == DrumKind.Rimshot ? 820f : 410f) * pitchMult;
                    float f = f0 * MathF.Exp(-t * 24f) + 135f;
                    _p1 = (_p1 + f / _sr) % 1f;
                    float env = MathF.Exp(-t / (DrumKind == DrumKind.Rimshot ? 0.018f : 0.075f));
                    s = (DrumKind == DrumKind.Rimshot ? DspMath.SquareBlep(_p1, f / _sr) * 0.5f : MathF.Sin(_p1 * MathF.Tau)) * env * 0.85f;
                    break;
                }
            }

            if (norm > 0.94f) s *= (1f - norm) / 0.06f;
            float amp = s * _velocity;
            busL[i] += amp * _gainL;
            busR[i] += amp * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 2. 808 SUBLAB BASS
    // ========================================================================
    private void RenderBass808(float[] busL, float[] busR, int frames)
    {
        _filter.Set(BiquadMode.LowPass, _cutoff, 1.1f);
        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            float t = _ageSamples / _sr;
            float pitchDrop = 1f + _punch * 2.1f * MathF.Exp(-t * 55f);
            float f = _baseFreq * pitchDrop;
            _p1 = (_p1 + f / _sr) % 1f;

            float sub = MathF.Sin(_p1 * MathF.Tau);
            float overtone = DspMath.Triangle(_p1) * 0.22f;
            float raw = sub + overtone;
            if (_drive > 0.02f) raw = DspMath.FastTanh(raw * (1f + _drive * 6.5f));

            float filtered = _filter.ProcessMono(raw) * env * _velocity * 0.92f;
            busL[i] += filtered * _gainL;
            busR[i] += filtered * _gainR;
            _ageSamples++;
        }
    }

    private float EvalWave(WaveKind kind, float phase, float dt) => kind switch
    {
        WaveKind.Sine => MathF.Sin(phase * MathF.Tau),
        WaveKind.Triangle => DspMath.Triangle(phase),
        WaveKind.Square => DspMath.SquareBlep(phase, dt),
        _ => DspMath.SawBlep(phase, dt)
    };

    // ========================================================================
    // 3. 3xOsc SUBTRACTIVE
    // ========================================================================
    private void RenderOsc3x(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float f1 = _baseFreq;
        float f2 = _baseFreq * MathF.Pow(2f, (_coarse2 + _detune * 0.01f) / 12f);
        float dt1 = f1 / _sr;
        float dt2 = f2 / _sr;

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            if ((i & 15) == 0)
            {
                float cut = Math.Clamp(_cutoff + _filterEnv * env, 80f, 18500f);
                _filter.Set(BiquadMode.LowPass, cut, _res);
            }

            _p1 = (_p1 + dt1) % 1f;
            float sig = EvalWave(Wave1, _p1, dt1) * 0.65f;

            if (!ecoMode && _osc2Mix > 0.02f)
            {
                _p2 = (_p2 + dt2) % 1f;
                sig += EvalWave(Wave2, _p2, dt2) * _osc2Mix * 0.55f;
            }

            float outS = _filter.ProcessMono(sig) * env * _velocity * 0.55f;
            busL[i] += outS * _gainL;
            busR[i] += outS * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 4. SYTRUS FM
    // ========================================================================
    private void RenderSytrus(float[] busL, float[] busR, int frames)
    {
        float dtC = _baseFreq / _sr;
        float dtM = (_baseFreq * _fmRatio) / _sr;
        _filter.Set(BiquadMode.LowPass, _cutoff, 1.0f);

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            float t = _ageSamples / _sr;
            float modEnv = MathF.Exp(-t / MathF.Max(0.02f, _fmDecay));
            _pMod = (_pMod + dtM) % 1f;
            float mod = MathF.Sin(_pMod * MathF.Tau) * _fmIndex * modEnv;

            _p1 = (_p1 + dtC) % 1f;
            float carrier = MathF.Sin(_p1 * MathF.Tau + mod);
            float outS = _filter.ProcessMono(carrier) * env * _velocity * 0.52f;
            busL[i] += outS * _gainL;
            busR[i] += outS * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 5. FLEX SUPERSAW
    // ========================================================================
    private void RenderSuperSaw(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float d1 = MathF.Pow(2f, -_detune / 1200f);
        float d2 = MathF.Pow(2f, _detune / 1200f);
        float d3 = MathF.Pow(2f, -_detune * 0.45f / 1200f);
        float d4 = MathF.Pow(2f, _detune * 0.45f / 1200f);

        float dt0 = _baseFreq / _sr;
        float dt1 = dt0 * d1, dt2 = dt0 * d2, dt3 = dt0 * d3, dt4 = dt0 * d4;
        float dtSub = dt0 * 0.5f;

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            if ((i & 15) == 0)
            {
                float cut = Math.Clamp(_cutoff + _filterEnv * env, 120f, 18500f);
                _filter.Set(BiquadMode.LowPass, cut, _res);
            }

            _p1 = (_p1 + dt0) % 1f;
            _p2 = (_p2 + dt1) % 1f;
            _p3 = (_p3 + dt2) % 1f;
            _pSub = (_pSub + dtSub) % 1f;

            float sigL = DspMath.SawBlep(_p1, dt0) * 0.5f + DspMath.SawBlep(_p2, dt1) * 0.7f + MathF.Sin(_pSub * MathF.Tau) * 0.35f;
            float sigR = DspMath.SawBlep(_p1, dt0) * 0.5f + DspMath.SawBlep(_p3, dt2) * 0.7f + MathF.Sin(_pSub * MathF.Tau) * 0.35f;

            if (!ecoMode)
            {
                _p4 = (_p4 + dt3) % 1f;
                _p5 = (_p5 + dt4) % 1f;
                sigL += DspMath.SawBlep(_p4, dt3) * 0.55f;
                sigR += DspMath.SawBlep(_p5, dt4) * 0.55f;
            }

            _filter.Process(ref sigL, ref sigR);
            float g = env * _velocity * 0.25f;
            busL[i] += sigL * g * _gainL;
            busR[i] += sigR * g * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 6. EXPANDED FL KEYS (Piano, Rhodes, Wurlitzer, Organ, Harpsichord, Accordion, Celesta)
    // ========================================================================
    private void RenderKeys(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float dt = _baseFreq / _sr;
        float dt2 = dt * 2.002f;
        float dtTine = dt * 7f;
        float bright = Math.Clamp(_tone, 0.1f, 1f);
        _filter.Set(BiquadMode.LowPass, 1400f + bright * 6800f, 0.85f);

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            float t = _ageSamples / _sr;
            _p1 = (_p1 + dt) % 1f;
            _p2 = (_p2 + dt2) % 1f;

            float sig;
            switch (KeysMode)
            {
                case KeysMode.Rhodes:
                {
                    float tine = 0f;
                    if (!ecoMode && t < 0.09f)
                    {
                        _p3 = (_p3 + dtTine) % 1f;
                        tine = MathF.Sin(_p3 * MathF.Tau) * MathF.Exp(-t * 38f) * 0.16f * bright;
                    }
                    sig = MathF.Sin(_p1 * MathF.Tau) * 0.78f + DspMath.Triangle(_p2) * 0.24f * bright * MathF.Exp(-t * 3.5f) + tine;
                    break;
                }
                case KeysMode.Wurlitzer:
                {
                    _p3 = (_p3 + dt * 3f) % 1f;
                    float bark = MathF.Sin(_p1 * MathF.Tau) * 0.7f + DspMath.SquareBlep(_p2, dt2) * 0.18f * MathF.Exp(-t * 6f) + MathF.Sin(_p3 * MathF.Tau) * 0.16f;
                    sig = DspMath.FastTanh(bark * (1.2f + _drive * 2.2f));
                    break;
                }
                case KeysMode.BellKeys:
                {
                    _p3 = (_p3 + dt * 2.756f) % 1f;
                    sig = MathF.Sin(_p1 * MathF.Tau) * 0.65f + MathF.Sin(_p3 * MathF.Tau) * 0.35f * MathF.Exp(-t * 4f);
                    break;
                }
                case KeysMode.Organ:
                {
                    _p3 = (_p3 + dt * 4f) % 1f;
                    sig = MathF.Sin(_p1 * MathF.Tau) * 0.55f + MathF.Sin(_p2 * MathF.Tau) * 0.28f + MathF.Sin(_p3 * MathF.Tau) * 0.18f;
                    break;
                }
                case KeysMode.Harpsichord:
                {
                    _p3 = (_p3 + dt * 3f) % 1f;
                    sig = DspMath.SawBlep(_p1, dt) * 0.52f + DspMath.SquareBlep(_p2, dt2) * 0.26f + MathF.Sin(_p3 * MathF.Tau) * 0.16f;
                    break;
                }
                case KeysMode.Accordion:
                {
                    _p3 = (_p3 + dt * 1.007f) % 1f; // Classic musette detuned reed pair
                    sig = (DspMath.SawBlep(_p1, dt) + DspMath.SawBlep(_p3, dt * 1.007f)) * 0.36f + DspMath.Triangle(_p2) * 0.22f;
                    break;
                }
                default: // Grand Piano
                {
                    sig = DspMath.Triangle(_p1) * 0.62f + DspMath.SawBlep(_p2, dt2) * 0.28f * bright * MathF.Exp(-t * 2.8f);
                    break;
                }
            }

            float outS = _filter.ProcessMono(sig) * env * _velocity * 0.48f;
            busL[i] += outS * _gainL;
            busR[i] += outS * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 7. KARPLUS-STRONG PLUCKED STRINGS (Guitars, Electric Bass, Harp, Koto, Pizz)
    // ========================================================================
    private void RenderPluckedString(float[] busL, float[] busR, int frames)
    {
        float cut = Math.Clamp(_cutoff * (0.45f + _tone * 0.85f), 250f, 16000f);
        _filter.Set(BiquadMode.LowPass, cut, 0.75f);
        float blend = Math.Clamp(0.25f + _tone * 0.55f, 0.15f, 0.85f);

        for (int i = 0; i < frames; i++)
        {
            if (!_inRelease && _ageSamples >= _noteSamples) _inRelease = true;

            float cur = _ksBuf[_ksIdx];
            float filtered = (cur * blend + _ksPrev * (1f - blend)) * (_inRelease ? _ksDamp * 0.988f : _ksDamp);
            if (MathF.Abs(filtered) < 1e-12f) filtered = 0f;
            _ksPrev = cur;
            _ksBuf[_ksIdx] = filtered;
            if (++_ksIdx >= _ksLen) _ksIdx = 0;

            float sig = _filter.ProcessMono(cur);
            if (PluckStyle == PluckStyle.ElectricRock || _drive > 0.15f)
            {
                sig = DspMath.FastTanh(sig * (1.2f + _drive * 6.5f)) * 0.65f;
            }

            float amp = sig * _velocity * 0.92f;
            busL[i] += amp * _gainL;
            busR[i] += amp * _gainR;
            _ageSamples++;

            if (_inRelease && _ageSamples > _noteSamples + _releaseSamples)
            {
                Active = false;
                return;
            }
        }
    }

    // ========================================================================
    // 8. BRASS & WOODWINDS (Flute, Trap Flute, Trumpet, Brass Hit, Sax, Clarinet, Trombone)
    // ========================================================================
    private void RenderBrassWind(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float lfoDt = _vibRate / _sr;
        bool isFlute = WindStyle is WindStyle.Flute or WindStyle.DarkFlute;
        bool isClarinet = WindStyle == WindStyle.Clarinet;
        bool isBrass = WindStyle is WindStyle.Trumpet or WindStyle.TrapBrass or WindStyle.Trombone;

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            _pLfo = (_pLfo + lfoDt) % 1f;
            float vibRamp = MathF.Min(1f, (_ageSamples / _sr) * 3.5f);
            float vib = MathF.Sin(_pLfo * MathF.Tau) * _vibDepth * 0.014f * vibRamp;
            float freq = _baseFreq * (1f + vib);
            float dt = freq / _sr;

            _p1 = (_p1 + dt) % 1f;
            _p2 = (_p2 + dt * 2f) % 1f;

            float sig;
            if (isFlute)
            {
                float breath = _rng.NextFloat() * (WindStyle == WindStyle.DarkFlute ? 0.12f : 0.07f) * (0.4f + (1f - env) * 0.6f);
                sig = MathF.Sin(_p1 * MathF.Tau) * 0.78f + MathF.Sin(_p2 * MathF.Tau) * 0.14f + breath;
                if ((i & 15) == 0) _filter.Set(BiquadMode.LowPass, 1400f + _tone * 4600f, 1.1f);
            }
            else if (isClarinet)
            {
                // Hollow odd-harmonic square wave
                sig = DspMath.SquareBlep(_p1, dt) * 0.58f + MathF.Sin(_p1 * MathF.Tau) * 0.30f;
                if ((i & 15) == 0) _filter.Set(BiquadMode.LowPass, 900f + _tone * 2600f, 1.3f);
            }
            else if (WindStyle == WindStyle.Saxophone)
            {
                sig = DspMath.SawBlep(_p1, dt) * 0.52f + DspMath.SquareBlep(_p1, dt) * 0.22f;
                if ((i & 15) == 0) _filter.Set(BiquadMode.LowPass, 750f + env * 2400f * _tone, 2.2f);
            }
            else // Trumpet / TrapBrass / Trombone
            {
                _p3 = (_p3 + dt * 1.005f) % 1f;
                sig = DspMath.SawBlep(_p1, dt) * 0.55f + (ecoMode ? 0f : DspMath.SawBlep(_p3, dt * 1.005f) * 0.42f);
                if (WindStyle == WindStyle.TrapBrass)
                {
                    _pSub = (_pSub + dt * 0.5f) % 1f;
                    sig += DspMath.SawBlep(_pSub, dt * 0.5f) * 0.38f;
                }
                if ((i & 15) == 0)
                {
                    float maxF = WindStyle == WindStyle.Trombone ? 2200f : 5600f;
                    _filter.Set(BiquadMode.LowPass, 420f + env * maxF * (0.4f + _tone * 0.7f), isBrass ? 1.8f : 1.2f);
                }
            }

            float outS = _filter.ProcessMono(sig);
            if (_drive > 0.08f) outS = DspMath.FastTanh(outS * (1f + _drive * 3.5f));
            outS *= env * _velocity * 0.52f;
            busL[i] += outS * _gainL;
            busR[i] += outS * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 9. ORCHESTRA STRINGS, CHOIR & AMBIENT PADS
    // ========================================================================
    private void RenderStringsPad(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float lfoDt = _vibRate / _sr;
        float det = MathF.Pow(2f, Math.Max(6f, _detune) / 1200f);
        float invDet = 1f / det;

        if (PadStyle == PadStyle.ChoirAah)
        {
            _filter.Set(BiquadMode.BandPass, 720f, 3.2f);
            _formant2.Set(BiquadMode.BandPass, 1220f, 3.6f);
        }
        else
        {
            _filter.Set(BiquadMode.LowPass, _cutoff, 0.9f);
        }

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            _pLfo = (_pLfo + lfoDt) % 1f;
            float vib = MathF.Sin(_pLfo * MathF.Tau) * _vibDepth * 0.012f;
            float f0 = _baseFreq * (1f + vib);
            float dt0 = f0 / _sr;
            float dtL = dt0 * invDet;
            float dtR = dt0 * det;

            _p1 = (_p1 + dt0) % 1f;
            _p2 = (_p2 + dtL) % 1f;
            _p3 = (_p3 + dtR) % 1f;

            float sigL, sigR;
            if (PadStyle is PadStyle.SpacePad or PadStyle.VintageTapePad)
            {
                sigL = DspMath.Triangle(_p1) * 0.5f + DspMath.SawBlep(_p2, dtL) * 0.35f;
                sigR = DspMath.Triangle(_p1) * 0.5f + DspMath.SawBlep(_p3, dtR) * 0.35f;
            }
            else
            {
                sigL = DspMath.SawBlep(_p1, dt0) * 0.45f + DspMath.SawBlep(_p2, dtL) * 0.45f;
                sigR = DspMath.SawBlep(_p1, dt0) * 0.45f + DspMath.SawBlep(_p3, dtR) * 0.45f;
            }

            if (PadStyle == PadStyle.ChoirAah)
            {
                float mono = (sigL + sigR) * 0.5f;
                float f1 = _filter.ProcessMono(mono) * 1.8f;
                float f2 = _formant2.ProcessMono(mono) * 1.4f;
                sigL = f1 + f2 * 0.85f;
                sigR = f1 * 0.85f + f2;
            }
            else
            {
                _filter.Process(ref sigL, ref sigR);
            }

            float g = env * _velocity * 0.36f;
            busL[i] += sigL * g * _gainL;
            busR[i] += sigR * g * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 10. WORLD MALLETS & MODAL PERCUSSION (Marimba, Kalimba, Vibraphone, SteelDrum, Bells)
    // ========================================================================
    private void RenderWorldMallet(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float r2, r3, a2, a3, d2, d3;
        switch (MalletStyle)
        {
            case MalletStyle.Marimba:     r2 = 4.0f;  r3 = 9.2f;  a2 = 0.24f; a3 = 0.08f; d2 = 8f;  d3 = 22f; break;
            case MalletStyle.Xylophone:   r2 = 3.0f;  r3 = 6.0f;  a2 = 0.38f; a3 = 0.16f; d2 = 12f; d3 = 26f; break;
            case MalletStyle.Vibraphone:  r2 = 4.0f;  r3 = 9.0f;  a2 = 0.22f; a3 = 0.06f; d2 = 5f;  d3 = 14f; break;
            case MalletStyle.Kalimba:     r2 = 2.95f; r3 = 5.4f;  a2 = 0.28f; a3 = 0.12f; d2 = 9f;  d3 = 20f; break;
            case MalletStyle.SteelDrum:   r2 = 2.0f;  r3 = 3.02f; a2 = 0.42f; a3 = 0.24f; d2 = 5f;  d3 = 9f;  break;
            case MalletStyle.TubularBell: r2 = 2.0f;  r3 = 2.756f;a2 = 0.48f; a3 = 0.34f; d2 = 2.2f;d3 = 3.4f;break;
            default: /* MusicBox */       r2 = 2.0f;  r3 = 4.18f; a2 = 0.32f; a3 = 0.22f; d2 = 4f;  d3 = 10f; break;
        }

        float dt1 = _baseFreq / _sr;
        float dt2 = (_baseFreq * r2) / _sr;
        float dt3 = (_baseFreq * r3) / _sr;
        float lfoDt = _vibRate / _sr;
        float bright = Math.Clamp(_tone, 0.1f, 1f);

        for (int i = 0; i < frames; i++)
        {
            float t = _ageSamples / _sr;
            float mainEnv = MathF.Exp(-t / MathF.Max(0.06f, _decay * 0.55f));
            if (mainEnv < 0.001f) { Active = false; return; }

            _p1 = (_p1 + dt1) % 1f;
            _p2 = (_p2 + dt2) % 1f;

            float sig = MathF.Sin(_p1 * MathF.Tau) * mainEnv
                      + MathF.Sin(_p2 * MathF.Tau) * a2 * bright * MathF.Exp(-t * d2);

            if (!ecoMode)
            {
                _p3 = (_p3 + dt3) % 1f;
                sig += MathF.Sin(_p3 * MathF.Tau) * a3 * bright * MathF.Exp(-t * d3);
            }

            // Crisp mallet strike transient
            if (t < 0.007f)
            {
                sig += _rng.NextFloat() * (1f - t / 0.007f) * 0.22f * bright;
            }

            // Vibraphone motor tremolo
            if (MalletStyle == MalletStyle.Vibraphone && _vibDepth > 0.02f)
            {
                _pLfo = (_pLfo + lfoDt) % 1f;
                sig *= 1f - _vibDepth * 0.35f * (0.5f + 0.5f * MathF.Sin(_pLfo * MathF.Tau));
            }

            float amp = sig * _velocity * 0.58f;
            busL[i] += amp * _gainL;
            busR[i] += amp * _gainR;
            _ageSamples++;
        }
    }

    // ========================================================================
    // 11. DIRECTWAVE WAV SAMPLER
    // ========================================================================
    private void RenderSampler(float[] busL, float[] busR, int frames)
    {
        if (_sample == null) { Active = false; return; }
        var sL = _sample.Left;
        var sR = _sample.Right;
        _filter.Set(BiquadMode.LowPass, _cutoff, 0.8f);

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            int idx = (int)_samplePos;
            if ((!_sampleReverse && idx >= _sampleEndIdx - 1) || (_sampleReverse && idx <= _sampleStartIdx))
            {
                Active = false;
                return;
            }

            int nextIdx = Math.Clamp(idx + (_sampleReverse ? -1 : 1), 0, sL.Length - 1);
            float frac = MathF.Abs((float)(_samplePos - idx));
            float l = sL[idx] + (sL[nextIdx] - sL[idx]) * frac;
            float r = sR[idx] + (sR[nextIdx] - sR[idx]) * frac;
            _filter.Process(ref l, ref r);

            busL[i] += l * env * _velocity * _gainL;
            busR[i] += r * env * _velocity * _gainR;

            _samplePos += _sampleReverse ? -_sampleStep : _sampleStep;
            _ageSamples++;
        }
    }
}
