using FolderBox.App.Services;
using FolderBox.App.Shell;
using FolderBox.App.Views;
using FolderBox.Core.Logging;
using FolderBox.Core.Services;
using Microsoft.UI.Xaml;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App;

/// <summary>
/// Composition root. FolderBox has no "main" UI: a hidden host window keeps the XAML runtime alive,
/// owns the tray icon and receives system broadcasts; all visible UI are desktop tiles, the panel
/// and the small management / settings windows.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\FolderBox.SingleInstance";
    private const int TrayAddFolder = 1, TrayManage = 2, TraySettings = 3, TrayShowAll = 4, TrayHideAll = 5, TrayLockAll = 6, TrayExit = 7;

    private static Mutex? s_instanceMutex;
    private readonly uint _showManageMessage = RegisterWindowMessageW("FolderBox.ShowManage");
    private readonly uint _showSettingsMessage = RegisterWindowMessageW("FolderBox.ShowSettings");
    private readonly uint _newWidgetMessage = RegisterWindowMessageW("FolderBox.NewWidget");
    private string? _pendingNewFolderLocation;

    private Window? _hostWindow;
    private WindowSubclass? _hostSubclass;
    private PersistenceService? _persistence;
    private MonitorService? _monitors;
    private DesktopHostService? _desktopHost;
    private IconService? _icons;
    private ThemeService? _theme;
    private WidgetManager? _widgets;
    private TrayIcon? _tray;
    private ManageFoldersWindow? _manageWindow;
    private SettingsWindow? _settingsWindow;
    private bool _exiting;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("AppDomain unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };
    }

    /// <summary>
    /// FolderBox launches other programs, so its environment must not carry Electron/VS Code process
    /// variables inherited from whoever started it (a terminal inside VS Code, a build task, ...):
    /// with ELECTRON_RUN_AS_NODE=1 an Electron app such as VS Code starts headless and exits silently.
    /// </summary>
    private static void ScrubInheritedLauncherEnvironment()
    {
        var removed = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key as string;
            if (name is null) continue;
            if (name.StartsWith("ELECTRON_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("VSCODE_", StringComparison.OrdinalIgnoreCase))
                removed.Add(name);
        }
        foreach (var name in removed) Environment.SetEnvironmentVariable(name, null);
        if (removed.Count > 0) Log.Info($"Removed inherited launcher environment: {string.Join(", ", removed)}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _persistence = new PersistenceService();
        Log.Initialize(_persistence.LogDirectory);
        Log.Info($"FolderBox starting (pid {Environment.ProcessId}, {Environment.OSVersion}, args: {Environment.CommandLine})");
        ScrubInheritedLauncherEnvironment();

        // "--new <path>" comes from Explorer's New > FolderBox menu; <path> is the file the shell would
        // have created, so its directory is where the user right-clicked.
        var cmdArgs = Environment.GetCommandLineArgs();
        var newIndex = Array.FindIndex(cmdArgs, a => string.Equals(a, "--new", StringComparison.OrdinalIgnoreCase));
        if (newIndex >= 0 && newIndex + 1 < cmdArgs.Length)
        {
            try { _pendingNewFolderLocation = Path.GetDirectoryName(cmdArgs[newIndex + 1]); } catch { }
        }

        s_instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (_pendingNewFolderLocation is not null)
            {
                // Hand the request to the running instance through a small file + broadcast.
                try { File.WriteAllText(PendingNewPath, _pendingNewFolderLocation); } catch (Exception ex) { Log.Warn("Could not write pending request: " + ex.Message); }
                PostMessageW(HWND_BROADCAST, _newWidgetMessage, IntPtr.Zero, IntPtr.Zero);
                Exit();
                return;
            }
            // "FolderBox.exe --settings" opens Settings in the running instance; anything else opens Manage Folders.
            var wantSettings = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));
            Log.Info($"Another instance is running; asking it to show the {(wantSettings ? "settings" : "manage")} window and exiting");
            PostMessageW(HWND_BROADCAST, wantSettings ? _showSettingsMessage : _showManageMessage, IntPtr.Zero, IntPtr.Zero);
            Exit();
            return;
        }

        try
        {
            Bootstrap();
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error during startup", ex);
            throw;
        }
    }

    private void Bootstrap()
    {
        // Hidden host window: anchors the XAML runtime, receives broadcasts, hosts the tray icon.
        _hostWindow = new Window { Title = "FolderBox Host" };
        var hostHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_hostWindow);
        _hostWindow.AppWindow.IsShownInSwitchers = false;
        SetExStyle(hostHwnd, (GetExStyle(hostHwnd) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        _hostSubclass = new WindowSubclass(hostHwnd);

        _monitors = new MonitorService();
        _monitors.Refresh();
        _desktopHost = new DesktopHostService();
        _icons = new IconService(_hostWindow.DispatcherQueue);
        _theme = new ThemeService();
        _widgets = new WidgetManager(_persistence!, _monitors, _desktopHost, _icons, _theme, _hostWindow.DispatcherQueue);
        _theme.Apply(_widgets.Settings.Theme);

        _hostSubclass.AddHandler(HandleHostMessage);

        _widgets.Initialize();
        Log.Info($"Desktop host attach: {_desktopHost.DesktopHostDescription}");

        _tray = new TrayIcon(hostHwnd, _hostSubclass, "FolderBox")
        {
            MenuProvider = BuildTrayMenu,
            CommandInvoked = OnTrayCommand,
            Activated = ShowManageWindow,
        };

        if (_widgets.Settings.AddToNewMenu && !ShellNewIntegration.IsRegistered())
            ShellNewIntegration.Register();
        StartupService.Synchronize(_widgets.Settings.StartWithWindows);
        if (!_widgets.Settings.DesktopShortcutCreated)
        {
            // One-time: give the user a launcher on the Desktop (it opens the admin panel when FolderBox runs).
            if (DesktopShortcutService.Create())
            {
                var s = _widgets.Settings.Clone();
                s.DesktopShortcutCreated = true;
                _widgets.ApplySettings(s);
            }
        }

        if (_pendingNewFolderLocation is not null)
        {
            var location = _pendingNewFolderLocation;
            _pendingNewFolderLocation = null;
            _hostWindow.DispatcherQueue.TryEnqueue(() => AddFolderNearCursor(location));
        }
        else if (_widgets.Widgets.Count == 0)
        {
            ShowManageWindow();
        }
    }

    private string PendingNewPath => Path.Combine(_persistence!.DataDirectory, "pending-new.txt");

    /// <summary>"New > FolderBox": create an empty FolderBox right away, placed next to the mouse cursor, ready to be named.</summary>
    private void AddFolderNearCursor(string? location)
    {
        _ = location; // the click location is not needed: new FolderBoxes live under WidgetManager.ManagedRoot
        GetCursorPos(out var cursor);
        _widgets!.CreateNewFolderBox(new Core.Models.PixelPoint(cursor.X, cursor.Y));
    }

    // ------------------------------------------------------------------ host window messages

    private MessageResult HandleHostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _showManageMessage)
        {
            ShowManageWindow();
            return MessageResult.HandledZero;
        }
        if (msg == _showSettingsMessage)
        {
            ShowSettingsWindow();
            return MessageResult.HandledZero;
        }
        if (msg == _newWidgetMessage)
        {
            string? location = null;
            try
            {
                if (File.Exists(PendingNewPath)) { location = File.ReadAllText(PendingNewPath).Trim(); File.Delete(PendingNewPath); }
            }
            catch (Exception ex) { Log.Warn("Could not read pending request: " + ex.Message); }
            AddFolderNearCursor(location);
            return MessageResult.HandledZero;
        }
        if (msg == WM_CLOSE)
        {
            // The hidden host window anchors the XAML runtime; never let a stray WM_CLOSE tear it down.
            return MessageResult.HandledZero;
        }
        if (msg == WM_DISPLAYCHANGE || (msg == WM_SETTINGCHANGE && wParam.ToInt64() == 0x2F /* SPI_SETWORKAREA */))
        {
            Log.Debug($"Display/work-area change message 0x{msg:X} wParam={wParam}");
            _monitors?.NotifyDisplayChanged();
            return MessageResult.Unhandled;
        }
        return _desktopHost?.HandleHostMessage(msg) ?? MessageResult.Unhandled;
    }

    // ------------------------------------------------------------------ tray

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var w = _widgets!;
        return new[]
        {
            new TrayMenuItem(TrayAddFolder, "Add Folder…"),
            new TrayMenuItem(TrayManage, "Manage Folders"),
            new TrayMenuItem(TraySettings, "Settings"),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayShowAll, "Show All", Enabled: w.Widgets.Count > 0),
            new TrayMenuItem(TrayHideAll, "Hide All", Enabled: w.Widgets.Count > 0 && !w.AllHidden),
            new TrayMenuItem(TrayLockAll, "Lock All Widgets", Checked: w.Settings.LockAllWidgets),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit"),
        };
    }

    private void OnTrayCommand(int id)
    {
        var w = _widgets!;
        switch (id)
        {
            case TrayAddFolder:
                w.AddFolderFromPicker(WinRT.Interop.WindowNative.GetWindowHandle(_hostWindow!));
                break;
            case TrayManage:
                ShowManageWindow();
                break;
            case TraySettings:
                ShowSettingsWindow();
                break;
            case TrayShowAll:
                w.ShowAll();
                break;
            case TrayHideAll:
                w.HideAll();
                break;
            case TrayLockAll:
            {
                var s = w.Settings.Clone();
                s.LockAllWidgets = !s.LockAllWidgets;
                w.ApplySettings(s);
                break;
            }
            case TrayExit:
                Shutdown();
                break;
        }
    }

    private void ShowManageWindow()
    {
        if (_manageWindow is null)
        {
            _manageWindow = new ManageFoldersWindow(_widgets!, ShowSettingsWindow);
            _manageWindow.Closed += (_, _) => _manageWindow = null;
        }
        _manageWindow.BringToFront();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_widgets!, _persistence!.DataDirectory);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.BringToFront();
    }

    // ------------------------------------------------------------------ shutdown / errors

    private void Shutdown()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("FolderBox exiting");
        try
        {
            _tray?.Dispose();
            _widgets?.Dispose();
            _manageWindow?.Close();
            _settingsWindow?.Close();
            _hostSubclass?.Dispose();
            _desktopHost?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error during shutdown", ex);
        }
        finally
        {
            s_instanceMutex?.ReleaseMutex();
            Exit();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled XAML exception: " + e.Message, e.Exception);
        // One misbehaving widget must not take the whole desktop layer down.
        e.Handled = true;
        try { _widgets?.SaveNow(); } catch { }
    }
}
