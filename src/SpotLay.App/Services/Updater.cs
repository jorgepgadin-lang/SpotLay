using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SpotLay.Core.Diagnostics;
using SpotLay.Core.Localization;

namespace SpotLay.App.Services;

/// <summary>
/// Actualizaciones desde las Releases públicas de GitHub. Comprueba la última versión,
/// descarga SpotLay.exe, lo verifica con su SHA-256 (SpotLay.exe.sha256) y lo instala
/// cambiando el exe en su sitio: el actual pasa a SpotLay.old.exe (Windows deja renombrar
/// un exe abierto, no borrarlo) y se borra en el siguiente arranque.
/// </summary>
public sealed class Updater
{
    public const string Repository = "jorgepgadin-lang/SpotLay";
    private const string ExeAsset = "SpotLay.exe";
    private const string HashAsset = "SpotLay.exe.sha256";

    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>Versión de este ejecutable (la del proyecto: &lt;Version&gt;).</summary>
    public static Version Current { get; } = Assembly.GetEntryAssembly()?.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>Versión nueva ya descargada y verificada, lista para instalar.</summary>
    public Version? Ready { get; private set; }
    private string? _readyFile;

    public string Status { get; private set; } = "";
    public event Action? Changed;

    // Solo el exe publicado se actualiza; el de desarrollo (bin\Debug) nunca.
#if DEBUG
    public static bool Enabled => false;
#else
    public static bool Enabled => Environment.ProcessPath is { } p && !p.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);
#endif

    private static string Folder => Path.Combine(AppPaths.Root, "update");

    /// <summary>Al arrancar: borra el exe anterior que quedó tras actualizar.</summary>
    public static void CleanUpPrevious()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && File.Exists(OldPath(exe)))
            {
                File.Delete(OldPath(exe));
                Log.Info("Actualización: exe anterior borrado");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Actualización: no se pudo borrar el exe anterior: " + ex.Message);
        }
    }

    /// <summary>Busca versión nueva y, si la hay, la descarga y verifica. true = hay una lista para instalar.</summary>
    public async Task<bool> CheckAndDownloadAsync()
    {
        if (!Enabled)
        {
            SetStatus(Tr.T("Las actualizaciones solo funcionan en el SpotLay.exe publicado."));
            return false;
        }
        if (!await _busy.WaitAsync(0)) return Ready is not null;
        try
        {
            if (Ready is not null) return true;
            SetStatus(Tr.T("Buscando actualizaciones…"));
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                SetStatus(Tr.F("Tienes la última versión ({0}).", CurrentText));
                return false;
            }
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= Current)
            {
                SetStatus(Tr.F("Tienes la última versión ({0}).", CurrentText));
                return false;
            }

            string? exeUrl = null, hashUrl = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (string.Equals(name, ExeAsset, StringComparison.OrdinalIgnoreCase)) exeUrl = url;
                else if (string.Equals(name, HashAsset, StringComparison.OrdinalIgnoreCase)) hashUrl = url;
            }
            if (exeUrl is null || hashUrl is null)
            {
                SetStatus(Tr.F("La versión {0} no trae el ejecutable o su huella; no se instala.", latest));
                return false;
            }

            SetStatus(Tr.F("Descargando SpotLay {0}…", latest));
            var expected = (await Http.GetStringAsync(hashUrl)).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, $"SpotLay-{latest}.exe");
            await using (var target = File.Create(file))
            await using (var source = await Http.GetStreamAsync(exeUrl))
                await source.CopyToAsync(target);

            string actual;
            await using (var check = File.OpenRead(file))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check));
            if (actual != expected)
            {
                File.Delete(file);
                Log.Warn($"Actualización {latest}: la huella no coincide (esperada {expected}, descargada {actual}); descartada");
                SetStatus(Tr.T("La descarga no coincide con su huella SHA-256; se ha descartado."));
                return false;
            }

            _readyFile = file;
            Ready = latest;
            Log.Info($"Actualización {latest} descargada y verificada");
            SetStatus(Tr.F("SpotLay {0} está listo para instalarse.", latest));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Actualización: no se pudo comprobar o descargar: " + ex.Message);
            SetStatus(Tr.T("No se pudo buscar actualizaciones (¿sin internet?)."));
            return false;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// Pone el exe nuevo en el sitio del actual. Hay que llamarlo al cerrar SpotLay:
    /// el siguiente arranque ya es la versión nueva. Si algo falla, deja el exe actual.
    /// </summary>
    public bool Install()
    {
        if (_readyFile is null || !File.Exists(_readyFile) || Environment.ProcessPath is not { } exe) return false;
        var old = OldPath(exe);
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Copy(_readyFile, exe);
            }
            catch
            {
                File.Move(old, exe);
                throw;
            }
            File.Delete(_readyFile);
            RefreshShellIcon(exe);
            Log.Info($"Actualización {Ready} instalada");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Actualización: no se pudo instalar", ex);
            SetStatus(Tr.T("No se pudo instalar la actualización; sigues con la versión actual."));
            return false;
        }
    }

    /// <summary>
    /// El exe se ha cambiado por otro: que Windows vuelva a leer su icono. Si no, los accesos
    /// directos pueden quedarse con el icono en blanco que cacheó mientras no estaba el archivo.
    /// </summary>
    private static void RefreshShellIcon(string exe)
    {
        const uint updateItem = 0x00002000, assocChanged = 0x08000000, pathW = 0x0005, idList = 0x0000, flush = 0x1000;
        try
        {
            var path = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(exe);
            try { SHChangeNotify(updateItem, pathW | flush, path, IntPtr.Zero); }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(path); }
            SHChangeNotify(assocChanged, idList | flush, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn("Actualización: no se pudo refrescar el icono: " + ex.Message);
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private static string OldPath(string exe) => Path.Combine(Path.GetDirectoryName(exe)!, "SpotLay.old.exe");

    private void SetStatus(string text)
    {
        Status = text;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub exige un User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpotLay-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
