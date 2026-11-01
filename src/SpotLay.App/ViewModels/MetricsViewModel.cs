using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Overlay;
using SpotLay.App.Services;
using SpotLay.Core;
using SpotLay.Core.Config;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

public sealed class MetricItem(string id, string label, string device, string group, bool isKnown, string? sensorType, Func<string, bool> isSelected, Action<string, bool> toggle)
    : ObservableObject
{
    private string _value = "—";

    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Device { get; } = device;
    public string Group { get; } = group;
    public bool IsKnown { get; } = isKnown;
    public string? SensorType { get; } = sensorType;
    public string Value { get => _value; set => Set(ref _value, value); }

    public bool IsSelected
    {
        get => isSelected(Id);
        set => toggle(Id, value);
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

public sealed class MetricsViewModel : PageViewModel
{
    public override bool HasModes => true;

    private readonly AppController _c;
    private readonly Dictionary<string, MetricItem> _byId = new(StringComparer.OrdinalIgnoreCase);
    private string _search = "";
    private string _filter = "todos";
    private DateTime _lastValues;
    private int _knownSensorCount = -1;

    public MetricsViewModel(AppController controller)
        : base(Tr.T("Métricas"), Tr.T("Elige qué datos salen en el overlay. Se añaden al diseño que estás editando."))
    {
        _c = controller;
        View = CollectionViewSource.GetDefaultView(Items);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MetricItem.Group)));
        View.Filter = o => o is MetricItem m && Matches(m);

        Filters =
        [
            new("todos", Tr.T("Todos")), new("seleccionados", Tr.T("En el overlay")), new("temperaturas", Tr.T("Temperaturas")),
            new("juego", Tr.T("Juego")), new("gpu", "GPU"), new("cpu", "CPU"), new("ventiladores", Tr.T("Ventiladores")),
            new("placa", Tr.T("Placa")), new("memoria", Tr.T("Memoria")), new("discos", Tr.T("Discos")), new("red", Tr.T("Red"))
        ];
        SetFilterCommand = new RelayCommand(p =>
        {
            if (p is string key) Filter = key;
        });

        controller.SnapshotUpdated += OnSnapshot;
        controller.LayoutChanged += () =>
        {
            foreach (var item in Items) item.Refresh();
            OnPropertiesChanged(nameof(EditingName), nameof(SelectedCount));
            if (_filter == "seleccionados") View.Refresh();
        };
        controller.SettingsChanged += () =>
        {
            View.Refresh();
            OnPropertiesChanged(nameof(IsAdvanced), nameof(HiddenHint));
        };
    }

    public ObservableCollection<MetricItem> Items { get; } = [];
    public ICollectionView View { get; }
    public IReadOnlyList<Option> Filters { get; }
    public ICommand SetFilterCommand { get; }
    public bool IsAdvanced => _c.Config.Mode == AppModes.Avanzado;
    public string EditingName => _c.EditingName;
    public int SelectedCount => _c.EditingLayout.Metrics.Count;

    public string HiddenHint => IsAdvanced
        ? ""
        : Tr.F("Modo Esencial: se muestran las métricas principales. Cambia a Avanzado para ver los {0} sensores del equipo.", Math.Max(0, Items.Count(i => !i.IsKnown)));

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) View.Refresh();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) View.Refresh();
        }
    }

    private void OnSnapshot(EngineSnapshot s)
    {
        var sensors = s.Metrics.Metrics.Count;
        if (sensors != _knownSensorCount)
        {
            _knownSensorCount = sensors;
            AddMissing(s);
        }
        if (DateTime.UtcNow - _lastValues < TimeSpan.FromSeconds(1)) return;
        _lastValues = DateTime.UtcNow;
        foreach (var item in Items)
        {
            var m = s.Metrics.Get(item.Id);
            item.Value = m is null ? Tr.T("sin lectura") : $"{MetricFormat.Value(m)} {m.Unit}".Trim();
        }
    }

    private void AddMissing(EngineSnapshot s)
    {
        var added = false;
        foreach (var d in KnownMetrics.All)
            added |= Add(d.Id, d.Label, d.Description, GroupName(d.Category), true, null);
        foreach (var m in s.Metrics.Metrics.Values.Where(m => m.Id.StartsWith(KnownMetrics.HardwarePrefix, StringComparison.Ordinal)))
            added |= Add(m.Id, m.Label, m.Device, $"{GroupName(m.Category)} · {m.Device}", false, m.SensorType);
        // Metricas del diseño que ahora no tienen lectura (p. ej. un sensor que ya no existe).
        foreach (var id in _c.EditingLayout.Metrics)
            added |= Add(id, id, Tr.T("Sin lectura ahora mismo"), Tr.T("Otros"), false, null);
        if (added) OnPropertyChanged(nameof(HiddenHint));
    }

    private bool Add(string id, string label, string device, string group, bool known, string? sensorType)
    {
        if (_byId.ContainsKey(id)) return false;
        var item = new MetricItem(id, label, device, group, known, sensorType, IsSelected, Toggle);
        _byId[id] = item;
        Items.Add(item);
        return true;
    }

    private bool IsSelected(string id) => _c.EditingLayout.Metrics.Contains(id, StringComparer.OrdinalIgnoreCase);

    private void Toggle(string id, bool on)
    {
        var layout = _c.EditingLayout;
        if (on && !IsSelected(id)) layout.Metrics.Add(id);
        else if (!on) layout.Metrics.RemoveAll(m => m.Equals(id, StringComparison.OrdinalIgnoreCase));
        _c.NotifyLayoutEdited();
    }

    private bool Matches(MetricItem m)
    {
        if (!IsAdvanced && !m.IsKnown) return false;
        if (!string.IsNullOrWhiteSpace(_search) &&
            !(m.Label.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
              m.Device.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
              m.Group.Contains(_search, StringComparison.OrdinalIgnoreCase)))
            return false;
        return _filter switch
        {
            "seleccionados" => m.IsSelected,
            "temperaturas" => m.SensorType == "Temperature" || m.Id.Contains("temp", StringComparison.OrdinalIgnoreCase) || m.Id == KnownMetrics.GpuHotSpot,
            "juego" => m.Group.StartsWith(Tr.T("Juego"), StringComparison.Ordinal),
            "gpu" => m.Group.StartsWith(Tr.T("Gráfica"), StringComparison.Ordinal),
            "cpu" => m.Group.StartsWith(Tr.T("Procesador"), StringComparison.Ordinal),
            "ventiladores" => m.SensorType is "Fan" or "Control" || m.Group.StartsWith(Tr.T("Ventiladores"), StringComparison.Ordinal),
            "placa" => m.Group.StartsWith(Tr.T("Placa"), StringComparison.Ordinal),
            "memoria" => m.Group.StartsWith(Tr.T("Memoria"), StringComparison.Ordinal),
            "discos" => m.Group.StartsWith(Tr.T("Discos"), StringComparison.Ordinal),
            "red" => m.Group.StartsWith(Tr.T("Red"), StringComparison.Ordinal),
            _ => true
        };
    }

    private static string GroupName(MetricCategory category) => category switch
    {
        MetricCategory.Juego => Tr.T("Juego"),
        MetricCategory.Gpu => Tr.T("Gráfica"),
        MetricCategory.Cpu => Tr.T("Procesador"),
        MetricCategory.Memoria => Tr.T("Memoria"),
        MetricCategory.Placa => Tr.T("Placa base"),
        MetricCategory.Almacenamiento => Tr.T("Discos"),
        MetricCategory.Red => Tr.T("Red"),
        MetricCategory.Ventiladores => Tr.T("Ventiladores"),
        _ => Tr.T("Otros")
    };
}
