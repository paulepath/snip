namespace Snip;

/// <summary>A tiny thread-safe file log: %LOCALAPPDATA%\Snip\snip.log, moved to snip.log.1 when it passes 1 MB.</summary>
public static class Log
{
    const long MaxBytes = 1024 * 1024;
    static readonly Lock Gate = new();

    /// <summary>Settable so tests don't write into the real log.</summary>
    public static string FilePath { get; set; } = Path.Combine(AppPaths.Dir, "snip.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, line);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}

public static class AppPaths
{
    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snip");
}
