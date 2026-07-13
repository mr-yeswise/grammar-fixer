TASK: Replace correction engine with LanguageTool local server
REPO: C:\Users\fadi4\Desktop\grammar-fixer
BASE BRANCH: main
NEW BRANCH: hermes/languagetool

═══════════════════════════════════════════════════
PART 1 — DOWNLOAD LANGUAGETOOL JAR
═══════════════════════════════════════════════════
1. Download the LanguageTool standalone server JAR (English only, ~200MB):
   URL: https://languagetool.org/download/LanguageTool-stable.zip
   Extract: LanguageTool-X.X/languagetool-server.jar
   Place at: tools/languagetool-server.jar (relative to repo root)
   Also copy: tools/org/languagetool/resource/en/ (English grammar rules only)

   If download fails, create tools/DOWNLOAD_LT.md with instructions instead.

═══════════════════════════════════════════════════
PART 2 — NEW FILE: src/GrammarFixer/Services/LanguageToolService.cs
═══════════════════════════════════════════════════
Manages the Java child process lifetime.

```csharp
namespace GrammarFixer.Services;

public sealed class LanguageToolService : IDisposable
{
    // Port the local server listens on
    public const int Port = 8081;
    public static string BaseUrl => $"http://localhost:{Port}";

    private Process? _serverProcess;
    private bool _ready;

    public bool IsReady => _ready;

    /// <summary>
    /// Starts java -jar languagetool-server.jar --port 8081 --allow-origin "*"
    /// Waits up to 15 seconds for the server to respond on /v2/languages.
    /// Returns true if server is up, false if Java not found or JAR missing.
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        var jarPath = Path.Combine(AppContext.BaseDirectory, "tools", "languagetool-server.jar");
        if (!File.Exists(jarPath))
        {
            DiagnosticLogger.Warn($"LT jar not found at {jarPath}");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-Dfile.encoding=utf-8 -jar \"{jarPath}\" --port {Port} --allow-origin \"*\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            _serverProcess = Process.Start(psi)!;
            DiagnosticLogger.Info($"LT process started PID={_serverProcess.Id}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"Failed to start LT: {ex.Message}");
            return false;
        }

        // Poll until ready (max 15s)
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var r = await http.GetAsync($"{BaseUrl}/v2/languages", ct);
                if (r.IsSuccessStatusCode) { _ready = true; break; }
            }
            catch { }
            await Task.Delay(500, ct);
        }

        DiagnosticLogger.Info(_ready ? "LT server ready" : "LT server did NOT become ready in time");
        return _ready;
    }

    public void Dispose()
    {
        try { if (_serverProcess is { HasExited: false }) _serverProcess.Kill(entireProcessTree: true); }
        catch { }
        _serverProcess?.Dispose();
    }
}
```

═══════════════════════════════════════════════════
PART 3 — NEW FILE: src/GrammarFixer/Core/LanguageToolClient.cs
═══════════════════════════════════════════════════
HTTP client that calls the local server and applies all matches.

```csharp
namespace GrammarFixer.Core;

public sealed class LanguageToolClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// Sends text to LT, applies ALL first-replacement suggestions,
    /// returns corrected string (or null on failure).
    /// </summary>
    public async Task<CorrectionResult?> CheckAsync(string text, CancellationToken ct = default)
    {
        DiagnosticLogger.Info($"LT: checking {text.Length} chars");
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["language"] = "en-US",
                ["text"]     = text,
                ["enabledOnly"] = "false"
            });

            var resp = await _http.PostAsync(
                $"{LanguageToolService.BaseUrl}/v2/check", content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLogger.Warn($"LT: HTTP {resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc  = System.Text.Json.JsonDocument.Parse(json);

            var matches = doc.RootElement.GetProperty("matches");

            if (matches.GetArrayLength() == 0)
            {
                DiagnosticLogger.Info("LT: no issues found");
                return new CorrectionResult { Original = text, Corrected = text, Edits = [] };
            }

            // Apply replacements from end to start so offsets stay valid
            var sb = new System.Text.StringBuilder(text);
            var edits = new List<Edit>();

            var matchList = matches.EnumerateArray()
                .Select(m => new {
                    Offset  = m.GetProperty("offset").GetInt32(),
                    Length  = m.GetProperty("length").GetInt32(),
                    Message = m.GetProperty("message").GetString() ?? "",
                    Replace = m.GetProperty("replacements").EnumerateArray()
                                .Select(r => r.GetProperty("value").GetString() ?? "")
                                .FirstOrDefault() ?? ""
                })
                .Where(m => m.Replace.Length > 0)
                .OrderByDescending(m => m.Offset)
                .ToList();

            foreach (var m in matchList)
            {
                var original = text.Substring(m.Offset, Math.Min(m.Length, text.Length - m.Offset));
                sb.Remove(m.Offset, Math.Min(m.Length, sb.Length - m.Offset));
                sb.Insert(m.Offset, m.Replace);
                edits.Add(new Edit { Original = original, Replacement = m.Replace, Message = m.Message });
            }

            var corrected = sb.ToString();
            DiagnosticLogger.Info($"LT: {matchList.Count} fixes applied");

            return new CorrectionResult
            {
                Original  = text,
                Corrected = corrected,
                Edits     = edits,
                FromCache = false
            };
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"LT client error: {ex.Message}");
            return null;
        }
    }
}
```

═══════════════════════════════════════════════════
PART 4 — UPDATE: CorrectionPipeline.cs
═══════════════════════════════════════════════════
Replace StaticCorrectionEngine + GroqClient with LanguageToolClient.
Keep same public API (Queue, CorrectNowAsync, CorrectionReady event).

- Constructor takes (AppSettings settings, LanguageToolClient ltClient)
- RunCorrectionForAsync → always calls ltClient.CheckAsync(text)
- Remove all StaticCorrectionEngine and GroqClient references
- Keep LruCache<string, CorrectionResult>(50) for caching

═══════════════════════════════════════════════════
PART 5 — UPDATE: App.xaml.cs
═══════════════════════════════════════════════════
- Create LanguageToolService instance
- In OnStartup: await _ltService.StartAsync() before creating AppController
- Pass LanguageToolClient into CorrectionPipeline
- In OnExit: _ltService.Dispose()
- If !ltService.IsReady: show tray balloon "LanguageTool not ready — install Java 11+"

═══════════════════════════════════════════════════
PART 6 — ADD: tools/Start-LanguageTool.bat
═══════════════════════════════════════════════════
```bat
@echo off
java -Dfile.encoding=utf-8 -jar "%~dp0languagetool-server.jar" --port 8081 --allow-origin "*"
pause
```

═══════════════════════════════════════════════════
PART 7 — ADD: tools/INSTALL.md
═══════════════════════════════════════════════════
# LanguageTool Setup
1. Install Java 11+ from https://adoptium.net/
2. Download LanguageTool standalone from https://languagetool.org/download/LanguageTool-stable.zip
3. Extract languagetool-server.jar into this tools/ folder
4. GrammarFixer will start the server automatically on launch

═══════════════════════════════════════════════════
PART 8 — UPDATE: Models/CorrectionResult.cs
═══════════════════════════════════════════════════
Add Edits list if not present:
```csharp
public record CorrectionResult
{
    public string Original  { get; init; } = "";
    public string Corrected { get; init; } = "";
    public List<Edit> Edits { get; init; } = [];
    public bool FromCache   { get; init; }
}
```

Models/Edit.cs — ensure it has Message field:
```csharp
public record Edit
{
    public string Original    { get; init; } = "";
    public string Replacement { get; init; } = "";
    public string Message     { get; init; } = "";
}
```

═══════════════════════════════════════════════════
PART 9 — BUILD CHECK
═══════════════════════════════════════════════════
dotnet build src/GrammarFixer/GrammarFixer.csproj -c Debug
Must be 0 errors. Fix any errors before committing.
Remove StaticCorrectionEngine.cs and GroqClient.cs if they cause unused reference errors.

═══════════════════════════════════════════════════
PART 10 — COMMIT & PUSH
═══════════════════════════════════════════════════
git add -A
git commit -m "feat: replace correction engine with LanguageTool local server (English only)"
git push origin hermes/languagetool