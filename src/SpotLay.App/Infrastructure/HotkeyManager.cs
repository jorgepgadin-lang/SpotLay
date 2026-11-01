using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using SpotLay.Core.Localization;

namespace SpotLay.App.Infrastructure;

/// <summary>Tecla rapida global (RegisterHotKey) sobre una ventana de mensajes oculta.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x5301;
    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("SpotLayHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Resultado del último registro de cada tecla de SpotLay (true = registrada).</summary>
    public static Dictionary<string, bool> Status { get; } = new();

    /// <summary>¿Nadie usa esa combinación? (se registra un instante y se suelta).</summary>
    public static bool IsFree(int virtualKey, int modifiers)
    {
        const int probeId = 0x5399;
        if (!NativeMethods.RegisterHotKey(IntPtr.Zero, probeId, (uint)modifiers | NativeMethods.ModNoRepeat, (uint)virtualKey)) return false;
        NativeMethods.UnregisterHotKey(IntPtr.Zero, probeId);
        return true;
    }

    /// <summary>
    /// Carácter que escribe esa combinación con la distribución de teclado actual, o null.
    /// Ctrl + Alt es AltGr para Windows: Ctrl + Alt + 2 en un teclado español es la arroba,
    /// y registrarla como tecla rápida impide escribirla.
    /// </summary>
    public static string? AltGrChar(int virtualKey, int modifiers)
    {
        if ((modifiers & 0x3) != 0x3 || (modifiers & 0x8) != 0) return null;
        var state = new byte[256];
        state[0x11] = state[0x12] = 0x80;               // Ctrl, Alt
        state[0xA2] = state[0xA5] = 0x80;               // Ctrl izq., Alt der. (AltGr)
        if ((modifiers & 0x4) != 0) state[0x10] = state[0xA0] = 0x80;
        var buffer = new StringBuilder(8);
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
        // 0x4: no tocar el estado de teclas muertas del teclado.
        var n = ToUnicodeEx((uint)virtualKey, 0, state, buffer, buffer.Capacity, 0x4, layout);
        // n < 0: tecla muerta (AltGr + 4 = ~ en español), también escribe.
        var length = n < 0 ? 1 : n;
        return length > 0 && buffer.Length > 0 && !char.IsControl(buffer[0]) ? buffer.ToString(0, Math.Min(length, buffer.Length)) : null;
    }

    /// <summary>Problema de la combinación: ocupada por otra app o choca con AltGr. "" si está bien.</summary>
    public static string Problem(int virtualKey, int modifiers, bool registered) =>
        !registered ? Tr.T("Ocupada por otra app")
        : AltGrChar(virtualKey, modifiers) is { } ch ? Tr.F("Choca con AltGr ({0})", ch)
        : "";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, StringBuilder buffer, int bufferSize, uint flags, IntPtr layout);

    public bool Register(int virtualKey, int modifiers)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, (uint)modifiers | NativeMethods.ModNoRepeat, (uint)virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public static string Describe(int virtualKey, int modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & 0x2) != 0) parts.Add(Tr.T("Ctrl"));
        if ((modifiers & 0x1) != 0) parts.Add(Tr.T("Alt"));
        if ((modifiers & 0x4) != 0) parts.Add(Tr.T("Mayús"));
        if ((modifiers & 0x8) != 0) parts.Add(Tr.T("Win"));
        parts.Add(KeyName(virtualKey));
        return string.Join(" + ", parts);
    }

    /// <summary>Nombre legible: "2" en vez de "D2", "Num 2", "Flecha arriba"…</summary>
    private static string KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => Tr.F("Num {0}", vk - 0x60),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x6A => Tr.T("Num *"), 0x6B => Tr.T("Num +"), 0x6D => Tr.T("Num -"), 0x6E => Tr.T("Num ."), 0x6F => Tr.T("Num /"),
        0x25 => Tr.T("Flecha izquierda"), 0x26 => Tr.T("Flecha arriba"), 0x27 => Tr.T("Flecha derecha"), 0x28 => Tr.T("Flecha abajo"),
        0x20 => Tr.T("Espacio"), 0x0D => Tr.T("Intro"), 0x09 => Tr.T("Tab"), 0x08 => Tr.T("Retroceso"),
        0x2D => Tr.T("Insert"), 0x2E => Tr.T("Supr"), 0x24 => Tr.T("Inicio"), 0x23 => Tr.T("Fin"), 0x21 => Tr.T("Re Pág"), 0x22 => Tr.T("Av Pág"),
        0x13 => Tr.T("Pausa"), 0x91 => Tr.T("Bloq Despl"), 0xC0 => "º", 0xDC => "ª", 0xBB => "+", 0xBD => "-", 0xBC => ",", 0xBE => ".",
        _ => System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk).ToString()
    };

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
