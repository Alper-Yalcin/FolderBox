using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using FolderBox.Core.Models;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>
/// Reads the screen rectangles of the real desktop icons (Explorer's SysListView32) so FolderBox
/// tiles can avoid them. Uses the classic LVM_GETITEMRECT cross-process technique; failures simply
/// yield an empty list (tiles then only avoid each other).
/// </summary>
internal sealed class DesktopIconService
{
    private const uint LVM_FIRST = 0x1000;
    private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const uint LVM_GETITEMRECT = LVM_FIRST + 14;
    private const uint LVM_GETITEMSPACING = LVM_FIRST + 51;
    private const int LVIR_BOUNDS = 0;
    private const uint PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020;
    private const uint MEM_COMMIT = 0x1000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x04;
    private const int MaxIcons = 2000;

    private IReadOnlyList<PixelRect> _rects = Array.Empty<PixelRect>();
    private (int Cx, int Cy)? _spacing;
    private long _lastRefreshTicks;

    /// <summary>Screen (physical, virtual-screen) rectangles of all desktop icons from the last refresh.</summary>
    public IReadOnlyList<PixelRect> IconRects => _rects;

    /// <summary>Re-reads icon positions; cheap enough to call before every layout decision (throttled to 250 ms).</summary>
    public IReadOnlyList<PixelRect> Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - _lastRefreshTicks < 250) return _rects;
        _lastRefreshTicks = now;
        try
        {
            var listView = FindDesktopListView();
            _spacing = ReadSpacing(listView);
            _rects = ReadIconRects(listView);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read desktop icon positions: " + ex.Message);
            _rects = Array.Empty<PixelRect>();
        }
        return _rects;
    }

    /// <summary>Icon rectangles converted to logical coordinates relative to the given monitor.</summary>
    public IReadOnlyList<LogicalRect> GetBlockedRects(MonitorInfo monitor)
    {
        var list = new List<LogicalRect>();
        foreach (var r in _rects)
        {
            if (!r.IntersectsWith(monitor.Bounds)) continue;
            var tl = monitor.ToLogical(new PixelPoint(r.Left, r.Top));
            var br = monitor.ToLogical(new PixelPoint(r.Right, r.Bottom));
            list.Add(new LogicalRect(tl.X, tl.Y, br.X, br.Y));
        }
        return list;
    }

    private static IntPtr FindDesktopListView()
    {
        IntPtr defView = IntPtr.Zero;
        var progman = FindWindowW("Progman", null);
        if (progman != IntPtr.Zero) defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            IntPtr worker = IntPtr.Zero;
            while (defView == IntPtr.Zero && (worker = FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                defView = FindWindowExW(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowExW(defView, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>
    /// Windows' own desktop icon lattice on a monitor (logical, monitor-relative): cell = icon spacing,
    /// tile = cell, origin aligned with the real icons on that monitor. Null when Explorer's desktop
    /// list view is not available (FolderBox's own grid is used then).
    /// </summary>
    public GridSpec? GetDesktopGrid(MonitorInfo monitor)
    {
        if (_spacing is not { } sp || sp.Cx <= 0 || sp.Cy <= 0) return null;
        var cellW = sp.Cx / monitor.Scale;
        var cellH = sp.Cy / monitor.Scale;
        var (workLeft, workTop, _, _) = monitor.LogicalWorkArea;
        double originX = workLeft, originY = workTop;
        var icons = GetBlockedRects(monitor);
        if (icons.Count > 0)
        {
            // Align our lattice with the existing icons: same phase modulo the cell size.
            var minLeft = icons.Min(r => r.Left);
            var minTop = icons.Min(r => r.Top);
            originX = workLeft + Mod(minLeft - workLeft, cellW);
            originY = workTop + Mod(minTop - workTop, cellH);
        }
        return new GridSpec(cellW, cellH, originX, originY, cellW, cellH);

        static double Mod(double a, double m) => ((a % m) + m) % m;
    }

    private static (int Cx, int Cy)? ReadSpacing(IntPtr listView)
    {
        if (listView == IntPtr.Zero) return null;
        var v = SendMessageW(listView, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt64();
        int cx = (int)(v & 0xFFFF), cy = (int)((v >> 16) & 0xFFFF);
        return cx > 0 && cy > 0 ? (cx, cy) : null;
    }

    private static IReadOnlyList<PixelRect> ReadIconRects(IntPtr listView)
    {
        if (listView == IntPtr.Zero) return Array.Empty<PixelRect>();

        var count = (int)SendMessageW(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        if (count <= 0) return Array.Empty<PixelRect>();
        count = Math.Min(count, MaxIcons);

        GetWindowThreadProcessId(listView, out var pid);
        var process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException("OpenProcess(explorer) failed: " + Marshal.GetLastWin32Error());

        var result = new List<PixelRect>(count);
        IntPtr remote = IntPtr.Zero;
        try
        {
            var size = (uint)Marshal.SizeOf<RECT>();
            remote = VirtualAllocEx(process, IntPtr.Zero, size, MEM_COMMIT, PAGE_READWRITE);
            if (remote == IntPtr.Zero) throw new InvalidOperationException("VirtualAllocEx failed");

            var local = Marshal.AllocHGlobal((int)size);
            try
            {
                // The list view's client origin in screen coordinates (it spans the whole virtual desktop).
                var origin = new POINT();
                ClientToScreen(listView, ref origin);

                for (int i = 0; i < count; i++)
                {
                    var request = new RECT { Left = LVIR_BOUNDS };
                    Marshal.StructureToPtr(request, local, false);
                    if (!WriteProcessMemory(process, remote, local, size, out _)) continue;
                    if (SendMessageW(listView, LVM_GETITEMRECT, new IntPtr(i), remote) == IntPtr.Zero) continue;
                    if (!ReadProcessMemory(process, remote, local, size, out _)) continue;
                    var rect = Marshal.PtrToStructure<RECT>(local);
                    if (rect.Width <= 0 || rect.Height <= 0) continue;
                    result.Add(new PixelRect(origin.X + rect.Left, origin.Y + rect.Top, rect.Width, rect.Height));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(local);
            }
        }
        finally
        {
            if (remote != IntPtr.Zero) VirtualFreeEx(process, remote, 0, MEM_RELEASE);
            CloseHandle(process);
        }
        Log.Debug($"Desktop icons: {result.Count} rectangles read");
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
}
