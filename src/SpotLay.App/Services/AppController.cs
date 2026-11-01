using System.Windows.Threading;
using SpotLay.App.Infrastructure;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.Services;

/// <summary>
/// Estado central de la app: configuracion, medicion en vivo, visibilidad del
/// overlay y diseño que se esta editando. Todo en el hilo de la interfaz.
/// </summary>
public sealed class AppController : ObservableObject, IDisposable
{
    public const string GlobalTarget = "global";

    private readonly ConfigStore _store = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Dispatcher _dispatcher;
    private int _snapshotQueued;
    private EngineSnapshot _latest = EngineSnapshot.Empty;
    private bool _userHidden;
    // F8 fuera de juegos: overlay global visible en todo Windows hasta volver a pulsarlo.
    private bool _desktopShown;
    // Tras pulsar la tecla de un diseño, ese diseño manda sobre el perfil de este juego hasta que cambie el juego.
    private string? _presetOverrideExe;
    // Diseño global que había antes de pulsar Ctrl+Mayús+N: se recupera con Ctrl+Mayús+0 o al salir del juego.
    private int? _presetBeforeOverride;
    private string _editingTarget = GlobalTarget;
    private bool _overlayShouldShow;
    private bool _editorOpen;
    private bool _isEditPreview;
    private bool _wasGame;
    private IntPtr _lastGameMonitor;
    private readonly Dictionary<string, MetricValue> _lastGameValues = new(StringComparer.OrdinalIgnoreCase);
    private string _hotkeyStatus = "";

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Config = _store.Load();
        FirstRun = !_store.ExistedOnStartup;
        ActiveLayout = Config.Global;
        ActiveLayoutName = Config.Presets[Config.ActivePreset].Name;
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => SaveNow(), dispatcher);
        _saveTimer.Stop();
        ImportLegacySafetyOnce();
        Engine.SetCoolingConfig(Config.Cooling);
        Engine.FrameStatsSeconds = Config.FrameStatsSeconds;
        Engine.Sessions.Limit = Config.SessionLimit;
        Engine.Updated += OnEngineUpdated;
    }

    public SpotLayConfig Config { get; private set; }
    public MonitoringEngine Engine { get; } = new();
    public bool FirstRun { get; }
    public EngineSnapshot Snapshot { get; private set; } = EngineSnapshot.Empty;
    public OverlayLayout ActiveLayout { get; private set; }
    public string ActiveLayoutName { get; private set; } = "";

    /// <summary>Snapshot nuevo (hilo de interfaz, maximo 4 por segundo).</summary>
    public event Action<EngineSnapshot>? SnapshotUpdated;
    /// <summary>Se ha editado un diseño, o han cambiado los perfiles o el diseño que se edita.</summary>
    public event Action? LayoutChanged;
    /// <summary>El overlay pasa a usar otro diseño (p. ej. se entra en un juego con perfil).</summary>
    public event Action? ActiveLayoutChanged;
    public event Action? SettingsChanged;
    /// <summary>Solo ha cambiado la posición del overlay (mini-monitor o flechas): no hace falta rehacer las tarjetas.</summary>
    public event Action? PlacementEdited;
    /// <summary>Han cambiado los avisos o el apagado de emergencia.</summary>
    public event Action? SafetyChanged;
    /// <summary>Ha cambiado la configuración de ventiladores.</summary>
    public event Action? CoolingChanged;

    public bool OverlayShouldShow { get => _overlayShouldShow; private set => Set(ref _overlayShouldShow, value); }
    public string HotkeyStatus { get => _hotkeyStatus; set => Set(ref _hotkeyStatus, value); }
    public bool OverlayUserHidden => _userHidden;

    public bool HasLegacyToImport => LegacyImporter.LegacyExists && Config.LegacyImportedAt is null;

    public string EditingTarget
    {
        get => _editingTarget;
        set
        {
            var target = value == GlobalTarget || Config.Profiles.ContainsKey(value) ? value : GlobalTarget;
            if (!Set(ref _editingTarget, target)) return;
            // En modo edición el overlay real cambia al diseño elegido.
            if (_isEditPreview) ApplySnapshot(Snapshot);
            LayoutChanged?.Invoke();
        }
    }

    /// <summary>
    /// La página Overlay está a la vista (ventana de SpotLay abierta y no minimizada).
    /// Sin un juego delante, el overlay real enseña entonces el diseño que se edita.
    /// </summary>
    public bool EditorOpen
    {
        get => _editorOpen;
        set
        {
            if (!Set(ref _editorOpen, value)) return;
            ApplySnapshot(Snapshot);
        }
    }

    /// <summary>El overlay real muestra el diseño que se edita, con las métricas del juego de muestra.</summary>
    public bool IsEditPreview { get => _isEditPreview; private set => Set(ref _isEditPreview, value); }

    /// <summary>Últimos valores de FPS, FG, frame time… del último juego (para ver el overlay al editarlo fuera del juego).</summary>
    public IReadOnlyDictionary<string, MetricValue> GameStandIn => _lastGameValues;

    /// <summary>Monitor donde va el overlay: el del juego; al editar fuera del juego, el del último juego.</summary>
    public IntPtr OverlayMonitor => _isEditPreview && _lastGameMonitor != IntPtr.Zero ? _lastGameMonitor : Snapshot.Game.Monitor;

    public OverlayLayout EditingLayout =>
        _editingTarget != GlobalTarget && Config.Profiles.TryGetValue(_editingTarget, out var p) ? p.Layout : Config.Global;

    public string EditingName => _editingTarget == GlobalTarget ? Tr.T("Overlay global") : _editingTarget;

    public void Start(uint targetProcessOverride)
    {
        Engine.TargetProcessOverride = targetProcessOverride;
        Engine.Start();
    }

    private void OnEngineUpdated(EngineSnapshot snapshot)
    {
        _latest = snapshot;
        // Coalescer: si la interfaz va con retraso no se acumulan instantaneas viejas.
        if (Interlocked.Exchange(ref _snapshotQueued, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Interlocked.Exchange(ref _snapshotQueued, 0);
            ApplySnapshot(_latest);
        });
    }

    /// <summary>
    /// Último juego visto en pantalla completa exclusiva (el overlay no se ve ahí) mientras
    /// siga abierto. Sirve para avisar después, cuando el usuario sale del juego.
    /// </summary>
    public (uint Pid, string ExeName)? ExclusiveGame { get; private set; }

    /// <summary>Se acaba de salir (Alt+Tab) de un juego en pantalla completa exclusiva.</summary>
    public event Action<string>? LeftExclusiveGame;

    private bool _exclusiveInFront;
    private DateTime _exclusiveAliveCheck;

    private void TrackExclusiveGame(GameState game)
    {
        if (game.Exclusive)
        {
            ExclusiveGame = (game.ProcessId, game.ExeName);
            _exclusiveInFront = true;
            return;
        }
        if (ExclusiveGame is not { } ex) return;
        if (game.ProcessId == ex.Pid)
        {
            // Mismo juego delante pero ya no en exclusiva: lo han pasado a sin bordes.
            if (game.Fullscreen) ExclusiveGame = null;
            return;
        }
        if (_exclusiveInFront)
        {
            _exclusiveInFront = false;
            LeftExclusiveGame?.Invoke(ex.ExeName);
        }
        if (DateTime.UtcNow - _exclusiveAliveCheck < TimeSpan.FromSeconds(1)) return;
        _exclusiveAliveCheck = DateTime.UtcNow;
        if (!ProcessAlive(ex.Pid)) ExclusiveGame = null;
    }

    private static bool ProcessAlive(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void ApplySnapshot(EngineSnapshot snapshot)
    {
        Snapshot = snapshot;
        if (_presetOverrideExe is not null && !string.Equals(_presetOverrideExe, snapshot.Game.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            _presetOverrideExe = null;
            RestorePresetBeforeOverride();
        }

        var inGameContext = snapshot.Game.IsGame;
        if (inGameContext)
        {
            _lastGameMonitor = snapshot.Game.Monitor;
            foreach (var (id, value) in snapshot.Metrics.Metrics)
                if (KnownMetrics.IsGameMetric(id) && value.HasValue) _lastGameValues[id] = value;
        }
        else if (_wasGame)
        {
            // Al salir de un juego (Alt+Tab para retocar el overlay) se edita el diseño que estaba usando.
            var key = ReferenceEquals(ActiveLayout, Config.Global) ? GlobalTarget
                : Config.Profiles.FirstOrDefault(p => ReferenceEquals(p.Value.Layout, ActiveLayout)).Key ?? GlobalTarget;
            if (key != _editingTarget)
            {
                _editingTarget = key;
                OnPropertyChanged(nameof(EditingTarget));
                _dispatcher.BeginInvoke(() => LayoutChanged?.Invoke());
            }
        }
        _wasGame = inGameContext;
        TrackExclusiveGame(snapshot.Game);
        var editPreview = _editorOpen && !inGameContext;
        var editPreviewChanged = editPreview != _isEditPreview;
        IsEditPreview = editPreview;
        if (inGameContext && Config.ProfileFor(snapshot.Game.ExeName) is { } seen &&
            (seen.LastSeenAsGame is null || DateTime.Now - seen.LastSeenAsGame > TimeSpan.FromHours(1)))
        {
            seen.LastSeenAsGame = DateTime.Now;
            ScheduleSave();
        }
        var layout = editPreview ? EditingLayout
            : inGameContext && _presetOverrideExe is null ? Config.ActiveLayout(snapshot.Game.ExeName) : Config.Global;
        if (!ReferenceEquals(layout, ActiveLayout) || editPreviewChanged)
        {
            ActiveLayout = layout;
            ActiveLayoutName = editPreview ? EditingName
                : ReferenceEquals(layout, Config.Global) ? Config.Presets[Config.ActivePreset].Name : SpotLayConfig.ProfileKey(snapshot.Game.ExeName);
            ActiveLayoutChanged?.Invoke();
        }

        // Editando se ve siempre, aunque esté oculto o sea "solo en juegos": es lo que se está diseñando.
        OverlayShouldShow = editPreview || (Config.OverlayEnabled && !_userHidden &&
                            (!Config.ShowOnlyInGames || snapshot.Game.IsGame || _desktopShown));
        SnapshotUpdated?.Invoke(snapshot);
    }

    /// <summary>
    /// F8 / bandeja. En un juego: oculta o muestra su overlay. Fuera de juegos:
    /// enciende o apaga el overlay global en todo Windows (también con SpotLay delante).
    /// </summary>
    public void ToggleOverlay()
    {
        if (OverlayShouldShow)
        {
            if (!Snapshot.Game.IsGame && _desktopShown) _desktopShown = false;
            else _userHidden = true;
        }
        else
        {
            _userHidden = false;
            if (!Config.OverlayEnabled)
            {
                Config.OverlayEnabled = true;
                ScheduleSave();
            }
            if (Config.ShowOnlyInGames && !Snapshot.Game.IsGame) _desktopShown = true;
        }
        ApplySnapshot(Snapshot);
        OnPropertyChanged(nameof(OverlayUserHidden));
    }

    public void NotifyLayoutEdited()
    {
        LayoutChanged?.Invoke();
        ScheduleSave();
    }

    public void NotifySettingsChanged()
    {
        ApplySnapshot(Snapshot);
        SettingsChanged?.Invoke();
        ScheduleSave();
    }

    /// <summary>Activa uno de los 5 diseños. Con la tecla, además manda sobre el perfil del juego abierto.</summary>
    public void ActivatePreset(int index, bool overrideGameProfile)
    {
        if (index < 0 || index >= Config.Presets.Count) return;
        // Solo dentro de un juego es temporal; fuera de juegos la tecla cambia el diseño global sin más.
        var temporary = overrideGameProfile && Snapshot.Game.IsGame;
        if (temporary && _presetBeforeOverride is null) _presetBeforeOverride = Config.ActivePreset;
        if (!temporary) _presetBeforeOverride = null;
        Config.Presets[Config.ActivePreset].Layout = Config.Global;
        Config.ActivePreset = index;
        Config.Global = Config.Presets[index].Layout;
        _presetOverrideExe = temporary ? Snapshot.Game.ExeName : null;
        // Con la tecla se quiere ver el diseño elegido: si el overlay no se veía, se muestra.
        if (overrideGameProfile && !OverlayShouldShow)
        {
            _userHidden = false;
            Config.OverlayEnabled = true;
            if (Config.ShowOnlyInGames && !Snapshot.Game.IsGame) _desktopShown = true;
        }
        Log.Info($"Diseño {index + 1} ({Config.Presets[index].Name}) activado{(_presetOverrideExe is null ? "" : " sobre el perfil de " + _presetOverrideExe)}");
        ActiveLayout = Config.Global;
        ActiveLayoutName = Config.Presets[index].Name;
        ApplySnapshot(Snapshot);
        ActiveLayoutChanged?.Invoke();
        LayoutChanged?.Invoke();
        PresetsChanged?.Invoke();
        ScheduleSave();
        if (overrideGameProfile) BannerRequested?.Invoke(Tr.F("Diseño {0} · {1}", index + 1, Config.Presets[index].Name));
    }

    /// <summary>Ctrl+Mayús+0: deja de forzar un diseño y vuelve al del juego (o al global que había).</summary>
    public void ReturnToGameLayout()
    {
        _presetOverrideExe = null;
        RestorePresetBeforeOverride();
        _userHidden = false;
        ApplySnapshot(Snapshot);
        ActiveLayoutChanged?.Invoke();
        var name = Snapshot.Game.IsGame && Config.ProfileFor(Snapshot.Game.ExeName) is { Enabled: true }
            ? SpotLayConfig.ProfileKey(Snapshot.Game.ExeName) : Config.Presets[Config.ActivePreset].Name;
        Log.Info("Vuelta al diseño del juego: " + name);
        BannerRequested?.Invoke(Tr.T("Diseño del juego · ") + name);
    }

    private void RestorePresetBeforeOverride()
    {
        if (_presetBeforeOverride is not { } previous) return;
        _presetBeforeOverride = null;
        Config.Presets[Config.ActivePreset].Layout = Config.Global;
        Config.ActivePreset = previous;
        Config.Global = Config.Presets[previous].Layout;
        ActiveLayout = Config.Global;
        ActiveLayoutName = Config.Presets[previous].Name;
        LayoutChanged?.Invoke();
        PresetsChanged?.Invoke();
        ScheduleSave();
    }

    /// <summary>Texto corto que se enseña 1,5 s en el overlay (al cambiar de diseño con una tecla).</summary>
    public event Action<string>? BannerRequested;

    public event Action? PresetsChanged;

    public void NotifyPresetsChanged()
    {
        PresetsChanged?.Invoke();
        ScheduleSave();
    }

    /// <summary>Ctrl + clic en una tarjeta del overlay: el editor selecciona esa métrica.</summary>
    public event Action<string>? MetricSelectRequested;

    public void RequestSelectMetric(string metricId) => MetricSelectRequested?.Invoke(metricId);

    /// <summary>Ctrl + arrastrar en el overlay: la métrica pasa a otro puesto del diseño que se ve.</summary>
    public void MoveMetric(string metricId, int index)
    {
        var metrics = ActiveLayout.Metrics;
        var from = metrics.FindIndex(m => m.Equals(metricId, StringComparison.OrdinalIgnoreCase));
        if (from < 0) return;
        metrics.RemoveAt(from);
        metrics.Insert(Math.Clamp(index, 0, metrics.Count), metricId);
        NotifyLayoutEdited();
        MetricSelectRequested?.Invoke(metricId);
    }

    public void NotifyPlacementEdited()
    {
        PlacementEdited?.Invoke();
        ScheduleSave();
    }

    public void NotifySafetyChanged()
    {
        SafetyChanged?.Invoke();
        ScheduleSave();
    }

    public void NotifyCoolingChanged()
    {
        Engine.SetCoolingConfig(Config.Cooling);
        CoolingChanged?.Invoke();
        ScheduleSave();
    }

    /// <summary>
    /// Quien importó el diseño de SpotLay 10 en la fase 1 recibe ahora sus
    /// alarmas, apagado y ventiladores una sola vez, sin tener que pulsar nada.
    /// </summary>
    private void ImportLegacySafetyOnce()
    {
        if (Config.LegacyImportedAt is null || Config.LegacySafetyImportedAt is not null || !LegacyImporter.LegacyExists) return;
        _store.Backup("antes-de-importar-fase2");
        if (LegacyImporter.ImportSafetyInto(Config).Imported) SaveNow();
    }

    public void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            _store.Save(Config);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar la configuración", ex);
        }
    }

    public LegacyImportResult ImportLegacy()
    {
        _store.Backup("antes-de-importar");
        var result = LegacyImporter.ImportInto(Config);
        if (result.Imported)
        {
            Config.Presets[Config.ActivePreset].Layout = Config.Global;
            _editingTarget = GlobalTarget;
            ActiveLayout = Config.Global;
            Engine.SetCoolingConfig(Config.Cooling);
            SaveNow();
            ApplySnapshot(Snapshot);
            LayoutChanged?.Invoke();
            ActiveLayoutChanged?.Invoke();
            SettingsChanged?.Invoke();
            SafetyChanged?.Invoke();
            CoolingChanged?.Invoke();
            OnPropertyChanged(nameof(HasLegacyToImport));
        }
        return result;
    }

    /// <summary>Crea (o reactiva) el perfil del juego detectado copiando el diseño global.</summary>
    public string? CreateProfileForCurrentGame()
    {
        var exe = Snapshot.Game.ExeName;
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var key = SpotLayConfig.ProfileKey(exe);
        if (!Config.Profiles.TryGetValue(key, out var profile))
            Config.Profiles[key] = profile = new GameProfile { ExeName = key, Layout = Config.Global.Clone() };
        profile.Enabled = true;
        _editingTarget = key;
        OnPropertyChanged(nameof(EditingTarget));
        ApplySnapshot(Snapshot);
        NotifyLayoutEdited();
        return key;
    }

    public void DeleteProfile(string key)
    {
        if (!Config.Profiles.Remove(key)) return;
        if (_editingTarget == key) _editingTarget = GlobalTarget;
        OnPropertyChanged(nameof(EditingTarget));
        ApplySnapshot(Snapshot);
        NotifyLayoutEdited();
    }

    /// <summary>
    /// Copia un diseño (global o de un juego) sobre otro. Si el destino es un
    /// juego sin perfil, se crea su perfil activo.
    /// </summary>
    public void CopyLayout(string sourceKey, string targetKey)
    {
        var source = sourceKey == GlobalTarget ? Config.Global
            : PresetIndex(sourceKey) is { } si ? Config.Presets[si].Layout
            : Config.Profiles.TryGetValue(sourceKey, out var sp) ? sp.Layout : null;
        if (source is null || sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase)) return;
        PutLayout(source.Clone(), targetKey);
        Log.Info($"Diseño copiado: {sourceKey} -> {targetKey}");
    }

    /// <summary>Pone un diseño en un destino: global en uso, "preset:N", o un juego (crea su perfil si no tiene).</summary>
    public void PutLayout(OverlayLayout copy, string targetKey)
    {
        if (targetKey == GlobalTarget || PresetIndex(targetKey) == Config.ActivePreset)
        {
            Config.Global = copy;
            Config.Presets[Config.ActivePreset].Layout = copy;
        }
        else if (PresetIndex(targetKey) is { } ti)
        {
            Config.Presets[ti].Layout = copy;
        }
        else
        {
            var key = SpotLayConfig.ProfileKey(targetKey);
            if (!Config.Profiles.TryGetValue(key, out var profile))
                Config.Profiles[key] = profile = new GameProfile { ExeName = key, Enabled = true };
            profile.Layout = copy;
        }
        // El overlay puede estar usando el diseño sustituido: se recalcula cuál toca.
        ActiveLayout = Config.Global;
        ApplySnapshot(Snapshot);
        ActiveLayoutChanged?.Invoke();
        NotifyLayoutEdited();
    }

    /// <summary>"preset:N" (0..4) = uno de los 5 diseños globales.</summary>
    public static int? PresetIndex(string key) =>
        key.StartsWith("preset:", StringComparison.Ordinal) && int.TryParse(key[7..], out var i) && i is >= 0 and < SpotLayConfig.PresetCount ? i : null;

    public void ResetProfileToGlobal(string key)
    {
        if (!Config.Profiles.TryGetValue(key, out var profile)) return;
        profile.Layout = Config.Global.Clone();
        ApplySnapshot(Snapshot);
        NotifyLayoutEdited();
    }

    public void Dispose()
    {
        SaveNow();
        Engine.Updated -= OnEngineUpdated;
        Engine.Dispose();
    }
}
