using Microsoft.UI.Xaml;

namespace FolderBox.App.Views;

/// <summary>Tiny helpers for x:Bind function bindings.</summary>
public static class Bind
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CardFill = new(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CardStrokeBrush = new(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    private static Windows.UI.Color Accent()
    {
        try
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SystemAccentColorLight2", out var c) && c is Windows.UI.Color color) return color;
        }
        catch { }
        return Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x80, 0xFF);
    }

    /// <summary>Card background: subtle glass, accent-tinted when selected.</summary>
    public static Microsoft.UI.Xaml.Media.Brush CardBackground(bool selected)
    {
        if (!selected) return CardFill;
        var a = Accent();
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x3A, a.R, a.G, a.B));
    }

    /// <summary>Card border: hairline, accent when selected.</summary>
    public static Microsoft.UI.Xaml.Media.Brush CardStroke(bool selected)
    {
        if (!selected) return CardStrokeBrush;
        var a = Accent();
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xC8, a.R, a.G, a.B));
    }
}
