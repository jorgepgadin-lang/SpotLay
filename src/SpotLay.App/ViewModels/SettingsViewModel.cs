using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Services;
using SpotLay.Core.Config;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppController _c;
    private readonly Func<int, int, bool> _registerHotkey;
    private readonly Func<bool> _registerGraph;
    private bool _isCapturingHotkey;
    private bool _capturingGraph;
    private string _graphHotkeyStatus = "";
    private string _importMessage = "";

    public SettingsViewModel(AppController controller, Func<int, int, bool> registerHotkey, Func<bool> registerGraph)
        : base(Tr.T("Ajustes"), Tr.T("Comportamiento de SpotLay, atajo de teclado y datos."))
    {
        _c = controller;
        _registerHotkey = registerHotkey;
        _registerGraph = registerGraph;
        CaptureHotkeyCommand = new RelayCommand(() => IsCapturingHotkey = true);
        CaptureGraphHotkeyCommand = new RelayCommand(() =>
        {
            _capturingGraph = true;
            IsCapturingHotkey = true;
            OnPropertyChanged(nameof(GraphHotkeyText));
        });
        ImportCommand = new RelayCommand(Import, () => LegacyImporter.LegacyExists);
        OpenDataFolderCommand = new RelayCommand(() => Open(AppPaths.Root));
        OpenLogCommand = new RelayCommand(() => Open(Log.FilePath));
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppController.HotkeyStatus)) OnPropertyChanged(nameof(HotkeyStatus));
        };
        controller.SettingsChanged += () => OnPropertiesChanged(nameof(ImportStatus), nameof(CaptureStatus));
    }

    public ICommand CaptureHotkeyCommand { get; }
    public ICommand CaptureGraphHotkeyCommand { get; }

    // ---- Gráfica de frametime y 1% low ----
    public string GraphHotkeyText => _capturingGraph ? Tr.T("Pulsa la tecla… (Esc cancela)")
        : _c.Config.Graph.HotkeyKey == 0 ? Tr.T("Sin tecla") : HotkeyManager.Describe(_c.Config.Graph.HotkeyKey, _c.Config.Graph.HotkeyModifiers);
    public string GraphHotkeyStatus { get => _graphHotkeyStatus; private set => Set(ref _graphHotkeyStatus, value); }
    public bool GraphVisible { get => _c.Config.Graph.Visible; set { _c.Config.Graph.Visible = value; _c.ScheduleSave(); GraphSettingsChanged?.Invoke(); } }
    public int GraphHeight { get => _c.Config.Graph.Height; set { _c.Config.Graph.Height = Math.Clamp(value, 60, 600); _c.ScheduleSave(); OnPropertyChanged(); } }
    public int GraphWidth { get => _c.Config.Graph.WidthPercent; set { _c.Config.Graph.WidthPercent = Math.Clamp(value, 20, 100); _c.ScheduleSave(); OnPropertyChanged(); } }
    public int GraphOpacity { get => _c.Config.Graph.Opacity; set { _c.Config.Graph.Opacity = Math.Clamp(value, 0, 100); _c.ScheduleSave(); OnPropertyChanged(); } }
    public bool GraphTop { get => _c.Config.Graph.Position == "top"; set { if (value) { _c.Config.Graph.Position = "top"; _c.ScheduleSave(); } } }
    public bool GraphBottom { get => _c.Config.Graph.Position != "top"; set { if (value) { _c.Config.Graph.Position = "bottom"; _c.ScheduleSave(); } } }
    /// <summary>Cada cuántos ms se redibuja la gráfica (100-2000).</summary>
    public string GraphRefreshMs
    {
        get => _c.Config.Graph.RefreshMs.ToString();
        set
        {
            if (int.TryParse(value?.Trim(), out var ms))
            {
                _c.Config.Graph.RefreshMs = Math.Clamp(ms, 100, 2000);
                _c.ScheduleSave();
                GraphSettingsChanged?.Invoke();
            }
            OnPropertyChanged();
        }
    }

    /// <summary>Segundos que abarca la gráfica y sobre los que se calcula el 1% low (5-60).</summary>
    public string StatsSeconds
    {
        get => _c.Config.FrameStatsSeconds.ToString();
        set
        {
            if (int.TryParse(value?.Trim(), out var s))
            {
                _c.Config.FrameStatsSeconds = Math.Clamp(s, 5, 60);
                _c.Engine.FrameStatsSeconds = _c.Config.FrameStatsSeconds;
                _c.ScheduleSave();
            }
            OnPropertyChanged();
        }
    }

    /// <summary>La app reajusta su temporizador de la gráfica.</summary>
    public static event Action? GraphSettingsChanged;

    // ---- Arranque con Windows ----
    private bool? _startWithWindows;
    private string _startupMessage = "";

    public bool StartWithWindows
    {
        get => _startWithWindows ??= StartupTask.Exists();
        set
        {
            var ok = value ? StartupTask.Create(Environment.ProcessPath ?? "") : StartupTask.Delete();
            _startWithWindows = StartupTask.Exists();
            StartupMessage = ok
                ? value ? Tr.T("SpotLay arrancará con Windows.") : Tr.T("SpotLay ya no arranca con Windows.")
                : Tr.T("Windows no dejó cambiar la tarea de arranque; mira el registro.");
            OnPropertyChanged();
        }
    }

    public string StartupMessage { get => _startupMessage; private set => Set(ref _startupMessage, value); }

    // ---- Todas las teclas en un sitio ----
    public sealed record ShortcutRow(string Action, string Keys, string Where, string Status, bool Conflict);

    public IReadOnlyList<ShortcutRow> Shortcuts
    {
        get
        {
            var c = _c.Config;
            string D(int k, int m) => k == 0 ? Tr.T("Sin tecla") : HotkeyManager.Describe(k, m);
            ShortcutRow Own(string action, string id, int k, int m, string where)
            {
                if (k == 0) return new(action, Tr.T("Sin tecla"), where, "", false);
                var problem = HotkeyManager.Problem(k, m, HotkeyManager.Status.GetValueOrDefault(id, true));
                return new(action, D(k, m), where, problem.Length == 0 ? "OK" : problem, problem.Length > 0);
            }
            ShortcutRow OnDemand(string action, int k, int m, string where, string when)
            {
                if (k == 0) return new(action, Tr.T("Sin tecla"), where, "", false);
                var problem = HotkeyManager.Problem(k, m, HotkeyManager.IsFree(k, m));
                return new(action, D(k, m), where, problem.Length == 0 ? when : problem, problem.Length > 0);
            }
            var rows = new List<ShortcutRow>
            {
                Own(Tr.T("Mostrar / ocultar overlay"), "overlay", c.HotkeyKey, c.HotkeyModifiers, Tr.T("Ajustes")),
                Own(Tr.T("Mostrar / ocultar gráfica de frametime"), "graph", c.Graph.HotkeyKey, c.Graph.HotkeyModifiers, Tr.T("Ajustes")),
            };
            for (var i = 0; i < c.Presets.Count; i++)
                rows.Add(Own(Tr.F("Diseño {0}: {1}", i + 1, c.Presets[i].Name), "preset" + i, c.Presets[i].HotkeyKey, c.Presets[i].HotkeyModifiers, Tr.T("Inicio")));
            rows.Add(Own(Tr.T("Volver al diseño del juego"), "game", c.GameLayoutHotkeyKey, c.GameLayoutHotkeyModifiers, Tr.T("Inicio")));
            rows.Add(OnDemand(Tr.T("Cancelar apagado de emergencia"), c.Safety.CancelHotkeyKey, c.Safety.CancelHotkeyModifiers, Tr.T("Alertas"),
                Tr.T("OK (solo se activa durante la cuenta atrás)")));
            rows.Add(new(Tr.T("Mover el overlay"), Tr.T("Ctrl + Mayús + flechas"), "—", Tr.T("Solo con el overlay a la vista y un juego delante"), false));
            return rows;
        }
    }
    public ICommand ImportCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }

    // ---- Idioma (se aplica al reiniciar) ----------------------------------------
    public IReadOnlyList<Option> LanguageOptions { get; } = [new(Tr.Spanish, Tr.T("Español")), new(Tr.English, Tr.T("English"))];

    public string Language
    {
        get => _c.Config.Language ?? Tr.Language;
        set
        {
            if (value == Language) return;
            _c.Config.Language = value;
            _c.SaveNow();
            OnPropertiesChanged(nameof(Language), nameof(LanguageNeedsRestart), nameof(RestartText), nameof(RestartHint));
        }
    }

    public bool LanguageNeedsRestart => (_c.Config.Language ?? Tr.Language) != Tr.Language;

    // En el idioma elegido, no en el actual: quien elige inglés puede no entender español.
    public string RestartText => Tr.In(Language, "Reiniciar SpotLay");
    public string RestartHint => Tr.In(Language, "El nuevo idioma se aplica al reiniciar SpotLay.");

    /// <summary>App reinicia SpotLay (cambio de idioma).</summary>
    public static event Action? RestartRequested;

    public ICommand RestartCommand { get; } = new RelayCommand(() => RestartRequested?.Invoke());

    public bool ThemeSystem { get => _c.Config.Theme == "system"; set => SetTheme(value, "system"); }
    public bool ThemeLight { get => _c.Config.Theme == "light"; set => SetTheme(value, "light"); }
    public bool ThemeDark { get => _c.Config.Theme == "dark"; set => SetTheme(value, "dark"); }

    public bool ShowOnlyInGames
    {
        get => _c.Config.ShowOnlyInGames;
        set
        {
            _c.Config.ShowOnlyInGames = value;
            _c.NotifySettingsChanged();
        }
    }

    public bool HideGameMetricsWithoutGame
    {
        get => _c.Config.HideGameMetricsWithoutGame;
        set
        {
            _c.Config.HideGameMetricsWithoutGame = value;
            _c.NotifySettingsChanged();
        }
    }

    public bool OverlayEnabled
    {
        get => _c.Config.OverlayEnabled;
        set
        {
            _c.Config.OverlayEnabled = value;
            _c.NotifySettingsChanged();
        }
    }

    public string HotkeyText => IsCapturingHotkey
        ? Tr.T("Pulsa la tecla… (Esc cancela)")
        : HotkeyManager.Describe(_c.Config.HotkeyKey, _c.Config.HotkeyModifiers);

    public string HotkeyStatus => _c.HotkeyStatus;

    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        set
        {
            if (Set(ref _isCapturingHotkey, value)) OnPropertyChanged(nameof(HotkeyText));
        }
    }

    public string ImportStatus => _c.Config.LegacyImportedAt is { } at
        ? Tr.F("Importado el {0:dd/MM/yyyy HH:mm}.", at)
        : LegacyImporter.LegacyExists ? Tr.T("Se ha encontrado la configuración de SpotLay 10.") : Tr.T("No hay configuración de SpotLay 10 en este equipo.");

    public string ImportMessage { get => _importMessage; private set => Set(ref _importMessage, value); }
    public string CaptureStatus => _c.Engine.CaptureStatus;
    public string DataFolder => AppPaths.Root;
    public string Version => $"SpotLay {Services.Updater.CurrentText} · .NET 10";

    // ---- Actualizaciones --------------------------------------------------------
    public bool AutoUpdate
    {
        get => _c.Config.AutoUpdate;
        set
        {
            _c.Config.AutoUpdate = value;
            _c.ScheduleSave();
        }
    }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }

    /// <summary>App busca actualizaciones ahora mismo.</summary>
    public static event Action? CheckUpdatesRequested;
    public ICommand CheckUpdatesCommand { get; } = new RelayCommand(() => CheckUpdatesRequested?.Invoke());

    /// <summary>La ventana principal le pasa la tecla pulsada mientras se captura el atajo.</summary>
    public void OnHotkeyCaptured(int virtualKey, int modifiers)
    {
        IsCapturingHotkey = false;
        if (_capturingGraph)
        {
            _capturingGraph = false;
            if (virtualKey != 0x1B)
            {
                _c.Config.Graph.HotkeyKey = virtualKey;
                _c.Config.Graph.HotkeyModifiers = modifiers;
                GraphHotkeyStatus = _registerGraph() ? "" : Tr.T("Esa tecla ya la usa otra aplicación.");
                _c.ScheduleSave();
            }
            OnPropertyChanged(nameof(GraphHotkeyText));
            return;
        }
        if (virtualKey == 0x1B) return; // Esc
        var previousKey = _c.Config.HotkeyKey;
        var previousModifiers = _c.Config.HotkeyModifiers;
        _c.Config.HotkeyKey = virtualKey;
        _c.Config.HotkeyModifiers = modifiers;
        if (!_registerHotkey(virtualKey, modifiers))
        {
            _c.Config.HotkeyKey = previousKey;
            _c.Config.HotkeyModifiers = previousModifiers;
            _registerHotkey(previousKey, previousModifiers);
        }
        _c.ScheduleSave();
        OnPropertiesChanged(nameof(HotkeyText), nameof(HotkeyStatus));
    }

    private void SetTheme(bool on, string theme)
    {
        if (!on || _c.Config.Theme == theme) return;
        _c.Config.Theme = theme;
        ThemeManager.Apply(theme);
        _c.ScheduleSave();
        OnPropertiesChanged(nameof(ThemeSystem), nameof(ThemeLight), nameof(ThemeDark));
    }

    private void Import()
    {
        ImportMessage = _c.ImportLegacy().Message;
        OnPropertyChanged(nameof(ImportStatus));
    }

    private static void Open(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo abrir " + path + ": " + ex.Message);
        }
    }
}
