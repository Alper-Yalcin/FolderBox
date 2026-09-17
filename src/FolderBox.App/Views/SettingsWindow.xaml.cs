using FolderBox.App.Services;
using FolderBox.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FolderBox.App.Views;

/// <summary>Settings window. Every change is applied and persisted immediately.</summary>
internal sealed partial class SettingsWindow : Window
{
    private readonly WidgetManager _manager;
    private readonly string _dataDirectory;
    private bool _loading = true;

    public SettingsWindow(WidgetManager manager, string dataDirectory)
    {
        _manager = manager;
        _dataDirectory = dataDirectory;
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        manager.Theme.Register(Root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FolderBox.ico"));

        var scale = manager.Monitors.Primary.Scale;
        AppWindow.Resize(new SizeInt32((int)(600 * scale), (int)(640 * scale)));
        var m = manager.Monitors.Primary;
        var s = AppWindow.Size;
        AppWindow.Move(new PointInt32(m.WorkArea.Left + (m.WorkArea.Width - s.Width) / 2, m.WorkArea.Top + (m.WorkArea.Height - s.Height) / 2));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            p.IsMaximizable = false;
        }

        Load(manager.Settings);
        _loading = false;
    }

    private void Load(AppSettings s)
    {
        StartWithWindows.IsOn = s.StartWithWindows;
        Theme.SelectedIndex = (int)s.Theme;
        AlignToGrid.IsOn = s.AlignToGrid;
        UseDesktopGrid.IsOn = s.UseDesktopIconGrid;
        GridSizeRow.IsHitTestVisible = !s.UseDesktopIconGrid;
        GridSizeRow.Opacity = s.UseDesktopIconGrid ? 0.5 : 1.0;
        CloseOnOutside.IsOn = s.ClosePanelOnOutsideClick;
        ShowItemCount.IsOn = s.ShowItemCount;
        LockAll.IsOn = s.LockAllWidgets;
        AddToNewMenu.IsOn = s.AddToNewMenu;
        GridWidth.Value = s.GridWidth;
        GridHeight.Value = s.GridHeight;
        PanelWidth.Value = s.PanelWidth;
        PanelMaxHeight.Value = s.PanelMaxHeight;
        DataPathText.Text = $"Settings and layout are stored in {_dataDirectory}";
    }

    private void Setting_Changed(object sender, object e)
    {
        if (_loading) return;
        var s = _manager.Settings.Clone();
        s.StartWithWindows = StartWithWindows.IsOn;
        s.Theme = (ThemeMode)Math.Max(0, Theme.SelectedIndex);
        s.AlignToGrid = AlignToGrid.IsOn;
        s.UseDesktopIconGrid = UseDesktopGrid.IsOn;
        GridSizeRow.IsHitTestVisible = !s.UseDesktopIconGrid;
        GridSizeRow.Opacity = s.UseDesktopIconGrid ? 0.5 : 1.0;
        s.ClosePanelOnOutsideClick = CloseOnOutside.IsOn;
        s.ShowItemCount = ShowItemCount.IsOn;
        s.LockAllWidgets = LockAll.IsOn;
        s.AddToNewMenu = AddToNewMenu.IsOn;
        s.GridWidth = ToInt(GridWidth.Value, s.GridWidth);
        s.GridHeight = ToInt(GridHeight.Value, s.GridHeight);
        s.PanelWidth = ToInt(PanelWidth.Value, s.PanelWidth);
        s.PanelMaxHeight = ToInt(PanelMaxHeight.Value, s.PanelMaxHeight);
        _manager.ApplySettings(s);
    }

    private static int ToInt(double value, int fallback) => double.IsNaN(value) ? fallback : (int)Math.Round(value);

    public void BringToFront()
    {
        Activate();
        Shell.NativeMethods.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }
}
