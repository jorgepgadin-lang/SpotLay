using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using SpotLay.App.Infrastructure;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.Overlay;

/// <summary>
/// Modelo del overlay. Lo usan tanto la ventana real como las vistas previas del
/// panel, asi que lo que ves al editar es exactamente lo que sale en el juego.
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, MetricValue> SampleGame = new Dictionary<string, MetricValue>
    {
        [KnownMetrics.FrameGen] = Sample(KnownMetrics.FrameGen, 4, Tr.T("x4 DINÁMICO")),
        [KnownMetrics.Fps] = Sample(KnownMetrics.Fps, 140),
        [KnownMetrics.FpsReal] = Sample(KnownMetrics.FpsReal, 35),
        [KnownMetrics.FpsGenerated] = Sample(KnownMetrics.FpsGenerated, 105),
        [KnownMetrics.FrameTime] = Sample(KnownMetrics.FrameTime, 28.6),
        [KnownMetrics.Api] = Sample(KnownMetrics.Api, null, Tr.T("DirectX 12")),
        [KnownMetrics.Low1] = Sample(KnownMetrics.Low1, 118),
        [KnownMetrics.Low01] = Sample(KnownMetrics.Low01, 96),
        [KnownMetrics.Low1Real] = Sample(KnownMetrics.Low1Real, 30),
        [KnownMetrics.Low01Real] = Sample(KnownMetrics.Low01Real, 24),
    };

    private OverlayLayout? _layout;
    private int _columns = 2;
    private Brush _background = Brushes.Transparent;
    private CornerRadius _cornerRadius;
    private Thickness _padding;
    private FontFamily _fontFamily = new("Segoe UI");
    private bool _hasVisibleTiles;
    private bool _isMoving;
    private bool _isEditing;
    private string _banner = "";
    private DateTime _lastGameRefresh;

    // FPS, FG y frame time se pintan una vez por segundo, como el overlay de
    // NVIDIA: cada lectura ya es la media del ultimo segundo y pintarla 4 veces
    // por segundo hacia que pareciera que bailaba.
    private static readonly TimeSpan GameRefresh = TimeSpan.FromSeconds(1);

    public ObservableCollection<OverlayTileViewModel> Tiles { get; } = [];

    public int Columns { get => _columns; private set => Set(ref _columns, value); }
    public Brush Background { get => _background; private set => Set(ref _background, value); }
    public CornerRadius CornerRadius { get => _cornerRadius; private set => Set(ref _cornerRadius, value); }
    public Thickness Padding { get => _padding; private set => Set(ref _padding, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => Set(ref _fontFamily, value); }
    public bool HasVisibleTiles { get => _hasVisibleTiles; private set => Set(ref _hasVisibleTiles, value); }
    public bool IsMoving
    {
        get => _isMoving;
        set
        {
            if (Set(ref _isMoving, value)) OnPropertyChanged(nameof(ShowFrame));
        }
    }

    /// <summary>El overlay real enseña el diseño que se edita en SpotLay.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (Set(ref _isEditing, value)) OnPropertyChanged(nameof(ShowFrame));
        }
    }

    public bool ShowFrame => _isMoving || _isEditing;
    public string Banner { get => _banner; set => Set(ref _banner, value); }

    public void SetLayout(OverlayLayout layout, MetricSnapshot labels)
    {
        _layout = layout;
        Background = BrushCache.From(layout.BackgroundColor, "#0B0D12", layout.BackgroundOpacity / 100.0);
        CornerRadius = new CornerRadius(layout.CornerRadius * layout.Scale);
        Padding = new Thickness(Math.Round(8 * layout.Scale));
        FontFamily = new FontFamily(layout.FontFamily + ", Segoe UI");

        Tiles.Clear();
        // Tarjetas nuevas: FPS/FG se pintan en la primera lectura, no al segundo (si no, salía "—").
        _lastGameRefresh = DateTime.MinValue;
        foreach (var id in layout.Metrics)
        {
            var known = KnownMetrics.Find(id);
            var live = labels.Get(id);
            var label = known?.Label ?? live?.Label ?? id;
            var unit = known?.Unit ?? live?.Unit ?? "";
            Tiles.Add(new OverlayTileViewModel(id, layout.StyleFor(id), layout, label, unit));
        }
        ApplyArrangement();
    }

    /// <summary>
    /// <paramref name="gameStandIn"/>: sin juego delante, las métricas del juego muestran
    /// estos valores (los últimos medidos) o uno de ejemplo, para ver dónde quedan al editar.
    /// Null = comportamiento normal (sin juego no hay dato).
    /// </summary>
    public void Update(EngineSnapshot snapshot, bool hideGameMetricsWithoutGame, IReadOnlyDictionary<string, MetricValue>? gameStandIn)
    {
        if (_layout is null) return;
        var useSample = gameStandIn is not null && !snapshot.Game.IsGame;
        var now = DateTime.UtcNow;
        var refreshGame = now - _lastGameRefresh >= GameRefresh;
        if (refreshGame) _lastGameRefresh = now;
        foreach (var tile in Tiles)
        {
            var metric = useSample && tile.IsGameMetric
                ? gameStandIn!.GetValueOrDefault(tile.MetricId) ?? SampleGame.GetValueOrDefault(tile.MetricId) ?? snapshot.Metrics.Get(tile.MetricId)
                : snapshot.Metrics.Get(tile.MetricId);
            // Sin juego, o en Vulkan (sin FPS reales/generados), las metricas de juego
            // que no tienen dato se ocultan en vez de ocupar sitio con "—".
            tile.IsVisible = !(tile.IsGameMetric && hideGameMetricsWithoutGame && metric?.HasValue != true);
            if (!tile.IsGameMetric || refreshGame) tile.Update(metric);
        }
        ApplyArrangement();
    }

    /// <summary>Tarjetas cuyo sensor está en aviso (id de métrica → regla).</summary>
    public void ApplyAlerts(IReadOnlyDictionary<string, AlarmRule> alerts)
    {
        foreach (var tile in Tiles) tile.SetAlert(alerts.GetValueOrDefault(tile.MetricId));
    }

    private void ApplyArrangement()
    {
        if (_layout is null) return;
        var visible = Tiles.Count(t => t.IsVisible);
        HasVisibleTiles = visible > 0;
        Columns = _layout.Template switch
        {
            OverlayTemplates.Barra => Math.Max(1, visible),
            OverlayTemplates.Compacta => 1,
            _ => Math.Max(1, Math.Min(_layout.Columns, Math.Max(1, visible)))
        };
    }

    private static MetricValue Sample(string id, double? value, string? text = null)
    {
        var d = KnownMetrics.Find(id)!;
        return new MetricValue(id, d.Label, value, d.Unit, MetricCategory.Juego, Tr.T("Ejemplo"), text);
    }
}
