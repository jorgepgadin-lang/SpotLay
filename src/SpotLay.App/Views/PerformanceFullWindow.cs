using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SpotLay.App.ViewModels;
using SpotLay.Core.Localization;

namespace SpotLay.App.Views;

/// <summary>
/// Las dos gráficas de la partida a pantalla completa, en el monitor de la ventana
/// principal. Esc, F11 o el botón vuelven a la ventana.
/// </summary>
public sealed class PerformanceFullWindow : Window
{
    public PerformanceFullWindow(PerformanceViewModel vm)
    {
        DataContext = vm;
        Title = Tr.T("Rendimiento");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SL.Background");
        Owner = Application.Current.MainWindow is { IsVisible: true } main ? main : null;
        if (Owner is not null)
        {
            // Mismo monitor que la ventana principal; se maximiza al cargar.
            Left = Owner.Left + 20;
            Top = Owner.Top + 20;
            Width = 400;
            Height = 300;
        }
        Loaded += (_, _) => WindowState = WindowState.Maximized;

        var title = new TextBlock { FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(StyleProperty, "SL.SectionTitle");
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(PerformanceViewModel.Title2)));
        var subtitle = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
        subtitle.SetResourceReference(StyleProperty, "SL.Caption");
        subtitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(PerformanceViewModel.Subtitle2)));
        var close = new Button { Content = Tr.T("Salir de pantalla completa (Esc)"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        close.SetResourceReference(StyleProperty, "SL.SubtleButton");
        close.Click += (_, _) => Close();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new StackPanel { Children = { title, subtitle } });
        header.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new SessionChartsView());
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F11)
            {
                e.Handled = true;
                Close();
            }
        };
    }
}
