namespace GrammarFixer.Models;

public class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public CorrectionMode Mode { get; set; } = CorrectionMode.LanguageTool;
    public UxMode UxMode { get; set; } = UxMode.OneClickRewrite;
    public string HotkeyTrigger { get; set; } = "Ctrl+Alt+G";
    public string CorrectionWindowHotkey { get; set; } = "Ctrl+Alt+Shift+G";
    public int DebounceMs { get; set; } = 300;
    public bool HotkeyOnlyMode { get; set; } = false;
    public List<string> AllowedApps { get; set; } = [];
    public List<string> DeniedApps { get; set; } = new() { "GrammarFixer", "devenv", "rider", "code" };
    
    // Window position persistence
    public double CorrectionWindowLeft { get; set; } = -1;
    public double CorrectionWindowTop { get; set; } = -1;
}

public enum CorrectionMode { LanguageTool }
public enum UxMode { OneClickRewrite, ReviewSuggestions }