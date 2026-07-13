namespace GrammarFixer.Models;

public class AppSettings
{
    public bool   Enabled              { get; set; } = true;
    public bool   HotkeyOnlyMode       { get; set; } = false;
    public bool   DebugMode            { get; set; } = false;
    public int    DebounceMs           { get; set; } = 400;
    public string HotkeyTrigger        { get; set; } = "Ctrl+Alt+G";
    public string CorrectionWindowHotkey { get; set; } = "Ctrl+Alt+Shift+G";
    public double CorrectionWindowLeft  { get; set; } = -1;
    public double CorrectionWindowTop   { get; set; } = -1;

    /// <summary>Apps that are never corrected (IDE, self, etc.).</summary>
    public List<string> DeniedApps  { get; set; } = ["GrammarFixer", "devenv", "rider", "code"];

    /// <summary>If empty, all apps are allowed except DeniedApps.</summary>
    public List<string> AllowedApps { get; set; } = [];
}
