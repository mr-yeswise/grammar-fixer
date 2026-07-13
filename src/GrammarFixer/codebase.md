# GrammarFixer Codebase Dictionary

> Generated: 2026-07-13 | Purpose: Navigation reference for the codebase

---

## Project Structure

```
src/GrammarFixer/
├── GrammarFixer.csproj          # Project config (WPF, net8.0-windows, single-file publish)
├── App.xaml / App.xaml.cs       # WPF app entry, AppController lifecycle
├── GlobalUsings.cs              # Implicit usings
├── codebase.md                  # THIS FILE
├── Core/
│   ├── AppController.cs         # Central orchestrator (hooks, UI, pipeline)
│   ├── CorrectionPipeline.cs    # Debounce, cache, routes to engines
│   ├── StaticCorrectionEngine.cs # Offline regex + typo dict (typos_en.json)
│   ├── GroqClient.cs            # Groq API (llama-3.1-8b-instant)
│   ├── HotkeyManager.cs         # Global hotkey (Ctrl+Alt+G)
│   ├── KeyboardHook.cs          # Low-level typing hook
│   ├── LruCache.cs              # Simple LRU cache (50 entries)
│   ├── UiaHelper.cs             # UI Automation text capture/set
│   └── [NEW] LanguageToolClient.cs   # HTTP client for local LT server
│   └── [NEW] LanguageToolService.cs  # Manages Java child process
├── Models/
│   ├── AppSettings.cs           # User config (mode, hotkey, debounce, Groq model)
│   ├── CorrectionResult.cs      # Original + Corrected + Edits[] + FromCache
│   └── Edit.cs                  # Original + Replacement + Reason + Offset + Length
├── Services/
│   ├── SettingsService.cs       # Load/Save settings.json (%APPDATA%\GrammarFixer)
│   ├── DiagnosticLogger.cs      # Daily log files (%LOCALAPPDATA%\GrammarFixer\logs)
│   ├── CredentialService.cs     # Groq API key (Windows Credential Manager)
│   └── AutostartHelper.cs       # Windows startup registration
├── UI/
│   ├── OverlayWindow.xaml/.cs   # Suggestion review overlay
│   ├── FloatingButton.xaml/.cs  # Pill near caret
│   ├── SettingsWindow.xaml/.cs  # Settings UI
│   └── TrayIconManager.cs       # System tray icon + context menu
└── Data/
    └── typos_en.json            # 118 common misspellings (Wikipedia list)
```

---

## Key Types & Records

### CorrectionResult (Models/CorrectionResult.cs)
```csharp
record CorrectionResult(
    string Original,           // Input text
    string Corrected,          // Output text
    List<Edit> Edits,          // Applied changes
    bool FromCache = false     // Cache hit flag
);
```

### Edit (Models/Edit.cs)
```csharp
record Edit(
    string Original,           // Matched text
    string Replacement,        // Suggested fix
    string Reason,             // Human-readable reason
    int Offset,                // Start index in Original
    int Length                 // Length of match
);
```

### AppSettings (Models/AppSettings.cs)
```csharp
class AppSettings {
    bool Enabled = true;
    bool DebugMode = false;
    CorrectionMode Mode = CorrectionMode.Static;  // Static | AI
    UxMode UxMode = UxMode.OneClickRewrite;       // OneClickRewrite | ReviewSuggestions
    string HotkeyTrigger = "Ctrl+Alt+G";
    int DebounceMs = 400;
    bool HotkeyOnlyMode = false;
    List<string> AllowedApps = [];
    List<string> DeniedApps = ["GrammarFixer", "devenv", "rider"];
    string GroqModel = "llama-3.1-8b-instant";
    string GroqFallbackModel = "llama-3.3-70b-versatile";
}
enum CorrectionMode { Static, AI }
enum UxMode { OneClickRewrite, ReviewSuggestions }
```

---

## Core Flow (AppController → Pipeline)

1. **KeyboardHook** fires on every keypress → `OnTypingKeyDown`
2. **Typing debounce** (400ms default) → `OnTypingPaused`
3. **UIA capture** → `UiaHelper.GetFocusedText()` + caret position
4. **CorrectionPipeline.Queue(text)** or `CorrectNowAsync(text)`
5. **Pipeline.RunCorrectionForAsync**:
   - Check LRU cache (50 entries)
   - If `Mode == Static` or no Groq key → `StaticCorrectionEngine.Correct()`
   - If `Mode == AI` and Groq available → `GroqClient.CorrectAsync()`
   - Fallback to Static on Groq failure
   - Cache result
   - Fire `CorrectionReady` event
6. **AppController.OnCorrectionReady** → show `FloatingButton` at caret
7. **User clicks pill** → `TriggerFromFloatingButton` → `UiaHelper.SetFocusedText(corrected)` (OneClick) or show `OverlayWindow` (Review)

---

## Existing Engines (TO BE REPLACED)

### StaticCorrectionEngine (Core/StaticCorrectionEngine.cs)
- Loads `Data/typos_en.json` (118 entries)
- Regex rules: double spaces, repeated words, lone "i", capitalization after period, contractions, typo dict
- Returns `CorrectionResult` with `List<Edit>`
- **No external deps** — pure C# regex

### GroqClient (Core/GroqClient.cs)
- HTTP POST to `https://api.groq.com/openai/v1/chat/completions`
- Model: `llama-3.1-8b-instant` (default) or fallback
- JSON mode response schema: `{ corrected: string, edits: [{ original, replacement, reason, offset, length }] }`
- 8s timeout, 0.1 temperature
- Returns `CorrectionResult` or null on error

---

## Services

### DiagnosticLogger (Services/DiagnosticLogger.cs)
- `Log(level, message)` → daily file `%LOCALAPPDATA%\GrammarFixer\logs\grammerfixer_yyyy-MM-dd.log`
- Levels: Debug, Info, Warn, Error
- Auto-cleans files older than 7 days
- Never throws

### SettingsService (Services/SettingsService.cs)
- `Load()` / `Save(AppSettings)` → `%APPDATA%\GrammarFixer\settings.json`
- JsonStringEnumConverter for enums

### CredentialService (Services/CredentialService.cs)
- Stores Groq API key in Windows Credential Manager (AdysTech.CredentialManager)
- `LoadApiKey()` / `SaveApiKey(string)`

### AutostartHelper (Services/AutostartHelper.cs)
- Registers/unregisters app in Windows startup (Run key)

---

## UI Components

| Component | Purpose | Key Methods |
|-----------|---------|-------------|
| `FloatingButton` | Pill near caret | `ShowAt(WpfPoint)`, `Click` → `AppController.TriggerFromFloatingButton()` |
| `OverlayWindow` | Review suggestions | `Show()`, `Apply` → `AppController.ApplyCorrection()`, `Dismiss` → `DismissOverlay()` |
| `SettingsWindow` | Config UI | Binds to `AppSettings`, saves via `SettingsService` |
| `TrayIconManager` | System tray | `Initialize()`, `SetProcessingState(bool)`, context menu (Settings, Self-test, Enabled, Exit) |

---

## Build & Publish

```bash
# Debug build
dotnet build src/GrammarFixer/GrammarFixer.csproj -c Debug

# Release single-file publish
dotnet publish src/GrammarFixer/GrammarFixer.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
# Output: src/GrammarFixer/bin/Release/net8.0-windows/win-x64/publish/GrammarFixer.exe
```

**Dependencies (NuGet):**
- `Hardcodet.NotifyIcon.Wpf` — system tray
- `AdysTech.CredentialManager` — Windows Credential Manager
- `DiffPlex` — diff for overlay (unused in current pipeline?)
- `Microsoft.Extensions.Http` — HttpClient factory (not used directly)
- `System.Text.Json` — built-in for .NET 8

**Content copied to output:**
- `Data/typos_en.json` (PreserveNewest)
- `Assets/tray_enabled.ico`, `tray_disabled.ico`, `tray_processing.ico`

---

## LanguageTool Integration Plan (from plan.md)

### New Files to Add

| File | Purpose |
|------|---------|
| `Core/LanguageToolService.cs` | Manages `java -jar languagetool-server.jar --port 8081 --allow-origin "*"` process |
| `Core/LanguageToolClient.cs` | HTTP client calling `http://localhost:8081/v2/check` with `language=en-US` |
| `tools/languagetool-server.jar` | LanguageTool standalone server (English only, ~200MB) |
| `tools/Start-LanguageTool.bat` | Manual launch script |
| `tools/INSTALL.md` | User setup instructions (Java 11+, download LT) |

### Files to Modify

| File | Changes |
|------|---------|
| `CorrectionPipeline.cs` | Remove `StaticCorrectionEngine` + `GroqClient`; inject `LanguageToolClient`; keep LRU cache (50) |
| `App.xaml.cs` | Create `LanguageToolService`; `await StartAsync()` in `OnStartup`; `Dispose()` in `OnExit`; tray balloon if not ready |
| `Models/CorrectionResult.cs` | Already has `Edits` list — no change needed |
| `Models/Edit.cs` | Ensure `Message` field exists (currently `Reason`) — rename or add alias |

### Data to Remove
- `Core/StaticCorrectionEngine.cs` (delete)
- `Core/GroqClient.cs` (delete)
- `AppSettings.GroqModel`, `GroqFallbackModel` (optional — keep for future?)

### LanguageTool API Contract

**POST** `http://localhost:8081/v2/check`
```
Content-Type: application/x-www-form-urlencoded
language=en-US&text=<input>&enabledOnly=false
```

**Response:**
```json
{
  "matches": [
    {
      "offset": 0,
      "length": 5,
      "message": "Possible typo",
      "replacements": [{ "value": "fixed" }]
    }
  ]
}
```

**Client logic:** Sort matches by offset descending, apply first replacement each, rebuild string via `StringBuilder`.

---

## Verification Checklist (before implementing)

- [ ] Java 11+ installed on target machine
- [ ] `languagetool-server.jar` downloaded and placed at `tools/languagetool-server.jar`
- [ ] Port 8081 free (no conflicts)
- [ ] `dotnet build` succeeds with 0 errors
- [ ] App starts, LT server launches, `/v2/languages` responds within 15s
- [ ] Correction works end-to-end (type → pill → click → text replaced)
- [ ] Cache still works (repeat same text → FromCache=true)
- [ ] Tray shows warning if LT not ready
- [ ] Self-test still passes (update to use LT)

---

## Key Paths & Constants

| Item | Path / Value |
|------|--------------|
| Repo root | `C:\Users\fadi4\Desktop\grammar-fixer` |
| Project dir | `src/GrammarFixer` |
| App data | `%APPDATA%\GrammarFixer\settings.json` |
| Logs | `%LOCALAPPDATA%\GrammarFixer\logs\` |
| LT JAR (planned) | `tools/languagetool-server.jar` (relative to `AppContext.BaseDirectory`) |
| LT Port | 8081 |
| LT Base URL | `http://localhost:8081` |
| LT Health check | `GET /v2/languages` |
| LT Check endpoint | `POST /v2/check` |
| Debounce default | 400ms |
| Cache size | 50 entries |
| Hotkey default | Ctrl+Alt+G |

---

## Notes for Implementer

1. **Single-file publish** means `AppContext.BaseDirectory` is the temp extraction folder — `tools/languagetool-server.jar` must be copied there by the publish process (add as `Content` with `CopyToOutputDirectory=PreserveNewest`)

2. **Process lifetime**: `LanguageToolService` must kill the Java process tree on `Dispose` (app exit). Use `Process.Kill(entireProcessTree: true)` (.NET 7+)

3. **Health check polling**: 500ms interval, 15s max. Log each attempt at Debug level.

4. **Error handling**: LT client returns `null` on any failure → pipeline should surface gracefully (no crash). Consider falling back to a minimal static engine for "no Java" scenarios.

5. **English only**: `language=en-US` hardcoded. No multi-language support needed per requirements.

6. **Model removal**: `StaticCorrectionEngine` and `GroqClient` can be deleted entirely. `CorrectionMode.AI` enum value becomes unused — either remove or repurpose for "LanguageTool".

7. **AppSettings cleanup**: Remove `GroqModel`, `GroqFallbackModel` unless keeping for future cloud fallback.

8. **DiffPlex**: Currently unused in pipeline — may be used by OverlayWindow for showing diffs. Keep package.

---

## Quick Reference: Find by Feature

| Feature | File(s) |
|---------|---------|
| App startup/shutdown | `App.xaml.cs` |
| Global hotkey | `Core/HotkeyManager.cs` |
| Typing hook + debounce | `Core/AppController.cs` (OnTypingKeyDown, OnTypingPaused) |
| Text capture (UIA) | `Core/UiaHelper.cs` |
| Correction routing | `Core/CorrectionPipeline.cs` |
| Static rules | `Core/StaticCorrectionEngine.cs` |
| Groq API | `Core/GroqClient.cs` |
| NEW: LanguageTool server | `Core/LanguageToolService.cs` (planned) |
| NEW: LanguageTool client | `Core/LanguageToolClient.cs` (planned) |
| Cache | `Core/LruCache.cs` |
| Settings persistence | `Services/SettingsService.cs` |
| Logging | `Services/DiagnosticLogger.cs` |
| Credentials | `Services/CredentialService.cs` |
| Tray icon | `UI/TrayIconManager.cs` |
| Floating pill | `UI/FloatingButton.xaml.cs` |
| Review overlay | `UI/OverlayWindow.xaml.cs` |
| Settings UI | `UI/SettingsWindow.xaml.cs` |
| Typo dictionary | `Data/typos_en.json` |