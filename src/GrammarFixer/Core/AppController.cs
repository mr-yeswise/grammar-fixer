using System.Windows;
using GrammarFixer.Models;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer.Core;

/// <summary>
/// Central orchestrator.
/// Flow:
///   KeyboardHook fires on every keypress
///   -> debounce 400ms
///   -> read focused text via UIA
///   -> CorrectionPipeline corrects
///   -> show FloatingButton pill near caret  (always)
///   -> on pill click OR Ctrl+Alt+G: apply correction or show overlay
/// </summary>
public sealed class AppController : IDisposable
{
    private AppSettings _settings;
    private readonly CorrectionPipeline _pipeline;
    private readonly HotkeyManager _hotkey;
    private readonly KeyboardHook _typingHook;
    private OverlayWindow? _overlay;
    private FloatingButton? _floatingBtn;
    private bool _disposed;

    // Last captured text for the floating button click path
    private string? _lastCapturedText;
    private GrammarFixer.Models.CorrectionResult? _lastResult;

    // Debounce timer for typing detection (separate from pipeline debounce)
    private readonly System.Timers.Timer _typingDebounce;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings)
    {
        _settings = settings;
        _pipeline = new CorrectionPipeline(settings);
        _pipeline.CorrectionReady += OnCorrectionReady;

        // Global hotkey Ctrl+Alt+G
        _hotkey = new HotkeyManager(settings.HotkeyTrigger);
        _hotkey.HotkeyPressed += OnHotkeyPressed;

        // Typing hook for floating button trigger
        _typingHook = new KeyboardHook();
        _typingHook.KeyDown += OnTypingKeyDown;

        _typingDebounce = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _typingDebounce.Elapsed += async (_, _) => await OnTypingPaused();
    }

    public void Start()
    {
        _hotkey.Start();
        _typingHook.Install();

        // Create floating button on UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            _floatingBtn = new FloatingButton(this);
        });
    }

    public void Stop()
    {
        _hotkey.Stop();
        _typingHook.Uninstall();
        _typingDebounce.Stop();
        Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _floatingBtn?.Close();
        });
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _pipeline.UpdateSettings(settings);
        _typingDebounce.Interval = settings.DebounceMs;
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

    // Called when user clicks the floating pill button
    public void TriggerFromFloatingButton()
    {
        if (_lastResult != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_settings.UxMode == UxMode.OneClickRewrite)
                    UiaHelper.SetFocusedText(_lastResult.Corrected);
                else
                    ShowOverlay(_lastResult);
            });
        }
        else if (_lastCapturedText != null)
        {
            // Result not ready yet — run synchronously
            _ = Task.Run(async () =>
            {
                var result = await _pipeline.CorrectNowAsync(_lastCapturedText);
                if (result == null || result.Corrected == result.Original) return;
                _lastResult = result;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_settings.UxMode == UxMode.OneClickRewrite)
                        UiaHelper.SetFocusedText(result.Corrected);
                    else
                        ShowOverlay(result);
                });
            });
        }
    }

    // Ctrl+Alt+G hotkey path
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
                UiaHelper.SetFocusedText(result.Corrected);
            else
                ShowOverlay(result);
        });
    }

    // Fires on every keypress — resets debounce
    private void OnTypingKeyDown(System.Windows.Input.Key key)
    {
        if (!_settings.Enabled) return;
        if (_settings.HotkeyOnlyMode) return;

        // Ignore modifier-only keys
        if (key == System.Windows.Input.Key.LeftCtrl ||
            key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftAlt ||
            key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LeftShift ||
            key == System.Windows.Input.Key.RightShift ||
            key == System.Windows.Input.Key.LWin ||
            key == System.Windows.Input.Key.RWin) return;

        _typingDebounce.Stop();
        _typingDebounce.Start();
    }

    // Fires 400ms after last keypress
    private async Task OnTypingPaused()
    {
        if (!_settings.Enabled) return;

        string? processName = null;
        string? text = null;
        System.Windows.Point caretPos = default;

        // Must read UIA on the UI thread
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            processName = UiaHelper.GetForegroundProcessName();
            if (!IsAppAllowed(processName)) return;
            text = UiaHelper.GetFocusedText();
            caretPos = UiaHelper.GetCaretScreenPosition();
        });

        if (string.IsNullOrWhiteSpace(text)) return;
        if (!IsAppAllowed(processName)) return;

        // Store for floating button click
        _lastCapturedText = text;
        _lastResult = null;

        // Pre-warm the correction in background
        var result = await _pipeline.CorrectNowAsync(text);
        if (result != null && result.Corrected != result.Original)
        {
            _lastResult = result;
            // Show the floating pill
            Application.Current.Dispatcher.Invoke(() =>
            {
                _floatingBtn?.ShowAt(caretPos);
            });
        }
    }

    // Fired by CorrectionPipeline debounce path (review mode)
    private void OnCorrectionReady(GrammarFixer.Models.CorrectionResult result)
    {
        if (!_settings.Enabled) return;
        if (result.Corrected == result.Original) return;
        _lastResult = result;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var pos = UiaHelper.GetCaretScreenPosition();
            _floatingBtn?.ShowAt(pos);
        });
    }

    private void ShowOverlay(GrammarFixer.Models.CorrectionResult result)
    {
        _overlay?.Close();
        var pos = UiaHelper.GetCaretScreenPosition();
        _overlay = new OverlayWindow(result, pos, this);
        _overlay.Show();
    }

    public void ApplyCorrection(GrammarFixer.Models.CorrectionResult result)
    {
        UiaHelper.SetFocusedText(result.Corrected);
        _overlay?.Close();
        _overlay = null;
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
        _typingHook.Dispose();
        _typingDebounce.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }
}
