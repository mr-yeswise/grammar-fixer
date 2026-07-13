using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// Groq API client using llama-3.1-8b-instant (~560 t/s) for sub-second grammar rewrites.
/// Falls back to llama-3.3-70b-versatile if configured.
///
/// API reference: https://console.groq.com/docs/openai
/// JSON mode: response_format = { type: "json_object" }
///
/// Prompt returns: { "corrected": "...", "edits": [{"original": "", "replacement": "", "reason": "", "offset": 0, "length": 0}] }
/// </summary>
public sealed class GroqClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private const string BaseUrl = "https://api.groq.com/openai/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GroqClient(string apiKey, string model = "llama-3.1-8b-instant")
    {
        _model = model;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<CorrectionResult?> CorrectAsync(string input, CancellationToken ct = default)
    {
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Groq: sending request, model={_model}");
        var started = DateTime.UtcNow;
        var systemPrompt =
            "You are a grammar and spelling correction assistant. " +
            "Return ONLY valid JSON with this exact schema: " +
            "{ \"corrected\": \"<full corrected text>\", \"edits\": [ { \"original\": \"\", \"replacement\": \"\", \"reason\": \"\", \"offset\": 0, \"length\": 0 } ] }. " +
            "Fix grammar, spelling, punctuation, and capitalization. Preserve the user's style and meaning. " +
            "Return only the JSON object, no markdown, no extra text.";

        var payload = new
        {
            model = _model,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = input }
            },
            temperature = 0.1,
            max_tokens = 1024
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync(BaseUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var completion = JsonSerializer.Deserialize<GroqCompletion>(responseJson, JsonOpts);
            var messageContent = completion?.Choices?[0]?.Message?.Content;
            if (messageContent == null) return null;

            var result = JsonSerializer.Deserialize<GroqCorrectionPayload>(messageContent, JsonOpts);
            if (result == null) return null;

            var edits = result.Edits?.Select(e =>
                new Edit(e.Original ?? "", e.Replacement ?? "", e.Reason ?? "", e.Offset, e.Length)
            ).ToList() ?? new List<Edit>();

            var elapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds;
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Groq: response received in {elapsedMs}ms");
            return new CorrectionResult(input, result.Corrected ?? input, edits);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Groq: error {ex.Message}");
            return null;
        }
    }

    // --- Response model types ---

    private record GroqCompletion(
        [property: JsonPropertyName("choices")] List<GroqChoice>? Choices
    );

    private record GroqChoice(
        [property: JsonPropertyName("message")] GroqMessage? Message
    );

    private record GroqMessage(
        [property: JsonPropertyName("content")] string? Content
    );

    private record GroqCorrectionPayload(
        [property: JsonPropertyName("corrected")] string? Corrected,
        [property: JsonPropertyName("edits")] List<GroqEditPayload>? Edits
    );

    private record GroqEditPayload(
        [property: JsonPropertyName("original")] string? Original,
        [property: JsonPropertyName("replacement")] string? Replacement,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("offset")] int Offset,
        [property: JsonPropertyName("length")] int Length
    );
}
