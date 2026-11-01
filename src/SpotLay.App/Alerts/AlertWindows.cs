using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.Core.Config;
using SpotLay.Core.Games;
using SpotLay.Core.Localization;

namespace SpotLay.App.Alerts;

/// <summary>Coloca una ventana en un monitor usando píxeles reales (como el overlay).</summary>
internal static class ScreenPlacement
{
    public static void Place(Window window, IntPtr hwnd, IntPtr monitor, string position, double freeX, double freeY, int stackIndex, int margin = 28)
    {
        if (hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var w = (int)Math.Round(window.Width * dpi.DpiScaleX);
        var h = (int)Math.Round(window.Height * dpi.DpiScaleY);
        if (monitor == IntPtr.Zero) monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var b = ForegroundWindow.MonitorBounds(monitor, workArea: false);
        var bw = b.Right - b.Left;
        var bh = b.Bottom - b.Top;
        var m = (int)Math.Round(margin * dpi.DpiScaleX);
        var gap = (int)Math.Round(12 * dpi.DpiScaleY);

        var (x, y) = position switch
        {
            OverlayPositions.Custom => (b.Left + (int)((bw - w) * Math.Clamp(freeX, 0, 1)), b.Top + (int)((bh - h) * Math.Clamp(freeY, 0, 1))),
            OverlayPositions.TopLeft => (b.Left + m, b.Top + m),
            OverlayPositions.TopCenter => (b.Left + (bw - w) / 2, b.Top + m),
            OverlayPositions.TopRight => (b.Right - w - m, b.Top + m),
            OverlayPositions.Center => (b.Left + (bw - w) / 2, b.Top + (bh - h) / 2),
            OverlayPositions.BottomLeft => (b.Left + m, b.Bottom - h - m),
            OverlayPositions.BottomCenter => (b.Left + (bw - w) / 2, b.Bottom - h - m),
            OverlayPositions.BottomRight => (b.Right - w - m, b.Bottom - h - m),
            // auto: arriba en el centro, apiladas por gravedad.
            _ => (b.Left + (bw - w) / 2, b.Top + m + stackIndex * (h + gap))
        };
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public static void MakeClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (clickThrough) style |= NativeMethods.WsExTransparent | NativeMethods.WsExLayered;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }
}

/// <summary>Aviso de un sensor encima del juego. No recibe clics ni foco.</summary>
internal sealed class AlertPopupWindow : Window
{
    private readonly Border _card;
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private IntPtr _hwnd;

    public AlertPopupWindow()
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
        Title = Tr.T("SpotLay aviso");

        _label = new TextBlock { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _value = new TextBlock { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
        _detail = new TextBlock { TextAlignment = TextAlignment.Center, Opacity = 0.9 };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 10, 18, 10) };
        stack.Children.Add(_label);
        stack.Children.Add(_value);
        stack.Children.Add(_detail);
        _card = new Border { BorderThickness = new Thickness(2), Child = stack };
        Content = _card;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ScreenPlacement.MakeClickThrough(_hwnd, true);
    }

    public void Show(string label, string value, string detail, AlarmRule rule, AlertWindowConfig look, string fontFamily, IntPtr monitor, int stackIndex)
    {
        Width = look.Width;
        Height = look.Height;
        Opacity = look.Opacity / 100.0;
        var text = BrushCache.From(rule.AlertTextColor, "#FFFFFF");
        _card.Background = BrushCache.From(rule.AlertBackground, "#B3132B");
        _card.BorderBrush = BrushCache.From(rule.AlertTextColor, "#FFFFFF", 0.6);
        _card.CornerRadius = new CornerRadius(look.Radius);
        var font = new FontFamily(fontFamily + ", Segoe UI");
        foreach (var block in new[] { _label, _value, _detail })
        {
            block.Foreground = text;
            block.FontFamily = font;
        }
        _label.Text = label;
        _label.FontSize = rule.AlertLabelSize;
        _value.Text = value;
        _value.FontSize = rule.AlertValueSize;
        _detail.Text = detail;
        _detail.FontSize = Math.Max(10, rule.AlertLabelSize * .65);
        if (!IsVisible) Show();
        ScreenPlacement.Place(this, _hwnd, monitor, rule.AlertPosition, rule.AlertPositionX, rule.AlertPositionY, stackIndex);
    }
}

/// <summary>Cuenta atrás del apagado de emergencia. Sí recibe clics (botón Cancelar), pero no roba el foco.</summary>
internal sealed class ShutdownWindow : Window
{
    private readonly TextBlock _title;
    private readonly TextBlock _sensor;
    private readonly TextBlock _countdown;
    private readonly TextBlock _hint;
    private IntPtr _hwnd;

    public ShutdownWindow(Action cancel)
    {
        ThemeMode = ThemeMode.None;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = Tr.T("SpotLay apagado de emergencia");
        Width = 640;
        Height = 250;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

        _title = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Foreground = Brush("#FF6A77") };
        _sensor = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
        _countdown = new TextBlock { FontSize = 48, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Foreground = Brush("#FFDD70") };
        _hint = new TextBlock { FontSize = 13, TextAlignment = TextAlignment.Center, Foreground = Brush("#D6CCCF"), TextWrapping = TextWrapping.Wrap };
        var button = new Button
        {
            Content = Tr.T("CANCELAR APAGADO"),
            Width = 240,
            Height = 40,
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = Brush("#801927"),
            BorderBrush = Brush("#FF6B78"),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => cancel();

        var stack = new StackPanel { Margin = new Thickness(22, 16, 22, 16) };
        foreach (var child in new UIElement[] { _title, _sensor, _countdown, _hint, button }) stack.Children.Add(child);
        Content = new Border
        {
            Background = Brush("#F01E090E"),
            BorderBrush = Brush("#FF485B"),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(14),
            Child = stack
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ScreenPlacement.MakeClickThrough(_hwnd, false);
    }

    public void Update(string sensor, int seconds, bool issued, bool simulated, string hotkey, IntPtr monitor)
    {
        _title.Text = simulated ? Tr.T("APAGADO DE EMERGENCIA · SIMULACIÓN") : Tr.T("APAGADO DE EMERGENCIA");
        _sensor.Text = sensor;
        _countdown.Text = !issued ? $"{seconds} s" : simulated ? Tr.T("AQUÍ SE APAGARÍA") : Tr.T("CERRANDO WINDOWS");
        _hint.Text = issued
            ? simulated ? Tr.T("Prueba terminada: Windows no se apaga en una simulación.") : ""
            : Tr.F("Pulsa {0} o usa el botón para cancelar", hotkey);
        if (!IsVisible) Show();
        ScreenPlacement.Place(this, _hwnd, monitor, OverlayPositions.TopCenter, 0, 0, 0, margin: 36);
    }

    private static SolidColorBrush Brush(string hex) => BrushCache.From(hex, "#FFFFFF");
}
