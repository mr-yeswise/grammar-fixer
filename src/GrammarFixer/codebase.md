# GrammarFixer — Codebase Navigation Reference

> **For AI agents:** Read this file first. It maps repo layout, runtime paths, build outputs, and how to debug LanguageTool failures.
>
> Last updated: 2026-07-13 | Engine: LanguageTool (local Java HTTP server) | Target: `net8.0-windows` / `win-x64`

---

## Agent Quick Start

1. **WPF app entry:** `App.xaml.cs` → starts `LanguageToolService`, then `AppController`, then tray icon.
2. **Correction engine:** `LanguageToolClient` (HTTP) — not in-process; requires a Java child process.
3. **LanguageTool files live at repo root `tools/`** — not under `src/GrammarFixer/`. The full ZIP contents are required (`libs/`, `org/`, `META-INF/`, JAR).
4. **Runtime logs:** `%LOCALAPPDATA%\GrammarFixer\logs\grammerfixer_YYYY-MM-DD.log` (note spelling: `grammerfixer`).
5. **Latest portable build:** `publish/portable/GrammarFixer.exe` (after `dotnet publish`).
6. **Setup guide for LT:** `tools/INSTALL.md`.

---

## Repo Layout (root)

```
grammar-fixer/
├── src/GrammarFixer/          # WPF project (all C# source)
├── tools/                     # LanguageTool standalone runtime (NOT in git — user must install)
│   ├── languagetool-server.jar
│   ├── libs/                  # ~138 dependency JARs — REQUIRED
│   ├── org/                   # language rules/resources — REQUIRED
│   ├── META-INF/              # REQUIRED
│   ├── INSTALL.md
│   └── Start-LanguageTool.bat # manual server launch for debugging
├── publish/
│   └── portable/
│       └── GrammarFixer.exe   # ← latest single-file release build
├── .github/workflows/build.yml
├── README.md
└── ARCHITECTURE_RESEARCH.md   # historical design notes (may be stale)
```

**Not in git:** `tools/languagetool-server.jar`, `tools/libs/`, `tools/org/`, `tools/META-INF/` (~200 MB total). CI/build on a fresh clone will succeed only if these exist locally or are downloaded in CI.

Optional: `src/GrammarFixer/Assets/LanguageTool-stable.zip` may be present as a local download cache — extract into `tools/` at repo root, not into `Assets/`.

---

## Project Structure (`src/GrammarFixer/`)

```
src/GrammarFixer/
├── GrammarFixer.csproj           # WPF, single-file publish, copies full LT runtime to output
├── App.xaml / App.xaml.cs        # Entry: LT service → AppController → TrayIconManager
├── GlobalUsings.cs               # WPF/WinForms alias table (see below)
├── codebase.md                   # THIS FILE
├── Core/
│   ├── AppController.cs          # Orchestrator: hook → debounce → UIA → pipeline → UI
│   ├── CorrectionPipeline.cs     # LRU cache (50) + debounce → LanguageToolClient
│   ├── HotkeyManager.cs          # Global hotkey (default Ctrl+Alt+G)
│   ├── KeyboardHook.cs           # WH_KEYBOARD_LL low-level hook
│   ├── LanguageToolClient.cs     # HTTP POST /v2/check → CorrectionResult
│   ├── LruCache.cs               # Generic LRU used by pipeline
│   └── UiaHelper.cs              # UI Automation: focused + selected text read/replace, caret position
├── Models/
│   ├── AppSettings.cs            # User config (Enabled, UxMode, hotkeys, debounce, app lists)
│   ├── CorrectionResult.cs       # record: Original, Corrected, Edits[], FromCache
│   ├── Edit.cs                   # record: Original, Replacement, Reason, Offset, Length
│   ├── UxMode.cs                 # enum: OneClickRewrite | ReviewSuggestions
│   └── DiffLineViewModel.cs      # Diff display model for CorrectionWindow
├── Services/
│   ├── LanguageToolService.cs    # Spawns java -jar languagetool-server.jar, health polling
│   ├── SettingsService.cs        # Load/Save → %APPDATA%\GrammarFixer\settings.json
│   ├── DiagnosticLogger.cs       # Daily file logs → %LOCALAPPDATA%\GrammarFixer\logs\
│   ├── AutostartHelper.cs        # Windows Run key registration
│   └── CredentialService.cs      # Legacy (Groq era) — currently unused
├── UI/
│   ├── CorrectionWindow.xaml/.cs # Floating paste-and-correct window
│   ├── OverlayWindow.xaml/.cs    # ReviewSuggestions overlay
│   ├── FloatingButton.xaml/.cs   # Pill button near caret
│   ├── SettingsWindow.xaml/.cs   # Settings UI
│   ├── TrayIconManager.cs        # System tray icon + context menu
│   ├── DiffColorConverter.cs     # DiffType → brush
│   └── Styles.xaml
├── Assets/
│   ├── tray_enabled.ico / tray_disabled.ico / tray_processing.ico
│   └── LanguageTool-stable.zip   # optional local download; extract to repo-root tools/
└── Data/
    └── typos_en.json             # legacy static engine data (still copied to output; unused)
```

---

## Startup Sequence

```
App.OnStartup
  ├─ SettingsService.Load()
  ├─ new LanguageToolService()
  ├─ new LanguageToolClient()
  ├─ await LanguageToolService.StartAsync()     ← blocks up to 30s on LT health check
  │     ├─ FindJarPath()                        ← see JAR resolution below
  │     ├─ validate libs/ exists
  │     ├─ Process.Start("java", "-jar languagetool-server.jar ...")
  │     └─ poll GET http://localhost:8081/v2/languages
  ├─ new AppController(settings, ltClient, ltService)
  ├─ TrayIconManager.Initialize()
  ├─ AppController.Start()                      ← keyboard hook + hotkeys
  └─ if !ltReady → tray balloon warning
```

On exit: `AppController.Stop()` → `LanguageToolService.Dispose()` kills Java process tree.

---

## LanguageTool Integration

### Runtime layout (required)

LanguageTool is **not** a single fat JAR. The server must run with this directory structure (working directory = folder containing the JAR):

```
tools/
├── languagetool-server.jar
├── libs/          ← dependency JARs (slf4j, etc.)
├── org/           ← grammar rules and language resources
└── META-INF/
```

Copy the **entire extracted** `LanguageTool-stable.zip` contents into repo-root `tools/`. See `tools/INSTALL.md`.

### JAR resolution (`LanguageToolService.FindJarPath`)

Checked in order (first existing file wins):

| # | Path |
|---|---|
| 1 | `{AppContext.BaseDirectory}/tools/languagetool-server.jar` |
| 2 | `{AppContext.BaseDirectory}/../../tools/languagetool-server.jar` |
| 3 | `{Environment.CurrentDirectory}/tools/languagetool-server.jar` |

- **Dev/build output:** #1 resolves to `src/GrammarFixer/bin/{Config}/net8.0-windows/win-x64/tools/`.
- **Single-file publish:** content extracts to temp alongside exe; `AppContext.BaseDirectory` points there.

### Server command

```bash
java -Dfile.encoding=utf-8 -Xmx512m -jar languagetool-server.jar --port 8081 --allow-origin "*"
```

Working directory = directory containing the JAR (must contain sibling `libs/`).

### HTTP API

| Endpoint | Method | Used by |
|---|---|---|
| `/v2/languages` | GET | `LanguageToolService` health check (startup, 30s timeout, 500ms poll) |
| `/v2/check` | POST | `LanguageToolClient.CheckAsync` — body: `language=en-US&text=...` |

Base URL: `http://localhost:8081` (`LanguageToolService.BaseUrl`).

### Client behavior (`LanguageToolClient`)

- Static `HttpClient`, 10s timeout.
- Applies LT match replacements end-to-start by offset.
- Returns `null` on any HTTP/parse failure (pipeline degrades gracefully).

---

## Build & Publish

### Prerequisites

- .NET 8 SDK
- Java 11+ on PATH (for running, not building)
- Full LanguageTool runtime in repo-root `tools/` (for build content copy + runtime)

### Commands

```powershell
# Fast iteration (Debug)
dotnet build src/GrammarFixer/GrammarFixer.csproj -c Debug

# Release folder build (tools/ copied beside exe)
dotnet build src/GrammarFixer/GrammarFixer.csproj -c Release

# Portable single-file exe (recommended output)
dotnet publish src/GrammarFixer/GrammarFixer.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeAllContentForSelfExtract=true `
  -o ./publish/portable
```

### Build output locations

| Artifact | Path |
|---|---|
| **Latest portable exe** | `publish/portable/GrammarFixer.exe` |
| Release folder build | `src/GrammarFixer/bin/Release/net8.0-windows/win-x64/GrammarFixer.exe` |
| Debug folder build | `src/GrammarFixer/bin/Debug/net8.0-windows/win-x64/GrammarFixer.exe` |
| LT runtime in build output | `{output dir}/tools/` (jar + libs + org + META-INF) |

### csproj content copy (`GrammarFixer.csproj`)

These repo-root paths are copied to `{output}/tools/`:

- `tools/languagetool-server.jar`
- `tools/libs/**/*`
- `tools/org/**/*`
- `tools/META-INF/**/*`

Properties: `PublishSingleFile=true`, `IncludeAllContentForSelfExtract=true` (bundles LT into single exe, extracts at runtime).

### CI (`.github/workflows/build.yml`)

Publishes to `./publish/portable/GrammarFixer.exe` on `windows-latest`. **Note:** CI does not download LanguageTool; build may warn/fail if `tools/languagetool-server.jar` is missing.

### NuGet dependencies

| Package | Purpose |
|---|---|
| `Hardcodet.NotifyIcon.Wpf` | System tray |
| `DiffPlex` | Inline diff in overlay/correction window |
| `AdysTech.CredentialManager` | Legacy credential storage (Groq era) |
| `Microsoft.Extensions.Http` | HTTP helpers |
| `System.Text.Json` | LT JSON response parsing |

---

## Debugging Playbook (for agents)

### 1. Check runtime logs first

```
%LOCALAPPDATA%\GrammarFixer\logs\grammerfixer_YYYY-MM-DD.log
```

Log format: `{timestamp} [{LEVEL}] [{CallerMethod}] {message}`

Useful grep patterns:

| Pattern | Meaning |
|---|---|
| `LanguageTool JAR not found` | Missing JAR in all candidate paths |
| `libs/ folder missing` | Incomplete LT install — only JAR copied |
| `Java not found` | `java` not on PATH |
| `ClassNotFoundException: org.slf4j.LoggerFactory` | LT started without `libs/` (Java stderr, logged as `LT[stderr]`) |
| `PortBindingException` / `Address already in use` | Port 8081 occupied (orphan Java from prior run) |
| `LanguageTool server ready` | Startup succeeded |
| `LT: HTTP 4xx/5xx` | Server up but check request failed |
| `Pipeline: cache hit` | Correction served from LRU |

### 2. Verify LanguageTool standalone (isolate from WPF)

```powershell
cd tools
.\Start-LanguageTool.bat
# Then in browser or curl:
curl http://localhost:8081/v2/languages
```

Expect HTTP 200. First start may take 10–30s while LT loads.

### 3. Verify build output has full LT runtime

```powershell
dir src\GrammarFixer\bin\Release\net8.0-windows\win-x64\tools
dir src\GrammarFixer\bin\Release\net8.0-windows\win-x64\tools\libs\*.jar | measure
```

Must show `libs/`, `org/`, `META-INF/`, and `languagetool-server.jar`. Expect ~138 JARs in `libs/`.

### 4. Kill orphan Java processes

If port 8081 is stuck after a crash:

```powershell
Get-NetTCPConnection -LocalPort 8081 -ErrorAction SilentlyContinue
# Then stop the owning Java process by PID
```

### 5. Common failure → fix matrix

| Symptom | Root cause | Fix |
|---|---|---|
| Build warning/error: missing `languagetool-server.jar` | LT not installed in `tools/` | Extract full ZIP into repo-root `tools/` |
| App starts, balloon "LanguageTool Not Ready" | JAR missing, Java missing, or libs missing | Check log + `tools/INSTALL.md` |
| `NoClassDefFoundError: org/slf4j/LoggerFactory` | Only JAR copied, no `libs/` | Copy full ZIP contents |
| Server ready but no corrections | LT client failure | Check log for `LT: HTTP` or `LT: checking` lines |
| `LT server did not become ready in time` | Slow first boot or crash | Read `LT[stderr]` lines in log; test with `Start-LanguageTool.bat` |
| Single-file exe fails but folder build works | Content not extracted | Ensure `IncludeAllContentForSelfExtract=true` in csproj |

### 6. Settings and config files

| File | Path |
|---|---|
| User settings | `%APPDATA%\GrammarFixer\settings.json` |
| Diagnostic logs | `%LOCALAPPDATA%\GrammarFixer\logs\` |
| LT install docs | `tools/INSTALL.md` |

---

## GlobalUsings Alias Table

> All WPF/WinForms ambiguity aliases live in `GlobalUsings.cs`. **Never use bare `Application`, `Clipboard`, `KeyEventArgs`, etc.**

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

### CorrectionResult (`Models/CorrectionResult.cs`)
```csharp
record CorrectionResult(string Original, string Corrected, List<Edit> Edits, bool FromCache = false);
```

### Edit (`Models/Edit.cs`)
```csharp
record Edit(string Original, string Replacement, string Reason, int Offset, int Length);
```

### AppSettings (`Models/AppSettings.cs`)
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

---

## Core Flow

```
KeyboardHook.KeyDown
  └─► AppController.OnTypingKeyDown
        └─► typing debounce (AppSettings.DebounceMs, default 400ms)
              └─► OnTypingPaused()
                    ├─► UiaHelper.GetForegroundProcessName()  [allow/deny list]
                    ├─► UiaHelper.GetFocusedText()
                    ├─► CorrectionPipeline.CorrectNowAsync(text)
                    │     ├─► LruCache check (50 entries)
                    │     └─► LanguageToolClient.CheckAsync(text)
                    │           └─► POST http://localhost:8081/v2/check
                    └─► FloatingButton.ShowAt(caretPos)   [if correction found]

FloatingButton click → AppController.TriggerFromFloatingButton()
  ├─► UxMode.OneClickRewrite → UiaHelper.SetFocusedText(corrected)
  └─► UxMode.ReviewSuggestions → OverlayWindow.Show()

Ctrl+Alt+G → AppController.OnHotkeyPressed() → pipeline → SetFocusedText
Ctrl+Alt+Shift+G → AppController.ToggleCorrectionWindow()
```

Hotkey selection path:

```
Ctrl+Alt+G
  └─► AppController.OnHotkeyPressed()
        ├─► UiaHelper.GetSelectedText()
        ├─► (fallback) UiaHelper.GetFocusedText()
        ├─► CorrectionPipeline.CorrectNowAsync(text)
        └─► if selection: UiaHelper.ReplaceSelectedText(corrected)
            else: UiaHelper.SetFocusedText(corrected)
```

---

## UI Components

| Component | File | Key API |
|---|---|---|
| `FloatingButton` | `UI/FloatingButton.xaml.cs` | `ShowAt(WpfPoint)`, `Hide()` |
| `OverlayWindow` | `UI/OverlayWindow.xaml.cs` | Accept → `ApplyCorrection()`, Dismiss → `DismissOverlay()` |
| `CorrectionWindow` | `UI/CorrectionWindow.xaml.cs` | Auto-correct on type; Ctrl+Enter sends to field |
| `SettingsWindow` | `UI/SettingsWindow.xaml.cs` | Binds `AppSettings`; Save → `UpdateSettings()` |
| `TrayIconManager` | `UI/TrayIconManager.cs` | `Initialize()`, `SetProcessingState()`, `ShowBalloonTip()` |
| `DiffColorConverter` | `UI/DiffColorConverter.cs` | `DiffType` → `SolidColorBrush` |

---

## AppController Public API

```csharp
void Start()
void Stop()
void UpdateSettings(AppSettings s)
void OpenSettings()
void ToggleCorrectionWindow()
void TriggerFromFloatingButton()
void ApplyCorrection(CorrectionResult result)
void DismissOverlay()
void ApplyCorrectionFromWindow(string text)
void AttachTrayIcon(TrayIconManager t)
void RunSelfTest()
```

Selection context state:
- `_lastWasSelection` (private bool) tracks whether the last hotkey capture came from selected text, so apply actions route to `ReplaceSelectedText(...)` instead of full-field replace.

UiaHelper selection helpers:
- `GetSelectedText()` — reads selection only, no Ctrl+A.
- `ReplaceSelectedText(string)` — pastes over current selection, no Ctrl+A.

---

## Key Paths & Constants

| Item | Value |
|---|---|
| Repo root | `grammar-fixer/` |
| WPF project | `src/GrammarFixer/` |
| LT runtime (source) | `tools/` at **repo root** |
| LT runtime (build output) | `{AppContext.BaseDirectory}/tools/` |
| Latest portable exe | `publish/portable/GrammarFixer.exe` |
| Settings | `%APPDATA%\GrammarFixer\settings.json` |
| Logs | `%LOCALAPPDATA%\GrammarFixer\logs\grammerfixer_*.log` |
| LT port | `8081` (fixed; no auto-fallback implemented despite INSTALL.md mention) |
| LT health timeout | 30 seconds |
| LT language | `en-US` (hardcoded) |
| Debounce default | 400 ms |
| LRU cache size | 50 entries |
| Hotkey default | `Ctrl+Alt+G` |
| Correction window hotkey | `Ctrl+Alt+Shift+G` |

---

## Quick Find by Task

| Task | Start here |
|---|---|
| App startup / shutdown | `App.xaml.cs` |
| LT process lifecycle | `Services/LanguageToolService.cs` |
| LT HTTP client | `Core/LanguageToolClient.cs` |
| Correction + cache | `Core/CorrectionPipeline.cs` |
| Typing debounce + orchestration | `Core/AppController.cs` |
| Text capture/apply (UIA) | `Core/UiaHelper.cs` |
| Selected text read | `Core/UiaHelper.cs` → `GetSelectedText()` |
| Selected text replace | `Core/UiaHelper.cs` → `ReplaceSelectedText()` |
| Global hotkey | `Core/HotkeyManager.cs` |
| Raw keyboard hook | `Core/KeyboardHook.cs` |
| Settings persistence | `Services/SettingsService.cs` |
| File logging | `Services/DiagnosticLogger.cs` |
| LT install/setup | `tools/INSTALL.md`, `tools/Start-LanguageTool.bat` |
| Build config / LT copy rules | `GrammarFixer.csproj` |
| CI pipeline | `.github/workflows/build.yml` |
| WPF/Forms aliases | `GlobalUsings.cs` |

---

## Removed / Deprecated

| Item | Notes |
|---|---|
| `Core/StaticCorrectionEngine.cs` | Replaced by LanguageTool |
| `Core/GroqClient.cs` | Replaced by LanguageTool |
| `Services/CredentialService.cs` | Groq-era; no callers remain |
| `Data/typos_en.json` | Static engine data; still in csproj but unused |
| `AppSettings.GroqModel` / `CorrectionMode` | Removed with Groq engine |
| Copying only `languagetool-server.jar` | **Broken** — always deploy full LT runtime |
