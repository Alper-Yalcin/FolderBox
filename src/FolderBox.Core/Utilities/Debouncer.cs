namespace FolderBox.Core.Utilities;

/// <summary>
/// Coalesces bursts of calls into a single callback after a quiet period.
/// Thread-safe; the callback runs on a thread-pool thread.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _callback;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action callback)
    {
        _delay = delay;
        _callback = callback;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Schedules (or reschedules) the callback.</summary>
    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        try { _callback(); } catch { /* callers handle their own errors */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
