using System.Windows;
using GrammarFixer.Models;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer.Core;

/// <summary>
/// Central orchestrator: wires KeyboardHook → UiaHelper → CorrectionPipeline → UX (overlay or direct replace).
/// </summary>
public sealed class AppController : IDisposable
{
    private AppSettings _settings;
    private readonly CorrectionPipeline _pipeline;
    private readonly HotkeyManager _hotkey;
    private OverlayWindow? _overlay;
    private bool _disposed;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings)
    {
        _settings = settings;
        _pipeline = new CorrectionPipeline(settings);
        _hotkey = new HotkeyManager(settings.HotkeyTrigger);
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        _pipeline.CorrectionReady += OnCorrectionReady;
    }

    public void Start()
    {
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.Stop();
        _overlay?.Hide();
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _pipeline.UpdateSettings(settings);
        SettingsService.Save(settings);
    }

    public void OpenSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new SettingsWindow(this);
            win.Show();
        });
    }

    private async void OnHotkeyPressed()
    {
        if (!_settings.Enabled) return;

        var processName = UiaHelper.GetForegroundProcessName();
        if (!IsAppAllowed(processName)) return;

        var text = UiaHelper.GetFocusedText();
        if (string.IsNullOrWhiteSpace(text)) return;

        var result = await _pipeline.CorrectNowAsync(text);
        if (result == null || result.Corrected == result.Original) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settings.UxMode == UxMode.OneClickRewrite)
            {
                UiaHelper.SetFocusedText(result.Corrected);
            }
            else
            {
                ShowOverlay(result);
            }
        });
    }

    private void OnCorrectionReady(CorrectionResult result)
    {
        if (!_settings.Enabled) return;
        if (_settings.UxMode != UxMode.ReviewSuggestions) return;
        if (result.Corrected == result.Original) return;

        Application.Current.Dispatcher.Invoke(() => ShowOverlay(result));
    }

    private void ShowOverlay(CorrectionResult result)
    {
        _overlay?.Close();
        var pos = UiaHelper.GetCaretScreenPosition();
        _overlay = new OverlayWindow(result, pos, this);
        _overlay.Show();
    }

    public void ApplyCorrection(CorrectionResult result)
    {
        UiaHelper.SetFocusedText(result.Corrected);
        _overlay?.Close();
    }

    public void DismissOverlay()
    {
        _overlay?.Close();
        _overlay = null;
    }

    private bool IsAppAllowed(string? processName)
    {
        if (processName == null) return false;
        if (_settings.DeniedApps.Any(d =>
            processName.Contains(d, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (_settings.AllowedApps.Count == 0) return true;
        return _settings.AllowedApps.Any(a =>
            processName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _hotkey.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }
}
