using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpotLay.Core.Localization;
using SpotLay.Core.Sessions;

namespace SpotLay.App.Views;

/// <summary>
/// Panel de hardware, debajo del de FPS y con el mismo eje de tiempo: temperaturas,
/// consumo, reloj, uso y memoria en tonos azules. Dos escalas: la unidad de la
/// primera curva marcada a la izquierda y la otra unidad a la derecha. El valor
/// exacto de cada curva sale al pasar el ratón.
/// </summary>
public sealed class HardwareChart : FrameworkElement
{
    private static readonly CultureInfo Es = Tr.Culture;

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(SessionRecord), typeof(HardwareChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<HardwareSeries>), typeof(HardwareChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverIndexProperty = SessionChart.HoverIndexProperty.AddOwner(typeof(HardwareChart),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public SessionRecord? Session { get => (SessionRecord?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public IReadOnlyList<HardwareSeries>? Series { get => (IReadOnlyList<HardwareSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public int HoverIndex { get => (int)GetValue(HoverIndexProperty); set => SetValue(HoverIndexProperty, value); }

    private const double Bottom = 26;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // recibe el ratón

        var s = Session;
        if (s is null || s.Samples.Count < 2) return;
        var plot = new Rect(SessionChart.PlotLeft, SessionChart.PlotTop, w - SessionChart.PlotLeft - SessionChart.PlotRight, h - SessionChart.PlotTop - Bottom);
        var samples = s.Samples;
        var duration = Math.Max(1, samples[^1].T);
        double X(int t) => plot.Left + t / (double)duration * plot.Width;

        // Eje de tiempo (solo en este panel, el de abajo).
        var step = SessionChart.TimeStep(duration);
        for (var t = 0; t <= duration; t += step)
        {
            dc.DrawLine(SessionChart.GridPen, new Point(X(t), plot.Top), new Point(X(t), plot.Bottom));
            var ft = SessionChart.Format(SessionChart.Clock(t), SessionChart.LabelBrush, 10, dpi);
            dc.DrawText(ft, new Point(X(t) - ft.Width / 2, plot.Bottom + 6));
        }

        var series = (Series ?? []).Where(x => x.HasData(s)).ToList();
        if (series.Count == 0)
        {
            for (var i = 0; i <= 4; i++)
                dc.DrawLine(SessionChart.GridPen, new Point(plot.Left, plot.Bottom - i / 4.0 * plot.Height), new Point(plot.Right, plot.Bottom - i / 4.0 * plot.Height));
            SessionChart.Text(dc, Tr.T("Marca abajo lo que quieres ver: temperaturas, consumo, reloj, uso o memoria."), new Point(plot.Left + 12, plot.Top + plot.Height / 2 - 8), SessionChart.LabelBrush, 13, dpi);
            if (HoverIndex >= 0 && HoverIndex < samples.Count)
                dc.DrawLine(SessionChart.HoverPen, new Point(X(samples[HoverIndex].T), 0), new Point(X(samples[HoverIndex].T), plot.Bottom));
            return;
        }

        // Dos escalas como mucho: izquierda = unidad de la primera curva, derecha = la otra.
        var leftUnit = series[0].Unit;
        var rightUnit = series.Select(x => x.Unit).FirstOrDefault(u => u != leftUnit);
        series = series.Where(x => x.Unit == leftUnit || x.Unit == rightUnit).ToList();
        var leftMax = series.Where(x => x.Unit == leftUnit).Max(x => x.ScaleMax(s));
        var rightMax = rightUnit is null ? 0 : series.Where(x => x.Unit == rightUnit).Max(x => x.ScaleMax(s));
        double MaxOf(HardwareSeries x) => x.Unit == leftUnit ? leftMax : rightMax;
        double Y(double v, double max) => plot.Bottom - Math.Clamp(v / max, 0, 1) * plot.Height;

        SessionChart.Header(dc, series.Select(x => (x.Label, x.Color)), plot.Left, dpi);
        var leftBrush = new SolidColorBrush(series.First(x => x.Unit == leftUnit).Color);
        var rightBrush = rightUnit is null ? null : new SolidColorBrush(series.First(x => x.Unit == rightUnit).Color);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - i / 4.0 * plot.Height;
            dc.DrawLine(SessionChart.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            SessionChart.AxisNumber(dc, Number(leftMax * i / 4, leftMax), plot.Left - 6, y, true, dpi);
            if (rightUnit is not null) SessionChart.AxisNumber(dc, Number(rightMax * i / 4, rightMax), plot.Right + 6, y, false, dpi);
        }
        SessionChart.AxisTitle(dc, AxisName(leftUnit), 12, plot.Top + plot.Height / 2, dpi, leftBrush);
        if (rightUnit is not null) SessionChart.AxisTitle(dc, AxisName(rightUnit), w - 12, plot.Top + plot.Height / 2, dpi, rightBrush);

        // Degradado solo bajo la curva principal (con varias, todo relleno sería una mancha).
        var main = series[0];
        dc.DrawGeometry(SessionChart.Fade(main.Color), null, SessionChart.Area(samples, main.Value, X, v => Y(v, MaxOf(main)), plot.Bottom));
        foreach (var serie in series)
        {
            var max = MaxOf(serie);
            var pen = new Pen(new SolidColorBrush(serie.Color), serie == main ? 1.8 : 1.6) { LineJoin = PenLineJoin.Round };
            if (serie.Dashed) pen.DashStyle = new DashStyle([4, 2], 0);
            // Sin lectura en un segundo: la línea se corta, no se inventa el valor.
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var open = false;
                foreach (var p in samples)
                {
                    if (serie.Value(p) is not { } v) { open = false; continue; }
                    var pt = new Point(X(p.T), Y(v, max));
                    if (!open) { ctx.BeginFigure(pt, false, false); open = true; }
                    else ctx.LineTo(pt, true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        var hover = HoverIndex;
        if (hover >= 0 && hover < samples.Count)
        {
            var p = samples[hover];
            var x = X(p.T);
            dc.DrawLine(SessionChart.HoverPen, new Point(x, 0), new Point(x, plot.Bottom));
            var lines = new List<(string Text, Color Color)> { (SessionChart.Clock(p.T), Colors.White) };
            foreach (var serie in series)
            {
                var v = serie.Value(p);
                if (v is { } value) dc.DrawEllipse(new SolidColorBrush(serie.Color), null, new Point(x, Y(value, MaxOf(serie))), 3.5, 3.5);
                lines.Add(($"{serie.Label}  {(v is { } val ? HardwareSeries.Format(val, serie.Unit) : "—")}", serie.Color));
            }
            var texts = lines.Select(l => SessionChart.Format(l.Text, new SolidColorBrush(l.Color), 11.5, dpi)).ToList();
            var bw = texts.Max(t => t.Width) + 16;
            var bh = texts.Sum(t => t.Height) + 8;
            var bx = x + 10 + bw > plot.Right ? x - 10 - bw : x + 10;
            bx = Math.Max(plot.Left, bx);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x0B, 0x0D, 0x12)), new Pen(new SolidColorBrush(main.Color), 1),
                new Rect(bx, plot.Top + 4, bw, bh), 5, 5);
            var ty = plot.Top + 8;
            foreach (var t in texts)
            {
                dc.DrawText(t, new Point(bx + 8, ty));
                ty += t.Height;
            }
        }
    }

    private static string Number(double v, double max) => v.ToString(max < 10 && v % 1 != 0 ? "F1" : "F0", Es);

    private static string AxisName(string unit) => unit switch
    {
        "°C" => Tr.T("Temperatura (°C)"),
        "W" => Tr.T("Consumo (W)"),
        "MHz" => Tr.T("Reloj (MHz)"),
        "%" => Tr.T("Uso (%)"),
        "GB" => Tr.T("Memoria (GB)"),
        _ => unit
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = SessionChart.IndexAt(Session, e.GetPosition(this).X, ActualWidth);
        if (index != HoverIndex) SetCurrentValue(HoverIndexProperty, index);
    }

    protected override void OnMouseLeave(MouseEventArgs e) => SetCurrentValue(HoverIndexProperty, -1);
}
