using Windows.UI;

namespace FolderBox.App.Services;

/// <summary>Catalog of tile icon designs and colours. Names are what gets persisted per widget.</summary>
internal static class IconStyles
{
    public sealed record Design(string Name, string Label);
    public sealed record Swatch(string Name, string Label, Color? Color);

    public const string DefaultDesign = "Flap";
    public const string DefaultColor = "Accent";

    public static readonly IReadOnlyList<Design> Designs = new[]
    {
        new Design("Flap", "Flap (default)"),
        new Design("Modern", "Modern"),
        new Design("Box", "Box"),
        new Design("Tab", "Tab"),
        new Design("Bubble", "Bubble"),
    };

    public static readonly IReadOnlyList<Swatch> Colors = new[]
    {
        new Swatch("Accent", "Windows accent", null),
        new Swatch("Blue", "Blue", Color.FromArgb(255, 0x3B, 0x82, 0xF6)),
        new Swatch("Purple", "Purple", Color.FromArgb(255, 0x8B, 0x5C, 0xF6)),
        new Swatch("Pink", "Pink", Color.FromArgb(255, 0xEC, 0x48, 0x99)),
        new Swatch("Red", "Red", Color.FromArgb(255, 0xEF, 0x44, 0x44)),
        new Swatch("Orange", "Orange", Color.FromArgb(255, 0xF9, 0x73, 0x16)),
        new Swatch("Yellow", "Yellow", Color.FromArgb(255, 0xEA, 0xB3, 0x08)),
        new Swatch("Green", "Green", Color.FromArgb(255, 0x22, 0xC5, 0x5E)),
        new Swatch("Teal", "Teal", Color.FromArgb(255, 0x14, 0xB8, 0xA6)),
        new Swatch("Gray", "Gray", Color.FromArgb(255, 0x6B, 0x72, 0x80)),
    };

    public static string NormalizeDesign(string? name) =>
        Designs.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) ? Designs.First(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).Name : DefaultDesign;

    /// <summary>Resolves a colour name (or #RRGGBB) to the base colour of the icon.</summary>
    public static Color ResolveColor(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var swatch = Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (swatch is { Color: { } c }) return c;
            if (name.StartsWith('#') && name.Length == 7 && int.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return AccentColor();
    }

    public static Color AccentColor()
    {
        try
        {
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("SystemAccentColorLight1", out var c) && c is Color color) return color;
        }
        catch { }
        return Color.FromArgb(255, 0x8B, 0x5C, 0xF6);
    }

    /// <summary>Lighter / darker variants used for the two folder plates.</summary>
    public static Color Lighten(Color c, double amount) => Mix(c, Color.FromArgb(255, 255, 255, 255), amount);
    public static Color Darken(Color c, double amount) => Mix(c, Color.FromArgb(255, 0, 0, 0), amount);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
