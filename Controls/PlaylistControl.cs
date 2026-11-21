using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FLLiteStudio.Core;

namespace FLLiteStudio.Controls;

public sealed class PlaylistControl : FrameworkElement
{
    private const double TrackHeaderW = 110;
    private const double RulerH = 26;
    private const int NumTracks = 12;
    private const double TrackH = 54;

    private ProjectModel? _project;
    private double _stepWidth = 14;
    private double _scrollX = 0;
    private double _scrollY = 0;
    private double _playheadStep = 0;

    private enum DragMode { None, MoveClip, ResizeClip, AutoPoint }
    private DragMode _dragMode = DragMode.None;
    private PlaylistClipModel? _dragClip;
    private int _dragAutoIndex = -1;
    private double _dragOffsetStep;

    private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(24, 28, 34)));
    private static readonly SolidColorBrush TrackEven = Freeze(new SolidColorBrush(Color.FromRgb(33, 38, 46)));
    private static readonly SolidColorBrush TrackOdd = Freeze(new SolidColorBrush(Color.FromRgb(29, 34, 41)));
    private static readonly SolidColorBrush TrackSep = Freeze(new SolidColorBrush(Color.FromRgb(19, 23, 28)));
    private static readonly SolidColorBrush HeaderBg = Freeze(new SolidColorBrush(Color.FromRgb(35, 41, 50)));
    private static readonly SolidColorBrush RulerBg = Freeze(new SolidColorBrush(Color.FromRgb(20, 24, 30)));
    private static readonly SolidColorBrush LineBar = Freeze(new SolidColorBrush(Color.FromRgb(64, 75, 90)));
    private static readonly SolidColorBrush LineBeat = Freeze(new SolidColorBrush(Color.FromRgb(38, 45, 55)));
    private static readonly SolidColorBrush ClipBody = Freeze(new SolidColorBrush(Color.FromArgb(225, 22, 27, 34)));
    private static readonly SolidColorBrush PlayheadBrush = Freeze(new SolidColorBrush(Color.FromRgb(70, 194, 115)));
    private static readonly SolidColorBrush TextLight = Freeze(new SolidColorBrush(Color.FromRgb(225, 232, 240)));
    private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(135, 148, 168)));
    private static readonly SolidColorBrush TextDark = Freeze(new SolidColorBrush(Color.FromRgb(16, 20, 26)));
    private static readonly SolidColorBrush AutoFill = Freeze(new SolidColorBrush(Color.FromArgb(45, 224, 82, 151)));
    private static readonly Pen AutoPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(236, 102, 168)), 2));
    private static readonly Pen NodePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(180, 30, 100)), 1.5));
    private static readonly SolidColorBrush WaveBrush = Freeze(new SolidColorBrush(Color.FromRgb(45, 200, 225)));
    private static readonly Typeface UiFont = new("Segoe UI");

    public event Action? PlaylistModified;
    public event Action<double>? PlayheadSeekRequested;
    public event Action<string>? PatternClipDoubleClicked;

    public PlaylistControl()
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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            _stepWidth = Math.Clamp(_stepWidth * (e.Delta > 0 ? 1.12 : 0.89), 6, 32);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _scrollX = Math.Max(0, _scrollX - e.Delta * 0.6);
        else
            _scrollY = Math.Clamp(_scrollY - e.Delta * 0.5, 0, Math.Max(0, NumTracks * TrackH - (ActualHeight - RulerH)));

        InvalidateVisual();
        e.Handled = true;
    }

    private (PlaylistClipModel clip, bool onRightEdge, bool inHeader, int autoPtIdx)? HitTest(Point pos)
    {
        if (_project == null) return null;
        double step = (pos.X - TrackHeaderW + _scrollX) / _stepWidth;
        int track = (int)Math.Floor((pos.Y - RulerH + _scrollY) / TrackH);

        for (int i = _project.Playlist.Count - 1; i >= 0; i--)
        {
            var c = _project.Playlist[i];
            if (c.TrackIndex != track || step < c.StartStep || step > c.StartStep + c.LengthSteps) continue;

            double cx = TrackHeaderW + c.StartStep * _stepWidth - _scrollX;
            double cw = c.LengthSteps * _stepWidth;
            double cy = RulerH + c.TrackIndex * TrackH - _scrollY + 2;
            double ch = TrackH - 4;

            if (c.Type == ClipType.Automation)
            {
                for (int pi = 0; pi < c.Points.Count; pi++)
                {
                    var pt = c.Points[pi];
                    double px = cx + pt.X * cw;
                    double py = cy + 16 + (1.0 - pt.Y) * (ch - 20);
                    double dx = pos.X - px, dy = pos.Y - py;
                    if (dx * dx + dy * dy <= 81)
                        return (c, false, false, pi);
                }
            }

            bool rightEdge = (cx + cw - pos.X) <= 9;
            bool inHeader = (pos.Y - cy) <= 16;
            return (c, rightEdge, inHeader, -1);
        }
        return null;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_project == null) return;
        var pos = e.GetPosition(this);
        CaptureMouse();

        if (pos.Y < RulerH && pos.X >= TrackHeaderW)
        {
            double s = Math.Max(0, (pos.X - TrackHeaderW + _scrollX) / _stepWidth);
            PlayheadSeekRequested?.Invoke(s);
            return;
        }

        if (pos.X < TrackHeaderW || pos.Y < RulerH) return;

        var hit = HitTest(pos);
        double step = (pos.X - TrackHeaderW + _scrollX) / _stepWidth;
        int track = Math.Clamp((int)Math.Floor((pos.Y - RulerH + _scrollY) / TrackH), 0, NumTracks - 1);

        if (e.ChangedButton == MouseButton.Right)
        {
            if (hit != null)
            {
                if (hit.Value.autoPtIdx >= 0 && hit.Value.clip.Points.Count > 2)
                    hit.Value.clip.Points.RemoveAt(hit.Value.autoPtIdx);
                else
                    _project.Playlist.Remove(hit.Value.clip);
                PlaylistModified?.Invoke();
                InvalidateVisual();
            }
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2 && hit != null && hit.Value.clip.Type == ClipType.Pattern && hit.Value.clip.PatternId != null)
            {
                PatternClipDoubleClicked?.Invoke(hit.Value.clip.PatternId);
                return;
            }

            if (hit != null)
            {
                _dragClip = hit.Value.clip;
                if (hit.Value.autoPtIdx >= 0)
                {
                    _dragMode = DragMode.AutoPoint;
                    _dragAutoIndex = hit.Value.autoPtIdx;
                    return;
                }
                if (hit.Value.clip.Type == ClipType.Automation && !hit.Value.inHeader && !hit.Value.onRightEdge)
                {
                    double cx = TrackHeaderW + _dragClip.StartStep * _stepWidth - _scrollX;
                    double cw = _dragClip.LengthSteps * _stepWidth;
                    double cy = RulerH + _dragClip.TrackIndex * TrackH - _scrollY + 2;
                    double ch = TrackH - 4;
                    var np = new AutomationPoint
                    {
                        X = Math.Clamp((pos.X - cx) / cw, 0, 1),
                        Y = Math.Clamp(1.0 - (pos.Y - cy - 16) / (ch - 20), 0, 1)
                    };
                    _dragClip.Points.Add(np);
                    _dragClip.Points.Sort((a, b) => a.X.CompareTo(b.X));
                    _dragAutoIndex = _dragClip.Points.IndexOf(np);
                    _dragMode = DragMode.AutoPoint;
                    InvalidateVisual();
                    return;
                }

                if (hit.Value.onRightEdge)
                {
                    _dragMode = DragMode.ResizeClip;
                }
                else
                {
                    _dragMode = DragMode.MoveClip;
                    _dragOffsetStep = step - _dragClip.StartStep;
                }
                return;
            }

            // Place Active Pattern Clip
            var pat = _project.GetActivePattern();
            if (pat == null) return;
            int snapped = Math.Max(0, (int)Math.Floor(step / 4.0) * 4);
            var created = new PlaylistClipModel
            {
                Type = ClipType.Pattern,
                PatternId = pat.Id,
                Name = pat.Name,
                ColorHex = pat.ColorHex,
                TrackIndex = track,
                StartStep = snapped,
                LengthSteps = Math.Max(16, pat.LengthSteps)
            };
            _project.Playlist.Add(created);
            _dragClip = created;
            _dragMode = DragMode.MoveClip;
            _dragOffsetStep = 0;
            PlaylistModified?.Invoke();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMode == DragMode.None || _dragClip == null) return;
        var pos = e.GetPosition(this);
        double step = (pos.X - TrackHeaderW + _scrollX) / _stepWidth;
        int track = Math.Clamp((int)Math.Floor((pos.Y - RulerH + _scrollY) / TrackH), 0, NumTracks - 1);

        if (_dragMode == DragMode.MoveClip)
        {
            _dragClip.StartStep = Math.Max(0, (int)Math.Round((step - _dragOffsetStep) / 4.0) * 4);
            _dragClip.TrackIndex = track;
            InvalidateVisual();
        }
        else if (_dragMode == DragMode.ResizeClip)
        {
            int end = (int)Math.Round(step / 4.0) * 4;
            _dragClip.LengthSteps = Math.Max(4, end - _dragClip.StartStep);
            InvalidateVisual();
        }
        else if (_dragMode == DragMode.AutoPoint && _dragAutoIndex >= 0 && _dragAutoIndex < _dragClip.Points.Count)
        {
            double cx = TrackHeaderW + _dragClip.StartStep * _stepWidth - _scrollX;
            double cw = _dragClip.LengthSteps * _stepWidth;
            double cy = RulerH + _dragClip.TrackIndex * TrackH - _scrollY + 2;
            double ch = TrackH - 4;
            var pt = _dragClip.Points[_dragAutoIndex];
            pt.X = Math.Clamp((pos.X - cx) / cw, 0, 1);
            pt.Y = Math.Clamp(1.0 - (pos.Y - cy - 16) / (ch - 20), 0, 1);
            _dragClip.Points.Sort((a, b) => a.X.CompareTo(b.X));
            _dragAutoIndex = _dragClip.Points.IndexOf(pt);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            _dragClip = null;
            _dragAutoIndex = -1;
            ReleaseMouseCapture();
            PlaylistModified?.Invoke();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;

        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
        if (_project == null) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 1. Main Track Area
        dc.PushClip(new RectangleGeometry(new Rect(TrackHeaderW, RulerH, w - TrackHeaderW, h - RulerH)));
        for (int t = 0; t < NumTracks; t++)
        {
            double y = RulerH + t * TrackH - _scrollY;
            if (y + TrackH < RulerH || y > h) continue;
            dc.DrawRectangle((t & 1) == 0 ? TrackEven : TrackOdd, null, new Rect(TrackHeaderW, y, w - TrackHeaderW, TrackH));
            dc.DrawRectangle(TrackSep, null, new Rect(TrackHeaderW, y + TrackH - 1, w - TrackHeaderW, 1));
        }

        int firstStep = Math.Max(0, (int)Math.Floor(_scrollX / _stepWidth));
        int lastStep = (int)Math.Ceiling((_scrollX + (w - TrackHeaderW)) / _stepWidth) + 16;
        for (int s = firstStep - (firstStep % 4); s <= lastStep; s += 4)
        {
            double x = TrackHeaderW + s * _stepWidth - _scrollX;
            dc.DrawRectangle((s % 16 == 0) ? LineBar : LineBeat, null, new Rect(x, RulerH, (s % 16 == 0) ? 1.5 : 1, h - RulerH));
        }

        // Clips
        foreach (var clip in _project.Playlist)
        {
            double cx = TrackHeaderW + clip.StartStep * _stepWidth - _scrollX;
            double cw = Math.Max(14, clip.LengthSteps * _stepWidth - 1);
            double cy = RulerH + clip.TrackIndex * TrackH - _scrollY + 2;
            double ch = TrackH - 5;
            if (cx + cw < TrackHeaderW || cx > w || cy + ch < RulerH || cy > h) continue;

            string title = clip.Name;
            string hex = clip.ColorHex;
            PatternModel? pat = null;
            if (clip.Type == ClipType.Pattern && clip.PatternId != null)
            {
                pat = _project.Patterns.FirstOrDefault(p => p.Id == clip.PatternId);
                if (pat != null) { title = pat.Name; hex = pat.ColorHex; }
            }

            var accentColor = (Color)ColorConverter.ConvertFromString(hex);
            var accentBrush = new SolidColorBrush(accentColor); accentBrush.Freeze();
            var borderPen = new Pen(accentBrush, 1.2); borderPen.Freeze();

            dc.DrawRoundedRectangle(ClipBody, borderPen, new Rect(cx, cy, cw, ch), 3, 3);
            dc.DrawRectangle(accentBrush, null, new Rect(cx, cy, cw, 15));

            var ft = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextDark, dpi)
            {
                MaxTextWidth = Math.Max(10, cw - 6),
                Trimming = TextTrimming.CharacterEllipsis
            };
            dc.DrawText(ft, new Point(cx + 4, cy + 1));

            if (clip.Type == ClipType.Pattern && pat != null)
            {
                var dimBrush = new SolidColorBrush(Color.FromArgb(165, accentColor.R, accentColor.G, accentColor.B));
                dimBrush.Freeze();
                int patLen = Math.Max(16, pat.LengthSteps);
                int reps = (int)Math.Ceiling(clip.LengthSteps / (double)patLen);
                for (int rep = 0; rep < reps; rep++)
                {
                    int off = rep * patLen;
                    for (int ci = 0; ci < _project.Channels.Count; ci++)
                    {
                        var chan = _project.Channels[ci];
                        if (!pat.Channels.TryGetValue(chan.Id, out var cd)) continue;
                        double rowY = cy + 19 + (ci % 7) * 4;
                        for (int si = 0; si < Math.Min(patLen, cd.Steps.Length); si++)
                        {
                            if (!cd.Steps[si] || off + si >= clip.LengthSteps) continue;
                            dc.DrawRectangle(dimBrush, null, new Rect(cx + (off + si) * _stepWidth, rowY, Math.Max(2, _stepWidth * 0.7), 2.5));
                        }
                        foreach (var n in cd.Notes)
                        {
                            if (off + n.StartStep >= clip.LengthSteps) continue;
                            dc.DrawRectangle(dimBrush, null, new Rect(cx + (off + n.StartStep) * _stepWidth, rowY, Math.Max(2, n.DurationSteps * _stepWidth * 0.9), 2.5));
                        }
                    }
                }
            }
            else if (clip.Type == ClipType.Audio && clip.SampleId != null && _project.Samples.TryGetValue(clip.SampleId, out var smp))
            {
                double midY = cy + 16 + (ch - 18) * 0.5;
                double amp = (ch - 20) * 0.5;
                for (int i = 0; i < smp.Peaks.Length; i++)
                {
                    double px = cx + (i / (double)smp.Peaks.Length) * cw;
                    double barH = Math.Max(1, smp.Peaks[i] * amp);
                    dc.DrawRectangle(WaveBrush, null, new Rect(px, midY - barH, Math.Max(1, cw / smp.Peaks.Length), barH * 2));
                }
            }
            else if (clip.Type == ClipType.Automation && clip.Points.Count >= 2)
            {
                var geo = new StreamGeometry();
                using (var gctx = geo.Open())
                {
                    for (int i = 0; i < clip.Points.Count; i++)
                    {
                        double px = cx + clip.Points[i].X * cw;
                        double py = cy + 16 + (1.0 - clip.Points[i].Y) * (ch - 20);
                        if (i == 0) gctx.BeginFigure(new Point(px, py), true, false);
                        else gctx.LineTo(new Point(px, py), true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, AutoPen, geo);

                foreach (var pt in clip.Points)
                {
                    double px = cx + pt.X * cw;
                    double py = cy + 16 + (1.0 - pt.Y) * (ch - 20);
                    dc.DrawEllipse(TextLight, NodePen, new Point(px, py), 4, 4);
                }
            }
        }

        // Playhead
        if (_project.Mode == PlaybackMode.Song)
        {
            double px = TrackHeaderW + _playheadStep * _stepWidth - _scrollX;
            if (px >= TrackHeaderW && px <= w)
                dc.DrawRectangle(PlayheadBrush, null, new Rect(px, RulerH, 2, h - RulerH));
        }
        dc.Pop();

        // 2. Left Track Headers
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerH, TrackHeaderW, h - RulerH)));
        for (int t = 0; t < NumTracks; t++)
        {
            double y = RulerH + t * TrackH - _scrollY;
            dc.DrawRectangle(HeaderBg, null, new Rect(0, y, TrackHeaderW - 1, TrackH - 1));
            dc.DrawRectangle(TrackSep, null, new Rect(0, y + TrackH - 1, TrackHeaderW, 1));
            var ft1 = new FormattedText($"Pista {t + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 11, TextLight, dpi);
            dc.DrawText(ft1, new Point(12, y + 10));
            string sub = t == 3 ? "Automatización" : t == 4 ? "Audio WAV" : "Patrones";
            var ft2 = new FormattedText(sub, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextMuted, dpi);
            dc.DrawText(ft2, new Point(12, y + 28));
        }
        dc.Pop();

        // 3. Top Ruler
        dc.DrawRectangle(RulerBg, null, new Rect(0, 0, w, RulerH));
        dc.DrawRectangle(LineBar, null, new Rect(0, RulerH - 1, w, 1));
        var rLbl = new FormattedText("CANCIÓN", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextMuted, dpi);
        dc.DrawText(rLbl, new Point(12, 6));

        for (int s = firstStep - (firstStep % 16); s <= lastStep; s += 16)
        {
            double x = TrackHeaderW + s * _stepWidth - _scrollX;
            if (x < TrackHeaderW - 8) continue;
            var ft = new FormattedText($"Compás {s / 16 + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiFont, 10, TextLight, dpi);
            dc.DrawText(ft, new Point(x + 4, 6));
        }
    }
}

internal static class MathExtensions
{
    public static double hypot(double a, double b) => Math.Sqrt(a * a + b * b);
}
