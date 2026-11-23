using System.Text.Json.Serialization;

namespace FLLiteStudio.Core;

public enum InstrumentType
{
    DrumSynth,
    Bass808,
    Osc3x,
    SytrusFM,
    SuperSaw,
    KeysPiano,
    PluckedString,
    BrassWind,
    StringsPad,
    WorldMallet,
    WavSampler
}

public enum DrumKind
{
    Kick,
    Kick808,
    KickBoomBap,
    Snare,
    SnareTrap,
    SnareReggaeton,
    Clap,
    ClosedHat,
    OpenHat,
    Rimshot,
    Perc,
    Cowbell,
    Tom,
    Shaker,
    Crash
}

public enum KeysMode
{
    Rhodes,
    GrandPiano,
    BellKeys,
    Organ,
    Harpsichord,
    Accordion,
    Wurlitzer
}

public enum PluckStyle
{
    AcousticNylon,
    AcousticSteel,
    ElectricClean,
    ElectricRock,
    ElectricBass,
    Harp,
    Koto,
    Pizzicato
}

public enum WindStyle
{
    Trumpet,
    TrapBrass,
    Saxophone,
    Flute,
    DarkFlute,
    Clarinet,
    Trombone
}

public enum PadStyle
{
    ViolinSolo,
    Cello,
    OrchestraStrings,
    ChoirAah,
    SpacePad,
    VintageTapePad,
    DarkTrapPad
}

public enum MalletStyle
{
    Marimba,
    Xylophone,
    Vibraphone,
    Kalimba,
    SteelDrum,
    TubularBell,
    MusicBox
}

public enum WaveKind
{
    Sine,
    Triangle,
    Saw,
    Square
}

public enum FxType
{
    ParametricEq2,
    Reverb2,
    Delay3,
    Soundgoodizer,
    Distortion,
    Chorus,
    GrossBeat,
    Limiter
}

public enum PlaybackMode
{
    Pattern,
    Song
}

public enum ClipType
{
    Pattern,
    Audio,
    Automation
}

public sealed class ChannelModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Canal";
    public InstrumentType Type { get; set; } = InstrumentType.Osc3x;
    public string ColorHex { get; set; } = "#E8832A";
    public int MixerTrack { get; set; } = 1; // 0 = Master, 1..12 = Insert
    public float Volume { get; set; } = 0.85f;
    public float Pan { get; set; } = 0f;
    public bool Muted { get; set; }
    public bool Solo { get; set; }

    // Unified clean synth/drum/sampler parameters
    public DrumKind DrumKind { get; set; } = DrumKind.Kick;
    public KeysMode KeysMode { get; set; } = KeysMode.Rhodes;
    public PluckStyle PluckStyle { get; set; } = PluckStyle.AcousticNylon;
    public WindStyle WindStyle { get; set; } = WindStyle.Flute;
    public PadStyle PadStyle { get; set; } = PadStyle.OrchestraStrings;
    public MalletStyle MalletStyle { get; set; } = MalletStyle.Marimba;
    public WaveKind Wave1 { get; set; } = WaveKind.Saw;
    public WaveKind Wave2 { get; set; } = WaveKind.Square;
    public int Coarse2 { get; set; } = -12;
    public float Osc2Mix { get; set; } = 0.35f;

    public float PitchSemitones { get; set; } = 0f;
    public float Detune { get; set; } = 14f;
    public float CutoffHz { get; set; } = 4200f;
    public float Resonance { get; set; } = 1.6f;
    public float FilterEnv { get; set; } = 1800f;
    public float Drive { get; set; } = 0.25f;
    public float Punch { get; set; } = 0.75f;
    public float Tone { get; set; } = 0.65f;
    public float VibratoDepth { get; set; } = 0.15f;
    public float VibratoRateHz { get; set; } = 5.2f;

    // FM (Sytrus)
    public float FmRatio { get; set; } = 2.0f;
    public float FmIndex { get; set; } = 2.6f;
    public float FmDecay { get; set; } = 0.28f;

    // Envelope ADSR (seconds)
    public float Attack { get; set; } = 0.008f;
    public float Decay { get; set; } = 0.35f;
    public float Sustain { get; set; } = 0.55f;
    public float Release { get; set; } = 0.25f;

    // Sampler
    public string? SampleId { get; set; }
    public string SampleName { get; set; } = "(Sin archivo WAV)";
    public float TrimStart { get; set; } = 0f;
    public float TrimEnd { get; set; } = 1f;
    public bool Reverse { get; set; }
}

public sealed class NoteModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public int Pitch { get; set; } = 60;      // MIDI 24..96
    public double StartStep { get; set; }     // in 16th steps
    public double DurationSteps { get; set; } = 2.0;
    public float Velocity { get; set; } = 0.85f;
    public float Pan { get; set; } = 0f;
}

public sealed class PatternChannelData
{
    public bool[] Steps { get; set; } = new bool[32];
    public float[] StepVelocity { get; set; } = CreateFilled(32, 0.85f);
    public List<NoteModel> Notes { get; set; } = new();

    public void EnsureLength(int length)
    {
        if (Steps.Length >= length) return;
        int old = Steps.Length;
        var steps = Steps;
        var vels = StepVelocity;
        Array.Resize(ref steps, length);
        Array.Resize(ref vels, length);
        for (int i = old; i < length; i++) vels[i] = 0.85f;
        Steps = steps;
        StepVelocity = vels;
    }

    private static float[] CreateFilled(int n, float val)
    {
        var arr = new float[n];
        Array.Fill(arr, val);
        return arr;
    }
}

public sealed class PatternModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Patrón 1";
    public string ColorHex { get; set; } = "#E8832A";
    public int LengthSteps { get; set; } = 32;
    public Dictionary<string, PatternChannelData> Channels { get; set; } = new();

    public PatternChannelData GetOrCreateChannelData(string channelId)
    {
        if (!Channels.TryGetValue(channelId, out var data))
        {
            data = new PatternChannelData();
            Channels[channelId] = data;
        }
        data.EnsureLength(LengthSteps);
        return data;
    }
}

public sealed class AutomationPoint
{
    public double X { get; set; } // 0..1 normalized along clip length
    public double Y { get; set; } // 0..1 normalized value
}

public sealed class PlaylistClipModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ClipType Type { get; set; } = ClipType.Pattern;
    public string Name { get; set; } = "Clip";
    public string ColorHex { get; set; } = "#E8832A";
    public int TrackIndex { get; set; } = 0; // 0..11
    public int StartStep { get; set; } = 0;
    public int LengthSteps { get; set; } = 32;

    // Pattern clip
    public string? PatternId { get; set; }

    // Audio clip
    public string? SampleId { get; set; }
    public float Volume { get; set; } = 0.9f;
    public int MixerTrack { get; set; } = 0;

    // Automation clip: "MasterFilter", "MasterVolume", "TrackVol:1".."TrackVol:12"
    public string AutomationTarget { get; set; } = "MasterFilter";
    public List<AutomationPoint> Points { get; set; } = new();
}

public sealed class FxSlotModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public FxType Type { get; set; } = FxType.ParametricEq2;
    public bool Enabled { get; set; } = true;

    // EQ 2 (5 bands in dB: -15..+15)
    public float EqLowDb { get; set; } = 0f;       // 80 Hz
    public float EqLowMidDb { get; set; } = 0f;    // 320 Hz
    public float EqMidDb { get; set; } = 0f;       // 1200 Hz
    public float EqHighMidDb { get; set; } = 0f;   // 4200 Hz
    public float EqHighDb { get; set; } = 0f;      // 10500 Hz

    // Shared Wet/Mix/Amount
    public float Mix { get; set; } = 0.32f;

    // Reverb 2
    public float ReverbDecay { get; set; } = 0.72f; // 0.1..0.95
    public float ReverbDamp { get; set; } = 0.45f;  // 0..0.9

    // Delay 3
    public int DelayDivisionIndex { get; set; } = 2; // 0=1/4, 1=1/8, 2=1/8D, 3=1/16
    public float DelayFeedback { get; set; } = 0.38f;
    public float DelayToneHz { get; set; } = 4200f;

    // Soundgoodizer
    public int SoundgoodizerMode { get; set; } = 0; // 0=A, 1=B, 2=C, 3=D
    public float SoundgoodizerAmount { get; set; } = 0.38f;

    // Distortion
    public float DistDrive { get; set; } = 0.45f;
    public int DistMode { get; set; } = 0; // 0=Warm, 1=Tube, 2=Hard, 3=Bitcrush

    // Chorus
    public float ChorusRateHz { get; set; } = 1.2f;
    public float ChorusDepth { get; set; } = 0.45f;

    // Gross Beat
    public int GrossBeatPreset { get; set; } = 0; // 0=1/4 Sidechain, 1=1/2 Duck, 2=Trance Gate, 3=Stutter, 4=Trap Bounce

    // Limiter
    public float LimiterThresholdDb { get; set; } = -6f;
    public float LimiterGain { get; set; } = 1.05f;
}

public sealed class MixerTrackModel
{
    public int Id { get; set; } // 0 = Master, 1..12 = Insert
    public string Name { get; set; } = "Insert";
    public string ColorHex { get; set; } = "#4A90E2";
    public float Volume { get; set; } = 0.85f;
    public float Pan { get; set; } = 0f;
    public bool Muted { get; set; }
    public bool Solo { get; set; }
    public List<FxSlotModel> Effects { get; set; } = new();
}

public sealed class LoadedSample
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Sample.wav";
    public int SampleRate { get; set; } = 44100;
    public float[] Left { get; set; } = Array.Empty<float>();
    public float[] Right { get; set; } = Array.Empty<float>();
    public float[] Peaks { get; set; } = Array.Empty<float>();
    public double DurationSeconds => SampleRate > 0 ? (double)Left.Length / SampleRate : 0;
}

public sealed class ProjectModel
{
    public string Name { get; set; } = "Proyecto Nuevo";
    public int Bpm { get; set; } = 140;
    public float Swing { get; set; } = 0.08f;
    public PlaybackMode Mode { get; set; } = PlaybackMode.Pattern;
    public bool Metronome { get; set; }
    public int ScaleRoot { get; set; } = 0;      // 0=C .. 11=B
    public int ScaleIndex { get; set; } = 2;     // Minor
    public string ActivePatternId { get; set; } = "";
    public string ActiveChannelId { get; set; } = "";
    public int SelectedMixerTrack { get; set; } = 0;

    public List<ChannelModel> Channels { get; set; } = new();
    public List<PatternModel> Patterns { get; set; } = new();
    public List<PlaylistClipModel> Playlist { get; set; } = new();
    public List<MixerTrackModel> Mixer { get; set; } = new();

    [JsonIgnore]
    public Dictionary<string, LoadedSample> Samples { get; } = new();

    public PatternModel? GetActivePattern() =>
        Patterns.FirstOrDefault(p => p.Id == ActivePatternId) ?? Patterns.FirstOrDefault();

    public ChannelModel? GetActiveChannel() =>
        Channels.FirstOrDefault(c => c.Id == ActiveChannelId) ?? Channels.FirstOrDefault();

    public void EnsureIntegrity()
    {
        if (Mixer.Count == 0)
        {
            Mixer = DemoProjects.CreateDefaultMixer();
        }
        if (Patterns.Count == 0)
        {
            var p = new PatternModel { Name = "Patrón 1", LengthSteps = 32 };
            Patterns.Add(p);
            ActivePatternId = p.Id;
        }
        if (Patterns.All(p => p.Id != ActivePatternId))
            ActivePatternId = Patterns[0].Id;

        if (Channels.Count > 0 && Channels.All(c => c.Id != ActiveChannelId))
            ActiveChannelId = Channels[0].Id;

        foreach (var p in Patterns)
        {
            foreach (var c in Channels)
                p.GetOrCreateChannelData(c.Id);
        }
    }
}
