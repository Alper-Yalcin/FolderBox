using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>Enumerates monitors (bounds, work area, DPI scale) via Win32. Refreshed on WM_DISPLAYCHANGE.</summary>
internal sealed class MonitorService
{
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get
        {
            if (_monitors.Count == 0) Refresh();
            return _monitors;
        }
    }

    public event Action? MonitorsChanged;

    public MonitorInfo Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors[0];

    public void Refresh()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT __, IntPtr ___) =>
        {
            var info = Describe(hMonitor);
            if (info is not null) list.Add(info);
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            // Should never happen, but never leave the app without a monitor model.
            list.Add(new MonitorInfo("\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0, true));
        }
        _monitors = list;
        Log.Debug("Monitors: " + string.Join("; ", list.Select(m => $"{m.Id} {m.Bounds} work={m.WorkArea} scale={m.Scale:0.00}{(m.IsPrimary ? " primary" : "")}")));
    }

    /// <summary>
    /// Re-enumerates monitors and raises <see cref="MonitorsChanged"/> only when the configuration
    /// (ids, bounds, work areas or DPI) really differs. WM_DISPLAYCHANGE is also broadcast for changes
    /// that do not affect layout, and reacting to those would needlessly close the panel.
    /// </summary>
    public void NotifyDisplayChanged()
    {
        var before = _monitors;
        Refresh();
        if (!SameConfiguration(before, _monitors))
            MonitorsChanged?.Invoke();
    }

    private static bool SameConfiguration(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b.FirstOrDefault(m => string.Equals(m.Id, x.Id, StringComparison.OrdinalIgnoreCase));
            if (y is null || x.Bounds != y.Bounds || x.WorkArea != y.WorkArea || Math.Abs(x.Scale - y.Scale) > 0.001 || x.IsPrimary != y.IsPrimary)
                return false;
        }
        return true;
    }

    public static MonitorInfo? Describe(IntPtr hMonitor)
    {
        var mi = new MONITORINFOEXW { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEXW>() };
        if (!GetMonitorInfoW(hMonitor, ref mi)) return null;
        double scale = 1.0;
        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;
        return new MonitorInfo(
            mi.szDevice,
            PixelRect.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
            PixelRect.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
            scale,
            (mi.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    public MonitorInfo? FindById(string id) =>
        Monitors.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public MonitorInfo FromPoint(int x, int y)
    {
        var h = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var described = Describe(h);
        return described is null ? Primary : (FindById(described.Id) ?? described);
    }

    public MonitorInfo FromWindow(IntPtr hwnd)
    {
        var h = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var described = Describe(h);
        return described is null ? Primary : (FindById(described.Id) ?? described);
    }
}
