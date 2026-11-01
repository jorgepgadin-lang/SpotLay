using System.Diagnostics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.FrameGen;

/// <summary>
/// Convierte los eventos crudos en una lectura. Misma logica y umbrales que
/// FgMult, validados en Cyberpunk (MFG dinamico x3/x4/x5) e Immortals of Aveum (x2).
/// </summary>
internal sealed class FrameGenAnalyzer
{
    private const double LagSeconds = 0.30;      // margen para eventos aun en buffers
    private const double WindowSeconds = 1.00;
    private const double OffBelowRatio = 1.5;
    private const int MinRealFramesPerWindow = 10;   // por debajo: pantalla de carga/transicion

    private const int EvalsForStable = 4;            // 4 x 250 ms = 1 s con el mismo xN
    private const int EvalsForFraction = 8;          // 2 s seguidos con cociente no entero
    private const double FractionTolerance = 0.30;
    private static readonly TimeSpan StepWindow = TimeSpan.FromSeconds(30);   // 2 saltos en 30 s => dinamico
    private static readonly TimeSpan DynamicHold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FixedAfter = TimeSpan.FromSeconds(5);

    private readonly Dictionary<uint, ModeTrack> _modes = new();

    public readonly record struct RawMeasure(int Presents, int RealFrames, double PresentFps, double RealFps);

    private sealed class ModeTrack
    {
        public int CandidateMultiplier;
        public int CandidateCount;
        public int StableMultiplier;
        public int FractionCount;
        public DateTime FrameGenSince;
        public DateTime DynamicUntil;
        // Visto dinámico una vez: se queda DINÁMICO hasta que se cierre el juego (cada pid es una ejecución).
        public bool DynamicSeen;
        public readonly Queue<DateTime> StepChanges = new();
    }

    public static RawMeasure Measure(EtwFrameSession session, uint pid)
    {
        var freq = (double)Stopwatch.Frequency;
        var to = Stopwatch.GetTimestamp() - (long)(LagSeconds * freq);
        var from = to - (long)(WindowSeconds * freq);

        return session.Read(pid, events =>
        {
            if (events is null) return default;
            var present = default(WindowStat);
            Queue<long>? presents = null;
            foreach (var q in events.PresentsBySwapChain.Values)
            {
                var s = WindowStat.Of(q, from, to, freq);
                if (s.Count > present.Count)
                {
                    present = s;
                    presents = q;
                }
            }
            var realQueue = events.SimulationStarts;
            var real = WindowStat.Of(realQueue, from, to, freq);
            if (real.Count == 0)
            {
                realQueue = events.PresentMarkers;
                real = WindowStat.Of(realQueue, from, to, freq);
            }

            // Con Frame Generation los Present llegan en rafagas (N por frame real)
            // y (n-1)/(ultimo-primero) sobre ellos inflaba el total (123 en vez de
            // 118 con x6 a 19,7 reales). Se cuentan los Present entre el primer y el
            // ultimo marcador real: periodos reales completos, mismo tramo medido.
            var presentRate = present.Rate;
            if (presents is not null && real.Count >= 3 && real.Last > real.First)
            {
                var n = 0;
                foreach (var t in presents)
                    if (t >= real.First && t < real.Last) n++;
                if (n > 0) presentRate = n * freq / (real.Last - real.First);
            }
            return new RawMeasure(present.Count, real.Count, presentRate, real.Rate);
        });
    }

    public static bool IsTransition(RawMeasure raw) => raw.RealFrames is > 0 and < MinRealFramesPerWindow;

    public static (FrameGenState State, int Multiplier, double Ratio, string Reason) Classify(RawMeasure raw)
    {
        if (raw.Presents < 5 && raw.RealFrames == 0) return (FrameGenState.NoData, 1, 0, Tr.T("sin frames"));
        if (raw.RealFrames == 0) return (FrameGenState.NotMeasurable, 1, 0, Tr.T("sin marcadores Reflex"));
        if (raw.Presents == 0) return (FrameGenState.NotMeasurable, 1, 0, Tr.T("sin Present DXGI"));
        if (raw.RealFps <= 0 || raw.PresentFps <= 0) return (FrameGenState.NoData, 1, 0, Tr.T("pocos frames"));

        var ratio = raw.PresentFps / raw.RealFps;
        if (ratio < OffBelowRatio) return (FrameGenState.Off, 1, ratio, "");
        return (FrameGenState.On, (int)Math.Round(ratio, MidpointRounding.AwayFromZero), ratio, "");
    }

    public FrameGenMode UpdateMode(uint pid, FrameGenState state, int multiplier, double ratio, DateTime now)
    {
        if (!_modes.TryGetValue(pid, out var t)) _modes[pid] = t = new ModeTrack();

        if (state != FrameGenState.On)
        {
            t.CandidateMultiplier = 0;
            t.CandidateCount = 0;
            t.FractionCount = 0;
            t.FrameGenSince = default;
            return FrameGenMode.Measuring;
        }

        if (t.FrameGenSince == default) t.FrameGenSince = now;

        if (multiplier == t.CandidateMultiplier) t.CandidateCount++;
        else
        {
            t.CandidateMultiplier = multiplier;
            t.CandidateCount = 1;
        }

        if (t.CandidateCount == EvalsForStable)
        {
            if (t.StableMultiplier >= 2 && t.CandidateMultiplier != t.StableMultiplier) t.StepChanges.Enqueue(now);
            t.StableMultiplier = t.CandidateMultiplier;
        }
        while (t.StepChanges.Count > 0 && now - t.StepChanges.Peek() > StepWindow) t.StepChanges.Dequeue();
        if (t.StepChanges.Count >= 2) t.DynamicUntil = now + DynamicHold;

        t.FractionCount = Math.Abs(ratio - multiplier) > FractionTolerance ? t.FractionCount + 1 : 0;
        if (t.FractionCount >= EvalsForFraction) t.DynamicUntil = now + DynamicHold;

        if (now < t.DynamicUntil) t.DynamicSeen = true;
        // Quieto en un sitio el multiplicador deja de cambiar, pero sigue siendo dinámico.
        if (t.DynamicSeen) return FrameGenMode.Dynamic;
        // Un solo salto reciente puede ser un cambio manual o el primer paso de
        // un dinamico: no se afirma FIJO hasta que pase la ventana sin saltos.
        if (t.StepChanges.Count > 0) return FrameGenMode.Measuring;
        return now - t.FrameGenSince >= FixedAfter ? FrameGenMode.Fixed : FrameGenMode.Measuring;
    }

    public void Forget(uint pid) => _modes.Remove(pid);

    private readonly record struct WindowStat(int Count, double Rate, long First, long Last)
    {
        // Frecuencia desde las marcas de tiempo recibidas: (n-1)/(ultima-primera).
        // Contar en un segundo fijo daba un ~23% menos porque los eventos del
        // final de la ventana llegan con retraso. Min/max porque ETW en tiempo
        // real puede entregar fuera de orden entre CPUs.
        public static WindowStat Of(IEnumerable<long> timestamps, long from, long to, double freq)
        {
            var count = 0;
            var lo = long.MaxValue;
            var hi = long.MinValue;
            foreach (var t in timestamps)
            {
                if (t < from || t >= to) continue;
                count++;
                if (t < lo) lo = t;
                if (t > hi) hi = t;
            }
            var rate = count >= 2 && hi > lo ? (count - 1) * freq / (hi - lo) : 0;
            return new WindowStat(count, rate, lo, hi);
        }
    }
}
