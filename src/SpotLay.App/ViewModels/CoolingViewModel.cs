using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Cooling;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

public sealed class FanChannelViewModel : ObservableObject
{
    private readonly AppController _c;
    private readonly CoolingViewModel _page;
    private CoolingChannelInfo _info;
    private int _testDuty = 100;
    private int _testSeconds = 5;
    private bool _isExpanded;
    private string _testMessage = "";
    private string _calibrationMessage = "";
    private int? _recommendedMinimum;

    public FanChannelViewModel(AppController controller, CoolingViewModel page, CoolingChannelInfo info)
    {
        _c = controller;
        _page = page;
        _info = info;
        TestCommand = new RelayCommand(async () => TestMessage = (await _c.Engine.TestFanAsync(Id, TestDuty, TestSeconds)).Message);
        StopTestCommand = new RelayCommand(async () => TestMessage = (await _c.Engine.StopFanTestAsync(Id)).Message);
        CalibrateCommand = new RelayCommand(async () => CalibrationMessage = (await _c.Engine.CalibrateFanAsync(Id)).Message, () => HasRpm);
        ApplyRecommendedCommand = new RelayCommand(() =>
        {
            if (_recommendedMinimum is { } min) MinimumDuty = min;
        }, () => _recommendedMinimum is not null);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        CopySettingsCommand = new RelayCommand(() => _page.Copy(Current, Name));
        PasteSettingsCommand = new RelayCommand(() => { if (_page.Copied is { } from) PasteFrom(from); }, () => _page.Copied is not null);
        PasteAllCommand = new RelayCommand(() => _page.PasteToAll(), () => _page.Copied is not null);
        CurveEdited = points => Edit(ch => ch.Curve = points.ToList());
    }

    public string Id => _info.Id;
    public string Name => _info.Name;
    public string Hardware => $"{_info.Category} · {_info.Hardware} · {_info.DefaultName}";
    public bool HasRpm => _info.HasRpm;
    public string DutyText => _info.CurrentDuty is { } d ? $"{d:F0} %" : "— %";
    public string RpmText => _info.CurrentRpm is { } r ? Tr.F("{0:F0} RPM", r) : _info.HasRpm ? Tr.T("— RPM") : Tr.T("sin RPM");
    public string Status => _info.Status;
    public bool Controlled => _info.Controlled;
    public double? CurrentTemperature => _info.Temperature;
    public double? CurrentDuty => _info.CurrentDuty;
    public string TemperatureText => _info.Temperature is { } t ? $"{t.ToString("F1", Tr.Culture)} °C" : "—";
    public bool IsAdvanced => true;
    public bool IsFirst => _page.Channels.Count > 0 && ReferenceEquals(_page.Channels[0], this);
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    public ICommand CopySettingsCommand { get; }
    public ICommand PasteSettingsCommand { get; }
    public ICommand PasteAllCommand { get; }
    public string CopiedText => _page.CopiedText;

    /// <summary>
    /// Pega modo, curva, velocidad fija, temperaturas, crítico, histéresis y tiempos de respuesta.
    /// No el nombre ni el mínimo/máximo: dependen de cada ventilador (los da su calibración).
    /// </summary>
    public void PasteFrom(FanChannelConfig from) => Edit(ch =>
    {
        ch.Mode = from.Mode;
        ch.Curve = from.Curve.Select(p => new FanPoint(p.Temperature, p.Duty)).ToList();
        ch.ManualDuty = from.ManualDuty;
        ch.TemperatureSourceId = from.TemperatureSourceId;
        ch.SecondaryTemperatureSourceId = from.SecondaryTemperatureSourceId;
        ch.MixMode = from.MixMode;
        ch.CriticalTemperature = from.CriticalTemperature;
        ch.Hysteresis = from.Hysteresis;
        ch.ResponseUpSeconds = from.ResponseUpSeconds;
        ch.ResponseDownSeconds = from.ResponseDownSeconds;
    });

    public bool IsMarked => Existing?.Enabled == true;

    public ICommand TestCommand { get; }
    public ICommand StopTestCommand { get; }
    public ICommand CalibrateCommand { get; }
    public ICommand ApplyRecommendedCommand { get; }
    public ICommand ToggleExpandCommand { get; }
    public Action<IReadOnlyList<FanPoint>> CurveEdited { get; }

    private FanChannelConfig? Existing => _c.Config.Cooling.Channels.GetValueOrDefault(Id);
    private FanChannelConfig Current => Existing ?? DefaultFor(_info);

    public bool Enabled { get => Existing?.Enabled == true; set => Edit(ch => ch.Enabled = value); }
    public string Label { get => Existing?.Label ?? ""; set => Edit(ch => ch.Label = value?.Trim() ?? ""); }
    public bool IsManual { get => Current.Mode == FanModes.Manual; set => Edit(ch => ch.Mode = value ? FanModes.Manual : FanModes.Curve); }
    public bool IsCurve { get => !IsManual; set => IsManual = !value; }
    public int ManualDuty { get => Current.ManualDuty; set => Edit(ch => ch.ManualDuty = value); }
    // Un desplegable pone null cuando se refresca su lista: eso nunca debe cambiar la configuración.
    public string TemperatureSourceId
    {
        get => Current.TemperatureSourceId;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Current.TemperatureSourceId) Edit(ch => ch.TemperatureSourceId = value);
        }
    }

    public string SecondaryTemperatureSourceId
    {
        get => Current.SecondaryTemperatureSourceId;
        set
        {
            if (value is not null && value != Current.SecondaryTemperatureSourceId) Edit(ch => ch.SecondaryTemperatureSourceId = value);
        }
    }

    public string MixMode
    {
        get => Current.MixMode;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Current.MixMode) Edit(ch => ch.MixMode = value);
        }
    }
    public int MinimumDuty { get => Current.MinimumDuty; set => Edit(ch => ch.MinimumDuty = value); }
    public int MaximumDuty { get => Current.MaximumDuty; set => Edit(ch => ch.MaximumDuty = value); }
    public string CriticalTemperature { get => Num(Current.CriticalTemperature); set => EditNumber(value, (ch, v) => ch.CriticalTemperature = v); }
    public string Hysteresis { get => Num(Current.Hysteresis); set => EditNumber(value, (ch, v) => ch.Hysteresis = v); }
    public string ResponseUpSeconds { get => Num(Current.ResponseUpSeconds); set => EditNumber(value, (ch, v) => ch.ResponseUpSeconds = v); }
    public string ResponseDownSeconds { get => Num(Current.ResponseDownSeconds); set => EditNumber(value, (ch, v) => ch.ResponseDownSeconds = v); }
    public IReadOnlyList<FanPoint> Curve => Current.Curve;

    public string SourceSummary
    {
        get
        {
            var ch = Current;
            var main = _page.SourceName(ch.TemperatureSourceId);
            if (string.IsNullOrWhiteSpace(ch.SecondaryTemperatureSourceId)) return main;
            var mix = ch.MixMode switch { "min" => Tr.T("la menor de"), "average" => Tr.T("la media de"), _ => Tr.T("la mayor de") };
            return Tr.F("{0} {1} y {2}", mix, main, _page.SourceName(ch.SecondaryTemperatureSourceId));
        }
    }

    public string ModeSummary => Current.Mode == FanModes.Manual
        ? Tr.F("Fijo al {0} %", Current.ManualDuty)
        : Tr.F("Curva · mín. {0} % · crítico {1} °C", Current.MinimumDuty, Num(Current.CriticalTemperature));

    public int TestDuty { get => _testDuty; set => Set(ref _testDuty, Math.Clamp(value, 35, 100)); }
    public int TestSeconds { get => _testSeconds; set => Set(ref _testSeconds, Math.Clamp(value, 2, 10)); }
    public string TestMessage { get => _testMessage; private set => Set(ref _testMessage, value); }
    public string CalibrationMessage { get => _calibrationMessage; private set => Set(ref _calibrationMessage, value); }
    // ---- Conector sin ventilador -------------------------------------------------
    private float _maxRpmSeen;

    /// <summary>
    /// Conector de la placa que nunca se ha visto girar y que SpotLay no controla: casi seguro
    /// que no tiene ventilador. Los de la gráfica nunca se ocultan (en reposo pueden estar a 0 RPM).
    /// </summary>
    public bool LooksEmpty => _info.Category != "GPU" && _maxRpmSeen < 100 && Existing?.Enabled != true;

    public bool IsVisible => !(_page.HideEmptyChannels && LooksEmpty);

    public void RefreshVisibility() => OnPropertyChanged(nameof(IsVisible));

    public void Update(CoolingChannelInfo info, CoolingSnapshot snapshot)
    {
        var nameChanged = info.Name != _info.Name;
        _info = info;
        if (info.CurrentRpm is { } seen && seen > _maxRpmSeen)
        {
            var wasEmpty = LooksEmpty;
            _maxRpmSeen = seen;
            if (wasEmpty != LooksEmpty) RefreshVisibility();
        }
        OnPropertiesChanged(nameof(DutyText), nameof(RpmText), nameof(Status), nameof(Controlled),
            nameof(CurrentTemperature), nameof(CurrentDuty), nameof(TemperatureText));
        if (nameChanged) OnPropertyChanged(nameof(Name));

        var test = snapshot.Test;
        if (test.ChannelId.Equals(Id, StringComparison.OrdinalIgnoreCase))
        {
            var rpm = test.CurrentRpm is { } r ? Tr.F(" · ahora {0:F0} RPM", r) : "";
            var peak = test.PeakRpm is { } p && test.BaselineRpm is { } b ? Tr.F(" (antes {0:F0}, pico {1:F0})", b, p) : "";
            TestMessage = test.Running ? Tr.F("{0} {1} % · quedan {2} s{3}{4}", test.Message, test.Duty, test.RemainingSeconds, rpm, peak) : test.Message + peak;
        }
        var calibration = snapshot.Calibration;
        if (calibration.ChannelId.Equals(Id, StringComparison.OrdinalIgnoreCase))
        {
            CalibrationMessage = calibration.Message;
            if (!calibration.Running && calibration.RecommendedMinimumDuty is { } min && _recommendedMinimum != min)
            {
                _recommendedMinimum = min;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public void RefreshAll() => OnPropertyChanged(string.Empty);

    public void RefreshCopied() => OnPropertyChanged(nameof(CopiedText));

    private void Edit(Action<FanChannelConfig> change)
    {
        var channels = _c.Config.Cooling.Channels;
        var isNew = !channels.TryGetValue(Id, out var channel);
        if (isNew) channels[Id] = channel = DefaultFor(_info);
        var before = System.Text.Json.JsonSerializer.Serialize(channel);
        change(channel!);
        SafetyLimits.Normalize(channel!);
        // Un control que reenvía el mismo valor al cargarse no debe crear ni guardar nada.
        if (System.Text.Json.JsonSerializer.Serialize(channel) == before)
        {
            if (isNew) channels.Remove(Id);
            return;
        }
        _c.NotifyCoolingChanged();
        OnPropertyChanged(string.Empty);
    }

    private void EditNumber(string text, Action<FanChannelConfig, double> set)
    {
        if (TryParse(text, out var value)) Edit(ch => set(ch, value));
        else OnPropertyChanged(string.Empty);
    }

    private static FanChannelConfig DefaultFor(CoolingChannelInfo info) => new()
    {
        Role = info.Category switch { "GPU" => "gpu", "CPU" => "cpu", _ => "case" },
        TemperatureSourceId = info.Category == "GPU" ? KnownMetrics.GpuHotSpot : KnownMetrics.CpuTemp,
        SecondaryTemperatureSourceId = info.Category == "GPU" ? "" : KnownMetrics.GpuHotSpot,
        MinimumDuty = 30
    };

    internal static string Num(double value) => value.ToString("0.#", Tr.Culture);

    internal static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}

public sealed class CoolingViewModel : PageViewModel
{
    public override bool HasModes => false;

    public override string? HeaderOption => Tr.T("Ocultar conectores sin ventilador");

    public override bool HeaderOptionValue
    {
        get => _c.Config.Cooling.HideEmptyChannels;
        set
        {
            _c.Config.Cooling.HideEmptyChannels = value;
            _c.ScheduleSave();
            OnPropertyChanged();
            foreach (var ch in Channels) ch.RefreshVisibility();
        }
    }

    public bool HideEmptyChannels => _c.Config.Cooling.HideEmptyChannels;

    private readonly AppController _c;
    private readonly Dictionary<string, FanChannelViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);
    private CoolingSnapshot _snapshot = CoolingSnapshot.Empty;
    private string _actionMessage = "";
    private DateTime _lastSources;

    public CoolingViewModel(AppController controller)
        : base(Tr.T("Refrigeración"), Tr.T("Curvas de ventiladores, identificación y calibración."))
    {
        _c = controller;
        ReleaseAllCommand = new RelayCommand(async () =>
        {
            _c.Config.Cooling.Enabled = false;
            _c.NotifyCoolingChanged();
            Report(await _c.Engine.ReleaseFansAsync());
            OnPropertyChanged(nameof(Enabled));
        });
        controller.SnapshotUpdated += OnSnapshot;
        controller.SettingsChanged += () =>
        {
            OnPropertyChanged(nameof(IsAdvanced));
            foreach (var ch in Channels) ch.RefreshAll();
        };
        controller.CoolingChanged += () => OnPropertyChanged(nameof(Enabled));
    }

    public ObservableCollection<FanChannelViewModel> Channels { get; } = [];

    // ---- Copiar / pegar ajustes entre ventiladores (dura mientras SpotLay está abierto) ----
    public FanChannelConfig? Copied { get; private set; }
    private string _copiedName = "";
    public string CopiedText => Copied is null ? "" : Tr.F("Copiado de: {0}", _copiedName);

    public void Copy(FanChannelConfig from, string name)
    {
        Copied = System.Text.Json.JsonSerializer.Deserialize<FanChannelConfig>(System.Text.Json.JsonSerializer.Serialize(from));
        _copiedName = name;
        foreach (var ch in Channels) ch.RefreshCopied();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Pega lo copiado en todos los ventiladores marcados para SpotLay.</summary>
    public void PasteToAll()
    {
        if (Copied is not { } from) return;
        foreach (var ch in Channels.Where(c => c.IsMarked)) ch.PasteFrom(from);
        ActionMessage = Tr.F("Ajustes de {0} pegados en todos los ventiladores marcados.", _copiedName);
    }
    public ObservableCollection<Option> TemperatureSources { get; } = [];
    public ObservableCollection<Option> SecondarySources { get; } = [new("", Tr.T("Ninguna"))];
    public IReadOnlyList<Option> MixOptions { get; } = [new("max", Tr.T("La mayor")), new("average", Tr.T("La media")), new("min", Tr.T("La menor"))];
    public ICommand ReleaseAllCommand { get; }
    public bool IsAdvanced => true;
    public string Message => _snapshot.Message;
    public bool Conflict => _snapshot.Conflict;
    public bool NoChannels => Channels.Count == 0;
    public bool WritesBlocked => _c.Engine.FanWritesBlocked;
    public string ActionMessage { get => _actionMessage; private set => Set(ref _actionMessage, value); }

    public bool Enabled
    {
        get => _c.Config.Cooling.Enabled;
        set
        {
            if (_c.Config.Cooling.Enabled == value) return;
            if (value && !FanDisclaimer.Accept(_c))
            {
                OnPropertyChanged();
                return;
            }
            _c.Config.Cooling.Enabled = value;
            _c.NotifyCoolingChanged();
            Report(new CoolingActionResult(true, value ? Tr.T("Control activado: se aplican las curvas de los canales marcados.") : Tr.T("Control desactivado: los ventiladores vuelven a la BIOS o a la gráfica.")));
        }
    }

    public string ControlledSummary
    {
        get
        {
            var controlled = Channels.Count(c => c.Controlled);
            var configured = Channels.Count(c => c.Enabled);
            return Tr.F("{0} canales detectados · {1} marcados para SpotLay · {2} controlados ahora", Channels.Count, configured, controlled);
        }
    }

    private System.Windows.Threading.DispatcherTimer? _messageTimer;

    public void Report(CoolingActionResult result)
    {
        ActionMessage = result.Message;
        _messageTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(6), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { ActionMessage = ""; _messageTimer!.Stop(); }, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    public string SourceName(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "ninguna";
        var option = TemperatureSources.FirstOrDefault(o => o.Key.Equals(id, StringComparison.OrdinalIgnoreCase));
        return option?.Label ?? KnownMetrics.Find(id)?.Label ?? id;
    }

    private void OnSnapshot(EngineSnapshot s)
    {
        if (DateTime.UtcNow - _lastSources > TimeSpan.FromSeconds(5)) RefreshSources(s);
        _snapshot = s.Cooling;
        var added = false;
        foreach (var info in s.Cooling.Channels)
        {
            if (!_byId.TryGetValue(info.Id, out var vm))
            {
                _byId[info.Id] = vm = new FanChannelViewModel(_c, this, info);
                // El primero sale con sus opciones abiertas: así se ve que existen.
                if (Channels.Count == 0) vm.IsExpanded = true;
                Channels.Add(vm);
                added = true;
            }
            vm.Update(info, s.Cooling);
        }
        OnPropertiesChanged(nameof(Message), nameof(Conflict), nameof(ControlledSummary));
        if (added) OnPropertyChanged(nameof(NoChannels));
    }

    private void RefreshSources(EngineSnapshot s)
    {
        _lastSources = DateTime.UtcNow;
        var temps = s.Metrics.Metrics.Values
            .Where(m => m.Unit == "°C" && m.Value is not null)
            .Select(m => new Option(m.Id, m.Id.StartsWith(KnownMetrics.HardwarePrefix, StringComparison.Ordinal)
                ? $"{m.Label} · {m.Device}" : KnownMetrics.Find(m.Id)?.Label ?? m.Label))
            .OrderBy(o => o.Key.StartsWith(KnownMetrics.HardwarePrefix, StringComparison.Ordinal))
            .ThenBy(o => o.Label, StringComparer.CurrentCulture)
            .ToList();
        // Fuentes configuradas que ahora no dan lectura siguen apareciendo.
        foreach (var id in _c.Config.Cooling.Channels.Values.SelectMany(c => new[] { c.TemperatureSourceId, c.SecondaryTemperatureSourceId }))
            if (!string.IsNullOrWhiteSpace(id) && temps.All(o => !o.Key.Equals(id, StringComparison.OrdinalIgnoreCase)))
                temps.Add(new Option(id, (KnownMetrics.Find(id)?.Label ?? id) + Tr.T(" (sin lectura)")));
        // Se compara también el texto: al llegar la primera lectura "CPU Temp (sin lectura)" pasa a "CPU Temp".
        if (temps.SequenceEqual(TemperatureSources)) return;
        // Se actualiza en su sitio (sin vaciar la lista): vaciarla dejaba los desplegables en blanco.
        Sync(TemperatureSources, temps);
        Sync(SecondarySources, [new Option("", Tr.T("Ninguna")), .. temps]);
        foreach (var ch in Channels) ch.RefreshAll();
    }

    private static void Sync(ObservableCollection<Option> target, IReadOnlyList<Option> wanted)
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (wanted.All(o => o.Key != target[i].Key)) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = -1;
            for (var j = 0; j < target.Count; j++)
                if (target[j].Key == wanted[i].Key) { at = j; break; }
            if (at < 0) target.Insert(Math.Min(i, target.Count), wanted[i]);
            else
            {
                if (at != i) target.Move(at, Math.Min(i, target.Count - 1));
                var now = Math.Min(i, target.Count - 1);
                if (target[now].Label != wanted[i].Label) target[now] = wanted[i];
            }
        }
    }
}

/// <summary>La primera vez que se activa el control de ventiladores hay que aceptar el aviso.</summary>
public static class FanDisclaimer
{
    public const string Text = "Uso bajo tu exclusiva responsabilidad. El control de ventiladores actúa directamente sobre el hardware. SpotLay se ofrece «tal cual», sin garantía de ningún tipo, expresa o implícita, incluidas las de funcionamiento, idoneidad o ausencia de errores. Los autores no serán responsables de daños en el equipo, pérdida de datos, sobrecalentamiento, apagados inesperados ni de ningún otro perjuicio directo o indirecto derivado de su uso o de su mal funcionamiento, en la máxima medida permitida por la ley aplicable. Si no aceptas estas condiciones, no actives esta función.";

    public static bool Accept(AppController c)
    {
        if (c.Config.FanDisclaimerAcceptedAt is not null) return true;
        var answer = System.Windows.MessageBox.Show(Tr.T(Text) + "\n\n" + Tr.T("¿Aceptas y quieres activar el control de ventiladores?"),
            Tr.T("Control de ventiladores"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return false;
        c.Config.FanDisclaimerAcceptedAt = DateTime.Now;
        SpotLay.Core.Diagnostics.Log.Info("Aviso del control de ventiladores aceptado");
        c.ScheduleSave();
        return true;
    }
}
