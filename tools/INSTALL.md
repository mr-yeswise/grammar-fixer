# LanguageTool Setup for GrammarFixer

GrammarFixer uses a local LanguageTool server for grammar checking (English only). This runs as a Java process managed by the app.

## Quick Setup

### 1. Install Java 11+
- **Recommended**: [Eclipse Temurin (Adoptium) JDK 17 or 21](https://adoptium.net/)
- Or: Oracle JDK, OpenJDK, etc.
- Verify: Open terminal and run `java -version` → should show 11+

### 2. Download LanguageTool Server JAR
1. Go to: https://languagetool.org/download/LanguageTool-stable.zip
2. Download and extract the ZIP
3. Find `languagetool-server.jar` in the extracted folder
4. Copy it to: `tools/languagetool-server.jar` (next to this file)

### 3. Run GrammarFixer
- The app will auto-start the LanguageTool server on first launch
- Server runs on `http://localhost:8081` (auto-selects 8081-8090 if busy)
- Health check: `GET /v2/languages` (waits up to 30 seconds on startup)

## Manual Server Start (for debugging)
```bat
# From the tools/ folder:
Start-LanguageTool.bat
```
Or directly:
```bash
java -Dfile.encoding=utf-8 -Xms256m -Xmx512m -jar languagetool-server.jar --port 8081 --allow-origin "*"
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Java not found" | Install JDK 11+, ensure `java` is in PATH |
| "LT JAR not found" | Copy `languagetool-server.jar` to `tools/` folder |
| Server won't start | Check port 8081-8090 not in use; check Java version |
| Server starts but corrections fail | Check firewall/antivirus isn't blocking localhost:8081 |
| "LT server did not become ready" | First run loads n-grams (10-30s); increase timeout in `LanguageToolService.cs` |

## Architecture Notes
- **LanguageToolService.cs**: Manages Java process lifecycle, health checks, port selection
- **LanguageToolClient.cs**: HTTP client calling `/v2/check`, applies all first-replacement suggestions
- **CorrectionPipeline.cs**: Debounce (300ms), LRU cache (50 entries), routes to LT client
- **English only**: Hardcoded `en-US` language code

## Single-File Publish
The `languagetool-server.jar` is included as `Content` with `CopyToOutputDirectory=PreserveNewest`. On single-file publish, it extracts to the temp folder alongside the executable and is found via `AppContext.BaseDirectory`.