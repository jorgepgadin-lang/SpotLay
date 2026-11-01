using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SpotLay.App.Infrastructure;
using SpotLay.Core.Config;
using SpotLay.Core.FrameGen;
using SpotLay.Core.Localization;

namespace SpotLay.App.Overlay;

/// <summary>
/// Gráfica de frametime frame a frame (Mayús + F9): ventana propia, independiente
/// del overlay, a lo ancho del monitor. Dos líneas: frames a pantalla (PresentMon,
/// tiempo entre cambios de pantalla) y frames reales (marcadores Reflex).
/// </summary>
public sealed class FrameGraphWindow : Window
{
    private readonly FrameGraphView _view = new();
    private readonly DispatcherTimer _topmost;
    private IntPtr _hwnd;

    public FrameGraphWindow()
    {
        ThemeMode = ThemeMode.None;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "SpotLay Frametime";
        Content = _view;
        _topmost = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            if (_hwnd != IntPtr.Zero && IsVisible)
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }, Dispatcher);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExLayered;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
        _topmost.Start();
    }

    public void Update(FrameTimeStats stats, bool hasGame, FrameGraphConfig config, IntPtr monitor)
    {
        _view.Stats = stats;
        _view.HasGame = hasGame;
        _view.BackgroundOpacity = config.Opacity / 100.0;
        _view.InvalidateVisual();
        if (!IsVisible) Show();
        Place(config, monitor);
    }

    private void Place(FrameGraphConfig config, IntPtr monitor)
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = OverlayWindow.ScreenOf(monitor);
        var w = (int)Math.Round(screen.Width * config.WidthPercent / 100.0);
        var h = (int)Math.Round(config.Height * dpi.DpiScaleY);
        // Tamaño en DIP para WPF; posición en píxeles reales.
        Width = w / dpi.DpiScaleX;
        Height = h / dpi.DpiScaleY;
        var x = screen.Left + (screen.Width - w) / 2;
        var y = config.Position == "top" ? screen.Top : screen.Top + screen.Height - h;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, x, y, w, h, NativeMethods.SwpNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmost.Stop();
        base.OnClosed(e);
    }
}

public sealed class FrameGraphView : FrameworkElement
{
    private static readonly Brush DisplayedBrush = Frozen(Color.FromRgb(0xFF, 0x8A, 0x00));
    private static readonly Brush RealBrush = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TextBrush = Frozen(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
    private double _scaleMax = 33.4;

    public FrameTimeStats Stats { get; set; } = FrameTimeStats.Empty;
    public bool HasGame { get; set; }
    public double BackgroundOpacity { get; set; } = 0.8;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * BackgroundOpacity), 0x0B, 0x0D, 0x12)), null, new Rect(0, 0, w, h));

        const double left = 44, right = 8, top = 22, bottom = 6;
        var plot = new Rect(left, top, Math.Max(10, w - left - right), Math.Max(10, h - top - bottom));
        var stats = Stats;

        if (!HasGame || (stats.DisplayedMs.Length < 2 && stats.RealMs.Length < 2))
        {
            Text(dc, HasGame ? Tr.T("Frametime: esperando frames…") : Tr.T("Frametime: sin juego en primer plano"), new Point(left, 4), TextBrush, 12, dpi);
            return;
        }

        // Escala en ms: se adapta a los picos, pero sin saltar en cada lectura.
        var peak = Math.Max(Max(stats.DisplayedMs), Max(stats.RealMs));
        var target = Nice(Math.Max(16.7, peak * 1.15));
        _scaleMax = target > _scaleMax ? target : _scaleMax * 0.9 + target * 0.1;

        foreach (var ms in new[] { 8.3, 16.7, 33.3, 50, 100, 200 })
        {
            if (ms > _scaleMax) break;
            var y = plot.Bottom - ms / _scaleMax * plot.Height;
            dc.DrawLine(new Pen(GridBrush, 1) { DashStyle = DashStyles.Dot }, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(dc, ms.ToString("0.#", Tr.Culture) + " ms", new Point(2, y - 7), GridBrush, 10, dpi);
        }

        Series(dc, stats.RealMs, plot, stats.WindowSeconds, RealBrush);
        Series(dc, stats.DisplayedMs, plot, stats.WindowSeconds, DisplayedBrush);

        // Leyenda y valores.
        var legend = Tr.F("A pantalla: 1% low {0} · 0,1% low {1}", Fps(stats.Low1Displayed), Fps(stats.Low01Displayed));
        Text(dc, "■ ", new Point(left, 4), DisplayedBrush, 12, dpi);
        var shown = Text(dc, legend, new Point(left + 12, 4), TextBrush, 12, dpi);
        if (stats.RealMs.Length >= 2)
        {
            var x = left + 12 + shown + 24;
            Text(dc, "■ ", new Point(x, 4), RealBrush, 12, dpi);
            Text(dc, Tr.F("Reales: 1% low {0} · 0,1% low {1}", Fps(stats.Low1Real), Fps(stats.Low01Real)), new Point(x + 12, 4), TextBrush, 12, dpi);
        }
        Text(dc, Tr.F("últimos {0:0} s", stats.WindowSeconds), new Point(w - 90, 4), GridBrush, 11, dpi);
    }

    // Lo más reciente a la derecha; el eje X es tiempo acumulado.
    private void Series(DrawingContext dc, double[] ms, Rect plot, double seconds, Brush brush)
    {
        if (ms.Length < 2) return;
        var total = seconds * 1000;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var t = 0.0;
            var started = false;
            for (var i = ms.Length - 1; i >= 0; i--)
            {
                var x = plot.Right - t / total * plot.Width;
                if (x < plot.Left) break;
                var y = plot.Bottom - Math.Min(ms[i], _scaleMax) / _scaleMax * plot.Height;
                if (!started)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    started = true;
                }
                else ctx.LineTo(new Point(x, y), true, false);
                t += ms[i];
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(brush, 1.5) { LineJoin = PenLineJoin.Round }, geometry);
    }

    private static double Text(DrawingContext dc, string s, Point at, Brush brush, double size, double dpi)
    {
        var text = new FormattedText(s, Tr.Culture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, brush, dpi);
        dc.DrawText(text, at);
        return text.Width;
    }

    private static string Fps(double? value) => value is { } v ? v.ToString("F0", Tr.Culture) : "—";

    private static double Max(double[] values) => values.Length == 0 ? 0 : values.Max();

    private static double Nice(double ms) => ms switch
    {
        <= 20 => 20, <= 25 => 25, <= 33.4 => 33.4, <= 40 => 40, <= 50 => 50, <= 70 => 70, <= 100 => 100, <= 150 => 150, _ => 200
    };
}
