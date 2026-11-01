using LibreHardwareMonitor.Hardware;
using SpotLay.Core.Config;
using SpotLay.Core.Cooling;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.Sensors;

public sealed record HardwareReading(string GpuName, string CpuName, IReadOnlyList<MetricValue> Metrics, CoolingSnapshot Cooling)
{
    public static HardwareReading Empty { get; } = new(Tr.T("GPU no detectada"), Tr.T("CPU no detectada"), [], CoolingSnapshot.Empty);
}

/// <summary>
/// Sensores de CPU, GPU, placa, memoria, discos y red via LibreHardwareMonitor,
/// y control de ventiladores (con el mismo cerrojo: nunca se escribe un canal
/// mientras LibreHardwareMonitor lo esta leyendo).
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsControllerEnabled = true
    };
    private readonly object _gate = new();
    private readonly CoolingController _cooling = new();
    private bool _opened;

    public void Open()
    {
        lock (_gate)
        {
            try
            {
                _computer.Open();
                _opened = true;
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo iniciar LibreHardwareMonitor", ex);
            }
        }
    }

    public HardwareReading Read(CoolingConfig cooling)
    {
        lock (_gate)
        {
            if (!_opened) return HardwareReading.Empty;
            foreach (var hardware in _computer.Hardware) Update(hardware);
            var devices = _computer.Hardware.SelectMany(Expand).ToArray();

            var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
                      ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel);
            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            var memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            var gpuSensors = gpu is null ? [] : Expand(gpu).SelectMany(h => h.Sensors).ToArray();
            var cpuSensors = cpu is null ? [] : Expand(cpu).SelectMany(h => h.Sensors).ToArray();
            var gpuName = gpu?.Name ?? "GPU";
            var cpuName = cpu?.Name ?? "CPU";

            var values = new List<MetricValue>();
            // Cada métrica sale solo de su sensor (los nombres alternativos son el mismo dato en
            // Intel/AMD/NVIDIA: p. ej. "Core (Tctl/Tdie)" es la temperatura de CPU en AMD).
            // Si no existe, no se añade y el overlay muestra "—".
            AddKnown(values, KnownMetrics.GpuLoad, Find(gpuSensors, SensorType.Load, "GPU Core", "GPU"), gpuName);
            AddKnown(values, KnownMetrics.GpuTemp, Find(gpuSensors, SensorType.Temperature, "GPU Core", "GPU"), gpuName);
            AddKnown(values, KnownMetrics.GpuHotSpot, HotSpot(gpuSensors), gpuName);
            AddKnown(values, KnownMetrics.GpuMemoryTemp, Contains(gpuSensors, SensorType.Temperature, "Memory Junction", "GPU Memory"), gpuName);
            AddKnown(values, KnownMetrics.GpuPower, Find(gpuSensors, SensorType.Power, "GPU Package", "GPU"), gpuName);
            AddKnown(values, KnownMetrics.GpuClock, Find(gpuSensors, SensorType.Clock, "GPU Core", "Graphics"), gpuName);
            AddKnown(values, KnownMetrics.Vram, ToGigabytes(Contains(gpuSensors, SensorType.SmallData, "Memory Used")), gpuName);
            AddKnown(values, KnownMetrics.GpuFanRpm, Maximum(gpuSensors, SensorType.Fan), gpuName);
            AddKnown(values, KnownMetrics.GpuFan, Maximum(gpuSensors, SensorType.Control), gpuName);
            AddKnown(values, KnownMetrics.CpuLoad, Find(cpuSensors, SensorType.Load, "CPU Total", "Total"), cpuName);
            AddKnown(values, KnownMetrics.CpuTemp, Find(cpuSensors, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)"), cpuName);
            AddKnown(values, KnownMetrics.CpuPower, Find(cpuSensors, SensorType.Power, "CPU Package", "Package"), cpuName);
            AddKnown(values, KnownMetrics.Ram,
                memory is null ? null : Contains(memory.Sensors, SensorType.Data, "Memory Used", "Used Memory"),
                memory?.Name ?? Tr.T("Memoria"));

            // Todos los sensores, con el mismo id que SpotLay 10: "hw:" + identificador.
            foreach (var device in devices)
            foreach (var sensor in device.Sensors)
            {
                if (sensor.Value is not { } v || !float.IsFinite(v)) continue;
                values.Add(new MetricValue(
                    KnownMetrics.HardwarePrefix + sensor.Identifier,
                    sensor.Name,
                    v,
                    UnitOf(sensor.SensorType),
                    CategoryOf(device.HardwareType, sensor.SensorType),
                    device.Name,
                    SensorType: sensor.SensorType.ToString()));
            }

            try
            {
                _cooling.Update(devices, values, cooling);
            }
            catch (Exception ex)
            {
                Log.Error("Error en el control de ventiladores; se devuelven a automático", ex);
                _cooling.ReleaseEverything(Tr.T("Error interno; automático restaurado"));
            }

            return new HardwareReading(gpu?.Name ?? Tr.T("GPU no detectada"), cpu?.Name ?? Tr.T("CPU no detectada"), values, _cooling.Snapshot);
        }
    }

    public CoolingActionResult TestFan(string channelId, int duty, int seconds)
    {
        lock (_gate) return _cooling.Test(channelId, duty, seconds);
    }

    public CoolingActionResult StopFanTest(string channelId)
    {
        lock (_gate) return _cooling.StopTest(channelId);
    }

    public CoolingActionResult CalibrateFan(string channelId)
    {
        lock (_gate) return _cooling.StartCalibration(channelId);
    }

    public CoolingActionResult ReleaseFans()
    {
        lock (_gate) return _cooling.ReleaseEverything();
    }

    /// <summary>
    /// Ultimo recurso (excepcion fatal, cierre del proceso): devuelve los
    /// ventiladores a la BIOS sin quedarse bloqueado si el hilo de sensores lo esta.
    /// </summary>
    public void ReleaseFansFailSafe(string reason)
    {
        if (!Monitor.TryEnter(_gate, TimeSpan.FromSeconds(2))) return;
        try
        {
            _cooling.ReleaseEverything(reason);
        }
        catch
        {
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    // Solo el sensor "Hot Spot" de verdad. Si el driver no lo da, no hay dato ("—"):
    // nunca se pone otra temperatura con esta etiqueta.
    private static double? HotSpot(ISensor[] gpuSensors) => gpuSensors
        .Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
        .Select(s => (double?)s.Value)
        .Where(v => v is > 0 and < 150)
        .DefaultIfEmpty()
        .Max();

    private static void AddKnown(List<MetricValue> output, string id, double? value, string device)
    {
        if (value is not { } v || !double.IsFinite(v)) return;
        var definition = KnownMetrics.Find(id)!;
        output.Add(new MetricValue(id, definition.Label, v, definition.Unit, definition.Category, device));
    }

    private static string UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Clock => "MHz",
        SensorType.Fan => "RPM",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Frequency => "Hz",
        SensorType.Energy => "mWh",
        SensorType.Flow => "L/h",
        _ => ""
    };

    private static MetricCategory CategoryOf(HardwareType hardware, SensorType sensor)
    {
        if (sensor is SensorType.Fan or SensorType.Control && hardware is HardwareType.SuperIO or HardwareType.Motherboard or HardwareType.Cooler)
            return MetricCategory.Ventiladores;
        return hardware switch
        {
            HardwareType.Cpu => MetricCategory.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => MetricCategory.Gpu,
            HardwareType.Memory => MetricCategory.Memoria,
            HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => MetricCategory.Placa,
            HardwareType.Storage => MetricCategory.Almacenamiento,
            HardwareType.Network => MetricCategory.Red,
            HardwareType.Cooler => MetricCategory.Ventiladores,
            _ => MetricCategory.Otros
        };
    }

    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) Update(sub);
    }

    private static IEnumerable<IHardware> Expand(IHardware hardware)
    {
        yield return hardware;
        foreach (var sub in hardware.SubHardware)
        foreach (var child in Expand(sub))
            yield return child;
    }

    private static double? Find(IEnumerable<ISensor> source, SensorType type, params string[] names)
    {
        var sensors = source.Where(s => s.SensorType == type && s.Value is not null).ToArray();
        foreach (var name in names)
        {
            var found = sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found?.Value is { } v) return v;
        }
        // Sin el sensor con ese nombre no hay dato ("—"): nunca otro sensor cualquiera.
        return null;
    }

    private static double? Contains(IEnumerable<ISensor> source, SensorType type, params string[] names)
    {
        var sensors = source.Where(s => s.SensorType == type && s.Value is not null).ToArray();
        foreach (var name in names)
        {
            var found = sensors.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (found?.Value is { } v) return v;
        }
        return null;
    }

    private static double? Maximum(IEnumerable<ISensor> source, SensorType type)
    {
        var values = source.Where(s => s.SensorType == type && s.Value is not null).Select(s => (double)s.Value!.Value).ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static double? ToGigabytes(double? megabytes) => megabytes / 1024d;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_opened)
            {
                _cooling.ReleaseEverything(Tr.T("SpotLay cerrado"));
                _computer.Close();
            }
            _opened = false;
        }
    }
}
