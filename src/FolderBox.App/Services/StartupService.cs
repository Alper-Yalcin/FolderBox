using FolderBox.Core.Logging;
using Microsoft.Win32;

namespace FolderBox.App.Services;

/// <summary>"Start with Windows" via HKCU\...\Run (per-user, no elevation needed).</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FolderBox";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read startup registration: " + ex.Message);
            return false;
        }
    }

    /// <summary>True when the Run entry exists and points at the executable that is running now.</summary>
    public static bool IsCurrentRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            var exe = Environment.ProcessPath;
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(exe) && value.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Applies the setting at startup: registers (or re-registers after the exe moved) or removes the entry.</summary>
    public static void Synchronize(bool startWithWindows)
    {
        if (startWithWindows)
        {
            if (!IsCurrentRegistration()) SetEnabled(true);
        }
        else if (IsEnabled())
        {
            SetEnabled(false);
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" --startup");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info($"Startup registration {(enabled ? "enabled" : "disabled")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not update startup registration", ex);
            return false;
        }
    }
}
