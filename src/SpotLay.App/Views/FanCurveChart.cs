using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpotLay.Core.Config;

namespace SpotLay.App.Views;

/// <summary>
/// Curva temperatura → % de un ventilador. Los puntos se arrastran con el ratón;
/// al soltar se llama a <see cref="Edited"/> con la curva nueva. Marca la
/// temperatura actual y la velocidad que está aplicando el canal.
/// </summary>
public sealed class FanCurveChart : FrameworkElement
{
    private const double MinTemp = 20, MaxTemp = 110;
    private const double PadLeft = 36, PadRight = 12, PadTop = 10, PadBottom = 24;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<FanPoint>), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((FanCurveChart)d).ResetWorking()));

    public static readonly DependencyProperty CurrentTemperatureProperty = DependencyProperty.Register(
        nameof(CurrentTemperature), typeof(double?), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentDutyProperty = DependencyProperty.Register(
        nameof(CurrentDuty), typeof(double?), typeof(FanCurveChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EditedProperty = DependencyProperty.Register(
        nameof(Edited), typeof(Action<IReadOnlyList<FanPoint>>), typeof(FanCurveChart));

    private List<FanPoint> _working = [];
    private int _drag = -1;
    private int _hover = -1;

    public FanCurveChart()
    {
        MinHeight = 160;
        Cursor = Cursors.Arrow;
        Focusable = false;
    }

    public IReadOnlyList<FanPoint>? Points { get => (IReadOnlyList<FanPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double? CurrentTemperature { get => (double?)GetValue(CurrentTemperatureProperty); set => SetValue(CurrentTemperatureProperty, value); }
    public double? CurrentDuty { get => (double?)GetValue(CurrentDutyProperty); set => SetValue(CurrentDutyProperty, value); }
    public Action<IReadOnlyList<FanPoint>>? Edited { get => (Action<IReadOnlyList<FanPoint>>?)GetValue(EditedProperty); set => SetValue(EditedProperty, value); }

    private void ResetWorking()
    {
        if (_drag >= 0) return;
        _working = Points?.OrderBy(p => p.Temperature).ToList() ?? [];
    }

    private Rect Plot => new(PadLeft, PadTop, Math.Max(10, ActualWidth - PadLeft - PadRight), Math.Max(10, ActualHeight - PadTop - PadBottom));

    private Point ToScreen(double temp, double duty)
    {
        var r = Plot;
        return new Point(r.Left + (temp - MinTemp) / (MaxTemp - MinTemp) * r.Width, r.Bottom - duty / 100 * r.Height);
    }

    private (double Temp, double Duty) FromScreen(Point p)
    {
        var r = Plot;
        return (MinTemp + (p.X - r.Left) / r.Width * (MaxTemp - MinTemp), (r.Bottom - p.Y) / r.Height * 100);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = Plot;
        var text = Brush("SL.TextTertiary", Colors.Gray);
        var grid = new Pen(Brush("SL.Stroke", Colors.DimGray), 1);
        var accent = Brush("SL.Accent", Colors.CornflowerBlue);
        var warn = Brush("SL.Warn", Colors.Orange);
        var surface = Brush("SL.SurfaceAlt", Colors.Black);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRoundedRectangle(surface, null, r, 6, 6);
        for (var duty = 0; duty <= 100; duty += 25)
        {
            var y = ToScreen(MinTemp, duty).Y;
            dc.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
            Label(dc, $"{duty}%", new Point(2, y - 7), text, dpi);
        }
        for (var temp = 30; temp <= 110; temp += 20)
        {
            var x = ToScreen(temp, 0).X;
            dc.DrawLine(grid, new Point(x, r.Top), new Point(x, r.Bottom));
            Label(dc, $"{temp}°", new Point(x - 10, r.Bottom + 5), text, dpi);
        }

        if (CurrentTemperature is { } t && double.IsFinite(t))
        {
            var x = ToScreen(Math.Clamp(t, MinTemp, MaxTemp), 0).X;
            var pen = new Pen(warn, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(x, r.Top), new Point(x, r.Bottom));
            Label(dc, $"{t.ToString("F0", CultureInfo.InvariantCulture)} °C", new Point(Math.Min(x + 4, r.Right - 40), r.Top + 2), warn, dpi);
            if (CurrentDuty is { } d && double.IsFinite(d))
                dc.DrawEllipse(warn, null, ToScreen(Math.Clamp(t, MinTemp, MaxTemp), Math.Clamp(d, 0, 100)), 4, 4);
        }

        if (_working.Count == 0) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToScreen(MinTemp, _working[0].Duty), false, false);
            foreach (var p in _working) ctx.LineTo(ToScreen(p.Temperature, p.Duty), true, true);
            ctx.LineTo(ToScreen(MaxTemp, _working[^1].Duty), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, geometry);

        for (var i = 0; i < _working.Count; i++)
        {
            var p = ToScreen(_working[i].Temperature, _working[i].Duty);
            var big = i == _drag || i == _hover;
            dc.DrawEllipse(big ? accent : surface, new Pen(accent, 2), p, big ? 7 : 5, big ? 7 : 5);
            if (big) Label(dc, $"{_working[i].Temperature}° · {_working[i].Duty}%", new Point(Math.Min(p.X + 8, r.Right - 70), Math.Max(r.Top, p.Y - 20)), accent, dpi);
        }
    }

    private void Label(DrawingContext dc, string s, Point at, Brush brush, double dpi) =>
        dc.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10.5, brush, dpi), at);

    private Brush Brush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private int HitTest(Point mouse)
    {
        for (var i = 0; i < _working.Count; i++)
            if ((ToScreen(_working[i].Temperature, _working[i].Duty) - mouse).Length <= 10) return i;
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drag = HitTest(e.GetPosition(this));
        if (_drag >= 0)
        {
            CaptureMouse();
            e.Handled = true;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var mouse = e.GetPosition(this);
        if (_drag >= 0)
        {
            var (temp, duty) = FromScreen(mouse);
            // Cada punto se queda entre sus vecinos: la curva nunca se cruza.
            var low = _drag > 0 ? _working[_drag - 1].Temperature + 1 : (int)MinTemp;
            var high = _drag < _working.Count - 1 ? _working[_drag + 1].Temperature - 1 : (int)MaxTemp;
            _working[_drag] = new FanPoint(
                (int)Math.Clamp(Math.Round(temp), low, Math.Max(low, high)),
                (int)Math.Clamp(Math.Round(duty), 0, 100));
            InvalidateVisual();
        }
        else
        {
            var hover = HitTest(mouse);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.SizeAll : Cursors.Arrow;
                InvalidateVisual();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag >= 0)
        {
            _drag = -1;
            ReleaseMouseCapture();
            Edited?.Invoke(_working.ToList());
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover >= 0 && _drag < 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
        base.OnMouseLeave(e);
    }
}
