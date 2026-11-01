using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SpotLay.App.Infrastructure;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;

namespace SpotLay.App.Overlay;

/// <summary>Una tarjeta del overlay. Solo notifica cuando cambia lo que se ve, para no redibujar de mas.</summary>
public sealed class OverlayTileViewModel : ObservableObject
{
    private const int HistoryLength = 60;
    private readonly MetricStyle _style;
    private readonly bool _labelFromStyle;
    private readonly Queue<double> _history = new();
    private double _peak;
    private string _label;
    private string _unit;
    private string _valueText = "—";
    private double _fraction;
    private IReadOnlyList<double>? _historyArray;
    private bool _isVisible = true;
    private readonly Brush _normalValueBrush;
    private readonly double _normalValueSize;
    private Brush _valueBrush;
    private double _valueSize;
    private AlarmRule? _alert;
    private readonly Typeface _typeface;
    private readonly string _unitPosition;
    private bool _unitBelow;
    // Reducción para que la cifra quepa dentro del arco/anillo. Solo baja: así no
    // cambia de tamaño cada vez que el valor gana o pierde una cifra.
    private double _fit = 1;
    private string _fittedText = "";

    public OverlayTileViewModel(string metricId, MetricStyle style, OverlayLayout layout, string label, string unit)
    {
        MetricId = metricId;
        _style = style;
        IsGameMetric = KnownMetrics.IsGameMetric(metricId);
        IsTextMetric = metricId is KnownMetrics.FrameGen or KnownMetrics.Api;
        _labelFromStyle = !string.IsNullOrWhiteSpace(style.Label);
        _label = _labelFromStyle ? style.Label : label;
        _unit = IsTextMetric ? "" : unit;

        var s = layout.Scale;
        Visualization = IsTextMetric ? Visualizations.Number : style.Visualization;
        Width = Math.Round(layout.TileWidth * s);
        Margin = new Thickness(Math.Round(layout.Gap * s / 2));
        LabelSize = Math.Max(8, style.LabelSize * s);
        _valueSize = _normalValueSize = Math.Max(9, style.ValueSize * s * (IsTextMetric ? 0.8 : 1));
        UnitSize = Math.Max(8, style.UnitSize * s);
        Thickness = Math.Max(2, 5 * s);
        LabelBrush = BrushCache.From(style.LabelColor, layout.LabelColor);
        _valueBrush = _normalValueBrush = BrushCache.From(style.ValueColor, layout.ValueColor);
        UnitBrush = BrushCache.From(style.UnitColor, layout.UnitColor);
        TrackBrush = BrushCache.From(style.ValueColor, layout.ValueColor, 0.18);
        ValueWeight = layout.FontBold ? FontWeights.Bold : FontWeights.SemiBold;
        ValueEffect = layout.ValueEffect is "shadow" or "outline"
            ? new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9, Color = Colors.Black }
            : null;
        if (ValueEffect is { } effect) effect.Freeze();

        (VisualHeight, VisualWidth) = Visualization switch
        {
            Visualizations.Gauge => (Math.Round(Width * 0.5), Math.Round(Width * 0.62)),
            Visualizations.Ring => (Math.Round(Width * 0.46), Math.Round(Width * 0.46)),
            Visualizations.BarHorizontal => (Math.Round(Thickness * 2), Math.Round(Width * 0.8)),
            Visualizations.BarVertical => (Math.Round(Width * 0.24), Math.Round(Thickness * 2)),
            Visualizations.Line => (Math.Round(Width * 0.2), Math.Round(Width * 0.8)),
            _ => (0d, 0d)
        };
        HasVisual = VisualHeight > 0;
        ValueInside = Visualization is Visualizations.Gauge or Visualizations.Ring;

        _typeface = new Typeface(new FontFamily(layout.FontFamily + ", Segoe UI"), FontStyles.Normal, ValueWeight, FontStretches.Normal);
        _unitPosition = style.UnitPosition;
        // FG: en automático "DINÁMICO"/"FIJO" va siempre debajo del "x4".
        _unitBelow = _unitPosition == UnitPositions.Below ||
                     (_unitPosition == UnitPositions.Auto && metricId == KnownMetrics.FrameGen);
        // Hueco libre dentro del gráfico, a la altura de la cifra (misma geometría que GaugeElement).
        if (Visualization == Visualizations.Gauge)
        {
            var r = Math.Max(1, Math.Min((VisualWidth - Thickness) / 2, (VisualHeight - Thickness) / 1.72));
            InsideWidth = Math.Max(10, 2 * (r - Thickness) - 2);
            InsideHeight = Math.Max(10, 1.55 * r);
        }
        else if (Visualization == Visualizations.Ring)
        {
            var r = Math.Max(1, (Math.Min(VisualWidth, VisualHeight) - Thickness) / 2);
            InsideWidth = Math.Max(10, 2 * (r - Thickness) - 2);
            InsideHeight = InsideWidth;
        }
    }

    /// <summary>Ancho y alto disponibles dentro del arco o anillo (DIP).</summary>
    private double InsideWidth { get; }
    private double InsideHeight { get; }

    public string MetricId { get; }
    public bool IsGameMetric { get; }
    public bool IsTextMetric { get; }
    public string Label { get => _label; private set => Set(ref _label, value); }
    public string Unit
    {
        get => _unit;
        private set
        {
            if (Set(ref _unit, value)) OnPropertyChanged(nameof(ShowUnit));
        }
    }
    public string Visualization { get; }
    public double Width { get; }
    public Thickness Margin { get; }
    public double LabelSize { get; }
    public double ValueSize
    {
        get => _valueSize;
        private set
        {
            if (Set(ref _valueSize, value)) OnPropertyChanged(nameof(DisplayValueSize));
        }
    }
    public double UnitSize { get; }
    public double Thickness { get; }
    public double VisualHeight { get; }
    public double VisualWidth { get; }
    public bool HasVisual { get; }
    public bool ValueInside { get; }
    public bool ShowValueRow => !ValueInside;
    public bool ShowUnit => _unitPosition != UnitPositions.Hidden && Unit.Length > 0;
    public Orientation UnitOrientation => _unitBelow ? Orientation.Vertical : Orientation.Horizontal;
    public Thickness UnitMargin => _unitBelow ? new Thickness(0, -2, 0, 0) : new Thickness(3, 0, 0, 2);
    public HorizontalAlignment UnitAlignment => _unitBelow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    public VerticalAlignment UnitVerticalAlignment => _unitBelow ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    /// <summary>Tamaños con la reducción para caber en el arco/anillo (fuera del gráfico, los normales).</summary>
    public double DisplayValueSize => ValueInside ? ValueSize * _fit : ValueSize;
    public double DisplayUnitSize => ValueInside ? UnitSize * _fit : UnitSize;
    // El centro del arco (abierto por abajo) queda algo por debajo de la mitad de su caja.
    public Thickness ValueInsideMargin => Visualization == Visualizations.Gauge
        ? new Thickness(0, Math.Round(VisualHeight * 0.15), 0, 0) : new Thickness(0);
    public Brush LabelBrush { get; }
    /// <summary>Cambia de color (y el valor de tamaño) mientras el sensor está en aviso.</summary>
    public Brush ValueBrush { get => _valueBrush; private set => Set(ref _valueBrush, value); }
    public Brush UnitBrush { get; }
    public Brush TrackBrush { get; }
    public FontWeight ValueWeight { get; }
    public Effect? ValueEffect { get; }

    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
    public double Fraction { get => _fraction; private set => Set(ref _fraction, value); }
    public IReadOnlyList<double>? History { get => _historyArray; private set => Set(ref _historyArray, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    private bool _isLifted;

    /// <summary>Se está moviendo con Ctrl + arrastrar en el overlay.</summary>
    public bool IsLifted { get => _isLifted; set => Set(ref _isLifted, value); }

    public void SetAlert(AlarmRule? rule)
    {
        if (ReferenceEquals(rule, _alert)) return;
        _alert = rule;
        ValueBrush = rule is null ? _normalValueBrush : BrushCache.From(rule.AlertTileColor, "#FF3030");
        ValueSize = rule is null ? _normalValueSize : _normalValueSize * rule.AlertTileScale / 100.0;
    }

    /// <summary>
    /// Ajusta la cifra al hueco del arco/anillo: en automático la unidad pasa debajo
    /// si al lado no cabe, y si aún no cabe se reduce el tamaño (nunca se agranda).
    /// </summary>
    private void FitInside()
    {
        if (ValueText == _fittedText) return;
        _fittedText = ValueText;
        var valueW = TextWidth(ValueText, _normalValueSize);
        var showUnit = ShowUnit;
        var unitW = showUnit ? TextWidth(Unit, UnitSize) : 0;
        var sideW = valueW + (showUnit ? 3 + unitW : 0);

        var below = _unitBelow;
        if (_unitPosition == UnitPositions.Auto && showUnit && !below && sideW > InsideWidth)
            below = true;
        double fit;
        if (below && showUnit)
        {
            var height = (_normalValueSize + UnitSize) * 1.2;
            fit = Math.Min(InsideWidth / Math.Max(valueW, unitW), InsideHeight / height);
        }
        else
        {
            fit = InsideWidth / sideW;
        }
        fit = Math.Min(_fit, Math.Clamp(fit, 0.35, 1));

        var changed = below != _unitBelow || Math.Abs(fit - _fit) > 0.001;
        _unitBelow = below;
        _fit = fit;
        if (changed)
            OnPropertiesChanged(nameof(UnitOrientation), nameof(UnitMargin), nameof(UnitAlignment), nameof(UnitVerticalAlignment),
                nameof(DisplayValueSize), nameof(DisplayUnitSize));
    }

    /// <summary>Fuera del gráfico, en automático la unidad pasa debajo si al lado no cabe en la tarjeta (y ahí se queda).</summary>
    private void FitRow()
    {
        if (_unitPosition != UnitPositions.Auto || _unitBelow || !ShowUnit) return;
        var key = ValueText + "|" + Unit;
        if (key == _fittedText) return;
        _fittedText = key;
        var sideW = TextWidth(ValueText, _normalValueSize) + 3 + TextWidth(Unit, UnitSize);
        if (sideW <= Width - 6) return;
        _unitBelow = true;
        OnPropertiesChanged(nameof(UnitOrientation), nameof(UnitMargin), nameof(UnitAlignment), nameof(UnitVerticalAlignment));
    }

    private double TextWidth(string text, double size) =>
        string.IsNullOrEmpty(text) ? 0
            : new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, size, Brushes.White, 1.0).WidthIncludingTrailingWhitespace;

    public void Update(MetricValue? metric)
    {
        // Los sensores sueltos (hw:) no tienen nombre ni unidad hasta la primera
        // lectura de LibreHardwareMonitor, que llega despues de montar el diseño.
        if (metric is not null)
        {
            if (!_labelFromStyle && Label == MetricId && !string.IsNullOrEmpty(metric.Label)) Label = metric.Label;
            if (!IsTextMetric && string.IsNullOrEmpty(Unit) && !string.IsNullOrEmpty(metric.Unit)) Unit = metric.Unit;
        }
        var text = MetricFormat.Value(metric);
        if (IsTextMetric)
        {
            // "x4 DINÁMICO" → "x4" es el valor y "DINÁMICO" hace de unidad (tamaño, color y posición de la unidad).
            // Igual con la API: "DirectX" + "12".
            var space = text.IndexOf(' ');
            Unit = space > 0 ? text[(space + 1)..] : "";
            if (space > 0) text = text[..space];
        }
        ValueText = text;
        if (ValueInside) FitInside();
        else FitRow();
        if (!HasVisual || metric?.Value is not { } v || !double.IsFinite(v)) return;

        _history.Enqueue(v);
        while (_history.Count > HistoryLength) _history.Dequeue();
        _peak = _history.Max();

        var max = _style.AutoScale ? Math.Max(_style.Max, _peak) : _style.Max;
        var span = max - _style.Min;
        var fraction = span > 0 ? Math.Clamp((v - _style.Min) / span, 0, 1) : 0;
        // Redondeo a 0,5 %: por debajo no se aprecia y evita redibujar el arco en cada lectura.
        Fraction = Math.Round(fraction * 200) / 200;
        if (Visualization == Visualizations.Line) History = _history.ToArray();
    }
}
