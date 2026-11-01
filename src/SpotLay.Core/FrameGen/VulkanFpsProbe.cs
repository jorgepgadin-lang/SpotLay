using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using SpotLay.Core.Diagnostics;

namespace SpotLay.Core.FrameGen;

/// <summary>
/// PresentMon embebido para el juego en primer plano: FPS de juegos sin DXGI
/// (Vulkan/OpenGL) y, en cualquier API, el tiempo entre frames que llegan a la
/// pantalla (MsBetweenDisplayChange), que es lo que usan FrameView/CapFrameX para
/// el 1% low. Con Frame Generation los Present van en ráfagas: no sirven para eso.
/// </summary>
internal sealed class VulkanFpsProbe : IDisposable
{
    private const string SessionName = "SpotLay11Vk";
    private readonly object _gate = new();
    private readonly Queue<double> _startTimesMs = new();
    // (inicio en CPU, ms desde el anterior frame mostrado) de los frames que llegan a pantalla.
    private readonly Queue<(double TimeMs, double IntervalMs, long Seq)> _displayed = new();
    private long _seq;

    /// <summary>Intervalos de los frames mostrados después de <paramref name="lastSeq"/> (y lo actualiza).</summary>
    public double[] DrainDisplayed(ref long lastSeq)
    {
        lock (_gate)
        {
            var from = lastSeq;
            var news = _displayed.Where(d => d.Seq > from).ToArray();
            if (news.Length > 0) lastSeq = news[^1].Seq;
            return news.Select(d => d.IntervalMs).ToArray();
        }
    }

    public uint Pid => _pid;

    /// <summary>Lanza PresentMon para ese proceso si no lo está ya.</summary>
    public void Ensure(uint pid)
    {
        if (pid != _pid || _process is null || _process.HasExited) Restart(pid);
    }

    /// <summary>Intervalos (ms) entre frames mostrados en los últimos <paramref name="seconds"/> s.</summary>
    public double[] DisplayedIntervals(double seconds)
    {
        lock (_gate)
        {
            if (_displayed.Count == 0) return [];
            var newest = _displayed.Max(d => d.TimeMs);
            return _displayed.Where(d => d.TimeMs >= newest - seconds * 1000).Select(d => d.IntervalMs).ToArray();
        }
    }
    private Process? _process;
    private uint _pid;

    public double GetFps(uint pid)
    {
        if (pid != _pid) Restart(pid);
        lock (_gate)
        {
            if (_startTimesMs.Count < 2) return 0;
            var newest = _startTimesMs.Max();
            var window = _startTimesMs.Where(t => t >= newest - 1000).ToArray();
            if (window.Length < 2) return 0;
            var span = window.Max() - window.Min();
            return span > 0 ? (window.Length - 1) * 1000.0 / span : 0;
        }
    }

    public void Stop()
    {
        _pid = 0;
        KillProcess();
        lock (_gate)
        {
            _startTimesMs.Clear();
            _displayed.Clear();
        }
    }

    private void Restart(uint pid)
    {
        Stop();
        _pid = pid;
        try
        {
            var start = new ProcessStartInfo(ExtractPresentMon(),
                $"--process_id {pid} --output_stdout --no_console_stats --session_name {SessionName} --stop_existing_session --terminate_on_proc_exit")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _process = Process.Start(start);
            if (_process is null) return;
            Log.Info($"PresentMon lanzado para pid {pid} (FPS sin DXGI)");
            var process = _process;
            _ = Task.Run(() => ReadFrames(process, pid));
            _ = Task.Run(() =>
            {
                var error = process.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(error)) Log.Warn("PresentMon: " + error.Trim().Replace(Environment.NewLine, " | "));
            });
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo iniciar PresentMon para Vulkan/OpenGL", ex);
        }
    }

    private void ReadFrames(Process process, uint pid)
    {
        try
        {
            var timeColumn = -1;
            var displayColumn = -1;
            var seconds = false;
            var frames = 0;
            while (!process.HasExited && _pid == pid)
            {
                var line = process.StandardOutput.ReadLine();
                if (line is null) break;
                var fields = line.Split(',');
                if (fields.Length > 0 && fields[0].Equals("Application", StringComparison.OrdinalIgnoreCase))
                {
                    // El nombre cambia según la versión de PresentMon (2.4.1: CPUStartTimeInMs).
                    foreach (var name in new[] { "CPUStartTimeInMs", "CPUStartTime", "TimeInMs" })
                    {
                        timeColumn = Array.FindIndex(fields, f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (timeColumn >= 0) break;
                    }
                    if (timeColumn < 0)
                    {
                        timeColumn = Array.FindIndex(fields, f => f.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase));
                        seconds = timeColumn >= 0;
                    }
                    displayColumn = Array.FindIndex(fields, f => f.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase));
                    if (timeColumn < 0) Log.Warn("PresentMon: no se encuentra la columna de tiempo en: " + line);
                    continue;
                }
                if (timeColumn < 0 || timeColumn >= fields.Length) continue;
                if (!double.TryParse(fields[timeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
                var ms = seconds ? t * 1000 : t;
                if (++frames == 1) Log.Info($"PresentMon: primer frame del pid {pid}");
                double display = 0;
                var shown = displayColumn >= 0 && displayColumn < fields.Length &&
                            double.TryParse(fields[displayColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out display) && display > 0;
                lock (_gate)
                {
                    _startTimesMs.Enqueue(ms);
                    while (_startTimesMs.Count > 2000 || (_startTimesMs.Count > 0 && _startTimesMs.Peek() < ms - 3000))
                        _startTimesMs.Dequeue();
                    if (shown) _displayed.Enqueue((ms, display, ++_seq));
                    while (_displayed.Count > 0 && _displayed.Peek().TimeMs < ms - 65_000) _displayed.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Lectura de PresentMon (Vulkan) interrumpida: " + ex.Message);
        }
        if (_pid == pid) Log.Warn($"PresentMon terminó sin que se le pidiera (pid {pid}, código {(process.HasExited ? process.ExitCode : -1)})");
    }

    private static string ExtractPresentMon()
    {
        Directory.CreateDirectory(AppPaths.ToolsDirectory);
        var path = Path.Combine(AppPaths.ToolsDirectory, "PresentMon.exe");
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpotLay.Tools.PresentMon.exe")
                           ?? throw new InvalidOperationException("PresentMon no esta incluido en el ejecutable.");
        if (!File.Exists(path) || new FileInfo(path).Length != source.Length)
        {
            using var destination = File.Create(path);
            source.CopyTo(destination);
        }
        return path;
    }

    private void KillProcess()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(true);
        }
        catch
        {
            // Si ya ha terminado no hay nada que hacer.
        }
        _process.Dispose();
        _process = null;
        // PresentMon cerrado a la fuerza no para su sesion ETW: si no se para
        // aqui queda colgada hasta reiniciar.
        EtwNative.StopSession(SessionName);
    }

    public void Dispose() => Stop();
}
