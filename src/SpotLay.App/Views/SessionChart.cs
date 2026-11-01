using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpotLay.Core.Sessions;
using SpotLay.Core.Localization;

namespace SpotLay.App.Views;

/// <summary>
/// Gráfica de una partida entera: FPS (naranja, con degradado) y FPS reales
/// (amarillo), tirones marcados en rojo y, al pasar el ratón, los FPS y el peor
/// frame de ese segundo. El segundo señalado se comparte con la gráfica de hardware.
/// </summary>
public sealed class SessionChart : FrameworkElement
{
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x00);
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
    private static readonly Color Spike = Color.FromRgb(0xFF, 0x4D, 0x2E);
    private static readonly CultureInfo Es = Tr.Culture;

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(SessionRecord), typeof(SessionChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Segundo señalado con el ratón (índice de muestra, -1 = ninguno), compartido entre gráficas.</summary>
    public static readonly DependencyProperty HoverIndexProperty = DependencyProperty.Register(
        nameof(HoverIndex), typeof(int), typeof(SessionChart),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public SessionRecord? Session { get => (SessionRecord?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public int HoverIndex { get => (int)GetValue(HoverIndexProperty); set => SetValue(HoverIndexProperty, value); }

    // Márgenes iguales en los dos paneles: el mismo segundo cae en la misma x. A la
    // derecha queda sitio para la segunda escala del panel de hardware.
    public const double PlotLeft = 58, PlotRight = 58, PlotTop = 26;
    private const double Left = PlotLeft, Right = PlotRight, Top = PlotTop, Bottom = 8;

    /// <summary>Rejilla punteada, estilo telemetría.</summary>
    public static Pen GridPen { get; } = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([1, 3], 0) });
    public static Brush LabelBrush { get; } = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
    public static Pen HoverPen { get; } = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), 1));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // recibe el ratón

        var s = Session;
        if (s is null || s.Samples.Count < 2)
        {
            Text(dc, s is null ? Tr.T("Juega al menos 1 minuto y aquí verás cómo fue la partida.") : Tr.T("Cargando…"), new Point(16, h / 2 - 8), LabelBrush, 13, dpi);
            return;
        }

        var plot = new Rect(Left, Top, w - Left - Right, h - Top - Bottom);
        var samples = s.Samples;
        var duration = Math.Max(1, samples[^1].T);
        var max = HardwareSeries.NiceMax(samples.Max(x => Math.Max(x.Fps, x.FpsReal)) * 1.1);
        double X(int t) => plot.Left + t / (double)duration * plot.Width;
        double Y(double fps) => plot.Bottom - Math.Clamp(fps / max, 0, 1) * plot.Height;
        var hasReal = samples.Any(x => x.FpsReal > 0);

        // Cabecera con la leyenda en los colores de cada línea.
        var header = new List<(string, Color)> { (Tr.T("FPS a pantalla"), Orange) };
        if (hasReal) header.Add((Tr.T("FPS reales"), Yellow));
        if (s.Spikes.Count > 0) header.Add((Tr.T("Tirón"), Spike));
        Header(dc, header, plot.Left, dpi);

        for (var i = 0; i <= 4; i++)
        {
            var fps = max * i / 4;
            var y = Y(fps);
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            AxisNumber(dc, fps.ToString("F0", Es), plot.Left - 6, y, true, dpi);
        }
        AxisTitle(dc, Tr.T("FPS"), 12, plot.Top + plot.Height / 2, dpi, new SolidColorBrush(Orange));
        var step = TimeStep(duration);
        for (var t = 0; t <= duration; t += step)
            dc.DrawLine(GridPen, new Point(X(t), plot.Top), new Point(X(t), plot.Bottom));

        // Tirones: franjas rojas finas.
        var spike = new SolidColorBrush(Color.FromArgb(0x66, Spike.R, Spike.G, Spike.B));
        foreach (var sp in s.Spikes)
            dc.DrawRectangle(spike, null, new Rect(X(sp.T) - 1, plot.Top, 2, plot.Height));

        // FPS a pantalla: área con degradado + línea.
        dc.DrawGeometry(Fade(Orange), null, Area(samples, x => x.Fps, X, Y, plot.Bottom));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Orange), 1.8) { LineJoin = PenLineJoin.Round }, Line(samples, x => x.Fps, X, Y));

        if (hasReal)
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Yellow), 1.4) { LineJoin = PenLineJoin.Round, DashStyle = new DashStyle([4, 2], 0) },
                Line(samples, x => x.FpsReal, X, Y));

        // Ratón: línea vertical y datos de ese segundo.
        var hover = HoverIndex;
        if (hover >= 0 && hover < samples.Count)
        {
            var p = samples[hover];
            var x = X(p.T);
            dc.DrawLine(HoverPen, new Point(x, plot.Top), new Point(x, h));
            dc.DrawEllipse(new SolidColorBrush(Orange), null, new Point(x, Y(p.Fps)), 4, 4);
            var info = Tr.F("{0}   FPS {1:F0}", Clock(p.T), p.Fps) + (p.FpsReal > 0 ? Tr.F(" · reales {0:F0}", p.FpsReal) : "") +
                       Tr.F("   peor frame {0:F1} ms", p.MaxFrametime);
            var ft = Format(info, Brushes.White, 11.5, dpi);
            var bx = Math.Clamp(x + 10, plot.Left, Math.Max(plot.Left, plot.Right - ft.Width - 16));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x0B, 0x0D, 0x12)), new Pen(new SolidColorBrush(Orange), 1),
                new Rect(bx, plot.Top + 4, ft.Width + 16, ft.Height + 8), 5, 5);
            dc.DrawText(ft, new Point(bx + 8, plot.Top + 8));
        }
    }

    /// <summary>Degradado del color hacia abajo, igual en los dos paneles.</summary>
    public static Brush Fade(Color c) => new LinearGradientBrush(Color.FromArgb(0x55, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);

    public static StreamGeometry Area(List<SessionSample> samples, Func<SessionSample, float?> value, Func<int, double> x, Func<double, double> y, double bottom)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var open = false;
            double lastX = 0;
            foreach (var p in samples)
            {
                if (value(p) is not { } v)
                {
                    if (open) { ctx.LineTo(new Point(lastX, bottom), false, false); open = false; }
                    continue;
                }
                lastX = x(p.T);
                if (!open) { ctx.BeginFigure(new Point(lastX, bottom), true, true); open = true; }
                ctx.LineTo(new Point(lastX, y(v)), true, true);
            }
            if (open) ctx.LineTo(new Point(lastX, bottom), false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Leyenda arriba del panel: cada nombre en el color de su línea.</summary>
    public static void Header(DrawingContext dc, IEnumerable<(string Text, Color Color)> items, double x, double dpi)
    {
        foreach (var (text, color) in items)
        {
            var brush = new SolidColorBrush(color);
            dc.DrawRectangle(brush, null, new Rect(x, 11, 12, 3));
            var ft = Format(text, brush, 11.5, dpi);
            dc.DrawText(ft, new Point(x + 17, 12.5 - ft.Height / 2));
            x += 17 + ft.Width + 18;
        }
    }

    /// <summary>Número de un eje, alineado al borde del gráfico (a su derecha o a su izquierda).</summary>
    public static void AxisNumber(DrawingContext dc, string text, double x, double y, bool alignRight, double dpi, Brush? brush = null)
    {
        var ft = Format(text, brush ?? LabelBrush, 10, dpi);
        dc.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y - ft.Height / 2));
    }

    /// <summary>Título de un eje, en vertical y centrado en cy.</summary>
    public static void AxisTitle(DrawingContext dc, string text, double cx, double cy, double dpi, Brush? brush = null)
    {
        var ft = Format(text, brush ?? LabelBrush, 11, dpi);
        dc.PushTransform(new RotateTransform(-90, cx, cy));
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        dc.Pop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(Session, e.GetPosition(this).X, ActualWidth);
        if (index != HoverIndex) SetCurrentValue(HoverIndexProperty, index);
    }

    protected override void OnMouseLeave(MouseEventArgs e) => SetCurrentValue(HoverIndexProperty, -1);

    /// <summary>Muestra más cercana a esa x (-1 sin partida).</summary>
    public static int IndexAt(SessionRecord? s, double mouseX, double width)
    {
        if (s is null || s.Samples.Count < 2) return -1;
        var t = (mouseX - Left) / (width - Left - Right) * s.Samples[^1].T;
        var index = 0;
        var best = double.MaxValue;
        for (var i = 0; i < s.Samples.Count; i++)
        {
            var d = Math.Abs(s.Samples[i].T - t);
            if (d < best) { best = d; index = i; }
        }
        return index;
    }

    private static StreamGeometry Line(List<SessionSample> samples, Func<SessionSample, float> value, Func<int, double> x, Func<double, double> y)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x(samples[0].T), y(value(samples[0]))), false, false);
            for (var i = 1; i < samples.Count; i++) ctx.LineTo(new Point(x(samples[i].T), y(value(samples[i]))), true, true);
        }
        g.Freeze();
        return g;
    }

    public static int TimeStep(int duration) => duration switch
    {
        <= 300 => 60, <= 1200 => 300, <= 3600 => 600, <= 7200 => 1200, _ => 3600
    };

    public static string Clock(int seconds) => seconds >= 3600
        ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
        : $"{seconds / 60}:{seconds % 60:00}";

    public static FormattedText Format(string s, Brush brush, double size, double dpi) =>
        new(s, Es, FlowDirection.LeftToRight, new Typeface(new FontFamily("Cascadia Mono, Consolas, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, dpi);

    public static void Text(DrawingContext dc, string s, Point at, Brush brush, double size, double dpi) =>
        dc.DrawText(Format(s, brush, size, dpi), at);
}
