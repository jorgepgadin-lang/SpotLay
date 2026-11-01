using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SpotLay.App.Infrastructure;
using SpotLay.Core.Config;
using SpotLay.Core.Games;

namespace SpotLay.App.Overlay;

/// <summary>
/// Ventana del overlay: transparente, siempre encima, no recibe clics ni foco.
/// Solo se mueve cuando cambia su posicion calculada: moverla en cada lectura
/// era lo que hacia temblar el overlay de SpotLay 10.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private OverlayLayout? _layout;
    private IntPtr _monitor;
    private (int X, int Y) _lastPosition = (int.MinValue, int.MinValue);
    private bool _editable;

    /// <summary>Se ha soltado el overlay tras arrastrarlo (x, y reales; true = con Alt, sin imán).</summary>
    public event Action<int, int, bool>? Dragged;
    /// <summary>Ctrl + arrastrar: la métrica ha quedado en otro puesto (id, nuevo índice).</summary>
    public event Action<string, int>? TileMoved;
    /// <summary>Ctrl + clic sin mover: elegir esa métrica en el editor.</summary>
    public event Action<string>? TileClicked;

    private const int VkControl = 0x11, VkMenu = 0x12, VkEscape = 0x1B;
    private OverlayTileViewModel? _dragTile;
    private int _dragStartIndex;
    private Point _dragStart;
    private bool _dragMoved;
    private OverlayViewModel Model => (OverlayViewModel)DataContext;

    public OverlayWindow(OverlayViewModel model)
    {
        InitializeComponent();
        // El tema Fluent pondria fondo Mica; el overlay tiene que ser transparente.
        ThemeMode = ThemeMode.None;
        DataContext = model;
        SizeChanged += (_, _) => Reposition();
        // La primera vez que se muestra, el tamaño real no se conoce hasta pintar.
        ContentRendered += (_, _) => Invalidate();
        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => KeepOnTop(), Dispatcher);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExLayered;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
        _topmostTimer.Start();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _lastPosition = (int.MinValue, int.MinValue);
        Reposition();
    }

    public void SetPlacement(OverlayLayout layout, IntPtr monitor)
    {
        if (ReferenceEquals(layout, _layout) && monitor == _monitor) return;
        _layout = layout;
        _monitor = monitor;
        _lastPosition = (int.MinValue, int.MinValue);
        Reposition();
    }

    /// <summary>
    /// Editando en SpotLay el overlay deja de ser "transparente a los clics" y se puede
    /// arrastrar con el ratón. En el juego sigue sin recibir clics ni foco.
    /// </summary>
    public void SetEditable(bool editable)
    {
        if (editable == _editable || _hwnd == IntPtr.Zero) return;
        _editable = editable;
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        style = editable ? style & ~NativeMethods.WsExTransparent : style | NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
        Cursor = editable ? System.Windows.Input.Cursors.SizeAll : null;
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_editable) return;
        // Ctrl: se mueve una tarjeta dentro del overlay; sin Ctrl, el overlay entero.
        if (NativeMethods.IsKeyDown(VkControl))
        {
            StartTileDrag(e.GetPosition(this));
            return;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        if (!NativeMethods.GetWindowRect(_hwnd, out var r)) return;
        // El overlay no tiene el foco: Keyboard.Modifiers no sirve aquí.
        var noSnap = NativeMethods.IsKeyDown(VkMenu);
        Dragged?.Invoke(r.Left, r.Top, noSnap);
    }

    // ---- Ctrl + arrastrar una tarjeta ------------------------------------------
    private void StartTileDrag(Point p)
    {
        if (TileAt(p) is not { } tile) return;
        _dragTile = tile;
        _dragStartIndex = Model.Tiles.IndexOf(tile);
        _dragStart = p;
        _dragMoved = false;
        tile.IsLifted = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTile is not { } tile) return;
        if (NativeMethods.IsKeyDown(VkEscape))
        {
            EndTileDrag(cancel: true);
            return;
        }
        var p = e.GetPosition(this);
        if (!_dragMoved && (p - _dragStart).Length < 4) return;
        _dragMoved = true;
        // Las demás se apartan al momento: la tarjeta ocupa el puesto de la que tiene debajo.
        if (TileAt(p) is not { } over || ReferenceEquals(over, tile)) return;
        var from = Model.Tiles.IndexOf(tile);
        var to = Model.Tiles.IndexOf(over);
        if (from >= 0 && to >= 0) Model.Tiles.Move(from, to);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragTile is not null) EndTileDrag(cancel: NativeMethods.IsKeyDown(VkEscape));
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragTile is not null) EndTileDrag(cancel: false);
    }

    private void EndTileDrag(bool cancel)
    {
        if (_dragTile is not { } tile) return;
        _dragTile = null;
        tile.IsLifted = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        var index = Model.Tiles.IndexOf(tile);
        if (cancel)
        {
            if (index >= 0 && index != _dragStartIndex) Model.Tiles.Move(index, _dragStartIndex);
            return;
        }
        if (!_dragMoved) TileClicked?.Invoke(tile.MetricId);
        else if (index >= 0 && index != _dragStartIndex) TileMoved?.Invoke(tile.MetricId, index);
    }

    private OverlayTileViewModel? TileAt(Point p)
    {
        var hit = VisualTreeHelper.HitTest(this, p)?.VisualHit as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: OverlayTileViewModel tile }) return tile;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    public void Invalidate()
    {
        _lastPosition = (int.MinValue, int.MinValue);
        Reposition();
    }

    /// <summary>Donde está ahora: monitor, tamaño y esquina superior izquierda, en píxeles reales.</summary>
    public (OverlayPlacement.Screen Screen, int Width, int Height, int X, int Y, double Dpi)? Current { get; private set; }

    public static OverlayPlacement.Screen ScreenOf(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero) monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var b = ForegroundWindow.MonitorBounds(monitor, workArea: false);
        return new OverlayPlacement.Screen(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _layout is null || ActualWidth <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var screen = ScreenOf(_monitor);
        var (x, y) = OverlayPlacement.Compute(_layout, screen, w, h, dpi.DpiScaleX);
        Current = (screen, w, h, x, y, dpi.DpiScaleX);

        if ((x, y) == _lastPosition) return;
        _lastPosition = (x, y);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, x, y, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        base.OnClosed(e);
    }
}
