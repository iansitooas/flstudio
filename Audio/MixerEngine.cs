using FLLiteStudio.Core;

namespace FLLiteStudio.Audio;

internal sealed class FxRuntimeState
{
    public readonly StereoBiquad[] EqBands = { new(), new(), new(), new(), new() };
    public readonly SchroederReverb Reverb = new();
    public readonly StereoDelayLine Delay = new();
    public readonly StereoChorusEffect Chorus = new();
    public readonly StereoBiquad SgLow = new();
    public readonly StereoBiquad SgHigh = new();
    public float LimiterEnv = 1f;
    public float GrossBeatEnv = 1f;

    public void Reset()
    {
        foreach (var b in EqBands) b.Reset();
        Reverb.Clear();
        Delay.Clear();
        Chorus.Clear();
        SgLow.Reset();
        SgHigh.Reset();
        LimiterEnv = 1f;
        GrossBeatEnv = 1f;
    }
}

public sealed class MixerEngine
{
    public const int TrackCount = 13; // 0 = Master, 1..12 = Inserts

    private float[][] _busL = Array.Empty<float[]>();
    private float[][] _busR = Array.Empty<float[]>();
    private readonly Dictionary<string, FxRuntimeState> _fxStates = new();
    private readonly float[] _peaks = new float[TrackCount];
    private readonly StereoBiquad _masterAutoFilter = new();

    public float MasterAutoFilterNorm = 1f; // 0..1 -> 80Hz..20kHz
    public float MasterAutoGain = 1f;
    public readonly float[] TrackAutoGain = new float[TrackCount];

    public MixerEngine()
    {
        EnsureBuffers(2048);
        ResetAutomation();
    }

    public void EnsureBuffers(int frames)
    {
        if (_busL.Length == TrackCount && _busL[0].Length >= frames) return;
        _busL = new float[TrackCount][];
        _busR = new float[TrackCount][];
        for (int i = 0; i < TrackCount; i++)
        {
            _busL[i] = new float[frames];
            _busR[i] = new float[frames];
        }
    }

    public void ClearBuses(int frames)
    {
        EnsureBuffers(frames);
        for (int i = 0; i < TrackCount; i++)
        {
            Array.Clear(_busL[i], 0, frames);
            Array.Clear(_busR[i], 0, frames);
        }
    }

    public (float[] L, float[] R) GetBus(int trackId)
    {
        int id = Math.Clamp(trackId, 0, TrackCount - 1);
        return (_busL[id], _busR[id]);
    }

    public float GetTrackPeak(int trackId) =>
        (trackId >= 0 && trackId < TrackCount) ? _peaks[trackId] : 0f;

    public void ResetAutomation()
    {
        MasterAutoFilterNorm = 1f;
        MasterAutoGain = 1f;
        Array.Fill(TrackAutoGain, 1f);
        _masterAutoFilter.Reset();
    }

    public void ResetEffects()
    {
        foreach (var st in _fxStates.Values) st.Reset();
        _masterAutoFilter.Reset();
        Array.Clear(_peaks);
    }

    public void ProcessMix(
        ProjectModel project,
        float[] outL,
        float[] outR,
        int frames,
        int stepInBar,
        bool isPlaying,
        bool ecoMode)
    {
        var tracks = project.Mixer;
        bool anySolo = false;
        for (int i = 1; i < tracks.Count; i++)
        {
            if (tracks[i].Solo) { anySolo = true; break; }
        }

        // 1. Process Insert Tracks 1..12 -> sum into Master Bus (0)
        for (int t = 1; t < Math.Min(TrackCount, tracks.Count); t++)
        {
            var tr = tracks[t];
            var bL = _busL[t];
            var bR = _busR[t];

            bool audible = !tr.Muted && (!anySolo || tr.Solo);
            if (!audible)
            {
                _peaks[t] *= 0.85f;
                continue;
            }

            // Smart Disable: skip empty silent tracks with no delay/reverb tail
            ProcessTrackEffects(tr, bL, bR, frames, project.Bpm, stepInBar, isPlaying, ecoMode);

            float vol = Math.Clamp(tr.Volume, 0f, 1.4f) * TrackAutoGain[t];
            float pan = Math.Clamp(tr.Pan, -1f, 1f);
            float gL = vol * MathF.Cos((pan + 1f) * 0.25f * MathF.PI) * 1.414f;
            float gR = vol * MathF.Sin((pan + 1f) * 0.25f * MathF.PI) * 1.414f;

            float peak = 0f;
            var mL = _busL[0];
            var mR = _busR[0];
            for (int i = 0; i < frames; i++)
            {
                float sl = bL[i] * gL;
                float sr = bR[i] * gR;
                mL[i] += sl;
                mR[i] += sr;
                float a = MathF.Max(MathF.Abs(sl), MathF.Abs(sr));
                if (a > peak) peak = a;
            }
            _peaks[t] = MathF.Max(peak, _peaks[t] * 0.86f);
        }

        // 2. Process Master Bus (0)
        if (tracks.Count > 0)
        {
            var master = tracks[0];
            var mL = _busL[0];
            var mR = _busR[0];

            if (!master.Muted)
            {
                ProcessTrackEffects(master, mL, mR, frames, project.Bpm, stepInBar, isPlaying, ecoMode);

                // Master Automation Filter
                bool filterActive = MasterAutoFilterNorm < 0.99f;
                if (filterActive)
                {
                    float freq = 85f * MathF.Pow(230f, Math.Clamp(MasterAutoFilterNorm, 0f, 1f));
                    _masterAutoFilter.Set(BiquadMode.LowPass, freq, 1.1f);
                }

                float mVol = Math.Clamp(master.Volume, 0f, 1.35f) * MasterAutoGain;
                float peak = 0f;
                for (int i = 0; i < frames; i++)
                {
                    float sl = mL[i];
                    float sr = mR[i];
                    if (filterActive) _masterAutoFilter.Process(ref sl, ref sr);
                    sl = DspMath.FastTanh(sl * mVol);
                    sr = DspMath.FastTanh(sr * mVol);
                    outL[i] = sl;
                    outR[i] = sr;
                    float a = MathF.Max(MathF.Abs(sl), MathF.Abs(sr));
                    if (a > peak) peak = a;
                }
                _peaks[0] = MathF.Max(peak, _peaks[0] * 0.86f);
            }
            else
            {
                _peaks[0] *= 0.85f;
            }
        }
    }

    private FxRuntimeState GetFxState(string id)
    {
        if (!_fxStates.TryGetValue(id, out var st))
        {
            st = new FxRuntimeState();
            _fxStates[id] = st;
        }
        return st;
    }

    private void ProcessTrackEffects(
        MixerTrackModel tr,
        float[] bL,
        float[] bR,
        int frames,
        int bpm,
        int stepInBar,
        bool isPlaying,
        bool ecoMode)
    {
        if (tr.Effects.Count == 0) return;

        for (int e = 0; e < tr.Effects.Count; e++)
        {
            var fx = tr.Effects[e];
            if (!fx.Enabled) continue;

            var st = GetFxState(fx.Id);

            switch (fx.Type)
            {
                case FxType.ParametricEq2:
                {
                    st.EqBands[0].Set(BiquadMode.LowShelf, 80f, 0.8f, fx.EqLowDb);
                    st.EqBands[1].Set(BiquadMode.Peaking, 320f, 1.2f, fx.EqLowMidDb);
                    st.EqBands[2].Set(BiquadMode.Peaking, 1200f, 1.2f, fx.EqMidDb);
                    st.EqBands[3].Set(BiquadMode.Peaking, 4200f, 1.2f, fx.EqHighMidDb);
                    st.EqBands[4].Set(BiquadMode.HighShelf, 10500f, 0.8f, fx.EqHighDb);

                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        st.EqBands[0].Process(ref l, ref r);
                        st.EqBands[1].Process(ref l, ref r);
                        st.EqBands[2].Process(ref l, ref r);
                        st.EqBands[3].Process(ref l, ref r);
                        st.EqBands[4].Process(ref l, ref r);
                        bL[i] = l; bR[i] = r;
                    }
                    break;
                }
                case FxType.Reverb2:
                {
                    float decay = ecoMode ? MathF.Min(fx.ReverbDecay, 0.65f) : fx.ReverbDecay;
                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        st.Reverb.Process(ref l, ref r, fx.Mix, decay, fx.ReverbDamp);
                        bL[i] = l; bR[i] = r;
                    }
                    break;
                }
                case FxType.Delay3:
                {
                    int divIdx = Math.Clamp(fx.DelayDivisionIndex, 0, MusicTheory.DelayDivisionBeats.Length - 1);
                    float delaySec = (float)((60.0 / Math.Max(40, bpm)) * MusicTheory.DelayDivisionBeats[divIdx]);
                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        st.Delay.Process(ref l, ref r, delaySec, fx.DelayFeedback, fx.Mix, fx.DelayToneHz);
                        bL[i] = l; bR[i] = r;
                    }
                    break;
                }
                case FxType.Soundgoodizer:
                {
                    float amt = Math.Clamp(fx.SoundgoodizerAmount, 0f, 1f);
                    float lowDb = fx.SoundgoodizerMode switch { 1 => 6.2f, 2 => 3.8f, 3 => 5.5f, _ => 4.8f } * amt;
                    float hiDb = fx.SoundgoodizerMode switch { 1 => 3.2f, 2 => 5.2f, 3 => 5.8f, _ => 4.2f } * amt;
                    st.SgLow.Set(BiquadMode.LowShelf, 105f, 0.7f, lowDb);
                    st.SgHigh.Set(BiquadMode.HighShelf, 7200f, 0.7f, hiDb);
                    float drive = 1f + amt * 1.4f;
                    float makeup = 1f + amt * 0.25f;

                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        st.SgLow.Process(ref l, ref r);
                        st.SgHigh.Process(ref l, ref r);
                        bL[i] = DspMath.FastTanh(l * drive) * makeup;
                        bR[i] = DspMath.FastTanh(r * drive) * makeup;
                    }
                    break;
                }
                case FxType.Distortion:
                {
                    float drv = Math.Clamp(fx.DistDrive, 0f, 1f);
                    float mix = Math.Clamp(fx.Mix, 0f, 1f);
                    float pre = 1f + drv * 14f;
                    float post = 1f / (1f + drv * 0.55f);
                    float levels = MathF.Max(3f, 26f - drv * 22f);

                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        float wl, wr;
                        if (fx.DistMode == 2) // Hard Clip
                        {
                            wl = Math.Clamp(l * pre, -0.85f, 0.85f);
                            wr = Math.Clamp(r * pre, -0.85f, 0.85f);
                        }
                        else if (fx.DistMode == 3) // Bitcrush
                        {
                            wl = MathF.Round(l * levels) / levels;
                            wr = MathF.Round(r * levels) / levels;
                        }
                        else // Warm / Tube
                        {
                            wl = DspMath.FastTanh(l * pre + (fx.DistMode == 1 ? 0.08f * drv : 0f));
                            wr = DspMath.FastTanh(r * pre + (fx.DistMode == 1 ? 0.08f * drv : 0f));
                        }
                        bL[i] = l * (1f - mix) + wl * post * mix;
                        bR[i] = r * (1f - mix) + wr * post * mix;
                    }
                    break;
                }
                case FxType.Chorus:
                {
                    for (int i = 0; i < frames; i++)
                    {
                        float l = bL[i], r = bR[i];
                        st.Chorus.Process(ref l, ref r, fx.ChorusRateHz, fx.ChorusDepth, fx.Mix);
                        bL[i] = l; bR[i] = r;
                    }
                    break;
                }
                case FxType.GrossBeat:
                {
                    float target = 1f;
                    if (isPlaying)
                    {
                        int pIdx = Math.Clamp(fx.GrossBeatPreset, 0, MusicTheory.GrossBeatPresets.Length - 1);
                        float raw = MusicTheory.GrossBeatPresets[pIdx].Curve16[stepInBar & 15];
                        target = 1f - Math.Clamp(fx.Mix, 0f, 1f) * (1f - raw);
                    }
                    for (int i = 0; i < frames; i++)
                    {
                        st.GrossBeatEnv += (target - st.GrossBeatEnv) * 0.008f;
                        bL[i] *= st.GrossBeatEnv;
                        bR[i] *= st.GrossBeatEnv;
                    }
                    break;
                }
                case FxType.Limiter:
                {
                    float thresh = DspMath.DbToGain(Math.Clamp(fx.LimiterThresholdDb, -30f, 0f));
                    float makeup = Math.Clamp(fx.LimiterGain, 0.4f, 2.2f);
                    for (int i = 0; i < frames; i++)
                    {
                        float peak = MathF.Max(MathF.Abs(bL[i]), MathF.Abs(bR[i]));
                        float desired = peak > thresh ? (thresh / peak) : 1f;
                        float coeff = desired < st.LimiterEnv ? 0.05f : 0.0008f;
                        st.LimiterEnv += (desired - st.LimiterEnv) * coeff;
                        float g = st.LimiterEnv * makeup;
                        bL[i] *= g;
                        bR[i] *= g;
                    }
                    break;
                }
            }
        }
    }
}
