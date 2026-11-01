using System.Windows;
using System.Windows.Controls;

namespace SpotLay.App.Overlay;

/// <summary>
/// Rejilla de tarjetas en la que cada fila mide lo que su tarjeta mas alta
/// (una fila de cifras no hereda la altura de una fila de arcos). Ignora las
/// tarjetas ocultas.
/// </summary>
public sealed class TilePanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(TilePanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var visible = Visible();
        var (columns, columnWidth) = Grid(visible);
        var height = 0.0;
        for (var i = 0; i < visible.Count; i += columns) height += RowHeight(visible, i, columns);
        return new Size(columnWidth * columns, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Visible();
        var (columns, columnWidth) = Grid(visible);
        var top = 0.0;
        for (var i = 0; i < visible.Count; i += columns)
        {
            var rowHeight = RowHeight(visible, i, columns);
            for (var c = 0; c < columns && i + c < visible.Count; c++)
                visible[i + c].Arrange(new Rect(c * columnWidth, top, columnWidth, rowHeight));
            top += rowHeight;
        }
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Arrange(new Rect());
        return finalSize;
    }

    private List<UIElement> Visible() =>
        InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    private (int Columns, double ColumnWidth) Grid(List<UIElement> visible)
    {
        var columns = Math.Max(1, Math.Min(Columns, Math.Max(1, visible.Count)));
        var width = visible.Count == 0 ? 0 : visible.Max(c => c.DesiredSize.Width);
        return (columns, width);
    }

    private static double RowHeight(List<UIElement> visible, int start, int columns) =>
        visible.Skip(start).Take(columns).Max(c => c.DesiredSize.Height);
}
