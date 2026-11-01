using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using SpotLay.App.Infrastructure;

namespace SpotLay.App.Overlay;

/// <summary>
/// Ctrl + Mayús + flechas mueve el overlay dentro del juego: un toque = 2 px;
/// manteniendo, a los 0,25 s se mueve seguido y a partir de 1 s acelera suave.
/// Dos flechas a la vez = diagonal. Las teclas solo se registran mientras se
/// ve el overlay con un juego delante, para no quitárselas a otras apps.
/// </summary>
public sealed partial class OverlayMover : IDisposable
{
    private const int BaseId = 0x5310;
    private const uint ModCtrlShift = 0x2 | 0x4;
    private static readonly int[] Keys = [0x25, 0x26, 0x27, 0x28]; // izquierda, arriba, derecha, abajo
    private const int TapStep = 2;
    private const double HoldDelay = 0.25;      // s antes de moverse seguido
    private const double SlowSpeed = 90;        // px/s hasta 1 s
    private const double FastSpeed = 900;       // px/s tras la rampa
    private const double RampStart = 1.0, RampSeconds = 1.5;

    private readonly HwndSource _source;
    private readonly DispatcherTimer _timer;
    private bool _registered;
    private DateTime _pressStart;
    private DateTime _lastTick;
    private double _carryX, _carryY;

    public OverlayMover()
    {
        _source = new HwndSource(new HwndSourceParameters("SpotLayOverlayMover") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Desplazamiento en píxeles reales.</summary>
    public event Action<int, int>? Moved;
    /// <summary>true al empezar a mover, false al soltar (momento de guardar).</summary>
    public event Action<bool>? MovingChanged;

    public bool IsMoving => _timer.IsEnabled;

    public void SetEnabled(bool enabled)
    {
        if (enabled == _registered) return;
        if (enabled)
        {
            // Sin MOD_NOREPEAT: la repetición del teclado no importa, el temporizador manda.
            for (var i = 0; i < Keys.Length; i++)
                NativeMethods.RegisterHotKey(_source.Handle, BaseId + i, ModCtrlShift, (uint)Keys[i]);
            _registered = true;
        }
        else
        {
            for (var i = 0; i < Keys.Length; i++) NativeMethods.UnregisterHotKey(_source.Handle, BaseId + i);
            _registered = false;
            Stop();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey) return IntPtr.Zero;
        var index = wParam.ToInt32() - BaseId;
        if (index is < 0 or > 3) return IntPtr.Zero;
        handled = true;
        if (IsMoving) return IntPtr.Zero;

        // Primer toque: 2 px en esa dirección.
        _pressStart = _lastTick = DateTime.UtcNow;
        _carryX = _carryY = 0;
        MovingChanged?.Invoke(true);
        var (dx, dy) = Direction();
        if (dx == 0 && dy == 0) (dx, dy) = index switch { 0 => (-1, 0), 1 => (0, -1), 2 => (1, 0), _ => (0, 1) };
        Moved?.Invoke(dx * TapStep, dy * TapStep);
        _timer.Start();
        return IntPtr.Zero;
    }

    private void Tick()
    {
        var (dx, dy) = Direction();
        if (dx == 0 && dy == 0)
        {
            Stop();
            return;
        }
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        var held = (now - _pressStart).TotalSeconds;
        if (held < HoldDelay) return;

        var t = Math.Clamp((held - RampStart) / RampSeconds, 0, 1);
        var smooth = t * t * (3 - 2 * t);
        var speed = SlowSpeed + (FastSpeed - SlowSpeed) * smooth;
        _carryX += dx * speed * dt;
        _carryY += dy * speed * dt;
        var mx = (int)_carryX;
        var my = (int)_carryY;
        _carryX -= mx;
        _carryY -= my;
        if (mx != 0 || my != 0) Moved?.Invoke(mx, my);
    }

    private void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        MovingChanged?.Invoke(false);
    }

    private static (int X, int Y) Direction()
    {
        var x = (Down(0x27) ? 1 : 0) - (Down(0x25) ? 1 : 0);
        var y = (Down(0x28) ? 1 : 0) - (Down(0x26) ? 1 : 0);
        return (x, y);
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    public void Dispose()
    {
        SetEnabled(false);
        _source.Dispose();
    }
}
