using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using FLLiteStudio.Audio;
using FLLiteStudio.Controls;
using FLLiteStudio.Core;

namespace FLLiteStudio.UI;

public partial class MainWindow : Window
{
    private ProjectModel _project;
    private readonly AudioEngine _audio;
    private readonly DispatcherTimer _uiTimer;
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();

    private readonly Dictionary<string, List<Border>> _stepPadVisuals = new();
    private readonly Dictionary<int, Border> _mixerMeterFills = new();
    private readonly HashSet<Key> _heldPianoKeys = new();

    private int _suppressDepth;
    private bool IsSuppressed => _suppressDepth > 0;
    private int _lastStepHighlighted = -1;
    private string? _selectedFxId;
    private int _pianoOctave = 4;
    private UpdaterSettings _updaterSettings = new();
    private UpdateReleaseInfo? _pendingUpdate;

    private static readonly Dictionary<Key, int> KeyToSemitone = new()
    {
        [Key.Z] = 0, [Key.S] = 1, [Key.X] = 2, [Key.D] = 3, [Key.C] = 4, [Key.V] = 5,
        [Key.G] = 6, [Key.B] = 7, [Key.H] = 8, [Key.N] = 9, [Key.J] = 10, [Key.M] = 11,
        [Key.Q] = 12, [Key.D2] = 13, [Key.W] = 14, [Key.D3] = 15, [Key.E] = 16, [Key.R] = 17,
        [Key.D5] = 18, [Key.T] = 19, [Key.D6] = 20, [Key.Y] = 21, [Key.D7] = 22, [Key.U] = 23, [Key.I] = 24
    };

    public MainWindow()
    {
        _suppressDepth++;
        _project = DemoProjects.CreateTrapDemo();
        InitializeComponent();
        _suppressDepth--;

        _updaterSettings = GitHubUpdater.LoadSettings();
        LblVersion.Text = $"v{GitHubUpdater.CurrentVersionString}";
        GuideBanner.Visibility = _updaterSettings.ShowGuideBanners ? Visibility.Visible : Visibility.Collapsed;
        MenuShowGuide.IsChecked = _updaterSettings.ShowGuideBanners;

        _audio = new AudioEngine(_project);
        _audio.MidiKeyboardNoteOn += (note, vel) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                var ch = _project.GetActiveChannel();
                if (ch != null) _audio.PreviewNote(ch, note, vel, 0.35);
            });
        };

        InitStaticDropdowns();
        WireCustomControls();

        try
        {
            _audio.StartAudioDriver();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Audio Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        RefreshAllUI();
        UpdateGuideBanner();
        _ = CheckForGitHubUpdatesOnStartupAsync();
    }

    private void InitStaticDropdowns()
    {
        _suppressDepth++;
        try
        {
            foreach (var n in MusicTheory.NoteNames) CmbScaleRoot.Items.Add(n);
            foreach (var s in MusicTheory.Scales) CmbScaleType.Items.Add(s.Name);
            foreach (var c in MusicTheory.Chords) CmbChordStamp.Items.Add(c.Name);
            CmbChordStamp.SelectedIndex = 0;
        }
        finally { _suppressDepth--; }
    }

    private void WireCustomControls()
    {
        PianoRollCtrl.NotePreviewRequested += (midi, vel) =>
        {
            var ch = _project.GetActiveChannel();
            if (ch != null) _audio.PreviewNote(ch, midi, vel, 0.32);
        };
        PianoRollCtrl.NotesModified += () =>
        {
            RebuildChannelRack();
            PlaylistCtrl.InvalidateVisual();
        };
        PianoRollCtrl.PlayheadSeekRequested += step =>
        {
            _project.Mode = PlaybackMode.Pattern;
            _audio.SeekStep(step);
            SyncTransportHeader();
        };

        PlaylistCtrl.PlaylistModified += () => { };
        PlaylistCtrl.PlayheadSeekRequested += step =>
        {
            _project.Mode = PlaybackMode.Song;
            _audio.SeekStep(step);
            SyncTransportHeader();
        };
        PlaylistCtrl.PatternClipDoubleClicked += patId =>
        {
            _project.ActivePatternId = patId;
            TabRack.IsChecked = true;
            RefreshAllUI();
        };
    }

    private void PushUndo()
    {
        _undoStack.Push(ProjectIO.SerializeSnapshot(_project));
        _redoStack.Clear();
    }

    private void LoadProjectModel(ProjectModel next)
    {
        var existingSamples = new Dictionary<string, LoadedSample>(_project.Samples);
        foreach (var kv in existingSamples)
            next.Samples.TryAdd(kv.Key, kv.Value);

        _project = next;
        _project.EnsureIntegrity();
        _audio.SetProject(_project);
        _selectedFxId = null;
        RefreshAllUI();
        SetStatus($"Proyecto cargado: {_project.Name}");
    }

    private void SetStatus(string msg) => LblStatus.Text = msg;

    // ========================================================================
    // FULL UI REFRESH
    // ========================================================================
    private void RefreshAllUI()
    {
        _suppressDepth++;
        try
        {
            Title = $"FL-Lite Studio — {_project.Name}";
            SyncTransportHeader();

            CmbPattern.Items.Clear();
            foreach (var p in _project.Patterns)
                CmbPattern.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
            int pIdx = _project.Patterns.FindIndex(p => p.Id == _project.ActivePatternId);
            CmbPattern.SelectedIndex = Math.Max(0, pIdx);

            var activePat = _project.GetActivePattern();
            if (activePat != null)
            {
                CmbPatLength.SelectedIndex = activePat.LengthSteps switch { 16 => 0, 64 => 2, _ => 1 };
            }

            CmbPianoChannel.Items.Clear();
            foreach (var ch in _project.Channels)
                CmbPianoChannel.Items.Add(new ComboBoxItem { Content = ch.Name, Tag = ch.Id });
            int cIdx = _project.Channels.FindIndex(c => c.Id == _project.ActiveChannelId);
            CmbPianoChannel.SelectedIndex = Math.Max(0, cIdx);

            CmbScaleRoot.SelectedIndex = Math.Clamp(_project.ScaleRoot, 0, 11);
            CmbScaleType.SelectedIndex = Math.Clamp(_project.ScaleIndex, 0, MusicTheory.Scales.Length - 1);
        }
        finally { _suppressDepth--; }

        PianoRollCtrl.BindProject(_project);
        PlaylistCtrl.BindProject(_project);
        RebuildChannelRack();
        RebuildInstrumentInspector();
        RebuildMixerView();
    }

    private void SyncTransportHeader()
    {
        _suppressDepth++;
        try
        {
            BtnPlay.Content = _audio.IsPlaying ? "Pausar" : "Reproducir";
            TxtBpm.Text = _project.Bpm.ToString();
            SldSwing.Value = _project.Swing;
            ChkMetro.IsChecked = _project.Metronome;

            BtnModePat.Background = BrushFromHex(_project.Mode == PlaybackMode.Pattern ? "#C86D1F" : "#242A33");
            BtnModeSong.Background = BrushFromHex(_project.Mode == PlaybackMode.Song ? "#2B8A4E" : "#242A33");
        }
        finally { _suppressDepth--; }
    }

    // ========================================================================
    // TAB 1: CHANNEL RACK + INSTRUMENT INSPECTOR
    // ========================================================================
    private void RebuildChannelRack()
    {
        RackRowsPanel.Children.Clear();
        _stepPadVisuals.Clear();

        var pat = _project.GetActivePattern();
        if (pat == null) return;
        int stepsCount = Math.Max(16, pat.LengthSteps);

        foreach (var ch in _project.Channels)
        {
            var cd = pat.GetOrCreateChannelData(ch.Id);
            bool isSelected = ch.Id == _project.ActiveChannelId;
            bool hasPianoNotes = cd.Notes.Count > 0;

            var rowBorder = new Border
            {
                Background = BrushFromHex(isSelected ? "#262D38" : "#1E232B"),
                BorderBrush = BrushFromHex(isSelected ? "#E8832A" : "#2F3744"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });   // Mute LED
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });  // Instrument button
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });   // Volume slider
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });   // Mixer route
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });   // Piano / Del
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Steps

            // 1. Mute/Solo LED
            var led = new Border
            {
                Width = 14, Height = 22, CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand,
                Background = BrushFromHex(ch.Solo ? "#E8A22A" : (!ch.Muted ? "#46C273" : "#2A303A")),
                ToolTip = "Clic izq: Silenciar (Mute) | Clic der: Solo"
            };
            led.MouseLeftButtonDown += (_, _) => { ch.Muted = !ch.Muted; if (!ch.Muted) ch.Solo = false; RebuildChannelRack(); };
            led.MouseRightButtonDown += (_, _) => { ch.Solo = !ch.Solo; if (ch.Solo) ch.Muted = false; RebuildChannelRack(); };
            Grid.SetColumn(led, 0);
            rowGrid.Children.Add(led);

            // 2. Instrument Name Button
            var nameCard = new Border
            {
                Background = BrushFromHex("#2C3340"),
                BorderBrush = BrushFromHex(ch.ColorHex),
                BorderThickness = new Thickness(4, 1, 1, 1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Clic para seleccionar y editar este instrumento"
            };
            var nameStack = new StackPanel();
            nameStack.Children.Add(new TextBlock { Text = ch.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            nameStack.Children.Add(new TextBlock { Text = MusicTheory.GetInstrumentDisplayName(ch.Type), Foreground = BrushFromHex("#93A0B3"), FontSize = 10 });
            nameCard.Child = nameStack;
            nameCard.MouseLeftButtonDown += (_, _) =>
            {
                _project.ActiveChannelId = ch.Id;
                _audio.PreviewNote(ch, ch.Type == InstrumentType.Bass808 ? 36 : 60, 0.85f, 0.28);
                RebuildChannelRack();
                RebuildInstrumentInspector();
                PianoRollCtrl.InvalidateVisual();
            };
            Grid.SetColumn(nameCard, 1);
            rowGrid.Children.Add(nameCard);

            // 3. Volume slider
            var volSlider = new Slider
            {
                Minimum = 0, Maximum = 1.35, Value = ch.Volume, Width = 66,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volumen del canal"
            };
            volSlider.ValueChanged += (_, e) => ch.Volume = (float)e.NewValue;
            Grid.SetColumn(volSlider, 2);
            rowGrid.Children.Add(volSlider);

            // 4. Mixer Track indicator (click cycles or wheel)
            var mixBtn = new Button
            {
                Content = ch.MixerTrack == 0 ? "M" : $"FX {ch.MixerTrack}",
                Padding = new Thickness(4, 3, 4, 3), FontSize = 10, Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Pista del Mixer (Clic para cambiar pista)"
            };
            mixBtn.Click += (_, _) =>
            {
                ch.MixerTrack = (ch.MixerTrack + 1) % 13;
                mixBtn.Content = ch.MixerTrack == 0 ? "M" : $"FX {ch.MixerTrack}";
            };
            Grid.SetColumn(mixBtn, 3);
            rowGrid.Children.Add(mixBtn);

            // 5. Piano Roll & Delete buttons
            var actPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var prBtn = new Button
            {
                Content = "Notas", Padding = new Thickness(6, 3, 6, 3), FontSize = 10,
                BorderBrush = BrushFromHex(hasPianoNotes ? "#46C273" : "#434D5E"),
                ToolTip = "Editar melodía en Melodías (Piano Roll)"
            };
            prBtn.Click += (_, _) =>
            {
                _project.ActiveChannelId = ch.Id;
                TabPiano.IsChecked = true;
                RefreshAllUI();
            };
            var delBtn = new Button
            {
                Content = "X", Padding = new Thickness(5, 3, 5, 3), FontSize = 10, Margin = new Thickness(3, 0, 6, 0),
                ToolTip = "Eliminar canal"
            };
            delBtn.Click += (_, _) =>
            {
                if (_project.Channels.Count <= 1) return;
                PushUndo();
                _project.Channels.Remove(ch);
                if (_project.ActiveChannelId == ch.Id) _project.ActiveChannelId = _project.Channels[0].Id;
                RefreshAllUI();
            };
            actPanel.Children.Add(prBtn);
            actPanel.Children.Add(delBtn);
            Grid.SetColumn(actPanel, 4);
            rowGrid.Children.Add(actPanel);

            // 6. Step Pads OR Piano Roll Mini Preview
            if (hasPianoNotes)
            {
                var preview = new Border
                {
                    Height = 30, Background = BrushFromHex("#14181F"),
                    BorderBrush = BrushFromHex("#343E4D"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                    ToolTip = $"{cd.Notes.Count} notas MIDI — Clic para abrir Melodías"
                };
                preview.Child = new TextBlock
                {
                    Text = $"Melodía MIDI ({cd.Notes.Count} notas) — Clic para editar en Piano Roll",
                    Foreground = BrushFromHex(ch.ColorHex), FontSize = 11, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
                };
                preview.MouseLeftButtonDown += (_, _) =>
                {
                    _project.ActiveChannelId = ch.Id;
                    TabPiano.IsChecked = true;
                    RefreshAllUI();
                };
                Grid.SetColumn(preview, 5);
                rowGrid.Children.Add(preview);
            }
            else
            {
                var stepsPanel = new UniformGridSteps(stepsCount);
                var padList = new List<Border>(stepsCount);
                for (int s = 0; s < stepsCount; s++)
                {
                    int stepIdx = s;
                    bool isGroupB = ((s / 4) & 1) == 1;
                    bool on = cd.Steps[s];
                    var pad = new Border
                    {
                        Height = 28,
                        Margin = new Thickness(1.5, 0, (s % 4 == 3) ? 5 : 1.5, 0),
                        CornerRadius = new CornerRadius(3),
                        BorderThickness = new Thickness(1),
                        BorderBrush = BrushFromHex(on ? "#FFC285" : "#15181E"),
                        Background = BrushFromHex(on ? "#E8832A" : (isGroupB ? "#5E3E44" : "#3E4652")),
                        Cursor = Cursors.Hand
                    };

                    pad.MouseLeftButtonDown += (_, _) =>
                    {
                        cd.Steps[stepIdx] = !cd.Steps[stepIdx];
                        bool active = cd.Steps[stepIdx];
                        pad.Background = BrushFromHex(active ? "#E8832A" : (isGroupB ? "#5E3E44" : "#3E4652"));
                        pad.BorderBrush = BrushFromHex(active ? "#FFC285" : "#15181E");
                        if (active && !_audio.IsPlaying)
                            _audio.PreviewNote(ch, 60, 0.85f, 0.2);
                    };
                    pad.MouseRightButtonDown += (_, _) =>
                    {
                        cd.Steps[stepIdx] = false;
                        pad.Background = BrushFromHex(isGroupB ? "#5E3E44" : "#3E4652");
                        pad.BorderBrush = BrushFromHex("#15181E");
                    };

                    stepsPanel.Children.Add(pad);
                    padList.Add(pad);
                }
                _stepPadVisuals[ch.Id] = padList;
                Grid.SetColumn(stepsPanel, 5);
                rowGrid.Children.Add(stepsPanel);
            }

            rowBorder.Child = rowGrid;
            RackRowsPanel.Children.Add(rowBorder);
        }
    }

    private void RebuildInstrumentInspector()
    {
        InstrumentInspectorPanel.Children.Clear();
        var ch = _project.GetActiveChannel();
        if (ch == null) return;

        // Title header
        var titleBlock = new TextBlock
        {
            Text = ch.Name, FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 2)
        };
        var subBlock = new TextBlock
        {
            Text = MusicTheory.GetInstrumentDisplayName(ch.Type), FontSize = 11,
            Foreground = BrushFromHex(ch.ColorHex), Margin = new Thickness(0, 0, 0, 10)
        };
        InstrumentInspectorPanel.Children.Add(titleBlock);
        InstrumentInspectorPanel.Children.Add(subBlock);

        // Quick action buttons
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var testBtn = new Button { Content = "Probar Sonido", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 6, 0) };
        testBtn.Click += (_, _) => _audio.PreviewNote(ch, ch.Type == InstrumentType.Bass808 ? 36 : 60, 0.9f, 0.45);
        var openPianoBtn = new Button { Content = "Editar Melodía" };
        openPianoBtn.Click += (_, _) => { TabPiano.IsChecked = true; RefreshAllUI(); };
        actions.Children.Add(testBtn);
        actions.Children.Add(openPianoBtn);
        InstrumentInspectorPanel.Children.Add(actions);

        // One-click friendly sound presets
        AddQuickSoundPresets(InstrumentInspectorPanel, ch);

        // Common controls
        AddInspectorSlider(InstrumentInspectorPanel, "Volumen", 0, 1.35, ch.Volume, v => ch.Volume = (float)v, v => $"{(int)(v * 100)}%");
        AddInspectorSlider(InstrumentInspectorPanel, "Panorama (L / R)", -1, 1, ch.Pan, v => ch.Pan = (float)v, v => Math.Abs(v) < 0.03 ? "Centro" : v < 0 ? $"{(int)(-v * 100)}% Izq" : $"{(int)(v * 100)}% Der");
        AddInspectorSlider(InstrumentInspectorPanel, "Afinación (Semitonos)", -12, 12, ch.PitchSemitones, v => ch.PitchSemitones = MathF.Round((float)v), v => $"{(int)Math.Round(v)} st");

        InstrumentInspectorPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 10), Background = BrushFromHex("#2F3744") });

        // Instrument-specific controls
        switch (ch.Type)
        {
            case InstrumentType.DrumSynth:
                AddInspectorCombo(InstrumentInspectorPanel, "Tipo de Percusión",
                    Enum.GetNames<DrumKind>(), (int)ch.DrumKind, idx => ch.DrumKind = (DrumKind)idx);
                AddInspectorSlider(InstrumentInspectorPanel, "Duración (Decay)", 0.04, 1.4, ch.Decay, v => ch.Decay = (float)v, v => $"{(int)(v * 1000)} ms");
                AddInspectorSlider(InstrumentInspectorPanel, "Pegada (Punch)", 0, 1, ch.Punch, v => ch.Punch = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Tono / Brillo", 0, 1, ch.Tone, v => ch.Tone = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Saturación (Drive)", 0, 1, ch.Drive, v => ch.Drive = (float)v, v => $"{(int)(v * 100)}%");
                break;

            case InstrumentType.Bass808:
                AddInspectorSlider(InstrumentInspectorPanel, "Distorsión / Drive", 0, 1, ch.Drive, v => ch.Drive = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Pegada Inicial (Punch)", 0, 1, ch.Punch, v => ch.Punch = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Filtro Pasa-Bajos", 120, 4500, ch.CutoffHz, v => ch.CutoffHz = (float)v, v => $"{(int)v} Hz");
                AddInspectorSlider(InstrumentInspectorPanel, "Duración (Decay)", 0.2, 2.5, ch.Decay, v => ch.Decay = (float)v, v => $"{v:0.00} s");
                AddInspectorSlider(InstrumentInspectorPanel, "Cola (Release)", 0.02, 0.8, ch.Release, v => ch.Release = (float)v, v => $"{v:0.00} s");
                break;

            case InstrumentType.Osc3x:
                AddInspectorCombo(InstrumentInspectorPanel, "Onda Principal", Enum.GetNames<WaveKind>(), (int)ch.Wave1, idx => ch.Wave1 = (WaveKind)idx);
                AddInspectorCombo(InstrumentInspectorPanel, "Onda Secundaria", Enum.GetNames<WaveKind>(), (int)ch.Wave2, idx => ch.Wave2 = (WaveKind)idx);
                AddInspectorSlider(InstrumentInspectorPanel, "Mezcla Osc 2", 0, 1, ch.Osc2Mix, v => ch.Osc2Mix = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Filtro Cutoff", 120, 16000, ch.CutoffHz, v => ch.CutoffHz = (float)v, v => $"{(int)v} Hz");
                AddInspectorSlider(InstrumentInspectorPanel, "Resonancia", 0.5, 10, ch.Resonance, v => ch.Resonance = (float)v, v => $"{v:0.0}");
                AddInspectorSlider(InstrumentInspectorPanel, "Ataque", 0.002, 0.8, ch.Attack, v => ch.Attack = (float)v, v => $"{(int)(v * 1000)} ms");
                AddInspectorSlider(InstrumentInspectorPanel, "Release", 0.02, 1.8, ch.Release, v => ch.Release = (float)v, v => $"{v:0.00} s");
                break;

            case InstrumentType.SytrusFM:
                AddInspectorSlider(InstrumentInspectorPanel, "Ratio FM", 0.5, 8.0, ch.FmRatio, v => ch.FmRatio = (float)v, v => $"{v:0.00}x");
                AddInspectorSlider(InstrumentInspectorPanel, "Intensidad FM", 0, 8.0, ch.FmIndex, v => ch.FmIndex = (float)v, v => $"{v:0.0}");
                AddInspectorSlider(InstrumentInspectorPanel, "Decay FM", 0.02, 1.8, ch.FmDecay, v => ch.FmDecay = (float)v, v => $"{v:0.00} s");
                AddInspectorSlider(InstrumentInspectorPanel, "Filtro Cutoff", 200, 16000, ch.CutoffHz, v => ch.CutoffHz = (float)v, v => $"{(int)v} Hz");
                AddInspectorSlider(InstrumentInspectorPanel, "Decay Nota", 0.05, 2.0, ch.Decay, v => ch.Decay = (float)v, v => $"{v:0.00} s");
                AddInspectorSlider(InstrumentInspectorPanel, "Release", 0.02, 2.0, ch.Release, v => ch.Release = (float)v, v => $"{v:0.00} s");
                break;

            case InstrumentType.SuperSaw:
                AddInspectorSlider(InstrumentInspectorPanel, "Desafinación Unísono", 0, 50, ch.Detune, v => ch.Detune = (float)v, v => $"{(int)v} cents");
                AddInspectorSlider(InstrumentInspectorPanel, "Filtro Cutoff", 150, 16000, ch.CutoffHz, v => ch.CutoffHz = (float)v, v => $"{(int)v} Hz");
                AddInspectorSlider(InstrumentInspectorPanel, "Resonancia", 0.5, 10, ch.Resonance, v => ch.Resonance = (float)v, v => $"{v:0.0}");
                AddInspectorSlider(InstrumentInspectorPanel, "Ataque", 0.005, 1.0, ch.Attack, v => ch.Attack = (float)v, v => $"{(int)(v * 1000)} ms");
                AddInspectorSlider(InstrumentInspectorPanel, "Release", 0.02, 2.0, ch.Release, v => ch.Release = (float)v, v => $"{v:0.00} s");
                break;

            case InstrumentType.KeysPiano:
                AddInspectorCombo(InstrumentInspectorPanel, "Tipo de Teclado",
                    new[] { "Rhodes Eléctrico", "Grand Piano", "Campana Suave", "Órgano Club" }, (int)ch.KeysMode, idx => ch.KeysMode = (KeysMode)idx);
                AddInspectorSlider(InstrumentInspectorPanel, "Brillo", 0.1, 1.0, ch.Tone, v => ch.Tone = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Duración (Decay)", 0.2, 3.0, ch.Decay, v => ch.Decay = (float)v, v => $"{v:0.00} s");
                AddInspectorSlider(InstrumentInspectorPanel, "Release", 0.04, 1.8, ch.Release, v => ch.Release = (float)v, v => $"{v:0.00} s");
                break;

            case InstrumentType.WavSampler:
                InstrumentInspectorPanel.Children.Add(new TextBlock
                {
                    Text = $"Archivo actual: {ch.SampleName}", Foreground = BrushFromHex("#D6DFEB"),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
                });
                var loadBtn = new Button { Content = "Cargar archivo WAV desde PC...", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 0, 8) };
                loadBtn.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Archivos de Audio WAV|*.wav", Title = "Cargar Sample WAV" };
                    if (dlg.ShowDialog() == true)
                    {
                        try
                        {
                            var smp = ProjectIO.LoadWavFile(dlg.FileName);
                            _project.Samples[smp.Id] = smp;
                            ch.SampleId = smp.Id;
                            ch.SampleName = smp.Name;
                            ch.Name = System.IO.Path.GetFileNameWithoutExtension(smp.Name);
                            RebuildChannelRack();
                            RebuildInstrumentInspector();
                            _audio.PreviewNote(ch, 60, 0.9f, Math.Min(1.5, smp.DurationSeconds));
                        }
                        catch (Exception ex) { MessageBox.Show(ex.Message, "Error WAV", MessageBoxButton.OK, MessageBoxImage.Error); }
                    }
                };
                InstrumentInspectorPanel.Children.Add(loadBtn);

                var revChk = new CheckBox { Content = "Reproducir en Reversa", IsChecked = ch.Reverse, Margin = new Thickness(0, 0, 0, 8) };
                revChk.Click += (_, _) => ch.Reverse = revChk.IsChecked == true;
                InstrumentInspectorPanel.Children.Add(revChk);

                AddInspectorSlider(InstrumentInspectorPanel, "Recorte Inicio", 0, 0.85, ch.TrimStart, v => ch.TrimStart = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Recorte Fin", 0.15, 1.0, ch.TrimEnd, v => ch.TrimEnd = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(InstrumentInspectorPanel, "Filtro Pasa-Bajos", 200, 18500, ch.CutoffHz, v => ch.CutoffHz = (float)v, v => $"{(int)v} Hz");
                break;
        }
    }

    // ========================================================================
    // TAB 4: MIXER CONSOLE & FX INSPECTOR
    // ========================================================================
    private void RebuildMixerView()
    {
        MixerStripsPanel.Children.Clear();
        _mixerMeterFills.Clear();

        foreach (var tr in _project.Mixer)
        {
            bool selected = tr.Id == _project.SelectedMixerTrack;
            int activeFx = tr.Effects.Count(f => f.Enabled);

            var strip = new Border
            {
                Width = tr.Id == 0 ? 84 : 74,
                Background = BrushFromHex(selected ? "#2A323F" : "#1F252E"),
                BorderBrush = BrushFromHex(selected ? "#E8832A" : "#333C4A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(5, 6, 5, 6),
                Cursor = Cursors.Hand
            };
            strip.MouseLeftButtonDown += (_, _) =>
            {
                if (_project.SelectedMixerTrack != tr.Id)
                {
                    _project.SelectedMixerTrack = tr.Id;
                    _selectedFxId = tr.Effects.FirstOrDefault()?.Id;
                    RebuildMixerView();
                }
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // FX count
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pan
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Fader + Meter
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Mute/Solo

            var topBar = new Border
            {
                BorderBrush = BrushFromHex(tr.ColorHex), BorderThickness = new Thickness(0, 3, 0, 0),
                Padding = new Thickness(0, 4, 0, 4)
            };
            var topStack = new StackPanel();
            topStack.Children.Add(new TextBlock { Text = tr.Id == 0 ? "MASTER" : $"#{tr.Id}", Foreground = BrushFromHex("#E8832A"), FontWeight = FontWeights.Bold, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            topStack.Children.Add(new TextBlock { Text = tr.Name, Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            topBar.Child = topStack;
            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            var fxTag = new Border
            {
                Background = BrushFromHex(activeFx > 0 ? "#1F3C4F" : "#151920"),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 2, 0, 4)
            };
            fxTag.Child = new TextBlock { Text = activeFx > 0 ? $"FX: {activeFx}" : "Sin FX", Foreground = BrushFromHex(activeFx > 0 ? "#4FC3F7" : "#6E7B8C"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold };
            Grid.SetRow(fxTag, 1);
            root.Children.Add(fxTag);

            var panSlider = new Slider { Minimum = -1, Maximum = 1, Value = tr.Pan, Margin = new Thickness(2, 0, 2, 6), ToolTip = "Panorama (Izq / Der)" };
            panSlider.ValueChanged += (_, e) => tr.Pan = (float)e.NewValue;
            Grid.SetRow(panSlider, 2);
            root.Children.Add(panSlider);

            // Meter + Vertical Fader
            var mid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var meterBg = new Border { Width = 10, Background = BrushFromHex("#0F1318"), BorderBrush = BrushFromHex("#2E3745"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Stretch };
            var meterFill = new Border { Background = BrushFromHex("#46C273"), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            meterBg.Child = meterFill;
            _mixerMeterFills[tr.Id] = meterFill;
            Grid.SetColumn(meterBg, 0);
            mid.Children.Add(meterBg);

            var fader = new Slider
            {
                Orientation = Orientation.Vertical, Minimum = 0, Maximum = 1.3, Value = tr.Volume,
                HorizontalAlignment = HorizontalAlignment.Center, ToolTip = "Volumen de pista"
            };
            fader.ValueChanged += (_, e) => tr.Volume = (float)e.NewValue;
            Grid.SetColumn(fader, 1);
            mid.Children.Add(fader);

            Grid.SetRow(mid, 3);
            root.Children.Add(mid);

            var ms = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var mBtn = new Button
            {
                Content = "M", Width = 26, Padding = new Thickness(0, 2, 0, 2), FontSize = 10,
                Background = BrushFromHex(!tr.Muted ? "#215C38" : "#242A33"), ToolTip = "Mute"
            };
            mBtn.Click += (_, e) => { e.Handled = true; tr.Muted = !tr.Muted; RebuildMixerView(); };

            var sBtn = new Button
            {
                Content = "S", Width = 26, Padding = new Thickness(0, 2, 0, 2), FontSize = 10, Margin = new Thickness(4, 0, 0, 0),
                Background = BrushFromHex(tr.Solo ? "#8C5A14" : "#242A33"), IsEnabled = tr.Id != 0, ToolTip = "Solo"
            };
            sBtn.Click += (_, e) => { e.Handled = true; tr.Solo = !tr.Solo; if (tr.Solo) tr.Muted = false; RebuildMixerView(); };

            ms.Children.Add(mBtn);
            ms.Children.Add(sBtn);
            Grid.SetRow(ms, 4);
            root.Children.Add(ms);

            strip.Child = root;
            MixerStripsPanel.Children.Add(strip);
        }

        RebuildMixerFxInspector();
    }

    private void RebuildMixerFxInspector()
    {
        MixerFxInspectorPanel.Children.Clear();
        var tr = _project.Mixer.FirstOrDefault(t => t.Id == _project.SelectedMixerTrack) ?? _project.Mixer[0];

        MixerFxInspectorPanel.Children.Add(new TextBlock
        {
            Text = $"Rack de Efectos: {(tr.Id == 0 ? "Master" : $"Pista {tr.Id} ({tr.Name})")}",
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8)
        });

        // Add FX Row
        var addRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fxCombo = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        foreach (FxType ft in Enum.GetValues<FxType>())
            fxCombo.Items.Add(new ComboBoxItem { Content = MusicTheory.GetFxDisplayName(ft), Tag = ft });
        fxCombo.SelectedIndex = 0;

        var addBtn = new Button { Content = "Añadir FX", Style = (Style)FindResource("AccentButton") };
        addBtn.Click += (_, _) =>
        {
            if (tr.Effects.Count >= 6)
            {
                MessageBox.Show("Máximo 6 efectos por pista para mantener un bajo consumo de CPU.", "Límite FX", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (fxCombo.SelectedItem is ComboBoxItem item && item.Tag is FxType ft)
            {
                var slot = new FxSlotModel { Type = ft, Enabled = true };
                tr.Effects.Add(slot);
                _selectedFxId = slot.Id;
                RebuildMixerView();
            }
        };

        Grid.SetColumn(fxCombo, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(fxCombo);
        addRow.Children.Add(addBtn);
        MixerFxInspectorPanel.Children.Add(addRow);

        // Slots list
        if (tr.Effects.Count == 0)
        {
            var guideCard = new Border
            {
                Background = BrushFromHex("#161B22"), BorderBrush = BrushFromHex("#2D3644"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 8)
            };
            guideCard.Child = new TextBlock
            {
                Text = "Esta pista no tiene efectos todavía. Elige uno arriba y pulsa Añadir FX:\n\n" +
                       "• Ecualizador (EQ 2): sube o baja graves y agudos.\n" +
                       "• Reverb de Sala: añade espacio y ambiente.\n" +
                       "• Eco (Delay 3): repite el sonido al ritmo del BPM.\n" +
                       "• Soundgoodizer: da fuerza y claridad al instante.\n" +
                       "• Gross Beat: crea bombeo rítmico o cortes tipo Trap/EDM.",
                Foreground = BrushFromHex("#97A5B8"), TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17
            };
            MixerFxInspectorPanel.Children.Add(guideCard);
            return;
        }

        if (_selectedFxId == null || tr.Effects.All(f => f.Id != _selectedFxId))
            _selectedFxId = tr.Effects[0].Id;

        for (int i = 0; i < tr.Effects.Count; i++)
        {
            var fx = tr.Effects[i];
            bool isSel = fx.Id == _selectedFxId;

            var slotCard = new Border
            {
                Background = BrushFromHex(isSel ? "#2C3544" : "#222832"),
                BorderBrush = BrushFromHex(isSel ? "#E8832A" : "#343E4D"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox { IsChecked = fx.Enabled, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Activar / Desactivar efecto" };
            chk.Click += (_, e) => { e.Handled = true; fx.Enabled = chk.IsChecked == true; RebuildMixerView(); };
            Grid.SetColumn(chk, 0);
            g.Children.Add(chk);

            var lbl = new TextBlock { Text = $"{i + 1}. {MusicTheory.GetFxDisplayName(fx.Type)}", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            lbl.MouseLeftButtonDown += (_, _) => { _selectedFxId = fx.Id; RebuildMixerFxInspector(); };
            Grid.SetColumn(lbl, 1);
            g.Children.Add(lbl);

            var del = new Button { Content = "Quitar", Padding = new Thickness(6, 2, 6, 2), FontSize = 10 };
            del.Click += (_, e) => { e.Handled = true; tr.Effects.Remove(fx); _selectedFxId = null; RebuildMixerView(); };
            Grid.SetColumn(del, 2);
            g.Children.Add(del);

            slotCard.Child = g;
            slotCard.MouseLeftButtonDown += (_, _) => { _selectedFxId = fx.Id; RebuildMixerFxInspector(); };
            MixerFxInspectorPanel.Children.Add(slotCard);
        }

        // Active FX Editor Card
        var activeFx = tr.Effects.FirstOrDefault(f => f.Id == _selectedFxId);
        if (activeFx == null) return;

        var edCard = new Border
        {
            Background = BrushFromHex("#161A20"), BorderBrush = BrushFromHex("#36404F"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0)
        };
        var edStack = new StackPanel();
        edStack.Children.Add(new TextBlock
        {
            Text = MusicTheory.GetFxDisplayName(activeFx.Type),
            Foreground = BrushFromHex("#F0A150"), FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 8)
        });

        FxGraphControl? graph = null;
        if (activeFx.Type is FxType.ParametricEq2 or FxType.GrossBeat)
        {
            graph = new FxGraphControl { Height = 92, Margin = new Thickness(0, 0, 0, 8) };
            graph.BindFx(activeFx);
            edStack.Children.Add(graph);
        }

        switch (activeFx.Type)
        {
            case FxType.ParametricEq2:
                AddInspectorSlider(edStack, "Graves (80 Hz)", -15, 15, activeFx.EqLowDb, v => { activeFx.EqLowDb = (float)v; graph?.InvalidateVisual(); }, v => $"{v:+0.0;-0.0;0.0} dB");
                AddInspectorSlider(edStack, "Medios-Graves (320 Hz)", -15, 15, activeFx.EqLowMidDb, v => { activeFx.EqLowMidDb = (float)v; graph?.InvalidateVisual(); }, v => $"{v:+0.0;-0.0;0.0} dB");
                AddInspectorSlider(edStack, "Medios (1.2 kHz)", -15, 15, activeFx.EqMidDb, v => { activeFx.EqMidDb = (float)v; graph?.InvalidateVisual(); }, v => $"{v:+0.0;-0.0;0.0} dB");
                AddInspectorSlider(edStack, "Presencia (4.2 kHz)", -15, 15, activeFx.EqHighMidDb, v => { activeFx.EqHighMidDb = (float)v; graph?.InvalidateVisual(); }, v => $"{v:+0.0;-0.0;0.0} dB");
                AddInspectorSlider(edStack, "Brillo / Aire (10.5 kHz)", -15, 15, activeFx.EqHighDb, v => { activeFx.EqHighDb = (float)v; graph?.InvalidateVisual(); }, v => $"{v:+0.0;-0.0;0.0} dB");
                break;

            case FxType.Reverb2:
                AddInspectorSlider(edStack, "Mezcla (Wet)", 0, 1, activeFx.Mix, v => activeFx.Mix = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Tamaño / Cola (Decay)", 0.2, 0.94, activeFx.ReverbDecay, v => activeFx.ReverbDecay = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Amortiguación (Damp)", 0.05, 0.9, activeFx.ReverbDamp, v => activeFx.ReverbDamp = (float)v, v => $"{(int)(v * 100)}%");
                break;

            case FxType.Delay3:
                AddInspectorCombo(edStack, "Tiempo sincronizado al BPM", MusicTheory.DelayDivisionNames, activeFx.DelayDivisionIndex, idx => activeFx.DelayDivisionIndex = idx);
                AddInspectorSlider(edStack, "Mezcla (Wet)", 0, 1, activeFx.Mix, v => activeFx.Mix = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Repeticiones (Feedback)", 0, 0.84, activeFx.DelayFeedback, v => activeFx.DelayFeedback = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Filtro de Eco (Tone)", 600, 14000, activeFx.DelayToneHz, v => activeFx.DelayToneHz = (float)v, v => $"{(int)v} Hz");
                break;

            case FxType.Soundgoodizer:
                AddInspectorCombo(edStack, "Perfil de Realce", new[] { "Modo A (Equilibrado)", "Modo B (Graves Potentes)", "Modo C (Presencia Vocal/Lead)", "Modo D (Club / Máximo Brillo)" }, activeFx.SoundgoodizerMode, idx => activeFx.SoundgoodizerMode = idx);
                AddInspectorSlider(edStack, "Cantidad Soundgoodizer", 0, 1, activeFx.SoundgoodizerAmount, v => activeFx.SoundgoodizerAmount = (float)v, v => $"{(int)(v * 100)}%");
                break;

            case FxType.Distortion:
                AddInspectorCombo(edStack, "Estilo", new[] { "Cálido (Soft Clip)", "Válvula (Tube)", "Agresivo (Hard Clip)", "Lo-Fi Bitcrusher" }, activeFx.DistMode, idx => activeFx.DistMode = idx);
                AddInspectorSlider(edStack, "Ganancia (Drive)", 0, 1, activeFx.DistDrive, v => activeFx.DistDrive = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Mezcla (Mix)", 0, 1, activeFx.Mix, v => activeFx.Mix = (float)v, v => $"{(int)(v * 100)}%");
                break;

            case FxType.Chorus:
                AddInspectorSlider(edStack, "Velocidad (Rate)", 0.1, 5.0, activeFx.ChorusRateHz, v => activeFx.ChorusRateHz = (float)v, v => $"{v:0.0} Hz");
                AddInspectorSlider(edStack, "Profundidad (Depth)", 0, 1, activeFx.ChorusDepth, v => activeFx.ChorusDepth = (float)v, v => $"{(int)(v * 100)}%");
                AddInspectorSlider(edStack, "Mezcla (Mix)", 0, 1, activeFx.Mix, v => activeFx.Mix = (float)v, v => $"{(int)(v * 100)}%");
                break;

            case FxType.GrossBeat:
                AddInspectorCombo(edStack, "Patrón Rítmico", MusicTheory.GrossBeatPresets.Select(p => p.Name).ToArray(), activeFx.GrossBeatPreset, idx => { activeFx.GrossBeatPreset = idx; graph?.InvalidateVisual(); });
                AddInspectorSlider(edStack, "Intensidad (Mix)", 0, 1, activeFx.Mix, v => { activeFx.Mix = (float)v; graph?.InvalidateVisual(); }, v => $"{(int)(v * 100)}%");
                break;

            case FxType.Limiter:
                AddInspectorSlider(edStack, "Techo (Threshold)", -24, 0, activeFx.LimiterThresholdDb, v => activeFx.LimiterThresholdDb = (float)v, v => $"{v:0.0} dB");
                AddInspectorSlider(edStack, "Ganancia Final", 0.5, 2.0, activeFx.LimiterGain, v => activeFx.LimiterGain = (float)v, v => $"{(int)(v * 100)}%");
                break;
        }

        edCard.Child = edStack;
        MixerFxInspectorPanel.Children.Add(edCard);
    }

    // Reusable clean labeled slider & combo builders
    private static void AddInspectorSlider(Panel parent, string label, double min, double max, double value, Action<double> onChange, Func<double, string> format)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var lbl = new TextBlock { Text = label, Foreground = BrushFromHex("#C5D0DE"), FontSize = 11 };
        var valTxt = new TextBlock { Text = format(value), Foreground = BrushFromHex("#F0A150"), FontFamily = new FontFamily("Consolas"), FontSize = 11 };
        DockPanel.SetDock(valTxt, Dock.Right);
        row.Children.Add(valTxt);
        row.Children.Add(lbl);
        parent.Children.Add(row);

        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(0, 0, 0, 8) };
        slider.ValueChanged += (_, e) =>
        {
            onChange(e.NewValue);
            valTxt.Text = format(e.NewValue);
        };
        parent.Children.Add(slider);
    }

    private static void AddInspectorCombo(Panel parent, string label, string[] items, int selectedIndex, Action<int> onChange)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = BrushFromHex("#C5D0DE"), FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
        var cmb = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var it in items) cmb.Items.Add(it);
        cmb.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Length - 1));
        cmb.SelectionChanged += (_, _) => { if (cmb.SelectedIndex >= 0) onChange(cmb.SelectedIndex); };
        parent.Children.Add(cmb);
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ========================================================================
    // UI TIMER: PLAYHEAD, PEAK METERS & CPU MONITOR
    // ========================================================================
    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        double step = _audio.PlayheadStep;

        if (_audio.IsPlaying)
        {
            int bar = (int)(step / 16) + 1;
            int beat = (int)((step % 16) / 4) + 1;
            int sub = (int)(step % 4) + 1;
            LblPos.Text = $"{bar:00}:{beat}:{sub}";

            if (ViewPiano.Visibility == Visibility.Visible) PianoRollCtrl.SetPlayhead(step);
            if (ViewPlaylist.Visibility == Visibility.Visible) PlaylistCtrl.SetPlayhead(step);
            if (ViewRack.Visibility == Visibility.Visible)
            {
                int patLen = Math.Max(16, _project.GetActivePattern()?.LengthSteps ?? 16);
                UpdateRackPlayhead((int)Math.Floor(step) % patLen);
            }
        }

        if (ViewMixer.Visibility == Visibility.Visible)
        {
            foreach (var (tid, fill) in _mixerMeterFills)
            {
                float peak = _audio.GetTrackPeak(tid);
                double parentH = (fill.Parent as FrameworkElement)?.ActualHeight ?? 140;
                fill.Height = Math.Clamp(peak * parentH * 1.1, 0, Math.Max(0, parentH - 2));
                fill.Background = BrushFromHex(peak > 0.92f ? "#E05A47" : peak > 0.72f ? "#E8A22A" : "#46C273");
            }
        }

        double cpu = _audio.CpuLoadPercent;
        LblCpu.Text = $"CPU {cpu:0}% | Voces: {_audio.ActiveVoiceCount}";
        LblCpu.Foreground = BrushFromHex(cpu > 70 ? "#E05A47" : cpu > 40 ? "#E8A22A" : "#46C273");
    }

    private void UpdateRackPlayhead(int currentStep)
    {
        if (currentStep == _lastStepHighlighted) return;
        foreach (var list in _stepPadVisuals.Values)
        {
            if (_lastStepHighlighted >= 0 && _lastStepHighlighted < list.Count)
                list[_lastStepHighlighted].BorderThickness = new Thickness(1);
            if (currentStep >= 0 && currentStep < list.Count)
                list[currentStep].BorderThickness = new Thickness(2);
        }
        _lastStepHighlighted = currentStep;
    }

    // ========================================================================
    // EVENT HANDLERS: TRANSPORT, TABS, TOOLS, MENU & KEYBOARD
    // ========================================================================
    private void TabNav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewRack == null) return;
        ViewRack.Visibility = TabRack.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewPiano.Visibility = TabPiano.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewPlaylist.Visibility = TabPlaylist.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewMixer.Visibility = TabMixer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (TabPiano.IsChecked == true) PianoRollCtrl.InvalidateVisual();
        if (TabPlaylist.IsChecked == true) PlaylistCtrl.InvalidateVisual();
        UpdateGuideBanner();
    }

    private void UpdateGuideBanner()
    {
        if (LblGuideText == null) return;
        if (TabRack.IsChecked == true)
            LblGuideText.Text = "Paso 1 (Ritmos): Haz clic en los cuadros para armar tu batería. Haz clic en cualquier instrumento para ajustar su sonido a la derecha.";
        else if (TabPiano.IsChecked == true)
            LblGuideText.Text = "Paso 2 (Melodías): Haz clic en la cuadrícula para poner notas o acordes, arrastra el borde derecho para alargarlas y clic derecho para borrar.";
        else if (TabPlaylist.IsChecked == true)
            LblGuideText.Text = "Paso 3 (Canción): Haz clic en las pistas para ordenar tus patrones y armar el tema completo. Luego pulsa 'Canción' arriba para escucharlo todo.";
        else if (TabMixer.IsChecked == true)
            LblGuideText.Text = "Paso 4 (Mezcla y Efectos): Elige una pista para ajustar su volumen y añade Ecualizador, Reverb, Eco, Soundgoodizer o Gross Beat a la derecha.";
    }

    private void BtnHideGuide_Click(object sender, RoutedEventArgs e)
    {
        GuideBanner.Visibility = Visibility.Collapsed;
        MenuShowGuide.IsChecked = false;
        _updaterSettings.ShowGuideBanners = false;
        GitHubUpdater.SaveSettings(_updaterSettings);
    }

    private void MenuShowGuide_Click(object sender, RoutedEventArgs e)
    {
        bool show = MenuShowGuide.IsChecked;
        GuideBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _updaterSettings.ShowGuideBanners = show;
        GitHubUpdater.SaveSettings(_updaterSettings);
    }

    private void AddQuickSoundPresets(Panel parent, ChannelModel ch)
    {
        (string label, Action apply)[] presets = ch.Type switch
        {
            InstrumentType.DrumSynth => new (string, Action)[]
            {
                ("Suave", () => { ch.Punch = 0.45f; ch.Drive = 0.05f; ch.Tone = 0.45f; }),
                ("Pegada Fuerte", () => { ch.Punch = 0.95f; ch.Drive = 0.35f; ch.Tone = 0.72f; }),
                ("Oscuro", () => { ch.Punch = 0.75f; ch.Drive = 0.18f; ch.Tone = 0.28f; })
            },
            InstrumentType.Bass808 => new (string, Action)[]
            {
                ("Sub Limpio", () => { ch.Drive = 0.12f; ch.CutoffHz = 420f; ch.Punch = 0.35f; ch.Decay = 1.4f; }),
                ("808 Trap Fuerte", () => { ch.Drive = 0.55f; ch.CutoffHz = 1100f; ch.Punch = 0.85f; ch.Decay = 1.15f; }),
                ("808 Corto", () => { ch.Drive = 0.38f; ch.CutoffHz = 850f; ch.Punch = 0.90f; ch.Decay = 0.55f; })
            },
            InstrumentType.KeysPiano => new (string, Action)[]
            {
                ("Rhodes", () => { ch.KeysMode = KeysMode.Rhodes; ch.Tone = 0.52f; ch.Decay = 1.8f; }),
                ("Piano", () => { ch.KeysMode = KeysMode.GrandPiano; ch.Tone = 0.72f; ch.Decay = 1.5f; }),
                ("Campana", () => { ch.KeysMode = KeysMode.BellKeys; ch.Tone = 0.80f; ch.Decay = 1.6f; })
            },
            InstrumentType.SuperSaw or InstrumentType.Osc3x or InstrumentType.SytrusFM => new (string, Action)[]
            {
                ("Oscuro", () => { ch.CutoffHz = 1400f; ch.Release = 0.35f; }),
                ("Brillante", () => { ch.CutoffHz = 7800f; ch.Release = 0.25f; }),
                ("Pad Suave", () => { ch.Attack = 0.22f; ch.CutoffHz = 2600f; ch.Release = 0.85f; })
            },
            _ => Array.Empty<(string, Action)>()
        };

        if (presets.Length == 0) return;

        parent.Children.Add(new TextBlock { Text = "Ajuste rápido:", Foreground = BrushFromHex("#93A0B3"), FontSize = 10, Margin = new Thickness(0, 0, 0, 4) });
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (label, apply) in presets)
        {
            var btn = new Button { Content = label, Padding = new Thickness(8, 3, 8, 3), FontSize = 11, Margin = new Thickness(0, 0, 5, 4) };
            btn.Click += (_, _) =>
            {
                apply();
                RebuildInstrumentInspector();
                _audio.PreviewNote(ch, ch.Type == InstrumentType.Bass808 ? 36 : 60, 0.88f, 0.35);
            };
            row.Children.Add(btn);
        }
        parent.Children.Add(row);
    }

    // ========================================================================
    // AUTOMATIC GITHUB UPDATES
    // ========================================================================
    private async Task CheckForGitHubUpdatesOnStartupAsync()
    {
        if (!_updaterSettings.AutoCheckOnStartup) return;
        try
        {
            await Task.Delay(2000);
            var info = await GitHubUpdater.CheckForUpdatesAsync();
            if (info.IsNewerAvailable)
            {
                _pendingUpdate = info;
                await Dispatcher.InvokeAsync(() =>
                {
                    LblUpdateBanner.Text = $"Nueva versión disponible en GitHub: {info.TagName} (tienes v{GitHubUpdater.CurrentVersionString}).";
                    UpdateBanner.Visibility = Visibility.Visible;
                    SetStatus($"Actualización disponible en GitHub: {info.TagName}");
                });
            }
        }
        catch
        {
            // Silent on startup if offline or rate-limited
        }
    }

    private void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        OpenUpdateDialog(_pendingUpdate);
    }

    private void BtnOpenFoundUpdate_Click(object sender, RoutedEventArgs e)
    {
        OpenUpdateDialog(_pendingUpdate);
    }

    private void BtnDismissUpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void OpenUpdateDialog(UpdateReleaseInfo? preloaded)
    {
        var win = new UpdateWindow(_updaterSettings, preloaded) { Owner = this };
        win.ShowDialog();
        if (win.RestartRequired)
        {
            Close();
        }
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e) { _audio.TogglePlay(); SyncTransportHeader(); }
    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop(true);
        LblPos.Text = "01:1:1";
        UpdateRackPlayhead(-1);
        PianoRollCtrl.SetPlayhead(0);
        PlaylistCtrl.SetPlayhead(0);
        SyncTransportHeader();
    }

    private void BtnModePat_Click(object sender, RoutedEventArgs e) { _project.Mode = PlaybackMode.Pattern; _audio.Stop(true); SyncTransportHeader(); }
    private void BtnModeSong_Click(object sender, RoutedEventArgs e) { _project.Mode = PlaybackMode.Song; _audio.Stop(true); SyncTransportHeader(); }

    private void BtnBpmDown_Click(object sender, RoutedEventArgs e) { _project.Bpm = Math.Clamp(_project.Bpm - 2, 40, 240); SyncTransportHeader(); }
    private void BtnBpmUp_Click(object sender, RoutedEventArgs e) { _project.Bpm = Math.Clamp(_project.Bpm + 2, 40, 240); SyncTransportHeader(); }
    private void TxtBpm_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtBpm.Text, out int v)) _project.Bpm = Math.Clamp(v, 40, 240);
        SyncTransportHeader();
    }
    private void TxtBpm_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) TxtBpm_LostFocus(sender, e); }

    private void SldSwing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsSuppressed) return;
        _project.Swing = (float)e.NewValue;
    }

    private void ChkMetro_Click(object sender, RoutedEventArgs e) => _project.Metronome = ChkMetro.IsChecked == true;

    private void ChkEco_Click(object sender, RoutedEventArgs e)
    {
        bool eco = ChkEco.IsChecked == true;
        _audio.EcoMode = eco;
        MenuEcoMode.IsChecked = eco;
        _uiTimer.Interval = TimeSpan.FromMilliseconds(eco ? 55 : 33);
        SetStatus(eco ? "Modo Ligero ACTIVADO: menor uso de CPU y RAM (máx. 12 voces, 18 FPS)." : "Modo Calidad Studio ACTIVADO.");
    }

    private void MenuEcoMode_Click(object sender, RoutedEventArgs e)
    {
        ChkEco.IsChecked = MenuEcoMode.IsChecked;
        ChkEco_Click(sender, e);
    }

    private void CmbPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSuppressed || CmbPattern.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        _project.ActivePatternId = id;
        RefreshAllUI();
    }

    private void BtnNewPattern_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        int num = _project.Patterns.Count + 1;
        var p = new PatternModel
        {
            Name = $"Patrón {num}",
            ColorHex = DemoProjects.ChannelPalette[(num - 1) % DemoProjects.ChannelPalette.Length],
            LengthSteps = 32
        };
        _project.Patterns.Add(p);
        _project.ActivePatternId = p.Id;
        _project.EnsureIntegrity();
        RefreshAllUI();
        SetStatus($"Creado {p.Name}");
    }

    private void BtnClonePattern_Click(object sender, RoutedEventArgs e)
    {
        var cur = _project.GetActivePattern();
        if (cur == null) return;
        PushUndo();
        var clone = new PatternModel
        {
            Name = $"{cur.Name} (Copia)",
            ColorHex = DemoProjects.ChannelPalette[_project.Patterns.Count % DemoProjects.ChannelPalette.Length],
            LengthSteps = cur.LengthSteps
        };
        foreach (var (cid, data) in cur.Channels)
        {
            var dst = clone.GetOrCreateChannelData(cid);
            Array.Copy(data.Steps, dst.Steps, Math.Min(data.Steps.Length, dst.Steps.Length));
            foreach (var n in data.Notes)
                dst.Notes.Add(new NoteModel { Pitch = n.Pitch, StartStep = n.StartStep, DurationSteps = n.DurationSteps, Velocity = n.Velocity, Pan = n.Pan });
        }
        _project.Patterns.Add(clone);
        _project.ActivePatternId = clone.Id;
        RefreshAllUI();
        SetStatus($"Duplicado: {clone.Name}");
    }

    private void CmbPatLength_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSuppressed || CmbPatLength.SelectedItem is not ComboBoxItem item) return;
        var pat = _project.GetActivePattern();
        if (pat == null) return;
        if (int.TryParse(item.Tag?.ToString(), out int len))
        {
            PushUndo();
            pat.LengthSteps = len;
            _project.EnsureIntegrity();
            RefreshAllUI();
        }
    }

    private void BtnAddInstrument_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        int idx = CmbAddInstrument.SelectedIndex;
        int nextTrack = Math.Clamp(_project.Channels.Count + 1, 1, 12);
        string color = DemoProjects.ChannelPalette[_project.Channels.Count % DemoProjects.ChannelPalette.Length];

        ChannelModel ch = idx switch
        {
            0 => new ChannelModel { Name = "Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = color, MixerTrack = 1 },
            1 => new ChannelModel { Name = "Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Snare, ColorHex = color, MixerTrack = 2 },
            2 => new ChannelModel { Name = "Clap 808", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Clap, ColorHex = color, MixerTrack = 2 },
            3 => new ChannelModel { Name = "Hi-Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.ClosedHat, ColorHex = color, MixerTrack = 3 },
            4 => new ChannelModel { Name = "Open Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.OpenHat, ColorHex = color, MixerTrack = 3 },
            5 => new ChannelModel { Name = "808 SubLab", Type = InstrumentType.Bass808, ColorHex = color, MixerTrack = 4 },
            6 => new ChannelModel { Name = "3xOsc Synth", Type = InstrumentType.Osc3x, ColorHex = color, MixerTrack = nextTrack },
            7 => new ChannelModel { Name = "Sytrus FM", Type = InstrumentType.SytrusFM, ColorHex = color, MixerTrack = nextTrack },
            8 => new ChannelModel { Name = "FLEX SuperSaw", Type = InstrumentType.SuperSaw, ColorHex = color, MixerTrack = nextTrack },
            9 => new ChannelModel { Name = "FL Keys", Type = InstrumentType.KeysPiano, ColorHex = color, MixerTrack = 6 },
            _ => new ChannelModel { Name = "Sampler WAV", Type = InstrumentType.WavSampler, ColorHex = color, MixerTrack = 8 }
        };

        _project.Channels.Add(ch);
        _project.ActiveChannelId = ch.Id;
        _project.EnsureIntegrity();
        RefreshAllUI();
        SetStatus($"Instrumento añadido: {ch.Name}");
    }

    private void BtnClearSteps_Click(object sender, RoutedEventArgs e)
    {
        var pat = _project.GetActivePattern();
        if (pat == null) return;
        PushUndo();
        foreach (var cd in pat.Channels.Values) Array.Clear(cd.Steps);
        RebuildChannelRack();
        PlaylistCtrl.InvalidateVisual();
    }

    // Piano Roll Controls
    private void CmbPianoChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSuppressed || CmbPianoChannel.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        _project.ActiveChannelId = id;
        PianoRollCtrl.InvalidateVisual();
        RebuildInstrumentInspector();
    }

    private void CmbSnap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PianoRollCtrl == null) return;
        if (CmbSnap?.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            PianoRollCtrl.SnapSteps = v;
        }
    }

    private void CmbScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSuppressed) return;
        _project.ScaleRoot = Math.Max(0, CmbScaleRoot.SelectedIndex);
        _project.ScaleIndex = Math.Max(0, CmbScaleType.SelectedIndex);
        PianoRollCtrl.InvalidateVisual();
    }

    private void CmbChordStamp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PianoRollCtrl != null && CmbChordStamp.SelectedIndex >= 0)
            PianoRollCtrl.ChordIndex = CmbChordStamp.SelectedIndex;
    }

    private void ChkGhost_Click(object sender, RoutedEventArgs e)
    {
        PianoRollCtrl.ShowGhostNotes = ChkGhost.IsChecked == true;
        PianoRollCtrl.InvalidateVisual();
    }

    private void CmbPianoTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPianoTools == null || CmbPianoTools.SelectedIndex <= 0) return;
        int tool = CmbPianoTools.SelectedIndex;
        CmbPianoTools.SelectedIndex = 0;

        var pat = _project.GetActivePattern();
        var ch = _project.GetActiveChannel();
        if (pat == null || ch == null) return;
        var cd = pat.GetOrCreateChannelData(ch.Id);

        PushUndo();
        switch (tool)
        {
            case 1: MusicTheory.QuantizeNotes(cd.Notes, PianoRollCtrl.SnapSteps, pat.LengthSteps); break;
            case 2: MusicTheory.HumanizeNotes(cd.Notes, pat.LengthSteps); break;
            case 3: MusicTheory.StrumNotes(cd.Notes, pat.LengthSteps); break;
            case 4: cd.Notes = MusicTheory.ArpeggiateNotes(cd.Notes, PianoRollCtrl.SnapSteps, pat.LengthSteps); break;
            case 5: foreach (var n in cd.Notes) n.Pitch = Math.Clamp(n.Pitch + 12, 24, 96); break;
            case 6: foreach (var n in cd.Notes) n.Pitch = Math.Clamp(n.Pitch - 12, 24, 96); break;
            case 7: cd.Notes.Clear(); break;
        }

        PianoRollCtrl.InvalidateVisual();
        RebuildChannelRack();
        PlaylistCtrl.InvalidateVisual();
    }

    // Playlist Actions
    private void BtnPlaySong_Click(object sender, RoutedEventArgs e)
    {
        _project.Mode = PlaybackMode.Song;
        _audio.SeekStep(0);
        _audio.Play();
        SyncTransportHeader();
    }

    private void BtnAddAutoFilter_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _project.Playlist.Add(new PlaylistClipModel
        {
            Type = ClipType.Automation, Name = "Auto: Filtro Master (Sweep)", AutomationTarget = "MasterFilter",
            ColorHex = "#E05297", TrackIndex = 3, StartStep = 0, LengthSteps = 32,
            Points = { new() { X = 0, Y = 0.25 }, new() { X = 0.6, Y = 0.65 }, new() { X = 1, Y = 1 } }
        });
        PlaylistCtrl.InvalidateVisual();
    }

    private void BtnAddAutoVol_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _project.Playlist.Add(new PlaylistClipModel
        {
            Type = ClipType.Automation, Name = "Auto: Volumen Master (Fade)", AutomationTarget = "MasterVolume",
            ColorHex = "#B564E8", TrackIndex = 3, StartStep = 32, LengthSteps = 32,
            Points = { new() { X = 0, Y = 1 }, new() { X = 0.7, Y = 0.85 }, new() { X = 1, Y = 0.1 } }
        });
        PlaylistCtrl.InvalidateVisual();
    }

    private void BtnImportPlaylistWav_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Archivos de Audio WAV|*.wav", Title = "Importar Audio WAV a la Canción" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var smp = ProjectIO.LoadWavFile(dlg.FileName);
            _project.Samples[smp.Id] = smp;
            double stepDur = 60.0 / Math.Max(40, _project.Bpm) / 4.0;
            int lenSteps = Math.Max(8, (int)Math.Ceiling(smp.DurationSeconds / stepDur / 4.0) * 4);
            PushUndo();
            _project.Playlist.Add(new PlaylistClipModel
            {
                Type = ClipType.Audio, SampleId = smp.Id, Name = System.IO.Path.GetFileNameWithoutExtension(smp.Name),
                ColorHex = "#29B6A8", TrackIndex = 4, StartStep = 0, LengthSteps = lenSteps, Volume = 0.9f
            });
            PlaylistCtrl.InvalidateVisual();
            SetStatus($"Audio WAV importado en Pista 5: {smp.Name}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error WAV", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // Menu Actions
    private void MenuNewEmpty_Click(object sender, RoutedEventArgs e) { PushUndo(); LoadProjectModel(DemoProjects.CreateEmptyProject()); }
    private void MenuDemoTrap_Click(object sender, RoutedEventArgs e) { PushUndo(); LoadProjectModel(DemoProjects.CreateTrapDemo()); }
    private void MenuDemoEdm_Click(object sender, RoutedEventArgs e) { PushUndo(); LoadProjectModel(DemoProjects.CreateEdmDemo()); }
    private void MenuDemoLoFi_Click(object sender, RoutedEventArgs e) { PushUndo(); LoadProjectModel(DemoProjects.CreateLoFiDemo()); }

    private void MenuSaveProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Proyecto FL-Lite Studio|*.fls", FileName = $"{_project.Name}.fls" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ProjectIO.SaveProject(_project, dlg.FileName);
            SetStatus($"Proyecto guardado en {dlg.FileName}");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error al guardar", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void MenuOpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Proyecto FL-Lite Studio|*.fls" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            PushUndo();
            LoadProjectModel(ProjectIO.LoadProject(dlg.FileName));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error al abrir", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void MenuExportWav16_Click(object sender, RoutedEventArgs e) => ExportWavAsync(16);
    private void MenuExportWav24_Click(object sender, RoutedEventArgs e) => ExportWavAsync(24);

    private async void ExportWavAsync(int bitDepth)
    {
        var mode = _project.Mode;
        var dlg = new SaveFileDialog
        {
            Filter = "Audio WAV Estéreo|*.wav",
            FileName = $"{_project.Name}_{mode}_{bitDepth}bit.wav",
            Title = $"Exportar WAV ({bitDepth}-bit, modo {mode})"
        };
        if (dlg.ShowDialog() != true) return;

        SetStatus($"Renderizando WAV {bitDepth}-bit (modo {mode})...");
        IsEnabled = false;
        try
        {
            await Task.Run(() => _audio.RenderOfflineWav(dlg.FileName, mode, bitDepth));
            SetStatus($"WAV exportado con éxito: {dlg.FileName}");
            MessageBox.Show($"Audio exportado correctamente:\n{dlg.FileName}", "Exportación WAV completa", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error exportando WAV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void MenuExportMidi_Click(object sender, RoutedEventArgs e)
    {
        var pat = _project.GetActivePattern();
        if (pat == null) return;
        var dlg = new SaveFileDialog { Filter = "Archivo MIDI estándar|*.mid", FileName = $"{pat.Name}.mid" };
        if (dlg.ShowDialog() != true) return;
        ProjectIO.ExportPatternMidi(dlg.FileName, _project, pat);
        SetStatus($"MIDI exportado: {dlg.FileName}");
    }

    private void MenuUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(ProjectIO.SerializeSnapshot(_project));
        var prev = ProjectIO.DeserializeSnapshot(_undoStack.Pop());
        if (prev != null) LoadProjectModel(prev);
        SetStatus("Deshacer aplicado.");
    }

    private void MenuRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(ProjectIO.SerializeSnapshot(_project));
        var next = ProjectIO.DeserializeSnapshot(_redoStack.Pop());
        if (next != null) LoadProjectModel(next);
        SetStatus("Rehacer aplicado.");
    }

    private void MenuBuf512_Click(object sender, RoutedEventArgs e) { _audio.SetBufferSize(512); SetStatus("Buffer de audio cambiado a 512 samples (baja latencia)."); }
    private void MenuBuf1024_Click(object sender, RoutedEventArgs e) { _audio.SetBufferSize(1024); SetStatus("Buffer de audio cambiado a 1024 samples (equilibrado)."); }
    private void MenuBuf2048_Click(object sender, RoutedEventArgs e) { _audio.SetBufferSize(2048); SetStatus("Buffer de audio cambiado a 2048 samples (máxima estabilidad para PC lenta)."); }

    private void MenuConnectMidi_Click(object sender, RoutedEventArgs e)
    {
        if (_audio.TryConnectMidiKeyboard(out int count))
            SetStatus($"Teclado MIDI USB conectado (dispositivos detectados: {count}).");
        else
            MessageBox.Show("No se detectó ningún teclado MIDI USB conectado.", "MIDI USB", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MenuHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "GUÍA RÁPIDA — FL-LITE STUDIO\n\n" +
            "1. RITMOS (F6): Haz clic en los cuadros para crear bases de batería. Selecciona un instrumento para editar su sonido en el panel derecho.\n" +
            "2. MELODÍAS (F7): Clic izquierdo añade notas o acordes, arrastra el borde derecho para alargarlas, clic derecho borra.\n" +
            "3. CANCIÓN (F5): Coloca tus patrones en las pistas para armar el tema completo, añade automatizaciones o audio WAV y activa SONG.\n" +
            "4. MEZCLA (F9): Ajusta volúmenes y añade Parametric EQ 2, Reverb 2, Delay 3, Gross Beat, Soundgoodizer o Limiter.\n\n" +
            "ATAJOS:\n" +
            "- Espacio: Reproducir / Pausar\n" +
            "- F5 / F6 / F7 / F9: Cambiar de pestaña\n" +
            "- Ctrl+Z / Ctrl+Y: Deshacer / Rehacer\n" +
            "- Teclas Z-M y Q-I: Tocar el instrumento activo como piano (- / + cambia octava)",
            "Guía Rápida", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // Global Keyboard Shortcuts & Piano Keys
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Space) { _audio.TogglePlay(); SyncTransportHeader(); e.Handled = true; return; }
        if (e.Key == Key.F5) { TabPlaylist.IsChecked = true; e.Handled = true; return; }
        if (e.Key == Key.F6) { TabRack.IsChecked = true; e.Handled = true; return; }
        if (e.Key == Key.F7) { TabPiano.IsChecked = true; e.Handled = true; return; }
        if (e.Key == Key.F9) { TabMixer.IsChecked = true; e.Handled = true; return; }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) { MenuUndo_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.Y) { MenuRedo_Click(sender, e); e.Handled = true; return; }
            if (e.Key == Key.S) { MenuSaveProject_Click(sender, e); e.Handled = true; return; }
        }

        if (e.Key is Key.OemMinus or Key.Subtract)
        {
            _pianoOctave = Math.Clamp(_pianoOctave - 1, 1, 6);
            LblKeyboardHint.Text = $"Teclado Piano: Octava C{_pianoOctave} (- / +)";
            return;
        }
        if (e.Key is Key.OemPlus or Key.Add)
        {
            _pianoOctave = Math.Clamp(_pianoOctave + 1, 1, 6);
            LblKeyboardHint.Text = $"Teclado Piano: Octava C{_pianoOctave} (- / +)";
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && KeyToSemitone.TryGetValue(e.Key, out int semi) && _heldPianoKeys.Add(e.Key))
        {
            var ch = _project.GetActiveChannel();
            if (ch != null) _audio.PreviewNote(ch, _pianoOctave * 12 + semi, 0.88f, 0.40);
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e) => _heldPianoKeys.Remove(e.Key);

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _uiTimer.Stop();
        _audio.Dispose();
    }
}

/// <summary>
/// Simple horizontal panel distributing step pads evenly.
/// </summary>
internal sealed class UniformGridSteps : System.Windows.Controls.Primitives.UniformGrid
{
    public UniformGridSteps(int columns)
    {
        Rows = 1;
        Columns = columns;
    }
}
