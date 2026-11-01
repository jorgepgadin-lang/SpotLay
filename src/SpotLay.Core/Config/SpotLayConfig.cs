using System.Text.Json;
using SpotLay.Core.Metrics;

namespace SpotLay.Core.Config;

public static class AppModes
{
    public const string Esencial = "esencial";
    public const string Avanzado = "avanzado";
}

public static class OverlayTemplates
{
    public const string Columna = "columna";
    public const string Barra = "barra";
    public const string Compacta = "compacta";
}

public static class Visualizations
{
    public const string Number = "number";
    public const string Gauge = "gauge";
    public const string Ring = "ring";
    public const string BarHorizontal = "bar-horizontal";
    public const string BarVertical = "bar-vertical";
    public const string Line = "line";
}

/// <summary>Dónde va la unidad respecto a la cifra.</summary>
public static class UnitPositions
{
    /// <summary>Al lado si cabe; en arco y anillo pasa debajo si no cabe.</summary>
    public const string Auto = "auto";
    public const string Side = "side";
    public const string Below = "below";
    public const string Hidden = "hidden";
}

public static class OverlayPositions
{
    public const string TopLeft = "top-left";
    public const string TopCenter = "top-center";
    public const string TopRight = "top-right";
    public const string Center = "center";
    public const string BottomLeft = "bottom-left";
    public const string BottomCenter = "bottom-center";
    public const string BottomRight = "bottom-right";
    public const string Custom = "custom";
    /// <summary>Borde más cercano + distancia en píxeles (mini-monitor y flechas).</summary>
    public const string Anchored = "anchored";
}

public sealed class MetricStyle
{
    /// <summary>Vacio = etiqueta por defecto de la metrica.</summary>
    public string Label { get; set; } = "";
    public string Visualization { get; set; } = Visualizations.Number;
    /// <summary>Null = color de la paleta del diseño.</summary>
    public string? LabelColor { get; set; }
    public string? ValueColor { get; set; }
    public string? UnitColor { get; set; }
    public int LabelSize { get; set; } = 12;
    public int ValueSize { get; set; } = 20;
    public int UnitSize { get; set; } = 11;
    public string UnitPosition { get; set; } = UnitPositions.Auto;
    public bool AutoScale { get; set; } = true;
    public double Min { get; set; }
    public double Max { get; set; } = 100;
}

public sealed class OverlayLayout
{
    public string Template { get; set; } = OverlayTemplates.Columna;
    public int Columns { get; set; } = 2;
    /// <summary>Factor de tamaño: 0.6 a 2.0.</summary>
    public double Scale { get; set; } = 1.0;
    public int TileWidth { get; set; } = 130;
    public int Gap { get; set; } = 6;
    public string Position { get; set; } = OverlayPositions.TopLeft;
    public int OffsetX { get; set; } = 24;
    public int OffsetY { get; set; } = 24;
    /// <summary>Posicion libre como fraccion (0..1) del monitor.</summary>
    public double FreeX { get; set; } = 0.02;
    public double FreeY { get; set; } = 0.05;
    /// <summary>Modo anclado: 0 izquierda/arriba, 1 centro, 2 derecha/abajo.</summary>
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    /// <summary>Modo anclado: distancia en píxeles reales al borde (o al centro, con signo).</summary>
    public int AnchorOffsetX { get; set; } = 24;
    public int AnchorOffsetY { get; set; } = 24;
    public string BackgroundColor { get; set; } = "#0B0D12";
    public int BackgroundOpacity { get; set; } = 85;
    public int CornerRadius { get; set; } = 12;
    public string FontFamily { get; set; } = "Segoe UI Variable Display";
    public bool FontBold { get; set; } = true;
    public string ValueEffect { get; set; } = "none";
    public string LabelColor { get; set; } = "#A5B0B8";
    public string ValueColor { get; set; } = "#FFFFFF";
    public string UnitColor { get; set; } = "#A5B0B8";

    public List<string> Metrics { get; set; } =
    [
        KnownMetrics.FrameGen, KnownMetrics.Fps, KnownMetrics.FpsReal, KnownMetrics.FpsGenerated,
        KnownMetrics.GpuLoad, KnownMetrics.GpuTemp, KnownMetrics.GpuHotSpot, KnownMetrics.GpuPower,
        KnownMetrics.Vram, KnownMetrics.CpuLoad, KnownMetrics.CpuTemp, KnownMetrics.Ram
    ];

    public Dictionary<string, MetricStyle> Styles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public MetricStyle StyleFor(string metricId)
    {
        if (!Styles.TryGetValue(metricId, out var style)) Styles[metricId] = style = DefaultStyle(metricId);
        return style;
    }

    public static MetricStyle DefaultStyle(string metricId)
    {
        var definition = KnownMetrics.Find(metricId);
        return new MetricStyle { Min = definition?.Min ?? 0, Max = definition?.Max ?? 100 };
    }

    public OverlayLayout Clone() => ConfigJson.Clone(this);
}

public sealed class GameProfile
{
    public string ExeName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public OverlayLayout Layout { get; set; } = new();
    /// <summary>Última vez que SpotLay detectó este exe como juego (null = nunca desde SpotLay 12).</summary>
    public DateTime? LastSeenAsGame { get; set; }
}

/// <summary>Uno de los 5 diseños globales, con su tecla para activarlo en el juego.</summary>
public sealed class OverlayPreset
{
    public string Name { get; set; } = "";
    public OverlayLayout Layout { get; set; } = new();
    public int HotkeyKey { get; set; }
    /// <summary>
    /// Ctrl + Mayús por defecto (F1-F5 a secas los usan muchos juegos, y Ctrl + Alt + número
    /// es AltGr + número: bloqueaba la @, #, € … en teclados como el español).
    /// </summary>
    public int HotkeyModifiers { get; set; } = 0x6;
}

/// <summary>Gráfica de frametime: ventana propia a lo ancho del monitor (Mayús + F9).</summary>
public sealed class FrameGraphConfig
{
    public bool Visible { get; set; }
    public int HotkeyKey { get; set; } = 0x78;
    public int HotkeyModifiers { get; set; } = 0x4;
    /// <summary>Alto en píxeles de 100 % de escala.</summary>
    public int Height { get; set; } = 140;
    /// <summary>Ancho en % del monitor (centrada).</summary>
    public int WidthPercent { get; set; } = 100;
    /// <summary>top | bottom</summary>
    public string Position { get; set; } = "bottom";
    public int Opacity { get; set; } = 80;
    /// <summary>Cada cuántos ms se redibuja la gráfica (100-2000).</summary>
    public int RefreshMs { get; set; } = 500;
}

public sealed class SpotLayConfig
{
    public const int PresetCount = 5;

    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Mode { get; set; } = AppModes.Esencial;
    /// <summary>system | light | dark</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Diseño global en uso: siempre es el del diseño activo (Presets[ActivePreset]).</summary>
    public OverlayLayout Global { get; set; } = new();
    public List<OverlayPreset> Presets { get; set; } = [];
    public int ActivePreset { get; set; }
    /// <summary>Tecla para volver al diseño del juego tras usar un diseño global (Ctrl + Mayús + 0).</summary>
    public int GameLayoutHotkeyKey { get; set; } = 0x30;
    public int GameLayoutHotkeyModifiers { get; set; } = 0x6;
    /// <summary>Clave: nombre del ejecutable sin ".exe" (igual que SpotLay 10).</summary>
    public Dictionary<string, GameProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool OverlayEnabled { get; set; } = true;
    /// <summary>Ya se ha avisado de que cerrar la ventana deja SpotLay en la bandeja.</summary>
    public bool TrayHintShown { get; set; }
    /// <summary>Versión de SpotLay que creó por última vez la tarea de arranque (se rehace al cambiar).</summary>
    public string StartupTaskVersion { get; set; } = "";
    /// <summary>"es" o "en". Null = aún no elegido (se pregunta al arrancar).</summary>
    public string? Language { get; set; }
    /// <summary>Buscar e instalar versiones nuevas desde GitHub.</summary>
    public bool AutoUpdate { get; set; } = true;
    public bool ShowOnlyInGames { get; set; } = true;
    public bool HideGameMetricsWithoutGame { get; set; } = true;
    /// <summary>Tecla virtual para mostrar/ocultar el overlay (F8 por defecto, como SpotLay 10).</summary>
    public int HotkeyKey { get; set; } = 0x77;
    /// <summary>Mayús + F8 por defecto (F8 a secas lo usan algunos juegos).</summary>
    public int HotkeyModifiers { get; set; } = 0x4;
    public FrameGraphConfig Graph { get; set; } = new();
    /// <summary>Fecha en que el usuario aceptó el aviso del control de ventiladores.</summary>
    public DateTime? FanDisclaimerAcceptedAt { get; set; }
    /// <summary>Segundos que abarcan el 1% low y la gráfica (5-60).</summary>
    public int FrameStatsSeconds { get; set; } = 10;
    /// <summary>Partidas que se guardan en el historial de Rendimiento (las más antiguas se borran).</summary>
    public int SessionLimit { get; set; } = 50;
    /// <summary>Curvas marcadas en la gráfica de hardware de Rendimiento.</summary>
    public List<string> PerformanceSeries { get; set; } = ["gpu-temp", "cpu-temp"];
    /// <summary>Avisos por sensor y apagado de emergencia.</summary>
    public SafetyConfig Safety { get; set; } = new();
    /// <summary>Control de ventiladores.</summary>
    public CoolingConfig Cooling { get; set; } = new();
    public string? LegacyImportedFrom { get; set; }
    public DateTime? LegacyImportedAt { get; set; }
    /// <summary>Alarmas, apagado y ventiladores de SpotLay 10 (fase 2) ya importados.</summary>
    public DateTime? LegacySafetyImportedAt { get; set; }

    public static string ProfileKey(string exeName) =>
        exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;

    public GameProfile? ProfileFor(string exeName) =>
        string.IsNullOrWhiteSpace(exeName) ? null : Profiles.GetValueOrDefault(ProfileKey(exeName));

    /// <summary>Diseño que se usa ahora: el del juego si tiene perfil activo, si no el global.</summary>
    public OverlayLayout ActiveLayout(string exeName) =>
        ProfileFor(exeName) is { Enabled: true } profile ? profile.Layout : Global;
}

public static class ConfigJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
