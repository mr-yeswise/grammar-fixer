using System.Net.Http;
using System.Text.Json;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// Calls the local LanguageTool HTTP server and applies all match replacements.
/// </summary>
public sealed class LanguageToolClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// POST /v2/check, apply all first-choice replacements end-to-start.
    /// Returns null on network/server failure.
    /// </summary>
    public async Task<CorrectionResult?> CheckAsync(string text, CancellationToken ct = default)
    {
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"LT: checking {text.Length} chars");
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["language"]    = "en-US",
                ["text"]        = text,
                ["enabledOnly"] = "false"
            });

            var resp = await _http.PostAsync(
                $"{LanguageToolService.BaseUrl}/v2/check", content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Warn, $"LT: HTTP {(int)resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc  = JsonDocument.Parse(json);
            var matches = doc.RootElement.GetProperty("matches");

            if (matches.GetArrayLength() == 0)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "LT: no issues found");
                return new CorrectionResult { Original = text, Corrected = text, Edits = [] };
            }

            // Build match list, apply end-to-start so offsets stay valid
            var matchList = matches.EnumerateArray()
                .Select(m => new
                {
                    Offset  = m.GetProperty("offset").GetInt32(),
                    Length  = m.GetProperty("length").GetInt32(),
                    Message = m.GetProperty("message").GetString() ?? string.Empty,
                    Replace = m.GetProperty("replacements").EnumerateArray()
                                .Select(r => r.GetProperty("value").GetString() ?? string.Empty)
                                .FirstOrDefault() ?? string.Empty
                })
                .Where(m => m.Replace.Length > 0)
                .OrderByDescending(m => m.Offset)
                .ToList();

            var sb    = new System.Text.StringBuilder(text);
            var edits = new List<Edit>();

            foreach (var m in matchList)
            {
                var safeLen = Math.Min(m.Length, sb.Length - m.Offset);
                if (safeLen <= 0) continue;
                var original = text.Substring(m.Offset, Math.Min(m.Length, text.Length - m.Offset));
                sb.Remove(m.Offset, safeLen);
                sb.Insert(m.Offset, m.Replace);
                edits.Add(new Edit { Original = original, Replacement = m.Replace, Message = m.Message });
            }

            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"LT: {matchList.Count} fix(es) applied");
            return new CorrectionResult
            {
                Original  = text,
                Corrected = sb.ToString(),
                Edits     = edits,
                FromCache = false
            };
        }
        catch (TaskCanceledException)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "LT: request timed out");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"LT: error: {ex.Message}");
            return null;
        }
    }
}
