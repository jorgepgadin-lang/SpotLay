using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpotLay.App.Overlay;

namespace SpotLay.App.Views;

/// <summary>
/// Fila de color: el color que se ve, la paleta y "+" para elegir cualquier color
/// (desplegable con el selector). Sin cajas de texto con el código hexadecimal.
/// </summary>
public partial class ColorRow : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(string), typeof(ColorRow),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorRow)d).Refresh()));

    /// <summary>Color que se usa cuando <see cref="Color"/> está vacío (el del diseño general).</summary>
    public static readonly DependencyProperty FallbackColorProperty = DependencyProperty.Register(nameof(FallbackColor), typeof(string), typeof(ColorRow),
        new PropertyMetadata("", (d, _) => ((ColorRow)d).Refresh()));

    private string _original = "";
    private bool _accepted;

    public ColorRow()
    {
        InitializeComponent();
        Picker.Changed += hex => Color = hex;
        Picker.Closed += ok =>
        {
            _accepted = ok;
            PickerPopup.IsOpen = false;
        };
        Refresh();
    }

    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public string FallbackColor { get => (string)GetValue(FallbackColorProperty); set => SetValue(FallbackColorProperty, value); }

    private string Effective => BrushCache.TryParse(Color ?? "", out _) ? Color : FallbackColor ?? "";

    private void Refresh()
    {
        if (CurrentSwatch is null) return;
        var effective = Effective;
        CurrentSwatch.Background = BrushCache.TryParse(effective, out _) ? BrushCache.From(effective, "#000000") : Brushes.Transparent;
        CurrentSwatch.ToolTip = effective;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex }) Color = hex;
    }

    private void Custom_Click(object sender, RoutedEventArgs e)
    {
        // Cada cambio se ve al momento en el overlay; Cancelar (o Esc) deja el color de antes.
        _original = Color ?? "";
        _accepted = true;
        Picker.Start(Effective);
        PickerPopup.IsOpen = true;
    }

    // Clic fuera = aceptar; Cancelar/Esc = volver al color de antes.
    private void Popup_Closed(object? sender, EventArgs e)
    {
        if (!_accepted) Color = _original;
    }
}
