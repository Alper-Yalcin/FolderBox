using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FolderBox.App.Shell;

/// <summary>
/// Desktop acrylic that stays "on" while the window is inactive. The stock <see cref="DesktopAcrylicBackdrop"/>
/// switches to a solid fallback colour as soon as the window loses focus, which makes FolderBox windows
/// look different when the user clicks the desktop; here the configuration always reports an active
/// window, and only the theme is tracked.
/// </summary>
internal sealed class PersistentAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private FrameworkElement? _themeSource;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported()) return;

        _configuration = new SystemBackdropConfiguration { IsInputActive = true, IsHighContrast = false };
        _themeSource = xamlRoot.Content as FrameworkElement;
        ApplyTheme();
        if (_themeSource is not null) _themeSource.ActualThemeChanged += OnThemeChanged;

        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // The framework reports activation/theme changes here; keep the acrylic active regardless.
        if (_configuration is not null) _configuration.IsInputActive = true;
        ApplyTheme();
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => ApplyTheme();

    private void ApplyTheme()
    {
        if (_configuration is null) return;
        _configuration.Theme = _themeSource?.ActualTheme switch
        {
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default,
        };
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (_themeSource is not null) _themeSource.ActualThemeChanged -= OnThemeChanged;
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
