using System.Text.Json;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Localization;

namespace SpotLay.Core.Config;

public sealed record LegacyImportResult(bool Imported, string Message, int Profiles, int Metrics);

/// <summary>
/// Importa el diseño de SpotLay 10 (%LOCALAPPDATA%\SpotLay\settings.json) sin
/// modificar ese fichero: diseño y perfiles (fase 1), y alarmas, apagado de
/// emergencia y ventiladores (fase 2).
/// </summary>
public static class LegacyImporter
{
    public static bool LegacyExists => File.Exists(AppPaths.LegacySettingsFile);

    public static LegacyImportResult ImportInto(SpotLayConfig config)
    {
        if (!LegacyExists) return new(false, Tr.T("No se encontró la configuración de SpotLay 10."), 0, 0);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.LegacySettingsFile));
            var root = document.RootElement;

            // SpotLay 10 guarda el diseño global en GlobalProfile; la raiz refleja
            // el perfil que estuviera aplicado en ese momento.
            var global = TryGet(root, "GlobalProfile", out var gp) && gp.ValueKind == JsonValueKind.Object ? gp : root;
            config.Global = ReadLayout(global);

            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TryGet(root, "EnabledGameProfiles", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (item.GetString() is { } name) enabled.Add(SpotLayConfig.ProfileKey(name));

            config.Profiles.Clear();
            if (TryGet(root, "GameProfiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in profiles.EnumerateObject())
                {
                    var key = SpotLayConfig.ProfileKey(entry.Name);
                    config.Profiles[key] = new GameProfile
                    {
                        ExeName = key,
                        Enabled = enabled.Contains(key),
                        Layout = ReadLayout(entry.Value)
                    };
                }
            }

            if (TryGet(root, "HotkeyKey", out var hk) && hk.TryGetInt32(out var key32) && key32 > 0) config.HotkeyKey = key32;
            if (TryGet(root, "HotkeyModifiers", out var hm) && hm.TryGetInt32(out var mod32)) config.HotkeyModifiers = mod32;

            var safety = ReadSafety(root, config);

            config.LegacyImportedFrom = AppPaths.LegacySettingsFile;
            config.LegacyImportedAt = DateTime.Now;
            var message = Tr.F("Importado: diseño global con {0} métricas, {1} perfiles de juego, {2}.", config.Global.Metrics.Count, config.Profiles.Count, safety);
            Log.Info(message);
            return new(true, message, config.Profiles.Count, config.Global.Metrics.Count);
        }
        catch (Exception ex)
        {
            Log.Error("Fallo al importar SpotLay 10", ex);
            return new(false, Tr.T("No se pudo leer la configuración de SpotLay 10: ") + ex.Message, 0, 0);
        }
    }

    /// <summary>Solo alarmas, apagado y ventiladores (para quien ya importó el diseño en la fase 1).</summary>
    public static LegacyImportResult ImportSafetyInto(SpotLayConfig config)
    {
        if (!LegacyExists) return new(false, Tr.T("No se encontró la configuración de SpotLay 10."), 0, 0);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.LegacySettingsFile));
            var message = Tr.T("Importado de SpotLay 10: ") + ReadSafety(document.RootElement, config) + ".";
            Log.Info(message);
            return new(true, message, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Error("Fallo al importar alarmas y ventiladores de SpotLay 10", ex);
            return new(false, Tr.T("No se pudieron leer las alarmas y ventiladores de SpotLay 10: ") + ex.Message, 0, 0);
        }
    }

    private static string ReadSafety(JsonElement root, SpotLayConfig config)
    {
        var safety = new SafetyConfig();
        var global = TryGet(root, "GlobalProfile", out var gp) && gp.ValueKind == JsonValueKind.Object ? gp : root;

        // En SpotLay 10 los avisos iban dentro del estilo de cada métrica (y por
        // perfil). En v11 son globales: manda el diseño global, luego la raíz y
        // por último cualquier perfil que tuviera un aviso que los otros no tienen.
        var sources = new List<JsonElement> { global, root };
        if (TryGet(root, "GameProfiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object)
            sources.AddRange(profiles.EnumerateObject().Select(p => p.Value));
        foreach (var source in sources)
        {
            if (!TryGet(source, "MetricStyles", out var styles) || styles.ValueKind != JsonValueKind.Object) continue;
            foreach (var style in styles.EnumerateObject())
            {
                if (safety.Rules.ContainsKey(style.Name)) continue;
                var v = style.Value;
                var rule = new AlarmRule
                {
                    AlertEnabled = Bool(v, "AlertEnabled", false),
                    AlertCondition = Str(v, "AlertCondition", AlarmConditions.Above),
                    AlertThreshold = Dbl(v, "AlertThreshold", 90),
                    AlertHysteresis = Dbl(v, "AlertHysteresis", 1),
                    AlertPopup = Bool(v, "AlertOverlayEnabled", true),
                    AlertText = Str(v, "AlertOverlayText", ""),
                    AlertTextColor = Str(v, "AlertOverlayTextColor", "#FFFFFF"),
                    AlertBackground = Str(v, "AlertOverlayBackground", "#B3132B"),
                    AlertLabelSize = Int(v, "AlertOverlayLabelSize", 18),
                    AlertValueSize = Int(v, "AlertOverlayValueSize", 44),
                    AlertPosition = Str(v, "AlertOverlayPosition", "auto"),
                    AlertPositionX = Dbl(v, "AlertOverlayPositionX", .5),
                    AlertPositionY = Dbl(v, "AlertOverlayPositionY", .08),
                    AlertTileColor = Str(v, "AlertColor", "#FF3030"),
                    AlertTileScale = Int(v, "AlertScale", 120),
                    ShutdownEnabled = Bool(v, "ShutdownEnabled", false),
                    ShutdownCondition = Str(v, "ShutdownCondition", AlarmConditions.Above),
                    ShutdownThreshold = Dbl(v, "ShutdownThreshold", 95),
                    ShutdownHysteresis = Dbl(v, "ShutdownHysteresis", 1),
                    ShutdownDelaySeconds = Int(v, "ShutdownDelaySeconds", 30),
                    ShutdownAutoCancel = Bool(v, "ShutdownAutoCancel", true)
                };
                if (!rule.IsEmpty) safety.Rules[style.Name] = rule;
            }
        }

        var window = TryGet(global, "AlertOverlay", out var ao) && ao.ValueKind == JsonValueKind.Object ? ao
            : TryGet(root, "AlertOverlay", out var rao) && rao.ValueKind == JsonValueKind.Object ? rao : default;
        if (window.ValueKind == JsonValueKind.Object)
        {
            safety.AlertWindow.Width = Int(window, "Width", 460);
            safety.AlertWindow.Height = Int(window, "Height", 150);
            safety.AlertWindow.Opacity = Int(window, "Opacity", 96);
            safety.AlertWindow.Radius = Int(window, "Radius", 20);
        }
        safety.CancelHotkeyKey = Int(root, "ShutdownCancelHotkeyKey", 0x78);
        safety.CancelHotkeyModifiers = Int(root, "ShutdownCancelHotkeyModifiers", 0x3);
        // Alt + F9 (el de SpotLay 10) lo usa la NVIDIA app para grabar: pasa a Ctrl + Alt + F9.
        if (safety.CancelHotkeyKey == 0x78 && safety.CancelHotkeyModifiers == 0x1) safety.CancelHotkeyModifiers = 0x3;
        safety.CooldownSeconds = Int(root, "ShutdownCooldownSeconds", 300);
        SafetyLimits.Normalize(safety);
        config.Safety = safety;

        var cooling = new CoolingConfig();
        if (TryGet(root, "Cooling", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            // Se importa tal cual estaba en SpotLay 10, incluido si el control estaba activado.
            cooling.Enabled = Bool(c, "Enabled", false);
            if (TryGet(c, "Channels", out var channels) && channels.ValueKind == JsonValueKind.Object)
            {
                foreach (var ch in channels.EnumerateObject())
                {
                    var v = ch.Value;
                    var curve = new List<FanPoint>();
                    if (TryGet(v, "Curve", out var points) && points.ValueKind == JsonValueKind.Array)
                        foreach (var p in points.EnumerateArray())
                            curve.Add(new FanPoint(Int(p, "Temperature", 50), Int(p, "Duty", 50)));
                    cooling.Channels[ch.Name] = new FanChannelConfig
                    {
                        Label = Str(v, "Label", ""),
                        Role = Str(v, "Role", "other"),
                        Enabled = Bool(v, "Enabled", false),
                        Mode = Str(v, "Mode", FanModes.Curve),
                        TemperatureSourceId = Str(v, "TemperatureSourceId", "gpu-hotspot"),
                        SecondaryTemperatureSourceId = Str(v, "SecondaryTemperatureSourceId", ""),
                        MixMode = Str(v, "MixMode", "max"),
                        ManualDuty = Int(v, "ManualDuty", 50),
                        MinimumDuty = Int(v, "MinimumDuty", 25),
                        MaximumDuty = Int(v, "MaximumDuty", 100),
                        CriticalTemperature = Dbl(v, "CriticalTemperature", 90),
                        Hysteresis = Dbl(v, "Hysteresis", 2),
                        ResponseUpSeconds = Dbl(v, "ResponseUpSeconds", 2),
                        ResponseDownSeconds = Dbl(v, "ResponseDownSeconds", 8),
                        Curve = curve
                    };
                }
            }
        }
        SafetyLimits.Normalize(cooling);
        config.Cooling = cooling;
        config.LegacySafetyImportedAt = DateTime.Now;

        var active = cooling.Channels.Values.Count(ch => ch.Enabled);
        return Tr.F("{0} sensores con aviso o apagado y {1} canales de ventilador ", safety.Rules.Count, cooling.Channels.Count) +
               Tr.F("({0} con curva o manual, control {1})", active, (cooling.Enabled ? Tr.T("activado") : Tr.T("desactivado")));
    }

    private static OverlayLayout ReadLayout(JsonElement e)
    {
        var layout = new OverlayLayout();
        var columns = Int(e, "Columns", 2);
        var scale = Math.Clamp(0.5 + 1.5 * Math.Clamp(Int(e, "Scale", 33), 0, 100) / 100.0, 0.6, 2.0);
        var frameWidth = Int(e, "FrameWidth", 0);

        layout.Columns = Math.Clamp(columns, 1, 12);
        layout.Scale = scale;
        if (frameWidth > 0) layout.TileWidth = Math.Clamp((int)Math.Round(frameWidth / (double)layout.Columns / scale), 70, 400);
        layout.Position = Str(e, "Position", OverlayPositions.TopRight);
        layout.OffsetX = Int(e, "OffsetX", 28);
        layout.OffsetY = Int(e, "OffsetY", 28);
        layout.FreeX = Dbl(e, "PositionX", 0.02);
        layout.FreeY = Dbl(e, "PositionY", 0.05);
        layout.BackgroundColor = Str(e, "BackgroundColor", layout.BackgroundColor);
        layout.BackgroundOpacity = Math.Clamp(Int(e, "Opacity", 85), 0, 100);
        layout.CornerRadius = Math.Clamp(Int(e, "Radius", 12), 0, 40);
        layout.FontFamily = Str(e, "FontFamily", layout.FontFamily);
        layout.FontBold = Bool(e, "FontBold", layout.FontBold);
        layout.ValueEffect = Str(e, "ValueEffect", "none");

        if (TryGet(e, "EnabledMetrics", out var metrics) && metrics.ValueKind == JsonValueKind.Array)
            layout.Metrics = metrics.EnumerateArray().Select(m => m.GetString()).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        layout.Template = layout.Columns == 1 ? OverlayTemplates.Compacta
            : layout.Columns >= Math.Max(2, layout.Metrics.Count) ? OverlayTemplates.Barra
            : OverlayTemplates.Columna;

        // Solo los estilos de las metricas activas: SpotLay 10 guarda cientos que no se usan.
        if (TryGet(e, "MetricStyles", out var styles) && styles.ValueKind == JsonValueKind.Object)
        {
            var active = new HashSet<string>(layout.Metrics, StringComparer.OrdinalIgnoreCase);
            foreach (var s in styles.EnumerateObject())
            {
                if (!active.Contains(s.Name)) continue;
                var v = s.Value;
                layout.Styles[s.Name] = new MetricStyle
                {
                    Label = Str(v, "Label", ""),
                    Visualization = Str(v, "Visualization", Visualizations.Number),
                    LabelColor = NullIfEmpty(Str(v, "LabelColor", "")),
                    ValueColor = NullIfEmpty(Str(v, "ValueColor", "")),
                    UnitColor = NullIfEmpty(Str(v, "UnitColor", "")),
                    LabelSize = Int(v, "LabelSize", 12),
                    ValueSize = Int(v, "ValueSize", 20),
                    UnitSize = Int(v, "UnitSize", 11),
                    AutoScale = Bool(v, "GraphAutoScale", true),
                    Min = Dbl(v, "GraphMin", 0),
                    Max = Dbl(v, "GraphMax", 100)
                };
            }
        }
        return layout;
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = p.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static int Int(JsonElement e, string name, int fallback) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (int)Math.Round(d) : fallback;

    private static double Dbl(JsonElement e, string name, double fallback) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : fallback;

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        TryGet(e, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static string Str(JsonElement e, string name, string fallback) =>
        TryGet(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
