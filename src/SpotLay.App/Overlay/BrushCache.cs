using System.Windows.Media;

namespace SpotLay.App.Overlay;

public static class BrushCache
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush From(string? hex, string fallback, double opacity = 1.0)
    {
        var key = $"{hex}|{fallback}|{opacity:F2}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var color = TryParse(hex, out var c) ? c : TryParse(fallback, out var f) ? f : Colors.White;
        color.A = (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return Cache[key] = brush;
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
