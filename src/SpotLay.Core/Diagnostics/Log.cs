using System.Text;

namespace SpotLay.Core.Diagnostics;

/// <summary>Log de texto con rotacion simple (2 ficheros de 5 MB).</summary>
public static class Log
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static StreamWriter? s_writer;

    public static string FilePath => Path.Combine(AppPaths.LogsDirectory, "spotlay.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                EnsureWriter();
                s_writer!.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}");
                s_writer.Flush();
                if (s_writer.BaseStream.Length > MaxBytes) Rotate();
            }
            catch
            {
                // El log nunca debe tumbar la app.
            }
        }
    }

    private static void EnsureWriter()
    {
        if (s_writer is not null) return;
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        s_writer = new StreamWriter(stream, new UTF8Encoding(false));
    }

    private static void Rotate()
    {
        s_writer!.Dispose();
        s_writer = null;
        var previous = FilePath + ".1";
        File.Delete(previous);
        File.Move(FilePath, previous);
    }

    public static void Close()
    {
        lock (Gate)
        {
            s_writer?.Dispose();
            s_writer = null;
        }
    }
}
