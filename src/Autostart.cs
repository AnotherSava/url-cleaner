using Microsoft.Win32;

namespace UrlCleaner;

/// <summary>
/// Registers the app to start with Windows through the current user's Run key.
/// </summary>
public static class Autostart
{
    private const string AppName = "UrlCleaner";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Checks whether the app is registered to start with Windows.
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>
    /// Adds or removes the app from the Windows startup registry.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
