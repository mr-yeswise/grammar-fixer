using System.Timers;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// Orchestrates correction: debounces, caches, routes to LanguageToolClient.
/// </summary>
public sealed class CorrectionPipeline : IDisposable
{
    private readonly LanguageToolClient _ltClient;
    private readonly LruCache<string, CorrectionResult> _cache = new(50);
    private readonly System.Timers.Timer _debounce;
    private string? _pendingText;
    private AppSettings _settings;
    private CancellationTokenSource _cts = new();

    public event Action<CorrectionResult>? CorrectionReady;

    public CorrectionPipeline(AppSettings settings, LanguageToolClient ltClient)
    {
        _settings  = settings;
        _ltClient  = ltClient;
        _debounce  = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await RunCorrectionAsync();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _debounce.Interval = settings.DebounceMs;
    }

    public void Queue(string text)
    {
        _pendingText = text;
        _debounce.Stop();
        _debounce.Start();
    }

    public async Task<CorrectionResult?> CorrectNowAsync(string text)
        => await RunCorrectionForAsync(text);

    private async Task RunCorrectionAsync()
    {
        var text = _pendingText;
        if (string.IsNullOrWhiteSpace(text)) return;
        var result = await RunCorrectionForAsync(text);
        if (result != null) CorrectionReady?.Invoke(result);
    }

    private async Task<CorrectionResult?> RunCorrectionForAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Pipeline: text length={text.Length}");

        if (_cache.TryGet(text, out var cached))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Pipeline: cache hit");
            return cached with { FromCache = true };
        }

        _cts.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Pipeline: sending to LanguageTool");
        var result = await _ltClient.CheckAsync(text, _cts.Token);

        if (result != null) _cache.Set(text, result);
        return result;
    }

    public void Dispose()
    {
        _debounce.Dispose();
        _cts.Dispose();
    }
}
