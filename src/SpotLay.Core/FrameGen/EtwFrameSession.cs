using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpotLay.Core.FrameGen;

[Flags]
internal enum LoadedApis
{
    None = 0,
    D3D9 = 1,
    D3D10 = 2,
    D3D11 = 4,
    D3D12 = 8,
    Vulkan = 16,
    OpenGL = 32
}

internal sealed class ProcessFrameEvents
{
    public Dictionary<ulong, Queue<long>> PresentsBySwapChain { get; } = new();
    public Queue<long> SimulationStarts { get; } = new();
    public Queue<long> PresentMarkers { get; } = new();

    public bool IsEmpty => PresentsBySwapChain.Count == 0 && SimulationStarts.Count == 0 && PresentMarkers.Count == 0;
}

/// <summary>
/// Sesion ETW en tiempo real (necesita administrador). Recoge por proceso:
/// Present de DXGI (reales + generados), marcadores Reflex/PCL (uno por frame
/// real) y las DLL graficas que carga cada juego. No abre ni toca el proceso
/// del juego, asi que no molesta a los anti-cheat.
/// </summary>
internal sealed unsafe class EtwFrameSession : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid PclProvider = new("0D216F06-82A6-4D49-BC4F-8F38AE56EFAB");
    private static readonly Guid KernelProcessProvider = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");

    private const ushort DxgiPresentStart = 42;
    private const ushort DxgiPresentMpoStart = 55;
    private const ulong DxgiKeywords = 0x8000000000000002UL;   // Analytic | Events
    private const ulong KernelProcessKeywords = 0x10 | 0x40;    // PROCESS | IMAGE
    private const ushort KernelProcessStop = 2;
    private const ulong MarkerSimulationStart = 0;
    private const ulong MarkerPresentStart = 4;

    // Nombres de propiedad para TDH, en memoria no gestionada para toda la vida del proceso.
    private static readonly IntPtr PropSwapChain = Marshal.StringToHGlobalUni("pIDXGISwapChain");
    private static readonly IntPtr PropMarker = Marshal.StringToHGlobalUni("Marker");
    private static readonly IntPtr PropProcessId = Marshal.StringToHGlobalUni("ProcessID");
    private static readonly IntPtr PropImageName = Marshal.StringToHGlobalUni("ImageName");

    // ProcessTrace llama a un callback estatico: solo hay una sesion viva a la vez.
    private static EtwFrameSession? s_current;

    private readonly object _gate = new();
    private readonly Dictionary<uint, ProcessFrameEvents> _events = new();
    private readonly Dictionary<uint, LoadedApis> _apis = new();
    private ulong _session;
    private ulong _consumer = EtwNative.InvalidProcessTraceHandle;
    private IntPtr _loggerName;
    private Thread? _thread;
    private long _eventsSeen;
    private long _dxgiPresents;
    private long _pclMarkers;
    private long _callbackErrors;

    public EtwFrameSession(string sessionName) => SessionName = sessionName;

    public string SessionName { get; }

    /// <summary>Contadores para el registro de diagnostico.</summary>
    public (long Events, long DxgiPresents, long PclMarkers, long Errors) Counters =>
        (Interlocked.Read(ref _eventsSeen), Interlocked.Read(ref _dxgiPresents),
         Interlocked.Read(ref _pclMarkers), Interlocked.Read(ref _callbackErrors));

    public uint Start()
    {
        s_current = this;

        // Limpia una sesion zombi de un cierre a lo bruto anterior.
        EtwNative.StopSession(SessionName);

        var props = EtwNative.CreateProperties();
        uint status;
        fixed (byte* p = props) status = EtwNative.StartTrace(out _session, SessionName, p);
        if (status != EtwNative.ErrorSuccess) return status;

        status = EtwNative.EnableTraceEx2(_session, DxgiProvider, EtwNative.EnableProvider,
            EtwNative.LevelInformation, DxgiKeywords, 0, 0, IntPtr.Zero);
        if (status != EtwNative.ErrorSuccess) return status;

        status = EtwNative.EnableTraceEx2(_session, PclProvider, EtwNative.EnableProvider,
            EtwNative.LevelVerbose, 0, 0, 0, IntPtr.Zero);
        if (status != EtwNative.ErrorSuccess) return status;

        // La API es informativa: si este proveedor falla se sigue sin ella.
        if (EtwNative.EnableTraceEx2(_session, KernelProcessProvider, EtwNative.EnableProvider,
                EtwNative.LevelInformation, KernelProcessKeywords, 0, 0, IntPtr.Zero) == EtwNative.ErrorSuccess)
        {
            EtwNative.EnableTraceEx2(_session, KernelProcessProvider, EtwNative.CaptureState,
                EtwNative.LevelInformation, KernelProcessKeywords, 0, 0, IntPtr.Zero);
        }

        _loggerName = Marshal.StringToHGlobalUni(SessionName);
        _consumer = OpenConsumer();
        if (_consumer == EtwNative.InvalidProcessTraceHandle) return (uint)Marshal.GetLastPInvokeError();

        var first = _consumer;
        _thread = new Thread(() => Consume(first))
        {
            IsBackground = true,
            Name = "SpotLay ETW"
        };
        _thread.Start();
        return EtwNative.ErrorSuccess;
    }

    private ulong OpenConsumer()
    {
        var logFile = new byte[EtwNative.LogFileSize];
        delegate* unmanaged<EtwNative.EventRecord*, void> callback = &OnEventRecord;
        fixed (byte* lf = logFile)
        {
            *(IntPtr*)(lf + 8) = _loggerName;
            *(uint*)(lf + 28) = EtwNative.ProcessTraceModeRealTime | EtwNative.ProcessTraceModeEventRecord |
                                EtwNative.ProcessTraceModeRawTimestamp;
            *(IntPtr*)(lf + 424) = (IntPtr)callback;
            return EtwNative.OpenTrace(lf);
        }
    }

    // Justo despues de reiniciar una sesion con el mismo nombre, ProcessTrace
    // puede fallar al momento con 4201 (sesion no encontrada): se reintenta.
    private void Consume(ulong consumer)
    {
        for (var attempt = 1; ; attempt++)
        {
            var handle = consumer;
            var started = Stopwatch.GetTimestamp();
            var result = EtwNative.ProcessTrace(&handle, 1, IntPtr.Zero, IntPtr.Zero);
            var quickFail = result != EtwNative.ErrorSuccess && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1);
            if (!quickFail || attempt >= 10 || _consumer != consumer)
            {
                SpotLay.Core.Diagnostics.Log.Info($"ETW: consumo de {SessionName} terminado (código {result}, intento {attempt})");
                return;
            }
            Thread.Sleep(500);
            EtwNative.CloseTrace(consumer);
            consumer = OpenConsumer();
            _consumer = consumer;
            if (consumer == EtwNative.InvalidProcessTraceHandle) return;
        }
    }

    public void Flush()
    {
        var props = EtwNative.CreateProperties();
        fixed (byte* p = props) EtwNative.ControlTrace(_session, null, p, EtwNative.ControlFlush);
    }

    /// <param name="olderThan">Present más antiguos que esto se descartan.</param>
    /// <param name="markersOlderThan">Marcadores Reflex (frames reales): se guardan más para las estadísticas.</param>
    public void Prune(long olderThan, long markersOlderThan)
    {
        lock (_gate)
        {
            foreach (var pid in _events.Keys.ToArray())
            {
                var pe = _events[pid];
                foreach (var sc in pe.PresentsBySwapChain.Keys.ToArray())
                {
                    var q = pe.PresentsBySwapChain[sc];
                    while (q.Count > 0 && q.Peek() < olderThan) q.Dequeue();
                    if (q.Count == 0) pe.PresentsBySwapChain.Remove(sc);
                }
                while (pe.SimulationStarts.Count > 0 && pe.SimulationStarts.Peek() < markersOlderThan) pe.SimulationStarts.Dequeue();
                while (pe.PresentMarkers.Count > 0 && pe.PresentMarkers.Peek() < markersOlderThan) pe.PresentMarkers.Dequeue();
                if (pe.IsEmpty) _events.Remove(pid);
            }
        }
    }

    /// <summary>Ejecuta <paramref name="read"/> con los eventos del proceso bajo el cerrojo.</summary>
    public T Read<T>(uint pid, Func<ProcessFrameEvents?, T> read)
    {
        lock (_gate) return read(_events.GetValueOrDefault(pid));
    }

    public LoadedApis GetApis(uint pid)
    {
        lock (_gate) return _apis.GetValueOrDefault(pid);
    }

    [UnmanagedCallersOnly]
    private static void OnEventRecord(EtwNative.EventRecord* record)
    {
        var session = s_current;
        if (session is null) return;
        try
        {
            session.Handle(record);
        }
        catch
        {
            // Una excepcion no puede cruzar a codigo nativo: el evento se descarta.
            Interlocked.Increment(ref session._callbackErrors);
        }
    }

    private void Handle(EtwNative.EventRecord* r)
    {
        Interlocked.Increment(ref _eventsSeen);
        var header = r->Header;

        if (header.ProviderId == DxgiProvider)
        {
            var id = header.Descriptor.Id;
            if (id != DxgiPresentStart && id != DxgiPresentMpoStart) return;
            Interlocked.Increment(ref _dxgiPresents);
            TryReadUInt(r, PropSwapChain, out var swapChain);
            lock (_gate)
            {
                var pe = GetOrAdd(header.ProcessId);
                if (!pe.PresentsBySwapChain.TryGetValue(swapChain, out var q))
                    pe.PresentsBySwapChain[swapChain] = q = new Queue<long>();
                q.Enqueue(header.TimeStamp);
            }
            return;
        }

        if (header.ProviderId == PclProvider)
        {
            // Los eventos sin campo Marker (init, input, shutdown) no son frames.
            if (!TryReadUInt(r, PropMarker, out var marker)) return;
            Interlocked.Increment(ref _pclMarkers);
            lock (_gate)
            {
                if (marker == MarkerSimulationStart) GetOrAdd(header.ProcessId).SimulationStarts.Enqueue(header.TimeStamp);
                else if (marker == MarkerPresentStart) GetOrAdd(header.ProcessId).PresentMarkers.Enqueue(header.TimeStamp);
            }
            return;
        }

        if (header.ProviderId == KernelProcessProvider)
        {
            if (header.Descriptor.Id == KernelProcessStop)
            {
                if (TryReadUInt(r, PropProcessId, out var stopped))
                    lock (_gate) _apis.Remove((uint)stopped);
                return;
            }
            OnImageLoad(r);
        }
    }

    private void OnImageLoad(EtwNative.EventRecord* r)
    {
        if (!TryReadUInt(r, PropProcessId, out var pid) || !TryReadString(r, PropImageName, out var image)) return;

        var api = Path.GetFileName(image).ToLowerInvariant() switch
        {
            "d3d12.dll" => LoadedApis.D3D12,
            "d3d11.dll" => LoadedApis.D3D11,
            "d3d10.dll" or "d3d10_1.dll" => LoadedApis.D3D10,
            "d3d9.dll" => LoadedApis.D3D9,
            "vulkan-1.dll" => LoadedApis.Vulkan,
            "opengl32.dll" => LoadedApis.OpenGL,
            _ => LoadedApis.None
        };
        if (api == LoadedApis.None) return;
        lock (_gate) _apis[(uint)pid] = _apis.GetValueOrDefault((uint)pid) | api;
    }

    private ProcessFrameEvents GetOrAdd(uint pid)
    {
        if (!_events.TryGetValue(pid, out var pe)) _events[pid] = pe = new ProcessFrameEvents();
        return pe;
    }

    private static bool TryReadUInt(EtwNative.EventRecord* r, IntPtr name, out ulong value)
    {
        value = 0;
        var d = new EtwNative.PropertyDataDescriptor { PropertyName = (ulong)name, ArrayIndex = uint.MaxValue };
        if (EtwNative.TdhGetPropertySize(r, 0, IntPtr.Zero, 1, &d, out var size) != EtwNative.ErrorSuccess ||
            size == 0 || size > 8)
            return false;
        ulong v = 0;
        if (EtwNative.TdhGetProperty(r, 0, IntPtr.Zero, 1, &d, size, (byte*)&v) != EtwNative.ErrorSuccess) return false;
        value = v;
        return true;
    }

    private static bool TryReadString(EtwNative.EventRecord* r, IntPtr name, out string value)
    {
        value = "";
        var d = new EtwNative.PropertyDataDescriptor { PropertyName = (ulong)name, ArrayIndex = uint.MaxValue };
        if (EtwNative.TdhGetPropertySize(r, 0, IntPtr.Zero, 1, &d, out var size) != EtwNative.ErrorSuccess ||
            size < sizeof(char) || size > 4096)
            return false;
        var buffer = stackalloc byte[(int)size + sizeof(char)];
        if (EtwNative.TdhGetProperty(r, 0, IntPtr.Zero, 1, &d, size, buffer) != EtwNative.ErrorSuccess) return false;
        buffer[size] = 0;
        buffer[size + 1] = 0;
        value = new string((char*)buffer);
        return true;
    }

    /// <summary>Para la sesion y la vuelve a crear (sesion muda o parada por otra herramienta).</summary>
    public uint Restart()
    {
        Stop();
        Thread.Sleep(1000);
        return Start();
    }

    public void Dispose()
    {
        Stop();
        if (ReferenceEquals(s_current, this)) s_current = null;
    }

    private void Stop()
    {
        if (_consumer != EtwNative.InvalidProcessTraceHandle)
        {
            EtwNative.CloseTrace(_consumer);
            _consumer = EtwNative.InvalidProcessTraceHandle;
        }
        if (_session != 0)
        {
            var props = EtwNative.CreateProperties();
            fixed (byte* p = props) EtwNative.ControlTrace(_session, null, p, EtwNative.ControlStop);
            _session = 0;
        }
        if (_loggerName != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_loggerName);
            _loggerName = IntPtr.Zero;
        }
    }
}
