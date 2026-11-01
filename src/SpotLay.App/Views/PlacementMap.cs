using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SpotLay.App.Views;

/// <summary>
/// Mini-monitor con la proporción real de la pantalla y el overlay dibujado a
/// escala. Se arrastra con el ratón; se imanta a bordes (con margen), esquinas
/// y centro; con Alt pulsado no se imanta. Un clic fuera lo lleva a ese punto.
/// Todas las medidas son píxeles reales del monitor.
/// </summary>
public sealed class PlacementMap : FrameworkElement
{
    public static readonly DependencyProperty ScreenWidthProperty = Register(nameof(ScreenWidth), 1920);
    public static readonly DependencyProperty ScreenHeightProperty = Register(nameof(ScreenHeight), 1080);
    public static readonly DependencyProperty BoxWidthProperty = Register(nameof(BoxWidth), 200);
    public static readonly DependencyProperty BoxHeightProperty = Register(nameof(BoxHeight), 300);
    public static readonly DependencyProperty BoxXProperty = Register(nameof(BoxX), 24);
    public static readonly DependencyProperty BoxYProperty = Register(nameof(BoxY), 24);
    public static readonly DependencyProperty SnapMarginProperty = Register(nameof(SnapMargin), 24);
    public static readonly DependencyProperty DraggedProperty = DependencyProperty.Register(
        nameof(Dragged), typeof(Action<int, int, bool>), typeof(PlacementMap));

    private Point? _grab;
    private int? _guideX, _guideY;

    public PlacementMap()
    {
        Cursor = Cursors.Arrow;
        Focusable = false;
    }

    public int ScreenWidth { get => (int)GetValue(ScreenWidthProperty); set => SetValue(ScreenWidthProperty, value); }
    public int ScreenHeight { get => (int)GetValue(ScreenHeightProperty); set => SetValue(ScreenHeightProperty, value); }
    public int BoxWidth { get => (int)GetValue(BoxWidthProperty); set => SetValue(BoxWidthProperty, value); }
    public int BoxHeight { get => (int)GetValue(BoxHeightProperty); set => SetValue(BoxHeightProperty, value); }
    public int BoxX { get => (int)GetValue(BoxXProperty); set => SetValue(BoxXProperty, value); }
    public int BoxY { get => (int)GetValue(BoxYProperty); set => SetValue(BoxYProperty, value); }
    public int SnapMargin { get => (int)GetValue(SnapMarginProperty); set => SetValue(SnapMarginProperty, value); }
    /// <summary>(x, y, terminado): nueva esquina superior izquierda en píxeles del monitor.</summary>
    public Action<int, int, bool>? Dragged { get => (Action<int, int, bool>?)GetValue(DraggedProperty); set => SetValue(DraggedProperty, value); }

    private static DependencyProperty Register(string name, int value) => DependencyProperty.Register(
        name, typeof(int), typeof(PlacementMap),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    // Ancho disponible; alto según la proporción del monitor.
    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 360 : available.Width;
        return new Size(w, w * Math.Max(1, ScreenHeight) / Math.Max(1, ScreenWidth));
    }

    private double Scale => ActualWidth / Math.Max(1, ScreenWidth);

    private Rect BoxRect => new(BoxX * Scale, BoxY * Scale, Math.Max(3, BoxWidth * Scale), Math.Max(3, BoxHeight * Scale));

    protected override void OnRender(DrawingContext dc)
    {
        var screen = new Rect(0, 0, ActualWidth, ActualHeight);
        var surface = Res("SL.SurfaceAlt", Colors.Black);
        var stroke = new Pen(Res("SL.Stroke", Colors.Gray), 1);
        var accent = Res("SL.Accent", Colors.CornflowerBlue);
        var text = Res("SL.TextTertiary", Colors.Gray);
        dc.DrawRoundedRectangle(surface, stroke, screen, 6, 6);

        // Tercios: dónde cambia el borde al que se ancla.
        var faint = new Pen(Res("SL.Stroke", Colors.Gray), 1) { DashStyle = DashStyles.Dot };
        for (var i = 1; i <= 2; i++)
        {
            dc.DrawLine(faint, new Point(ActualWidth * i / 3, 0), new Point(ActualWidth * i / 3, ActualHeight));
            dc.DrawLine(faint, new Point(0, ActualHeight * i / 3), new Point(ActualWidth, ActualHeight * i / 3));
        }

        var guide = new Pen(Res("SL.Warn", Colors.Orange), 1);
        if (_guideX is { } gx) dc.DrawLine(guide, new Point(gx * Scale, 0), new Point(gx * Scale, ActualHeight));
        if (_guideY is { } gy) dc.DrawLine(guide, new Point(0, gy * Scale), new Point(ActualWidth, gy * Scale));

        var fill = accent.Clone();
        fill.Opacity = 0.35;
        dc.DrawRoundedRectangle(fill, new Pen(accent, 2), BoxRect, 3, 3);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = new FormattedText($"{ScreenWidth}×{ScreenHeight} · overlay en {BoxX}, {BoxY} px",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, text, dpi);
        // En el lado contrario al overlay para no taparlo.
        var bottom = BoxRect.Top + BoxRect.Height / 2 < ActualHeight / 2;
        dc.DrawText(label, new Point(8, bottom ? ActualHeight - label.Height - 6 : 6));
    }

    private Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var box = BoxRect;
        // Clic fuera del recuadro: lo centra ahí y se sigue arrastrando.
        _grab = box.Contains(p) ? new Point(p.X - box.X, p.Y - box.Y) : new Point(box.Width / 2, box.Height / 2);
        CaptureMouse();
        MoveTo(p, finished: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_grab is not null) MoveTo(p, finished: false);
        else Cursor = BoxRect.Contains(p) ? Cursors.SizeAll : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_grab is null) return;
        MoveTo(e.GetPosition(this), finished: true);
        _grab = null;
        _guideX = _guideY = null;
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
    }

    private void MoveTo(Point mouse, bool finished)
    {
        if (_grab is not { } grab) return;
        var s = Scale;
        var x = (int)Math.Round((mouse.X - grab.X) / s);
        var y = (int)Math.Round((mouse.Y - grab.Y) / s);
        var maxX = Math.Max(0, ScreenWidth - BoxWidth);
        var maxY = Math.Max(0, ScreenHeight - BoxHeight);
        x = Math.Clamp(x, 0, maxX);
        y = Math.Clamp(y, 0, maxY);

        _guideX = _guideY = null;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var threshold = 8 / s;   // 8 píxeles del mini-monitor
            (x, _guideX) = Snap(x, [0, SnapMargin, maxX / 2, maxX - SnapMargin, maxX], threshold, v => v == maxX / 2 ? v + BoxWidth / 2 : v);
            (y, _guideY) = Snap(y, [0, SnapMargin, maxY / 2, maxY - SnapMargin, maxY], threshold, v => v == maxY / 2 ? v + BoxHeight / 2 : v);
        }
        Dragged?.Invoke(x, y, finished);
        InvalidateVisual();
    }

    private static (int Value, int? Guide) Snap(int value, int[] targets, double threshold, Func<int, int> guideFor)
    {
        foreach (var t in targets)
            if (Math.Abs(value - t) <= threshold) return (t, guideFor(t));
        return (value, null);
    }
}
