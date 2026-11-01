using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SpotLay.App.Alerts;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core;
using SpotLay.Core.Alarms;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

/// <summary>Un sensor con su aviso y su apagado de emergencia.</summary>
public sealed class AlarmRowViewModel : ObservableObject
{
    private readonly AppController _c;
    private readonly AlertManager _alerts;
    private readonly Action<AlarmRowViewModel> _remove;
    private string _value = Tr.T("sin lectura");
    private string _label;
    private string _unit;
    private bool _isAlerting;
    private bool _isExpanded;

    public AlarmRowViewModel(AppController controller, AlertManager alerts, string id, string label, string device, string unit, bool isDefault,
        Action<AlarmRowViewModel> remove)
    {
        _c = controller;
        _alerts = alerts;
        _remove = remove;
        Id = id;
        _label = label;
        Device = device;
        _unit = unit;
        IsDefault = isDefault;
        TestCommand = new RelayCommand(() => _alerts.TestPopup(Id, Current));
        RemoveCommand = new RelayCommand(() => _remove(this), () => !IsDefault);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        PickColorCommand = new RelayCommand(p => PickColor(p as string));
    }

    public string Id { get; }
    public string Label { get => _label; private set => Set(ref _label, value); }
    public string Device { get; }
    public string Unit { get => _unit; private set => Set(ref _unit, value); }
    public bool IsDefault { get; }
    public string Value { get => _value; private set => Set(ref _value, value); }
    public bool IsAlerting { get => _isAlerting; private set => Set(ref _isAlerting, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsAdvanced => _c.Config.Mode == AppModes.Avanzado;
    public ICommand TestCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ToggleExpandCommand { get; }
    public ICommand PickColorCommand { get; }

    private AlarmRule? Existing => _c.Config.Safety.Rules.GetValueOrDefault(Id);
    private AlarmRule Current => Existing ?? DefaultFor(Id);

    // ---- Esencial ----
    public bool AlertEnabled { get => Existing?.AlertEnabled == true; set => Edit(r => r.AlertEnabled = value); }
    public string AlertThreshold { get => FanChannelViewModel.Num(Current.AlertThreshold); set => EditNumber(value, (r, v) => r.AlertThreshold = v); }
    public bool ShutdownEnabled { get => Existing?.ShutdownEnabled == true; set => Edit(r => r.ShutdownEnabled = value); }
    public string ShutdownThreshold { get => FanChannelViewModel.Num(Current.ShutdownThreshold); set => EditNumber(value, (r, v) => r.ShutdownThreshold = v); }

    public string Summary
    {
        get
        {
            var r = Current;
            var parts = new List<string>();
            if (r.AlertEnabled) parts.Add(Tr.F("aviso {0} {1}", (r.AlertCondition == AlarmConditions.Below ? "<" : "≥"), FanChannelViewModel.Num(r.AlertThreshold)));
            if (r.ShutdownEnabled) parts.Add(Tr.F("apagado {0} {1} tras {2} s", (r.ShutdownCondition == AlarmConditions.Below ? "<" : "≥"), FanChannelViewModel.Num(r.ShutdownThreshold), r.ShutdownDelaySeconds));
            return parts.Count == 0 ? Tr.T("Sin aviso ni apagado") : string.Join(" · ", parts);
        }
    }

    // ---- Avanzado: aviso ----
    public bool AlertBelow { get => Current.AlertCondition == AlarmConditions.Below; set => Edit(r => r.AlertCondition = value ? AlarmConditions.Below : AlarmConditions.Above); }
    public string AlertHysteresis { get => FanChannelViewModel.Num(Current.AlertHysteresis); set => EditNumber(value, (r, v) => r.AlertHysteresis = v); }
    public bool AlertPopup { get => Current.AlertPopup; set => Edit(r => r.AlertPopup = value); }
    public string AlertText { get => Current.AlertText; set => Edit(r => r.AlertText = value ?? ""); }
    public string AlertTextColor { get => Current.AlertTextColor; set => EditColor(value, (r, v) => r.AlertTextColor = v); }
    public string AlertBackground { get => Current.AlertBackground; set => EditColor(value, (r, v) => r.AlertBackground = v); }
    public string AlertTileColor { get => Current.AlertTileColor; set => EditColor(value, (r, v) => r.AlertTileColor = v); }
    public int AlertLabelSize { get => Current.AlertLabelSize; set => Edit(r => r.AlertLabelSize = value); }
    public int AlertValueSize { get => Current.AlertValueSize; set => Edit(r => r.AlertValueSize = value); }
    public int AlertTileScale { get => Current.AlertTileScale; set => Edit(r => r.AlertTileScale = value); }
    public string AlertPosition
    {
        get => Current.AlertPosition;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Current.AlertPosition) Edit(r => r.AlertPosition = value);
        }
    }
    public double AlertPositionX { get => Current.AlertPositionX * 100; set => Edit(r => r.AlertPositionX = value / 100); }
    public double AlertPositionY { get => Current.AlertPositionY * 100; set => Edit(r => r.AlertPositionY = value / 100); }
    public bool IsCustomPosition => Current.AlertPosition == OverlayPositions.Custom;

    // ---- Avanzado: apagado ----
    public bool ShutdownBelow { get => Current.ShutdownCondition == AlarmConditions.Below; set => Edit(r => r.ShutdownCondition = value ? AlarmConditions.Below : AlarmConditions.Above); }
    public string ShutdownHysteresis { get => FanChannelViewModel.Num(Current.ShutdownHysteresis); set => EditNumber(value, (r, v) => r.ShutdownHysteresis = v); }
    public string ShutdownDelaySeconds
    {
        get => Current.ShutdownDelaySeconds.ToString();
        set => EditNumber(value, (r, v) => r.ShutdownDelaySeconds = (int)Math.Round(v));
    }
    public bool ShutdownAutoCancel { get => Current.ShutdownAutoCancel; set => Edit(r => r.ShutdownAutoCancel = value); }

    public void Refresh(MetricSnapshot snapshot, IReadOnlyDictionary<string, AlarmRule> active)
    {
        var metric = snapshot.Get(Id);
        if (metric is not null)
        {
            Value = AlertManager.Format(metric);
            if (string.IsNullOrEmpty(Unit) && !string.IsNullOrEmpty(metric.Unit)) Unit = metric.Unit;
            if (Label == Id && !string.IsNullOrEmpty(metric.Label)) Label = metric.Label;
        }
        else
        {
            Value = Tr.T("sin lectura");
        }
        IsAlerting = active.ContainsKey(Id);
    }

    public void RefreshAll() => OnPropertyChanged(string.Empty);

    private void Edit(Action<AlarmRule> change)
    {
        var isNew = Existing is null;
        var rule = _c.Config.Safety.RuleFor(Id);
        if (isNew) CopyDefaults(rule, DefaultFor(Id));
        var before = System.Text.Json.JsonSerializer.Serialize(rule);
        change(rule);
        SafetyLimits.Normalize(rule);
        // Un control que reenvía el mismo valor al cargarse no debe crear ni guardar nada.
        if (System.Text.Json.JsonSerializer.Serialize(rule) == before)
        {
            if (isNew) _c.Config.Safety.Rules.Remove(Id);
            return;
        }
        _c.NotifySafetyChanged();
        OnPropertyChanged(string.Empty);
    }

    private void EditNumber(string text, Action<AlarmRule, double> set)
    {
        if (FanChannelViewModel.TryParse(text, out var value)) Edit(r => set(r, value));
        else OnPropertyChanged(string.Empty);
    }

    private void EditColor(string text, Action<AlarmRule, string> set)
    {
        if (BrushCache.TryParse(text, out _)) Edit(r => set(r, text.Trim()));
        else OnPropertyChanged(string.Empty);
    }

    private void PickColor(string? parameter)
    {
        var parts = parameter?.Split('|');
        if (parts is not { Length: 2 }) return;
        switch (parts[0])
        {
            case "text": AlertTextColor = parts[1]; break;
            case "bg": AlertBackground = parts[1]; break;
            case "tile": AlertTileColor = parts[1]; break;
        }
    }

    /// <summary>Umbrales de partida sensatos según el sensor (SpotLay 10 usaba 90/95 para todo).</summary>
    private static AlarmRule DefaultFor(string id)
    {
        var (alert, shutdown) = id switch
        {
            KnownMetrics.GpuTemp => (83d, 90d),
            KnownMetrics.GpuHotSpot => (95d, 105d),
            KnownMetrics.GpuMemoryTemp => (95d, 105d),
            KnownMetrics.CpuTemp => (90d, 98d),
            _ => (90d, 95d)
        };
        return new AlarmRule { AlertThreshold = alert, ShutdownThreshold = shutdown };
    }

    private static void CopyDefaults(AlarmRule target, AlarmRule source)
    {
        target.AlertThreshold = source.AlertThreshold;
        target.ShutdownThreshold = source.ShutdownThreshold;
    }
}

public sealed class AlertsViewModel : PageViewModel
{
    public override bool HasModes => true;

    private static readonly string[] DefaultSensors = [KnownMetrics.GpuTemp, KnownMetrics.GpuHotSpot, KnownMetrics.GpuMemoryTemp, KnownMetrics.CpuTemp];

    private readonly AppController _c;
    private readonly AlertManager _alerts;
    private readonly Dictionary<string, AlarmRowViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastValues;
    private DateTime _lastSensors;
    private string? _sensorToAdd;
    private bool _isCapturingHotkey;

    public AlertsViewModel(AppController controller, AlertManager alerts)
        : base(Tr.T("Alertas y seguridad"), Tr.T("Avisos por sensor encima del juego y apagado de emergencia."))
    {
        _c = controller;
        _alerts = alerts;
        SimulateCommand = new RelayCommand(() => _alerts.StartSimulation(), () => !_alerts.Shutdown.Pending);
        CancelCommand = new RelayCommand(() => _alerts.CancelShutdown(Tr.T("Apagado cancelado desde el panel")), () => _alerts.Shutdown.Pending);
        AddSensorCommand = new RelayCommand(AddSelected, () => !string.IsNullOrEmpty(SensorToAdd));
        CaptureHotkeyCommand = new RelayCommand(() => IsCapturingHotkey = true);
        DisableHotkeyCommand = new RelayCommand(() =>
        {
            _c.Config.Safety.CancelHotkeyKey = 0;
            _c.NotifySafetyChanged();
            OnPropertiesChanged(nameof(HotkeyText));
        });

        foreach (var id in DefaultSensors) AddRow(id, isDefault: true);
        foreach (var id in _c.Config.Safety.Rules.Where(r => !r.Value.IsEmpty).Select(r => r.Key)) AddRow(id, isDefault: false);

        controller.SnapshotUpdated += OnSnapshot;
        controller.SettingsChanged += () =>
        {
            OnPropertyChanged(nameof(IsAdvanced));
            foreach (var row in Rows) row.RefreshAll();
        };
        controller.SafetyChanged += () =>
        {
            foreach (var id in _c.Config.Safety.Rules.Where(r => !r.Value.IsEmpty).Select(r => r.Key)) AddRow(id, isDefault: false);
            OnPropertiesChanged(nameof(HotkeyText), nameof(CooldownSeconds), nameof(ArmedCount),
                nameof(WindowWidth), nameof(WindowHeight), nameof(WindowOpacity), nameof(WindowRadius));
        };
        alerts.PropertyChanged += OnAlertsChanged;
    }

    public ObservableCollection<AlarmRowViewModel> Rows { get; } = [];
    public ObservableCollection<Option> AddableSensors { get; } = [];
    public IReadOnlyList<Option> PositionOptions { get; } =
    [
        new("auto", Tr.T("Automática (arriba, apiladas)")), new(OverlayPositions.TopLeft, Tr.T("Arriba izquierda")),
        new(OverlayPositions.TopCenter, Tr.T("Arriba centro")), new(OverlayPositions.TopRight, Tr.T("Arriba derecha")),
        new(OverlayPositions.Center, Tr.T("Centro")), new(OverlayPositions.BottomLeft, Tr.T("Abajo izquierda")),
        new(OverlayPositions.BottomCenter, Tr.T("Abajo centro")), new(OverlayPositions.BottomRight, Tr.T("Abajo derecha")),
        new(OverlayPositions.Custom, Tr.T("Libre"))
    ];
    public static IReadOnlyList<string> PaletteColors { get; } =
        ["#FFFFFF", "#FFEB8A", "#FFB547", "#FF6B5C", "#FF3030", "#B3132B", "#7A1020", "#1E090E", "#0B0D12", "#3DDC97"];

    public ICommand SimulateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddSensorCommand { get; }
    public ICommand CaptureHotkeyCommand { get; }
    public ICommand DisableHotkeyCommand { get; }
    public bool IsAdvanced => _c.Config.Mode == AppModes.Avanzado;

    public string? SensorToAdd { get => _sensorToAdd; set => Set(ref _sensorToAdd, value); }

    public ShutdownState Shutdown => _alerts.Shutdown;
    public bool IsPending => _alerts.Shutdown.Pending;
    public string ShutdownStatus => _alerts.Shutdown.Status;
    public string LastEvent => _alerts.LastEvent;
    public string HotkeyStatus => _alerts.HotkeyStatus;
    public bool DryRun => _alerts.DryRun;
    public int ArmedCount => _c.Config.Safety.Rules.Values.Count(r => r.ShutdownEnabled);

    /// <summary>ok | warn | bad | off, para el color del indicador.</summary>
    public string ShutdownLevel => _alerts.Shutdown.Phase switch
    {
        ShutdownPhase.Countdown or ShutdownPhase.Issued => "bad",
        ShutdownPhase.Cooldown => "warn",
        ShutdownPhase.Armed => "ok",
        _ => "off"
    };

    public string HotkeyText => IsCapturingHotkey ? Tr.T("Pulsa la tecla… (Esc cancela)") : _alerts.HotkeyText;

    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        set
        {
            if (Set(ref _isCapturingHotkey, value)) OnPropertyChanged(nameof(HotkeyText));
        }
    }

    public string CooldownSeconds
    {
        get => _c.Config.Safety.CooldownSeconds.ToString();
        set
        {
            if (FanChannelViewModel.TryParse(value, out var v))
            {
                _c.Config.Safety.CooldownSeconds = (int)Math.Round(v);
                SafetyLimits.Normalize(_c.Config.Safety);
                _c.NotifySafetyChanged();
            }
            OnPropertyChanged();
        }
    }

    public int WindowWidth { get => _c.Config.Safety.AlertWindow.Width; set => EditWindow(w => w.Width = value); }
    public int WindowHeight { get => _c.Config.Safety.AlertWindow.Height; set => EditWindow(w => w.Height = value); }
    public int WindowOpacity { get => _c.Config.Safety.AlertWindow.Opacity; set => EditWindow(w => w.Opacity = value); }
    public int WindowRadius { get => _c.Config.Safety.AlertWindow.Radius; set => EditWindow(w => w.Radius = value); }

    /// <summary>La ventana principal le pasa la tecla pulsada mientras se captura.</summary>
    public void OnHotkeyCaptured(int virtualKey, int modifiers)
    {
        IsCapturingHotkey = false;
        if (virtualKey == 0x1B) return; // Esc
        _c.Config.Safety.CancelHotkeyKey = virtualKey;
        _c.Config.Safety.CancelHotkeyModifiers = modifiers;
        _c.NotifySafetyChanged();
        OnPropertiesChanged(nameof(HotkeyText));
    }

    private void EditWindow(Action<AlertWindowConfig> change)
    {
        change(_c.Config.Safety.AlertWindow);
        SafetyLimits.Normalize(_c.Config.Safety);
        _c.NotifySafetyChanged();
    }

    private void OnAlertsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlertManager.Shutdown))
        {
            OnPropertiesChanged(nameof(Shutdown), nameof(IsPending), nameof(ShutdownStatus), nameof(ShutdownLevel));
            CommandManager.InvalidateRequerySuggested();
        }
        else if (e.PropertyName is nameof(AlertManager.LastEvent) or nameof(AlertManager.HotkeyStatus))
        {
            OnPropertiesChanged(nameof(LastEvent), nameof(HotkeyStatus));
        }
    }

    private void OnSnapshot(EngineSnapshot s)
    {
        if (DateTime.UtcNow - _lastValues >= TimeSpan.FromSeconds(1))
        {
            _lastValues = DateTime.UtcNow;
            foreach (var row in Rows) row.Refresh(s.Metrics, _alerts.ActiveAlerts);
        }
        if (IsAdvanced && DateTime.UtcNow - _lastSensors >= TimeSpan.FromSeconds(5))
        {
            _lastSensors = DateTime.UtcNow;
            RefreshAddable(s);
        }
    }

    private void RefreshAddable(EngineSnapshot s)
    {
        var options = s.Metrics.Metrics.Values
            .Where(m => m.Value is not null && m.Text is null && !_byId.ContainsKey(m.Id))
            .Select(m => new Option(m.Id, m.Id.StartsWith(KnownMetrics.HardwarePrefix, StringComparison.Ordinal)
                ? $"{m.Label} · {m.Device} ({m.Unit})" : $"{KnownMetrics.Find(m.Id)?.Label ?? m.Label} ({m.Unit})"))
            .OrderBy(o => o.Key.StartsWith(KnownMetrics.HardwarePrefix, StringComparison.Ordinal))
            .ThenBy(o => o.Label, StringComparer.CurrentCulture)
            .ToList();
        if (options.Select(o => o.Key).SequenceEqual(AddableSensors.Select(o => o.Key))) return;
        var keep = SensorToAdd;
        AddableSensors.Clear();
        foreach (var o in options) AddableSensors.Add(o);
        SensorToAdd = options.Any(o => o.Key == keep) ? keep : null;
    }

    private void AddSelected()
    {
        if (SensorToAdd is not { } id) return;
        var row = AddRow(id, isDefault: false);
        if (row is not null)
        {
            row.IsExpanded = true;
            row.Refresh(_c.Snapshot.Metrics, _alerts.ActiveAlerts);
        }
        _lastSensors = DateTime.MinValue;
        SensorToAdd = null;
    }

    private AlarmRowViewModel? AddRow(string id, bool isDefault)
    {
        if (_byId.ContainsKey(id)) return null;
        var metric = _c.Snapshot.Metrics.Get(id);
        var known = KnownMetrics.Find(id);
        var label = _alerts.LabelOf(id, metric);
        var device = known?.Description ?? metric?.Device ?? "";
        var unit = known?.Unit ?? metric?.Unit ?? "";
        var row = new AlarmRowViewModel(_c, _alerts, id, label, device, unit, isDefault, Remove);
        _byId[id] = row;
        Rows.Add(row);
        return row;
    }

    private void Remove(AlarmRowViewModel row)
    {
        _c.Config.Safety.Rules.Remove(row.Id);
        _byId.Remove(row.Id);
        Rows.Remove(row);
        _c.NotifySafetyChanged();
        _lastSensors = DateTime.MinValue;
    }
}
