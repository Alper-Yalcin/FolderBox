using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

internal sealed record TrayMenuItem(int Id, string? Text, bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new(0, null);
}

/// <summary>
/// Notification-area icon with a native popup menu. Uses Shell_NotifyIcon on a host window whose
/// messages we observe through a <see cref="WindowSubclass"/>. Native menus were chosen on purpose:
/// they behave exactly like every other tray menu on the system.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly IntPtr _hwnd;
    private readonly WindowSubclass _subclass;
    private readonly WindowMessageHandler _handler;
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private IntPtr _hIcon;
    private bool _added;
    private bool _disposed;

    /// <summary>Supplies the menu to show; called on every right-click so check states are fresh.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }
    /// <summary>Invoked with the chosen menu id.</summary>
    public Action<int>? CommandInvoked { get; set; }
    /// <summary>Invoked on left click / Enter.</summary>
    public Action? Activated { get; set; }

    public TrayIcon(IntPtr hwnd, WindowSubclass subclass, string tooltip)
    {
        _hwnd = hwnd;
        _subclass = subclass;
        _handler = HandleMessage;
        _subclass.AddHandler(_handler);
        _hIcon = LoadAppIcon();
        Add(tooltip);
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                // Icon index 0 of our own executable (the embedded FolderBox.ico).
                var h = ExtractIconW(IntPtr.Zero, exe, 0);
                if (h != IntPtr.Zero && h.ToInt64() > 1) return h;
            }
        }
        catch { }
        return IconService.LoadStockFolderIcon(small: true);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private void Add(string tooltip)
    {
        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _hIcon;
        data.szTip = tooltip;
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (_added)
        {
            data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref data);
        }
        else
        {
            Log.Warn("Tray icon could not be added");
        }
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private MessageResult HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreated)
        {
            // Explorer restarted: the notification area forgot us.
            _added = false;
            Add("FolderBox");
            return MessageResult.Unhandled; // let other handlers (desktop host) see it too
        }
        if (msg != CallbackMessage) return MessageResult.Unhandled;

        var evt = (uint)LoWord(lParam);
        switch (evt)
        {
            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                ShowMenu(LoWord(wParam), HiWord(wParam));
                return MessageResult.HandledZero;
            case NIN_SELECT:
            case NIN_KEYSELECT:
            case WM_LBUTTONDBLCLK:
                Activated?.Invoke();
                return MessageResult.HandledZero;
        }
        return MessageResult.Unhandled;
    }

    private void ShowMenu(int x, int y)
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;

        var menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.Text is null)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                var flags = MF_STRING;
                if (item.Checked) flags |= MF_CHECKED;
                if (!item.Enabled) flags |= MF_GRAYED;
                AppendMenuW(menu, flags, new UIntPtr((uint)item.Id), item.Text);
            }

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN, x, y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0, IntPtr.Zero, IntPtr.Zero); // classic tray-menu quirk
            if (cmd > 0) CommandInvoked?.Invoke(cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subclass.RemoveHandler(_handler);
        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
    }
}
