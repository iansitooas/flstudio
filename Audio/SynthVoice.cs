using FLLiteStudio.Core;

namespace FLLiteStudio.Audio;

public sealed class SynthVoice
{
    public bool Active;
    public int MixerTrack;
    public InstrumentType Type;
    public DrumKind DrumKind;
    public KeysMode KeysMode;
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
    private int _coarse2;
    private float _osc2Mix;

    // Oscillator & envelope state
    private float _env;
    private float _p1, _p2, _p3, _p4, _p5, _pSub, _pMod;
    private readonly StereoBiquad _filter = new();
    private readonly StereoBiquad _hpFilter = new();
    private FastRng _rng = new(0x5A17E123);

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
        Wave1 = ch.Wave1;
        Wave2 = ch.Wave2;

        _ageSamples = 0;
        _inRelease = false;
        _env = 0f;
        _p1 = _p2 = _p3 = _p4 = _p5 = _pSub = _pMod = 0f;
        _filter.Reset();
        _hpFilter.Reset();

        _baseFreq = MusicTheory.MidiToFreq(midi + ch.PitchSemitones);
        _velocity = Math.Clamp(velocity * ch.Volume, 0.01f, 1.5f);

        float pan = Math.Clamp(ch.Pan + panOffset, -1f, 1f);
        float angle = (pan + 1f) * 0.25f * MathF.PI;
        _gainL = MathF.Cos(angle) * 1.15f;
        _gainR = MathF.Sin(angle) * 1.15f;

        _attack = Math.Clamp(ch.Attack, 0.002f, 1.5f);
        _decay = Math.Clamp(ch.Decay, 0.03f, 3.0f);
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
        _coarse2 = ch.Coarse2;
        _osc2Mix = Math.Clamp(ch.Osc2Mix, 0f, 1f);

        _noteSamples = Math.Max(64, (int)(durationSec * _sr));
        _releaseSamples = Math.Max(64, (int)(_release * _sr));

        if (Type == InstrumentType.DrumSynth)
        {
            _noteSamples = (int)(Math.Clamp(_decay, 0.04f, 1.8f) * _sr);
            if (DrumKind == DrumKind.Crash) _noteSamples = Math.Max(_noteSamples, (int)(0.85f * _sr));
            if (DrumKind == DrumKind.ClosedHat) _noteSamples = Math.Min(_noteSamples, (int)(0.11f * _sr));
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

    private void RenderDrum(float[] busL, float[] busR, int frames)
    {
        float pitchMult = _baseFreq / 261.6256f; // relative to C4
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
                {
                    float baseF = (DrumKind == DrumKind.Kick808 ? 44f : 52f) * pitchMult;
                    float sweep = MathF.Exp(-t * (28f + _punch * 36f));
                    float freq = baseF + (115f + _punch * 135f) * sweep * pitchMult;
                    _p1 = (_p1 + freq / _sr) % 1f;
                    float env = MathF.Exp(-t / MathF.Max(0.04f, _decay * 0.42f));
                    float click = t < 0.012f ? MathF.Sin(t * 2800f) * (1f - t / 0.012f) * _tone * 0.45f : 0f;
                    s = (MathF.Sin(_p1 * MathF.Tau) + click) * env * 1.15f;
                    if (_drive > 0.05f) s = DspMath.FastTanh(s * (1f + _drive * 4f));
                    break;
                }
                case DrumKind.Snare:
                {
                    float bodyF = (145f + 100f * MathF.Exp(-t * 40f)) * pitchMult;
                    _p1 = (_p1 + bodyF / _sr) % 1f;
                    float body = MathF.Sin(_p1 * MathF.Tau) * MathF.Exp(-t * 22f) * 0.65f;
                    _hpFilter.Set(BiquadMode.HighPass, 850f + _tone * 1400f, 0.8f);
                    float n = _hpFilter.ProcessMono(_rng.NextFloat()) * MathF.Exp(-t / MathF.Max(0.03f, _decay * 0.35f)) * 0.85f;
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
                case DrumKind.ClosedHat:
                case DrumKind.OpenHat:
                case DrumKind.Crash:
                {
                    float hpFreq = DrumKind == DrumKind.Crash ? 4200f : 6500f + _tone * 2800f;
                    _hpFilter.Set(BiquadMode.HighPass, hpFreq, 0.9f);
                    float tau = DrumKind == DrumKind.Crash ? 0.38f : DrumKind == DrumKind.OpenHat ? _decay * 0.5f : 0.024f;
                    float env = MathF.Exp(-t / MathF.Max(0.012f, tau));
                    s = _hpFilter.ProcessMono(_rng.NextFloat()) * env * (DrumKind == DrumKind.Crash ? 0.62f : 0.72f);
                    break;
                }
                default: // Rimshot / Perc
                {
                    float f0 = (DrumKind == DrumKind.Rimshot ? 820f : 380f) * pitchMult;
                    float f = f0 * MathF.Exp(-t * 24f) + 120f;
                    _p1 = (_p1 + f / _sr) % 1f;
                    float env = MathF.Exp(-t / (DrumKind == DrumKind.Rimshot ? 0.018f : 0.07f));
                    s = (DrumKind == DrumKind.Rimshot ? DspMath.SquareBlep(_p1, f / _sr) * 0.5f : MathF.Sin(_p1 * MathF.Tau)) * env * 0.85f;
                    break;
                }
            }

            if (norm > 0.94f) s *= (1f - norm) / 0.06f; // clickless tail fade
            float amp = s * _velocity;
            busL[i] += amp * _gainL;
            busR[i] += amp * _gainR;
            _ageSamples++;
        }
    }

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

    private void RenderKeys(float[] busL, float[] busR, int frames, bool ecoMode)
    {
        float dt = _baseFreq / _sr;
        float dt2 = dt * 2.002f;
        float dtTine = dt * 7f;
        float bright = Math.Clamp(_tone, 0.1f, 1f);
        _filter.Set(BiquadMode.LowPass, 1400f + bright * 5400f, 0.8f);

        for (int i = 0; i < frames; i++)
        {
            float env = NextAdsr(_noteSamples);
            if (!Active) return;

            float t = _ageSamples / _sr;
            _p1 = (_p1 + dt) % 1f;
            _p2 = (_p2 + dt2) % 1f;

            float sig;
            if (KeysMode == KeysMode.Rhodes)
            {
                float tine = 0f;
                if (!ecoMode && t < 0.09f)
                {
                    _p3 = (_p3 + dtTine) % 1f;
                    tine = MathF.Sin(_p3 * MathF.Tau) * MathF.Exp(-t * 38f) * 0.16f * bright;
                }
                sig = MathF.Sin(_p1 * MathF.Tau) * 0.78f + DspMath.Triangle(_p2) * 0.24f * bright * MathF.Exp(-t * 3.5f) + tine;
            }
            else if (KeysMode == KeysMode.BellKeys)
            {
                _p3 = (_p3 + dt * 2.756f) % 1f;
                sig = MathF.Sin(_p1 * MathF.Tau) * 0.65f + MathF.Sin(_p3 * MathF.Tau) * 0.35f * MathF.Exp(-t * 4f);
            }
            else if (KeysMode == KeysMode.Organ)
            {
                _p3 = (_p3 + dt * 4f) % 1f;
                sig = MathF.Sin(_p1 * MathF.Tau) * 0.55f + MathF.Sin(_p2 * MathF.Tau) * 0.28f + MathF.Sin(_p3 * MathF.Tau) * 0.18f;
            }
            else // Grand Piano
            {
                sig = DspMath.Triangle(_p1) * 0.62f + DspMath.SawBlep(_p2, dt2) * 0.28f * bright * MathF.Exp(-t * 2.8f);
            }

            float outS = _filter.ProcessMono(sig) * env * _velocity * 0.48f;
            busL[i] += outS * _gainL;
            busR[i] += outS * _gainR;
            _ageSamples++;
        }
    }

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
