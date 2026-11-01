using SpotLay.Core.Metrics;
using System.Text.Json;
using SpotLay.Core.Diagnostics;

namespace SpotLay.Core.Config;

/// <summary>Carga y guarda config.json. Escritura atomica: nunca deja un fichero a medias.</summary>
public sealed class ConfigStore
{
    private readonly object _gate = new();

    public bool ExistedOnStartup { get; private set; }

    public SpotLayConfig Load()
    {
        lock (_gate)
        {
            ExistedOnStartup = File.Exists(AppPaths.ConfigFile);
            if (!ExistedOnStartup) return Normalize(new SpotLayConfig());
            try
            {
                var config = JsonSerializer.Deserialize<SpotLayConfig>(File.ReadAllText(AppPaths.ConfigFile), ConfigJson.Options);
                return Normalize(config ?? new SpotLayConfig());
            }
            catch (Exception ex)
            {
                Log.Error("config.json ilegible; se guarda una copia y se empieza con valores por defecto", ex);
                Backup("ilegible");
                return Normalize(new SpotLayConfig());
            }
        }
    }

    public void Save(SpotLayConfig config)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.ConfigFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(config, ConfigJson.Options));
            File.Move(temp, AppPaths.ConfigFile, overwrite: true);
        }
    }

    public void Backup(string reason)
    {
        if (!File.Exists(AppPaths.ConfigFile)) return;
        Directory.CreateDirectory(AppPaths.BackupsDirectory);
        var target = Path.Combine(AppPaths.BackupsDirectory, $"config-{DateTime.Now:yyyyMMdd-HHmmss}-{reason}.json");
        File.Copy(AppPaths.ConfigFile, target, overwrite: true);
    }

    private static SpotLayConfig Normalize(SpotLayConfig config)
    {
        // Diccionarios deserializados pierden el comparador sin distincion de mayusculas.
        config.Profiles = new Dictionary<string, GameProfile>(config.Profiles, StringComparer.OrdinalIgnoreCase);
        NormalizeLayout(config.Global);
        foreach (var profile in config.Profiles.Values) NormalizeLayout(profile.Layout);
        NormalizePresets(config);
        config.Safety ??= new SafetyConfig();
        config.Cooling ??= new CoolingConfig();
        SafetyLimits.Normalize(config.Safety);
        SafetyLimits.Normalize(config.Cooling);
        if (config.Mode is not (AppModes.Esencial or AppModes.Avanzado)) config.Mode = AppModes.Esencial;
        // v2: F8 a secas pasa a Mayús + F8 (decisión del usuario, 25/09/2026).
        if (config.SchemaVersion < 2 && config.HotkeyKey == 0x77 && config.HotkeyModifiers == 0) config.HotkeyModifiers = 0x4;
        // v3: Ctrl + Alt + número (= AltGr + número, la @ en teclado español) pasa a Ctrl + Mayús + número,
        // solo si siguen siendo las teclas de fábrica (27/09/2026).
        if (config.SchemaVersion < 3)
        {
            for (var i = 0; i < config.Presets.Count; i++)
                if (config.Presets[i].HotkeyKey == 0x31 + i && config.Presets[i].HotkeyModifiers == 0x3) config.Presets[i].HotkeyModifiers = 0x6;
            if (config.GameLayoutHotkeyKey == 0x30 && config.GameLayoutHotkeyModifiers == 0x3) config.GameLayoutHotkeyModifiers = 0x6;
        }
        config.Graph ??= new FrameGraphConfig();
        config.Graph.Height = Math.Clamp(config.Graph.Height, 60, 600);
        config.Graph.WidthPercent = Math.Clamp(config.Graph.WidthPercent, 20, 100);
        config.Graph.Opacity = Math.Clamp(config.Graph.Opacity, 0, 100);
        config.Graph.Position = config.Graph.Position == "top" ? "top" : "bottom";
        config.FrameStatsSeconds = Math.Clamp(config.FrameStatsSeconds, 5, 60);
        config.PerformanceSeries ??= ["gpu-temp", "cpu-temp"];
        config.SessionLimit = Math.Clamp(config.SessionLimit <= 0 ? 50 : config.SessionLimit, 5, 1000);
        config.Graph.RefreshMs = Math.Clamp(config.Graph.RefreshMs <= 0 ? 500 : config.Graph.RefreshMs, 100, 2000);
        config.SchemaVersion = SpotLayConfig.CurrentSchemaVersion;
        return config;
    }

    /// <summary>
    /// Siempre 5 diseños. El global que ya existía pasa a ser el diseño 1; el
    /// activo comparte objeto con Global (Global manda si difieren al cargar).
    /// </summary>
    public static void NormalizePresets(SpotLayConfig config)
    {
        config.Presets ??= [];
        string[] names = ["Completo", "Mínimo", "Solo FPS", "Diseño 4", "Diseño 5"];
        while (config.Presets.Count < SpotLayConfig.PresetCount)
        {
            var i = config.Presets.Count;
            config.Presets.Add(new OverlayPreset
            {
                Name = names[i],
                Layout = i == 0 ? config.Global : DefaultPreset(i, config.Global),
                HotkeyKey = 0x31 + i
            });
        }
        if (config.Presets.Count > SpotLayConfig.PresetCount) config.Presets.RemoveRange(SpotLayConfig.PresetCount, config.Presets.Count - SpotLayConfig.PresetCount);
        config.ActivePreset = Math.Clamp(config.ActivePreset, 0, SpotLayConfig.PresetCount - 1);
        foreach (var preset in config.Presets)
        {
            preset.Layout ??= new OverlayLayout();
            preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? "Diseño" : preset.Name.Trim();
            preset.HotkeyKey = Math.Clamp(preset.HotkeyKey, 0, 254);
            preset.HotkeyModifiers &= 0xF;
            NormalizeLayout(preset.Layout);
        }
        config.Presets[config.ActivePreset].Layout = config.Global;
    }

    private static OverlayLayout DefaultPreset(int index, OverlayLayout basedOn)
    {
        var layout = basedOn.Clone();
        layout.Metrics = index switch
        {
            1 => [KnownMetrics.Fps, KnownMetrics.GpuTemp, KnownMetrics.CpuTemp],
            2 => [KnownMetrics.Fps],
            _ => layout.Metrics
        };
        return layout;
    }

    private static void NormalizeLayout(OverlayLayout layout)
    {
        layout.Styles = new Dictionary<string, MetricStyle>(layout.Styles, StringComparer.OrdinalIgnoreCase);
        layout.Metrics = layout.Metrics.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        layout.Scale = Math.Clamp(layout.Scale, 0.6, 2.0);
        layout.Columns = Math.Clamp(layout.Columns, 1, 12);
        layout.BackgroundOpacity = Math.Clamp(layout.BackgroundOpacity, 0, 100);
        layout.TileWidth = Math.Clamp(layout.TileWidth, 70, 400);
        layout.AnchorX = Math.Clamp(layout.AnchorX, 0, 2);
        layout.AnchorY = Math.Clamp(layout.AnchorY, 0, 2);
    }
}
