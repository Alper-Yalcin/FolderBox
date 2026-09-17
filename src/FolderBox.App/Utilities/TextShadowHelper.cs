using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace FolderBox.App.Utilities;

/// <summary>
/// Draws a soft drop shadow behind a TextBlock (same idea as desktop icon labels) using a
/// composition DropShadow masked with the text's alpha mask. Keeps white labels readable on any wallpaper.
/// </summary>
internal static class TextShadowHelper
{
    public static void Attach(TextBlock text, Canvas shadowHost)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(shadowHost).Compositor;
        var shadow = compositor.CreateDropShadow();
        shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        shadow.BlurRadius = 6f;
        shadow.Opacity = 0.85f;
        shadow.Offset = new Vector3(0f, 1f, 0f);
        shadow.Mask = text.GetAlphaMask();

        var visual = compositor.CreateSpriteVisual();
        visual.Shadow = shadow;
        visual.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
        ElementCompositionPreview.SetElementChildVisual(shadowHost, visual);
    }

    public static void Update(TextBlock text, Canvas shadowHost)
    {
        if (ElementCompositionPreview.GetElementChildVisual(shadowHost) is SpriteVisual visual)
        {
            visual.Size = new Vector2((float)text.ActualWidth, (float)text.ActualHeight);
            if (visual.Shadow is DropShadow ds) ds.Mask = text.GetAlphaMask();
        }
        else
        {
            Attach(text, shadowHost);
        }
    }
}
