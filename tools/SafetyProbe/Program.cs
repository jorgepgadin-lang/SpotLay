// Pruebas de la logica de alarmas, apagado (siempre en modo prueba) e importacion.
// No escribe ningun fichero de configuracion ni toca ventiladores.
using System.Text.Json;
using SpotLay.Core.Alarms;
using SpotLay.Core.Config;
using SpotLay.Core.Cooling;
using SpotLay.Core.Metrics;

var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "OK  " : "FALLO")} {what}");
    if (!ok) failures++;
}

MetricSnapshot Snap(double temp) => new(DateTimeOffset.Now,
    new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase)
    {
        ["gpu-hotspot"] = new("gpu-hotspot", "GPU Hot Spot", temp, "°C", MetricCategory.Gpu, "GPU")
    }, "GPU", "CPU");

// --- Avisos con histeresis ---
var cfg = new SafetyConfig();
var r = cfg.RuleFor("gpu-hotspot");
r.AlertEnabled = true; r.AlertThreshold = 80; r.AlertHysteresis = 2;
var eng = new AlarmEngine { DryRun = true };
var t = new DateTime(2026, 9, 24, 20, 0, 0, DateTimeKind.Utc);
Check(eng.Evaluate(Snap(79), cfg, t).Alerts.Count == 0, "79 °C < 80: sin aviso");
Check(eng.Evaluate(Snap(80), cfg, t).Alerts.Count == 1, "80 °C: aviso");
Check(eng.Evaluate(Snap(78.5), cfg, t).Alerts.Count == 1, "78,5 °C: sigue (histeresis 2)");
Check(eng.Evaluate(Snap(77.9), cfg, t).Alerts.Count == 0, "77,9 °C: se quita");
r.AlertCondition = AlarmConditions.Below; r.AlertThreshold = 30;
Check(eng.Evaluate(Snap(29), cfg, t).Alerts.Count == 1, "condicion 'por debajo': 29 < 30 avisa");
r.AlertEnabled = false;

// --- Apagado: cuenta atras, auto-cancelacion sin pausa ---
r.ShutdownEnabled = true; r.ShutdownThreshold = 90; r.ShutdownHysteresis = 1; r.ShutdownDelaySeconds = 10; r.ShutdownAutoCancel = true;
var s = eng.Evaluate(Snap(85), cfg, t).Shutdown;
Check(s.Phase == ShutdownPhase.Armed, $"85 °C: armado ({s.Status})");
s = eng.Evaluate(Snap(91), cfg, t).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown && s.RemainingSeconds == 10, $"91 °C: cuenta atras 10 s ({s.Status})");
s = eng.Evaluate(Snap(89.5), cfg, t.AddSeconds(3)).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown && s.RemainingSeconds == 7, $"89,5 °C (histeresis): sigue, quedan 7 ({s.Status})");
s = eng.Evaluate(Snap(88), cfg, t.AddSeconds(4)).Shutdown;
Check(s.Phase == ShutdownPhase.Armed, $"88 °C: auto-cancelado y SIN pausa ({s.Status})");
s = eng.Evaluate(Snap(92), cfg, t.AddSeconds(5)).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown, "92 °C otra vez: nueva cuenta atras inmediata");

// --- Llega a cero en modo prueba ---
s = eng.Evaluate(Snap(93), cfg, t.AddSeconds(15)).Shutdown;
Check(s.Phase == ShutdownPhase.Issued && s.Simulated, $"a los 10 s: 'apagado' simulado ({s.Status})");
s = eng.Evaluate(Snap(93), cfg, t.AddSeconds(22)).Shutdown;
Check(s.Phase != ShutdownPhase.Issued, $"6 s despues la simulacion se limpia ({s.Phase}: {s.Status})");

// --- Cancelacion manual: pausa ---
var eng2 = new AlarmEngine { DryRun = true };
cfg.CooldownSeconds = 300;
eng2.Evaluate(Snap(95), cfg, t);
eng2.Cancel("Cancelado con Alt+F9", cfg, t.AddSeconds(2));
s = eng2.Evaluate(Snap(95), cfg, t.AddSeconds(3)).Shutdown;
Check(s.Phase == ShutdownPhase.Cooldown, $"tras cancelar a mano: pausa ({s.Status})");
s = eng2.Evaluate(Snap(95), cfg, t.AddSeconds(303)).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown, "tras 300 s de pausa vuelve a contar");

// --- Desarmar desde el panel ---
r.ShutdownEnabled = false;
s = eng2.Evaluate(Snap(95), cfg, t.AddSeconds(304)).Shutdown;
Check(s.Phase == ShutdownPhase.Disabled, $"desactivar el apagado cancela la cuenta atras ({s.Status})");

// --- Simulacion desde el boton ---
var eng3 = new AlarmEngine();
eng3.StartSimulation(5, t);
s = eng3.Evaluate(Snap(40), cfg, t.AddSeconds(1)).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown && s.Simulated && s.RemainingSeconds == 4, "simulacion: cuenta atras aunque el sensor este bien");
s = eng3.Evaluate(Snap(40), cfg, t.AddSeconds(5)).Shutdown;
Check(s.Phase == ShutdownPhase.Issued && s.Simulated, $"simulacion llega a cero sin apagar ({s.Status})");
eng3.StartSimulation(5, t.AddSeconds(20));
eng3.Cancel("Cancelado", cfg, t.AddSeconds(21));
r.ShutdownEnabled = true; r.ShutdownThreshold = 90;
s = eng3.Evaluate(Snap(95), cfg, t.AddSeconds(22)).Shutdown;
Check(s.Phase == ShutdownPhase.Countdown, "cancelar una simulacion no pausa la proteccion real");

// --- Curva ---
var curve = new List<FanPoint> { new(40, 30), new(60, 50), new(80, 100) };
Check(CurveMath.Interpolate(curve, 30) == 30, "curva: por debajo del primer punto = 30%");
Check(Math.Abs(CurveMath.Interpolate(curve, 70) - 75) < 0.01, "curva: 70 °C = 75%");
Check(CurveMath.Interpolate(curve, 95) == 100, "curva: por encima del ultimo = 100%");

// --- Importacion (solo lectura del settings.json de SpotLay 10) ---
if (LegacyImporter.LegacyExists)
{
    var config = new SpotLayConfig();
    var result = LegacyImporter.ImportSafetyInto(config);
    Console.WriteLine("     " + result.Message);
    Check(result.Imported, "importacion de alarmas y ventiladores");
    Check(config.Cooling.Enabled, "control de ventiladores importado ACTIVADO (como en SpotLay 10)");
    foreach (var (id, ch) in config.Cooling.Channels)
        Console.WriteLine($"     {id,-28} {ch.Label,-16} {(ch.Enabled ? "control" : "BIOS"),-7} {ch.Mode,-6} {ch.TemperatureSourceId}{(ch.SecondaryTemperatureSourceId != "" ? "+" + ch.SecondaryTemperatureSourceId : "")} " +
                          string.Join(" ", ch.Curve.Select(p => $"{p.Temperature}°:{p.Duty}%")));
    Console.WriteLine($"     cancelar: tecla {config.Safety.CancelHotkeyKey} mod {config.Safety.CancelHotkeyModifiers}, pausa {config.Safety.CooldownSeconds} s");
    var json = JsonSerializer.Serialize(config, ConfigJson.Options);
    var back = JsonSerializer.Deserialize<SpotLayConfig>(json, ConfigJson.Options)!;
    Check(back.Cooling.Channels.Count == config.Cooling.Channels.Count &&
          back.Cooling.Channels.Values.Zip(config.Cooling.Channels.Values).All(p => p.First.Curve.SequenceEqual(p.Second.Curve)),
          "ida y vuelta por JSON conserva canales y curvas");
}

Console.WriteLine(failures == 0 ? "TODO OK" : $"{failures} FALLOS");
return failures;
