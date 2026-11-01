using System.Windows;
using SpotLay.Core.Localization;

namespace SpotLay.App.Views;

/// <summary>Elección del idioma la primera vez que se abre SpotLay.</summary>
public partial class LanguageWindow : Window
{
    private string _choice;

    private LanguageWindow(string suggested)
    {
        InitializeComponent();
        // Sin el tema Fluent: una ventana aparte con Fluent llegó a no dibujarse (selector de color).
        ThemeMode = ThemeMode.None;
        Topmost = true;
        _choice = suggested;
        // El idioma de Windows sale resaltado.
        var accent = (Style)FindResource("SL.AccentButton");
        var subtle = (Style)FindResource("SL.SubtleButton");
        SpanishButton.Style = suggested == Tr.Spanish ? accent : subtle;
        EnglishButton.Style = suggested == Tr.English ? accent : subtle;
    }

    /// <summary>Muestra la ventana y devuelve "es" o "en" (si se cierra sin elegir, el sugerido).</summary>
    public static string Ask(string suggested)
    {
        var window = new LanguageWindow(suggested);
        window.ShowDialog();
        return window._choice;
    }

    private void Spanish_Click(object sender, RoutedEventArgs e) => Choose(Tr.Spanish);

    private void English_Click(object sender, RoutedEventArgs e) => Choose(Tr.English);

    private void Choose(string language)
    {
        _choice = language;
        Close();
    }
}
