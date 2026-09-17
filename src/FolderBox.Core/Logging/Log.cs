using System.Text;

namespace FolderBox.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Minimal, thread-safe, size-capped file logger. Writes to
/// %LocalAppData%\FolderBox\Logs\folderbox-yyyyMMdd.log and prunes old files.
/// </summary>
public static class Log
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 7;
    private static readonly object Gate = new();
    private static string? _directory;
    private static string? _currentPath;
    private static bool _initialized;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>Optional sink for tests or debug output.</summary>
    public static event Action<LogLevel, string>? MessageWritten;

    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            _directory = directory;
            try
            {
                Directory.CreateDirectory(directory);
                Prune();
            }
            catch
            {
                // Logging must never throw.
            }
            _initialized = true;
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [{Environment.CurrentManagedThreadId,3}] {message}";
        try { MessageWritten?.Invoke(level, line); } catch { }
        System.Diagnostics.Debug.WriteLine(line);

        if (!_initialized || _directory is null) return;
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(_directory, $"folderbox-{DateTime.Now:yyyyMMdd}.log");
                if (path != _currentPath)
                {
                    _currentPath = path;
                    Prune();
                }
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var rolled = Path.ChangeExtension(path, ".1.log");
                    File.Copy(path, rolled, overwrite: true);
                    File.WriteAllText(path, string.Empty);
                }
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never let logging take the app down.
            }
        }
    }

    private static void Prune()
    {
        if (_directory is null || !Directory.Exists(_directory)) return;
        var files = new DirectoryInfo(_directory)
            .GetFiles("folderbox-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxFiles);
        foreach (var f in files)
        {
            try { f.Delete(); } catch { }
        }
    }
}
