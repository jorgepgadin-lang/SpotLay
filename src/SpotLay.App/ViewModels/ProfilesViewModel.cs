using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using SpotLay.App.Infrastructure;
using SpotLay.App.Services;
using SpotLay.Core.Config;
using SpotLay.Core.Localization;

namespace SpotLay.App.ViewModels;

public sealed class ProfileItem : ObservableObject
{
    private readonly AppController _c;

    private readonly Func<string, IReadOnlyList<Option>> _targets;

    public ProfileItem(AppController controller, string key, bool isGlobal, Action<string> navigate, Action refresh,
        Func<string, IReadOnlyList<Option>> targets)
    {
        _c = controller;
        _targets = targets;
        CopyToCommand = new RelayCommand(p =>
        {
            if (p is not Option target) return;
            var replaces = target.Key == AppController.GlobalTarget || _c.Config.Profiles.ContainsKey(target.Key);
            if (replaces && System.Windows.MessageBox.Show(
                    Tr.F("¿Sustituir el diseño de «{0}» por una copia de «{1}»?", target.Label, Name), Tr.T("Copiar diseño"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;
            _c.CopyLayout(Key, target.Key);
            refresh();
        });
        Key = key;
        IsGlobal = isGlobal;
        EditCommand = new RelayCommand(() =>
        {
            _c.EditingTarget = Key;
            navigate("overlay");
        });
        DeleteCommand = new RelayCommand(() =>
        {
            _c.DeleteProfile(Key);
            refresh();
        }, () => !IsGlobal);
        ResetCommand = new RelayCommand(() => _c.ResetProfileToGlobal(Key), () => !IsGlobal);
        ExportCommand = new RelayCommand(Export);
    }

    public string Key { get; }
    public bool IsGlobal { get; }
    public string Name => IsGlobal ? Tr.F("Diseño global en uso: {0}", _c.Config.Presets[_c.Config.ActivePreset].Name) : Key;
    public string Detail
    {
        get
        {
            var layout = IsGlobal ? _c.Config.Global : _c.Config.Profiles[Key].Layout;
            var what = IsGlobal ? Tr.T("Para todos los juegos sin perfil propio") : Tr.F("Solo en {0}.exe", Key);
            return Tr.F("{0} · {1} métricas", what, layout.Metrics.Count);
        }
    }

    public bool Enabled
    {
        get => IsGlobal || _c.Config.Profiles.TryGetValue(Key, out var p) && p.Enabled;
        set
        {
            if (IsGlobal || !_c.Config.Profiles.TryGetValue(Key, out var p)) return;
            p.Enabled = value;
            _c.NotifySettingsChanged();
            OnPropertyChanged();
        }
    }

    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CopyToCommand { get; }
    public ICommand ExportCommand { get; }

    /// <summary>Guarda este diseño en un archivo .spotlay.json (para pasarlo o hacer copia).</summary>
    private void Export()
    {
        var layout = IsGlobal ? _c.Config.Global : _c.Config.Profiles[Key].Layout;
        var name = IsGlobal ? _c.Config.Presets[_c.Config.ActivePreset].Name : Key;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{name}.spotlay.json",
            Filter = Tr.T("Diseño de SpotLay (*.spotlay.json)|*.spotlay.json")
        };
        if (dialog.ShowDialog() != true) return;
        LayoutFile.Save(dialog.FileName, name, layout);
    }

    /// <summary>Destinos posibles (se recalculan al abrir el menú).</summary>
    public IReadOnlyList<Option> CopyTargets => _targets(Key);

    public void RefreshTargets() => OnPropertyChanged(nameof(CopyTargets));
}

public sealed class ProfilesViewModel : PageViewModel
{
    private readonly AppController _c;
    private readonly Action<string> _navigate;
    private string _currentGame = "";

    public ProfilesViewModel(AppController controller, Action<string> navigate)
        : base(Tr.T("Perfiles"), Tr.T("Un diseño distinto para cada juego. Si un juego no tiene perfil, se usa el global."))
    {
        _c = controller;
        _navigate = navigate;
        CreateForCurrentGameCommand = new RelayCommand(() =>
        {
            if (_c.CreateProfileForCurrentGame() is null) return;
            Rebuild();
            navigate("overlay");
        }, () => _c.Snapshot.Game.IsGame && _c.Config.ProfileFor(_c.Snapshot.Game.ExeName) is null);

        SetupExtras();
        controller.LayoutChanged += Rebuild;
        controller.SnapshotUpdated += s =>
        {
            CurrentGame = s.Game.IsGame ? SpotLayConfig.ProfileKey(s.Game.ExeName) : "";
        };
        Rebuild();
    }

    public ObservableCollection<ProfileItem> Items { get; } = [];
    public ICollectionView View { get; private set; } = null!;
    public ObservableCollection<CleanupItem> Cleanup { get; } = [];
    private string _search = "";
    private bool _showCleanup;
    private string _message = "";
    private OverlayLayout? _imported;

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) View.Refresh();
        }
    }

    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool ShowCleanup { get => _showCleanup; private set => Set(ref _showCleanup, value); }
    public ICommand ToggleCleanupCommand { get; private set; } = null!;
    public ICommand DeleteCheckedCommand { get; private set; } = null!;
    public ICommand ImportCommand { get; private set; } = null!;
    public ICommand ImportToCommand { get; private set; } = null!;

    /// <summary>Destinos para un diseño importado (se piden al abrir el menú).</summary>
    public IReadOnlyList<Option> ImportTargets => TargetsFor("");

    /// <summary>Elige y lee el archivo. true si hay un diseño listo para colocar.</summary>
    public bool PickImportFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Tr.T("Diseño de SpotLay (*.spotlay.json)|*.spotlay.json|Todos (*.*)|*.*") };
        if (dialog.ShowDialog() != true) return false;
        _imported = LayoutFile.Load(dialog.FileName, out var error);
        Message = _imported is null ? Tr.T("No se pudo leer: ") + error : Tr.T("Diseño leído. Elige dónde ponerlo.");
        OnPropertyChanged(nameof(ImportTargets));
        return _imported is not null;
    }

    private void SetupExtras()
    {
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is ProfileItem p && (p.IsGlobal || string.IsNullOrWhiteSpace(_search) ||
                                                  p.Key.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));
        ToggleCleanupCommand = new RelayCommand(() =>
        {
            ShowCleanup = !ShowCleanup;
            if (!ShowCleanup) return;
            Cleanup.Clear();
            foreach (var (key, profile) in _c.Config.Profiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                if (profile.LastSeenAsGame is null) Cleanup.Add(new CleanupItem(key));
        });
        DeleteCheckedCommand = new RelayCommand(() =>
        {
            var keys = Cleanup.Where(c => c.IsChecked).Select(c => c.Key).ToList();
            if (keys.Count == 0) return;
            if (System.Windows.MessageBox.Show(Tr.F("¿Borrar {0} perfil(es)? No se puede deshacer.", keys.Count), Tr.T("Quitar perfiles"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
            foreach (var key in keys) _c.DeleteProfile(key);
            Message = Tr.F("{0} perfil(es) borrado(s).", keys.Count);
            ShowCleanup = false;
            Rebuild();
        });
        ImportCommand = new RelayCommand(() => PickImportFile());
        ImportToCommand = new RelayCommand(p =>
        {
            if (_imported is null || p is not Option target) return;
            var replaces = target.Key == AppController.GlobalTarget || AppController.PresetIndex(target.Key) is not null || _c.Config.Profiles.ContainsKey(target.Key);
            if (replaces && System.Windows.MessageBox.Show(Tr.F("¿Sustituir el diseño de «{0}» por el importado?", target.Label), Tr.T("Importar diseño"),
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
            _c.PutLayout(_imported.Clone(), target.Key);
            Message = Tr.F("Diseño importado en «{0}».", target.Label);
            _imported = null;
            Rebuild();
        });
    }
    public ICommand CreateForCurrentGameCommand { get; }

    public string CurrentGame
    {
        get => _currentGame;
        private set
        {
            if (Set(ref _currentGame, value)) OnPropertyChanged(nameof(CreateText));
        }
    }

    public string CreateText => string.IsNullOrEmpty(CurrentGame)
        ? Tr.T("Abre un juego para crear su perfil")
        : _c.Config.ProfileFor(CurrentGame) is null ? Tr.F("Crear perfil para {0}", CurrentGame) : Tr.F("{0} ya tiene perfil", CurrentGame);

    private IReadOnlyList<Option> TargetsFor(string source)
    {
        var list = new List<Option>();
        for (var i = 0; i < _c.Config.Presets.Count; i++)
        {
            var key = $"preset:{i}";
            if (key == source || (source == AppController.GlobalTarget && i == _c.Config.ActivePreset)) continue;
            list.Add(new Option(key, Tr.F("Diseño {0}: {1}{2}", i + 1, _c.Config.Presets[i].Name, (i == _c.Config.ActivePreset ? Tr.T(" (en uso)") : ""))));
        }
        if (!string.IsNullOrEmpty(CurrentGame) && _c.Config.ProfileFor(CurrentGame) is null)
            list.Add(new Option(CurrentGame, Tr.F("{0} (juego abierto, perfil nuevo)", CurrentGame)));
        foreach (var key in _c.Config.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
            if (!key.Equals(source, StringComparison.OrdinalIgnoreCase)) list.Add(new Option(key, key));
        return list;
    }

    private void Rebuild()
    {
        Items.Clear();
        Items.Add(new ProfileItem(_c, AppController.GlobalTarget, true, _navigate, Rebuild, TargetsFor));
        foreach (var key in _c.Config.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
            Items.Add(new ProfileItem(_c, key, false, _navigate, Rebuild, TargetsFor));
        OnPropertyChanged(nameof(CreateText));
    }
}

public sealed class CleanupItem(string key) : ObservableObject
{
    private bool _isChecked;
    public string Key { get; } = key;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Archivo de un diseño: {"kind":"SpotLay.Layout","version":1,"name":…,"layout":…}.</summary>
public static class LayoutFile
{
    private sealed class Envelope
    {
        public string Kind { get; set; } = "SpotLay.Layout";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public OverlayLayout? Layout { get; set; }
    }

    public static void Save(string path, string name, OverlayLayout layout) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new Envelope { Name = name, Layout = layout }, ConfigJson.Options));

    public static OverlayLayout? Load(string path, out string error)
    {
        error = "";
        try
        {
            var env = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), ConfigJson.Options);
            if (env?.Kind != "SpotLay.Layout" || env.Layout is null) { error = Tr.T("no es un diseño de SpotLay"); return null; }
            env.Layout.Styles = new Dictionary<string, MetricStyle>(env.Layout.Styles ?? [], StringComparer.OrdinalIgnoreCase);
            env.Layout.Metrics ??= [];
            return env.Layout;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
