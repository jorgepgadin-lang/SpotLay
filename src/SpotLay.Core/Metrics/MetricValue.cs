using SpotLay.Core.Localization;

namespace SpotLay.Core.Metrics;

public enum MetricCategory
{
    Juego,
    Gpu,
    Cpu,
    Memoria,
    Placa,
    Almacenamiento,
    Red,
    Ventiladores,
    Otros
}

/// <summary>Una lectura. <see cref="Text"/> se usa para valores no numericos (modo FG, API).</summary>
public sealed record MetricValue(
    string Id,
    string Label,
    double? Value,
    string Unit,
    MetricCategory Category,
    string Device,
    string? Text = null,
    string? SensorType = null)
{
    public bool HasValue => Text is not null || (Value is { } v && double.IsFinite(v));
}

public sealed record MetricSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, MetricValue> Metrics,
    string GpuName,
    string CpuName)
{
    public static MetricSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        new Dictionary<string, MetricValue>(),
        Tr.T("Detectando GPU…"),
        Tr.T("Detectando CPU…"));

    public MetricValue? Get(string id) => Metrics.TryGetValue(id, out var m) ? m : null;
}
