using FolderBox.Core.Models;
using Microsoft.UI.Xaml;

namespace FolderBox.App.Services;

/// <summary>
/// Applies the theme (System / Light / Dark) to every FolderBox window root. With "System" the
/// XAML runtime follows the Windows app-mode setting automatically.
/// </summary>
internal sealed class ThemeService
{
    private readonly List<WeakReference<FrameworkElement>> _roots = new();
    private ThemeMode _mode = ThemeMode.System;

    public ThemeMode Mode => _mode;

    public event Action? ThemeChanged;

    public void Register(FrameworkElement root)
    {
        _roots.Add(new WeakReference<FrameworkElement>(root));
        root.RequestedTheme = ToElementTheme(_mode);
    }

    public void Apply(ThemeMode mode)
    {
        _mode = mode;
        var theme = ToElementTheme(mode);
        _roots.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var r in _roots)
        {
            if (r.TryGetTarget(out var root)) root.RequestedTheme = theme;
        }
        ThemeChanged?.Invoke();
    }

    private static ElementTheme ToElementTheme(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ElementTheme.Light,
        ThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
