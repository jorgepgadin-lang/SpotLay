using System.Windows;
using System.Windows.Controls;
using SpotLay.App.ViewModels;

namespace SpotLay.App.Views;

public partial class OverlayPage : UserControl
{
    public OverlayPage()
    {
        InitializeComponent();
    }

    // SelectedItems no se puede enlazar: la lista avisa al modelo de la selección múltiple.
    private void MetricList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        (DataContext as OverlayEditorViewModel)?.SetSelection(MetricList.SelectedItems.OfType<LayoutMetricItem>());

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        MetricList.SelectAll();
        MetricList.Focus();
    }
}
