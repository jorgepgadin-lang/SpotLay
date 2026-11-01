using System.Diagnostics;
using SpotLay.Core.Config;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.Alarms;

public sealed record ActiveAlert(string MetricId, MetricValue Metric, AlarmRule Rule, double Severity);

public enum ShutdownPhase
{
    /// <summary>Sin apagados configurados.</summary>
    Disabled,
    /// <summary>Armado, sensores dentro del límite.</summary>
    Armed,
    Countdown,
    /// <summary>Se ha pedido a Windows que se apague (o, en simulación, se habría pedido).</summary>
    Issued,
    /// <summary>Pausa tras una cancelación.</summary>
    Cooldown
}

public sealed record ShutdownState(
    ShutdownPhase Phase,
    string MetricId,
    MetricValue? Metric,
    AlarmRule? Rule,
    int RemainingSeconds,
    bool Simulated,
    string Status)
{
    public static ShutdownState Idle { get; } = new(ShutdownPhase.Disabled, "", null, null, 0, false, Tr.T("Apagado de emergencia desactivado"));
    public bool Pending => Phase == ShutdownPhase.Countdown;
}

public sealed record AlarmResult(IReadOnlyList<ActiveAlert> Alerts, ShutdownState Shutdown);

/// <summary>
/// Avisos por sensor (con histéresis) y apagado de emergencia con cuenta atrás,
/// cancelación y pausa, como en SpotLay 10. Sin interfaz: la app muestra las
/// ventanas y registra la tecla de cancelación según lo que devuelve Evaluate.
/// Se usa siempre desde el mismo hilo (el de la interfaz).
/// </summary>
public sealed class AlarmEngine
{
    public const string SimulationId = "__simulacion__";

    private readonly Dictionary<string, bool> _alertActive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _shutdownActive = new(StringComparer.OrdinalIgnoreCase);
    private string? _shutdownMetricId;
    private MetricValue? _shutdownMetric;
    private AlarmRule? _shutdownRule;
    private DateTime _shutdownAt;
    private DateTime _cooldownUntil;
    private DateTime _issuedAt;
    private bool _issued;
    private bool _simulation;

    /// <summary>Modo de prueba: hace toda la cuenta atrás pero nunca apaga Windows.</summary>
    public bool DryRun { get; set; }

    /// <summary>Estado del último cancelado o fallo, para mostrarlo en la página.</summary>
    public string LastEvent { get; private set; } = "";

    public AlarmResult Evaluate(MetricSnapshot snapshot, SafetyConfig config, DateTime now)
    {
        var alerts = new List<ActiveAlert>();
        var shutdownCandidates = new List<ActiveAlert>();
        foreach (var (id, rule) in config.Rules)
        {
            var metric = snapshot.Get(id);
            var value = metric?.Value;
            var valid = value is { } v && double.IsFinite(v);

            var alert = rule.AlertEnabled && valid && Check(_alertActive, id, value!.Value, rule.AlertCondition, rule.AlertThreshold, rule.AlertHysteresis);
            if (!rule.AlertEnabled || !valid) _alertActive[id] = false;
            if (alert) alerts.Add(new ActiveAlert(id, metric!, rule, Severity(id, value!.Value, rule.AlertCondition, rule.AlertThreshold)));

            var shutdown = rule.ShutdownEnabled && valid && Check(_shutdownActive, id, value!.Value, rule.ShutdownCondition, rule.ShutdownThreshold, rule.ShutdownHysteresis);
            if (!rule.ShutdownEnabled || !valid) _shutdownActive[id] = false;
            if (shutdown) shutdownCandidates.Add(new ActiveAlert(id, metric!, rule, Severity(id, value!.Value, rule.ShutdownCondition, rule.ShutdownThreshold)));
        }

        alerts.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return new AlarmResult(alerts, UpdateShutdown(config, shutdownCandidates, now));
    }

    /// <summary>Cuenta atrás de prueba: no depende de ningún sensor y nunca apaga.</summary>
    public void StartSimulation(int seconds, DateTime now)
    {
        _simulation = true;
        _issued = false;
        _shutdownMetricId = SimulationId;
        _shutdownRule = new AlarmRule { ShutdownEnabled = true, ShutdownDelaySeconds = seconds, ShutdownAutoCancel = false };
        _shutdownMetric = new MetricValue(SimulationId, Tr.T("Prueba de apagado"), null, "", MetricCategory.Otros, "SpotLay", "simulación");
        _shutdownAt = now.AddSeconds(Math.Clamp(seconds, 3, 60));
        Log.Info($"Apagado: simulación iniciada ({seconds} s)");
    }

    /// <summary>Cancelación del usuario (botón, tecla o bandeja): pausa la protección un tiempo.</summary>
    public void Cancel(string reason, SafetyConfig config, DateTime now) => Cancel(reason, config, now, pause: true);

    private void Cancel(string reason, SafetyConfig config, DateTime now, bool pause)
    {
        if (_shutdownMetricId is null) return;
        var wasSimulation = _simulation;
        Clear();
        LastEvent = reason;
        // SpotLay 10 pausaba la protección también cuando el sensor bajaba solo;
        // aqui solo pausa una cancelación manual de un apagado real.
        pause &= !wasSimulation;
        if (pause) _cooldownUntil = now.AddSeconds(config.CooldownSeconds);
        Log.Info("Apagado: " + reason + (wasSimulation ? " (simulación)" : pause ? $"; protección en pausa {config.CooldownSeconds} s" : ""));
    }

    private ShutdownState UpdateShutdown(SafetyConfig config, List<ActiveAlert> candidates, DateTime now)
    {
        var anyEnabled = config.Rules.Values.Any(r => r.ShutdownEnabled);

        if (_shutdownMetricId is not null)
        {
            if (_issued)
            {
                // Tras una simulación el aviso se queda unos segundos y desaparece.
                if (_simulation || DryRun)
                {
                    if (now - _issuedAt > TimeSpan.FromSeconds(6))
                    {
                        Clear();
                        return Result(anyEnabled ? ShutdownPhase.Armed : ShutdownPhase.Disabled, "", null, null, 0,
                            Tr.T("Simulación terminada: aquí Windows se habría apagado."));
                    }
                }
                return Result(ShutdownPhase.Issued, _shutdownMetricId, _shutdownMetric, _shutdownRule, 0,
                    _simulation || DryRun ? Tr.T("Simulación: aquí Windows se apagaría.") : Tr.T("Pedido a Windows el cierre ordenado."));
            }

            if (!_simulation)
            {
                var configured = config.Rules.GetValueOrDefault(_shutdownMetricId);
                if (configured is null || !configured.ShutdownEnabled)
                {
                    Cancel(Tr.T("Apagado desarmado desde el panel"), config, now, pause: false);
                    return UpdateShutdown(config, candidates, now);
                }
                _shutdownRule = configured;
                var active = candidates.FirstOrDefault(x => x.MetricId.Equals(_shutdownMetricId, StringComparison.OrdinalIgnoreCase));
                if (active is null)
                {
                    if (configured.ShutdownAutoCancel)
                    {
                        Cancel(Tr.T("El sensor volvió a un valor seguro"), config, now, pause: false);
                        return UpdateShutdown(config, candidates, now);
                    }
                }
                else
                {
                    _shutdownMetric = active.Metric;
                }
            }

            var remaining = Math.Max(0, (int)Math.Ceiling((_shutdownAt - now).TotalSeconds));
            if (remaining == 0)
            {
                Execute(now);
                if (_shutdownMetricId is null)
                    return Result(ShutdownPhase.Cooldown, "", null, null, 60, LastEvent);
            }
            return Result(_issued ? ShutdownPhase.Issued : ShutdownPhase.Countdown, _shutdownMetricId, _shutdownMetric, _shutdownRule, remaining,
                !_issued ? Tr.F("Apagado en {0} s", remaining)
                : _simulation || DryRun ? Tr.T("Simulación: aquí Windows se apagaría.") : Tr.T("Pedido a Windows el cierre ordenado."));
        }

        if (!anyEnabled) return Result(ShutdownPhase.Disabled, "", null, null, 0, Tr.T("Apagado de emergencia desactivado"));

        if (now < _cooldownUntil)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((_cooldownUntil - now).TotalSeconds));
            return Result(ShutdownPhase.Cooldown, "", null, null, seconds, Tr.F("Protección en pausa tras una cancelación ({0} s)", seconds));
        }

        var trigger = candidates.OrderByDescending(x => x.Severity).FirstOrDefault();
        if (trigger is null) return Result(ShutdownPhase.Armed, "", null, null, 0, Tr.T("Armado; sensores dentro del límite"));

        _shutdownMetricId = trigger.MetricId;
        _shutdownMetric = trigger.Metric;
        _shutdownRule = trigger.Rule;
        _shutdownAt = now.AddSeconds(trigger.Rule.ShutdownDelaySeconds);
        _issued = false;
        _simulation = false;
        Log.Warn($"Apagado: cuenta atrás de {trigger.Rule.ShutdownDelaySeconds} s por {trigger.MetricId} = {trigger.Metric.Value:F1} {trigger.Metric.Unit}" +
                 Tr.F(" (límite {0}){1}", trigger.Rule.ShutdownThreshold, (DryRun ? Tr.T(" [modo prueba]") : "")));
        return Result(ShutdownPhase.Countdown, trigger.MetricId, trigger.Metric, trigger.Rule, trigger.Rule.ShutdownDelaySeconds,
            Tr.F("Apagado en {0} s", trigger.Rule.ShutdownDelaySeconds));
    }

    private ShutdownState Result(ShutdownPhase phase, string id, MetricValue? metric, AlarmRule? rule, int remaining, string status) =>
        new(phase, id, metric, rule, remaining, _simulation || DryRun, status);

    private void Execute(DateTime now)
    {
        _issued = true;
        _issuedAt = now;
        var label = _shutdownMetric?.Label ?? _shutdownMetricId ?? "sensor";
        if (_simulation || DryRun)
        {
            Log.Warn($"Apagado: SIMULACIÓN, aquí se apagaría Windows por {label}");
            return;
        }

        Log.Warn($"Apagado: pidiendo a Windows el cierre por {label}");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("/s");
            start.ArgumentList.Add("/t");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("p:0:0");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(Tr.F("SpotLay: alarma de {0}", label));
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Windows rechazó el apagado", ex);
            LastEvent = Tr.T("Windows rechazó el apagado: ") + ex.Message;
            Clear();
            _cooldownUntil = now.AddMinutes(1);
        }
    }

    private void Clear()
    {
        _shutdownMetricId = null;
        _shutdownMetric = null;
        _shutdownRule = null;
        _issued = false;
        _simulation = false;
    }

    private static bool Check(Dictionary<string, bool> state, string id, double value, string condition, double threshold, double hysteresis)
    {
        var was = state.GetValueOrDefault(id);
        var active = condition == AlarmConditions.Below
            ? was ? value <= threshold + hysteresis : value <= threshold
            : was ? value >= threshold - hysteresis : value >= threshold;
        state[id] = active;
        return active;
    }

    private static double Severity(string id, double value, string condition, double threshold)
    {
        var definition = KnownMetrics.Find(id);
        var span = Math.Max(1, Math.Abs((definition?.Max ?? 100) - (definition?.Min ?? 0)));
        return condition == AlarmConditions.Below ? (threshold - value) / span : (value - threshold) / span;
    }
}
