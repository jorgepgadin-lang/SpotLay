using System.Windows;
using System.Windows.Controls;
using SpotLay.App.ViewModels;

namespace SpotLay.App.Views;

public partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    // "Importar diseño…": primero el archivo (comando) y luego el menú de destinos.
    private void OpenImportMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button || button.DataContext is not ProfilesViewModel vm) return;
        if (!vm.PickImportFile()) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // "Copiar a…" abre su menú con un clic normal (no solo con el botón derecho).
    private void OpenCopyMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        (button.DataContext as ProfileItem)?.RefreshTargets();
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
