using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using Microsoft.UI.Xaml;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

internal enum DesktopLayer
{
    /// <summary>Collapsed folder tiles: lowest, directly above the desktop icons.</summary>
    Tiles = 0,
    /// <summary>The expanded panel: above tiles, still below every normal application window.</summary>
    Panel = 1,
}

/// <summary>
/// Keeps FolderBox windows glued to the desktop layer.
///
/// Approach: every widget window is an ordinary top-level tool window (no taskbar button, no Alt+Tab
/// entry) that we pin to the bottom of the Z-order. Windows keeps the shell window (Progman) below
/// HWND_BOTTOM windows, so ours end up just above the desktop icons and below every application.
/// A window subclass intercepts WM_WINDOWPOSCHANGING and cancels any Z-order change that we did not
/// request ourselves (e.g. the raise that normally happens when a window is clicked/activated).
///
/// We deliberately do NOT re-parent into Progman/WorkerW: a cross-process parent/owner relationship
/// would tie the widgets' lifetime to explorer.exe and get them destroyed when Explorer restarts.
/// Instead, the "TaskbarCreated" broadcast tells us Explorer came back and we simply re-apply the order.
/// </summary>
internal sealed class DesktopHostService : IDisposable
{
    private sealed class Entry
    {
        public required IntPtr Hwnd;
        public required DesktopLayer Layer;
        public required WindowSubclass Subclass;
        public required WindowMessageHandler Handler;
    }

    private readonly Dictionary<IntPtr, Entry> _entries = new();
    private readonly uint _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
    [ThreadStatic] private static bool t_allowZOrderChange;
    private long _lastDeactivationTicks;

    /// <summary>Raised (on the UI thread) when the application as a whole loses activation.</summary>
    public event Action? AppDeactivated;

    /// <summary>Raised after Explorer restarted and windows were re-pinned.</summary>
    public event Action? ShellRestarted;

    public IntPtr DesktopHostWindow { get; private set; }
    public string DesktopHostDescription { get; private set; } = "unknown";

    public DesktopHostService()
    {
        DetectDesktopHost();
    }

    /// <summary>Converts a WinUI window into a frameless desktop-layer window and pins it.</summary>
    public void Attach(Window window, DesktopLayer layer, bool roundedCorners = false)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_entries.ContainsKey(hwnd)) return;

        ConfigureWindowStyles(window, hwnd, roundedCorners);

        var subclass = new WindowSubclass(hwnd);
        WindowMessageHandler handler = (h, msg, wParam, lParam) => HandleMessage(h, msg, wParam, lParam);
        subclass.AddHandler(handler);
        _entries[hwnd] = new Entry { Hwnd = hwnd, Layer = layer, Subclass = subclass, Handler = handler };
        PinToBottom(hwnd);
    }

    public void Detach(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_entries.Remove(hwnd, out var entry))
            entry.Subclass.Dispose();
    }

    public bool IsOurWindow(IntPtr hwnd) => _entries.ContainsKey(hwnd);

    public WindowSubclass? GetSubclass(IntPtr hwnd) => _entries.TryGetValue(hwnd, out var e) ? e.Subclass : null;

    /// <summary>
    /// Re-establishes the desktop-layer ordering: panel above tiles, everything at the bottom.
    /// Safe to call often (it is cheap) — used after showing windows and after Explorer restarts.
    /// </summary>
    public void EnsureOrder()
    {
        // Pushing windows to HWND_BOTTOM one after another leaves the last pushed window lowest,
        // so push the panel first and the tiles afterwards.
        foreach (var e in _entries.Values.Where(e => e.Layer == DesktopLayer.Panel))
            PinToBottom(e.Hwnd);
        foreach (var e in _entries.Values.Where(e => e.Layer == DesktopLayer.Tiles))
            PinToBottom(e.Hwnd);
    }

    /// <summary>Handles messages arriving at the hidden main window (broadcasts such as TaskbarCreated).</summary>
    public MessageResult HandleHostMessage(uint msg)
    {
        if (msg == _taskbarCreatedMsg)
        {
            Log.Info("Explorer restart detected (TaskbarCreated) — re-attaching desktop host");
            DetectDesktopHost();
            EnsureOrder();
            ShellRestarted?.Invoke();
            return MessageResult.HandledZero;
        }
        return MessageResult.Unhandled;
    }

    /// <summary>Debug aid: FOLDERBOX_DEBUG_TOPMOST=1 pins windows to the top instead (for screenshots/tests).</summary>
    private static readonly bool s_debugTopmost = Environment.GetEnvironmentVariable("FOLDERBOX_DEBUG_TOPMOST") == "1";

    private void PinToBottom(IntPtr hwnd)
    {
        t_allowZOrderChange = true;
        try
        {
            SetWindowPos(hwnd, s_debugTopmost ? HWND_TOPMOST : HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        finally
        {
            t_allowZOrderChange = false;
        }
    }

    private MessageResult HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
            {
                if (t_allowZOrderChange) return MessageResult.Unhandled;
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((wp.flags & SWP_NOZORDER) == 0)
                {
                    // Somebody (usually activation) wants to raise us. Refuse: we live on the desktop layer.
                    wp.flags |= SWP_NOZORDER;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
                return MessageResult.Unhandled;
            }
            case WM_ACTIVATEAPP:
            {
                // WM_ACTIVATEAPP(FALSE) reaches the window that was active; whichever of ours that is,
                // report it once (windows may receive it in the same burst).
                if (wParam == IntPtr.Zero)
                {
                    var now = Environment.TickCount64;
                    if (now - _lastDeactivationTicks > 50)
                    {
                        _lastDeactivationTicks = now;
                        AppDeactivated?.Invoke();
                    }
                }
                return MessageResult.Unhandled;
            }
        }
        return MessageResult.Unhandled;
    }

    private static void ConfigureWindowStyles(Window window, IntPtr hwnd, bool roundedCorners)
    {
        // Frameless via AppWindow: no title bar, no border, not resizable.
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = false;
        }
        window.AppWindow.IsShownInSwitchers = false;

        var ex = GetExStyle(hwnd);
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        SetExStyle(hwnd, ex);

        var style = GetStyle(hwnd);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetStyle(hwnd, style);

        // Windows 11 would otherwise round the (transparent) window corners and draw a border/shadow.
        int corner = roundedCorners ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        if (!roundedCorners)
        {
            uint noBorder = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));
            // Extend the DWM frame over the whole client area: pixels with alpha 0 then show the desktop
            // through (together with the transparent composition backdrop this yields a see-through window).
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        int noTransitions = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref noTransitions, sizeof(int));

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Locates the window that hosts the desktop icons (SHELLDLL_DefView). This is Progman on a
    /// plain Windows 11 desktop, or a WorkerW when a wallpaper engine / "show desktop" state moved it.
    /// Only used for diagnostics and for verifying our windows sit above it.
    /// </summary>
    public void DetectDesktopHost()
    {
        try
        {
            var progman = FindWindowW("Progman", null);
            var shell = GetShellWindow();
            IntPtr host = IntPtr.Zero;
            string desc = "none";

            if (progman != IntPtr.Zero && FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                host = progman;
                desc = "Progman";
            }
            else
            {
                IntPtr worker = IntPtr.Zero;
                while ((worker = FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                {
                    if (FindWindowExW(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    {
                        host = worker;
                        desc = "WorkerW";
                        break;
                    }
                }
            }

            DesktopHostWindow = host;
            DesktopHostDescription = desc;
            Log.Info($"Desktop host: {desc} (0x{host.ToInt64():X}), Progman=0x{progman.ToInt64():X}, ShellWindow=0x{shell.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Log.Error("DetectDesktopHost failed", ex);
        }
    }

    /// <summary>Diagnostic: is the given window below every non-shell top-level window? (Used in logging/tests.)</summary>
    public bool IsAtDesktopLayer(IntPtr hwnd)
    {
        // Walk downwards from our window; the only visible windows below us should be ours or the shell's.
        var next = GetWindow(hwnd, GW_HWNDNEXT);
        while (next != IntPtr.Zero)
        {
            if (IsWindowVisible(next) && !_entries.ContainsKey(next))
            {
                var cls = GetClassName(next);
                if (cls is not ("Progman" or "WorkerW")) return false;
            }
            next = GetWindow(next, GW_HWNDNEXT);
        }
        return true;
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Subclass.Dispose();
        _entries.Clear();
    }
}
