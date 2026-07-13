using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly LanguageToolService _ltService;
    private readonly LruCache<string, CorrectionResult> _cache = new(50);
    private readonly System.Timers.Timer _debounce;
    private string? _pendingText;
    private AppSettings _settings;
    private CancellationTokenSource _cts = new();

    public event Action<CorrectionResult>? CorrectionReady;

    public CorrectionPipeline(AppSettings settings, LanguageToolClient ltClient, LanguageToolService ltService)
    {
        _settings = settings;
        _ltClient = ltClient;
        _ltService = ltService;

        _debounce = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await RunCorrectionAsync();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _debounce.Interval = settings.DebounceMs;
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
        
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Pipeline: mode={_settings.Mode}, text length={text.Length}");

        if (_cache.TryGet(text, out var cached))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Pipeline: cache hit");
            return cached with { FromCache = true };
        }

        CorrectionResult? result;

        if (_settings.Mode == CorrectionMode.LanguageTool)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Pipeline: running LanguageTool");
            _cts.Cancel();
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            result = await _ltClient.CheckAsync(text, _ltService.BaseUrl, _cts.Token);
        }
        else
        {
            // Should not happen - only LanguageTool mode exists now
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "Pipeline: unknown mode, returning original");
            result = new CorrectionResult { Original = text, Corrected = text, Edits = [], FromCache = false };
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