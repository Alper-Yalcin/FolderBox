using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054 // DllImport is fine here; keeps the interop layer compact.
#pragma warning disable CS0649

namespace FolderBox.App.Shell;

/// <summary>Hand-written Win32 declarations used by the shell/desktop integration layer.</summary>
internal static partial class NativeMethods
{
    // ---------------------------------------------------------------- structs

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    // ---------------------------------------------------------------- constants

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GWLP_HWNDPARENT = -8;

    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOPMOST = 0x00000008;
    public const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_MINIMIZEBOX = 0x00020000;
    public const long WS_MAXIMIZEBOX = 0x00010000;
    public const long WS_SYSMENU = 0x00080000;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_WINDOWPOSCHANGED = 0x0047;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_INITMENUPOPUP = 0x0117;
    public const uint WM_DRAWITEM = 0x002B;
    public const uint WM_MEASUREITEM = 0x002C;
    public const uint WM_MENUCHAR = 0x0120;
    public const uint WM_MENUSELECT = 0x011F;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_APP = 0x8000;
    public const uint WM_USER = 0x0400;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_NCACTIVATE = 0x0086;

    public const int WA_INACTIVE = 0;
    public const int MA_NOACTIVATE = 3;
    public const int HTTRANSPARENT = -1;

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;

    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;

    public const uint GW_HWNDFIRST = 0;
    public const uint GW_HWNDLAST = 1;
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;
    public const uint GW_OWNER = 4;

    public const int DWMWA_NCRENDERING_POLICY = 2;
    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    public const int DWMNCRP_DISABLED = 1;

    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1;
    public const uint NIF_ICON = 2;
    public const uint NIF_TIP = 4;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_BYPOSITION = 0x0400;
    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RIGHTALIGN = 0x0008;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_LEFTBUTTON = 0x0000;
    public const uint TPM_NONOTIFY = 0x0080;

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_TYPENAME = 0x000000400;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    public const uint SIIGBF_RESIZETOFIT = 0x00;
    public const uint SIIGBF_BIGGERSIZEOK = 0x01;
    public const uint SIIGBF_MEMORYONLY = 0x02;
    public const uint SIIGBF_ICONONLY = 0x04;
    public const uint SIIGBF_THUMBNAILONLY = 0x08;
    public const uint SIIGBF_INCACHEONLY = 0x10;
    public const uint SIIGBF_SCALEUP = 0x100;

    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;

    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB = 0;

    public const int SIID_FOLDER = 3;
    public const int SIID_FOLDEROPEN = 4;
    public const uint SHGSI_ICON = 0x000000100;
    public const uint SHGSI_LARGEICON = 0;
    public const uint SHGSI_SMALLICON = 1;

    public const uint CMF_NORMAL = 0x00000000;
    public const uint CMF_EXPLORE = 0x00000004;
    public const uint CMF_CANRENAME = 0x00000010;
    public const uint CMF_EXTENDEDVERBS = 0x00000100;
    public const uint CMF_ITEMMENU = 0x00000080;
    public const uint CMIC_MASK_UNICODE = 0x00004000;
    public const uint CMIC_MASK_PTINVOKE = 0x20000000;
    public const uint CMIC_MASK_FLAG_NO_UI = 0x00000400;
    public const uint GCS_VERBW = 0x00000004;

    public const int MDT_EFFECTIVE_DPI = 0;

    public const uint FOF_SILENT = 0x0004;
    public const uint FOF_RENAMEONCOLLISION = 0x0008;
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_ALLOWUNDO = 0x0040;
    public const uint FOF_NOCONFIRMMKDIR = 0x0200;
    public const uint FOF_NOERRORUI = 0x0400;
    public const uint FOF_WANTNUKEWARNING = 0x4000;
    public const uint FOFX_ADDUNDORECORD = 0x20000000;
    public const uint FOFX_RECYCLEONDELETE = 0x00080000;
    public const uint FOFX_SHOWELEVATIONPROMPT = 0x00040000;

    public const uint FOS_PICKFOLDERS = 0x00000020;
    public const uint FOS_FORCEFILESYSTEM = 0x00000040;
    public const uint FOS_PATHMUSTEXIST = 0x00000800;
    public const uint FOS_NOCHANGEDIR = 0x00000008;
    public const uint FOS_DONTADDTORECENT = 0x02000000;
    public const uint FOS_ALLOWMULTISELECT = 0x00000200;
    public const uint FOS_FILEMUSTEXIST = 0x00001000;

    public const int SIGDN_FILESYSPATH = unchecked((int)0x80058000);
    public const int SIGDN_NORMALDISPLAY = 0;

    public const uint SFGAO_FILESYSTEM = 0x40000000;

    public const int VK_LBUTTON = 0x01;
    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;

    public const int E_ABORT = unchecked((int)0x80004004);
    public const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);
    public const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    // ---------------------------------------------------------------- delegates

    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // ---------------------------------------------------------------- user32

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImageW(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    public static extern bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    public const uint MB_YESNOCANCEL = 0x00000003;
    public const uint MB_ICONQUESTION = 0x00000020;
    public const uint MB_DEFBUTTON1 = 0x00000000;
    public const uint MB_SETFOREGROUND = 0x00010000;
    public const int IDYES = 6;
    public const int IDNO = 7;
    public const int IDCANCEL = 2;

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTSIZE = 0x0040;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const uint LR_SHARED = 0x8000;

    // ---------------------------------------------------------------- comctl32

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // ---------------------------------------------------------------- dwmapi

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    // ---------------------------------------------------------------- shcore

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ---------------------------------------------------------------- shell32

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", PreserveSig = false)]
    public static extern void SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetStockIconInfo(int siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string psz1, string psz2);

    // ---------------------------------------------------------------- gdi32

    [DllImport("gdi32.dll")]
    public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    // ---------------------------------------------------------------- CoreMessaging

    [DllImport("CoreMessaging.dll")]
    public static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

    // ---------------------------------------------------------------- helpers

    public static long GetExStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
    public static void SetExStyle(IntPtr hwnd, long value) => SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(value));
    public static long GetStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
    public static void SetStyle(IntPtr hwnd, long value) => SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(value));

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    public static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public static int LoWord(IntPtr v) => unchecked((short)(v.ToInt64() & 0xFFFF));
    public static int HiWord(IntPtr v) => unchecked((short)((v.ToInt64() >> 16) & 0xFFFF));
}
