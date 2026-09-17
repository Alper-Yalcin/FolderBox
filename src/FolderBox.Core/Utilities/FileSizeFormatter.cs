using System.Globalization;

namespace FolderBox.Core.Utilities;

public static class FileSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return string.Empty;
        if (bytes < 1024) return bytes + " B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var digits = value >= 100 ? 0 : 1;
        return value.ToString("F" + digits, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
