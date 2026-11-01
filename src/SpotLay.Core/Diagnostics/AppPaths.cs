namespace SpotLay.Core.Diagnostics;

/// <summary>
/// Carpeta propia (v11) para no pisar la configuracion de SpotLay 10, que vive
/// en %LOCALAPPDATA%\SpotLay\settings.json y solo se lee al importar.
/// </summary>
public static class AppPaths
{
    /// <summary>SPOTLAY_DATA cambia la carpeta (solo para pruebas: no tocar la configuración real).</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SPOTLAY_DATA") is { Length: > 0 } test
            ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotLay", "v11");

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string BackupsDirectory => Path.Combine(Root, "backups");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string SessionsDirectory => Path.Combine(Root, "sessions");
    public static string ToolsDirectory => Path.Combine(Root, "tools");

    public static string LegacySettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotLay", "settings.json");
}
