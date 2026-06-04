using Microsoft.Win32;

namespace VpncBar.Core;

// Launch-at-login via the per-user Run key (HKCU). Points at the installed
// tray exe; no admin needed, no scheduled-task plumbing. The mac app starts
// from /Applications via the user opening it — this is the Windows analog
// users expect from a tray app.
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "VpncBar";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception) { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception) { /* registry locked down — toggle is best-effort */ }
    }
}
