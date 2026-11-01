// Sonda de diagnostico del motor de Frame Generation (sin interfaz).
// Uso (como administrador): EngineProbe.exe <pid> <segundos> <fichero_salida>
using SpotLay.Core.FrameGen;

var pid = uint.Parse(args[0]);
var seconds = int.Parse(args[1]);
using var output = new StreamWriter(args[2]) { AutoFlush = true };
using var monitor = new FrameGenMonitor();
output.WriteLine($"Start = {monitor.Start()}");
for (var i = 0; i < seconds * 4; i++)
{
    Thread.Sleep(250);
    var r = monitor.Read(pid, looksLikeGame: true);
    if (i % 4 != 3) continue;
    var c = monitor.Counters;
    output.WriteLine($"t={(i + 1) / 4}s eventos={c.Events} dxgi={c.DxgiPresents} pcl={c.PclMarkers} errores={c.Errors} | {r.State} x{r.Multiplier} {r.Mode} total={r.TotalFps:F1} reales={r.RealFps:F1} api={r.ApiName} ({r.Reason})");
}