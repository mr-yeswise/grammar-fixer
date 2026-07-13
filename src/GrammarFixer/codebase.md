# GrammarFixer — Codebase Navigation Reference

> Last updated: 2026-07-13 | Engine: LanguageTool (local Java server) | Build: net8.0-windows win-x64

---

## Project Structure

```
src/GrammarFixer/
├── GrammarFixer.csproj           # WPF project (net8.0-windows, single-file publish, win-x64)
├── App.xaml / App.xaml.cs        # WPF entry: boots LT service, AppController, TrayIconManager
├── GlobalUsings.cs               # ALL WPF/WinForms alias resolutions (see Alias Table below)
├── codebase.md                   # THIS FILE
├── Core/
│   ├── AppController.cs          # Central orchestrator: hook→debounce→UIA→pipeline→UI
│   ├── CorrectionPipeline.cs     # LRU cache (50) + routes to LanguageToolClient
│   ├── HotkeyManager.cs          # Global hotkey listener (default Ctrl+Alt+G)
│   ├── KeyboardHook.cs           # Low-level WH_KEYBOARD_LL hook; fires KeyDown only
│   ├── UiaHelper.cs              # UI Automation: GetFocusedText / SetFocusedText / GetCaretPos
│   ├── LanguageToolClient.cs     # HTTP POST /v2/check → CorrectionResult
│   └── LanguageToolService.cs    # Manages java -jar languagetool-server.jar child process
├── Models/
│   ├── AppSettings.cs            # User config (Enabled, UxMode, hotkeys, debounce, app lists)
│   ├── CorrectionResult.cs       # record: Original, Corrected, Edits[], FromCache
│   ├── Edit.cs                   # record: Original, Replacement, Reason, Offset, Length
│   ├── UxMode.cs                 # enum: OneClickRewrite | ReviewSuggestions
│   └── DiffLineViewModel.cs      # class + DiffType enum for CorrectionWindow diff view
├── Services/
│   ├── SettingsService.cs        # Load/Save → %APPDATA%\GrammarFixer\settings.json
│   ├── DiagnosticLogger.cs       # Daily logs → %LOCALAPPDATA%\GrammarFixer\logs\
│   └── AutostartHelper.cs        # Windows Run key registration
├── UI/
│   ├── CorrectionWindow.xaml/.cs # Floating paste-and-correct window with inline diff
│   ├── OverlayWindow.xaml/.cs    # Borderless overlay for ReviewSuggestions mode
│   ├── FloatingButton.xaml/.cs   # Pill button near caret
│   ├── SettingsWindow.xaml/.cs   # Config UI (hotkeys, UxMode, debounce, allowed/denied apps)
│   ├── TrayIconManager.cs        # System tray: icon, context menu, ShowBalloonTip
│   ├── DiffColorConverter.cs     # IValueConverter: DiffType → SolidColorBrush (for CorrectionWindow)
│   └── Styles.xaml               # Shared WPF styles
├── Assets/
│   ├── tray_enabled.ico
│   ├── tray_disabled.ico
│   └── tray_processing.ico
└── tools/
    ├── languagetool-server.jar   # LanguageTool standalone server (~200 MB, not in git)
    ├── Start-LanguageTool.bat    # Manual launch helper
    └── INSTALL.md                # Setup guide (Java 11+ required)
```

---

## GlobalUsings Alias Table

> All ambiguity aliases live in `GlobalUsings.cs`. **Never add bare `Application`, `Clipboard`, `KeyEventArgs`, etc. — always use the alias.**

| Alias | Resolves to |
|---|---|
| `WpfApp` | `System.Windows.Application` |
| `WpfPoint` | `System.Windows.Point` |
| `WpfColor` | `System.Windows.Media.Color` |
| `WpfColors` | `System.Windows.Media.Colors` |
| `WpfMessageBox` | `System.Windows.MessageBox` |
| `WpfMouseArgs` | `System.Windows.Input.MouseEventArgs` |
| `WpfClipboard` | `System.Windows.Clipboard` |
| `WpfKeyEventArgs` | `System.Windows.Input.KeyEventArgs` |
| `FormsKeys` | `System.Windows.Forms.Keys` |
| `FormsSendKeys` | `System.Windows.Forms.SendKeys` |
| `FormsCursor` | `System.Windows.Forms.Cursor` |
| `FormsTimer` | `System.Windows.Forms.Timer` |

---

## Key Types

### CorrectionResult (Models/CorrectionResult.cs)
```csharp
record CorrectionResult(
    string Original,
    string Corrected,
    List<Edit> Edits,
    bool FromCache = false
);
```

### Edit (Models/Edit.cs)
```csharp
record Edit(
    string Original,
    string Replacement,
    string Reason,
    int Offset,
    int Length
);
```

### AppSettings (Models/AppSettings.cs)
```csharp
class AppSettings {
    bool   Enabled                = true;
    bool   HotkeyOnlyMode         = false;
    bool   DebugMode              = false;
    int    DebounceMs             = 400;
    string HotkeyTrigger          = "Ctrl+Alt+G";
    string CorrectionWindowHotkey = "Ctrl+Alt+Shift+G";
    double CorrectionWindowLeft   = -1;
    double CorrectionWindowTop    = -1;
    UxMode UxMode                 = UxMode.OneClickRewrite;
    List<string> DeniedApps  = ["GrammarFixer", "devenv", "rider", "code"];
    List<string> AllowedApps = [];  // empty = allow all except denied
}
```

### DiffLineViewModel + DiffType (Models/DiffLineViewModel.cs)
```csharp
enum DiffType { None, Insert, Delete, Modify }
class DiffLineViewModel { string Text; DiffType Type; }
```

---

## Core Flow

```
KeyboardHook.KeyDown
  └─► AppController.OnTypingKeyDown
        └─► _typingDebounce (400ms)
              └─► OnTypingPaused()
                    ├─► UiaHelper.GetForegroundProcessName()  [allowed-list check]
                    ├─► UiaHelper.GetFocusedText()
                    ├─► CorrectionPipeline.CorrectNowAsync(text)
                    │     ├─► LruCache check (50 entries)
                    │     └─► LanguageToolClient.CheckAsync(text)
                    │           └─► POST http://localhost:8081/v2/check
                    └─► FloatingButton.ShowAt(caretPos)   [if correction found]

FloatingButton click
  └─► AppController.TriggerFromFloatingButton()
        ├─► UxMode.OneClickRewrite → UiaHelper.SetFocusedText(corrected)
        └─► UxMode.ReviewSuggestions → OverlayWindow.Show()

Ctrl+Alt+G (HotkeyManager)
  └─► AppController.OnHotkeyPressed()
        └─► pipeline.CorrectNowAsync() → UiaHelper.SetFocusedText()

Ctrl+Alt+Shift+G
  └─► AppController.ToggleCorrectionWindow()  [open/close CorrectionWindow]
```

---

## UI Components

| Component | File | Key Public API |
|---|---|---|
| `FloatingButton` | `UI/FloatingButton.xaml.cs` | `ShowAt(WpfPoint)`, `Hide()` |
| `OverlayWindow` | `UI/OverlayWindow.xaml.cs` | `ctor(result, caretPos, controller)` → Accept calls `ApplyCorrection()`, Dismiss calls `DismissOverlay()` |
| `CorrectionWindow` | `UI/CorrectionWindow.xaml.cs` | `ctor(ltClient, controller)` → auto-corrects on type, Ctrl+Enter sends to field |
| `SettingsWindow` | `UI/SettingsWindow.xaml.cs` | Binds all `AppSettings` fields; Save calls `UpdateSettings()` |
| `TrayIconManager` | `UI/TrayIconManager.cs` | `Initialize()`, `SetProcessingState(bool)`, `ShowBalloonTip(title, msg, icon)` |
| `DiffColorConverter` | `UI/DiffColorConverter.cs` | `IValueConverter`: `DiffType` → `SolidColorBrush` |

---

## AppController Public API

```csharp
void Start()                                    // install hooks, create FloatingButton
void Stop()                                     // uninstall hooks, close all windows
void UpdateSettings(AppSettings s)              // propagate settings changes
void OpenSettings()                             // show SettingsWindow
void ToggleCorrectionWindow()                   // open or close CorrectionWindow
void TriggerFromFloatingButton()                // pill click handler
void ApplyCorrection(CorrectionResult result)   // OverlayWindow accept
void DismissOverlay()                           // OverlayWindow dismiss
void ApplyCorrectionFromWindow(string text)     // CorrectionWindow send-to-field
void AttachTrayIcon(TrayIconManager t)          // wire up processing state callbacks
void RunSelfTest()                              // correction smoke test with messagebox result
```

---

## LanguageTool Integration

**Server:** `java -jar tools/languagetool-server.jar --port 8081 --allow-origin "*"`
- Managed by `LanguageToolService` (auto-start on app launch, auto-kill on exit)
- Health check: `GET /v2/languages` — polled every 500ms, 15s timeout

**Client:** `LanguageToolClient.CheckAsync(string text, CancellationToken ct = default)`
- `POST http://localhost:8081/v2/check`
- Body: `language=en-US&text=<input>`
- Applies matches sorted by offset descending → returns `CorrectionResult`
- Returns `null` on any failure (pipeline degrades gracefully)

---

## Build & Publish

```bash
# Debug (fast iteration)
dotnet build src/GrammarFixer/GrammarFixer.csproj -c Debug

# Release — single-file self-contained
dotnet publish src/GrammarFixer/GrammarFixer.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true --self-contained true
# Output: src/GrammarFixer/bin/Release/net8.0-windows/win-x64/publish/GrammarFixer.exe
```

**NuGet dependencies:**
- `Hardcodet.NotifyIcon.Wpf` — system tray (`TaskbarIcon`)
- `DiffPlex` — inline diff in `OverlayWindow` and `CorrectionWindow`
- `System.Text.Json` — built-in (.NET 8)

**Assets (CopyToOutputDirectory=PreserveNewest):**
- `Assets/tray_enabled.ico`, `tray_disabled.ico`, `tray_processing.ico`
- `tools/languagetool-server.jar` *(not in git — user must download)*

---

## Key Paths & Constants

| Item | Value |
|---|---|
| Repo root | `C:\Users\fadi4\Desktop\grammar-fixer` |
| Project dir | `src/GrammarFixer` |
| Settings file | `%APPDATA%\GrammarFixer\settings.json` |
| Logs dir | `%LOCALAPPDATA%\GrammarFixer\logs\` |
| LT JAR path | `tools/languagetool-server.jar` (relative to `AppContext.BaseDirectory`) |
| LT port | `8081` |
| LT base URL | `http://localhost:8081` |
| LT health | `GET /v2/languages` |
| LT check | `POST /v2/check` |
| Debounce default | `400ms` |
| LRU cache size | `50 entries` |
| Hotkey default | `Ctrl+Alt+G` |
| CW hotkey default | `Ctrl+Alt+Shift+G` |

---

## Quick Find by Feature

| Feature | File(s) |
|---|---|
| App startup/shutdown | `App.xaml.cs` |
| Global hotkey | `Core/HotkeyManager.cs` |
| Raw keyboard hook | `Core/KeyboardHook.cs` |
| Typing debounce | `Core/AppController.cs` → `OnTypingKeyDown`, `OnTypingPaused` |
| Text capture/apply (UIA) | `Core/UiaHelper.cs` |
| Correction routing + cache | `Core/CorrectionPipeline.cs` |
| LanguageTool HTTP client | `Core/LanguageToolClient.cs` |
| LanguageTool process mgmt | `Core/LanguageToolService.cs` |
| Settings persistence | `Services/SettingsService.cs` |
| Logging | `Services/DiagnosticLogger.cs` |
| Windows autostart | `Services/AutostartHelper.cs` |
| Tray icon + menu | `UI/TrayIconManager.cs` |
| Floating pill | `UI/FloatingButton.xaml.cs` |
| Review overlay | `UI/OverlayWindow.xaml.cs` |
| Paste-correct window | `UI/CorrectionWindow.xaml.cs` |
| Settings UI | `UI/SettingsWindow.xaml.cs` |
| Diff colour binding | `UI/DiffColorConverter.cs` |
| WPF/Forms ambiguity aliases | `GlobalUsings.cs` |
| Shared styles | `UI/Styles.xaml` |

---

## Removed / Deprecated

| Item | Reason |
|---|---|
| `Core/StaticCorrectionEngine.cs` | Replaced by LanguageTool |
| `Core/GroqClient.cs` | Replaced by LanguageTool |
| `AppSettings.GroqModel` / `GroqFallbackModel` | Removed with Groq engine |
| `AppSettings.Mode` / `CorrectionMode` enum | Replaced by single LT engine; UxMode still active |
| `Data/typos_en.json` | Was used by StaticCorrectionEngine |
