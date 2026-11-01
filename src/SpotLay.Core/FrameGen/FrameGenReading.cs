using SpotLay.Core.Localization;

namespace SpotLay.Core.FrameGen;

public enum FrameGenState
{
    /// <summary>El proceso no esta enviando frames.</summary>
    NoData,
    /// <summary>Hay frames pero no se puede saber cuantos son reales (sin Reflex, o API sin DXGI).</summary>
    NotMeasurable,
    Off,
    On
}

public enum FrameGenMode
{
    Measuring,
    Fixed,
    Dynamic
}

public enum GraphicsApi
{
    Unknown,
    DirectX,
    DirectX9,
    DirectX10,
    DirectX11,
    DirectX12,
    Vulkan,
    OpenGL,
    VulkanOrOpenGL
}

public sealed record FrameGenReading(
    FrameGenState State,
    int Multiplier,
    FrameGenMode Mode,
    double TotalFps,
    double RealFps,
    GraphicsApi Api,
    string Reason)
{
    public static FrameGenReading None { get; } =
        new(FrameGenState.NoData, 1, FrameGenMode.Measuring, 0, 0, GraphicsApi.Unknown, Tr.T("sin proceso"));

    public double GeneratedFps => State == FrameGenState.On ? Math.Max(0, TotalFps - RealFps) : 0;

    /// <summary>Frametimes y 1% low de la ventana de estadísticas.</summary>
    public FrameTimeStats Stats { get; init; } = FrameTimeStats.Empty;

    /// <summary>Frametimes (ms) de frames mostrados y reales llegados desde la lectura anterior.</summary>
    public double[] NewDisplayedMs { get; init; } = [];
    public double[] NewRealMs { get; init; } = [];

    public bool HasFrames => TotalFps > 0;

    /// <summary>Solo con DXGI (DirectX 10/11/12) se puede medir Frame Generation.</summary>
    public bool ApiSupportsFrameGen => Api is GraphicsApi.DirectX or GraphicsApi.DirectX10 or
        GraphicsApi.DirectX11 or GraphicsApi.DirectX12;

    public string ApiName => Api switch
    {
        GraphicsApi.DirectX => "DirectX",
        GraphicsApi.DirectX9 => "DirectX 9",
        GraphicsApi.DirectX10 => "DirectX 10",
        GraphicsApi.DirectX11 => "DirectX 11",
        GraphicsApi.DirectX12 => "DirectX 12",
        GraphicsApi.Vulkan => "Vulkan",
        GraphicsApi.OpenGL => "OpenGL",
        GraphicsApi.VulkanOrOpenGL => "Vulkan/OpenGL",
        _ => ""
    };

    /// <summary>Texto corto para el overlay: "x4 DINÁMICO", "OFF", "—".</summary>
    public string ShortText => State switch
    {
        FrameGenState.On => $"x{Multiplier} " + Mode switch
        {
            FrameGenMode.Fixed => Tr.T("FIJO"),
            FrameGenMode.Dynamic => Tr.T("DINÁMICO"),
            _ => "…"
        },
        FrameGenState.Off => "OFF",
        FrameGenState.NotMeasurable => Tr.T("no medible"),
        _ => "—"
    };
}

/// <summary>
/// Frametimes medidos en la ventana de estadísticas: a pantalla (PresentMon,
/// tiempo entre cambios de pantalla) y reales (entre marcadores Reflex).
/// 1% low / 0,1% low = percentil 99 / 99,9 del frametime pasado a FPS (como FrameView).
/// </summary>
public sealed record FrameTimeStats(double[] DisplayedMs, double[] RealMs, double WindowSeconds)
{
    public static FrameTimeStats Empty { get; } = new([], [], 10);

    public double? Low1Displayed => Low(DisplayedMs, 0.99);
    public double? Low01Displayed => Low(DisplayedMs, 0.999);
    public double? Low1Real => Low(RealMs, 0.99);
    public double? Low01Real => Low(RealMs, 0.999);

    private static double? Low(double[] frametimes, double percentile)
    {
        // Menos de 100 frames no dan un percentil con sentido.
        if (frametimes.Length < 100) return null;
        var sorted = (double[])frametimes.Clone();
        Array.Sort(sorted);
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index] > 0 ? 1000.0 / sorted[index] : null;
    }
}
