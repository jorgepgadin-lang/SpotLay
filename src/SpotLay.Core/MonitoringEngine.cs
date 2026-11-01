using System.Diagnostics;
using System.Globalization;
using SpotLay.Core.Config;
using SpotLay.Core.Cooling;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.FrameGen;
using SpotLay.Core.Games;
using SpotLay.Core.Metrics;
using SpotLay.Core.Sensors;
using SpotLay.Core.Sessions;
using SpotLay.Core.Localization;

namespace SpotLay.Core;

public sealed record GameState(uint ProcessId, string ExeName, bool Fullscreen, bool IsGame, IntPtr Monitor, bool Exclusive = false)
{
    public static GameState None { get; } = new(0, "", false, false, IntPtr.Zero);
}

public sealed record EngineSnapshot(MetricSnapshot Metrics, FrameGenReading FrameGen, GameState Game, CoolingSnapshot Cooling)
{
    public static EngineSnapshot Empty { get; } = new(MetricSnapshot.Empty, FrameGenReading.None, GameState.None, CoolingSnapshot.Empty);
}

/// <summary>
/// Bucle de medida: Frame Generation del proceso en primer plano cada 250 ms y
/// sensores de hardware cada segundo (LibreHardwareMonitor tarda en leer y no
/// debe frenar la medida de FPS). Publica una instantanea combinada.
/// </summary>
public sealed class MonitoringEngine : IDisposable
{
    private volatile int _frameIntervalMs = 250;
    private static readonly TimeSpan HardwareInterval = TimeSpan.FromSeconds(1);

    private readonly FrameGenMonitor _frameGen = new();
    private readonly HardwareMonitor _hardware = new();
    private readonly HashSet<uint> _ignored = [(uint)Environment.ProcessId];
    private readonly CancellationTokenSource _stop = new();
    private volatile HardwareReading _lastHardware = HardwareReading.Empty;
    private volatile CoolingConfig _cooling = new();
    private Thread? _frameThread;
    private Thread? _hardwareThread;
    private DateTime _lastLog;
    private DateTime _lastCounters;

    public event Action<EngineSnapshot>? Updated;

    /// <summary>Graba cada partida para la pestaña Rendimiento.</summary>
    public SessionRecorder Sessions { get; } = new();

    public EngineSnapshot Current { get; private set; } = EngineSnapshot.Empty;

    /// <summary>Para pruebas: medir este proceso en vez del de primer plano.</summary>
    public uint TargetProcessOverride { get; set; }

    public string CaptureStatus { get; private set; } = Tr.T("Iniciando…");

    public bool CaptureAvailable => _frameGen.IsRunning;

    /// <summary>Cada cuánto se mide (ms): 250 normalmente; más rápido si la gráfica lo pide.</summary>
    public int FrameIntervalMs { get => _frameIntervalMs; set => _frameIntervalMs = Math.Clamp(value, 100, 250); }

    /// <summary>Segundos del 1% low y de la gráfica de frametime.</summary>
    public double FrameStatsSeconds { get => _frameGen.StatsSeconds; set => _frameGen.StatsSeconds = Math.Clamp(value, 5, 60); }

    /// <summary>
    /// --sin-ventiladores: se leen los canales pero nunca se escribe en ellos
    /// (para probar la interfaz sin tocar el hardware).
    /// </summary>
    public bool FanWritesBlocked { get; set; }

    /// <summary>Copia de la configuracion de ventiladores (la interfaz nunca comparte la suya con este hilo).</summary>
    public void SetCoolingConfig(CoolingConfig config)
    {
        var copy = ConfigJson.Clone(config);
        if (FanWritesBlocked) copy.Enabled = false;
        _cooling = copy;
    }

    public Task<CoolingActionResult> TestFanAsync(string channelId, int duty, int seconds) =>
        FanWritesBlocked ? Blocked() : Task.Run(() => _hardware.TestFan(channelId, duty, seconds));

    public Task<CoolingActionResult> StopFanTestAsync(string channelId) => Task.Run(() => _hardware.StopFanTest(channelId));

    public Task<CoolingActionResult> CalibrateFanAsync(string channelId) =>
        FanWritesBlocked ? Blocked() : Task.Run(() => _hardware.CalibrateFan(channelId));

    private static Task<CoolingActionResult> Blocked() =>
        Task.FromResult(new CoolingActionResult(false, Tr.T("SpotLay se ha abierto con --sin-ventiladores: no escribe en ningún canal.")));

    public Task<CoolingActionResult> ReleaseFansAsync() => Task.Run(_hardware.ReleaseFans);

    public void ReleaseFansFailSafe(string reason) => _hardware.ReleaseFansFailSafe(reason);

    public void Start()
    {
        var status = _frameGen.Start();
        CaptureStatus = status == 0
            ? Tr.T("Captura de frames activa")
            : FrameGenMonitor.IsAccessDenied(status)
                ? Tr.T("Sin permisos: abre SpotLay como administrador")
                : Tr.F("No se pudo iniciar la captura de frames (código {0})", status);
        Log.Info("Sesion ETW: " + CaptureStatus);

        _hardwareThread = new Thread(HardwareLoop) { IsBackground = true, Name = "SpotLay sensores" };
        _frameThread = new Thread(FrameLoop) { IsBackground = true, Name = "SpotLay frames" };
        _hardwareThread.Start();
        _frameThread.Start();
    }

    private void HardwareLoop()
    {
        _hardware.Open();
        while (!_stop.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                _lastHardware = _hardware.Read(_cooling);
            }
            catch (Exception ex)
            {
                Log.Error("Error leyendo sensores", ex);
            }
            Wait(HardwareInterval - Stopwatch.GetElapsedTime(started));
        }
    }

    private void FrameLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error("Error midiendo frames", ex);
            }
            Wait(TimeSpan.FromMilliseconds(_frameIntervalMs) - Stopwatch.GetElapsedTime(started));
        }
    }

    private void Tick()
    {
        var foreground = TargetProcessOverride != 0
            ? new ForegroundInfo(TargetProcessOverride, ForegroundWindow.ExeNameOf(TargetProcessOverride), IntPtr.Zero, true, IntPtr.Zero)
            : ForegroundWindow.Read(_ignored);

        var reading = _frameGen.Read(foreground.ProcessId, foreground.CoversMonitor);
        // Juego = envia frames y ademas ocupa la pantalla o lleva Reflex. Evita
        // que el navegador o el escritorio cuenten como juego.
        // o una app a pantalla completa con Vulkan/OpenGL/DirectX 9 aunque aún no haya FPS.
        var isGame = reading.HasFrames && (foreground.CoversMonitor || reading.RealFps > 0) ||
                     foreground.CoversMonitor && reading.Api is GraphicsApi.Vulkan or GraphicsApi.OpenGL or GraphicsApi.DirectX9;
        var game = new GameState(foreground.ProcessId, foreground.ExeName, foreground.CoversMonitor, isGame, foreground.Monitor, foreground.Exclusive);

        var hardware = _lastHardware;
        var metrics = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in hardware.Metrics) metrics[m.Id] = m;
        AddFrameMetrics(metrics, reading, foreground.ExeName);

        var snapshot = new EngineSnapshot(
            new MetricSnapshot(DateTimeOffset.Now, metrics, hardware.GpuName, hardware.CpuName), reading, game, hardware.Cooling);
        Current = snapshot;
        try
        {
            Sessions.Update(game, reading, snapshot.Metrics, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Log.Error("Error grabando la partida", ex);
        }
        Updated?.Invoke(snapshot);
        LogReading(game, reading);
    }

    private static void AddFrameMetrics(Dictionary<string, MetricValue> metrics, FrameGenReading r, string exe)
    {
        if (!r.HasFrames) return;

        void Add(string id, double? value, string? text = null)
        {
            var d = KnownMetrics.Find(id)!;
            metrics[id] = new MetricValue(id, d.Label, value, d.Unit, MetricCategory.Juego, exe, text);
        }

        Add(KnownMetrics.Fps, r.TotalFps);
        // 1% / 0,1% low: solo si hay frames suficientes medidos (si no, la tarjeta se oculta).
        if (r.Stats.Low1Displayed is { } l1) Add(KnownMetrics.Low1, l1);
        if (r.Stats.Low01Displayed is { } l01) Add(KnownMetrics.Low01, l01);
        if (r.Stats.Low1Real is { } r1) Add(KnownMetrics.Low1Real, r1);
        if (r.Stats.Low01Real is { } r01) Add(KnownMetrics.Low01Real, r01);
        if (!string.IsNullOrEmpty(r.ApiName)) Add(KnownMetrics.Api, null, r.ApiName);

        // Sin DXGI (Vulkan, OpenGL, DirectX 9) solo se sabe el FPS final: nada más.
        if (!r.ApiSupportsFrameGen) return;

        switch (r.State)
        {
            case FrameGenState.On:
                Add(KnownMetrics.FrameGen, r.Multiplier, r.ShortText);
                Add(KnownMetrics.FpsReal, r.RealFps);
                Add(KnownMetrics.FpsGenerated, r.GeneratedFps);
                Add(KnownMetrics.FrameTime, 1000.0 / r.RealFps);
                break;
            case FrameGenState.Off:
                Add(KnownMetrics.FrameGen, 1, r.ShortText);
                Add(KnownMetrics.FpsReal, r.RealFps);
                Add(KnownMetrics.FpsGenerated, 0);
                Add(KnownMetrics.FrameTime, 1000.0 / r.RealFps);
                break;
            case FrameGenState.NotMeasurable:
                // Sin marcadores Reflex no se puede medir FG: solo FPS, nada que confunda.
                break;
        }
    }

    private void LogReading(GameState game, FrameGenReading r)
    {
        if (DateTime.UtcNow - _lastCounters >= TimeSpan.FromSeconds(10))
        {
            _lastCounters = DateTime.UtcNow;
            var c = _frameGen.Counters;
            Log.Info($"ETW eventos={c.Events} presentsDXGI={c.DxgiPresents} marcadoresReflex={c.PclMarkers} errores={c.Errors} primerPlano={game.ExeName}({game.ProcessId})");
        }
        if (game.ProcessId == 0 || !r.HasFrames || DateTime.UtcNow - _lastLog < TimeSpan.FromSeconds(1)) return;
        _lastLog = DateTime.UtcNow;
        var inv = CultureInfo.InvariantCulture;
        Log.Info(string.Create(inv,
            $"FG exe={game.ExeName} pid={game.ProcessId} juego={game.IsGame} pantallaCompleta={game.Fullscreen} exclusiva={game.Exclusive} api={r.ApiName} total={r.TotalFps:F1} reales={r.RealFps:F1} -> {r.State} x{r.Multiplier} {r.Mode} ({r.Reason})" +
            $" | pantalla n={r.Stats.DisplayedMs.Length} 1%={r.Stats.Low1Displayed:F0} 0,1%={r.Stats.Low01Displayed:F0}" +
            $" reales n={r.Stats.RealMs.Length} 1%={r.Stats.Low1Real:F0} 0,1%={r.Stats.Low01Real:F0}"));
    }

    private void Wait(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero) _stop.Token.WaitHandle.WaitOne(delay);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _frameThread?.Join(TimeSpan.FromSeconds(2));
        Sessions.Flush();
        _frameThread?.Join(TimeSpan.FromSeconds(2));
        _hardwareThread?.Join(TimeSpan.FromSeconds(3));
        _frameGen.Dispose();
        _hardware.Dispose();
        _stop.Dispose();
    }
}
