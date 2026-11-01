namespace SpotLay.Core.Config;

public static class AlarmConditions
{
    public const string Above = "above";
    public const string Below = "below";
}

/// <summary>
/// Aviso y apagado de emergencia de un sensor. Son globales (no dependen del
/// perfil del juego): una protección térmica no debe cambiar según el juego.
/// Campos equivalentes a los de MetricStyle en SpotLay 10.
/// </summary>
public sealed class AlarmRule
{
    public bool AlertEnabled { get; set; }
    public string AlertCondition { get; set; } = AlarmConditions.Above;
    public double AlertThreshold { get; set; } = 90;
    public double AlertHysteresis { get; set; } = 1;
    /// <summary>Ventana de aviso encima del juego (en SpotLay 10: AlertOverlayEnabled).</summary>
    public bool AlertPopup { get; set; } = true;
    public string AlertText { get; set; } = "";
    public string AlertTextColor { get; set; } = "#FFFFFF";
    public string AlertBackground { get; set; } = "#B3132B";
    public int AlertLabelSize { get; set; } = 18;
    public int AlertValueSize { get; set; } = 44;
    /// <summary>auto (apiladas arriba en el centro) o una de OverlayPositions.</summary>
    public string AlertPosition { get; set; } = "auto";
    public double AlertPositionX { get; set; } = .5;
    public double AlertPositionY { get; set; } = .08;
    /// <summary>Color y tamaño (%) del valor en la tarjeta del overlay mientras dura el aviso.</summary>
    public string AlertTileColor { get; set; } = "#FF3030";
    public int AlertTileScale { get; set; } = 120;

    public bool ShutdownEnabled { get; set; }
    public string ShutdownCondition { get; set; } = AlarmConditions.Above;
    public double ShutdownThreshold { get; set; } = 95;
    public double ShutdownHysteresis { get; set; } = 1;
    public int ShutdownDelaySeconds { get; set; } = 30;
    /// <summary>Cancela la cuenta atrás si el sensor vuelve a un valor seguro.</summary>
    public bool ShutdownAutoCancel { get; set; } = true;

    public bool IsEmpty => !AlertEnabled && !ShutdownEnabled;
}

/// <summary>Tamaño y aspecto comunes de las ventanas de aviso.</summary>
public sealed class AlertWindowConfig
{
    public int Width { get; set; } = 460;
    public int Height { get; set; } = 150;
    public int Opacity { get; set; } = 96;
    public int Radius { get; set; } = 20;
}

public sealed class SafetyConfig
{
    /// <summary>Clave: id de la métrica (mismo id que en el overlay).</summary>
    public Dictionary<string, AlarmRule> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AlertWindowConfig AlertWindow { get; set; } = new();
    /// <summary>Ctrl + Alt + F9 por defecto (Alt + F9 lo usa la NVIDIA app para grabar). 0 = sin tecla (solo botón).</summary>
    public int CancelHotkeyKey { get; set; } = 0x78;
    public int CancelHotkeyModifiers { get; set; } = 0x3;
    /// <summary>Pausa de la protección tras cancelar un apagado.</summary>
    public int CooldownSeconds { get; set; } = 300;

    public AlarmRule RuleFor(string metricId)
    {
        if (!Rules.TryGetValue(metricId, out var rule)) Rules[metricId] = rule = new AlarmRule();
        return rule;
    }
}

public sealed record FanPoint(int Temperature, int Duty);

public static class FanModes
{
    public const string Curve = "curve";
    public const string Manual = "manual";
}

public sealed class CoolingConfig
{
    public bool Enabled { get; set; }
    /// <summary>Ocultar en Refrigeración los conectores de la placa sin ventilador (nunca vistos girar).</summary>
    public bool HideEmptyChannels { get; set; } = true;
    /// <summary>Clave: identificador de LibreHardwareMonitor del control (p. ej. /lpc/nct6796dr/0/control/1).</summary>
    public Dictionary<string, FanChannelConfig> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FanChannelConfig
{
    public string Label { get; set; } = "";
    /// <summary>gpu | cpu | case | other</summary>
    public string Role { get; set; } = "other";
    public bool Enabled { get; set; }
    public string Mode { get; set; } = FanModes.Curve;
    public string TemperatureSourceId { get; set; } = "gpu-hotspot";
    public string SecondaryTemperatureSourceId { get; set; } = "";
    /// <summary>max | min | average</summary>
    public string MixMode { get; set; } = "max";
    public int ManualDuty { get; set; } = 50;
    public int MinimumDuty { get; set; } = 25;
    public int MaximumDuty { get; set; } = 100;
    public double CriticalTemperature { get; set; } = 90;
    public double Hysteresis { get; set; } = 2;
    public double ResponseUpSeconds { get; set; } = 2;
    public double ResponseDownSeconds { get; set; } = 8;
    public List<FanPoint> Curve { get; set; } = DefaultCurve();

    public static List<FanPoint> DefaultCurve() =>
    [
        new(30, 25), new(45, 30), new(60, 45), new(72, 65), new(82, 85), new(90, 100)
    ];
}

/// <summary>Límites de valores, iguales a los que aplicaba SpotLay 10 al cargar.</summary>
public static class SafetyLimits
{
    private static readonly HashSet<string> Positions =
    [
        "auto", OverlayPositions.TopLeft, OverlayPositions.TopCenter, OverlayPositions.TopRight, OverlayPositions.Center,
        OverlayPositions.BottomLeft, OverlayPositions.BottomCenter, OverlayPositions.BottomRight, OverlayPositions.Custom
    ];

    public static void Normalize(SafetyConfig safety)
    {
        safety.Rules = new Dictionary<string, AlarmRule>(safety.Rules ?? [], StringComparer.OrdinalIgnoreCase);
        safety.AlertWindow ??= new();
        foreach (var rule in safety.Rules.Values) Normalize(rule);
        var w = safety.AlertWindow;
        w.Width = Math.Clamp(w.Width, 220, 1600);
        w.Height = Math.Clamp(w.Height, 90, 800);
        w.Opacity = Math.Clamp(w.Opacity, 20, 100);
        w.Radius = Math.Clamp(w.Radius, 0, 80);
        safety.CancelHotkeyKey = Math.Clamp(safety.CancelHotkeyKey, 0, 254);
        safety.CancelHotkeyModifiers &= 0xF;
        safety.CooldownSeconds = Math.Clamp(safety.CooldownSeconds, 30, 3600);
    }

    public static void Normalize(AlarmRule r)
    {
        r.AlertCondition = r.AlertCondition == AlarmConditions.Below ? AlarmConditions.Below : AlarmConditions.Above;
        r.AlertThreshold = Finite(r.AlertThreshold, 90, -100, 1000);
        r.AlertHysteresis = Finite(r.AlertHysteresis, 1, 0, 1000);
        r.AlertText = (r.AlertText ?? "").Length > 100 ? r.AlertText![..100] : r.AlertText ?? "";
        r.AlertTextColor = string.IsNullOrWhiteSpace(r.AlertTextColor) ? "#FFFFFF" : r.AlertTextColor;
        r.AlertBackground = string.IsNullOrWhiteSpace(r.AlertBackground) ? "#B3132B" : r.AlertBackground;
        r.AlertTileColor = string.IsNullOrWhiteSpace(r.AlertTileColor) ? "#FF3030" : r.AlertTileColor;
        r.AlertLabelSize = Math.Clamp(r.AlertLabelSize, 10, 64);
        r.AlertValueSize = Math.Clamp(r.AlertValueSize, 16, 120);
        r.AlertTileScale = Math.Clamp(r.AlertTileScale, 100, 300);
        r.AlertPosition = Positions.Contains(r.AlertPosition ?? "") ? r.AlertPosition! : "auto";
        r.AlertPositionX = Finite(r.AlertPositionX, .5, 0, 1);
        r.AlertPositionY = Finite(r.AlertPositionY, .08, 0, 1);
        r.ShutdownCondition = r.ShutdownCondition == AlarmConditions.Below ? AlarmConditions.Below : AlarmConditions.Above;
        r.ShutdownThreshold = Finite(r.ShutdownThreshold, 95, -100, 1000);
        r.ShutdownHysteresis = Finite(r.ShutdownHysteresis, 1, 0, 1000);
        r.ShutdownDelaySeconds = Math.Clamp(r.ShutdownDelaySeconds, 5, 3600);
    }

    public static void Normalize(CoolingConfig cooling)
    {
        cooling.Channels = new Dictionary<string, FanChannelConfig>(cooling.Channels ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var (id, channel) in cooling.Channels.ToArray())
        {
            if (string.IsNullOrWhiteSpace(id) || channel is null)
            {
                cooling.Channels.Remove(id);
                continue;
            }
            Normalize(channel);
        }
    }

    public static void Normalize(FanChannelConfig c)
    {
        c.Label = (c.Label ?? "").Trim();
        c.Role = c.Role is "gpu" or "cpu" or "case" ? c.Role : "other";
        c.Mode = c.Mode == FanModes.Manual ? FanModes.Manual : FanModes.Curve;
        c.TemperatureSourceId = string.IsNullOrWhiteSpace(c.TemperatureSourceId) ? "gpu-hotspot" : c.TemperatureSourceId;
        c.SecondaryTemperatureSourceId ??= "";
        c.MixMode = c.MixMode is "min" or "average" ? c.MixMode : "max";
        c.ManualDuty = Math.Clamp(c.ManualDuty, 0, 100);
        c.MinimumDuty = Math.Clamp(c.MinimumDuty, 0, 100);
        c.MaximumDuty = Math.Clamp(c.MaximumDuty, c.MinimumDuty, 100);
        c.CriticalTemperature = Finite(c.CriticalTemperature, 90, 40, 110);
        c.Hysteresis = Finite(c.Hysteresis, 2, 0, 20);
        c.ResponseUpSeconds = Finite(c.ResponseUpSeconds, 2, 0, 60);
        c.ResponseDownSeconds = Finite(c.ResponseDownSeconds, 8, 0, 120);
        var curve = (c.Curve ?? [])
            .Where(p => p is not null)
            .Select(p => new FanPoint(Math.Clamp(p.Temperature, 20, 110), Math.Clamp(p.Duty, 0, 100)))
            .OrderBy(p => p.Temperature)
            .ToList();
        c.Curve = curve.Count >= 2 ? curve : FanChannelConfig.DefaultCurve();
    }

    private static double Finite(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
