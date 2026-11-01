using System.Windows;
using Forms = System.Windows.Forms;
using SpotLay.Core.Localization;

namespace SpotLay.App.Infrastructure;

/// <summary>Icono en la bandeja: abrir panel, mostrar/ocultar overlay, cancelar un apagado y salir.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Forms.ToolStripMenuItem _cancelShutdownItem;

    public TrayIcon(Action open, Action toggleOverlay, Action cancelShutdown, Action exit,
        Func<IReadOnlyList<string>> presetNames, Func<int> activePreset, Action<int> activatePreset)
    {
        // Submenú "Diseño": los 5 diseños, marcado el que está en uso.
        var presets = new Forms.ToolStripMenuItem(Tr.T("Diseño"));
        presets.DropDownOpening += (_, _) =>
        {
            presets.DropDownItems.Clear();
            var names = presetNames();
            for (var i = 0; i < names.Count; i++)
            {
                var index = i;
                presets.DropDownItems.Add(new Forms.ToolStripMenuItem($"{i + 1}. {names[i]}", null, (_, _) => activatePreset(index))
                {
                    Checked = index == activePreset()
                });
            }
        };
        presets.DropDownItems.Add("…");
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SpotLay.ico"))!.Stream;
        _toggleItem = new Forms.ToolStripMenuItem(Tr.T("Ocultar overlay"), null, (_, _) => toggleOverlay());
        _cancelShutdownItem = new Forms.ToolStripMenuItem(Tr.T("Cancelar apagado de emergencia"), null, (_, _) => cancelShutdown())
        {
            Visible = false,
            ForeColor = System.Drawing.Color.Firebrick
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_cancelShutdownItem);
        menu.Items.Add(new Forms.ToolStripMenuItem(Tr.T("Abrir SpotLay"), null, (_, _) => open()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_toggleItem);
        menu.Items.Add(presets);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Tr.T("Salir"), null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "SpotLay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void SetShutdownPending(bool pending) => _cancelShutdownItem.Visible = pending;

    public void SetOverlayVisible(bool visible) => _toggleItem.Text = visible ? Tr.T("Ocultar overlay") : Tr.T("Mostrar overlay");

    public void Notify(string title, string text) => _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
