using FolderBox.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace FolderBox.App.Views;

/// <summary>
/// Vector FolderBox folder glyph with selectable design (see <see cref="IconStyles.Designs"/>) and
/// colour (<see cref="IconStyles.Colors"/> name or #RRGGBB). Crisp at any DPI.
/// </summary>
public sealed partial class FolderBoxIcon : UserControl
{
    public static readonly DependencyProperty DesignProperty =
        DependencyProperty.Register(nameof(Design), typeof(string), typeof(FolderBoxIcon), new PropertyMetadata(IconStyles.DefaultDesign, (d, _) => ((FolderBoxIcon)d).Apply()));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(nameof(Tint), typeof(string), typeof(FolderBoxIcon), new PropertyMetadata(IconStyles.DefaultColor, (d, _) => ((FolderBoxIcon)d).Apply()));

    public string Design
    {
        get => (string)GetValue(DesignProperty);
        set => SetValue(DesignProperty, value);
    }

    /// <summary>Colour name from the catalog, "Accent", or #RRGGBB.</summary>
    public string Tint
    {
        get => (string)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public FolderBoxIcon()
    {
        InitializeComponent();
        Apply();
    }

    private void Apply()
    {
        var design = IconStyles.NormalizeDesign(Design);
        var baseColor = IconStyles.ResolveColor(Tint);
        var dark = new SolidColorBrush(IconStyles.Darken(baseColor, 0.22));
        var light = new SolidColorBrush(IconStyles.Lighten(baseColor, 0.18));
        var mid = new SolidColorBrush(baseColor);

        DesignFlap.Visibility = design == "Flap" ? Visibility.Visible : Visibility.Collapsed;
        DesignModern.Visibility = design == "Modern" ? Visibility.Visible : Visibility.Collapsed;
        DesignBox.Visibility = design == "Box" ? Visibility.Visible : Visibility.Collapsed;
        DesignTab.Visibility = design == "Tab" ? Visibility.Visible : Visibility.Collapsed;
        DesignBubble.Visibility = design == "Bubble" ? Visibility.Visible : Visibility.Collapsed;

        FlapBack.Fill = dark; FlapFront.Fill = light;
        ModernBack.Fill = dark; ModernFront.Fill = light;
        BoxBody.Fill = dark; BoxLid.Fill = light;
        TabBack.Fill = dark; TabFront.Fill = light;
        BubbleBack.Fill = mid; BubbleGlyphBack.Fill = dark;
    }
}
