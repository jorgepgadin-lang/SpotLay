using System.Windows;
using Microsoft.Win32;

namespace SpotLay.App.Infrastructure;

/// <summary>Aplica tema claro/oscuro: el tema Fluent de Windows 11 para los controles y nuestra paleta.</summary>
public static class ThemeManager
{
    private static readonly Uri DarkPalette = new("pack://application:,,,/Themes/Dark.xaml");
    private static readonly Uri LightPalette = new("pack://application:,,,/Themes/Light.xaml");

    public static bool IsDark { get; private set; } = true;

    public static void Apply(string theme)
    {
        IsDark = theme switch
        {
            "light" => false,
            "dark" => true,
            _ => SystemUsesDark()
        };

        var app = Application.Current;
        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        var merged = app.Resources.MergedDictionaries;
        foreach (var palette in merged.Where(d => d.Contains("SL.Background")).ToList()) merged.Remove(palette);
        merged.Add(new ResourceDictionary { Source = IsDark ? DarkPalette : LightPalette });
    }

    private static bool SystemUsesDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }
}
