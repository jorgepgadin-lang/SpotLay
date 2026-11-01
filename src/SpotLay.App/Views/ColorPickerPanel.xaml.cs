using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpotLay.App.Views;

/// <summary>
/// Selector de color con el estilo de SpotLay (tono, saturación/brillo y código). Va dentro
/// de un desplegable de <see cref="ColorRow"/>: una ventana aparte no se dibujaba bien.
/// </summary>
public partial class ColorPickerPanel : UserControl
{
    private double _h, _s = 1, _v = 1;
    private bool _updatingHex;

    public ColorPickerPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh(notify: false);
    }

    /// <summary>Cada cambio (se aplica al momento).</summary>
    public event Action<string>? Changed;
    /// <summary>true = Aceptar, false = Cancelar.</summary>
    public event Action<bool>? Closed;

    public void Start(string initial)
    {
        if (TryParse(initial, out var c)) (_h, _s, _v) = ToHsv(c);
        if (IsLoaded) Refresh(notify: false);
    }

    private string Hex => ToHex(FromHsv(_h, _s, _v));

    private void Refresh(bool notify)
    {
        HueFill.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        var color = FromHsv(_h, _s, _v);
        PreviewSwatch.Background = new SolidColorBrush(color);
        Canvas.SetLeft(SvThumb, _s * SvArea.ActualWidth - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _v) * SvArea.ActualHeight - SvThumb.Height / 2);
        Canvas.SetTop(HueThumb, _h / 360 * HueArea.ActualHeight - HueThumb.Height / 2);
        _updatingHex = true;
        HexBox.Text = ToHex(color);
        _updatingHex = false;
        if (notify) Changed?.Invoke(ToHex(color));
    }

    // ---- Ratón ----------------------------------------------------------------
    private void Sv_Down(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        SetSv(e.GetPosition(SvArea));
    }

    private void Sv_Move(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured) SetSv(e.GetPosition(SvArea));
    }

    private void Hue_Down(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        SetHue(e.GetPosition(HueArea));
    }

    private void Hue_Move(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured) SetHue(e.GetPosition(HueArea));
    }

    private void Area_Up(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void SetSv(Point p)
    {
        _s = Math.Clamp(p.X / SvArea.ActualWidth, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvArea.ActualHeight, 0, 1);
        Refresh(notify: true);
    }

    private void SetHue(Point p)
    {
        _h = Math.Clamp(p.Y / HueArea.ActualHeight, 0, 1) * 360;
        if (_h >= 360) _h = 359.9;
        Refresh(notify: true);
    }

    // ---- Código ---------------------------------------------------------------
    private void Hex_Commit(object sender, RoutedEventArgs e)
    {
        if (_updatingHex) return;
        if (TryParse(HexBox.Text, out var c))
        {
            (_h, _s, _v) = ToHsv(c);
            Refresh(notify: true);
        }
        else
        {
            Refresh(notify: false);
        }
    }

    private void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Hex_Commit(sender, e);
        else if (e.Key == Key.Escape) Closed?.Invoke(false);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Hex_Commit(sender, e);
        Closed?.Invoke(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Closed?.Invoke(false);

    // ---- Conversión -----------------------------------------------------------
    private static bool TryParse(string? text, out Color color)
    {
        color = default;
        var t = text?.Trim().TrimStart('#') ?? "";
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
