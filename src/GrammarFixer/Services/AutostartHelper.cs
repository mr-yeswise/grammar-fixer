using Microsoft.Win32;

namespace GrammarFixer.Services;

/// <summary>
/// Manages autostart via HKCU Run key — no admin rights required.
/// Uses Environment.ProcessPath (correct for single-file publish).
/// AppContext.BaseDirectory is the fallback — never Assembly.Location
/// which returns empty string in single-file apps (IL3000).
/// </summary>
public static class AutostartHelper
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GrammarFixer";

    public static void EnsureAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        // Environment.ProcessPath is the reliable path for single-file published apps
        var exePath = Environment.ProcessPath
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "GrammarFixer.exe");

        key.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void RemoveAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }
}
