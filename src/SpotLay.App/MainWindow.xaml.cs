using System.Windows;
using System.Windows.Input;
using SpotLay.App.ViewModels;

namespace SpotLay.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow(MainViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        PreviewKeyDown += CaptureHotkey;
    }

    // Mientras Ajustes o Alertas esperan una tecla, la siguiente pulsacion (con sus modificadores) es el nuevo atajo.
    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        var settings = _model.Settings;
        var alerts = _model.Alerts;
        var home = _model.Home;
        if (!settings.IsCapturingHotkey && !alerts.IsCapturingHotkey && !home.IsCapturingHotkey) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 0x1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 0x2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 0x4;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 0x8;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (settings.IsCapturingHotkey) settings.OnHotkeyCaptured(vk, modifiers);
        else if (alerts.IsCapturingHotkey) alerts.OnHotkeyCaptured(vk, modifiers);
        else home.OnHotkeyCaptured(vk, modifiers);
        e.Handled = true;
    }
}
