using System.Windows.Input;
using SpotLay.App.Alerts;
using SpotLay.App.Infrastructure;
using SpotLay.App.Services;
using SpotLay.Core.Config;
using SpotLay.Core.FrameGen;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ObservableObject
{
    public string Title { get; } = Tr.T(title);
    public string Subtitle { get; } = Tr.T(subtitle);
    /// <summary>La página cambia entre Esencial y Avanzado (si no, el selector se oculta).</summary>
    public virtual bool HasModes => false;

    /// <summary>Casilla propia de la página en la cabecera (null = sin casilla).</summary>
    public virtual string? HeaderOption => null;
    public virtual bool HeaderOptionValue { get => false; set { } }
}

public sealed record NavItem(string Title, string Glyph, PageViewModel Page, string? Badge = null);

public sealed class MainViewModel : ObservableObject
{
    private readonly AppController _c;
    private NavItem _selected;
    private string _statusText = Tr.T("Sin juego en primer plano");
    private bool _isGameActive;

    public MainViewModel(AppController controller, AlertManager alerts, Func<int, int, bool> registerHotkey, Func<int, bool> registerPreset,
        Func<bool> registerGraph)
    {
        _c = controller;
        Home = new HomeViewModel(controller, key => Navigate(key), registerPreset);
        Overlay = new OverlayEditorViewModel(controller);
        Metrics = new MetricsViewModel(controller);
        Profiles = new ProfilesViewModel(controller, key => Navigate(key));
        Settings = new SettingsViewModel(controller, registerHotkey, registerGraph);
        Alerts = new AlertsViewModel(controller, alerts);
        Performance = new PerformanceViewModel(controller, Settings);
        Cooling = new CoolingViewModel(controller);

        Nav =
        [
            new(Tr.T("Inicio"), "", Home),
            new(Tr.T("Overlay"), "", Overlay),
            new(Tr.T("Métricas"), "", Metrics),
            new(Tr.T("Rendimiento"), "\uE9D9", Performance),
            new(Tr.T("Perfiles"), "", Profiles),
            new(Tr.T("Alertas"), "", Alerts),
            new(Tr.T("Refrigeración"), "", Cooling),
            new(Tr.T("Ajustes"), "", Settings),
        ];
        _selected = Nav[0];

        ToggleOverlayCommand = new RelayCommand(controller.ToggleOverlay);
        controller.SnapshotUpdated += _ => RefreshStatus();
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppController.OverlayShouldShow)) OnPropertyChanged(nameof(OverlayButtonText));
        };
        controller.SettingsChanged += () => OnPropertiesChanged(nameof(IsEsencial), nameof(IsAvanzado));
    }

    public HomeViewModel Home { get; }
    public OverlayEditorViewModel Overlay { get; }
    public MetricsViewModel Metrics { get; }
    public ProfilesViewModel Profiles { get; }
    public SettingsViewModel Settings { get; }
    public AlertsViewModel Alerts { get; }
    public PerformanceViewModel Performance { get; }
    public CoolingViewModel Cooling { get; }
    public IReadOnlyList<NavItem> Nav { get; }

    public NavItem SelectedNav
    {
        get => _selected;
        set
        {
            if (value is null || !Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public PageViewModel CurrentPage => _selected.Page;

    public bool IsEsencial
    {
        get => _c.Config.Mode == AppModes.Esencial;
        set
        {
            if (!value || IsEsencial) return;
            _c.Config.Mode = AppModes.Esencial;
            _c.NotifySettingsChanged();
        }
    }

    public bool IsAvanzado
    {
        get => _c.Config.Mode == AppModes.Avanzado;
        set
        {
            if (!value || IsAvanzado) return;
            _c.Config.Mode = AppModes.Avanzado;
            _c.NotifySettingsChanged();
        }
    }

    public ICommand ToggleOverlayCommand { get; }

    public string OverlayButtonText => _c.OverlayShouldShow ? Tr.T("Ocultar overlay") : Tr.T("Mostrar overlay");

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public bool IsGameActive { get => _isGameActive; private set => Set(ref _isGameActive, value); }

    public string Version => "SpotLay " + Services.Updater.CurrentText;

    // ---- Actualización lista (barra arriba) ------------------------------------
    private string _updateText = "";
    private bool _updateVisible;

    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }
    public bool UpdateVisible { get => _updateVisible; private set => Set(ref _updateVisible, value); }

    public void ShowUpdate(Version version)
    {
        UpdateText = Tr.F("SpotLay {0} está listo. Se instala al reiniciar SpotLay.", $"{version.Major}.{version.Minor}.{version.Build}");
        UpdateVisible = true;
    }

    /// <summary>App: reiniciar SpotLay ya para instalar / instalar al cerrar.</summary>
    public static event Action? UpdateNowRequested;
    public static event Action? UpdateLaterRequested;

    public ICommand UpdateNowCommand { get; } = new RelayCommand(() => UpdateNowRequested?.Invoke());

    public ICommand UpdateLaterCommand => _updateLater ??= new RelayCommand(() =>
    {
        UpdateLaterRequested?.Invoke();
        UpdateVisible = false;
    });

    private ICommand? _updateLater;

    public void Navigate(string key)
    {
        var target = key switch
        {
            "overlay" => Overlay as PageViewModel,
            "metricas" => Metrics,
            "perfiles" => Profiles,
            "ajustes" => Settings,
            "alertas" => Alerts,
            "rendimiento" => Performance,
            "refrigeracion" => Cooling,
            _ => Home
        };
        SelectedNav = Nav.First(n => ReferenceEquals(n.Page, target));
    }

    private void RefreshStatus()
    {
        var s = _c.Snapshot;
        IsGameActive = s.Game.IsGame;
        if (!s.Game.IsGame)
        {
            StatusText = Tr.T("Sin juego en primer plano");
            return;
        }
        var fg = s.FrameGen;
        var parts = new List<string> { SpotLayConfig.ProfileKey(s.Game.ExeName) };
        if (!string.IsNullOrEmpty(fg.ApiName)) parts.Add(fg.ApiName);
        if (fg.State is FrameGenState.On or FrameGenState.Off) parts.Add("FG " + fg.ShortText);
        StatusText = string.Join(" · ", parts);
    }
}
