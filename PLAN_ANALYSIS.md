# Plan vs Codebase Analysis

## Issues Found

### 1. Namespace Mismatch (Part 2 & 3)
- **Plan**: `LanguageToolService` in `GrammarFixer.Services`, `LanguageToolClient` in `GrammarFixer.Core`
- **Codebase pattern**: Services are in `GrammarFixer.Services`, Core logic in `GrammarFixer.Core`
- **Verdict**: ✅ Matches pattern

### 2. CorrectionResult Model (Part 8)
- **Current**: `CorrectionResult` is a **record** with positional constructor:
  ```csharp
  record CorrectionResult(string Original, string Corrected, List<Edit> Edits, bool FromCache = false);
  ```
- **Plan**: Shows it as a **class** with init-only properties
- **Conflict**: The plan's suggested change won't compile with existing record syntax
- **Fix needed**: Either keep record syntax or properly convert to class with init properties

### 3. Edit Model (Part 8)
- **Current**: `record Edit(string Original, string Replacement, string Reason, int Offset, int Length);`
- **Plan**: Wants `Message` field instead of `Reason`
- **Conflict**: LanguageToolClient uses `Message` property name, but model has `Reason`
- **Fix**: Either rename `Reason` → `Message` in model, or use `Reason` in client

### 4. AppSettings (Part 4 & 5)
- **Current**: Has `CorrectionMode { Static, AI }` and `GroqModel`, `GroqFallbackModel`
- **Plan**: Says "Remove all StaticCorrectionEngine and GroqClient references" and "Remove GroqModel, GroqFallbackModel"
- **Verdict**: ✅ Correct - these should be removed

### 5. App.xaml.cs (Part 5)
- **Current**: Creates `AppController` with settings, starts it
- **Plan**: Needs to create `LanguageToolService`, await `StartAsync()`, pass `LanguageToolClient` to pipeline
- **Dependency flow**: AppController → CorrectionPipeline → LanguageToolClient
- **Check**: AppController currently creates `CorrectionPipeline` internally - needs to accept LT client

### 6. CorrectionPipeline Constructor (Part 4)
- **Current**: `public CorrectionPipeline(AppSettings settings)` - creates StaticEngine internally
- **Plan**: `Constructor takes (AppSettings settings, LanguageToolClient ltClient)`
- **Impact**: AppController must create LT client first, then pass to pipeline

### 7. LanguageTool JAR Location (Part 1)
- **Plan**: `tools/languagetool-server.jar` relative to repo root
- **Code**: Uses `Path.Combine(AppContext.BaseDirectory, "tools", "languagetool-server.jar")`
- **Issue**: In single-file publish, `AppContext.BaseDirectory` is temp extraction folder. The JAR must be included as Content with `CopyToOutputDirectory=PreserveNewest`
- **Missing**: Need to add `<Content Include="tools\languagetool-server.jar" CopyToOutputDirectory="PreserveNewest" />` to csproj

### 8. Process.Kill(entireProcessTree: true) (Part 2)
- **Issue**: This method is .NET 7+ only. Project targets net8.0-windows → ✅ Available
- **But**: Need to handle case where process already exited

### 9. DiagnosticLogger Usage
- **Current**: `DiagnosticLogger.Log(DiagnosticLogLevel.Info, "msg")` 
- **Plan**: Uses `DiagnosticLogger.Info("msg")` and `DiagnosticLogger.Warn("msg")`
- **Conflict**: Static helper methods don't exist - must use `Log(level, msg)` or add extension methods

### 10. LanguageTool API Endpoint
- **Plan**: Uses `/v2/check` with form data
- **Check**: LanguageTool server API typically uses `/v2/check` - this is correct
- **Response format**: Plan assumes `matches` array with `offset`, `length`, `message`, `replacements[].value` - matches LT spec

### 11. Missing: AppController Changes
- **Plan Part 4** mentions updating CorrectionPipeline
- **Plan Part 5** mentions App.xaml.cs
- **Missing**: AppController.cs changes - it creates CorrectionPipeline and needs to pass LT client
- **Current AppController line 41**: `_pipeline = new CorrectionPipeline(settings);`
- **Must change**: Accept LT client in constructor or create it there

### 12. Models/Edit.cs - Offset/Length
- **Current**: `record Edit(string Original, string Replacement, string Reason, int Offset, int Length);`
- **Plan's LanguageToolClient**: Creates `new Edit { Original = original, Replacement = m.Replace, Message = m.Message }` (object initializer)
- **Conflict**: Record with positional constructor can't use object initializer unless it has init properties
- **Fix**: Convert Edit to class with init properties, or use record with `with` expression

### 13. CorrectionPipeline.RunCorrectionForAsync
- **Current**: Returns `CorrectionResult?` with `FromCache` flag
- **Plan's LT Client**: Returns `CorrectionResult` with `FromCache = false`
- **Cache key**: Uses input text as key - should work

### 14. LanguageTool Server Startup Time
- **Plan**: 15 second timeout, 500ms polling
- **Reality**: LT server can take 10-20s to start on first run (loading n-grams)
- **Recommendation**: Increase to 30s timeout, add progress logging

### 15. English Only - Language Code
- **Plan**: Uses `en-US`
- **LT Server**: Supports `en-US`, `en-GB`, etc. - `en-US` is fine

## Required Code Changes Summary

### Files to CREATE:
1. `Core/LanguageToolClient.cs` (new)
2. `Services/LanguageToolService.cs` (new)
3. `tools/Start-LanguageTool.bat` (new)
4. `tools/INSTALL.md` (new)
5. `tools/languagetool-server.jar` (download, ~200MB)

### Files to MODIFY:
1. `Core/CorrectionPipeline.cs` - Replace engines, inject LT client
2. `Core/AppController.cs` - Pass LT client to pipeline
3. `App.xaml.cs` - Manage LT service lifecycle
4. `Models/CorrectionResult.cs` - Convert to class with init props (or fix LT client to match record)
5. `Models/Edit.cs` - Convert to class with init props, rename Reason→Message
6. `Models/AppSettings.cs` - Remove GroqModel, GroqFallbackModel, CorrectionMode.AI
7. `GrammarFixer.csproj` - Add JAR as Content

### Files to DELETE:
1. `Core/StaticCorrectionEngine.cs`
2. `Core/GroqClient.cs`

## Recommended Implementation Order

1. **First**: Add JAR to tools/ and update csproj
2. **Then**: Create LanguageToolService and LanguageToolClient
3. **Then**: Update Models (Edit, CorrectionResult) to work with LT client
4. **Then**: Update CorrectionPipeline to use LT client
5. **Then**: Update AppController to create/pass LT client
6. **Then**: Update App.xaml.cs for service lifecycle
7. **Then**: Clean up AppSettings
8. **Then**: Delete old engine files
9. **Finally**: Build and test

## Risk Items
- [ ] JAR file size (~200MB) - git LFS needed or instruct manual download
- [ ] Single-file publish + external JAR - must verify extraction works
- [ ] Java not installed - graceful degradation needed
- [ ] LT server startup time - may need longer timeout
- [ ] Port 8081 conflicts - consider random port or check

---

## NEW REQUIREMENT: Hovering UI Enhancement (Added 2026-07-13)

### Floating Icon with Dual Mode

The current `FloatingButton` (pill near caret) should be enhanced to support **two interaction modes**:

| Mode | Trigger | Behavior |
|------|---------|----------|
| **Quick Fix** (existing) | Type → debounce → pill appears | Click pill → applies correction inline via `UiaHelper.SetFocusedText()` |
| **Paste & Correct** (NEW) | Click tray icon → "Open Correction Window" OR dedicated hotkey | Opens a **floating window** with: |
| | | • Text area (paste or type) |
| | | • Auto-correct as you type (debounced) |
| | | • "Apply to Clipboard" button |
| | | • "Copy Corrected" button |
| | | • "Send to Active Field" button (uses UIA to paste at caret) |
| | | • Rolling diff view (original ↔ corrected side-by-side or inline) |

### Implementation Notes

1. **New Window**: `CorrectionWindow.xaml/.cs` (not OverlayWindow — that's for review suggestions)
   - Modeless, topmost, remembers position
   - TextArea with `TextChanged` → debounce → `LanguageToolClient.CheckAsync()`
   - Shows corrected text in real-time (green/red diff highlights via `DiffPlex`)
   - Buttons: `Apply to Clipboard`, `Copy Corrected`, `Send to Field`

2. **Tray Menu Addition**:
   - "Correction Window" → opens/closes the window
   - Separate from "Settings", "Self-Test", etc.

3. **Hotkey**: Add `CorrectionWindowHotkey` to `AppSettings` (default: `Ctrl+Alt+Shift+G`)

4. **Reuse Existing**:
   - `LanguageToolClient` for corrections
   - `UiaHelper.GetCaretScreenPosition()` / `SetFocusedText()` for "Send to Field"
   - `DiffPlex` for inline diff rendering (already in NuGet)

5. **Rolling Text Holder**: 
   - As user types/pastes, background correction runs
   - Show original text with colored markers (red=delete, green=insert)
   - Click any marker → accept/reject individual change
   - "Accept All" → copies corrected to clipboard or sends to field

### Files to Add/Modify

| File | Change |
|------|--------|
| `UI/CorrectionWindow.xaml/.cs` | **NEW** — main floating correction window |
| `UI/TrayIconManager.cs` | Add menu item + window management |
| `Models/AppSettings.cs` | Add `CorrectionWindowHotkey`, window position persistence |
| `Core/AppController.cs` | Handle new hotkey, manage window lifecycle |
| `GrammarFixer.csproj` | Ensure `DiffPlex` is used (already referenced) |

---

## Risk Items