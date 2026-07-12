using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// Orchestrates correction: routes to StaticEngine or GroqClient,
/// manages 400ms debounce, and caches last 50 results.
/// </summary>
public sealed class CorrectionPipeline : IDisposable
{
    private readonly StaticCorrectionEngine _staticEngine;
    private GroqClient? _groqClient;
    private readonly LruCache<string, CorrectionResult> _cache = new(50);
    private readonly System.Timers.Timer _debounce;
    private string? _pendingText;
    private AppSettings _settings;
    private CancellationTokenSource _cts = new();

    public event Action<CorrectionResult>? CorrectionReady;

    public CorrectionPipeline(AppSettings settings)
    {
        _settings = settings;
        _staticEngine = new StaticCorrectionEngine();

        var typosPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "typos_en.json");
        _staticEngine.LoadDictionary(typosPath);

        _debounce = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await RunCorrectionAsync();

        RefreshGroqClient();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _debounce.Interval = settings.DebounceMs;
        RefreshGroqClient();
    }

    private void RefreshGroqClient()
    {
        var apiKey = CredentialService.LoadApiKey();
        _groqClient = !string.IsNullOrWhiteSpace(apiKey)
            ? new GroqClient(apiKey, _settings.GroqModel)
            : null;
    }

    /// <summary>Queue text for correction after debounce interval.</summary>
    public void Queue(string text)
    {
        _pendingText = text;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Run correction immediately (hotkey path).</summary>
    public async Task<CorrectionResult?> CorrectNowAsync(string text)
    {
        return await RunCorrectionForAsync(text);
    }

    private async Task RunCorrectionAsync()
    {
        var text = _pendingText;
        if (string.IsNullOrWhiteSpace(text)) return;
        var result = await RunCorrectionForAsync(text);
        if (result != null)
            CorrectionReady?.Invoke(result);
    }

    private async Task<CorrectionResult?> RunCorrectionForAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Cache hit
        if (_cache.TryGet(text, out var cached))
            return cached with { FromCache = true };

        CorrectionResult? result;

        if (_settings.Mode == CorrectionMode.Static || _groqClient == null)
        {
            result = _staticEngine.Correct(text);
        }
        else
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            result = await _groqClient.CorrectAsync(text, _cts.Token);

            // If Groq fails, fall back to static
            result ??= _staticEngine.Correct(text);
        }

        if (result != null)
            _cache.Set(text, result);

        return result;
    }

    public void Dispose()
    {
        _debounce.Dispose();
        _cts.Dispose();
    }
}
