using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.FrameGen;
using SpotLay.Core.Metrics;

namespace SpotLay.Core.Sessions;

/// <summary>Un segundo de partida. Nombres cortos: el archivo ocupa menos.</summary>
public sealed class SessionSample
{
    [JsonPropertyName("t")] public int T { get; set; }
    [JsonPropertyName("f")] public float Fps { get; set; }
    [JsonPropertyName("r")] public float FpsReal { get; set; }
    /// <summary>Peor frametime a pantalla de ese segundo (ms).</summary>
    [JsonPropertyName("m")] public float MaxFrametime { get; set; }
    [JsonPropertyName("g")] public float GpuTemp { get; set; }
    [JsonPropertyName("h")] public float HotSpot { get; set; }
    [JsonPropertyName("c")] public float CpuTemp { get; set; }
    [JsonPropertyName("p")] public float GpuPower { get; set; }
    [JsonPropertyName("k")] public float GpuClock { get; set; }
    [JsonPropertyName("u")] public float GpuLoad { get; set; }
    // Desde 12.1.5. null = sin lectura (partidas antiguas o sensor que no existe).
    [JsonPropertyName("vt")] public float? VramTemp { get; set; }
    [JsonPropertyName("cl")] public float? CpuLoad { get; set; }
    [JsonPropertyName("cp")] public float? CpuPower { get; set; }
    [JsonPropertyName("vr")] public float? Vram { get; set; }
    [JsonPropertyName("ra")] public float? Ram { get; set; }
}

public sealed record SessionSpike(int T, float Ms);

/// <summary>Una partida grabada.</summary>
public sealed class SessionRecord
{
    public int Version { get; set; } = 1;
    public string Exe { get; set; } = "";
    public string Api { get; set; } = "";
    public DateTime Start { get; set; }
    public int DurationSeconds { get; set; }
    public double AvgFps { get; set; }
    public double AvgFpsReal { get; set; }
    public double? Low1 { get; set; }
    public double? Low01 { get; set; }
    public double? Low1Real { get; set; }
    public double? Low01Real { get; set; }
    public double MaxGpuTemp { get; set; }
    public double MaxHotSpot { get; set; }
    public double MaxCpuTemp { get; set; }
    public double AvgGpuPower { get; set; }
    public double AvgGpuClock { get; set; }
    public List<SessionSpike> Spikes { get; set; } = [];
    public List<SessionSample> Samples { get; set; } = [];
    [JsonIgnore] public string File { get; set; } = "";
    [JsonIgnore] public long FileBytes { get; set; }
}

/// <summary>Histograma de frametimes (0,05 ms por casilla, hasta 250 ms) para el 1% low de la partida entera.</summary>
internal sealed class FrametimeHistogram
{
    private const double Bin = 0.05;
    private readonly long[] _counts = new long[5000];
    public long Count { get; private set; }

    public void Add(IEnumerable<double> ms)
    {
        foreach (var v in ms)
        {
            _counts[Math.Clamp((int)(v / Bin), 0, _counts.Length - 1)]++;
            Count++;
        }
    }

    /// <summary>FPS del percentil (0,99 = 1% low). null con pocos frames.</summary>
    public double? Low(double percentile)
    {
        if (Count < 100) return null;
        var target = (long)Math.Ceiling(percentile * Count);
        long seen = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            seen += _counts[i];
            if (seen >= target) return 1000.0 / ((i + 0.5) * Bin);
        }
        return null;
    }
}

/// <summary>
/// Graba cada partida sola: un resumen por segundo y todos los frametimes en un
/// histograma. Al acabar (15 s sin juego o cambio de juego) la guarda comprimida
/// si ha durado al menos 1 minuto. Se usa desde el hilo de medida.
/// </summary>
public sealed class SessionRecorder
{
    private static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(15);
    private const int MinSeconds = 60;

    private uint _pid;
    private SessionRecord? _current;
    private FrametimeHistogram _shown = new(), _real = new();
    private DateTime _started, _lastGame, _lastSample;
    private float _secondMax;
    // Los frames entran al histograma con 5 s de retraso: al cerrar el juego se
    // descartan los últimos 5 s (tirones de salida, no de juego); los 5 primeros
    // (carga) tampoco cuentan.
    private const int TrimSeconds = 5;
    private readonly Queue<(DateTime At, double[] Shown, double[] Real)> _pending = new();

    public int Limit { get; set; } = 50;

    /// <summary>Se ha guardado una partida (hilo de medida).</summary>
    public event Action? Saved;

    /// <summary>Partida en curso: (juego, segundos). null si no hay.</summary>
    public (string Exe, int Seconds)? Recording => _current is null ? null : (_current.Exe, (int)(DateTime.UtcNow - _started).TotalSeconds);

    public void Update(GameState game, FrameGenReading r, MetricSnapshot metrics, DateTime now)
    {
        if (game.IsGame && r.HasFrames)
        {
            if (_current is not null && game.ProcessId != _pid) Finish();
            if (_current is null) Begin(game, r, now);
            _lastGame = now;
            _pending.Enqueue((now, r.NewDisplayedMs, r.NewRealMs));
            while (_pending.Count > 0 && now - _pending.Peek().At > TimeSpan.FromSeconds(TrimSeconds))
            {
                var (at, shown, real) = _pending.Dequeue();
                if (at - _started < TimeSpan.FromSeconds(TrimSeconds)) continue;
                _shown.Add(shown);
                _real.Add(real);
            }
            foreach (var ms in r.NewDisplayedMs) if (ms > _secondMax) _secondMax = (float)ms;
            if (now - _lastSample >= TimeSpan.FromSeconds(1))
            {
                _lastSample = now;
                _current!.Samples.Add(new SessionSample
                {
                    T = (int)(now - _started).TotalSeconds,
                    Fps = (float)r.TotalFps,
                    FpsReal = (float)r.RealFps,
                    MaxFrametime = _secondMax,
                    GpuTemp = V(metrics, KnownMetrics.GpuTemp),
                    HotSpot = V(metrics, KnownMetrics.GpuHotSpot),
                    CpuTemp = V(metrics, KnownMetrics.CpuTemp),
                    GpuPower = V(metrics, KnownMetrics.GpuPower),
                    GpuClock = V(metrics, KnownMetrics.GpuClock),
                    GpuLoad = V(metrics, KnownMetrics.GpuLoad),
                    VramTemp = N(metrics, KnownMetrics.GpuMemoryTemp),
                    CpuLoad = N(metrics, KnownMetrics.CpuLoad),
                    CpuPower = N(metrics, KnownMetrics.CpuPower),
                    Vram = N(metrics, KnownMetrics.Vram),
                    Ram = N(metrics, KnownMetrics.Ram)
                });
                _secondMax = 0;
            }
        }
        else if (_current is not null && now - _lastGame > EndAfter)
        {
            Finish();
        }
    }

    /// <summary>Al cerrar SpotLay: se guarda lo que haya.</summary>
    public void Flush()
    {
        if (_current is not null) Finish();
    }

    private void Begin(GameState game, FrameGenReading r, DateTime now)
    {
        _pid = game.ProcessId;
        _started = _lastSample = now;
        _shown = new();
        _real = new();
        _secondMax = 0;
        _pending.Clear();
        _current = new SessionRecord { Exe = Path.GetFileNameWithoutExtension(game.ExeName), Api = r.ApiName, Start = DateTime.Now };
        Log.Info($"Partida: empieza la grabación de {_current.Exe}");
    }

    private void Finish()
    {
        var s = _current!;
        _current = null;
        _pending.Clear();
        // Fuera los primeros y los últimos segundos (carga y salida del juego).
        s.Samples = s.Samples.Where(x => x.T >= TrimSeconds).ToList();
        if (s.Samples.Count > TrimSeconds) s.Samples.RemoveRange(s.Samples.Count - TrimSeconds, TrimSeconds);
        s.DurationSeconds = s.Samples.Count > 0 ? s.Samples[^1].T : 0;
        if (s.DurationSeconds < MinSeconds)
        {
            Log.Info($"Partida de {s.Exe} descartada: {s.DurationSeconds} s (menos de 1 minuto)");
            return;
        }
        var samples = s.Samples;
        s.AvgFps = samples.Average(x => x.Fps);
        s.AvgFpsReal = samples.Average(x => x.FpsReal);
        s.Low1 = _shown.Low(0.99);
        s.Low01 = _shown.Low(0.999);
        s.Low1Real = _real.Low(0.99);
        s.Low01Real = _real.Low(0.999);
        s.MaxGpuTemp = samples.Max(x => x.GpuTemp);
        s.MaxHotSpot = samples.Max(x => x.HotSpot);
        s.MaxCpuTemp = samples.Max(x => x.CpuTemp);
        s.AvgGpuPower = samples.Average(x => x.GpuPower);
        s.AvgGpuClock = samples.Average(x => x.GpuClock);
        // Tirones: segundos cuyo peor frame tarda más del doble de lo normal (y más de 25 ms).
        var typical = samples.Where(x => x.Fps > 0).Select(x => 1000.0 / x.Fps).DefaultIfEmpty(16.7).Order().ElementAt(samples.Count(x => x.Fps > 0) / 2);
        s.Spikes = samples.Where(x => x.MaxFrametime > Math.Max(25, typical * 2.5))
            .OrderByDescending(x => x.MaxFrametime).Take(8)
            .Select(x => new SessionSpike(x.T, x.MaxFrametime)).OrderBy(x => x.T).ToList();
        try
        {
            SessionStore.Save(s, Limit);
            Log.Info($"Partida guardada: {s.Exe} {s.DurationSeconds / 60} min, media {s.AvgFps:F0}, 1% low {s.Low1:F0}");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar la partida", ex);
        }
    }

    private static float V(MetricSnapshot m, string id) => N(m, id) ?? 0;
    private static float? N(MetricSnapshot m, string id) => m.Get(id)?.Value is { } v && double.IsFinite(v) ? (float)Math.Round(v, 2) : null;
}

/// <summary>Partidas en %LOCALAPPDATA%\SpotLay\v11\sessions\ (JSON comprimido con gzip).</summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static void Save(SessionRecord s, int limit)
    {
        Directory.CreateDirectory(AppPaths.SessionsDirectory);
        var safe = string.Concat(s.Exe.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(AppPaths.SessionsDirectory, $"{s.Start:yyyy-MM-dd_HHmmss}_{safe}.json.gz");
        using (var file = File.Create(path))
        using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gz, s, Json);
        Trim(limit);
    }

    /// <summary>Solo el resumen (sin muestras) de todas las partidas, de la más nueva a la más vieja.</summary>
    public static List<SessionRecord> List()
    {
        if (!Directory.Exists(AppPaths.SessionsDirectory)) return [];
        var list = new List<SessionRecord>();
        foreach (var f in Directory.GetFiles(AppPaths.SessionsDirectory, "*.json.gz").OrderDescending())
        {
            var s = Load(f);
            if (s is null) continue;
            s.Samples = [];
            list.Add(s);
        }
        return list;
    }

    public static SessionRecord? Load(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gz = new GZipStream(file, CompressionMode.Decompress);
            var s = JsonSerializer.Deserialize<SessionRecord>(gz, Json);
            if (s is null) return null;
            s.File = path;
            s.FileBytes = new FileInfo(path).Length;
            return s;
        }
        catch (Exception ex)
        {
            Log.Warn($"Partida ilegible {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    public static (int Count, long Bytes) Totals()
    {
        if (!Directory.Exists(AppPaths.SessionsDirectory)) return (0, 0);
        var files = Directory.GetFiles(AppPaths.SessionsDirectory, "*.json.gz");
        return (files.Length, files.Sum(f => new FileInfo(f).Length));
    }

    public static void DeleteAll()
    {
        if (!Directory.Exists(AppPaths.SessionsDirectory)) return;
        foreach (var f in Directory.GetFiles(AppPaths.SessionsDirectory, "*.json.gz")) File.Delete(f);
    }

    public static void Trim(int limit)
    {
        if (!Directory.Exists(AppPaths.SessionsDirectory)) return;
        foreach (var f in Directory.GetFiles(AppPaths.SessionsDirectory, "*.json.gz").OrderDescending().Skip(limit)) File.Delete(f);
    }
}
