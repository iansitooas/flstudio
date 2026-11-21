namespace FLLiteStudio.Core;

public static class DemoProjects
{
    public static readonly string[] ChannelPalette =
    {
        "#E05A47", "#E8832A", "#D9A826", "#46C273",
        "#29B6A8", "#3B9EFF", "#7B6BF2", "#B564E8", "#E05297"
    };

    public static List<MixerTrackModel> CreateDefaultMixer()
    {
        var list = new List<MixerTrackModel>
        {
            new()
            {
                Id = 0,
                Name = "Master",
                ColorHex = "#E8832A",
                Volume = 0.92f,
                Effects =
                {
                    new FxSlotModel { Type = FxType.Soundgoodizer, Enabled = true, SoundgoodizerAmount = 0.28f, SoundgoodizerMode = 0 },
                    new FxSlotModel { Type = FxType.Limiter, Enabled = true, LimiterThresholdDb = -3.0f, LimiterGain = 1.04f }
                }
            }
        };

        string[] names =
        {
            "Kick", "Snare / Clap", "Hi-Hats", "808 / Bajo",
            "Melodía", "Teclados", "Pluck / Arp", "Perc / FX",
            "Insert 9", "Insert 10", "Insert 11", "Insert 12"
        };

        for (int i = 1; i <= 12; i++)
        {
            list.Add(new MixerTrackModel
            {
                Id = i,
                Name = names[i - 1],
                ColorHex = ChannelPalette[(i - 1) % ChannelPalette.Length],
                Volume = 0.85f
            });
        }
        return list;
    }

    private static void SetSteps(PatternChannelData data, params int[] indices)
    {
        foreach (int i in indices)
        {
            if (i >= 0 && i < data.Steps.Length) data.Steps[i] = true;
        }
    }

    private static void AddNotes(PatternChannelData data, params (int pitch, double start, double dur, float vel)[] notes)
    {
        foreach (var (p, s, d, v) in notes)
        {
            data.Notes.Add(new NoteModel { Pitch = p, StartStep = s, DurationSteps = d, Velocity = v });
        }
    }

    public static ProjectModel CreateTrapDemo()
    {
        var proj = new ProjectModel
        {
            Name = "Trap 808 - Metro Dark",
            Bpm = 140,
            Swing = 0.08f,
            Mode = PlaybackMode.Pattern,
            ScaleRoot = 0,
            ScaleIndex = 2,
            Mixer = CreateDefaultMixer()
        };

        proj.Mixer[4].Effects.Add(new FxSlotModel { Type = FxType.Distortion, DistDrive = 0.35f, DistMode = 1, Mix = 0.45f });
        proj.Mixer[5].Effects.Add(new FxSlotModel { Type = FxType.Reverb2, Mix = 0.34f, ReverbDecay = 0.78f, ReverbDamp = 0.45f });
        proj.Mixer[5].Effects.Add(new FxSlotModel { Type = FxType.Delay3, Mix = 0.22f, DelayDivisionIndex = 2, DelayFeedback = 0.36f });
        proj.Mixer[6].Effects.Add(new FxSlotModel { Type = FxType.ParametricEq2, EqLowDb = -5f, EqHighMidDb = 3f });
        proj.Mixer[6].Effects.Add(new FxSlotModel { Type = FxType.Reverb2, Mix = 0.40f, ReverbDecay = 0.82f, ReverbDamp = 0.50f });

        var kick = new ChannelModel { Name = "Trap Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = "#E05A47", MixerTrack = 1, Volume = 0.96f, Decay = 0.28f, Punch = 0.92f, Tone = 0.55f, Drive = 0.22f };
        var clap = new ChannelModel { Name = "808 Clap", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Clap, ColorHex = "#E8832A", MixerTrack = 2, Volume = 0.86f, Decay = 0.24f, Tone = 0.72f };
        var hat = new ChannelModel { Name = "Hi-Hat Tight", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.ClosedHat, ColorHex = "#D9A826", MixerTrack = 3, Volume = 0.65f, Decay = 0.08f, Tone = 0.78f };
        var ohat = new ChannelModel { Name = "Open Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.OpenHat, ColorHex = "#46C273", MixerTrack = 3, Volume = 0.56f, Decay = 0.28f, Tone = 0.70f };
        var bass = new ChannelModel { Name = "SubLab 808", Type = InstrumentType.Bass808, ColorHex = "#E05297", MixerTrack = 4, Volume = 0.94f, Drive = 0.44f, Decay = 1.15f, Punch = 0.82f, CutoffHz = 860f, Release = 0.16f };
        var bell = new ChannelModel { Name = "Sytrus Dark Bell", Type = InstrumentType.SytrusFM, ColorHex = "#3B9EFF", MixerTrack = 5, Volume = 0.68f, FmRatio = 3.0f, FmIndex = 3.1f, FmDecay = 0.30f, CutoffHz = 5600f, Attack = 0.004f, Decay = 0.52f, Sustain = 0.15f, Release = 0.42f };
        var keys = new ChannelModel { Name = "FL Keys Pad", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Rhodes, ColorHex = "#B564E8", MixerTrack = 6, Volume = 0.64f, Tone = 0.50f, Decay = 1.7f, Release = 0.45f };

        proj.Channels.AddRange(new[] { kick, clap, hat, ohat, bass, bell, keys });

        var pat1 = new PatternModel { Name = "Patrón 1 - Beat Completo", ColorHex = "#E8832A", LengthSteps = 32 };
        SetSteps(pat1.GetOrCreateChannelData(kick.Id), 0, 6, 10, 16, 22, 26, 29);
        SetSteps(pat1.GetOrCreateChannelData(clap.Id), 4, 12, 20, 28);
        SetSteps(pat1.GetOrCreateChannelData(hat.Id), 0, 2, 4, 6, 7, 8, 10, 12, 14, 15, 16, 18, 20, 22, 24, 26, 27, 28, 30, 31);
        SetSteps(pat1.GetOrCreateChannelData(ohat.Id), 2, 18, 30);

        AddNotes(pat1.GetOrCreateChannelData(bass.Id),
            (36, 0, 5.5, 0.95f), (36, 6, 3.5, 0.90f), (39, 10, 5.5, 0.92f),
            (36, 16, 5.5, 0.95f), (36, 22, 3.5, 0.90f), (43, 26, 2.5, 0.90f), (41, 29, 2.5, 0.88f));

        AddNotes(pat1.GetOrCreateChannelData(bell.Id),
            (72, 0, 2, 0.82f), (75, 2, 2, 0.78f), (79, 4, 2, 0.85f), (75, 6, 2, 0.74f),
            (80, 8, 2, 0.84f), (79, 10, 2, 0.76f), (75, 12, 4, 0.80f),
            (72, 16, 2, 0.82f), (75, 18, 2, 0.78f), (79, 20, 2, 0.88f), (84, 22, 2, 0.82f),
            (83, 24, 2, 0.84f), (79, 26, 2, 0.76f), (75, 28, 4, 0.78f));

        AddNotes(pat1.GetOrCreateChannelData(keys.Id),
            (60, 0, 15, 0.65f), (63, 0, 15, 0.62f), (67, 0, 15, 0.62f),
            (56, 16, 7.5, 0.65f), (60, 16, 7.5, 0.62f), (63, 16, 7.5, 0.62f),
            (55, 24, 7.5, 0.65f), (59, 24, 7.5, 0.62f), (62, 24, 7.5, 0.62f));

        var pat2 = new PatternModel { Name = "Patrón 2 - Intro Melodía", ColorHex = "#3B9EFF", LengthSteps = 32 };
        foreach (var n in pat1.GetOrCreateChannelData(bell.Id).Notes)
            pat2.GetOrCreateChannelData(bell.Id).Notes.Add(new NoteModel { Pitch = n.Pitch, StartStep = n.StartStep, DurationSteps = n.DurationSteps, Velocity = n.Velocity });
        foreach (var n in pat1.GetOrCreateChannelData(keys.Id).Notes)
            pat2.GetOrCreateChannelData(keys.Id).Notes.Add(new NoteModel { Pitch = n.Pitch, StartStep = n.StartStep, DurationSteps = n.DurationSteps, Velocity = n.Velocity });

        var pat3 = new PatternModel { Name = "Patrón 3 - Build Percusión", ColorHex = "#46C273", LengthSteps = 16 };
        SetSteps(pat3.GetOrCreateChannelData(clap.Id), 4, 12);
        SetSteps(pat3.GetOrCreateChannelData(hat.Id), 0, 2, 4, 6, 8, 10, 12, 13, 14, 15);

        proj.Patterns.AddRange(new[] { pat1, pat2, pat3 });
        proj.ActivePatternId = pat1.Id;
        proj.ActiveChannelId = bell.Id;

        proj.Playlist.AddRange(new[]
        {
            new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat2.Id, Name = pat2.Name, ColorHex = pat2.ColorHex, TrackIndex = 0, StartStep = 0, LengthSteps = 32 },
            new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat3.Id, Name = pat3.Name, ColorHex = pat3.ColorHex, TrackIndex = 1, StartStep = 16, LengthSteps = 16 },
            new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat1.Id, Name = pat1.Name, ColorHex = pat1.ColorHex, TrackIndex = 0, StartStep = 32, LengthSteps = 32 },
            new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat1.Id, Name = pat1.Name, ColorHex = pat1.ColorHex, TrackIndex = 0, StartStep = 64, LengthSteps = 32 },
            new PlaylistClipModel
            {
                Type = ClipType.Automation,
                Name = "Auto: Filtro Master (Sweep)",
                AutomationTarget = "MasterFilter",
                ColorHex = "#E05297",
                TrackIndex = 3,
                StartStep = 0,
                LengthSteps = 32,
                Points =
                {
                    new AutomationPoint { X = 0.0, Y = 0.25 },
                    new AutomationPoint { X = 0.65, Y = 0.58 },
                    new AutomationPoint { X = 0.96, Y = 1.0 },
                    new AutomationPoint { X = 1.0, Y = 1.0 }
                }
            }
        });

        proj.EnsureIntegrity();
        return proj;
    }

    public static ProjectModel CreateEdmDemo()
    {
        var proj = new ProjectModel
        {
            Name = "EDM Synthwave - Neon Drive",
            Bpm = 126,
            Swing = 0f,
            Mode = PlaybackMode.Pattern,
            ScaleRoot = 9,
            ScaleIndex = 2,
            Mixer = CreateDefaultMixer()
        };

        proj.Mixer[5].Effects.Add(new FxSlotModel { Type = FxType.GrossBeat, GrossBeatPreset = 0, Mix = 0.85f });
        proj.Mixer[5].Effects.Add(new FxSlotModel { Type = FxType.Reverb2, Mix = 0.30f, ReverbDecay = 0.72f });
        proj.Mixer[7].Effects.Add(new FxSlotModel { Type = FxType.Delay3, Mix = 0.28f, DelayDivisionIndex = 2, DelayFeedback = 0.42f });

        var kick = new ChannelModel { Name = "EDM Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = "#E05A47", MixerTrack = 1, Volume = 0.96f, Decay = 0.26f, Punch = 0.95f, Tone = 0.72f };
        var clap = new ChannelModel { Name = "House Clap", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Clap, ColorHex = "#E8832A", MixerTrack = 2, Volume = 0.82f, Decay = 0.22f, Tone = 0.78f };
        var hat = new ChannelModel { Name = "Offbeat Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.OpenHat, ColorHex = "#D9A826", MixerTrack = 3, Volume = 0.62f, Decay = 0.18f, Tone = 0.82f };
        var bass = new ChannelModel { Name = "3xOsc Rolling Bass", Type = InstrumentType.Osc3x, ColorHex = "#E05297", MixerTrack = 4, Volume = 0.80f, Wave1 = WaveKind.Saw, Wave2 = WaveKind.Square, Coarse2 = -12, Osc2Mix = 0.5f, CutoffHz = 1400f, Resonance = 2.6f, FilterEnv = 1800f, Attack = 0.004f, Decay = 0.16f, Sustain = 0.22f, Release = 0.10f };
        var saw = new ChannelModel { Name = "FLEX SuperSaw", Type = InstrumentType.SuperSaw, ColorHex = "#3B9EFF", MixerTrack = 5, Volume = 0.74f, Detune = 26f, CutoffHz = 5200f, Resonance = 1.4f, FilterEnv = 2000f, Attack = 0.01f, Decay = 0.35f, Sustain = 0.75f, Release = 0.32f };
        var pluck = new ChannelModel { Name = "Sytrus Pluck", Type = InstrumentType.SytrusFM, ColorHex = "#B564E8", MixerTrack = 7, Volume = 0.65f, FmRatio = 2.0f, FmIndex = 2.4f, FmDecay = 0.18f, CutoffHz = 7800f, Attack = 0.003f, Decay = 0.24f, Sustain = 0.08f, Release = 0.22f };

        proj.Channels.AddRange(new[] { kick, clap, hat, bass, saw, pluck });

        var pat = new PatternModel { Name = "Patrón 1 - Neon Drop", ColorHex = "#3B9EFF", LengthSteps = 32 };
        SetSteps(pat.GetOrCreateChannelData(kick.Id), 0, 4, 8, 12, 16, 20, 24, 28);
        SetSteps(pat.GetOrCreateChannelData(clap.Id), 4, 12, 20, 28);
        SetSteps(pat.GetOrCreateChannelData(hat.Id), 2, 6, 10, 14, 18, 22, 26, 30);

        AddNotes(pat.GetOrCreateChannelData(bass.Id),
            (45, 0, 1.5, 0.85f), (45, 2, 1.5, 0.85f), (45, 4, 1.5, 0.85f), (45, 6, 1.5, 0.85f),
            (41, 8, 1.5, 0.85f), (41, 10, 1.5, 0.85f), (41, 12, 1.5, 0.85f), (41, 14, 1.5, 0.85f),
            (48, 16, 1.5, 0.85f), (48, 18, 1.5, 0.85f), (48, 20, 1.5, 0.85f), (48, 22, 1.5, 0.85f),
            (43, 24, 1.5, 0.85f), (43, 26, 1.5, 0.85f), (43, 28, 1.5, 0.85f), (43, 30, 1.5, 0.85f));

        AddNotes(pat.GetOrCreateChannelData(saw.Id),
            (57, 0, 7.8, 0.76f), (60, 0, 7.8, 0.74f), (64, 0, 7.8, 0.76f), (69, 0, 7.8, 0.72f),
            (53, 8, 7.8, 0.76f), (57, 8, 7.8, 0.74f), (60, 8, 7.8, 0.76f), (65, 8, 7.8, 0.72f),
            (60, 16, 7.8, 0.76f), (64, 16, 7.8, 0.74f), (67, 16, 7.8, 0.76f), (72, 16, 7.8, 0.72f),
            (55, 24, 7.8, 0.76f), (59, 24, 7.8, 0.74f), (62, 24, 7.8, 0.76f), (67, 24, 7.8, 0.72f));

        AddNotes(pat.GetOrCreateChannelData(pluck.Id),
            (69, 0, 1, 0.82f), (72, 2, 1, 0.82f), (76, 4, 1, 0.82f), (81, 6, 1, 0.82f),
            (69, 8, 1, 0.82f), (72, 10, 1, 0.82f), (77, 12, 1, 0.82f), (81, 14, 1, 0.82f),
            (72, 16, 1, 0.82f), (76, 18, 1, 0.82f), (79, 20, 1, 0.82f), (84, 22, 1, 0.82f),
            (71, 24, 1, 0.82f), (74, 26, 1, 0.82f), (79, 28, 1, 0.82f), (83, 30, 1, 0.82f));

        proj.Patterns.Add(pat);
        proj.ActivePatternId = pat.Id;
        proj.ActiveChannelId = saw.Id;
        proj.SelectedMixerTrack = 5;

        proj.Playlist.Add(new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat.Id, Name = pat.Name, ColorHex = pat.ColorHex, TrackIndex = 0, StartStep = 0, LengthSteps = 32 });
        proj.Playlist.Add(new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat.Id, Name = pat.Name, ColorHex = pat.ColorHex, TrackIndex = 0, StartStep = 32, LengthSteps = 32 });

        proj.EnsureIntegrity();
        return proj;
    }

    public static ProjectModel CreateLoFiDemo()
    {
        var proj = new ProjectModel
        {
            Name = "Lo-Fi Chill - Midnight Rhodes",
            Bpm = 84,
            Swing = 0.28f,
            Mode = PlaybackMode.Pattern,
            ScaleRoot = 0,
            ScaleIndex = 1,
            Mixer = CreateDefaultMixer()
        };

        proj.Mixer[6].Effects.Add(new FxSlotModel { Type = FxType.Chorus, ChorusRateHz = 0.8f, ChorusDepth = 0.45f, Mix = 0.35f });
        proj.Mixer[6].Effects.Add(new FxSlotModel { Type = FxType.Reverb2, Mix = 0.34f, ReverbDecay = 0.76f, ReverbDamp = 0.62f });

        var kick = new ChannelModel { Name = "Dusty Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = "#E05A47", MixerTrack = 1, Volume = 0.90f, Decay = 0.30f, Punch = 0.55f, Tone = 0.30f };
        var snare = new ChannelModel { Name = "Lo-Fi Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Snare, ColorHex = "#E8832A", MixerTrack = 2, Volume = 0.78f, Decay = 0.20f, Tone = 0.42f };
        var hat = new ChannelModel { Name = "Brush Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.ClosedHat, ColorHex = "#D9A826", MixerTrack = 3, Volume = 0.50f, Decay = 0.09f, Tone = 0.40f };
        var bass = new ChannelModel { Name = "Warm Sub", Type = InstrumentType.Bass808, ColorHex = "#29B6A8", MixerTrack = 4, Volume = 0.76f, Drive = 0.14f, Decay = 1.3f, Punch = 0.22f, CutoffHz = 420f };
        var rhodes = new ChannelModel { Name = "FL Keys Rhodes", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Rhodes, ColorHex = "#B564E8", MixerTrack = 6, Volume = 0.82f, Tone = 0.55f, Decay = 1.9f, Release = 0.55f };

        proj.Channels.AddRange(new[] { kick, snare, hat, bass, rhodes });

        var pat = new PatternModel { Name = "Patrón 1 - Midnight Chill", ColorHex = "#B564E8", LengthSteps = 32 };
        SetSteps(pat.GetOrCreateChannelData(kick.Id), 0, 7, 10, 16, 23, 26);
        SetSteps(pat.GetOrCreateChannelData(snare.Id), 4, 12, 20, 28);
        SetSteps(pat.GetOrCreateChannelData(hat.Id), 0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30);

        AddNotes(pat.GetOrCreateChannelData(bass.Id),
            (38, 0, 7.5, 0.85f), (43, 8, 7.5, 0.85f), (36, 16, 7.5, 0.85f), (45, 24, 7.5, 0.85f));

        AddNotes(pat.GetOrCreateChannelData(rhodes.Id),
            (62, 0, 7.5, 0.72f), (65, 0.1, 7.4, 0.68f), (69, 0.2, 7.3, 0.70f), (72, 0.3, 7.2, 0.66f), (76, 0.4, 7.1, 0.64f),
            (59, 8, 7.5, 0.72f), (65, 8.1, 7.4, 0.68f), (69, 8.2, 7.3, 0.70f), (74, 8.3, 7.2, 0.66f),
            (60, 16, 7.5, 0.72f), (64, 16.1, 7.4, 0.68f), (67, 16.2, 7.3, 0.70f), (71, 16.3, 7.2, 0.66f), (74, 16.4, 7.1, 0.64f),
            (57, 24, 7.5, 0.72f), (61, 24.1, 7.4, 0.68f), (67, 24.2, 7.3, 0.70f), (72, 24.3, 7.2, 0.66f));

        proj.Patterns.Add(pat);
        proj.ActivePatternId = pat.Id;
        proj.ActiveChannelId = rhodes.Id;
        proj.SelectedMixerTrack = 6;
        proj.Playlist.Add(new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat.Id, Name = pat.Name, ColorHex = pat.ColorHex, TrackIndex = 0, StartStep = 0, LengthSteps = 32 });
        proj.EnsureIntegrity();
        return proj;
    }

    public static ProjectModel CreateEmptyProject()
    {
        var proj = new ProjectModel
        {
            Name = "Nuevo Proyecto",
            Bpm = 130,
            Swing = 0f,
            Mode = PlaybackMode.Pattern,
            Mixer = CreateDefaultMixer()
        };

        var kick = new ChannelModel { Name = "Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = "#E05A47", MixerTrack = 1 };
        var clap = new ChannelModel { Name = "Clap", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Clap, ColorHex = "#E8832A", MixerTrack = 2 };
        var hat = new ChannelModel { Name = "Hi-Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.ClosedHat, ColorHex = "#D9A826", MixerTrack = 3 };
        var snare = new ChannelModel { Name = "Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Snare, ColorHex = "#46C273", MixerTrack = 2 };
        var bass = new ChannelModel { Name = "808 Bass", Type = InstrumentType.Bass808, ColorHex = "#E05297", MixerTrack = 4 };
        var keys = new ChannelModel { Name = "FL Keys", Type = InstrumentType.KeysPiano, ColorHex = "#B564E8", MixerTrack = 6 };

        proj.Channels.AddRange(new[] { kick, clap, hat, snare, bass, keys });
        var pat = new PatternModel { Name = "Patrón 1", ColorHex = "#E8832A", LengthSteps = 16 };
        proj.Patterns.Add(pat);
        proj.ActivePatternId = pat.Id;
        proj.ActiveChannelId = keys.Id;
        proj.Playlist.Add(new PlaylistClipModel { Type = ClipType.Pattern, PatternId = pat.Id, Name = pat.Name, ColorHex = pat.ColorHex, TrackIndex = 0, StartStep = 0, LengthSteps = 16 });
        proj.EnsureIntegrity();
        return proj;
    }
}
