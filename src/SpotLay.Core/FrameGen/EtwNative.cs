using System.Runtime.InteropServices;

namespace SpotLay.Core.FrameGen;

// Tamanos y desplazamientos comprobados con offsetof() en el SDK de Windows (x64):
// EVENT_TRACE_PROPERTIES 120, EVENT_TRACE_LOGFILEW 448, EVENT_RECORD 112, EVENT_HEADER 80.
internal static unsafe partial class EtwNative
{
    public const uint WnodeFlagTracedGuid = 0x00020000;
    public const uint EventTraceRealTimeMode = 0x00000100;
    public const uint ProcessTraceModeRealTime = 0x00000100;
    public const uint ProcessTraceModeRawTimestamp = 0x00001000;
    public const uint ProcessTraceModeEventRecord = 0x10000000;
    public const uint ControlStop = 1;
    public const uint ControlFlush = 3;
    public const uint EnableProvider = 1;
    public const uint CaptureState = 2;
    public const byte LevelInformation = 4;
    public const byte LevelVerbose = 5;
    public const ulong InvalidProcessTraceHandle = ulong.MaxValue;
    public const uint ErrorSuccess = 0;
    public const uint ErrorAccessDenied = 5;

    public const int PropertiesSize = 120;
    public const int PropertiesBufferSize = PropertiesSize + 2 * 1024 * sizeof(char);
    public const int LogFileSize = 448;

    [StructLayout(LayoutKind.Sequential)]
    public struct EventDescriptor
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public struct EventHeader
    {
        [FieldOffset(4)] public ushort Flags;
        [FieldOffset(8)] public uint ThreadId;
        [FieldOffset(12)] public uint ProcessId;
        [FieldOffset(16)] public long TimeStamp;
        [FieldOffset(24)] public Guid ProviderId;
        [FieldOffset(40)] public EventDescriptor Descriptor;
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    public struct EventRecord
    {
        [FieldOffset(0)] public EventHeader Header;
        [FieldOffset(86)] public ushort UserDataLength;
        [FieldOffset(96)] public IntPtr UserData;
        [FieldOffset(104)] public IntPtr UserContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyDataDescriptor
    {
        public ulong PropertyName;
        public uint ArrayIndex;
        public uint Reserved;
    }

    /// <summary>Rellena un EVENT_TRACE_PROPERTIES para una sesion en tiempo real con marcas QPC.</summary>
    public static byte[] CreateProperties()
    {
        var buffer = new byte[PropertiesBufferSize];
        fixed (byte* p = buffer)
        {
            *(uint*)(p + 0) = PropertiesBufferSize;    // Wnode.BufferSize
            *(uint*)(p + 40) = 1;                        // Wnode.ClientContext = QPC
            *(uint*)(p + 44) = WnodeFlagTracedGuid;      // Wnode.Flags
            *(uint*)(p + 48) = 64;                       // BufferSize (KB)
            *(uint*)(p + 52) = 32;                       // MinimumBuffers
            *(uint*)(p + 56) = 128;                      // MaximumBuffers
            *(uint*)(p + 64) = EventTraceRealTimeMode;   // LogFileMode
            *(uint*)(p + 68) = 1;                        // FlushTimer (s)
            *(uint*)(p + 116) = PropertiesSize;          // LoggerNameOffset
        }
        return buffer;
    }

    /// <summary>Detiene una sesion por nombre (p. ej. la que deja un PresentMon cerrado a la fuerza).</summary>
    public static void StopSession(string name)
    {
        var props = CreateProperties();
        fixed (byte* p = props) ControlTrace(0, name, p, ControlStop);
    }

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTrace(out ulong handle, string name, byte* properties);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTrace(ulong handle, string? name, byte* properties, uint control);

    [LibraryImport("advapi32.dll")]
    public static partial uint EnableTraceEx2(ulong handle, in Guid provider, uint controlCode, byte level,
        ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr parameters);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true)]
    public static partial ulong OpenTrace(byte* logFile);

    [LibraryImport("advapi32.dll")]
    public static partial uint ProcessTrace(ulong* handles, uint count, IntPtr startTime, IntPtr endTime);

    [LibraryImport("advapi32.dll")]
    public static partial uint CloseTrace(ulong handle);

    [LibraryImport("tdh.dll")]
    public static partial uint TdhGetPropertySize(EventRecord* record, uint contextCount, IntPtr context,
        uint descriptorCount, PropertyDataDescriptor* descriptors, out uint size);

    [LibraryImport("tdh.dll")]
    public static partial uint TdhGetProperty(EventRecord* record, uint contextCount, IntPtr context,
        uint descriptorCount, PropertyDataDescriptor* descriptors, uint bufferSize, byte* buffer);
}
