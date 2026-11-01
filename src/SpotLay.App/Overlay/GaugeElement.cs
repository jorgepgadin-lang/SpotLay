using System.Windows;
using System.Windows.Media;
using SpotLay.Core.Config;

namespace SpotLay.App.Overlay;

/// <summary>Dibuja el grafico de una metrica: arco, anillo, barra o linea temporal.</summary>
public sealed class GaugeElement : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = Register(nameof(Fraction), 0.0);
    public static readonly DependencyProperty ModeProperty = Register(nameof(Mode), Visualizations.Gauge);
    public static readonly DependencyProperty FillProperty = Register<Brush>(nameof(Fill), Brushes.White);
    public static readonly DependencyProperty TrackProperty = Register<Brush>(nameof(Track), Brushes.Gray);
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 4.0);
    public static readonly DependencyProperty HistoryProperty = Register<IReadOnlyList<double>?>(nameof(History), null);

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public IReadOnlyList<double>? History { get => (IReadOnlyList<double>?)GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(GaugeElement),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var t = Math.Max(1, Thickness);
        var f = Math.Clamp(double.IsFinite(Fraction) ? Fraction : 0, 0, 1);
        var track = new Pen(Track, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fill = new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        track.Freeze();
        fill.Freeze();

        switch (Mode)
        {
            case Visualizations.Gauge:
            {
                // Arco de 270 grados abierto por abajo.
                var r = Math.Max(1, Math.Min((w - t) / 2, (h - t) / 1.72));
                var center = new Point(w / 2, t / 2 + r);
                dc.DrawGeometry(null, track, Arc(center, r, 135, 270));
                if (f > 0.001) dc.DrawGeometry(null, fill, Arc(center, r, 135, 270 * f));
                break;
            }
            case Visualizations.Ring:
            {
                var r = Math.Max(1, (Math.Min(w, h) - t) / 2);
                var center = new Point(w / 2, h / 2);
                dc.DrawEllipse(null, track, center, r, r);
                if (f > 0.001) dc.DrawGeometry(null, fill, Arc(center, r, -90, Math.Min(359.9, 360 * f)));
                break;
            }
            case Visualizations.BarHorizontal:
            {
                var y = h / 2;
                dc.DrawLine(track, new Point(t / 2, y), new Point(w - t / 2, y));
                if (f > 0.001) dc.DrawLine(fill, new Point(t / 2, y), new Point(t / 2 + (w - t) * f, y));
                break;
            }
            case Visualizations.BarVertical:
            {
                var x = w / 2;
                dc.DrawLine(track, new Point(x, t / 2), new Point(x, h - t / 2));
                if (f > 0.001) dc.DrawLine(fill, new Point(x, h - t / 2), new Point(x, h - t / 2 - (h - t) * f));
                break;
            }
            case Visualizations.Line:
                DrawLine(dc, fill, w, h, t);
                break;
        }
    }

    private void DrawLine(DrawingContext dc, Pen pen, double w, double h, double t)
    {
        var history = History;
        if (history is not { Count: >= 2 }) return;
        var min = history.Min();
        var max = history.Max();
        var span = Math.Max(1e-6, max - min);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < history.Count; i++)
            {
                var x = t / 2 + (w - t) * i / (history.Count - 1);
                var y = h - t / 2 - (h - t) * (history[i] - min) / span;
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static StreamGeometry Arc(Point center, double r, double startDeg, double sweepDeg)
    {
        static Point On(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(On(center, r, startDeg), false, false);
            ctx.ArcTo(On(center, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}
