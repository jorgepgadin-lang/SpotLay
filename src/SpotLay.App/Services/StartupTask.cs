using System.Diagnostics;
using System.IO;
using System.Security;
using SpotLay.Core.Diagnostics;

namespace SpotLay.App.Services;

/// <summary>
/// Arranque con Windows mediante el Programador de tareas: "al iniciar sesión,
/// con privilegios elevados". La clave Run del registro no puede elevar (saldría
/// un UAC en cada arranque o Windows la saltaría). Sin límite de tiempo ni
/// parada con batería, que el programador pone por defecto.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "SpotLay";

    public static bool Exists() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    /// <summary>Ruta del exe que arranca la tarea (null si no existe).</summary>
    public static string? RegisteredPath()
    {
        var (code, output) = Run("/Query", "/TN", TaskName, "/XML");
        if (code != 0) return null;
        var start = output.IndexOf("<Command>", StringComparison.Ordinal);
        var end = output.IndexOf("</Command>", StringComparison.Ordinal);
        return start >= 0 && end > start ? SecurityElement.FromString("<c>" + output[(start + 9)..end] + "</c>")?.Text : null;
    }

    public static bool Create(string exePath)
    {
        var user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SpotLay: overlay, avisos, apagado de emergencia y ventiladores.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT15S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exePath)}</Command><Arguments>--minimized</Arguments></Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "SpotLay-task.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            var (code, output) = Run("/Create", "/TN", TaskName, "/XML", file, "/F");
            if (code != 0) Log.Warn("No se pudo crear la tarea de arranque: " + output.Trim());
            else Log.Info("Arranque con Windows activado: " + exePath);
            return code == 0;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static bool Delete()
    {
        var (code, output) = Run("/Delete", "/TN", TaskName, "/F");
        if (code == 0) Log.Info("Arranque con Windows desactivado");
        else Log.Warn("No se pudo borrar la tarea de arranque: " + output.Trim());
        return code == 0;
    }

    /// <summary>
    /// Si el exe de la tarea ya no existe (SpotLay se ha movido), la tarea apunta a este.
    /// Una copia abierta desde otra carpeta (pruebas, descargas) no se queda con el arranque
    /// mientras el exe instalado siga en su sitio.
    /// </summary>
    /// <returns>true si la tarea se ha vuelto a crear.</returns>
    public static bool RefreshPath(string exePath, bool recreate)
    {
        var current = RegisteredPath();
        if (current is null) return false;
        Log.Info("Arranque con Windows: la tarea abre " + current);
        var moved = !string.Equals(current, exePath, StringComparison.OrdinalIgnoreCase) && !File.Exists(current);
        if (!moved && !recreate) return false;
        // Versión nueva: se rehace la tarea igual (tareas de versiones antiguas dejaron de dispararse
        // al iniciar sesión hasta volver a crearlas, 27/09/2026). Sigue abriendo el mismo exe.
        return Create(moved ? exePath : current);
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var p = Process.Start(start)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
