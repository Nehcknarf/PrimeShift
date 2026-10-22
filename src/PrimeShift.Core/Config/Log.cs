using System.Text;

namespace PrimeShift.Core.Config;

/// <summary>Minimal append-only log at %APPDATA%\PrimeShift\primeshift.log.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ConfigStore.Directory);
                var path = Path.Combine(ConfigStore.Directory, "primeshift.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
