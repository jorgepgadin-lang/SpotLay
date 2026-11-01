using System.Diagnostics;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.FrameGen;

/// <summary>
/// Frame Generation medido, no deducido:
///   frames reales     = marcadores Reflex SimulationStart del juego
///                       (DLSS Frame Generation obliga a llevar Reflex),
///   frames a pantalla = Present de DXGI del juego (reales + generados),
///   multiplicador     = frames a pantalla / frames reales.
/// En Vulkan/OpenGL no hay Present de DXGI: solo se dan los FPS finales.
/// </summary>
public sealed class FrameGenMonitor : IDisposable
{
    private readonly EtwFrameSession _session = new("SpotLay11Etw");
    private readonly FrameGenAnalyzer _analyzer = new();
    private readonly VulkanFpsProbe _vulkan = new();
    private readonly Dictionary<uint, FrameGenReading> _lastShown = new();
    private bool _started;
    private long _lastEventCount = -1;
    private DateTime _lastEventChange = DateTime.UtcNow;
    private DateTime _lastRestart;

    public uint Start()
    {
        var status = _session.Start();
        _started = status == EtwNative.ErrorSuccess;
        return status;
    }

    public bool IsRunning => _started;

    /// <summary>Segundos que abarcan el 1% low y la gráfica de frametime.</summary>
    public double StatsSeconds { get; set; } = 10;

    public (long Events, long DxgiPresents, long PclMarkers, long Errors) Counters => _session.Counters;

    public static bool IsAccessDenied(uint status) => status == EtwNative.ErrorAccessDenied;

    /// <param name="pid">Proceso a medir (normalmente el de primer plano).</param>
    /// <param name="looksLikeGame">Ventana a pantalla completa: permite lanzar PresentMon aunque no se sepa la API.</param>
    public FrameGenReading Read(uint pid, bool looksLikeGame)
    {
        if (!_started || pid == 0) return FrameGenReading.None;

        WatchSession();
        _session.Flush();
        var now = Stopwatch.GetTimestamp();
        _session.Prune(now - 5 * Stopwatch.Frequency, now - (long)((StatsSeconds + 2) * Stopwatch.Frequency));

        var raw = FrameGenAnalyzer.Measure(_session, pid);
        var apis = _session.GetApis(pid);
        // Navegadores, Claude, Discord… dibujan con Vulkan/OpenGL o a pantalla completa, pero
        // no son juegos: nunca se les lanza PresentMon (antes arrancaba una y otra vez).
        var nonGame = IsKnownNonGame(pid);
        if (nonGame) looksLikeGame = false;

        if (raw.Presents > 0)
        {
            // PresentMon también en DirectX (solo si parece un juego: no para el navegador):
            // da el tiempo real entre frames mostrados.
            if (looksLikeGame || raw.RealFrames > 0) _vulkan.Ensure(pid);
            else _vulkan.Stop();
            var (newShown, newReal) = Drain(pid);
            return ReadDxgi(pid, raw, DxgiApi(apis)) with { Stats = Stats(pid), NewDisplayedMs = newShown, NewRealMs = newReal };
        }

        var nonDxgi = NonDxgiApi(apis);
        // ETW puede no ver las DLL cargadas antes de arrancar SpotLay: se miran los módulos del proceso.
        if (nonDxgi == GraphicsApi.Unknown) nonDxgi = ModuleApi(pid);
        if (!nonGame && (nonDxgi != GraphicsApi.Unknown || looksLikeGame))
        {
            var fps = _vulkan.GetFps(pid);
            var api = nonDxgi == GraphicsApi.Unknown ? GraphicsApi.VulkanOrOpenGL : nonDxgi;
            return fps > 0
                ? new FrameGenReading(FrameGenState.NotMeasurable, 1, FrameGenMode.Measuring, fps, 0, api, Tr.T("sin DXGI: solo FPS"))
                    { Stats = Stats(pid), NewDisplayedMs = Drain(pid).Displayed }
                : new FrameGenReading(FrameGenState.NoData, 1, FrameGenMode.Measuring, 0, 0, api, Tr.T("sin frames"));
        }

        _vulkan.Stop();
        return FrameGenReading.None with { Reason = Tr.T("sin frames") };
    }

    private static readonly HashSet<string> NonGameApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude.exe", "chrome.exe", "msedge.exe", "msedgewebview2.exe", "firefox.exe", "opera.exe", "opera_gx.exe",
        "brave.exe", "vivaldi.exe", "discord.exe", "slack.exe", "teams.exe", "ms-teams.exe", "whatsapp.exe",
        "telegram.exe", "spotify.exe", "code.exe", "githubdesktop.exe", "steamwebhelper.exe", "obs64.exe",
        "notion.exe", "figma.exe", "chatgpt.exe", "explorer.exe", "spotlay.exe"
    };

    private uint _nonGamePid;
    private bool _nonGame;

    private bool IsKnownNonGame(uint pid)
    {
        if (pid != _nonGamePid)
        {
            _nonGamePid = pid;
            _nonGame = NonGameApps.Contains(Games.ForegroundWindow.ExeNameOf(pid));
        }
        return _nonGame;
    }

    /// <summary>
    /// Si la sesion ETW lleva 10 s sin ningun evento (otra herramienta la paro o
    /// ETW se quedo mudo), se reinicia sola. Como mucho una vez cada 30 s.
    /// </summary>
    private void WatchSession()
    {
        var now = DateTime.UtcNow;
        var events = _session.Counters.Events;
        if (events != _lastEventCount)
        {
            _lastEventCount = events;
            _lastEventChange = now;
            return;
        }
        if (now - _lastEventChange < TimeSpan.FromSeconds(10) || now - _lastRestart < TimeSpan.FromSeconds(30)) return;
        _lastRestart = now;
        _lastEventChange = now;
        var status = _session.Restart();
        Log.Warn($"ETW: 10 s sin ningun evento; sesion reiniciada (codigo {status})");
    }

    private FrameGenReading ReadDxgi(uint pid, FrameGenAnalyzer.RawMeasure raw, GraphicsApi api)
    {
        // Pantallas de carga: con 2-9 frames reales en el segundo el cociente sale
        // absurdo (x14, x161). Se mantiene lo que ya se estaba mostrando.
        if (FrameGenAnalyzer.IsTransition(raw))
        {
            return _lastShown.TryGetValue(pid, out var previous)
                ? previous with { Reason = Tr.T("transición: pocos frames reales") }
                : new FrameGenReading(FrameGenState.NoData, 1, FrameGenMode.Measuring, 0, 0, api, "transición");
        }

        var (state, multiplier, ratio, reason) = FrameGenAnalyzer.Classify(raw);
        var mode = _analyzer.UpdateMode(pid, state, multiplier, ratio, DateTime.UtcNow);
        var reading = new FrameGenReading(state, multiplier, mode, raw.PresentFps, raw.RealFps, api, reason);
        _lastShown[pid] = reading;
        return reading;
    }

    // Si presenta por DXGI es DirectX aunque tenga vulkan-1.dll cargada (hay
    // juegos que la cargan solo para comprobar soporte).
    private static GraphicsApi DxgiApi(LoadedApis apis) =>
        apis.HasFlag(LoadedApis.D3D12) ? GraphicsApi.DirectX12 :
        apis.HasFlag(LoadedApis.D3D11) ? GraphicsApi.DirectX11 :
        apis.HasFlag(LoadedApis.D3D10) ? GraphicsApi.DirectX10 :
        GraphicsApi.DirectX;

    private static GraphicsApi NonDxgiApi(LoadedApis apis) =>
        apis.HasFlag(LoadedApis.Vulkan) ? GraphicsApi.Vulkan :
        apis.HasFlag(LoadedApis.OpenGL) ? GraphicsApi.OpenGL :
        apis.HasFlag(LoadedApis.D3D9) ? GraphicsApi.DirectX9 :
        GraphicsApi.Unknown;

    private uint _modulePid;
    private GraphicsApi _moduleApi;
    private DateTime _moduleChecked;

    private GraphicsApi ModuleApi(uint pid)
    {
        if (pid == _modulePid && (_moduleApi != GraphicsApi.Unknown || DateTime.UtcNow - _moduleChecked < TimeSpan.FromSeconds(5)))
            return _moduleApi;
        _modulePid = pid;
        _moduleChecked = DateTime.UtcNow;
        _moduleApi = GraphicsApi.Unknown;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            foreach (ProcessModule module in process.Modules)
            {
                var name = module.ModuleName.ToLowerInvariant();
                if (name == "vulkan-1.dll") { _moduleApi = GraphicsApi.Vulkan; break; }
                if (name == "opengl32.dll") _moduleApi = GraphicsApi.OpenGL;
            }
        }
        catch
        {
            // Proceso protegido o ya cerrado.
        }
        if (_moduleApi != GraphicsApi.Unknown) Log.Info($"API por módulos: pid {pid} usa {_moduleApi}");
        return _moduleApi;
    }

    private uint _drainPid;
    private long _drainSeq;
    private long _drainRealTs;

    /// <summary>Frames nuevos desde la lectura anterior de este proceso.</summary>
    private (double[] Displayed, double[] Real) Drain(uint pid)
    {
        if (pid != _drainPid)
        {
            _drainPid = pid;
            _drainSeq = long.MaxValue;   // se sincroniza abajo: no se cuenta lo que había antes
            _drainRealTs = Stopwatch.GetTimestamp();
        }
        double[] displayed = [];
        if (_vulkan.Pid == pid)
        {
            if (_drainSeq == long.MaxValue) { _drainSeq = 0; _vulkan.DrainDisplayed(ref _drainSeq); }
            else displayed = _vulkan.DrainDisplayed(ref _drainSeq).Where(ms => ms < 2000).ToArray();
        }
        var freq = (double)Stopwatch.Frequency;
        var last = _drainRealTs;
        var (real, newest) = _session.Read(pid, events =>
        {
            if (events is null) return (Array.Empty<double>(), last);
            var queue = events.SimulationStarts.Count > 0 ? events.SimulationStarts : events.PresentMarkers;
            var times = queue.Where(t => t >= last).ToArray();
            Array.Sort(times);
            var list = new List<double>();
            for (var i = 1; i < times.Length; i++)
            {
                var ms = (times[i] - times[i - 1]) * 1000.0 / freq;
                if (ms is > 0 and < 2000) list.Add(ms);
            }
            return (list.ToArray(), times.Length > 0 ? times[^1] : last);
        });
        _drainRealTs = newest;
        return (displayed, real);
    }

    private FrameTimeStats Stats(uint pid)
    {
        var seconds = StatsSeconds;
        var freq = (double)Stopwatch.Frequency;
        var from = Stopwatch.GetTimestamp() - (long)(seconds * freq);
        var real = _session.Read(pid, events =>
        {
            if (events is null) return Array.Empty<double>();
            var queue = events.SimulationStarts.Count > 0 ? events.SimulationStarts : events.PresentMarkers;
            var times = queue.Where(t => t >= from).ToArray();
            Array.Sort(times);   // ETW en tiempo real puede entregar fuera de orden
            var intervals = new List<double>(Math.Max(0, times.Length - 1));
            for (var i = 1; i < times.Length; i++)
            {
                var ms = (times[i] - times[i - 1]) * 1000.0 / freq;
                if (ms is > 0 and < 2000) intervals.Add(ms);   // fuera: pausas y pantallas de carga
            }
            return intervals.ToArray();
        });
        var displayed = _vulkan.Pid == pid ? _vulkan.DisplayedIntervals(seconds).Where(ms => ms < 2000).ToArray() : [];
        return new FrameTimeStats(displayed, real, seconds);
    }

    public void Forget(uint pid)
    {
        _lastShown.Remove(pid);
        _analyzer.Forget(pid);
    }

    public void Dispose()
    {
        _vulkan.Dispose();
        _session.Dispose();
        _started = false;
    }
}
