using System.Runtime.CompilerServices;

namespace FLLiteStudio.Audio;

public struct FastRng
{
    private uint _state;
    public FastRng(uint seed = 0x13579BDF) => _state = seed == 0 ? 0x13579BDF : seed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return (x / 2147483648f) - 1f;
    }
}

public static class DspMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastTanh(float x)
    {
        if (x < -3f) return -1f;
        if (x > 3f) return 1f;
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PolyBlep(float t, float dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1f;
        }
        if (t > 1f - dt)
        {
            t = (t - 1f) / dt;
            return t * t + t + t + 1f;
        }
        return 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SawBlep(float phase, float dt)
    {
        float val = 2f * phase - 1f;
        return val - PolyBlep(phase, dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SquareBlep(float phase, float dt)
    {
        float val = phase < 0.5f ? 1f : -1f;
        val += PolyBlep(phase, dt);
        val -= PolyBlep((phase + 0.5f) % 1f, dt);
        return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Triangle(float phase)
    {
        return 4f * MathF.Abs(phase - 0.5f) - 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DbToGain(float db) => MathF.Pow(10f, db / 20f);
}

public enum BiquadMode { LowPass, HighPass, BandPass, LowShelf, Peaking, HighShelf }

public sealed class StereoBiquad
{
    private float _b0 = 1, _b1, _b2, _a1, _a2;
    private float _x1L, _x2L, _y1L, _y2L;
    private float _x1R, _x2R, _y1R, _y2R;
    private float _lastFreq = -1, _lastQ = -1, _lastDb = -999;
    private BiquadMode _lastMode = (BiquadMode)(-1);

    public void Set(BiquadMode mode, float freq, float q, float gainDb = 0f, float sr = WinMmAudioOut.SampleRate)
    {
        freq = Math.Clamp(freq, 25f, sr * 0.46f);
        q = Math.Clamp(q, 0.25f, 18f);
        if (mode == _lastMode && MathF.Abs(freq - _lastFreq) < 0.5f && MathF.Abs(q - _lastQ) < 0.01f && MathF.Abs(gainDb - _lastDb) < 0.05f)
            return;

        _lastMode = mode; _lastFreq = freq; _lastQ = q; _lastDb = gainDb;

        float w0 = 2f * MathF.PI * freq / sr;
        float cosW = MathF.Cos(w0);
        float sinW = MathF.Sin(w0);
        float alpha = sinW / (2f * q);
        float a = MathF.Pow(10f, gainDb / 40f);

        float b0, b1, b2, a0, a1, a2;

        switch (mode)
        {
            case BiquadMode.LowPass:
                b0 = (1f - cosW) * 0.5f; b1 = 1f - cosW; b2 = b0;
                a0 = 1f + alpha; a1 = -2f * cosW; a2 = 1f - alpha;
                break;
            case BiquadMode.HighPass:
                b0 = (1f + cosW) * 0.5f; b1 = -(1f + cosW); b2 = b0;
                a0 = 1f + alpha; a1 = -2f * cosW; a2 = 1f - alpha;
                break;
            case BiquadMode.BandPass:
                b0 = alpha; b1 = 0f; b2 = -alpha;
                a0 = 1f + alpha; a1 = -2f * cosW; a2 = 1f - alpha;
                break;
            case BiquadMode.Peaking:
                b0 = 1f + alpha * a; b1 = -2f * cosW; b2 = 1f - alpha * a;
                a0 = 1f + alpha / a; a1 = -2f * cosW; a2 = 1f - alpha / a;
                break;
            case BiquadMode.LowShelf:
            {
                float sq = 2f * MathF.Sqrt(a) * alpha;
                b0 = a * ((a + 1f) - (a - 1f) * cosW + sq);
                b1 = 2f * a * ((a - 1f) - (a + 1f) * cosW);
                b2 = a * ((a + 1f) - (a - 1f) * cosW - sq);
                a0 = (a + 1f) + (a - 1f) * cosW + sq;
                a1 = -2f * ((a - 1f) + (a + 1f) * cosW);
                a2 = (a + 1f) + (a - 1f) * cosW - sq;
                break;
            }
            default: // HighShelf
            {
                float sq = 2f * MathF.Sqrt(a) * alpha;
                b0 = a * ((a + 1f) + (a - 1f) * cosW + sq);
                b1 = -2f * a * ((a - 1f) + (a + 1f) * cosW);
                b2 = a * ((a + 1f) + (a - 1f) * cosW - sq);
                a0 = (a + 1f) - (a - 1f) * cosW + sq;
                a1 = 2f * ((a - 1f) - (a + 1f) * cosW);
                a2 = (a + 1f) - (a - 1f) * cosW - sq;
                break;
            }
        }

        float invA0 = 1f / a0;
        _b0 = b0 * invA0; _b1 = b1 * invA0; _b2 = b2 * invA0;
        _a1 = a1 * invA0; _a2 = a2 * invA0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        float yl = _b0 * l + _b1 * _x1L + _b2 * _x2L - _a1 * _y1L - _a2 * _y2L;
        if (MathF.Abs(yl) < 1e-18f) yl = 0f; // flush denormals
        _x2L = _x1L; _x1L = l; _y2L = _y1L; _y1L = yl;
        l = yl;

        float yr = _b0 * r + _b1 * _x1R + _b2 * _x2R - _a1 * _y1R - _a2 * _y2R;
        if (MathF.Abs(yr) < 1e-18f) yr = 0f;
        _x2R = _x1R; _x1R = r; _y2R = _y1R; _y1R = yr;
        r = yr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ProcessMono(float x)
    {
        float y = _b0 * x + _b1 * _x1L + _b2 * _x2L - _a1 * _y1L - _a2 * _y2L;
        if (MathF.Abs(y) < 1e-18f) y = 0f;
        _x2L = _x1L; _x1L = x; _y2L = _y1L; _y1L = y;
        return y;
    }

    public void Reset()
    {
        _x1L = _x2L = _y1L = _y2L = 0;
        _x1R = _x2R = _y1R = _y2R = 0;
    }
}

/// <summary>
/// Ultra-lightweight Freeverb-style Stereo Reverb (4 combs + 2 allpasses per side).
/// Very lush tail, negligible CPU cost even on old dual-core laptops.
/// </summary>
public sealed class SchroederReverb
{
    private sealed class Comb
    {
        private readonly float[] _buf;
        private int _idx;
        private float _lp;
        public Comb(int size) => _buf = new float[size];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input, float feedback, float damp)
        {
            float output = _buf[_idx];
            _lp = output * (1f - damp) + _lp * damp;
            float next = input + _lp * feedback;
            if (MathF.Abs(next) < 1e-15f) next = 0f;
            _buf[_idx] = next;
            if (++_idx >= _buf.Length) _idx = 0;
            return output;
        }
        public void Clear() { Array.Clear(_buf); _lp = 0; _idx = 0; }
    }

    private sealed class AllPass
    {
        private readonly float[] _buf;
        private int _idx;
        public AllPass(int size) => _buf = new float[size];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input)
        {
            float bufOut = _buf[_idx];
            float output = -input + bufOut;
            float next = input + bufOut * 0.5f;
            if (MathF.Abs(next) < 1e-15f) next = 0f;
            _buf[_idx] = next;
            if (++_idx >= _buf.Length) _idx = 0;
            return output;
        }
        public void Clear() { Array.Clear(_buf); _idx = 0; }
    }

    private readonly Comb[] _combsL = { new(1116), new(1188), new(1277), new(1356) };
    private readonly Comb[] _combsR = { new(1139), new(1211), new(1300), new(1379) };
    private readonly AllPass[] _apsL = { new(556), new(441) };
    private readonly AllPass[] _apsR = { new(579), new(464) };

    public void Process(ref float l, ref float r, float wet, float decay, float damp)
    {
        float monoIn = (l + r) * 0.16f;
        float fb = Math.Clamp(decay, 0.2f, 0.94f);
        float d = Math.Clamp(damp, 0.05f, 0.92f);

        float outL = 0f, outR = 0f;
        for (int i = 0; i < 4; i++)
        {
            outL += _combsL[i].Process(monoIn, fb, d);
            outR += _combsR[i].Process(monoIn, fb, d);
        }
        outL = _apsL[1].Process(_apsL[0].Process(outL));
        outR = _apsR[1].Process(_apsR[0].Process(outR));

        float w = Math.Clamp(wet, 0f, 1f);
        float dry = 1f - w * 0.35f;
        l = l * dry + outL * w;
        r = r * dry + outR * w;
    }

    public void Clear()
    {
        foreach (var c in _combsL) c.Clear();
        foreach (var c in _combsR) c.Clear();
        foreach (var a in _apsL) a.Clear();
        foreach (var a in _apsR) a.Clear();
    }
}

public sealed class StereoDelayLine
{
    private readonly float[] _bufL = new float[WinMmAudioOut.SampleRate * 2];
    private readonly float[] _bufR = new float[WinMmAudioOut.SampleRate * 2];
    private int _writeIdx;
    private float _lpL, _lpR;

    public void Process(ref float l, ref float r, float delaySec, float feedback, float wet, float toneHz)
    {
        int maxLen = _bufL.Length;
        int delayL = Math.Clamp((int)(delaySec * WinMmAudioOut.SampleRate), 64, maxLen - 2);
        int delayR = Math.Clamp((int)(delaySec * 1.018f * WinMmAudioOut.SampleRate), 64, maxLen - 2);

        int readL = _writeIdx - delayL; if (readL < 0) readL += maxLen;
        int readR = _writeIdx - delayR; if (readR < 0) readR += maxLen;

        float dL = _bufL[readL];
        float dR = _bufR[readR];

        float coeff = Math.Clamp(toneHz / (WinMmAudioOut.SampleRate * 0.5f), 0.03f, 0.95f);
        _lpL += coeff * (dL - _lpL);
        _lpR += coeff * (dR - _lpR);

        float fb = Math.Clamp(feedback, 0f, 0.86f);
        // Subtle ping-pong cross-feed
        _bufL[_writeIdx] = l + _lpR * fb;
        _bufR[_writeIdx] = r + _lpL * fb;

        if (++_writeIdx >= maxLen) _writeIdx = 0;

        float w = Math.Clamp(wet, 0f, 1f);
        l += dL * w;
        r += dR * w;
    }

    public void Clear() { Array.Clear(_bufL); Array.Clear(_bufR); _lpL = _lpR = 0; _writeIdx = 0; }
}

public sealed class StereoChorusEffect
{
    private readonly float[] _bufL = new float[2048];
    private readonly float[] _bufR = new float[2048];
    private int _writeIdx;
    private float _phase;

    public void Process(ref float l, ref float r, float rateHz, float depth, float mix)
    {
        _bufL[_writeIdx] = l;
        _bufR[_writeIdx] = r;

        _phase += rateHz / WinMmAudioOut.SampleRate;
        if (_phase >= 1f) _phase -= 1f;

        float lfoL = MathF.Sin(_phase * 2f * MathF.PI);
        float lfoR = MathF.Cos(_phase * 2f * MathF.PI);

        float center = 520f;
        float mod = depth * 260f;
        float tapL = ReadFrac(_bufL, center + lfoL * mod);
        float tapR = ReadFrac(_bufR, center + lfoR * mod);

        if (++_writeIdx >= _bufL.Length) _writeIdx = 0;

        float m = Math.Clamp(mix, 0f, 1f);
        l = l * (1f - m * 0.35f) + tapL * m;
        r = r * (1f - m * 0.35f) + tapR * m;
    }

    private float ReadFrac(float[] buf, float delaySamples)
    {
        float pos = _writeIdx - delaySamples;
        while (pos < 0) pos += buf.Length;
        int i0 = (int)pos % buf.Length;
        int i1 = (i0 + 1) % buf.Length;
        float frac = pos - (int)pos;
        return buf[i0] + (buf[i1] - buf[i0]) * frac;
    }

    public void Clear() { Array.Clear(_bufL); Array.Clear(_bufR); _writeIdx = 0; _phase = 0; }
}
