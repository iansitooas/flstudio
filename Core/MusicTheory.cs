namespace FLLiteStudio.Core;

public readonly record struct ScaleDefinition(string Name, int[] Intervals);
public readonly record struct ChordDefinition(string Name, int[] Intervals);
public readonly record struct GrossBeatPreset(string Name, float[] Curve16);

public static class MusicTheory
{
    public static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static readonly ScaleDefinition[] Scales =
    {
        new("Sin escala (Libre)", new[] { 0,1,2,3,4,5,6,7,8,9,10,11 }),
        new("Mayor", new[] { 0,2,4,5,7,9,11 }),
        new("Menor Natural", new[] { 0,2,3,5,7,8,10 }),
        new("Menor Armónica (Trap)", new[] { 0,2,3,5,7,8,11 }),
        new("Frigia (Oscura)", new[] { 0,1,3,5,7,8,10 }),
        new("Dórica (Lo-Fi / House)", new[] { 0,2,3,5,7,9,10 }),
        new("Pentatónica Menor", new[] { 0,3,5,7,10 }),
        new("Pentatónica Mayor", new[] { 0,2,4,7,9 }),
    };

    public static readonly ChordDefinition[] Chords =
    {
        new("Nota simple", new[] { 0 }),
        new("Acorde Mayor", new[] { 0, 4, 7 }),
        new("Acorde Menor", new[] { 0, 3, 7 }),
        new("Maj7 (Lo-Fi)", new[] { 0, 4, 7, 11 }),
        new("Min7 (Trap/Jazz)", new[] { 0, 3, 7, 10 }),
        new("Sus2", new[] { 0, 2, 7 }),
        new("Power Chord (5ta)", new[] { 0, 7, 12 }),
    };

    public static readonly GrossBeatPreset[] GrossBeatPresets =
    {
        new("Sidechain 1/4 Pump", new[] { 0.06f, 0.52f, 0.90f, 1f, 0.06f, 0.52f, 0.90f, 1f, 0.06f, 0.52f, 0.90f, 1f, 0.06f, 0.52f, 0.90f, 1f }),
        new("Duck 1/2 Pesado", new[] { 0.05f, 0.25f, 0.55f, 0.80f, 0.95f, 1f, 1f, 0.95f, 0.05f, 0.25f, 0.55f, 0.80f, 0.95f, 1f, 1f, 0.95f }),
        new("Trance Gate 1/16", new[] { 1f, 0.08f, 1f, 0.08f, 1f, 1f, 0.08f, 1f, 0.08f, 1f, 0.08f, 1f, 1f, 0.08f, 0.85f, 0.08f }),
        new("Stutter 1/8 Chop", new[] { 1f, 0.06f, 1f, 0.06f, 1f, 0.06f, 1f, 0.06f, 1f, 0.06f, 1f, 0.06f, 1f, 0.06f, 1f, 0.06f }),
        new("Trap Bounce Gate", new[] { 1f, 0.88f, 0.12f, 1f, 0.12f, 0.92f, 1f, 0.10f, 1f, 0.85f, 0.12f, 1f, 0.92f, 0.12f, 1f, 0.22f }),
    };

    public static readonly string[] DelayDivisionNames = { "1/4", "1/8", "1/8 Puntillo", "1/16" };
    public static readonly double[] DelayDivisionBeats = { 1.0, 0.5, 0.75, 0.25 };

    public static float MidiToFreq(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

    public static string MidiToName(int midi)
    {
        int oct = (midi / 12) - 1;
        return $"{NoteNames[((midi % 12) + 12) % 12]}{oct}";
    }

    public static bool IsBlackKey(int midi)
    {
        int n = ((midi % 12) + 12) % 12;
        return n is 1 or 3 or 6 or 8 or 10;
    }

    public static bool IsInScale(int midi, int root, int scaleIndex)
    {
        if (scaleIndex <= 0 || scaleIndex >= Scales.Length) return true;
        int rel = (((midi - root) % 12) + 12) % 12;
        return Array.IndexOf(Scales[scaleIndex].Intervals, rel) >= 0;
    }

    public static float EvaluateAutomation(List<AutomationPoint> points, double normX)
    {
        if (points == null || points.Count == 0) return 1f;
        double x = Math.Clamp(normX, 0.0, 1.0);
        if (x <= points[0].X) return (float)points[0].Y;
        var last = points[^1];
        if (x >= last.X) return (float)last.Y;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (x >= a.X && x <= b.X)
            {
                double span = Math.Max(0.0001, b.X - a.X);
                double t = (x - a.X) / span;
                return (float)(a.Y + (b.Y - a.Y) * t);
            }
        }
        return (float)last.Y;
    }

    public static string GetInstrumentDisplayName(InstrumentType type) => type switch
    {
        InstrumentType.DrumSynth => "Batería (FPC Drums)",
        InstrumentType.Bass808 => "Bajo 808 (SubLab)",
        InstrumentType.Osc3x => "Sintetizador (3xOsc)",
        InstrumentType.SytrusFM => "Campanas / Pluck (Sytrus FM)",
        InstrumentType.SuperSaw => "Acordes / Lead (SuperSaw)",
        InstrumentType.KeysPiano => "Piano / Teclado (FL Keys)",
        InstrumentType.WavSampler => "Audio Propio (Sampler WAV)",
        _ => type.ToString()
    };

    public static string GetFxDisplayName(FxType type) => type switch
    {
        FxType.ParametricEq2 => "Ecualizador 5 Bandas (EQ 2)",
        FxType.Reverb2 => "Reverb de Sala (Reverb 2)",
        FxType.Delay3 => "Eco al Ritmo (Delay 3)",
        FxType.Soundgoodizer => "Realce Rápido (Soundgoodizer)",
        FxType.Distortion => "Distorsión / Saturación",
        FxType.Chorus => "Efecto Coro (Chorus)",
        FxType.GrossBeat => "Bombeo / Corte Rítmico (Gross Beat)",
        FxType.Limiter => "Limitador de Volumen (Limiter)",
        _ => type.ToString()
    };

    public static void QuantizeNotes(List<NoteModel> notes, double snap, int maxSteps)
    {
        double q = snap > 0 ? snap : 1.0;
        foreach (var n in notes)
        {
            n.StartStep = Math.Clamp(Math.Round(n.StartStep / q) * q, 0, maxSteps - 0.25);
            n.DurationSteps = Math.Max(q, Math.Round(n.DurationSteps / q) * q);
        }
    }

    public static void HumanizeNotes(List<NoteModel> notes, int maxSteps)
    {
        var rng = Random.Shared;
        foreach (var n in notes)
        {
            n.Velocity = Math.Clamp(n.Velocity + (float)(rng.NextDouble() - 0.5) * 0.20f, 0.28f, 1.0f);
            n.StartStep = Math.Clamp(n.StartStep + (rng.NextDouble() - 0.5) * 0.14, 0, maxSteps - 0.15);
        }
    }

    public static void StrumNotes(List<NoteModel> notes, int maxSteps)
    {
        var groups = notes.GroupBy(n => (int)Math.Round(n.StartStep * 4.0));
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(n => n.Pitch).ToList();
            if (ordered.Count < 2) continue;
            for (int i = 0; i < ordered.Count; i++)
            {
                double off = i * 0.12;
                ordered[i].StartStep = Math.Clamp(ordered[i].StartStep + off, 0, maxSteps - 0.2);
                ordered[i].DurationSteps = Math.Max(0.25, ordered[i].DurationSteps - off * 0.4);
            }
        }
    }

    public static List<NoteModel> ArpeggiateNotes(List<NoteModel> notes, double stepSize, int maxSteps)
    {
        double sz = stepSize > 0 ? Math.Min(stepSize, 1.0) : 1.0;
        var result = new List<NoteModel>();
        var groups = notes.GroupBy(n => (int)Math.Round(n.StartStep * 2.0));
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(n => n.Pitch).ToList();
            if (ordered.Count < 2)
            {
                result.AddRange(ordered);
                continue;
            }
            double start = ordered[0].StartStep;
            double span = ordered.Max(n => n.DurationSteps);
            double cur = start;
            int idx = 0;
            while (cur < start + span - 0.05 && cur < maxSteps)
            {
                var src = ordered[idx % ordered.Count];
                result.Add(new NoteModel
                {
                    Pitch = src.Pitch,
                    StartStep = cur,
                    DurationSteps = sz * 0.9,
                    Velocity = src.Velocity,
                    Pan = src.Pan
                });
                cur += sz;
                idx++;
            }
        }
        return result;
    }
}
