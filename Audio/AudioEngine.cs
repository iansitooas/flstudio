using FLLiteStudio.Core;

namespace FLLiteStudio.Audio;

public sealed class AudioEngine : IDisposable
{
    private const int MaxVoicesNormal = 32;
    private const int MaxVoicesEco = 12;
    private const int SliceFrames = 64;

    private readonly object _sync = new();
    private readonly SynthVoice[] _voices;
    private readonly MixerEngine _mixer = new();
    private readonly float[] _sliceOutL = new float[SliceFrames];
    private readonly float[] _sliceOutR = new float[SliceFrames];
    private readonly WinMmAudioOut _audioOut;
    private readonly WinMmMidiIn _midiIn = new();

    private ProjectModel _project;
    private volatile bool _isPlaying;
    private volatile bool _ecoMode;
    private double _playheadStep;
    private int _activeVoiceCount;

    public ProjectModel Project => _project;
    public bool IsPlaying => _isPlaying;
    public double PlayheadStep => _playheadStep;
    public int ActiveVoiceCount => _activeVoiceCount;
    public double CpuLoadPercent => _audioOut.CpuLoadPercent;
    public int BufferFrames => _audioOut.BufferFrames;

    public bool EcoMode
    {
        get => _ecoMode;
        set => _ecoMode = value;
    }

    public event Action<int, float>? MidiKeyboardNoteOn;

    public AudioEngine(ProjectModel initialProject)
    {
        _project = initialProject;
        _voices = new SynthVoice[MaxVoicesNormal];
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new SynthVoice();

        _audioOut = new WinMmAudioOut(RenderRealtimeBuffer, 1024);
        _midiIn.NoteOnReceived += (note, vel) => MidiKeyboardNoteOn?.Invoke(note, vel);
    }

    public void StartAudioDriver() => _audioOut.Start();

    public void SetBufferSize(int frames) => _audioOut.RestartWithBufferSize(frames);

    public bool TryConnectMidiKeyboard(out int deviceCount)
    {
        deviceCount = _midiIn.DeviceCount;
        return _midiIn.TryOpenFirstDevice();
    }

    public void SetProject(ProjectModel project)
    {
        lock (_sync)
        {
            _project = project;
            _isPlaying = false;
            _playheadStep = 0;
            StopAllVoicesLocked();
            _mixer.ResetEffects();
            _mixer.ResetAutomation();
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            _isPlaying = true;
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            _isPlaying = false;
        }
    }

    public void TogglePlay()
    {
        lock (_sync)
        {
            _isPlaying = !_isPlaying;
        }
    }

    public void Stop(bool resetPosition = true)
    {
        lock (_sync)
        {
            _isPlaying = false;
            if (resetPosition) _playheadStep = 0;
            StopAllVoicesLocked();
            _mixer.ResetAutomation();
        }
    }

    public void SeekStep(double step)
    {
        lock (_sync)
        {
            double max = GetLoopLengthStepsLocked(_project, _project.Mode);
            _playheadStep = Math.Clamp(step, 0, Math.Max(0, max - 0.01));
        }
    }

    public void PreviewNote(ChannelModel ch, int midi, float velocity = 0.85f, double durationSec = 0.35)
    {
        lock (_sync)
        {
            LoadedSample? smp = null;
            if (ch.SampleId != null) _project.Samples.TryGetValue(ch.SampleId, out smp);
            var v = AllocateVoiceLocked(_voices, _ecoMode);
            v.TriggerInstrument(ch, midi, velocity, durationSec, 0f, smp, _ecoMode);
        }
    }

    public float GetTrackPeak(int trackId) => _mixer.GetTrackPeak(trackId);

    private void StopAllVoicesLocked()
    {
        for (int i = 0; i < _voices.Length; i++) _voices[i].Active = false;
        _activeVoiceCount = 0;
    }

    private static SynthVoice AllocateVoiceLocked(SynthVoice[] pool, bool ecoMode)
    {
        int cap = ecoMode ? Math.Min(MaxVoicesEco, pool.Length) : pool.Length;
        SynthVoice oldest = pool[0];
        for (int i = 0; i < cap; i++)
        {
            if (!pool[i].Active) return pool[i];
            if (pool[i].AgeSamples > oldest.AgeSamples) oldest = pool[i];
        }
        return oldest;
    }

    private static double GetLoopLengthStepsLocked(ProjectModel proj, PlaybackMode mode)
    {
        if (mode == PlaybackMode.Pattern)
        {
            var pat = proj.GetActivePattern();
            return pat != null ? Math.Max(16, pat.LengthSteps) : 16;
        }
        int maxEnd = 64;
        foreach (var c in proj.Playlist)
        {
            int end = c.StartStep + c.LengthSteps;
            if (end > maxEnd) maxEnd = end;
        }
        return Math.Ceiling(maxEnd / 16.0) * 16.0;
    }

    private void RenderRealtimeBuffer(float[] outL, float[] outR, int totalFrames)
    {
        lock (_sync)
        {
            RenderInternal(_project, _voices, _mixer, _sliceOutL, _sliceOutR, outL, outR, totalFrames, _isPlaying, _ecoMode, ref _playheadStep, isOffline: false);
            int active = 0;
            for (int i = 0; i < _voices.Length; i++) if (_voices[i].Active) active++;
            _activeVoiceCount = active;
        }
    }

    private static void RenderInternal(
        ProjectModel proj,
        SynthVoice[] voices,
        MixerEngine mixer,
        float[] tmpOutL,
        float[] tmpOutR,
        float[] outL,
        float[] outR,
        int totalFrames,
        bool playing,
        bool ecoMode,
        ref double playheadStep,
        bool isOffline)
    {
        int offset = 0;
        double stepDurSec = 60.0 / Math.Clamp(proj.Bpm, 40, 240) / 4.0;
        double stepsPerSample = 1.0 / (stepDurSec * WinMmAudioOut.SampleRate);
        double loopLen = GetLoopLengthStepsLocked(proj, proj.Mode);

        while (offset < totalFrames)
        {
            int n = Math.Min(SliceFrames, totalFrames - offset);
            double prevStep = playheadStep;
            double nextStep = prevStep;

            if (playing)
            {
                nextStep = prevStep + n * stepsPerSample;
                if (nextStep >= loopLen)
                {
                    ScheduleWindow(proj, voices, mixer, prevStep, loopLen, stepDurSec, ecoMode, isOffline);
                    double rem = nextStep - loopLen;
                    if (!isOffline)
                    {
                        ScheduleWindow(proj, voices, mixer, 0.0, rem, stepDurSec, ecoMode, isOffline);
                        playheadStep = rem;
                    }
                    else
                    {
                        playheadStep = nextStep;
                    }
                }
                else
                {
                    ScheduleWindow(proj, voices, mixer, prevStep, nextStep, stepDurSec, ecoMode, isOffline);
                    playheadStep = nextStep;
                }
            }

            mixer.ClearBuses(n);
            for (int v = 0; v < voices.Length; v++)
            {
                if (!voices[v].Active) continue;
                var (bL, bR) = mixer.GetBus(voices[v].MixerTrack);
                voices[v].RenderInto(bL, bR, n, ecoMode);
            }

            int stepInBar = ((int)Math.Floor(playheadStep)) & 15;
            Array.Clear(tmpOutL, 0, n);
            Array.Clear(tmpOutR, 0, n);
            mixer.ProcessMix(proj, tmpOutL, tmpOutR, n, stepInBar, playing, ecoMode);

            Array.Copy(tmpOutL, 0, outL, offset, n);
            Array.Copy(tmpOutR, 0, outR, offset, n);
            offset += n;
        }
    }

    private static void ScheduleWindow(
        ProjectModel proj,
        SynthVoice[] voices,
        MixerEngine mixer,
        double fromStep,
        double toStep,
        double stepDurSec,
        bool ecoMode,
        bool isOffline)
    {
        if (toStep <= fromStep) return;

        // 1. Metronome on beats 0, 4, 8, 12...
        if (!isOffline && proj.Metronome)
        {
            int beatStart = (int)Math.Ceiling(fromStep / 4.0) * 4;
            if (beatStart >= fromStep && beatStart < toStep)
            {
                var v = AllocateVoiceLocked(voices, ecoMode);
                v.TriggerMetronome((beatStart % 16) == 0);
            }
        }

        bool anySoloChannel = false;
        for (int i = 0; i < proj.Channels.Count; i++)
        {
            if (proj.Channels[i].Solo) { anySoloChannel = true; break; }
        }

        if (proj.Mode == PlaybackMode.Pattern)
        {
            var pat = proj.GetActivePattern();
            if (pat != null)
            {
                SchedulePatternRange(proj, pat, voices, fromStep, toStep, stepDurSec, anySoloChannel, ecoMode);
            }
        }
        else
        {
            bool masterFilterTouched = false;
            bool masterVolTouched = false;

            for (int ci = 0; ci < proj.Playlist.Count; ci++)
            {
                var clip = proj.Playlist[ci];
                double cStart = clip.StartStep;
                double cEnd = clip.StartStep + clip.LengthSteps;
                if (toStep <= cStart || fromStep >= cEnd) continue;

                double localFrom = Math.Max(0.0, fromStep - cStart);
                double localTo = Math.Min(clip.LengthSteps, toStep - cStart);

                if (clip.Type == ClipType.Pattern && clip.PatternId != null)
                {
                    var pat = proj.Patterns.FirstOrDefault(p => p.Id == clip.PatternId);
                    if (pat != null)
                    {
                        int patLen = Math.Max(16, pat.LengthSteps);
                        double wFrom = localFrom % patLen;
                        double wTo = wFrom + (localTo - localFrom);
                        if (wTo <= patLen)
                        {
                            SchedulePatternRange(proj, pat, voices, wFrom, wTo, stepDurSec, anySoloChannel, ecoMode);
                        }
                        else
                        {
                            SchedulePatternRange(proj, pat, voices, wFrom, patLen, stepDurSec, anySoloChannel, ecoMode);
                            SchedulePatternRange(proj, pat, voices, 0, wTo - patLen, stepDurSec, anySoloChannel, ecoMode);
                        }
                    }
                }
                else if (clip.Type == ClipType.Audio && clip.SampleId != null)
                {
                    if (fromStep <= cStart && toStep > cStart)
                    {
                        if (proj.Samples.TryGetValue(clip.SampleId, out var smp))
                        {
                            var v = AllocateVoiceLocked(voices, ecoMode);
                            v.TriggerAudioClip(smp, clip.Volume, clip.MixerTrack, clip.LengthSteps * stepDurSec);
                        }
                    }
                }
                else if (clip.Type == ClipType.Automation)
                {
                    double normX = localFrom / Math.Max(1.0, clip.LengthSteps);
                    float val = MusicTheory.EvaluateAutomation(clip.Points, normX);
                    if (clip.AutomationTarget == "MasterFilter")
                    {
                        mixer.MasterAutoFilterNorm = val;
                        masterFilterTouched = true;
                    }
                    else if (clip.AutomationTarget == "MasterVolume")
                    {
                        mixer.MasterAutoGain = val * 1.15f;
                        masterVolTouched = true;
                    }
                    else if (clip.AutomationTarget.StartsWith("TrackVol:") &&
                             int.TryParse(clip.AutomationTarget["TrackVol:".Length..], out int tid) &&
                             tid >= 1 && tid < MixerEngine.TrackCount)
                    {
                        mixer.TrackAutoGain[tid] = val * 1.25f;
                    }
                }
            }

            if (!masterFilterTouched) mixer.MasterAutoFilterNorm += (1f - mixer.MasterAutoFilterNorm) * 0.08f;
            if (!masterVolTouched) mixer.MasterAutoGain += (1f - mixer.MasterAutoGain) * 0.08f;
        }
    }

    private static void SchedulePatternRange(
        ProjectModel proj,
        PatternModel pat,
        SynthVoice[] voices,
        double fromStep,
        double toStep,
        double stepDurSec,
        bool anySoloChannel,
        bool ecoMode)
    {
        double swingOffset = Math.Clamp(proj.Swing, 0f, 0.7f) * 0.45;
        int patLen = Math.Max(16, pat.LengthSteps);

        for (int ci = 0; ci < proj.Channels.Count; ci++)
        {
            var ch = proj.Channels[ci];
            if (ch.Muted || (anySoloChannel && !ch.Solo)) continue;
            if (!pat.Channels.TryGetValue(ch.Id, out var cd)) continue;

            LoadedSample? smp = null;
            if (ch.SampleId != null) proj.Samples.TryGetValue(ch.SampleId, out smp);

            // A) Step Sequencer triggers (with Swing on odd steps)
            int firstInt = Math.Max(0, (int)Math.Floor(fromStep) - 1);
            int lastInt = Math.Min(patLen - 1, (int)Math.Ceiling(toStep));
            for (int s = firstInt; s <= lastInt; s++)
            {
                if (s >= cd.Steps.Length || !cd.Steps[s]) continue;
                double firePos = s + ((s & 1) == 1 ? swingOffset : 0.0);
                if (firePos >= fromStep && firePos < toStep)
                {
                    var v = AllocateVoiceLocked(voices, ecoMode);
                    float vel = s < cd.StepVelocity.Length ? cd.StepVelocity[s] : 0.85f;
                    v.TriggerInstrument(ch, 60, vel, stepDurSec * 1.15, 0f, smp, ecoMode);
                }
            }

            // B) Piano Roll notes
            var notes = cd.Notes;
            for (int ni = 0; ni < notes.Count; ni++)
            {
                var note = notes[ni];
                double start = note.StartStep;
                if (start >= fromStep && start < toStep)
                {
                    var v = AllocateVoiceLocked(voices, ecoMode);
                    v.TriggerInstrument(ch, note.Pitch, note.Velocity, Math.Max(0.04, note.DurationSteps * stepDurSec), note.Pan, smp, ecoMode);
                }
            }
        }
    }

    // ========================================================================
    // Offline Faster-Than-Realtime Studio WAV Export (Exact same DSP engine!)
    // ========================================================================
    public void RenderOfflineWav(string outputPath, PlaybackMode exportMode, int bitDepth = 16)
    {
        ProjectModel snapshot;
        Dictionary<string, LoadedSample> samplesRef;
        lock (_sync)
        {
            string json = ProjectIO.SerializeSnapshot(_project);
            snapshot = ProjectIO.DeserializeSnapshot(json) ?? _project;
            snapshot.Mode = exportMode;
            samplesRef = new Dictionary<string, LoadedSample>(_project.Samples);
        }
        foreach (var kv in samplesRef) snapshot.Samples[kv.Key] = kv.Value;

        double totalSteps = GetLoopLengthStepsLocked(snapshot, exportMode);
        double stepDurSec = 60.0 / Math.Clamp(snapshot.Bpm, 40, 240) / 4.0;
        double totalSec = totalSteps * stepDurSec + 1.6; // tail for reverb/delay
        int totalFrames = (int)Math.Ceiling(totalSec * WinMmAudioOut.SampleRate);

        var outL = new float[totalFrames];
        var outR = new float[totalFrames];
        var offlineVoices = new SynthVoice[MaxVoicesNormal];
        for (int i = 0; i < offlineVoices.Length; i++) offlineVoices[i] = new SynthVoice();
        var offlineMixer = new MixerEngine();

        double playhead = 0;
        const int chunk = 2048;
        var bufL = new float[chunk];
        var bufR = new float[chunk];
        var offSliceL = new float[SliceFrames];
        var offSliceR = new float[SliceFrames];

        int pos = 0;
        while (pos < totalFrames)
        {
            int n = Math.Min(chunk, totalFrames - pos);
            bool stillSeq = playhead < totalSteps;
            RenderInternal(snapshot, offlineVoices, offlineMixer, offSliceL, offSliceR, bufL, bufR, n, stillSeq, ecoMode: false, ref playhead, isOffline: true);
            Array.Copy(bufL, 0, outL, pos, n);
            Array.Copy(bufR, 0, outR, pos, n);
            pos += n;
        }

        ProjectIO.WriteWavFile(outputPath, outL, outR, WinMmAudioOut.SampleRate, bitDepth);
    }

    public void Dispose()
    {
        _midiIn.Dispose();
        _audioOut.Dispose();
    }
}
