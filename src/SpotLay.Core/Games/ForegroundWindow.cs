using System.Runtime.InteropServices;

namespace SpotLay.Core.Games;

public sealed record ForegroundInfo(uint ProcessId, string ExeName, IntPtr Window, bool CoversMonitor, IntPtr Monitor, bool Exclusive = false)
{
    public static ForegroundInfo None { get; } = new(0, "", IntPtr.Zero, false, IntPtr.Zero);
}

/// <summary>Ventana en primer plano. Solo abre el proceso con permiso de consulta limitada.</summary>
public static partial class ForegroundWindow
{
    public static ForegroundInfo Read(IReadOnlySet<uint> ignoredProcessIds)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return ForegroundInfo.None;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || ignoredProcessIds.Contains(pid)) return ForegroundInfo.None;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var exe = ExeNameOf(pid);
        // El escritorio y el menú Inicio ocupan toda la pantalla pero no son juegos.
        var covers = !ShellProcesses.Contains(exe) && CoversMonitor(window, monitor);
        return new ForegroundInfo(pid, exe, window, covers, monitor, covers && IsExclusiveFullscreen());
    }

    /// <summary>
    /// Windows avisa cuando una app Direct3D tiene la pantalla en exclusiva: ahí ninguna
    /// ventana (tampoco el overlay) se puede dibujar encima.
    /// </summary>
    private static bool IsExclusiveFullscreen() =>
        SHQueryUserNotificationState(out var state) == 0 && state == QunsRunningD3DFullScreen;

    private const int QunsRunningD3DFullScreen = 3;

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "SearchHost.exe", "SearchApp.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
        "TextInputHost.exe", "LockApp.exe", "dwm.exe", "ShellHost.exe"
    };

    private static bool CoversMonitor(IntPtr window, IntPtr monitor)
    {
        if (!GetWindowRect(window, out var rect)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return false;
        var m = info.Monitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    public static string ExeNameOf(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size)
                ? Path.GetFileName(new string(buffer, 0, size))
                : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public static Rect MonitorBounds(IntPtr monitor, bool workArea)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(monitor, ref info) ? (workArea ? info.Work : info.Monitor) : default;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] buffer, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
