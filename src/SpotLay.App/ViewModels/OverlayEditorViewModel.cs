using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

/// <summary>Opción de una lista: el texto se traduce al crearla.</summary>
public sealed record Option(string Key, string Label)
{
    public string Label { get; init; } = Tr.T(Label);
}

public sealed class TargetItem(string key, string name)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class ChoiceItem(string key, string title, string description, string glyph, Action<string> apply, Func<string> current)
    : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = Tr.T(title);
    public string Description { get; } = Tr.T(description);
    public string Glyph { get; } = glyph;

    public bool IsSelected
    {
        get => current() == Key;
        set
        {
            if (value) apply(Key);
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

public sealed class BlockItem(MetricBlock block, Func<MetricBlock, bool> isOn, Action<MetricBlock, bool> toggle) : ObservableObject
{
    public MetricBlock Block { get; } = block;
    public string Title => Block.Title;
    public string Description => Block.Description;

    public bool IsOn
    {
        get => isOn(Block);
        set => toggle(Block, value);
    }

    public void Refresh() => OnPropertyChanged(nameof(IsOn));
}

public sealed record LayoutMetricItem(string Id, string Label, string Device);

public sealed class OverlayEditorViewModel : PageViewModel
{
    public override bool HasModes => true;

    public static IReadOnlyList<string> PaletteColors { get; } =
        ["#FFFFFF", "#A5B0B8", "#FFEB8A", "#FFB547", "#FF6B5C", "#3DDC97", "#5CC8FF", "#7C8CFF", "#C084FC", "#0B0D12"];

    private readonly AppController _c;
    private bool _suppress;
    private bool _selfEdit;
    private TargetItem? _selectedTarget;
    private LayoutMetricItem? _selectedMetric;
    // Métricas seleccionadas en la lista (Ctrl/Mayús + clic): lo que se cambia se aplica a todas.
    private List<LayoutMetricItem> _selection = [];
    // Estilo copiado con "Copiar estilo" (no se guarda: dura mientras SpotLay está abierto).
    private MetricStyle? _copiedStyle;
    private string _copiedName = "";

    public OverlayEditorViewModel(AppController controller)
        : base(Tr.T("Overlay"), Tr.T("Diseña lo que ves encima del juego. Los cambios se aplican al momento."))
    {
        _c = controller;
        Preview = new OverlayViewModel();

        Templates =
        [
            new(OverlayTemplates.Columna, Tr.T("Columna"), Tr.T("Tarjetas en dos columnas, con medidores de arco."), "", ApplyTemplate, () => L.Template),
            new(OverlayTemplates.Barra, Tr.T("Barra"), Tr.T("Una fila fina, ideal arriba o abajo de la pantalla."), "", ApplyTemplate, () => L.Template),
            new(OverlayTemplates.Compacta, Tr.T("Compacta"), Tr.T("Una columna estrecha, solo cifras."), "", ApplyTemplate, () => L.Template),
        ];
        Positions =
        [
            new(OverlayPositions.TopLeft, Tr.T("Arriba izquierda"), "", "", SetPosition, () => L.Position),
            new(OverlayPositions.TopCenter, Tr.T("Arriba centro"), "", "", SetPosition, () => L.Position),
            new(OverlayPositions.TopRight, Tr.T("Arriba derecha"), "", "", SetPosition, () => L.Position),
            new(OverlayPositions.BottomLeft, Tr.T("Abajo izquierda"), "", "", SetPosition, () => L.Position),
            new(OverlayPositions.BottomCenter, Tr.T("Abajo centro"), "", "", SetPosition, () => L.Position),
            new(OverlayPositions.BottomRight, Tr.T("Abajo derecha"), "", "", SetPosition, () => L.Position),
        ];
        Blocks = MetricBlocks.All.Select(b => new BlockItem(b, IsBlockOn, ToggleBlock)).ToList();

        PositionOptions =
        [
            new(OverlayPositions.TopLeft, Tr.T("Arriba izquierda")), new(OverlayPositions.TopCenter, Tr.T("Arriba centro")),
            new(OverlayPositions.TopRight, Tr.T("Arriba derecha")), new(OverlayPositions.Center, Tr.T("Centro")),
            new(OverlayPositions.BottomLeft, Tr.T("Abajo izquierda")), new(OverlayPositions.BottomCenter, Tr.T("Abajo centro")),
            new(OverlayPositions.BottomRight, Tr.T("Abajo derecha")), new(OverlayPositions.Custom, Tr.T("Libre"))
        ];
        TemplateOptions =
        [
            new(OverlayTemplates.Columna, Tr.T("Columna / cuadrícula")), new(OverlayTemplates.Barra, Tr.T("Barra (una fila)")),
            new(OverlayTemplates.Compacta, Tr.T("Compacta (una columna)"))
        ];
        VisualizationOptions =
        [
            new(Visualizations.Number, Tr.T("Solo número")), new(Visualizations.Gauge, Tr.T("Arco")), new(Visualizations.Ring, Tr.T("Anillo")),
            new(Visualizations.BarHorizontal, Tr.T("Barra horizontal")), new(Visualizations.BarVertical, Tr.T("Barra vertical")),
            new(Visualizations.Line, Tr.T("Línea temporal"))
        ];
        EffectOptions = [new("none", Tr.T("Ninguno")), new("shadow", Tr.T("Sombra"))];
        UnitPositionOptions =
        [
            new(UnitPositions.Auto, Tr.T("Automática")), new(UnitPositions.Side, Tr.T("Al lado")),
            new(UnitPositions.Below, Tr.T("Debajo")), new(UnitPositions.Hidden, Tr.T("Oculta"))
        ];
        FontOptions = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().Order().ToList();

        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedMetric is not null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => SelectedMetric is not null);
        RemoveMetricCommand = new RelayCommand(RemoveSelected, () => SelectedMetric is not null);
        ApplyStyleToAllCommand = new RelayCommand(ApplyStyleToAll, () => SelectedMetric is not null);
        CopyStyleCommand = new RelayCommand(CopyStyle, () => SelectedMetric is not null && !IsMultiSelection);
        PasteStyleCommand = new RelayCommand(PasteStyle, () => SelectedMetric is not null && _copiedStyle is not null);
        UnitLikeValueCommand = new RelayCommand(() => EditStyle(s => s.UnitColor = s.ValueColor), () => SelectedMetric is not null);
        PickColorCommand = new RelayCommand(p => PickColor(p as string));

        QuickPlaceCommand = new RelayCommand(p =>
        {
            if (p is not string key || key.Split(',') is not [var ax, var ay]) return;
            OverlayPlacement.SetQuick(L, int.Parse(ax), int.Parse(ay), Dpi);
            _c.NotifyPlacementEdited();
        });
        MapDragged = (x, y, _) =>
        {
            var screen = MapScreen;
            OverlayPlacement.SetAnchored(L, screen, MapBoxWidth, MapBoxHeight, screen.Left + x, screen.Top + y);
            _c.NotifyPlacementEdited();
        };
        controller.PlacementEdited += RefreshMap;

        controller.LayoutChanged += OnLayoutChanged;
        controller.MetricSelectRequested += id =>
        {
            if (LayoutMetrics.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } item) SelectedMetric = item;
        };
        controller.SnapshotUpdated += s =>
        {
            Preview.Update(s, _c.Config.HideGameMetricsWithoutGame, _c.GameStandIn);
            TilePreview.Update(s, false, _c.GameStandIn);
            if (_c.OverlayMonitor == _mapMonitor) return;
            _mapMonitor = _c.OverlayMonitor;
            RefreshMap();
        };
        controller.SettingsChanged += () => OnPropertyChanged(nameof(IsAdvanced));
        Reload();
    }

    private OverlayLayout L => _c.EditingLayout;

    // ---- Mini-monitor -----------------------------------------------------------
    private double _previewWidth = 200, _previewHeight = 300;
    private IntPtr _mapMonitor = (IntPtr)(-1);

    public ICommand QuickPlaceCommand { get; }
    public Action<int, int, bool> MapDragged { get; }
    public int MapScreenWidth => MapScreen.Width;
    public int MapScreenHeight => MapScreen.Height;
    public int MapBoxWidth => (int)Math.Round(_previewWidth * Dpi);
    public int MapBoxHeight => (int)Math.Round(_previewHeight * Dpi);
    public int MapX => Placed.X - MapScreen.Left;
    public int MapY => Placed.Y - MapScreen.Top;
    public int MapSnapMargin => (int)Math.Round(OverlayPlacement.QuickMargin * Dpi);

    private static double Dpi => System.Windows.Application.Current?.MainWindow is { } w
        ? System.Windows.Media.VisualTreeHelper.GetDpi(w).DpiScaleX : 1.0;

    // El monitor donde está el juego (editando fuera de él, el del último juego; si no, el principal).
    private OverlayPlacement.Screen MapScreen => SpotLay.App.Overlay.OverlayWindow.ScreenOf(_c.OverlayMonitor);

    private (int X, int Y) Placed => OverlayPlacement.Compute(L, MapScreen, MapBoxWidth, MapBoxHeight, Dpi);

    /// <summary>La vista previa dice cuánto mide el overlay (en DIP, sin la escala del Viewbox).</summary>
    public void SetPreviewSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _previewWidth = width;
        _previewHeight = height;
        RefreshMap();
    }

    private void RefreshMap() => OnPropertiesChanged(nameof(MapScreenWidth), nameof(MapScreenHeight), nameof(MapBoxWidth),
        nameof(MapBoxHeight), nameof(MapX), nameof(MapY), nameof(MapSnapMargin));

    public OverlayViewModel Preview { get; }
    /// <summary>Solo la métrica seleccionada, con su estilo, junto a sus controles.</summary>
    public OverlayViewModel TilePreview { get; } = new();
    public bool IsAdvanced => _c.Config.Mode == AppModes.Avanzado;
    public IReadOnlyList<ChoiceItem> Templates { get; }
    public IReadOnlyList<ChoiceItem> Positions { get; }
    public IReadOnlyList<BlockItem> Blocks { get; }
    public IReadOnlyList<Option> PositionOptions { get; }
    public IReadOnlyList<Option> TemplateOptions { get; }
    public IReadOnlyList<Option> VisualizationOptions { get; }
    public IReadOnlyList<Option> EffectOptions { get; }
    public IReadOnlyList<Option> UnitPositionOptions { get; }
    public IReadOnlyList<string> FontOptions { get; }
    public ObservableCollection<TargetItem> Targets { get; } = [];
    public ObservableCollection<LayoutMetricItem> LayoutMetrics { get; } = [];

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveMetricCommand { get; }
    public ICommand ApplyStyleToAllCommand { get; }
    public ICommand CopyStyleCommand { get; }
    public ICommand PasteStyleCommand { get; }
    public ICommand UnitLikeValueCommand { get; }
    public ICommand PickColorCommand { get; }

    public TargetItem? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is null || !Set(ref _selectedTarget, value) || _suppress) return;
            _c.EditingTarget = value.Key;
        }
    }

    public string EditingHint => _c.EditingTarget == AppController.GlobalTarget
        ? Tr.T("Se usa en todos los juegos que no tienen perfil propio.")
        : Tr.F("Solo se usa cuando juegas a {0}.", _c.EditingTarget);

    // ---- Diseño general -------------------------------------------------------
    public string Template { get => L.Template; set => Edit(() => L.Template = value); }
    public double Scale { get => L.Scale; set => Edit(() => L.Scale = Math.Round(value, 2)); }
    public int BackgroundOpacity { get => L.BackgroundOpacity; set => Edit(() => L.BackgroundOpacity = value); }
    public int Columns { get => L.Columns; set => Edit(() => L.Columns = Math.Clamp(value, 1, 12)); }
    public int TileWidth { get => L.TileWidth; set => Edit(() => L.TileWidth = Math.Clamp(value, 70, 400)); }
    public int Gap { get => L.Gap; set => Edit(() => L.Gap = Math.Clamp(value, 0, 40)); }
    public int CornerRadius { get => L.CornerRadius; set => Edit(() => L.CornerRadius = Math.Clamp(value, 0, 40)); }
    public string Position { get => L.Position; set => Edit(() => L.Position = value); }
    public bool IsCustomPosition => L.Position == OverlayPositions.Custom;
    public int OffsetX { get => L.OffsetX; set => Edit(() => L.OffsetX = Math.Clamp(value, 0, 2000)); }
    public int OffsetY { get => L.OffsetY; set => Edit(() => L.OffsetY = Math.Clamp(value, 0, 2000)); }
    public double FreeX { get => L.FreeX * 100; set => Edit(() => L.FreeX = Math.Clamp(value / 100, 0, 1)); }
    public double FreeY { get => L.FreeY * 100; set => Edit(() => L.FreeY = Math.Clamp(value / 100, 0, 1)); }
    public string BackgroundColor { get => L.BackgroundColor; set => EditColor(value, v => L.BackgroundColor = v); }
    public string LabelColor { get => L.LabelColor; set => EditColor(value, v => L.LabelColor = v); }
    public string ValueColor { get => L.ValueColor; set => EditColor(value, v => L.ValueColor = v); }
    public string UnitColor { get => L.UnitColor; set => EditColor(value, v => L.UnitColor = v); }
    public string FontFamily { get => L.FontFamily; set => Edit(() => L.FontFamily = value); }
    public bool FontBold { get => L.FontBold; set => Edit(() => L.FontBold = value); }
    public string ValueEffect { get => L.ValueEffect; set => Edit(() => L.ValueEffect = value); }
    public string SizeText => $"{Math.Round(L.Scale * 100)} %";

    /// <summary>Lo que hay en "Diseño general", para verlo sin abrirlo.</summary>
    public string DesignSummary
    {
        get
        {
            var form = TemplateOptions.FirstOrDefault(o => o.Key == L.Template)?.Label ?? L.Template;
            var columns = L.Template == OverlayTemplates.Columna ? Tr.F(" · {0} columnas", L.Columns) : "";
            return Tr.F("{0}{1} · tarjeta {2} px · tamaño {3} · {4} · fondo {5} %", form, columns, L.TileWidth, SizeText, L.FontFamily, L.BackgroundOpacity);
        }
    }

    private static bool _designExpanded;

    /// <summary>"Diseño general" abierto o cerrado; se recuerda al cambiar de página.</summary>
    public bool DesignExpanded
    {
        get => _designExpanded;
        set
        {
            if (_designExpanded == value) return;
            _designExpanded = value;
            OnPropertyChanged();
        }
    }

    // ---- Métrica seleccionada (Avanzado) --------------------------------------
    public LayoutMetricItem? SelectedMetric
    {
        get => _selectedMetric;
        set
        {
            if (!Set(ref _selectedMetric, value)) return;
            // Desde el código la selección pasa a ser solo esta.
            if (value is null) _selection = [];
            else if (!_selection.Contains(value)) _selection = [value];
            RaiseStyle();
        }
    }

    /// <summary>La lista avisa de lo seleccionado (varias con Ctrl/Mayús + clic).</summary>
    public void SetSelection(IEnumerable<LayoutMetricItem> items)
    {
        _selection = items.ToList();
        var primary = _selectedMetric is not null && _selection.Contains(_selectedMetric) ? _selectedMetric : _selection.FirstOrDefault();
        if (!ReferenceEquals(primary, _selectedMetric))
        {
            _selectedMetric = primary;
            OnPropertyChanged(nameof(SelectedMetric));
        }
        RaiseStyle();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshTilePreview()
    {
        var copy = L.Clone();
        copy.Metrics.Clear();
        if (SelectedMetric is not null) copy.Metrics.Add(SelectedMetric.Id);
        copy.Columns = 1;
        TilePreview.SetLayout(copy, _c.Snapshot.Metrics);
        TilePreview.Update(_c.Snapshot, false, _c.GameStandIn);
    }

    public bool HasSelectedMetric => SelectedMetric is not null;
    public bool IsMultiSelection => _selection.Count > 1;
    public bool IsSingleSelection => !IsMultiSelection;
    public string SelectionText => IsMultiSelection
        ? Tr.F("{0} métricas seleccionadas: lo que cambies se aplica a todas.", _selection.Count)
        : Tr.T("Ctrl + clic o Mayús + clic para elegir varias.");
    public string CopiedText => _copiedStyle is null ? "" : Tr.F("Copiado: {0}", _copiedName);
    private MetricStyle? S => SelectedMetric is null ? null : L.StyleFor(SelectedMetric.Id);
    private IEnumerable<MetricStyle> SelectedStyles => _selection.Count > 0
        ? _selection.Select(i => L.StyleFor(i.Id))
        : S is { } s ? [s] : [];

    // Con varias seleccionadas, si no coinciden se ve vacío.
    private string Shared(Func<MetricStyle, string?> get)
    {
        var values = SelectedStyles.Select(get).Distinct().Take(2).ToList();
        return values.Count == 1 ? values[0] ?? "" : "";
    }

    public string StyleLabel
    {
        get => IsMultiSelection ? "" : S?.Label ?? "";
        set
        {
            if (_suppress || SelectedMetric is not { } current || (S?.Label ?? "") == value) return;
            EditStyle(s => s.Label = value);
            RebuildMetricList();
            SelectedMetric = LayoutMetrics.FirstOrDefault(m => m.Id == current.Id);
        }
    }
    public string StyleVisualization { get => S?.Visualization ?? Visualizations.Number; set => EditStyle(s => s.Visualization = value); }
    public string StyleUnitPosition { get => S?.UnitPosition ?? UnitPositions.Auto; set => EditStyle(s => s.UnitPosition = value); }
    public string StyleLabelColor { get => Shared(s => s.LabelColor); set => EditStyleColor(value, (s, v) => s.LabelColor = v); }
    public string StyleValueColor { get => Shared(s => s.ValueColor); set => EditStyleColor(value, (s, v) => s.ValueColor = v); }
    public string StyleUnitColor { get => Shared(s => s.UnitColor); set => EditStyleColor(value, (s, v) => s.UnitColor = v); }
    public int StyleLabelSize { get => S?.LabelSize ?? 12; set => EditStyle(s => s.LabelSize = Math.Clamp(value, 6, 60)); }
    public int StyleValueSize { get => S?.ValueSize ?? 20; set => EditStyle(s => s.ValueSize = Math.Clamp(value, 6, 96)); }
    public int StyleUnitSize { get => S?.UnitSize ?? 11; set => EditStyle(s => s.UnitSize = Math.Clamp(value, 6, 60)); }
    public bool StyleAutoScale { get => S?.AutoScale ?? true; set => EditStyle(s => s.AutoScale = value); }
    public double StyleMin { get => S?.Min ?? 0; set => EditStyle(s => s.Min = value); }
    public double StyleMax { get => S?.Max ?? 100; set => EditStyle(s => s.Max = value); }

    // ---- Lógica -----------------------------------------------------------------
    private void OnLayoutChanged()
    {
        RefreshMap();
        if (_selfEdit)
        {
            Preview.SetLayout(L, _c.Snapshot.Metrics);
            return;
        }
        Reload();
    }

    private void Reload()
    {
        _suppress = true;
        try
        {
            Targets.Clear();
            Targets.Add(new TargetItem(AppController.GlobalTarget, Tr.T("Overlay global")));
            foreach (var key in _c.Config.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
                Targets.Add(new TargetItem(key, key));
            SelectedTarget = Targets.FirstOrDefault(t => t.Key == _c.EditingTarget) ?? Targets[0];

            var previous = SelectedMetric?.Id;
            RebuildMetricList();
            // Siempre hay una seleccionada: un panel vacío parecía roto.
            SelectedMetric = LayoutMetrics.FirstOrDefault(m => m.Id == previous) ?? LayoutMetrics.FirstOrDefault();
            RefreshAll();
            Preview.SetLayout(L, _c.Snapshot.Metrics);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RebuildMetricList()
    {
        LayoutMetrics.Clear();
        foreach (var id in L.Metrics)
        {
            var known = KnownMetrics.Find(id);
            var live = _c.Snapshot.Metrics.Get(id);
            // Si la etiqueta se ha renombrado (p. ej. "CPU Fan"), la lista usa ese nombre.
            var custom = L.Styles.TryGetValue(id, out var st) && !string.IsNullOrWhiteSpace(st.Label) ? st.Label.Trim() : null;
            LayoutMetrics.Add(new LayoutMetricItem(id, custom ?? known?.Label ?? live?.Label ?? id,
                known is not null ? known.Description : live?.Device ?? Tr.T("Sin lectura ahora mismo")));
        }
    }

    private void RefreshAll()
    {
        foreach (var t in Templates) t.Refresh();
        foreach (var p in Positions) p.Refresh();
        foreach (var b in Blocks) b.Refresh();
        OnPropertiesChanged(nameof(Template), nameof(Scale), nameof(SizeText), nameof(BackgroundOpacity), nameof(Columns),
            nameof(TileWidth), nameof(Gap), nameof(CornerRadius), nameof(Position), nameof(IsCustomPosition), nameof(OffsetX),
            nameof(OffsetY), nameof(FreeX), nameof(FreeY), nameof(BackgroundColor), nameof(LabelColor), nameof(ValueColor),
            nameof(UnitColor), nameof(FontFamily), nameof(FontBold), nameof(ValueEffect), nameof(EditingHint), nameof(IsAdvanced),
            nameof(DesignSummary));
        RaiseStyle();
    }

    private void RaiseStyle()
    {
        RefreshTilePreview();
        RaiseStyleProperties();
    }

    private void RaiseStyleProperties() => OnPropertiesChanged(nameof(HasSelectedMetric), nameof(IsMultiSelection), nameof(IsSingleSelection),
        nameof(SelectionText), nameof(CopiedText), nameof(StyleLabel), nameof(StyleVisualization), nameof(StyleUnitPosition),
        nameof(StyleLabelColor), nameof(StyleValueColor), nameof(StyleUnitColor), nameof(StyleLabelSize), nameof(StyleValueSize),
        nameof(StyleUnitSize), nameof(StyleAutoScale), nameof(StyleMin), nameof(StyleMax));

    private void Edit(Action change)
    {
        if (_suppress) return;
        change();
        Commit();
    }

    private void Commit()
    {
        _selfEdit = true;
        try
        {
            _c.NotifyLayoutEdited();
        }
        finally
        {
            _selfEdit = false;
        }
        RefreshTilePreview();
        foreach (var t in Templates) t.Refresh();
        foreach (var p in Positions) p.Refresh();
        foreach (var b in Blocks) b.Refresh();
        // Las cifras junto a las barras (opacidad, ancho…) se refrescan al moverlas.
        OnPropertiesChanged(nameof(SizeText), nameof(IsCustomPosition), nameof(Template), nameof(Position), nameof(Columns),
            nameof(Scale), nameof(BackgroundOpacity), nameof(TileWidth), nameof(Gap), nameof(CornerRadius),
            nameof(BackgroundColor), nameof(LabelColor), nameof(ValueColor), nameof(UnitColor), nameof(DesignSummary));
    }

    private void EditColor(string value, Action<string> apply)
    {
        if (!BrushCache.TryParse(value, out _)) return;
        Edit(() => apply(value.Trim()));
    }

    private void EditStyle(Action<MetricStyle> change)
    {
        if (_suppress || S is null) return;
        foreach (var style in SelectedStyles.ToList()) change(style);
        Commit();
        RaiseStyle();
    }

    private void EditStyleColor(string value, Action<MetricStyle, string?> apply)
    {
        if (string.IsNullOrWhiteSpace(value)) EditStyle(s => apply(s, null));
        else if (BrushCache.TryParse(value, out _)) EditStyle(s => apply(s, value.Trim()));
    }

    private void ApplyTemplate(string key)
    {
        Edit(() =>
        {
            L.Template = key;
            var gauges = key == OverlayTemplates.Columna;
            L.Columns = key switch { OverlayTemplates.Compacta => 1, OverlayTemplates.Columna => Math.Max(2, L.Columns == 1 ? 2 : L.Columns), _ => L.Columns };
            L.TileWidth = key switch { OverlayTemplates.Barra => 104, OverlayTemplates.Compacta => 150, _ => 130 };
            foreach (var id in L.Metrics)
            {
                var style = L.StyleFor(id);
                style.Visualization = gauges && id is KnownMetrics.GpuLoad or KnownMetrics.CpuLoad or KnownMetrics.GpuPower or KnownMetrics.CpuPower
                    ? Visualizations.Gauge
                    : Visualizations.Number;
            }
        });
        RefreshAll();
    }

    private void SetPosition(string key) => Edit(() => L.Position = key);

    private bool IsBlockOn(MetricBlock block) => block.Metrics.Any(m => L.Metrics.Contains(m, StringComparer.OrdinalIgnoreCase));

    private void ToggleBlock(MetricBlock block, bool on)
    {
        Edit(() =>
        {
            if (on)
            {
                var missing = block.Metrics.Where(m => !L.Metrics.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
                // FPS y Frame Generation van delante: es lo primero que se mira.
                if (block.Id == "fg") L.Metrics.InsertRange(0, missing);
                else L.Metrics.AddRange(missing);
            }
            else
            {
                L.Metrics.RemoveAll(m => block.Metrics.Contains(m, StringComparer.OrdinalIgnoreCase));
            }
        });
        RebuildMetricList();
    }

    private void Move(int delta)
    {
        if (SelectedMetric is null) return;
        var index = L.Metrics.FindIndex(m => m.Equals(SelectedMetric.Id, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= L.Metrics.Count) return;
        var id = L.Metrics[index];
        L.Metrics.RemoveAt(index);
        L.Metrics.Insert(target, id);
        Commit();
        var keep = SelectedMetric.Id;
        RebuildMetricList();
        SelectedMetric = LayoutMetrics.FirstOrDefault(m => m.Id == keep);
    }

    private void RemoveSelected()
    {
        if (SelectedMetric is null) return;
        var ids = _selection.Count > 0 ? _selection.Select(i => i.Id).ToList() : [SelectedMetric.Id];
        L.Metrics.RemoveAll(m => ids.Contains(m, StringComparer.OrdinalIgnoreCase));
        Commit();
        RebuildMetricList();
        SelectedMetric = LayoutMetrics.FirstOrDefault();
    }

    private void ApplyStyleToAll()
    {
        if (S is not { } source) return;
        foreach (var id in L.Metrics)
        {
            var style = L.StyleFor(id);
            if (ReferenceEquals(style, source)) continue;
            CopyLook(source, style, visualization: !KnownMetrics.IsGameMetric(id));
        }
        Commit();
    }

    private void CopyStyle()
    {
        if (S is not { } source || SelectedMetric is null) return;
        _copiedStyle = new MetricStyle();
        CopyLook(source, _copiedStyle, visualization: true);
        _copiedName = SelectedMetric.Label;
        OnPropertyChanged(nameof(CopiedText));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Pega colores, tamaños y unidad. No el gráfico (un arco de RPM no debe volverse número),
    /// ni el texto de la etiqueta ni la escala (°C no es RPM).
    /// </summary>
    private void PasteStyle()
    {
        if (_copiedStyle is not { } source) return;
        EditStyle(s => CopyLook(source, s, visualization: false));
    }

    private static void CopyLook(MetricStyle from, MetricStyle to, bool visualization)
    {
        to.LabelSize = from.LabelSize;
        to.ValueSize = from.ValueSize;
        to.UnitSize = from.UnitSize;
        to.LabelColor = from.LabelColor;
        to.ValueColor = from.ValueColor;
        to.UnitColor = from.UnitColor;
        to.UnitPosition = from.UnitPosition;
        if (visualization) to.Visualization = from.Visualization;
    }

    private void PickColor(string? parameter)
    {
        var parts = parameter?.Split('|');
        if (parts is not { Length: 2 }) return;
        var hex = parts[1];
        switch (parts[0])
        {
            case "bg": BackgroundColor = hex; break;
            case "label": LabelColor = hex; break;
            case "value": ValueColor = hex; break;
            case "unit": UnitColor = hex; break;
            case "style-value": StyleValueColor = hex; break;
            case "style-label": StyleLabelColor = hex; break;
            case "style-unit": StyleUnitColor = hex; break;
        }
        OnPropertiesChanged(nameof(BackgroundColor), nameof(LabelColor), nameof(ValueColor), nameof(UnitColor),
            nameof(StyleValueColor), nameof(StyleLabelColor), nameof(StyleUnitColor));
    }
}
