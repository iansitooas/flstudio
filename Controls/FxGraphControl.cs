using System.Windows;
using System.Windows.Media;
using FLLiteStudio.Core;

namespace FLLiteStudio.Controls;

public sealed class FxGraphControl : FrameworkElement
{
    private FxSlotModel? _fx;

    private static readonly SolidColorBrush Bg = Freeze(new SolidColorBrush(Color.FromRgb(18, 22, 28)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(42, 50, 62)), 1));
    private static readonly Pen EqPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(181, 100, 232)), 2));
    private static readonly Pen GbPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(232, 131, 42)), 2));
    private static readonly SolidColorBrush DotBrush = Freeze(new SolidColorBrush(Color.FromRgb(240, 245, 252)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public void BindFx(FxSlotModel? fx)
    {
        _fx = fx;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;

        dc.DrawRoundedRectangle(Bg, GridPen, new Rect(0, 0, w, h), 4, 4);
        dc.DrawLine(GridPen, new Point(0, h * 0.5), new Point(w, h * 0.5));
        if (_fx == null) return;

        if (_fx.Type == FxType.ParametricEq2)
        {
            (double freq, double db, int kind)[] bands =
            {
                (80, _fx.EqLowDb, 0),
                (320, _fx.EqLowMidDb, 1),
                (1200, _fx.EqMidDb, 1),
                (4200, _fx.EqHighMidDb, 1),
                (10500, _fx.EqHighDb, 2)
            };

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int x = 0; x <= (int)w; x += 3)
                {
                    double freq = 20.0 * Math.Pow(1000.0, x / w);
                    double totalDb = 0;
                    foreach (var (bf, bdb, kind) in bands)
                    {
                        double oct = Math.Log2(freq / bf);
                        totalDb += kind == 0
                            ? bdb / (1.0 + Math.Exp(oct * 2.2))
                            : kind == 2
                                ? bdb / (1.0 + Math.Exp(-oct * 2.2))
                                : bdb * Math.Exp(-(oct * oct) * 1.8);
                    }
                    double y = h * 0.5 - Math.Clamp(totalDb, -16, 16) * (h / 36.0);
                    if (x == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, EqPen, geo);

            foreach (var (bf, bdb, _) in bands)
            {
                double bx = (Math.Log10(bf / 20.0) / 3.0) * w;
                double by = h * 0.5 - Math.Clamp(bdb, -16, 16) * (h / 36.0);
                dc.DrawEllipse(DotBrush, EqPen, new Point(bx, by), 4, 4);
            }
        }
        else if (_fx.Type == FxType.GrossBeat)
        {
            int idx = Math.Clamp(_fx.GrossBeatPreset, 0, MusicTheory.GrossBeatPresets.Length - 1);
            var curve = MusicTheory.GrossBeatPresets[idx].Curve16;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < curve.Length; i++)
                {
                    double eff = 1.0 - _fx.Mix * (1.0 - curve[i]);
                    double x = (i / (double)(curve.Length - 1)) * (w - 8) + 4;
                    double y = h - 8 - eff * (h - 16);
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, GbPen, geo);
        }
    }
}
