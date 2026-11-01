using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using SpotLay.Core.Config;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.Cooling;

/// <summary>
/// Único dueño de las escrituras en los ventiladores (lógica de SpotLay 10).
/// La interfaz nunca recibe un IControl y ningún otro componente escribe un
/// canal, así que devolver el control a la BIOS/GPU es siempre determinista.
/// No es seguro para hilos: HardwareMonitor lo usa siempre bajo su cerrojo.
/// </summary>
internal sealed class CoolingController
{
    private sealed record Binding(
        string Id,
        string Hardware,
        string Name,
        string Category,
        IControl Control,
        ISensor DutySensor,
        ISensor? RpmSensor);

    private sealed class RuntimeState
    {
        public bool Applied { get; set; }
        public float? LastDuty { get; set; }
        public double? LastTemperature { get; set; }
        public double? CurrentTemperature { get; set; }
        public float? HeldTarget { get; set; }
        /// <summary>Gráfica devuelta a su driver porque la curva pide menos que el mínimo de la tarjeta.</summary>
        public bool DriverAuto { get; set; }
        /// <summary>Curva y ajustes con los que se calculó la velocidad retenida.</summary>
        public string SettingsKey { get; set; } = "";
        public DateTime LastWrite { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = Tr.T("Automático de fábrica");
    }

    private sealed class TestOverride(int duty, DateTime expires, float? baselineRpm)
    {
        public int Duty { get; } = duty;
        public DateTime Expires { get; } = expires;
        public float? BaselineRpm { get; } = baselineRpm;
        public float? PeakRpm { get; set; } = baselineRpm;
    }

    private sealed class CalibrationRun(string channelId)
    {
        public string ChannelId { get; } = channelId;
        // Hasta 0 %: si el ventilador puede pararse, el mínimo es 0 (modo semipasivo).
        public int[] Steps { get; } = [100, 80, 60, 50, 40, 30, 20, 10, 0];
        public int StepIndex { get; set; }
        public DateTime StepStarted { get; set; } = DateTime.UtcNow;
        // Al 100 % se espera a que las RPM dejen de subir (algunos, como los de la gráfica, tardan).
        public int Peak { get; set; }
        public DateTime LastRise { get; set; } = DateTime.UtcNow;
        public List<(int Duty, int Rpm)> Samples { get; } = [];
    }

    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TestOverride> _tests = new(StringComparer.OrdinalIgnoreCase);
    private CalibrationRun? _calibration;
    private CoolingCalibrationInfo _calibrationInfo = CoolingCalibrationInfo.Empty;
    private CoolingTestInfo _testInfo = CoolingTestInfo.Empty;
    private DateTime _nextConflictCheck;
    private string? _conflict;
    private bool? _lastEnabled;

    public CoolingSnapshot Snapshot { get; private set; } = CoolingSnapshot.Empty;

    public void Update(IReadOnlyList<IHardware> hardware, IReadOnlyList<MetricValue> metrics, CoolingConfig config)
    {
        Discover(hardware);
        RefreshConflict();
        if (_lastEnabled != config.Enabled)
        {
            _lastEnabled = config.Enabled;
            Log.Info($"Refrigeración: control {(config.Enabled ? "activado" : "desactivado")}, {_bindings.Count} canales detectados, " +
                     $"{config.Channels.Values.Count(c => c.Enabled)} configurados con control");
        }

        var now = DateTime.UtcNow;
        foreach (var id in _tests.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
            FinishTest(id, Tr.T("Prueba terminada; control automático restaurado."));

        if (_conflict is not null)
        {
            _tests.Clear();
            _testInfo = CoolingTestInfo.Empty;
            AbortCalibration(Tr.F("Calibración cancelada: {0} está abierto.", _conflict));
            ReleaseAll(Tr.F("Bloqueado: cierra {0}.", _conflict));
            BuildSnapshot(config, true, Tr.F("{0} está abierto y también controla ventiladores. SpotLay no escribirá ningún canal hasta que lo cierres.", _conflict));
            return;
        }

        var temperatures = metrics
            .Where(metric => metric.Unit == "°C" && metric.Value is { } v && double.IsFinite(v))
            .GroupBy(metric => metric.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value!.Value, StringComparer.OrdinalIgnoreCase);

        var hottest = SafetyTemperature(temperatures, config);
        if (_calibration is not null)
            RunCalibration(hottest, now);

        foreach (var binding in _bindings.Values)
        {
            var state = State(binding.Id);
            state.CurrentTemperature = config.Channels.TryGetValue(binding.Id, out var configured) &&
                                       TryTemperature(configured, temperatures, out var shown) ? shown : null;

            if (_calibration?.ChannelId.Equals(binding.Id, StringComparison.OrdinalIgnoreCase) == true)
                continue;

            if (_tests.TryGetValue(binding.Id, out var test))
            {
                var rpm = binding.RpmSensor?.Value;
                if (rpm is not null)
                    test.PeakRpm = Math.Max(test.PeakRpm ?? rpm.Value, rpm.Value);
                if (hottest >= 90)
                {
                    FinishTest(binding.Id, Tr.T("Prueba cancelada por temperatura alta."));
                    continue;
                }
                _testInfo = new(true, binding.Id, test.Duty,
                    Math.Max(0, (int)Math.Ceiling((test.Expires - now).TotalSeconds)),
                    test.BaselineRpm, rpm, test.PeakRpm,
                    Tr.T("Mira y escucha qué ventilador cambia de velocidad."));
                Apply(binding, state, test.Duty, Tr.F("Identificando a {0}%", test.Duty));
                continue;
            }

            if (!config.Enabled || configured is null || !configured.Enabled)
            {
                Release(binding, state, Tr.T("Automático de fábrica"));
                continue;
            }

            if (state.CurrentTemperature is not { } temperature)
            {
                Release(binding, state, Tr.T("Protección: sensor de temperatura no disponible"));
                continue;
            }

            if (temperature >= configured.CriticalTemperature)
            {
                var emergencyDuty = binding.Control.MaxSoftwareValue;
                state.HeldTarget = emergencyDuty;
                state.LastTemperature = temperature;
                // Un límite crítico es una acción de seguridad, no una curva de
                // confort: nunca se retrasa con el tiempo de respuesta.
                Apply(binding, state, emergencyDuty, Tr.F("Emergencia térmica · {0:F1} °C", temperature));
                continue;
            }

            if (configured.Mode == FanModes.Manual)
            {
                if (HandToDriverIfBelowMinimum(binding, state, configured, configured.ManualDuty)) continue;
                ApplySmoothed(binding, state, configured, configured.ManualDuty, temperature, now, Tr.T("Manual"));
                continue;
            }

            // Si se ha editado la curva (o el modo, mínimo…), se recalcula ya: la histéresis
            // solo debe evitar cambios por variaciones pequeñas de temperatura, no por ediciones.
            var key = SettingsKeyOf(configured);
            if (key != state.SettingsKey)
            {
                state.SettingsKey = key;
                state.HeldTarget = null;
                state.LastTemperature = null;
            }

            var target = CurveMath.Interpolate(configured.Curve, temperature);
            if (state.LastTemperature is null || Math.Abs(temperature - state.LastTemperature.Value) >= configured.Hysteresis)
            {
                state.HeldTarget = target;
                state.LastTemperature = temperature;
            }
            else
            {
                target = state.HeldTarget ?? target;
            }

            if (HandToDriverIfBelowMinimum(binding, state, configured, target)) continue;
            ApplySmoothed(binding, state, configured, target, temperature, now, Tr.T("Curva activa"));
        }

        BuildSnapshot(config, false, _bindings.Count == 0
            ? Tr.T("El hardware no ha expuesto canales de control. Revisa PawnIO y el modo PWM/DC de la BIOS.")
            : config.Enabled ? Tr.T("Control de ventiladores activo.") : Tr.T("Control desactivado: todos los ventiladores siguen a la BIOS o a la gráfica."));
    }

    public CoolingActionResult Test(string channelId, int duty, int seconds)
    {
        if (_conflict is not null)
            return new(false, Tr.F("Cierra {0} antes de probar un canal.", _conflict));
        if (!_bindings.TryGetValue(channelId, out var binding))
            return new(false, Tr.T("El canal ya no está disponible."));
        if (_calibration is not null)
            return new(false, Tr.T("Espera a que termine la calibración actual."));

        duty = Math.Clamp(duty, 35, 100);
        seconds = Math.Clamp(seconds, 2, 10);
        foreach (var otherId in _tests.Keys.Where(id => !id.Equals(channelId, StringComparison.OrdinalIgnoreCase)).ToArray())
            FinishTest(otherId, Tr.T("Prueba anterior detenida."));
        var baselineRpm = binding.RpmSensor?.Value;
        _tests[channelId] = new(duty, DateTime.UtcNow.AddSeconds(seconds), baselineRpm);
        _testInfo = new(true, channelId, duty, seconds, baselineRpm, baselineRpm, baselineRpm,
            Tr.T("Mira y escucha qué ventilador cambia de velocidad."));
        Apply(binding, State(channelId), duty, Tr.F("Identificando a {0}%", duty));
        Log.Info($"Refrigeración: identificando {channelId} a {duty}% durante {seconds} s");
        return new(true, Tr.F("Canal a {0}% durante {1} segundos; después vuelve a su control normal.", duty, seconds));
    }

    public CoolingActionResult StopTest(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || !_tests.ContainsKey(channelId))
            return new(true, Tr.T("No hay una prueba activa en ese canal."));
        FinishTest(channelId, Tr.T("Prueba detenida; control automático restaurado."));
        return new(true, Tr.T("Prueba detenida; el canal ha vuelto a su control normal."));
    }

    public CoolingActionResult StartCalibration(string channelId)
    {
        if (_conflict is not null)
            return new(false, Tr.F("Cierra {0} antes de calibrar.", _conflict));
        if (!_bindings.TryGetValue(channelId, out var binding))
            return new(false, Tr.T("El canal ya no está disponible."));
        if (binding.RpmSensor is null)
            return new(false, Tr.T("Este canal no ofrece lectura de RPM; ajusta su mínimo a mano."));
        if (_calibration is not null)
            return new(false, Tr.T("Ya hay una calibración en curso."));

        foreach (var testId in _tests.Keys.ToArray())
            FinishTest(testId, Tr.T("Prueba detenida para iniciar la calibración."));
        _calibration = new(channelId);
        _calibrationInfo = new(true, channelId, 100, null, null,
            Tr.T("Calibrando: el ventilador puede llegar a pararse un momento. No cierres SpotLay."));
        Apply(binding, State(channelId), 100, Tr.T("Calibrando 100%"));
        Log.Info($"Refrigeración: calibrando {channelId}");
        return new(true, Tr.T("Calibración iniciada; tarda menos de un minuto."));
    }

    public CoolingActionResult ReleaseEverything(string? reason = null)
    {
        reason ??= Tr.T("Automático de fábrica restaurado");
        _tests.Clear();
        _testInfo = CoolingTestInfo.Empty;
        AbortCalibration(Tr.T("Calibración cancelada."));
        ReleaseAll(reason);
        return new(true, Tr.T("Todos los canales han vuelto al control de la BIOS o de la gráfica."));
    }

    private void Discover(IReadOnlyList<IHardware> hardware)
    {
        var discovered = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in hardware)
        {
            var sensors = device.Sensors;
            foreach (var sensor in sensors.Where(sensor => sensor.SensorType == SensorType.Control && sensor.Control is not null))
            {
                var id = sensor.Identifier.ToString();
                var rpm = sensors.FirstOrDefault(candidate => candidate.SensorType == SensorType.Fan && candidate.Index == sensor.Index)
                          ?? sensors.FirstOrDefault(candidate => candidate.SensorType == SensorType.Fan &&
                              NumericSuffix(candidate.Name) == NumericSuffix(sensor.Name));
                var type = device.HardwareType.ToString();
                var category = type.Contains("Gpu", StringComparison.OrdinalIgnoreCase) ? "GPU"
                    : type.Contains("Cpu", StringComparison.OrdinalIgnoreCase) ? "CPU"
                    : Tr.T("Placa / caja");
                discovered[id] = new(id, device.Name, sensor.Name, category, sensor.Control!, sensor, rpm);
            }
        }

        foreach (var old in _bindings.Where(item => !discovered.ContainsKey(item.Key)).ToArray())
        {
            if (_states.TryGetValue(old.Key, out var state)) Release(old.Value, state, Tr.T("Canal desconectado"));
            _bindings.Remove(old.Key);
        }
        foreach (var item in discovered) _bindings[item.Key] = item.Value;
    }

    /// <summary>
    /// La temperatura más alta de CPU, GPU, hot spot, VRAM y las fuentes de las
    /// curvas. No todos los sensores: algunos dan un "límite" fijo (p. ej. 89 °C)
    /// que cancelaba la calibración con el equipo en reposo.
    /// </summary>
    private static double SafetyTemperature(IReadOnlyDictionary<string, double> temperatures, CoolingConfig config)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            KnownMetrics.CpuTemp, KnownMetrics.GpuTemp, KnownMetrics.GpuHotSpot, KnownMetrics.GpuMemoryTemp
        };
        foreach (var ch in config.Channels.Values)
        {
            ids.Add(ch.TemperatureSourceId);
            if (!string.IsNullOrWhiteSpace(ch.SecondaryTemperatureSourceId)) ids.Add(ch.SecondaryTemperatureSourceId);
        }
        return ids.Select(id => temperatures.TryGetValue(id, out var t) ? t : 0).DefaultIfEmpty(0).Max();
    }

    private void RunCalibration(double hottest, DateTime now)
    {
        var run = _calibration;
        if (run is null || !_bindings.TryGetValue(run.ChannelId, out var binding))
        {
            AbortCalibration(Tr.T("Calibración cancelada: canal desconectado."));
            return;
        }

        if (hottest >= 85)
        {
            Apply(binding, State(binding.Id), 100, Tr.T("Protección térmica durante calibración"));
            AbortCalibration(Tr.F("Calibración cancelada por temperatura alta ({0:F0} °C).", hottest));
            return;
        }

        var duty = run.Steps[run.StepIndex];
        Apply(binding, State(binding.Id), duty, Tr.F("Calibrando {0}%", duty));
        _calibrationInfo = _calibrationInfo with { CurrentDuty = duty, Message = Tr.F("Midiendo respuesta a {0}%…", duty) };
        var rpm = Math.Max(0, (int)Math.Round(binding.RpmSensor?.Value ?? 0));
        var elapsed = now - run.StepStarted;
        if (run.StepIndex == 0)
        {
            // Máximo: al menos 7 s y hasta que lleve 3 s sin subir (tope 20 s).
            if (rpm > run.Peak * 1.01)
            {
                run.Peak = rpm;
                run.LastRise = now;
            }
            if (elapsed < TimeSpan.FromSeconds(7) || (now - run.LastRise < TimeSpan.FromSeconds(3) && elapsed < TimeSpan.FromSeconds(20)))
                return;
            rpm = Math.Max(rpm, run.Peak);
        }
        // Bajar de velocidad tarda más que subir: 4 s por paso.
        else if (elapsed < TimeSpan.FromSeconds(4)) return;

        run.Samples.Add((duty, rpm));
        run.StepIndex++;
        run.StepStarted = now;
        if (run.StepIndex < run.Steps.Length) return;

        // Mínimo 0 %: el ventilador o sigue girando a 0 % o se para (y puede pararse sin problema).
        const int minimum = 0;
        var maximumRpm = run.Samples.Select(sample => sample.Rpm).DefaultIfEmpty(0).Max();
        var atZero = run.Samples.Last().Rpm;
        var spinning = run.Samples.Where(sample => sample.Rpm >= 100).ToArray();
        var cardMinimum = (int)Math.Round(binding.Control.MinSoftwareValue);
        var detail = binding.Category == "GPU" && cardMinimum > 0
            ? Tr.F("la tarjeta no baja del {0} %; por debajo lo controla el driver (0 RPM en reposo)", cardMinimum)
            : atZero >= 100
            ? Tr.F("a 0 % sigue girando a {0} RPM", atZero)
            : spinning.Length > 0 ? Tr.F("se para por debajo del {0} %", spinning.Min(sample => sample.Duty))
            : Tr.T("no se ha visto girar");
        Release(binding, State(binding.Id), Tr.T("Calibración terminada; automático restaurado"));
        _calibrationInfo = new(false, binding.Id, 0, minimum, maximumRpm,
            Tr.F("Calibración terminada: mínimo 0 % ({0}) · máximo {1} RPM.", detail, maximumRpm));
        Log.Info($"Refrigeración: calibración de {binding.Id} = " +
                 string.Join(", ", run.Samples.Select(s => $"{s.Duty}%:{s.Rpm}rpm")) + $" -> mínimo {minimum}%");
        _calibration = null;
    }

    private void AbortCalibration(string message)
    {
        if (_calibration is null) return;
        if (_bindings.TryGetValue(_calibration.ChannelId, out var binding))
            Release(binding, State(binding.Id), message);
        var channel = _calibration.ChannelId;
        _calibration = null;
        _calibrationInfo = new(false, channel, 0, null, null, message);
        Log.Info("Refrigeración: " + message);
    }

    private void FinishTest(string channelId, string message)
    {
        if (!_tests.Remove(channelId, out var test)) return;
        float? currentRpm = null;
        if (_bindings.TryGetValue(channelId, out var binding))
        {
            currentRpm = binding.RpmSensor?.Value;
            Release(binding, State(channelId), message);
        }
        _testInfo = new(false, channelId, test.Duty, 0, test.BaselineRpm, currentRpm, test.PeakRpm, message);
        Log.Info($"Refrigeración: fin de identificación {channelId} ({message}) RPM antes={test.BaselineRpm:F0} pico={test.PeakRpm:F0} ahora={currentRpm:F0}");
    }

    private static bool TryTemperature(FanChannelConfig channel,
        IReadOnlyDictionary<string, double> temperatures, out double temperature)
    {
        var hasPrimary = temperatures.TryGetValue(channel.TemperatureSourceId, out var primary);
        var secondary = 0d;
        var hasSecondary = !string.IsNullOrWhiteSpace(channel.SecondaryTemperatureSourceId) &&
                           temperatures.TryGetValue(channel.SecondaryTemperatureSourceId, out secondary);
        if (!hasPrimary && !hasSecondary) { temperature = 0; return false; }
        if (!hasPrimary) { temperature = secondary; return true; }
        if (!hasSecondary) { temperature = primary; return true; }
        temperature = channel.MixMode switch
        {
            "min" => Math.Min(primary, secondary),
            "average" => (primary + secondary) / 2,
            _ => Math.Max(primary, secondary)
        };
        return true;
    }

    private void ApplySmoothed(Binding binding, RuntimeState state, FanChannelConfig channel,
        double requested, double? temperature, DateTime now, string mode)
    {
        var hardwareMin = Math.Clamp(binding.Control.MinSoftwareValue, 0, 100);
        var hardwareMax = Math.Clamp(binding.Control.MaxSoftwareValue, hardwareMin, 100);
        var minimum = Math.Max(hardwareMin, channel.MinimumDuty);
        var maximum = Math.Min(hardwareMax, channel.MaximumDuty);
        var target = (float)Math.Clamp(requested, minimum, Math.Max(minimum, maximum));

        if (state.LastDuty is float previous)
        {
            var elapsed = Math.Max(0, (now - state.LastWrite).TotalSeconds);
            var seconds = target >= previous ? channel.ResponseUpSeconds : channel.ResponseDownSeconds;
            if (seconds > 0)
            {
                var delta = (float)(100d * elapsed / seconds);
                target = target >= previous ? Math.Min(target, previous + delta) : Math.Max(target, previous - delta);
            }
        }

        var suffix = temperature is null ? "" : $" · {temperature:F1} °C";
        Apply(binding, state, target, $"{mode}{suffix}");
    }

    private static void Apply(Binding binding, RuntimeState state, float duty, string status)
    {
        duty = Math.Clamp(duty, binding.Control.MinSoftwareValue, binding.Control.MaxSoftwareValue);
        try
        {
            if (!state.Applied || state.LastDuty is null || Math.Abs(state.LastDuty.Value - duty) >= .2f)
                binding.Control.SetSoftware(duty);
            state.Applied = true;
            state.LastDuty = duty;
            state.LastWrite = DateTime.UtcNow;
            state.Status = status;
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo escribir el canal {binding.Id}", ex);
            try { binding.Control.SetDefault(); } catch { }
            state.Applied = false;
            state.LastDuty = null;
            state.Status = Tr.T("Error de escritura; automático restaurado");
        }
    }

    /// <summary>
    /// En manual, la gráfica no deja bajar de su mínimo (p. ej. 30 %): el 0 RPM en reposo solo lo
    /// hace su driver en automático. Si la curva pide menos que ese mínimo, se le devuelve el
    /// control al driver; se retoma cuando pide el mínimo + 5 % (para no entrar y salir sin parar).
    /// </summary>
    private static string SettingsKeyOf(FanChannelConfig c) =>
        string.Join(";", c.Curve.Select(p => $"{p.Temperature}:{p.Duty}")) + $"|{c.MinimumDuty}|{c.MaximumDuty}|{c.TemperatureSourceId}|{c.SecondaryTemperatureSourceId}|{c.MixMode}";

    private static bool HandToDriverIfBelowMinimum(Binding binding, RuntimeState state, FanChannelConfig channel, double requested)
    {
        var hardwareMin = binding.Control.MinSoftwareValue;
        if (binding.Category != "GPU" || hardwareMin <= 0 || channel.MinimumDuty >= hardwareMin)
        {
            state.DriverAuto = false;
            return false;
        }
        var threshold = state.DriverAuto ? hardwareMin + 5 : hardwareMin;
        if (requested >= threshold)
        {
            state.DriverAuto = false;
            return false;
        }
        if (!state.DriverAuto) Log.Info($"Refrigeración: {binding.Id} vuelve al driver (curva {requested:F0} % < mínimo de la tarjeta {hardwareMin:F0} %)");
        Release(binding, state, Tr.T("Automático del driver (0 RPM en reposo)"));
        state.DriverAuto = true;
        return true;
    }

    private static void Release(Binding binding, RuntimeState state, string status)
    {
        if (state.Applied)
        {
            try { binding.Control.SetDefault(); }
            catch (Exception ex) { Log.Error($"No se pudo devolver {binding.Id} a automático", ex); }
        }
        state.Applied = false;
        state.LastDuty = null;
        state.LastTemperature = null;
        state.HeldTarget = null;
        state.Status = status;
    }

    private void ReleaseAll(string status)
    {
        foreach (var binding in _bindings.Values) Release(binding, State(binding.Id), status);
    }

    private RuntimeState State(string id)
    {
        if (!_states.TryGetValue(id, out var state)) _states[id] = state = new();
        return state;
    }

    private void BuildSnapshot(CoolingConfig config, bool conflict, string message)
    {
        var channels = _bindings.Values
            .OrderBy(binding => binding.Category)
            .ThenBy(binding => binding.Hardware)
            .ThenBy(binding => binding.Name)
            .Select(binding =>
            {
                var state = State(binding.Id);
                return new CoolingChannelInfo(
                    binding.Id,
                    binding.Hardware,
                    config.Channels.TryGetValue(binding.Id, out var channel) && !string.IsNullOrWhiteSpace(channel.Label)
                        ? channel.Label : binding.Name,
                    binding.Name,
                    binding.Category,
                    binding.Control.MinSoftwareValue,
                    binding.Control.MaxSoftwareValue,
                    binding.DutySensor.Value,
                    binding.RpmSensor?.Value,
                    binding.RpmSensor is not null,
                    state.Applied,
                    state.CurrentTemperature,
                    state.Status);
            }).ToArray();
        Snapshot = new(config.Enabled, conflict, message, channels, _calibrationInfo, _testInfo);
    }

    private void RefreshConflict()
    {
        if (DateTime.UtcNow < _nextConflictCheck) return;
        _nextConflictCheck = DateTime.UtcNow.AddSeconds(3);
        var previous = _conflict;
        _conflict = FindConflict();
        if (_conflict != previous)
            Log.Warn(_conflict is null ? "Refrigeración: ya no hay otro controlador de ventiladores abierto"
                                       : $"Refrigeración: {_conflict} está abierto; SpotLay deja de escribir ventiladores");
    }

    private static string? FindConflict()
    {
        try
        {
            if (Process.GetProcessesByName("FanControl").Length > 0) return "Fan Control";
            if (Process.GetProcessesByName("MSIAfterburner").Length > 0) return "MSI Afterburner";
            if (Process.GetProcessesByName("PrecisionX_x64").Length > 0) return "EVGA Precision";
            // SpotLay 10 se llama igual; se distingue por la versión del ejecutable.
            foreach (var process in Process.GetProcessesByName("SpotLay"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    var major = 0;
                    try { major = process.MainModule?.FileVersionInfo.FileMajorPart ?? 0; }
                    catch { }
                    if (major < 11) return "SpotLay 10";
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static int NumericSuffix(string name)
    {
        var digits = new string(name.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var value) ? value : -1;
    }
}

public static class CurveMath
{
    public static float Interpolate(IReadOnlyList<FanPoint> points, double temperature)
    {
        if (points.Count == 0) return 50;
        var sorted = points.OrderBy(point => point.Temperature).ToArray();
        if (temperature <= sorted[0].Temperature) return sorted[0].Duty;
        if (temperature >= sorted[^1].Temperature) return sorted[^1].Duty;
        for (var index = 1; index < sorted.Length; index++)
        {
            if (temperature > sorted[index].Temperature) continue;
            var left = sorted[index - 1];
            var right = sorted[index];
            var span = Math.Max(1, right.Temperature - left.Temperature);
            var ratio = (temperature - left.Temperature) / span;
            return (float)Math.Clamp(left.Duty + ratio * (right.Duty - left.Duty), 0, 100);
        }
        return 50;
    }
}
