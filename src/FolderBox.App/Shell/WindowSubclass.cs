using System.Runtime.InteropServices;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>Result of a subclass message handler.</summary>
internal readonly record struct MessageResult(bool Handled, IntPtr Result)
{
    public static readonly MessageResult Unhandled = new(false, IntPtr.Zero);
    public static MessageResult HandledWith(IntPtr result) => new(true, result);
    public static MessageResult HandledZero => new(true, IntPtr.Zero);
}

/// <summary>Message handler signature: (hwnd, msg, wParam, lParam).</summary>
internal delegate MessageResult WindowMessageHandler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

/// <summary>
/// Installs a comctl32 window subclass on an HWND so managed code can observe / override messages.
/// Keeps the native delegate alive for the lifetime of the subclass.
/// </summary>
internal sealed class WindowSubclass : IDisposable
{
    private static int s_nextId = 1;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _id;
    private readonly SubclassProc _proc;
    private readonly List<WindowMessageHandler> _handlers = new();
    private bool _disposed;

    public IntPtr Hwnd => _hwnd;

    public WindowSubclass(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _id = new IntPtr(Interlocked.Increment(ref s_nextId));
        _proc = Proc;
        if (!SetWindowSubclass(hwnd, _proc, _id, IntPtr.Zero))
            throw new InvalidOperationException("SetWindowSubclass failed: " + Marshal.GetLastWin32Error());
    }

    public void AddHandler(WindowMessageHandler handler) => _handlers.Add(handler);
    public void RemoveHandler(WindowMessageHandler handler) => _handlers.Remove(handler);

    private IntPtr Proc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (!_disposed)
        {
            // Iterate over a snapshot; handlers may add/remove themselves while running.
            foreach (var handler in _handlers.ToArray())
            {
                MessageResult r;
                try { r = handler(hWnd, uMsg, wParam, lParam); }
                catch (Exception ex)
                {
                    Core.Logging.Log.Error($"Subclass handler threw for msg 0x{uMsg:X}", ex);
                    continue;
                }
                if (r.Handled) return r.Result;
            }
            if (uMsg == WM_DESTROY)
            {
                Dispose();
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { RemoveWindowSubclass(_hwnd, _proc, _id); } catch { }
        _handlers.Clear();
    }
}
