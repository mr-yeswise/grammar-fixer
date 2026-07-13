using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// HTTP client for LanguageTool local server.
/// Calls POST /v2/check with form data, applies all first-replacement suggestions.
/// </summary>
public sealed class LanguageToolClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Sends text to LanguageTool, applies ALL first-replacement suggestions.
    /// Returns corrected string with list of edits, or null on failure.
    /// </summary>
    public async Task<CorrectionResult?> CheckAsync(string text, string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CorrectionResult { Original = text, Corrected = text, Edits = [], FromCache = false };

        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"LT: checking {text.Length} chars");

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["language"] = "en-US",
                ["text"] = text,
                ["enabledOnly"] = "false"
            });

            var resp = await _http.PostAsync($"{baseUrl}/v2/check", content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Warn, $"LT: HTTP {resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("matches", out var matches) || matches.GetArrayLength() == 0)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "LT: no issues found");
                return new CorrectionResult { Original = text, Corrected = text, Edits = [], FromCache = false };
            }

            // Collect matches with replacements, sort by offset DESC
            var matchList = new List<MatchInfo>();
            foreach (var m in matches.EnumerateArray())
            {
                var offset = m.GetProperty("offset").GetInt32();
                var length = m.GetProperty("length").GetInt32();
                var message = m.GetProperty("message").GetString() ?? "";
                
                string? replacement = null;
                if (m.TryGetProperty("replacements", out var repls) && repls.GetArrayLength() > 0)
                {
                    replacement = repls[0].GetProperty("value").GetString();
                }

                if (!string.IsNullOrEmpty(replacement))
                {
                    matchList.Add(new MatchInfo
                    {
                        Offset = offset,
                        Length = length,
                        Message = message,
                        Replacement = replacement
                    });
                }
            }

            if (matchList.Count == 0)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "LT: no actionable replacements");
                return new CorrectionResult { Original = text, Corrected = text, Edits = [], FromCache = false };
            }

            // Sort by offset descending so replacements don't shift indices
            matchList.Sort((a, b) => b.Offset.CompareTo(a.Offset));

            var sb = new StringBuilder(text);
            var edits = new List<Edit>();

            foreach (var m in matchList)
            {
                // Clamp length to string bounds
                var len = Math.Min(m.Length, sb.Length - m.Offset);
                if (len <= 0) continue;

                var original = sb.ToString(m.Offset, len);
                sb.Remove(m.Offset, len);
                sb.Insert(m.Offset, m.Replacement);

                edits.Add(new Edit
                {
                    Original = original,
                    Replacement = m.Replacement,
                    Message = m.Message,
                    Offset = m.Offset,
                    Length = len
                });
            }

            var corrected = sb.ToString();
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"LT: {edits.Count} fixes applied");

            return new CorrectionResult
            {
                Original = text,
                Corrected = corrected,
                Edits = edits,
                FromCache = false
            };
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"LT client error: {ex.Message}");
            return null;
        }
    }

    private sealed class MatchInfo
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Message { get; set; } = "";
        public string Replacement { get; set; } = "";
    }
}