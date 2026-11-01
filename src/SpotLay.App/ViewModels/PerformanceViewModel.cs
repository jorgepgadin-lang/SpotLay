using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Services;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Sessions;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

/// <summary>Una partida en el historial.</summary>
public sealed class SessionItem(SessionRecord record)
{
    private static readonly CultureInfo Es = Tr.Culture;
    public SessionRecord Record { get; } = record;
    public string Game => Record.Exe;
    public string When => Record.Start.ToString(Tr.T("dd/MM HH:mm"), Es);
    public string Duration => Minutes(Record.DurationSeconds);
    public string Avg => Record.AvgFps.ToString("F0", Es);
    public string Low1 => Record.Low1?.ToString("F0", Es) ?? "—";

    public static string Minutes(int seconds) => seconds >= 3600 ? Tr.F("{0} h {1} min", seconds / 3600, seconds % 3600 / 60) : Tr.F("{0} min", seconds / 60);
}

/// <summary>Casilla de una curva de la gráfica de hardware.</summary>
public sealed class SeriesToggle(Views.HardwareSeries series, Action changed) : ObservableObject
{
    private bool _isChecked;
    private bool _isAvailable = true;
    private bool _isAllowed = true;
    public Views.HardwareSeries Series { get; } = series;
    public string Label => Series.Label;
    public System.Windows.Media.Brush Brush { get; } = new System.Windows.Media.SolidColorBrush(series.Color);
    public bool Dashed => Series.Dashed;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!Set(ref _isChecked, value)) return;
            OnPropertiesChanged(nameof(IsEnabled), nameof(Hint));
            changed();
        }
    }
    /// <summary>La partida elegida tiene datos de esta curva.</summary>
    public bool IsAvailable { get => _isAvailable; set { if (Set(ref _isAvailable, value)) OnPropertiesChanged(nameof(IsEnabled), nameof(Hint)); } }
    /// <summary>Cabe en las dos escalas (izquierda y derecha) que ya hay en uso.</summary>
    public bool IsAllowed { get => _isAllowed; set { if (Set(ref _isAllowed, value)) OnPropertiesChanged(nameof(IsEnabled), nameof(Hint)); } }
    public bool IsEnabled => IsAvailable && (IsAllowed || IsChecked);
    public string Hint => !IsAvailable ? Tr.T("Sin datos en esta partida")
        : !IsAllowed && !IsChecked ? Tr.T("Máximo 2 unidades (una por escala): desmarca una")
        : Tr.T("Pulsa para mostrar u ocultar esta curva");
    internal void Load(bool value) { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
}

/// <summary>
/// Pestaña Rendimiento: resumen de cada partida (se graba sola), su gráfica
/// entera con los tirones marcados, el historial y los ajustes de la gráfica del juego.
/// </summary>
public sealed class PerformanceViewModel : PageViewModel
{
    private static readonly CultureInfo Es = Tr.Culture;
    private readonly AppController _c;
    private SessionItem? _selected;
    private SessionRecord? _full;
    private string _totals = "";
    private string _recording = "";

    public PerformanceViewModel(AppController controller, SettingsViewModel settings)
        : base(Tr.T("Rendimiento"), Tr.T("Cómo fue cada partida: FPS, 1% low, tirones y temperaturas. Se graba sola."))
    {
        _c = controller;
        Settings = settings;
        OpenFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.SessionsDirectory);
            Process.Start(new ProcessStartInfo(AppPaths.SessionsDirectory) { UseShellExecute = true });
        });
        DeleteAllCommand = new RelayCommand(() =>
        {
            if (System.Windows.MessageBox.Show(Tr.T("¿Borrar todas las partidas guardadas?"), Tr.T("Historial"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
            SessionStore.DeleteAll();
            Reload();
        });
        foreach (var serie in Views.HardwareSeries.All)
        {
            var toggle = new SeriesToggle(serie, OnSeriesChanged);
            SeriesToggles.Add(toggle);
        }
        // Como mucho dos unidades (izquierda y derecha); lo que sobre de una config vieja se desmarca.
        var keptUnits = new List<string>();
        var kept = new List<string>();
        foreach (var id in _c.Config.PerformanceSeries)
        {
            if (SeriesToggles.FirstOrDefault(t => t.Series.Id == id) is not { } t || kept.Contains(id)) continue;
            if (!keptUnits.Contains(t.Series.Unit))
            {
                if (keptUnits.Count == 2) continue;
                keptUnits.Add(t.Series.Unit);
            }
            kept.Add(id);
            t.Load(true);
        }
        _c.Config.PerformanceSeries = kept;
        FullScreenCommand = new RelayCommand(() => new Views.PerformanceFullWindow(this).Show());
        controller.Engine.Sessions.Saved += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Reload);
        controller.SnapshotUpdated += _ =>
        {
            var r = _c.Engine.Sessions.Recording;
            Recording = r is { } x ? Tr.F("● Grabando {0} · {1}", x.Exe, SessionItem.Minutes(x.Seconds)) + (x.Seconds < 60 ? Tr.T(" (se guarda a partir de 1 min)") : "") : "";
        };
        Reload();
    }

    public SettingsViewModel Settings { get; }
    public ObservableCollection<SeriesToggle> SeriesToggles { get; } = [];
    public ICommand FullScreenCommand { get; }
    private IReadOnlyList<Views.HardwareSeries> _selectedSeries = [];
    /// <summary>Curvas marcadas que tienen datos en la partida elegida.</summary>
    public IReadOnlyList<Views.HardwareSeries> SelectedSeries { get => _selectedSeries; private set => Set(ref _selectedSeries, value); }
    private int _hoverIndex = -1;
    /// <summary>Segundo señalado con el ratón, el mismo en las dos gráficas.</summary>
    public int HoverIndex { get => _hoverIndex; set => Set(ref _hoverIndex, value); }

    private void OnSeriesChanged()
    {
        // En el orden en que se marcan: la primera curva decide la escala de la izquierda.
        var order = _c.Config.PerformanceSeries.Where(id => SeriesToggles.Any(t => t.Series.Id == id && t.IsChecked)).ToList();
        order.AddRange(SeriesToggles.Where(t => t.IsChecked && !order.Contains(t.Series.Id)).Select(t => t.Series.Id));
        _c.Config.PerformanceSeries = order;
        _c.ScheduleSave();
        RefreshSeries();
    }

    private void RefreshSeries()
    {
        var full = Full;
        foreach (var t in SeriesToggles) t.IsAvailable = full is null || t.Series.HasData(full);
        var selected = _c.Config.PerformanceSeries
            .Select(id => SeriesToggles.FirstOrDefault(t => t.Series.Id == id))
            .Where(t => t is { IsChecked: true, IsAvailable: true })
            .Select(t => t!.Series).ToList();
        var units = selected.Select(x => x.Unit).Distinct().ToList();
        foreach (var t in SeriesToggles) t.IsAllowed = units.Count < 2 || units.Contains(t.Series.Unit);
        SelectedSeries = selected;
    }
    public ObservableCollection<SessionItem> Sessions { get; } = [];
    public ICommand OpenFolderCommand { get; }
    public ICommand DeleteAllCommand { get; }
    public bool HasSessions => Sessions.Count > 0;
    public string Totals { get => _totals; private set => Set(ref _totals, value); }
    public string Recording { get => _recording; private set => Set(ref _recording, value); }

    public string Limit
    {
        get => _c.Config.SessionLimit.ToString();
        set
        {
            if (int.TryParse(value?.Trim(), out var n))
            {
                _c.Config.SessionLimit = Math.Clamp(n, 5, 1000);
                _c.Engine.Sessions.Limit = _c.Config.SessionLimit;
                SessionStore.Trim(_c.Config.SessionLimit);
                _c.ScheduleSave();
                Reload();
            }
            OnPropertyChanged();
        }
    }

    public SessionItem? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Full = value is null ? null : SessionStore.Load(value.Record.File);
        }
    }

    /// <summary>La partida elegida con todos sus segundos (para la gráfica).</summary>
    public SessionRecord? Full
    {
        get => _full;
        private set
        {
            if (!Set(ref _full, value)) return;
            HoverIndex = -1;
            RefreshSeries();
            OnPropertiesChanged(nameof(Title2), nameof(Subtitle2), nameof(Avg), nameof(Low1), nameof(Low01), nameof(Low1Real), nameof(Low01Real),
                nameof(HasReal), nameof(Temps), nameof(Power), nameof(SpikesText));
        }
    }

    public string Title2 => Full is { } s ? s.Exe : Tr.T("Todavía no hay partidas");
    public string Subtitle2 => Full is { } s ? $"{s.Start.ToString(Tr.T("dddd d 'de' MMMM, HH:mm"), Es)} · {SessionItem.Minutes(s.DurationSeconds)} · {s.Api}" : Tr.T("Juega al menos 1 minuto y aquí verás cómo fue.");
    public string Avg => F(Full?.AvgFps);
    public string Low1 => F(Full?.Low1);
    public string Low01 => F(Full?.Low01);
    public string Low1Real => F(Full?.Low1Real);
    public string Low01Real => F(Full?.Low01Real);
    public bool HasReal => Full?.Low1Real is not null;
    public string Temps => Full is { } s ? Tr.F("GPU máx. {0:F0} °C · hot spot {1:F0} °C · CPU máx. {2:F0} °C", s.MaxGpuTemp, s.MaxHotSpot, s.MaxCpuTemp) : "";
    public string Power => Full is { } s ? Tr.F("GPU media {0:F0} W · {1:F0} MHz", s.AvgGpuPower, s.AvgGpuClock) : "";
    public string SpikesText => Full is { } s
        ? s.Spikes.Count == 0 ? Tr.T("Sin tirones destacables.") : Tr.T("Peores tirones: ") + string.Join("   ", s.Spikes.OrderByDescending(x => x.Ms).Take(5).Select(x => Tr.F("{0} · {1:F0} ms", SessionChart2.Clock(x.T), x.Ms)))
        : "";

    private void Reload()
    {
        var keep = _selected?.Record.File;
        Sessions.Clear();
        foreach (var s in SessionStore.List()) Sessions.Add(new SessionItem(s));
        var (count, bytes) = SessionStore.Totals();
        Totals = Tr.F("{0} partida(s) · {1}", count, Size(bytes));
        OnPropertyChanged(nameof(HasSessions));
        Selected = Sessions.FirstOrDefault(x => x.Record.File == keep) ?? Sessions.FirstOrDefault();
        if (Selected is not null && Full is null) Full = SessionStore.Load(Selected.Record.File);
    }

    private static string Size(long bytes) => bytes < 1024 * 1024 ? Tr.F("{0:F0} KB", bytes / 1024.0) : Tr.F("{0:F1} MB", bytes / 1024.0 / 1024.0);
    private static string F(double? v) => v is { } x ? x.ToString("F0", Es) : "—";
}

/// <summary>Formato de reloj compartido con la gráfica.</summary>
internal static class SessionChart2
{
    public static string Clock(int s) => Views.SessionChart.Clock(s);
}
