using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FLLiteStudio.Core;

namespace FLLiteStudio.Controls;

public sealed class PianoRollControl : FrameworkElement
{
    private const int MinMidi = 24; // C1
    private const int MaxMidi = 96; // C7
    private const int TotalRows = MaxMidi - MinMidi + 1;
    private const double KeyWidth = 62;
    private const double HeaderHeight = 24;
    private const double VelHeight = 68;

    private ProjectModel? _project;
    private double _stepWidth = 34;
    private double _noteHeight = 16;
    private double _scrollX = 0;
    private double _scrollY = (MaxMidi - 78) * 16;
    private double _playheadStep = 0;

    public double SnapSteps { get; set; } = 1.0;
    public int ChordIndex { get; set; } = 0;
    public bool ShowGhostNotes { get; set; } = true;
    private double _lastDuration = 2.0;

    // Drag state
    private enum DragKind { None, MoveNote, ResizeNote, Erase, Velocity, Keys }
    private DragKind _dragKind = DragKind.None;
    private NoteModel? _dragNote;
    private double _dragOffsetStep;
    private int _lastPreviewMidi = -1;

    // Cached frozen brushes & pens (zero GC on redraw!)
    private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(26, 30, 36)));
    private static readonly SolidColorBrush HeaderBrush = Freeze(new SolidColorBrush(Color.FromRgb(20, 24, 30)));
    private static readonly SolidColorBrush InScaleWhite = Freeze(new SolidColorBrush(Color.FromRgb(42, 48, 58)));
    private static readonly SolidColorBrush InScaleBlack = Freeze(new SolidColorBrush(Color.FromRgb(34, 40, 49)));
    private static readonly SolidColorBrush OutScaleRow = Freeze(new SolidColorBrush(Color.FromRgb(22, 26, 32)));
    private static readonly SolidColorBrush GridLineSub = Freeze(new SolidColorBrush(Color.FromRgb(30, 35, 43)));
    private static readonly SolidColorBrush GridLineBeat = Freeze(new SolidColorBrush(Color.FromRgb(54, 62, 75)));
    private static readonly SolidColorBrush GridLineBar = Freeze(new SolidColorBrush(Color.FromRgb(82, 94, 112)));
    private static readonly SolidColorBrush RowSep = Freeze(new SolidColorBrush(Color.FromRgb(22, 26, 32)));
    private static readonly SolidColorBrush RowSepC = Freeze(new SolidColorBrush(Color.FromRgb(68, 79, 96)));
    private static readonly SolidColorBrush KeyWhite = Freeze(new SolidColorBrush(Color.FromRgb(224, 229, 236)));
    private static readonly SolidColorBrush KeyBlack = Freeze(new SolidColorBrush(Color.FromRgb(30, 34, 40)));
    private static readonly SolidColorBrush KeyBlackInner = Freeze(new SolidColorBrush(Color.FromRgb(48, 54, 64)));
    private static readonly SolidColorBrush GhostFill = Freeze(new SolidColorBrush(Color.FromArgb(45, 160, 175, 195)));
    private static readonly Pen GhostPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(95, 160, 175, 195)), 1));
    private static readonly Pen NoteBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(16, 20, 26)), 1));
    private static readonly SolidColorBrush PlayheadBrush = Freeze(new SolidColorBrush(Color.FromRgb(232, 131, 42)));
    private static readonly SolidColorBrush VelBg = Freeze(new SolidColorBrush(Color.FromRgb(19, 23, 29)));
    private static readonly SolidColorBrush TextLight = Freeze(new SolidColorBrush(Color.FromRgb(220, 228, 238)));
    private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(130, 145, 165)));
    private static readonly SolidColorBrush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(18, 22, 28)));
    private static readonly SolidColorBrush ShadeOut = Freeze(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)));
    private static readonly Typeface UiFont = new("Segoe UI");

    public event Action<int, float>? NotePreviewRequested;
    public event Action? NotesModified;
    public event Action<double>? PlayheadSeekRequested;

    public PianoRollControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void BindProject(ProjectModel project)
    {
        _project = project;
        InvalidateVisual();
    }

    public void SetPlayhead(double step)
    {
        if (Math.Abs(_playheadStep - step) < 0.01) return;
        _playheadStep = step;
        InvalidateVisual();
    }

    private (PatternModel pat, ChannelModel ch, PatternChannelData data)? GetActive()
    {
        if (_project == null) return null;
        var pat = _project.GetActivePattern();
        var ch = _project.GetActiveChannel();
        if (pat == null || ch == null) return null;
        return (pat, ch, pat.GetOrCreateChannelData(ch.Id));
    }

    private double SnapFloor(double raw) => SnapSteps <= 0 ? Math.Max(0, raw) : Math.Max(0, Math.Floor(raw / SnapSteps) * SnapSteps);
    private double SnapRound(double raw) => SnapSteps <= 0 ? Math.Max(0, raw) : Math.Max(0, Math.Round(raw / SnapSteps) * SnapSteps);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _stepWidth = Math.Clamp(_stepWidth * (e.Delta > 0 ? 1.12 : 0.89), 14, 84);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _scrollX = Math.Max(0, _scrollX - e.Delta * 0.6);
        }
        else
        {
            double gridH = Math.Max(100, ActualHeight - HeaderHeight - VelHeight);
            double maxY = Math.Max(0, TotalRows * _noteHeight - gridH);
            _scrollY = Math.Clamp(_scrollY - e.Delta * 0.55, 0, maxY);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private (NoteModel note, bool onRightEdge)? HitTestNote(List<NoteModel> notes, double step, int pitch)
    {
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var n = notes[i];
            if (n.Pitch == pitch && step >= n.StartStep && step <= n.StartStep + n.DurationSteps)
            {
                double rightPx = (n.StartStep + n.DurationSteps - step) * _stepWidth;
                return (n, rightPx <= 9);
            }
        }
        return null;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var active = GetActive();
        if (active == null) return;
        var (pat, ch, data) = active.Value;

        var pos = e.GetPosition(this);
        double gridH = ActualHeight - HeaderHeight - VelHeight;
        double step = (pos.X - KeyWidth + _scrollX) / _stepWidth;
        int row = (int)Math.Floor((pos.Y - HeaderHeight + _scrollY) / _noteHeight);
        int pitch = MaxMidi - row;

        CaptureMouse();

        // Header scrub
        if (pos.Y < HeaderHeight && pos.X >= KeyWidth)
        {
            PlayheadSeekRequested?.Invoke(Math.Clamp(step, 0, pat.LengthSteps - 0.1));
            return;
        }

        // Piano keys audition
        if (pos.X < KeyWidth && pos.Y >= HeaderHeight && pos.Y < HeaderHeight + gridH)
        {
            int p = Math.Clamp(pitch, MinMidi, MaxMidi);
            _lastPreviewMidi = p;
            _dragKind = DragKind.Keys;
            NotePreviewRequested?.Invoke(p, 0.88f);
            return;
        }

        // Velocity lane
        if (pos.X >= KeyWidth && pos.Y >= HeaderHeight + gridH)
        {
            EditVelocityAt(data.Notes, pos.X, pos.Y, gridH);
            _dragKind = DragKind.Velocity;
            return;
        }

        // Grid area
        if (pos.X >= KeyWidth && pos.Y >= HeaderHeight && pos.Y < HeaderHeight + gridH)
        {
            var hit = HitTestNote(data.Notes, step, pitch);

            if (e.ChangedButton == MouseButton.Right)
            {
                if (hit != null)
                {
                    data.Notes.Remove(hit.Value.note);
                    NotesModified?.Invoke();
                    InvalidateVisual();
                }
                _dragKind = DragKind.Erase;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (hit != null)
                {
                    _dragNote = hit.Value.note;
                    if (hit.Value.onRightEdge)
                    {
                        _dragKind = DragKind.ResizeNote;
                    }
                    else
                    {
                        _dragKind = DragKind.MoveNote;
                        _dragOffsetStep = step - hit.Value.note.StartStep;
                        _lastPreviewMidi = hit.Value.note.Pitch;
                        NotePreviewRequested?.Invoke(hit.Value.note.Pitch, hit.Value.note.Velocity);
                    }
                    return;
                }

                // Add note or chord stamp
                double startStep = Math.Clamp(SnapFloor(step), 0, pat.LengthSteps - 0.25);
                var chord = MusicTheory.Chords[Math.Clamp(ChordIndex, 0, MusicTheory.Chords.Length - 1)];
                double dur = SnapSteps > 0 ? Math.Max(SnapSteps, _lastDuration) : _lastDuration;
                NoteModel? primary = null;

                foreach (int iv in chord.Intervals)
                {
                    int p = Math.Clamp(pitch + iv, MinMidi, MaxMidi);
                    if (!data.Notes.Any(n => n.Pitch == p && Math.Abs(n.StartStep - startStep) < 0.05))
                    {
                        var created = new NoteModel
                        {
                            Pitch = p,
                            StartStep = startStep,
                            DurationSteps = Math.Min(dur, pat.LengthSteps - startStep),
                            Velocity = 0.85f
                        };
                        data.Notes.Add(created);
                        primary ??= created;
                    }
                    NotePreviewRequested?.Invoke(p, 0.84f);
                }

                if (primary != null)
                {
                    _dragNote = primary;
                    _dragKind = DragKind.ResizeNote;
                }
                NotesModified?.Invoke();
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var active = GetActive();
        if (active == null) return;
        var (pat, _, data) = active.Value;

        var pos = e.GetPosition(this);
        double gridH = ActualHeight - HeaderHeight - VelHeight;
        double step = (pos.X - KeyWidth + _scrollX) / _stepWidth;
        int pitch = Math.Clamp(MaxMidi - (int)Math.Floor((pos.Y - HeaderHeight + _scrollY) / _noteHeight), MinMidi, MaxMidi);

        if (_dragKind == DragKind.None)
        {
            if (pos.X >= KeyWidth && pos.Y >= HeaderHeight && pos.Y < HeaderHeight + gridH)
            {
                var hit = HitTestNote(data.Notes, step, pitch);
                Cursor = hit == null ? Cursors.Arrow : hit.Value.onRightEdge ? Cursors.SizeWE : Cursors.Hand;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
            return;
        }

        if (_dragKind == DragKind.Keys)
        {
            if (pitch != _lastPreviewMidi)
            {
                _lastPreviewMidi = pitch;
                NotePreviewRequested?.Invoke(pitch, 0.85f);
            }
        }
        else if (_dragKind == DragKind.Velocity)
        {
            EditVelocityAt(data.Notes, pos.X, pos.Y, gridH);
        }
        else if (_dragKind == DragKind.Erase)
        {
            var hit = HitTestNote(data.Notes, step, pitch);
            if (hit != null)
            {
                data.Notes.Remove(hit.Value.note);
                InvalidateVisual();
            }
        }
        else if (_dragKind == DragKind.ResizeNote && _dragNote != null)
        {
            double minDur = SnapSteps > 0 ? SnapSteps : 0.25;
            double end = SnapRound(step);
            _dragNote.DurationSteps = Math.Clamp(end - _dragNote.StartStep, minDur, pat.LengthSteps - _dragNote.StartStep);
            _lastDuration = _dragNote.DurationSteps;
            InvalidateVisual();
        }
        else if (_dragKind == DragKind.MoveNote && _dragNote != null)
        {
            _dragNote.StartStep = Math.Clamp(SnapRound(step - _dragOffsetStep), 0, pat.LengthSteps - _dragNote.DurationSteps);
            if (pitch != _lastPreviewMidi)
            {
                _lastPreviewMidi = pitch;
                NotePreviewRequested?.Invoke(pitch, _dragNote.Velocity);
            }
            _dragNote.Pitch = pitch;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragKind != DragKind.None)
        {
            bool changed = _dragKind is DragKind.MoveNote or DragKind.ResizeNote or DragKind.Erase or DragKind.Velocity;
            _dragKind = DragKind.None;
            _dragNote = null;
            ReleaseMouseCapture();
            if (changed) NotesModified?.Invoke();
        }
    }

    private void EditVelocityAt(List<NoteModel> notes, double x, double y, double gridH)
    {
        double laneTop = HeaderHeight + gridH;
        float vel = (float)Math.Clamp(1.0 - (y - laneTop - 6) / Math.Max(10, VelHeight - 12), 0.08, 1.0);
        double step = (x - KeyWidth + _scrollX) / _stepWidth;
        bool touched = false;
        foreach (var n in notes)
        {
            if (Math.Abs(n.StartStep - step) < 0.6)
            {
                n.Velocity = vel;
                touched = true;
            }
        }
        if (touched) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 20 || h < 60) return;

        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        var active = GetActive();
        if (active == null || _project == null) return;
        var (pat, ch, data) = active.Value;

        double gridH = Math.Max(40, h - HeaderHeight - VelHeight);
        int stepsTotal = Math.Max(16, pat.LengthSteps);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 1. Main Grid Clip
        dc.PushClip(new RectangleGeometry(new Rect(KeyWidth, HeaderHeight, w - KeyWidth, gridH)));

        int firstRow = Math.Max(0, (int)Math.Floor(_scrollY / _noteHeight));
        int lastRow = Math.Min(TotalRows - 1, (int)Math.Ceiling((_scrollY + gridH) / _noteHeight));

        for (int r = firstRow; r <= lastRow; r++)
        {
            int midi = MaxMidi - r;
            double y = HeaderHeight + r * _noteHeight - _scrollY;
            bool black = MusicTheory.IsBlackKey(midi);
            bool inScale = MusicTheory.IsInScale(midi, _project.ScaleRoot, _project.ScaleIndex);
            var rowBrush = _project.ScaleIndex > 0
                ? (inScale ? (black ? InScaleBlack : InScaleWhite) : OutScaleRow)
                : (black ? InScaleBlack : InScaleWhite);

            dc.DrawRectangle(rowBrush, null, new Rect(KeyWidth, y, w - KeyWidth, _noteHeight));
            dc.DrawRectangle((midi % 12) == 0 ? RowSepC : RowSep, null, new Rect(KeyWidth, y + _noteHeight - 1, w - KeyWidth, 1));
        }

        // Out of pattern length shade
        double patEndX = KeyWidth + stepsTotal * _stepWidth - _scrollX;
        if (patEndX < w) dc.DrawRectangle(ShadeOut, null, new Rect(patEndX, HeaderHeight, w - patEndX, gridH));

        // Vertical grid lines
        int firstStep = Math.Max(0, (int)Math.Floor(_scrollX / _stepWidth));
        int lastStep = Math.Min(stepsTotal, (int)Math.Ceiling((_scrollX + (w - KeyWidth)) / _stepWidth));
        for (int s = firstStep; s <= lastStep; s++)
        {
            double x = KeyWidth + s * _stepWidth - _scrollX;
            var lineBrush = (s % 16 == 0) ? GridLineBar : (s % 4 == 0) ? GridLineBeat : GridLineSub;
            dc.DrawRectangle(lineBrush, null, new Rect(x, HeaderHeight, (s % 16 == 0) ? 1.5 : 1, gridH));
        }

        // Ghost Notes
        if (ShowGhostNotes)
        {
            foreach (var other in _project.Channels)
            {
                if (other.Id == ch.Id) continue;
                if (!pat.Channels.TryGetValue(other.Id, out var oData)) continue;
                foreach (var gn in oData.Notes)
                {
                    double nx = KeyWidth + gn.StartStep * _stepWidth - _scrollX;
                    double nw = Math.Max(4, gn.DurationSteps * _stepWidth - 1);
                    double ny = HeaderHeight + (MaxMidi - gn.Pitch) * _noteHeight - _scrollY + 1;
                    if (nx + nw < KeyWidth || nx > w || ny + _noteHeight < HeaderHeight || ny > HeaderHeight + gridH) continue;
                    dc.DrawRectangle(GhostFill, GhostPen, new Rect(nx, ny, nw, _noteHeight - 2));
                }
            }
        }

        // Active Notes
        var noteColor = (Color)ColorConverter.ConvertFromString(ch.ColorHex);
        var noteBrush = new SolidColorBrush(noteColor); noteBrush.Freeze();
        foreach (var n in data.Notes)
        {
            double nx = KeyWidth + n.StartStep * _stepWidth - _scrollX;
            double nw = Math.Max(6, n.DurationSteps * _stepWidth - 1);
            double ny = HeaderHeight + (MaxMidi - n.Pitch) * _noteHeight - _scrollY + 1;
            if (nx + nw < KeyWidth || nx > w || ny + _noteHeight < HeaderHeight || ny > HeaderHeight + gridH) continue;

            dc.DrawRoundedRectangle(noteBrush, NoteBorderPen, new Rect(nx, ny, nw, _noteHeight - 2), 2, 2);
            if (nw >= 26)
            {
                var ft = new FormattedText(MusicTheory.MidiToName(n.Pitch), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, UiFont, 10, TextDark, dpi);
                dc.DrawText(ft, new Point(nx + 4, ny + 1));
            }
        }

        // Playhead
        double phX = KeyWidth + (_playheadStep % stepsTotal) * _stepWidth - _scrollX;
        if (phX >= KeyWidth && phX <= w)
            dc.DrawRectangle(PlayheadBrush, null, new Rect(phX, HeaderHeight, 2, gridH));

        dc.Pop(); // end Grid Clip

        // 2. Left Piano Keyboard
        dc.PushClip(new RectangleGeometry(new Rect(0, HeaderHeight, KeyWidth, gridH)));
        for (int r = firstRow; r <= lastRow; r++)
        {
            int midi = MaxMidi - r;
            double y = HeaderHeight + r * _noteHeight - _scrollY;
            bool black = MusicTheory.IsBlackKey(midi);
            dc.DrawRectangle(black ? KeyBlack : KeyWhite, null, new Rect(0, y, KeyWidth - 1, _noteHeight));
            if (black) dc.DrawRectangle(KeyBlackInner, null, new Rect(0, y + 1, KeyWidth * 0.62, _noteHeight - 2));
            dc.DrawRectangle(GridLineBeat, null, new Rect(0, y + _noteHeight - 1, KeyWidth, 1));

            if (midi % 12 == 0)
            {
                var ft = new FormattedText(MusicTheory.MidiToName(midi), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, UiFont, 10, TextDark, dpi);
                dc.DrawText(ft, new Point(KeyWidth - 24, y + 1));
            }
        }
        dc.Pop();

        // 3. Top Bar Ruler
        dc.DrawRectangle(HeaderBrush, null, new Rect(0, 0, w, HeaderHeight));
        dc.DrawRectangle(GridLineBeat, null, new Rect(0, HeaderHeight - 1, w, 1));
        var lblP = new FormattedText("PIANO", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextMuted, dpi);
        dc.DrawText(lblP, new Point(12, 5));

        for (int s = firstStep - (firstStep % 4); s <= lastStep; s += 4)
        {
            double x = KeyWidth + s * _stepWidth - _scrollX;
            if (x < KeyWidth - 10) continue;
            string txt = (s % 16 == 0) ? $"{s / 16 + 1}" : $"{s / 16 + 1}.{(s % 16) / 4 + 1}";
            var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10,
                (s % 16 == 0) ? TextLight : TextMuted, dpi);
            dc.DrawText(ft, new Point(x + 4, 5));
        }

        // 4. Bottom Velocity Lane
        double velTop = HeaderHeight + gridH;
        dc.DrawRectangle(VelBg, null, new Rect(0, velTop, w, VelHeight));
        dc.DrawRectangle(GridLineBeat, null, new Rect(0, velTop, w, 1));
        var vLbl = new FormattedText("FUERZA", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextMuted, dpi);
        dc.DrawText(vLbl, new Point(10, velTop + 8));

        dc.PushClip(new RectangleGeometry(new Rect(KeyWidth, velTop, w - KeyWidth, VelHeight)));
        dc.DrawRectangle(GridLineSub, null, new Rect(KeyWidth, velTop + VelHeight * 0.5, w - KeyWidth, 1));
        foreach (var n in data.Notes)
        {
            double nx = KeyWidth + n.StartStep * _stepWidth - _scrollX;
            if (nx < KeyWidth - 6 || nx > w + 6) continue;
            double barH = Math.Clamp(n.Velocity * (VelHeight - 14), 4, VelHeight - 12);
            double by = h - barH - 4;
            dc.DrawRectangle(noteBrush, null, new Rect(nx + 1, by, 4, barH));
            dc.DrawRectangle(TextLight, null, new Rect(nx, by - 2, 6, 3));
        }
        dc.Pop();
    }
}
