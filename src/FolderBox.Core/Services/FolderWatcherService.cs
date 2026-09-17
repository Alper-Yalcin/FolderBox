using FolderBox.Core.Logging;
using FolderBox.Core.Utilities;

namespace FolderBox.Core.Services;

/// <summary>
/// Wraps a <see cref="FileSystemWatcher"/> with debouncing. Raises <see cref="Changed"/> on a
/// thread-pool thread at most once per quiet period; the consumer marshals to the UI thread.
/// Raises <see cref="Failed"/> if the watcher dies (e.g. the folder disappears or a network share drops).
/// </summary>
public sealed class FolderWatcherService : IDisposable
{
    private readonly object _gate = new();
    private readonly Debouncer _debouncer;
    private FileSystemWatcher? _watcher;
    private string? _path;
    private bool _disposed;

    public event Action? Changed;
    public event Action<Exception>? Failed;

    public string? Path => _path;

    public FolderWatcherService(TimeSpan? debounce = null)
    {
        _debouncer = new Debouncer(debounce ?? TimeSpan.FromMilliseconds(300), () => Changed?.Invoke());
    }

    /// <summary>Starts watching <paramref name="path"/>, replacing any previous target. Returns false if watching failed.</summary>
    public bool Watch(string path)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) && _watcher is { EnableRaisingEvents: true })
                return true;

            StopCore();
            _path = path;
            try
            {
                if (!Directory.Exists(path)) return false;
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                                 | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += OnAnyChange;
                w.Deleted += OnAnyChange;
                w.Changed += OnAnyChange;
                w.Renamed += OnAnyChange;
                w.Error += OnError;
                w.EnableRaisingEvents = true;
                _watcher = w;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"FolderWatcher could not watch '{path}': {ex.Message}");
                _watcher?.Dispose();
                _watcher = null;
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
            _path = null;
        }
    }

    private void StopCore()
    {
        _debouncer.Cancel();
        if (_watcher is null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnAnyChange;
            _watcher.Deleted -= OnAnyChange;
            _watcher.Changed -= OnAnyChange;
            _watcher.Renamed -= OnAnyChange;
            _watcher.Error -= OnError;
            _watcher.Dispose();
        }
        catch { }
        _watcher = null;
    }

    private void OnAnyChange(object sender, FileSystemEventArgs e) => _debouncer.Trigger();

    private void OnError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Log.Warn($"FolderWatcher error on '{_path}': {ex.Message}");
        // Buffer overflow: content probably changed a lot, just ask for a refresh.
        if (ex is InternalBufferOverflowException)
        {
            _debouncer.Trigger();
            return;
        }
        Failed?.Invoke(ex);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopCore();
            _debouncer.Dispose();
        }
    }
}
