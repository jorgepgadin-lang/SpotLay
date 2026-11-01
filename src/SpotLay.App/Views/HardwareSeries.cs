using System.Windows.Media;
using SpotLay.Core.Localization;
using SpotLay.Core.Sessions;

namespace SpotLay.App.Views;

/// <summary>Una curva que se puede marcar en la gráfica de hardware de Rendimiento.</summary>
public sealed record HardwareSeries(string Id, string Label, string Unit, Color Color, bool Dashed, Func<SessionSample, float?> Value)
{
    /// <summary>Tonos azules; lo de la CPU va discontinuo para distinguirlo de la GPU.</summary>
    public static IReadOnlyList<HardwareSeries> All { get; } =
    [
        new("gpu-temp", Tr.T("GPU °C"), "°C", Color.FromRgb(0x38, 0xBD, 0xF8), false, s => Positive(s.GpuTemp)),
        new("gpu-hotspot", Tr.T("Hot spot °C"), "°C", Color.FromRgb(0x3B, 0x82, 0xF6), false, s => Positive(s.HotSpot)),
        new("gpu-memory-temp", Tr.T("VRAM °C"), "°C", Color.FromRgb(0x81, 0x8C, 0xF8), false, s => s.VramTemp),
        new("cpu-temp", Tr.T("CPU °C"), "°C", Color.FromRgb(0x22, 0xD3, 0xEE), true, s => Positive(s.CpuTemp)),
        new("gpu-power", Tr.T("GPU W"), "W", Color.FromRgb(0x60, 0xA5, 0xFA), false, s => Positive(s.GpuPower)),
        new("cpu-power", Tr.T("CPU W"), "W", Color.FromRgb(0x67, 0xE8, 0xF9), true, s => s.CpuPower),
        new("gpu-clock", Tr.T("GPU MHz"), "MHz", Color.FromRgb(0x2D, 0xD4, 0xBF), false, s => Positive(s.GpuClock)),
        new("gpu-load", Tr.T("Uso GPU %"), "%", Color.FromRgb(0x93, 0xC5, 0xFD), false, s => s.GpuLoad),
        new("cpu-load", Tr.T("Uso CPU %"), "%", Color.FromRgb(0xA5, 0xF3, 0xFC), true, s => s.CpuLoad),
        new("vram", Tr.T("VRAM GB"), "GB", Color.FromRgb(0xA7, 0x8B, 0xFA), false, s => s.Vram),
        new("ram", Tr.T("RAM GB"), "GB", Color.FromRgb(0xC4, 0xB5, 0xFD), true, s => s.Ram),
    ];

    // Las partidas antiguas guardaban 0 cuando no había lectura: 0 °C, 0 W o 0 MHz jugando no es un dato real.
    private static float? Positive(float v) => v > 0 ? v : null;

    public bool HasData(SessionRecord? s) => s is not null && s.Samples.Any(x => Value(x) is not null);

    /// <summary>Máximo "redondo" de la escala para esta curva en esta partida.</summary>
    public double ScaleMax(SessionRecord s)
    {
        if (Unit == "%") return 100;
        var max = s.Samples.Select(Value).Where(v => v is not null).Select(v => (double)v!.Value).DefaultIfEmpty(0).Max();
        return NiceMax(max * 1.1);
    }

    /// <summary>Máximo de la escala con 4 divisiones de números redondos (20, 40, 60, 80…).</summary>
    public static double NiceMax(double v)
    {
        if (v <= 0) return 1;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(v / 4)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (v <= 4 * m * mag) return 4 * m * mag;
        return 40 * mag;
    }

    public static string Format(double v, string unit) => unit switch
    {
        "GB" => Tr.F("{0:F1} GB", v),
        _ => Tr.F("{0:F0} {1}", v, unit)
    };
}
