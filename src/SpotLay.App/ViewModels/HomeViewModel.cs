using System.Collections.ObjectModel;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

/// <summary>Uno de los 5 diseños globales en la página de Inicio.</summary>
public sealed class PresetItem : ObservableObject
{
    private readonly AppController _c;
    private readonly HomeViewModel _home;
    private string _hotkeyStatus = "";

    public PresetItem(AppController controller, HomeViewModel home, int index, Action<string> navigate)
    {
        _c = controller;
        _home = home;
        Index = index;
        ActivateCommand = new RelayCommand(() => _c.ActivatePreset(Index, overrideGameProfile: false));
        EditCommand = new RelayCommand(() =>
        {
            if (!IsActive) _c.ActivatePreset(Index, overrideGameProfile: false);
            _c.EditingTarget = AppController.GlobalTarget;
            navigate("overlay");
        });
        CaptureCommand = new RelayCommand(() => _home.StartCapture(this));
    }

    private OverlayPreset P => _c.Config.Presets[Index];

    public int Index { get; }
    public string Number => (Index + 1).ToString();
    public bool IsActive => _c.Config.ActivePreset == Index;
    public string Summary => Tr.F("{0} métricas", P.Layout.Metrics.Count);

    public string Name
    {
        get => P.Name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? P.Name : value.Trim();
            if (name == P.Name) return;
            P.Name = name;
            _c.NotifyPresetsChanged();
        }
    }

    public string HotkeyText => _home.CapturingPreset == this ? Tr.T("Pulsa la tecla… (Esc cancela)")
        : P.HotkeyKey == 0 ? Tr.T("Sin tecla") : HotkeyManager.Describe(P.HotkeyKey, P.HotkeyModifiers);

    public string HotkeyStatus { get => _hotkeyStatus; set => Set(ref _hotkeyStatus, value); }

    public ICommand ActivateCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CaptureCommand { get; }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class HomeViewModel : PageViewModel
{
    private readonly AppController _c;
    private readonly Func<int, bool> _registerPreset;
    private readonly Action<string> _navigate;
    private string _gameTitle = Tr.T("Sin juego en primer plano");
    private string _gameDetail = Tr.T("Abre un juego y SpotLay lo detecta solo.");
    private string _fps = "—";
    private string _gpuTemp = "—", _hotSpot = "—", _cpuTemp = "—", _gpuLoad = "—", _cpuLoad = "—";
    private string _importMessage = "";
    private PresetItem? _capturing;
    private bool _capturingGame;
    private string _gameHotkeyStatus = "";

    public HomeViewModel(AppController controller, Action<string> navigate, Func<int, bool> registerPreset)
        : base(Tr.T("Inicio"), Tr.T("Lo que pasa ahora y tus 5 diseños de overlay, cada uno con su tecla."))
    {
        _c = controller;
        _navigate = navigate;
        _registerPreset = registerPreset;
        ImportCommand = new RelayCommand(Import);
        GoAlertsCommand = new RelayCommand(() => navigate("alertas"));
        GoCoolingCommand = new RelayCommand(() => navigate("refrigeracion"));
        CaptureGameHotkeyCommand = new RelayCommand(() =>
        {
            _capturing = null;
            _capturingGame = true;
            OnPropertyChanged(nameof(GameHotkeyText));
        });
        for (var i = 0; i < SpotLayConfig.PresetCount; i++) Presets.Add(new PresetItem(controller, this, i, navigate));
        RefreshHotkeyStatus();

        controller.SnapshotUpdated += OnSnapshot;
        controller.PresetsChanged += RefreshPresets;
        controller.LayoutChanged += RefreshPresets;
        controller.SettingsChanged += () => OnPropertyChanged(nameof(OverlayEnabled));
        controller.CoolingChanged += () => OnPropertyChanged(nameof(FansEnabled));
        controller.SafetyChanged += () => OnPropertyChanged(nameof(SafetySummary));
    }

    public ObservableCollection<PresetItem> Presets { get; } = [];
    public ICommand ImportCommand { get; }
    public ICommand GoAlertsCommand { get; }
    public ICommand GoCoolingCommand { get; }
    public ICommand CaptureGameHotkeyCommand { get; }

    public string GameHotkeyText => _capturingGame ? Tr.T("Pulsa la tecla… (Esc cancela)")
        : _c.Config.GameLayoutHotkeyKey == 0 ? Tr.T("Sin tecla") : HotkeyManager.Describe(_c.Config.GameLayoutHotkeyKey, _c.Config.GameLayoutHotkeyModifiers);

    public string GameHotkeyStatus { get => _gameHotkeyStatus; private set => Set(ref _gameHotkeyStatus, value); }

    public string GameTitle { get => _gameTitle; private set => Set(ref _gameTitle, value); }
    public string GameDetail { get => _gameDetail; private set => Set(ref _gameDetail, value); }
    private string _exclusiveWarning = "";
    /// <summary>Aviso fuera del juego: el último juego sigue abierto en pantalla completa exclusiva.</summary>
    public string ExclusiveWarning { get => _exclusiveWarning; private set => Set(ref _exclusiveWarning, value); }
    public string Fps { get => _fps; private set => Set(ref _fps, value); }
    public string GpuTemp { get => _gpuTemp; private set => Set(ref _gpuTemp, value); }
    public string HotSpot { get => _hotSpot; private set => Set(ref _hotSpot, value); }
    public string CpuTemp { get => _cpuTemp; private set => Set(ref _cpuTemp, value); }
    public string GpuLoad { get => _gpuLoad; private set => Set(ref _gpuLoad, value); }
    public string CpuLoad { get => _cpuLoad; private set => Set(ref _cpuLoad, value); }
    public string CaptureStatus => _c.Engine.CaptureStatus;
    public bool CaptureOk => _c.Engine.CaptureAvailable;
    public bool ShowImportBanner => _c.HasLegacyToImport;
    public string ImportMessage { get => _importMessage; private set => Set(ref _importMessage, value); }
    public string ActiveLayoutName => _c.ActiveLayoutName;

    public bool OverlayEnabled
    {
        get => _c.Config.OverlayEnabled;
        set
        {
            _c.Config.OverlayEnabled = value;
            _c.NotifySettingsChanged();
        }
    }

    public bool FansEnabled
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
        }
    }

    public string SafetySummary
    {
        get
        {
            var shutdown = _c.Config.Safety.Rules.Values.Count(r => r.ShutdownEnabled);
            var alerts = _c.Config.Safety.Rules.Values.Count(r => r.AlertEnabled);
            return Tr.F("{0} aviso(s) · {1} apagado(s) de emergencia armado(s)", alerts, shutdown);
        }
    }

    // ---- Captura de la tecla de un diseño ----
    public PresetItem? CapturingPreset => _capturing;
    public bool IsCapturingHotkey => _capturing is not null || _capturingGame;

    public void StartCapture(PresetItem item)
    {
        _capturing = item;
        foreach (var p in Presets) p.Refresh();
    }

    public void OnHotkeyCaptured(int virtualKey, int modifiers)
    {
        var item = _capturing;
        _capturing = null;
        if (_capturingGame)
        {
            _capturingGame = false;
            if (virtualKey != 0x1B)
            {
                _c.Config.GameLayoutHotkeyKey = virtualKey;
                _c.Config.GameLayoutHotkeyModifiers = modifiers;
                _c.NotifyPresetsChanged();
            }
            OnPropertyChanged(nameof(GameHotkeyText));
        }
        else if (item is not null && virtualKey != 0x1B)
        {
            var preset = _c.Config.Presets[item.Index];
            preset.HotkeyKey = virtualKey;
            preset.HotkeyModifiers = modifiers;
            _c.NotifyPresetsChanged();
        }
        RefreshHotkeyStatus();
        foreach (var p in Presets) p.Refresh();
    }

    private void RefreshPresets()
    {
        foreach (var p in Presets) p.Refresh();
        OnPropertyChanged(nameof(ActiveLayoutName));
    }

    private void RefreshHotkeyStatus()
    {
        foreach (var p in Presets)
        {
            var preset = _c.Config.Presets[p.Index];
            p.HotkeyStatus = preset.HotkeyKey == 0 ? "" : HotkeyManager.Problem(preset.HotkeyKey, preset.HotkeyModifiers, _registerPreset(p.Index));
        }
        GameHotkeyStatus = _c.Config.GameLayoutHotkeyKey == 0 ? ""
            : HotkeyManager.Problem(_c.Config.GameLayoutHotkeyKey, _c.Config.GameLayoutHotkeyModifiers, _registerPreset(-1));
    }

    private void OnSnapshot(EngineSnapshot s)
    {
        var fg = s.FrameGen;
        if (s.Game.IsGame)
        {
            GameTitle = SpotLayConfig.ProfileKey(s.Game.ExeName);
            var mode = s.Game.Exclusive
                ? Tr.T("Pantalla completa exclusiva: el overlay no se puede mostrar. Usa ventana sin bordes.")
                : s.Game.Fullscreen ? Tr.T("Pantalla completa") : Tr.T("Ventana");
            GameDetail = string.Join(" · ", new[] { fg.ApiName, mode }.Where(x => !string.IsNullOrEmpty(x)));
        }
        else
        {
            GameTitle = Tr.T("Sin juego en primer plano");
            GameDetail = Tr.T("Abre un juego y SpotLay lo detecta solo.");
        }
        ExclusiveWarning = !s.Game.IsGame && _c.ExclusiveGame is { } ex
            ? Tr.F("{0} está en pantalla completa exclusiva: el overlay no se puede mostrar encima. Ponlo en ventana sin bordes (borderless).",
                SpotLayConfig.ProfileKey(ex.ExeName))
            : "";
        Fps = s.Game.IsGame && fg.HasFrames ? MetricFormat.Number(fg.TotalFps) : "—";
        GpuTemp = Format(s, KnownMetrics.GpuTemp);
        HotSpot = Format(s, KnownMetrics.GpuHotSpot);
        CpuTemp = Format(s, KnownMetrics.CpuTemp);
        GpuLoad = Format(s, KnownMetrics.GpuLoad);
        CpuLoad = Format(s, KnownMetrics.CpuLoad);
        OnPropertiesChanged(nameof(CaptureStatus), nameof(CaptureOk), nameof(ActiveLayoutName));
    }

    private static string Format(EngineSnapshot s, string id)
    {
        var m = s.Metrics.Get(id);
        return m is null ? "—" : $"{MetricFormat.Value(m)} {m.Unit}".Trim();
    }

    private void Import()
    {
        var result = _c.ImportLegacy();
        ImportMessage = result.Message;
        OnPropertyChanged(nameof(ShowImportBanner));
    }
}
