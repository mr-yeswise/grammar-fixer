namespace GrammarFixer.Models;

public class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public CorrectionMode Mode { get; set; } = CorrectionMode.Static;
    public UxMode UxMode { get; set; } = UxMode.OneClickRewrite;
    public string HotkeyTrigger { get; set; } = "Ctrl+Alt+G";
    public int DebounceMs { get; set; } = 400;
    public bool HotkeyOnlyMode { get; set; } = false;
    public List<string> AllowedApps { get; set; } = new();
    public List<string> DeniedApps { get; set; } = new() { "GrammarFixer", "devenv", "rider" };
    public string GroqModel { get; set; } = "llama-3.1-8b-instant";
    public string GroqFallbackModel { get; set; } = "llama-3.3-70b-versatile";
}

public enum CorrectionMode { Static, AI }
public enum UxMode { OneClickRewrite, ReviewSuggestions }
