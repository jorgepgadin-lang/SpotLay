using System.ComponentModel;
using System.Windows;
using SpotLay.App.Alerts;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.App.ViewModels;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Localization;
using SpotLay.App.Views;

namespace SpotLay.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\SpotLay11.Instance";
    private const string ShowEventName = @"Local\SpotLay11.Show";
    private const string ExitEventName = @"Local\SpotLay11.Exit";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _exitEvent;
    private AppController? _controller;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private OverlayViewModel? _overlayModel;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private AlertManager? _alerts;
    private OverlayMover? _mover;
    private readonly HotkeyManager[] _presetHotkeys = new HotkeyManager[SpotLayConfig.PresetCount];
    private HotkeyManager? _gameLayoutHotkey;
    private HotkeyManager? _graphHotkey;
    private FrameGraphWindow? _graph;
    private System.Windows.Threading.DispatcherTimer? _graphTimer;

    // Con la gráfica visible se mide más a menudo si pide menos de 250 ms.
    private void ApplyGraphTimer()
    {
        if (_graphTimer is null || _controller is null) return;
        var g = _controller.Config.Graph;
        _graphTimer.Interval = TimeSpan.FromMilliseconds(g.RefreshMs);
        _controller.Engine.FrameIntervalMs = g.Visible ? g.RefreshMs : 250;
        if (g.Visible) _graphTimer.Start();
        else _graphTimer.Stop();
        UpdateGraph(_controller.Snapshot);
    }
    private System.Windows.Threading.DispatcherTimer? _bannerTimer;
    private bool _exiting;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Una sola instancia: dos sesiones ETW con el mismo nombre se pisarian.
        _instanceMutex = new Mutex(true, InstanceMutexName, out var firstInstance);
        if (!firstInstance)
        {
            // --salir: cierra la instancia abierta de forma ordenada (ventiladores a la BIOS).
            var name = e.Args.Contains("--salir", StringComparer.OrdinalIgnoreCase) ? ExitEventName : ShowEventName;
            try
            {
                EventWaitHandle.OpenExisting(name).Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, -1, false);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(ExitApp), null, -1, true);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Excepción no controlada en la interfaz", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Excepción fatal", args.ExceptionObject as Exception);
            // Nunca dejar un ventilador fijado a mano si SpotLay se cae.
            _controller?.Engine.ReleaseFansFailSafe(Tr.T("SpotLay se ha cerrado por un error"));
        };
        Log.Info($"SpotLay {Updater.CurrentText} arrancando ({(e.Args.Length == 0 ? "a mano" : string.Join(' ', e.Args))}) desde {Environment.ProcessPath}");
        Updater.CleanUpPrevious();

        _controller = new AppController(Dispatcher);
        if (e.Args.Contains("--sin-ventiladores", StringComparer.OrdinalIgnoreCase))
        {
            _controller.Engine.FanWritesBlocked = true;
            _controller.Engine.SetCoolingConfig(_controller.Config.Cooling);
            Log.Info("Arrancado con --sin-ventiladores: no se escribe en ningún ventilador");
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _controller?.Engine.ReleaseFansFailSafe(Tr.T("SpotLay cerrado"));
        ThemeManager.Apply(_controller.Config.Theme);

        // Idioma: se elige la primera vez; todos los textos se leen ya en ese idioma.
        if (_controller.Config.Language is not (Tr.Spanish or Tr.English))
        {
            _controller.Config.Language = LanguageWindow.Ask(Tr.SystemDefault);
            _controller.SaveNow();
        }
        Tr.Language = _controller.Config.Language;

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += _controller.ToggleOverlay;
        RegisterHotkey(_controller.Config.HotkeyKey, _controller.Config.HotkeyModifiers);

        // --simular-apagado: la cuenta atrás real (por sensor) nunca llega a apagar Windows.
        _alerts = new AlertManager(_controller, e.Args.Contains("--simular-apagado", StringComparer.OrdinalIgnoreCase));

        _overlayModel = new OverlayViewModel();
        _overlay = new OverlayWindow(_overlayModel);
        _controller.LayoutChanged += RebuildOverlay;
        _controller.ActiveLayoutChanged += RebuildOverlay;
        _controller.SettingsChanged += RebuildOverlay;
        _controller.SnapshotUpdated += OnSnapshot;
        _controller.LeftExclusiveGame += OnLeftExclusiveGame;
        _controller.PlacementEdited += () => _overlay.Invalidate();
        _overlay.TileMoved += (id, index) => _controller.MoveMetric(id, index);
        _overlay.TileClicked += id => _controller.RequestSelectMetric(id);
        _overlay.Dragged += (x, y, noSnap) =>
        {
            if (_overlay.Current is not { } now) return;
            OverlayPlacement.SetAnchored(_controller.ActiveLayout, now.Screen, now.Width, now.Height, x, y);
            if (!noSnap) OverlayPlacement.Snap(_controller.ActiveLayout, now.Dpi);
            _controller.NotifyPlacementEdited();
        };
        RebuildOverlay();

        _mover = new OverlayMover();
        _mover.Moved += MoveOverlayBy;
        _mover.MovingChanged += moving => _overlayModel.IsMoving = moving;

        for (var i = 0; i < _presetHotkeys.Length; i++)
        {
            var index = i;
            _presetHotkeys[i] = new HotkeyManager();
            _presetHotkeys[i].Pressed += () => _controller.ActivatePreset(index, overrideGameProfile: true);
        }
        _graph = new FrameGraphWindow();
        _graphHotkey = new HotkeyManager();
        _graphHotkey.Pressed += () =>
        {
            _controller.Config.Graph.Visible = !_controller.Config.Graph.Visible;
            _controller.ScheduleSave();
            ApplyGraphTimer();
            Log.Info("Gráfica de frametime " + (_controller.Config.Graph.Visible ? "visible" : "oculta"));
            UpdateGraph(_controller.Snapshot);
        };
        RegisterGraphHotkey();
        _graphTimer = new System.Windows.Threading.DispatcherTimer();
        _graphTimer.Tick += (_, _) => UpdateGraph(_controller.Snapshot);
        ApplyGraphTimer();
        SettingsViewModel.GraphSettingsChanged += ApplyGraphTimer;
        SettingsViewModel.RestartRequested += RestartApp;
        SettingsViewModel.CheckUpdatesRequested += () => _ = CheckForUpdatesAsync(manual: true);
        MainViewModel.UpdateNowRequested += () =>
        {
            _installUpdateOnExit = true;
            RestartApp();
        };
        MainViewModel.UpdateLaterRequested += () => _installUpdateOnExit = true;
        _updater.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (_main?.DataContext is MainViewModel vm) vm.Settings.UpdateStatus = _updater.Status;
        });

        _gameLayoutHotkey = new HotkeyManager();
        _gameLayoutHotkey.Pressed += _controller.ReturnToGameLayout;
        _controller.PresetsChanged += () =>
        {
            for (var i = 0; i < _presetHotkeys.Length; i++) RegisterPreset(i);
            RegisterGameLayoutHotkey();
        };
        _bannerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _bannerTimer.Tick += (_, _) =>
        {
            _bannerTimer.Stop();
            _overlayModel.Banner = "";
            OnSnapshot(_controller.Snapshot);
        };
        _controller.BannerRequested += text =>
        {
            _overlayModel.Banner = text;
            _bannerTimer.Stop();
            _bannerTimer.Start();
            OnSnapshot(_controller.Snapshot);
        };

        _main = new MainWindow(new MainViewModel(_controller, _alerts, RegisterHotkey,
            i => i < 0 ? RegisterGameLayoutHotkey() : RegisterPreset(i), RegisterGraphHotkey));
        MainWindow = _main;
        _main.Closing += OnMainClosing;
        _main.IsVisibleChanged += (_, _) => UpdateEditorOpen();
        _main.StateChanged += (_, _) => UpdateEditorOpen();
        ((MainViewModel)_main.DataContext).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentPage)) UpdateEditorOpen();
        };

        _tray = new TrayIcon(ShowMain, _controller.ToggleOverlay, () => _alerts.CancelShutdown(Tr.T("Apagado cancelado desde la bandeja")), ExitApp,
            () => _controller.Config.Presets.Select(p => p.HotkeyKey == 0 ? p.Name
                : $"{p.Name}  ({HotkeyManager.Describe(p.HotkeyKey, p.HotkeyModifiers)})").ToList(), () => _controller.Config.ActivePreset,
            i => _controller.ActivatePreset(i, overrideGameProfile: false));
        _alerts.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AlertManager.Shutdown)) _tray.SetShutdownPending(_alerts.Shutdown.Pending);
        };
        _controller.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppController.OverlayShouldShow)) _tray.SetOverlayVisible(_controller.OverlayShouldShow);
        };

        _startedMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!_startedMinimized) _main.Show();
        _controller.Start(ParseTargetPid(e.Args));

        // Actualizaciones: a los 20 s de arrancar y luego cada 6 horas.
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (_controller.Config.AutoUpdate) _ = CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
        var exe = Environment.ProcessPath;
        var recreate = _controller.Config.StartupTaskVersion != Updater.CurrentText;
        if (!string.IsNullOrEmpty(exe))
            Task.Run(() =>
            {
                StartupTask.RefreshPath(exe, recreate);
                if (recreate) Dispatcher.BeginInvoke(() =>
                {
                    _controller.Config.StartupTaskVersion = Updater.CurrentText;
                    _controller.ScheduleSave();
                });
            });
    }

    private bool RegisterHotkey(int key, int modifiers)
    {
        var ok = _hotkeys!.Register(key, modifiers);
        HotkeyManager.Status["overlay"] = ok;
        var name = HotkeyManager.Describe(key, modifiers);
        _controller!.HotkeyStatus = ok
            ? Tr.F("{0} muestra u oculta el overlay.", name)
            : Tr.F("{0} ya lo usa otra aplicación (¿SpotLay 10 abierto?). Elige otra tecla.", name);
        if (!ok) Log.Warn("No se pudo registrar el atajo " + name);
        return ok;
    }

    /// <summary>Tecla de la gráfica de frametime (Mayús + F9). false si otra app ya la usa.</summary>
    public bool RegisterGraphHotkey()
    {
        var g = _controller!.Config.Graph;
        if (g.HotkeyKey == 0)
        {
            _graphHotkey!.Unregister();
            return true;
        }
        var ok = _graphHotkey!.Register(g.HotkeyKey, g.HotkeyModifiers);
        HotkeyManager.Status["graph"] = ok;
        if (!ok) Log.Warn("No se pudo registrar la tecla de la gráfica: " + HotkeyManager.Describe(g.HotkeyKey, g.HotkeyModifiers));
        return ok;
    }

    private void UpdateGraph(EngineSnapshot snapshot)
    {
        if (_graph is null || _controller is null) return;
        var g = _controller.Config.Graph;
        if (!g.Visible)
        {
            if (_graph.IsVisible) _graph.Hide();
            return;
        }
        _graph.Update(snapshot.FrameGen.Stats, snapshot.Game.IsGame, g, snapshot.Game.Monitor);
    }

    /// <summary>Tecla para volver al diseño del juego. false si otra app ya la usa.</summary>
    private bool RegisterGameLayoutHotkey()
    {
        var c = _controller!.Config;
        if (c.GameLayoutHotkeyKey == 0)
        {
            _gameLayoutHotkey!.Unregister();
            return true;
        }
        var ok = _gameLayoutHotkey!.Register(c.GameLayoutHotkeyKey, c.GameLayoutHotkeyModifiers);
        HotkeyManager.Status["game"] = ok;
        if (!ok) Log.Warn("No se pudo registrar la tecla de volver al diseño del juego");
        return ok;
    }

    /// <summary>Registra la tecla del diseño N. false si otra app ya la usa.</summary>
    private bool RegisterPreset(int index)
    {
        var preset = _controller!.Config.Presets[index];
        if (preset.HotkeyKey == 0)
        {
            _presetHotkeys[index].Unregister();
            return true;
        }
        var ok = _presetHotkeys[index].Register(preset.HotkeyKey, preset.HotkeyModifiers);
        HotkeyManager.Status["preset" + index] = ok;
        if (!ok) Log.Warn($"No se pudo registrar la tecla del diseño {index + 1}: {HotkeyManager.Describe(preset.HotkeyKey, preset.HotkeyModifiers)}");
        return ok;
    }

    private void RebuildOverlay()
    {
        if (_controller is null || _overlayModel is null || _overlay is null) return;
        _overlayModel.SetLayout(_controller.ActiveLayout, _controller.Snapshot.Metrics);
        _overlayModel.IsEditing = _controller.IsEditPreview;
        _overlayModel.Update(_controller.Snapshot, _controller.Config.HideGameMetricsWithoutGame, StandIn());
        _overlay.SetPlacement(_controller.ActiveLayout, _controller.OverlayMonitor);
        _overlay.SetEditable(_controller.IsEditPreview);
        _overlay.Invalidate();
    }

    private void OnSnapshot(EngineSnapshot snapshot)
    {
        if (_overlayModel is null || _overlay is null || _controller is null) return;
        try
        {
            _alerts?.Evaluate(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error("Error evaluando avisos", ex);
        }
        _overlayModel.IsEditing = _controller.IsEditPreview;
        _overlayModel.Update(snapshot, _controller.Config.HideGameMetricsWithoutGame, StandIn());
        if (_alerts is not null) _overlayModel.ApplyAlerts(_alerts.ActiveAlerts);
        var show = _controller.OverlayShouldShow && (_overlayModel.HasVisibleTiles || _overlayModel.Banner.Length > 0);
        if (show && !_overlay.IsVisible)
        {
            _overlay.Show();
            _overlay.Invalidate();
        }
        else if (!show && _overlay.IsVisible)
        {
            _overlay.Hide();
        }
        _overlay.SetPlacement(_controller.ActiveLayout, _controller.OverlayMonitor);
        _overlay.SetEditable(_controller.IsEditPreview && _overlay.IsVisible);
        // Ctrl + Mayús + flechas solo con el overlay a la vista y un juego delante
        // (o cualquier app a pantalla completa: en Vulkan/OpenGL puede no haber frames medidos).
        _mover?.SetEnabled(_overlay.IsVisible && (snapshot.Game.IsGame || snapshot.Game.Fullscreen));
    }

    private readonly HashSet<uint> _exclusiveWarned = [];

    /// <summary>
    /// En pantalla completa exclusiva el overlay no se ve. Se avisa al salir del juego
    /// (mientras está en exclusiva Windows descarta las notificaciones), una vez por partida.
    /// </summary>
    private void OnLeftExclusiveGame(string exeName)
    {
        if (_controller?.ExclusiveGame is not { } ex || !_exclusiveWarned.Add(ex.Pid)) return;
        Log.Info($"{exeName} está en pantalla completa exclusiva: el overlay no se puede mostrar");
        _tray?.Notify(Tr.F("{0}: pantalla completa exclusiva", SpotLayConfig.ProfileKey(exeName)),
            Tr.T("El overlay no se puede mostrar encima. Pon el juego en ventana sin bordes (borderless)."));
    }

    // Editando fuera del juego, FPS/FG/frame time muestran el último valor medido (o uno de ejemplo).
    private IReadOnlyDictionary<string, SpotLay.Core.Metrics.MetricValue>? StandIn() =>
        _controller!.IsEditPreview ? _controller.GameStandIn : null;

    /// <summary>La página Overlay está a la vista: el overlay real pasa a modo edición.</summary>
    private void UpdateEditorOpen()
    {
        if (_main is null || _controller is null) return;
        _controller.EditorOpen = _main.IsVisible && _main.WindowState != WindowState.Minimized &&
                                 _main.DataContext is MainViewModel { CurrentPage: OverlayEditorViewModel };
    }

    /// <summary>Mueve el overlay que se está viendo (el del perfil del juego o el global).</summary>
    private void MoveOverlayBy(int dx, int dy)
    {
        if (_controller is null || _overlay?.Current is not { } now) return;
        OverlayPlacement.SetAnchored(_controller.ActiveLayout, now.Screen, now.Width, now.Height, now.X + dx, now.Y + dy);
        _controller.NotifyPlacementEdited();
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void OnMainClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        // Cerrar la ventana deja SpotLay en la bandeja: el overlay sigue funcionando.
        e.Cancel = true;
        _main!.Hide();
        _controller!.SaveNow();
        // El aviso solo sale la primera vez (queda guardado).
        if (_trayHintShown || _controller.Config.TrayHintShown) return;
        _trayHintShown = true;
        _controller.Config.TrayHintShown = true;
        _controller.ScheduleSave();
        _tray?.Notify(Tr.T("SpotLay sigue funcionando"), Tr.T("El overlay sigue activo. Ábrelo o ciérralo desde el icono de la bandeja."));
    }

    private readonly Updater _updater = new();
    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private bool _startedMinimized;
    private bool _installUpdateOnExit;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    /// <summary>
    /// Busca y descarga. Si SpotLay acaba de arrancar con Windows (sin ventana), instala ya y
    /// se reinicia; si está en uso, avisa y deja elegir "ahora" o "al cerrar".
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!await _updater.CheckAndDownloadAsync() || _updater.Ready is not { } version || _exiting) return;
        var justStartedHidden = _startedMinimized && _main is { IsVisible: false } && DateTime.UtcNow - _startedAt < TimeSpan.FromMinutes(5);
        if (justStartedHidden && !manual)
        {
            Log.Info($"Actualización {version}: se instala al arrancar con Windows");
            _installUpdateOnExit = true;
            RestartApp();
            return;
        }
        if (_main?.DataContext is MainViewModel vm) vm.ShowUpdate(version);
        _tray?.Notify(Tr.F("SpotLay {0} disponible", $"{version.Major}.{version.Minor}.{version.Build}"),
            Tr.T("Ábrelo para reiniciar SpotLay ahora, o se instalará al cerrarlo."));
        _installUpdateOnExit = true;
    }

    /// <summary>Cierra de forma ordenada y vuelve a abrir SpotLay (p. ej. al cambiar el idioma).</summary>
    private void RestartApp()
    {
        _restartAfterExit = true;
        _restartHidden = _startedMinimized && _main is { IsVisible: false };
        ExitApp();
    }

    private bool _restartAfterExit;
    private bool _restartHidden;

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info($"SpotLay {Updater.CurrentText} cerrándose");
        _hotkeys?.Dispose();
        foreach (var h in _presetHotkeys) h?.Dispose();
        _gameLayoutHotkey?.Dispose();
        _graphHotkey?.Dispose();
        _graph?.Close();
        _mover?.Dispose();
        _alerts?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        _overlay?.Close();
        _main?.Close();
        // Con los ventiladores ya devueltos a la BIOS: el exe nuevo ocupa el sitio del actual.
        if (_installUpdateOnExit) _updater.Install();
        Log.Close();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows se apaga o cierra sesión (también tras un apagado de emergencia).
        Log.Info("Windows está cerrando la sesión");
        _controller?.SaveNow();
        _controller?.Engine.ReleaseFansFailSafe(Tr.T("Windows se está cerrando"));
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting && _controller is not null)
        {
            _exiting = true;
            _hotkeys?.Dispose();
            _alerts?.Dispose();
            _tray?.Dispose();
            _controller.Dispose();
            Log.Close();
        }
        _instanceMutex?.Dispose();
        _showEvent?.Dispose();
        _exitEvent?.Dispose();
        if (_restartAfterExit && Environment.ProcessPath is { } exe)
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true };
            // Si arrancó con Windows sin ventana, sigue sin ventana tras reiniciarse.
            if (_restartHidden) start.ArgumentList.Add("--minimized");
            System.Diagnostics.Process.Start(start);
        }
        base.OnExit(e);
    }

    private static uint ParseTargetPid(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--pid", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && uint.TryParse(args[index + 1], out var pid) ? pid : 0;
    }
}
