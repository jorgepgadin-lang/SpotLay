using SpotLay.Core.Localization;

namespace SpotLay.Core.Metrics;

public sealed record MetricDefinition(
    string Id,
    string SpanishLabel,
    string SpanishDescription,
    string Unit,
    MetricCategory Category,
    double Min,
    double Max)
{
    public string Label => Tr.T(SpanishLabel);
    public string Description => Tr.T(SpanishDescription);
}

/// <summary>
/// Metricas con identificador estable. Los ids coinciden con los de SpotLay 10
/// para que la importacion de perfiles y estilos funcione tal cual; los sensores
/// sueltos de LibreHardwareMonitor usan "hw:" + su identificador, igual que antes.
/// </summary>
public static class KnownMetrics
{
    public const string Fps = "fps";
    public const string FpsReal = "fps-real";
    public const string FpsGenerated = "fps-gen";
    public const string FrameGen = "fg-mult";
    public const string FrameTime = "frametime";
    public const string Api = "api";
    public const string Low1 = "fps-low1";
    public const string Low01 = "fps-low01";
    public const string Low1Real = "fps-low1-real";
    public const string Low01Real = "fps-low01-real";
    public const string GpuLoad = "gpu-load";
    public const string GpuTemp = "gpu-temp";
    public const string GpuHotSpot = "gpu-hotspot";
    public const string GpuMemoryTemp = "gpu-memory-temp";
    public const string GpuPower = "gpu-power";
    public const string GpuClock = "gpu-clock";
    public const string Vram = "vram";
    public const string GpuFanRpm = "fan-rpm";
    public const string GpuFan = "fan";
    public const string CpuLoad = "cpu-load";
    public const string CpuTemp = "cpu-temp";
    public const string CpuPower = "cpu-power";
    public const string Ram = "ram";
    public const string HardwarePrefix = "hw:";

    public static IReadOnlyList<MetricDefinition> All { get; } =
    [
        new(Fps, "FPS", "Fotogramas que llegan a pantalla por segundo", "", MetricCategory.Juego, 0, 240),
        new(FpsReal, "FPS reales", "Fotogramas que renderiza el juego de verdad", "", MetricCategory.Juego, 0, 240),
        new(FpsGenerated, "FPS generados", "Fotogramas inventados por Frame Generation", "", MetricCategory.Juego, 0, 240),
        new(FrameGen, "FG", "Frame Generation: estado, multiplicador y modo", "", MetricCategory.Juego, 0, 6),
        new(Low1, "1% low", "FPS del 1% de frames más lentos que llegan a pantalla (últimos segundos)", "", MetricCategory.Juego, 0, 240),
        new(Low01, "0,1% low", "FPS del 0,1% de frames más lentos que llegan a pantalla", "", MetricCategory.Juego, 0, 240),
        new(Low1Real, "1% low reales", "1% low de los frames reales (sin los generados)", "", MetricCategory.Juego, 0, 240),
        new(Low01Real, "0,1% low reales", "0,1% low de los frames reales (sin los generados)", "", MetricCategory.Juego, 0, 240),
        new(FrameTime, "Frame time", "Milisegundos por fotograma real", "ms", MetricCategory.Juego, 0, 50),
        new(Api, "API", "API grafica del juego", "", MetricCategory.Juego, 0, 1),
        new(GpuLoad, "GPU", "Uso de la GPU", "%", MetricCategory.Gpu, 0, 100),
        new(GpuTemp, "GPU Temp", "Temperatura de la GPU", "°C", MetricCategory.Gpu, 0, 100),
        new(GpuHotSpot, "GPU Hot Spot", "Punto mas caliente de la GPU", "°C", MetricCategory.Gpu, 0, 110),
        new(GpuMemoryTemp, "VRAM Temp", "Temperatura de la memoria de video", "°C", MetricCategory.Gpu, 0, 110),
        new(GpuPower, "GPU Power", "Consumo de la GPU", "W", MetricCategory.Gpu, 0, 450),
        new(GpuClock, "GPU Clock", "Frecuencia de la GPU", "MHz", MetricCategory.Gpu, 0, 3200),
        new(Vram, "VRAM", "Memoria de video en uso", "GB", MetricCategory.Gpu, 0, 16),
        new(GpuFanRpm, "GPU Fan", "Ventilador de la GPU", "RPM", MetricCategory.Gpu, 0, 3500),
        new(GpuFan, "GPU Fan %", "Ventilador de la GPU", "%", MetricCategory.Gpu, 0, 100),
        new(CpuLoad, "CPU", "Uso de la CPU", "%", MetricCategory.Cpu, 0, 100),
        new(CpuTemp, "CPU Temp", "Temperatura de la CPU", "°C", MetricCategory.Cpu, 0, 100),
        new(CpuPower, "CPU Power", "Consumo de la CPU", "W", MetricCategory.Cpu, 0, 250),
        new(Ram, "RAM", "Memoria en uso", "GB", MetricCategory.Memoria, 0, 32),
    ];

    private static readonly Dictionary<string, MetricDefinition> ById =
        All.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

    public static MetricDefinition? Find(string id) => ById.GetValueOrDefault(id);

    public static bool IsGameMetric(string id) =>
        id is Fps or FpsReal or FpsGenerated or FrameGen or FrameTime or Api or Low1 or Low01 or Low1Real or Low01Real;
}

public sealed record MetricBlock(string Id, string SpanishTitle, string SpanishDescription, IReadOnlyList<string> Metrics)
{
    public string Title => Tr.T(SpanishTitle);
    public string Description => Tr.T(SpanishDescription);
}

/// <summary>Bloques del modo Esencial: se activan de golpe en vez de sensor a sensor.</summary>
public static class MetricBlocks
{
    public static IReadOnlyList<MetricBlock> All { get; } =
    [
        new("fg", "FPS y Frame Generation", "FPS en pantalla, reales, generados y el multiplicador",
            [KnownMetrics.FrameGen, KnownMetrics.Fps, KnownMetrics.FpsReal, KnownMetrics.FpsGenerated]),
        new("gpu", "Gráfica", "Uso, temperatura, hot spot, consumo y VRAM",
            [KnownMetrics.GpuLoad, KnownMetrics.GpuTemp, KnownMetrics.GpuHotSpot, KnownMetrics.GpuPower, KnownMetrics.Vram]),
        new("cpu", "Procesador", "Uso, temperatura y consumo",
            [KnownMetrics.CpuLoad, KnownMetrics.CpuTemp, KnownMetrics.CpuPower]),
        new("lows", "Estabilidad (1% low)", "1% y 0,1% low a pantalla y de los frames reales",
            [KnownMetrics.Low1, KnownMetrics.Low01, KnownMetrics.Low1Real, KnownMetrics.Low01Real]),
        new("mem", "Memoria", "RAM en uso", [KnownMetrics.Ram]),
        new("fans", "Ventilador de la gráfica", "RPM del ventilador de la GPU", [KnownMetrics.GpuFanRpm]),
    ];
}
