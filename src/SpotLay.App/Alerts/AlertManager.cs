using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core;
using SpotLay.Core.Alarms;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.Alerts;

/// <summary>
/// Evalúa los avisos y el apagado de emergencia con cada lectura y muestra sus
/// ventanas. La tecla de cancelación (Alt+F9) solo se registra durante la
/// cuenta atrás, igual que en SpotLay 10, para no quitársela a los juegos.
/// </summary>
public sealed class AlertManager : ObservableObject, IDisposable
{
    private readonly AppController _c;
    private readonly AlarmEngine _engine = new();
    private readonly Dictionary<string, AlertPopupWindow> _popups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HotkeyManager _cancelHotkey = new();
    private ShutdownWindow? _shutdownWindow;
    private bool _hotkeyRegistered;
    private (int Key, int Modifiers) _registeredHotkey;
    private string? _testMetricId;
    private AlarmRule? _testRule;
    private DateTime _testUntil;
    private ShutdownState _shutdown = ShutdownState.Idle;
    private string _hotkeyStatus = "";

    public AlertManager(AppController controller, bool dryRun)
    {
        _c = controller;
        _engine.DryRun = dryRun;
        _cancelHotkey.Pressed += () => CancelShutdown(Tr.T("Apagado cancelado con ") + HotkeyText);
        if (dryRun) Log(Tr.T("Modo prueba del apagado: la cuenta atrás nunca apaga Windows"));
    }

    /// <summary>Sensores en aviso ahora mismo (para colorear las tarjetas del overlay).</summary>
    public IReadOnlyDictionary<string, AlarmRule> ActiveAlerts { get; private set; } = new Dictionary<string, AlarmRule>();

    public ShutdownState Shutdown { get => _shutdown; private set => Set(ref _shutdown, value); }
    public bool DryRun => _engine.DryRun;
    public string LastEvent => _engine.LastEvent;
    public string HotkeyStatus { get => _hotkeyStatus; private set => Set(ref _hotkeyStatus, value); }

    public string HotkeyText => _c.Config.Safety.CancelHotkeyKey == 0
        ? Tr.T("el botón Cancelar")
        : HotkeyManager.Describe(_c.Config.Safety.CancelHotkeyKey, _c.Config.Safety.CancelHotkeyModifiers);

    /// <summary>Se llama con cada instantánea, antes de refrescar el overlay.</summary>
    public void Evaluate(EngineSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var safety = _c.Config.Safety;
        var result = _engine.Evaluate(snapshot.Metrics, safety, now);
        var monitor = snapshot.Game.Monitor;

        ActiveAlerts = result.Alerts.ToDictionary(a => a.MetricId, a => a.Rule, StringComparer.OrdinalIgnoreCase);

        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var alert in result.Alerts.Where(a => a.Rule.AlertPopup))
        {
            ShowPopup(alert.MetricId, alert.Metric, alert.Rule, monitor, index++);
            visible.Add(alert.MetricId);
        }

        // Aviso de prueba desde la página de Alertas.
        if (_testMetricId is not null)
        {
            // La regla la da la página: el sensor puede no tener aviso guardado todavía.
            var rule = safety.Rules.GetValueOrDefault(_testMetricId) ?? _testRule;
            if (now > _testUntil || rule is null) _testMetricId = null;
            else if (!visible.Contains(_testMetricId))
            {
                var metric = snapshot.Metrics.Get(_testMetricId) ?? new MetricValue(_testMetricId, _testMetricId, rule.AlertThreshold, "", MetricCategory.Otros, "");
                ShowPopup(_testMetricId, metric, rule, monitor, index++);
                visible.Add(_testMetricId);
            }
        }

        foreach (var (id, popup) in _popups)
            if (!visible.Contains(id) && popup.IsVisible) popup.Hide();

        UpdateShutdown(result.Shutdown, monitor);
    }

    public void CancelShutdown(string reason)
    {
        if (!Shutdown.Pending && Shutdown.Phase != ShutdownPhase.Issued) return;
        _engine.Cancel(reason, _c.Config.Safety, DateTime.UtcNow);
        _shutdownWindow?.Hide();
        ReleaseHotkey();
        Shutdown = Shutdown with { Phase = ShutdownPhase.Cooldown, Status = reason };
        OnPropertyChanged(nameof(LastEvent));
    }

    /// <summary>Cuenta atrás de prueba: enseña la ventana y la tecla de cancelar, nunca apaga.</summary>
    public void StartSimulation(int seconds = 10)
    {
        if (Shutdown.Pending) return;
        _engine.StartSimulation(seconds, DateTime.UtcNow);
        Evaluate(_c.Snapshot);
    }

    /// <summary>Muestra el aviso de un sensor durante unos segundos, aunque no esté en alarma.</summary>
    public void TestPopup(string metricId, AlarmRule rule)
    {
        _testMetricId = metricId;
        _testRule = rule;
        _testUntil = DateTime.UtcNow.AddSeconds(5);
        Evaluate(_c.Snapshot);
    }

    private void ShowPopup(string id, MetricValue metric, AlarmRule rule, IntPtr monitor, int index)
    {
        if (!_popups.TryGetValue(id, out var popup)) _popups[id] = popup = new AlertPopupWindow();
        var label = string.IsNullOrWhiteSpace(rule.AlertText) ? LabelOf(id, metric) : rule.AlertText;
        var condition = rule.AlertCondition == AlarmConditions.Below ? Tr.T("por debajo de") : Tr.T("por encima de");
        popup.Show(label, Format(metric), Tr.F("Límite: {0} {1} {2}", condition, MetricFormat.Number(rule.AlertThreshold, "0.##"), metric.Unit).TrimEnd(),
            rule, _c.Config.Safety.AlertWindow, _c.Config.Global.FontFamily, monitor, index);
    }

    private void UpdateShutdown(ShutdownState state, IntPtr monitor)
    {
        var changedPhase = state.Phase != Shutdown.Phase;
        Shutdown = state;
        if (changedPhase) OnPropertyChanged(nameof(LastEvent));

        if (state.Phase is ShutdownPhase.Countdown or ShutdownPhase.Issued && state.Metric is { } metric)
        {
            RegisterHotkey();
            var sensor = state.MetricId == AlarmEngine.SimulationId
                ? Tr.T("Prueba: ningún sensor ha superado su límite")
                : $"{LabelOf(state.MetricId, metric)}: {Format(metric)}";
            _shutdownWindow ??= new ShutdownWindow(() => CancelShutdown(Tr.T("Apagado cancelado con el botón")));
            _shutdownWindow.Update(sensor, state.RemainingSeconds, state.Phase == ShutdownPhase.Issued, state.Simulated, HotkeyText, monitor);
            return;
        }

        ReleaseHotkey();
        if (_shutdownWindow?.IsVisible == true) _shutdownWindow.Hide();
        var safety = _c.Config.Safety;
        HotkeyStatus = safety.CancelHotkeyKey == 0
            ? Tr.T("Sin tecla: cancela con el botón de la ventana o desde el icono de la bandeja.")
            : Tr.F("{0} cancela la cuenta atrás. Solo se activa mientras cuenta, para no quitársela a los juegos.", HotkeyText);
    }

    private void RegisterHotkey()
    {
        var safety = _c.Config.Safety;
        var wanted = (safety.CancelHotkeyKey, safety.CancelHotkeyModifiers);
        if (_hotkeyRegistered && wanted == _registeredHotkey) return;
        ReleaseHotkey();
        if (wanted.CancelHotkeyKey == 0) return;
        _hotkeyRegistered = _cancelHotkey.Register(wanted.CancelHotkeyKey, wanted.CancelHotkeyModifiers);
        _registeredHotkey = wanted;
        HotkeyStatus = _hotkeyRegistered
            ? Tr.F("{0} activo: púlsalo para cancelar.", HotkeyText)
            : Tr.F("{0} está ocupado por otra aplicación: cancela con el botón o desde la bandeja.", HotkeyText);
        if (!_hotkeyRegistered) Log(Tr.F("No se pudo registrar {0} para cancelar el apagado", HotkeyText));
    }

    private void ReleaseHotkey()
    {
        if (!_hotkeyRegistered) return;
        _cancelHotkey.Unregister();
        _hotkeyRegistered = false;
    }

    public string LabelOf(string id, MetricValue? metric)
    {
        var custom = _c.Config.Global.Styles.GetValueOrDefault(id)?.Label;
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        return KnownMetrics.Find(id)?.Label ?? metric?.Label ?? id;
    }

    public static string Format(MetricValue metric)
    {
        var value = MetricFormat.Value(metric);
        return string.IsNullOrWhiteSpace(metric.Unit) || metric.Text is not null ? value : $"{value} {metric.Unit}";
    }

    private static void Log(string message) => SpotLay.Core.Diagnostics.Log.Info(message);

    public void Dispose()
    {
        ReleaseHotkey();
        _cancelHotkey.Dispose();
        foreach (var popup in _popups.Values) popup.Close();
        _popups.Clear();
        _shutdownWindow?.Close();
    }
}
